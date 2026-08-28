using System.Collections;
using System.Text;
using UnityEngine;
using SeaSick.Ocean;

/// **Is the sea more than one number?** The ocean used to have exactly one
/// degree of freedom: wind speed, fetch, both swell heights, BOTH SWELL
/// DIRECTIONS, choppiness and dispersion depth were all a single lerp along
/// `severity` between four anchors. Two seas at the same Hs were therefore
/// byte-identical, and there was no way to have an old south-westerly swell
/// under a fresh north-easterly wind, because swell direction was welded to
/// wind direction by the blend.
///
/// This measures whether that is still true, by scrubbing `OceanTime` over
/// hours and asking the shipped rule -- `SeaStateController.BlendAt` for the
/// anchor sea and `ApplyAxes` for the two axes, never a copy of either -- what
/// the sea is at each moment. It reports:
///
/// * **The headline.** Samples are bucketed by Hs to within a couple of per
///   cent, and the probe reports the SPREAD OF WIND-AGAINST-SWELL ANGLE inside
///   a bucket. With one degree of freedom that spread is exactly zero by
///   construction: same Hs, same everything. It is the single number that says
///   whether the sea can be two different days at one size.
/// * **Independence.** The Pearson correlation between the wind's offset and
///   the swell's. Two axes that track each other are one axis wearing a hat.
/// * **Coverage.** How much of the time the wind runs WITH the swell, across
///   it, and AGAINST it -- the three seas the split exists to produce.
/// * **That it changed character and not size.** Total swell variance must not
///   move (the split is variance-preserving), and Hs must come from the
///   weather rather than from the axes.
/// * **Purity.** The same time sampled twice must give the same sea, or
///   `OceanTime.Scrub` has stopped making probes repeatable.
///
/// Play mode, Sea.unity. Writes /tmp/seasick-twoaxis.txt.
public class TwoAxisTrace : MonoBehaviour
{
    const int Samples = 720;        // 6 hours of ocean time at 30 s
    const double Step = 30.0;
    static bool running;

    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("TwoAxisTrace: not in play mode"); return; }
        if (running) { Debug.LogError("TwoAxisTrace: already running"); return; }
        running = true;
        new GameObject("TwoAxisTrace").AddComponent<TwoAxisTrace>();
    }

    IEnumerator Start()
    {
        var sb = new StringBuilder();
        System.IO.File.WriteAllText("/tmp/seasick-twoaxis.txt", "TwoAxisTrace: did not finish\n");
        var sea = SeaStateController.Instance;
        if (sea == null) { Finish(sb, "ABORT: no SeaStateController"); yield break; }

        var motor = FindAnyObjectByType<SeaSick.Ship.ShipMotor>();
        var helm = FindAnyObjectByType<SeaSick.Ship.HelmInput>();
        if (helm != null) helm.enabled = false;
        Vector3 sp = motor != null ? motor.transform.position : Vector3.zero;
        Vector2 p = new Vector2(sp.x, sp.z);
        yield return null;

        var scratch = ScriptableObject.CreateInstance<OceanSpectrumSettings>();
        var plain = ScriptableObject.CreateInstance<OceanSpectrumSettings>();

        double t0 = OceanTime.Now;
        var hs = new float[Samples];
        var rel = new float[Samples];        // |wind - swell|, degrees
        var relPlain = new float[Samples];   // the same, from the anchors alone
        var cross = new float[Samples];
        var windOff = new float[Samples];
        var swellOff = new float[Samples];
        var swellVar = new float[Samples];
        var varDrift = new float[Samples];   // what ApplyAxes did to the swell's total variance
        var gamma = new float[Samples];

        for (int i = 0; i < Samples; i++)
        {
            double t = t0 + i * Step;
            // The weather the shipped rule asks for at this place and time,
            // sets included -- the same chain Update runs.
            float target = sea.TargetHsAt(p, t) * sea.SetFactor(t);
            float sev = sea.SeverityForHs(target);
            hs[i] = sea.HsAt(sev);

            // The anchor sea: what this moment WAS before the axes existed.
            sea.BlendAt(sev, plain);
            relPlain[i] = Mathf.Abs(Mathf.DeltaAngle(plain.windDirectionDeg, plain.swellDirectionDeg));

            // And with the two axes turned on top of it.
            sea.BlendAt(sev, scratch);
            float baseWind = scratch.windDirectionDeg, baseSwell = scratch.swellDirectionDeg;
            sea.ApplyAxes(scratch, p, t);
            windOff[i] = Mathf.DeltaAngle(baseWind, scratch.windDirectionDeg);
            swellOff[i] = Mathf.DeltaAngle(baseSwell, scratch.swellDirectionDeg);
            rel[i] = Mathf.Abs(Mathf.DeltaAngle(scratch.windDirectionDeg, scratch.swellDirectionDeg));
            cross[i] = Mathf.DeltaAngle(scratch.swellDirectionDeg, scratch.swell2DirectionDeg);
            swellVar[i] = scratch.swellHeight * scratch.swellHeight
                        + scratch.swell2Height * scratch.swell2Height;
            // The variance-preserving claim is about what ApplyAxes does AT A
            // FIXED severity. Comparing swellVar across the run instead would
            // be comparing a 29 m sea with a 60 m one and calling the weather
            // a leak.
            float plainVar = plain.swellHeight * plain.swellHeight
                           + plain.swell2Height * plain.swell2Height;
            varDrift[i] = plainVar > 1e-6f ? Mathf.Abs(swellVar[i] - plainVar) / plainVar : 0f;
            gamma[i] = scratch.gamma;
            if ((i & 63) == 63) yield return null;
        }

        sb.AppendLine("TwoAxisTrace -- can the sea be two different days at one size?");
        sb.AppendLine($"at ({p.x:F0}, {p.y:F0}), {Samples} samples {Step:F0} s apart " +
                      $"= {Samples * Step / 3600.0:F1} hours of ocean time");
        sb.AppendLine();

        // ---- the headline ---------------------------------------------------
        // Bucket by Hs and ask how differently the sea can be running at the
        // same size. One degree of freedom gives exactly zero.
        sb.AppendLine("SAME Hs, DIFFERENT SEA (the headline):");
        sb.AppendLine("  Hs band        n   wind-against-swell, anchors only   with the two axes");
        var edges = new[] { 1f, 2f, 3f, 4.5f, 7f, 11f, 17f, 26f, 40f, 66f };
        for (int b = 0; b < edges.Length - 1; b++)
        {
            float loA = 999f, hiA = -999f, loB = 999f, hiB = -999f;
            int n = 0;
            for (int i = 0; i < Samples; i++)
            {
                if (hs[i] < edges[b] || hs[i] >= edges[b + 1]) continue;
                loA = Mathf.Min(loA, relPlain[i]); hiA = Mathf.Max(hiA, relPlain[i]);
                loB = Mathf.Min(loB, rel[i]); hiB = Mathf.Max(hiB, rel[i]);
                n++;
            }
            if (n < 3) continue;
            sb.AppendLine($"  {edges[b],5:F1}-{edges[b + 1],-5:F1} {n,5}        " +
                          $"{hiA - loA,6:F1} deg of spread            {hiB - loB,6:F1} deg");
        }
        sb.AppendLine("  (spread is max minus min inside the band; the anchors alone give a");
        sb.AppendLine("   deterministic angle per severity, so their column is the old sea.)");

        // ---- independence ---------------------------------------------------
        sb.AppendLine();
        sb.AppendLine($"INDEPENDENCE: correlation of the wind's turn with the swell's = " +
                      $"{Correlation(windOff, swellOff):F3}  (0 = independent, 1 = one axis in a hat)");
        sb.AppendLine($"  wind  offset {Min(windOff),7:F0} .. {Max(windOff),6:F0} deg");
        sb.AppendLine($"  swell offset {Min(swellOff),7:F0} .. {Max(swellOff),6:F0} deg");
        sb.AppendLine($"  wind turns {TurnRate(windOff):F2} deg/min, swell {TurnRate(swellOff):F2} deg/min " +
                      "(the swell must be the slow one -- it is made by weather that has gone)");

        // ---- coverage --------------------------------------------------------
        int with = 0, across = 0, against = 0;
        for (int i = 0; i < Samples; i++)
        {
            if (rel[i] < 60f) with++;
            else if (rel[i] < 120f) across++;
            else against++;
        }
        sb.AppendLine();
        sb.AppendLine("THE THREE SEAS, as a fraction of the time:");
        sb.AppendLine($"  wind WITH the swell    (< 60 deg)  {with * 100f / Samples,5:F1}%   long, ordered rollers");
        sb.AppendLine($"  wind ACROSS it     (60-120 deg)    {across * 100f / Samples,5:F1}%   confused, pyramidal");
        sb.AppendLine($"  wind AGAINST it       (>120 deg)   {against * 100f / Samples,5:F1}%   short, steep, dangerous");
        sb.AppendLine($"  swell trains cross {Min(cross):F0} .. {Max(cross):F0} deg " +
                      "(swinging about the ANGLE THE ASSET AUTHORED, never through zero:");
        sb.AppendLine("   two trains lying on each other is the parallel corduroy a single");
        sb.AppendLine("   narrow train gives at any height, and it reads high on a transect)");
        sb.AppendLine($"  gamma (the wind sea's age) {Min(gamma):F2} .. {Max(gamma):F2}");

        // ---- character, not size ---------------------------------------------
        sb.AppendLine();
        sb.AppendLine("CHARACTER, NOT SIZE:");
        sb.AppendLine($"  worst change ApplyAxes makes to total swell variance, at a FIXED");
        sb.AppendLine($"  severity: {Max(varDrift) * 100f:F4}%  (the axes may move what the sea is LIKE");
        sb.AppendLine($"  and never how big -- if this is not ~0, something has started moving");
        sb.AppendLine($"  energy in or out of the swell and NewtonIterations has no headroom)");
        sb.AppendLine($"  swell height split ran {Min(swellVar):F0} .. {Max(swellVar):F0} m^2 across the run,");
        sb.AppendLine($"  but that is the WEATHER: Hs itself ran {Min(hs):F2} .. {Max(hs):F2} m over the same samples");

        // ---- purity -----------------------------------------------------------
        sea.BlendAt(0.8f, scratch);
        sea.ApplyAxes(scratch, p, t0 + 900.0);
        float a1 = scratch.windDirectionDeg, b1 = scratch.swell2DirectionDeg, c1 = scratch.swellHeight;
        sea.BlendAt(0.8f, scratch);
        sea.ApplyAxes(scratch, p, t0 + 900.0);
        bool pure = Mathf.Approximately(a1, scratch.windDirectionDeg)
                 && Mathf.Approximately(b1, scratch.swell2DirectionDeg)
                 && Mathf.Approximately(c1, scratch.swellHeight);
        sb.AppendLine();
        sb.AppendLine($"PURITY: the same (place, time) twice gives the same sea -> {(pure ? "PASS" : "FAIL")}");
        sb.AppendLine("  (if this fails, OceanTime.Scrub has stopped making probes repeatable)");

        Destroy(scratch); Destroy(plain);
        Finish(sb, null);
    }

    static float Min(float[] a) { float m = float.MaxValue; foreach (var v in a) m = Mathf.Min(m, v); return m; }
    static float Max(float[] a) { float m = float.MinValue; foreach (var v in a) m = Mathf.Max(m, v); return m; }

    /// Mean absolute change per minute, which is what "fast" and "slow" mean
    /// here -- the range alone cannot tell a wind that swings from one that
    /// sits at an extreme.
    static float TurnRate(float[] a)
    {
        double sum = 0;
        for (int i = 1; i < a.Length; i++) sum += Mathf.Abs(a[i] - a[i - 1]);
        return (float)(sum / (a.Length - 1) / (Step / 60.0));
    }

    static float Correlation(float[] a, float[] b)
    {
        double ma = 0, mb = 0;
        for (int i = 0; i < a.Length; i++) { ma += a[i]; mb += b[i]; }
        ma /= a.Length; mb /= b.Length;
        double sab = 0, saa = 0, sbb = 0;
        for (int i = 0; i < a.Length; i++)
        {
            double da = a[i] - ma, db = b[i] - mb;
            sab += da * db; saa += da * da; sbb += db * db;
        }
        return saa > 1e-9 && sbb > 1e-9 ? (float)(sab / System.Math.Sqrt(saa * sbb)) : 0f;
    }

    void Finish(StringBuilder sb, string err)
    {
        running = false;
        if (err != null) sb.AppendLine(err);
        System.IO.File.WriteAllText("/tmp/seasick-twoaxis.txt", sb.ToString());
        Debug.Log("TwoAxisTrace:\n" + sb);
        Destroy(gameObject);
    }
}
