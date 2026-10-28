using UnityEngine;

namespace UnityResearchCapture
{
    public enum MovementMode { Manual, Automatic }

    [CreateAssetMenu(menuName = "Research Capture/Settings")]
    public sealed class ResearchCaptureSettings : ScriptableObject
    {
        [Header("Scale / walking (metres)")]
        [Min(0.001f)] public float unityUnitsPerMetre = 1f;
        [Min(0.5f)] public float eyeHeight = 1.6f;
        [Min(0.1f)] public float bodyRadius = 0.22f;
        [Min(0.1f)] public float walkSpeed = 1.4f;
        [Min(0.1f)] public float fastSpeed = 3f;
        [Range(0.01f, 10f)] public float mouseSensitivity = 2f;
        [Range(5, 89)] public float pitchLimit = 80;
        [Range(0.01f, 0.5f)] public float stepHeight = 0.18f;
        [Range(1, 60)] public float maxSlope = 35;
        public MovementMode initialMovement = MovementMode.Manual;
        public bool initialAutoCapture = false;
        public bool lockCursorOnStart = false;
        [Tooltip("Pass through walls, doors and furniture while following verified floor support. Toggle at runtime with F3.")]
        public bool initialPassThroughObstacles = false;

        [Header("Route / automatic capture")]
        [Min(0.1f)] public float pointSpacing = 1;
        [Range(1, 72)] public int directionCount = 8;
        [Min(0.1f)] public float manualCaptureDistance = 1;
        [Min(0.05f)] public float settleSeconds = 0.3f;
        [Min(1)] public float stuckSeconds = 8;
        [Min(2)] public float waypointTimeout = 90;
        [Min(0.05f)] public float navigationCellSize = 0.2f;
        [Range(100, 100000)] public int maximumGridSamples = 50000;
        [Min(1)] public int maximumWaypoints = 3000;
        public bool loopRoute = false;

        [Header("Images / output")]
        [Range(64, 8192)] public int imageWidth = 1920;
        [Range(64, 8192)] public int imageHeight = 1080;
        [Tooltip("Empty: Application.persistentDataPath/ResearchCaptures. Relative paths are relative to persistentDataPath.")]
        public string outputFolder = "";
        [Range(20, 110)] public float verticalFieldOfView = 60;
        public LayerMask collisionMask = ~0;
        public LayerMask imageMask = ~(1 << 5);
        [Header("Research scene lighting")]
        [Tooltip("Uniform fill for otherwise unlit interior geometry. Recorded in metadata; source scene is unchanged.")]
        public bool useAmbientFill = true;
        public Color ambientFillColor = new Color(0.55f, 0.55f, 0.55f, 1);
        [Tooltip("Wait for Cesium tiles in the current view before a capture. Timeout produces a logged failure, not a partial image.")]
        public bool waitForTiles = true;
        [Min(2)] public float captureTimeout = 30;
    }
}
