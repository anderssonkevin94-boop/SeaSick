using UnityEngine;
namespace SeaSick.Combat
{
    // Snapshot the rendered body, not its scripts or inventory. Dead villagers
    // leave the roster immediately; this temporary visual has no gameplay owner.
    public sealed class VillagerCorpse : MonoBehaviour
    {
        float age;
        public static void Leave(GameObject body) {
            var root=new GameObject("Fallen villager");
            root.transform.SetPositionAndRotation(body.transform.position,body.transform.rotation);
            root.AddComponent<VillagerCorpse>();
            foreach(var r in body.GetComponentsInChildren<Renderer>()) {
                if(!r.enabled || !r.gameObject.activeInHierarchy) continue;
                Mesh mesh=null;
                if(r is SkinnedMeshRenderer skin) { mesh=new Mesh(); skin.BakeMesh(mesh); }
                else if(r.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh!=null) mesh=Instantiate(filter.sharedMesh);
                if(mesh==null) continue;
                var part=new GameObject("Body"); part.transform.SetPositionAndRotation(r.transform.position,r.transform.rotation);
                part.transform.localScale=r.transform.lossyScale; part.transform.SetParent(root.transform,true);
                part.AddComponent<MeshFilter>().sharedMesh=mesh;
                part.AddComponent<MeshRenderer>().sharedMaterials=r.sharedMaterials;
            }
        }
        void Update() { age+=Time.deltaTime; if(age>28f) transform.position-=Vector3.up*Time.deltaTime*.3f; if(age>=30f) Destroy(gameObject); }
        void OnDestroy() { foreach(var f in GetComponentsInChildren<MeshFilter>()) if(f.sharedMesh!=null) Destroy(f.sharedMesh); }
    }
}
