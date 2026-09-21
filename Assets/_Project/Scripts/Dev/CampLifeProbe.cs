using System.Text;
using UnityEngine;
using SeaSick.Ship;
using SeaSick.World;

/// **The people at a camp do their work, and doing it changes nothing.**
///
/// `CampProbe` gates the camp as a PLACE: the fire goes up, the ring is a
/// ring, the wood thins, an assigned hand produces. This gates the camp as a
/// picture of that place — and, far more important, gates that the picture is
/// only ever a picture.
///
/// **The load-bearing gate is D2.** `CampWorker` and `VillagerActing` walk,
/// swing, carry and drop. If either of them ever put a log in a pile or took
/// a tree down, a camp would pay differently depending on whether anybody was
/// standing in it, and the entire absentee loop would be a lie. So this probe
/// stops the clock, runs thirty seconds of villagers, and demands that the
/// ledger JSON and the felled-tree count in the MESH come out byte-identical.
///
/// The other five are about whether it reads: a sawyer who walks to his own
/// mill rather than being teleported to it, a camp that does not jump when
/// one hand is given a job, a body that faces where it is going (which is the
/// gate that catches `CrewAgent.ActBody` fighting the puppet for the root
/// rotation), a man who carries on working after being picked up and put
/// down, and a stalled mill that reads as stalled.
///
/// Lives beside `CampProbe` and is deliberately NOT part of it: `CampProbe`
/// is 58 gates and four minutes of sailing, and a sixth of that spent waiting
/// for a sawyer to walk 11 m is a probe nobody runs.
public class CampLifeProbe : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("CampLifeProbe: not in play mode"); return; }
        var runner = new GameObject("CampLifeProbeRunner").AddComponent<CampLifeProbe>();
        runner.StartCoroutine(runner.Run());
    }

    StringBuilder sb;
    int fails;

    System.Collections.IEnumerator Run()
    {
        sb = new StringBuilder();
        fails = 0;

        var anchor = Object.FindFirstObjectByType<AnchorController>();
        var motor = Object.FindFirstObjectByType<ShipMotor>();
        if (anchor == null || motor == null) { Fail("no ship in the scene"); yield break; }

        // --- get her to an island with wood on it -----------------------------
        //
        // Lifted from `CampProbe`: ranked by SHORE gap and asking the same
        // three questions `AnchorController` asks, in the same order. A scan
        // that ranks by centre distance says "landable" at spots where no
        // prompt ever appears.

        Island target = null; Vector3 standOff = default; float bestGap = float.MaxValue;
        Vector3 from = motor.transform.position;
        foreach (var isle in Island.All)
        {
            if (isle == null || isle.IsHome) continue;
            for (int b = 0; b < 24; b++)
            {
                float ang = b / 24f * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang));
                Vector3 ship = isle.transform.position + dir * (isle.RadiusAt(ang) + 18f);
                if (Island.TerrainHeight == null || Island.TerrainHeight(ship.x, ship.z) > -0.5f) continue;
                if (Island.Nearest(ship) != isle) continue;
                if (Island.FlatDistance(ship, isle.transform.position)
                    > isle.RadiusToward(ship) + 30f) continue;
                if (!isle.HasBeachToward(ship)) continue;
                float gap = Island.FlatDistance(ship, from);
                if (gap < bestGap) { bestGap = gap; target = isle; standOff = ship; }
            }
        }
        if (target == null) { Fail("no island in the world offers a beach"); yield break; }

        if (anchor.CurrentState != AnchorController.State.Underway)
        {
            anchor.CastOff();
            float cast = Time.realtimeSinceStartup;
            while (anchor.CurrentState != AnchorController.State.Underway
                   && Time.realtimeSinceStartup - cast < 10f) yield return null;
        }

        bool landed = false; string why = "never tried";
        float landBy = Time.realtimeSinceStartup + 5f;
        while (!landed && Time.realtimeSinceStartup < landBy)
        {
            Warp(motor, standOff, Quaternion.LookRotation(target.transform.position - standOff));
            yield return new WaitForFixedUpdate();
            yield return null;
            landed = anchor.TryLand(out why);
        }
        sb.AppendLine($"target {target.name}, r {target.Radius:F0} m — land: {(landed ? "yes" : "NO")} ({why})");
        Gate("she-can-land-there", landed, why);
        if (!landed) { Finish(); yield break; }

        float t0 = Time.realtimeSinceStartup;
        while (Outpost.Surveying(target) && Time.realtimeSinceStartup - t0 < 30f) yield return null;

        var camp = Outpost.Of(target);
        if (camp == null) { Fail("this island will not take a camp"); yield break; }

        // A lit fire, the fast way. The blueprint path is `CampProbe`'s job.
        if (!camp.HasCamp) camp.MakeCamp(out string campWhy);
        camp.CatchUp();
        yield return null;
        Gate("there-is-a-fire-to-stand-round", camp.HasCamp, "MakeCamp refused");
        if (!camp.HasCamp) { Finish(); yield break; }

        // --- three hands, drawn, with bodies on them --------------------------

        var roster = Object.FindFirstObjectByType<SeaSick.Crew.CrewRoster>();
        var stood = new System.Collections.Generic.List<SeaSick.Crew.CrewAgent>();
        if (roster != null)
            foreach (var hand in roster.All)
            {
                if (stood.Count >= 3) break;
                if (hand == null) continue;
                if (camp.Station(hand)) stood.Add(hand);
            }
        roster?.Refresh();
        // Forced rather than assumed. `ShowHands(true)` is the arrival path,
        // and `CampProbe` deliberately does not call it because it is gating
        // that arrival wakes the camp. That is not this probe's question, and
        // a probe that measures nothing because the bodies were asleep is the
        // most expensive kind of green.
        camp.ShowHands(true);
        for (int f = 0; f < 4; f++) yield return null;

        int withBody = 0, withActing = 0;
        foreach (var a in stood)
        {
            if (a == null) continue;
            if (CampWorker.Of(a) != null) withBody++;
            if (a.GetComponent<VillagerActing>() != null) withActing++;
        }
        sb.AppendLine($"{stood.Count} hands stationed: {withBody} with a CampWorker, "
            + $"{withActing} with a body to act with");
        Gate("every-drawn-hand-has-a-worker-and-an-actor",
            stood.Count == 3 && withBody == 3 && withActing == 3,
            $"{stood.Count} stood, {withBody} workers, {withActing} actors");
        if (stood.Count < 3) { Finish(); yield break; }

        // --- a sawmill, and a sawyer in it ------------------------------------

        Vector3 millAt = camp.CampCentre;
        bool millPlaced = false;
        for (int ring = 0; ring < 6 && !millPlaced; ring++)
            for (int i = 0; i < 12 && !millPlaced; i++)
            {
                float a3 = i * Mathf.PI * 2f / 12f + ring * 0.7f;
                float rad = 10f + ring * 4f;
                var q = camp.CampCentre + new Vector3(Mathf.Cos(a3) * rad, 0f, Mathf.Sin(a3) * rad);
                q.y = camp.GroundAt(q);
                if (camp.CanPlace(BuildPlans.Sawmill, q, out _)) { millAt = q; millPlaced = true; }
            }
        var mill = millPlaced ? camp.Raise(BuildPlans.Sawmill, millAt) : null;
        if (mill != null) camp.Ledger.built.Add(BuildPlans.Sawmill.id);
        yield return null;
        Gate("a-mill-to-work-at", mill != null,
            millPlaced ? "Raise refused" : "nowhere within 30 m would take a sawmill");
        if (mill == null) { Finish(); yield break; }

        var sawyerBody = stood[0];
        var sawyerRow = camp.HandNamed(sawyerBody.DisplayName);
        var sawyerWorker = CampWorker.Of(sawyerBody);
        var sawyerActs = sawyerBody.GetComponent<VillagerActing>();
        float millToFire = Island.FlatDistance(mill.transform.position, camp.CampCentre);
        Vector3 door = CampWorker.WorkSpot(camp, mill);

        // Give the mill something to saw, or he is stalled before he starts.
        camp.Ledger.Add(Res.Timber, 8);
        bool assigned = camp.Assign(sawyerRow, BuildPlans.Sawmill.id);
        sawyerWorker.PreferWorkplace(mill);

        // --- 1. he WALKS to it, and he stays at his trade ---------------------
        //
        // The distances are against the DOOR, not the mill's origin: a worker
        // stands just clear of the footprint (`CampWorker.WorkSpot`), which on
        // a 7.6 m sawmill is 4.4 m off its centre. And "stays" cannot mean
        // "never moves": the carry leg to the boards pile is the point of the
        // feature. What it must not do is wander off to a tree.

        float reachBy = Time.realtimeSinceStartup + 25f;
        float toDoor = 999f;
        while (Time.realtimeSinceStartup < reachBy)
        {
            toDoor = Island.FlatDistance(sawyerBody.transform.position, door);
            if (toDoor < 1.5f) break;
            yield return null;
        }
        float walkTook = 25f - (reachBy - Time.realtimeSinceStartup);

        float nearestMill = 999f, furthestMill = 0f;
        float watchUntil = Time.realtimeSinceStartup + 10f;
        while (Time.realtimeSinceStartup < watchUntil)
        {
            float d = Island.FlatDistance(sawyerBody.transform.position, mill.transform.position);
            nearestMill = Mathf.Min(nearestMill, d);
            furthestMill = Mathf.Max(furthestMill, d);
            yield return null;
        }
        // The furthest he is allowed is the boards pile, which sits round the
        // FIRE. Anything past that is a man who went back to the wood.
        float leash = millToFire + 5.2f + 3f;

        sb.AppendLine();
        sb.AppendLine("THE SAWYER:");
        sb.AppendLine($"  mill {millToFire:F1} m from the fire, its door {Island.FlatDistance(door, mill.transform.position):F1} m off its centre");
        sb.AppendLine($"  reached the door in {walkTook:F1} s ({toDoor:F2} m off it)");
        sb.AppendLine($"  over the next 10 s he was {nearestMill:F1}–{furthestMill:F1} m "
            + $"from the mill (leash {leash:F1} m)");
        Gate("a-sawyer-walks-to-his-own-mill", assigned && toDoor < 1.5f,
            assigned ? $"{toDoor:F2} m off the door after {walkTook:F1} s" : "Assign refused");
        Gate("and-is-still-at-it-ten-seconds-later", nearestMill < 6f,
            $"closest approach {nearestMill:F1} m");
        Gate("and-did-not-wander-back-to-the-wood", furthestMill < leash,
            $"got {furthestMill:F1} m out against a {leash:F1} m leash");

        // --- 2. an order for one hand does not move the others ----------------
        //
        // `ArrangeHands` runs INSIDE the order call, so this is measured with
        // no yield at all: the same frame, which is what the gate says.

        var bodyB = stood[1];
        var bodyC = stood[2];
        Vector3 wasB = bodyB.transform.position, wasC = bodyC.transform.position;
        Vector3 wasA = sawyerBody.transform.position;
        camp.OrderGather(camp.HandNamed(bodyB.DisplayName), Res.Timber);
        float movedC = Vector3.Distance(bodyC.transform.position, wasC);
        float movedA = Vector3.Distance(sawyerBody.transform.position, wasA);
        float movedB = Vector3.Distance(bodyB.transform.position, wasB);

        sb.AppendLine();
        sb.AppendLine("ONE ORDER, ONE HAND:");
        sb.AppendLine($"  ordering {bodyB.DisplayName} to gather moved the sawyer {movedA * 100f:F1} cm, "
            + $"{bodyC.DisplayName} {movedC * 100f:F1} cm, and {bodyB.DisplayName} himself {movedB * 100f:F1} cm");
        Gate("an-order-for-one-hand-does-not-snap-the-camp",
            movedA < 0.1f && movedC < 0.1f && movedB < 0.1f,
            $"{movedA * 100f:F1} / {movedB * 100f:F1} / {movedC * 100f:F1} cm");

        // --- 3. a walking body faces where it is going ------------------------
        //
        // **This is the `ActBody` gate.** A parked hand's state is `Station`,
        // and `Station` used to write `localRotation = Euler(0, 0, sway)` every
        // frame — under a parent that is the island, so the body faced world
        // +Z for ever whichever way its feet were carrying it. `Puppeted` is
        // what stops that, and a mean facing error of ninety-odd degrees is
        // what it looks like when it comes back.

        int samples = 0, wide = 0;
        float sumErr = 0f, worstErr = 0f;
        Vector3 prev = bodyB.transform.position;
        // Sampled while he is WALKING, up to forty seconds of looking for
        // it. Three seconds from the order used to be enough because the
        // order sent him off at once; since the felling claims (2026-09-20)
        // a man re-told to cut timber keeps his tree and goes on swinging at
        // it until it falls, so the three seconds after the order can be
        // three seconds of standing still (2026-09-21: "0 moving frames").
        // The gate waits for him, thresholds unchanged.
        float faceStart = Time.realtimeSinceStartup;
        float faceUntil = faceStart + 40f;
        while (Time.realtimeSinceStartup < faceUntil && samples < 30)
        {
            yield return null;
            Vector3 now = bodyB.transform.position;
            Vector3 v = now - prev; v.y = 0f;
            prev = now;
            float dt = Time.deltaTime;
            if (dt <= 0f || v.magnitude / dt < 0.6f) continue;
            Vector3 f = bodyB.transform.forward; f.y = 0f;
            if (f.sqrMagnitude < 0.0001f) continue;
            float err = Vector3.Angle(f, v);
            samples++; sumErr += err;
            worstErr = Mathf.Max(worstErr, err);
            if (err > 25f) wide++;
        }
        float meanErr = samples > 0 ? sumErr / samples : 999f;
        float wideShare = samples > 0 ? wide / (float)samples : 1f;

        sb.AppendLine();
        sb.AppendLine("WHICH WAY HE IS FACING:");
        sb.AppendLine($"  {samples} moving frames over {Time.realtimeSinceStartup - faceStart:F0} s: mean {meanErr:F1}° off his velocity, "
            + $"worst {worstErr:F0}°, {wideShare * 100f:F0}% past 25°");
        // Not "never past 25°": a turn at the end of a leg is a transient the
        // 8 Hz smoothing is *supposed* to produce. A fight with another writer
        // is not a transient, and shows up in the mean.
        Gate("a-walking-hand-faces-where-he-is-walking",
            samples > 20 && meanErr < 25f && wideShare < 0.2f,
            $"{samples} samples, mean {meanErr:F1}°, {wideShare * 100f:F0}% wide");

        // --- 4. the mode is the trade, and a stalled mill reads stalled -------

        var wantMode = VillagerActing.Mode.Saw;
        bool sawed = false;
        float sawBy = Time.realtimeSinceStartup + 25f;
        while (Time.realtimeSinceStartup < sawBy)
        {
            if (sawyerActs != null && sawyerActs.Current == wantMode) { sawed = true; break; }
            yield return null;
        }

        // Stall him by emptying the pile he saws out of. **The probe may touch
        // the ledger; the components under test may not.** Put back afterwards.
        var store = camp.Ledger.Store(Res.Timber, true);
        int keptWhole = store.whole; float keptPart = store.part;
        store.whole = 0; store.part = 0f;
        bool stalledRow = camp.Ledger.Stalled(sawyerRow);

        // Run the whole window rather than breaking on the first `None`: he
        // may have been out on the carry leg when the pile emptied, and
        // "stands idle AT THE DOOR" is half the gate.
        bool idled = false;
        float stallBy = Time.realtimeSinceStartup + 8f;
        while (Time.realtimeSinceStartup < stallBy)
        {
            yield return null;
            idled = sawyerActs != null && sawyerActs.Current == VillagerActing.Mode.None;
            if (idled && Island.FlatDistance(sawyerBody.transform.position, door) < 3f) break;
        }
        float stalledAtMill = Island.FlatDistance(sawyerBody.transform.position, door);
        store.whole = keptWhole; store.part = keptPart;

        sb.AppendLine();
        sb.AppendLine("WHAT HIS BODY IS SAYING:");
        sb.AppendLine($"  working: {(sawed ? "Saw" : sawyerActs != null ? sawyerActs.Current.ToString() : "no actor")}"
            + $"   ·   phase {(sawyerWorker != null ? sawyerWorker.PhaseName : "-")}");
        sb.AppendLine($"  with the timber pile emptied (Stalled {stalledRow}): "
            + $"{(sawyerActs != null ? sawyerActs.Current.ToString() : "-")}, "
            + $"{stalledAtMill:F1} m from the door");
        Gate("a-sawyer-saws", sawed,
            sawyerActs != null ? $"he is {sawyerActs.Current}" : "no VillagerActing on him");
        Gate("a-stalled-mill-reads-as-stalled", stalledRow && idled,
            $"Stalled {stalledRow}, acting {(sawyerActs != null ? sawyerActs.Current.ToString() : "-")}");
        Gate("and-he-waits-at-the-door", stalledAtMill < 4f,
            $"{stalledAtMill:F1} m from the door");

        // --- 5. picked up, put down, back to work -----------------------------

        var lifted = CampWorker.Of(bodyB);
        Vector3 heldAt = bodyB.transform.position;
        lifted.PickedUp();
        float holdUntil = Time.realtimeSinceStartup + 0.6f;
        float drift = 0f;
        while (Time.realtimeSinceStartup < holdUntil)
        {
            yield return null;
            drift = Mathf.Max(drift, Vector3.Distance(bodyB.transform.position, heldAt));
        }
        var dangling = bodyB.GetComponent<VillagerActing>();
        bool dangles = dangling != null && dangling.Current == VillagerActing.Mode.Dangle;

        Vector3 setAt = camp.CampCentre + new Vector3(9f, 0f, -4f);
        setAt.y = camp.GroundAt(setAt);
        lifted.PutDown(setAt);
        Vector3 landedAt = bodyB.transform.position;
        float resumeBy = Time.realtimeSinceStartup + 2f;
        float moved = 0f;
        while (Time.realtimeSinceStartup < resumeBy)
        {
            yield return null;
            moved = Mathf.Max(moved, Vector3.Distance(bodyB.transform.position, landedAt));
        }

        sb.AppendLine();
        sb.AppendLine("PICKED UP AND PUT DOWN:");
        sb.AppendLine($"  held for 0.6 s: drifted {drift * 100f:F1} cm, acting {(dangling != null ? dangling.Current.ToString() : "-")}");
        sb.AppendLine($"  set down {Island.FlatDistance(landedAt, setAt) * 100f:F1} cm from the spot, "
            + $"moved {moved:F2} m in the next 2 s (phase {lifted.PhaseName})");
        Gate("a-held-hand-stops-walking-himself", drift < 0.02f,
            $"{drift * 100f:F1} cm of drift while the Hand held him");
        Gate("and-hangs-from-the-scruff", dangles,
            dangling != null ? dangling.Current.ToString() : "no VillagerActing");
        Gate("put-down-where-he-was-put", Island.FlatDistance(landedAt, setAt) < 0.05f,
            $"{Island.FlatDistance(landedAt, setAt) * 100f:F1} cm out");
        Gate("and-back-at-work-inside-two-seconds", moved > 0.8f,
            $"{moved:F2} m in 2 s");

        // =====================================================================
        // 6. THE TREE THAT COMES DOWN IS THE ONE SOMEBODY IS CHOPPING
        // =====================================================================
        //
        // Kevin, 2026-09-20: *"when collecting wood they seem to cut at random
        // areas while other, random trees disappear, not wanted behavior."*
        // Two choosers: each hand walked to the nearest trunk to HIM, and
        // `SyncFelling` took trees down nearest the CAMP the instant the
        // ledger's count rose. Now the camp hands out the front of its own
        // felling order and the fall waits for the man to be standing at it.
        //
        // Nobody but the camp's own puppets may be on this island from here
        // on. A shore-party hand walking a log into the pile is the SHIP
        // paying the camp — a real feature, gated elsewhere — and it would
        // read here as the villagers cheating, and its felled trees would read
        // as the order going ragged. `PutBackOnStation` moves them without
        // delivering anything, which `ReturnAboard` would.
        int wereAshore = 0;
        if (roster != null)
            foreach (var c in roster.All)
                if (c != null && c.IsAshore) { wereAshore++; c.PutBackOnStation(); }
        roster?.Refresh();
        yield return null;

        var wood = target.GetComponentInChildren<SeaSick.Terrain.SceneryWood>();
        bool wasPaused = TimeOfDay.Paused;

        sb.AppendLine();
        sb.AppendLine("WHO IS STANDING AT THE TREE WHEN IT GOES OVER:");
        if (wood == null || wood.TreeCount == 0)
        {
            sb.AppendLine("  SKIPPED — no welded wood on this island");
            Gate("this-island-has-wood-to-cut", false, "no SceneryWood under it");
        }
        else
        {
            // Two hands on timber. The sawyer stays at his mill, which is the
            // point: an assigned hand is not a cutter and must not claim a
            // tree.
            camp.OrderGather(camp.HandNamed(bodyB.DisplayName), Res.Timber);
            camp.OrderGather(camp.HandNamed(bodyC.DisplayName), Res.Timber);

            // Room to cut into. The ceiling is ten of each and the camp is
            // holding whatever the earlier sections left it; a full pile is a
            // camp that has stopped paying for wood, and this section is about
            // what happens when it does.
            camp.Ledger.stores.Clear();

            // **Start from a settled mesh.** The sections above ran for a
            // minute and a half of real time with the clock going, so the camp
            // may well be standing here owing a tree or two -- which is legal
            // and is the feature, but it would land inside the stretch below
            // as a flush that the stretch did not cause. Leaving and coming
            // straight back is the game's own way of settling it, and it also
            // puts every body in a known place.
            camp.CatchUp();
            camp.ShowHands(false);
            camp.ShowHands(true);
            for (int f = 0; f < 4; f++) yield return null;

            // **The clock is scrubbed by hand, not scaled.** A probe that
            // relied on `TimeOfDay.Scale` would be measuring whether a
            // SkyDirector happened to be in the scene and how fast the editor
            // was running that afternoon. This advances game time off REAL
            // time at a rate chosen so that two hands fell one tree about
            // every fourteen seconds — deliberately slower than a man's walk
            // out and back, because the thing being gated is that somebody is
            // there when it falls.
            TimeOfDay.Paused = true;
            double clock = TimeOfDay.Seconds;
            camp.Ledger.lastTicked = clock;
            camp.CatchUp();
            yield return null;

            const float SecondsPerTree = 14f;
            double gamePerReal =
                TimeOfDay.DayLength / (2.0 * OutpostLedger.TimberPerHandPerDay) / SecondsPerTree;

            // Room in the pile, or nothing is owed and nothing falls: the
            // sawyer section put eight logs on a ten-log pile and the day
            // since then filled the rest (2026-09-21: "0 came down in 62 s").
            // Emptied BEFORE the snapshot so the replay below starts from the
            // same books.
            camp.Ledger.stores.Clear();
            // And the clearing paid for. Making camp fells its clearing and
            // books those trees against `treesFelled` before a log has been
            // cut, so the first `LastClearingFelled` logs out of the ground
            // owe no tree -- by design, see the-ledger-owes-one-tree-per-log.
            // Sixty-two seconds here is five ledger quanta of two hands, 4.0
            // logs, and a four-tree clearing ate exactly that (2026-09-21:
            // "0 came down, 0 flushed" with timberTaken 4.0 at the end of
            // the loop). Start the loop with the debt settled.
            camp.Ledger.timberTaken = Mathf.Max(camp.Ledger.timberTaken, camp.Ledger.treesFelled);
            string startLedger = JsonUtility.ToJson(camp.Ledger);
            double startClock = clock;
            int felledAtStart = camp.Ledger.treesFelled;
            int flushAtStart = camp.FlushedTrees;

            // Two arrays, and the second one is not redundant. `wasDown` is
            // walked and written as fells are spotted; `preDown` is the
            // baseline and is never touched again. The distance gate at the
            // end has to be about the trees THIS section took down: a shore
            // party working the island before the probe reached it leaves real
            // stumps forty metres out, and they are nothing to do with the
            // camp's order.
            var wasDown = new bool[wood.TreeCount];
            var preDown = new bool[wood.TreeCount];
            for (int i = 0; i < wood.TreeCount; i++)
                preDown[i] = wasDown[i] = wood.TreeAt(i).felled;

            // Two snapshots of what each body is doing, because a tree can
            // also come down in somebody else's `CatchUp` — `CampSheet.OnGUI`
            // calls it every GUI event — which lands AFTER this loop's step
            // and before the next one, by which time the man who was swinging
            // at it has shouldered his log and started walking. The snapshot
            // taken immediately before each `CatchUp` is the honest answer to
            // "who was at it"; keeping the previous one too covers the fell
            // that happened in the frame's GUI pass.
            var bodies = new SeaSick.Crew.CrewAgent[] { stood[0], stood[1], stood[2] };
            var phaseNow = new string[3];
            var phaseWas = new string[3];
            var atNow = new Vector3[3];
            var atWas = new Vector3[3];

            int fellSlow = 0, attendedSlow = 0, loneSlow = 0;
            int sameTree = 0;
            float worstAttendedGap = 0f;

            float slowUntil = Time.realtimeSinceStartup + 62f;
            while (Time.realtimeSinceStartup < slowUntil)
            {
                for (int i = 0; i < 3; i++)
                {
                    phaseWas[i] = phaseNow[i];
                    atWas[i] = atNow[i];
                    var w = CampWorker.Of(bodies[i]);
                    phaseNow[i] = w != null ? w.PhaseName : "-";
                    atNow[i] = bodies[i] != null ? bodies[i].transform.position : Vector3.zero;
                }

                // Two men must never be sent to the same trunk.
                var wB = CampWorker.Of(bodyB);
                var wC = CampWorker.Of(bodyC);
                int tB = wB != null ? camp.TreeClaimedBy(wB) : -1;
                int tC = wC != null ? camp.TreeClaimedBy(wC) : -1;
                if (tB >= 0 && tB == tC) sameTree++;

                int flushWas = camp.FlushedTrees;
                clock += Time.unscaledDeltaTime * gamePerReal;
                TimeOfDay.Scrub(clock);
                camp.CatchUp();
                int flushDelta = camp.FlushedTrees - flushWas;

                for (int i = 0; i < wood.TreeCount; i++)
                {
                    if (wasDown[i] || !wood.TreeAt(i).felled) continue;
                    wasDown[i] = true;
                    fellSlow++;
                    Vector3 root = wood.TreeAt(i).baseAt;
                    float gap = 99f;
                    for (int k = 0; k < 3; k++)
                    {
                        if (phaseNow[k] == "Working")
                            gap = Mathf.Min(gap, Island.FlatDistance(atNow[k], root));
                        if (phaseWas[k] == "Working")
                            gap = Mathf.Min(gap, Island.FlatDistance(atWas[k], root));
                    }
                    if (gap < 3f) { attendedSlow++; worstAttendedGap = Mathf.Max(worstAttendedGap, gap); }
                    else if (flushDelta > 0) { }        // counted as a flush below
                    else loneSlow++;
                }

                yield return null;
            }
            int flushSlow = camp.FlushedTrees - flushAtStart;

            sb.AppendLine($"  a tree about every {SecondsPerTree:F0} s for 62 s: "
                + $"{fellSlow} came down, {attendedSlow} with a man swinging at them "
                + $"(worst {worstAttendedGap:F2} m), {flushSlow} flushed, {loneSlow} with nobody near");
            sb.AppendLine($"  the two cutters claimed the same trunk on {sameTree} frames");

            // A section that felled nothing proved nothing, which is a failure
            // and not a pass — the trap `CampProbe.she-got-back` is a note
            // about.
            Gate("watching-a-camp-fells-several-trees", fellSlow >= 2,
                $"{fellSlow} trees in 62 s of cutting");
            Gate("every-tree-that-fell-had-a-man-swinging-at-it",
                fellSlow >= 2 && attendedSlow + flushSlow >= fellSlow && loneSlow == 0,
                $"{attendedSlow} attended + {flushSlow} flushed of {fellSlow}, {loneSlow} unexplained");
            Gate("and-at-the-pace-of-the-day-none-of-them-were-flushed",
                flushSlow == 0, $"{flushSlow} trees came down with nobody at them");
            Gate("two-cutters-never-claim-the-same-tree", sameTree == 0,
                $"{sameTree} frames with both men on one trunk");

            // --- and a scrubbed clock catches the mesh up at once -------------
            //
            // Rule (c): the ledger running far ahead of the mesh is not a man
            // walking, it is a jump, and the wait would read as a bug. Three
            // days at once, with the pile emptied so the ceiling does not stop
            // the cutting before it starts.
            double clockBeforeClear = clock;
            camp.Ledger.stores.Clear();
            clock += 3.0 * TimeOfDay.DayLength;
            TimeOfDay.Scrub(clock);
            int flushBeforeJump = camp.FlushedTrees;
            camp.CatchUp();
            yield return null;
            int owedAfterJump = camp.TreesOwed;
            int jumpFlush = camp.FlushedTrees - flushBeforeJump;
            sb.AppendLine($"  three days scrubbed past in one call: {jumpFlush} trees flushed, "
                + $"{owedAfterJump} still owed");
            Gate("a-scrubbed-clock-catches-the-mesh-up-at-once",
                owedAfterJump == 0 && jumpFlush > 0,
                $"{owedAfterJump} owed, {jumpFlush} flushed");

            // --- she sails: nothing may be left owed -------------------------
            //
            // The game's own leave path, in the game's own order — see
            // `AnchorController.StowCampHands`, which catches up first and
            // lowers the flag second. That ORDER is why the flush lives inside
            // `ShowHands(false)`: at the moment of the `CatchUp` the camp is
            // still being watched and the lag is still legal.
            camp.CatchUp();
            camp.ShowHands(false);
            yield return null;

            int owedOnSailing = camp.TreesOwed;
            int newDown = 0;
            float furthestNew = 0f, nearestStanding = float.MaxValue;
            for (int i = 0; i < wood.TreeCount; i++)
            {
                bool down = wood.TreeAt(i).felled;
                float d = Island.FlatDistance(wood.TreeAt(i).baseAt, camp.CampCentre);
                if (down && !preDown[i]) { newDown++; furthestNew = Mathf.Max(furthestNew, d); }
                if (!down) nearestStanding = Mathf.Min(nearestStanding, d);
            }
            int felledDelta = camp.Ledger.treesFelled - felledAtStart;

            sb.AppendLine($"  after she stops looking: {owedOnSailing} owed, "
                + $"treesFelled {felledAtStart} -> {camp.Ledger.treesFelled} "
                + $"(timberTaken {camp.Ledger.timberTaken:F2}), {newDown} new stumps in the mesh");
            sb.AppendLine($"  this camp's stumps out to {furthestNew:F1} m, nearest tree still "
                + $"standing {nearestStanding:F1} m");

            Gate("nothing-is-owed-when-she-sails", owedOnSailing == 0,
                $"{owedOnSailing} trees the ledger paid for and the mesh has not shown");
            // **Plus the clearing credit, and that is not slop.** Making camp
            // fells the wood it stands on and books those trees against
            // `treesFelled` without them ever having been logs out of the
            // standing stock — which is the fix for the day the probe reported
            // "4 logs out of the clearing, 8 trees down". So the honest
            // statement is: the mesh is never BEHIND the books, and is never
            // ahead of them by more than that one clearing.
            int owedByLogs = Mathf.FloorToInt(camp.Ledger.timberTaken);
            Gate("the-ledger-owes-one-tree-per-log",
                camp.Ledger.treesFelled >= owedByLogs
                && camp.Ledger.treesFelled - owedByLogs <= camp.LastClearingFelled,
                $"{camp.Ledger.treesFelled} felled against {owedByLogs} logs out of the "
                + $"ground and a {camp.LastClearingFelled}-tree clearing");
            // **The SET, not the count.** Nearest-to-the-camp outward with no
            // holes in it is what makes the wood a pure function of one
            // integer: come back after the island has streamed out and in, and
            // the same ledger reproduces the same stumps. A hole would be
            // invisible today and a different wood tomorrow.
            Gate("the-wood-that-is-down-is-the-nearest-first-set",
                felledDelta > 0 && newDown == felledDelta
                && nearestStanding >= furthestNew - 0.01f,
                $"{newDown} stumps against {felledDelta} on the books; a tree still stands at "
                + $"{nearestStanding:F2} m inside a stump at {furthestNew:F2} m");

            // --- D2's companion: the choreography moved no number -------------
            //
            // The same span of game time, ticked once with nobody looking,
            // against what the watched camp came out with. Measured AFTER the
            // flush on purpose: `treesFelled` is allowed to lag while she is
            // standing there, and comparing mid-lag would be gating the
            // feature against itself.
            string watchedJson = JsonUtility.ToJson(camp.Ledger);
            JsonUtility.FromJsonOverwrite(startLedger, camp.Ledger);
            TimeOfDay.Scrub(startClock);
            camp.CatchUp();
            // The same two legs the watched camp had, with the pile emptied
            // between them at the same instant. A replay that skipped the
            // clear ran into the ceiling the watched camp never met, and read
            // "DIFFERENT" for a difference the probe itself had made.
            TimeOfDay.Scrub(clockBeforeClear);
            camp.CatchUp();
            camp.Ledger.stores.Clear();
            TimeOfDay.Scrub(clock);
            camp.CatchUp();
            string unwatchedJson = JsonUtility.ToJson(camp.Ledger);
            JsonUtility.FromJsonOverwrite(watchedJson, camp.Ledger);

            sb.AppendLine($"  the same span ticked with nobody looking: "
                + $"{(watchedJson == unwatchedJson ? "identical" : "DIFFERENT")}");
            if (watchedJson != unwatchedJson)
            {
                sb.AppendLine("    watched:   " + Clip(watchedJson));
                sb.AppendLine("    unwatched: " + Clip(unwatchedJson));
            }
            Gate("choreographing-the-felling-changed-no-number",
                watchedJson == unwatchedJson,
                "the watched camp and the unwatched one came out with different books");

            // Put the bodies back and let them settle, because the section
            // below is about what they do while somebody is watching.
            camp.ShowHands(true);
            for (int f = 0; f < 4; f++) yield return null;
        }

        // --- 7. D2: thirty seconds of villagers changes nothing ---------------

        TimeOfDay.Paused = true;
        // One catch-up FIRST, so the snapshot is taken after whatever the
        // arithmetic owed itself — otherwise the ceiling `CatchUp` pushes in
        // would look like the villagers having written to the ledger.
        camp.CatchUp();
        yield return null;

        string before = JsonUtility.ToJson(camp.Ledger);
        int felledBefore = wood != null ? wood.FelledInMesh() : -1;
        int movingHands = 0;

        float d2Until = Time.realtimeSinceStartup + 30f;
        Vector3[] seen = { stood[0].transform.position, stood[1].transform.position, stood[2].transform.position };
        while (Time.realtimeSinceStartup < d2Until)
        {
            yield return null;
            for (int i = 0; i < 3; i++)
                if (Vector3.Distance(stood[i].transform.position, seen[i]) > 2f)
                { movingHands |= 1 << i; seen[i] = stood[i].transform.position; }
        }

        string after = JsonUtility.ToJson(camp.Ledger);
        int felledAfter = wood != null ? wood.FelledInMesh() : -1;
        TimeOfDay.Paused = wasPaused;

        int walkers = 0;
        for (int i = 0; i < 3; i++) if ((movingHands & (1 << i)) != 0) walkers++;

        sb.AppendLine();
        sb.AppendLine("WATCHING MUST NOT BEAT LEAVING (D2):");
        sb.AppendLine($"  30 s of watched villagers with the clock stopped, {walkers} of 3 covering ground "
            + $"({wereAshore} shore-party hands put back aboard first)");
        sb.AppendLine($"  ledger JSON {(before == after ? "identical" : "CHANGED")}, "
            + $"trees down in the mesh {felledBefore} -> {felledAfter}");
        if (before != after)
        {
            sb.AppendLine("    before: " + Clip(before));
            sb.AppendLine("    after:  " + Clip(after));
        }
        // If nobody walked, the gate below proved nothing. That is a failure,
        // not a pass — the same trap `CampProbe.she-got-back` is a note about.
        Gate("the-villagers-were-actually-working", walkers >= 2,
            $"{walkers} of 3 moved more than 2 m in 30 s");
        Gate("and-changed-not-one-number", before == after,
            "the ledger JSON moved while nothing but bodies were running");
        Gate("and-felled-not-one-tree", felledBefore == felledAfter,
            $"{felledAfter - felledBefore} trees came down");

        // --- 7. the tree grid agrees with the scan it replaced ----------------

        var ix = TreeIndex.For(camp);
        int checkedQ = 0, disagreed = 0;
        double gridTicks = 0, scanTicks = 0;
        if (ix != null && wood != null && wood.TreeCount > 0)
        {
            var watch = new System.Diagnostics.Stopwatch();
            Random.InitState(20260920);
            for (int q = 0; q < 200; q++)
            {
                Vector2 o = Random.insideUnitCircle * 80f;
                Vector3 at = camp.CampCentre + new Vector3(o.x, 0f, o.y);

                watch.Restart();
                bool got = ix.NearestStanding(at, 34f, out Vector3 gridAt);
                watch.Stop();
                gridTicks += watch.Elapsed.TotalMilliseconds;

                watch.Restart();
                bool any = false; float best = 34f * 34f; Vector3 scanAt = Vector3.zero;
                for (int i = 0; i < wood.TreeCount; i++)
                {
                    var t = wood.TreeAt(i);
                    if (t.felled) continue;
                    float dx = t.baseAt.x - at.x, dz = t.baseAt.z - at.z;
                    float m = dx * dx + dz * dz;
                    if (m < best) { best = m; scanAt = t.baseAt; any = true; }
                }
                watch.Stop();
                scanTicks += watch.Elapsed.TotalMilliseconds;

                checkedQ++;
                if (got != any) { disagreed++; continue; }
                if (!got) continue;
                // Ties are legitimate: two trees at the same range are two
                // right answers. Compare the DISTANCE, not the index.
                float dg = Island.FlatDistance(gridAt, at);
                float ds = Island.FlatDistance(scanAt, at);
                if (Mathf.Abs(dg - ds) > 0.01f) disagreed++;
            }
        }
        float perQuery = checkedQ > 0 ? (float)(gridTicks / checkedQ) : 999f;

        sb.AppendLine();
        sb.AppendLine("THE TREE GRID:");
        sb.AppendLine($"  {checkedQ} random queries over {(wood != null ? wood.TreeCount : 0)} trees: "
            + $"{disagreed} disagreed with a full scan");
        sb.AppendLine($"  grid {perQuery * 1000f:F1} us a query against "
            + $"{(checkedQ > 0 ? scanTicks / checkedQ * 1000.0 : 0.0):F1} us for the scan");
        Gate("the-grid-answers-what-the-scan-answered",
            checkedQ >= 200 && disagreed == 0, $"{disagreed} of {checkedQ} disagreed");
        Gate("and-answers-it-in-under-50-us", checkedQ > 0 && perQuery < 0.05f,
            $"{perQuery * 1000f:F1} us a query");

        Finish();
    }

    static string Clip(string s) => s.Length <= 300 ? s : s.Substring(0, 300) + "…";

    void Fail(string what)
    {
        Report("FAIL: " + what + "\n");
        Destroy(gameObject);
    }

    void Finish()
    {
        sb.AppendLine();
        sb.AppendLine(fails == 0
            ? "PASS — they walk to their work, act it, carry it, and change nothing by it"
            : $"{fails} GATE(S) FAILED");
        Report(sb.ToString());
        Destroy(gameObject);
    }

    void Gate(string name, bool ok, string detail)
    {
        if (!ok) fails++;
        sb.AppendLine($"  [{(ok ? "ok  " : "FAIL")}] {name}   {(ok ? "" : detail)}");
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

    static void Report(string text)
    {
        Debug.Log("CampLifeProbe\n" + text);
        var path = System.IO.Path.Combine(Application.dataPath, "../Logs/CampLifeProbe.txt");
        try { System.IO.File.WriteAllText(path, text); } catch { }
    }
}
