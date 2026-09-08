using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.Terrain
{
    /// Trees, boulders and cliffs, in bulk, so the eye can tell how big an
    /// island is and so an island reads as a WOOD rather than as a lawn with
    /// trees on it.
    ///
    /// The land had almost nothing on it: props are RESOURCE NODES, capped at
    /// 26 an island, and 26 trees on a 400 m island is a golf course. That is
    /// most of why the islands read as small no matter how tall they were
    /// made -- scale is not something a landform can state on its own. The eye
    /// works it out from things it already knows the size of, and there was
    /// nothing out there to know.
    ///
    /// Everything an island gets is baked into welded meshes with vertex
    /// colours -- one renderer per 96 m CELL and level of detail, a few dozen
    /// draw calls for the biggest island. Six hundred GameObjects each
    /// carrying a trunk and two canopy spheres, which is what the prop
    /// factory builds, would be eighteen hundred renderers per island.
    ///
    /// **The trees are the Blender kit now** (`SceneryKit`, built by
    /// `tools/blender/seasick_style.py`): a spruce, a broadleaf and a palm at
    /// two levels of detail, unit shards for boulders and cliffs. They are
    /// stamped into the buffers as vertex data, so the cost model is
    /// unchanged -- it is still one mesh per cell -- and the island gets the
    /// "carved" style the GDD records instead of a prism with two cones on
    /// it. The cones are kept as the fallback for a project without the FBX.
    public static class IslandScenery
    {
        /// Tree height comes off the charter in WorldScale, not from a
        /// number typed here -- which is how it ended up at 5.2-8.6 m and
        /// then at 11-26 m, each time chosen against whatever was on screen
        /// rather than against the crew, the buildings and the giants it has
        /// to be seen beside. These must NOT scale with the island.
        static float TreeMinH => SeaSick.World.WorldScale.TreeMin;
        static float TreeMaxH => SeaSick.World.WorldScale.TreeMax;

        /// Bias on the height roll. Below 1 puts most trees in the upper half
        /// of the range, which is what a stand of mature conifers looks like;
        /// an even spread reads as a nursery.
        const float HeightBias = 0.75f;

        /// Cell edge, metres. Small enough that the near cells of a big
        /// island can be at full detail while the far side is cheap, big
        /// enough that a 700 m island is a hundred renderers and not a
        /// thousand.
        const float CellSize = 96f;

        /// The tree cap was 3000 when every tree was one draw's worth of
        /// eighteen triangles in one mesh. With per-cell detail (SceneryLod)
        /// the far side of an island costs 80 a tree and the cells behind
        /// the camera cost nothing, so the cap can carry the density the
        /// reference boards have without the frame paying for all of it.
        // A guard, not the operating point -- see the density note in
        // the scatter. Raised from 4500 when the wood was allowed to
        // close: at 4500 the r380 crag hit the cap exactly, and the cap
        // EXITS the scan, so the last stretch of the walk came back
        // bare with a visible edge down the island.
        const int MaxTrees = 6000, MaxRocks = 2500;    // a cliff shard is 20 triangles

        /// Every tree yaws to the island's wind, then jitters +/- 20 deg: the
        /// kit's trees LEAN downwind (+X in the template), and thirteen leans
        /// in thirteen directions read as thirteen accidents.
        const float Wind = 0.35f;

        class CellBuild
        {
            public readonly List<Vector3> v0 = new List<Vector3>(), n0 = new List<Vector3>();
            public readonly List<Color32> c0 = new List<Color32>();
            public readonly List<int> t0 = new List<int>();
            public readonly List<Vector3> v1 = new List<Vector3>(), n1 = new List<Vector3>();
            public readonly List<Color32> c1 = new List<Color32>();
            public readonly List<int> t1 = new List<int>();
            public Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            public Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            public int index;

            public void Grow(Vector3 p, float r, float h)
            {
                min = Vector3.Min(min, new Vector3(p.x - r, p.y, p.z - r));
                max = Vector3.Max(max, new Vector3(p.x + r, p.y + h, p.z + r));
            }
        }

        /// `keepOut` is where nothing may stand -- the village clearing.
        ///
        /// **It suppresses the geometry, never the draws.** This walks ONE
        /// `System.Random` through the grid in order, so a `continue` here
        /// removes rolls and reshuffles every tree after it: nudge a clearing
        /// five metres and the whole wood moves. Every candidate therefore
        /// takes exactly the same numbers out of the stream whether it is
        /// built or not, and the trees outside a clearing stand exactly where
        /// they stood before there was one.
        public static GameObject Build(Transform parent, Vector3 centre, float meanR,
            System.Func<float, float, float> height, TerrainSettings terrain,
            System.Func<float, float> radiusAt, int seed, TerrainParams prm,
            SeaSick.World.Island isle = null,
            System.Func<float, float, bool> keepOut = null)
        {
            var rng = new System.Random(seed);
            bool kit = SceneryKit.Available;
            var index = new List<SceneryWood.Tree>();
            var cellMap = new Dictionary<long, CellBuild>();
            var cellList = new List<CellBuild>();

            CellBuild CellFor(float wx, float wz)
            {
                int cx = kit ? Mathf.FloorToInt((wx - centre.x) / CellSize) : 0;
                int cz = kit ? Mathf.FloorToInt((wz - centre.z) / CellSize) : 0;
                long key = ((long)cx << 32) ^ (uint)cz;
                if (!cellMap.TryGetValue(key, out var cb))
                {
                    cb = new CellBuild { index = cellList.Count };
                    cellMap[key] = cb;
                    cellList.Add(cb);
                }
                return cb;
            }

            // Highest ground on the island, so exposure is a fraction of
            // THIS island rather than a world constant.
            float peak = 0f;
            for (int a = 0; a < 32; a++)
            {
                float ang = a / 32f * Mathf.PI * 2f;
                for (int k = 1; k <= 4; k++)
                {
                    float d = meanR * k / 5f;
                    peak = Mathf.Max(peak, height(centre.x + Mathf.Sin(ang) * d,
                                                  centre.z + Mathf.Cos(ang) * d));
                }
            }
            float sand = terrain != null ? terrain.sandHeight : 3.2f;

            // **There is no tree line.** Cover is a function of SLOPE and of
            // what the ground is made of; altitude barely enters into it.
            // Only the very top thins, for exposure, and that is a summit
            // rather than a line. The two island-scale character fields are
            // read ONCE, at the centre: they are slow enough that one
            // landmass sits inside one value of each.
            var c2 = new Unity.Mathematics.float2(centre.x, centre.z);
            float verdancy = TerrainHeight.Verdancy01(c2, prm);
            float rockiness = TerrainHeight.Rock01(c2, prm);

            // North is cold, south is hot (GDD §5): south of `palmLatitude`
            // the wood turns to palms over a band, so an island's kind is a
            // fact about where it is and not a roll.
            float tropical = 0f;
            if (terrain != null)
                tropical = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(
                    terrain.palmLatitude + terrain.palmBand, terrain.palmLatitude - terrain.palmBand, centre.z));

            // The scatter box has to cover the island's REACH, not its mean
            // radius: islands are lobed, and a headland past the mean came
            // back bare.
            float maxR = meanR;
            if (radiusAt != null)
                for (int a = 0; a < 48; a++)
                    maxR = Mathf.Max(maxR, radiusAt(a / 48f * Mathf.PI * 2f));
            maxR = Mathf.Min(maxR, meanR * 3f);   // guard against a bad sector

            // Spacing is read against tree HEIGHT: the canopy of a 13 m
            // spruce is about 4.5 m across, so 5 m puts the crowns in contact
            // and the stand closes up into woodland, which is what the
            // reference boards are made of. The cones needed 7-12 m only
            // because at 5 m they were an obvious grid.
            float step = kit
                ? Mathf.Clamp(terrain != null ? terrain.treeSpacing : 5f, 3f, 12f)
                : Mathf.Clamp(meanR * 0.02f, 7f, 12f);
            int trees = 0, rocks = 0, cliffs = 0, formations = 0;
            // Formations are rationed by island size, so a big rocky
            // island gets outcrops and a sandbank gets one at most.
            int maxFormations = 8 + Mathf.RoundToInt(maxR * 0.35f);

            /// Below this peak an island has no exposed summit to thin.
            const float ExposedAbove = 60f;

            // Thin UNIFORMLY to the budget rather than filling until it runs
            // out: a hard cap dresses one band of the island and leaves the
            // rest bare.
            //
            // But thin only to stay inside the guard, never as the normal
            // operating point -- and DENSITY, not count, is what the eye
            // reads. A budget expressed as a count divided by an island's
            // AREA, so the bigger the island the thinner its wood: measured
            // across one world, the r386 crag came back at 22 trees a
            // hectare where an r48 islet had 129 on the same settings, and
            // the big rocky islands -- the only ones with ridges worth
            // photographing -- were the barest things in the sea. The
            // reference board is a wood at about 230 trees a hectare
            // whatever the island's size.
            //
            // Two arithmetic faults went with it. Verdancy was counted
            // THREE times (in the budget, in the expected acceptance, and
            // again in `chance`), so a half-green island was thinned as if
            // it were a quarter-green one. And the assumed acceptance was a
            // CONSTANT, which is the fault that survived two attempts to
            // pick a better constant: what the scan accepts is a property
            // of the island, not of the world. Measured, it ran from 0.11
            // on a lobed sandbank to 0.35 on a compact wooded dome and 0.26
            // on this crag, so 0.20 and then 0.24 each walked some island
            // into the cap -- and the cap does not just stop placing, it
            // ENDS THE SCAN, leaving a bare band with a visible straight
            // edge down the far side.
            //
            // So measure it. A few hundred samples of the island's own
            // accept test cost about 1 % of the walk that follows and give
            // this island's number, which is the only one that can size its
            // own thinning.
            // The accept test, in ONE place. The pre-pass that sizes the
            // thinning and the walk that does the placing have to be the
            // same function, or the estimate describes a different island
            // than the one being built. Returns the chance BEFORE thinning;
            // zero means the candidate was rejected outright.
            float ChanceAt(float wx, float wz, out float h, out float sx, out float sz, out float proud)
            {
                h = 0f; sx = 0f; sz = 0f; proud = 0f;
                float ang = Mathf.Atan2(wx - centre.x, wz - centre.z);
                float dist = Mathf.Sqrt((wx - centre.x) * (wx - centre.x) + (wz - centre.z) * (wz - centre.z));
                if (radiusAt != null && dist > radiusAt(ang) * 0.94f) return 0f;

                h = height(wx, wz);
                if (h < sand + 1.2f) return 0f;             // not on the beach
                sx = (height(wx + 3f, wz) - height(wx - 3f, wz)) / 6f;
                sz = (height(wx, wz + 3f) - height(wx, wz - 3f)) / 6f;
                float slope = Mathf.Sqrt(sx * sx + sz * sz);

                // Rock that has broken through the soil is stone, and
                // nothing roots in it.
                proud = TerrainHeight.RockBreak(
                    new Unity.Mathematics.float2(wx, wz), rockiness, prm);

                float slopeTerm = 1f - Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(prm.vegSlopeSoft, prm.vegSlopeHard, slope));
                float rockTerm = proud > 0.4f ? 1f - prm.vegRockSuppress : 1f;
                // Exposure is a MOUNTAIN phenomenon and has to be gated on
                // the island being one: keyed to `peak` alone it put a 5 m
                // sandbank's exposure band over the whole island.
                float exposure = peak < ExposedAbove ? 1f
                    : 1f - Mathf.SmoothStep(0f, 1f,
                        Mathf.InverseLerp(peak * 0.80f, peak * 1.02f, h));
                return verdancy * slopeTerm * rockTerm * exposure;
            }

            float cells = Mathf.PI * maxR * maxR / (step * step);
            float accept = 0f;
            {
                const int Samples = 600;
                var srng = new System.Random(seed * 977 + 13);
                for (int i = 0; i < Samples; i++)
                {
                    // Uniform over the disc: sqrt, or every sample crowds
                    // the middle and the shore gates never get counted.
                    float sa = (float)srng.NextDouble() * Mathf.PI * 2f;
                    float sr = maxR * Mathf.Sqrt((float)srng.NextDouble());
                    accept += ChanceAt(centre.x + Mathf.Sin(sa) * sr, centre.z + Mathf.Cos(sa) * sr,
                        out _, out _, out _, out _);
                }
                accept /= Samples;
            }
            // 0.95, not 1.0: the estimate is a sample mean and may run a
            // little under the truth.
            float keep = Mathf.Clamp01(MaxTrees * 0.95f / Mathf.Max(1f, cells * accept));

            var spruce0 = SceneryKit.Get("Spruce"); var spruce1 = SceneryKit.Get("Spruce_LOD1");
            var broad0 = SceneryKit.Get("Broad"); var broad1 = SceneryKit.Get("Broad_LOD1");
            var palm0 = SceneryKit.Get("Palm"); var palm1 = SceneryKit.Get("Palm_LOD1");
            var boulders = new[] { SceneryKit.Get("Boulder_0"), SceneryKit.Get("Boulder_1"), SceneryKit.Get("Boulder_2"), SceneryKit.Get("Boulder_3") };
            var cliffTp = new[] { SceneryKit.Get("Cliff_0"), SceneryKit.Get("Cliff_1"), SceneryKit.Get("Cliff_2") };
            if (kit && (spruce0 == null || broad0 == null || palm0 == null || boulders[0] == null || cliffTp[0] == null))
            {
                Debug.LogWarning("IslandScenery: kit is incomplete -- falling back to cones");
                kit = false;
            }

            for (float z = -maxR; z <= maxR && trees < MaxTrees; z += step)
            {
                for (float x = -maxR; x <= maxR && trees < MaxTrees; x += step)
                {
                    // Every roll this candidate could ever need is drawn up
                    // front, so the stream is the same whatever it becomes.
                    float jx = (float)(rng.NextDouble() - 0.5) * step * 0.9f;
                    float jz = (float)(rng.NextDouble() - 0.5) * step * 0.9f;
                    double rChance = rng.NextDouble();
                    float rClimate = (float)rng.NextDouble();
                    float rSpecies = (float)rng.NextDouble();
                    float rHeight = (float)rng.NextDouble();
                    float rYaw = (float)rng.NextDouble();
                    float rVariant = (float)rng.NextDouble();
                    double rRock = rng.NextDouble();
                    float rRockA = (float)rng.NextDouble();
                    float rRockB = (float)rng.NextDouble();
                    float rRockC = (float)rng.NextDouble();

                    float wx = centre.x + x + jx, wz = centre.z + z + jz;

                    float raw = ChanceAt(wx, wz, out float h, out float sx, out float sz, out float proud);
                    if (raw <= 0f) continue;
                    float slope = Mathf.Sqrt(sx * sx + sz * sz);
                    float chance = raw * keep;
                    bool place = keepOut == null || !keepOut(wx, wz);
                    var at = new Vector3(wx, h, wz);

                    if (rChance > chance)
                    {
                        // Where a tree could not hold: steep ground, or rock
                        // that has already surfaced. Rock arrives as
                        // FORMATIONS -- a sparse seed spawns a cluster of
                        // leaning, overlapping shards of mixed size with
                        // scree round it -- because one upright shard per
                        // cell, evenly spread, is a field of monoliths
                        // (Kevin), and an outcrop is a family of stones.
                        bool stony = slope > prm.vegSlopeSoft || proud > 0.4f;
                        bool sheer = slope > 0.85f && slope < 1.3f && h > sand + 3f;
                        bool ridge = proud > 0.8f;
                        float crag = Mathf.PerlinNoise((wx + 1000f) * 0.035f, (wz + 1000f) * 0.035f);
                        double pForm = (ridge || sheer)
                            ? 0.10 * Mathf.Clamp01((crag - 0.40f) / 0.30f) * (0.5f + rockiness)
                            : (stony ? 0.012 : 0.006 * Mathf.Clamp01((crag - 0.55f) / 0.25f));
                        if (kit && place && rocks < MaxRocks && formations < maxFormations && rRock < pForm)
                        {
                            int made = Formation(new Vector3(wx, h, wz), sx, sz, slope, proud, rockiness,
                                height, sand, seed, cliffTp, boulders, CellFor, ref cliffs);
                            rocks += made;
                            formations++;
                        }
                        else if (rocks < MaxRocks && stony && rRockB < 0.10 * keep)
                        {
                            // A lone boulder, and only a boulder: the
                            // standing stones are gone.
                            var cb = CellFor(wx, wz);
                            if (!kit)
                            {
                                AddBoulder(cb.v0, cb.n0, cb.c0, cb.t0, at, rRockA, rRockB, rRockC, place);
                                if (place) cb.Grow(at, 3f, 3f);
                            }
                            else if (place)
                            {
                                var tp = boulders[Mathf.Min(3, (int)(rVariant * 4f))];
                                float size = Mathf.Lerp(SeaSick.World.WorldScale.BoulderMin,
                                                        SeaSick.World.WorldScale.BoulderMax, rRockA);
                                var sc = new Vector3(size * 0.5f, size * 0.5f * (0.7f + 0.5f * rRockC),
                                                     size * 0.5f * (0.8f + 0.4f * rRockB));
                                StampBoth(cb, tp, tp, at, rYaw * Mathf.PI * 2f, sc, sc);
                                cb.Grow(at, size, size);
                            }
                            rocks++;
                        }
                        continue;
                    }

                    {
                        var cb = CellFor(wx, wz);
                        int v0Start = cb.v0.Count, v1Start = cb.v1.Count;
                        if (!kit)
                        {
                            AddTree(cb.v0, cb.n0, cb.c0, cb.t0, at, rHeight, rSpecies, rYaw, rVariant, rClimate, place);
                            if (place) cb.Grow(at, 4f, TreeMaxH);
                        }
                        else if (place)
                        {
                            // Species: palms on the hot sandbanks and the
                            // low ground of a hot island, broadleaves on the
                            // low ground of a temperate one, spruce
                            // everywhere else. Altitude decides within an
                            // island, latitude decides between them.
                            SceneryKit.Template tp0, tp1;
                            float factor;
                            if (rClimate < tropical)
                            {
                                bool palm = h < sand + 6f || rSpecies < 0.6f;
                                tp0 = palm ? palm0 : broad0; tp1 = palm ? palm1 : broad1;
                                factor = palm ? 0.72f : 0.82f;
                            }
                            else
                            {
                                bool broad = h < sand + 10f && rSpecies < 0.28f;
                                tp0 = broad ? broad0 : spruce0; tp1 = broad ? broad1 : spruce1;
                                factor = broad ? 0.82f : 1f;
                            }
                            float target = Mathf.Lerp(TreeMinH, TreeMaxH, Mathf.Pow(rHeight, HeightBias)) * factor;
                            float s = target / Mathf.Max(1f, tp0.height);
                            float yaw = Wind + (rYaw - 0.5f) * 0.7f;
                            var sc = new Vector3(s, s, s);
                            StampBoth(cb, tp0, tp1, at, yaw, sc, sc);
                            cb.Grow(at, tp0.radius * s, target);
                        }
                        if (cb.v0.Count > v0Start)
                            index.Add(new SceneryWood.Tree
                            {
                                baseAt = at,
                                cell = cb.index,
                                vertStart = v0Start,
                                vertCount = cb.v0.Count - v0Start,
                                lod1Start = v1Start,
                                lod1Count = cb.v1.Count - v1Start,
                            });
                    }
                    // Counted whether or not it was placed, so the budget and
                    // the loop's exit are the same with a clearing as without.
                    trees++;
                }
            }

            int tri0 = 0, tri1 = 0;
            foreach (var cb in cellList) { tri0 += cb.t0.Count / 3; tri1 += cb.t1.Count / 3; }
            Debug.Log($"IslandScenery: r{meanR:F0} peak {peak:F0} verdancy {verdancy:F2} "
                + $"rockiness {rockiness:F2} tropical {tropical:F2} accept {accept:F3} keep {keep:F3} cells {cells:F0} step {step:F1} "
                + $"-> {trees} trees ({trees / Mathf.Max(0.01f, Mathf.PI * meanR * meanR / 10000f):F0}/ha), {rocks} rocks ({cliffs} cliffs), {cellList.Count} cells, "
                + $"{tri0} tris LOD0 / {tri1} LOD1{(kit ? "" : " [cones fallback]")}");

            if (index.Count == 0 && rocks == 0) return null;

            var go = new GameObject("Scenery");
            go.transform.SetParent(parent, false);
            go.transform.position = Vector3.zero;
            var wcells = new List<SceneryWood.Cell>();
            for (int i = 0; i < cellList.Count; i++)
            {
                var cb = cellList[i];
                var cell = new SceneryWood.Cell();
                if (cb.v0.Count > 0)
                {
                    var cgo = new GameObject("Cell_" + i);
                    cgo.transform.SetParent(go.transform, false);
                    cell.lod0 = MakeMesh(cb.v0, cb.n0, cb.c0, cb.t0, "SceneryLOD0");
                    cell.r0 = Attach(cgo.transform, "LOD0", cell.lod0);
                    if (cb.v1.Count > 0)
                    {
                        cell.lod1 = MakeMesh(cb.v1, cb.n1, cb.c1, cb.t1, "SceneryLOD1");
                        cell.r1 = Attach(cgo.transform, "LOD1", cell.lod1);
                        cell.r1.enabled = false;
                    }
                    cell.centre = (cb.min + cb.max) * 0.5f;
                    cell.radius = Mathf.Max(cb.max.x - cb.min.x, cb.max.z - cb.min.z) * 0.5f;
                }
                wcells.Add(cell);
            }
            go.AddComponent<SceneryWood>().Configure(wcells, index, isle);
            go.AddComponent<SceneryLod>().Configure(wcells, terrain);
            return go;
        }

        /// One outcrop: a run of shards along the ridge (across the slope,
        /// or any way on flat ground), a big core in the middle and smaller
        /// stones tailing off either side, every one leaning in toward the
        /// core and sunk a third of its height, with scree scattered round
        /// the foot. A quarter of them are LARGE -- ten to thirteen shards
        /// over twenty-odd metres -- so an island has both a crag and the
        /// stones below it. Deterministic per spot from its own generator,
        /// so it never disturbs the scatter stream.
        static int Formation(Vector3 c, float sx, float sz, float slope, float proud, float rockiness,
            System.Func<float, float, float> height, float sand, int seed,
            SceneryKit.Template[] cliffTp, SceneryKit.Template[] boulders,
            System.Func<float, float, CellBuild> cellFor, ref int cliffs)
        {
            var lr = new System.Random(seed * 31 + Mathf.RoundToInt(c.x * 7.3f) * 131 + Mathf.RoundToInt(c.z * 13.1f));
            bool big = lr.NextDouble() < 0.35;
            int n = big ? 10 + lr.Next(6) : 3 + lr.Next(5);
            float L = n * (big ? 2.4f : 1.6f);
            float dx, dz;
            if (slope > 0.12f) { float inv = 1f / slope; dx = -sz * inv; dz = sx * inv; }   // along the contour
            else { float a = (float)lr.NextDouble() * Mathf.PI * 2f; dx = Mathf.Cos(a); dz = Mathf.Sin(a); }
            float px_ = -dz, pz_ = dx;
            float yawBase = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
            int made = 0;
            for (int k = 0; k < n; k++)
            {
                float t = (k + 0.5f) / n - 0.5f;
                float along = t * L + ((float)lr.NextDouble() - 0.5f) * 1.4f;
                float across = ((float)lr.NextDouble() - 0.5f) * (big ? 4f : 2.2f);
                float px = c.x + dx * along + px_ * across;
                float pz = c.z + dz * along + pz_ * across;
                float ph = height(px, pz);
                if (ph < sand + 0.5f) continue;
                float core = Mathf.Clamp(1f - Mathf.Abs(t) * 1.7f, 0.22f, 1f);
                float j = 0.8f + 0.4f * (float)lr.NextDouble();
                float w = (big ? 3.5f + 6f * core : 1.6f + 2.8f * core) * j;
                float hgt = (big ? 3.5f + 9f * core : 1.4f + 4f * core) * (0.8f + 0.4f * (float)lr.NextDouble());
                float dep = w * (0.5f + 0.4f * (float)lr.NextDouble());
                float lean = (8f + 20f * (float)lr.NextDouble()) * (t < 0 ? 1f : -1f) * (Mathf.Abs(t) < 0.08f ? 0.3f : 1f);
                var rot = Quaternion.Euler(lean, yawBase + ((float)lr.NextDouble() - 0.5f) * 70f,
                                           ((float)lr.NextDouble() - 0.5f) * 18f);
                var tp = cliffTp[lr.Next(cliffTp.Length)];
                var cb = cellFor(px, pz);
                var at = new Vector3(px, ph - hgt * 0.30f, pz);
                StampBoth(cb, tp, tp, at, rot, new Vector3(w, hgt, dep));
                cb.Grow(new Vector3(px, ph, pz), w, hgt);
                made++;
                cliffs++;
            }
            // scree at the foot
            int ns = 2 + lr.Next(big ? 5 : 3);
            for (int k = 0; k < ns; k++)
            {
                float a = (float)lr.NextDouble() * Mathf.PI * 2f;
                float r = L * (0.35f + 0.55f * (float)lr.NextDouble());
                float px = c.x + Mathf.Cos(a) * r, pz = c.z + Mathf.Sin(a) * r;
                float ph = height(px, pz);
                if (ph < sand + 0.5f) continue;
                float size = 0.8f + 1.6f * (float)lr.NextDouble();
                var tp = boulders[lr.Next(boulders.Length)];
                var cb = cellFor(px, pz);
                var sc = new Vector3(size * 0.5f, size * 0.4f, size * 0.45f);
                StampBoth(cb, tp, tp, new Vector3(px, ph, pz), (float)lr.NextDouble() * Mathf.PI * 2f, sc, sc);
                cb.Grow(new Vector3(px, ph, pz), size, size);
                made++;
            }
            return made;
        }

        static void StampBoth(CellBuild cb, SceneryKit.Template tp0, SceneryKit.Template tp1,
            Vector3 at, float yaw, Vector3 s0, Vector3 s1)
        {
            SceneryKit.Stamp(tp0, cb.v0, cb.n0, cb.c0, cb.t0, at, yaw, s0);
            if (tp1 != null) SceneryKit.Stamp(tp1, cb.v1, cb.n1, cb.c1, cb.t1, at, yaw, s1);
        }

        static void StampBoth(CellBuild cb, SceneryKit.Template tp0, SceneryKit.Template tp1,
            Vector3 at, Quaternion rot, Vector3 s)
        {
            SceneryKit.Stamp(tp0, cb.v0, cb.n0, cb.c0, cb.t0, at, rot, s);
            if (tp1 != null) SceneryKit.Stamp(tp1, cb.v1, cb.n1, cb.c1, cb.t1, at, rot, s);
        }

        static Mesh MakeMesh(List<Vector3> v, List<Vector3> n, List<Color32> c, List<int> t, string name)
        {
            var mesh = new Mesh { name = name };
            if (v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetColors(c);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static MeshRenderer Attach(Transform parent, string name, Mesh mesh)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = SceneryMaterial();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return r;
        }

        static Material scenery;

        /// The terrain's own shader, so scenery is lit exactly like the ground
        /// it stands on -- but with the rock striation off, which is meant for
        /// cliff faces and looks like a fault on a canopy, and the albedo
        /// break-up low, because the kit carries its own per-vertex shading.
        public static Material SceneryMaterial()
        {
            if (scenery != null) return scenery;
            var sh = Shader.Find("SeaSick/Terrain Vertex Color");
            scenery = new Material(sh) { name = "Scenery" };
            if (scenery.HasProperty("_StriationStrength")) scenery.SetFloat("_StriationStrength", 0f);
            if (scenery.HasProperty("_DetailScale")) scenery.SetFloat("_DetailScale", 1.1f);
            if (scenery.HasProperty("_NormalStrength")) scenery.SetFloat("_NormalStrength", 0.15f);
            if (scenery.HasProperty("_DetailStrength")) scenery.SetFloat("_DetailStrength", 0.12f);
            return scenery;
        }

        // ---- the cones: fallback for a project without the kit --------------

        static void AddTree(List<Vector3> v, List<Vector3> n, List<Color32> c, List<int> t,
            Vector3 at, float rHeight, float rCanopy, float rLeanX, float rLeanZ, float rColour, bool place)
        {
            float h = Mathf.Lerp(TreeMinH, TreeMaxH, Mathf.Pow(rHeight, HeightBias));
            float trunkH = h * 0.34f;
            float trunkR = h * 0.035f;
            float canopyR = h * Mathf.Lerp(0.19f, 0.29f, rCanopy);
            var leanV = new Vector3((rLeanX - 0.5f) * 0.12f, 0f, (rLeanZ - 0.5f) * 0.12f);
            var trunk = new Color32(92, 64, 40, 255);
            byte g = (byte)(74 + (int)(rColour * 58f));
            var leaf = new Color32((byte)(24 + (int)(rColour * 26f)), g, (byte)(34 + (int)(rCanopy * 26f)), 255);
            if (!place) return;
            Prism(v, n, c, t, at, trunkR, trunkH, leanV, trunk);
            Cone(v, n, c, t, at + new Vector3(0f, trunkH, 0f) + leanV * 0.5f,
                canopyR, h * 0.42f, leanV, leaf);
            Cone(v, n, c, t, at + new Vector3(0f, trunkH + h * 0.26f, 0f) + leanV * 0.8f,
                canopyR * 0.68f, h * 0.40f, leanV, leaf);
        }

        static void AddBoulder(List<Vector3> v, List<Vector3> n, List<Color32> c, List<int> t,
            Vector3 at, float rSize, float rGrey, float rLean, bool place)
        {
            float s = Mathf.Lerp(SeaSick.World.WorldScale.BoulderMin,
                                 SeaSick.World.WorldScale.BoulderMax, rSize);
            byte grey = (byte)(96 + (int)(rGrey * 40f));
            var col = new Color32(grey, (byte)(grey + 4), (byte)(grey + 10), 255);
            var lean = new Vector3((rLean - 0.5f) * 0.5f, 0f, (rGrey - 0.5f) * 0.5f);
            if (!place) return;
            Cone(v, n, c, t, at + new Vector3(0f, -s * 0.25f, 0f), s, s * 1.5f, lean, col, 5);
        }

        /// Four-sided prism for a trunk. Flat-shaded: normals per face.
        static void Prism(List<Vector3> v, List<Vector3> n, List<Color32> c, List<int> t,
            Vector3 baseAt, float r, float h, Vector3 lean, Color32 col)
        {
            Vector3 top = baseAt + new Vector3(0f, h, 0f) + lean;
            for (int i = 0; i < 4; i++)
            {
                float a0 = i / 4f * Mathf.PI * 2f, a1 = (i + 1) / 4f * Mathf.PI * 2f;
                Vector3 p0 = baseAt + new Vector3(Mathf.Sin(a0) * r, 0f, Mathf.Cos(a0) * r);
                Vector3 p1 = baseAt + new Vector3(Mathf.Sin(a1) * r, 0f, Mathf.Cos(a1) * r);
                Vector3 p2 = top + new Vector3(Mathf.Sin(a1) * r * 0.7f, 0f, Mathf.Cos(a1) * r * 0.7f);
                Vector3 p3 = top + new Vector3(Mathf.Sin(a0) * r * 0.7f, 0f, Mathf.Cos(a0) * r * 0.7f);
                Quad(v, n, c, t, p0, p1, p2, p3, col);
            }
        }

        /// Cone with `sides` faces, used for canopies and boulders alike.
        static void Cone(List<Vector3> v, List<Vector3> n, List<Color32> c, List<int> t,
            Vector3 baseAt, float r, float h, Vector3 lean, Color32 col, int sides = 6)
        {
            Vector3 apex = baseAt + new Vector3(0f, h, 0f) + lean;
            for (int i = 0; i < sides; i++)
            {
                float a0 = i / (float)sides * Mathf.PI * 2f, a1 = (i + 1) / (float)sides * Mathf.PI * 2f;
                Vector3 p0 = baseAt + new Vector3(Mathf.Sin(a0) * r, 0f, Mathf.Cos(a0) * r);
                Vector3 p1 = baseAt + new Vector3(Mathf.Sin(a1) * r, 0f, Mathf.Cos(a1) * r);
                Tri(v, n, c, t, p0, p1, apex, col);
            }
        }

        static void Tri(List<Vector3> v, List<Vector3> n, List<Color32> c, List<int> t,
            Vector3 a, Vector3 b, Vector3 d, Color32 col)
        {
            int i0 = v.Count;
            Vector3 nrm = Vector3.Normalize(Vector3.Cross(b - a, d - a));
            v.Add(a); v.Add(b); v.Add(d);
            n.Add(nrm); n.Add(nrm); n.Add(nrm);
            c.Add(col); c.Add(col); c.Add(col);
            t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 1);
        }

        static void Quad(List<Vector3> v, List<Vector3> n, List<Color32> c, List<int> t,
            Vector3 a, Vector3 b, Vector3 d, Vector3 e, Color32 col)
        {
            Tri(v, n, c, t, a, b, d, col);
            Tri(v, n, c, t, a, d, e, col);
        }
    }
}
