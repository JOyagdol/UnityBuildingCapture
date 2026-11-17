using System;
using UnityEditor;
using UnityEngine;
namespace UnityResearchCapture.Editor
{
    public static class RoomCatalogTools
    {
        [MenuItem("Tools/Research Capture/Validate Selected Room Catalog")]
        public static void ValidateSelected()
        {
            TextAsset asset=Selection.activeObject as TextAsset;
            if(asset==null && Selection.activeObject is ResearchCaptureSettings settings)asset=settings.roomCatalog;
            if(asset==null){Debug.LogWarning("Select a Room catalog JSON or capture settings asset.");return;}
            try
            {
                var data=JsonUtility.FromJson<RoomCatalogData>(asset.text);
                if(data==null)throw new ArgumentException("Invalid catalog JSON");
                data.Validate();Debug.Log("Room catalog validated: "+data.rooms.Length+" closed Room meshes. This checks geometry format, not human label verification.");
            }
            catch(Exception e){Debug.LogError("Room catalog invalid: "+e.Message);}
        }
    }
}
