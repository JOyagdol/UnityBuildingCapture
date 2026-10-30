#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace UnityResearchCapture
{
    // Run in an isolated copy with two configured building scenes; no model-specific data is embedded here.
    public sealed class BuildingControlsValidationProbe : MonoBehaviour
    {
        [Serializable] class Check { public string name; public bool passed; public string detail; }
        [Serializable] class FloorResult { public string floorId; public int routePoints,clearSamples,connectedSamples,outsideNavMesh,disconnected; public Vector3 feet; }
        [Serializable] class Report { public bool graphicsAvailable; public List<Check> checks=new List<Check>(); public List<FloorResult> floors=new List<FloorResult>(); public List<string> imageRecords=new List<string>(); public int routeCompletions; }
        Report report=new Report(); ResearchWalker w; ResearchBuildingExplorer e; float deadline;
        bool finishing;
        static BuildingControlsValidationProbe active;
        void Awake()
        {
            if(active!=null && active!=this){enabled=false;Destroy(gameObject);return;}
            active=this;
        }
        void CheckThat(string name,bool passed,string detail="") {report.checks.Add(new Check{name=name,passed=passed,detail=detail});Debug.Log("BUILDING_CHECK "+(passed?"PASS ":"FAIL ")+name+" "+detail);}
        void Update(){if(!finishing && deadline>0 && Time.realtimeSinceStartup>deadline){CheckThat("deadline",false);Finish();}}
        void Finish()
        {
            if(finishing)return;finishing=true;
            Directory.CreateDirectory("ResearchValidation");File.WriteAllText("ResearchValidation/building-controls-validation.json",JsonUtility.ToJson(report,true));
            Debug.Log("BUILDING_VALIDATION_COMPLETE passed="+report.checks.Count(x=>x.passed)+" failed="+report.checks.Count(x=>!x.passed));
            ValidationSceneSession.Complete(report.checks.All(x=>x.passed)?0:2);
        }
        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);deadline=Time.realtimeSinceStartup+480;
            report.graphicsAvailable=SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Null;
            yield return WaitForScene("");
            CheckThat("two_floor_profile",e!=null && e.floors.Length==2);
            if(e==null || e.floors.Length!=2){Finish();yield break;}
            Prepare(); string firstBuilding=e.buildingId;string firstSession=w.capture.SessionFolder;
            for(int i=0;i<e.floors.Length;i++)
            {
                CheckThat("floor_select_"+i,e.SelectFloor(i));
                CheckThat("eye_height_"+i,Mathf.Abs(w.viewCamera.transform.position.y-w.transform.position.y-1.6f)<0.001f);
                yield return w.navigation.Build(w.transform.position);
                var f=new FloorResult{floorId=e.CurrentFloorId,routePoints=w.navigation.Points.Count,feet=w.transform.position};report.floors.Add(f);
                CheckThat("route_ready_"+i,w.navigation.IsReady && f.routePoints>0,"points="+f.routePoints);
                var area=w.navigation.areas[0];var b=area.WorldBounds;var path=new NavMeshPath();
                for(float z=b.min.z;z<=b.max.z;z+=0.25f)
                {
                    for(float x=b.min.x;x<=b.max.x;x+=0.25f)
                    {
                        if(!w.navigation.TryGround(new Vector3(x,area.floorHeight,z),out var hit)||!w.navigation.ClearBody(hit.point))continue;
                        f.clearSamples++;
                        if(!NavMesh.SamplePosition(hit.point,out var h,0.12f,w.navigation.Filter)){f.outsideNavMesh++;continue;}
                        if(w.navigation.Path(w.transform.position,h.position,path))f.connectedSamples++;else f.disconnected++;
                    }
                    yield return null;
                }
                CheckThat("no_disconnected_floor_samples_"+i,f.disconnected==0,"clear="+f.clearSamples+" connected="+f.connectedSamples+" outside="+f.outsideNavMesh);
            }
            CheckThat("return_first_floor",e.SelectFloor(0));
            Vector3 before=w.transform.position;var oldAreas=w.navigation.areas;var arrival=e.floors[1].arrivalFeet;
            e.floors[1].arrivalFeet=new Vector3(10000,10000,10000);
            CheckThat("invalid_floor_rejected",!e.SelectFloor(1));
            CheckThat("invalid_floor_preserves_pose_and_area",w.transform.position==before&&w.navigation.areas==oldAreas&&e.CurrentFloorIndex==0);
            e.floors[1].arrivalFeet=arrival;
            var original=e.buildings[0];e.buildings[0]=new ResearchBuildingDestination{scenePath="Assets/Scenes/MissingValidationScene.unity"};
            CheckThat("missing_building_rejected",!e.SwitchBuilding(0)&&!e.Busy&&w.transform.position==before);e.buildings[0]=original;
            w.ToggleAutoCapture();CheckThat("auto_capture_independent_of_floor",e.SelectFloor(1)&&w.AutoCapture);w.ToggleAutoCapture();e.SelectFloor(0);
            yield return w.navigation.Build(w.transform.position);before=w.transform.position;var rotation=w.viewCamera.transform.rotation;
            w.ToggleMovement();CheckThat("F1_no_teleport",w.Mode==MovementMode.Automatic&&w.transform.position==before&&w.viewCamera.transform.rotation==rotation);w.ToggleMovement();
            w.navigation.ClearRoute();CheckThat("tour_start_for_cancel",e.StartTour());yield return null;yield return null;e.StopTour();
            while(w.IsPreparing||e.TourRunning)yield return null;
            yield return null;CheckThat("cancel_during_preparation_stays_manual",w.Mode==MovementMode.Manual);
            w.settings.loopRoute=true;w.AutomaticRouteCompleted+=()=>report.routeCompletions++;
            CheckThat("full_tour_start",e.StartTour());
            yield return null;
            while(e.TourRunning)yield return null;
            CheckThat("both_floors_completed",report.routeCompletions==2&&e.CurrentFloorIndex==1,"completed="+report.routeCompletions+" status="+e.Status);
            CheckThat("tour_finishes_manual",w.Mode==MovementMode.Manual&&!w.SuppressRouteLoop);
            string events=File.ReadAllText(Path.Combine(firstSession,"events.jsonl"));
            CheckThat("no_stalled_waypoints",!events.Contains("no progress / collider blocked")&&!events.Contains("waypoint timeout"));
            w.settings.loopRoute=false;
            if(report.graphicsAvailable)
            {
                for(int i=0;i<e.floors.Length;i++)
                {
                    e.SelectFloor(i);yield return null;
                    string expectedFloor=e.CurrentFloorId;int count=w.capture.SavedCount;
                    CheckThat("manual_photo_request_"+i,w.capture.RequestCapture(false));
                    CheckThat("floor_switch_waits_for_photo_"+i,!e.SelectFloor(1-i));
                    CheckThat("building_switch_waits_for_photo_"+i,!e.SwitchBuilding(0));
                    while(w.capture.Busy)yield return null;
                    CheckPhoto("manual_photo_"+i,count+1,firstBuilding,expectedFloor,"manual");
                    var target=new GameObject("Validation capture point").transform;target.position=w.transform.position;
                    w.navigation.useInspectorWaypoints=true;w.navigation.waypoints=new[]{target};
                    yield return w.navigation.Build(w.transform.position);w.ToggleAutoCapture();w.ToggleMovement();
                    while(w.IsPreparing||w.Mode==MovementMode.Automatic||w.capture.Busy)yield return null;
                    CheckPhoto("automatic_photo_"+i,count+2,firstBuilding,expectedFloor,"automatic");
                    w.ToggleAutoCapture();w.navigation.useInspectorWaypoints=false;Destroy(target.gameObject);
                }
            }
            int other=Array.FindIndex(e.buildings,b=>b!=null&&b.buildingId!=firstBuilding);string otherPath=e.buildings[other].scenePath;string otherId=e.buildings[other].buildingId;
            CheckThat("switch_other_building",e.SwitchBuilding(other));
            CheckThat("capture_locked_during_scene_switch",w.InputSuspended&&!w.capture.RequestCapture(false));
            yield return WaitForScene(otherPath);Prepare();
            CheckThat("other_building_context",e.buildingId==otherId&&e.CurrentFloorIndex==0&&w.Mode==MovementMode.Manual&&!w.AutoCapture);
            CheckThat("new_capture_session",w.capture.SessionFolder!=firstSession&&w.capture.SavedCount==0);
            if(report.graphicsAvailable){int count=w.capture.SavedCount;w.capture.RequestCapture(false);while(w.capture.Busy)yield return null;CheckPhoto("other_building_photo",count+1,otherId,e.CurrentFloorId,"manual");}
            int back=Array.FindIndex(e.buildings,b=>b!=null&&b.buildingId==firstBuilding);string backPath=e.buildings[back].scenePath;
            CheckThat("switch_back",e.SwitchBuilding(back));yield return WaitForScene(backPath);
            CheckThat("switch_back_resets_to_first_floor",e.buildingId==firstBuilding&&e.CurrentFloorIndex==0&&w.Mode==MovementMode.Manual&&w.capture.SessionFolder!=firstSession);
            Finish();
        }
        IEnumerator WaitForScene(string path)
        {
            w=null;e=null;
            while(w==null||!w.Ready||!w.capture.TilesReady())
            {
                if(string.IsNullOrEmpty(path)||SceneManager.GetActiveScene().path==path){w=FindObjectOfType<ResearchWalker>();if(w!=null)e=w.GetComponent<ResearchBuildingExplorer>();}
                yield return null;
            }
        }
        void Prepare()
        {
            w.settings=Instantiate(w.settings);w.navigation.settings=w.settings;w.capture.settings=w.settings;
            w.settings.walkSpeed=6;w.settings.imageWidth=320;w.settings.imageHeight=180;w.settings.directionCount=1;w.settings.settleSeconds=0.05f;w.settings.captureTimeout=15;
            w.settings.stuckSeconds=3;w.settings.waypointTimeout=15;w.SetCursorLocked(false);
        }
        void CheckPhoto(string name,int count,string building,string floor,string mode)
        {
            var r=w.capture.LastRecord;
            bool passed=w.capture.SavedCount==count&&w.capture.LastCaptureSucceeded&&r!=null&&r.schemaVersion==3&&r.buildingId==building&&r.floorId==floor&&r.captureMode==mode;
            if(r!=null)
            {
                string record=Path.Combine(w.capture.SessionFolder,"ground_truth",r.imageId+".json");
                passed&=File.Exists(record)&&File.Exists(Path.Combine(w.capture.SessionFolder,r.fileName));
                report.imageRecords.Add(record);
            }
            CheckThat(name,passed,w.capture.Status);
        }
    }
}
#endif