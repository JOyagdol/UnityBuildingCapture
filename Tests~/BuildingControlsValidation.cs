#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace UnityResearchCapture.Editor
{
    public static class BuildingControlsValidation
    {
        [MenuItem("Tools/Research Capture/Validate Building and Floor Controls")]
        public static void Run()
        {
            string[] args=Environment.GetCommandLineArgs();int sceneArg=Array.IndexOf(args,"-researchValidationScene");
            if(sceneArg>=0&&sceneArg+1<args.Length)EditorSceneManager.OpenScene(args[sceneArg+1]);
            var explorer=UnityEngine.Object.FindObjectOfType<ResearchBuildingExplorer>();
            if(explorer==null||explorer.floors.Length!=2||explorer.buildings.Length<2)
                throw new InvalidOperationException("Open a saved test scene with two floors and at least two building destinations");
            if(!ValidationSceneSession.Begin())return;
            var view=EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));view.Show();view.Focus();
            new GameObject("Building controls validation").AddComponent<BuildingControlsValidationProbe>();
            EditorApplication.EnterPlaymode();
        }
    }
}
#endif