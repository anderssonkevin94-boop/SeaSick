using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SeaSick.Ship.Modular
{
    public static class CoasterKitImport
    {
        [Serializable] class Kit { public Model[] models; }
        [Serializable] class Model { public string name; public Part[] parts; }
        [Serializable] class Part { public string name; public float[] vertices,normals,colors; public int[] triangles; }
        const string Folder="Assets/_Project/Resources/ShipModules/Meshes/FCoaster";
        public static void ConnectedStern()
        {
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/SternRaised.prefab");
            var root=UnityEngine.Object.Instantiate(source);root.name="SternRaisedConnected";
            foreach(var mf in root.GetComponentsInChildren<MeshFilter>())
            {
                if(mf.name!="Transom_Wall" && mf.name!="Continuous_Stern_Cap")continue;
                var src=mf.sharedMesh;var vs=src.vertices;var ns=src.normals;var cs=src.colors;var ts=src.triangles;
                var v=new System.Collections.Generic.List<Vector3>();var n=new System.Collections.Generic.List<Vector3>();var c=new System.Collections.Generic.List<Color>();var indices=new System.Collections.Generic.List<int>();
                for(int t=0;t<ts.Length;t+=3)
                {
                    var poly=new System.Collections.Generic.List<(Vector3 p,Vector3 n,Color c)>();
                    for(int j=0;j<3;j++){int a=ts[t+j],b=ts[t+(j+1)%3];bool inside=vs[a].z<=7.2f,other=vs[b].z<=7.2f;if(inside)poly.Add((vs[a],ns[a],cs[a]));if(inside!=other){float f=(7.2f-vs[a].z)/(vs[b].z-vs[a].z);poly.Add((Vector3.Lerp(vs[a],vs[b],f),Vector3.Lerp(ns[a],ns[b],f).normalized,Color.Lerp(cs[a],cs[b],f)));}}
                    for(int j=1;j+1<poly.Count;j++)foreach(int k in new[]{0,j,j+1}){indices.Add(v.Count);v.Add(poly[k].p);n.Add(poly[k].n);c.Add(poly[k].c);}
                }
                var mesh=new Mesh{name=mf.name+"Connected",indexFormat=IndexFormat.UInt32};mesh.SetVertices(v);mesh.SetNormals(n);mesh.SetColors(c);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();
                string path=Folder+"/SternConnected_"+mf.name+".asset";var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(old!=null){EditorUtility.CopySerialized(mesh,old);UnityEngine.Object.DestroyImmediate(mesh);mesh=old;}else AssetDatabase.CreateAsset(mesh,path);mf.sharedMesh=mesh;
            }
            PrefabUtility.SaveAsPrefabAsset(root,Folder+"/SternRaisedConnected.prefab");UnityEngine.Object.DestroyImmediate(root);AssetDatabase.SaveAssets();
        }
        [MenuItem("SeaSick/Ship/Import approved F coaster")]
        public static void Import()
        {
            Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
            var kit=JsonUtility.FromJson<Kit>(File.ReadAllText("art-staging/f-coaster-runtime/kit.json"));
            foreach(var model in kit.models)
            {
                var root=new GameObject(model.name);
                foreach(var part in model.parts)
                {
                    var mesh=new Mesh {name=part.name,indexFormat=IndexFormat.UInt32};
                    int n=part.vertices.Length/3; var v=new Vector3[n];var normals=new Vector3[n];var colors=new Color[n];
                    for(int i=0;i<n;i++) { v[i]=new Vector3(part.vertices[i*3],part.vertices[i*3+1],part.vertices[i*3+2])*2; normals[i]=new Vector3(part.normals[i*3],part.normals[i*3+1],part.normals[i*3+2]); colors[i]=new Color(part.colors[i*4],part.colors[i*4+1],part.colors[i*4+2],part.colors[i*4+3]); }
                    mesh.vertices=v;mesh.normals=normals;mesh.colors=colors;mesh.triangles=part.triangles;mesh.RecalculateBounds();mesh.Optimize();
                    string path=Folder+"/"+model.name+"_"+part.name+".asset";
                    var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if(old!=null){EditorUtility.CopySerialized(mesh,old);UnityEngine.Object.DestroyImmediate(mesh);mesh=old;} else AssetDatabase.CreateAsset(mesh,path);
                    var go=new GameObject(part.name);go.transform.SetParent(root.transform,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
                    go.AddComponent<MeshRenderer>().sharedMaterial=SeaSick.Terrain.IslandScenery.SceneryMaterial();
                }
                PrefabUtility.SaveAsPrefabAsset(root,Folder+"/"+model.name+".prefab"); UnityEngine.Object.DestroyImmediate(root);
            }
            var carrier=new GameObject("IntegratedCarrier");PrefabUtility.SaveAsPrefabAsset(carrier,Folder+"/IntegratedCarrier.prefab");UnityEngine.Object.DestroyImmediate(carrier);
            ConnectedStern();AssetDatabase.SaveAssets();AssetDatabase.Refresh();Debug.Log("F_COASTER_IMPORT_OK");
        }
    }
}
