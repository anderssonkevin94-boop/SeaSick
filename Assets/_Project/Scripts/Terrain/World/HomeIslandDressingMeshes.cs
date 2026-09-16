using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.Terrain
{
    /// Owns runtime mesh and shared material clones for home-island palette adjustments.
    public sealed class HomeIslandDressingMeshes : MonoBehaviour
    {
        readonly List<Mesh> owned = new List<Mesh>();
        readonly List<Material> materials = new List<Material>();
        public void Own(Mesh mesh) { if (mesh != null) owned.Add(mesh); }
        public void Own(Material material) { if (material != null) materials.Add(material); }
        void OnDestroy()
        {
            foreach (var mesh in owned) if (mesh != null) Destroy(mesh);
            owned.Clear();
            foreach (var material in materials) if (material != null) Destroy(material);
            materials.Clear();
        }
    }
}
