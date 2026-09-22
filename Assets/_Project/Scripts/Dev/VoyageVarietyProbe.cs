using System.Collections;
using System.Reflection;
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using SeaSick.Ocean;
using SeaSick.Ship;

/// **If I sail the same route twice, do I meet the same sea?**
///
/// The complaint is that consecutive voyages feel identical. That is a claim
/// about the WEATHER FIELD, not about a ship, so this measures the field and
/// nothing else.
///
/// PURE FIELD MATH AT CHOSEN OCEAN TIMES -- it does not sail. Every number
/// here comes from evaluating the shipped rule at a list of (position, time)
/// pairs, so the result is independent of frame rate and the run is valid on
/// a remote editor throttled to 10 fps. The frame rate IS printed, so a
/// reader can see it was noticed and dismissed rather than forgotten.
///
/// THE SAMPLE IS A DIAGONAL, and that is the whole point. Voyage k, waypoint
/// i of samples:
///
///     frac = i / (samples - 1)
///     t    = t0 + k * voyageSeconds + frac * voyageSeconds
///     pos  = start + direction * (frac * routeMetres)
///
/// so a voyage walks west while the clock runs, exactly as a real crossing
/// does. Sampling space and time independently would answer a different and
/// much easier question -- "does the sea vary at all" -- and every field
/// varies at SOME scale. What matters is whether it varies over the scales a
/// crossing actually covers, which is one diagonal through the field.
///
/// FOUR NUMBERS, because the complaint has two quite different causes and
/// they want opposite fixes:
///
///   A  per-voyage mean, and their spread. Do different crossings differ in
///      overall roughness AT ALL. This is the headline.
///   B  per consecutive pair, Pearson correlation of the two profiles and RMS
///      difference over the overall mean. Correlation 1.0 means the two
///      crossings were the same crossing.
///   C  B summarised across all pairs.
///   D  within-voyage range, averaged. How much the sea moves DURING one
///      crossing. A sea that never changes en route is a SECOND problem and
///      is deliberately not mixed into the first.
///
/// TWO SERIES. Hs is the sea's target significant wave height, read through
/// SeaStateController's own rule so the probe measures shipped code rather
/// than a hand copy of it. The roughness patch field is read separately, so
/// slow storm cells and fast roughness patches can be judged apart instead of
/// being summed into one number that cannot be acted on.
///
/// Everything is reached by REFLECTION on purpose: SeaStateController,
/// WeatherField and RegionField are under concurrent edit, and a probe that
/// names their members by compile-time reference breaks the build every time
/// one of them moves. It also lets the probe read a private member without
/// asking for it to be made public.
///
/// Play mode, Sea.unity. `RunProbe.VoyageVariety()` and
/// `RunProbe.VoyageVarietySweep()`.
/// Writes /tmp/seasick-voyagevariety.txt.
public class VoyageVarietyProbe : MonoBehaviour
{
    const string OutPath = "/tmp/seasick-voyagevariety.txt";

    const string Sentinel =
        "DID NOT FINISH -- VoyageVarietyProbe started and never reached its report.\n" +
        "Anything you remember reading in this file was a PREVIOUS run and has been\n" +
        "destroyed on purpose, so that a probe which dies silently cannot leave the\n" +
        "last run's numbers looking like this one's.\n";

    // ---- parameters ------------------------------------------------------

    public float routeMetres = 3000f;
    public int samples = 60;
    public float voyageSeconds = 300f;
    public int voyages = 8;

    /// Compass heading, degrees. 0 = +Z, 90 = +X, 180 = -Z, 270 = -X.
    /// 270 is west, which is the direction the designer actually sails.
    public float headingDeg = 270f;

    /// Sweep mode repeats the whole measurement at these crossing lengths.
    /// The answer plainly depends on how long a crossing is, so one number
    /// for one duration is not an answer.
    public bool sweep;
    public float[] sweepSeconds = new float[] { 180f, 300f, 600f };

    // ---- launchers -------------------------------------------------------

    public static void Execute() { Launch(false); }
    public static void Sweep() { Launch(true); }

    static void Launch(bool sweepMode)
    {
        if (!Application.isPlaying)
        {
            Debug.LogError("VoyageVarietyProbe: not in play mode");
            return;
        }
        var old = FindAnyObjectByType<VoyageVarietyProbe>();
        if (old != null) DestroyImmediate(old.gameObject);
        var go = new GameObject("VoyageVarietyProbe");
        var pr = go.AddComponent<VoyageVarietyProbe>();
        pr.sweep = sweepMode;
    }

    // ---- what the field is reached through -------------------------------

    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public
                           | BindingFlags.NonPublic;

    object seaObj;
    MethodInfo hsMethod;
    bool hsTakesDouble = true;

    object weatherObj;
    MethodInfo patchMethod;          // Patch01(float2), the shipped lookup

    // The supplementary time-correct patch read. Patch01 takes no time
    // argument -- it folds in OceanTime.Now through PatchOffset -- so along a
    // route sampled at chosen times it is a function of POSITION ONLY, and
    // every voyage visits the same positions. Left at that, the patch series
    // would come back bit-identical across voyages and the report would say
    // "correlation 1.000" about an artefact of the probe rather than about
    // the sea. These three members let the same field be read at an explicit
    // time; if any is missing the supplementary series is simply omitted and
    // the shipped-lookup series still stands.
    MethodInfo meanderMethod;
    MethodInfo sampleMethod;
    PropertyInfo invTileProp;
    FieldInfo driftSpeedField, driftHeadingField;
    bool patchTimeOk;

    IEnumerator Start()
    {
        // FIRST, before anything can throw.
        SafeWrite(Sentinel);

        // Frame rate, measured and then set aside. Nothing below reads the
        // clock or waits on a frame, so this is context, not an input.
        float acc = 0f;
        int frames = 0;
        for (int f = 0; f < 30; f++)
        {
            yield return null;
            acc += Time.unscaledDeltaTime;
            frames++;
        }
        float fps = frames / Mathf.Max(acc, 1e-6f);

        seaObj = SeaStateController.Instance;
        if (seaObj == null)
        {
            Fail("There is no SeaStateController.Instance in this scene, so there is\n"
               + "no sea to measure. Run this in play mode in Sea.unity.");
            yield break;
        }

        hsMethod = FindHsMethod(seaObj.GetType());
        if (hsMethod == null)
        {
            Fail("COULD NOT FIND the (position, time) -> metres of Hs method on\n"
               + seaObj.GetType().FullName + ".\n\n"
               + "The spec expects something shaped like TargetHsAt(Vector2, double).\n"
               + "Nothing on the type matches, and this probe will NOT substitute a\n"
               + "different reader -- SeaHsAt, CurrentHs and the rest answer other\n"
               + "questions and would have been reported under this one's name.\n\n"
               + "Candidates rejected, for whoever renames it next:\n"
               + DescribeCandidates(seaObj.GetType()));
            yield break;
        }

        FindPatch();

        double t0 = OceanTime.Now;

        Vector2 start = Vector2.zero;
        string startWhy = "world origin (no player ship found)";
        var motor = FindAnyObjectByType<ShipMotor>();
        if (motor != null)
        {
            Vector3 p = motor.transform.position;
            start = new Vector2(p.x, p.z);
            startWhy = "player ship (" + motor.gameObject.name + ")";
        }

        // Compass convention stated rather than assumed: 0 = +Z north,
        // 90 = +X east, so 270 comes out -X, which is west and is where the
        // deep water is in this world.
        float rad = headingDeg * Mathf.Deg2Rad;
        Vector2 dir = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));

        var sb = new StringBuilder();
        sb.AppendLine("VoyageVarietyProbe -- if I sail the same route twice, do I meet");
        sb.AppendLine("the same sea?");
        sb.AppendLine();
        sb.AppendLine("PURE FIELD MATH at chosen ocean times. It does not sail, it does not");
        sb.AppendLine("wait on physics, and no number below moves with the frame rate.");
        sb.AppendLine();
        sb.AppendLine("PARAMETERS");
        sb.AppendLine("  routeMetres     " + routeMetres.ToString("F0"));
        sb.AppendLine("  samples         " + samples);
        sb.AppendLine("  voyages         " + voyages);
        sb.AppendLine("  voyageSeconds   " + (sweep ? Join(sweepSeconds) + "   (sweep)"
                                                    : voyageSeconds.ToString("F0")));
        sb.AppendLine("  headingDeg      " + headingDeg.ToString("F0")
                    + "   direction (" + dir.x.ToString("F3") + ", " + dir.y.ToString("F3")
                    + ") in world XZ   [0 = +Z, 90 = +X]");
        sb.AppendLine("  start           (" + start.x.ToString("F1") + ", "
                    + start.y.ToString("F1") + ")   from " + startWhy);
        sb.AppendLine("  end of route    (" + (start.x + dir.x * routeMetres).ToString("F1")
                    + ", " + (start.y + dir.y * routeMetres).ToString("F1") + ")");
        sb.AppendLine("  OceanTime.Now   " + t0.ToString("F2") + " s at probe start (= t0)");
        sb.AppendLine("  frame rate      " + fps.ToString("F1")
                    + " fps over " + frames + " frames");
        sb.AppendLine("                  -- NOT AN INPUT. Recorded so a slow remote run");
        sb.AppendLine("                  cannot be blamed for, or credited with, anything");
        sb.AppendLine("                  in this report.");
        sb.AppendLine();
        sb.AppendLine("HOW THE FIELD IS READ");
        sb.AppendLine("  Hs      " + seaObj.GetType().Name + "." + hsMethod.Name
                    + "(" + ParamList(hsMethod) + ")  by reflection");
        sb.AppendLine("  patch   " + (patchMethod == null
                      ? "NOT AVAILABLE -- no WeatherField.Instance, or no Patch01(float2)."
                      : "WeatherField." + patchMethod.Name + "(float2)  by reflection"));
        if (patchMethod != null)
        {
            sb.AppendLine("          Patch01 TAKES NO TIME ARGUMENT: it folds OceanTime.Now in");
            sb.AppendLine("          through PatchOffset. Every voyage below visits the same");
            sb.AppendLine("          positions, so the shipped lookup is very nearly a function");
            sb.AppendLine("          of position alone here and its across-voyage numbers are");
            sb.AppendLine("          degenerate BY CONSTRUCTION, not by the sea's fault. Read");
            sb.AppendLine("          the supplementary series for the question you asked.");
            sb.AppendLine("  patch(t) " + (patchTimeOk
                          ? "the same tile read at the voyage's own time, via the private"
                          : "NOT AVAILABLE -- WeatherField's private drift members moved."));
            if (patchTimeOk)
                sb.AppendLine("           MeanderedOffset/Sample pair. SUPPLEMENTARY: it is not in");
            if (patchTimeOk)
                sb.AppendLine("           the spec, and it is the only patch row that can vary.");
        }
        sb.AppendLine();

        if (sweep)
        {
            var results = new Result[sweepSeconds.Length][];
            for (int s = 0; s < sweepSeconds.Length; s++)
                results[s] = Measure(start, dir, t0, sweepSeconds[s]);

            sb.AppendLine(Sidebyside(results, sweepSeconds));
            for (int s = 0; s < sweepSeconds.Length; s++)
            {
                sb.AppendLine();
                sb.AppendLine("============================================================");
                sb.AppendLine("  voyageSeconds = " + sweepSeconds[s].ToString("F0")
                            + "   (a crossing of " + routeMetres.ToString("F0") + " m in "
                            + (routeMetres / Mathf.Max(sweepSeconds[s], 1e-6f)).ToString("F2")
                            + " m/s)");
                sb.AppendLine("============================================================");
                foreach (var r in results[s]) sb.Append(Render(r));
            }
        }
        else
        {
            var r = Measure(start, dir, t0, voyageSeconds);
            sb.AppendLine("A crossing of " + routeMetres.ToString("F0") + " m in "
                        + voyageSeconds.ToString("F0") + " s is "
                        + (routeMetres / Mathf.Max(voyageSeconds, 1e-6f)).ToString("F2")
                        + " m/s.");
            sb.AppendLine();
            foreach (var one in r) sb.Append(Render(one));
        }

        sb.AppendLine();
        sb.AppendLine("OceanTime.Now at finish " + OceanTime.Now.ToString("F2")
                    + " s (it ran " + (OceanTime.Now - t0).ToString("F2")
                    + " s of wall clock during the run; the clock was NOT scrubbed,");
        sb.AppendLine("paused or rescaled by this probe, and every sample above was taken at");
        sb.AppendLine("an explicit time argument rather than at the live clock).");

        SafeWrite(sb.ToString());
        Debug.Log("VoyageVarietyProbe: wrote " + OutPath + "\n" + sb);
    }

    // ---- the measurement -------------------------------------------------

    /// One named series: a voyages x samples grid and everything derived
    /// from it. Kept as a struct of plain arrays so the report code never
    /// re-derives a statistic a second time and drifts.
    struct Result
    {
        public string name;
        public string units;
        public string caveat;
        public float[][] grid;        // [voyage][sample]
        public float[] voyageMean;
        public float meanOfMeans, sdOfMeans, minMean, maxMean;
        public float[] pairR, pairRms;   // length voyages - 1
        public float meanR, minR, maxR;
        public float meanRms, minRms, maxRms;
        public float[] voyageRange;
        public float meanRange, minRange, maxRange;
    }

    Result[] Measure(Vector2 start, Vector2 dir, double t0, float vSeconds)
    {
        int n = Mathf.Max(samples, 2);
        int v = Mathf.Max(voyages, 2);

        var hs = new float[v][];
        var patch = patchMethod != null ? new float[v][] : null;
        var patchT = patchTimeOk ? new float[v][] : null;

        for (int k = 0; k < v; k++)
        {
            hs[k] = new float[n];
            if (patch != null) patch[k] = new float[n];
            if (patchT != null) patchT[k] = new float[n];

            for (int i = 0; i < n; i++)
            {
                float frac = i / (float)(n - 1);
                double t = t0 + k * (double)vSeconds + frac * (double)vSeconds;
                Vector2 pos = start + dir * (frac * routeMetres);

                hs[k][i] = ReadHs(pos, t);
                if (patch != null) patch[k][i] = ReadPatch(pos);
                if (patchT != null) patchT[k][i] = ReadPatchAt(pos, t);
            }
        }

        int count = 1 + (patch != null ? 1 : 0) + (patchT != null ? 1 : 0);
        var outp = new Result[count];
        int w = 0;
        outp[w++] = Summarise("Hs -- the sea's TARGET significant wave height", "m",
                              "", hs);
        if (patch != null)
            outp[w++] = Summarise("patch -- WeatherField.Patch01, the SHIPPED lookup", "0..1",
                "Patch01 carries no time argument, so along a fixed route it is a\n"
              + "  function of position only and all voyages read the same profile.\n"
              + "  A correlation of 1.000 on this row says nothing about the sea.",
                patch);
        if (patchT != null)
            outp[w++] = Summarise("patch(t) -- the same tile at the voyage's own time", "0..1",
                "SUPPLEMENTARY, not in the spec: WeatherField's private\n"
              + "  MeanderedOffset/Sample pair read at an explicit t. This is the patch\n"
              + "  row that can actually answer the question.",
                patchT);
        return outp;
    }

    Result Summarise(string name, string units, string caveat, float[][] grid)
    {
        var r = new Result();
        r.name = name;
        r.units = units;
        r.caveat = caveat;
        r.grid = grid;

        int v = grid.Length;
        int n = grid[0].Length;

        // A -- per voyage means, and their spread.
        r.voyageMean = new float[v];
        for (int k = 0; k < v; k++) r.voyageMean[k] = Mean(grid[k]);
        r.meanOfMeans = Mean(r.voyageMean);
        r.sdOfMeans = Sd(r.voyageMean, r.meanOfMeans);
        r.minMean = Min(r.voyageMean);
        r.maxMean = Max(r.voyageMean);

        // The normaliser for B is the OVERALL mean, so an RMS difference is
        // read as a fraction of the sea's own size rather than in metres --
        // which makes the calm rows and the storm rows comparable.
        float overall = r.meanOfMeans;
        float norm = Mathf.Abs(overall) > 1e-6f ? overall : 1f;

        // B -- consecutive pairs.
        r.pairR = new float[v - 1];
        r.pairRms = new float[v - 1];
        for (int k = 0; k < v - 1; k++)
        {
            r.pairR[k] = Pearson(grid[k], grid[k + 1]);
            double sq = 0.0;
            for (int i = 0; i < n; i++)
            {
                double d = grid[k][i] - grid[k + 1][i];
                sq += d * d;
            }
            r.pairRms[k] = (float)(System.Math.Sqrt(sq / n) / norm);
        }

        // C -- B across all pairs.
        r.meanR = Mean(r.pairR); r.minR = Min(r.pairR); r.maxR = Max(r.pairR);
        r.meanRms = Mean(r.pairRms); r.minRms = Min(r.pairRms); r.maxRms = Max(r.pairRms);

        // D -- how much the sea moves DURING one crossing. Deliberately a
        // separate number: a sea that is different every voyage but flat
        // within one is a different complaint from a sea that changes en
        // route but always the same way.
        r.voyageRange = new float[v];
        for (int k = 0; k < v; k++) r.voyageRange[k] = Max(grid[k]) - Min(grid[k]);
        r.meanRange = Mean(r.voyageRange);
        r.minRange = Min(r.voyageRange);
        r.maxRange = Max(r.voyageRange);
        return r;
    }

    // ---- reading the shipped field --------------------------------------

    float ReadHs(Vector2 p, double t)
    {
        object time = hsTakesDouble ? (object)t : (object)(float)t;
        return (float)hsMethod.Invoke(seaObj, new object[] { p, time });
    }

    float ReadPatch(Vector2 p)
    {
        return (float)patchMethod.Invoke(weatherObj,
            new object[] { new float2(p.x, p.y) });
    }

    float ReadPatchAt(Vector2 p, double t)
    {
        float inv = (float)invTileProp.GetValue(weatherObj);
        float speed = (float)driftSpeedField.GetValue(weatherObj);
        float head = (float)driftHeadingField.GetValue(weatherObj);
        var off = (float2)meanderMethod.Invoke(weatherObj,
            new object[] { t, speed, head, inv });
        var uv = new float2(p.x, p.y) * inv + off;
        return (float)sampleMethod.Invoke(weatherObj, new object[] { uv });
    }

    static MethodInfo FindHsMethod(System.Type t)
    {
        var exact = t.GetMethod("TargetHsAt", Any, null,
            new System.Type[] { typeof(Vector2), typeof(double) }, null);
        if (exact != null) return exact;

        // The name is allowed to move; the SHAPE is not. Anything taking a
        // position and a time and returning metres, with Hs in its name.
        foreach (var m in t.GetMethods(Any))
        {
            if (m.ReturnType != typeof(float)) continue;
            if (m.Name.IndexOf("Hs", System.StringComparison.Ordinal) < 0) continue;
            var ps = m.GetParameters();
            if (ps.Length != 2) continue;
            if (ps[0].ParameterType != typeof(Vector2)) continue;
            if (ps[1].ParameterType == typeof(double)) return m;
            if (ps[1].ParameterType == typeof(float)) return m;
        }
        return null;
    }

    static string DescribeCandidates(System.Type t)
    {
        var sb = new StringBuilder();
        foreach (var m in t.GetMethods(Any))
        {
            if (m.Name.IndexOf("Hs", System.StringComparison.Ordinal) < 0) continue;
            sb.AppendLine("    " + m.ReturnType.Name + " " + m.Name
                        + "(" + ParamList(m) + ")");
        }
        if (sb.Length == 0) sb.AppendLine("    (none -- nothing on the type has Hs in its name)");
        return sb.ToString();
    }

    static string ParamList(MethodInfo m)
    {
        var sb = new StringBuilder();
        var ps = m.GetParameters();
        for (int i = 0; i < ps.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(ps[i].ParameterType.Name);
        }
        return sb.ToString();
    }

    void FindPatch()
    {
        weatherObj = WeatherField.Instance;
        if (weatherObj == null) return;
        var t = weatherObj.GetType();

        patchMethod = t.GetMethod("Patch01", Any, null,
            new System.Type[] { typeof(float2) }, null);
        if (patchMethod == null) return;

        sampleMethod = t.GetMethod("Sample", Any, null,
            new System.Type[] { typeof(float2) }, null);
        invTileProp = t.GetProperty("InvTileMetres", Any);
        driftSpeedField = t.GetField("driftSpeed", Any);
        driftHeadingField = t.GetField("driftHeadingDeg", Any);
        meanderMethod = t.GetMethod("MeanderedOffset", Any, null,
            new System.Type[] { typeof(double), typeof(float), typeof(float), typeof(float) },
            null);

        patchTimeOk = sampleMethod != null && invTileProp != null
                   && driftSpeedField != null && driftHeadingField != null
                   && meanderMethod != null
                   && meanderMethod.ReturnType == typeof(float2)
                   && invTileProp.PropertyType == typeof(float)
                   && driftSpeedField.FieldType == typeof(float)
                   && driftHeadingField.FieldType == typeof(float);
    }

    // ---- the report ------------------------------------------------------

    string Render(Result r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("------------------------------------------------------------");
        sb.AppendLine(r.name + "   [" + r.units + "]");
        if (r.caveat.Length > 0) sb.AppendLine("  " + r.caveat);
        sb.AppendLine("------------------------------------------------------------");

        sb.AppendLine("A. PER-VOYAGE MEAN -- do different voyages differ in overall");
        sb.AppendLine("   roughness at all?");
        for (int k = 0; k < r.voyageMean.Length; k++)
            sb.AppendLine("     voyage " + k.ToString().PadLeft(2) + "   mean "
                        + r.voyageMean[k].ToString("F4").PadLeft(10));
        sb.AppendLine("     ----");
        sb.AppendLine("     mean of means    " + r.meanOfMeans.ToString("F4"));
        sb.AppendLine("     sd across voyages " + r.sdOfMeans.ToString("F4")
                    + "   (" + Pct(r.sdOfMeans, r.meanOfMeans) + " of the mean)");
        sb.AppendLine("     min / max        " + r.minMean.ToString("F4") + " / "
                    + r.maxMean.ToString("F4")
                    + "   spread " + (r.maxMean - r.minMean).ToString("F4")
                    + "   (" + Pct(r.maxMean - r.minMean, r.meanOfMeans) + ")");
        sb.AppendLine();

        sb.AppendLine("B. CONSECUTIVE PAIRS -- correlation 1.000 means the two crossings");
        sb.AppendLine("   were the same crossing. RMS difference is normalised by the");
        sb.AppendLine("   overall mean.");
        sb.AppendLine("     pair        Pearson r    rms diff / mean");
        for (int k = 0; k < r.pairR.Length; k++)
            sb.AppendLine("     " + (k + " -> " + (k + 1)).PadRight(10)
                        + r.pairR[k].ToString("F4").PadLeft(10)
                        + r.pairRms[k].ToString("F4").PadLeft(19));
        sb.AppendLine();

        sb.AppendLine("C. ACROSS ALL PAIRS");
        sb.AppendLine("     Pearson r        mean " + r.meanR.ToString("F4")
                    + "   range " + r.minR.ToString("F4") + " .. " + r.maxR.ToString("F4"));
        sb.AppendLine("     rms diff / mean  mean " + r.meanRms.ToString("F4")
                    + "   range " + r.minRms.ToString("F4") + " .. " + r.maxRms.ToString("F4"));
        sb.AppendLine();

        sb.AppendLine("D. WITHIN-VOYAGE RANGE -- how much the sea changes DURING one");
        sb.AppendLine("   crossing (max minus min along the route), averaged over voyages.");
        sb.AppendLine("   Separated from A on purpose: a sea that never changes en route");
        sb.AppendLine("   is its own problem and wants a different fix.");
        sb.AppendLine("     mean range       " + r.meanRange.ToString("F4")
                    + "   (" + Pct(r.meanRange, r.meanOfMeans) + " of the mean)");
        sb.AppendLine("     min / max range  " + r.minRange.ToString("F4") + " / "
                    + r.maxRange.ToString("F4"));
        sb.AppendLine();
        return sb.ToString();
    }

    /// The three crossing lengths on one table, because the whole reason for
    /// the sweep is that the numbers should be READ against each other.
    string Sidebyside(Result[][] results, float[] seconds)
    {
        var sb = new StringBuilder();
        sb.AppendLine("SWEEP -- the same measurement at three crossing lengths");
        sb.AppendLine();

        int rows = results[0].Length;
        for (int s = 1; s < results.Length; s++) rows = Mathf.Min(rows, results[s].Length);

        for (int row = 0; row < rows; row++)
        {
            sb.AppendLine("  " + results[0][row].name);
            sb.Append("    voyageSeconds      ");
            for (int s = 0; s < seconds.Length; s++)
                sb.Append(seconds[s].ToString("F0").PadLeft(12));
            sb.AppendLine();
            sb.Append("    A sd across voyages");
            for (int s = 0; s < seconds.Length; s++)
                sb.Append(results[s][row].sdOfMeans.ToString("F4").PadLeft(12));
            sb.AppendLine();
            sb.Append("    A spread of means  ");
            for (int s = 0; s < seconds.Length; s++)
                sb.Append((results[s][row].maxMean - results[s][row].minMean)
                          .ToString("F4").PadLeft(12));
            sb.AppendLine();
            sb.Append("    C mean Pearson r   ");
            for (int s = 0; s < seconds.Length; s++)
                sb.Append(results[s][row].meanR.ToString("F4").PadLeft(12));
            sb.AppendLine();
            sb.Append("    C mean rms / mean  ");
            for (int s = 0; s < seconds.Length; s++)
                sb.Append(results[s][row].meanRms.ToString("F4").PadLeft(12));
            sb.AppendLine();
            sb.Append("    D mean within-range");
            for (int s = 0; s < seconds.Length; s++)
                sb.Append(results[s][row].meanRange.ToString("F4").PadLeft(12));
            sb.AppendLine();
            sb.Append("    overall mean       ");
            for (int s = 0; s < seconds.Length; s++)
                sb.Append(results[s][row].meanOfMeans.ToString("F4").PadLeft(12));
            sb.AppendLine();
            sb.AppendLine();
        }
        return sb.ToString();
    }

    // ---- arithmetic ------------------------------------------------------

    static float Mean(float[] a)
    {
        if (a == null || a.Length == 0) return 0f;
        double s = 0.0;
        for (int i = 0; i < a.Length; i++) s += a[i];
        return (float)(s / a.Length);
    }

    static float Sd(float[] a, float mean)
    {
        if (a == null || a.Length < 2) return 0f;
        double s = 0.0;
        for (int i = 0; i < a.Length; i++)
        {
            double d = a[i] - mean;
            s += d * d;
        }
        return (float)System.Math.Sqrt(s / (a.Length - 1));
    }

    static float Min(float[] a)
    {
        if (a == null || a.Length == 0) return 0f;
        float m = a[0];
        for (int i = 1; i < a.Length; i++) if (a[i] < m) m = a[i];
        return m;
    }

    static float Max(float[] a)
    {
        if (a == null || a.Length == 0) return 0f;
        float m = a[0];
        for (int i = 1; i < a.Length; i++) if (a[i] > m) m = a[i];
        return m;
    }

    /// Pearson between two equal-length profiles. A flat profile has no
    /// correlation to report and says so rather than returning a 0 or a 1
    /// that would be read as a finding.
    static float Pearson(float[] a, float[] b)
    {
        int n = Mathf.Min(a.Length, b.Length);
        if (n < 2) return float.NaN;
        float ma = Mean(a), mb = Mean(b);
        double sab = 0.0, saa = 0.0, sbb = 0.0;
        for (int i = 0; i < n; i++)
        {
            double da = a[i] - ma, db = b[i] - mb;
            sab += da * db; saa += da * da; sbb += db * db;
        }
        if (saa <= 1e-20 || sbb <= 1e-20) return float.NaN;
        return (float)(sab / System.Math.Sqrt(saa * sbb));
    }

    static string Pct(float part, float whole)
    {
        if (Mathf.Abs(whole) < 1e-6f) return "n/a";
        return (100f * part / whole).ToString("F1") + "%";
    }

    static string Join(float[] a)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < a.Length; i++)
        {
            if (i > 0) sb.Append(" / ");
            sb.Append(a[i].ToString("F0"));
        }
        return sb.ToString();
    }

    // ---- output ----------------------------------------------------------

    void Fail(string why)
    {
        string text = "VoyageVarietyProbe -- STOPPED, nothing measured.\n\n" + why + "\n";
        SafeWrite(text);
        Debug.LogError(text);
    }

    static void SafeWrite(string text)
    {
        try { System.IO.File.WriteAllText(OutPath, text); }
        catch (System.Exception e)
        {
            Debug.LogError("VoyageVarietyProbe: could not write " + OutPath + ": " + e.Message);
        }
    }
}
