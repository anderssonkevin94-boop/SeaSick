using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using SeaSick.World;
using SeaSick.Terrain;

public static class ReviewIslandNature
{
    const string Output="art-staging/island-nature-survey/redesign";
    static Island Target() => Island.All.First(i=>Vector2.Distance(new Vector2(i.transform.position.x,i.transform.position.z),new Vector2(660,81))<1);
    public static string Verify()
    {
        if(!Application.isPlaying || !SeaSick.Save.SaveGame.Suppressed) throw new Exception("Save protection required");
        var island=Target();var wood=island.GetComponentInChildren<SceneryWood>();
        var profile=IslandNatureProfile.Active;
        var library=JsonUtility.FromJson<IslandNatureProfile.Library>(File.ReadAllText("Assets/_Project/Art/AstraPlaytest/NatureIsland2/kit.json"));
        foreach(var entry in library.entries)
        {
            for(int i=0;i<entry.triangles.Length;i+=3)
            {
                int a=entry.triangles[i],b=entry.triangles[i+1],c=entry.triangles[i+2];
                if(Vector3.Dot(Vector3.Cross(entry.vertices[b]-entry.vertices[a],entry.vertices[c]-entry.vertices[a]),entry.normals[a])<=0)
                    throw new Exception("Bad winding or degenerate triangle: "+entry.name);
            }
        }
        var streamer=UnityEngine.Object.FindAnyObjectByType<TerrainStreamer>();
        var settings=streamer.settings;
        var holder=new GameObject("Temporary original scenery comparison");
        GameObject baseline=null;
        var so=new SerializedObject(profile);var centre=so.FindProperty("islandCentre").vector2Value;
        try
        {
            so.FindProperty("islandCentre").vector2Value=new Vector2(-99999,-99999);so.ApplyModifiedPropertiesWithoutUndo();
            baseline=IslandScenery.Build(holder.transform,island.transform.position,island.Radius,Island.TerrainHeight,
                settings,a=>island.RadiusAt(a),settings.seed*7919+2,TerrainParams.From(settings),island);
            var original=baseline.GetComponent<SceneryWood>();
            if(original.TreeCount!=wood.TreeCount) throw new Exception("Tree count changed: "+original.TreeCount+" -> "+wood.TreeCount);
            for(int i=0;i<wood.TreeCount;i++)
                if(Vector3.Distance(original.TreeAt(i).baseAt,wood.TreeAt(i).baseAt)>.001f) throw new Exception("Tree index moved: "+i);
            int prior=wood.FelledInMesh();
            wood.FellForLedger(0);
            if(wood.FelledInMesh()!=prior+1)throw new Exception("Felling did not collapse mesh");
            wood.Restand(0);
            if(wood.FelledInMesh()!=prior)throw new Exception("Regrowth did not restore mesh");
            int high=wood.Cells.Sum(c=>c.lod0!=null ? c.lod0.triangles.Length/3:0);
            int low=wood.Cells.Sum(c=>c.lod1!=null ? c.lod1.triangles.Length/3:0);
            if(low>=high)throw new Exception("Distant geometry is not cheaper");
            var text=$"PASS: {library.entries.Length} templates have outward face winding.\nPASS: {wood.TreeCount} tree positions and index order unchanged. Felling and regrowth mesh checks pass.\n"+
                $"New scenery: {wood.Cells.Count} cells, {high} LOD0 triangles, {low} LOD1 triangles.\n"+
                "No terrain heights, resource yields or save files modified. No on-device frame-time claim.\n";
            Directory.CreateDirectory(Output);File.WriteAllText(Output+"/validation.txt",text);return text;
        }
        finally
        {
            so.FindProperty("islandCentre").vector2Value=centre;so.ApplyModifiedPropertiesWithoutUndo();
            UnityEngine.Object.DestroyImmediate(holder);
        }
    }
    public static string Prepare()
    {
        if(!Application.isPlaying || !SeaSick.Save.SaveGame.Suppressed)throw new Exception("Protected Play mode required");
        Directory.CreateDirectory(Output);
        var cam=new GameObject("Island nature review camera").AddComponent<Camera>();
        cam.CopyFrom(Camera.main);cam.enabled=false;cam.orthographic=true;cam.orthographicSize=385;cam.farClipPlane=5000;
        cam.transform.position=new Vector3(660,700,81);cam.transform.rotation=Quaternion.Euler(90,0,0);
        UnityEngine.Object.FindAnyObjectByType<TerrainStreamer>().target=cam.transform;
        var ocean=UnityEngine.Object.FindAnyObjectByType<SeaSick.Ocean.OceanClipmap>();if(ocean)ocean.FollowOverride=cam.transform;
        var sky=UnityEngine.Object.FindAnyObjectByType<SkyDirector>();
        var so=new SerializedObject(sky);so.FindProperty("pinTime").floatValue=.38f;so.FindProperty("forceStorm").floatValue=0;so.ApplyModifiedPropertiesWithoutUndo();sky.ForceApply();
        foreach(var lod in UnityEngine.Object.FindObjectsByType<SceneryLod>(FindObjectsSortMode.None))lod.SendMessage("Update");
        return "Uninhabited island framed";
    }
    public static string View(string kind)
    {
        var cam=GameObject.Find("Island nature review camera").GetComponent<Camera>();
        Vector3 target;
        if(kind=="forest") {target=new Vector3(603,Island.TerrainHeight(603,55)+4,55);cam.orthographicSize=44;cam.transform.position=target+new Vector3(25,45,-36);}
        else if(kind=="coast") {target=new Vector3(745,Island.TerrainHeight(745,152)+4,152);cam.orthographicSize=66;cam.transform.position=target+new Vector3(45,60,50);}
        else {target=new Vector3(660,20,75);cam.orthographicSize=290;cam.transform.position=target+new Vector3(230,320,400);}
        cam.transform.LookAt(target);
        return kind+" framed";
    }
    public static string Sea(string side)
    {
        var cam=GameObject.Find("Island nature review camera").GetComponent<Camera>();
        cam.orthographic=false;cam.fieldOfView=58;
        cam.transform.position=side=="east" ? new Vector3(1120,9,81) : new Vector3(660,7,-180);
        cam.fieldOfView=side=="east" ? 38 : 58;
        cam.transform.LookAt(side=="east" ? new Vector3(700,25,81) : new Vector3(660,22,81));
        RefreshLod();return side;
    }
    public static string RefreshLod()
    {
        foreach(var lod in UnityEngine.Object.FindObjectsByType<SceneryLod>())
        {
            typeof(SceneryLod).GetField("next",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).SetValue(lod,0f);
            lod.SendMessage("Update");
        }
        string report="";
        foreach(var g in UnityEngine.Object.FindObjectsByType<NatureGrounding>())report+=g.name+": "+g.Audit()+"\n";
        File.WriteAllText(Output+"/grounding.txt",report);return report;
    }
    public static string Capture(string name,bool portrait=false)
    {
        var camera=GameObject.Find("Island nature review camera").GetComponent<Camera>();
        int w=portrait ? 1080:1600,h=portrait ? 2340:1200;
        var rt=RenderTexture.GetTemporary(w,h,24);var old=RenderTexture.active;Texture2D image=null;
        try
        {
            camera.aspect=(float)w/h;camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
            image=new Texture2D(w,h,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,w,h),0,0);image.Apply();
            File.WriteAllBytes(Output+"/"+name+".png",image.EncodeToPNG());
            if(ShaderUtil.ShaderHasError(Shader.Find("SeaSick/Terrain Vertex Color")))throw new Exception("Terrain shader errors");
        }
        finally{camera.targetTexture=null;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);if(image)UnityEngine.Object.DestroyImmediate(image);}
        return Path.GetFullPath(Output+"/"+name+".png");
    }
}
