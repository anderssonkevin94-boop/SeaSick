using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using SeaSick.Ship;
using SeaSick.Voyage;
using SeaSick.World;

/// **Does the ladder actually cost anything?**
///
/// Phase 2 of the island loop is "the sink": until 2026-09-20 the twenty-rung
/// hull ladder and all fifteen fittings were free, so the biggest thing in the
/// game was bought by pressing a button. Kevin's call: **ship rungs are priced
/// in camp-made goods that must be sailed home in the hold.** This gates that
/// the price is real, that it is taken at the pier and nowhere else, that a
/// hold with several kinds in it lands all of them, and that the free dev path
/// every other probe drives the hull through is still free.
///
/// **It presses the real calls.** `Shipyard.Move`, `Shipyard.Upgrade`,
/// `VoyageManager.AddLoot` at sea, and then the real homecoming — cast off,
/// stand out, load, warp back to the berth, `TryComeAlongside`, wait for
/// `AtHome` — exactly as `LoopProbe` does. It never calls `CompleteVoyage` and
/// never sets a phase behind the game's back, because a check that drove the
/// state machine by hand would prove only that the state machine can be driven
/// by hand. The one exception is `Apply`, which is BY DESIGN the free path:
/// it is used to start from a known rung, to give her a hold big enough to
/// carry a test haul, and to put her back where she was found.
///
/// It also prints a PACING TABLE, which is not gated and is the point of the
/// whole run for Kevin: every rung's price against the hold she has at the
/// rung BEFORE it, in round trips. That is where a silly curve shows up.
///
/// Play mode, Sea.unity. Writes `Logs/SinkProbe.txt`.
public class SinkProbe : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("SinkProbe: not in play mode"); return; }
        var old = FindAnyObjectByType<SinkProbe>();
        if (old != null) Destroy(old.gameObject);
        var runner = new GameObject("SinkProbe").AddComponent<SinkProbe>();
        runner.StartCoroutine(runner.Run());
    }

    StringBuilder sb;
    int fails;

    /// Everything a hold or a beach can hold, so the probe can empty the
    /// stores before it measures them. Not `ShipPrices.Priced` — that is only
    /// what the yard charges in, and a beach full of food would still be a
    /// beach that was not empty.
    static readonly string[] AllRes =
    {
        Res.Timber, Res.Stone, Res.Ore, Res.Spice,
        Res.Boards, Res.Tools, Res.Food, Res.Meals,
    };

    IEnumerator Run()
    {
        sb = new StringBuilder();
        fails = 0;
        // Written BEFORE anything is read, so a run that dies in the middle
        // leaves a file that says it died rather than the last run's PASS.
        Report("SinkProbe: did not finish\n");

        // The first segment of a coroutine started from `Start`/`Execute` runs
        // before the frame has been drawn, and half the state this reads is
        // written in Update. Nothing is measured until a whole frame has been.
        yield return new WaitForEndOfFrame();

        var yard = FindFirstObjectByType<Shipyard>();
        var voyage = FindFirstObjectByType<VoyageManager>();
        var motor = FindFirstObjectByType<ShipMotor>();
        var anchor = motor != null ? motor.GetComponent<AnchorController>() : null;
        if (yard == null || voyage == null || motor == null || anchor == null)
        { Fail("no yard / voyage / ship / anchor in the scene"); yield break; }

        // The dock does not exist until the populator has found the island.
        float wait = 0f;
        while (Dock.Home == null && wait < 25f) { wait += Time.deltaTime; yield return null; }
        var dock = Dock.Home;
        if (dock == null) { Fail("no home dock after 25 s"); yield break; }

        int startRung = yard.NodeIndex;
        var startFit = new int[5];
        for (int i = 0; i < 5; i++) startFit[i] = yard.Fit.Level((FitTrack)i);
        bool startedAtHome = voyage.AtHome;
        var startBanked = new Dictionary<string, int>();
        foreach (var r in AllRes) startBanked[r] = voyage.Banked(r);

        sb.AppendLine("SinkProbe — does the ladder cost anything?");
        sb.AppendLine($"found her on rung {startRung}, "
            + $"AtHome {startedAtHome}, home keeps {voyage.StoreCapacity} of each");

        // ================================================================
        // 1. THE TABLE ITSELF. No ship needed — these are facts about the
        //    price list, and a wrong one is wrong before anybody sails.
        // ================================================================
        sb.AppendLine("\n== the price list ==");

        bool everyRung = true, realNames = true;
        string rungGap = "", nameGap = "";
        for (int r = 1; r < ShipLadder.Count; r++)
        {
            var p = ShipPrices.ForRung(r);
            if (!p.Has) { everyRung = false; rungGap += $" rung {r}"; }
            if (!IsRes(p.a)) { realNames = false; nameGap += $" rung {r}:'{p.a}'"; }
            if (p.HasSecond && !IsRes(p.b)) { realNames = false; nameGap += $" rung {r}:'{p.b}'"; }
        }
        Gate("every-rung-has-a-price", everyRung, $"free rungs:{rungGap}");

        bool everyFit = true;
        string fitGap = "";
        for (int t = 0; t < 5; t++)
            for (int lvl = 1; lvl <= ShipFit.MaxLevel; lvl++)
            {
                var p = ShipPrices.ForFit((FitTrack)t, lvl);
                if (!p.Has) { everyFit = false; fitGap += $" {(FitTrack)t} L{lvl}"; }
                if (!IsRes(p.a)) { realNames = false; nameGap += $" {(FitTrack)t}L{lvl}:'{p.a}'"; }
                if (p.HasSecond && !IsRes(p.b)) { realNames = false; nameGap += $" {(FitTrack)t}L{lvl}:'{p.b}'"; }
            }
        Gate("every-fitting-level-has-a-price", everyFit, $"free fittings:{fitGap}");
        Gate("every-price-names-a-real-resource", realNames, nameGap);

        // A price is only reachable if the world has put the resource within
        // sailing range by the time that rung is the next one. `WorldSettings`
        // unlocks by ring (Stone 0.30, Ore 0.55, Spice 0.78 of 3000 m) and a
        // sawmill/forge stands on what is gathered, so the bands in
        // `ShipPrices` have to hold or a rung is a wall, not a price.
        int firstBoards = FirstRungUsing(Res.Boards);
        int firstTools = FirstRungUsing(Res.Tools);
        int firstSpice = FirstRungUsing(Res.Spice);
        Gate("boards-are-not-asked-for-before-rung-7", firstBoards >= 7,
             $"first asked at rung {firstBoards}");
        Gate("tools-are-not-asked-for-before-rung-12", firstTools >= 12,
             $"first asked at rung {firstTools}");
        Gate("spice-is-not-asked-for-before-rung-17", firstSpice >= 17,
             $"first asked at rung {firstSpice}");

        // ================================================================
        // 2. AT SEA THE YARD IS SHUT.
        // ================================================================
        sb.AppendLine("\n== at sea ==");

        yard.Apply(0);                    // a known rung, by the free dev path
        yield return null;
        Drain(voyage);
        if (voyage.AtHome) voyage.BeginVoyage();
        anchor.CastOff();
        yield return null;
        Warp(motor, dock.Berth + dock.Seaward * 400f,
             Quaternion.LookRotation(dock.Seaward));
        for (int i = 0; i < 6; i++) yield return null;

        bool moveAtSea = yard.Move("lengthen");
        string seaWhy = yard.Status;
        sb.AppendLine($"  {dock.DistanceFrom(motor.transform.position):F0} m off the berth, "
            + $"AtHome {voyage.AtHome}   Move -> {moveAtSea} \"{seaWhy}\"");
        Gate("at-sea-the-yard-refuses-and-says-why",
             !moveAtSea && yard.NodeIndex == 0 && Has(seaWhy, "pier"),
             $"returned {moveAtSea}, rung {yard.NodeIndex}, said \"{seaWhy}\"");

        // ================================================================
        // 3. HOME AND BROKE: the refusal has to name the shortfall.
        // ================================================================
        yield return Alongside(motor, anchor, voyage, dock);
        sb.AppendLine("\n== home, with nothing in store ==");
        Drain(voyage);
        yield return null;

        bool moveBroke = yard.Move("lengthen");
        string brokeWhy = yard.Status;
        var rung1 = ShipPrices.ForRung(1);
        sb.AppendLine($"  AtHome {voyage.AtHome}, timber {voyage.Banked(Res.Timber)}   "
            + $"rung 1 wants {rung1}   Move -> {moveBroke} \"{brokeWhy}\"");
        Gate("broke-at-home-the-refusal-names-the-shortfall",
             !moveBroke && yard.NodeIndex == 0
             && Has(brokeWhy, "timber") && Has(brokeWhy, "home has 0"),
             $"returned {moveBroke}, said \"{brokeWhy}\"");

        // ================================================================
        // 4. A MIXED HAUL. The bug this replaces: ONE pool of room shared
        //    across every kind, walked in dictionary order, so whichever
        //    resource enumerated first silently ate the space.
        // ================================================================
        sb.AppendLine("\n== a mixed haul comes home ==");

        int keeps = voyage.StoreCapacity;
        int wantTimber = keeps + 9;       // deliberately more than home keeps
        const int WantBoards = 12, WantTools = 8;
        int wantAll = wantTimber + WantBoards + WantTools;

        // She needs a hold before she can carry a test haul: rung 0 has two
        // bays. `Apply` and `AddCell` are the free dev path, and the haul
        // still comes home through the real homecoming below.
        voyage.BeginVoyage();
        yard.Apply(15);
        yield return null;
        int guard = 0;
        while (voyage.MaxHold < wantAll + 4 && guard++ < 60)
            if (!yard.AddCell(BayUse.Hold)) break;
        yield return null;

        anchor.CastOff();
        Warp(motor, dock.Berth + dock.Seaward * 400f,
             Quaternion.LookRotation(dock.Seaward));
        for (int i = 0; i < 6; i++) yield return null;

        voyage.TakeDeckCargo = true;
        voyage.AddLoot(wantTimber, Res.Timber);
        voyage.AddLoot(WantBoards, Res.Boards);
        voyage.AddLoot(WantTools, Res.Tools);
        int gotTimber = voyage.AmountOf(Res.Timber);
        int gotBoards = voyage.AmountOf(Res.Boards);
        int gotTools = voyage.AmountOf(Res.Tools);
        sb.AppendLine($"  hold {voyage.TotalHeld} of {voyage.MaxHold} "
            + $"(line {voyage.HoldCapacity}): timber {gotTimber}, "
            + $"boards {gotBoards}, tools {gotTools}");

        var pile = Stockpile.Instance;
        int pileTimberWas = Count(pile, Res.Timber);
        int pileBoardsWas = Count(pile, Res.Boards);
        int pileToolsWas = Count(pile, Res.Tools);

        yield return Alongside(motor, anchor, voyage, dock);

        int bankTimber = voyage.Banked(Res.Timber);
        int bankBoards = voyage.Banked(Res.Boards);
        int bankTools = voyage.Banked(Res.Tools);
        sb.AppendLine($"  landed: timber {bankTimber}/{keeps}, boards {bankBoards}/{keeps}, "
            + $"tools {bankTools}/{keeps}   spoiled {voyage.Spoiled} "
            + $"\"{voyage.SpoiledDetail}\"");

        Gate("boards-are-not-squeezed-out-by-timber", bankBoards == gotBoards,
             $"carried {gotBoards} boards, landed {bankBoards}");
        Gate("tools-are-not-squeezed-out-by-timber", bankTools == gotTools,
             $"carried {gotTools} tools, landed {bankTools}");
        Gate("timber-fills-its-own-pile-and-stops", bankTimber == Mathf.Min(gotTimber, keeps),
             $"carried {gotTimber}, home keeps {keeps}, landed {bankTimber}");
        Gate("only-the-timber-spoiled",
             voyage.Spoiled == Mathf.Max(0, gotTimber - keeps)
             && Has(voyage.SpoiledDetail, "timber")
             && !Has(voyage.SpoiledDetail, "boards")
             && !Has(voyage.SpoiledDetail, "tools"),
             $"spoiled {voyage.Spoiled} \"{voyage.SpoiledDetail}\", "
             + $"want {Mathf.Max(0, gotTimber - keeps)} of timber alone");

        // The carry-ashore is a coroutine, one unit every 0.18 s, so the beach
        // is behind the ledger until it finishes. Counting it early is how a
        // green run reports an empty beach.
        yield return new WaitForSeconds(voyage.BankedTotal * 0.18f + 0.8f);
        int dTimber = Count(pile, Res.Timber) - pileTimberWas;
        int dBoards = Count(pile, Res.Boards) - pileBoardsWas;
        int dTools = Count(pile, Res.Tools) - pileToolsWas;
        sb.AppendLine($"  on the beach: +{dTimber} timber, +{dBoards} boards, +{dTools} tools");
        Gate("the-beach-agrees-with-the-ledger",
             pile != null && dTimber == bankTimber && dBoards == bankBoards
             && dTools == bankTools,
             $"beach +{dTimber}/+{dBoards}/+{dTools} vs ledger "
             + $"{bankTimber}/{bankBoards}/{bankTools}");

        // ================================================================
        // 5. EXACTLY THE PRICE BUYS THE RUNG.
        // ================================================================
        sb.AppendLine("\n== buying rung 1 ==");
        yard.Apply(0);                    // free dev path, back to the start
        yield return null;
        Trim(voyage, rung1.a, rung1.na);
        yield return null;

        int purseWas = voyage.Banked(rung1.a);
        int pileWas = Count(pile, rung1.a);
        bool bought = yard.Move("lengthen");
        yield return null;
        int purseNow = voyage.Banked(rung1.a);
        int pileNow = Count(pile, rung1.a);
        sb.AppendLine($"  rung 1 wants {rung1}; had {purseWas}   Move -> {bought}   "
            + $"rung {yard.NodeIndex}   store {purseWas} -> {purseNow}   "
            + $"beach {pileWas} -> {pileNow}");
        Gate("exactly-the-price-buys-the-rung", bought && yard.NodeIndex == 1,
             $"returned {bought}, rung {yard.NodeIndex}, said \"{yard.Status}\"");
        Gate("the-rung-took-exactly-its-price", purseWas - purseNow == rung1.na,
             $"store fell by {purseWas - purseNow}, price is {rung1.na}");
        Gate("and-the-visible-pile-came-down-with-it",
             pile == null || pileWas - pileNow == rung1.na,
             $"beach fell by {pileWas - pileNow}, price is {rung1.na}");

        // ================================================================
        // 6. AND EXACTLY THE PRICE BUYS THE FITTING. One more real voyage,
        //    because the only way to put something in the stores is to sail
        //    it home — which is the whole design.
        // ================================================================
        sb.AppendLine("\n== buying a fitting ==");
        var rudder = ShipPrices.ForFit(FitTrack.Rudder, 1);

        voyage.BeginVoyage();
        guard = 0;
        while (voyage.MaxHold < rudder.na + 2 && guard++ < 30)
            if (!yard.AddCell(BayUse.Hold)) break;
        anchor.CastOff();
        Warp(motor, dock.Berth + dock.Seaward * 400f,
             Quaternion.LookRotation(dock.Seaward));
        for (int i = 0; i < 6; i++) yield return null;
        voyage.TakeDeckCargo = true;
        voyage.AddLoot(rudder.na, rudder.a);
        sb.AppendLine($"  carrying {voyage.AmountOf(rudder.a)} {rudder.a.ToLowerInvariant()} "
            + $"home in a hold of {voyage.MaxHold}");
        yield return Alongside(motor, anchor, voyage, dock);
        Trim(voyage, rudder.a, rudder.na);
        yield return new WaitForSeconds(voyage.BankedTotal * 0.18f + 0.6f);

        int fitPurseWas = voyage.Banked(rudder.a);
        int fitPileWas = Count(pile, rudder.a);
        int lvlWas = yard.Fit.Level(FitTrack.Rudder);
        bool fitted = yard.Upgrade(FitTrack.Rudder);
        yield return null;
        int fitPurseNow = voyage.Banked(rudder.a);
        int fitPileNow = Count(pile, rudder.a);
        sb.AppendLine($"  rudder L{lvlWas + 1} wants {rudder}; had {fitPurseWas}   "
            + $"Upgrade -> {fitted}   level {lvlWas} -> {yard.Fit.Level(FitTrack.Rudder)}   "
            + $"store {fitPurseWas} -> {fitPurseNow}   beach {fitPileWas} -> {fitPileNow}");
        Gate("exactly-the-price-buys-the-fitting",
             fitted && yard.Fit.Level(FitTrack.Rudder) == lvlWas + 1,
             $"returned {fitted}, level {yard.Fit.Level(FitTrack.Rudder)}, "
             + $"said \"{yard.Status}\"");
        Gate("the-fitting-took-exactly-its-price",
             fitPurseWas - fitPurseNow == rudder.na,
             $"store fell by {fitPurseWas - fitPurseNow}, price is {rudder.na}");
        Gate("and-the-fitting-took-it-off-the-beach-too",
             pile == null || fitPileWas - fitPileNow == rudder.na,
             $"beach fell by {fitPileWas - fitPileNow}, price is {rudder.na}");

        // ================================================================
        // 7. THE DEV PATH IS STILL FREE. This is the gate that protects every
        //    other probe in `Scripts/Dev`: they all walk the ladder with
        //    `Apply`, and a charged `Apply` would make each of them a test of
        //    the stores instead of a test of the ship.
        // ================================================================
        sb.AppendLine("\n== the dev path ==");
        var before = new Dictionary<string, int>();
        foreach (var r in AllRes) before[r] = voyage.Banked(r);
        int rungBefore = yard.NodeIndex;
        yard.Apply(9);
        yield return null;
        yard.Undo();
        yield return null;
        yard.Apply(rungBefore);
        yield return null;
        bool freeStill = true;
        string moved = "";
        foreach (var r in AllRes)
            if (voyage.Banked(r) != before[r])
            { freeStill = false; moved += $" {r} {before[r]}->{voyage.Banked(r)}"; }
        sb.AppendLine($"  Apply(9), Undo(), Apply({rungBefore}) — stores moved:{(moved.Length > 0 ? moved : " nothing")}");
        Gate("apply-and-undo-cost-nothing", freeStill, moved);

        // ================================================================
        // 8. THE PACING TABLE. Not gated — this is the thing to READ.
        // ================================================================
        Pacing(voyage);

        // ================================================================
        // 9. Put her back as far as anything can.
        // ================================================================
        yard.Apply(startRung);
        for (int i = 0; i < 5; i++) yard.Fit.SetLevel((FitTrack)i, startFit[i]);
        yard.PushToGame();
        yield return null;
        if (!startedAtHome && voyage.AtHome) voyage.BeginVoyage();

        sb.AppendLine("\n== restored ==");
        sb.AppendLine($"  rung {yard.NodeIndex} (was {startRung})   "
            + $"AtHome {voyage.AtHome} (was {startedAtHome})");
        // The stores are NOT restorable: there is no way to put something into
        // them but to sail it home, which is the design. Said out loud so the
        // next probe in the session knows what it inherited.
        var spent = new StringBuilder();
        foreach (var r in AllRes)
            if (voyage.Banked(r) != startBanked[r])
                spent.Append($" {r.ToLowerInvariant()} {startBanked[r]}->{voyage.Banked(r)}");
        sb.AppendLine(spent.Length > 0
            ? $"  stores NOT restored (nothing can bank but a voyage):{spent}"
            : "  stores unchanged");

        Finish();
    }

    // --- the pacing table ---------------------------------------------------

    /// **Every rung's price against the hold she has at the rung BEFORE it.**
    ///
    /// The hold is quoted as what the hull COULD carry with every bay given to
    /// cargo (`cells × Shipyard.CargoPerHold`) rather than what she happens to
    /// be carrying, because the question is "how many voyages is this rung",
    /// and a player saving for a rung gives her bays to cargo. The overload
    /// multiplier is read off the live voyage manager rather than restated, so
    /// it cannot drift from `overloadLimit`.
    void Pacing(VoyageManager voyage)
    {
        float overload = voyage.HoldCapacity > 0
            ? voyage.MaxHold / (float)voyage.HoldCapacity : 1.6f;
        sb.AppendLine("\n== PACING — price vs the hold of the rung before it ==");
        sb.AppendLine($"   (hold = every bay given to cargo; stuffed = ×{overload:F2} deck cargo)");
        sb.AppendLine("rung  hull                price                        units  hold  stuff  trips  stuffed");
        for (int r = 1; r < ShipLadder.Count; r++)
        {
            var node = ShipLadder.Node(r);
            var from = ShipLadder.Node(r - 1);
            var p = ShipPrices.ForRung(r);
            int hold = from != null ? from.cells * Shipyard.CargoPerHold : 0;
            int stuffed = Mathf.RoundToInt(hold * overload);
            int units = p.Units;
            string trips = hold > 0 ? Mathf.CeilToInt(units / (float)hold).ToString() : "—";
            string stuffTrips = stuffed > 0 ? Mathf.CeilToInt(units / (float)stuffed).ToString() : "—";
            sb.AppendLine($"{r,4}  {(node != null ? node.label : "?"),-18}  "
                + $"{p,-26}  {units,5}  {hold,4}  {stuffed,5}  {trips,5}  {stuffTrips,7}");
        }
        sb.AppendLine("fittings: " + $"L1 {ShipPrices.ForFit(FitTrack.Rudder, 1)}   "
            + $"L2 {ShipPrices.ForFit(FitTrack.Rudder, 2)}   "
            + $"L3 {ShipPrices.ForFit(FitTrack.Rudder, 3)}   (same on every track, for now)");
    }

    // --- the moves a voyage is made of --------------------------------------

    /// Warp her onto her berth, take a line, and wait for the VOYAGE to close
    /// on its own. Lifted from `LoopProbe`: the rule is move the SHIP and let
    /// the real systems run.
    IEnumerator Alongside(ShipMotor motor, AnchorController anchor,
                          VoyageManager voyage, Dock dock)
    {
        Warp(motor, dock.Berth, dock.Heading);
        for (int i = 0; i < 4; i++) yield return null;
        anchor.TryComeAlongside();
        float t = 0f;
        while (!voyage.AtHome && t < 6f) { t += Time.deltaTime; yield return null; }
        if (!voyage.AtHome) sb.AppendLine("  ! she never tied up — the rest proves nothing");
    }

    static void Warp(ShipMotor motor, Vector3 to, Quaternion facing)
    {
        to.y = motor.transform.position.y;
        var rb = motor.GetComponent<Rigidbody>();
        motor.transform.SetPositionAndRotation(to, facing);
        if (rb != null)
        {
            rb.position = to;
            rb.rotation = facing;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        motor.AnchorPoint = to;
    }

    // --- small helpers -------------------------------------------------------

    /// Empty the stores through the same call the yard and the build buttons
    /// spend with, so the visible pile comes down with the number.
    static void Drain(VoyageManager v)
    {
        foreach (var r in AllRes)
        {
            int n = v.Banked(r);
            if (n > 0) v.SpendBanked(r, n);
        }
    }

    /// Leave EXACTLY `n` of a resource in the stores.
    static void Trim(VoyageManager v, string res, int n)
    {
        int over = v.Banked(res) - n;
        if (over > 0) v.SpendBanked(res, over);
    }

    static int Count(Stockpile pile, string res) => pile != null ? pile.CountOf(res) : 0;

    static bool IsRes(string r)
    {
        if (string.IsNullOrEmpty(r)) return true;      // an absent half is fine
        foreach (var k in AllRes) if (k == r) return true;
        return false;
    }

    static int FirstRungUsing(string res)
    {
        for (int r = 1; r < ShipLadder.Count; r++)
        {
            var p = ShipPrices.ForRung(r);
            if (p.a == res || (p.HasSecond && p.b == res)) return r;
        }
        return int.MaxValue;
    }

    static bool Has(string s, string bit) =>
        !string.IsNullOrEmpty(s) && s.IndexOf(bit, System.StringComparison.OrdinalIgnoreCase) >= 0;

    void Gate(string name, bool ok, string detail)
    {
        if (!ok) fails++;
        sb.AppendLine($"  [{(ok ? "ok  " : "FAIL")}] {name}   {(ok ? "" : detail)}");
    }

    void Fail(string what)
    {
        Report("SinkProbe ABORT: " + what + "\n");
        Destroy(gameObject);
    }

    void Finish()
    {
        sb.AppendLine();
        sb.AppendLine(fails == 0 ? "PASS" : $"FAIL: {fails} gate(s)");
        Report(sb.ToString());
        Destroy(gameObject);
    }

    static void Report(string text)
    {
        Debug.Log("SinkProbe\n" + text);
        var path = System.IO.Path.Combine(Application.dataPath, "../Logs/SinkProbe.txt");
        try { System.IO.File.WriteAllText(path, text); } catch { }
    }
}
