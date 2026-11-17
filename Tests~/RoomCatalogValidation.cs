#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityResearchCapture;

// Place this file in an Editor folder in a disposable test project.
public static class RoomCatalogValidation
{
    static int count;
    static void Check(bool condition,string name)
    { if(!condition) throw new Exception("ROOM_CHECK failed: "+name); count++; Debug.Log("ROOM_CHECK PASS "+name); }
    static RoomVolume Cube(string id)
    {
        return new RoomVolume { roomId=id,roomName="Synthetic room",floorId="L1",
            vertices=new[]{new Vector3(-1,-1,-1),new Vector3(1,-1,-1),new Vector3(1,1,-1),new Vector3(-1,1,-1),
                new Vector3(-1,-1,1),new Vector3(1,-1,1),new Vector3(1,1,1),new Vector3(-1,1,1)},
            triangles=new[]{0,2,1,0,3,2,4,5,6,4,6,7,0,1,5,0,5,4,3,7,6,3,6,2,0,4,7,0,7,3,1,2,6,1,6,5}};
    }
    static RoomCatalogData Catalog(params RoomVolume[] rooms) => new RoomCatalogData {schemaVersion=1,catalogId="synthetic-test",
        buildingId="test",originEcef=new double[3],ecefToLocal=new double[]{1,0,0,0,1,0,0,0,1},rooms=rooms};
    public static int Run()
    {
        count=0;var cube=Cube("room-A");cube.Validate();
        Check(cube.Classify(Vector3.zero,.02f)==1,"closed mesh interior");
        Check(cube.Classify(new Vector3(2,0,0),.02f)==0,"outside mesh");
        Check(cube.Classify(new Vector3(.99f,0,0),.02f)==2,"boundary remains uncertain");
        var rotation=Quaternion.Euler(17,31,8);
        for(int i=0;i<cube.vertices.Length;i++)cube.vertices[i]=rotation*cube.vertices[i]+new Vector3(4,5,6);
        cube.Validate();Check(cube.Classify(new Vector3(4,5,6),.02f)==1,"rotated room interior");
        var open=Cube("open");Array.Resize(ref open.triangles,open.triangles.Length-3);
        bool rejected=false;try{open.Validate();}catch(ArgumentException){rejected=true;}
        Check(rejected,"open mesh rejected");
        var lookup=new RoomLabelLookup();var asset=new TextAsset(JsonUtility.ToJson(Catalog(Cube("A"),Cube("B"))));lookup.Load(asset);
        Check(lookup.ResolveLocal(Vector3.zero,"L1",.02f).status=="ambiguous","overlap has no asserted ID");
        UnityEngine.Object.DestroyImmediate(asset);
        asset=new TextAsset(JsonUtility.ToJson(Catalog(Cube("A"))));lookup.Load(asset);
        var label=lookup.ResolveLocal(Vector3.zero,"L1",.02f);
        Check(label.roomId=="A"&&label.Resolved&&label.catalogSha256.Length==64,"identifier and provenance");
        label=lookup.ResolveLocal(new Vector3(3,0,0),"L1",.02f,label);
        Check(label.roomId=="unknown"&&label.status=="outside_rooms","reused result clears prior ID");
        Check(lookup.ResolveLocal(Vector3.zero,"L2",.02f).roomId=="unknown","floor scope");
        Check(lookup.Resolve(Vector3.zero,SceneManager.GetActiveScene(),null,asset,"other","L1",.02f).status=="building_mismatch","building scope");
        Check(lookup.Resolve(Vector3.zero,SceneManager.GetActiveScene(),null,asset,"test","L1",.02f).status=="georeference_missing","missing transform never guesses");
        UnityEngine.Object.DestroyImmediate(asset);
        asset=new TextAsset("{}");lookup.Load(asset);
        Check(lookup.ResolveLocal(Vector3.zero,"L1",.02f).status=="invalid_catalog","invalid catalog");
        UnityEngine.Object.DestroyImmediate(asset);
        var go=new GameObject("Synthetic RoomRegion");var region=go.AddComponent<RoomRegion>();region.roomId="explicit";
        Check(RoomRegion.ResolveLabel(Vector3.zero,go.scene,""," ")?.status=="unreviewed_region","unreviewed region is not certified");
        region.enabled=false;Check(RoomRegion.ResolveLabel(Vector3.zero,go.scene,"","")==null,"disabled region excluded");
        region.enabled=true;region.buildingId="other";
        Check(RoomRegion.ResolveLabel(Vector3.zero,go.scene,"test","")==null,"authored building scope");
        region.buildingId="";var duplicate=new GameObject("Conflicting RoomRegion");duplicate.AddComponent<RoomRegion>().roomId="different";
        Check(RoomRegion.ResolveLabel(Vector3.zero,go.scene,"","").status=="ambiguous","conflicting authored regions");
        UnityEngine.Object.DestroyImmediate(duplicate);UnityEngine.Object.DestroyImmediate(go);
        Debug.Log("ROOM_MATH_COMPLETE "+count);return count;
    }
}
#endif
