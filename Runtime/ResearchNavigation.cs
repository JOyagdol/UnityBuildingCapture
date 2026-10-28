using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace UnityResearchCapture
{
    public sealed class ResearchNavigation : MonoBehaviour
    {
        public ResearchCaptureSettings settings;
        public CaptureArea[] areas = Array.Empty<CaptureArea>();
        public Transform[] waypoints = Array.Empty<Transform>();
        public bool useInspectorWaypoints;
        public bool PassThroughObstacles { get; private set; }
        public bool IsReady { get; private set; }
        public bool IsBuilding { get; private set; }
        public string Status { get; private set; } = "Prepare walking area first";
        public int AgentTypeId { get; private set; }
        public readonly List<Vector3> Points = new List<Vector3>();
        public Action<string, string> Log;
        NavMeshData data;
        NavMeshDataInstance instance;
        Mesh mesh;
        bool createdAgentSettings;
        float U => settings.unityUnitsPerMetre;
        public float Radius => settings.bodyRadius * U;
        public float Height => (settings.eyeHeight + 0.16f) * U;
        public NavMeshQueryFilter Filter => new NavMeshQueryFilter { agentTypeID = AgentTypeId, areaMask = NavMesh.AllAreas };

        public void SetPassThroughObstacles(bool enabled)
        {
            if (PassThroughObstacles == enabled) return;
            PassThroughObstacles = enabled;
            IsReady = false;
            Points.Clear();
            RemoveMesh();
            Status = "Traversal changed; rebuild route at current position";
        }

        public bool TryGround(Vector3 expectedFeet, out RaycastHit hit, bool requireApproved = true)
        {
            hit = default;
            foreach (var area in areas)
            {
                if (area == null || (requireApproved && !area.approvedForCollection) || !area.Contains(expectedFeet)) continue;
                // Cast only through the selected floor's narrow slab; never from the roof.
                Vector3 from = new Vector3(expectedFeet.x, area.floorHeight + area.floorTolerance + 0.02f * U, expectedFeet.z);
                var hits = Physics.RaycastAll(from, Vector3.down, 2 * area.floorTolerance + 0.05f * U,
                    settings.collisionMask, QueryTriggerInteraction.Ignore);
                Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                foreach (var h in hits)
                {
                    if (h.collider is CharacterController) continue;
                    // Pass-through may look past an obstacle, but still requires the selected
                    // floor semantics, area and narrow height band. It never invents support.
                    if (Vector3.Angle(h.normal, Vector3.up) > settings.maxSlope || !CityGmlSurface.IsFloor(h, area))
                    {
                        if (PassThroughObstacles) continue;
                        break;
                    }
                    hit = h;
                    return true;
                }
            }
            return false;
        }

        public bool IsInSelectedArea(Vector3 feet, bool requireApproved = true)
        {
            foreach (var area in areas)
                if (area != null && (!requireApproved || area.approvedForCollection) && area.Contains(feet)) return true;
            return false;
        }

        // Traversal support can bridge a short seam underneath a wall/door. Image
        // endpoints still use TryGround and ClearBody: no images are taken on a bridge.
        public bool TryTraversalGround(Vector3 feet, out Vector3 support, bool requireApproved = true)
        {
            if (TryGround(feet, out var hit, requireApproved)) { support = hit.point; return true; }
            support = default;
            float limit = Mathf.Clamp(settings.passThroughMaximumGap, 0, 2) * U;
            if (!PassThroughObstacles || limit < 0.05f * U || !IsInSelectedArea(feet, requireApproved)) return false;
            // Skip open air before doing the more expensive anchor search.
            bool nearbyObstacle = false;
            foreach (var collider in Physics.OverlapSphere(feet + Vector3.up * Height * 0.5f, limit,
                settings.collisionMask, QueryTriggerInteraction.Ignore))
                if (!(collider is CharacterController)) { nearbyObstacle = true; break; }
            if (!nearbyObstacle) return false;
            foreach (var axis in BridgeAxes)
            {
                if (!FindFloorAnchor(feet, axis, limit, requireApproved, out var forward, out float forwardDistance) ||
                    !FindFloorAnchor(feet, -axis, limit - forwardDistance, requireApproved, out var backward, out float backwardDistance)) continue;
                float length = forwardDistance + backwardDistance;
                if (length > limit + 0.001f * U || Mathf.Abs(forward.y - backward.y) > settings.stepHeight * U) continue;
                // Both supports must belong to one explicitly selected floor/area.
                CaptureArea selected = null;
                foreach (var area in areas)
                    if (area != null && (!requireApproved || area.approvedForCollection) &&
                        area.Contains(feet) && area.Contains(forward) && area.Contains(backward)) { selected = area; break; }
                if (selected == null || !HasBarrierBetween(backward, forward, selected)) continue;
                float y = Mathf.Lerp(backward.y, forward.y, backwardDistance / length);
                support = new Vector3(feet.x, y, feet.z);
                return true;
            }
            return false;
        }
        static readonly Vector3[] BridgeAxes = {
            Vector3.right, Vector3.forward, new Vector3(1,0,1).normalized, new Vector3(1,0,-1).normalized
        };
        bool FindFloorAnchor(Vector3 feet, Vector3 axis, float limit, bool approved, out Vector3 point, out float distance)
        {
            point = default; distance = 0;
            float increment = 0.05f * U;
            for (float d = increment; d <= limit + 0.001f * U; d += increment)
            {
                Vector3 candidate = feet + axis * d;
                if (!IsInSelectedArea(candidate, approved)) return false;
                if (TryGround(candidate, out var hit, approved)) { point = hit.point; distance = d; return true; }
            }
            return false;
        }
        bool HasBarrierBetween(Vector3 from, Vector3 to, CaptureArea area)
        {
            Vector3 start = from + Vector3.up * Height * 0.5f;
            Vector3 delta = to - from;
            foreach (var hit in Physics.RaycastAll(start, delta.normalized, delta.magnitude,
                settings.collisionMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider is CharacterController || Mathf.Abs(hit.normal.y) > 0.7f) continue;
                if (!area.requireCityGmlFloor) return true;
                string kind = CityGmlSurface.Property(hit, "gml_class");
                if (kind == "bldg_WallSurface" || kind == "bldg_InteriorWallSurface" || kind == "bldg_Door" ||
                    kind == "bldg_Window" || kind == "bldg_BuildingInstallation" || kind == "bldg_IntBuildingInstallation") return true;
            }
            return false;
        }

        public bool ClearBody(Vector3 feet, float extra = 0)
        {
            float r = Radius + extra;
            // A small bottom clearance avoids touching the supporting floor.
            Vector3 bottom = feet + Vector3.up * (r + 0.035f * U);
            Vector3 top = feet + Vector3.up * (Height - r);
            foreach (var c in Physics.OverlapCapsule(bottom, top, r, settings.collisionMask, QueryTriggerInteraction.Ignore))
                if (!(c is CharacterController)) return false;
            return true;
        }

        public bool ClearSegment(Vector3 from, Vector3 to)
        {
            if (PassThroughObstacles) return true; // Support is checked separately at floor samples.
            Vector3 delta = to - from;
            float length = delta.magnitude;
            if (length < 0.001f) return true;
            float r = Radius;
            var hits = Physics.CapsuleCastAll(from + Vector3.up * (r + 0.04f * U),
                from + Vector3.up * (Height - r), r, delta / length, length,
                settings.collisionMask, QueryTriggerInteraction.Ignore);
            foreach (var h in hits) if (!(h.collider is CharacterController)) return false;
            return true;
        }

        public IEnumerator Build(Vector3 start)
        {
            if (IsBuilding) yield break;
            IsBuilding = true;
            IsReady = false;
            Points.Clear();
            if (!TryGround(start, out var initial) || (!PassThroughObstacles && !ClearBody(initial.point)))
            {
                Status = "Current position is not on an approved, clear floor. Manual mode retained.";
                Log?.Invoke("navigation_rejected", Status);
                IsBuilding = false;
                yield break;
            }
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            float cell = Mathf.Max(0.05f, settings.navigationCellSize) * U;
            int samples = 0;
            foreach (var area in areas)
            {
                if (area == null || !area.approvedForCollection) continue;
                Bounds bounds = area.WorldBounds;
                int nx = Mathf.CeilToInt(bounds.size.x / cell) + 1;
                int nz = Mathf.CeilToInt(bounds.size.z / cell) + 1;
                if ((long)nx * nz + samples > settings.maximumGridSamples)
                {
                    Status = "Area exceeds maximum grid samples. Reduce area size or increase cell size.";
                    Log?.Invoke("navigation_rejected", Status);
                    IsBuilding = false;
                    yield break;
                }
                var grid = new Vector3[nx, nz];
                var valid = new bool[nx, nz];
                for (int z = 0; z < nz; z++)
                {
                    for (int x = 0; x < nx; x++)
                    {
                        var p = new Vector3(bounds.min.x + x * cell, area.floorHeight, bounds.min.z + z * cell);
                        samples++;
                        if (PassThroughObstacles)
                        {
                            if (TryTraversalGround(p, out var support)) { grid[x, z] = support; valid[x, z] = true; }
                        }
                        else if (TryGround(p, out var h) && ClearBody(h.point, cell * 0.71f))
                        { grid[x, z] = h.point; valid[x, z] = true; }
                    }
                    if (z % 8 == 0) { Status = "Sampling floor: " + samples; yield return null; }
                }
                for (int z = 0; z < nz - 1; z++) for (int x = 0; x < nx - 1; x++)
                {
                    if (!valid[x,z] || !valid[x+1,z] || !valid[x,z+1] || !valid[x+1,z+1]) continue;
                    var a = grid[x,z]; var b = grid[x,z+1]; var c = grid[x+1,z+1]; var d = grid[x+1,z];
                    if (Mathf.Max(Mathf.Abs(a.y-b.y), Mathf.Abs(a.y-c.y), Mathf.Abs(a.y-d.y)) > settings.stepHeight * U) continue;
                    if (!ClearSegment(a,c) || !ClearSegment(b,d)) continue;
                    int n = vertices.Count;
                    vertices.AddRange(new[] { a,b,c,d });
                    triangles.AddRange(new[] { n,n+1,n+2,n,n+2,n+3 });
                }
            }
            RemoveMesh();
            if (vertices.Count == 0)
            {
                Status = "No verified walkable floor cells. Check tiles, bounds, level and colliders.";
                Log?.Invoke("navigation_rejected", Status); IsBuilding = false; yield break;
            }
            mesh = new Mesh { name = "Verified floor navigation", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
            var build = NavMesh.CreateSettings();
            AgentTypeId = build.agentTypeID; createdAgentSettings = true;
            // Normal mode already erodes cells by the body radius. Pass-through
            // keeps supported cells under obstacles, but capture endpoints remain clear.
            build.agentRadius = 0.01f * U;
            build.agentHeight = Height;
            build.agentClimb = settings.stepHeight * U;
            build.agentSlope = settings.maxSlope;
            build.overrideVoxelSize = true; build.voxelSize = Mathf.Min(cell / 3, Radius / 3);
            build.minRegionArea = 0.05f * U * U;
            var sources = new List<NavMeshBuildSource> { new NavMeshBuildSource {
                shape = NavMeshBuildSourceShape.Mesh, sourceObject = mesh, transform = Matrix4x4.identity, area = 0 } };
            Bounds all = mesh.bounds; all.Expand(new Vector3(2 * U, 2 * Height, 2 * U));
            data = NavMeshBuilder.BuildNavMeshData(build, sources, all, Vector3.zero, Quaternion.identity);
            if (data == null) { Status = "NavMesh build failed"; IsBuilding = false; yield break; }
            instance = NavMesh.AddNavMeshData(data);
            if (!NavMesh.SamplePosition(start, out var origin, 0.15f * U, Filter))
            {
                Status = "No NavMesh at current feet. Walk to a clear area, then retry; no teleport was performed.";
                if (NavMesh.SamplePosition(start, out var nearest, 3f * U, Filter))
                    Status += " Nearest valid floor=" + nearest.position + "; distance=" + Vector3.Distance(start, nearest.position);
                Log?.Invoke("navigation_rejected", Status); IsBuilding = false; yield break;
            }
            if (useInspectorWaypoints)
            {
                foreach (var point in waypoints)
                    if (point != null) AddCandidate(point.position, origin.position, point.name);
            }
            else
            {
                float spacing = Mathf.Max(0.1f, settings.pointSpacing) * U;
                foreach (var area in areas)
                {
                    if (area == null || !area.approvedForCollection) continue;
                    Bounds bounds = area.WorldBounds;
                    int row = 0;
                    for (float z = bounds.min.z; z <= bounds.max.z; z += spacing, row++)
                    {
                        int count = Mathf.FloorToInt(bounds.size.x / spacing);
                        for (int i = 0; i <= count && Points.Count < settings.maximumWaypoints; i++)
                        {
                            float x = bounds.min.x + ((row % 2 == 0) ? i : count-i) * spacing;
                            AddCandidate(new Vector3(x,area.floorHeight,z), origin.position, "grid");
                        }
                        yield return null;
                    }
                }
            }
            IsReady = Points.Count > 0;
            Status = IsReady ? "Route ready: " + Points.Count + " points" : "No reachable capture points";
            Log?.Invoke("navigation_ready", Status);
            IsBuilding = false;
        }

        void AddCandidate(Vector3 point, Vector3 origin, string name)
        {
            // Never create capture endpoints inside walls/furniture, including pass-through mode.
            if (!TryGround(point, out var h) || !ClearBody(h.point))
            { if(name!="grid") Log?.Invoke("waypoint_skipped",name+": no approved clear floor"); return; }
            if (!NavMesh.SamplePosition(h.point, out var sampled, 0.12f * U, Filter) ||
                Vector3.Distance(sampled.position,h.point) > 0.12f * U)
            { if (name != "grid") Log?.Invoke("waypoint_skipped", name + ": outside NavMesh"); return; }
            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(origin, sampled.position, Filter, path) || path.status != NavMeshPathStatus.PathComplete)
            { Log?.Invoke("waypoint_skipped", name + ": unreachable " + point); return; }
            foreach (var p in Points) if ((p - sampled.position).sqrMagnitude < 0.01f * U * U) return;
            Points.Add(sampled.position);
        }

        public bool Path(Vector3 start, Vector3 target, NavMeshPath path) => IsReady &&
            NavMesh.CalculatePath(start,target,Filter,path) && path.status == NavMeshPathStatus.PathComplete;

        void RemoveMesh()
        {
            if (instance.valid) instance.Remove();
            if (data != null) Destroy(data);
            if (mesh != null) Destroy(mesh);
            if (createdAgentSettings) { NavMesh.RemoveSettings(AgentTypeId); createdAgentSettings = false; }
        }
        void OnDestroy() { RemoveMesh(); }
    }
}
