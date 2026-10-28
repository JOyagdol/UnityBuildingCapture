using UnityEngine;

namespace UnityResearchCapture
{
    // IMGUI is a screen overlay, never part of the camera-only RenderTexture.
    public sealed class ResearchCaptureHud : MonoBehaviour
    {
        public ResearchWalker walker;
        public ResearchCaptureWriter capture;
        public ResearchNavigation navigation;
        static Rect Panel => new Rect(12,12,430,430);
        GUIStyle wrapped;
        Font font;
        void Awake() { font=Font.CreateDynamicFontFromOSFont(new[]{"Malgun Gothic","Arial"},14); }
        public static bool IsOverPanel(Vector3 mouse)
            => Panel.Contains(new Vector2(mouse.x,Screen.height-mouse.y));

        void OnGUI()
        {
            if(walker==null || capture==null) return;
            if(font!=null) GUI.skin.font=font;
            if(wrapped==null) wrapped=new GUIStyle(GUI.skin.label) { wordWrap=true, fontSize=13 };
            GUILayout.BeginArea(Panel,GUI.skin.box);
            GUILayout.Label("RESEARCH CAPTURE");
            GUILayout.Label("이동: "+(walker.Mode==MovementMode.Manual?"수동":"자동")+"   자동 촬영: "+(walker.AutoCapture?"켜짐":"꺼짐"));
            GUILayout.Label("저장 완료 (이미지 + 메타데이터): "+capture.SavedCount);
            bool locked=Cursor.lockState==CursorLockMode.Locked;
            GUI.enabled=!locked;
            if(GUILayout.Button(walker.Mode==MovementMode.Manual?"자동 이동 시작  [F1]":"수동 이동으로 전환  [F1]",GUILayout.Height(30))) walker.ToggleMovement();
            if(GUILayout.Button(walker.AutoCapture?"자동 촬영 끄기  [F2]":"자동 촬영 켜기  [F2]",GUILayout.Height(30))) walker.ToggleAutoCapture();
            if(GUILayout.Button("현재 시점 한 장 촬영  [P]",GUILayout.Height(30))) walker.TakePhoto();
            GUI.enabled=!locked&&!navigation.IsBuilding&&!capture.Busy;
            if(GUILayout.Button(walker.PassThroughObstacles?"벽·문 통과 끄기  [F3]":"벽·문 통과 켜기  [F3]",GUILayout.Height(30))) walker.TogglePassThroughObstacles();
            GUILayout.Label("벽·문 통과: "+(walker.PassThroughObstacles?"켜짐 (바닥 유지 / 장애물 내부 촬영 제한)":"꺼짐"),wrapped);
            GUI.enabled=!locked&&!navigation.IsBuilding;
            if(GUILayout.Button("보행 경로 준비 / 다시 생성",GUILayout.Height(25))) walker.RebuildRoute();
            GUI.enabled=true;
            GUILayout.Label("WASD 이동 | 마우스 시선 | Shift 빠르게\nEsc: 버튼 조작 | 패널 밖 클릭: 시선 조작",wrapped);
            GUILayout.Label(walker.Status,wrapped);
            GUILayout.Label(navigation.IsBuilding?navigation.Status:capture.Status,wrapped);
            GUILayout.EndArea();
            if(Cursor.lockState==CursorLockMode.None && !string.IsNullOrEmpty(capture.SessionFolder))
                GUI.Label(new Rect(12,Screen.height-45,Screen.width-24,40),"Output: "+capture.SessionFolder,wrapped);
        }
    }
}
