using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SeaSick.Ship.Modular
{
    /// Keep named authoring meshes for navigation and inspection, while drawing
    /// each rigid section in a few material batches on a moving ship.
    public static class CoasterRenderBatch
    {
        public static void Build(Transform root)
        {
            var groups=new Dictionary<Material,List<CombineInstance>>();
            var sources=new List<MeshRenderer>();
            foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>())
            {
                var filter=renderer.GetComponent<MeshFilter>();
                if(!renderer.enabled||filter==null||filter.sharedMesh==null||!filter.sharedMesh.isReadable)continue;
                var materials=renderer.sharedMaterials;var mesh=filter.sharedMesh;
                for(int i=0;i<mesh.subMeshCount;i++)
                {
                    var material=materials[Mathf.Min(i,materials.Length-1)];if(material==null)continue;
                    if(!groups.TryGetValue(material,out var list))groups[material]=list=new List<CombineInstance>();
                    list.Add(new CombineInstance{mesh=mesh,subMeshIndex=i,transform=root.worldToLocalMatrix*filter.transform.localToWorldMatrix});
                }
                sources.Add(renderer);
            }
            foreach(var pair in groups)
            {
                var mesh=new Mesh{name="Coaster rigid section batch",indexFormat=IndexFormat.UInt32};
                mesh.CombineMeshes(pair.Value.ToArray(),true,true,false);mesh.RecalculateBounds();
                var go=new GameObject("Section draw batch");go.transform.SetParent(root,false);
                go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=pair.Key;
                go.AddComponent<CoasterOwnedMesh>().mesh=mesh;
            }
            foreach(var source in sources)source.enabled=false;
        }
    }
}
