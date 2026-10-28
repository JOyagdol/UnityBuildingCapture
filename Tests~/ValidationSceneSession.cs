#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityResearchCapture
{
    // Survives the Play Mode domain reload and restores the saved Editor scene set.
    [InitializeOnLoad]
    public static class ValidationSceneSession
    {
        const string Key = "ResearchCapture.ValidationSceneSession.";
        [Serializable] sealed class SavedScene
        {
            public string path;
            public bool isLoaded;
            public bool isActive;
        }
        [Serializable] sealed class SavedSetup { public SavedScene[] scenes; }
        static ValidationSceneSession()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.wantsToQuit += OnWantsToQuit;
            if (SessionState.GetBool(Key + "finishing", false))
                EditorApplication.delayCall += StopAndRestore;
        }
        public static bool Begin()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetBool(Key + "active", false))
            {
                Debug.LogError("Finish the current Play Mode or validation session before starting another test.");
                return false;
            }
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
            var setup = EditorSceneManager.GetSceneManagerSetup();
            // A refused save must never silently discard the user's scene.
            if (setup.Length == 0 || setup.Any(entry => string.IsNullOrEmpty(entry.path)) ||
                Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty))
            {
                Debug.LogError("Save all open scenes before validation. No scenes were replaced.");
                return false;
            }
            var saved = new SavedSetup
            {
                scenes = setup.Select(entry => new SavedScene
                { path = entry.path, isLoaded = entry.isLoaded, isActive = entry.isActive }).ToArray()
            };
            SessionState.SetString(Key + "setup", JsonUtility.ToJson(saved));
            SessionState.SetBool(Key + "exit", Application.isBatchMode ||
                Environment.GetCommandLineArgs().Contains("-researchValidationExit"));
            SessionState.SetBool(Key + "finishing", false);
            SessionState.SetBool(Key + "active", true);
            return true;
        }
        public static void Complete(int exitCode)
        {
            if (!SessionState.GetBool(Key + "active", false))
            {
                Debug.LogError("No saved validation scene session exists; the Editor was kept open.");
                return;
            }
            if (SessionState.GetBool(Key + "finishing", false)) return;
            SessionState.SetInt(Key + "code", exitCode);
            SessionState.SetBool(Key + "finishing", true);
            EditorApplication.delayCall += StopAndRestore;
        }
        static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(Key + "active", false)) return;
            if (!SessionState.GetBool(Key + "finishing", false))
            {
                SessionState.SetInt(Key + "code", 130); // Test was stopped by the user.
                SessionState.SetBool(Key + "finishing", true);
            }
            EditorApplication.delayCall += StopAndRestore;
        }
        static bool OnWantsToQuit()
        {
            if (!SessionState.GetBool(Key + "active", false)) return true;
            SessionState.SetBool(Key + "exit", true);
            Complete(130);
            return false;
        }
        static void StopAndRestore()
        {
            if (!SessionState.GetBool(Key + "active", false) || !SessionState.GetBool(Key + "finishing", false)) return;
            if (EditorApplication.isPlaying)
            {
                EditorApplication.ExitPlaymode();
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            try
            {
                var saved = JsonUtility.FromJson<SavedSetup>(SessionState.GetString(Key + "setup", ""));
                if (saved == null || saved.scenes == null || saved.scenes.Length == 0)
                    throw new InvalidOperationException("The original scene setup could not be read.");
                var setup = saved.scenes.Select(entry => new SceneSetup
                { path = entry.path, isLoaded = entry.isLoaded, isActive = entry.isActive }).ToArray();
                EditorSceneManager.RestoreSceneManagerSetup(setup);
                bool exit = SessionState.GetBool(Key + "exit", false);
                int code = SessionState.GetInt(Key + "code", 130);
                SessionState.SetBool(Key + "active", false);
                SessionState.SetBool(Key + "finishing", false);
                SessionState.EraseString(Key + "setup");
                Debug.Log("VALIDATION_SCENES_RESTORED: " + string.Join(", ", setup.Select(entry => entry.path)));
                if (exit) EditorApplication.Exit(code);
            }
            catch (Exception error)
            {
                // Keep the Editor and stored setup available if restoration cannot complete.
                Debug.LogError("Validation scene restoration failed. The Editor was kept open: " + error);
            }
        }
    }
}
#endif