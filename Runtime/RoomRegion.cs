using UnityEngine;

namespace UnityResearchCapture
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class RoomRegion : MonoBehaviour
    {
        [Tooltip("An externally verified ground-truth room identifier. Never infer this from an image.")]
        public string roomId = "unknown";
        public bool Contains(Vector3 position)
        {
            var box = GetComponent<BoxCollider>();
            var p = transform.InverseTransformPoint(position) - box.center;
            var h = box.size * 0.5f;
            return Mathf.Abs(p.x) <= h.x && Mathf.Abs(p.y) <= h.y && Mathf.Abs(p.z) <= h.z;
        }
        void Reset() { GetComponent<BoxCollider>().isTrigger = true; }
        void OnValidate() { GetComponent<BoxCollider>().isTrigger = true; }
        public static string Resolve(Vector3 position)
        {
            string result = null;
            foreach (var r in FindObjectsOfType<RoomRegion>())
            {
                if (!r.Contains(position) || string.IsNullOrWhiteSpace(r.roomId) || r.roomId == "unknown") continue;
                if (result != null && result != r.roomId) return "unknown"; // Ambiguous overlaps cannot be labels.
                result = r.roomId;
            }
            return result ?? "unknown";
        }
    }
}
