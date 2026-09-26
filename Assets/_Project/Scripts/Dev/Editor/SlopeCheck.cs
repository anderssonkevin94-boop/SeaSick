using System.Collections.Generic;
using System.Text;
using SeaSick.World;
using UnityEngine;

/// **Slope check (2026-09-27): can the camp's people still get everywhere
/// they need to, and nowhere they shouldn't?**
///
/// Kevin's phone report: villagers and goats walked straight up mountain
/// sides. The fix put every walker through `Walkability` (men / raiders /
/// boar 33 degrees, goats 45, a per-step backstop at 1.25x) and made the
/// target picks skip what `CampPath.Reachable` says the fire cannot reach.
/// This is the read-out that says the cap did not seal the camp off.
///
/// PLAY mode, a loaded camp. One `unity cmd eval` call:
///
///   `SlopeCheck.Run("Island_2")` -- or `Run()` for the first camp found;
///   `Run("Island_2", "/tmp/seasick-slope.png")` also writes a top-down
///   picture of the walkability grid (1 px per 2 m cell, x4): sea blue,
///   closed-by-slope red, rock grey, wall black, open-and-reachable green,
///   open-but-cut-off yellow; the fire white, targets magenta.
///
/// Reports: % of land cells closed by slope; for every built building,
/// pier, the nearest unfelled tree, the nearest stone / ore node and the
/// nearest animal -- reachable from the fire or not, the route's corner
/// count, and the steepest grade and biggest 2 m rise ALONG that route
/// (sampled every 0.5 m on the real ground, not at cell centres); and each
/// hand's current standing slope. Text also to `/tmp/seasick-slopecheck.txt`.
///
/// e.g. `unity cmd eval --json --code 'return SlopeCheck.Run("Island_2", "/tmp/seasick-slope.png");'`
public static class SlopeCheck
{
    public static string Run(string islandName = null, string pngPath = null)
    {
        var sb = new StringBuilder();
        if (!Application.isPlaying) return "SlopeCheck: enter play mode first.";

        Outpost camp = null;
        foreach (var o in Outpost.All)
        {
            if (o == null || !o.Sited) continue;
            if (string.IsNullOrEmpty(islandName) || (o.Island != null && o.Island.name == islandName) || o.name == islandName)
            { camp = o; break; }
        }
        if (camp == null) return $"SlopeCheck: no sited camp for '{islandName}'.";

        var map = CampPath.For(camp);
        Vector3 fire = camp.CampCentre;
        map.Reachable(fire);   // builds the map if it is not up yet
        if (!map.Built) return "SlopeCheck: the camp's path grid would not build (camp not sited?).";

        float manDeg = Walkability.ManMaxDegrees, goatDeg = Walkability.GoatMaxDegrees;
        sb.AppendLine($"SlopeCheck {camp.name} (island {(camp.Island != null ? camp.Island.name : "?")})");
        sb.AppendLine($"caps: man/raider/boar {manDeg:0} deg (grade {Walkability.Grade(Walkability.Feet.Man):0.00}, "
                    + $"max 2 m rise {Walkability.MaxStep(Walkability.Feet.Man):0.00} m), goat {goatDeg:0} deg, "
                    + $"step backstop x{Walkability.StepSlack:0.00}");
        sb.AppendLine($"grid {map.Side}x{map.Side} @ {map.CellSize:0.0} m; land cells closed by slope: {map.SlopeClosedPercent:0.0}%");

        int bad = 0;
        var route = new List<Vector3>();

        void Target(string label, Vector3 at)
        {
            bool reach = map.Reachable(at);
            bool routed = map.Route(fire, at, CampPath.Walker.Hand, route);
            string walk = "no route";
            if (routed)
            {
                Along(camp, fire, route, out float grade, out float rise);
                walk = $"{route.Count} corners, steepest {Mathf.Atan(grade) * Mathf.Rad2Deg:0} deg, biggest 2 m rise {rise:0.00} m";
            }
            if (!reach) bad++;
            sb.AppendLine($"  {(reach ? "ok  " : "CUT ")} {label} @ ({at.x:0},{at.z:0}) -- {walk}");
        }

        sb.AppendLine("buildings / piers:");
        foreach (var b in Object.FindObjectsByType<Building>(FindObjectsSortMode.None))
            if (b != null && Island.Nearest(b.transform.position) == camp.Island) Target("building " + b.name, b.transform.position);
        foreach (var p in Object.FindObjectsByType<Pier>(FindObjectsSortMode.None))
            if (p != null && Island.Nearest(p.transform.position) == camp.Island) Target("pier " + p.name, p.transform.position);

        sb.AppendLine("nearest resources:");
        {
            float best = float.MaxValue; Vector3 bestAt = default; bool any = false;
            for (int i = 0; i < 20000; i++)
            {
                if (!camp.TreeBase(i, out var at)) break;
                if (camp.TreeIsFelled(i)) continue;
                float d = (at - fire).sqrMagnitude;
                if (d < best) { best = d; bestAt = at; any = true; }
            }
            if (any) Target("tree", bestAt); else sb.AppendLine("  (no standing tree)");
        }
        var nearestByRes = new Dictionary<string, ResourceNode>();
        foreach (var n in ResourceNode.All)
        {
            if (n == null || n.Harvested || n.Resource == Res.Timber) continue;
            if (camp.Island != null && n.Home != camp.Island) continue;
            if (!nearestByRes.TryGetValue(n.Resource, out var cur)
                || (n.transform.position - fire).sqrMagnitude < (cur.transform.position - fire).sqrMagnitude)
                nearestByRes[n.Resource] = n;
        }
        foreach (var kv in nearestByRes) Target("node " + kv.Key, kv.Value.transform.position);
        var herd = camp.FaunaHere();
        if (herd != null && herd.Animals != null)
        {
            Animal best = null; float bd = float.MaxValue; int reachable = 0, alive = 0;
            foreach (var a in herd.Animals)
            {
                if (a == null || a.Dead) continue;
                alive++;
                if (map.Reachable(a.transform.position)) reachable++;
                float d = (a.transform.position - fire).sqrMagnitude;
                if (d < bd) { bd = d; best = a; }
            }
            if (best != null) Target("animal " + best.name, best.transform.position);
            sb.AppendLine($"  herd: {reachable}/{alive} animals on the fire's ground");
        }

        sb.AppendLine("hands (standing slope):");
        foreach (var w in Object.FindObjectsByType<CampWorker>(FindObjectsSortMode.None))
        {
            if (w == null || Island.Nearest(w.transform.position) != camp.Island) continue;
            Vector3 p = w.transform.position;
            float h = camp.GroundAt(p), worst = 0f;
            for (int d = 0; d < 8; d++)
            {
                float ang = d * Mathf.PI * 0.25f;
                var q = p + new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * Walkability.Span;
                worst = Mathf.Max(worst, Mathf.Abs(camp.GroundAt(q) - h) / Walkability.Span);
            }
            sb.AppendLine($"  {w.name} @ ({p.x:0},{p.z:0}) {Mathf.Atan(worst) * Mathf.Rad2Deg:0} deg"
                        + (worst > Walkability.Grade(Walkability.Feet.Man) * Walkability.StepSlack ? "  STEEP" : ""));
        }

        sb.AppendLine(bad == 0 ? "PASS: every checked target is on the fire's ground."
                               : $"FAIL: {bad} target(s) cut off from the fire.");

        if (!string.IsNullOrEmpty(pngPath))
        {
            int n = map.Side, s = 4;
            var tex = new Texture2D(n * s, n * s, TextureFormat.RGB24, false);
            var px = new Color32[n * s * n * s];
            Color32[] pal =
            {
                new Color32(40, 80, 160, 255), new Color32(200, 40, 40, 255), new Color32(120, 120, 120, 255),
                new Color32(0, 0, 0, 255), new Color32(60, 170, 70, 255), new Color32(230, 200, 40, 255),
            };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    var c = pal[map.CellKind(x, y)];
                    for (int yy = 0; yy < s; yy++)
                        for (int xx = 0; xx < s; xx++)
                            px[(y * s + yy) * n * s + x * s + xx] = c;
                }
            void Dot(Vector3 at, Color32 c)
            {
                int cx = Mathf.RoundToInt((at.x - map.Origin.x) / map.CellSize) * s;
                int cy = Mathf.RoundToInt((at.z - map.Origin.y) / map.CellSize) * s;
                for (int yy = -3; yy <= 3; yy++)
                    for (int xx = -3; xx <= 3; xx++)
                    {
                        int X = cx + xx, Y = cy + yy;
                        if (X >= 0 && Y >= 0 && X < n * s && Y < n * s) px[Y * n * s + X] = c;
                    }
            }
            foreach (var n2 in nearestByRes.Values) Dot(n2.transform.position, new Color32(230, 60, 230, 255));
            Dot(fire, new Color32(255, 255, 255, 255));
            tex.SetPixels32(px);
            tex.Apply();
            System.IO.File.WriteAllBytes(pngPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            sb.AppendLine("png: " + pngPath);
        }

        var text = sb.ToString();
        try { System.IO.File.WriteAllText("/tmp/seasick-slopecheck.txt", text); } catch { }
        return text;
    }

    /// Walk a route's polyline on the real ground every 0.5 m: the steepest
    /// grade over 1 m and the biggest rise over 2 m.
    static void Along(Outpost camp, Vector3 from, List<Vector3> corners, out float grade, out float rise)
    {
        grade = 0f; rise = 0f;
        var pts = new List<float>();
        Vector3 a = from;
        for (int k = 0; k < corners.Count; k++)
        {
            Vector3 b = corners[k];
            Vector3 d = b - a; d.y = 0f;
            float len = d.magnitude;
            int steps = Mathf.Max(1, Mathf.CeilToInt(len / 0.5f));
            for (int i = 0; i < steps; i++)
                pts.Add(camp.GroundAt(a + d * (i / (float)steps)));
            a = b;
        }
        pts.Add(camp.GroundAt(a));
        for (int i = 0; i < pts.Count; i++)
        {
            if (i + 2 < pts.Count) grade = Mathf.Max(grade, Mathf.Abs(pts[i + 2] - pts[i]));
            if (i + 4 < pts.Count) rise = Mathf.Max(rise, Mathf.Abs(pts[i + 4] - pts[i]));
        }
    }
}
