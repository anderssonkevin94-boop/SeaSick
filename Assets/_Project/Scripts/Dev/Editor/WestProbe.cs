using System.Text;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using SeaSick.Terrain;

/// Where west is actually deep enough to spawn in.
///
/// The lesson this exists for: a hardcoded warp coordinate eventually lands on
/// an island, because the world is procedural. StallProbe warps to
/// (-1500, 0, 0) and has reported nothing for weeks because that spot is
/// twelve metres of water — and the first pass of this probe found that
/// 1500 m due west, the obvious playtest spawn, is FORTY METRES ABOVE SEA
/// LEVEL. There is an island chain across the western approach.
///
/// The bar is not "is it wet". The depth limit caps a wave at
/// breakFraction x depth / Hs, so at breakFraction 0.55 and the storm's
/// nominal Hs 65 the sea does not reach full height until about 118 m of
/// water. Anything shallower is a quietly shrunk sea that reads as a tuning
/// problem, which is exactly the trap StallProbe fell into.
///
/// So this does not pick a distance — it picks a POCKET. It scans the western
/// sector, measures how far you can sail from each candidate before the water
/// comes up, and takes the best open-water room, nearest home breaking ties.
/// Writes the map to Temp/west-probe.txt.
public static class WestProbe
{
    const string SettingsPath = "Assets/_Project/Settings/Terrain/TerrainSettings.asset";
    const string OutPath = "Temp/west-probe.txt";

    // RegionField's shipped geography, mirrored so the probe can report storm
    // weight and envelope without entering play mode.
    const float StormNear = 450f, StormFar = 1250f;
    const float CalmRadius = 260f, WildRadius = 1150f;
    const float NearScale = 0.35f, FarScale = 1f;
    const float BreakFraction = 0.55f, StormHs = 65f;

    static readonly Vector2 Home = new Vector2(0f, -75f);

    // Search window, metres west of home and either side of the axis.
    const float MinWest = 600f, MaxWest = 6000f, HalfWidth = 3000f;
    const float Step = 100f;
    // How far open water must extend for the pocket to be worth spawning in.
    const float WantRoom = 900f;

    public static string Execute()
    {
        var s = AssetDatabase.LoadAssetAtPath<TerrainSettings>(SettingsPath);
        if (s == null) return "no TerrainSettings at " + SettingsPath;

        var prm = TerrainParams.From(s);
        var lut = TerrainCurveLut.Bake(s.profileCurve, Allocator.Temp);

        float needDepth = StormHs / BreakFraction;

        int nx = Mathf.RoundToInt((MaxWest - MinWest) / Step) + 1;
        int nz = Mathf.RoundToInt(2f * HalfWidth / Step) + 1;

        // Depth grid first, so the room measurement is a lookup, not a resample.
        var deep = new bool[nx, nz];
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
            {
                float2 p = Cell(i, j);
                deep[i, j] = -TerrainHeight.Height(p, prm, lut) >= needDepth;
            }

        // Room = distance to the nearest cell that is NOT deep enough, capped
        // at the window edge so a candidate near the boundary can't win by
        // being unmeasured.
        float bestScore = float.MinValue;
        int bi = -1, bj = -1; float bestRoom = 0f;
        int maxR = Mathf.RoundToInt(WantRoom / Step);

        for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
            {
                if (!deep[i, j]) continue;
                float room = maxR * Step;
                for (int r = 1; r <= maxR && room > (r - 1) * Step; r++)
                    for (int dx = -r; dx <= r; dx++)
                        for (int dz = -r; dz <= r; dz++)
                        {
                            if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue;
                            int qi = i + dx, qj = j + dz;
                            bool bad = qi < 0 || qj < 0 || qi >= nx || qj >= nz || !deep[qi, qj];
                            if (bad) { room = Mathf.Min(room, (r - 1) * Step); break; }
                        }

                float2 p = Cell(i, j);
                float dist = math.distance(p, new float2(Home.x, Home.y));
                float storm = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(StormNear, StormFar, Home.x - p.x));
                if (storm < 0.995f) continue;

                // Room is what matters; nearness only breaks ties, so a pocket
                // twice as roomy always beats one a kilometre closer.
                float score = room - 0.15f * dist;
                if (score > bestScore) { bestScore = score; bi = i; bj = j; bestRoom = room; }
            }

        var sb = new StringBuilder();
        sb.AppendLine("WestProbe — open water west of home " + Home);
        sb.AppendLine("full-height sea needs " + needDepth.ToString("F0") + " m of water"
            + " (Hs " + StormHs + " / breakFraction " + BreakFraction + ")");
        sb.AppendLine($"scanned {MinWest:F0}..{MaxWest:F0} m west, +-{HalfWidth:F0} m wide, {Step:F0} m grid");
        sb.AppendLine();

        // Axis profile, kept because it is what a human reads to understand
        // the coastline rather than to pick a number.
        sb.AppendLine("  dist   depth_on_axis   storm   env");
        for (float d = MinWest; d <= MaxWest; d += 200f)
        {
            float2 p = new float2(Home.x - d, Home.y);
            float depth = -TerrainHeight.Height(p, prm, lut);
            float storm = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(StormNear, StormFar, d));
            float env = Mathf.Lerp(NearScale, FarScale,
                Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(CalmRadius, WildRadius, d)));
            sb.AppendLine($"  {d,5:F0}   {depth,10:F1}   {storm,5:F2}   {env,4:F2}"
                + (depth < 0f ? "   LAND" : depth < needDepth ? "   shallow" : ""));
        }
        sb.AppendLine();

        string pick;
        if (bi < 0)
        {
            pick = "NO deep-water pocket found in the western sector";
        }
        else
        {
            float2 p = Cell(bi, bj);
            float depth = -TerrainHeight.Height(p, prm, lut);
            float dist = math.distance(p, new float2(Home.x, Home.y));
            float bearing = Mathf.Atan2(p.y - Home.y, p.x - Home.x) * Mathf.Rad2Deg;
            pick = $"best pocket ({p.x:F0}, {p.y:F0}) — {depth:F0} m deep, "
                 + $"{bestRoom:F0} m of open water all round, {dist:F0} m from home at {bearing:F0} deg";
            sb.AppendLine(pick);
            sb.AppendLine($"  offset from home: dx {p.x - Home.x:F0}, dz {p.y - Home.y:F0}");
        }

        lut.Dispose();
        System.IO.File.WriteAllText(OutPath, sb.ToString());
        Debug.Log("WestProbe: " + pick);
        return pick;
    }

    static float2 Cell(int i, int j) =>
        new float2(Home.x - MinWest - i * Step, Home.y - HalfWidth + j * Step);
}
