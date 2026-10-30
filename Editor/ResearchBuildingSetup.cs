using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityResearchCapture.Editor
{
    public static class ResearchBuildingSetup
    {
        [MenuItem("Tools/Research Capture/Add Building and Floor Controls")]
        public static void AddControls()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) return;
            var scene=SceneManager.GetActiveScene();
            var walker=scene.GetRootGameObjects().SelectMany(go=>go.GetComponentsInChildren<ResearchWalker>(true)).FirstOrDefault();
            if(walker==null){Debug.LogError("Set up a Research Capture rig in the active scene first.");return;}
            var controls=walker.GetComponent<ResearchBuildingExplorer>();
            if(controls==null) controls=Undo.AddComponent<ResearchBuildingExplorer>(walker.gameObject);
            Undo.RecordObject(controls,"Configure building and floor controls");
            controls.walker=walker;
            if(controls.buildingId=="unknown")controls.buildingId=scene.name;
            if(controls.displayName=="Building")controls.displayName=scene.name;
            if(controls.floors==null||controls.floors.Length==0)
            {
                string level=walker.navigation.areas.Length>0?walker.navigation.areas[0].requiredLevel:"";
                if(string.IsNullOrWhiteSpace(level))level="unknown";
                controls.floors=new[]{new ResearchFloorDestination{floorId=level,displayName="Floor",areas=walker.navigation.areas,arrivalFeet=walker.transform.position}};
            }
            walker.capture.exploration=controls;
            var hud=walker.GetComponent<ResearchCaptureHud>();if(hud!=null)hud.exploration=controls;
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject=walker.gameObject;
            Debug.Log("Configure building scene paths and verified floor arrival points in the Inspector. Add destination scenes to Build Settings. Save all site data only in your local project.");
        }
    }
}