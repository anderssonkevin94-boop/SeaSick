using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SeaSick.World
{
    /// **The drawing of the roads the player laid (2026-09-27).**
    ///
    /// Kevin: *"I don't like the road system. I'd rather place it myself,
    /// give the villagers a slight speed boost when using it."* So the worn
    /// roads of 2026-09-26 (wear per 2 m cell, hysteresis, thinning) are
    /// gone; what is left is their DRAWING, fed from the player's segments
    /// (`OutpostLedger.builtRoads`, sited by `UI/RoadSiting`, raised by
    /// `Outpost.Roads`). The gameplay half -- road cells cheaper to route
    /// over, feet faster on them -- is `CampPath.Roads`, not this file.
    ///
    /// **Drawing.** Segments that share an end are joined back into one
    /// chain (so a tap-to-tap run with bends draws as one smooth line, and a
    /// crossroads is where chains meet); each chain is cut into ~`Densify`
    /// metre points, given a slight wander, and Chaikin-rounded at the
    /// bends. ONE mesh per camp: a 0.5 m lattice sheet draped on the height
    /// field, each vertex storing its distance to the nearest line; the
    /// `SeaSick/Worn Road` shader turns that into coverage in Astra's
    /// palette at the kit's 2.16 m. Dead ends taper like her `Road_End`.
    /// The build is an iterator stepped within `SliceMs` per frame, then
    /// `RoadTorches` stands posts along the chains.
    ///
    /// Lives on the island GameObject beside `CampPath`; the only statics
    /// are tunables and shared scratch.
    [DisallowMultipleComponent]
    public class CampRoads : MonoBehaviour
    {
        // --- tunables ---------------------------------------------------------

        public static bool Enabled = true;
        /// Half width incl. feather (Astra: 2.16 m across), feather, tip.
        public static float HalfWidth = 1.08f;
        public static float Feather = 0.2f;
        public static float TipHalfWidth = 0.2f;
        public static float Lift = 0.02f;
        public static bool LogBuild = true;
        /// Spacing of the points a chain is cut into before rounding.
        public static float Densify = 2f;

        /// Astra's trampled-earth palette, her exact vertex colours
        /// (tools/blender/roads_astra_lvl1.py `COL`, sRGB): worn crown
        /// #B09A76, earth #A38B65, shoulder #968763. `Exposure` scales all three.
        public static Color CentreColour = new Color32(0xB0, 0x9A, 0x76, 0xFF);
        public static Color EarthColour = new Color32(0xA3, 0x8B, 0x65, 0xFF);
        public static Color EdgeColour = new Color32(0x96, 0x87, 0x63, 0xFF);
        public static float Exposure = 1f;
        public static float EarthShade = 0.95f;
        public static float ShoulderShade = 0.84f;

        public static float LastBuildMs { get; private set; }
        public static int LastVertexCount { get; private set; }

        const float Cell = 2f;           // world lattice (= CampPath.DesiredCell, WallPostStep)
        const int Side = 160;            // cells per side: 320 m round the fire
        const int Sub = 4;               // lattice vertices per cell edge -> 0.5 m
        const int SubSide = Side * Sub + 1;

        /// How far from the fire a road can be drawn (the sheet is 320 m
        /// across, centred on the fire). `Outpost.CanPlaceRoad` refuses past it.
        public const float Reach = Side * Cell * 0.5f - 6f;

        // --- the speed rule (read by CampPath and the walkers) ----------------

        /// How much faster a hand walks on a road: `EconomyTuning`'s
        /// `roadSpeedMultiplier` (first pass 1.3). The route prices a road
        /// cell at 1/this, the body walks this much faster on one, and the
        /// invisible walker's leg is metered the same way -- one number.
        public static float SpeedMultiplier => Economy.EconomyTuning.RoadSpeedMultiplier;

        /// Speed factor at a point: `SpeedMultiplier` on a road cell of the
        /// camp's map, else 1.
        public static float SpeedAt(Outpost camp, Vector3 at)
        {
            if (camp == null) return 1f;
            var map = CampPath.For(camp);
            return map != null && map.OnRoad(at) ? SpeedMultiplier : 1f;
        }

        // --- state ------------------------------------------------------------

        Outpost camp;
        int ox, oz;                      // world lattice index of cell (0,0)
        bool[] footprint;
        bool ready;
        bool dirty = true;
        OutpostLedger loadedFrom;

        GameObject view;
        Mesh mesh;
        static Material material;
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<int> tris = new List<int>();

        public static CampRoads For(Outpost camp)
        {
            if (camp == null) return null;
            var r = camp.GetComponent<CampRoads>();
            if (r == null) r = camp.gameObject.AddComponent<CampRoads>();
            r.camp = camp;
            return r;
        }

        /// The roads changed (raised, torn down, loaded): redraw soon.
        public void MarkDirty() => dirty = true;

        bool Ensure()
        {
            if (ready) return true;
            if (camp == null) camp = GetComponent<Outpost>();
            if (camp == null || !camp.Sited || camp.Ledger == null) return false;
            Vector3 c = camp.CampCentre;
            ox = Mathf.RoundToInt(c.x / Cell) - Side / 2;
            oz = Mathf.RoundToInt(c.z / Cell) - Side / 2;
            footprint = new bool[Side * Side];
            building = null;
            buildPending = false;
            ready = true;
            loadedFrom = camp.Ledger;
            dirty = true;
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

        /// Is this point inside the drawable sheet round the fire?
        public static bool InReach(Outpost camp, Vector3 at)
            => camp != null && Island.FlatDistance(at, camp.CampCentre) <= Reach;

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
            if (dirty)
            {
                dirty = false;
                MarkFootprints();
                Rebuild();
            }
            StepBuild(false);
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

        // --- drawing -----------------------------------------------------------

        public static float Wobble = 0f;             // per-pixel edge noise
        /// v3 shoulders (her polygonal outline, broad bulges): a slow width
        /// wander and a per-vertex facet offset, baked into the vertex
        /// distance so the 0.5 m lattice draws them as straight edges.
        public static float WidthWander = 0.3f;      // metres, +-
        public static float WanderFreq = 0.33f;      // per metre
        public static float Facet = 0.12f;           // metres, +-
        /// Centre-line wander, metres (peak). Small: the player put the road
        /// where they wanted it, and the route cells follow the straight line.
        public static float Jitter = 0.25f;
        public static float TaperLength = 2.6f;      // dead ends taper over this
        public static float SliceMs = 2.5f;          // main-thread budget per frame

        public static float LastMaxSliceMs { get; private set; }
        public static int LastSlices { get; private set; }

        const float Step = Cell / Sub;               // 0.5 m sheet lattice
        const float FarD = 9f;

        struct Seg { public float ax, az, bx, bz, sa, sb, oa, ob; }

        readonly List<Seg> segs = new List<Seg>();
        readonly List<Vector2> pts = new List<Vector2>();
        readonly List<Vector2> pts2 = new List<Vector2>();
        float[] hCache;                               // per lattice vertex, NaN = unknown
        // Every smoothed chain of the last build, for `RoadTorches`: points
        // in `chainPts`, (first, count, junction-at-start | junction-at-end << 1).
        internal readonly List<Vector2> chainPts = new List<Vector2>();
        internal readonly List<Vector3Int> chainSpans = new List<Vector3Int>();
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
                        Debug.Log($"[CampRoads] {camp.name}: {chainSpans.Count} chains, {segs.Count} segs, {verts.Count} verts, {tris.Count / 3} tris, "
                            + $"{buildWork:0.0} ms work in {buildSlices} slices, worst {buildMax:0.00} ms");
                    return;
                }
            } while (all);
        }

        /// Finish any pending sheet now (dev / probes).
        public void FinishBuild() { if (Ensure()) { if (dirty) { dirty = false; MarkFootprints(); Rebuild(); } StepBuild(true); } }

        // --- chains from the player's segments ---------------------------------

        readonly Dictionary<long, int> nodeOf = new Dictionary<long, int>();
        readonly List<Vector2> nodePos = new List<Vector2>();
        readonly List<List<int>> nodeEdges = new List<List<int>>();   // edge indices
        readonly List<Vector2Int> edges = new List<Vector2Int>();      // node a, node b
        bool[] edgeUsed = new bool[0];
        readonly List<int> chainNodes = new List<int>();

        int NodeAt(Vector2 p)
        {
            // Ends within ~0.3 m are one node: `RoadSiting` snaps a new run
            // onto an existing end, and a saved float comes back exact.
            long key = ((long)Mathf.RoundToInt(p.x * 3f) << 32) ^ (uint)Mathf.RoundToInt(p.y * 3f);
            if (nodeOf.TryGetValue(key, out int n)) return n;
            n = nodePos.Count;
            nodeOf[key] = n;
            nodePos.Add(p);
            if (nodeEdges.Count <= n) nodeEdges.Add(new List<int>());
            else nodeEdges[n].Clear();
            return n;
        }

        void Chains()
        {
            nodeOf.Clear(); nodePos.Clear(); edges.Clear();
            foreach (var l in nodeEdges) l.Clear();
            var roads = camp.Ledger.builtRoads;
            if (roads == null) return;
            foreach (var r in roads)
            {
                if (r == null) continue;
                int a = NodeAt(new Vector2(r.ax, r.az)), b = NodeAt(new Vector2(r.bx, r.bz));
                if (a == b) continue;
                nodeEdges[a].Add(edges.Count);
                nodeEdges[b].Add(edges.Count);
                edges.Add(new Vector2Int(a, b));
            }
            if (edgeUsed.Length < edges.Count) edgeUsed = new bool[edges.Count * 2];
            System.Array.Clear(edgeUsed, 0, edgeUsed.Length);

            // Open chains from every end and junction, then closed loops.
            for (int pass = 0; pass < 2; pass++)
                for (int s = 0; s < nodePos.Count; s++)
                {
                    int deg = nodeEdges[s].Count;
                    if (pass == 0 && deg == 2) continue;
                    foreach (int e0 in nodeEdges[s])
                    {
                        if (edgeUsed[e0]) continue;
                        chainNodes.Clear();
                        chainNodes.Add(s);
                        int cur = s, e = e0;
                        while (true)
                        {
                            edgeUsed[e] = true;
                            int nx = edges[e].x == cur ? edges[e].y : edges[e].x;
                            chainNodes.Add(nx);
                            cur = nx;
                            if (nodeEdges[cur].Count != 2) break;
                            int ne = nodeEdges[cur][0] == e ? nodeEdges[cur][1] : nodeEdges[cur][0];
                            if (edgeUsed[ne]) break;
                            e = ne;
                        }
                        bool loop = chainNodes[0] == chainNodes[chainNodes.Count - 1];
                        EmitChain(!loop && deg <= 1, !loop && nodeEdges[cur].Count <= 1, loop);
                    }
                }
        }

        /// One chain of nodes -> densified, lightly wandered, Chaikin-rounded
        /// polyline -> segments with end taper.
        void EmitChain(bool openStart, bool openEnd, bool loop)
        {
            if (chainNodes.Count < 2) return;
            pts.Clear();
            for (int k = 0; k + 1 < chainNodes.Count; k++)
            {
                Vector2 a = nodePos[chainNodes[k]], b = nodePos[chainNodes[k + 1]];
                int pieces = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / Mathf.Max(0.5f, Densify)));
                for (int i = 0; i < pieces; i++) pts.Add(Vector2.Lerp(a, b, i / (float)pieces));
            }
            pts.Add(nodePos[chainNodes[chainNodes.Count - 1]]);
            // A slow wander (~12 m wavelength), the same every build and
            // every load; the chain's ends and the player's bends are held.
            for (int n = 1; n < pts.Count - 1; n++)
            {
                float x = pts[n].x, z = pts[n].y;
                x += (Noise(x * 0.08f + 3.1f, z * 0.08f - 7.7f) - 0.5f) * 2f * Jitter;
                z += (Noise(x * 0.08f - 11.3f, z * 0.08f + 5.9f) - 0.5f) * 2f * Jitter;
                pts[n] = new Vector2(x, z);
            }
            // Chaikin, three rounds, ends kept.
            for (int round = 0; round < 3; round++)
            {
                pts2.Clear();
                pts2.Add(pts[0]);
                for (int n = 0; n < pts.Count - 1; n++)
                {
                    Vector2 a = pts[n], b = pts[n + 1];
                    pts2.Add(Vector2.Lerp(a, b, 0.25f));
                    pts2.Add(Vector2.Lerp(a, b, 0.75f));
                }
                pts2.Add(pts[pts.Count - 1]);
                pts.Clear(); pts.AddRange(pts2);
            }
            chainSpans.Add(new Vector3Int(chainPts.Count, pts.Count,
                (!loop && !openStart ? 1 : 0) | (!loop && !openEnd ? 2 : 0)));
            chainPts.AddRange(pts);
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
                    sa = 1f, sb = 1f, oa = prevOff, ob = off,
                });
                prevOff = off;
            }
        }

        System.Collections.IEnumerator Build()
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            float hw = HalfWidth;
            segs.Clear();
            chainPts.Clear(); chainSpans.Clear();
            Chains();
            if (clock.Elapsed.TotalMilliseconds > SliceMs) { yield return null; clock.Restart(); }

            // Nothing laid and nothing drawn yet: no sheet, no 5 MB of scratch.
            if (segs.Count == 0 && view == null) yield break;

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
            float show = hw + 0.5f * Wobble + WidthWander + Facet + 0.05f;
            for (int v = vmin; v < vmax; v++)
            {
                for (int u = umin; u < umax; u++)
                {
                    int k = v * SubSide + u;
                    if (sIdx[k] == -1 && sIdx[k + 1] == -1 && sIdx[k + SubSide] == -1 && sIdx[k + SubSide + 1] == -1) continue;
                    if (Mathf.Min(Mathf.Min(sD[k], sD[k + 1]), Mathf.Min(sD[k + SubSide], sD[k + SubSide + 1])) >= show) continue;
                    // Split along the diagonal whose ends are closer in the
                    // SHAPED distance (what the shader draws), not the raw one.
                    int a = SheetVertex(u, v, baseX, baseZ, origin), b = SheetVertex(u + 1, v, baseX, baseZ, origin);
                    int d0 = SheetVertex(u, v + 1, baseX, baseZ, origin), e = SheetVertex(u + 1, v + 1, baseX, baseZ, origin);
                    // Clockwise seen from above (Unity front face); split
                    // along the diagonal whose ends are closer in distance so
                    // a curved edge does not zig-zag across quads.
                    if (Mathf.Abs(uvs[a].x - uvs[e].x) <= Mathf.Abs(uvs[b].x - uvs[d0].x))
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
                // Her colours are sRGB hex: SetColor linearises them, as
                // Blender did for her vertex colours.
                if (m.HasProperty("_CentreColour")) m.SetColor("_CentreColour", CentreColour * Exposure);
                if (m.HasProperty("_EarthColour")) m.SetColor("_EarthColour", EarthColour * (Exposure * EarthShade));
                if (m.HasProperty("_EdgeColour")) m.SetColor("_EdgeColour", EdgeColour * (Exposure * ShoulderShade));
            }
            CopyTerrainLight(m);
            LastVertexCount = verts.Count;

            // ---- 6. torch posts, in a slice of their own --------------------
            // (first time: the art loads in yet another slice first)
            if (!RoadTorches.ArtLoaded) { yield return null; RoadTorches.Preload(); }
            yield return null;
            RoadTorches.For(this).Layout();
        }

        /// The distance the shader draws: the true distance to the line,
        /// minus a slow wander of the shoulder (so the width breathes along
        /// the run, each side on its own) and a small per-vertex offset (so
        /// the outline is a polygon, like her faceted shoulders). Both are
        /// hashes of world position: every build and every load agree.
        static float Shaped(float d, float wx, float wz)
        {
            if (d >= FarD) return d;
            float wander = (Noise(wx * WanderFreq + 40.3f, wz * WanderFreq - 12.9f) - 0.5f) * 2f * WidthWander;
            int ix = Mathf.RoundToInt(wx * 2f), iz = Mathf.RoundToInt(wz * 2f);
            float facet = (Hash(ix * 7 + 3, iz * 13 - 5) - 0.5f) * 2f * Facet;
            // Only the shoulders move: the crown stays where it is.
            float w = Mathf.Clamp01((d - 0.3f) / 0.5f);
            return d - (wander + facet) * w;
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
            uvs.Add(new Vector2(Mathf.Min(Shaped(sD[k], wx, wz), FarD), sS[k]));
            if (sIdx[k] == -1) sTouched.Add(k);
            sIdx[k] = idx;
            return idx;
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

        // --- for RoadTorches ----------------------------------------------------

        internal Outpost Camp => camp;

        /// Every player road counts as established (torches stand on all of
        /// it); the worn roads' wear gate is gone.
        internal float WearNear(Vector2 p) => float.MaxValue;

        /// Distance from a point to the drawn road's edge-free centre-lines
        /// (plus their end/yard offsets), i.e. the sheet's raw distance.
        internal float RoadDistance(Vector2 p)
        {
            float best = FarD;
            for (int n = 0; n < segs.Count; n++)
            {
                Seg sg = segs[n];
                float ex = sg.bx - sg.ax, ez = sg.bz - sg.az;
                float ll = ex * ex + ez * ez;
                float t = ll > 1e-6f ? Mathf.Clamp01(((p.x - sg.ax) * ex + (p.y - sg.az) * ez) / ll) : 0f;
                float px = sg.ax + ex * t - p.x, pz = sg.az + ez * t - p.y;
                float d = Mathf.Sqrt(px * px + pz * pz) + (sg.oa + (sg.ob - sg.oa) * t);
                if (d < best) best = d;
            }
            return best;
        }

        internal bool UnderBuilding(Vector2 p)
        {
            int i = IndexOf(new Vector3(p.x, 0f, p.y));
            return i >= 0 && footprint[i];
        }

        internal static float HashOf(int x, int z) => Hash(x, z);
    }
}
