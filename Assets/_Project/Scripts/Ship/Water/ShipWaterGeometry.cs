using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace SeaSick.Ship
{
    // Two reusable buffers/draws, shared geometry and no per-particle GameObjects.
    internal sealed class ShipWaterGeometry
    {
        internal sealed class Shape
        {
            public Vector3[] vertices, normals; public Color[] colors; public int[] triangles;
            public Shape(Mesh mesh) {vertices=mesh.vertices;normals=mesh.normals;colors=mesh.colors;triangles=mesh.triangles;}
        }
        readonly List<Vector3> vertices=new List<Vector3>(12000),normals=new List<Vector3>(12000);
        readonly List<Color> colors=new List<Color>(12000);
        readonly List<int> triangles=new List<int>(24000);
        public readonly GameObject root; readonly Mesh mesh; readonly Material material;
        public int TriangleCount=>triangles.Count/3;
        public ShipWaterGeometry(string name,Shader shader,bool depthWrite)
        {
            root=new GameObject(name); mesh=new Mesh {name=name,indexFormat=IndexFormat.UInt32};mesh.MarkDynamic();
            root.AddComponent<MeshFilter>().sharedMesh=mesh;
            var r=root.AddComponent<MeshRenderer>();material=new Material(shader){name=name};material.SetFloat("_ZWrite",depthWrite?1:0);
            r.sharedMaterial=material;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;
            r.lightProbeUsage=LightProbeUsage.Off;r.reflectionProbeUsage=ReflectionProbeUsage.Off;
        }
        public void Clear(){vertices.Clear();normals.Clear();colors.Clear();triangles.Clear();}
        public void ShapeAt(Shape s,Vector3 at,Quaternion rotation,Vector3 scale,Color tint,bool painted=false)
        {
            int offset=vertices.Count;
            for(int i=0;i<s.vertices.Length;i++) {
                vertices.Add(at+rotation*Vector3.Scale(s.vertices[i],scale));
                Vector3 n=i<s.normals.Length?s.normals[i]:Vector3.up;
                n=new Vector3(n.x/Mathf.Max(.001f,scale.x),n.y/Mathf.Max(.001f,scale.y),n.z/Mathf.Max(.001f,scale.z));
                normals.Add(rotation*n.normalized);
                Color c=painted&&i<s.colors.Length?s.colors[i]:tint;c.a=tint.a;colors.Add(c);
            }
            foreach(int index in s.triangles)triangles.Add(offset+index);
        }
        public int Vertex(Vector3 p,Vector3 n,Color c){int i=vertices.Count;vertices.Add(p);normals.Add(n);colors.Add(c);return i;}
        public void Quad(int a,int b,int c,int d){triangles.Add(a);triangles.Add(b);triangles.Add(c);triangles.Add(a);triangles.Add(c);triangles.Add(d);}
        public void Upload(bool visible)
        {
            root.SetActive(visible&&vertices.Count>0);if(!root.activeSelf)return;
            mesh.Clear(false);mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetColors(colors);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();
        }
        public void Dispose(){if(root)Object.Destroy(root);if(mesh)Object.Destroy(mesh);if(material)Object.Destroy(material);}
    }
}
