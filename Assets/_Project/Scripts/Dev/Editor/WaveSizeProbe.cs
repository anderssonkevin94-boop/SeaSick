using System.Collections;
using System.Text;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using SeaSick.Ocean;
using SeaSick.Ship;
using SeaSick.World;

/// How big are the waves, and CAN SHE CLIMB THEM. Height alone has already
/// fooled this project once -- the old sea read as a sheet at 16.6 m of
/// amplitude because the faces were 0.6 degrees -- so the acceptance criterion
/// here is slope and face length, not metres.
///
/// Reports, per sea state, over a patch of genuine deep water:
///   Hs = 4 x RMS, the figure a forecast quotes.
///   patch max-min, comparable with this probe's older runs.
///   FACE ANGLE from the surface normal: median / p90 / p99 / max. This is
///     the number that says whether the sea reads as water or as a sheet.
///   Wavelength and per-wave height by zero-upcrossing along transects run in
///     the direction the waves actually travel (found by steepest transect,
///     not assumed from the wind setting).
///   FACE LENGTH = lambda/2, in boat lengths -- how far she climbs.
///   SEABED CLEARANCE, the minimum of surface minus terrain over the patch.
///     Negative means the trough is under the seafloor.
///
/// Sampled over AREA at one instant rather than one point over time: a single
/// point measures the wave period as much as its height. Uses SampleBatch,
/// the real API, not thousands of SampleImmediate calls.
///
/// Play mode, Sea.unity. Writes /tmp/seasick-wavesize.txt.
public class WaveSizeProbe : MonoBehaviour
{
    const float BoatLength = 24.2f;
    const float PatchHalf = 800f;      // 1600 m patch
    const float PatchStep = 20f;
    const float TransectLen = 4000f;
    const float TransectStep = 5f;
    const int TransectLines = 5;      // parallel lines, pooled, for enough waves
    const float TransectSpacing = 200f;
    // Boxcar half-width in samples. A boxcar of full width (2W+1)*step has its
    // first zero at that wavelength, so W=12 at 5 m steps erases everything
    // below ~121 m -- the wind sea -- and leaves the 500 m rollers untouched.
    // Without this the zero-upcrossing counts every ripple riding on a
    // mountain and reports the mountain as 275 m long.
    const int SmoothW = 12;
    // The patch must be in water deep enough that the depth-limited envelope
    // is NOT capping it, or this measures the cap instead of the spectrum.
    // The cap releases at depth = Hs / breakFraction, about 100 m for the
    // storm sea, so -9 m (which was right when the whole seabed was -12) now
    // reliably finds SHELF water and would read a damped sea as a broken one.
    static readonly float[] DepthLadder = { -110f, -60f, -30f, -9f };

    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("WaveSizeProbe: not in play mode"); return; }
        new GameObject("WaveSizeProbe").AddComponent<WaveSizeProbe>();
    }

    static readonly float[] Severities = { 0.25f, 0.50f, 0.75f, 1.00f };

    /// Is the WHOLE patch in undamped water? The first version of this probe
    /// warped to a hardcoded spot that turned out to be an island and measured
    /// damped water at every sea state.
    static bool DeepEverywhere(Vector3 centre, float half, float need)
    {
        var h = Island.TerrainHeight;
        if (h == null) return false;
        for (int i = -2; i <= 2; i++)
            for (int j = -2; j <= 2; j++)
            {
                float x = centre.x + i * half * 0.5f, z = centre.z + j * half * 0.5f;
                if (h(x, z) > need) return false;
            }
        return true;
    }

    /// Shallowest and deepest seabed under the patch, so the report says what
    /// water was actually measured instead of leaving it to be assumed.
    static float2 SeabedRange(Vector3 centre, float half)
    {
        var h = Island.TerrainHeight;
        float lo = 9999f, hi = -9999f;
        if (h == null) return new float2(0f, 0f);
        for (int i = -4; i <= 4; i++)
            for (int j = -4; j <= 4; j++)
            {
                float v = h(centre.x + i * half * 0.25f, centre.z + j * half * 0.25f);
                lo = math.min(lo, v); hi = math.max(hi, v);
            }
        return new float2(lo, hi);
    }

    /// Zero-upcrossing wave counting on one profile: each pair of upward
    /// mean-crossings is one wave, giving its length and its crest-to-trough.
    static void Upcross(float[] h, int n,
        System.Collections.Generic.List<float> lens,
        System.Collections.Generic.List<float> hts)
    {
        double sum = 0;
        for (int i = 0; i < n; i++) sum += h[i];
        float mean = (float)(sum / n);
        int lastUp = -1;
        float lo = 9999f, hi = -9999f;
        for (int i = 1; i < n; i++)
        {
            if (h[i] < lo) lo = h[i];
            if (h[i] > hi) hi = h[i];
            if (h[i - 1] - mean <= 0f && h[i] - mean > 0f)
            {
                if (lastUp >= 0) { lens.Add((i - lastUp) * TransectStep); hts.Add(hi - lo); }
                lastUp = i;
                lo = 9999f; hi = -9999f;
            }
        }
    }

    static float Pct(float[] sorted, float p)
    {
        if (sorted.Length == 0) return 0f;
        int i = Mathf.Clamp(Mathf.RoundToInt(p * (sorted.Length - 1)), 0, sorted.Length - 1);
        return sorted[i];
    }

    IEnumerator Start()
    {
        StringBuilder sb = new StringBuilder();

        // Tune-on-PC: play mode enters the build target's default tier (iOS =
        // Mobile), which is NOT the config being tuned. Force the ocean's own
        // quality asset and rebuild the stack around it, then say so loudly --
        // never identify a tier by a name lookup (QualitySettings.names is not
        // indexed by quality level).
        var ocean = OceanRenderer.Instance;
        var pc = Resources.Load<OceanQuality>("Ocean/OceanQuality_PC");
        string tier = "unchanged";
        if (ocean != null && pc != null)
        {
            OceanQuality.Override(pc);
            ocean.enabled = false;
            ocean.enabled = true;                  // re-runs OnEnable: new CascadeSet
            var clip = FindAnyObjectByType<OceanClipmap>();
            if (clip != null) clip.Build();
            tier = "OceanQuality_PC";
            yield return null;
        }
        var q = OceanQuality.Active;

        ShipMotor motor = FindAnyObjectByType<ShipMotor>();
        HelmInput helm = FindAnyObjectByType<HelmInput>();
        if (helm != null) helm.enabled = false;
        SeaStateController sea = SeaStateController.Instance;
        Rigidbody rb = motor != null ? motor.GetComponent<Rigidbody>() : null;

        // FIND deep water, do not assume it.
        Vector3 spot = Vector3.zero;
        bool found = false;
        float need = 0f;
        for (int d = 0; d < DepthLadder.Length && !found; d++)
        {
            need = DepthLadder[d];
            for (float dist = 2000f; dist <= 20000f && !found; dist += 250f)
                for (int b = 0; b < 16 && !found; b++)
                {
                    float ang = b / 16f * Mathf.PI * 2f;
                    Vector3 c = new Vector3(Mathf.Sin(ang) * dist, 0f, Mathf.Cos(ang) * dist);
                    if (DeepEverywhere(c, PatchHalf, need)) { spot = c; found = true; }
                }
        }
        if (!found) { Debug.LogError("WaveSizeProbe: no deep-water patch found"); yield break; }
        float2 bedRange = SeabedRange(spot, PatchHalf);
        if (rb != null)
        {
            rb.position = new Vector3(spot.x, 2f, spot.z);
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        if (motor != null) { motor.ThrottleOrder = 0f; motor.Rudder = 0f; }
        yield return new WaitForSeconds(8f);

        sb.AppendLine("WaveSizeProbe -- how big, and can she climb it");
        sb.AppendLine("ocean quality FORCED to " + tier + ": fftSize " + q.fftSize
            + ", patch0 " + q.patchSizes[0].ToString("F0") + " m, rings " + q.clipmapRings
            + ", displacement fade " + q.displacementFadeDistance.ToString("F0") + " m");
        sb.AppendLine("deep-water patch at " + spot.ToString("F0") + ", "
            + spot.magnitude.ToString("F0") + " m from origin");
        sb.AppendLine("whole " + (PatchHalf * 2f).ToString("F0")
            + " m patch is over seabed below " + need.ToString("F0")
            + " m (checked, not assumed). Seabed under it runs "
            + bedRange.x.ToString("F0") + " to " + bedRange.y.ToString("F0") + " m");
        if (need > -100f)
            sb.AppendLine("   WARNING: no patch deeper than -110 m existed, so the depth-limited");
        if (need > -100f)
            sb.AppendLine("   envelope may be capping these numbers. Read them as the CAP, not the spectrum.");
        sb.AppendLine("boat " + BoatLength.ToString("F1") + " m");
        sb.AppendLine();

        int side = Mathf.RoundToInt(PatchHalf * 2f / PatchStep) + 1;
        int patchN = side * side;
        var terrain = Island.TerrainHeight;

        for (int s = 0; s < Severities.Length; s++)
        {
            if (sea != null) sea.ForceSeverity(Severities[s]);
            // The spectrum rebuild is throttled; give it time to settle.
            yield return new WaitForSeconds(14f);

            // ---- area pass: Hs, face angle, seabed clearance ----
            var qArr = new NativeArray<float3>(patchN, Allocator.TempJob);
            var rArr = new NativeArray<OceanSample>(patchN, Allocator.TempJob);
            for (int i = 0; i < side; i++)
                for (int j = 0; j < side; j++)
                    qArr[i * side + j] = new float3(
                        spot.x - PatchHalf + i * PatchStep, 0f,
                        spot.z - PatchHalf + j * PatchStep);
            OceanSampler.SampleBatch(qArr, rArr, default).Complete();

            double sum = 0, sumSq = 0;
            float lo = 9999f, hi = -9999f, clearance = 9999f;
            var faces = new float[patchN];
            for (int i = 0; i < patchN; i++)
            {
                float h = rArr[i].height;
                sum += h; sumSq += (double)h * h;
                if (h < lo) lo = h;
                if (h > hi) hi = h;
                float ny = Mathf.Clamp(rArr[i].normal.y, -1f, 1f);
                faces[i] = Mathf.Acos(ny) * Mathf.Rad2Deg;
                if (terrain != null)
                {
                    float bed = terrain(qArr[i].x, qArr[i].z);
                    float c = h - bed;
                    if (c < clearance) clearance = c;
                }
            }
            qArr.Dispose(); rArr.Dispose();

            float mean = (float)(sum / patchN);
            float rms = Mathf.Sqrt(Mathf.Max(0f, (float)(sumSq / patchN) - mean * mean));
            float hs = 4f * rms;
            System.Array.Sort(faces);

            // ---- transects: find the travel direction, then per-wave stats ----
            int tSteps = Mathf.RoundToInt(TransectLen / TransectStep);
            int dirs = 8;
            var tq = new NativeArray<float3>(tSteps * dirs, Allocator.TempJob);
            var tr = new NativeArray<OceanSample>(tSteps * dirs, Allocator.TempJob);
            for (int d = 0; d < dirs; d++)
            {
                float a = d / (float)dirs * Mathf.PI;   // 0..180, a line is symmetric
                float dx = Mathf.Cos(a), dz = Mathf.Sin(a);
                for (int i = 0; i < tSteps; i++)
                {
                    float t = (i - tSteps * 0.5f) * TransectStep;
                    tq[d * tSteps + i] = new float3(spot.x + dx * t, 0f, spot.z + dz * t);
                }
            }
            OceanSampler.SampleBatch(tq, tr, default).Complete();

            // The travel direction is the one the surface changes fastest
            // along: across the crests. Along a crest the water barely varies.
            int best = 0; float bestSlope = -1f;
            for (int d = 0; d < dirs; d++)
            {
                double acc = 0;
                for (int i = 1; i < tSteps; i++)
                {
                    float dh = tr[d * tSteps + i].height - tr[d * tSteps + i - 1].height;
                    acc += (double)dh * dh;
                }
                float sl = Mathf.Sqrt((float)(acc / (tSteps - 1)));
                if (sl > bestSlope) { bestSlope = sl; best = d; }
            }
            float bestDeg = best / (float)dirs * 180f;

            tq.Dispose(); tr.Dispose();

            // Now measure properly along that direction: several parallel
            // lines pooled, so there are enough waves for a median to mean
            // something (one 3 km line across 500 m rollers gave seven).
            float bdx = Mathf.Cos(bestDeg * Mathf.Deg2Rad);
            float bdz = Mathf.Sin(bestDeg * Mathf.Deg2Rad);
            float pdx = -bdz, pdz = bdx;
            int lSteps = Mathf.RoundToInt(TransectLen / TransectStep);
            var lq = new NativeArray<float3>(lSteps * TransectLines, Allocator.TempJob);
            var lr = new NativeArray<OceanSample>(lSteps * TransectLines, Allocator.TempJob);
            for (int L = 0; L < TransectLines; L++)
            {
                float off = (L - (TransectLines - 1) * 0.5f) * TransectSpacing;
                for (int i = 0; i < lSteps; i++)
                {
                    float t = (i - lSteps * 0.5f) * TransectStep;
                    lq[L * lSteps + i] = new float3(
                        spot.x + bdx * t + pdx * off, 0f, spot.z + bdz * t + pdz * off);
                }
            }
            OceanSampler.SampleBatch(lq, lr, default).Complete();

            // Hs from the pooled transects, not from the patch. A 1600 m patch
            // holds barely three wavelengths of a 500 m sea, so its RMS has a
            // standard error around 25% -- two runs of identical code returned
            // Hs 47.78 and 37.20 and neither was wrong. Five 4 km lines are
            // 20 km of profile, about forty wavelengths, and settle it.
            double lsum = 0, lsumSq = 0;
            for (int i = 0; i < lq.Length; i++)
            {
                float h = lr[i].height;
                lsum += h; lsumSq += (double)h * h;
            }
            float lmean = (float)(lsum / lq.Length);
            float hsLine = 4f * Mathf.Sqrt(Mathf.Max(0f,
                (float)(lsumSq / lq.Length) - lmean * lmean));

            var lens = new System.Collections.Generic.List<float>();
            var hts = new System.Collections.Generic.List<float>();
            var sLens = new System.Collections.Generic.List<float>();
            var sHts = new System.Collections.Generic.List<float>();
            var sFaces = new System.Collections.Generic.List<float>();
            var prof = new float[lSteps];
            var smoo = new float[lSteps];
            for (int L = 0; L < TransectLines; L++)
            {
                for (int i = 0; i < lSteps; i++) prof[i] = lr[L * lSteps + i].height;
                // Boxcar low-pass: the mountain without the chop on it.
                for (int i = 0; i < lSteps; i++)
                {
                    float acc = 0f; int cnt = 0;
                    for (int k = -SmoothW; k <= SmoothW; k++)
                    {
                        int j = i + k;
                        if (j < 0 || j >= lSteps) continue;
                        acc += prof[j]; cnt++;
                    }
                    smoo[i] = acc / cnt;
                }
                Upcross(prof, lSteps, lens, hts);
                Upcross(smoo, lSteps, sLens, sHts);
                // Face angle of the smoothed profile, central difference over
                // 20 m -- the slope the hull actually climbs.
                for (int i = 4; i < lSteps - 4; i++)
                    sFaces.Add(Mathf.Atan(Mathf.Abs(smoo[i + 4] - smoo[i - 4])
                        / (8f * TransectStep)) * Mathf.Rad2Deg);
            }
            lq.Dispose(); lr.Dispose();

            var lenA = lens.ToArray(); System.Array.Sort(lenA);
            var htA = hts.ToArray(); System.Array.Sort(htA);
            float medLen = Pct(lenA, 0.5f), medHt = Pct(htA, 0.5f);
            float worstHt = htA.Length > 0 ? htA[htA.Length - 1] : 0f;

            var sLenA = sLens.ToArray(); System.Array.Sort(sLenA);
            var sHtA = sHts.ToArray(); System.Array.Sort(sHtA);
            var sFaceA = sFaces.ToArray(); System.Array.Sort(sFaceA);
            float sMedLen = Pct(sLenA, 0.5f), sMedHt = Pct(sHtA, 0.5f);
            float sWorstHt = sHtA.Length > 0 ? sHtA[sHtA.Length - 1] : 0f;
            float faceLen = sMedLen * 0.5f;
            float geoAngle = sMedLen > 1f
                ? Mathf.Atan(Mathf.PI * sMedHt / sMedLen) * Mathf.Rad2Deg : 0f;

            float local = sea != null ? sea.SeaSeverityAt(new Vector2(spot.x, spot.z)) : 0f;
            string name = motor != null ? motor.SeaStateName : "?";

            sb.AppendLine("severity " + Severities[s].ToString("F2") + "  \"" + name + "\"");
            if (local < Severities[s] * 0.9f)
                sb.AppendLine("   WARNING: local severity " + local.ToString("F2")
                    + " below the forced " + Severities[s].ToString("F2") + " -- patch is damped, row is void");
            sb.AppendLine(string.Format("  Hs (4 x RMS, 20 km transects) {0,6:F2} m   <- the trustworthy one", hsLine));
            sb.AppendLine(string.Format("  Hs (4 x RMS, 1600 m patch)  {0,8:F2} m   (+-25%, too few wavelengths)", hs));
            sb.AppendLine(string.Format("  patch max-min               {0,8:F2} m", hi - lo));
            sb.AppendLine(string.Format("  face angle   median {0,5:F1}   p90 {1,5:F1}   p99 {2,5:F1}   max {3,5:F1}  deg",
                Pct(faces, 0.5f), Pct(faces, 0.9f), Pct(faces, 0.99f), faces[faces.Length - 1]));
            sb.AppendLine(string.Format("  travel direction            {0,8:F0} deg (steepest of {1} transects)",
                bestDeg, dirs));
            sb.AppendLine(string.Format("  wavelength   median {0,6:F0} m   p10 {1,5:F0}   p90 {2,5:F0}   ({3} waves, chop included)",
                medLen, Pct(lenA, 0.1f), Pct(lenA, 0.9f), lenA.Length));
            sb.AppendLine(string.Format("  per-wave height  median {0,6:F2} m   worst {1,6:F2} m", medHt, worstHt));
            sb.AppendLine("  -- the MOUNTAIN alone, chop below 121 m filtered out --");
            sb.AppendLine(string.Format("  swell wavelength median {0,6:F0} m   p10 {1,5:F0}   p90 {2,5:F0}   ({3} waves)",
                sMedLen, Pct(sLenA, 0.1f), Pct(sLenA, 0.9f), sLenA.Length));
            sb.AppendLine(string.Format("  swell height     median {0,6:F2} m   worst {1,6:F2} m", sMedHt, sWorstHt));
            sb.AppendLine(string.Format("  SWELL FACE ANGLE median {0,5:F1}   p90 {1,5:F1}   max {2,5:F1}  deg",
                Pct(sFaceA, 0.5f), Pct(sFaceA, 0.9f), sFaceA.Length > 0 ? sFaceA[sFaceA.Length - 1] : 0f));
            sb.AppendLine(string.Format("  FACE LENGTH                 {0,8:F0} m  = {1,5:F1} boat lengths",
                faceLen, faceLen / BoatLength));
            sb.AppendLine(string.Format("  face angle from H,lambda    {0,8:F1} deg (cross-check)",
                geoAngle));
            sb.AppendLine(string.Format("  SEABED CLEARANCE (min)      {0,8:F2} m  {1}",
                clearance, clearance < 0f ? "<-- TROUGH IS UNDER THE SEAFLOOR" : ""));
            sb.AppendLine();
        }

        if (sea != null) sea.ReleaseForce();
        sb.AppendLine("Face angle is the acceptance criterion, not height: 60 m of wave at");
        sb.AppendLine("0.6 degrees is the sheet this project already shipped once. Target for");
        sb.AppendLine("the storm sea is median 15-20 deg with the biggest waves breaking at");
        sb.AppendLine("25-30, on a face of 10-18 boat lengths.");

        System.IO.File.WriteAllText("/tmp/seasick-wavesize.txt", sb.ToString());
        Debug.Log("WaveSizeProbe:\n" + sb);
        Destroy(gameObject);
    }
}
