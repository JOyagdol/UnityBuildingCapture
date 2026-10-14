using System.Linq;
using CesiumForUnity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;

namespace UnityResearchCapture.Editor
{
    public static class ResearchCaptureSetup
    {
        const string LocalFolder = "Assets/ResearchCaptureLocal";
        const string SettingsPath = LocalFolder + "/ResearchCaptureSettings.asset";

        [MenuItem("Tools/Research Capture/Set Up Active Scene")]
        public static void SetUpActiveScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var scene = SceneManager.GetActiveScene();
            var existing = scene.GetRootGameObjects()
                .SelectMany(go => go.GetComponentsInChildren<ResearchWalker>(true)).FirstOrDefault();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.Log("Research Capture: existing rig selected; no duplicate created.");
                return;
            }
            if (!AssetDatabase.IsValidFolder(LocalFolder))
                AssetDatabase.CreateFolder("Assets", "ResearchCaptureLocal");
            var settings = AssetDatabase.LoadAssetAtPath<ResearchCaptureSettings>(SettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<ResearchCaptureSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }

            // A locally selected marker supplies the feet position. No site coordinates are embedded.
            var selected = Selection.activeTransform;
            Vector3 feet = selected != null && selected.gameObject.scene == scene
                ? selected.position : Vector3.zero;
            var sceneBehaviours = scene.GetRootGameObjects()
                .SelectMany(go => go.GetComponentsInChildren<MonoBehaviour>(true)).ToArray();
            var geo = sceneBehaviours.OfType<CesiumGeoreference>().FirstOrDefault();
            var tilesets = sceneBehaviours.OfType<Cesium3DTileset>().ToArray();
            var otherCameras = scene.GetRootGameObjects()
                .SelectMany(go => go.GetComponentsInChildren<Camera>(true)).ToArray();

            var root = new GameObject("ResearchCaptureRig");
            Undo.RegisterCreatedObjectUndo(root, "Set up research capture");
            root.SetActive(false);
            root.transform.position = feet;
            root.AddComponent<CharacterController>();
            var walker = root.AddComponent<ResearchWalker>();
            var navigation = root.AddComponent<ResearchNavigation>();
            var writer = root.AddComponent<ResearchCaptureWriter>();
            var hud = root.AddComponent<ResearchCaptureHud>();

            var cameraObject = new GameObject("Research Camera");
            cameraObject.transform.SetParent(root.transform, false);
            cameraObject.transform.localPosition = Vector3.up * settings.eyeHeight * settings.unityUnitsPerMetre;
            var camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f * settings.unityUnitsPerMetre;
            camera.farClipPlane = 500f * settings.unityUnitsPerMetre;
            camera.fieldOfView = settings.verticalFieldOfView;
            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<UniversalAdditionalCameraData>();
            cameraObject.tag = "MainCamera";

            walker.settings = settings;
            walker.navigation = navigation;
            walker.capture = writer;
            walker.viewCamera = camera;
            walker.otherCameras = otherCameras;
            walker.competingControllers = sceneBehaviours.Where(b => b is CesiumCameraController ||
                b is CesiumFlyToController || b is CesiumOriginShift).Cast<Behaviour>().ToArray();
            writer.settings = settings;
            writer.walker = walker;
            writer.sourceCamera = camera;
            writer.georeference = geo;
            writer.tilesets = tilesets;
            navigation.settings = settings;

            var areaObject = new GameObject("CaptureArea");
            Undo.RegisterCreatedObjectUndo(areaObject, "Create capture area");
            areaObject.transform.position = feet;
            var box = areaObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(4, 3, 4) * settings.unityUnitsPerMetre;
            box.center = Vector3.up * settings.unityUnitsPerMetre;
            var area = areaObject.AddComponent<CaptureArea>();
            area.floorHeight = feet.y;
            area.floorTolerance = 0.25f * settings.unityUnitsPerMetre;
            area.requireCityGmlFloor = tilesets.Length > 0;
            area.approvedForCollection = false;
            navigation.areas = new[] { area };
            hud.walker = walker;
            hud.capture = writer;
            hud.navigation = navigation;
            root.SetActive(true);
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = areaObject;
            Debug.Log("Research Capture: configure floor height, scale, colliders and collection bounds locally. " +
                "Enable Approved For Collection after checking the area, then save your scene outside this source repository.");
        }

        [MenuItem("Tools/Research Capture/Add Room Ground Truth Region")]
        public static void AddRoomRegion()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var go = new GameObject("RoomRegion_unknown");
            Undo.RegisterCreatedObjectUndo(go, "Create room region");
            go.AddComponent<BoxCollider>().isTrigger = true;
            go.AddComponent<RoomRegion>();
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }
    }
}
