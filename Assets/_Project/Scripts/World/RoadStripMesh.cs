using UnityEngine;

namespace SeaSick.World
{
    /// Frees a `RoadStrip` ribbon's own mesh with its object (a siting ghost
    /// is rebuilt as the thumb drags; without this each one leaked a mesh).
    public class RoadStripMesh : MonoBehaviour
    {
        void OnDestroy()
        {
            var mf = GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
        }
    }
}
