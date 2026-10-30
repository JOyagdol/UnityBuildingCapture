using System.Collections;
using CesiumForUnity;
using UnityEngine;
using UnityEngine.AI;

namespace UnityResearchCapture
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class ResearchWalker : MonoBehaviour
    {
        public ResearchCaptureSettings settings;
        public Camera viewCamera;
        public ResearchNavigation navigation;
        public ResearchCaptureWriter capture;
        public Behaviour[] competingControllers;
        public Camera[] otherCameras;
        public MovementMode Mode { get; private set; } = MovementMode.Manual;
        public bool AutoCapture { get; private set; }
        public bool PassThroughObstacles { get; private set; }
        public bool IsPreparing => preparing || navigation.IsBuilding;
        public bool InputSuspended { get; set; }
        public bool SuppressRouteLoop { get; set; }
        public event System.Action AutomaticRouteCompleted;
        public bool CanCaptureHere => Ready && !InputSuspended && navigation.TryGround(transform.position, out var floor, false) &&
            navigation.ClearBody(floor.point);
        public bool Ready { get; private set; }
        public string Status { get; private set; } = "Loading floor and colliders";
        public int RouteIndex { get; private set; }
        public bool IsCollecting => Mode == MovementMode.Automatic;
        CharacterController body;
        NavMeshPath path;
        int corner, direction, automaticRequestGeneration;
        float yaw, pitch, distanceWalked, reachedAt, startYaw, pointStarted, lastProgressAt;
        Vector3 lastProgressPosition;
        bool visiting, requestedDirection, preparing;
        float U => settings.unityUnitsPerMetre;

        void Awake()
        {
            Application.runInBackground=true;
            body=GetComponent<CharacterController>();
            body.radius=settings.bodyRadius*U;
            body.height=(settings.eyeHeight+0.16f)*U;
            body.center=Vector3.up*(body.height/2);
            body.skinWidth=0.015f*U;
            body.stepOffset=settings.stepHeight*U;
            body.slopeLimit=settings.maxSlope;
            body.minMoveDistance=0;
            var angles=viewCamera.transform.eulerAngles;
            yaw=angles.y; pitch=Mathf.DeltaAngle(0,angles.x);
            viewCamera.transform.localPosition=Vector3.up*settings.eyeHeight*U;
            viewCamera.fieldOfView=settings.verticalFieldOfView;
            AutoCapture=settings.initialAutoCapture;
            PassThroughObstacles=settings.initialPassThroughObstacles;
            navigation.SetPassThroughObstacles(PassThroughObstacles);
            body.enabled=!PassThroughObstacles;
            if(settings.useAmbientFill)
            {
                RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight=settings.ambientFillColor;
                var probe=new UnityEngine.Rendering.SphericalHarmonicsL2();
                probe.AddAmbientLight(settings.ambientFillColor);
                RenderSettings.ambientProbe=probe;
            }
            if(competingControllers!=null) foreach(var c in competingControllers) if(c!=null) c.enabled=false;
            if(otherCameras!=null) foreach(var c in otherCameras) if(c!=null && c!=viewCamera)
            { c.enabled=false; var listener=c.GetComponent<AudioListener>(); if(listener!=null) listener.enabled=false; }
            // Research coordinates remain fixed for the whole run. Camera movement never shifts the globe origin.
            foreach(var shift in FindObjectsOfType<CesiumOriginShift>()) shift.enabled=false;
            navigation.Log=capture.LogEvent;
        }

        IEnumerator Start()
        {
            SetCursorLocked(settings.lockCursorOnStart);
            float started=Time.realtimeSinceStartup;
            while(!Ready)
            {
                if(navigation.TryGround(transform.position,out var floor,false) && navigation.ClearBody(floor.point))
                {
                    body.enabled=false; transform.position=floor.point; body.enabled=!PassThroughObstacles;
                    Ready=true; Status="Manual movement ready";
                    capture.LogEvent("walking_ready","Feet="+transform.position+"; eye height metres="+settings.eyeHeight);
                }
                else
                {
                    Status=Time.realtimeSinceStartup-started>30
                        ? "Floor not ready. Check area floor height, tiles and collision geometry."
                        : "Loading verified floor and colliders...";
                    yield return null;
                }
            }
            if(settings.initialMovement==MovementMode.Automatic) ToggleMovement();
        }

        void Update()
        {
            if(Input.GetKeyDown(KeyCode.Escape)) SetCursorLocked(false);
            if(InputSuspended) return;
            if(Input.GetKeyDown(KeyCode.F1)) ToggleMovement();
            if(Input.GetKeyDown(KeyCode.F2)) ToggleAutoCapture();
            if(Input.GetKeyDown(KeyCode.F3)) TogglePassThroughObstacles();
            if(Input.GetKeyDown(KeyCode.P)) TakePhoto();
            if(Input.GetKeyDown(KeyCode.Escape)) SetCursorLocked(false);
            if(Input.GetMouseButtonDown(0) && !ResearchCaptureHud.IsOverPanel(Input.mousePosition)) SetCursorLocked(true);
            if(Cursor.lockState==CursorLockMode.Locked && !visiting)
                Look(new Vector2(Input.GetAxisRaw("Mouse X"),Input.GetAxisRaw("Mouse Y")));
            if(!Ready || preparing || navigation.IsBuilding) return;
            if(Mode==MovementMode.Manual)
            {
                if(Cursor.lockState==CursorLockMode.Locked)
                {
                    var input=new Vector2((Input.GetKey(KeyCode.D)?1:0)-(Input.GetKey(KeyCode.A)?1:0),
                        (Input.GetKey(KeyCode.W)?1:0)-(Input.GetKey(KeyCode.S)?1:0));
                    StepManual(input,Time.deltaTime,Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift));
                }
            }
            else StepAutomatic(Time.deltaTime);
        }

        public void Look(Vector2 delta)
        {
            yaw+=delta.x*settings.mouseSensitivity;
            pitch=Mathf.Clamp(pitch-delta.y*settings.mouseSensitivity,-settings.pitchLimit,settings.pitchLimit);
            ApplyView();
        }
        void ApplyView() { viewCamera.transform.rotation=Quaternion.Euler(pitch,yaw,0); }

        public void StepManual(Vector2 input,float dt,bool fast)
        {
            if(!Ready || InputSuspended || Mode!=MovementMode.Manual || preparing) return;
            input=Vector2.ClampMagnitude(input,1);
            Vector3 direction=Quaternion.Euler(0,yaw,0)*new Vector3(input.x,0,input.y);
            Vector3 before=transform.position;
            MoveSafely(direction*(fast?settings.fastSpeed:settings.walkSpeed)*U*dt);
            float travelled=Vector3.ProjectOnPlane(transform.position-before,Vector3.up).magnitude;
            if(AutoCapture && travelled>0.0001f*U)
            {
                distanceWalked+=travelled;
                if(distanceWalked>=settings.manualCaptureDistance*U && CanCaptureHere && !capture.Busy && capture.RequestCapture(true))
                    distanceWalked=0; // Only a new movement can schedule the next distance capture.
            }
        }

        bool MoveSafely(Vector3 motion)
        {
            if(PassThroughObstacles) return MoveThroughObstacles(motion);
            // Preflight the intended support: do not walk over unapproved gaps or unloaded floors.
            Vector3 next=transform.position+motion;
            if(!navigation.TryGround(next,out var nextGround,false)) { Status="Movement blocked: no approved floor support"; return false; }
            if(Mathf.Abs(nextGround.point.y-transform.position.y)>settings.stepHeight*U+0.04f*U) return false;
            Vector3 previous=transform.position;
            body.Move(motion+Vector3.down*(2*U*Mathf.Max(Time.deltaTime,0.01f)));
            if(navigation.TryGround(transform.position,out var under,false))
            {
                // Follow actual supporting surface; never use a fixed world camera Y.
                body.Move(Vector3.up*(under.point.y-transform.position.y));
            }
            else
            {
                body.enabled=false; transform.position=previous; body.enabled=true;
                Status="Movement paused: supporting collider unavailable";
                return false;
            }
            return (transform.position-previous).sqrMagnitude>0.000001f;
        }

        bool MoveThroughObstacles(Vector3 motion)
        {
            // Small substeps follow real floor or a bounded wall/door bridge between real floor anchors.
            // No gravity/free-flight and no collider changes to the building are involved.
            motion=Vector3.ProjectOnPlane(motion,Vector3.up);
            float step=0.05f*U;
            motion=Vector3.ClampMagnitude(motion,256*step);
            int count=Mathf.Max(1,Mathf.CeilToInt(motion.magnitude/step));
            Vector3 increment=motion/count;
            Vector3 before=transform.position;
            for(int i=0;i<count;i++)
            {
                Vector3 next=transform.position+increment;
                if(!navigation.IsInSelectedArea(next,false))
                {
                    Status="이동 영역 경계입니다. CaptureArea 범위 밖으로는 이동하지 않습니다.";
                    break;
                }
                if(!navigation.TryTraversalGround(next,out var ground,false))
                {
                    Status="바닥 연결이 없습니다. 같은 층의 양쪽 바닥이 확인된 짧은 벽·문 구간만 통과합니다.";
                    break;
                }
                if(Mathf.Abs(ground.y-transform.position.y)>settings.stepHeight*U+0.04f*U)
                {
                    Status="허용된 단차보다 높습니다. 다른 층으로 이동하지 않습니다.";
                    break;
                }
                transform.position=new Vector3(next.x,ground.y,next.z);
            }
            return (transform.position-before).sqrMagnitude>0.000001f;
        }

        public void TogglePassThroughObstacles() { SetPassThroughObstacles(!PassThroughObstacles); }
        public bool SetPassThroughObstacles(bool enabled)
        {
            if(InputSuspended) return false;
            if(enabled==PassThroughObstacles) return true;
            if(preparing || navigation.IsBuilding || capture.Busy)
            {
                Status="Wait for navigation preparation/capture before changing pass-through";
                return false;
            }
            if(!enabled && !navigation.ClearBody(transform.position,0.02f*U))
            {
                Status="Cannot restore collisions inside an obstacle. Move to an open floor first.";
                capture.LogEvent("obstacle_pass_through_rejected",Status);
                return false;
            }
            UseManual("Pass-through changed; press F1 to prepare a new automatic route");
            PassThroughObstacles=enabled;
            body.enabled=!enabled;
            navigation.SetPassThroughObstacles(enabled);
            // Transform, view and automatic-capture flag are intentionally preserved.
            capture.LogEvent("obstacle_pass_through",enabled?"on":"off");
            Status=enabled?"벽·문 통과 켜짐: 같은 층 바닥과 설정된 이동 영역을 유지합니다.":
                "Wall/door pass-through OFF; collision walking restored";
            return true;
        }

        public void ToggleMovement()
        {
            if(InputSuspended) return;
            if(Mode==MovementMode.Automatic) { UseManual("Manual movement"); return; }
            if(preparing) { UseManual("Automatic preparation cancelled"); return; }
            StartCoroutine(StartAutomatic());
        }

        IEnumerator StartAutomatic()
        {
            if(!Ready) { Status="Cannot start automatic movement: floor is not ready"; yield break; }
            int generation=++automaticRequestGeneration;
            preparing=true;
            if(!navigation.IsReady) yield return navigation.Build(transform.position);
            preparing=false;
            if(generation!=automaticRequestGeneration) yield break;
            if(!navigation.IsReady || !NavMesh.SamplePosition(transform.position,out var atFeet,0.15f*U,navigation.Filter) ||
                Vector3.Distance(atFeet.position,transform.position)>0.15f*U)
            {
                Status="Automatic movement unavailable at current feet. "+navigation.Status;
                capture.LogEvent("mode_switch_rejected",Status); yield break;
            }
            Mode=MovementMode.Automatic; distanceWalked=0; RouteIndex=0;
            Status="Automatic route started"; capture.LogEvent("movement_mode","automatic");
            StartNextPoint();
        }

        public void UseManual(string reason)
        {
            automaticRequestGeneration++;
            Mode=MovementMode.Manual; visiting=false; requestedDirection=false; path=null; distanceWalked=0;
            // No transform, camera rotation, or CharacterController changes here.
            Status=reason; capture.LogEvent("movement_mode","manual: "+reason);
        }
        public void ToggleAutoCapture()
        {
            if(InputSuspended) return;
            AutoCapture=!AutoCapture; distanceWalked=0;
            capture.LogEvent("automatic_capture",AutoCapture?"on":"off");
            if(visiting && !AutoCapture && !capture.Busy) AdvancePoint();
        }
        public void TakePhoto() { capture.RequestCapture(false); }
        public void RebuildRoute()
        {
            if(InputSuspended || preparing || navigation.IsBuilding) return;
            UseManual("Preparing navigation at current position");
            StartCoroutine(navigation.Build(transform.position));
        }

        void StartNextPoint()
        {
            visiting=false; requestedDirection=false;
            while(RouteIndex<navigation.Points.Count)
            {
                path=new NavMeshPath();
                if(navigation.Path(transform.position,navigation.Points[RouteIndex],path) && path.corners.Length>0)
                {
                    corner=path.corners.Length>1?1:0;
                    pointStarted=lastProgressAt=Time.realtimeSinceStartup; lastProgressPosition=transform.position;
                    Status="Walking to point "+(RouteIndex+1)+" / "+navigation.Points.Count; return;
                }
                capture.LogEvent("waypoint_skipped","point="+RouteIndex+"; path is no longer complete"); RouteIndex++;
            }
            UseManual("Route complete");
            AutomaticRouteCompleted?.Invoke();
        }
        void AdvancePoint()
        {
            RouteIndex++;
            if(RouteIndex>=navigation.Points.Count && settings.loopRoute && !SuppressRouteLoop && navigation.Points.Count>1) RouteIndex=0;
            StartNextPoint();
        }

        void StepAutomatic(float dt)
        {
            if(visiting)
            {
                if(capture.Busy) return;
                if(!AutoCapture) { AdvancePoint(); return; }
                if(requestedDirection)
                {
                    requestedDirection=false; direction++;
                    if(direction>=settings.directionCount) { AdvancePoint(); return; }
                    SetCaptureDirection();
                }
                if(Time.realtimeSinceStartup-reachedAt>=settings.settleSeconds)
                {
                    if(capture.RequestCapture(true)) requestedDirection=true;
                    else { capture.LogEvent("waypoint_skipped","capture could not start at point "+RouteIndex); AdvancePoint(); }
                }
                return;
            }
            if(path==null || path.corners.Length==0) { StartNextPoint(); return; }
            Vector3 target=path.corners[corner];
            Vector3 delta=Vector3.ProjectOnPlane(target-transform.position,Vector3.up);
            if(delta.magnitude<0.09f*U)
            {
                if(corner<path.corners.Length-1) { corner++; return; }
                if(!AutoCapture) { AdvancePoint(); return; }
                visiting=true; direction=0; startYaw=yaw; SetCaptureDirection(); return;
            }
            MoveSafely(delta.normalized*Mathf.Min(delta.magnitude,settings.walkSpeed*U*dt));
            if((transform.position-lastProgressPosition).sqrMagnitude>0.05f*0.05f*U*U)
            { lastProgressPosition=transform.position; lastProgressAt=Time.realtimeSinceStartup; }
            if(Time.realtimeSinceStartup-lastProgressAt>settings.stuckSeconds || Time.realtimeSinceStartup-pointStarted>settings.waypointTimeout)
            {
                capture.LogEvent("waypoint_skipped","point="+RouteIndex+"; "+
                    (Time.realtimeSinceStartup-lastProgressAt>settings.stuckSeconds?"no progress / collider blocked":"waypoint timeout"));
                AdvancePoint();
            }
        }
        void SetCaptureDirection()
        {
            yaw=startYaw+direction*360f/Mathf.Max(1,settings.directionCount); pitch=0; ApplyView();
            reachedAt=Time.realtimeSinceStartup;
            Status="Point "+(RouteIndex+1)+", direction "+(direction+1)+" / "+settings.directionCount;
        }
        // Explicit floor selection only. F1 never calls this or changes the camera position.
        public bool TryMoveToFloor(CaptureArea[] areas, Vector3 expectedFeet, out string reason)
        {
            reason = "";
            if(!Ready || InputSuspended || IsPreparing || capture.Busy || areas == null || areas.Length == 0)
            { reason = "층 전환을 위한 바닥/경로/촬영 준비가 끝나지 않았습니다."; return false; }
            var previousAreas = navigation.areas;
            RaycastHit floor;
            bool valid;
            try
            {
                navigation.areas = areas;
                valid = navigation.TryGround(expectedFeet, out floor) && navigation.ClearBody(floor.point);
            }
            finally { navigation.areas = previousAreas; }
            if(!valid)
            { reason = "선택한 층의 시작 바닥 또는 빈 공간을 확인할 수 없어 현재 위치를 유지합니다."; return false; }
            UseManual("선택한 층의 시작점으로 전환했습니다.");
            navigation.ClearRoute();
            navigation.areas = areas;
            body.enabled = false;
            transform.position = floor.point;
            body.enabled = !PassThroughObstacles;
            distanceWalked = 0;
            return true;
        }

        public void SetCursorLocked(bool locked)
        { Cursor.lockState=locked?CursorLockMode.Locked:CursorLockMode.None; Cursor.visible=!locked; }
        void OnApplicationFocus(bool focus) { if(!focus) SetCursorLocked(false); }
        void OnDisable() { SetCursorLocked(false); }
    }
}
