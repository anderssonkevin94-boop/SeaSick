using System.Collections.Generic;
using UnityEngine;
using SeaSick.World;

namespace SeaSick.Terrain
{
    /// **The loose rocks an island was dressed with, indexed (2026-09-27).**
    /// Kevin, phone: *"there are so many rocks on the island but I guess they
    /// don't qualify as a stone resource, please change so they do."*
    ///
    /// Same shape as `SceneryWood` and `SceneryCrops`, for the same reason:
    /// the boulders are welded into the scenery cells so an island of them
    /// costs one draw call a cell, and a GameObject per rock on every island
    /// in the sea is the thing that shape exists to avoid. So the bake
    /// records, for every LOOSE rock it stamps, its ground point, its size
    /// and the exact vertex runs it owns in both LOD meshes; hiding one
    /// collapses those vertices onto its ground point (every triangle it owns
    /// turns degenerate, the index buffers are untouched), restoring puts the
    /// snapshot back.
    ///
    /// **What counts as loose** (decided in `IslandScenery` at stamp time):
    /// the lone inland boulders, the scree at the foot of an outcrop, dry
    /// tideline boulders, and Astra's `Boulder_*` accents (the stones beside
    /// the trees and on the edge of an outcrop, Island_2's profile). **What
    /// stays landform**: cliff shards and every outcrop/headland/sea stack
    /// built from them, `Cliff_*` accents (broken slab, talus), washed beach
    /// stones, rocks standing in the surf, and anything over
    /// `MaxLooseRadius` across. Those are never indexed.
    ///
    /// **Nothing here is an economy.** Which rocks are gone is the camp's
    /// ledger (`GatherSync`, nearest the camp first); a camp stands a
    /// `ResourceNode` on each of its reachable rocks (`SeaSick.World
    /// .SceneryStone`) and that node's `StoneDeposit` calls `SetHidden`.
    ///
    /// **Cost.** A hide writes the cached vertex array and marks the cell;
    /// the mesh upload happens ONCE per dirty cell in `LateUpdate`, however
    /// many rocks in it changed that frame (a reload re-hiding fifty rocks
    /// is one upload per cell, not fifty). `LastFlushMs` / `MaxFlushMs` are
    /// the measured cost, for `SceneryStoneCheck`.
    public class SceneryRocks : MonoBehaviour
    {
        public enum Source : byte { Lone, Scree, Shore, Accent }

        public struct Rock
        {
            /// Ground point the rock sits on (its collapse point).
            public Vector3 at;
            public int cell;
            public int vertStart, vertCount;       // LOD0 run
            public int lod1Start, lod1Count;       // LOD1 run (0 if none)
            /// Widest horizontal reach from `at`, metres.
            public float radius;
            /// Top above the ground point, metres.
            public float height;
            public Source source;
        }

        /// A stamp wider than this (radius, m) is a landmark, not a stone a
        /// man breaks up and carries home.
        public const float MaxLooseRadius = 3f;

        /// **Stone a rock is worth, by size (PROVISIONAL, GDD 2026-09-27):**
        /// small 2, medium 4, large 8. Size is the rock's diameter across the
        /// ground: under 1.5 m small, under 2.8 m medium, else large.
        public static int UnitsOf(in Rock r)
        {
            float d = r.radius * 2f;
            // Yields from the tuning asset (`rockSmall/Medium/Large`), 2026-09-27.
            return SeaSick.World.Economy.EconomyTuning.RockUnits(d < 1.5f ? 0 : d < 2.8f ? 1 : 2);
        }

        Rock[] rocks = System.Array.Empty<Rock>();
        bool[] hidden = System.Array.Empty<bool>();
        Vector3[][] keep0, keep1;
        List<SceneryWood.Cell> cells;
        readonly HashSet<int> dirty = new HashSet<int>();

        public int Count => rocks.Length;
        public Rock RockAt(int i) => rocks[i];
        public bool IsHidden(int i) => i >= 0 && i < hidden.Length && hidden[i];
        public int HiddenCount { get { int n = 0; foreach (bool h in hidden) if (h) n++; return n; } }

        /// Rock stamps the bake left as landform (cliff shards, sea stacks,
        /// surf and oversize boulders, cliff accents). An instrument.
        public int LandformCount { get; private set; }

        /// Set by `SceneryStone` once this island's camp has stood its nodes
        /// on these rocks. Not saved: a reloaded island is a fresh bake.
        public bool Materialized { get; set; }

        /// Milliseconds the last / worst `LateUpdate` mesh upload took.
        public float LastFlushMs { get; private set; }
        public float MaxFlushMs { get; private set; }
        public int Flushes { get; private set; }

        public void Configure(List<SceneryWood.Cell> cells, List<Rock> index, int landform)
        {
            this.cells = cells;
            rocks = index.ToArray();
            hidden = new bool[rocks.Length];
            keep0 = new Vector3[rocks.Length][];
            keep1 = new Vector3[rocks.Length][];
            LandformCount = landform;
        }

        /// The rocks on this island, if its scenery indexed any.
        public static SceneryRocks On(Component isle)
            => isle != null ? isle.GetComponentInChildren<SceneryRocks>() : null;

        /// Hide or restore rock `i`. Idempotent; the upload is deferred to
        /// `LateUpdate` (see the class note).
        public void SetHidden(int i, bool hide)
        {
            if (i < 0 || i >= rocks.Length || hidden[i] == hide || cells == null) return;
            var r = rocks[i];
            if (r.cell < 0 || r.cell >= cells.Count) return;
            var c = cells[r.cell];
            if (c.v0 == null && c.lod0 != null) c.v0 = c.lod0.vertices;
            if (c.v1 == null && c.lod1 != null) c.v1 = c.lod1.vertices;

            if (hide)
            {
                keep0[i] = Collapse(c.v0, r.vertStart, r.vertCount, r.at);
                keep1[i] = Collapse(c.v1, r.lod1Start, r.lod1Count, r.at);
            }
            else
            {
                Restore(c.v0, r.vertStart, keep0[i]);
                Restore(c.v1, r.lod1Start, keep1[i]);
                keep0[i] = keep1[i] = null;
            }
            hidden[i] = hide;
            dirty.Add(r.cell);
        }

        static Vector3[] Collapse(Vector3[] v, int start, int count, Vector3 to)
        {
            if (v == null || count <= 0 || start < 0 || start >= v.Length) return null;
            int n = Mathf.Min(count, v.Length - start);
            var snap = new Vector3[n];
            System.Array.Copy(v, start, snap, 0, n);
            for (int k = 0; k < n; k++) v[start + k] = to;
            return snap;
        }

        static void Restore(Vector3[] v, int start, Vector3[] snap)
        {
            if (v == null || snap == null) return;
            for (int k = 0; k < snap.Length && start + k < v.Length; k++) v[start + k] = snap[k];
        }

        void LateUpdate()
        {
            if (dirty.Count == 0 || cells == null) return;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            foreach (int i in dirty)
            {
                var c = cells[i];
                if (c.lod0 != null && c.v0 != null) c.lod0.SetVertices(c.v0);
                if (c.lod1 != null && c.v1 != null) c.lod1.SetVertices(c.v1);
            }
            dirty.Clear();
            watch.Stop();
            LastFlushMs = (float)watch.Elapsed.TotalMilliseconds;
            MaxFlushMs = Mathf.Max(MaxFlushMs, LastFlushMs);
            Flushes++;
        }

        /// For the check: is rock `i`'s LOD0 run collapsed IN THE MESH (the
        /// shipped artefact, not the flag)? Reads the mesh, so it is slow.
        public bool CollapsedInMesh(int i)
        {
            if (i < 0 || i >= rocks.Length || cells == null) return false;
            var r = rocks[i];
            var m = cells[r.cell].lod0;
            if (m == null || r.vertCount < 2) return false;
            var v = m.vertices;
            for (int k = r.vertStart + 1; k < r.vertStart + r.vertCount && k < v.Length; k++)
                if ((v[k] - v[r.vertStart]).sqrMagnitude > 1e-6f) return false;
            return true;
        }

        public void ResetFlushStats() { MaxFlushMs = 0f; LastFlushMs = 0f; Flushes = 0; }
    }
}
