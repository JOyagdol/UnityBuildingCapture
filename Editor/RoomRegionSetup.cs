using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityResearchCapture.Editor
{
    public static class RoomRegionSetup
    {
        [MenuItem("Tools/Research Capture/Room GT/Create Unreviewed Region")]
        public static void CreateUnreviewed()
        {
            var go = new GameObject("RoomRegion_UNREVIEWED");
            Undo.RegisterCreatedObjectUndo(go, "Create unreviewed Room GT region");
            var region = go.AddComponent<RoomRegion>();
            region.regionId = Guid.NewGuid().ToString("N");
            region.reviewStatus = "pending";
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            Debug.Log("Assign the frozen canonical catalog and exact Room ID, fit/check bounds, save scene, then record an explicit review. Capture remains blocked.");
        }

        [MenuItem("Tools/Research Capture/Room GT/Record Review of Selected Region")]
        public static void RecordSelectedReview()
        {
            var region = Selection.activeGameObject == null ? null : Selection.activeGameObject.GetComponent<RoomRegion>();
            if (region == null) throw new InvalidOperationException("Select one RoomRegion");
            string error;
            if (!region.ValidateDraft(out error)) throw new InvalidOperationException(error);
            if (string.IsNullOrWhiteSpace(region.reviewedBy) || string.IsNullOrWhiteSpace(region.reviewEvidence))
                throw new InvalidOperationException("Enter reviewer identity and review evidence before recording approval");
            if (Application.isBatchMode) throw new InvalidOperationException("A batch setup cannot certify human spatial/identity review");
            if (!EditorUtility.DisplayDialog("Record explicit Room GT review",
                "Confirm that you checked this exact canonical Room ID, source-model alignment, the complete spatial box including vertical/floor limits, and excluded neighboring rooms. A catalog match alone does not verify the geometry.",
                "Record my completed review", "Cancel")) return;
            Undo.RecordObject(region, "Record reviewed Room GT configuration");
            region.reviewedAtUtc = DateTime.UtcNow.ToString("O");
            region.reviewedCatalogTextSha256 = RoomRegion.TextSha256(region.canonicalRoomCatalog.text);
            region.reviewedGeometrySha256 = region.GeometrySha256();
            region.reviewStatus = "reviewed";
            EditorUtility.SetDirty(region);
            EditorSceneManager.MarkSceneDirty(region.gameObject.scene);
            Debug.Log("Room GT review recorded. Save scene; any subsequent identity/bounds/transform/catalog change invalidates this review.");
        }

        public static void ValidateCaptureScene(Scene scene, bool checkStartPosition)
        {
            foreach (var writer in UnityEngine.Object.FindObjectsOfType<ResearchCaptureWriter>())
            {
                if (!writer.isActiveAndEnabled || writer.gameObject.scene != scene || writer.settings == null || !writer.settings.requireReviewedRoom) continue;
                string error;
                if (!RoomRegion.ValidateScene(scene, out error)) throw new BuildFailedException("Room GT preflight failed: " + error);
                if (writer.sourceCamera == null || writer.sourceCamera.gameObject.scene != scene)
                    throw new BuildFailedException("Room GT preflight requires the capture camera in the same scene");
                if (checkStartPosition)
                {
                    RoomRegion selected;
                    if (!RoomRegion.TryResolveVerified(writer.sourceCamera.transform.position, scene, out selected, out error))
                        throw new BuildFailedException("Room GT start-position preflight failed: " + error);
                }
            }
        }

        [MenuItem("Tools/Research Capture/Room GT/Validate Current Capture Scene")]
        public static void ValidateCurrentScene()
        {
            var scene = SceneManager.GetActiveScene();
            ValidateCaptureScene(scene, true);
            string error;
            if (!RoomRegion.ValidateScene(scene, out error)) throw new BuildFailedException(error);
            Debug.Log("Room GT preflight passed for current configured regions and capture position. This does not certify complete room coverage or every possible overlap position.");
        }
    }

    // Invoked by normal Unity builds as well as the existing BuildWindows menu.
    public sealed class RoomRegionBuildGuard : IProcessSceneWithReport
    {
        public int callbackOrder { get { return 0; } }
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            // A null report is also used while entering Play Mode; runtime capture still revalidates.
            if (report != null) RoomRegionSetup.ValidateCaptureScene(scene, true);
        }
    }
}
