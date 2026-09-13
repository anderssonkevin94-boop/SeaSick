using System.Text;
using UnityEngine;
using SeaSick.World;

/// The D2 gate: **the ledger is the truth, and its arithmetic does not depend
/// on how often anybody asks it.**
///
/// This is the one property the whole absentee loop rests on. If ticking often
/// produced a different answer from ticking rarely, then a camp would pay
/// differently depending on whether the player was standing in front of it —
/// and the optimal play would be to sit and watch a man chop wood, which is
/// the exact opposite of the game being built.
///
/// **Pure arithmetic, no scene.** That is deliberate and it is the point:
/// these gates run against ledgers built in this file, not against islands,
/// because the thing under test has to work for an island that is not loaded.
/// Nothing here touches a MonoBehaviour, so nothing here can be fooled by one.
///
/// Note what is NOT tested: any claim about crew agents matching this rate.
/// There is no such claim to test — production is this arithmetic and the
/// walking crew are its animation. Parity is structural, not tuned.
public class LedgerProbe : MonoBehaviour
{
    public static void Execute()
    {
        var sb = new StringBuilder();
        int fails = 0;

        float day = Mathf.Max(0.0001f, SeaSick.World.TimeOfDay.DayLength);
        sb.AppendLine($"day = {day:F0} s, quantum = {OutpostLedger.QuantumDays:F2} day "
            + $"({OutpostLedger.QuantumDays * day:F1} s)");
        sb.AppendLine($"rates: {OutpostLedger.TimberPerHandPerDay:F1} logs/hand/day, "
            + $"ceiling {OutpostLedger.CampfireCeiling}, "
            + $"{OutpostLedger.StandingPerHectare:F0} logs/ha standing, "
            + $"regrowth {OutpostLedger.RegrowthPerDay * 100f:F0}%/day");
        sb.AppendLine("  (every one of those is a guess and none has been played)");
        sb.AppendLine();

        // --- 1. path independence -------------------------------------------
        //
        // The headline property. Same elapsed time, wildly different numbers of
        // calls, and the state has to come out identical.

        double t0 = 1000.0;
        double span = 12.0 * day;          // twelve game days

        var once = Working(t0);
        once.Tick(t0 + span);

        var many = Working(t0);
        for (int i = 1; i <= 480; i++) many.Tick(t0 + span * i / 480.0);

        var lumpy = Working(t0);
        lumpy.Tick(t0 + span * 0.03);
        lumpy.Tick(t0 + span * 0.31);
        lumpy.Tick(t0 + span * 0.32);      // a tiny step
        lumpy.Tick(t0 + span * 0.87);
        lumpy.Tick(t0 + span);

        sb.AppendLine("PATH INDEPENDENCE over 12 game days, 2 hands cutting:");
        sb.AppendLine($"  one call      timber {once.timber,3}  standing {once.standing,7:F2}  part {once.timberPart:F4}");
        sb.AppendLine($"  480 calls     timber {many.timber,3}  standing {many.standing,7:F2}  part {many.timberPart:F4}");
        sb.AppendLine($"  5 ragged      timber {lumpy.timber,3}  standing {lumpy.standing,7:F2}  part {lumpy.timberPart:F4}");

        Gate(sb, ref fails, "one-call-equals-many", Same(once, many),
            $"{once.timber}/{once.standing:F3} vs {many.timber}/{many.standing:F3}");
        Gate(sb, ref fails, "one-call-equals-ragged", Same(once, lumpy),
            $"{once.timber}/{once.standing:F3} vs {lumpy.timber}/{lumpy.standing:F3}");

        // --- 2. idempotence --------------------------------------------------

        var idem = Working(t0);
        idem.Tick(t0 + 5.0 * day);
        int afterFirst = idem.timber;
        double stampAfterFirst = idem.lastTicked;
        for (int i = 0; i < 20; i++) idem.Tick(t0 + 5.0 * day);

        sb.AppendLine();
        sb.AppendLine($"IDEMPOTENCE: {afterFirst} logs, then 20 more calls at the same instant "
            + $"-> {idem.timber} logs");
        Gate(sb, ref fails, "ticking-twice-does-nothing",
            idem.timber == afterFirst && System.Math.Abs(idem.lastTicked - stampAfterFirst) < 1e-9,
            $"{afterFirst} -> {idem.timber}");

        // --- 3. the clock running backwards ----------------------------------
        //
        // A dev tool can scrub TimeOfDay. A ledger that banked negative time
        // would pay it back later as a burst of free timber.

        var back = Working(t0);
        back.Tick(t0 + 4.0 * day);
        int beforeScrub = back.timber;
        back.Tick(t0);                      // the clock jumps backwards
        back.Tick(t0 + 0.5 * day);
        sb.AppendLine();
        sb.AppendLine($"CLOCK SCRUBBED BACK: {beforeScrub} logs, wound back, half a day on "
            + $"-> {back.timber} logs");
        Gate(sb, ref fails, "no-free-timber-from-a-scrub",
            back.timber <= beforeScrub + 2, $"{beforeScrub} -> {back.timber}");

        // --- 4. the ceiling is the whole design ------------------------------

        var full = Working(t0);
        full.Tick(t0 + 400.0 * day);
        sb.AppendLine();
        sb.AppendLine($"CEILING after 400 days: timber {full.timber} against a ceiling of {full.ceiling}");
        Gate(sb, ref fails, "ceiling-holds", full.timber <= full.ceiling,
            $"{full.timber} logs, ceiling {full.ceiling}");
        Gate(sb, ref fails, "ceiling-is-reached", full.timber == full.ceiling,
            $"{full.timber} of {full.ceiling} after 400 days");

        // --- 5. depletion has to actually bite -------------------------------
        //
        // Kevin chose a stock that depletes. If the numbers make it
        // unreachable then "depletes and regrows" is a claim, not a mechanic.

        var strip = Working(t0, hectares: 0.5f);
        float startStanding = strip.standing;
        int daysToStrip = 0;
        for (int d = 1; d <= 400; d++)
        {
            strip.ceiling = 100000;         // take the ceiling out of the question
            strip.Tick(t0 + d * (double)day);
            if (strip.standing < 1f) { daysToStrip = d; break; }
        }
        sb.AppendLine();
        sb.AppendLine($"DEPLETION on half a hectare ({startStanding:F0} logs standing), "
            + $"ceiling lifted: stripped in {daysToStrip} game days "
            + $"({daysToStrip * day / 60f:F1} real minutes at the current day length)");
        Gate(sb, ref fails, "the-wood-can-run-out", daysToStrip > 0,
            daysToStrip > 0 ? $"stripped on day {daysToStrip}"
                            : $"still {strip.standing:F1} standing after 400 days");
        Gate(sb, ref fails, "but-not-instantly", daysToStrip == 0 || daysToStrip >= 3,
            $"stripped in {daysToStrip} days");

        // --- 6. and it has to come back --------------------------------------

        var regrow = new OutpostLedger { standingMax = 100f, standing = 0f, lastTicked = t0 };
        regrow.Tick(t0 + 30.0 * day);
        sb.AppendLine();
        sb.AppendLine($"REGROWTH from bare: {regrow.standing:F1} of {regrow.standingMax:F0} "
            + $"after 30 game days");
        Gate(sb, ref fails, "the-wood-comes-back", regrow.standing > 20f,
            $"{regrow.standing:F1} after 30 days");

        // --- 7. it has to survive a save -------------------------------------
        //
        // There is no save system yet (D4). This gate is why the ledger is a
        // plain serialisable object anyway: the cheap moment to find out that
        // it will not round-trip is now, not after everything reads it.

        var before = Working(t0);
        before.Tick(t0 + 7.0 * day);
        before.hands[0].mood = 0.42f;
        before.built.Add("Storehouse");
        string json = JsonUtility.ToJson(before);
        var after = JsonUtility.FromJson<OutpostLedger>(json);

        sb.AppendLine();
        sb.AppendLine($"SAVE ROUND-TRIP: {json.Length} chars of JSON");
        sb.AppendLine($"  {json}");
        bool same = after != null && Same(before, after)
            && after.hands.Count == before.hands.Count
            && after.hands[0].name == before.hands[0].name
            && Mathf.Abs(after.hands[0].mood - 0.42f) < 1e-5
            && after.built.Count == 1 && after.built[0] == "Storehouse"
            && after.keyX == before.keyX && after.keyZ == before.keyZ
            && System.Math.Abs(after.lastTicked - before.lastTicked) < 1e-9;
        Gate(sb, ref fails, "round-trips-through-json", same,
            same ? "identical after reload" : "state differs after reload");

        // A restored ledger must go on ticking from where it was, not from now.
        after.Tick(t0 + 9.0 * day);
        var control = Working(t0);
        control.Tick(t0 + 9.0 * day);
        control.hands[0].mood = 0.42f;
        Gate(sb, ref fails, "a-restored-ledger-carries-on", Same(after, control),
            $"restored {after.timber}/{after.standing:F2} vs control {control.timber}/{control.standing:F2}");

        // --- 8. identity is positional ---------------------------------------

        var keyed = new OutpostLedger();
        keyed.SetKey(new Vector3(-758.6f, 12f, 104.4f));
        sb.AppendLine();
        sb.AppendLine($"KEY: (-758.6, 104.4) -> {keyed.keyX},{keyed.keyZ}");
        Gate(sb, ref fails, "key-is-position-not-index",
            keyed.Matches(new Vector3(-758.6f, 99f, 104.4f))
            && !keyed.Matches(new Vector3(-750f, 12f, 104.4f)),
            "matches the same spot at any height, not a spot 8 m away");

        sb.AppendLine();
        sb.AppendLine(fails == 0
            ? "PASS — the ledger answers the same however often it is asked"
            : $"{fails} GATE(S) FAILED");
        Report(sb.ToString());
    }

    /// Two hands cutting on a hectare of ground, at a known instant.
    static OutpostLedger Working(double at, float hectares = 1f)
    {
        var l = OutpostLedger.For(Vector3.zero, hectares);
        l.lastTicked = at;
        l.hands.Add(new OutpostHand { name = "Bo", order = OutpostOrder.Cut });
        l.hands.Add(new OutpostHand { name = "Sten", order = OutpostOrder.Cut });
        return l;
    }

    /// Same state, to the resolution anything downstream could notice.
    static bool Same(OutpostLedger a, OutpostLedger b)
        => a.timber == b.timber
        && Mathf.Abs(a.timberPart - b.timberPart) < 1e-4f
        && Mathf.Abs(a.standing - b.standing) < 1e-3f;

    /// `detail` must read as a MEASUREMENT, not as a complaint -- it prints
    /// on a pass as well as a failure, and "[ok] ceiling-holds 10 > 10" is a
    /// line that makes a green run look broken.
    static void Gate(StringBuilder sb, ref int fails, string name, bool ok, string detail)
    {
        if (!ok) fails++;
        sb.AppendLine($"  [{(ok ? "ok  " : "FAIL")}] {name}   {detail}");
    }

    static void Report(string text)
    {
        Debug.Log("LedgerProbe\n" + text);
        var path = System.IO.Path.Combine(Application.dataPath, "../Logs/LedgerProbe.txt");
        try { System.IO.File.WriteAllText(path, text); } catch { }
    }
}
