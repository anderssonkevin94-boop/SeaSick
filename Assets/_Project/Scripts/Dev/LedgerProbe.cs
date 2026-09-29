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

        float day = SeaSick.World.TimeOfDay.WorkDaySeconds;
        sb.AppendLine($"day = {day:F0} s, quantum = {OutpostLedger.QuantumDays:F2} day "
            + $"({OutpostLedger.QuantumDays * day:F1} s)");
        // Gathering is TRIPS since 2026-09-23 (walk out, 5 s a log, walk
        // back): the rate is an armful per trip, from the default 25 m.
        sb.AppendLine($"rates: {new OutpostLedger().GatherTripPerDay(Res.Timber):F1} logs/hand/day by trips "
            + $"({OutpostLedger.DefaultSourceMetres:F0} m out, {Playtest.CutSecondsPerLog:F0} s a log), "
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
        sb.AppendLine($"  one call      timber {once.Timber,3}  standing {once.Wood.standing,7:F2}  part {once.TimberPart():F4}");
        sb.AppendLine($"  480 calls     timber {many.Timber,3}  standing {many.Wood.standing,7:F2}  part {many.TimberPart():F4}");
        sb.AppendLine($"  5 ragged      timber {lumpy.Timber,3}  standing {lumpy.Wood.standing,7:F2}  part {lumpy.TimberPart():F4}");

        Gate(sb, ref fails, "one-call-equals-many", Same(once, many),
            $"{once.Timber}/{once.Wood.standing:F3} vs {many.Timber}/{many.Wood.standing:F3}");
        Gate(sb, ref fails, "one-call-equals-ragged", Same(once, lumpy),
            $"{once.Timber}/{once.Wood.standing:F3} vs {lumpy.Timber}/{lumpy.Wood.standing:F3}");

        // --- 2. idempotence --------------------------------------------------

        var idem = Working(t0);
        idem.Tick(t0 + 5.0 * day);
        int afterFirst = idem.Timber;
        double stampAfterFirst = idem.lastTicked;
        for (int i = 0; i < 20; i++) idem.Tick(t0 + 5.0 * day);

        sb.AppendLine();
        sb.AppendLine($"IDEMPOTENCE: {afterFirst} logs, then 20 more calls at the same instant "
            + $"-> {idem.Timber} logs");
        Gate(sb, ref fails, "ticking-twice-does-nothing",
            idem.Timber == afterFirst && System.Math.Abs(idem.lastTicked - stampAfterFirst) < 1e-9,
            $"{afterFirst} -> {idem.Timber}");

        // --- 3. the clock running backwards ----------------------------------
        //
        // A dev tool can scrub TimeOfDay. A ledger that banked negative time
        // would pay it back later as a burst of free timber.

        var back = Working(t0);
        back.Tick(t0 + 4.0 * day);
        int beforeScrub = back.Timber;
        back.Tick(t0);                      // the clock jumps backwards
        back.Tick(t0 + 0.5 * day);
        sb.AppendLine();
        sb.AppendLine($"CLOCK SCRUBBED BACK: {beforeScrub} logs, wound back, half a day on "
            + $"-> {back.Timber} logs");
        Gate(sb, ref fails, "no-free-timber-from-a-scrub",
            back.Timber <= beforeScrub + 2, $"{beforeScrub} -> {back.Timber}");

        // --- 4. the ceiling is the whole design ------------------------------

        var full = Working(t0);
        full.Tick(t0 + 400.0 * day);
        sb.AppendLine();
        sb.AppendLine($"CEILING after 400 days: timber {full.Timber} against a ceiling of {full.ceilingPer}");
        Gate(sb, ref fails, "ceiling-holds", full.Timber <= full.ceilingPer,
            $"{full.Timber} logs, ceiling {full.ceilingPer}");
        Gate(sb, ref fails, "ceiling-is-reached", full.Timber == full.ceilingPer,
            $"{full.Timber} of {full.ceilingPer} after 400 days");

        // --- 5. depletion has to actually bite -------------------------------
        //
        // Kevin chose a stock that depletes. If the numbers make it
        // unreachable then "depletes and regrows" is a claim, not a mechanic.

        // **Measured per quantum since 2026-09-23.** Gathering is trips now
        // (Kevin: walk out, 5 s a log, walk back), so two hands take the
        // 20 logs of half a hectare in under a day, not the old "3+ days at
        // 4 logs a hand a day" -- a whole-day loop can no longer tell
        // "instantly" from "as fast as the trips allow". The bite is still
        // tested (the wood runs out); "not instantly" is now "no faster than
        // the trips": each hand takes an armful a trip, so the last armful
        // cannot leave the ground before (rounds - 1) whole trips have run.
        var strip = Working(t0, hectares: 0.5f);
        float startStanding = strip.Wood.standing;
        float daysToStrip = 0f;
        for (int q = 1; q <= 4000; q++)
        {
            strip.ceilingPer = 100000;         // take the ceiling out of the question
            strip.Tick(t0 + q * (double)OutpostLedger.QuantumDays * day);
            if (strip.Wood.standing < 1f) { daysToStrip = q * OutpostLedger.QuantumDays; break; }
        }
        int armful = Res.Armful(Res.Timber);
        int rounds = Mathf.CeilToInt(startStanding / (strip.hands.Count * armful));
        float fastest = (rounds - 1) * strip.TripDays(Res.Timber, armful, HaulPlace.Field, -1, HaulPlace.Store, -1);
        sb.AppendLine();
        sb.AppendLine($"DEPLETION on half a hectare ({startStanding:F0} logs standing), "
            + $"ceiling lifted: stripped in {daysToStrip:F1} game days "
            + $"({daysToStrip * day / 60f:F1} real minutes at the current day length)");
        Gate(sb, ref fails, "the-wood-can-run-out", daysToStrip > 0f,
            daysToStrip > 0f ? $"stripped on day {daysToStrip:F1}"
                             : $"still {strip.Wood.standing:F1} standing after 400 days");
        Gate(sb, ref fails, "but-not-instantly", daysToStrip == 0f || daysToStrip >= fastest - 1e-3f,
            $"stripped in {daysToStrip:F2} days; {rounds} rounds of 2-log trips can do it no faster than {fastest:F2}");

        // --- 6. and it has to come back --------------------------------------

        var regrow = new OutpostLedger { lastTicked = t0 };
        var regrowWood = regrow.Wood;
        regrowWood.standingMax = 100f;
        regrowWood.standing = 0f;
        regrowWood.regrowPerDay = OutpostLedger.RegrowthPerDay;
        regrow.Tick(t0 + 30.0 * day);
        sb.AppendLine();
        sb.AppendLine($"REGROWTH from bare: {regrow.Wood.standing:F1} of {regrow.Wood.standingMax:F0} "
            + $"after 30 game days");
        Gate(sb, ref fails, "the-wood-comes-back", regrow.Wood.standing > 20f,
            $"{regrow.Wood.standing:F1} after 30 days");

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
            $"restored {after.Timber}/{after.Wood.standing:F2} vs control {control.Timber}/{control.Wood.standing:F2}");

        // --- 8. identity is positional ---------------------------------------

        var keyed = new OutpostLedger();
        keyed.SetKey(new Vector3(-758.6f, 12f, 104.4f));
        sb.AppendLine();
        sb.AppendLine($"KEY: (-758.6, 104.4) -> {keyed.keyX},{keyed.keyZ}");
        Gate(sb, ref fails, "key-is-position-not-index",
            keyed.Matches(new Vector3(-758.6f, 99f, 104.4f))
            && !keyed.Matches(new Vector3(-750f, 12f, 104.4f)),
            "matches the same spot at any height, not a spot 8 m away");

        // --- 9. the blueprint ------------------------------------------------
        //
        // A camp is SITED before it is built now, and the wood that builds it
        // is cut by the hands left behind. That work is arithmetic for exactly
        // the same reason the felling is: a blueprint must go on being built
        // while its island is unloaded, or the player has to sit and watch a
        // fire being laid.

        double bspan = 3.0 * day;
        var b1 = Building(t0);
        b1.Tick(t0 + bspan);

        var b2 = Building(t0);
        for (int i = 1; i <= 240; i++) b2.Tick(t0 + bspan * i / 240.0);

        var b3 = Building(t0);
        b3.Tick(t0 + bspan * 0.07);
        b3.Tick(t0 + bspan * 0.41);
        b3.Tick(t0 + bspan * 0.42);
        b3.Tick(t0 + bspan);

        sb.AppendLine();
        sb.AppendLine($"BLUEPRINT ({BuildPlans.Campfire.cost} logs, 1 hand building) "
            + "over 3 game days:");
        sb.AppendLine($"  one call      done {b1.Pending.done}  part {b1.Pending.donePart:F4}  standing {b1.Wood.standing:F2}");
        sb.AppendLine($"  240 calls     done {b2.Pending.done}  part {b2.Pending.donePart:F4}  standing {b2.Wood.standing:F2}");
        sb.AppendLine($"  4 ragged      done {b3.Pending.done}  part {b3.Pending.donePart:F4}  standing {b3.Wood.standing:F2}");

        Gate(sb, ref fails, "build-is-path-independent",
            SameBuild(b1, b2) && SameBuild(b1, b3),
            $"{b1.Pending.done}/{b1.Wood.standing:F3} vs {b2.Pending.done}/{b2.Wood.standing:F3} "
            + $"vs {b3.Pending.done}/{b3.Wood.standing:F3}");

        Gate(sb, ref fails, "a-finished-build-stops-eating-wood",
            b1.Pending.Complete && b1.Pending.done == BuildPlans.Campfire.cost,
            $"{b1.Pending.done} of {BuildPlans.Campfire.cost} logs, no overshoot");

        // How long one hand takes, which is the number the cost was chosen
        // to express: four logs at four logs a day is one day's work.
        var timed = Building(t0);
        int steps = 0;
        while (!timed.Pending.Complete && steps < 400)
        {
            steps++;
            timed.Tick(t0 + steps * OutpostLedger.QuantumDays * day);
        }
        float daysTaken = steps * OutpostLedger.QuantumDays;
        sb.AppendLine($"  one hand finishes it in {daysTaken:F1} game days "
            + $"({daysTaken * day:F0} s at the current day length)");
        // **Stock, THEN build (7820c7e, 2026-09-23).** Standing the fire up
        // is a second phase of `LabourFor` hand-days on top of the wood.
        // **Trip-timed wood (2026-09-23, Kevin: trips are the walk there and
        // back, a log is 5 s to cut):** the wood phase is no longer "4 logs
        // at 4 a day = 1 day" but the trips it takes -- one hand, armfuls of
        // `Res.Armful(Timber)`, each `TripDays` (walk to the nearest standing
        // tree and back + cutting + handling). This ledger was never
        // watched, so the tree is `DefaultSourceMetres` out.
        float woodDays = 0f;
        for (int left = BuildPlans.Campfire.cost; left > 0; )
        {
            int n = Mathf.Min(Res.Armful(Res.Timber), left);
            woodDays += timed.TripDays(Res.Timber, n, HaulPlace.Field, -1, HaulPlace.Site, -1);
            left -= n;
        }
        float labourDays = OutpostLedger.LabourFor(timed.Pending);
        float expectDays = woodDays + labourDays;
        Gate(sb, ref fails, "one-hand-one-day", Mathf.Abs(daysTaken - expectDays) < 0.15f,
            $"{daysTaken:F2} days against {woodDays:F2} of wood trips + {labourDays:F2} of building = {expectDays:F2}");

        // Builders do not stockpile. Until the fire is lit there is nothing to
        // stockpile INTO — in the game the ceiling is zero before the first
        // building — and a builder who also filled a pile would be paid twice.
        Gate(sb, ref fails, "building-does-not-also-fill-the-pile",
            b1.Timber == 0 && b1.TimberPart() < 1e-4f,
            $"pile {b1.Timber} logs while building");

        // The wood comes off the island. A camp sited on bare rock cannot
        // build itself out of nothing.
        //
        // **`standingMax` has to go to zero too, and the first version of this
        // gate did not do it.** Zeroing the stock alone leaves the regrowth
        // term running against a 40-log maximum, so ten game days quietly grew
        // the four logs back and the build finished -- correctly. The gate
        // failed on ground that was only bare for the first quantum, which is
        // not what it says it is measuring.
        var bare = Building(t0);
        bare.Wood.standing = 0f;
        bare.Wood.standingMax = 0f;
        bare.Tick(t0 + 10.0 * day);
        Gate(sb, ref fails, "a-build-draws-on-standing-timber",
            bare.Pending.done == 0 && !bare.Pending.Complete,
            $"{bare.Pending.done} logs delivered on ground with nothing standing");

        // And the other half of the same fact: on ground that was stripped but
        // is growing back, a day's building delivers what GREW, not what a
        // hand could have cut. This is the term that makes a worked-out island
        // worth leaving and coming back to rather than worth abandoning.
        var thin = Building(t0);
        thin.Wood.standing = 0f;
        thin.Tick(t0 + 1.0 * day);
        float grew = OutpostLedger.StandingPerHectare * OutpostLedger.RegrowthPerDay;
        Gate(sb, ref fails, "a-stripped-camp-builds-at-the-rate-it-regrows",
            thin.Pending.done + thin.Pending.donePart <= grew + 0.05f,
            $"{thin.Pending.done + thin.Pending.donePart:F2} logs in a day "
            + $"against {grew:F2} regrown, not the {thin.GatherTripPerDay(Res.Timber):F0} a hand can cut by trips");

        // And it survives a save half built, which is the whole reason the
        // blueprint is a row and not a GameObject.
        var half = Building(t0);
        half.Tick(t0 + 0.5 * day);
        var reloaded = JsonUtility.FromJson<OutpostLedger>(JsonUtility.ToJson(half));
        Gate(sb, ref fails, "a-half-built-blueprint-round-trips",
            reloaded != null && reloaded.Pending != null
            && reloaded.Pending.planId == half.Pending.planId
            && reloaded.Pending.done == half.Pending.done
            && Mathf.Abs(reloaded.Pending.x - half.Pending.x) < 1e-3f,
            $"{half.Pending.done}/{half.Pending.needed} logs at "
            + $"({half.Pending.x:F1}, {half.Pending.z:F1}) restored intact");

        // --- 10. many resources, and a ceiling for each ----------------------
        //
        // Kevin, 2026-09-19: *"crew on the island can gather resources up to
        // 10 of each without a storage unit."* So two hands after two
        // different things do not compete for the same ten slots, and neither
        // starves the other's stock.

        // **Three hectares, not one, and that is not a fudge.** A hectare of
        // ore is `Res.PerHectare(Ore)` = 9 units and ore does not regrow, so
        // on one hectare the ISLAND is the limit and the ceiling is never
        // reached -- which is true, interesting, and not what this gate is
        // about. Give it enough ground that the ceiling is the binding
        // constraint, and test the other thing separately below.
        var two = new OutpostLedger { lastTicked = t0 };
        two.SeedStock(Res.Timber, 3f);
        two.SeedStock(Res.Ore, 3f);
        two.hands.Add(new OutpostHand { name = "Bo",   order = OutpostOrder.Gather, target = Res.Timber });
        two.hands.Add(new OutpostHand { name = "Sten", order = OutpostOrder.Gather, target = Res.Ore });
        two.Tick(t0 + 40.0 * day);

        sb.AppendLine();
        sb.AppendLine($"TWO RESOURCES, 40 days, one hand on each, ceiling {two.ceilingPer} of each:");
        sb.AppendLine($"  timber {two.CountOf(Res.Timber)}   ore {two.CountOf(Res.Ore)}   "
            + $"total on the ground {two.Total}");
        Gate(sb, ref fails, "each-resource-has-its-own-ceiling",
            two.CountOf(Res.Timber) == two.ceilingPer && two.CountOf(Res.Ore) == two.ceilingPer,
            $"{two.CountOf(Res.Timber)} timber and {two.CountOf(Res.Ore)} ore, "
            + $"{two.ceilingPer} allowed of each");
        Gate(sb, ref fails, "a-full-pile-does-not-block-another",
            two.Total == two.ceilingPer * 2,
            $"{two.Total} on the ground against {two.ceilingPer * 2} the two ceilings allow");

        // Ore does not grow back. That is what makes a mining island a thing
        // you use up rather than a thing you farm.
        var oreStock = two.Stock(Res.Ore);
        Gate(sb, ref fails, "rock-does-not-regrow",
            oreStock != null && oreStock.standing < oreStock.standingMax - 0.5f,
            $"{oreStock?.standing:F1} of {oreStock?.standingMax:F1} ore left after 40 days");

        // And the other half of that: on a THIN seam the island runs out
        // first, and the pile stops below the ceiling for ever. One hectare
        // holds 9 units of ore against a ceiling of 10, so a camp there can
        // never fill its own store hut -- which is the honest consequence of
        // a stock that does not come back.
        var thinSeam = new OutpostLedger { lastTicked = t0 };
        thinSeam.SeedStock(Res.Ore, 1f);
        thinSeam.hands.Add(new OutpostHand { name = "Bo", order = OutpostOrder.Gather, target = Res.Ore });
        thinSeam.Tick(t0 + 200.0 * day);
        sb.AppendLine($"  a ONE-hectare seam, worked for 200 days: "
            + $"{thinSeam.CountOf(Res.Ore)} ore of a possible {thinSeam.ceilingPer}, "
            + $"{thinSeam.Stock(Res.Ore).standing:F1} left in the ground");
        Gate(sb, ref fails, "a-worked-out-seam-stops-below-the-ceiling",
            thinSeam.CountOf(Res.Ore) < thinSeam.ceilingPer
            && thinSeam.Stock(Res.Ore).standing < 1f,
            $"{thinSeam.CountOf(Res.Ore)} of {thinSeam.ceilingPer}, "
            + $"the island held {Res.PerHectare(Res.Ore):F0}");

        // --- 11. a position turns one thing into another ---------------------

        var mill = new OutpostLedger { lastTicked = t0, ceilingPer = 30 };
        mill.SeedStock(Res.Timber, 4f);
        mill.built.Add(BuildPlans.Sawmill.id);
        mill.hands.Add(new OutpostHand { name = "Bo",   order = OutpostOrder.Gather, target = Res.Timber });
        mill.hands.Add(new OutpostHand { name = "Sten", order = OutpostOrder.Work,   target = BuildPlans.Sawmill.id });
        // A station makes nothing without a player order (2026-09-23,
        // storage-hub rules) -- `stationsMigrated = true` so this exercises
        // that explicit path rather than the old-save free-order migration.
        mill.stationsMigrated = true;
        mill.PlaceOrder(BuildPlans.Sawmill.id, "boards", OutpostLedger.RepeatOrder);
        mill.Tick(t0 + 6.0 * day);

        sb.AppendLine();
        sb.AppendLine($"A SAWYER, 6 days, one hand cutting and one at the mill "
            + $"({SeaSick.World.Economy.Recipes.Named("boards").ratePerDay:F0} boards/day):");
        sb.AppendLine($"  timber {mill.CountOf(Res.Timber)}   boards {mill.CountOf(Res.Boards)}");
        Gate(sb, ref fails, "a-sawyer-makes-boards", mill.CountOf(Res.Boards) > 0,
            $"{mill.CountOf(Res.Boards)} boards after 6 days");
        // **Measured against what was CUT, not a rate (2026-09-23).** The
        // old bound was "less than two hands at 4 logs a day"; gathering is
        // trips now (~10 logs a hand a day), which made that bound loose
        // enough to pass with nothing sawn. `timberTaken` is every log that
        // left the ground, so timber still held anywhere (store, bays, arms)
        // below it is timber the saw ate.
        int timberHeld = mill.CountOf(Res.Timber) + mill.CarriedOf(Res.Timber);
        Gate(sb, ref fails, "and-eats-the-timber-to-do-it",
            mill.CountOf(Res.Boards) > 0 && timberHeld < mill.timberTaken - 0.5f,
            $"{mill.timberTaken:F0} logs cut, {timberHeld} still held, {mill.CountOf(Res.Boards)} boards: "
            + $"{mill.timberTaken - timberHeld:F0} went into the saw");

        // A position with nothing to work on produces nothing, which is the
        // whole point of the chain: a forge on an island with no ore is a shed.
        var forge = new OutpostLedger { lastTicked = t0 };
        forge.built.Add(BuildPlans.Blacksmith.id);
        forge.hands.Add(new OutpostHand { name = "Bo", order = OutpostOrder.Work, target = BuildPlans.Blacksmith.id });
        // A player order and nothing to work with -- the station waits
        // rather than making anything (2026-09-23, storage-hub rules).
        forge.stationsMigrated = true;
        forge.PlaceOrder(BuildPlans.Blacksmith.id, "spear", OutpostLedger.RepeatOrder);
        forge.Tick(t0 + 20.0 * day);
        Gate(sb, ref fails, "a-forge-with-no-ore-makes-nothing",
            forge.CountOf(Res.Tools) == 0 && forge.CountOf(Res.Spear) == 0,
            $"{forge.CountOf(Res.Tools)} tools, {forge.CountOf(Res.Spear)} spears out of no boards or stone at all");

        // A farm's input is the ground, so it needs nothing but a farmhand.
        var farm = new OutpostLedger { lastTicked = t0 };
        farm.built.Add(BuildPlans.Farm.id);
        farm.hands.Add(new OutpostHand { name = "Bo", order = OutpostOrder.Work, target = BuildPlans.Farm.id });
        farm.Tick(t0 + 3.0 * day);
        Gate(sb, ref fails, "a-farmhand-needs-no-input",
            farm.CountOf(Res.Food) > 0,
            $"{farm.CountOf(Res.Food)} food in 3 days at {BuildPlans.Farm.rate:F0}/day");

        // And all of it is still path-independent, which is the property the
        // whole absentee loop rests on and the one most easily broken by
        // adding a second pass over the hands.
        var mixA = Mixed(t0);
        mixA.Tick(t0 + 8.0 * day);
        var mixB = Mixed(t0);
        for (int i = 1; i <= 320; i++) mixB.Tick(t0 + 8.0 * day * i / 320.0);
        bool same2 = mixA.CountOf(Res.Timber) == mixB.CountOf(Res.Timber)
            && mixA.CountOf(Res.Boards) == mixB.CountOf(Res.Boards)
            && mixA.CountOf(Res.Food) == mixB.CountOf(Res.Food)
            && Mathf.Abs(mixA.Wood.standing - mixB.Wood.standing) < 1e-3f;
        sb.AppendLine();
        sb.AppendLine($"A MIXED CAMP over 8 days, one call vs 320:");
        sb.AppendLine($"  one call   timber {mixA.CountOf(Res.Timber)}  boards {mixA.CountOf(Res.Boards)}  food {mixA.CountOf(Res.Food)}");
        sb.AppendLine($"  320 calls  timber {mixB.CountOf(Res.Timber)}  boards {mixB.CountOf(Res.Boards)}  food {mixB.CountOf(Res.Food)}");
        Gate(sb, ref fails, "production-is-path-independent-too", same2,
            "gathering, sawing and farming all land on the same state");

        sb.AppendLine();
        sb.AppendLine(fails == 0
            ? "PASS — the ledger answers the same however often it is asked"
            : $"{fails} GATE(S) FAILED");
        Report(sb.ToString());
    }

    /// One hand building a campfire that was sited 40 m off the survey's
    /// clearing -- which is the case the position field exists for.
    static OutpostLedger Building(double at, float hectares = 1f)
    {
        var l = OutpostLedger.For(Vector3.zero, hectares);
        l.lastTicked = at;
        l.ceilingPer = 0;                     // no fire yet, so nothing keeps anything
        l.hands.Add(new OutpostHand { name = "Bo", order = OutpostOrder.Build });
        // One drawing in the queue -- `sites` replaced the single
        // `pending` row on 2026-09-22 and this probe's "one build" is the
        // one-element case of it.
        l.sites.Add(new PendingBuild
        {
            planId = BuildPlans.Campfire.id,
            x = 40f,
            z = -12.5f,
            needed = BuildPlans.Campfire.cost,
        });
        return l;
    }

    static bool SameBuild(OutpostLedger a, OutpostLedger b)
        => a.Pending.done == b.Pending.done
        && Mathf.Abs(a.Pending.donePart - b.Pending.donePart) < 1e-4f
        && Mathf.Abs(a.Wood.standing - b.Wood.standing) < 1e-3f;

    /// A camp with a bit of everything in it: two cutting, a sawyer and a
    /// farmhand. The case where the tick has to do three different things in
    /// one quantum and still land on the same answer whenever it is asked.
    static OutpostLedger Mixed(double at)
    {
        var l = new OutpostLedger { lastTicked = at, ceilingPer = 30 };
        l.SeedStock(Res.Timber, 3f);
        l.built.Add(BuildPlans.Sawmill.id);
        l.built.Add(BuildPlans.Farm.id);
        l.hands.Add(new OutpostHand { name = "Bo",   order = OutpostOrder.Gather, target = Res.Timber });
        l.hands.Add(new OutpostHand { name = "Sten", order = OutpostOrder.Gather, target = Res.Timber });
        l.hands.Add(new OutpostHand { name = "Ola",  order = OutpostOrder.Work,   target = BuildPlans.Sawmill.id });
        l.hands.Add(new OutpostHand { name = "Nils", order = OutpostOrder.Work,   target = BuildPlans.Farm.id });
        // The sawmill is a station and makes nothing without a player order
        // (2026-09-23, storage-hub rules); the farm is not a station (its
        // field is its input) and needs none.
        l.stationsMigrated = true;
        l.PlaceOrder(BuildPlans.Sawmill.id, "boards", OutpostLedger.RepeatOrder);
        return l;
    }

    /// Two hands cutting on a hectare of ground, at a known instant.
    static OutpostLedger Working(double at, float hectares = 1f)
    {
        var l = OutpostLedger.For(Vector3.zero, hectares);
        l.lastTicked = at;
        l.hands.Add(new OutpostHand { name = "Bo",   order = OutpostOrder.Gather, target = Res.Timber });
        l.hands.Add(new OutpostHand { name = "Sten", order = OutpostOrder.Gather, target = Res.Timber });
        return l;
    }

    /// Same state, to the resolution anything downstream could notice.
    static bool Same(OutpostLedger a, OutpostLedger b)
        => a.Timber == b.Timber
        && Mathf.Abs(a.TimberPart() - b.TimberPart()) < 1e-4f
        && Mathf.Abs(a.Wood.standing - b.Wood.standing) < 1e-3f;

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
