using System;
using System.Collections.Generic;

namespace UnityResearchCapture
{
    // URP 13 draws editor gizmos even for procedural Game cameras. Suppress the
    // editor view flags for this one render, then restore the user's preferences.
    internal sealed class CaptureGizmoScope : IDisposable
    {
        readonly List<Action> restore = new List<Action>();
        public CaptureGizmoScope()
        {
#if UNITY_EDITOR
            foreach (UnityEditor.SceneView view in UnityEditor.SceneView.sceneViews)
            {
                bool value=view.drawGizmos; view.drawGizmos=false;
                restore.Add(()=> { if(view!=null) view.drawGizmos=value; });
            }
            var type=typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.PlayModeView");
            if(type!=null)
            {
                var get=type.GetMethod("IsShowingGizmos");
                var set=type.GetMethod("SetShowGizmos");
                if(get!=null && set!=null)
                    foreach(var view in UnityEngine.Resources.FindObjectsOfTypeAll(type))
                    {
                        bool value=(bool)get.Invoke(view,null);
                        set.Invoke(view,new object[]{false});
                        restore.Add(()=> { if(view!=null) set.Invoke(view,new object[]{value}); });
                    }
            }
#endif
        }
        public void Dispose() { for(int i=restore.Count-1;i>=0;i--) restore[i](); }
    }
}
