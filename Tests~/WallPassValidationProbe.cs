#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityResearchCapture
{
    public sealed class WallPassValidationProbe : MonoBehaviour
    {
        public GameObject floor;
        [Serializable] class Report { public List<string> passed=new List<string>(); public List<string> failed=new List<string>(); }
        readonly Report report=new Report();
        float deadline;
        void Check(bool value,string name)
        {
            (value?report.passed:report.failed).Add(name);
            Debug.Log((value?"WALL_PASS_OK: ":"WALL_PASS_FAIL: ")+name);
        }
        void Update() { if(deadline>0 && Time.realtimeSinceStartup>deadline) Finish(3); }
        void Finish(int code)
        {
            Directory.CreateDirectory("ResearchValidation");
            File.WriteAllText("ResearchValidation/wall-pass-validation.json",JsonUtility.ToJson(report,true));
            Debug.Log("WALL_PASS_COMPLETE passed="+report.passed.Count+" failed="+report.failed.Count);
            ValidationSceneSession.Complete(code);
        }
        IEnumerator Start()
        {
            deadline=Time.realtimeSinceStartup+90;
            var walker=GetComponent<ResearchWalker>();
            var nav=walker.navigation;
            var writer=walker.capture;
            var body=GetComponent<CharacterController>();
            yield return null; yield return null;
            Check(walker.Ready && !walker.PassThroughObstacles && body.enabled,"default collision mode and floor initialization");
            for(int i=0;i<50;i++) { walker.StepManual(Vector2.up,0.03f,false); yield return null; }
            Check(transform.position.z < -0.2f,"closed barrier blocks normal walking");
            for(int i=0;i<12;i++) { walker.StepManual(Vector2.down,0.04f,false); yield return null; }
            yield return nav.Build(transform.position);
            Check(!nav.IsReady,"closed barrier rejects normal route to the opposite room");

            var pose=transform.position;
            var look=walker.viewCamera.transform.rotation;
            walker.ToggleAutoCapture();
            Check(walker.SetPassThroughObstacles(true),"pass-through can be enabled");
            Check(transform.position==pose && Quaternion.Angle(look,walker.viewCamera.transform.rotation)<0.001f &&
                walker.AutoCapture && !body.enabled,"toggle preserves pose and capture state with a single position owner");
            walker.ToggleAutoCapture();
            yield return nav.Build(transform.position);
            Check(nav.IsReady && nav.Points.Count==1 && nav.Points[0].z>1,"pass-through route reaches opposite room but excludes barrier capture point");
            if(nav.IsReady)
            {
                if(!Application.isBatchMode) walker.ToggleAutoCapture();
                walker.ToggleMovement();
                Check(walker.Mode==MovementMode.Automatic,"automatic traversal starts on supported floor");
                float end=Time.realtimeSinceStartup+8;
                while(walker.Mode==MovementMode.Automatic && Time.realtimeSinceStartup<end) yield return null;
                Check(walker.Mode==MovementMode.Manual && transform.position.z>1,"automatic route crosses closed barrier and completes");
                if(!Application.isBatchMode)
                {
                    Check(writer.SavedCount==2 && writer.LastRecord!=null && writer.LastRecord.obstaclePassThrough &&
                        writer.LastRecord.schemaVersion==3 && writer.LastRecord.movementMode=="automatic",
                        "automatic pass-through route saves directional PNG and traversal metadata");
                    if(walker.AutoCapture) walker.ToggleAutoCapture();
                }
            }
            Check(Mathf.Abs(transform.position.y)<0.03f && Mathf.Abs(walker.viewCamera.transform.position.y-1.6f)<0.03f,
                "automatic traversal follows the floor at human eye height");
            // Directional capture changes yaw; subsequent synthetic input expects forward +Z.
            walker.Look(new Vector2(-Mathf.DeltaAngle(0,walker.viewCamera.transform.eulerAngles.y)/walker.settings.mouseSensitivity,0));
            Check(walker.SetPassThroughObstacles(false) && body.enabled && !nav.IsReady,"restoring collisions invalidates old route");
            for(int i=0;i<35;i++) { walker.StepManual(Vector2.down,0.04f,false); yield return null; }
            Check(transform.position.z>0.2f,"collision walking blocks the reverse crossing");

            walker.SetPassThroughObstacles(true);
            walker.Look(new Vector2(0,100));
            for(int i=0;i<50 && transform.position.z>0.05f;i++)
            { walker.StepManual(Vector2.down,0.01f,false); yield return null; }
            Check(Mathf.Abs(transform.position.z)<0.09f && Mathf.Abs(transform.position.y)<0.03f,
                "manual pass-through enters barrier horizontally even while looking up");
            pose=transform.position;
            Check(!walker.SetPassThroughObstacles(false) && walker.PassThroughObstacles && transform.position==pose,
                "collision re-enable is rejected inside barrier without relocation");
            int saved=writer.SavedCount;
            Check(!writer.RequestCapture(false) && !writer.Busy && writer.SavedCount==saved,
                "manual image capture is rejected inside barrier");
            walker.ToggleAutoCapture();
            for(int i=0;i<30;i++) { walker.StepManual(Vector2.zero,0.03f,false); yield return null; }
            Check(!writer.Busy && writer.SavedCount==saved,"stationary pass-through does not schedule automatic images");
            walker.ToggleAutoCapture();
            walker.StepManual(Vector2.down,1,false);
            yield return null;
            Check(transform.position.z < -1 && walker.CanCaptureHere,"manual traversal exits into a clear capture location");

            if(!Application.isBatchMode)
            {
                saved=writer.SavedCount;
                writer.RequestCapture(false);
                while(writer.Busy) yield return null;
                Check(writer.SavedCount==saved+1 && writer.LastRecord.obstaclePassThrough &&
                    writer.LastRecord.captureMode=="manual","manual photo saves pass-through metadata");
                saved=writer.SavedCount;
                pose=transform.position;
                writer.RequestCapture(false);
                transform.position=Vector3.zero; // Enter the synthetic barrier before the render callback.
                while(writer.Busy) yield return null;
                Check(writer.SavedCount==saved && !writer.LastCaptureSucceeded,
                    "capture request is cancelled if camera enters barrier before rendering");
                transform.position=pose;
                walker.ToggleAutoCapture();
                walker.StepManual(Vector2.left,1,false);
                while(writer.Busy) yield return null;
                Check(writer.SavedCount==saved+1 && writer.LastRecord.captureMode=="automatic" &&
                    writer.LastRecord.movementMode=="manual" && writer.LastRecord.obstaclePassThrough,
                    "manual pass-through walking supports distance-triggered photos");
                walker.ToggleAutoCapture();
            }

            floor.SetActive(false);
            MakeFloor("Synthetic near floor",new Vector3(0,-0.25f,-2),new Vector3(8,0.5f,2));
            MakeFloor("Synthetic far floor",new Vector3(0,-0.25f,2),new Vector3(8,0.5f,2));
            MakeFloor("Synthetic lower floor",new Vector3(0,-3.25f,0),new Vector3(8,0.5f,2));
            Physics.SyncTransforms();
            walker.StepManual(Vector2.up,3,false);
            Check(transform.position.z<=-0.95f && transform.position.z>=-1.1f && Mathf.Abs(transform.position.y)<0.03f,
                "substeps stop at floor gap even when the requested endpoint is supported");
            walker.StepManual(Vector2.right,8,true);
            Check(transform.position.x<=3.51f && Mathf.Abs(transform.position.y)<0.03f,
                "collection boundary and floor level remain enforced");
            Check(Mathf.Abs(walker.viewCamera.transform.position.y-transform.position.y-1.6f)<0.01f,
                "manual traversal preserves eye height");
            // Real exports often end floor finishes at each wall face.
            // Model that topology rather than hiding it under a continuous test floor.
            GameObject.Find("Synthetic near floor").SetActive(false);
            GameObject.Find("Synthetic far floor").SetActive(false);
            GameObject.Find("Synthetic lower floor").SetActive(false);
            MakeFloor("Seam near floor",new Vector3(0,-0.25f,-1.075f),new Vector3(8,0.5f,1.85f));
            MakeFloor("Seam far floor",new Vector3(0,-0.25f,1.075f),new Vector3(8,0.5f,1.85f));
            transform.position=new Vector3(0,0,-1);
            walker.Look(new Vector2(-Mathf.DeltaAngle(0,walker.viewCamera.transform.eulerAngles.y)/walker.settings.mouseSensitivity,0));
            Physics.SyncTransforms();
            Check(!nav.TryGround(Vector3.zero,out var rawSeam),"wall seam has no fabricated capture floor");
            Check(nav.TryTraversalGround(Vector3.zero,out var seam) && Mathf.Abs(seam.y)<0.01f,
                "wall seam uses two same-floor anchors for traversal support");
            for(int i=0;i<75;i++) {walker.StepManual(Vector2.up,0.02f,false); yield return null;}
            Check(transform.position.z>0.8f && Mathf.Abs(transform.position.y)<0.02f,
                "manual pass-through crosses a missing floor finish beneath a wall");
            transform.position=Vector3.zero;
            Check(!walker.CanCaptureHere && !writer.RequestCapture(false),"interpolated wall seam cannot become an image endpoint");
            transform.position=new Vector3(0,0,-1);
            yield return nav.Build(transform.position);
            Check(nav.IsReady && nav.Points.Count==1,"automatic route connects real floor on both sides of wall seam");
            if(nav.IsReady)
            {
                walker.ToggleMovement();
                float seamEnd=Time.realtimeSinceStartup+8;
                while(walker.Mode==MovementMode.Automatic && Time.realtimeSinceStartup<seamEnd) yield return null;
                Check(transform.position.z>1 && walker.Mode==MovementMode.Manual,"automatic traversal crosses wall seam and completes");
                walker.UseManual("Synthetic seam tests complete");
            }
            var seamBarrier=GameObject.Find("Synthetic closed barrier");
            seamBarrier.SetActive(false);
            Physics.SyncTransforms();
            Check(!nav.TryTraversalGround(Vector3.zero,out seam),"an open floor hole without a wall is not bridged");
            seamBarrier.SetActive(true);
            GameObject.Find("Seam far floor").SetActive(false);
            Physics.SyncTransforms();
            Check(!nav.TryTraversalGround(Vector3.zero,out seam),"wall traversal requires a real floor on the far side");
            // A real low step must not become a gap when floor samples are triangulated.
            GameObject.Find("Seam near floor").SetActive(false);
            seamBarrier.SetActive(false);
            MakeFloor("Step lower floor",new Vector3(0,-0.25f,-1),new Vector3(8,0.5f,2));
            MakeFloor("Step upper floor",new Vector3(0,-0.10f,1),new Vector3(8,0.5f,2));
            transform.position=new Vector3(0,0,-1);
            nav.waypoints=new[]{nav.waypoints[0]};
            nav.waypoints[0].position=new Vector3(0,0.15f,1.2f);
            Physics.SyncTransforms();
            yield return nav.Build(transform.position);
            Check(nav.IsReady && nav.Points.Count==1,"permitted low step keeps the automatic floor graph connected");
            if(nav.IsReady)
            {
                walker.ToggleMovement();
                float stepEnd=Time.realtimeSinceStartup+8;
                while(walker.Mode==MovementMode.Automatic && Time.realtimeSinceStartup<stepEnd) yield return null;
                Check(walker.Mode==MovementMode.Manual && transform.position.z>1 && Mathf.Abs(transform.position.y-0.15f)<0.02f,
                    "automatic pass-through traverses a real low step at its actual floor height");
            }
            GameObject.Find("Step upper floor").transform.position=new Vector3(0,0.25f,1);
            nav.areas[0].floorTolerance=0.75f;
            nav.waypoints[0].position=new Vector3(0,0.5f,1.2f);
            transform.position=new Vector3(0,0,-1);
            Physics.SyncTransforms();
            yield return nav.Build(transform.position);
            Check(!nav.IsReady,"excessive step remains disconnected despite the bake seam allowance");
            walker.StepManual(Vector2.up,2,false);
            Check(transform.position.z<=0.01f && Mathf.Abs(transform.position.y)<0.02f,"manual movement cannot climb an excessive step");
            Finish(report.failed.Count==0?0:2);
        }
        static void MakeFloor(string name,Vector3 position,Vector3 size)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name=name; go.transform.position=position; go.transform.localScale=size;
        }
    }
}
#endif
