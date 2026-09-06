using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using SeaSick.Ship;

/// Does WHERE you put things actually change the ship?
///
/// The claim behind the whole stowage idea is that a player rearranging weight
/// should feel it. That is a claim about numbers before it is a claim about
/// fun, and there are two that matter: **GM** (`KM - KG`, her metacentric
/// height — small is tender, big is stiff, negative and she does not stand up)
/// and **roll period**, which is what a player without an instrument actually
/// perceives and which moves as `1/sqrt(GM)`.
///
/// It builds the same stowages a player would, through `SetUse` — the call the
/// board makes — rather than poking `ShipLoad`, because a probe with its own
/// path can pass while the yard is broken.
///
/// **Two traps this probe fell into itself, both worth keeping:**
///
///  * *"Guns high" and "guns low" filled the SAME CELLS.* Asking for 99 gun
///    bays from the top and 99 from the bottom fills every port-bearing cell
///    either way, so the two rows agreed to the last decimal and looked like
///    a model that ignored height. Fill a LIMITED number or the comparison is
///    not a comparison. And a two-tier hull has one gun deck, so there is no
///    high-vs-low question to ask on her at all.
///  * *The inclining experiment shifted one hand.* `Crew` is `max(1, Berths)`
///    and these stowages have no quarters, so it walked 90 kg across a 276 t
///    brig and measured wave noise.
public class StowProbe : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("StowProbe: play mode only"); return; }
        var old = FindAnyObjectByType<StowProbe>();
        if (old != null) Destroy(old.gameObject);
        new GameObject("StowProbe").AddComponent<StowProbe>();
    }

    const string Out = "/tmp/seasick-stowprobe.txt";

    Shipyard yard;
    StringBuilder sb;

    void Clear(LadderNode n)
    {
        foreach (var b in n.bay_labels)
            foreach (var t in n.tier_names)
                yard.SetUse(b, t, BayUse.Empty);
    }

    /// Fill `count` cells, taking tiers from the top down or the bottom up,
    /// and within a tier from amidships outward. `fore` biases to one end
    /// instead, which is how the trim rows are built.
    int Fill(LadderNode n, BayUse use, int count, bool fromTop,
             bool needPort = false, int fore = 0)
    {
        var order = new List<(int bi, int ti, float rank)>();
        for (int ti = 0; ti < n.tier_names.Length; ti++)
            for (int bi = 0; bi < n.bay_labels.Length; bi++)
            {
                float tierRank = fromTop ? (n.tier_names.Length - 1 - ti) : ti;
                float bayRank = fore == 0 ? Mathf.Abs(n.bay_x[bi])
                                          : -fore * n.bay_x[bi];
                order.Add((bi, ti, tierRank * 1000f + bayRank));
            }
        order.Sort((a, b) => a.rank.CompareTo(b.rank));

        int done = 0;
        foreach (var (bi, ti, _) in order)
        {
            if (done >= count) break;
            string b = n.bay_labels[bi], t = n.tier_names[ti];
            if (yard.Use(b, t) != BayUse.Empty) continue;
            if (needPort && !yard.CanBearGun(b, t)) continue;
            yard.SetUse(b, t, use);
            done++;
        }
        return done;
    }

    void Row(string what)
    {
        var L = yard.Load;
        sb.AppendLine($"  {what,-38} {L.TotalKg / 1000f,6:F0} {L.KGm,6:F2} "
                    + $"{L.GMm,6:F2} {Period(L.RollPeriodS),7} {L.TrimM,6:F2} "
                    + $"  {Verdict(L)}"
                    + (L.Overweight ? "  [BALLAST CLAMPED — floats deep]" : ""));
    }

    static string Period(float s) => s <= 0.01f ? "  —  " : s.ToString("F1") + " s";

    static string Verdict(ShipLoad L)
    {
        float gm = L.GMm;
        if (gm <= 0f) return "SHE WILL NOT STAND UP";
        if (gm < 0.35f) return "dangerously tender";
        if (gm < 0.75f) return "tender";
        if (gm < 1.60f) return "easy";
        if (gm < 2.60f) return "stiff";
        return "very stiff";
    }

    IEnumerator Start()
    {
        sb = new StringBuilder("=== StowProbe ===\n");
        yard = FindFirstObjectByType<Shipyard>();
        if (yard == null)
        {
            System.IO.File.WriteAllText(Out, "StowProbe: no Shipyard\n");
            Debug.LogError("StowProbe: no Shipyard"); Destroy(gameObject); yield break;
        }
        var sea = SeaSick.Ocean.SeaStateController.Instance;
        if (sea != null) sea.ForceHs(0.15f);      // flat: roll is the stowage
        int startedOn = yard.NodeIndex;

        // **The hold is SHARED STATE and this probe fills it.** The first run
        // left 48 units of timber aboard, and the next probe to run — the
        // float gate, which builds its own twenty rigs and reads cargo off the
        // same VoyageManager — put 24 tonnes into every one of them. On a
        // 5.3 t skiff that is four times her displacement and she went under;
        // on the three-decker it is 1.2% and she barely moved. Twenty rungs
        // failing in inverse proportion to their size, and not one of them a
        // regression. Give the hold back exactly as it was found.
        var voyageState = FindFirstObjectByType<SeaSick.Voyage.VoyageManager>();
        int heldWas = voyageState != null ? voyageState.TotalHeld : 0;
        int capacityWas = voyageState != null ? voyageState.HoldCapacity : 0;

        foreach (int rung in new[] { 12, 19 })
        {
            yard.Apply(rung);
            yield return null;
            var n = yard.Node;
            sb.AppendLine($"\n== rung {rung}: {n.label} — {n.length:F0} x {n.beam:F1} m, "
                        + $"depth {n.depth:F1}, KM {n.km_above_keel_m:F2}, "
                        + $"{n.cells} cells, {n.gun_rows} gun deck(s) ==");

            // Where her mass actually is before the player touches anything.
            Clear(n); yield return null;
            var B = yard.Load;
            sb.AppendLine($"  mass budget, bare hull: lightship {B.LightshipKg / 1000f:F0} t "
                        + $"({B.LightshipKg / B.TotalKg:P0}), "
                        + $"ground ballast {B.BallastKg / 1000f:F0} t "
                        + $"({B.BallastKg / B.TotalKg:P0}) sitting at "
                        + $"{n.depth * 0.10f:F2} m above the keel");

            sb.AppendLine("  stowage                                mass t    KG     GM"
                        + "    roll   trim   she reads as");

            // A LIMITED battery, so high and low are different cells.
            int k = Mathf.Max(2, n.ports_per_side / 3);
            int third = Mathf.Max(1, n.cells / 3);

            Clear(n); yield return null; Row("bare hull, nothing aboard");

            Clear(n); Fill(n, BayUse.Battery, k, true, needPort: true);
            yield return null; Row($"{k} gun bays, HIGHEST deck");

            Clear(n); Fill(n, BayUse.Battery, k, false, needPort: true);
            yield return null;
            Row(n.gun_rows > 1 ? $"{k} gun bays, LOWEST deck"
                               : $"{k} gun bays, lowest (= same deck, 1 gun row)");

            Clear(n); Fill(n, BayUse.Battery, k, true, needPort: true);
            int bal = Fill(n, BayUse.Ballast, third, false);
            yield return null; Row($"high battery + {bal} bays of iron");

            Clear(n); Fill(n, BayUse.Ballast, third, false);
            yield return null; Row($"{third} bays of iron, nothing else");

            // Trim: the same hold, stowed forward and then aft — WITH CARGO
            // IN IT. Hold cells are capacity, not contents: `CargoUnits` comes
            // from the voyage manager, and an empty hold stowed forward is an
            // empty hold. The first run of this probe reported trim 0.00 for
            // both and it was measuring nothing at all.
            var voyage = FindFirstObjectByType<SeaSick.Voyage.VoyageManager>();
            Clear(n); Fill(n, BayUse.Hold, third, false, fore: 1);
            yield return null;
            int loaded = LoadHer(voyage, third * Shipyard.CargoPerHold);
            yield return null; Row($"{third} hold bays FORWARD, {loaded} units in");

            Clear(n); Fill(n, BayUse.Hold, third, false, fore: -1);
            yield return null; Row($"{third} hold bays AFT, {loaded} units in");

            Clear(n); Fill(n, BayUse.Hold, third, true, fore: 0);
            yield return null; Row($"{third} hold bays on her TOP deck");

            Span(n);
        }

        yield return StartCoroutine(Incline(12));

        // Give the sea back too. A forced Hs survives leaving play mode
        // (domain reload is disabled), so a probe that pins it flat and walks
        // away leaves every later run measuring a pond.
        if (sea != null) sea.ReleaseForce();

        if (voyageState != null)
        {
            voyageState.Jettison(voyageState.TotalHeld);
            if (heldWas > 0) voyageState.AddLoot(heldWas, "Timber");
            voyageState.SetHoldCapacity(Mathf.Max(1, capacityWas));
        }

        System.IO.File.WriteAllText(Out, sb.ToString());
        Debug.Log(sb.ToString());
        yard.Apply(startedOn);
        Destroy(gameObject);
    }

    /// The inclining experiment: shift a KNOWN weight athwartships and see how
    /// far she leans. `tan(theta) = w*d / (W*GM)`, so the heel measures GM.
    ///
    /// This is the honest check that the emergent per-probe buoyancy and the
    /// `KM - KG` arithmetic tell the same story. If they ever disagree, believe
    /// the water: the arithmetic is a summary, the probes are the physics.
    IEnumerator Incline(int rung)
    {
        yard.Apply(rung);
        yield return null;
        var n = yard.Node;
        Clear(n);
        Fill(n, BayUse.Battery, Mathf.Max(2, n.ports_per_side / 3), true, needPort: true);
        Fill(n, BayUse.Hold, 99, false);
        yield return null;

        var rb = yard.GetComponent<Rigidbody>();
        if (rb == null) yield break;
        var L = yard.Load;

        // A real inclining weight is a few per cent of displacement, not one
        // sailor. Five per cent, moved a third of her beam.
        float shiftKg = L.TotalKg * 0.05f;
        float arm = n.beam * 0.33f;
        float predicted = Mathf.Atan2(shiftKg * arm,
                          Mathf.Max(1f, L.TotalKg * Mathf.Max(0.01f, L.GMm)))
                          * Mathf.Rad2Deg;

        sb.AppendLine($"\n== inclining experiment: {n.label} ==");
        sb.AppendLine($"  GM {L.GMm:F2} m, displacement {L.TotalKg / 1000f:F0} t, "
                    + $"roll {Period(L.RollPeriodS)}");
        sb.AppendLine($"  shifting {shiftKg / 1000f:F0} t ({5}% of her) {arm:F1} m "
                    + $"to leeward should heel her {predicted:F2} deg");

        var com = rb.centerOfMass;
        yield return new WaitForSeconds(6f);
        float before = 0f;
        yield return SampleRoll(6f, r => before = r);

        rb.centerOfMass = com + new Vector3(shiftKg * arm / Mathf.Max(1f, L.TotalKg),
                                            0f, 0f);
        yield return new WaitForSeconds(6f);
        float after = 0f;
        yield return SampleRoll(6f, r => after = r);
        rb.centerOfMass = com;

        sb.AppendLine($"  measured {before:F2} deg -> {after:F2} deg, "
                    + $"a heel of {Mathf.Abs(after - before):F2} deg");
        sb.AppendLine($"  arithmetic and water differ by "
                    + $"{Mathf.Abs(Mathf.Abs(after - before) - predicted):F2} deg");
    }

    /// Average roll over whole seconds — a single frame catches her mid-swing.
    IEnumerator SampleRoll(float seconds, System.Action<float> done)
    {
        float sum = 0f; int c = 0; float t0 = Time.time;
        while (Time.time - t0 < seconds)
        {
            sum += SignedRoll(yard.transform); c++;
            yield return null;
        }
        done(c > 0 ? sum / c : 0f);
    }

    /// Put real cargo aboard, so a hold has contents and not just capacity.
    int LoadHer(SeaSick.Voyage.VoyageManager v, int units)
    {
        if (v == null) return 0;
        v.SetHoldCapacity(units);
        v.AddLoot(units - v.TotalHeld, "Timber");
        yard.ApplyLoad();
        return v.TotalHeld;
    }

    /// The whole question in one line: how much of her stability is the
    /// player's to command? Walks the extremes — everything as low as it goes
    /// against everything as high as it goes — and reports the spread.
    void Span(LadderNode n)
    {
        // **The SAME weight, moved.** The first version of this filled every
        // cell low and only the top tier high, so it compared 192 t of iron
        // against 48 t and reported that stowing it high made her MORE stable.
        // A stowage comparison has to hold the mass fixed and move it.
        int k = Mathf.Max(1, Mathf.Min(n.bay_labels.Length, n.cells / 4));

        Clear(n);
        int put = Fill(n, BayUse.Ballast, k, false);
        var lo = yard.Load; float gmLo = lo.GMm, tLo = lo.RollPeriodS;

        Clear(n);
        Fill(n, BayUse.Ballast, put, true);
        var hi = yard.Load; float gmHi = hi.GMm, tHi = hi.RollPeriodS;

        sb.AppendLine($"  -> the SAME {put} bays of iron ({put * ShipLoad.BallastPerCellKg / 1000f:F0} t, "
                    + $"{put * ShipLoad.BallastPerCellKg / Mathf.Max(1f, lo.TotalKg):P0} of her), "
                    + $"stowed LOW vs HIGH: GM {gmLo:F2} -> {gmHi:F2} "
                    + $"({(gmLo - gmHi) / Mathf.Max(0.01f, gmLo):P0} of her stability), "
                    + $"roll {Period(tLo)} -> {Period(tHi)}");
        Clear(n);
    }

    static float SignedRoll(Transform t)
    {
        var right = t.right;
        return Mathf.Atan2(right.y, new Vector2(right.x, right.z).magnitude)
               * Mathf.Rad2Deg;
    }
}
