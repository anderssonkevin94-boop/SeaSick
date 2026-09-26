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
    /// lattice sheet over the road cells, each lattice vertex written once,
    /// draped on the analytic height field. Its alpha is a coverage FIELD --
    /// the max over capsules between neighbouring road cells -- so a
    /// junction, a crossing or a bend is simply where two capsules meet; ends
    /// taper to a point like her `Road_End`. Her palette and cross-section
    /// (light compacted centre, darker ruts, soft feathered edge) are the
    /// vertex colours. Depth: `SeaSick/Worn Road` pulls each vertex toward
    /// the camera along its view ray, so it never z-fights and never floats.
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
        float[] rad, str;                // per drawn cell, cached for the field
        readonly List<int> stack = new List<int>();
        readonly List<int> drawn = new List<int>();
        bool ready;
        float tick;
        double lastSeconds = -1;
        OutpostLedger loadedFrom;

        GameObject view;
        Mesh mesh;
        static Material material;
        readonly Dictionary<int, int> vIndex = new Dictionary<int, int>();
        readonly Dictionary<int, float> hCache = new Dictionary<int, float>();
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Color32> cols = new List<Color32>();
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
            rad = new float[count];
            str = new float[count];
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

        // --- mesh -------------------------------------------------------------

        float Radius(int i) => Degree(i) <= 1 ? TipHalfWidth : HalfWidth;
        float Strength(int i) => Mathf.Lerp(MinOpacity, MaxOpacity, (level[i] - 1) / 6f);

        void Rebuild()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            EnsureView();
            verts.Clear(); cols.Clear(); tris.Clear(); vIndex.Clear();

            // Cells to tessellate: every road cell and its 8 neighbours,
            // each once (reuse `comp` as the mark).
            System.Array.Clear(comp, 0, comp.Length);
            stack.Clear();
            foreach (int c in drawn)
            {
                int cx = c % Side, cz = c / Side;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = cx + dx, nz = cz + dz;
                        if (nx < 0 || nz < 0 || nx >= Side || nz >= Side) continue;
                        int j = nz * Side + nx;
                        if (comp[j] == 0) { comp[j] = 1; stack.Add(j); }
                    }
            }

            foreach (int c in drawn) { rad[c] = Radius(c); str[c] = Strength(c); }

            foreach (int c in stack)
            {
                int cx = c % Side, cz = c / Side;
                for (int sz = 0; sz < Sub; sz++)
                    for (int sx = 0; sx < Sub; sx++)
                    {
                        int u = cx * Sub + sx, v = cz * Sub + sz;
                        int a = Vertex(u, v), b = Vertex(u + 1, v), d = Vertex(u, v + 1), e = Vertex(u + 1, v + 1);
                        if (cols[a].a == 0 && cols[b].a == 0 && cols[d].a == 0 && cols[e].a == 0) continue;
                        // Clockwise seen from above (Unity front face).
                        tris.Add(a); tris.Add(d); tris.Add(e);
                        tris.Add(a); tris.Add(e); tris.Add(b);
                    }
            }

            mesh.Clear();
            mesh.indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0, true);
            mesh.RecalculateNormals();
            var mr = view.GetComponent<MeshRenderer>();
            mr.enabled = tris.Count > 0;
            CopyTerrainLight(mr.sharedMaterial);
            LastBuildMs = (float)watch.Elapsed.TotalMilliseconds;
            LastVertexCount = verts.Count;
            if (LogBuild)
                Debug.Log($"[CampRoads] {camp.name}: {drawn.Count} road cells, {verts.Count} verts, {tris.Count / 3} tris, {LastBuildMs:0.0} ms");
        }

        /// Lattice vertex (u, v) in sub-cell units from cell (0,0)'s centre
        /// minus half a cell: evaluated and added once per rebuild.
        int Vertex(int u, int v)
        {
            int key = v * SubSide + u;
            if (vIndex.TryGetValue(key, out int idx)) return idx;

            float step = Cell / Sub;
            float wx = (ox - 0.5f) * Cell + u * step;
            float wz = (oz - 0.5f) * Cell + v * step;

            if (!hCache.TryGetValue(key, out float h))
            {
                h = camp.GroundAt(new Vector3(wx, 0f, wz));
                hCache[key] = h;
            }

            // Irregular shoulders: a little value noise on the distance.
            float wobble = (Noise(wx * 0.55f, wz * 0.55f) - 0.5f) * 0.36f;

            float best = 0f, bestU = 1f;
            int cx = Mathf.RoundToInt(wx / Cell) - ox, cz = Mathf.RoundToInt(wz / Cell) - oz;
            for (int dz = -2; dz <= 2; dz++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    int x = cx + dx, z = cz + dz;
                    if (x < 0 || z < 0 || x >= Side || z >= Side) continue;
                    int i = z * Side + x;
                    if (!draw[i]) continue;
                    float ax = (ox + x) * Cell, az = (oz + z) * Cell;
                    float ra = rad[i], sa = str[i];
                    // The cell's own disc.
                    Field(Dist(wx, wz, ax, az), ra, sa, wobble, ref best, ref bestU);
                    // Capsules to four forward neighbours (each pair once).
                    for (int n = 0; n < 4; n++)
                    {
                        int ndx = n == 0 ? 1 : n == 1 ? 0 : n == 2 ? 1 : -1;
                        int ndz = n == 0 ? 0 : 1;
                        int jx = x + ndx, jz = z + ndz;
                        if (jx < 0 || jx >= Side || jz >= Side) continue;
                        int j = i + ndx + ndz * Side;
                        // All eight links for DRAWING (a staircase of cells
                        // becomes one smooth diagonal band); `Linked`'s
                        // stricter rule is only for finding ends.
                        if (!draw[j]) continue;
                        float bx = ax + ndx * Cell, bz = az + ndz * Cell;
                        float ex = bx - ax, ez = bz - az;
                        float t = Mathf.Clamp01(((wx - ax) * ex + (wz - az) * ez) / (ex * ex + ez * ez));
                        float d = Dist(wx, wz, ax + ex * t, az + ez * t);
                        Field(d, Mathf.Lerp(ra, rad[j], t), Mathf.Lerp(sa, str[j], t), wobble, ref best, ref bestU);
                    }
                }

            // Astra's cross-section: light compacted crown fading to the
            // darker shoulder. `bestU` is the distance to the NEAREST
            // centreline, so a broad trampled yard stays crown-coloured
            // (per-capsule ruts drew a lattice across a yard -- rejected).
            Color c = Color.Lerp(CentreColour, EdgeColour, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((bestU - 0.25f) / 0.65f)));
            float mottle = 0.92f + 0.16f * Noise(wx * 0.23f + 17.1f, wz * 0.23f - 3.7f);
            c.r *= mottle; c.g *= mottle; c.b *= mottle;
            c.a = best;

            idx = verts.Count;
            verts.Add(view.transform.InverseTransformPoint(new Vector3(wx, h + Lift, wz)));
            cols.Add(best < 0.004f ? new Color32(0, 0, 0, 0) : (Color32)c);
            vIndex[key] = idx;
            return idx;
        }

        static float Dist(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx, dz = az - bz;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        static void Field(float d, float r, float s, float wobble, ref float best, ref float bestU)
        {
            float dd = d + wobble * Mathf.Clamp01(r / HalfWidth);
            float f = Mathf.Min(Feather, r * 0.9f);
            float cover = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((dd - (r - f)) / Mathf.Max(0.001f, f)));
            float a = cover * s;
            if (a > best) best = a;
            if (cover > 0f) bestU = Mathf.Min(bestU, Mathf.Clamp01(dd / Mathf.Max(0.001f, r)));
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
            return $"{legs.Count} legs, {routed} routed, {r.drawn.Count} road cells, {LastVertexCount} verts, {LastBuildMs:0.0} ms";
        }

        /// Dev: wipe the wear (and the ledger rows) for this camp.
        public static void Clear(Outpost camp)
        {
            var r = For(camp);
            if (r == null || !r.Ensure()) return;
            System.Array.Clear(r.wear, 0, r.wear.Length);
            System.Array.Clear(r.road, 0, r.road.Length);
            r.Pass();
        }
    }
}
