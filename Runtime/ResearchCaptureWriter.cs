using System;
using System.IO;
using System.Text;
using CesiumForUnity;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityResearchCapture
{
    [Serializable] public sealed class CaptureRecord
    {
        public int schemaVersion = 1;
        public string imageId, fileName, capturedAtUtc, movementMode, captureMode, roomId;
        public int unityFrame, width, height;
        public double unityRealtimeSeconds;
        public Vector3 cameraPositionUnity, cameraEulerDegrees;
        public Quaternion cameraRotationUnity;
        public string coordinateSystem = "Unity world; left-handed; X right, Y up, Z forward. Quaternion order x,y,z,w.";
        public string lengthUnit = "metre";
        public float unityUnitsPerMetre, verticalFieldOfViewDegrees, horizontalFieldOfViewDegrees;
        public float[] projectionMatrix;
        public string roomIdSource = "Explicit RoomRegion containing camera; unknown if absent or overlapping";
        public string originalCityGmlCoordinates = "not included: original CityGML CRS/transform has not been verified";
        public string georeferenceDefinition;
        public bool researchAmbientFill;
        public Color ambientFillColor;
        public double[] ecefMetres;
    }

    [Serializable] public sealed class CaptureEvent
    { public string timestampUtc, type, detail; }

    public sealed class ResearchCaptureWriter : MonoBehaviour
    {
        public ResearchCaptureSettings settings;
        public Camera sourceCamera;
        public ResearchWalker walker;
        public CesiumGeoreference georeference;
        public Cesium3DTileset[] tilesets = Array.Empty<Cesium3DTileset>();
        public string SessionFolder { get; private set; } = "";
        public int SavedCount { get; private set; }
        public bool Busy { get; private set; }
        public string Status { get; private set; } = "Ready";
        public bool LastCaptureSucceeded { get; private set; }
        public CaptureRecord LastRecord { get; private set; }
        public int SourceRenderedFrames { get; private set; }
        public event Action<bool> Completed;
        Camera captureCamera;
        RenderTexture target;
        string pendingMode;
        float requestedAt;
        bool rendering;
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        void Awake()
        {
            try
            {
                string folder=settings.outputFolder;
                string[] args=Environment.GetCommandLineArgs();
                int outputArg=Array.IndexOf(args,"-capture-output");
                if(outputArg>=0 && outputArg+1<args.Length) folder=args[outputArg+1];
                string root = string.IsNullOrWhiteSpace(folder)
                    ? Path.Combine(Application.persistentDataPath, "ResearchCaptures")
                    : (Path.IsPathRooted(folder) ? folder : Path.Combine(Application.persistentDataPath, folder));
                SessionFolder = Path.Combine(root, DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path.Combine(SessionFolder, "images"));
                Directory.CreateDirectory(Path.Combine(SessionFolder, "ground_truth"));
                File.WriteAllText(Path.Combine(SessionFolder, "session.json"), JsonUtility.ToJson(new CaptureEvent {
                    timestampUtc = DateTime.UtcNow.ToString("O"), type = "session_started",
                    detail = "Only images/ may be supplied to image-based inference. ground_truth/ contains evaluation labels and poses. Unity " + Application.unityVersion
                }, true), Utf8);
            }
            catch (Exception e) { SessionFolder = ""; Fail("Cannot create output folder: " + e.Message); }
        }

        void OnEnable() { RenderPipelineManager.endCameraRendering += OnEndCameraRendering; }
        void OnDisable()
        {
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            if (Busy) Fail("Capture cancelled because component was disabled");
        }

        public bool RequestCapture(bool automatic)
        {
            if (Busy) { Status = "Capture already pending"; return false; }
            if (sourceCamera == null || !sourceCamera.isActiveAndEnabled || walker == null || !walker.Ready)
            { Fail("Camera or walkable floor is not ready"); return false; }
            if (string.IsNullOrEmpty(SessionFolder)) { Fail("Output folder is not available"); return false; }
            Busy = true; LastCaptureSucceeded = false;
            pendingMode = automatic ? "automatic" : "manual";
            requestedAt = Time.realtimeSinceStartup;
            Status = "Waiting for rendered frame";
            return true;
        }

        public bool TilesReady()
        {
            if (!settings.waitForTiles) return true;
            foreach (var t in tilesets)
                if (t != null && t.isActiveAndEnabled && t.ComputeLoadProgress() < 99.9f) return false;
            return true;
        }

        void Update()
        {
            if (Busy && Time.realtimeSinceStartup - requestedAt > settings.captureTimeout)
            {
                string progress="";
                foreach(var t in tilesets) if(t!=null) progress+=t.name+"="+t.ComputeLoadProgress().ToString("F2")+"%; ";
                Fail("Capture timeout; camera rendered frames="+SourceRenderedFrames+"; tile progress: "+progress);
            }
        }

        void EnsureCamera()
        {
            if (captureCamera == null)
            {
                var go = new GameObject("Research image camera") { hideFlags = HideFlags.HideAndDontSave };
                captureCamera = go.AddComponent<Camera>(); captureCamera.enabled = false;
                go.AddComponent<UniversalAdditionalCameraData>();
            }
            if (target != null && (target.width != settings.imageWidth || target.height != settings.imageHeight))
            { target.Release(); Destroy(target); target = null; }
            if (target == null)
            {
                target = new RenderTexture(Mathf.Clamp(settings.imageWidth,64,8192), Mathf.Clamp(settings.imageHeight,64,8192), 24,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "Research PNG target", antiAliasing = 1 };
                target.Create();
            }
            captureCamera.CopyFrom(sourceCamera);
            captureCamera.enabled = false;
            captureCamera.transform.SetPositionAndRotation(sourceCamera.transform.position, sourceCamera.transform.rotation);
            captureCamera.targetTexture = target;
            captureCamera.aspect = (float)target.width / target.height;
            captureCamera.cullingMask = sourceCamera.cullingMask & settings.imageMask;
            captureCamera.useOcclusionCulling = false;
            var additional = captureCamera.GetUniversalAdditionalCameraData();
            additional.renderType = CameraRenderType.Base;
            additional.cameraStack.Clear();
            additional.renderPostProcessing = sourceCamera.GetUniversalAdditionalCameraData().renderPostProcessing;
            additional.volumeLayerMask = sourceCamera.GetUniversalAdditionalCameraData().volumeLayerMask;
            additional.volumeTrigger = sourceCamera.transform;
            additional.antialiasing = AntialiasingMode.None;
            additional.allowXRRendering = false;
        }

        // URP 13.1's supported standalone rendering entry point. This callback runs after
        // Update/LateUpdate, so pose, modes, timestamp and pixels are sampled together.
        void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if(camera==sourceCamera) SourceRenderedFrames++;
            if (!Busy || rendering || camera != sourceCamera || !TilesReady()) return;
            rendering = true;
            var canvases = FindObjectsOfType<Canvas>();
            var enabledCanvases = new bool[canvases.Length];
            RenderTexture previous = RenderTexture.active;
            Texture2D pixels = null;
            try
            {
                EnsureCamera();
                for (int i = 0; i < canvases.Length; i++) { enabledCanvases[i] = canvases[i].enabled; canvases[i].enabled = false; }
                CaptureRecord record = MakeRecord();
                using (new CaptureGizmoScope())
                {
                    UniversalRenderPipeline.RenderSingleCamera(context, captureCamera);
                    context.Submit();
                }
                RenderTexture.active = target;
                pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false, false);
                pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0,false);
                pixels.Apply(false, false);
                byte[] png = pixels.EncodeToPNG();
                CommitPair(png, record);
                LastRecord = record;
                SavedCount++;
                LastCaptureSucceeded = true;
                Busy = false;
                Status = "Saved " + record.imageId;
                Completed?.Invoke(true);
            }
            catch (Exception e) { Fail(e.GetType().Name + ": " + e.Message); }
            finally
            {
                RenderTexture.active = previous;
                if (pixels != null) Destroy(pixels);
                for (int i = 0; i < canvases.Length; i++) if (canvases[i] != null) canvases[i].enabled = enabledCanvases[i];
                rendering = false;
            }
        }

        CaptureRecord MakeRecord()
        {
            var p = captureCamera.transform.position;
            string id = Guid.NewGuid().ToString("N");
            float fov = captureCamera.fieldOfView;
            var result = new CaptureRecord {
                imageId=id, fileName="images/" + id + ".png", capturedAtUtc=DateTime.UtcNow.ToString("O"),
                unityFrame=Time.frameCount, unityRealtimeSeconds=Time.realtimeSinceStartupAsDouble,
                cameraPositionUnity=p, cameraRotationUnity=captureCamera.transform.rotation,
                cameraEulerDegrees=captureCamera.transform.eulerAngles,
                unityUnitsPerMetre=settings.unityUnitsPerMetre,
                lengthUnit = settings.unityUnitsPerMetre == 1 ? "metre (1 Unity unit = 1 metre)" : "Unity unit; metres = Unity units / unityUnitsPerMetre",
                verticalFieldOfViewDegrees=fov,
                horizontalFieldOfViewDegrees=2 * Mathf.Atan(Mathf.Tan(fov*Mathf.Deg2Rad/2)*captureCamera.aspect)*Mathf.Rad2Deg,
                width=target.width, height=target.height, movementMode=walker.Mode.ToString().ToLowerInvariant(),
                captureMode=pendingMode, roomId=RoomRegion.Resolve(p), projectionMatrix=new float[16]
            };
            result.researchAmbientFill=settings.useAmbientFill;
            result.ambientFillColor=settings.ambientFillColor;
            for (int i=0;i<16;i++) result.projectionMatrix[i]=captureCamera.projectionMatrix[i];
            if (georeference != null)
            {
                Vector3 local=georeference.transform.InverseTransformPoint(p);
                var ecef=georeference.TransformUnityPositionToEarthCenteredEarthFixed(new Unity.Mathematics.double3(local.x,local.y,local.z));
                result.ecefMetres = new[] { ecef.x,ecef.y,ecef.z };
                result.georeferenceDefinition="Cesium ECEF metres from serialized CesiumGeoreference; local axes East/Up/North. This is not asserted to be the original CityGML CRS.";
            }
            return result;
        }

        void CommitPair(byte[] png, CaptureRecord record)
        {
            string image = Path.Combine(SessionFolder,"images",record.imageId+".png");
            string label = Path.Combine(SessionFolder,"ground_truth",record.imageId+".json");
            string tempImage=image+".tmp", tempLabel=label+".tmp";
            bool imageCommitted=false, labelCommitted=false;
            try
            {
                using(var stream=new FileStream(tempImage,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                { stream.Write(png,0,png.Length); stream.Flush(true); }
                using(var stream=new FileStream(tempLabel,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                {
                    byte[] json=Utf8.GetBytes(JsonUtility.ToJson(record,true));
                    stream.Write(json,0,json.Length); stream.Flush(true);
                }
                File.Move(tempImage,image); imageCommitted=true;
                File.Move(tempLabel,label); labelCommitted=true;
            }
            catch
            {
                if (imageCommitted && !labelCommitted) TryDelete(image);
                TryDelete(tempImage); TryDelete(tempLabel);
                throw;
            }
        }

        static void TryDelete(string path) { try { File.Delete(path); } catch (Exception) { } }
        void Fail(string message)
        {
            bool wasBusy=Busy; Busy=false; LastCaptureSucceeded=false; Status=message;
            LogEvent("capture_failed",message);
            Debug.LogWarning("[ResearchCapture] " + message);
            if (wasBusy) Completed?.Invoke(false);
        }
        public void LogEvent(string type,string detail)
        {
            if (string.IsNullOrEmpty(SessionFolder)) return;
            try { File.AppendAllText(Path.Combine(SessionFolder,"events.jsonl"),JsonUtility.ToJson(new CaptureEvent {
                timestampUtc=DateTime.UtcNow.ToString("O"),type=type,detail=detail }) + "\n",Utf8); }
            catch(Exception e) { Debug.LogWarning("[ResearchCapture] Event log failed: "+e.Message); }
        }
        void OnDestroy()
        {
            if(target!=null) { target.Release(); Destroy(target); }
            if(captureCamera!=null) Destroy(captureCamera.gameObject);
        }
    }
}
