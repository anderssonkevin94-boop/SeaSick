using System.Text;
using UnityEngine;
using SeaSick.Ship;
using SeaSick.UI;
using SeaSick.Voyage;
using SeaSick.World;

/// **STALE since 2026-09-24 (carried cargo).** `CampLoading` no longer
/// moves a unit every `Interval`: loads are transfer orders that villagers
/// carry an armful at a time (`OutpostLedger.Transfers`). The waits below
/// (40 / 60 s) and the "at most one more unit after cancel" gate were sized
/// for the old coroutine and will misreport. The ledger side is gated by
/// `StationStockSelfTest` section (n); rewrite this probe before trusting it.
/// **Does a camp's pile end up in the hold, unit for unit, and never by
/// itself?**
///
/// The fourth step of the loop — *return and load* — did not exist in code
/// until 2026-09-20: `OutpostLedger.Take` had no callers at all. So this is a
/// conservation probe before it is anything else. **Every gate below is about
/// a number that must be EQUAL to another number**, because the failure mode
/// of a loader is not a crash, it is four logs quietly becoming three and
/// presenting as a balance problem two features later.
///
/// Four families:
///
/// 1. **Unit for unit.** Five boards asked for are five off the ground, five
///    in the hold and five on the stack at the stern. Then a whole run, per
///    resource, across a hold that fills half-way through it — which is the
///    case where a naive loader loses the unit it could not fit.
/// 2. **The line is the player's, not the loader's.** `AddLoot` clamps only to
///    the physical limit, so with deck cargo OFF the carry must stop itself at
///    `HoldCapacity` and never once go past it; with it ON, at `MaxHold`.
/// 3. **A camp never ships home by itself.** Three game days with nobody
///    pressing anything and the hold does not move; and with her under way,
///    loading is refused outright.
/// 4. **The pile by the fire is what the ledger holds**, unit for unit up
///    to the drawn cap.
///
/// Plain C#, no editor references. Play mode, `Sea.unity`. Writes
/// `Logs/CampLoadProbe.txt`. It restores the hold on the way out: everything
/// it put aboard goes over the side, so a session can be carried on with.
public class CampLoadProbe : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("CampLoadProbe: not in play mode"); return; }
        var old = FindAnyObjectByType<CampLoadProbe>();
        if (old != null) Destroy(old.gameObject);
        var runner = new GameObject("CampLoadProbeRunner").AddComponent<CampLoadProbe>();
        runner.StartCoroutine(runner.Run());
    }

    readonly StringBuilder sb = new StringBuilder();
    int fails;

    /// Units this probe put aboard by any route, so it can put them back over
    /// the side at the end.
    int heldAtStart;

    /// A pile beside the fire stops growing in height past this. `CampPiles`
    /// owns the number (`MaxDrawn`); it is private, so it is named here rather
    /// than guessed at, and if it moves this gate is the thing that says so.
    const int PileDrawnCap = 12;

    /// `ShipHold.maxVisible` — the stack at the stern stops at this many
    /// objects however much she is carrying. Same rule as above.
    const int StackCap = 24;

    System.Collections.IEnumerator Run()
    {
        // **First, before anything reads the screen or the scene.** A
        // coroutine's opening segment runs inside the call that started it,
        // which in a probe launched from an editor script is the EDITOR's
        // frame — `Screen` is the editor window there, and any layout measured
        // in it is a measurement of the wrong thing.
        yield return new WaitForEndOfFrame();

        var anchor = Object.FindFirstObjectByType<AnchorController>();
        var motor = Object.FindFirstObjectByType<ShipMotor>();
        var voyage = Object.FindFirstObjectByType<VoyageManager>();
        if (anchor == null || motor == null || voyage == null)
        { Finish("no ship in the scene"); yield break; }
        var hold = anchor.GetComponent<ShipHold>();

        heldAtStart = voyage.TotalHeld;
        voyage.TakeDeckCargo = false;

        sb.AppendLine($"hold: {voyage.TotalHeld} aboard, line {voyage.HoldCapacity}, "
            + $"stuffed {voyage.MaxHold}, {(hold != null ? hold.VisibleCount : -1)} drawn");

        // --- somewhere with a beach on it -------------------------------------
        //
        // Ranked by shore gap and asking the same three questions
        // `AnchorController` asks, in the same order. `CampProbe` learned both
        // the hard way: a ship 18 m off a small island is routinely nearer the
        // CENTRE of a big one, and a scan that only checks the beach says yes
        // where no prompt ever appears.
        Island target = null; Vector3 standOff = default; float bestGap = float.MaxValue;
        Vector3 from = motor.transform.position;
        foreach (var isle in Island.All)
        {
            if (isle == null || isle.IsHome) continue;
            for (int b = 0; b < 24; b++)
            {
                float ang = b / 24f * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang));
                Vector3 at = isle.transform.position + dir * (isle.RadiusAt(ang) + 18f);
                if (Island.TerrainHeight == null || Island.TerrainHeight(at.x, at.z) > -0.5f) continue;
                if (Island.Nearest(at) != isle) continue;
                if (Island.FlatDistance(at, isle.transform.position)
                    > isle.RadiusToward(at) + 30f) continue;
                if (!isle.HasBeachToward(at)) continue;
                float gap = Island.FlatDistance(at, from);
                if (gap < bestGap) { bestGap = gap; target = isle; standOff = at; }
            }
        }
        if (target == null) { Finish("no island in the world offers a beach to land on"); yield break; }
        sb.AppendLine($"target {target.name}  r {target.Radius:F0} m, {bestGap:F0} m off");

        if (anchor.CurrentState != AnchorController.State.Underway)
        {
            anchor.CastOff();
            float cast = Time.realtimeSinceStartup;
            while (anchor.CurrentState != AnchorController.State.Underway
                   && Time.realtimeSinceStartup - cast < 10f) yield return null;
        }

        // Re-place and ask again rather than warping once: she has way on
        // after casting off, and a single warp plus two frames can leave her
        // drifting off the spot before the landing test runs.
        bool landed = false; string why = "never tried";
        float landBy = Time.realtimeSinceStartup + 5f;
        while (!landed && Time.realtimeSinceStartup < landBy)
        {
            Warp(motor, standOff, Quaternion.LookRotation(target.transform.position - standOff));
            yield return new WaitForFixedUpdate();
            yield return null;
            landed = anchor.TryLand(out why);
        }
        Gate("she-can-land-there", landed, why);
        if (!landed) { Finish("could not land"); yield break; }

        float t0 = Time.realtimeSinceStartup;
        while (Outpost.Surveying(target) && Time.realtimeSinceStartup - t0 < 30f) yield return null;
        var camp = Outpost.Of(target);
        if (camp == null)
        { Finish($"{target.name} will not take a camp — this probe needs one that will"); yield break; }

        float settle = Time.realtimeSinceStartup + 8f;
        while (anchor.CurrentState == AnchorController.State.Dropping
               && Time.realtimeSinceStartup < settle) yield return null;

        // --- a camp, the dev way ----------------------------------------------
        //
        // `MakeCamp` sites at the surveyed clearing and pays for it outright.
        // The slow path is `CampProbe`'s to gate; a probe about LOADING that
        // waited for a camp to be built would measure the build every run.
        if (!camp.HasCamp)
        {
            camp.MakeCamp(out string campWhy);
            if (!camp.HasCamp) { Finish("could not make a camp: " + campWhy); yield break; }
        }
        var l = camp.Ledger;
        // Nobody works here for the duration. The gates below are about units
        // MOVING, and a hand quietly cutting another log in the middle of a
        // conservation count is noise in the only number that matters.
        l.hands.Clear();
        l.lastTicked = TimeOfDay.Seconds;
        camp.CatchUp();

        // A store hut, so the camp can hold thirty of each rather than the
        // fire's ten. The ceiling is the BUILDINGS' — `Outpost.CatchUp` pushes
        // `KeepsOfEach` into the ledger before every tick, so setting
        // `ceilingPer` by hand here would be undone on the next call.
        var store = RaiseNear(camp, BuildPlans.Storage, 12f);
        if (store != null) l.built.Add(BuildPlans.Storage.id);
        camp.CatchUp();
        sb.AppendLine($"camp at {camp.CampCentre.x:F0},{camp.CampCentre.z:F0}   "
            + $"store hut {(store != null ? "raised" : "NO")}   keeps {l.ceilingPer} of each");
        Gate("the-camp-can-hold-thirty", l.ceilingPer >= 25,
            $"ceiling {l.ceilingPer} — the seeding below needs room for 12/9/4 and more");

        // =====================================================================
        // 1. FIVE BOARDS, AND EXACTLY FIVE
        // =====================================================================

        Seed(l, 12, 9, 4);
        camp.CatchUp();
        sb.AppendLine();
        sb.AppendLine($"SEEDED: {CampLoading.Summary(l)}");

        int ledgerB0 = l.CountOf(Res.Boards);
        int heldB0 = voyage.AmountOf(Res.Boards);
        int vis0 = hold != null ? hold.VisibleCount : 0;
        int total0 = voyage.TotalHeld;

        int moved = CampLoading.LoadNow(camp, voyage, hold, Res.Boards, 5);
        yield return null;

        int wantVis = Mathf.Min(vis0 + 5, StackCap);
        sb.AppendLine();
        sb.AppendLine("LOADING FIVE BOARDS BY HAND:");
        sb.AppendLine($"  moved {moved}   ledger {ledgerB0} -> {l.CountOf(Res.Boards)}   "
            + $"hold {heldB0} -> {voyage.AmountOf(Res.Boards)}   "
            + $"stack {vis0} -> {(hold != null ? hold.VisibleCount : -1)}");
        Gate("five-boards-move-exactly-five", moved == 5, $"{moved} moved");
        Gate("five-leave-the-ground", l.CountOf(Res.Boards) == ledgerB0 - 5,
            $"{ledgerB0} -> {l.CountOf(Res.Boards)}");
        Gate("five-arrive-in-the-hold",
            voyage.AmountOf(Res.Boards) == heldB0 + 5 && voyage.TotalHeld == total0 + 5,
            $"{heldB0} -> {voyage.AmountOf(Res.Boards)}, total {total0} -> {voyage.TotalHeld}");
        Gate("and-the-stack-on-deck-grows-with-them",
            hold == null || hold.VisibleCount == wantVis,
            $"{(hold != null ? hold.VisibleCount : -1)} drawn, expected {wantVis}");

        // =====================================================================
        // 2. A WHOLE RUN, WITH THE HOLD FILLING HALF-WAY THROUGH IT
        // =====================================================================
        //
        // Her room is set to a known 17 so the run is short and so the pile
        // OUTLASTS it: a loader that loses the unit it could not fit loses it
        // at exactly this moment, and a run that empties the camp would never
        // reach that moment at all.

        int room0 = CampLoading.RoomAboard(voyage);
        if (room0 > 17 && hold != null)
        {
            // Ballast, so the line is close. It is stone rather than anything
            // the camp holds, so it cannot be confused with a carried unit in
            // the per-resource sums below.
            int fill = room0 - 17;
            voyage.AddLoot(fill, Res.Stone);
            for (int i = 0; i < fill; i++) hold.AddVisual(Res.Stone);
        }
        int room = CampLoading.RoomAboard(voyage);

        // More on the ground than she can take, whatever her hold turned out
        // to be.
        Seed(l, Mathf.Min(l.ceilingPer, room + 5), 9, 4);
        camp.CatchUp();

        int lgT0 = l.CountOf(Res.Timber), lgB1 = l.CountOf(Res.Boards), lgK0 = l.CountOf(Res.Tools);
        int hdT0 = voyage.AmountOf(Res.Timber), hdB1 = voyage.AmountOf(Res.Boards),
            hdK0 = voyage.AmountOf(Res.Tools);
        int held0 = voyage.TotalHeld;

        voyage.TakeDeckCargo = false;
        bool began = CampLoading.Begin(camp, voyage, hold);
        int peak = voyage.TotalHeld;
        int tick = 0, toolsDoneAt = -1, timberFirstAt = -1;
        float until = Time.realtimeSinceStartup + 40f;
        while (CampLoading.Busy && Time.realtimeSinceStartup < until)
        {
            yield return null;
            tick++;
            if (voyage.TotalHeld > peak) peak = voyage.TotalHeld;
            if (toolsDoneAt < 0 && l.CountOf(Res.Tools) == 0) toolsDoneAt = tick;
            if (timberFirstAt < 0 && voyage.AmountOf(Res.Timber) > hdT0) timberFirstAt = tick;
        }

        int gotT = voyage.AmountOf(Res.Timber) - hdT0;
        int gotB = voyage.AmountOf(Res.Boards) - hdB1;
        int gotK = voyage.AmountOf(Res.Tools) - hdK0;
        int leftT = lgT0 - l.CountOf(Res.Timber);
        int leftB = lgB1 - l.CountOf(Res.Boards);
        int leftK = lgK0 - l.CountOf(Res.Tools);

        sb.AppendLine();
        sb.AppendLine($"A WHOLE RUN, DECK CARGO OFF (room for {room}, "
            + $"{lgT0 + lgB1 + lgK0} ashore):");
        sb.AppendLine($"  timber  ledger -{leftT}  hold +{gotT}");
        sb.AppendLine($"  boards  ledger -{leftB}  hold +{gotB}");
        sb.AppendLine($"  tools   ledger -{leftK}  hold +{gotK}");
        sb.AppendLine($"  hold {held0} -> {voyage.TotalHeld} (peak {peak}) "
            + $"against a line of {voyage.HoldCapacity}; {l.Total} left on the ground");

        Gate("the-run-starts", began, "Begin refused");
        Gate("nothing-is-created-or-lost",
            leftT == gotT && leftB == gotB && leftK == gotK
            && (leftT + leftB + leftK) == voyage.TotalHeld - held0,
            $"ledger -{leftT}/-{leftB}/-{leftK} against hold +{gotT}/+{gotB}/+{gotK}, "
            + $"total +{voyage.TotalHeld - held0}");
        Gate("the-hold-fills-before-the-camp-empties", l.Total > 0,
            "the camp ran dry first — this gate measured nothing");
        Gate("deck-cargo-off-stops-at-the-line",
            voyage.TotalHeld == voyage.HoldCapacity && peak <= voyage.HoldCapacity,
            $"{voyage.TotalHeld} aboard, peak {peak}, line {voyage.HoldCapacity}");
        Gate("tools-come-aboard-before-any-timber",
            toolsDoneAt > 0 && (timberFirstAt < 0 || toolsDoneAt <= timberFirstAt),
            $"tools emptied at frame {toolsDoneAt}, timber first moved at {timberFirstAt}");

        // =====================================================================
        // 3. DECK CARGO ON — the same run, one limit further out
        // =====================================================================

        Seed(l, l.ceilingPer, l.ceilingPer, l.ceilingPer);
        camp.CatchUp();
        int held1 = voyage.TotalHeld;
        int ashore1 = l.Total;
        voyage.TakeDeckCargo = true;
        CampLoading.Begin(camp, voyage, hold);
        int peak2 = voyage.TotalHeld;
        until = Time.realtimeSinceStartup + 60f;
        while (CampLoading.Busy && Time.realtimeSinceStartup < until)
        {
            yield return null;
            if (voyage.TotalHeld > peak2) peak2 = voyage.TotalHeld;
        }

        sb.AppendLine();
        sb.AppendLine($"THE SAME RUN WITH DECK CARGO ON: hold {held1} -> {voyage.TotalHeld} "
            + $"(peak {peak2}) against a stuffed limit of {voyage.MaxHold}; "
            + $"ashore {ashore1} -> {l.Total}");
        Gate("deck-cargo-on-stops-at-the-stuffed-limit",
            voyage.TotalHeld == voyage.MaxHold && peak2 <= voyage.MaxHold,
            $"{voyage.TotalHeld} aboard, peak {peak2}, stuffed {voyage.MaxHold}");
        Gate("past-the-line-conserves-too",
            (ashore1 - l.Total) == (voyage.TotalHeld - held1),
            $"ledger -{ashore1 - l.Total} against hold +{voyage.TotalHeld - held1}");

        // =====================================================================
        // 4. STOP MEANS STOP
        // =====================================================================
        //
        // She is stuffed after the run above, so the probe makes room the way
        // a player in trouble does — over the side. That destroys goods on
        // purpose and outside every conservation window below.

        // **Down to below the LINE, not down by twenty.** She is stuffed, so
        // "jettison 20" leaves her still over her marks and `RoomAboard` with
        // deck cargo off is then zero — the run would never start and every
        // gate below would fail for a reason that has nothing to do with ✕.
        voyage.TakeDeckCargo = false;
        int shed = Mathf.Max(0, voyage.TotalHeld - Mathf.Max(1, voyage.HoldCapacity - 20));
        if (shed > 0) voyage.Jettison(shed);
        yield return null;

        CampLoading.Begin(camp, voyage, hold);
        for (int f = 0; f < 3; f++) yield return new WaitForSeconds(CampLoading.Interval);
        int heldAtCancel = voyage.TotalHeld;
        int ashoreAtCancel = l.Total;
        int carried = CampLoading.Moved;
        CampLoading.Cancel();
        for (int f = 0; f < 6; f++) yield return new WaitForSeconds(CampLoading.Interval);

        sb.AppendLine();
        sb.AppendLine($"STOP, MID-CARRY (after {carried} units): "
            + $"hold {heldAtCancel} -> {voyage.TotalHeld}, ashore {ashoreAtCancel} -> {l.Total}");
        Gate("cancel-stops-within-one-unit",
            !CampLoading.Busy && voyage.TotalHeld - heldAtCancel <= 1
            && voyage.TotalHeld >= heldAtCancel,
            $"busy {CampLoading.Busy}, {voyage.TotalHeld - heldAtCancel} more units after ✕");
        Gate("and-conserves-what-it-had-carried",
            (ashoreAtCancel - l.Total) == (voyage.TotalHeld - heldAtCancel),
            $"ledger -{ashoreAtCancel - l.Total} against hold +{voyage.TotalHeld - heldAtCancel}");

        // =====================================================================
        // 5. A CAMP NEVER SHIPS HOME BY ITSELF
        // =====================================================================
        //
        // The rule the whole design rests on (PLAN-island-outposts, risk #2).
        // Three game days of the ledger ticking with nobody pressing anything:
        // the pile may do whatever it likes, the HOLD must not move.

        int holdBefore = voyage.TotalHeld;
        int visBefore = hold != null ? hold.VisibleCount : 0;
        int ashoreBefore = l.Total;
        l.lastTicked = TimeOfDay.Seconds;
        l.Tick(TimeOfDay.Seconds + 3.0 * TimeOfDay.WorkDaySeconds);
        camp.CatchUp();
        for (int f = 0; f < 20; f++) yield return null;

        sb.AppendLine();
        sb.AppendLine($"THREE GAME DAYS WITH NOBODY PRESSING ANYTHING: "
            + $"hold {holdBefore} -> {voyage.TotalHeld}, "
            + $"stack {visBefore} -> {(hold != null ? hold.VisibleCount : -1)}, "
            + $"ashore {ashoreBefore} -> {l.Total}");
        Gate("an-absent-camp-never-ships-home",
            voyage.TotalHeld == holdBefore
            && (hold == null || hold.VisibleCount == visBefore),
            $"hold moved {voyage.TotalHeld - holdBefore}");

        // =====================================================================
        // 6. THE PILE BY THE FIRE IS WHAT THE LEDGER HOLDS
        // =====================================================================

        var piles = CampPiles.EnsureOn(camp);
        if (piles != null) piles.Refresh();
        // Two frames, not one: `Rebuild` destroys the old stack and builds the
        // new one in the same call, and `Destroy` is deferred to the end of
        // the frame — so a `childCount` read too early counts both.
        yield return null;
        yield return null;

        var stack = piles != null ? piles.transform.Find("Pile_" + Res.Timber) : null;
        int drawn = stack != null ? stack.childCount : -1;
        int haveTimber = l.CountOf(Res.Timber);

        sb.AppendLine();
        sb.AppendLine("THE PICTURE AND THE NUMBER:");
        sb.AppendLine($"  ledger holds {haveTimber} timber; the pile draws {drawn} "
            + $"(caps at {PileDrawnCap})");
        Gate("the-pile-by-the-fire-agrees-with-the-ledger",
            drawn == Mathf.Min(haveTimber, PileDrawnCap),
            $"{drawn} drawn of {haveTimber} held");

        // =====================================================================
        // 7. UNDER WAY, NOTHING LOADS
        // =====================================================================

        foreach (var c in Object.FindObjectsByType<SeaSick.Crew.CrewAgent>(FindObjectsSortMode.None))
            if (c != null && c.IsAshore) c.ReturnAboard();
        float away = Time.realtimeSinceStartup + 20f;
        while (anchor.CurrentState != AnchorController.State.Underway
               && Time.realtimeSinceStartup < away)
        {
            anchor.CastOff();
            yield return null;
        }

        int ashoreAtSea = l.Total;
        int heldAtSea = voyage.TotalHeld;
        int refused = CampLoading.LoadNow(camp, voyage, hold, Res.Timber, 3);
        bool beganAtSea = CampLoading.Begin(camp, voyage, hold);
        yield return null;

        sb.AppendLine();
        sb.AppendLine($"UNDER WAY ({anchor.CurrentState}): LoadNow moved {refused}, "
            + $"Begin {(beganAtSea ? "STARTED" : "refused")}");
        Gate("loading-is-refused-at-sea",
            refused == 0 && !beganAtSea
            && l.Total == ashoreAtSea && voyage.TotalHeld == heldAtSea,
            $"{refused} moved, began {beganAtSea}");

        // Put the hold back the way it was found.
        CampLoading.Cancel();
        int over = voyage.TotalHeld - heldAtStart;
        if (over > 0) voyage.Jettison(over);
        yield return null;
        sb.AppendLine();
        sb.AppendLine($"restored: jettisoned {Mathf.Max(0, over)}, hold now {voyage.TotalHeld} "
            + $"(found it at {heldAtStart})");

        Finish(null);
    }

    /// **The probe writing the ledger, which is the one thing allowed to.**
    ///
    /// The rows are emptied rather than removed: `CampPiles` keys its stacks
    /// on a resource's whole-unit count and only rebuilds one when that count
    /// MOVES, so clearing the list would leave the old stacks standing beside
    /// a fire that holds nothing.
    static void Seed(OutpostLedger l, int timber, int boards, int tools)
    {
        foreach (var s in l.stores) if (s != null) { s.whole = 0; s.part = 0f; }
        l.Add(Res.Timber, timber);
        l.Add(Res.Boards, boards);
        l.Add(Res.Tools, tools);
    }

    static Building RaiseNear(Outpost camp, BuildPlan plan, float startRadius)
    {
        for (int ring = 0; ring < 7; ring++)
            for (int i = 0; i < 14; i++)
            {
                float a = i * Mathf.PI * 2f / 14f + ring * 0.7f;
                float r = startRadius + ring * 4f;
                var p = camp.CampCentre + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                p.y = camp.GroundAt(p);
                if (!camp.CanPlace(plan, p, out _)) continue;
                var b = camp.Raise(plan, p);
                if (b != null) return b;
            }
        return null;
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

    void Gate(string name, bool ok, string detail)
    {
        if (!ok) fails++;
        sb.AppendLine($"  [{(ok ? "ok  " : "FAIL")}] {name}   {(ok ? "" : detail)}");
    }

    void Finish(string stopped)
    {
        sb.AppendLine();
        if (!string.IsNullOrEmpty(stopped)) sb.AppendLine("STOPPED: " + stopped);
        sb.AppendLine(fails == 0
            ? "PASS — the pile goes aboard unit for unit, stops at the line, and never sails itself"
            : $"{fails} GATE(S) FAILED");
        Report(sb.ToString());
        Destroy(gameObject);
    }

    static void Report(string text)
    {
        Debug.Log("CampLoadProbe\n" + text);
        var path = System.IO.Path.Combine(Application.dataPath, "../Logs/CampLoadProbe.txt");
        try { System.IO.File.WriteAllText(path, text); } catch { }
    }
}
