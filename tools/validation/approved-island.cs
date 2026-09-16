using System;
using System.Linq;
using System.Text;
using UnityEngine;
using SeaSick.Terrain;
public static class ApprovedIslandCheck
{
    public static string Main()
    {
        if (!Application.isPlaying) throw new Exception("Play mode required");
        var go=GameObject.Find("Home Plateau — Connected Ground");
        if (!go) throw new Exception("Authored terrain missing");
        var collider=go.GetComponent<MeshCollider>(); var mesh=go.GetComponent<MeshFilter>().sharedMesh;
        var st=UnityEngine.Object.FindFirstObjectByType<TerrainStreamer>();var prm=TerrainParams.From(st.settings);
        var v=mesh.vertices;var t=mesh.triangles;float max=0;int tested=0;
        for(int i=0;i<t.Length;i+=3)
        {
            var local=(v[t[i]]+v[t[i+1]]+v[t[i+2]])/3f;var world=go.transform.TransformPoint(local);
            if(!collider.Raycast(new Ray(world+Vector3.up*150,Vector3.down),out var hit,300))throw new Exception("Collider hole "+i/3);
            if(!HomePlateauSurface.TryHeight(new Unity.Mathematics.float2(world.x,world.z),prm,out float h))throw new Exception("Height hole "+i/3);
            max=Mathf.Max(max,Mathf.Abs(hit.point.y-h));tested++;
        }
        if(max>.005f)throw new Exception("Collider/height mismatch "+max);
        var home=UnityEngine.Object.FindObjectsByType<SeaSick.World.Island>(FindObjectsSortMode.None).First(x=>x.IsHome);
        var wood=home.GetComponentInChildren<SceneryWood>();
        if(!wood || wood.TreeCount<50)throw new Exception("Authored trees missing");
        float treeError=0; string worst="";
        for(int i=0;i<wood.TreeCount;i++)
        {
            var p=wood.TreeAt(i).baseAt;
            HomePlateauSurface.TryHeight(new Unity.Mathematics.float2(p.x,p.z),prm,out float h);
            if(Mathf.Abs(p.y+.06f-h)>treeError) { treeError=Mathf.Abs(p.y+.06f-h); worst=" tree "+i+" at "+p+" ground "+h+" root "+wood.transform.position+" instance "+wood.TreeAt(i).instance.name; }
        }
        if(treeError>.02f)throw new Exception("Tree grounding mismatch "+treeError+worst);
        var report="Exact ground: "+tested+" triangles; max collision/height error "+max+"m. Trees: "+wood.TreeCount+"; grounding error "+treeError+"m.\n";
        report+="Home "+home.transform.position+"; ship "+UnityEngine.Object.FindFirstObjectByType<SeaSick.Ship.ShipMotor>().transform.position+"\n";
        var dock=SeaSick.World.Dock.Home;
        if(!dock)throw new Exception("Home dock missing");
        float berthGround=SeaSick.World.Island.TerrainHeight(dock.Berth.x,dock.Berth.z);
        if(berthGround>-2f)throw new Exception("Berth too shallow "+berthGround);
        report+="Dock landing "+dock.Landing+"; berth "+dock.Berth+"; actual seabed "+berthGround+"m.\n";
        var tree=wood.TreeAt(0);var neighbour=wood.TreeAt(1).instance;
        wood.Populate(tree.baseAt,2f,48);
        var node=tree.instance.GetComponent<SeaSick.World.ResourceNode>();
        if(!node)throw new Exception("Harvest node missing");
        node.Harvest();
        if(tree.instance.activeSelf || !neighbour.activeSelf)throw new Exception("Individual harvest failed");
        report+="Individual tree harvest passed; neighbour intact. Restart play before handoff.\n";
        foreach(var b in home.GetComponentsInChildren<Transform>())if(b.name.ToLower().Contains("dock"))report+=b.name+" "+b.position+"\n";
        System.IO.File.WriteAllText("docs/art-direction/approved-island-unity-validation.txt",report);
        return report;
    }
}
