using System.Collections;
using System.Collections.Generic;
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using SeaSick.Crew;
using SeaSick.Ocean;
using SeaSick.Ship;
using SeaSick.World;

/// WHAT HEAVY WEATHER COSTS HER, as a function of sea state.
///
/// Not "does she survive". She always does: `PlayerHull.Alive` is hardcoded
/// true and roll is spring-clamped, so a survival gate can only ever come
/// back green and say nothing. The question worth measuring is the PRICE --
/// how much broach, overspeed, water aboard, hull, roughness, heel, way and
/// crew a given Hs takes off her, and how that price curves as the sea grows.
///
/// One leg per rung of an Hs ladder (2, 5, 9, 14, 22, 35, 50 m). Each leg
/// pins the sea, puts her back on the SAME patch of verified deep water, lets
/// the throttle ramp, and then samples every cost term at 10 Hz for 45 s while
/// she RUNS WITH THE SEAS.
///
/// Three things in that are load-bearing:
///
///   FOLLOWING SEA. `ShipMotor.UpdateBroach` only builds while she is running
///   DOWN a face with way on -- the dot product of her heading with the
///   downslope has to be positive. A probe that beats to windward the whole
///   time reports broach 0.00 at every sea state and concludes, wrongly, that
///   the mechanic does not fire. So each leg heads down-sea, recomputed as the
///   weather axes rotate.
///
///   THE PATCH IS SEARCHED AND THEN CONTINUOUSLY RE-ASSERTED. The first run of
///   this probe verified a 5x5 grid over a 1200 m half-patch -- 600 m between
///   samples -- called it deep, and then lost FIVE OF SEVEN RUNGS to a shoal
///   130 m from the start point. She grounded, `HullIntegrity.Aground` killed
///   her closing speed every frame, and the rows reported a stationary ship on
///   a 10 m shelf under headings that said Hs 50. Verifying once and assuming
///   is exactly the failure this project's notes warn about. So now: samples
///   no more than 150 m apart, EVERY one of them deeper than 120 m, 2 km of
///   clearance from every island's outer radius -- and then the depth under
///   her is re-checked on EVERY sampling tick, contaminated ticks are thrown
///   away instead of averaged in, and she is put back on the verified spot and
///   allowed to sail on.
///
///   EVERY TERM IS READ, NOT RE-DERIVED. Broach, overspeed, surf seconds,
///   bilge, integrity, roughness, sea resistance, breaking and labour are all
///   public properties on components already on the ship. A probe that
///   recomputes the rule it is measuring can only ever agree with itself.
///
/// Play mode, Sea.unity. `RunProbe.StormCost()`. About nine minutes.
/// Writes /tmp/seasick-stormcost.txt.
public class StormCostProbe : MonoBehaviour
{
    const string OutPath = "/tmp/seasick-stormcost.txt";

    /// The ladder. Calm working water at the bottom, the authored storm
    /// anchor (nominalHs 65) not quite reached at the top.
    static readonly float[] Ladder = { 2f, 5f, 9f, 14f, 22f, 35f, 50f };

    const float PatchSpacing = 150f;        // never more than this between depth samples

    /// Fallback ladder for "water good enough to measure a storm in", tried in
    /// order: half-patch, minimum depth, island clearance.
    ///
    /// The first version demanded a 2400 m square at 120 m everywhere and 2 km
    /// clear of every island, and NO SUCH WATER EXISTS within 26 km of origin
    /// -- 1464 candidates, the best missing by a hair at 15 km out, which is
    /// far past where the game is played. That bar was mine, not the game's:
    /// she drifts 500-700 m in a leg and is re-placed on contamination anyway,
    /// so the patch never needed to be 2.4 km across.
    ///
    /// Rung B and C trade depth for findability, and depth is not free: the
    /// cap stops binding at 1.82 x Hs, so 80 m of water means anything above
    /// about Hs 44 is measuring the shelf rather than the storm. Which rung
    /// was used, and the Hs above which it is compromised, are printed in the
    /// header -- and the `local` column on every row shows the capping
    /// directly, which is how the first bad run was caught.
    static readonly float[][] WaterLadder =
    {
        new[] { 600f, 100f, 1200f },
        new[] { 600f,  90f,  900f },
        new[] { 400f,  80f,  600f },
    };
    float PatchHalf = 600f;
    float MinPatchDepth = 100f;
    float IslandClearance = 1200f;
    int waterRung = -1;
    Vector3 deepestAnywhere; float deepestAnywhereMin = float.NegativeInfinity;
    Vector3 bestNearHome; float bestNearHomeMin = float.NegativeInfinity;
    const float ContaminationDepth = 100f;  // below this the tick is discarded
    const float ReplaceBlackout = 3f;       // dead time after a re-place, seconds
    const int MaxReplaces = 6;              // more than this and the leg is unusable
    const float SailingSpeed = 1.0f;        // m/s that counts as "she made way"

    const float SettleSeconds = 9f;         // sea rebuild after a force
    const float RampSeconds = 15f;          // Throttle moves at sailTrimRate x Labour01
    const float LegSeconds = 46f;           // sampling window
    const float SampleHz = 10f;

    public static void Execute()
    {
        // The sentinel FIRST, before anything that can fail. A probe that dies
        // halfway leaves a file saying so rather than leaving yesterday's
        // report sitting there looking like today's.
        try { System.IO.File.WriteAllText(OutPath, "DID NOT FINISH\n"); }
        catch (System.Exception e) { Debug.LogError("StormCostProbe: cannot write " + OutPath + ": " + e.Message); }

        if (!Application.isPlaying) { Debug.LogError("StormCostProbe: not in play mode"); return; }
        var old = FindAnyObjectByType<StormCostProbe>();
        if (old != null) Destroy(old.gameObject);
        new GameObject("StormCostProbe").AddComponent<StormCostProbe>();
    }

    // ------------------------------------------------------------------
    /// Mean / max / min over a leg. Kept as a double sum so a 460-sample
    /// leg at 1e-3 does not lose the low end to float error.
    class Acc
    {
        double sum;
        float hi = float.NegativeInfinity, lo = float.PositiveInfinity;
        public int N;
        public void Add(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return;
            sum += v; if (v > hi) hi = v; if (v < lo) lo = v; N++;
        }
        public float Mean { get { return N > 0 ? (float)(sum / N) : 0f; } }
        public float Max { get { return N > 0 ? hi : 0f; } }
        public float Min { get { return N > 0 ? lo : 0f; } }
    }

    class Leg
    {
        public float askedHs;
        public bool ran;
        public string skip = "";
        public float pinnedHs, localHs, maxSpeed = 1f;
        public float throttleEnd;
        public int samples;              // CLEAN samples only
        public int dirtyTicks;           // ticks discarded as contaminated
        public int blackoutTicks;        // ticks discarded as post-replace transient
        public int replaces, groundings;
        public float firstContaminationAt = -1f;
        public float shallowestSeen = float.PositiveInfinity;
        public float overSeconds, legSeconds, cleanSeconds;
        public float bilgeStart, bilgeEnd, integStart, integEnd;
        public float signedHeelMean;
        public float maxExcursion;       // furthest she got from the verified spot

        public Acc broach = new Acc(), overspeed = new Acc(), surfRun = new Acc();
        public Acc bilge = new Acc(), integ = new Acc(), rough = new Acc();
        public Acc heel = new Acc(), speedFrac = new Acc(), resist = new Acc();
        public Acc breaking = new Acc(), labour = new Acc();
        public Acc speedMs = new Acc(), depth = new Acc();

        public float BroachOver03 { get { return cleanSeconds > 0f ? overSeconds / cleanSeconds : 0f; } }
        public float BilgeRatePerMin { get { return legSeconds > 0f ? (bilgeEnd - bilgeStart) / legSeconds * 60f : 0f; } }
        public float HullLoss { get { return integStart - integEnd; } }

        /// A leg is only worth reading if most of it happened in the water it
        /// claims. Anything under this is printed as VOID rather than quietly
        /// averaged into the cost curve.
        public bool Clean
        {
            get
            {
                int wanted = Mathf.RoundToInt(LegSeconds * SampleHz);
                return ran && replaces <= MaxReplaces && samples >= 0.6f * wanted;
            }
        }
    }

    // --- restore state -------------------------------------------------
    ShipMotor motor;
    HelmInput helm;
    SeaStateController sea;
    Rigidbody rb;
    HullIntegrity hull;
    bool helmWasEnabled, anchorWas;
    float throttleWas;
    bool restored = true;

    // --- the verified water --------------------------------------------
    Vector3 spot;
    float spotMinDepth, spotIslandGap;
    int heightCalls;

    static float Signed(float deg) { return deg > 180f ? deg - 360f : deg; }

    static float Ground(float x, float z)
    {
        var h = Island.TerrainHeight;
        return h != null ? h(x, z) : -1000f;
    }

    /// Metres of water below MEAN water level. The wave adds and subtracts a
    /// few metres either side of this, which is noise against a 100 m gate,
    /// and taking it off the seabed directly avoids paying for a surface
    /// sample ten times a second.
    static float DepthUnder(Vector3 p) { return -Ground(p.x, p.z); }

    /// Depth from the SHORE GRID, which is the grid the envelope itself
    /// reads. Outside it the lookup returns 1e9, and that means "no data
    /// here", NOT "deep water" -- accepting the sentinel as deep is how an
    /// offshore search ends up certifying a spot it never measured. Returns
    /// a negative number for "not covered", which the caller prints as such.
    static float ShoreDepth(Vector3 p)
    {
        var region = RegionField.Instance;
        if (region == null || region.ShoreN <= 0 || !region.Shore.IsCreated) return -1f;
        float d = region.Params.ShoreWetDepth(new float2(p.x, p.z), region.Shore).z;
        return d > 1e8f ? -1f : d;
    }

    /// Metres between this point and the nearest island's OUTER radius. A
    /// cheap O(islands) reject that rules out the shoal a sampling grid may
    /// straddle: land does not stop at the waterline, and the bar off a
    /// headland is exactly the thing a coarse grid steps over.
    static float IslandGap(Vector3 c)
    {
        float worst = float.PositiveInfinity;
        var all = Island.All;
        for (int i = 0; i < all.Count; i++)
        {
            var isle = all[i];
            if (isle == null) continue;
            Vector3 ip = isle.transform.position;
            float d = Vector2.Distance(new Vector2(c.x, c.z), new Vector2(ip.x, ip.z));
            float gap = d - isle.MaxRadius;
            if (gap < worst) worst = gap;
        }
        return worst;
    }

    /// The shallowest point anywhere on the patch, sampled no more than
    /// `PatchSpacing` apart. Stops early the moment it finds water shallower
    /// than the bar and returns what it found there -- that sample IS the
    /// diagnostic, so a rejected candidate can still say why it was rejected.
    float PatchMinDepth(Vector3 c, float bar)
    {
        int n = Mathf.CeilToInt(PatchHalf / PatchSpacing);
        float step = PatchHalf / n;
        float worst = float.PositiveInfinity;
        for (int i = -n; i <= n; i++)
            for (int j = -n; j <= n; j++)
            {
                heightCalls++;
                float d = -Ground(c.x + i * step, c.z + j * step);
                if (d < worst) worst = d;
                if (worst < bar) return worst;
            }
        return worst;
    }

    IEnumerator Start()
    {
        var sb = new StringBuilder();

        motor = FindAnyObjectByType<ShipMotor>();
        helm = FindAnyObjectByType<HelmInput>();
        sea = SeaStateController.Instance;
        rb = motor != null ? motor.GetComponent<Rigidbody>() : null;
        if (motor == null || rb == null || sea == null)
        {
            Fail("StormCostProbe: no ShipMotor / Rigidbody / SeaStateController");
            yield break;
        }
        if (Island.TerrainHeight == null)
        {
            Fail("StormCostProbe: Island.TerrainHeight is null -- no terrain, cannot verify depth");
            yield break;
        }

        var bilge = motor.GetComponent<Bilge>();
        hull = motor.GetComponent<HullIntegrity>();
        var meter = motor.GetComponent<SmoothnessMeter>();
        var breakers = motor.GetComponent<Breakers>();
        var roster = motor.GetComponent<CrewRoster>();
        var yard = FindAnyObjectByType<Shipyard>();

        // --- take the helm ------------------------------------------------
        helmWasEnabled = helm != null && helm.enabled;
        anchorWas = motor.Anchored;
        throttleWas = motor.ThrottleOrder;
        restored = false;
        if (helm != null) helm.enabled = false;
        motor.Anchored = false;

        while (!OceanSampler.Ready) yield return null;

        // --- find the water ------------------------------------------------
        bool found = false;
        Vector3 bestP = Vector3.zero;
        float bestMin = float.NegativeInfinity, bestGap = 0f;
        int rejectedByIsland = 0, tried = 0;
        int nextYield = 512;

        for (int rungIdx = 0; rungIdx < WaterLadder.Length && !found; rungIdx++)
        {
            PatchHalf = WaterLadder[rungIdx][0];
            MinPatchDepth = WaterLadder[rungIdx][1];
            IslandClearance = WaterLadder[rungIdx][2];

            for (float dist = 2000f; dist <= 26000f && !found; dist += 400f)
                for (int b = 0; b < 24 && !found; b++)
                {
                    float a = b / 24f * Mathf.PI * 2f;
                    var c = new Vector3(Mathf.Sin(a) * dist, 0f, Mathf.Cos(a) * dist);
                    tried++;

                    float gap = IslandGap(c);
                    if (gap < IslandClearance) { rejectedByIsland++; }
                    else
                    {
                        float m = PatchMinDepth(c, MinPatchDepth);
                        if (m > bestMin) { bestMin = m; bestP = c; bestGap = gap; }
                        // Kept regardless of which rung is running, so the
                        // report can say where the deep water actually IS --
                        // and whether any of it is near where the game is
                        // played, which is a fact about the game and not
                        // about this probe.
                        if (m > deepestAnywhereMin) { deepestAnywhereMin = m; deepestAnywhere = c; }
                        if (dist <= 5000f && m > bestNearHomeMin) { bestNearHomeMin = m; bestNearHome = c; }
                        if (m >= MinPatchDepth)
                        {
                            spot = c; spotMinDepth = m; spotIslandGap = gap;
                            waterRung = rungIdx; found = true;
                        }
                    }
                    // The height function is fBm; a hundred thousand of them in
                    // one frame stalls the editor and the run looks hung.
                    if (heightCalls > nextYield) { nextYield = heightCalls + 512; yield return null; }
                }
        }

        if (!found)
        {
            Fail(string.Format(
                "StormCostProbe: no {0:F0} m square is deeper than {1:F0} m at EVERY sample "
                + "and {2:F0} m clear of every island, within 26 km. Tried {3} candidates "
                + "({4} rejected on island clearance alone). The best was {5:F0},{6:F0}, whose "
                + "shallowest point was {7:F0} m with {8:F0} m of island clearance. "
                + "WITHOUT THAT WATER THERE IS NOTHING TO MEASURE -- an Hs 50 leg over a shelf "
                + "measures the depth cap and reports it as a storm, which is what the first "
                + "run of this probe did at five of its seven rungs.",
                PatchHalf * 2f, MinPatchDepth, IslandClearance, tried, rejectedByIsland,
                bestP.x, bestP.z, bestMin, bestGap));
            yield break;
        }

        yield return Place(spot, true);
        float gridDepth = ShoreDepth(motor.transform.position);
        int gridN = Mathf.CeilToInt(PatchHalf / PatchSpacing);

        // --- header ---------------------------------------------------------
        sb.AppendLine("STORM COST PROBE -- what heavy weather costs her, by sea state");
        sb.AppendLine("=============================================================");
        sb.AppendLine();
        string rung = yard == null ? "no Shipyard in scene"
            : "nodeIndex " + yard.NodeIndex
              + (yard.Node != null ? "  (" + yard.Node.name + " / " + yard.Node.label + ")" : "");
        sb.AppendLine("ship          " + rung);
        sb.AppendLine(string.Format("mass          {0:F0} kg   HullLength {1:F1} m   maxSpeed {2:F1} m/s",
            rb.mass, motor.HullLength, motor.MaxSpeed));
        sb.AppendLine(string.Format("sailed at     {0:F0}, {1:F0}", spot.x, spot.z));
        sb.AppendLine(string.Format(
            "water         {0:F0} m square, sampled every {1:F0} m ({2} points): SHALLOWEST {3:F0} m (bar {4:F0} m)",
            PatchHalf * 2f, PatchHalf / gridN, (2 * gridN + 1) * (2 * gridN + 1),
            spotMinDepth, MinPatchDepth));
        sb.AppendLine(string.Format(
            "island gap    {0:F0} m clear of the nearest island's outer radius (bar {1:F0} m); {2} islands registered",
            spotIslandGap, IslandClearance, Island.All.Count));
        if (Island.All.Count == 0)
            sb.AppendLine("              <-- NO ISLANDS REGISTERED, so the clearance test did nothing. "
                + "The depth grid is the only thing between this run and a shoal.");
        sb.AppendLine(string.Format("search        {0} candidates tried, {1} rejected on island clearance alone",
            tried, rejectedByIsland));
        sb.AppendLine(string.Format(
            "water rung    {0} of {1}  (half-patch {2:F0} m, min depth {3:F0} m, island clearance {4:F0} m)",
            waterRung + 1, WaterLadder.Length, PatchHalf, MinPatchDepth, IslandClearance));
        float cappedAbove = MinPatchDepth / 1.82f;
        if (waterRung > 0)
            sb.AppendLine(string.Format(
                "              <-- FELL BACK. The depth cap stops binding at 1.82 x Hs, so above "
                + "Hs {0:F0} these rows measure the SHELF, not the storm. Watch the `local` column.",
                cappedAbove));
        else
            sb.AppendLine(string.Format(
                "              the depth cap stops binding at 1.82 x Hs, so rows above Hs {0:F0} "
                + "are partly measuring the shelf. Watch the `local` column.", cappedAbove));
        sb.AppendLine(string.Format(
            "deepest seen  {0:F0} m at {1:F0},{2:F0}  ({3:F1} km from origin)",
            deepestAnywhereMin, deepestAnywhere.x, deepestAnywhere.z,
            new Vector2(deepestAnywhere.x, deepestAnywhere.z).magnitude / 1000f));
        sb.AppendLine(string.Format(
            "best within 5 km of origin   {0:F0} m at {1:F0},{2:F0}   <-- the water the player actually sails",
            bestNearHomeMin, bestNearHome.x, bestNearHome.z));
        sb.AppendLine("shore grid    " + (gridDepth < 0f
            ? "does not cover her -- the lookup returned the 1e9 no-data sentinel, "
              + "which is NOT a depth. The seabed grid above is the verified one."
            : string.Format("{0:F0} m of water under her (grid agrees)", gridDepth)));
        sb.AppendLine(string.Format("crew          {0} aboard, {1} able, labour {2:F2}",
            roster != null ? roster.CrewCount : 0,
            roster != null ? roster.AbleCount : 0,
            roster != null ? roster.Labour01 : 1f));
        sb.AppendLine("components    " + (bilge != null ? "Bilge " : "NO Bilge ")
            + (hull != null ? "HullIntegrity " : "NO HullIntegrity ")
            + (meter != null ? "SmoothnessMeter " : "NO SmoothnessMeter ")
            + (breakers != null ? "Breakers " : "NO Breakers ")
            + (roster != null ? "CrewRoster" : "NO CrewRoster"));
        sb.AppendLine();

        // --- the ladder -------------------------------------------------------
        var legs = new List<Leg>();
        var frame = new Acc();

        for (int i = 0; i < Ladder.Length; i++)
        {
            var leg = new Leg();
            leg.askedHs = Ladder[i];
            leg.maxSpeed = motor.MaxSpeed;
            legs.Add(leg);

            sea.ForceHs(leg.askedHs);
            yield return Place(spot, true);
            motor.ThrottleOrder = 1f;
            yield return new WaitForSeconds(SettleSeconds);

            leg.pinnedHs = sea.CurrentHs;
            // Gracefully skip a rung the controller cannot actually reach --
            // the anchors bound the blend, so an Hs outside them saturates and
            // the leg would be a duplicate of its neighbour under a false name.
            if (Mathf.Abs(leg.pinnedHs - leg.askedHs) > 0.15f * leg.askedHs)
            {
                leg.skip = string.Format(
                    "cannot be forced: asked {0:F0} m, the blend delivers {1:F1} m (severity {2:F2})",
                    leg.askedHs, leg.pinnedHs, sea.Severity01);
                Debug.LogWarning("StormCostProbe: skipping Hs " + leg.askedHs + " -- " + leg.skip);
                continue;
            }

            // Run WITH the seas, and let the throttle ramp -- but WATCH her
            // while it does, because grounding during the ramp is just as
            // ruinous as grounding during the leg and the first version of
            // this probe slept through it.
            HeadDownSea();
            yield return Sail(leg, RampSeconds, null, null, null, null, null);

            leg.localHs = motor.SeaHs;
            leg.bilgeStart = bilge != null ? bilge.Bilge01 : 0f;
            leg.integStart = hull != null ? hull.Integrity01 : 1f;
            leg.ran = true;

            yield return Sail(leg, LegSeconds, frame, bilge, meter, breakers, roster);

            leg.bilgeEnd = bilge != null ? bilge.Bilge01 : 0f;
            leg.integEnd = hull != null ? hull.Integrity01 : 1f;
            leg.throttleEnd = motor.Throttle;
            motor.AutopilotTarget = null;
        }

        Restore();

        // --- report ------------------------------------------------------
        float meanMs = frame.Mean, worstMs = frame.Max;
        sb.AppendLine(string.Format(
            "frame rate    {0:F1} fps mean ({1:F1} ms), worst frame {2:F1} ms",
            meanMs > 0f ? 1000f / meanMs : 0f, meanMs, worstMs));
        sb.AppendLine("  WHICH NUMBERS DEPEND ON IT. Physics runs on a fixed timestep, and");
        sb.AppendLine("  Broach01, Overspeed01, SurfRunSeconds and Breaking01 are all integrated");
        sb.AppendLine("  in FixedUpdate -- those should be frame-rate independent. Bilge01,");
        sb.AppendLine("  Roughness01, SeaResistance01 and the Throttle ramp are integrated in");
        sb.AppendLine("  Update against Time.deltaTime: the RATES are dt-correct, but at a low");
        sb.AppendLine("  frame rate they sample a hull whose motion they only see occasionally,");
        sb.AppendLine("  so Roughness01 in particular is the one to distrust on a slow editor.");
        sb.AppendLine("  Sampling here is wall-clock 10 Hz, so a slow frame does not reweight a");
        sb.AppendLine("  leg. This is stated rather than assumed -- it has not been A/B'd at two");
        sb.AppendLine("  frame rates, and that A/B is the only thing that would settle it.");
        sb.AppendLine();
        sb.AppendLine(string.Format(
            "each leg: sea pinned, ship replaced on the spot, {0:F0} s settle, {1:F0} s throttle ramp "
            + "(watched, not slept through), {2:F0} s sampled at {3:F0} Hz, running down-sea.",
            SettleSeconds, RampSeconds, LegSeconds, SampleHz));
        sb.AppendLine(string.Format(
            "depth is re-asserted EVERY tick: under {0:F0} m the tick is DISCARDED, she is put back on "
            + "the verified spot and sails on, and the {1:F0} s after a re-place is discarded too so a "
            + "re-accelerating hull is not read as a slow one.", ContaminationDepth, ReplaceBlackout));
        sb.AppendLine();

        AppendTable(sb, legs);
        AppendCouldSheSail(sb, legs);
        AppendWhatBit(sb, legs);
        AppendZeroes(sb, legs);

        sb.AppendLine();
        sb.AppendLine("HOW TO READ A ROW");
        sb.AppendLine("  Hs asked / pinned / local. `pinned` is the controller's blend; `local` is");
        sb.AppendLine("  ShipMotor.SeaHs where she actually sailed, after the envelope. If local");
        sb.AppendLine("  falls badly short of pinned the depth limit was binding and that row is a");
        sb.AppendLine("  measurement of the shelf, not of the storm.");
        sb.AppendLine("  A row marked V is VOID: it did not spend enough of its leg in the water it");
        sb.AppendLine("  claims. Read the leg-conditions block before reading the row, and do not");
        sb.AppendLine("  average a VOID row into a cost curve.");
        sb.AppendLine("  resist is SeaResistance01, a MULTIPLIER on her way: 1.00 is a free ride and");
        sb.AppendLine("  its MIN is the bite, which is why the min is printed beside the mean.");
        sb.AppendLine("  hull loss is Integrity01 lost over the leg. Nothing in open water damages");
        sb.AppendLine("  the hull except Breakers.Batter and collision, so a column of zeros here is");
        sb.AppendLine("  the shipped design, not a broken probe -- see the zero flags. A row with");
        sb.AppendLine("  hull loss AND groundings is measuring the seabed, not the weather.");

        System.IO.File.WriteAllText(OutPath, sb.ToString());
        Debug.Log("StormCostProbe: wrote " + OutPath + "\n" + sb);
        Destroy(gameObject);
    }

    // ------------------------------------------------------------------
    /// Sail for `seconds`, keeping her down-sea, asserting the depth under
    /// her on EVERY tick, and accumulating into `leg` only while she is in
    /// water this run has verified. Passing null accumulators makes it a
    /// monitored wait -- same watchdog, no statistics, which is what the
    /// throttle ramp wants.
    IEnumerator Sail(Leg leg, float seconds, Acc frame, Bilge bilge,
                     SmoothnessMeter meter, Breakers breakers, CrewRoster roster)
    {
        bool measuring = frame != null;
        float t = 0f, nextSample = 0f, retarget = 0f, blackout = 0f;
        float lastImpact = hull != null ? hull.LastImpactTime : -99f;
        double signedSum = 0.0;
        int signedN = 0;

        motor.AutopilotTarget = DownSeaTarget();

        while (t < seconds)
        {
            float dt = Time.deltaTime;
            t += dt;
            if (blackout > 0f) blackout -= dt;
            if (measuring) frame.Add(Time.unscaledDeltaTime * 1000f);

            // Keep her running down-sea as the axes rotate. This is STEERING,
            // not a measurement -- nothing read below depends on it beyond
            // putting her on the faces at all.
            retarget += dt;
            if (retarget > 2f) { motor.AutopilotTarget = DownSeaTarget(); retarget = 0f; }

            // Grounding is READ off the hull's own record rather than inferred
            // from a speed of zero, so "she touched" can be told apart from
            // "the drive path gave up". Those are completely different
            // findings and the first run of this probe could not separate them.
            if (hull != null && hull.LastImpactTime > lastImpact)
            {
                lastImpact = hull.LastImpactTime;
                leg.groundings++;
            }

            Vector3 at = motor.transform.position;
            float depth = DepthUnder(at);
            if (depth < leg.shallowestSeen) leg.shallowestSeen = depth;
            float excursion = Vector2.Distance(new Vector2(at.x, at.z), new Vector2(spot.x, spot.z));
            if (excursion > leg.maxExcursion) leg.maxExcursion = excursion;

            if (depth < ContaminationDepth)
            {
                if (leg.firstContaminationAt < 0f && measuring) leg.firstContaminationAt = t;
                // Put her back on the verified spot and let her sail on. She
                // is NOT pinned there: the broach term only builds while she
                // is making way down a face, and a pinned ship measures
                // nothing at all.
                leg.replaces++;
                yield return Place(spot, false);
                HeadDownSea();
                motor.AutopilotTarget = DownSeaTarget();
                blackout = ReplaceBlackout;
                if (leg.replaces > MaxReplaces) break;
                continue;
            }

            if (measuring && t >= nextSample)
            {
                nextSample += 1f / SampleHz;
                if (blackout > 0f) leg.blackoutTicks++;
                else
                {
                    float b = motor.Broach01;
                    leg.broach.Add(b);
                    if (b > 0.3f) leg.overSeconds += 1f / SampleHz;
                    leg.cleanSeconds += 1f / SampleHz;
                    leg.overspeed.Add(motor.Overspeed01);
                    leg.surfRun.Add(motor.SurfRunSeconds);
                    if (bilge != null) leg.bilge.Add(bilge.Bilge01);
                    if (hull != null) leg.integ.Add(hull.Integrity01);
                    if (meter != null) leg.rough.Add(meter.Roughness01);
                    float roll = Signed(motor.transform.eulerAngles.z);
                    leg.heel.Add(Mathf.Abs(roll));
                    signedSum += roll; signedN++;
                    leg.speedMs.Add(motor.CurrentSpeed);
                    leg.speedFrac.Add(motor.CurrentSpeed / Mathf.Max(0.01f, motor.MaxSpeed));
                    leg.resist.Add(motor.SeaResistance01);
                    leg.depth.Add(depth);
                    if (breakers != null) leg.breaking.Add(breakers.Breaking01);
                    if (roster != null) leg.labour.Add(roster.Labour01);
                    leg.samples++;
                }
            }
            yield return null;
        }

        if (measuring)
        {
            leg.legSeconds = t;
            leg.dirtyTicks = Mathf.Max(0,
                Mathf.RoundToInt(seconds * SampleHz) - leg.samples - leg.blackoutTicks);
            leg.signedHeelMean = signedN > 0 ? (float)(signedSum / signedN) : 0f;
        }
    }

    void HeadDownSea()
    {
        motor.transform.rotation = Quaternion.Euler(0f, motor.SeasFromDeg + 180f, 0f);
        motor.Rudder = 0f;
    }

    Vector3 DownSeaTarget()
    {
        float downSea = (motor.SeasFromDeg + 180f) * Mathf.Deg2Rad;
        Vector3 dir = new Vector3(Mathf.Sin(downSea), 0f, Mathf.Cos(downSea));
        return motor.transform.position + dir * 6000f;
    }

    /// Moving a NON-KINEMATIC rigidbody by its transform alone does not stick;
    /// the body keeps its own position and snaps back on the next physics
    /// step. Set `rb.position` too. A mid-leg re-place waits only a moment,
    /// because a long wait is dead time inside a timed leg.
    IEnumerator Place(Vector3 to, bool full)
    {
        Vector3 p = new Vector3(to.x, 3f, to.z);
        motor.transform.position = p;
        if (rb != null)
        {
            rb.position = p;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        motor.Anchored = false;
        if (full) yield return new WaitForSeconds(1.0f);
        else { yield return new WaitForFixedUpdate(); yield return null; }
    }

    // ------------------------------------------------------------------
    static string Mark(Leg L) { return L.Clean ? " " : "V"; }

    static void AppendTable(StringBuilder sb, List<Leg> legs)
    {
        sb.AppendLine("THE COST CURVE -- driving her (mean / max, CLEAN samples only)");
        sb.AppendLine("  V Hs asked  pinned   local |  broach  max   %>0.3 | overspd  max | surfRun  max");
        sb.AppendLine("  - --------  ------  ------ |  ------ -----  ----- | ------- ----- | ------- -----");
        for (int i = 0; i < legs.Count; i++)
        {
            var L = legs[i];
            if (!L.ran) { sb.AppendLine(string.Format("    {0,7:F0}   SKIPPED -- {1}", L.askedHs, L.skip)); continue; }
            sb.AppendLine(string.Format(
                "  {0} {1,7:F0}  {2,6:F1}  {3,6:F1} |  {4,6:F3} {5,5:F2}  {6,4:F0}% | {7,7:F3} {8,5:F2} | {9,7:F1} {10,5:F1}",
                Mark(L), L.askedHs, L.pinnedHs, L.localHs,
                L.broach.Mean, L.broach.Max, 100f * L.BroachOver03,
                L.overspeed.Mean, L.overspeed.Max,
                L.surfRun.Mean, L.surfRun.Max));
        }

        sb.AppendLine();
        sb.AppendLine("THE COST CURVE -- what she takes aboard (mean / max)");
        sb.AppendLine("  V Hs asked |  bilge   max   per min | hull loss | rough   max  | breaking  max");
        sb.AppendLine("  - -------- | ------ ------ -------- | --------- | ------ ----- | -------- -----");
        for (int i = 0; i < legs.Count; i++)
        {
            var L = legs[i];
            if (!L.ran) { sb.AppendLine(string.Format("    {0,7:F0}   SKIPPED", L.askedHs)); continue; }
            sb.AppendLine(string.Format(
                "  {0} {1,7:F0} | {2,6:F3} {3,6:F3} {4,8:F4} | {5,9:F5} | {6,6:F3} {7,5:F2} | {8,8:F3} {9,5:F2}",
                Mark(L), L.askedHs,
                L.bilge.Mean, L.bilge.Max, L.BilgeRatePerMin,
                L.HullLoss,
                L.rough.Mean, L.rough.Max,
                L.breaking.Mean, L.breaking.Max));
        }

        sb.AppendLine();
        sb.AppendLine("THE COST CURVE -- how she moves and who is left (mean / max)");
        sb.AppendLine("  V Hs asked |  heel   max  signed | speed/max  max | resist  max   min | labour");
        sb.AppendLine("  - -------- | ------ -----  ----- | --------- ---- | ------ ----- ----- | ------");
        for (int i = 0; i < legs.Count; i++)
        {
            var L = legs[i];
            if (!L.ran) { sb.AppendLine(string.Format("    {0,7:F0}   SKIPPED", L.askedHs)); continue; }
            sb.AppendLine(string.Format(
                "  {0} {1,7:F0} | {2,6:F2} {3,5:F1} {4,6:F2} | {5,9:F3} {6,4:F2} | {7,6:F3} {8,5:F3} {9,5:F3} | {10,6:F3}",
                Mark(L), L.askedHs,
                L.heel.Mean, L.heel.Max, L.signedHeelMean,
                L.speedFrac.Mean, L.speedFrac.Max,
                L.resist.Mean, L.resist.Max, L.resist.Min,
                L.labour.Mean));
        }

        sb.AppendLine();
        sb.AppendLine("  LEG CONDITIONS -- did she sail the water the row claims?");
        int wanted = Mathf.RoundToInt(LegSeconds * SampleHz);
        for (int i = 0; i < legs.Count; i++)
        {
            var L = legs[i];
            if (!L.ran) continue;
            sb.AppendLine(string.Format(
                "    Hs {0,3:F0}  {1,4}/{2} clean ticks ({3,3:F0}%)   {4} discarded shallow, {5} discarded "
                + "post-replace   depth mean {6:F0} m, shallowest touched {7:F0} m",
                L.askedHs, L.samples, wanted, 100f * L.samples / Mathf.Max(1, wanted),
                L.dirtyTicks, L.blackoutTicks, L.depth.Mean,
                L.shallowestSeen == float.PositiveInfinity ? 0f : L.shallowestSeen));
            sb.AppendLine(string.Format(
                "           re-placed {0}x, grounded {1}x, furthest from the spot {2:F0} m, throttle {3:F2}{4}",
                L.replaces, L.groundings, L.maxExcursion, L.throttleEnd,
                L.firstContaminationAt >= 0f
                    ? string.Format(", first left deep water at t={0:F0} s", L.firstContaminationAt) : ""));
            if (!L.Clean)
                sb.AppendLine("           <-- VOID: too little of this leg happened in verified water. "
                    + "Do not read the row.");
            else if (L.localHs < 0.7f * L.pinnedHs)
                sb.AppendLine("           <-- local sea far below the pin: the envelope was binding even "
                    + "in deep water. Read this row against `local`, not `asked`.");
        }
        sb.AppendLine();
    }

    // ------------------------------------------------------------------
    /// The first run showed throttle 1.00 with 0.0-0.2 m/s at five rungs and
    /// 8.5 m/s at the other two. That is either grounding or a drive path
    /// that fails at the extremes, and those are completely different
    /// findings -- one is the harness, the other is the game. Grounding is
    /// read off `HullIntegrity.LastImpactTime`, and way is reported from
    /// CLEAN deep-water samples only, so the two can be told apart by reading.
    static void AppendCouldSheSail(StringBuilder sb, List<Leg> legs)
    {
        sb.AppendLine("COULD SHE SAIL AT ALL?");
        sb.AppendLine("======================");
        sb.AppendLine("  Hs asked | throttle | way in DEEP water (m/s) | groundings | verdict");
        sb.AppendLine("  -------- | -------- | ----------------------- | ---------- | -------");
        for (int i = 0; i < legs.Count; i++)
        {
            var L = legs[i];
            if (!L.ran) continue;
            string verdict;
            if (!L.Clean)
                verdict = "VOID -- too little clean water to say";
            else if (L.speedMs.Mean >= SailingSpeed)
                verdict = string.Format("she sailed ({0:P0} of her maximum)", L.speedMs.Mean / Mathf.Max(0.01f, L.maxSpeed));
            else if (L.groundings > 0)
                verdict = "stopped, and she GROUNDED -- the seabed, not the drive path";
            else
                verdict = "STOPPED IN DEEP WATER UNDER FULL SAIL -- this is a FINDING";
            sb.AppendLine(string.Format("  {0,7:F0} | {1,8:F2} | {2,7:F2} mean, {3,5:F2} max  | {4,10} | {5}",
                L.askedHs, L.throttleEnd, L.speedMs.Mean, L.speedMs.Max, L.groundings, verdict));
        }
        sb.AppendLine("  `way` comes from clean deep-water ticks only, so a leg that touched is not");
        sb.AppendLine("  credited with the zero that touching produced. `HullIntegrity.Aground`");
        sb.AppendLine("  shoves her clear and calls `KillVelocityAlong` EVERY frame she is on the");
        sb.AppendLine("  bottom, so a grounded ship reads as full canvas and no way -- which is");
        sb.AppendLine("  exactly what the first run of this probe reported at five of seven rungs.");
        sb.AppendLine("  A row saying STOPPED IN DEEP WATER is the interesting one: full sail, no");
        sb.AppendLine("  seabed within 100 m, and no way. That is a sailing model that gives up in");
        sb.AppendLine("  a storm, and it belongs in the cost curve rather than hidden as a bad leg.");
        sb.AppendLine();
    }

    // ------------------------------------------------------------------
    /// Which term moved MOST from the rung below. Deltas in raw units are not
    /// comparable -- surfRun is in seconds and bilge is a fraction -- so each
    /// is normalised by its own span across the whole ladder. A term that
    /// barely moves anywhere cannot win a rung just by being noisy.
    ///
    /// VOID legs are excluded outright. Comparing against a row that measured
    /// a shelf produces a confident sentence about nothing, which is what the
    /// first run of this probe printed six times over.
    static void AppendWhatBit(StringBuilder sb, List<Leg> legs)
    {
        var names = new string[] {
            "Broach01", "Overspeed01", "SurfRunSeconds", "Bilge01", "hull loss",
            "Roughness01", "heel deg", "speed/max", "SeaResistance01", "Breaking01", "Labour01" };

        var ran = new List<Leg>();
        for (int i = 0; i < legs.Count; i++) if (legs[i].Clean) ran.Add(legs[i]);

        sb.AppendLine("WHAT ACTUALLY BIT");
        sb.AppendLine("=================");
        int voided = 0;
        for (int i = 0; i < legs.Count; i++) if (legs[i].ran && !legs[i].Clean) voided++;
        if (voided > 0)
            sb.AppendLine(string.Format("  ({0} VOID leg(s) excluded -- a rung that measured a shelf "
                + "cannot tell you what the sea did.)", voided));
        if (ran.Count < 2)
        {
            sb.AppendLine("  fewer than two clean legs ran; there is nothing to compare.");
            sb.AppendLine();
            return;
        }

        int T = names.Length;
        var span = new float[T];
        for (int k = 0; k < T; k++)
        {
            float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
            for (int i = 0; i < ran.Count; i++)
            {
                float v = Term(ran[i], k);
                if (v < lo) lo = v;
                if (v > hi) hi = v;
            }
            span[k] = Mathf.Max(1e-6f, hi - lo);
        }

        for (int i = 1; i < ran.Count; i++)
        {
            int best = -1, second = -1;
            float bestS = -1f, secondS = -1f;
            for (int k = 0; k < T; k++)
            {
                float s = Mathf.Abs(Term(ran[i], k) - Term(ran[i - 1], k)) / span[k];
                if (s > bestS) { second = best; secondS = bestS; best = k; bestS = s; }
                else if (s > secondS) { second = k; secondS = s; }
            }
            sb.AppendLine(string.Format("  Hs {0,3:F0} -> {1,3:F0}", ran[i - 1].askedHs, ran[i].askedHs));
            sb.AppendLine(string.Format("      {0,-16} {1,9:F4} -> {2,9:F4}   ({3:+0.0%;-0.0%;0.0%} of its whole-ladder span)",
                names[best], Term(ran[i - 1], best), Term(ran[i], best),
                (Term(ran[i], best) - Term(ran[i - 1], best)) / span[best]));
            if (second >= 0 && secondS > 0.02f)
                sb.AppendLine(string.Format("      {0,-16} {1,9:F4} -> {2,9:F4}   ({3:+0.0%;-0.0%;0.0%})",
                    names[second], Term(ran[i - 1], second), Term(ran[i], second),
                    (Term(ran[i], second) - Term(ran[i - 1], second)) / span[second]));
        }
        sb.AppendLine();
    }

    static float Term(Leg L, int k)
    {
        switch (k)
        {
            case 0: return L.broach.Mean;
            case 1: return L.overspeed.Mean;
            case 2: return L.surfRun.Mean;
            case 3: return L.bilge.Mean;
            case 4: return L.HullLoss;
            case 5: return L.rough.Mean;
            case 6: return L.heel.Mean;
            case 7: return L.speedFrac.Mean;
            case 8: return L.resist.Mean;
            case 9: return L.breaking.Mean;
            default: return L.labour.Mean;
        }
    }

    static float TermMax(Leg L, int k)
    {
        switch (k)
        {
            case 0: return L.broach.Max;
            case 1: return L.overspeed.Max;
            case 2: return L.surfRun.Max;
            case 3: return L.bilge.Max;
            case 4: return L.HullLoss;
            case 5: return L.rough.Max;
            case 6: return L.heel.Max;
            case 7: return L.speedFrac.Max;
            case 8: return L.resist.Max;
            case 9: return L.breaking.Max;
            default: return L.labour.Max;
        }
    }

    // ------------------------------------------------------------------
    /// A mechanic that never fires anywhere on the ladder is the finding.
    /// Two of these have a known structural reason and are named, so a real
    /// dead term is not lost among the expected ones. Only CLEAN legs count:
    /// a term is not dead because the legs that would have shown it were void.
    static void AppendZeroes(StringBuilder sb, List<Leg> legs)
    {
        var names = new string[] {
            "Broach01", "Overspeed01", "SurfRunSeconds", "Bilge01", "hull loss",
            "Roughness01", "heel deg", "speed/max", "SeaResistance01", "Breaking01", "Labour01" };

        sb.AppendLine("FLAT ACROSS THE WHOLE LADDER");
        sb.AppendLine("============================");
        int clean = 0;
        for (int i = 0; i < legs.Count; i++) if (legs[i].Clean) clean++;
        if (clean == 0)
        {
            sb.AppendLine("  NO CLEAN LEGS. Nothing here can be called dead, because nothing here");
            sb.AppendLine("  was measured. Fix the water before reading the mechanics.");
            sb.AppendLine();
            return;
        }
        sb.AppendLine(string.Format("  (over {0} clean leg(s))", clean));

        int flagged = 0;
        for (int k = 0; k < names.Length; k++)
        {
            float peak = 0f;
            for (int i = 0; i < legs.Count; i++)
            {
                if (!legs[i].Clean) continue;
                peak = Mathf.Max(peak, Mathf.Abs(TermMax(legs[i], k)));
            }
            if (peak > 1e-4f) continue;
            flagged++;
            string why = "";
            if (k == 9)
                why = "  -- EXPECTED HERE: Breakers keys on Hs / depth under the keel, and this "
                    + "probe deliberately sails water over 120 m deep. It is NOT evidence the surf "
                    + "is dead; it is evidence this probe cannot see it. Measure it inshore.";
            if (k == 4)
                why = "  -- nothing in open water touches Integrity01 except Breakers.Batter and "
                    + "collision, so with no surf and no land this is the shipped design. The "
                    + "finding is that A STORM ALONE COSTS HER NO HULL AT ALL.";
            sb.AppendLine("  " + names[k] + " never left zero at any sea state." + why);
        }
        if (flagged == 0) sb.AppendLine("  none -- every term moved somewhere on the ladder.");
        sb.AppendLine();
    }

    // ------------------------------------------------------------------
    void Fail(string msg)
    {
        Debug.LogError(msg);
        try { System.IO.File.WriteAllText(OutPath, "DID NOT FINISH\n" + msg + "\n"); }
        catch (System.Exception) { }
        Restore();
        Destroy(gameObject);
    }

    /// Probe statics and forced state survive leaving play mode in this
    /// project, so a leg that dies half way poisons every run after it.
    /// Called on every exit path, and again from OnDisable in case the
    /// coroutine is killed by a domain reload.
    void Restore()
    {
        if (restored) return;
        restored = true;
        if (sea != null) sea.ReleaseForce();
        if (motor != null)
        {
            motor.AutopilotTarget = null;
            motor.Rudder = 0f;
            motor.ThrottleOrder = throttleWas;
            motor.Anchored = anchorWas;
        }
        if (helm != null) helm.enabled = helmWasEnabled;
    }

    void OnDisable() { Restore(); }
}
