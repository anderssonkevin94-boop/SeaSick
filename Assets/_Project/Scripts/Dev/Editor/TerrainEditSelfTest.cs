using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using SeaSick.Terrain;

/// **Gate for `TerrainEdits` (Kevin's home spit, 2026-10-04).** Edit mode,
/// plain C#, no play. Compares the edited height field against the same
/// params with `edits = default`:
///   1. keyed: only seed 1337 + the asset's world offset get edits;
///   2. the spit was where the capsules say (crest -1.2..+0.5 m before) and is
///      now at or below the target;
///   3. a 30 m-radius turning circle at (-548, 226) is all at or below
///      -3.4 m (measured turn r15.1 m), and the kept beach ends as a rounded
///      sandy point with no cliff;
///   4. ZERO delta under every saved structure of the home outpost in the save
///      at `savePath` (buildings 14 m squares, pier deck + berth + pile bed,
///      dry dock + 2 m, walls and roads +-2 m) -- or, with no save, under
///      Kevin's dry dock, pier and berth as measured from his a1 save;
///   5. home island's 46-sector shore outline (`TerrainWorldPopulator.
///      MeasureProfile`'s rule) and every > 0.5 m land cell unchanged, so the
///      seeded world build is unchanged;
///   6. other islands (the save's other camps, else four fixed centres) zero
///      delta out to 130 m;
///   7. nothing lowered below the target (reef -4 m / anchorage -12 m
///      thresholds keep their answers).
/// Run: `unity cmd eval --json --code 'return TerrainEditSelfTest.Run();'`
/// or `TerrainEditSelfTest.Run("/path/to/seasick-save-a1.json")`.
public static class TerrainEditSelfTest
{
    const string SettingsPath = "Assets/_Project/Materials/GraphicArt/TerrainSettings.asset";

    public static string Run(string savePath = null)
    {
        var sb = new StringBuilder();
        int fails = 0;
        void Gate(string name, bool ok, string detail)
        {
            if (!ok) fails++;
            sb.AppendLine((ok ? "PASS " : "FAIL ") + name + "  " + detail);
        }

        var settings = AssetDatabase.LoadAssetAtPath<TerrainSettings>(SettingsPath);
        if (settings == null) return "FAIL no TerrainSettings at " + SettingsPath;
        var edited = TerrainParams.From(settings);
        var plain = edited; plain.edits = default;
        var lut = TerrainCurveLut.Bake(settings.profileCurve, Allocator.Temp);
        try
        {
            float Before(float2 p) => TerrainHeight.Height(p, plain, lut);
            float After(float2 p) => TerrainHeight.Height(p, edited, lut);
            float T = edited.seaLevel + TerrainEdits.SpitTarget;

            // 1. keyed
            Gate("keyed-to-this-world", edited.edits.count == 3 && edited.edits.tipCount == 1
                    && TerrainEdits.For(edited.seed + 1, edited.worldOffset).hasAny == 0
                    && TerrainEdits.For(edited.seed, edited.worldOffset + 1f).hasAny == 0,
                 "capsules " + edited.edits.count + " + point " + edited.edits.tipCount + " seed " + edited.seed);

            // 2. spit crest
            var crest = new[] { new float2(-575, 219), new float2(-560, 218), new float2(-550, 216), new float2(-520, 216),
                                new float2(-490, 216), new float2(-460, 226), new float2(-440, 234), new float2(-420, 244),
                                new float2(-400, 250) };
            float worstBefore = 99f, worstAfter = -99f, highBefore = -99f;
            foreach (var q in crest)
            {
                float b = Before(q), a = After(q);
                worstBefore = math.min(worstBefore, b); highBefore = math.max(highBefore, b);
                worstAfter = math.max(worstAfter, a);
            }
            Gate("spit-was-there", worstBefore > -1.2f && highBefore < 0.5f,
                 "crest before " + worstBefore.ToString("F2") + ".." + highBefore.ToString("F2") + " m");
            Gate("spit-is-gone", worstAfter <= T + 0.01f, "crest after <= " + worstAfter.ToString("F2") + " m (target " + T + ")");

            // 3. turning circle
            float circleMax = -99f;
            // Her measured turn (play test 2026-10-04): r 15.1 m at 4 m/s,
            // so a 30 m circle is the circle plus her own length.
            var cc = new float2(-548, 226);
            for (float x = -30; x <= 30; x += 2)
                for (float z = -30; z <= 30; z += 2)
                    if (x * x + z * z <= 30 * 30) circleMax = math.max(circleMax, After(cc + new float2(x, z)));
            Gate("turning-circle-clear", circleMax <= T + 0.1f, "max " + circleMax.ToString("F2") + " m in r30 at " + cc);

            // The kept beach ends as a rounded sandy POINT, not a cut (Kevin:
            // no squared-off arm at the dry dock). Steepest 1 m step anywhere
            // the edit changed the ground, vs the natural flanks' own (~1.2 m;
            // the first capsule-only cut made 1.7 m), and the crest falling
            // steadily from the beach into the water.
            float steepest = 0f;
            for (float x = -618; x <= -540; x += 1f)
                for (float z = 198; z <= 250; z += 1f)
                {
                    var p = new float2(x, z);
                    float a0 = After(p), b0 = Before(p);
                    foreach (var q in new[] { p + new float2(1, 0), p + new float2(0, 1) })
                    {
                        float a1 = After(q);
                        if (a0 != b0 || a1 != Before(q)) steepest = math.max(steepest, math.abs(a1 - a0));
                    }
                }
            bool falling = true; float prevH = 99f; var prof = new StringBuilder();
            for (float x = -606; x <= -576; x += 2f)
            {
                float a = After(new float2(x, 222));
                if (a > prevH + 0.01f) falling = false;
                prevH = a;
                if ((int)x % 6 == 0) prof.Append((int)x).Append(':').Append(a.ToString("F2")).Append(' ');
            }
            Gate("sandy-point-no-cliff", steepest <= 1.3f && falling,
                 "steepest step " + steepest.ToString("F2") + " m/m, crest " + prof);

            // 4 + 6. structures and other islands
            var points = new List<(string, float2)>();
            var others = new List<float2>();
            string source = LoadSave(savePath, points, others);
            if (others.Count == 0)
                others.AddRange(new[] { new float2(-132, 561), new float2(660, 33), new float2(204, -447), new float2(324, 1329) });
            var worst = new Dictionary<string, float>();
            foreach (var (label, p) in points)
            {
                float d = math.abs(After(p) - Before(p));
                worst[label] = worst.TryGetValue(label, out var w) ? math.max(w, d) : d;
            }
            float structWorst = 0f;
            var per = new StringBuilder();
            foreach (var kv in worst) { structWorst = math.max(structWorst, kv.Value); per.Append(kv.Key).Append('=').Append(kv.Value.ToString("G3")).Append(' '); }
            Gate("structures-untouched", structWorst == 0f && points.Count > 0,
                 points.Count + " points from " + source + ", max |delta| " + structWorst + " (" + per + ")");

            float otherWorst = 0f; int otherN = 0;
            foreach (var c in others)
                for (float r = 0; r <= 130; r += 10)
                    for (int k = 0; k < 24; k++)
                    {
                        float a = k * math.PI * 2f / 24f;
                        var p = c + r * new float2(math.sin(a), math.cos(a));
                        otherWorst = math.max(otherWorst, math.abs(After(p) - Before(p))); otherN++;
                    }
            Gate("other-islands-untouched", otherWorst == 0f, otherN + " points round " + others.Count + " islands, max |delta| " + otherWorst);

            // 5. home outline + land cells
            int changed = 0;
            var home = TerrainEdits.SpitIsland;
            for (int s = 0; s < 46; s++)
            {
                float ang = s / 46f * math.PI * 2f;
                var dir = new float2(math.sin(ang), math.cos(ang));
                float r0 = -1f, r1 = -1f;
                for (float r = 2f; r < 800f && (r0 < 0f || r1 < 0f); r += 2f)
                {
                    var p = home + dir * r;
                    if (r0 < 0f && Before(p) < -0.3f) r0 = r;
                    if (r1 < 0f && After(p) < -0.3f) r1 = r;
                }
                if (r0 != r1) changed++;
            }
            Gate("home-outline-unchanged", changed == 0, changed + " of 46 sectors moved");

            var bb = edited.edits.bounds;
            int landFlips = 0, belowTarget = 0, lowered = 0;
            for (float x = bb.x; x <= bb.z; x += 2f)
                for (float z = bb.y; z <= bb.w; z += 2f)
                {
                    var p = new float2(x, z);
                    float b = Before(p), a = After(p);
                    if ((b > 0.5f) != (a > 0.5f) || (b > 0.5f && a != b)) landFlips++;
                    if (a < b) { lowered++; if (a < T - 1e-4f) belowTarget++; }
                }
            Gate("dry-land-untouched", landFlips == 0, landFlips + " land cells changed in the edit box");
            Gate("never-below-target", belowTarget == 0, lowered + " points lowered, " + belowTarget + " below " + T);
        }
        finally { lut.Dispose(); }

        sb.Insert(0, (fails == 0 ? "TerrainEditSelfTest PASS" : "TerrainEditSelfTest FAIL x" + fails) + "\n");
        Debug.Log(sb.ToString());
        return sb.ToString();
    }

    /// Structures of the home outpost (and other camps' island centres) from a
    /// save; Kevin's measured dry dock / pier / berth when there is none.
    static string LoadSave(string path, List<(string, float2)> points, List<float2> others)
    {
        SeaSick.Save.SaveData data = null;
        if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
            data = JsonUtility.FromJson<SeaSick.Save.SaveData>(System.IO.File.ReadAllText(path));
        if (data == null)
        {
            Rect(points, "DryDock", new float2(-611.92f, 242.54f), -80f, 28f, 16f, 1f);
            Rect(points, "Pier", new float2(-651.25f, 263.31f), 9.08f, 26f, 8f, 1f);
            points.Add(("Berth", new float2(-640.39f, 261.57f)));
            return "built-in (Kevin a1)";
        }
        foreach (var o in data.outposts)
        {
            if (!o.isHome) { if (o.hasIsle) others.Add(new float2(o.isleX, o.isleZ)); continue; }
            var L = o.ledger;
            if (L == null) continue;
            foreach (var b in L.raised)
            {
                var c = new float2(b.x, b.z);
                if (b.planId == "Pier") Rect(points, "Pier", c, b.yaw, b.length + 12f, 8f, 1f);
                else if (b.planId == "DryDock") Rect(points, "DryDock", c, b.yaw, 28f, 16f, 1f);
                else Rect(points, b.planId, c, b.yaw, 14f, 14f, 2f);
            }
            foreach (var w in L.builtWalls) Line(points, "Wall", new float2(w.ax, w.az), new float2(w.bx, w.bz));
            foreach (var r in L.builtRoads) Line(points, "Road", new float2(r.ax, r.az), new float2(r.bx, r.bz));
        }
        if (data.ship != null && data.ship.hasHomeBerth)
            points.Add(("Berth", new float2(data.ship.homeBerthX, data.ship.homeBerthZ)));
        return System.IO.Path.GetFileName(path);
    }

    /// A building's frame: local +X (planks, slip) = transform.right.
    static void Rect(List<(string, float2)> pts, string label, float2 c, float yaw, float length, float width, float step)
    {
        float th = math.radians(yaw);
        var right = new float2(math.cos(th), -math.sin(th));
        var fwd = new float2(math.sin(th), math.cos(th));
        int nl = Mathf.CeilToInt(length / step), nw = Mathf.CeilToInt(width / step);
        for (int i = 0; i <= nl; i++)
            for (int j = 0; j <= nw; j++)
                pts.Add((label, c + right * (-length * 0.5f + length * i / nl) + fwd * (-width * 0.5f + width * j / nw)));
    }

    static void Line(List<(string, float2)> pts, string label, float2 a, float2 b)
    {
        int n = Mathf.Max(2, Mathf.CeilToInt(math.distance(a, b)));
        for (int k = 0; k <= n; k++)
        {
            var p = math.lerp(a, b, k / (float)n);
            for (int ox = -2; ox <= 2; ox += 2)
                for (int oz = -2; oz <= 2; oz += 2)
                    pts.Add((label, p + new float2(ox, oz)));
        }
    }
}
