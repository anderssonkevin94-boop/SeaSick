using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SeaSick.World
{
    /// **Roads that wear in where the villagers walk (2026-09-26).**
    ///
    /// Kevin: roads form on their own between the fire, the stores, the
    /// workplaces, the gates and the pier -- *"just make sure that the roads
    /// don't overlap and glitch or look bad."* No gameplay effect yet; the
    /// seam for a later speed bonus is `RoadAt`.
    ///
    /// **Wear.** Every step a hand takes (`CampWorker.Walk`) adds its length
    /// to the 2 m cell it is in -- the same world lattice `CampPath` routes
    /// on and walls snap to. Wear decays with GAME time (`DecayDays`), so a
    /// path nobody uses grows back.
    ///
    /// **Road set.** A few times per game day (`TickSeconds`) wear becomes a
    /// set of road cells with HYSTERESIS (`OnWear` to appear, `OffWear` to
    /// go), so a road never flickers; only ground a hand can stand on
    /// (`CampPath.RoadGround`: no sea, cliff, rock or wall) and outside every
    /// building's footprint; 1-cell gaps bridged, 1-cell spurs pruned,
    /// pieces under `MinPiece` cells dropped.
    ///
    /// **Drawing -- why it cannot overlap.** Not Astra's modules laid end to
    /// end (a free-form wear graph needs crossroads, short stubs and
    /// arbitrary angles her kit does not have, and every junction would be
    /// two translucent ribbons stacked). Instead ONE mesh per camp: a 0.5 m
    /// lattice sheet draped on the analytic height field, each vertex written
    /// once. v2 (same day, Kevin's screenshots showed staircases and a yard
    /// stain): the road cells are THINNED to centre-lines, cut into chains,
    /// relaxed + given a slow wander + Chaikin-smoothed, and each sheet
    /// vertex stores its distance to the nearest line; the shader turns that
    /// into coverage per pixel with a little edge noise, at the kit's 2.16 m.
    /// A junction is just where two distance fields meet; dead ends taper
    /// like her `Road_End`; a small irregular yard is kept round the fire.
    /// The build is an iterator stepped within `SliceMs` per frame.
    /// Depth: `SeaSick/Worn Road` pulls each vertex toward the camera along
    /// its view ray, so it never z-fights and never floats.
    ///
    /// Persisted in the ledger (`OutpostLedger.roadCell/roadWear`). Lives on
    /// the island GameObject beside `CampPath`, so a scene reload destroys
    /// it; the only statics are tunables and a one-entry lookup cache that
    /// is compared by reference (a destroyed camp never matches).
    [DisallowMultipleComponent]
    public class CampRoads : MonoBehaviour
    {
        // --- tunables ---------------------------------------------------------

        public static bool Enabled = true;
        /// Metres walked through a cell (after decay) before it wears into a
        /// road, and the level it must fall below to grow back. A busy trunk
        /// (a few hands, a trip each every half minute) passes `OnWear` in
        /// about one game day; a path walked once a day never does.
        public static float OnWear = 30f;
        public static float OffWear = 14f;
        /// Wear at which a road is fully trodden (full opacity).
        public static float FullWear = 140f;
        /// e-folding time of wear, in game days.
        public static float DecayDays = 6f;
        /// Real seconds between wear -> road passes (a day is 180 s).
        public static float TickSeconds = 30f;
        public static int MinPiece = 4;
        /// Half width incl. feather (Astra: 2.16 m across), feather, tip.
        public static float HalfWidth = 1.05f;
        public static float Feather = 0.32f;
        public static float TipHalfWidth = 0.2f;
        public static float MinOpacity = 0.5f;
        public static float MaxOpacity = 0.93f;
        public static float Lift = 0.02f;
        public static bool LogBuild = true;

        /// Astra's trampled-earth palette (roads-astra-lvl1-v1 vertex
        /// colours, re-exposed to sit beside the terrain's meadow albedo):
        /// compacted centre, shoulder.
        public static Color CentreColour = new Color(0.52f, 0.40f, 0.25f);
        public static Color EdgeColour = new Color(0.40f, 0.30f, 0.17f);

        public static float LastBuildMs { get; private set; }
        public static int LastVertexCount { get; private set; }

        const float Cell = 2f;           // world lattice (= CampPath.DesiredCell, WallPostStep)
        const int Side = 160;            // cells per side: 320 m, CampPath.MaxCells
        const int Sub = 4;               // lattice vertices per cell edge -> 0.5 m
        const int SubSide = Side * Sub + 1;

        // --- state ------------------------------------------------------------

        Outpost camp;
        int ox, oz;                      // world lattice index of cell (0,0)
        float[] wear;
        bool[] road;                     // hysteresis state
        bool[] draw;                     // cleaned set actually drawn
        bool[] prevDraw;
        byte[] level;                    // opacity bucket per drawn cell
        byte[] prevLevel;
        bool[] footprint;
        int[] comp;
        readonly List<int> stack = new List<int>();
        readonly List<int> drawn = new List<int>();
        bool ready;
        float tick;
        double lastSeconds = -1;
        OutpostLedger loadedFrom;

        GameObject view;
        Mesh mesh;
        static Material material;
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<int> tris = new List<int>();
        readonly List<Vector3> route = new List<Vector3>();

        public static CampRoads For(Outpost camp)
        {
            if (camp == null) return null;
            var r = camp.GetComponent<CampRoads>();
            if (r == null) r = camp.gameObject.AddComponent<CampRoads>();
            r.camp = camp;
            return r;
        }

        // --- wear in ----------------------------------------------------------

        static Outpost lastCamp;
        static CampRoads lastRoads;

        /// A hand stepped from `a` to `b`. Called once per walking frame per
        /// hand: a reference compare, an index and an add.
        public static void Walked(Outpost camp, Vector3 a, Vector3 b)
        {
            if (!Enabled || camp == null) return;
            if (!ReferenceEquals(camp, lastCamp) || lastRoads == null)
            {
                lastCamp = camp;
                lastRoads = For(camp);
            }
            if (lastRoads == null) return;
            lastRoads.Add(0.5f * (a + b), Mathf.Sqrt((b.x - a.x) * (b.x - a.x) + (b.z - a.z) * (b.z - a.z)));
        }

        void Add(Vector3 at, float metres)
        {
            if (!Ensure()) return;
            int i = IndexOf(at);
            if (i >= 0) wear[i] += metres;
        }

        /// **The speed-bonus seam.** 0 off-road, up to 1 on a fully trodden
        /// road. Nothing reads it yet.
        public static float RoadAt(Outpost camp, Vector3 at)
        {
            var r = camp != null ? camp.GetComponent<CampRoads>() : null;
            if (r == null || !r.ready) return 0f;
            int i = r.IndexOf(at);
            if (i < 0 || !r.draw[i]) return 0f;
            return Mathf.Clamp01(r.wear[i] / FullWear);
        }

        // --- grid -------------------------------------------------------------

        bool Ensure()
        {
            if (ready) return true;
            if (camp == null) camp = GetComponent<Outpost>();
            if (camp == null || !camp.Sited || camp.Ledger == null) return false;
            Vector3 c = camp.CampCentre;
            ox = Mathf.RoundToInt(c.x / Cell) - Side / 2;
            oz = Mathf.RoundToInt(c.z / Cell) - Side / 2;
            int count = Side * Side;
            wear = new float[count];
            road = new bool[count];
            draw = new bool[count];
            prevDraw = new bool[count];
            level = new byte[count];
            prevLevel = new byte[count];
            footprint = new bool[count];
            comp = new int[count];
            building = null;
            buildPending = false;
            ready = true;
            Load(camp.Ledger);
            lastSeconds = TimeOfDay.Seconds;
            tick = 0.5f;                 // first pass right after load
            return true;
        }

        int IndexOf(Vector3 at)
        {
            int x = Mathf.RoundToInt(at.x / Cell) - ox;
            int z = Mathf.RoundToInt(at.z / Cell) - oz;
            if (x < 0 || z < 0 || x >= Side || z >= Side) return -1;
            return z * Side + x;
        }

        Vector3 CentreOf(int i) => new Vector3((ox + i % Side) * Cell, 0f, (oz + i / Side) * Cell);

        public static int Pack(int lx, int lz) => (lx & 0xFFFF) | (lz << 16);
        static void Unpack(int key, out int lx, out int lz)
        {
            lx = (short)(key & 0xFFFF);
            lz = key >> 16;
        }

        void Load(OutpostLedger l)
        {
            loadedFrom = l;
            if (l.roadCell == null || l.roadWear == null) return;
            int k = Mathf.Min(l.roadCell.Count, l.roadWear.Count);
            for (int j = 0; j < k; j++)
            {
                Unpack(l.roadCell[j], out int lx, out int lz);
                int x = lx - ox, z = lz - oz;
                if (x < 0 || z < 0 || x >= Side || z >= Side) continue;
                int i = z * Side + x;
                float w = l.roadWear[j];
                wear[i] = Mathf.Abs(w);
                road[i] = w < 0f;
            }
        }

        void Save(OutpostLedger l)
        {
            if (l.roadCell == null) l.roadCell = new List<int>();
            if (l.roadWear == null) l.roadWear = new List<float>();
            l.roadCell.Clear();
            l.roadWear.Clear();
            for (int i = 0; i < wear.Length; i++)
            {
                if (wear[i] < 1f) continue;
                l.roadCell.Add(Pack(ox + i % Side, oz + i / Side));
                l.roadWear.Add(Mathf.Round(wear[i] * 10f) / 10f * (road[i] ? -1f : 1f));
            }
        }

        // --- tick -------------------------------------------------------------

        void Update()
        {
            if (!Enabled || !Ensure()) return;
            if (camp.Ledger != loadedFrom)
            {
                // The books were swapped under us (a load without a scene
                // reload): start again from the new ones.
                ready = false;
                if (!Ensure()) return;
            }
            StepBuild(false);
            tick -= Time.unscaledDeltaTime;
            if (tick > 0f) return;
            tick = TickSeconds;
            Pass();
        }

        /// Decay, classify, clean, and rebuild the mesh if the drawn picture
        /// changed. Also writes the ledger rows.
        public void Pass()
        {
            if (!Ensure()) return;
            double now = TimeOfDay.Seconds;
            float days = lastSeconds >= 0 && TimeOfDay.DayLength > 0f
                ? Mathf.Max(0f, (float)((now - lastSeconds) / TimeOfDay.DayLength)) : 0f;
            lastSeconds = now;
            if (days > 0f)
            {
                float k = Mathf.Exp(-days / Mathf.Max(0.01f, DecayDays));
                for (int i = 0; i < wear.Length; i++) wear[i] *= k;
            }
            Classify();
            Save(camp.Ledger);
            if (Changed()) Rebuild();
        }

        void Classify()
        {
            var map = CampPath.For(camp);
            MarkFootprints();
            for (int i = 0; i < wear.Length; i++)
            {
                float w = wear[i];
                if (road[i]) road[i] = w >= OffWear;
                else if (w >= OnWear) road[i] = true;
            }

            // Drawable: road, on ground a hand can stand on, not under a
            // building. `RoadGround` is only asked about road cells.
            for (int i = 0; i < wear.Length; i++)
                draw[i] = road[i] && !footprint[i] && map != null && map.RoadGround(CentreOf(i));

            // Bridge one-cell gaps between two road cells in a line.
            for (int z = 1; z < Side - 1; z++)
                for (int x = 1; x < Side - 1; x++)
                {
                    int i = z * Side + x;
                    if (draw[i] || footprint[i] || wear[i] < 0.5f * OffWear) continue;
                    bool bridge = (draw[i - 1] && draw[i + 1]) || (draw[i - Side] && draw[i + Side])
                        || (draw[i - Side - 1] && draw[i + Side + 1]) || (draw[i - Side + 1] && draw[i + Side - 1]);
                    if (bridge && map != null && map.RoadGround(CentreOf(i))) draw[i] = true;
                }

            // Close holes: a cell mostly surrounded by road is trampled too
            // (parallel routes a cell apart read as one broad way, not a
            // lattice with grass windows in it).
            System.Array.Clear(comp, 0, comp.Length);
            for (int z = 1; z < Side - 1; z++)
                for (int x = 1; x < Side - 1; x++)
                {
                    int i = z * Side + x;
                    if (draw[i] || footprint[i]) continue;
                    int k = 0;
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                            if (draw[i + dx + dz * Side]) k++;
                    if (k >= 5 && map != null && map.RoadGround(CentreOf(i))) comp[i] = 1;
                }
            for (int i = 0; i < draw.Length; i++) if (comp[i] != 0) draw[i] = true;

            // Prune one-cell spurs: an end cell whose only neighbour is a
            // junction is a corner a hand cut, not a road.
            for (int i = 0; i < draw.Length; i++)
            {
                if (!draw[i] || Degree(i) != 1) continue;
                int nb = OnlyNeighbour(i);
                if (nb >= 0 && Degree(nb) >= 3) draw[i] = false;
            }

            // Drop small pieces.
            System.Array.Clear(comp, 0, comp.Length);
            for (int i = 0; i < draw.Length; i++)
            {
                if (!draw[i] || comp[i] != 0) continue;
                stack.Clear();
                drawn.Clear();
                stack.Add(i);
                comp[i] = 1;
                while (stack.Count > 0)
                {
                    int c = stack[stack.Count - 1];
                    stack.RemoveAt(stack.Count - 1);
                    drawn.Add(c);
                    int cx = c % Side, cz = c / Side;
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = cx + dx, nz = cz + dz;
                            if (nx < 0 || nz < 0 || nx >= Side || nz >= Side) continue;
                            int j = nz * Side + nx;
                            if (draw[j] && comp[j] == 0) { comp[j] = 1; stack.Add(j); }
                        }
                }
                if (drawn.Count < MinPiece)
                    foreach (int c in drawn) draw[c] = false;
            }

            drawn.Clear();
            for (int i = 0; i < draw.Length; i++)
            {
                if (!draw[i]) { level[i] = 0; continue; }
                drawn.Add(i);
                float s = Mathf.Clamp01((wear[i] - OffWear) / Mathf.Max(1f, FullWear - OffWear));
                level[i] = (byte)(1 + Mathf.RoundToInt(s * 6f));
            }
        }

        void MarkFootprints()
        {
            System.Array.Clear(footprint, 0, footprint.Length);
            var built = camp.Built;
            for (int b = 0; b < built.Count; b++)
            {
                var bd = built[b];
                if (bd == null || bd is WallSegment) continue;
                Vector2 f = bd.Footprint;
                if (f.x <= 0f || f.y <= 0f) continue;
                Transform t = bd.transform;
                float hx = 0.5f * f.x + 0.4f, hz = 0.5f * f.y + 0.4f;
                float reach = Mathf.Sqrt(hx * hx + hz * hz);
                Vector3 p = t.position;
                Vector3 r = t.right; r.y = 0f; r.Normalize();
                Vector3 fw = t.forward; fw.y = 0f; fw.Normalize();
                int x0 = Mathf.FloorToInt((p.x - reach) / Cell) - ox, x1 = Mathf.CeilToInt((p.x + reach) / Cell) - ox;
                int z0 = Mathf.FloorToInt((p.z - reach) / Cell) - oz, z1 = Mathf.CeilToInt((p.z + reach) / Cell) - oz;
                for (int z = Mathf.Max(0, z0); z <= Mathf.Min(Side - 1, z1); z++)
                    for (int x = Mathf.Max(0, x0); x <= Mathf.Min(Side - 1, x1); x++)
                    {
                        Vector3 d = CentreOf(z * Side + x) - p;
                        d.y = 0f;
                        if (Mathf.Abs(Vector3.Dot(d, r)) <= hx && Mathf.Abs(Vector3.Dot(d, fw)) <= hz)
                            footprint[z * Side + x] = true;
                    }
            }
        }

        /// Road adjacency: the four sides always; a diagonal only where
        /// neither shared side cell is road (else the two sides carry it and
        /// the diagonal would only thicken the corner).
        bool Linked(int i, int dx, int dz)
        {
            int x = i % Side + dx, z = i / Side + dz;
            if (x < 0 || z < 0 || x >= Side || z >= Side) return false;
            if (!draw[z * Side + x]) return false;
            if (dx == 0 || dz == 0) return true;
            return !draw[i + dx] && !draw[i + dz * Side];
        }

        int Degree(int i)
        {
            int d = 0;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                    if ((dx != 0 || dz != 0) && Linked(i, dx, dz)) d++;
            return d;
        }

        int OnlyNeighbour(int i)
        {
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                    if ((dx != 0 || dz != 0) && Linked(i, dx, dz)) return i + dx + dz * Side;
            return -1;
        }

        bool Changed()
        {
            bool changed = false;
            for (int i = 0; i < draw.Length; i++)
                if (draw[i] != prevDraw[i] || level[i] != prevLevel[i]) { changed = true; break; }
            if (!changed) return false;
            System.Array.Copy(draw, prevDraw, draw.Length);
            System.Array.Copy(level, prevLevel, level.Length);
            return true;
        }

        // --- drawing: centre-lines ---------------------------------------------
        //
        // v2 (2026-09-26, Kevin's screenshots): the v1 field was a max over
        // capsules between neighbouring 2 m cells, and a union of cell-sized
        // capsules IS a staircase -- every diagonal drew as steps and a busy
        // yard drew as one big stain. Now the cleaned cell set is THINNED to
        // centre-lines (Zhang-Suen), cut into chains between junctions and
        // ends, jittered a little and Chaikin-smoothed, and the sheet stores
        // each vertex's DISTANCE to the nearest smoothed line. The shader turns
        // that distance into coverage per pixel (with a little edge noise), so
        // an edge is a smooth curve at any zoom and the width is the kit's
        // everywhere; only a small trodden yard stays round the fire.

        /// Kit width: 2.16 m across incl. feather (1.08 half), ~1.76 m core.
        public static float Wobble = 0.34f;          // edge noise, metres peak-to-peak
        public static float Jitter = 0.6f;           // centre-line wander, metres (peak)
        public static float TaperLength = 2.6f;      // dead ends taper over this
        public static float YardRadius = 2.8f;       // packed earth round the fire
        public static int SpurCells = 2;             // thinning spurs this short are cut
        public static float SliceMs = 2.5f;          // main-thread budget per frame

        public static float LastMaxSliceMs { get; private set; }
        public static int LastSlices { get; private set; }

        const float Step = Cell / Sub;               // 0.5 m sheet lattice
        const float FarD = 9f;

        struct Seg { public float ax, az, bx, bz, sa, sb, oa, ob; }

        bool[] skel;
        readonly List<Seg> segs = new List<Seg>();
        readonly List<int> chain = new List<int>();
        readonly List<Vector2> pts = new List<Vector2>();
        readonly List<Vector2> pts2 = new List<Vector2>();
        readonly List<float> ps = new List<float>(), ps2 = new List<float>();
        readonly HashSet<long> seenEdge = new HashSet<long>();
        float[] hCache;                               // per lattice vertex, NaN = unknown
        readonly List<Vector2> uvs = new List<Vector2>();

        // Scratch shared by every camp (one sheet builds at a time).
        static float[] sD, sS;
        static int[] sIdx;
        static readonly List<int> sTouched = new List<int>();
        static CampRoads sOwner;

        System.Collections.IEnumerator building;
        bool buildPending;
        float buildWork, buildMax;
        int buildSlices;

        float Strength(int i) => Mathf.Lerp(MinOpacity, MaxOpacity, (Mathf.Max(1, (int)level[i]) - 1) / 6f);

        void Rebuild()
        {
            buildPending = true;
            building = null;
            if (ReferenceEquals(sOwner, this)) sOwner = null;
        }

        /// Runs the pending build within `SliceMs` per frame. `all` finishes
        /// it now (dev hooks).
        void StepBuild(bool all)
        {
            if (building == null)
            {
                if (!buildPending) return;
                if (sOwner != null && !ReferenceEquals(sOwner, this) && sOwner.building != null)
                {
                    if (!all) return;                  // another camp is mid-build: wait a frame
                    sOwner.StepBuild(true);
                }
                buildPending = false;
                sOwner = this;
                building = Build();
                buildWork = 0f; buildMax = 0f; buildSlices = 0;
            }
            var w = System.Diagnostics.Stopwatch.StartNew();
            do
            {
                w.Restart();
                bool more = building.MoveNext();
                float ms = (float)w.Elapsed.TotalMilliseconds;
                buildWork += ms; buildMax = Mathf.Max(buildMax, ms); buildSlices++;
                if (!more)
                {
                    building = null;
                    if (ReferenceEquals(sOwner, this)) sOwner = null;
                    LastBuildMs = buildWork; LastMaxSliceMs = buildMax; LastSlices = buildSlices;
                    if (LogBuild)
                        Debug.Log($"[CampRoads] {camp.name}: {drawn.Count} road cells, {segs.Count} segs, {verts.Count} verts, {tris.Count / 3} tris, "
                            + $"{buildWork:0.0} ms work in {buildSlices} slices, worst {buildMax:0.00} ms");
                    return;
                }
            } while (all);
        }

        /// Finish any pending sheet now (dev / probes).
        public void FinishBuild() => StepBuild(true);

        System.Collections.IEnumerator Build()
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            float hw = HalfWidth;

            // ---- 1. thin the drawn cells to centre-lines --------------------
            if (skel == null) skel = new bool[Side * Side];
            System.Array.Clear(skel, 0, skel.Length);
            int bx0 = Side, bz0 = Side, bx1 = -1, bz1 = -1;
            foreach (int c in drawn)
            {
                skel[c] = true;
                int x = c % Side, z = c / Side;
                if (x < bx0) bx0 = x; if (x > bx1) bx1 = x;
                if (z < bz0) bz0 = z; if (z > bz1) bz1 = z;
            }
            segs.Clear();
            if (drawn.Count > 0)
            {
                bx0 = Mathf.Max(1, bx0); bz0 = Mathf.Max(1, bz0);
                bx1 = Mathf.Min(Side - 2, bx1); bz1 = Mathf.Min(Side - 2, bz1);
                Thin(bx0, bz0, bx1, bz1);
                if (clock.Elapsed.TotalMilliseconds > SliceMs) { yield return null; clock.Restart(); }
                for (int k = 0; k < 2; k++) PruneSpurs();
                if (clock.Elapsed.TotalMilliseconds > SliceMs) { yield return null; clock.Restart(); }

                // ---- 2. chains -> smoothed polylines -> segments --------------
                TraceChains();
            }

            // The fire yard: a modest trodden disc, only if roads reach it.
            Vector3 fire = camp.CampCentre;
            float yardS = 0f;
            foreach (int c in drawn)
            {
                Vector3 p = CentreOf(c);
                float dx = p.x - fire.x, dz = p.z - fire.z;
                if (dx * dx + dz * dz <= (YardRadius + Cell) * (YardRadius + Cell)) yardS = Mathf.Max(yardS, Strength(c));
            }
            if (yardS > 0f)
            {
                // Not a disc (that read as a rug): four overlapping blobs
                // round the fire, placed by a hash of the fire's cell.
                int fx = Mathf.RoundToInt(fire.x / Cell), fz = Mathf.RoundToInt(fire.z / Cell);
                for (int n = 0; n < 4; n++)
                {
                    float ang = (n + 0.6f * Hash(fx + n * 17, fz - n * 5)) * Mathf.PI * 0.5f;
                    float off = 0.35f * YardRadius * (0.6f + 0.6f * Hash(fx - n * 3, fz + n * 23));
                    float rr = YardRadius * (0.62f + 0.22f * Hash(fx + n * 7, fz + n * 13));
                    float cx = fire.x + Mathf.Cos(ang) * off, cz = fire.z + Mathf.Sin(ang) * off;
                    float ys = yardS * 0.85f;
                    segs.Add(new Seg { ax = cx, az = cz, bx = cx, bz = cz, sa = ys, sb = ys, oa = hw - rr, ob = hw - rr });
                }
            }
            if (clock.Elapsed.TotalMilliseconds > SliceMs) { yield return null; clock.Restart(); }

            // ---- 3. distance + strength per 0.5 m sheet vertex ---------------
            int count = SubSide * SubSide;
            if (sD == null || sD.Length != count)
            {
                sD = new float[count]; sS = new float[count]; sIdx = new int[count];
                for (int k = 0; k < count; k++) { sD[k] = FarD; sIdx[k] = -1; }
                sTouched.Clear();
                yield return null; clock.Restart();          // first build only: 5 MB of scratch
            }
            foreach (int k in sTouched) { sD[k] = FarD; sS[k] = 0f; sIdx[k] = -1; }
            sTouched.Clear();
            if (hCache == null || hCache.Length != count)
            {
                hCache = new float[count];
                for (int k = 0; k < count; k++) hCache[k] = float.NaN;
                yield return null; clock.Restart();
            }
            float baseX = (ox - 0.5f) * Cell, baseZ = (oz - 0.5f) * Cell;
            float reach = hw + 1.3f;
            int umin = SubSide, vmin = SubSide, umax = -1, vmax = -1;
            for (int n = 0; n < segs.Count; n++)
            {
                Seg sg = segs[n];
                float grow = reach - Mathf.Min(sg.oa, sg.ob);
                int u0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(sg.ax, sg.bx) - grow - baseX) / Step));
                int u1 = Mathf.Min(SubSide - 1, Mathf.CeilToInt((Mathf.Max(sg.ax, sg.bx) + grow - baseX) / Step));
                int v0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(sg.az, sg.bz) - grow - baseZ) / Step));
                int v1 = Mathf.Min(SubSide - 1, Mathf.CeilToInt((Mathf.Max(sg.az, sg.bz) + grow - baseZ) / Step));
                float ex = sg.bx - sg.ax, ez = sg.bz - sg.az;
                float ll = ex * ex + ez * ez;
                for (int v = v0; v <= v1; v++)
                {
                    float wz = baseZ + v * Step;
                    for (int u = u0; u <= u1; u++)
                    {
                        float wx = baseX + u * Step;
                        float t = ll > 1e-6f ? Mathf.Clamp01(((wx - sg.ax) * ex + (wz - sg.az) * ez) / ll) : 0f;
                        float px = sg.ax + ex * t - wx, pz = sg.az + ez * t - wz;
                        float d = Mathf.Sqrt(px * px + pz * pz) + (sg.oa + (sg.ob - sg.oa) * t);
                        if (d >= reach) continue;
                        int k = v * SubSide + u;
                        if (sIdx[k] == -1)
                        {
                            sIdx[k] = -2;
                            sTouched.Add(k);
                            if (u < umin) umin = u; if (u > umax) umax = u;
                            if (v < vmin) vmin = v; if (v > vmax) vmax = v;
                        }
                        if (d < sD[k]) sD[k] = d;
                        float s = (sg.sa + (sg.sb - sg.sa) * t) * Mathf.Clamp01((reach - d) / 1.3f);
                        if (s > sS[k]) sS[k] = s;
                    }
                }
                if ((n & 15) == 15 && clock.Elapsed.TotalMilliseconds > SliceMs) { yield return null; clock.Restart(); }
            }

            // ---- 4. the sheet: only quads that can show ----------------------
            verts.Clear(); uvs.Clear(); tris.Clear();
            Vector3 origin = view != null ? view.transform.position : camp.CampCentre;
            float show = hw + 0.5f * Wobble + 0.05f;
            for (int v = vmin; v < vmax; v++)
            {
                for (int u = umin; u < umax; u++)
                {
                    int k = v * SubSide + u;
                    if (sIdx[k] == -1 && sIdx[k + 1] == -1 && sIdx[k + SubSide] == -1 && sIdx[k + SubSide + 1] == -1) continue;
                    if (Mathf.Min(Mathf.Min(sD[k], sD[k + 1]), Mathf.Min(sD[k + SubSide], sD[k + SubSide + 1])) >= show) continue;
                    int a = SheetVertex(u, v, baseX, baseZ, origin), b = SheetVertex(u + 1, v, baseX, baseZ, origin);
                    int d0 = SheetVertex(u, v + 1, baseX, baseZ, origin), e = SheetVertex(u + 1, v + 1, baseX, baseZ, origin);
                    // Clockwise seen from above (Unity front face); split
                    // along the diagonal whose ends are closer in distance so
                    // a curved edge does not zig-zag across quads.
                    if (Mathf.Abs(sD[k] - sD[k + SubSide + 1]) <= Mathf.Abs(sD[k + 1] - sD[k + SubSide]))
                    { tris.Add(a); tris.Add(d0); tris.Add(e); tris.Add(a); tris.Add(e); tris.Add(b); }
                    else
                    { tris.Add(a); tris.Add(d0); tris.Add(b); tris.Add(b); tris.Add(d0); tris.Add(e); }
                    // Fresh ground heights are the dear part (first build):
                    // stop mid-row when the budget is gone.
                    if ((u & 7) == 7 && clock.Elapsed.TotalMilliseconds > SliceMs) { yield return null; clock.Restart(); }
                }
            }

            // ---- 5. upload (the only step that must be one frame) ------------
            EnsureView();
            mesh.Clear();
            mesh.indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0, true);
            mesh.RecalculateNormals();
            var mr = view.GetComponent<MeshRenderer>();
            mr.enabled = tris.Count > 0;
            var m = mr.sharedMaterial;
            if (m != null)
            {
                if (m.HasProperty("_HalfWidth")) m.SetFloat("_HalfWidth", HalfWidth);
                if (m.HasProperty("_Feather")) m.SetFloat("_Feather", Feather);
                if (m.HasProperty("_Wobble")) m.SetFloat("_Wobble", Wobble);
                // Raw values, as v1's vertex colours were (no sRGB->linear):
                // the palette was tuned against the meadow that way.
                if (m.HasProperty("_CentreColour")) m.SetVector("_CentreColour", (Vector4)CentreColour);
                if (m.HasProperty("_EdgeColour")) m.SetVector("_EdgeColour", (Vector4)EdgeColour);
            }
            CopyTerrainLight(m);
            LastVertexCount = verts.Count;
        }

        int SheetVertex(int u, int v, float baseX, float baseZ, Vector3 origin)
        {
            int k = v * SubSide + u;
            int idx = sIdx[k];
            if (idx >= 0) return idx;
            float wx = baseX + u * Step, wz = baseZ + v * Step;
            float h = hCache[k];
            if (float.IsNaN(h)) { h = camp.GroundAt(new Vector3(wx, 0f, wz)); hCache[k] = h; }
            idx = verts.Count;
            verts.Add(new Vector3(wx - origin.x, h + Lift - origin.y, wz - origin.z));
            uvs.Add(new Vector2(Mathf.Min(sD[k], FarD), sS[k]));
            if (sIdx[k] == -1) sTouched.Add(k);
            sIdx[k] = idx;
            return idx;
        }

        // --- thinning ----------------------------------------------------------

        /// Zhang-Suen: peel boundary cells in two alternating sub-passes until
        /// only 8-connected centre-lines remain. Endpoints survive, so a road
        /// keeps its length; a broad yard becomes the paths that cross it.
        void Thin(int x0, int z0, int x1, int z1)
        {
            stack.Clear();
            bool changed = true;
            int guard = 0;
            while (changed && guard++ < 64)
            {
                changed = false;
                for (int pass = 0; pass < 2; pass++)
                {
                    stack.Clear();
                    for (int z = z0; z <= z1; z++)
                        for (int x = x0; x <= x1; x++)
                        {
                            int i = z * Side + x;
                            if (!skel[i]) continue;
                            bool p2 = skel[i + Side], p3 = skel[i + Side + 1], p4 = skel[i + 1], p5 = skel[i - Side + 1];
                            bool p6 = skel[i - Side], p7 = skel[i - Side - 1], p8 = skel[i - 1], p9 = skel[i + Side - 1];
                            int b = (p2 ? 1 : 0) + (p3 ? 1 : 0) + (p4 ? 1 : 0) + (p5 ? 1 : 0) + (p6 ? 1 : 0) + (p7 ? 1 : 0) + (p8 ? 1 : 0) + (p9 ? 1 : 0);
                            if (b < 2 || b > 6) continue;
                            int a = (!p2 && p3 ? 1 : 0) + (!p3 && p4 ? 1 : 0) + (!p4 && p5 ? 1 : 0) + (!p5 && p6 ? 1 : 0)
                                  + (!p6 && p7 ? 1 : 0) + (!p7 && p8 ? 1 : 0) + (!p8 && p9 ? 1 : 0) + (!p9 && p2 ? 1 : 0);
                            if (a != 1) continue;
                            if (pass == 0) { if ((p2 && p4 && p6) || (p4 && p6 && p8)) continue; }
                            else { if ((p2 && p4 && p8) || (p2 && p6 && p8)) continue; }
                            stack.Add(i);
                        }
                    foreach (int i in stack) skel[i] = false;
                    if (stack.Count > 0) changed = true;
                }
            }
        }

        bool SkelLinked(int i, int dx, int dz)
        {
            int x = i % Side + dx, z = i / Side + dz;
            if (x < 0 || z < 0 || x >= Side || z >= Side) return false;
            if (!skel[z * Side + x]) return false;
            if (dx == 0 || dz == 0) return true;
            return !skel[i + dx] && !skel[i + dz * Side];
        }

        int SkelDegree(int i)
        {
            int d = 0;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                    if ((dx != 0 || dz != 0) && SkelLinked(i, dx, dz)) d++;
            return d;
        }

        int SkelNext(int i, int notThis)
        {
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if ((dx == 0 && dz == 0) || !SkelLinked(i, dx, dz)) continue;
                    int j = i + dx + dz * Side;
                    if (j != notThis) return j;
                }
            return -1;
        }

        /// Cut thinning whiskers: an end that reaches a junction within
        /// `SpurCells` cells is a bump on a broad yard's edge, not a road.
        void PruneSpurs()
        {
            foreach (int start in drawn)
            {
                if (!skel[start] || SkelDegree(start) != 1) continue;
                chain.Clear();
                int prev = -1, cur = start;
                while (cur >= 0 && chain.Count <= SpurCells)
                {
                    int deg = SkelDegree(cur);
                    if (deg >= 3) break;
                    chain.Add(cur);
                    int nx = SkelNext(cur, prev);
                    prev = cur; cur = nx;
                }
                if (cur >= 0 && chain.Count <= SpurCells && SkelDegree(cur) >= 3)
                    foreach (int c in chain) skel[c] = false;
            }
        }

        static long EdgeKey(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        void TraceChains()
        {
            seenEdge.Clear();
            System.Array.Clear(comp, 0, comp.Length);
            // Open chains, from every end and junction.
            foreach (int s in drawn)
            {
                if (!skel[s]) continue;
                int deg = SkelDegree(s);
                if (deg == 2 || deg == 0) continue;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if ((dx == 0 && dz == 0) || !SkelLinked(s, dx, dz)) continue;
                        int j = s + dx + dz * Side;
                        if (!seenEdge.Add(EdgeKey(s, j))) continue;
                        chain.Clear();
                        chain.Add(s);
                        int prev = s, cur = j;
                        while (true)
                        {
                            chain.Add(cur);
                            comp[cur] = 1;
                            if (SkelDegree(cur) != 2) break;
                            int nx = SkelNext(cur, prev);
                            if (nx < 0 || !seenEdge.Add(EdgeKey(cur, nx))) break;
                            prev = cur; cur = nx;
                        }
                        comp[s] = 1;
                        EmitChain(deg <= 1, SkelDegree(chain[chain.Count - 1]) <= 1, false);
                    }
            }
            // Closed loops (every cell degree 2).
            foreach (int s in drawn)
            {
                if (!skel[s] || comp[s] != 0 || SkelDegree(s) != 2) continue;
                chain.Clear();
                int prev = -1, cur = s;
                while (cur >= 0 && comp[cur] == 0)
                {
                    comp[cur] = 1;
                    chain.Add(cur);
                    int nx = SkelNext(cur, prev);
                    prev = cur; cur = nx;
                }
                chain.Add(s);
                EmitChain(false, false, true);
            }
        }

        /// One chain of skeleton cells -> jittered, Chaikin-smoothed polyline
        /// -> segments with strength and end taper.
        void EmitChain(bool openStart, bool openEnd, bool loop)
        {
            if (chain.Count < 2) return;
            pts.Clear(); ps.Clear();
            for (int n = 0; n < chain.Count; n++)
            {
                int c = chain[n];
                int lx = ox + c % Side, lz = oz + c / Side;
                float x = lx * Cell, z = lz * Cell;
                bool pinned = !loop && (n == 0 || n == chain.Count - 1) && !(n == 0 ? openStart : openEnd);
                if (!pinned)
                {
                    // A slow wander (~12 m wavelength), the same every build
                    // and every load: a per-cell wiggle read as a worm.
                    x += (Noise(x * 0.08f + 3.1f, z * 0.08f - 7.7f) - 0.5f) * 2f * Jitter;
                    z += (Noise(x * 0.08f - 11.3f, z * 0.08f + 5.9f) - 0.5f) * 2f * Jitter;
                }
                pts.Add(new Vector2(x, z));
                ps.Add(Strength(c));
            }
            // Relax the lattice out of it first (a 30-degree line on 2 m
            // cells is a run of axis and diagonal steps): Laplacian rounds,
            // ends held.
            for (int round = 0; round < 4; round++)
            {
                pts2.Clear(); pts2.AddRange(pts);
                for (int n = 1; n < pts.Count - 1; n++)
                    pts[n] = 0.5f * pts2[n] + 0.25f * (pts2[n - 1] + pts2[n + 1]);
            }
            // Chaikin, three rounds, ends kept (a loop is closed already).
            for (int round = 0; round < 3; round++)
            {
                pts2.Clear(); ps2.Clear();
                pts2.Add(pts[0]); ps2.Add(ps[0]);
                for (int n = 0; n < pts.Count - 1; n++)
                {
                    Vector2 a = pts[n], b = pts[n + 1];
                    float sa = ps[n], sb = ps[n + 1];
                    pts2.Add(Vector2.Lerp(a, b, 0.25f)); ps2.Add(Mathf.Lerp(sa, sb, 0.25f));
                    pts2.Add(Vector2.Lerp(a, b, 0.75f)); ps2.Add(Mathf.Lerp(sa, sb, 0.75f));
                }
                pts2.Add(pts[pts.Count - 1]); ps2.Add(ps[ps.Count - 1]);
                pts.Clear(); pts.AddRange(pts2);
                ps.Clear(); ps.AddRange(ps2);
            }
            // Arc length for the taper at open ends.
            float total = 0f;
            for (int n = 1; n < pts.Count; n++) total += Vector2.Distance(pts[n - 1], pts[n]);
            float along = 0f, taperDepth = HalfWidth - TipHalfWidth;
            float prevOff = Off(0f, total, openStart, openEnd, taperDepth);
            for (int n = 1; n < pts.Count; n++)
            {
                along += Vector2.Distance(pts[n - 1], pts[n]);
                float off = Off(along, total, openStart, openEnd, taperDepth);
                segs.Add(new Seg
                {
                    ax = pts[n - 1].x, az = pts[n - 1].y, bx = pts[n].x, bz = pts[n].y,
                    sa = ps[n - 1], sb = ps[n], oa = prevOff, ob = off,
                });
                prevOff = off;
            }
        }

        static float Off(float along, float total, bool openStart, bool openEnd, float depth)
        {
            float t = 1f;
            if (openStart) t = Mathf.Min(t, along / TaperLength);
            if (openEnd) t = Mathf.Min(t, (total - along) / TaperLength);
            t = Mathf.Clamp01(t);
            return depth * (1f - t * t * (3f - 2f * t));
        }

        static float Hash(int x, int z)
        {
            uint h = (uint)(x * 374761393 + z * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
        }

        static float Noise(float x, float z)
        {
            int ix = Mathf.FloorToInt(x), iz = Mathf.FloorToInt(z);
            float fx = x - ix, fz = z - iz;
            fx = fx * fx * (3f - 2f * fx);
            fz = fz * fz * (3f - 2f * fz);
            float a = Mathf.Lerp(Hash(ix, iz), Hash(ix + 1, iz), fx);
            float b = Mathf.Lerp(Hash(ix, iz + 1), Hash(ix + 1, iz + 1), fx);
            return Mathf.Lerp(a, b, fz);
        }

        void EnsureView()
        {
            if (view != null) return;
            view = new GameObject("WornRoads");
            view.transform.SetParent(transform, false);
            view.transform.position = camp.CampCentre;
            view.transform.rotation = Quaternion.identity;
            mesh = new Mesh { name = "WornRoads" };
            mesh.MarkDynamic();
            view.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = view.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = true;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            mr.sharedMaterial = Material();
        }

        void OnDestroy()
        {
            if (ReferenceEquals(sOwner, this)) sOwner = null;
            if (mesh != null) Destroy(mesh);
        }

        /// Shared by every camp. `Shader.Find` is safe in a player because
        /// `Resources/Shaders/Keepalive/Keep_WornRoad.mat` ships the shader.
        static Material Material()
        {
            if (material != null) return material;
            var sh = Shader.Find("SeaSick/Worn Road");
            if (sh == null) { Debug.LogError("[CampRoads] SeaSick/Worn Road shader missing"); sh = Shader.Find("Universal Render Pipeline/Unlit"); }
            material = new Material(sh) { name = "WornRoads (runtime)" };
            return material;
        }

        /// Light the dirt the way this island lights its ground: copy the
        /// terrain material's band settings off whatever is under the fire.
        void CopyTerrainLight(Material m)
        {
            if (m == null || !m.HasProperty("_GraphicLight")) return;
            Vector3 c = camp.CampCentre;
            if (!Physics.Raycast(new Vector3(c.x, c.y + 60f, c.z), Vector3.down, out var hit, 200f,
                    ~0, QueryTriggerInteraction.Ignore))
                return;
            var r = hit.collider.GetComponent<Renderer>();
            var tm = r != null ? r.sharedMaterial : null;
            if (tm == null || !tm.HasProperty("_GraphicLight")) return;
            m.SetFloat("_GraphicLight", tm.GetFloat("_GraphicLight"));
            if (tm.HasProperty("_ShadowTint")) m.SetColor("_ShadowTint", tm.GetColor("_ShadowTint"));
            if (tm.HasProperty("_AuthoredFormLighting")) m.SetFloat("_AuthoredFormLighting", tm.GetFloat("_AuthoredFormLighting"));
        }

        // --- dev: fast-forward wear -------------------------------------------

        /// **Dev hook: `days` of camp traffic in one call.** Routes a hand
        /// (real `CampPath` A*) from the fire to every building, from the
        /// stores to every other building, and through every gate, and walks
        /// `tripsPerDay` round trips a day along each, with the real decay
        /// between days; then runs a pass so the roads draw now.
        /// `CampRoads.Simulate(Outpost.Home, 4)` from `unity cmd eval`.
        public static string Simulate(Outpost camp, float days, int tripsPerDay = 6)
        {
            var r = For(camp);
            if (r == null || !r.Ensure()) return "no sited camp";
            var map = CampPath.For(camp);
            var legs = new List<(Vector3, Vector3)>();
            Vector3 fire = camp.CampCentre;
            var stores = new List<Vector3>();
            foreach (var b in camp.Built)
            {
                if (b == null || b is WallSegment) continue;
                legs.Add((fire, b.transform.position));
                if (b.StoreCapacity > 0) stores.Add(b.transform.position);
            }
            foreach (var s in stores)
                foreach (var b in camp.Built)
                    if (b != null && !(b is WallSegment) && b.StoreCapacity <= 0)
                        legs.Add((s, b.transform.position));
            foreach (var w in camp.Walls)
            {
                if (w == null || !w.IsGate) continue;
                Vector3 run = w.B - w.A; run.y = 0f;
                Vector3 across = Vector3.Cross(Vector3.up, run.normalized);
                Vector3 m = w.Midpoint;
                Vector3 outer = Vector3.Dot(m - fire, across) >= 0f ? m + across * 12f : m - across * 12f;
                legs.Add((fire, outer));
            }

            // Cells each leg crosses, walked at 0.5 m.
            var cellHits = new Dictionary<int, float>();
            int routed = 0;
            foreach (var (a, b) in legs)
            {
                r.route.Clear();
                if (map == null || !map.Route(a, b, CampPath.Walker.Hand, r.route) || r.route.Count == 0) continue;
                routed++;
                Vector3 prev = a;
                foreach (var corner in r.route)
                {
                    Vector3 d = corner - prev; d.y = 0f;
                    int steps = Mathf.Max(1, Mathf.CeilToInt(d.magnitude / 0.5f));
                    float len = d.magnitude / steps;
                    for (int k = 0; k < steps; k++)
                    {
                        int i = r.IndexOf(prev + d * ((k + 0.5f) / steps));
                        if (i < 0) continue;
                        cellHits.TryGetValue(i, out float have);
                        cellHits[i] = have + len * 2f;          // there and back
                    }
                    prev = corner;
                }
            }
            int whole = Mathf.CeilToInt(days);
            for (int day = 0; day < whole; day++)
            {
                float part = Mathf.Min(1f, days - day);
                float k = Mathf.Exp(-part / Mathf.Max(0.01f, DecayDays));
                for (int i = 0; i < r.wear.Length; i++) r.wear[i] *= k;
                foreach (var kv in cellHits) r.wear[kv.Key] += kv.Value * tripsPerDay * part;
            }
            r.lastSeconds = TimeOfDay.Seconds;
            r.Pass();
            r.FinishBuild();
            return $"{legs.Count} legs, {routed} routed, {r.drawn.Count} road cells, {r.segs.Count} segs, {LastVertexCount} verts, "
                + $"{LastBuildMs:0.0} ms work in {LastSlices} slices, worst slice {LastMaxSliceMs:0.00} ms";
        }

        /// Dev: wipe the wear (and the ledger rows) for this camp.
        public static void Clear(Outpost camp)
        {
            var r = For(camp);
            if (r == null || !r.Ensure()) return;
            System.Array.Clear(r.wear, 0, r.wear.Length);
            System.Array.Clear(r.road, 0, r.road.Length);
            r.Pass();
            r.FinishBuild();
        }
    }
}
