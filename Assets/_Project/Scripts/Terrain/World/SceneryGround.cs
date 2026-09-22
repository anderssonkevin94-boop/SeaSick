using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.Terrain
{
    /// Harvest-free twin of `SceneryWood`, for the ground layer: fern,
    /// grass, scrub sprigs and driftwood, welded into the same cell meshes
    /// the wood is.
    ///
    /// Kevin, 2026-09-22: *"the foliage on the ground is a bit too big and
    /// invasive. It grows through buildings and makes the camp seem busy."*
    /// The size fix is in `WorldScale`; this is the other half -- a building
    /// raised over a patch collapses it, the same way felling a tree
    /// collapses a `SceneryWood.Tree`: every vertex the patch owns snaps to
    /// its own base, so its triangles draw nothing without touching the
    /// island's vertex/index buffers.
    ///
    /// One of these lives on every island's "Scenery" object next to its
    /// `SceneryWood` (only when the island actually has ground foliage --
    /// see `IslandScenery.Build`). `ClearFootprintNear` is the entry point
    /// a builder calls without needing to know which island it is standing
    /// on: every live instance registers itself and is asked in turn, and a
    /// patch nowhere near the given point is a cheap rejection.
    public class SceneryGround : MonoBehaviour
    {
        /// One stamped patch: fern, grass, scrub or driftwood, indexed the
        /// same way a `SceneryWood.Tree` is.
        public struct Patch
        {
            public Vector3 baseAt;
            public int cell;
            public int vertStart, vertCount;   // in the cell's LOD0 mesh
            public int lod1Start, lod1Count;   // in the cell's LOD1 mesh (0 if none)
            public bool cleared;
        }

        Patch[] patches;
        List<SceneryWood.Cell> cells;

        static readonly List<SceneryGround> Active = new List<SceneryGround>();

        void OnEnable() { if (!Active.Contains(this)) Active.Add(this); }
        void OnDisable() { Active.Remove(this); }

        public int PatchCount => patches != null ? patches.Length : 0;

        /// Same cell list `SceneryWood`/`SceneryCrops` were configured
        /// with, so the cached vertex arrays are shared rather than each
        /// component fetching its own copy of a mesh the others already
        /// have open.
        public void Configure(List<SceneryWood.Cell> cells, List<Patch> index)
        {
            this.cells = cells;
            patches = index.ToArray();
            foreach (var c in cells)
            {
                if (c.v0 == null && c.lod0 != null) c.v0 = c.lod0.vertices;
                if (c.v1 == null && c.lod1 != null) c.v1 = c.lod1.vertices;
            }
        }

        /// Drop the patch onto its own base in both meshes -- see
        /// `SceneryWood.Fell`, which this mirrors exactly.
        void Collapse(int i)
        {
            if (patches == null || i < 0 || i >= patches.Length || patches[i].cleared) return;
            var p = patches[i];
            var c = cells[p.cell];
            if (c.v0 != null)
            {
                for (int v = p.vertStart; v < p.vertStart + p.vertCount && v < c.v0.Length; v++)
                    c.v0[v] = p.baseAt;
                c.lod0.SetVertices(c.v0);
            }
            if (c.v1 != null && p.lod1Count > 0)
            {
                for (int v = p.lod1Start; v < p.lod1Start + p.lod1Count && v < c.v1.Length; v++)
                    c.v1[v] = p.baseAt;
                c.lod1.SetVertices(c.v1);
            }
            patches[i].cleared = true;
        }

        /// Collapse every patch within a disc. Cheap, general-purpose --
        /// `ClearFootprint` below is the one a building actually wants.
        public int ClearWithin(Vector3 at, float radius)
        {
            if (patches == null) return 0;
            float r2 = radius * radius;
            int n = 0;
            for (int i = 0; i < patches.Length; i++)
            {
                if (patches[i].cleared) continue;
                Vector3 d = patches[i].baseAt - at;
                d.y = 0f;
                if (d.sqrMagnitude > r2) continue;
                Collapse(i);
                n++;
            }
            return n;
        }

        /// Collapse every patch under a building's rotated footprint rect,
        /// plus a flat margin on every side.
        ///
        /// `footprint` is `BuildPlan.footprint`: x along the ridge, z
        /// across it -- the same axes `BuildingFactory`/`BuildSite` extrude
        /// the walls and stakes on, so a patch that would poke through a
        /// wall is exactly the one this rejects.
        public int ClearFootprint(Vector3 at, Quaternion facing, Vector2 footprint, float margin)
        {
            if (patches == null) return 0;
            float halfLen = footprint.x * 0.5f + margin;
            float halfWid = footprint.y * 0.5f + margin;
            // Cheap first-pass reject on the circle that bounds the rect,
            // before paying for the per-patch rotation below.
            float boundR = Mathf.Sqrt(halfLen * halfLen + halfWid * halfWid);
            float boundR2 = boundR * boundR;
            var inv = Quaternion.Inverse(facing);
            int n = 0;
            for (int i = 0; i < patches.Length; i++)
            {
                if (patches[i].cleared) continue;
                Vector3 d = patches[i].baseAt - at;
                d.y = 0f;
                if (d.sqrMagnitude > boundR2) continue;
                Vector3 local = inv * d;
                if (Mathf.Abs(local.x) > halfLen || Mathf.Abs(local.z) > halfWid) continue;
                Collapse(i);
                n++;
            }
            return n;
        }

        /// The one-line entry point a builder calls: every `SceneryGround`
        /// currently enabled gets asked, so the caller does not need to
        /// know or look up which island's welded mesh a building landed on.
        /// Islands are few (a couple of dozen at most) and a miss is a
        /// bounding-circle rejection per patch, so this is cheap even
        /// called once per building raised.
        public static int ClearFootprintNear(Vector3 at, Quaternion facing, Vector2 footprint, float margin)
        {
            int n = 0;
            for (int i = 0; i < Active.Count; i++)
                if (Active[i] != null) n += Active[i].ClearFootprint(at, facing, footprint, margin);
            return n;
        }
    }
}
