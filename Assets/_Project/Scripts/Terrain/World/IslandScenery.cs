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
    ///
    /// **The wood is PATCHY.** Verdancy says how green an island is; it said
    /// nothing about where the wood is thick, so an island came back as one
    /// even carpet at its own density -- never dense and open on the same
    /// landmass, which is what every real wood is. `Cover01` is the missing
    /// half: a slow field that closes the canopy in places and opens it in
    /// others, renormalised about its mean so it changes the PATCHINESS and
    /// not the amount of wood. Out of it come the three things this class
    /// gained: glades (with scrub in them, because bare ground beside a
    /// stand reads as a hole rather than as a clearing), a canopy with a
    /// shape (trees in a closed stand grow taller), and somewhere for the
    /// wheat to go.
    ///
    /// **Wheat is a second pass.** A crop mat is 2.5 m across where a tree
    /// is 5 m apart, so it cannot share the tree walk. Fields are decided
    /// FIRST and the wood is told to stay out of them: a field is cleared
    /// ground, and the order is what makes it read as somebody's enclosure
    /// rather than as a gap that happens to be gold.
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

        /// A crop mat is 105 triangles for 2.5 m of ground and a bush is 30,
        /// so these are the guards that keep a well-farmed island from
        /// costing more in wheat than it does in wood. Same shape as the
        /// tree cap: a number the scan should never reach.
        const int MaxCrops = 2200, MaxScrub = 1800;

        /// Everything the scatter needs to know about one candidate spot,
        /// filled by ONE function so the pre-pass that sizes the thinning
        /// and the walk that does the placing can never describe different
        /// islands. (That is not a hypothetical: a constant standing in for
        /// this was the fault that left a bare band down one side.)
        /// What one island's bake came out as, kept so a look probe can FRAME
        /// a farmed island. There is no other way to find one: farming is a
        /// roll taken inside the bake, so nothing outside it knows which
        /// island in the sea has the wheat on it. An instrument, not state --
        /// nothing in the game reads this.
        public struct Dressed
        {
            public Vector3 centre;
            public float radius;
            public int trees, crops, scrub;
        }

        /// Newest world first; cleared by the populator when it rebuilds.
        public static readonly List<Dressed> Report = new List<Dressed>();

        /// One probe cell of sowable ground, from the crop pass's first
        /// sweep. Kept so the second sweep can fill the fields it already
        /// found instead of asking the slope question sixteen times over.
        struct FieldCell { public float x, z, gate, n; }

        /// How hard the middle of a field is filled. Above 1 on purpose: at
        /// the raw noise-times-gate product a field's interior accepted about
        /// half its mats and read as gold speckles on grass rather than as a
        /// crop. This saturates the middle and leaves only the outer band
        /// partial, which is where a real field's edge actually is.
        const float CropFill = 1.9f;

        struct Spot
        {
            public float h, sx, sz, slope, proud;
            /// How closed the wood is HERE, 0 in a glade and 1 in a stand.
            public float cover;
            /// How much of this spot is sown field, 0 to 1.
            public float field;
        }

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
            bool individualTrees = terrain != null && terrain.individualTrees;
            var index = new List<SceneryWood.Tree>();
            var individuals = new List<(string id, Vector3 at, float yaw, Vector3 scale)>();
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
            bool sculptedHome = terrain != null && terrain.storybookLandforms && TerrainHeight.HomeIsleWeight(c2, prm) > .9f;
            float verdancy = TerrainHeight.Verdancy01(c2, prm);
            float rockiness = TerrainHeight.Rock01(c2, prm);

            // North is cold, south is hot (GDD §5): south of `palmLatitude`
            // the wood turns to palms over a band, so an island's kind is a
            // fact about where it is and not a roll.
            float tropical = 0f;
            if (terrain != null)
                tropical = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(
                    terrain.palmLatitude + terrain.palmBand, terrain.palmLatitude - terrain.palmBand, centre.z));

            // **Where sand stops**, read off the thing that PAINTS it rather
            // than guessed at again here. The old gate was `sand + 1.2`,
            // which is inside the mesher's sand-to-grass blend -- so half the
            // wood on a low island stood with its feet in the beach, which is
            // exactly what Kevin saw. A tree wants grass under it.
            float sandTop = sand + TerrainChunkMesher.SandBlend
                          + (terrain != null ? terrain.sandTreeMargin : 2.0f);

            // **One species per island.** The mix used to be a slow field, so
            // an island came back as groves of conifer and groves of
            // broadleaf together -- which Kevin's verdict on is that it does
            // not make sense, and he is right: two species sharing a 3 ha
            // island is a botanical garden, not a wood. Latitude decides
            // whether it is palm; north of that a roll decides conifer or
            // broadleaf, ONCE, for the whole landmass.
            int species;                    // 0 spruce, 1 broadleaf, 2 palm
            {
                var srng2 = new System.Random(seed * 193 + 41);
                float u = (float)srng2.NextDouble();
                float broadShare = terrain != null ? terrain.broadleafIslands : 0.4f;
                species = tropical > 0.5f ? 2 : (u < broadShare ? 1 : 0);
            }

            // Is this island WORKED, and how hard? Rolled on a generator of
            // its own: the scatter's stream must not move because a field
            // was decided, or adding farming to the world reshuffles every
            // tree in it.
            //
            // Most islands get NOTHING. Biasing the roll rather than the
            // threshold is what keeps the worked ones properly worked --
            // bias the threshold instead and every island in the sea comes
            // back with a token patch of wheat, which says nothing about
            // any of them. Finding a farmed island should mean something.
            float farm;
            {
                var frng = new System.Random(seed * 61 + 7);
                float u = (float)frng.NextDouble();
                float share = terrain != null ? terrain.farmedShare : 0.35f;
                farm = u < 1f - share ? 0f : Mathf.InverseLerp(1f - share, 1f, u);
                farm *= 1f - tropical * 0.75f;     // palms and wheat are different worlds
            }

            // --- the cover field ------------------------------------------
            // Verdancy says how green an ISLAND is. It said nothing about
            // where the wood is thick, so every island came back as one even
            // carpet at its own density -- a lawn with trees on it at 22 a
            // hectare and a solid mat at 230, but never both on the same
            // island. Cover is the missing half: a slow field that closes
            // the canopy in places and opens it in others.
            //
            // The MEAN is renormalised, so contrast changes the patchiness
            // and not the amount of wood -- and because the thinning
            // pre-pass runs through the same function, the budget follows it
            // for free.
            // The stand wavelength is a fraction of the ISLAND, not a world
            // constant. At a fixed 90 m an island 100 m across fits inside one
            // lobe of the field and comes back uniformly open or uniformly
            // closed -- one value for the whole landmass, which is the exact
            // trap `uplandFrequency` was caught in at 1/700. Sized off meanR
            // every island gets about the same NUMBER of stands, big or
            // small, which is what makes the patchiness read as a property of
            // woodland rather than of island size.
            float treeDensity = sculptedHome ? 1.05f : terrain != null ? terrain.treeDensity : 0.55f;
            float palmOnSand = terrain != null ? terrain.palmOnSand : 0.06f;
            /// Set by `ChanceAt` for the spot it was just asked about: this
            /// one is standing on the beach, on the palm allowance. Read
            /// immediately by the caller and never across calls.
            bool sandPalm = false;

            float standWave = Mathf.Clamp(meanR * (terrain != null ? terrain.standSpan : 0.55f), 34f, 140f);
            float standF = 1f / standWave;
            float standC = terrain != null ? terrain.standContrast : 2.7f;
            float standFloor = terrain != null ? terrain.standFloor : 0.13f;
            float coverMean = (standFloor + 1f) * 0.5f;
            // Same reasoning, one size down: a field is an enclosure inside a
            // stand's worth of ground, so it is about half a stand across.
            float fieldF = 1f / Mathf.Clamp(standWave * 0.62f, 26f, 90f);
            float fieldSlope = terrain != null ? terrain.fieldSlopeMax : 0.17f;
            float fieldRise = terrain != null ? terrain.fieldMaxRise : 26f;

            float Cover01(float wx, float wz)
            {
                // Two octaves. One gives round blobs with a clean edge, which
                // reads as a stamp; the second is where the stand frays out
                // into the open, and that fraying is the whole effect.
                float a = Mathf.PerlinNoise((wx + 7300f) * standF, (wz - 2100f) * standF);
                float b = Mathf.PerlinNoise((wx - 1500f) * standF * 2.9f, (wz + 900f) * standF * 2.9f);
                float t = Mathf.Clamp01(((a * 0.74f + b * 0.26f) - 0.5f) * standC + 0.5f);
                return Mathf.Lerp(sculptedHome ? .65f : standFloor, 1f, t);
            }

            // --- the fields -----------------------------------------------
            // A field is CLEARED ground, so it is decided before the wood and
            // the wood is told to stay out of it. Split in two on purpose:
            // the NOISE is what shape the enclosure is and costs two Perlin
            // samples, the GATE is whether this ground could be sown at all
            // and costs five height samples. The crop pass finds its fields
            // on the gate at a coarse step and then fills them against the
            // noise, so the edge of a field is the noise's edge and not a
            // staircase of probe cells.
            float FieldNoise(float wx, float wz)
            {
                if (farm <= 0.02f) return 0f;
                float f = Mathf.PerlinNoise((wx + 3100f) * fieldF, (wz - 5200f) * fieldF);
                // The second octave only frays the EDGE. At a quarter of the
                // weight it was cutting the fields up instead: an island came
                // back with a dozen scraps of wheat where it should have had
                // three fields, because the fine octave punched holes through
                // the middle of each one. A field is a worked enclosure and
                // the thing that makes it read as one is that it is WHOLE.
                float g = Mathf.PerlinNoise((wx - 800f) * fieldF * 1.9f, (wz + 4400f) * fieldF * 1.9f);
                float thr = Mathf.Lerp(0.78f, 0.38f, farm);
                return Mathf.SmoothStep(0f, 1f, ((f * 0.87f + g * 0.13f) - thr) / 0.12f);
            }

            float FieldGate(float h, float slope, float proud)
            {
                if (farm <= 0.02f || proud > 0.25f) return 0f;
                float rise = h - sand;
                if (rise < 1.4f || rise > fieldRise || slope > fieldSlope) return 0f;
                // Out at the edges it thins rather than stopping on a line:
                // nobody ploughs right up to where the hill starts.
                return (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(fieldSlope * 0.55f, fieldSlope, slope)))
                     * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(fieldRise * 0.72f, fieldRise, rise)));
            }

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
            // The warp runs at about four spacings: shorter and it is just
            // more jitter, longer and the whole wood slides sideways without
            // changing shape.
            if (sculptedHome) step = 6f;
            float warpF = 1f / Mathf.Max(6f, step * 4f);
            float warpAmp = step * (terrain != null ? terrain.treeWarp : 1.5f);
            double scrubP = terrain != null ? terrain.scrubChance : 0.45f;
            float cragScale = terrain != null ? terrain.cragScale : 1f;
            float headlandShare = terrain != null ? terrain.headlandShare : 0.14f;
            float shoreRock = terrain != null ? terrain.shoreRock : 1f;
            int trees = 0, rocks = 0, cliffs = 0, formations = 0, bushes = 0;
            // For the density report: how much of the island the cover field
            // calls a stand, counted over the candidates actually evaluated.
            int coverN = 0, coverDense = 0, coverOpen = 0;
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
            float ChanceAt(float wx, float wz, out Spot sp)
            {
                sp = default;
                float ang = Mathf.Atan2(wx - centre.x, wz - centre.z);
                float dist = Mathf.Sqrt((wx - centre.x) * (wx - centre.x) + (wz - centre.z) * (wz - centre.z));
                if (radiusAt != null && dist > radiusAt(ang) * 0.94f) return 0f;

                sp.h = height(wx, wz);
                // Never on sand. A palm island keeps a small allowance for
                // the beach itself, because a palm is the one tree that
                // belongs there -- but rarely, or the beach stops being one.
                if (sp.h < sandTop)
                {
                    if (species != 2 || sp.h < sand + 0.6f) return 0f;
                    sandPalm = true;
                }
                else sandPalm = false;
                sp.sx = (height(wx + 3f, wz) - height(wx - 3f, wz)) / 6f;
                sp.sz = (height(wx, wz + 3f) - height(wx, wz - 3f)) / 6f;
                sp.slope = Mathf.Sqrt(sp.sx * sp.sx + sp.sz * sp.sz);

                // Rock that has broken through the soil is stone, and
                // nothing roots in it.
                sp.proud = TerrainHeight.RockBreak(
                    new Unity.Mathematics.float2(wx, wz), rockiness, prm);

                sp.cover = Cover01(wx, wz);
                sp.field = FieldNoise(wx, wz) * FieldGate(sp.h, sp.slope, sp.proud);

                float slopeTerm = 1f - Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(prm.vegSlopeSoft, prm.vegSlopeHard, sp.slope));
                float rockTerm = sp.proud > 0.4f ? 1f - prm.vegRockSuppress : 1f;
                // Exposure is a MOUNTAIN phenomenon and has to be gated on
                // the island being one: keyed to `peak` alone it put a 5 m
                // sandbank's exposure band over the whole island.
                float exposure = peak < ExposedAbove ? 1f
                    : 1f - Mathf.SmoothStep(0f, 1f,
                        Mathf.InverseLerp(peak * 0.80f, peak * 1.02f, sp.h));
                // Standing wheat does not have trees in it. Not quite zero:
                // a hedge line and the odd tree left for shade are what say
                // somebody made this enclosure rather than the noise did.
                return verdancy * slopeTerm * rockTerm * exposure * treeDensity
                     * (sp.cover / coverMean) * (1f - sp.field * 0.94f)
                     * (sandPalm ? palmOnSand : 1f);
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
                    accept += ChanceAt(centre.x + Mathf.Sin(sa) * sr, centre.z + Mathf.Cos(sa) * sr, out _);
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
            // The crop and the heath are the newest half of the kit, so an
            // FBX from before them has to degrade to an island with no wheat
            // rather than to a null reference in the middle of a bake.
            var crop0 = new[] { SceneryKit.Get("Crop_0"), SceneryKit.Get("Crop_1"), SceneryKit.Get("Crop_2") };
            var crop1 = new[] { SceneryKit.Get("Crop_0_LOD1"), SceneryKit.Get("Crop_1_LOD1"), SceneryKit.Get("Crop_2_LOD1") };
            var scrub0 = new[] { SceneryKit.Get("Scrub_0"), SceneryKit.Get("Scrub_1"), SceneryKit.Get("Scrub_2") };
            var scrub1 = new[] { SceneryKit.Get("Scrub_0_LOD1"), SceneryKit.Get("Scrub_1_LOD1"), SceneryKit.Get("Scrub_2_LOD1") };
            bool hasCrop = kit, hasScrub = kit;
            for (int i = 0; i < 3; i++)
            {
                if (crop0[i] == null || crop1[i] == null) hasCrop = false;
                if (scrub0[i] == null || scrub1[i] == null) hasScrub = false;
            }
            if (kit && !(hasCrop && hasScrub))
                Debug.LogWarning("IslandScenery: the flora FBX predates the crop/scrub kit -- "
                    + "no wheat and no heath. Re-run seasick_style.build_kit + export_kit.");

            for (float z = -maxR; z <= maxR && trees < MaxTrees; z += step)
            {
                for (float x = -maxR; x <= maxR && trees < MaxTrees; x += step)
                {
                    // Every roll this candidate could ever need is drawn up
                    // front, so the stream is the same whatever it becomes.
                    float jx = (float)(rng.NextDouble() - 0.5) * step * 0.9f;
                    float jz = (float)(rng.NextDouble() - 0.5) * step * 0.9f;
                    double rChance = rng.NextDouble();
                    float rHeight = (float)rng.NextDouble();
                    float rYaw = (float)rng.NextDouble();
                    float rVariant = (float)rng.NextDouble();
                    double rRock = rng.NextDouble();
                    float rRockA = (float)rng.NextDouble();
                    float rRockB = (float)rng.NextDouble();
                    float rRockC = (float)rng.NextDouble();
                    double rScrub = rng.NextDouble();
                    float rScrubA = (float)rng.NextDouble();

                    // The lattice, WARPED. Jitter inside a cell cannot hide a
                    // grid, because every tree still belongs to its own cell
                    // and the cells are in rows -- at 5 m in a closed wood
                    // that shows as aisles. A slow warp moves NEIGHBOURS
                    // together, which bends the rows into drifts and crowds
                    // and opens gaps between them, and gaps and crowds are
                    // what a real wood is made of.
                    float wx = centre.x + x + jx, wz = centre.z + z + jz;
                    if (kit && warpAmp > 0f)
                    {
                        float ux = wx * warpF, uz = wz * warpF;   // both axes off the SAME point
                        wx += (Mathf.PerlinNoise(ux + 11.3f, uz) - 0.5f) * warpAmp;
                        wz += (Mathf.PerlinNoise(ux, uz + 19.7f) - 0.5f) * warpAmp;
                    }

                    float raw = ChanceAt(wx, wz, out Spot sp);
                    if (raw <= 0f) continue;
                    float h = sp.h, sx = sp.sx, sz = sp.sz, proud = sp.proud, slope = sp.slope;
                    coverN++;
                    if (sp.cover > 0.78f) coverDense++;
                    else if (sp.cover < 0.35f) coverOpen++;
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
                                height, sand, seed, cliffTp, boulders, CellFor, ref cliffs,
                                cragScale, headlandShare);
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
                        else if (hasScrub && place && bushes < MaxScrub && !stony
                                 && rScrub < scrubP * (1f - sp.cover) * (1f - sp.field) * verdancy * keep)
                        {
                            // Open ground inside the island. Left bare it is
                            // a lawn, and a lawn beside a closed stand reads
                            // as a hole in the wood rather than as a glade --
                            // which would waste the whole point of making the
                            // wood patchy. The bush is strongest in the
                            // MIDDLE of a glade and fades out under the
                            // canopy, where nothing grows anyway.
                            var cb = CellFor(wx, wz);
                            int v = (int)(rVariant * 3f); if (v > 2) v = 2;
                            float target = Mathf.Lerp(SeaSick.World.WorldScale.ScrubMin,
                                                      SeaSick.World.WorldScale.ScrubMax, rScrubA);
                            float sb = target / Mathf.Max(0.2f, scrub0[v].height);
                            var sc = new Vector3(sb * (0.85f + 0.35f * rRockB), sb, sb * (0.85f + 0.35f * rRockC));
                            StampBoth(cb, scrub0[v], scrub1[v], at, rYaw * Mathf.PI * 2f, sc, sc);
                            cb.Grow(at, scrub0[v].radius * sb * 1.3f, target);
                            bushes++;
                        }
                        continue;
                    }

                    {
                        var cb = CellFor(wx, wz);
                        int v0Start = cb.v0.Count, v1Start = cb.v1.Count;
                        if (!kit)
                        {
                            AddTree(cb.v0, cb.n0, cb.c0, cb.t0, at, rHeight, rVariant, rYaw, rVariant, rHeight, place);
                            if (place) cb.Grow(at, 4f, TreeMaxH);
                        }
                        else if (place)
                        {
                            // Species: palms on the hot sandbanks and the
                            // low ground of a hot island, broadleaves on the
                            // low ground of a temperate one, spruce
                            // everywhere else. Altitude decides within an
                            // island, latitude decides between them.
                            // **The island's one species.** Decided once, at
                            // the top, from latitude and a single roll. It
                            // was a slow field putting groves of conifer next
                            // to groves of broadleaf, and Kevin's verdict is
                            // that it does not make sense -- two species
                            // sharing a 3 ha island is a botanical garden.
                            SceneryKit.Template tp0 = spruce0, tp1 = spruce1;
                            float factor = 1f;
                            if (species == 2) { tp0 = palm0; tp1 = palm1; factor = 0.72f; }
                            else if (species == 1) { tp0 = broad0; tp1 = broad1; factor = 0.82f; }
                            if (individualTrees && species != 2)
                            {
                                bool broad = Mathf.PerlinNoise(wx * .025f + 12f, wz * .025f) > .38f;
                                string id = broad ? (rVariant < .33f ? "Broad" : rVariant < .66f ? "Broad_B" : "Broad_C") : (rVariant < .5f ? "Spruce" : "Spruce_B");
                                tp0 = SceneryKit.Get(id); tp1 = SceneryKit.Get(id + "_LOD1");
                                factor = broad ? .90f : 1.02f;
                            }
                            // Trees inside a closed stand are taller than the
                            // ones out on its edge -- they grew up competing
                            // for the light. It is the cheapest thing that
                            // makes a stand read as a stand from a mile off,
                            // because the canopy gets a SHAPE: high in the
                            // middle, falling away where the wood opens.
                            factor *= Mathf.Lerp(0.82f, 1.07f, sp.cover);
                            float target = Mathf.Lerp(TreeMinH, TreeMaxH, Mathf.Pow(rHeight, HeightBias)) * factor;
                            float s = target / Mathf.Max(1f, tp0.height);
                            float yaw = Wind + (rYaw - 0.5f) * 0.7f;
                            var sc = new Vector3(s, s, s);
                            if (individualTrees)
                                individuals.Add((tp0.name, at, yaw, sc));
                            else StampBoth(cb, tp0, tp1, at, yaw, sc, sc);
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
                    if (individualTrees && place && rRockA < .65f)
                    {
                        var under = SceneryKit.Get(rRockB < .3f ? "Scrub_0" : rRockB < .65f ? "Fern" : "Grass");
                        float ux = wx + 2f + rRockC, uz = wz - 2f;
                        float uy = height(ux, uz);
                        if (under != null && Mathf.Abs(uy - h) < 1f && (keepOut == null || !keepOut(ux, uz)))
                        {
                            var cb = CellFor(ux, uz);
                            var low = SceneryKit.Get(under.name + "_LOD1") ?? under;
                            var scale = Vector3.one * (1f + rScrubA);
                            StampBoth(cb, under, low, new Vector3(ux,uy,uz),rYaw*Mathf.PI*2f,scale,scale);
                            cb.Grow(new Vector3(ux,uy,uz),3f,3f);bushes++;
                        }
                    }
                    trees++;
                }
            }

            // ---- stone on the shore ---------------------------------------
            // Every rock in this class used to be placed inside the TREE
            // walk, which only ever looks at ground a tree could have stood
            // on -- so the one band of an island that is guaranteed to have
            // no wood on it, the beach and the shallows, was also guaranteed
            // to have nothing else. Every shore in the world was bare sand.
            // Kevin asked for stones "around and on some of the islands";
            // this is the "around".
            //
            // Its own walk, at its own step, gated on HEIGHT rather than on
            // whether anything could grow. Clustered, because rock comes in
            // reefs and headlands: an even sprinkle of boulders round an
            // island is a necklace.
            int shoreStones = 0, stacks = 0;
            if (kit && shoreRock > 0f)
            {
                var srng = new System.Random(seed * 337 + 71);
                float sstep = 7f;
                for (float z = -maxR * 1.12f; z <= maxR * 1.12f; z += sstep)
                {
                    for (float x = -maxR * 1.12f; x <= maxR * 1.12f; x += sstep)
                    {
                        float jx3 = (float)(srng.NextDouble() - 0.5f) * sstep * 0.9f;
                        float jz3 = (float)(srng.NextDouble() - 0.5f) * sstep * 0.9f;
                        double rPlace = srng.NextDouble();
                        float rA = (float)srng.NextDouble(), rB = (float)srng.NextDouble();
                        float rC = (float)srng.NextDouble(), rYaw3 = (float)srng.NextDouble();
                        if (shoreStones + stacks > 900) continue;
                        float wx3 = centre.x + x + jx3, wz3 = centre.z + z + jz3;
                        float h3 = height(wx3, wz3);
                        // The band the wood cannot use: from a few metres
                        // under water up to where grass starts.
                        if (h3 < -9f || h3 > sandTop) continue;

                        // Clustered along particular stretches of coast.
                        float reef = Mathf.PerlinNoise((wx3 - 2200f) * 0.014f, (wz3 + 3300f) * 0.014f);
                        float clump = Mathf.SmoothStep(0f, 1f, (reef - 0.46f) / 0.26f) * (0.55f + rockiness);

                        if (h3 < -1.2f)
                        {
                            // Offshore. Mostly nothing; now and then a SEA
                            // STACK, which is the same cliff shard standing
                            // in its own water -- the cheapest landmark there
                            // is, and the reason to give an island a bearing.
                            if (rPlace < 0.006 * clump * shoreRock)
                            {
                                var tp = cliffTp[(int)(rA * cliffTp.Length) % cliffTp.Length];
                                float above = (terrain.graphicArtPalette ? 4f + 6f * rB : 5f + 13f * rB) * cragScale;
                                float hgt = above - h3;                 // it has to reach the seabed
                                float w = (terrain.graphicArtPalette ? 4.5f + 5f * rC : 2.6f + 4.4f * rC) * cragScale;
                                var cb = CellFor(wx3, wz3);
                                var rot = Quaternion.Euler((rA - 0.5f) * 16f, rYaw3 * 360f, (rB - 0.5f) * 14f);
                                var at3 = new Vector3(wx3, h3 + hgt * 0.34f, wz3);
                                StampBoth(cb, tp, tp, at3, rot, new Vector3(w, hgt, w * (0.7f + 0.5f * rC)));
                                cb.Grow(new Vector3(wx3, h3, wz3), w, hgt);
                                stacks++; cliffs++;
                            }
                            continue;
                        }

                        // The tideline itself: boulders, in groups.
                        if (rPlace > 0.30 * clump * shoreRock) continue;
                        {
                            var tp = boulders[(int)(rA * boulders.Length) % boulders.Length];
                            // Bigger than the inland scree: a stone on a
                            // beach has nothing beside it to give it scale
                            // except the beach, so a 1 m one reads as gravel.
                            float size = Mathf.Lerp(SeaSick.World.WorldScale.BoulderMin * 1.6f,
                                                    SeaSick.World.WorldScale.BoulderMax * 2.4f, rB * rB) * cragScale;
                            var cb = CellFor(wx3, wz3);
                            var sc = new Vector3(size * 0.5f, size * 0.5f * (0.6f + 0.6f * rC),
                                                 size * 0.5f * (0.8f + 0.4f * rA));
                            // Sunk a third: a boulder sitting ON sand is a
                            // prop, one buried in it is a boulder.
                            var at3 = new Vector3(wx3, h3 - size * 0.16f, wz3);
                            StampBoth(cb, tp, tp, at3, rYaw3 * Mathf.PI * 2f, sc, sc);
                            cb.Grow(new Vector3(wx3, h3, wz3), size, size);
                            shoreStones++;
                        }
                    }
                }
            }

            // ---- the wheat ------------------------------------------------
            // Its own pass, because a crop mat is 2.5 m across and a tree is
            // 5 m apart: sharing the tree walk would either space the wheat
            // like an orchard or run the whole island at the mat spacing.
            //
            // Two phases. The gate -- is this ground sowable -- costs five
            // height samples because it needs the slope, so it is asked once
            // per PROBE cell; the fill inside an accepted cell costs one
            // sample a mat and is accepted against the field NOISE at the
            // mat's own position, which is what keeps a field's edge the
            // shape of the noise instead of a staircase of probe cells.
            int crops = 0, fieldCells = 0;
            float cropArea = 0f;          // hectares actually under wheat
            float cropCut = 0f;           // the contour the fields were eroded to, 0 if not
            if (hasCrop && farm > 0.02f)
            {
                var crng = new System.Random(seed * 131 + 29);
                float cstep = Mathf.Clamp(terrain != null ? terrain.cropSpacing : 1.95f, 1.2f, 4f);
                int sub = 4;
                float probe = cstep * sub;

                // Pass A finds the fields. The gate -- is this ground sowable
                // -- costs five height samples because it needs the slope, so
                // it is asked once per PROBE cell and never per mat.
                var found = new List<FieldCell>();
                float estimate = 0f;
                for (float z = -maxR; z <= maxR; z += probe)
                {
                    for (float x = -maxR; x <= maxR; x += probe)
                    {
                        float cx = centre.x + x, cz = centre.z + z;
                        float ang = Mathf.Atan2(x, z);
                        if (radiusAt != null && Mathf.Sqrt(x * x + z * z) > radiusAt(ang) * 0.94f) continue;
                        float ch = height(cx, cz);
                        if (ch < sand + 1.4f) continue;
                        float csx = (height(cx + 3f, cz) - height(cx - 3f, cz)) / 6f;
                        float csz = (height(cx, cz + 3f) - height(cx, cz - 3f)) / 6f;
                        float cproud = TerrainHeight.RockBreak(
                            new Unity.Mathematics.float2(cx, cz), rockiness, prm);
                        float gate = FieldGate(ch, Mathf.Sqrt(csx * csx + csz * csz), cproud);
                        if (gate <= 0.04f) continue;
                        float n = FieldNoise(cx, cz);
                        if (n <= 0.02f) continue;
                        found.Add(new FieldCell { x = cx, z = cz, gate = gate, n = n });
                        estimate += Mathf.Min(1f, n * gate * CropFill) * sub * sub;
                    }
                }

                // Over budget, an island gets LESS FIELD -- not thinner
                // field. Two wrong answers were tried first and both are
                // worth remembering, because they are the two obvious ones.
                // Stopping the scan at the cap leaves a straight edge down
                // the island, which is the same fault the tree pass was
                // caught in. Dropping whole cells at random scatters the
                // crop into confetti: the wheat came back as gold specks
                // spread over the entire island instead of as fields, and
                // a field that is not CONTIGUOUS is not a field at all.
                //
                // So erode them from the edges. The cells are sorted by how
                // deep in a field they sit and taken until the budget is
                // spent; the noise value of the last one becomes a contour,
                // and everything outside that contour -- cell and mat alike
                // -- is simply not sown. The fields that survive are whole,
                // there are just fewer and smaller ones. Which is what an
                // island with more good ground than hands to work it would
                // actually look like.
                float nCut = 0f;
                if (estimate > MaxCrops * 0.95f)
                {
                    found.Sort((a, b) => b.n.CompareTo(a.n));
                    float acc = 0f;
                    foreach (var fc in found)
                    {
                        acc += Mathf.Min(1f, fc.n * fc.gate * CropFill) * sub * sub;
                        if (acc > MaxCrops * 0.95f) { nCut = fc.n; break; }
                    }
                    cropCut = nCut;
                }

                foreach (var fc in found)
                {
                    if (fc.n < nCut) continue;
                    bool any = false;
                    for (int iz = 0; iz < sub; iz++)
                    {
                        for (int ix = 0; ix < sub; ix++)
                        {
                            float jx2 = (float)(crng.NextDouble() - 0.5) * cstep * 0.85f;
                            float jz2 = (float)(crng.NextDouble() - 0.5) * cstep * 0.85f;
                            double rKeep = crng.NextDouble();
                            float rYaw2 = (float)crng.NextDouble();
                            float rSize = (float)crng.NextDouble();
                            int v = crng.Next(3);
                            if (crops >= MaxCrops) continue;
                            float mx = fc.x + (ix + 0.5f) * cstep - probe * 0.5f + jx2;
                            float mz = fc.z + (iz + 0.5f) * cstep - probe * 0.5f + jz2;
                            // Accepted against the noise at the MAT's own
                            // position, so a field's edge is the shape of the
                            // noise and not a staircase of probe cells. The
                            // fill factor saturates the middle: at the raw
                            // product the interior of a field accepted about
                            // half its mats and came back as gold speckles on
                            // grass. A crop is DENSE -- that density is most
                            // of what says crop rather than meadow -- so only
                            // the outer edge is allowed to be partial.
                            float mn = FieldNoise(mx, mz);
                            if (mn < nCut || rKeep > mn * fc.gate * CropFill) continue;
                            if (keepOut != null && keepOut(mx, mz)) continue;
                            float mh = height(mx, mz);
                            if (mh < sand + 1.0f) continue;
                            var cb = CellFor(mx, mz);
                            float target = SeaSick.World.WorldScale.CropHeight
                                         * Mathf.Lerp(0.86f, 1.26f, rSize);
                            float ms = target / Mathf.Max(0.2f, crop0[v].height);
                            var msc = new Vector3(ms, ms, ms);
                            // Sunk. The mat's foot flares out under the ground
                            // so its skirt never shows as a slab of earth
                            // standing on the grass -- which is exactly what
                            // it did before it was buried.
                            StampBoth(cb, crop0[v], crop1[v],
                                      new Vector3(mx, mh - 0.12f, mz), rYaw2 * Mathf.PI * 2f, msc, msc);
                            cb.Grow(new Vector3(mx, mh, mz), crop0[v].radius * ms, target);
                            crops++; any = true;
                        }
                    }
                    if (any) { fieldCells++; cropArea += probe * probe / 10000f; }
                }
            }

            int tri0 = 0, tri1 = 0;
            foreach (var cb in cellList) { tri0 += cb.t0.Count / 3; tri1 += cb.t1.Count / 3; }
            float ha = Mathf.Max(0.01f, Mathf.PI * meanR * meanR / 10000f);
            // Trees a hectare is the number to judge a look change by (the
            // reference board is ~230), and `closed` is the new one: the
            // fraction of the island the cover field calls a stand. An
            // island at 230/ha and 100 % closed is the old even carpet.
            float closedPct = coverN > 0 ? 100f * coverDense / coverN : 0f;
            float openPct = coverN > 0 ? 100f * coverOpen / coverN : 0f;
            Debug.Log($"IslandScenery: r{meanR:F0} at ({centre.x:F0},{centre.z:F0}) peak {peak:F0} verdancy {verdancy:F2} "
                + $"rockiness {rockiness:F2} tropical {tropical:F2} farm {farm:F2} accept {accept:F3} keep {keep:F3} cells {cells:F0} step {step:F1} "
                + $"-> {trees} trees ({trees / ha:F0}/ha, stand {closedPct:F0}% / glade {openPct:F0}%), "
                + $"{bushes} scrub, {crops} wheat over {cropArea:F2} ha ({100f * cropArea / ha:F1}% of the island"
                + (cropCut > 0f ? $", eroded to {cropCut:F2}" : "") + "), "
                + $"{rocks} rocks ({cliffs} cliffs), {shoreStones} shore stones, {stacks} sea stacks, {cellList.Count} cells, "
                + $"{tri0} tris LOD0 / {tri1} LOD1{(kit ? "" : " [cones fallback]")}");

            // Replace, never append: the flora tuner re-dresses an island in
            // place, and a Report that grows with every rebake would hand the
            // look probes a stale entry for the same island.
            for (int i = Report.Count - 1; i >= 0; i--)
                if ((Report[i].centre - centre).sqrMagnitude < 1f) Report.RemoveAt(i);
            Report.Add(new Dressed { centre = centre, radius = meanR, trees = trees, crops = crops, scrub = bushes });

            if (index.Count == 0 && individuals.Count == 0 && rocks == 0 && crops == 0 && bushes == 0
                && shoreStones == 0 && stacks == 0) return null;

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
            foreach (var item in individuals)
            {
                var prefab = Resources.Load<GameObject>("IslandAssets/" + item.id);
                if (prefab == null) { Debug.LogError("Missing island asset " + item.id); continue; }
                var tree = Object.Instantiate(prefab, item.at, Quaternion.Euler(0f, item.yaw * Mathf.Rad2Deg, 0f), go.transform);
                tree.transform.localScale = item.scale;
                var group = tree.GetComponent<LODGroup>();
                if (group != null) group.enabled = false; // island-level scheduler owns culling
                var rs = tree.GetComponentsInChildren<MeshRenderer>(true);
                MeshRenderer high = null, low = null;
                foreach (var r in rs) { if (r.name.EndsWith("_LOD1")) low=r; else high=r; }
                index.Add(new SceneryWood.Tree { baseAt=item.at, cell=wcells.Count, instance=tree });
                wcells.Add(new SceneryWood.Cell { r0=high,r1=low,centre=item.at,radius=6f*item.scale.x });
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
            System.Func<float, float, CellBuild> cellFor, ref int cliffs,
            float scale = 1f, float headlandShare = 0.14f)
        {
            var lr = new System.Random(seed * 31 + Mathf.RoundToInt(c.x * 7.3f) * 131 + Mathf.RoundToInt(c.z * 13.1f));
            // Three tiers, not two. Kevin liked the outcrops and asked for
            // BIG versions of them "acting as cliffs" -- and the top tier the
            // formations had was 15 m, which is a stone you walk round rather
            // than a cliff you sail past. A headland is a run of shards two
            // or three times that, and it is rare on purpose: one an island
            // is a landmark, five is a quarry.
            double roll = lr.NextDouble();
            bool head = roll < headlandShare;
            bool big = !head && roll < headlandShare + 0.35;
            int n = head ? 14 + lr.Next(9) : big ? 10 + lr.Next(6) : 3 + lr.Next(5);
            float L = n * (head ? 3.6f : big ? 2.4f : 1.6f) * scale;
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
                float w = (head ? 7f + 14f * core : big ? 3.5f + 6f * core : 1.6f + 2.8f * core) * j * scale;
                float hgt = (head ? 9f + 26f * core : big ? 3.5f + 9f * core : 1.4f + 4f * core)
                          * (0.8f + 0.4f * (float)lr.NextDouble()) * scale;
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
            int ns = 2 + lr.Next(head ? 8 : big ? 5 : 3);
            for (int k = 0; k < ns; k++)
            {
                float a = (float)lr.NextDouble() * Mathf.PI * 2f;
                float r = L * (0.35f + 0.55f * (float)lr.NextDouble());
                float px = c.x + Mathf.Cos(a) * r, pz = c.z + Mathf.Sin(a) * r;
                float ph = height(px, pz);
                if (ph < sand + 0.5f) continue;
                float size = (0.8f + 1.6f * (float)lr.NextDouble()) * (head ? 1.8f : 1f) * scale;
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
            if (SeaSick.World.WorldArtStyle.SceneryOverride != null)
                return SeaSick.World.WorldArtStyle.SceneryOverride;
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
