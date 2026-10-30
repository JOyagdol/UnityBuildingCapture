using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityResearchCapture
{
    [Serializable] public sealed class ResearchBuildingDestination
    {
        public string buildingId;
        public string displayName;
        [Tooltip("Scene asset path included in Build Settings, for example Assets/Scenes/Building.unity")]
        public string scenePath;
    }
    [Serializable] public sealed class ResearchFloorDestination
    {
        public string floorId;
        public string displayName;
        public CaptureArea[] areas = Array.Empty<CaptureArea>();
        [Tooltip("Verified feet position on this floor, in this scene's Unity coordinates.")]
        public Vector3 arrivalFeet;
    }

    // Building scenes own their geometry, georeference, floor profiles and capture sessions.
    [DefaultExecutionOrder(-100)]
    public sealed class ResearchBuildingExplorer : MonoBehaviour
    {
        public ResearchWalker walker;
        public string buildingId = "unknown";
        public string displayName = "Building";
        public ResearchBuildingDestination[] buildings = Array.Empty<ResearchBuildingDestination>();
        public ResearchFloorDestination[] floors = Array.Empty<ResearchFloorDestination>();
        [Min(0)] public int initialFloorIndex;
        public int CurrentFloorIndex { get; private set; }
        public string CurrentFloorId => floors != null && CurrentFloorIndex >= 0 && CurrentFloorIndex < floors.Length && floors[CurrentFloorIndex] != null
            ? floors[CurrentFloorIndex].floorId : "unknown";
        public bool Busy { get; private set; }
        public bool TourRunning { get; private set; }
        public string Status { get; private set; } = "";
        public bool CanSwitchBuilding => walker != null && !Busy && !walker.IsPreparing && !walker.capture.Busy;
        public bool CanChange => CanSwitchBuilding && walker.Ready;
        Coroutine tour;
        bool routeCompleted;

        void Awake()
        {
            if(walker == null) walker = GetComponent<ResearchWalker>();
            CurrentFloorIndex = Mathf.Clamp(initialFloorIndex, 0, Mathf.Max(0, floors.Length - 1));
            if(walker != null)
            {
                walker.AutomaticRouteCompleted += OnRouteComplete;
                if(floors.Length > 0 && floors[CurrentFloorIndex] != null && floors[CurrentFloorIndex].areas != null && floors[CurrentFloorIndex].areas.Length > 0)
                {
                    walker.navigation.areas = floors[CurrentFloorIndex].areas;
                    walker.transform.position = floors[CurrentFloorIndex].arrivalFeet;
                }
                else CurrentFloorIndex = -1;
            }
        }
        void OnRouteComplete() { routeCompleted = true; }
        void OnDestroy() { if(walker != null) walker.AutomaticRouteCompleted -= OnRouteComplete; }

        public bool SelectFloor(int index)
        {
            if(!CanChange || index < 0 || index >= floors.Length) return Reject("층 전환을 준비할 수 없습니다. 로딩/촬영 완료를 기다려 주세요.");
            StopTour("층을 직접 선택했습니다.");
            return MoveToFloor(index);
        }
        bool MoveToFloor(int index)
        {
            var floor = floors[index];
            string reason = "";
            if(floor == null || !walker.TryMoveToFloor(floor.areas, floor.arrivalFeet, out reason))
                return Reject(floor == null ? "층 설정이 없습니다." : reason);
            string previous = CurrentFloorId;
            CurrentFloorIndex = index;
            Status = floor.displayName + " 시작점으로 전환했습니다.";
            walker.capture.LogEvent("floor_transition", "building=" + buildingId + "; from=" + previous + "; to=" + CurrentFloorId + "; method=verified_start_position");
            return true;
        }
        public bool StartTour()
        {
            if(!CanChange || floors.Length == 0 || tour != null) return Reject("자동 탐색을 시작할 수 없습니다. 바닥/경로/촬영 상태를 확인하세요.");
            tour = StartCoroutine(RunTour());
            return true;
        }
        public void StopTour(string reason = "전체 층 자동 탐색을 중지했습니다.")
        {
            if(!TourRunning) return;
            TourRunning = false;
            walker.SuppressRouteLoop = false;
            walker.UseManual(reason);
            Status = reason;
        }
        IEnumerator RunTour()
        {
            TourRunning = true;
            walker.SuppressRouteLoop = true;
            // Always yield once so the Coroutine handle exists before any early failure.
            yield return null;
            try
            {
                for(int i = 0; i < floors.Length && TourRunning; i++)
                {
                    if(!MoveToFloor(i)) yield break;
                    routeCompleted = false;
                    Status = floors[i].displayName + " 자동 탐색 중";
                    walker.ToggleMovement();
                    while(TourRunning && walker.IsPreparing) yield return null;
                    while(TourRunning && walker.Mode == MovementMode.Automatic && !routeCompleted) yield return null;
                    if(!TourRunning) yield break;
                    if(!routeCompleted)
                    {
                        Status = "자동 탐색이 중지되었습니다: " + walker.Status;
                        yield break;
                    }
                    // A manual capture requested on the final frame must finish on its original floor.
                    while(TourRunning && walker.capture.Busy) yield return null;
                }
                if(TourRunning)
                {
                    Status = "선택한 건물의 전체 층 탐색을 완료했습니다.";
                    walker.capture.LogEvent("building_tour_complete", buildingId);
                }
            }
            finally { TourRunning = false; walker.SuppressRouteLoop = false; tour = null; }
        }
        public bool SwitchBuilding(int index)
        {
            if(!CanSwitchBuilding || index < 0 || index >= buildings.Length) return Reject("건물 전환을 준비할 수 없습니다. 로딩/촬영 완료를 기다려 주세요.");
            var target = buildings[index];
            if(target == null || string.IsNullOrWhiteSpace(target.scenePath) || !Application.CanStreamedLevelBeLoaded(target.scenePath))
                return Reject("건물 Scene이 Build Settings에 없습니다. 현재 건물을 유지합니다.");
            if(target.scenePath == gameObject.scene.path) return true;
            StopTour("건물을 전환합니다.");
            walker.UseManual("건물을 불러오는 중입니다.");
            Busy = true; walker.InputSuspended = true; walker.SetCursorLocked(false);
            Status = target.displayName + " 불러오는 중";
            walker.capture.LogEvent("building_switch", "from=" + buildingId + "; to=" + target.buildingId);
            try
            {
                var operation = SceneManager.LoadSceneAsync(target.scenePath, LoadSceneMode.Single);
                if(operation == null) throw new InvalidOperationException("Scene loading did not start");
                return true;
            }
            catch(Exception error)
            {
                Busy = false; walker.InputSuspended = false;
                return Reject("건물 전환 실패: " + error.Message);
            }
        }
        bool Reject(string reason)
        {
            Status = reason;
            if(walker != null) walker.capture.LogEvent("building_or_floor_rejected", reason);
            return false;
        }
    }
}