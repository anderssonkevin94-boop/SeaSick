using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.Terrain
{
    /// Swaps each scenery cell between its full and its cheap mesh by how
    /// far it is from the camera, and drops it altogether where the ground
    /// under it is no longer streamed.
    ///
    /// The budget in the GDD is "about 200 triangles a tree within 150 m,
    /// about 100 beyond, nothing past the streamed terrain". A LODGroup
    /// cannot do that for a welded mesh -- its screen-height test is on the
    /// whole island, which is always big -- so the island is baked in CELLS
    /// and this decides per cell. Cells are also what lets the camera's
    /// frustum cull the back of a big island for free.
    ///
    /// This owns the on/off state of the scenery renderers entirely;
    /// `DistantSceneryCull` skips anything under a SceneryLod so the two
    /// never fight over a flag.
    public class SceneryLod : MonoBehaviour
    {
        List<SceneryWood.Cell> cells;
        float lod0Distance = 220f;
        float cullDistance = 1700f;
        float next, nextStreamerLook;
        Transform target;
        TerrainStreamer streamer;

        public void Configure(List<SceneryWood.Cell> cells, TerrainSettings terrain)
        {
            this.cells = cells;
            if (terrain != null)
            {
                lod0Distance = terrain.sceneryLod0Distance;
                cullDistance = terrain.viewRadius * terrain.chunkSize + 120f;
            }
            next = 0f;
        }

        void Update()
        {
            if (cells == null) return;
            if (Time.time < next) return;
            next = Time.time + 0.3f;
            // Judge from the STREAMER's target where there is one: that is
            // what the ground is built around, so scenery and terrain agree
            // -- and a look-sheet camera that borrows the streamer gets its
            // scenery too, instead of photographing bare ground.
            if (streamer == null && Time.time >= nextStreamerLook)
            {
                nextStreamerLook = Time.time + 5f;
                streamer = FindFirstObjectByType<TerrainStreamer>();
            }
            var t = streamer != null && streamer.target != null ? streamer.target : target;
            if (t == null && Camera.main != null) t = target = Camera.main.transform;
            if (t == null) return;
            var p = t.position;
            for (int i = 0; i < cells.Count; i++)
            {
                var cell = cells[i];
                float dx = p.x - cell.centre.x, dz = p.z - cell.centre.z;
                float d = Mathf.Sqrt(dx * dx + dz * dz) - cell.radius;
                bool shown = d <= cullDistance;
                bool near = d <= lod0Distance || cell.r1 == null;
                if (cell.r0 != null) cell.r0.enabled = shown && near;
                if (cell.r1 != null) cell.r1.enabled = shown && !near;
            }
        }

        /// For probes: how many cells draw at each level right now.
        public (int lod0, int lod1, int off) Count()
        {
            int a = 0, b = 0, c = 0;
            if (cells == null) return (0, 0, 0);
            foreach (var cell in cells)
            {
                if (cell.r0 != null && cell.r0.enabled) a++;
                else if (cell.r1 != null && cell.r1.enabled) b++;
                else c++;
            }
            return (a, b, c);
        }
    }
}
