using System.Collections.Generic;
using System.Reflection;
using System.Text;
using SeaSick.World;
using UnityEngine;

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
    }
}
