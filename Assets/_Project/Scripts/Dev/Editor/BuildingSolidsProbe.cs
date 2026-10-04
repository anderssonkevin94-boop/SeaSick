using System.Collections.Generic;
using System.Reflection;
using System.Text;
using SeaSick.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace SeaSick.Dev
{
    /// **Checks for the building solids (2026-10-01, `CampPath.Solids`).**
    /// Play mode, a camp in view (Kevin's save via
    /// `RunProbe.SetSaveDirOverride` + Continue).
    ///
    ///  - `Map(png)`: top-down picture of the watched camp's grid -- cells
    ///    closed by a building (red), wall (grey), slope/sea (dark), open
    ///    (green) -- with every box (white), lane (yellow), marker (blue)
    ///    and villager (black).
    ///  - `Snapshot()`: every building's position/yaw against its ledger
    ///    row, and how many villagers stand inside a box.
    ///  - `Walk(id)`: borrows one villager and walks him, with the REAL
    ///    `CampWorker.Walk` (reflection), from 12 m out to every marker of
    ///    every building with that plan id, marker to marker, and back out;
    ///    counts arrivals and steps taken inside any box.
    ///  - `Trips(n, seed)` + `TripsReport()`: chained random trips with the
    ///    real resolvers and the real walk, in the player loop (see below).
    public static class BuildingSolidsProbe
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        public static Outpost Camp()
        {
            Outpost best = null;
            foreach (var o in Outpost.All)
            {
                if (o == null) continue;
                if (o.Watched) return o;
                if (best == null || o.Built.Count > best.Built.Count) best = o;
            }
            return best;
        }

        /// **Can every station marker be walked to?** (2026-10-04, after
        /// Kevin's kitchen runner.) For every building of the camp in view and
        /// every access marker (`BuildingSolids.AccessStems`): its free spot
        /// (`CampPath.FreeSpot`, where a walker really goes) must be in the
        /// camp's walkable region (the walk-grid flood from the camp,
        /// `CampPath.Reachable`) and at least `minClear` m off every box.
        /// One line per failure; the first line PASS / FAIL with the count.
        /// Play mode, camp in view. Read-only.
        public static string MarkerReach(float minClear = 0.2f)
        {
            var camp = Camp();
            if (camp == null) return "FAIL: no camp";
            int markers = 0, bad = 0;
            var sb = new StringBuilder();
            foreach (var b in camp.Built)
            {
                if (b == null) continue;
                foreach (var t in b.GetComponentsInChildren<Transform>(true))
                {
                    // The kit name without the FBX import's `.001` suffix
                    // (as `BuildingFactory.Stem`, which is internal to the game).
                    int dot = t.name.LastIndexOf('.');
                    string stem = dot > 0 ? t.name.Substring(0, dot) : t.name;
                    if (System.Array.IndexOf(BuildingSolids.AccessStems, stem) < 0) continue;
                    markers++;
                    Vector3 spot = CampPath.FreeSpot(camp, t.position);
                    bool reach = CampPath.Reachable(camp, spot);
                    float clear = CampPath.SolidDistance(camp, spot);
                    if (reach && clear >= minClear - 1e-3f) continue;
                    bad++;
                    sb.Append($"\n  {b.Id}@({b.transform.position.x:F1},{b.transform.position.z:F1}) {stem}: free spot ({spot.x:F1},{spot.z:F1}) "
                              + (reach ? "" : "NOT REACHABLE ") + $"clear {clear:F2} m");
                }
            }
            return $"{(bad == 0 ? "PASS" : "FAIL")} marker reach: {bad} of {markers} access markers fail (reachable from the camp, {minClear:F2} m+ off every box)" + sb;
        }

        static Outpost CampOf(CampWorker w) => (Outpost)typeof(CampWorker).GetField("camp", Any).GetValue(w);

        public static string Snapshot()
        {
            var camp = Camp();
            if (camp == null) return "no camp";
            var map = CampPath.For(camp);
            map.ResyncSolids();
            var sb = new StringBuilder();
            sb.Append($"camp {camp.name}: {camp.Built.Count} buildings, {map.SolidCount} boxes, {map.LaneCount} lanes, grid built {map.Built}\n");
            var ledger = camp.Ledger;
            foreach (var b in camp.Built)
            {
                if (b == null) continue;
                var p = b.transform.position;
                float rowD = -1f;
                int ri = camp.RaisedIndexOf(b);
                if (ledger != null && ri >= 0 && ri < ledger.raised.Count)
                {
                    var r = ledger.raised[ri];
                    rowD = Vector2.Distance(new Vector2(r.At.x, r.At.z), new Vector2(p.x, p.z));
                }
                sb.Append($"  {b.Id} ({p.x:0.00},{p.z:0.00}) yaw {b.transform.eulerAngles.y:0} rowDist {rowD:0.000}\n");
            }
            int bodies = 0, inside = 0;
            foreach (var w in CampWorker.Bodies)
            {
                if (w == null || CampOf(w) != camp || !w.gameObject.activeInHierarchy) continue;
                bodies++;
                float d = map.SolidDistance(w.transform.position);
                if (d < -0.02f && !w.OnTower) { inside++; sb.Append($"  INSIDE: {w.name} at {w.transform.position} d={d:0.00}\n"); }
            }
            sb.Append($"bodies {bodies}, inside a box {inside}\n");
            return sb.ToString();
        }

        // --- the map picture ---------------------------------------------

        public static string Map(string png)
        {
            var camp = Camp();
            if (camp == null) return "no camp";
            var map = CampPath.For(camp);
            map.HasRoute(camp.CampCentre, camp.CampCentre + Vector3.right, CampPath.Walker.Hand);   // builds
            map.ResyncSolids();
            // Frame: the buildings plus 8 m.
            float x0 = 1e9f, z0 = 1e9f, x1 = -1e9f, z1 = -1e9f;
            foreach (var b in camp.Built)
            {
                if (b == null) continue;
                var p = b.transform.position;
                x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x); z0 = Mathf.Min(z0, p.z); z1 = Mathf.Max(z1, p.z);
            }
            x0 -= 8f; z0 -= 8f; x1 += 8f; z1 += 8f;
            const float ppm = 8f;
            int W = Mathf.CeilToInt((x1 - x0) * ppm), H = Mathf.CeilToInt((z1 - z0) * ppm);
            if (W > 1400 || H > 1400) { float s = 1400f / Mathf.Max(W, H); W = (int)(W * s); H = (int)(H * s); }
            float sx = W / (x1 - x0), sz = H / (z1 - z0);
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            var px = new Color32[W * H];
            int n = map.Side;
            float cell = map.CellSize;
            Vector2 o = map.Origin;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float wx = x0 + x / sx, wz = z0 + y / sz;
                    int cx = Mathf.RoundToInt((wx - o.x) / cell), cy = Mathf.RoundToInt((wz - o.y) / cell);
                    Color32 c = new Color32(30, 30, 30, 255);
                    if (cx >= 0 && cy >= 0 && cx < n && cy < n)
                    {
                        int k = map.CellKind(cx, cy);
                        if (map.BuildingCell(cx, cy) && k >= 4) c = new Color32(170, 60, 50, 255);
                        else c = k switch
                        {
                            0 => new Color32(20, 40, 80, 255),
                            1 => new Color32(60, 55, 50, 255),
                            2 => new Color32(110, 100, 90, 255),
                            3 => new Color32(140, 140, 140, 255),
                            4 => new Color32(70, 120, 60, 255),
                            _ => new Color32(90, 100, 50, 255),
                        };
                        // cell grid lines
                        float fx = (wx - o.x) / cell + 0.5f, fz = (wz - o.y) / cell + 0.5f;
                        if (fx - Mathf.Floor(fx) < 0.03f || fz - Mathf.Floor(fz) < 0.03f)
                            c = new Color32((byte)(c.r * 0.7f), (byte)(c.g * 0.7f), (byte)(c.b * 0.7f), 255);
                    }
                    px[y * W + x] = c;
                }
            void Line(Vector3 a, Vector3 b, Color32 col, int thick)
            {
                int steps = Mathf.CeilToInt(Vector3.Distance(a, b) * ppm * 2f) + 1;
                for (int i = 0; i <= steps; i++)
                {
                    var p = Vector3.Lerp(a, b, i / (float)steps);
                    int ix = (int)((p.x - x0) * sx), iy = (int)((p.z - z0) * sz);
                    for (int dy = -thick; dy <= thick; dy++)
                        for (int dx = -thick; dx <= thick; dx++)
                        {
                            int qx = ix + dx, qy = iy + dy;
                            if (qx >= 0 && qy >= 0 && qx < W && qy < H) px[qy * W + qx] = col;
                        }
                }
            }
            var corners = new List<Vector3>();
            map.BoxCornersInto(corners);
            for (int i = 0; i + 3 < corners.Count; i += 4)
                for (int e = 0; e < 4; e++) Line(corners[i + e], corners[i + (e + 1) % 4], new Color32(255, 255, 255, 255), 0);
            var lanes = new List<Vector3>();
            map.LanesInto(lanes);
            for (int i = 0; i + 1 < lanes.Count; i += 2)
            {
                Line(lanes[i], lanes[i + 1], new Color32(255, 220, 40, 255), 1);
                Line(lanes[i], lanes[i], new Color32(60, 140, 255, 255), 3);
            }
            foreach (var w in CampWorker.Bodies)
                if (w != null && CampOf(w) == camp && w.gameObject.activeInHierarchy)
                    Line(w.transform.position, w.transform.position, new Color32(0, 0, 0, 255), 2);
            tex.SetPixels32(px);
            tex.Apply();
            System.IO.File.WriteAllBytes(png, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            return $"{W}x{H} px, {corners.Count / 4} boxes, {lanes.Count / 2} lanes -> {png}";
        }

        // --- the walk test -----------------------------------------------

        static readonly List<Vector3> pathLog = new List<Vector3>();

        /// Walk one borrowed villager through every marker of every
        /// building with plan id `id`. Returns a one-line-per-leg log.
        public static string Walk(string id, int maxSteps = 1600)
        {
            var camp = Camp();
            if (camp == null) return "no camp";
            var map = CampPath.For(camp);
            map.ResyncSolids();
            CampWorker w = null;
            foreach (var c in CampWorker.Bodies)
                if (c != null && CampOf(c) == camp && c.gameObject.activeInHierarchy && !c.OnTower) { w = c; break; }
            if (w == null) return "no body";
            var walk = typeof(CampWorker).GetMethod("Walk", Any, null, new[] { typeof(Vector3), typeof(float) }, null);
            var clear = typeof(CampWorker).GetMethod("ClearRoute", Any);
            var budget = typeof(CampPath).GetField("budgetFrame", Any);
            Vector3 home = w.transform.position;
            var sb = new StringBuilder();
            int legs = 0, arrived = 0, near = 0, insideSteps = 0, totalSteps = 0;
            foreach (var b in camp.Built)
            {
                if (b == null || b.Id != id) continue;
                var marks = new List<(string, Vector3)>();
                foreach (var t in b.GetComponentsInChildren<Transform>(true))
                {
                    string stem = BuildingFactoryStem(t.name);
                    if (System.Array.IndexOf(BuildingSolids.AccessStems, stem) < 0) continue;
                    if (map.SolidDistance(t.position) < 0f) continue;
                    marks.Add((stem, t.position));
                }
                marks.Add(("WorkSpot", CampWorker.WorkSpot(camp, b)));
                // 12 m out toward the fire, then every marker, then back out.
                Vector3 c = b.transform.position;
                Vector3 toFire = camp.CampCentre - c; toFire.y = 0f;
                if (toFire.sqrMagnitude < 1f) toFire = Vector3.forward;
                Vector3 start = c + toFire.normalized * 12f;
                if (CampPath.PushOut(camp, start, out var s2)) start = s2;
                start.y = camp.GroundAt(start);
                w.transform.position = start;
                clear.Invoke(w, null);
                sb.Append($"{b.Id} @({c.x:0.0},{c.z:0.0}) yaw {b.transform.eulerAngles.y:0}: {marks.Count} spots\n");
                var legsTo = new List<(string, Vector3)>(marks) { ("out", start) };
                foreach (var (label, at) in legsTo)
                {
                    legs++;
                    int steps = 0, inside = 0;
                    bool done = false;
                    Vector3 from = w.transform.position;
                    while (steps < maxSteps)
                    {
                        budget.SetValue(null, -1);
                        done = (bool)walk.Invoke(w, new object[] { at, 0.05f });
                        steps++;
                        if (map.SolidDistance(w.transform.position) < -0.02f) inside++;
                        if (done) break;
                    }
                    float d = Vector2.Distance(new Vector2(w.transform.position.x, w.transform.position.z), new Vector2(at.x, at.z));
                    totalSteps += steps;
                    insideSteps += inside;
                    if (done && d < 0.6f) arrived++;
                    else if (done && d < 1.3f) near++;
                    sb.Append($"   -> {label}: {(done ? "done" : "NOT DONE")} in {steps} steps ({steps * 0.05f:0.0} s), end {d:0.00} m off, inside {inside}, straight {Vector2.Distance(new Vector2(from.x, from.z), new Vector2(at.x, at.z)):0.0} m\n");
                    clear.Invoke(w, null);
                }
            }
            w.transform.position = home;
            clear.Invoke(w, null);
            sb.Append($"SUMMARY {id}: legs {legs}, arrived(<0.6m) {arrived}, near(<1.3m) {near}, steps {totalSteps}, inside-a-box steps {insideSteps}\n");
            return sb.ToString();
        }

        static string BuildingFactoryStem(string name)
        {
            int dot = name.LastIndexOf('.');
            return dot > 0 ? name.Substring(0, dot) : name;
        }

        // =====================================================================
        // Trips (2026-10-04): chained random errands, the real walk
        // =====================================================================
        //
        // Kevin: "A lot of times they're just running into a building, trying
        // to force their way to where they're going, and they end up being
        // stuck." `Trips(n, seed)` borrows 1-3 villagers (their `CampWorker`
        // switched off, exactly as `WalkSlipProbe.Drive`) and walks them
        // through n random trips between them, each from where the last
        // ended, with the REAL private `CampWorker.Walk`. Targets come from
        // the real resolvers: `StoreSpot`, every building's `InputSpot` /
        // `OutputSpot` (`EdgeBeyond` where it has no marker), `WorkSpot`,
        // hut `Entry`s, `EdgeBeyond` toward the fire, plus random free
        // points. Counted: arrivals (<= 0.35 m), fake arrivals (the walk
        // said "there" further out: the 1.2 m pin rule, or the stall
        // guard's 2.5 m / 5 m), spots held by another body, stuck events
        // (< 0.25 m gained in a 0.5 s window), pins (consecutive stuck
        // windows: total and longest), overlaps (the body's centre inside a
        // box), trips not arrived in 60 s, and the worst 10 trips.
        //
        // **Runs in the player loop.** An eval runs outside it (the
        // `HarpoonTapCheck` trap): `Time.frameCount`, the plan budget and the
        // walk's per-frame state would all be read in the wrong frame. So
        // `Trips` only queues; every step happens in `InputSystem
        // .onAfterUpdate` for the Dynamic update -- once per player frame --
        // `Substeps` walk steps a frame (fast-forward). Read `TripsReport()`
        // any time; it says when it is done. Play mode, a camp loaded.
        //
        // Eval: `return SeaSick.Dev.BuildingSolidsProbe.Trips(40, 1);` then
        // `return SeaSick.Dev.BuildingSolidsProbe.TripsReport();`.

        const float TripTimeout = 60f;
        const float StuckWindow = 0.5f;
        const float StuckGain = 0.25f;
        const float ArriveOk = 0.35f;
        const int Substeps = 4;

        class Trip
        {
            public string label;
            public Vector3 from, to, end;
            public float t, longestPin, pin, straight;
            public string result = "walking";
            public int stuck, overlaps;
        }

        class Walker
        {
            public CampWorker w;
            public Vector3 home;
            public Trip trip;
            public float win;
            public Vector3 winFrom;
            public bool inside;
            public float pinRun;
        }

        static readonly List<Walker> tripWalkers = new List<Walker>();
        static readonly List<Trip> tripsDone = new List<Trip>();
        static readonly List<(string label, Vector3 at)> tripTargets = new List<(string, Vector3)>();
        static System.Random tripRng;
        static int tripsWanted, tripsStarted;
        static bool tripsRunning;
        static string tripsNote = "not run";
        static Outpost tripCamp;
        static MethodInfo walkMI, clearMI;
        static FieldInfo budgetFI;

        /// Queue `n` chained trips (seed `seed`); returns at once.
        public static string Trips(int n, int seed)
        {
            if (!Application.isPlaying) return "FAIL: play mode only";
            StopTrips();
            tripsWanted = Mathf.Max(1, n);
            tripsStarted = 0;
            tripRng = new System.Random(seed);
            tripsDone.Clear();
            tripTargets.Clear();
            tripsNote = "queued for the next player frame";
            InputSystem.onAfterUpdate -= TripsTick;
            InputSystem.onAfterUpdate += TripsTick;
            tripsRunning = true;
            return $"Trips({n}, {seed}) queued: read BuildingSolidsProbe.TripsReport()";
        }

        /// Hand the villagers back and stop (also done when the trips end).
        public static string StopTrips()
        {
            InputSystem.onAfterUpdate -= TripsTick;
            foreach (var b in tripWalkers)
            {
                if (b.w == null) continue;
                clearMI?.Invoke(b.w, null);
                b.w.GetComponent<VillagerActing>()?.Set(VillagerActing.Mode.None);
                b.w.enabled = true;
            }
            int c = tripWalkers.Count;
            tripWalkers.Clear();
            tripsRunning = false;
            return $"released {c}";
        }

        static void TripsTick()
        {
            if (InputState.currentUpdateType != InputUpdateType.Dynamic) return;
            if (!Application.isPlaying) { StopTrips(); return; }
            try
            {
                if (tripWalkers.Count == 0 && !TripsSetup()) { StopTrips(); return; }
                float dt = Mathf.Min(Time.deltaTime, 1f / 30f);
                if (dt <= 0f) return;
                for (int sub = 0; sub < Substeps && tripsRunning; sub++)
                    for (int i = 0; i < tripWalkers.Count; i++) StepWalker(tripWalkers[i], dt);
                bool any = false;
                foreach (var b in tripWalkers) if (b.trip != null) any = true;
                if (!any) { tripsNote = "done"; StopTrips(); }
            }
            catch (System.Exception e)
            {
                tripsNote = "FAIL: " + e.GetType().Name + ": " + e.Message;
                StopTrips();
            }
        }

        static bool TripsSetup()
        {
            tripCamp = Camp();
            if (tripCamp == null) { tripsNote = "FAIL: no camp"; return false; }
            var map = CampPath.For(tripCamp);
            map.HasRoute(tripCamp.CampCentre, tripCamp.CampCentre + Vector3.right, CampPath.Walker.Hand);   // builds
            map.ResyncSolids();
            walkMI = typeof(CampWorker).GetMethod("Walk", Any, null, new[] { typeof(Vector3), typeof(float) }, null);
            clearMI = typeof(CampWorker).GetMethod("ClearRoute", Any);
            budgetFI = typeof(CampPath).GetField("budgetFrame", Any);
            // Borrow up to three (`WalkSlipProbe.Drive`'s way).
            foreach (var w in new List<CampWorker>(CampWorker.Bodies))
            {
                if (tripWalkers.Count >= 3) break;
                if (w == null || !w.isActiveAndEnabled || w.OnTower || CampOf(w) != tripCamp) continue;
                var acting = w.GetComponent<VillagerActing>();
                if (acting == null) continue;
                w.enabled = false;
                acting.WalkGait = VillagerActing.Gait.Errand;
                acting.Set(VillagerActing.Mode.None);
                clearMI.Invoke(w, null);
                tripWalkers.Add(new Walker { w = w, home = w.transform.position });
            }
            if (tripWalkers.Count == 0) { tripsNote = "FAIL: no villager to borrow"; return false; }
            CollectTargets(tripWalkers[0].w, map);
            if (tripTargets.Count < 2) { tripsNote = "FAIL: no targets"; return false; }
            foreach (var b in tripWalkers) NextTrip(b);
            tripsNote = $"running: {tripWalkers.Count} villagers, {tripTargets.Count} targets";
            return true;
        }

        /// Every target the real resolvers give, plus random free points.
        static void CollectTargets(CampWorker w, CampPath map)
        {
            var camp = tripCamp;
            var store = typeof(CampWorker).GetMethod("StoreSpot", Any);
            var input = typeof(CampWorker).GetMethod("InputSpot", Any);
            var output = typeof(CampWorker).GetMethod("OutputSpot", Any);
            var edge = typeof(CampWorker).GetMethod("EdgeBeyond", Any);
            var args2 = new object[2];
            args2[0] = "Timber";
            tripTargets.Add(("StoreSpot", (Vector3)store.Invoke(w, args2)));
            float x0 = 1e9f, z0 = 1e9f, x1 = -1e9f, z1 = -1e9f;
            foreach (var b in camp.Built)
            {
                if (b == null || b.Kind == BuildKind.Pier || b.Kind == BuildKind.DryDock) continue;
                var p = b.transform.position;
                x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x); z0 = Mathf.Min(z0, p.z); z1 = Mathf.Max(z1, p.z);
                string tag = $"{b.Id}@({p.x - camp.CampCentre.x:0},{p.z - camp.CampCentre.z:0})";
                args2[0] = b;
                tripTargets.Add((tag + " InputSpot", (Vector3)input.Invoke(w, args2)));
                tripTargets.Add((tag + " OutputSpot", (Vector3)output.Invoke(w, args2)));
                tripTargets.Add((tag + " WorkSpot", CampWorker.WorkSpot(camp, b)));
                tripTargets.Add((tag + " EdgeBeyond", (Vector3)edge.Invoke(w, new object[] { b, camp.CampCentre })));
                foreach (var t in b.GetComponentsInChildren<Transform>(true))
                    if (BuildingFactoryStem(t.name) == "Entry" && map.SolidDistance(t.position) >= 0f)
                        tripTargets.Add((tag + " Entry", t.position));
            }
            // Random free points among the buildings.
            int want = Mathf.Max(8, tripsWanted / 4), tries = 0, got = 0;
            while (got < want && tries++ < want * 40)
            {
                var q = new Vector3(Mathf.Lerp(x0 - 4f, x1 + 4f, (float)tripRng.NextDouble()), 0f,
                                    Mathf.Lerp(z0 - 4f, z1 + 4f, (float)tripRng.NextDouble()));
                if (map.SolidDistance(q) < 0.4f || !map.Reachable(q) || !map.WalkableAt(q, CampPath.Walker.Hand)) continue;
                q.y = camp.GroundAt(q);
                tripTargets.Add(($"free({q.x - camp.CampCentre.x:0.0},{q.z - camp.CampCentre.z:0.0})", q));
                got++;
            }
        }

        static void NextTrip(Walker b)
        {
            if (b.trip != null) tripsDone.Add(b.trip);
            b.trip = null;
            if (tripsStarted >= tripsWanted) return;
            Vector3 here = b.w.transform.position;
            for (int k = 0; k < 50; k++)
            {
                var (label, at) = tripTargets[tripRng.Next(tripTargets.Count)];
                if (Flat(here, at) < 3f) continue;
                tripsStarted++;
                clearMI.Invoke(b.w, null);
                b.trip = new Trip { label = label, from = here, to = at, straight = Flat(here, at) };
                b.win = 0f;
                b.winFrom = here;
                b.pinRun = 0f;
                b.inside = tripCamp != null && CampPath.SolidDistance(tripCamp, here) < -0.02f;
                return;
            }
        }

        static void StepWalker(Walker b, float dt)
        {
            var tr = b.trip;
            if (tr == null || b.w == null) return;
            budgetFI.SetValue(null, -1);
            bool done = (bool)walkMI.Invoke(b.w, new object[] { tr.to, dt });
            tr.t += dt;
            Vector3 here = b.w.transform.position;
            bool inside = CampPath.SolidDistance(tripCamp, here) < -0.02f;
            if (inside && !b.inside) tr.overlaps++;
            b.inside = inside;
            b.win += dt;
            if (b.win >= StuckWindow)
            {
                if (Flat(here, b.winFrom) < StuckGain)
                {
                    tr.stuck++;
                    b.pinRun += b.win;
                    tr.pin += b.win;
                    tr.longestPin = Mathf.Max(tr.longestPin, b.pinRun);
                }
                else b.pinRun = 0f;
                b.win = 0f;
                b.winFrom = here;
            }
            if (!done && tr.t < TripTimeout) return;
            tr.end = here;
            float d = Flat(here, tr.to);
            if (!done) tr.result = "TIMEOUT";
            else if (d <= ArriveOk) tr.result = "arrived";
            else if (HeldByOther(b.w, tr.to)) tr.result = "held";
            else tr.result = d <= 1.2f ? "FAKE(pin 1.2 m)" : "FAKE(stall)";
            NextTrip(b);
        }

        static bool HeldByOther(CampWorker me, Vector3 at)
        {
            float r = 2f * CampWorker.BodyRadius + 0.2f;
            foreach (var o in CampWorker.Bodies)
                if (o != null && o != me && o.gameObject.activeInHierarchy && Flat(o.transform.position, at) < r) return true;
            return false;
        }

        static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        /// The trips so far (or the final count once `done`).
        public static string TripsReport()
        {
            var all = new List<Trip>(tripsDone);
            foreach (var b in tripWalkers) if (b.trip != null) all.Add(b.trip);
            int arrived = 0, fakePin = 0, fakeStall = 0, held = 0, timeouts = 0, stuck = 0, overlaps = 0, finished = 0;
            float pinTotal = 0f, pinLongest = 0f, time = 0f;
            foreach (var t in all)
            {
                if (t.result != "walking") finished++;
                if (t.result == "arrived") arrived++;
                else if (t.result == "FAKE(pin 1.2 m)") fakePin++;
                else if (t.result == "FAKE(stall)") fakeStall++;
                else if (t.result == "held") held++;
                else if (t.result == "TIMEOUT") timeouts++;
                stuck += t.stuck;
                overlaps += t.overlaps;
                pinTotal += t.pin;
                pinLongest = Mathf.Max(pinLongest, t.longestPin);
                time += t.t;
            }
            var sb = new StringBuilder();
            sb.Append($"trips {tripsNote}: {finished}/{tripsWanted} finished, {tripWalkers.Count} walking now, {tripTargets.Count} targets\n");
            sb.Append($"arrived(<= {ArriveOk} m) {arrived}, fake arrivals {fakePin + fakeStall} (pin 1.2 m {fakePin}, stall {fakeStall}), held by another {held}, not arrived in {TripTimeout:0} s {timeouts}\n");
            sb.Append($"stuck events (< {StuckGain} m in {StuckWindow} s) {stuck}, pinned total {pinTotal:0.0} s, longest pin {pinLongest:0.0} s, overlap events {overlaps}, walked {time:0} s\n");
            all.Sort((a, c) => Badness(c).CompareTo(Badness(a)));
            sb.Append("worst trips:\n");
            var o = tripCamp != null ? tripCamp.CampCentre : Vector3.zero;
            for (int i = 0; i < all.Count && i < 10; i++)
            {
                var t = all[i];
                if (Badness(t) <= 0f) break;
                sb.Append($"  {t.result} {t.t:0.0} s, pin {t.longestPin:0.0} s (total {t.pin:0.0}), stuck {t.stuck}, overlaps {t.overlaps}: ");
                sb.Append($"({t.from.x - o.x:0.0},{t.from.z - o.z:0.0}) -> {t.label} ({t.to.x - o.x:0.0},{t.to.z - o.z:0.0}), ended ({t.end.x - o.x:0.0},{t.end.z - o.z:0.0}) {Flat(t.end, t.to):0.00} m off, straight {t.straight:0.0} m\n");
            }
            return sb.ToString();
        }

        static float Badness(Trip t)
        {
            float s = t.longestPin * 10f + t.overlaps * 5f + t.stuck;
            if (t.result == "TIMEOUT") s += 10000f;
            else if (t.result.StartsWith("FAKE")) s += 1000f;
            return s;
        }
    }
}
