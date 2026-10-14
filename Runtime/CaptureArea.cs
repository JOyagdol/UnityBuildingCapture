using UnityEngine;

namespace UnityResearchCapture
{
    // This volume is an explicit allow-list. It never creates a floor or collision proxy.
    [RequireComponent(typeof(BoxCollider))]
    public sealed class CaptureArea : MonoBehaviour
    {
        public string areaId = "CaptureArea";
        public bool approvedForCollection;
        [Tooltip("Cesium Constraints.Level. Empty accepts any non-roof floor within the narrow height band.")]
        public string requiredLevel = "";
        public bool requireCityGmlFloor = true;
        [Tooltip("Exclude foundation slabs and trenches from the walkable floor semantic mask.")]
        public string[] excludedFloorNameFragments = { "Foundation", "Trench" };
        [Tooltip("World Unity Y of the intended floor, not camera height.")]
        public float floorHeight = 0;
        [Min(0.02f)] public float floorTolerance = 0.25f;
        public Bounds WorldBounds => GetComponent<BoxCollider>().bounds;

        public bool Contains(Vector3 feet)
        {
            var box = GetComponent<BoxCollider>();
            var p = transform.InverseTransformPoint(feet) - box.center;
            var half = box.size * 0.5f;
            return Mathf.Abs(p.x) <= half.x && Mathf.Abs(p.z) <= half.z &&
                   Mathf.Abs(feet.y - floorHeight) <= floorTolerance;
        }

        void Reset() { GetComponent<BoxCollider>().isTrigger = true; }
        void OnValidate() { GetComponent<BoxCollider>().isTrigger = true; }
        void OnDrawGizmosSelected()
        {
            var b = GetComponent<BoxCollider>();
            Gizmos.color = approvedForCollection ? Color.cyan : Color.yellow;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(b.center, b.size);
        }
    }
}
