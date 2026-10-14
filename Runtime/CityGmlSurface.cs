using System;
using CesiumForUnity;
using UnityEngine;

namespace UnityResearchCapture
{
    public static class CityGmlSurface
    {
        public static string Property(RaycastHit hit, string key)
        {
            if (hit.collider == null || hit.triangleIndex < 0) return "";
            var primitive = hit.collider.GetComponent<CesiumPrimitiveFeatures>();
            var metadata = hit.collider.GetComponentInParent<CesiumModelMetadata>();
            if (primitive == null || metadata == null || primitive.featureIdSets == null) return "";
            for (int i = 0; i < primitive.featureIdSets.Length; ++i)
            {
                long table = primitive.featureIdSets[i].propertyTableIndex;
                if (table < 0 || metadata.propertyTables == null || table >= metadata.propertyTables.Length) continue;
                var properties = metadata.propertyTables[table].properties;
                if (!properties.TryGetValue(key, out var value)) continue;
                long id = primitive.GetFeatureIdFromRaycastHit(hit, i);
                if (id >= 0) return value.GetString(id, "");
            }
            return "";
        }

        public static bool IsFloor(RaycastHit hit, CaptureArea area)
        {
            if (!area.Contains(hit.point)) return false;
            if (!area.requireCityGmlFloor) return true; // Explicitly authored test/static floor only.
            if (Property(hit, "gml_class") != "bldg_FloorSurface") return false;
            string name = Property(hit, "gml_name");
            if (area.excludedFloorNameFragments != null)
                foreach (var fragment in area.excludedFloorNameFragments)
                    if (!string.IsNullOrEmpty(fragment) && name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0) return false;
            string level = Property(hit, "Constraints.Level");
            if (level.IndexOf("RF", StringComparison.OrdinalIgnoreCase) >= 0 ||
                level.IndexOf("roof", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            string external = Property(hit, "Pset_SlabCommon.IsExternal");
            if (external.Equals("Yes", StringComparison.OrdinalIgnoreCase) || external == "true") return false;
            return string.IsNullOrEmpty(area.requiredLevel) || area.requiredLevel == level;
        }
    }
}
