using UnityEngine;

namespace UnityResearchCapture
{
    // IMGUI is a screen overlay, never part of the camera-only RenderTexture.
    public sealed class ResearchCaptureHud : MonoBehaviour
    {
        public ResearchWalker walker;
        public ResearchCaptureWriter capture;
        public ResearchNavigation navigation;
        public ResearchBuildingExplorer exploration;
        static Rect Panel => new Rect(12,12,430,Mathf.Max(160,Mathf.Min(720,Screen.height-75)));
        GUIStyle wrapped;
        Font font;
        Vector2 scroll;
        void Awake()
        {
            font=Font.CreateDynamicFontFromOSFont(new[]{"Malgun Gothic","Arial"},14);
            if(exploration==null) exploration=GetComponent<ResearchBuildingExplorer>();
        }
        public static bool IsOverPanel(Vector3 mouse)
            => Panel.Contains(new Vector2(mouse.x,Screen.height-mouse.y));
        void OnGUI()
        {
            if(walker==null || capture==null) return;
            if(font!=null) GUI.skin.font=font;
            if(wrapped==null) wrapped=new GUIStyle(GUI.skin.label) { wordWrap=true, fontSize=13 };
            GUILayout.BeginArea(Panel,GUI.skin.box);
            scroll=GUILayout.BeginScrollView(scroll);
            GUILayout.Label("Research Capture 0.3.0");
            bool locked=Cursor.lockState==CursorLockMode.Locked;
            if(exploration!=null)
            {
                GUILayout.Label("건물: "+exploration.displayName+" | 층: "+exploration.CurrentFloorId,wrapped);
                GUI.enabled=!locked&&exploration.CanSwitchBuilding;
                GUILayout.BeginHorizontal();
                for(int i=0;i<exploration.buildings.Length;i++)
                {
                    var b=exploration.buildings[i]; if(b==null) continue;
                    bool can=GUI.enabled; GUI.enabled=can&&b.buildingId!=exploration.buildingId;
                    if(GUILayout.Button(b.displayName+" 전환",GUILayout.Height(30))) exploration.SwitchBuilding(i);
                    GUI.enabled=can;
                }
                GUILayout.EndHorizontal();
                GUI.enabled=!locked&&exploration.CanChange;
                if(exploration.floors.Length>1)
                {
                    GUILayout.BeginHorizontal();
                    for(int i=0;i<exploration.floors.Length;i++)
                    {
                        var f=exploration.floors[i]; if(f==null) continue;
                        if(GUILayout.Button(f.displayName+" 시작점",GUILayout.Height(26))) exploration.SelectFloor(i);
                    }
                    GUILayout.EndHorizontal();
                    GUI.enabled=!locked&&!exploration.Busy&&(exploration.TourRunning||exploration.CanChange);
                    string route=string.Join(" → ",System.Array.ConvertAll(exploration.floors,f=>f==null?"?":f.displayName));
                    if(GUILayout.Button(exploration.TourRunning?"전체 층 탐색 중지":route+" 자동 탐색",GUILayout.Height(30)))
                    {
                        if(exploration.TourRunning) exploration.StopTour(); else exploration.StartTour();
                    }
                    GUI.enabled=true;
                    GUILayout.Label("각 층 완료 후 다음 층의 확인된 시작점으로 이동합니다.",wrapped);
                }
                GUI.enabled=true;
                if(!string.IsNullOrEmpty(exploration.Status)) GUILayout.Label(exploration.Status,wrapped);
            }
            GUILayout.Label("이동: "+(walker.Mode==MovementMode.Manual?"수동":"자동")+"   자동 촬영: "+(walker.AutoCapture?"켜짐":"꺼짐"));
            GUILayout.Label("저장 완료 (이미지 + 메타데이터): "+capture.SavedCount);
            GUILayout.Label("이동 영역: "+string.Join(", ",System.Array.ConvertAll(navigation.areas,a=>a==null?"-":a.areaId)),wrapped);
            GUI.enabled=!locked&&!walker.InputSuspended;
            if(GUILayout.Button(walker.Mode==MovementMode.Manual?"현재 층 자동 이동 시작  [F1]":"수동 이동으로 전환  [F1]",GUILayout.Height(30))) walker.ToggleMovement();
            if(GUILayout.Button(walker.AutoCapture?"자동 촬영 끄기  [F2]":"자동 촬영 켜기  [F2]",GUILayout.Height(30))) walker.ToggleAutoCapture();
            if(GUILayout.Button("현재 시점 한 장 촬영  [P]",GUILayout.Height(30))) walker.TakePhoto();
            GUI.enabled=!locked&&!walker.InputSuspended&&!walker.IsPreparing&&!capture.Busy;
            if(GUILayout.Button(walker.PassThroughObstacles?"벽·문 통과 끄기  [F3]":"벽·문 통과 켜기  [F3]",GUILayout.Height(30))) walker.TogglePassThroughObstacles();
            GUI.enabled=true;
            GUILayout.Label("벽·문 통과: "+(walker.PassThroughObstacles?"켜짐 (바닥 유지 / 장애물 내부 촬영 제한)":"꺼짐"),wrapped);
            GUI.enabled=!locked&&!walker.InputSuspended&&!walker.IsPreparing;
            if(GUILayout.Button("보행 경로 준비 / 다시 생성",GUILayout.Height(25))) walker.RebuildRoute();
            GUI.enabled=true;
            GUILayout.Label("WASD 이동 | 마우스 시선 | Shift 빠르게\nEsc: 버튼 조작 | 패널 밖 클릭: 시선 조작",wrapped);
            GUILayout.Label(walker.Status,wrapped);
            GUILayout.Label(navigation.IsBuilding?navigation.Status:capture.Status,wrapped);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            if(Cursor.lockState==CursorLockMode.None && !string.IsNullOrEmpty(capture.SessionFolder))
                GUI.Label(new Rect(12,Screen.height-45,Screen.width-24,40),"Output: "+capture.SessionFolder,wrapped);
        }
    }
}