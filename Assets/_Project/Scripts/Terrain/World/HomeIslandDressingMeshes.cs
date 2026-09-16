using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.Terrain
{
    /// Owns runtime mesh clones made for home-island palette adjustments.
    public sealed class HomeIslandDressingMeshes : MonoBehaviour
    {
        readonly List<Mesh> owned = new List<Mesh>();
        public void Own(Mesh mesh) { if (mesh != null) owned.Add(mesh); }
        void OnDestroy()
        {
            foreach (var mesh in owned) if (mesh != null) Destroy(mesh);
            owned.Clear();
        }
    }
}
