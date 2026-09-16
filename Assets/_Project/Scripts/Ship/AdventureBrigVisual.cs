using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace SeaSick.Ship
{
    /// Approved brig artwork; physics and bay ownership remain with Shipyard.
    public sealed class AdventureBrigVisual : MonoBehaviour
    {
        [Serializable] class Group { public string name; public Vector3[] vertices,normals; public Color[] colors; public int[] triangles; }
        [Serializable] class Data { public Group[] groups; public float[] stations,deckHeights; public Vector3[] gunSockets; }
        Data data;
        readonly List<Mesh> meshes=new List<Mesh>();
        Material material;
        public static AdventureBrigVisual Build(Transform parent)
        {
            var source=Resources.Load<TextAsset>("Ships/AdventureBrig");
            if(!source) return null;
            var root=new GameObject("HullVisual");root.transform.SetParent(parent,false);
            var art=root.AddComponent<AdventureBrigVisual>();
            art.data=JsonUtility.FromJson<Data>(source.text);
            art.material=new Material(Shader.Find("SeaSick/Terrain Vertex Color")){name="Adventure brig painted timber"};
            art.material.SetFloat("_DetailStrength",0);art.material.SetFloat("_NormalStrength",0);art.material.SetFloat("_StriationStrength",0);
            art.material.SetFloat("_GraphicLight",.75f);
            foreach(var group in art.data.groups)
            {
                var mesh=new Mesh {name=group.name,indexFormat=IndexFormat.UInt32};
                mesh.vertices=group.vertices;mesh.normals=group.normals;mesh.colors=group.colors;mesh.triangles=group.triangles;mesh.RecalculateBounds();
                art.meshes.Add(mesh);
                var go=new GameObject(group.name);go.transform.SetParent(root.transform,false);
                go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=art.material;
            }
            return art;
        }
        public float DeckHeight(float station)
        {
            int best=0;for(int i=1;i<data.stations.Length;i++) if(Mathf.Abs(station-data.stations[i])<Mathf.Abs(station-data.stations[best]))best=i;
            return data.deckHeights[best];
        }
        public Vector3 GunSocket(int bay) => data.gunSockets[bay];
        void OnDestroy(){foreach(var mesh in meshes) if(mesh)Destroy(mesh);if(material)Destroy(material);}
    }
}
