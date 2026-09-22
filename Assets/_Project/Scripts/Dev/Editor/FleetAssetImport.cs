using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using SeaSick.Ship;

namespace SeaSick.Dev
{
    public static class FleetAssetImport
    {
        const string Source = "tools/blender/exports/fleet-v3-unity";
        const string Art = "Assets/_Project/Art/FleetV3";
        const string ResourcesDir = "Assets/_Project/Resources/Ships/FleetV3";
        [Serializable] public class Gun { public string name; public Vector3 position; public int side; public float pivot, muzzle; public bool deck; }
        [Serializable] public class Sail { public string name; public Vector3 position; }
        [Serializable] public class Definition { public int stage,cannons,triangles; public string name; public float freeboard; public Vector3 helm; public Gun[] guns; public Sail[] sails; public LadderNode node; }
        [MenuItem("SeaSick/Art/Import approved fleet V3")]
        public static void Execute()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Fleet import requires edit mode.");
            Directory.CreateDirectory(Art); Directory.CreateDirectory(ResourcesDir);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var shader = Shader.Find("SeaSick/Fleet Vertex Color");
            if (!shader || ShaderUtil.ShaderHasError(shader)) throw new Exception("Fleet vertex colour shader unavailable.");
            var material = AssetDatabase.LoadAssetAtPath<Material>(Art + "/FleetPalette.mat");
            if (!material) { material = new Material(shader); AssetDatabase.CreateAsset(material, Art + "/FleetPalette.mat"); }
            material.shader=shader; material.SetFloat("_Ambient",.25f); material.SetColor("_BaseColor",Color.white); material.enableInstancing = true; EditorUtility.SetDirty(material);
            var ladder = JsonUtility.FromJson<LadderData>(File.ReadAllText("Assets/_Project/Resources/Ladder/ladder.txt"));
            var report = new StringBuilder("Approved fleet Unity import\nBow +Z, metres, waterline Y=0; baked vertex colours; native meshes and prefabs.\n");
            for (int stage=1;stage<=20;stage++)
            {
                var d=JsonUtility.FromJson<Definition>(File.ReadAllText($"{Source}/{stage:00}.json"));
                ladder.nodes[stage-1]=d.node;
                var scene=EditorSceneManager.NewPreviewScene();var root=new GameObject($"Ship{stage:00}");SceneManager.MoveGameObjectToScene(root,scene);
                try
                {
                    var visual=root.AddComponent<FleetVisual>();visual.stage=stage;visual.helm=d.helm;visual.freeboard=d.freeboard;visual.length=d.node.length;
                    var groups=new Dictionary<string,Transform>();var gunRoots=new List<Transform>();var sailRoots=new List<Transform>();
                    foreach(var g in d.guns)
                    {
                        var go=new GameObject(g.name);go.transform.SetParent(root.transform,false);go.transform.localPosition=g.position;go.transform.localRotation=Quaternion.Euler(0,g.side*90,0);
                        var fg=go.AddComponent<FleetGun>();fg.pivotHeight=g.pivot;fg.muzzleLength=g.muzzle;fg.weatherDeck=g.deck;
                        groups[g.name]=go.transform;gunRoots.Add(go.transform);
                    }
                    foreach(var s in d.sails)
                    {
                        var go=new GameObject(s.name);go.transform.SetParent(root.transform,false);go.transform.localPosition=s.position;
                        groups[s.name]=go.transform;sailRoots.Add(go.transform);
                    }
                    int triangles=0;
                    using(var reader=new BinaryReader(File.OpenRead($"{Source}/{stage:00}.fleetmesh")))
                    {
                        int count=reader.ReadInt32();
                        for(int k=0;k<count;k++)
                        {
                            string key=Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32()));int vc=reader.ReadInt32(),ic=reader.ReadInt32();
                            var vertices=new Vector3[vc];var normals=new Vector3[vc];var colors=new Color[vc];var indices=new int[ic];
                            for(int v=0;v<vc;v++){vertices[v]=ReadVector(reader);normals[v]=ReadVector(reader);colors[v]=new Color(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());}
                            for(int t=0;t<ic;t++)indices[t]=reader.ReadInt32();
                            if(vc==0)continue;
                            string path=$"{Art}/{stage:00}-{key.Replace('/','-')}.asset";
                            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                            bool fresh=!mesh;if(fresh)mesh=new Mesh();else mesh.Clear();
                            mesh.name=Path.GetFileNameWithoutExtension(path);mesh.indexFormat=IndexFormat.UInt32;mesh.vertices=vertices;mesh.normals=normals;mesh.colors=colors;mesh.triangles=indices;mesh.RecalculateBounds();
                            if(fresh)AssetDatabase.CreateAsset(mesh,path);else EditorUtility.SetDirty(mesh);
                            triangles+=ic/3;
                            Transform part;
                            if(key.StartsWith("Sail"))part=groups[key];
                            else {var go=new GameObject(key.Contains('/')?key.Split('/')[1]:key);part=go.transform;part.SetParent(key.Contains('/')?groups[key.Split('/')[0]]:root.transform,false);}
                            part.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;part.gameObject.AddComponent<MeshRenderer>().sharedMaterial=material;
                            if(key.EndsWith("/Barrel")){var gun=part.parent.GetComponent<FleetGun>();gun.barrel=part;part.localPosition=Vector3.up*gun.pivotHeight;}
                        }
                    }
                    visual.gunTemplates=gunRoots.ToArray();visual.sailPivots=sailRoots.ToArray();
                    if(triangles!=d.triangles || gunRoots.Count!=d.cannons)throw new Exception("Fleet source count mismatch " + stage);
                    PrefabUtility.SaveAsPrefabAsset(root,$"{ResourcesDir}/Ship{stage:00}.prefab");
                    report.AppendLine($"{stage:00} {d.name}: {triangles} triangles, {gunRoots.Count} cannons, {sailRoots.Count} sails; {d.node.length} x {d.node.beam} m");
                }
                finally {UnityEngine.Object.DestroyImmediate(root);EditorSceneManager.ClosePreviewScene(scene);}
            }
            File.WriteAllText(ResourcesDir+"/ladder.txt",JsonUtility.ToJson(ladder,true));AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);foreach (var guid in AssetDatabase.FindAssets("", new[]{Art,ResourcesDir})) AssetDatabase.SaveAssetIfDirty(new GUID(guid));
            File.WriteAllText("docs/art-direction/fleet-v3/unity-import-validation.txt",report.ToString());
            Validate();
        }
        static Vector3 ReadVector(BinaryReader r)=>new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());
        [MenuItem("SeaSick/Art/Validate approved fleet V3")]
        public static void Validate()
        {
            var ladder=JsonUtility.FromJson<LadderData>(File.ReadAllText(ResourcesDir+"/ladder.txt"));int previous=0;float length=0,beam=0;var report=new StringBuilder();
            for(int stage=1;stage<=20;stage++)
            {
                var p=AssetDatabase.LoadAssetAtPath<FleetVisual>($"{ResourcesDir}/Ship{stage:00}.prefab");var n=ladder.nodes[stage-1];
                if(!p || p.gunTemplates.Length!=n.ports_per_side*2 || p.gunTemplates.Length<previous || (stage>=4&&p.gunTemplates.Length==0) || n.length<length || n.beam<beam)throw new Exception("Upgrade regression " + stage);
                foreach(var g in p.gunTemplates)if(!g.GetComponent<FleetGun>().barrel)throw new Exception("Missing barrel " + stage);
                foreach(var renderer in p.GetComponentsInChildren<MeshRenderer>())if(!renderer.sharedMaterial || ShaderUtil.ShaderHasError(renderer.sharedMaterial.shader))throw new Exception("Invalid fleet material " + stage);
                previous=p.gunTemplates.Length;length=n.length;beam=n.beam;report.AppendLine($"PASS {stage:00}: {previous} cannons; dimensions {length} x {beam}");
            }
            File.WriteAllText("docs/art-direction/fleet-v3/unity-prefab-validation.txt",report.ToString());Debug.Log("[FleetV3] PASS: 20 prefabs and monotonic progression verified.");
        }
    }

}
