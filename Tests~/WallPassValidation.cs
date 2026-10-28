using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UnityResearchCapture.Editor
{
    public static class WallPassValidation
    {
        [MenuItem("Tools/Research Capture/Validate Wall Pass (Synthetic)")]
        public static void Run()
        {
            if(!ValidationSceneSession.Begin()) return;
            if(!Application.isBatchMode)
            {
                var type=typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
                var game=EditorWindow.GetWindow(type); game.Show(); game.Focus();
            }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var root=new GameObject("Synthetic capture rig");
            root.SetActive(false);
            root.transform.position=new Vector3(0,0,-1.2f);
            var settings=ScriptableObject.CreateInstance<ResearchCaptureSettings>();
            settings.outputFolder=Path.GetFullPath("ResearchValidation/WallPassCaptures");
            settings.waitForTiles=false;
            settings.imageWidth=64; settings.imageHeight=64;
            settings.navigationCellSize=0.15f;
            settings.directionCount=2; // Short synthetic capture sequence; runtime default stays eight.
            settings.captureTimeout=2;
            root.AddComponent<CharacterController>();
            var walker=root.AddComponent<ResearchWalker>();
            var nav=root.AddComponent<ResearchNavigation>();
            var writer=root.AddComponent<ResearchCaptureWriter>();
            var cameraObject=new GameObject("Synthetic camera");
            cameraObject.transform.SetParent(root.transform,false);
            var camera=cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            walker.settings=settings; walker.viewCamera=camera; walker.navigation=nav; walker.capture=writer;
            nav.settings=settings;
            writer.settings=settings; writer.walker=walker; writer.sourceCamera=camera;
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name="Synthetic continuous floor";
            floor.transform.position=new Vector3(0,-0.25f,0);
            floor.transform.localScale=new Vector3(8,0.5f,8);
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name="Synthetic closed barrier";
            wall.transform.position=new Vector3(0,1.5f,0);
            wall.transform.localScale=new Vector3(8,3,0.3f);
            var areaObject=new GameObject("Synthetic collection area");
            var box=areaObject.AddComponent<BoxCollider>();
            box.isTrigger=true; box.size=new Vector3(7,4,7);
            var area=areaObject.AddComponent<CaptureArea>();
            area.approvedForCollection=true; area.requireCityGmlFloor=false;
            nav.areas=new[]{area};
            var far=new GameObject("Synthetic target").transform;
            far.position=new Vector3(0,0,1.2f);
            var inside=new GameObject("Synthetic invalid endpoint").transform;
            inside.position=Vector3.zero;
            nav.useInspectorWaypoints=true; nav.waypoints=new[]{far,inside};
            root.AddComponent<WallPassValidationProbe>().floor=floor;
            root.SetActive(true);
            EditorApplication.EnterPlaymode();
        }
    }
}
