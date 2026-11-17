using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityResearchCapture
{
    [Serializable] public sealed class CanonicalRoomCatalog
    {
        public int schemaVersion;
        public string referenceSnapshotSha256, candidatePoolSha256;
        public string[] canonicalRoomIds;
    }

    [RequireComponent(typeof(BoxCollider))]
    public sealed class RoomRegion : MonoBehaviour
    {
        [Header("Evaluation-only canonical identity")]
        public TextAsset canonicalRoomCatalog;
        public string regionId = "";
        public string roomId = "unknown";
        public string roomName = "", roomDescription = "";
        public string buildingId = "", floorId = "";
        [Header("Explicit mapping and spatial review")]
        [Tooltip("Person who checked the canonical ID and the complete spatial box against the reference scene.")]
        public string reviewedBy = "";
        [Tooltip("A local review record identifying the source model, scene alignment, boundary checks and exclusions. Not an image annotation.")]
        public string reviewEvidence = "";
        [HideInInspector] public string reviewedAtUtc = "";
        [HideInInspector] public string reviewedGeometrySha256 = "";
        [HideInInspector] public string reviewedCatalogTextSha256 = "";
        [HideInInspector] public string reviewStatus = "pending";

        void Reset() { GetComponent<BoxCollider>().isTrigger = true; }
        void OnValidate() { GetComponent<BoxCollider>().isTrigger = true; }
        static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        static bool ValidId(string value)
        { return !string.IsNullOrWhiteSpace(value) && value == value.Trim() && !string.Equals(value, "unknown", StringComparison.OrdinalIgnoreCase); }
        static bool Sha(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char c in value) if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            return true;
        }
        public static string TextSha256(string text)
        {
            using (var hash = SHA256.Create())
            {
                byte[] digest = hash.ComputeHash(Encoding.UTF8.GetBytes(text));
                var result = new StringBuilder(64);
                foreach (byte value in digest) result.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                return result.ToString();
            }
        }

        public bool TryReadCatalog(out CanonicalRoomCatalog catalog, out string error)
        {
            catalog = null; error = "";
            if (canonicalRoomCatalog == null) { error = "Canonical Room catalog is not assigned"; return false; }
            try { catalog = JsonUtility.FromJson<CanonicalRoomCatalog>(canonicalRoomCatalog.text); }
            catch (Exception e) { error = "Invalid canonical Room catalog: " + e.Message; return false; }
            if (catalog == null || catalog.schemaVersion != 1 || !Sha(catalog.referenceSnapshotSha256)
                || !Sha(catalog.candidatePoolSha256) || catalog.canonicalRoomIds == null || catalog.canonicalRoomIds.Length == 0)
            { error = "Catalog requires schemaVersion=1, snapshot/pool SHA256 and canonical Room IDs"; return false; }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in catalog.canonicalRoomIds)
                if (!ValidId(id) || !ids.Add(id)) { error = "Catalog Room IDs must be explicit, nonempty and unique"; return false; }
            if (!ValidId(roomId) || !ids.Contains(roomId)) { error = "Room ID does not exactly match the frozen canonical catalog"; return false; }
            return true;
        }

        public string GeometrySha256()
        {
            var box = GetComponent<BoxCollider>();
            if (box == null) return "";
            // Include identity, scene and the complete world transform, not merely a room name.
            var data = new StringBuilder();
            data.Append(gameObject.scene.path).Append('\n').Append(regionId).Append('\n').Append(roomId).Append('\n');
            data.Append(reviewedBy).Append('\n').Append(reviewEvidence).Append('\n').Append(reviewedAtUtc).Append('\n');
            if (!string.IsNullOrEmpty(roomName+roomDescription+buildingId+floorId))
                data.Append(roomName).Append("\n").Append(roomDescription).Append("\n").Append(buildingId).Append("\n").Append(floorId).Append("\n");
            var matrix = transform.localToWorldMatrix;
            for (int i = 0; i < 16; i++) data.Append(matrix[i].ToString("R", CultureInfo.InvariantCulture)).Append(';');
            foreach (float value in new[] { box.center.x, box.center.y, box.center.z, box.size.x, box.size.y, box.size.z })
                data.Append(value.ToString("R", CultureInfo.InvariantCulture)).Append(';');
            data.Append(box.isTrigger).Append(';').Append(box.enabled);
            return TextSha256(data.ToString());
        }

        public bool ValidateDraft(out string error)
        {
            error = "";
            if (!ValidId(regionId)) { error = "Region requires its own stable regionId"; return false; }
            CanonicalRoomCatalog catalog;
            if (!TryReadCatalog(out catalog, out error)) return false;
            var box = GetComponent<BoxCollider>();
            if (box == null || !box.enabled || !box.isTrigger) { error = "Region requires an enabled trigger BoxCollider"; return false; }
            foreach (float value in new[] { box.center.x, box.center.y, box.center.z, box.size.x, box.size.y, box.size.z })
                if (!Finite(value)) { error = "Region geometry must be finite"; return false; }
            if (box.size.x <= 0 || box.size.y <= 0 || box.size.z <= 0) { error = "Region extents must be positive"; return false; }
            var matrix = transform.localToWorldMatrix;
            for (int i = 0; i < 16; i++) if (!Finite(matrix[i])) { error = "Region transform must be finite"; return false; }
            if (!Finite(matrix.determinant) || Mathf.Abs(matrix.determinant) < 0.000001f)
            { error = "Region transform must be nonsingular"; return false; }
            Vector3 x = matrix.GetColumn(0), y = matrix.GetColumn(1), z = matrix.GetColumn(2);
            if (Mathf.Abs(Vector3.Dot(x.normalized, y.normalized)) > 0.0001f
                || Mathf.Abs(Vector3.Dot(x.normalized, z.normalized)) > 0.0001f
                || Mathf.Abs(Vector3.Dot(y.normalized, z.normalized)) > 0.0001f)
            { error = "Sheared region transforms are unsupported; use an orthogonal box hierarchy"; return false; }
            if (string.IsNullOrWhiteSpace(gameObject.scene.path)) { error = "Save the scene before reviewing spatial configuration"; return false; }
            return true;
        }

        public bool ValidateReviewed(out string error)
        {
            if (!ValidateDraft(out error)) return false;
            DateTime reviewTime;
            if (reviewStatus != "reviewed" || string.IsNullOrWhiteSpace(reviewedBy) || string.IsNullOrWhiteSpace(reviewEvidence)
                || !DateTime.TryParse(reviewedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out reviewTime))
            { error = "Canonical mapping and spatial configuration have not been explicitly reviewed"; return false; }
            if (reviewedCatalogTextSha256 != TextSha256(canonicalRoomCatalog.text))
            { error = "Canonical catalog changed after review"; return false; }
            if (reviewedGeometrySha256 != GeometrySha256())
            { error = "Region identity, bounds, scene or world transform changed after review"; return false; }
            return true;
        }

        public bool Contains(Vector3 position)
        {
            if (!Finite(position.x) || !Finite(position.y) || !Finite(position.z)) return false;
            var box = GetComponent<BoxCollider>();
            if (!isActiveAndEnabled || box == null || !box.enabled) return false;
            var p = transform.InverseTransformPoint(position) - box.center;
            var half = box.size * 0.5f;
            return Mathf.Abs(p.x) <= half.x && Mathf.Abs(p.y) <= half.y && Mathf.Abs(p.z) <= half.z;
        }

        public static RoomRegion[] ActiveInScene(Scene scene)
        {
            var result = new List<RoomRegion>();
            foreach (var region in FindObjectsOfType<RoomRegion>())
                if (region.isActiveAndEnabled && region.gameObject.scene == scene) result.Add(region);
            return result.ToArray();
        }

        public static bool ValidateScene(Scene scene, out string error)
        {
            error = "";
            var regions = ActiveInScene(scene);
            if (regions.Length == 0) { error = "No active RoomRegion in capture scene"; return false; }
            var regionIds = new HashSet<string>(StringComparer.Ordinal);
            string catalogHash = null;
            foreach (var region in regions)
            {
                string problem;
                if (!region.ValidateReviewed(out problem)) { error = region.name + ": " + problem; return false; }
                if (!regionIds.Add(region.regionId)) { error = "Duplicate regionId in capture scene"; return false; }
                string currentHash = TextSha256(region.canonicalRoomCatalog.text);
                if (catalogHash != null && currentHash != catalogHash) { error = "Active regions use different canonical catalogs"; return false; }
                catalogHash = currentHash;
            }
            return true;
        }

        public static bool TryResolveVerified(Vector3 position, Scene scene, out RoomRegion selected, out string error)
        {
            selected = null;
            if (!ValidateScene(scene, out error)) return false;
            foreach (var region in ActiveInScene(scene))
            {
                if (!region.Contains(position)) continue;
                // Even equal-room overlaps are refused: each capture records one reviewed region.
                if (selected != null) { selected = null; error = "Capture position lies in overlapping RoomRegions"; return false; }
                selected = region;
            }
            if (selected == null) { error = "Capture position has no reviewed canonical RoomRegion"; return false; }
            error = ""; return true;
        }

        public static RoomLabel LabelFromVerified(RoomRegion region)
        {
            CanonicalRoomCatalog canonical; string error;
            if (!region.ValidateReviewed(out error) || !region.TryReadCatalog(out canonical,out error))
                return new RoomLabel {status="unreviewed_region",reason=error,source="authored RoomRegion"};
            return new RoomLabel {roomId=region.roomId,roomName=string.IsNullOrWhiteSpace(region.roomName)?region.roomId:region.roomName,
                roomDescription=region.roomDescription,status="resolved",reviewStatus="reviewed",source="explicitly reviewed canonical RoomRegion",
                reason="Reviewed region contains the camera optical centre",candidateIds=new[]{region.roomId},
                regionId=region.regionId,regionGeometrySha256=region.reviewedGeometrySha256,regionCatalogTextSha256=region.reviewedCatalogTextSha256,
                reviewedBy=region.reviewedBy,reviewedAtUtc=region.reviewedAtUtc,reviewEvidence=region.reviewEvidence,
                referenceSnapshotSha256=canonical.referenceSnapshotSha256,candidatePoolSha256=canonical.candidatePoolSha256};
        }
        public static RoomLabel ResolveLabel(Vector3 position,Scene scene,string building,string floor)
        {
            var matches=new List<RoomRegion>();
            foreach(var region in ActiveInScene(scene))
            {
                if(!region.Contains(position))continue;
                if(!string.IsNullOrEmpty(region.buildingId)&&region.buildingId!=building || !string.IsNullOrEmpty(region.floorId)&&region.floorId!=floor)continue;
                matches.Add(region);
            }
            if(matches.Count==0)return null;
            if(matches.Count>1)return new RoomLabel {status="ambiguous",source="authored RoomRegion",reason="Overlapping authored RoomRegions",
                candidateIds=matches.ConvertAll(r=>r.roomId).ToArray()};
            return LabelFromVerified(matches[0]);
        }

        // Compatibility API for callers that display status. The capture writer uses TryResolveVerified.
        public static string Resolve(Vector3 position)
        {
            RoomRegion region; string error;
            return TryResolveVerified(position, SceneManager.GetActiveScene(), out region, out error) ? region.roomId : "unknown";
        }
    }
}
