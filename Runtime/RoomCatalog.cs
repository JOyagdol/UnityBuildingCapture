using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using CesiumForUnity;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityResearchCapture
{
    [Serializable]
    public sealed class RoomLabel
    {
        public string roomId = "unknown", roomName = "unknown", roomDescription = "";
        public string status = "no_catalog", source = "none", reason = "No room catalog or authored region";
        public string catalogId = "", catalogSha256 = "", sourceSha256 = "";
        public string[] candidateIds = Array.Empty<string>();
        public string reviewStatus="not_reviewed";
        public string regionId="",regionGeometrySha256="",regionCatalogTextSha256="";
        public string reviewedBy="",reviewedAtUtc="",reviewEvidence="",referenceSnapshotSha256="",candidatePoolSha256="";
        public bool Resolved => status == "resolved";
    }

    [Serializable]
    public sealed class RoomCatalogData
    {
        public int schemaVersion;
        public string catalogId, buildingId, sourceSha256, sourceCrs, coordinateSystem;
        public double[] originEcef, ecefToLocal;
        public RoomVolume[] rooms;

        public void Validate()
        {
            if (schemaVersion != 1 || string.IsNullOrWhiteSpace(catalogId) || string.IsNullOrWhiteSpace(buildingId) ||
                originEcef == null || originEcef.Length != 3 || ecefToLocal == null || ecefToLocal.Length != 9 || rooms == null || rooms.Length == 0)
                throw new ArgumentException("Incomplete room catalog header");
            foreach (double v in originEcef) if (double.IsNaN(v) || double.IsInfinity(v)) throw new ArgumentException("Nonfinite origin");
            foreach (double v in ecefToLocal) if (double.IsNaN(v) || double.IsInfinity(v)) throw new ArgumentException("Nonfinite axes");
            for (int a=0;a<3;a++) for (int b=0;b<3;b++)
            {
                double dot=0; for(int k=0;k<3;k++) dot+=ecefToLocal[a*3+k]*ecefToLocal[b*3+k];
                if(Math.Abs(dot-(a==b?1:0))>1e-6) throw new ArgumentException("Catalog axes must be orthonormal");
            }
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var room in rooms)
            {
                if(room==null || string.IsNullOrWhiteSpace(room.roomId) || room.roomId=="unknown" || !ids.Add(room.roomId))
                    throw new ArgumentException("Missing or duplicate Room ID");
                room.Validate();
            }
        }
        public Vector3 ToLocal(double3 ecef)
        {
            var d=new[]{ecef.x-originEcef[0],ecef.y-originEcef[1],ecef.z-originEcef[2]};
            return new Vector3((float)(ecefToLocal[0]*d[0]+ecefToLocal[1]*d[1]+ecefToLocal[2]*d[2]),
                (float)(ecefToLocal[3]*d[0]+ecefToLocal[4]*d[1]+ecefToLocal[5]*d[2]),
                (float)(ecefToLocal[6]*d[0]+ecefToLocal[7]*d[1]+ecefToLocal[8]*d[2]));
        }
    }

    [Serializable]
    public sealed class RoomVolume
    {
        public string roomId, roomName, roomDescription, floorId;
        public Vector3[] vertices;
        public int[] triangles;
        [NonSerialized] Bounds bounds;
        public void Validate()
        {
            if(vertices==null || vertices.Length<4 || triangles==null || triangles.Length<12 || triangles.Length%3!=0)
                throw new ArgumentException("Room requires a closed triangle mesh: "+roomId);
            bounds=new Bounds(vertices[0],Vector3.zero);
            foreach(var v in vertices)
            {
                if(!Finite(v.x)||!Finite(v.y)||!Finite(v.z)) throw new ArgumentException("Nonfinite room vertex");
                bounds.Encapsulate(v);
            }
            var edges=new Dictionary<ulong,int>();
            for(int i=0;i<triangles.Length;i+=3)
            {
                for(int j=0;j<3;j++) if(triangles[i+j]<0 || triangles[i+j]>=vertices.Length) throw new ArgumentException("Invalid triangle index");
                if(Vector3.Cross(vertices[triangles[i+1]]-vertices[triangles[i]],vertices[triangles[i+2]]-vertices[triangles[i]]).sqrMagnitude<1e-12f)
                    throw new ArgumentException("Degenerate room triangle");
                for(int j=0;j<3;j++)
                {
                    int a=triangles[i+j],b=triangles[i+(j+1)%3];
                    ulong key=((ulong)(uint)Math.Min(a,b)<<32)|(uint)Math.Max(a,b);
                    edges.TryGetValue(key,out int count); edges[key]=count+1;
                }
            }
            foreach(int count in edges.Values) if(count!=2) throw new ArgumentException("Room mesh is open or non-manifold: "+roomId);
        }
        static bool Finite(float v) => !float.IsNaN(v)&&!float.IsInfinity(v);
        // 0 outside, 1 strictly inside, 2 within the boundary tolerance, 3 inconsistent rays.
        public int Classify(Vector3 p,float tolerance)
        {
            if(!Finite(p.x)||!Finite(p.y)||!Finite(p.z)) return 3;
            var expanded=bounds; expanded.Expand(2*tolerance);
            if(!expanded.Contains(p)) return 0;
            for(int i=0;i<triangles.Length;i+=3)
            {
                var a=vertices[triangles[i]];var b=vertices[triangles[i+1]];var c=vertices[triangles[i+2]];
                if(TriangleDistanceSquared(p,a,b,c)<=tolerance*tolerance) return 2;
            }
            int first=RayParity(p,new Vector3(0.81273f,0.36719f,0.45281f).normalized);
            int second=RayParity(p,new Vector3(-0.28371f,0.91731f,0.27953f).normalized);
            return first==second?first:3;
        }
        int RayParity(Vector3 p,Vector3 direction)
        {
            var hits=new List<float>();
            for(int i=0;i<triangles.Length;i+=3)
            {
                Vector3 a=vertices[triangles[i]],e1=vertices[triangles[i+1]]-a,e2=vertices[triangles[i+2]]-a;
                Vector3 h=Vector3.Cross(direction,e2); float determinant=Vector3.Dot(e1,h);
                if(Mathf.Abs(determinant)<1e-8f) continue;
                float inv=1/determinant; Vector3 s=p-a;float u=inv*Vector3.Dot(s,h);
                if(u< -1e-6f || u>1+1e-6f) continue;
                Vector3 q=Vector3.Cross(s,e1);float v=inv*Vector3.Dot(direction,q);
                if(v< -1e-6f || u+v>1+1e-6f) continue;
                float t=inv*Vector3.Dot(e2,q); if(t>1e-6f) hits.Add(t);
            }
            hits.Sort(); int unique=0;float last=float.NegativeInfinity;
            foreach(float t in hits) if(t-last>1e-4f){unique++;last=t;}
            return unique%2;
        }
        static float TriangleDistanceSquared(Vector3 p,Vector3 a,Vector3 b,Vector3 c)
        {
            Vector3 ab=b-a,ac=c-a,ap=p-a;float d1=Vector3.Dot(ab,ap),d2=Vector3.Dot(ac,ap);
            if(d1<=0&&d2<=0)return ap.sqrMagnitude;
            Vector3 bp=p-b;float d3=Vector3.Dot(ab,bp),d4=Vector3.Dot(ac,bp);
            if(d3>=0&&d4<=d3)return bp.sqrMagnitude;
            float vc=d1*d4-d3*d2;if(vc<=0&&d1>=0&&d3<=0)return (p-(a+ab*(d1/(d1-d3)))).sqrMagnitude;
            Vector3 cp=p-c;float d5=Vector3.Dot(ab,cp),d6=Vector3.Dot(ac,cp);
            if(d6>=0&&d5<=d6)return cp.sqrMagnitude;
            float vb=d5*d2-d1*d6;if(vb<=0&&d2>=0&&d6<=0)return (p-(a+ac*(d2/(d2-d6)))).sqrMagnitude;
            float va=d3*d6-d5*d4;if(va<=0&&(d4-d3)>=0&&(d5-d6)>=0)return (p-(b+(c-b)*((d4-d3)/((d4-d3)+(d5-d6))))).sqrMagnitude;
            float denom=1/(va+vb+vc);return (p-(a+ab*(vb*denom)+ac*(vc*denom))).sqrMagnitude;
        }
    }

    public sealed class RoomLabelLookup
    {
        TextAsset cachedAsset;
        RoomCatalogData catalog;
        string hash="",error="";
        bool loaded;
        public RoomLabel Resolve(Vector3 worldPosition,Scene scene,CesiumGeoreference georeference,TextAsset asset,
            string buildingId,string floorId,float boundaryToleranceMetres)
        {
            var explicitLabel=RoomRegion.ResolveLabel(worldPosition,scene,buildingId,floorId);
            if(explicitLabel!=null) return explicitLabel;
            Load(asset);
            var result=new RoomLabel{catalogSha256=hash};
            if(asset==null) return result;
            if(catalog==null){result.status="invalid_catalog";result.reason=error;return result;}
            result.catalogId=catalog.catalogId;result.sourceSha256=catalog.sourceSha256;
            result.source="CityGML Room closed-volume containment; camera optical centre";
            if(string.IsNullOrEmpty(buildingId)||buildingId=="unknown"||catalog.buildingId!=buildingId)
            {result.status="building_mismatch";result.reason="Catalog does not match the active building";return result;}
            if(georeference==null){result.status="georeference_missing";result.reason="ECEF conversion requires a CesiumGeoreference";return result;}
            var local=georeference.transform.InverseTransformPoint(worldPosition);
            var ecef=georeference.TransformUnityPositionToEarthCenteredEarthFixed(new double3(local.x,local.y,local.z));
            return ResolveLocal(catalog.ToLocal(ecef),floorId,boundaryToleranceMetres,result);
        }
        public RoomLabel ResolveLocal(Vector3 point,string floorId,float tolerance,RoomLabel result=null)
        {
            if(result==null) result=new RoomLabel{catalogSha256=hash};
            result.roomId="unknown"; result.roomName="unknown"; result.roomDescription="";
            result.candidateIds=Array.Empty<string>();
            if(catalog==null){result.status="invalid_catalog";result.reason=error;return result;}
            result.catalogId=catalog.catalogId;result.sourceSha256=catalog.sourceSha256;
            result.source="CityGML Room closed-volume containment; camera optical centre";
            var inside=new List<RoomVolume>();var uncertain=new List<string>();
            foreach(var room in catalog.rooms)
            {
                if(!string.IsNullOrEmpty(floorId)&&floorId!="unknown"&&room.floorId!=floorId) continue;
                int match=room.Classify(point,Mathf.Max(0.0001f,tolerance));
                if(match==1) inside.Add(room); else if(match>=2) uncertain.Add(room.roomId);
            }
            var candidates=new List<string>(); foreach(var r in inside)candidates.Add(r.roomId); candidates.AddRange(uncertain);
            result.candidateIds=candidates.ToArray();
            if(uncertain.Count>0){result.status="boundary";result.reason="Near a Room boundary or geometry gives inconsistent containment";}
            else if(inside.Count>1){result.status="ambiguous";result.reason="Overlapping Room volumes";}
            else if(inside.Count==0){result.status="outside_rooms";result.reason="No Room volume contains the camera on this floor";}
            else
            {
                result.reviewStatus="source_geometry_unreviewed";
                var room=inside[0];result.roomId=room.roomId;result.roomName=string.IsNullOrWhiteSpace(room.roomName)?room.roomId:room.roomName;
                result.roomDescription=room.roomDescription??"";result.status="resolved";result.reason="Exactly one source Room volume contains the camera";
            }
            return result;
        }
        public void Load(TextAsset asset)
        {
            if(loaded&&cachedAsset==asset)return;
            loaded=true;cachedAsset=asset;catalog=null;hash="";error="";
            if(asset==null)return;
            try
            {
                using(var sha=SHA256.Create()) hash=BitConverter.ToString(sha.ComputeHash(asset.bytes)).Replace("-","").ToLowerInvariant();
                var parsed=JsonUtility.FromJson<RoomCatalogData>(asset.text); if(parsed==null)throw new ArgumentException("Invalid JSON");
                parsed.Validate();catalog=parsed;
            }
            catch(Exception e){error=e.GetType().Name+": "+e.Message;}
        }
    }
}
