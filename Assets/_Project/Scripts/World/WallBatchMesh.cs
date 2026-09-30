using UnityEngine;

namespace SeaSick.World
{
    /// **Owns one merged palisade mesh (2026-09-30)** -- `WallVisual`'s
    /// per-material batch of a segment state or of the chain's posts. The
    /// mesh is made at runtime, so nothing else frees it: it goes when this
    /// object does (a redraw, a tear-down, an island unloading).
    public sealed class WallBatchMesh : MonoBehaviour
    {
        internal Mesh mesh;

        void OnDestroy()
        {
            if (mesh != null) WallVisual.Kill(mesh);
        }
    }
}
