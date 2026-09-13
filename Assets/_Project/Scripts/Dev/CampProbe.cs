using System.Text;
using UnityEngine;
using SeaSick.Ship;
using SeaSick.World;

/// **Press the button and watch.** Sail her to a real island, land, make camp,
/// and then look at what is actually on the ground.
///
/// The shape of this is copied from `LandProbe`'s lesson deliberately: a
/// geometry survey once said all fifteen islands were landable and every beach
/// test passed, and you still could not gather a log, because everything AFTER
/// the prompt was broken. So nothing here asks whether making camp *would*
/// work. It makes one, then counts the trees that came down IN THE MESH, reads
/// the fire off the scene, and reads the pile off the ledger.
///
/// Measuring the artefact, not the rule that produced it: `FelledInMesh`
/// counts vertex runs that have collapsed, not the `felled` flags that the
/// felling set.
public class CampProbe : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("CampProbe: not in play mode"); return; }
        var runner = new GameObject("CampProbeRunner").AddComponent<CampProbe>();
        runner.StartCoroutine(runner.Run());
    }

    System.Collections.IEnumerator Run()
    {
        var sb = new StringBuilder();
        int fails = 0;

        var anchor = Object.FindFirstObjectByType<AnchorController>();
        var motor = Object.FindFirstObjectByType<ShipMotor>();
        var cam = Object.FindFirstObjectByType<SeaSick.CameraRig.ChaseCamera>();
        if (anchor == null || motor == null)
        {
            Report("FAIL: no ship in the scene\n");
            Destroy(gameObject);
            yield break;
        }

        // --- pick somewhere with a beach and some wood on it ------------------
        //
        // Ranked by shore gap, never by centre distance: in a world of lobed
        // islands a ship 20 m off a small island's beach is routinely nearer
        // the CENTRE of a big one, and that exact mistake once cost 12 of 34
        // approach bearings their landing prompt.

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

                // Ask the SAME three questions `AnchorController` asks, in the
                // same order. Checking only the beach says "yes" on a shore
                // where no prompt ever appears, because the first question
                // already picked a different island — a ship 18 m off a small
                // island is routinely nearer the CENTRE of a big one, and this
                // probe was told "no island in range" at a spot its own scan
                // had called landable.
                var think = Island.Nearest(ship);
                if (think != isle) continue;
                if (Island.FlatDistance(ship, isle.transform.position)
                    > isle.RadiusToward(ship) + 30f) continue;
                if (!isle.HasBeachToward(ship)) continue;

                float gap = Island.FlatDistance(ship, from);
                if (gap < bestGap) { bestGap = gap; target = isle; standOff = ship; }
            }
        }

        if (target == null)
        {
            Report("FAIL: no island in the world offers a beach to land on\n");
            Destroy(gameObject);
            yield break;
        }

        sb.AppendLine($"target {target.name}  r {target.Radius:F0} m, "
            + $"{bestGap:F0} m from where she started");

        // She starts the game tied up at her own berth, so she is Anchored and
        // `TryLand` refuses before it looks at anything. Let go first — through
        // the real call, because `CastOff` is the one place that knows how to
        // stop being moored and a second copy of that routine is exactly the
        // fault `GetUnderway` was written to end.
        if (anchor.CurrentState != AnchorController.State.Underway)
        {
            anchor.CastOff();
            float cast = Time.realtimeSinceStartup;
            while (anchor.CurrentState != AnchorController.State.Underway
                   && Time.realtimeSinceStartup - cast < 10f)
                yield return null;
            sb.AppendLine($"cast off: now {anchor.CurrentState}");
        }

        // Re-place and ask again for a second or two rather than warping once.
        //
        // She has way on after casting off, so a single warp plus two frames
        // can leave her drifting off the spot before the landing test runs --
        // measured, intermittently, as "no island in range" at a position this
        // probe's own scan had just called landable. The return leg needed the
        // same treatment and got it first; the arrival leg was left fragile.
        bool landed = false; string why = "never tried";
        float landBy = Time.realtimeSinceStartup + 4f;
        while (!landed && Time.realtimeSinceStartup < landBy)
        {
            Warp(motor, standOff, Quaternion.LookRotation(target.transform.position - standOff));
            yield return new WaitForFixedUpdate();
            yield return null;
            landed = anchor.TryLand(out why);
        }
        sb.AppendLine($"land: {(landed ? "yes" : "NO")} — {why}");
        Gate(sb, ref fails, "she-can-land-there", landed, why);
        if (!landed) { Finish(sb, fails); yield break; }

        // The survey runs over frames. Wait for it rather than assuming, and
        // time how long the player would have waited.
        float t0 = Time.realtimeSinceStartup;
        while (Outpost.Surveying(target) && Time.realtimeSinceStartup - t0 < 30f)
            yield return null;
        float waited = Time.realtimeSinceStartup - t0;

        // Landing must no longer empty the ship. This is the change that lets
        // the sheet mean anything: arriving used to put the whole crew on the
        // beach before the player had been asked who should go.
        int aboardOnArrival = 0;
        var roster0 = Object.FindFirstObjectByType<SeaSick.Crew.CrewRoster>();
        foreach (var c in roster0.All) if (c != null && c.IsAboard) aboardOnArrival++;
        sb.AppendLine($"aboard after landing: {aboardOnArrival} of {roster0.All.Length}");
        Gate(sb, ref fails, "landing-does-not-empty-the-ship",
            aboardOnArrival == roster0.All.Length,
            $"{aboardOnArrival} of {roster0.All.Length} still aboard");

        var outpost = Outpost.Of(target);
        sb.AppendLine($"survey: {(outpost != null ? "ground found" : "refused")} "
            + $"after {waited:F2} s of waiting");
        Gate(sb, ref fails, "the-ground-was-surveyed", Outpost.Surveyed(target),
            "still not looked at");
        if (outpost == null)
        {
            sb.AppendLine("  (this island will not take a camp — a real answer, "
                + "but this probe needs one that will)");
            Finish(sb, fails); yield break;
        }

        // --- the camera should have left the water ---------------------------

        yield return null;
        bool overview = cam != null && cam.Overview.HasValue;
        sb.AppendLine($"camera: overview {(overview ? "engaged" : "NOT engaged")}"
            + (overview ? $", framing {cam.Overview.Value.radius:F0} m "
                + $"centred {cam.Overview.Value.centre.x:F0},{cam.Overview.Value.centre.z:F0}" : ""));
        Gate(sb, ref fails, "birds-eye-at-a-plain-island", overview,
            "the overview only engages at the home dock");

        // --- the player's hands on the view ----------------------------------

        var icam = Object.FindFirstObjectByType<SeaSick.CameraRig.IslandCam>();
        if (icam != null && overview)
        {
            // Untouched, the shot must be EXACTLY the shipped composition --
            // the authored zoom, the legibility clamp still on, the ship still
            // dragged into frame. A camera feature that changes the default
            // view has changed something nobody asked it to.
            var asShipped = cam.Overview.Value;
            sb.AppendLine($"view: {asShipped.ground:F0} m of ground, free {asShipped.free}"
                + $"   (range {icam.MinGround:F0}–{icam.MaxGround:F0} m, default {icam.DefaultGround:F0})");
            Gate(sb, ref fails, "an-untouched-view-is-the-shipped-one",
                !asShipped.free
                && Mathf.Abs(asShipped.ground - icam.DefaultGround) < 1f,
                $"ground {asShipped.ground:F1}, free {asShipped.free}");

            // Zoom right out, then right in, through the same path the keys take.
            // **Read AFTER the camera has run, not merely after a frame.**
            // `LastOverviewSpan` is written in the rig's LateUpdate and
            // coroutines resume before it, so a single `yield return null`
            // reads the span from BEFORE the change -- which reported the
            // zoomed-out shot as 249 m back and the zoomed-in one as 786 m,
            // exactly inverted, and looked like a broken feature rather than a
            // mis-timed measurement.
            icam.ZoomTo(10000f); icam.SnapToTarget();
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            float wideGround = cam.Overview.Value.ground;
            float wideSpan = cam.LastOverviewSpan;

            icam.ZoomTo(0f); icam.SnapToTarget();
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            float closeGround = cam.Overview.Value.ground;
            float closeSpan = cam.LastOverviewSpan;

            sb.AppendLine($"  zoomed out: {wideGround:F0} m of ground at {wideSpan:F0} m back");
            sb.AppendLine($"  zoomed in:  {closeGround:F0} m of ground at {closeSpan:F0} m back");
            Gate(sb, ref fails, "zoom-is-clamped-both-ways",
                Mathf.Abs(wideGround - icam.MaxGround) < 1f
                && Mathf.Abs(closeGround - icam.MinGround) < 1f,
                $"{closeGround:F0}–{wideGround:F0} against {icam.MinGround:F0}–{icam.MaxGround:F0}");
            // The whole point: zooming out must actually move the camera back,
            // which the legibility clamp used to forbid.
            Gate(sb, ref fails, "zooming-out-really-pulls-back", wideSpan > closeSpan * 2f,
                $"{closeSpan:F0} m -> {wideSpan:F0} m");

            // Pan, and it must stay over the island.
            icam.PanTo(new Vector3(100000f, 0f, 0f)); icam.SnapToTarget();
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            float walked = (cam.Overview.Value.centre - asShipped.centre).magnitude;
            sb.AppendLine($"  panned as far as it goes: {walked:F0} m off centre "
                + $"(island r {target.Radius:F0} m)");
            Gate(sb, ref fails, "pan-stays-over-the-island",
                walked > 1f && walked <= Mathf.Max(60f, target.Radius * 1.16f) + 1f,
                $"{walked:F0} m from centre on an island of r {target.Radius:F0}");

            // Put it back so the rest of the probe sees the normal view.
            icam.PanTo(Vector3.zero);
            icam.ZoomTo(icam.DefaultGround);
            icam.SnapToTarget();
            yield return null;
        }

        // --- make camp --------------------------------------------------------

        var wood = target.GetComponentInChildren<SeaSick.Terrain.SceneryWood>();
        int standingBefore = wood != null ? wood.TreeCount - wood.FelledInMesh() : -1;

        sb.AppendLine();
        sb.AppendLine($"outpost: sited {outpost.Sited}, clearing {outpost.ClearingRadius:F1} m "
            + $"at {outpost.ClearingCentre.x:F0},{outpost.ClearingCentre.z:F0} "
            + $"standing {(Island.TerrainHeight != null ? Island.TerrainHeight(outpost.ClearingCentre.x, outpost.ClearingCentre.z) : 0f):F2} m");
        sb.AppendLine($"  fires already under this island: "
            + $"{target.GetComponentsInChildren<Campfire>().Length}");

        int felled = outpost.MakeCamp(out string whyNot);
        if (felled < 0) sb.AppendLine($"  MakeCamp refused: {whyNot}");
        yield return null;

        int standingAfter = wood != null ? wood.TreeCount - wood.FelledInMesh() : -1;
        int downInMesh = standingBefore - standingAfter;

        sb.AppendLine();
        sb.AppendLine($"MAKE CAMP on {target.name}:");
        sb.AppendLine($"  reported {felled} logs out of the clearing");
        sb.AppendLine($"  trees standing in the MESH: {standingBefore} -> {standingAfter} "
            + $"({downInMesh} came down)");
        sb.AppendLine($"  keeps {outpost.StoreCapacity}, ledger ceiling {outpost.Ledger?.ceiling}, "
            + $"pile {outpost.Ledger?.timber}");

        Gate(sb, ref fails, "the-camp-exists", outpost.HasCamp, "no campfire raised");
        Gate(sb, ref fails, "the-fire-is-on-the-ground",
            target.GetComponentInChildren<Campfire>() != null, "no Campfire in the scene");
        Gate(sb, ref fails, "making-camp-fells-its-own-site",
            wood == null || downInMesh == felled,
            $"reported {felled}, mesh says {downInMesh}");
        Gate(sb, ref fails, "a-camp-gives-the-place-a-ceiling",
            outpost.StoreCapacity == OutpostLedger.CampfireCeiling,
            $"keeps {outpost.StoreCapacity}, expected {OutpostLedger.CampfireCeiling}");
        Gate(sb, ref fails, "one-definition-of-the-ceiling",
            outpost.Ledger != null && outpost.Ledger.ceiling == outpost.StoreCapacity,
            $"ledger {outpost.Ledger?.ceiling} vs outpost {outpost.StoreCapacity}");

        // Making camp twice must not raise a second fire.
        int again = outpost.MakeCamp();
        Gate(sb, ref fails, "camp-is-made-once", again < 0 && outpost.CountOf("Campfire") == 1,
            $"second attempt returned {again}, {outpost.CountOf("Campfire")} fires");

        // --- and it has to actually produce ----------------------------------
        //
        // Put two hands on the wood and wind the clock. The ledger is the only
        // thing that produces; nobody has to be standing here.

        var l = outpost.Ledger;
        l.timber = 0; l.timberPart = 0f;
        l.hands.Clear();
        l.hands.Add(new OutpostHand { name = "probe-1", order = OutpostOrder.Cut });
        l.hands.Add(new OutpostHand { name = "probe-2", order = OutpostOrder.Cut });
        l.lastTicked = TimeOfDay.Seconds;

        double twoDays = TimeOfDay.Seconds + 2.0 * TimeOfDay.DayLength;
        l.ceiling = outpost.StoreCapacity;
        l.Tick(twoDays);

        sb.AppendLine();
        sb.AppendLine($"TWO HANDS, TWO GAME DAYS with nobody watching: "
            + $"{l.timber} logs of a possible {l.ceiling}, {l.standing:F0} still standing");
        Gate(sb, ref fails, "an-absent-camp-produces", l.timber > 0,
            $"{l.timber} logs after two days");

        // --- leave two hands, sail away, come back ---------------------------
        //
        // The whole point of the design, driven end to end. Nothing here asks
        // whether it WOULD work: it stations real crew, gets her underway,
        // moves her a kilometre off, brings her back and reads what is there.

        var roster = Object.FindFirstObjectByType<SeaSick.Crew.CrewRoster>();
        int aboardBefore = 0;
        foreach (var c in roster.All) if (c != null && c.IsAboard) aboardBefore++;

        l.hands.Clear();
        l.timber = 0; l.timberPart = 0f;
        l.lastTicked = TimeOfDay.Seconds;

        // NOT filtered on `IsAboard`. Landing still sends the whole crew
        // ashore on the old harvest errand, so at this moment there is nobody
        // "aboard" to assign -- which is the design conflict this probe found
        // and which is Kevin's call, not mine. Stationing works from either
        // state, so the probe takes whoever is there.
        int left = 0;
        foreach (var c in roster.All)
        {
            if (c == null) continue;
            if (left >= 2) break;
            if (outpost.Station(c)) left++;
        }
        roster.Refresh();

        int aboardAfter = 0;
        foreach (var c in roster.All) if (c != null && c.IsAboard) aboardAfter++;

        sb.AppendLine();
        sb.AppendLine($"LEAVING {left} HANDS: aboard {aboardBefore} -> {aboardAfter}, "
            + $"ledger holds {l.hands.Count}");
        Gate(sb, ref fails, "two-hands-can-be-left", left == 2,
            $"stationed {left}");
        Gate(sb, ref fails, "the-ledger-has-them", l.hands.Count == left,
            $"{l.hands.Count} rows for {left} hands");

        // Cast off. The bodies must go away with her gone, not fall through
        // terrain that is about to stream out.
        // Get any shore party back aboard first.
        //
        // Landing still sends the WHOLE crew ashore on the old harvest errand,
        // and she cannot weigh with people on the beach — so without this she
        // stays `Ashore`, never leaves, and the gates below pass on a voyage
        // that did not happen. `ReturnAboard` is the same call the recall
        // button makes.
        foreach (var c in roster.All)
            if (c != null && c.IsAshore) c.ReturnAboard();
        float back = Time.realtimeSinceStartup;
        while (anchor.CurrentState == AnchorController.State.Ashore
               && Time.realtimeSinceStartup - back < 25f)
            yield return null;
        sb.AppendLine($"  shore party recalled: now {anchor.CurrentState}");

        anchor.CastOff();
        float cast2 = Time.realtimeSinceStartup;
        while (anchor.CurrentState != AnchorController.State.Underway
               && Time.realtimeSinceStartup - cast2 < 10f)
            yield return null;
        sb.AppendLine($"  cast off: now {anchor.CurrentState}");
        int awake = 0;
        foreach (var a in outpost.Parked())
            if (a != null && a.gameObject.activeSelf && outpost.HandNamed(a.DisplayName) != null) awake++;
        sb.AppendLine($"  after casting off: {awake} of {left} bodies still drawn");
        Gate(sb, ref fails, "she-actually-left",
            anchor.CurrentState == AnchorController.State.Underway,
            $"still {anchor.CurrentState}");
        Gate(sb, ref fails, "the-camp-costs-nothing-once-she-is-gone", awake == 0,
            $"{awake} still active");

        // A kilometre out, and time passes.
        Vector3 away = standOff + (standOff - target.transform.position).normalized * 1000f;
        Warp(motor, away, motor.transform.rotation);
        double sailedFor = 3.0 * TimeOfDay.DayLength;
        TimeOfDay.Scrub(TimeOfDay.Seconds + sailedFor);
        yield return null;

        // Home again.
        // Coming BACK is not the same as arriving the first time: she is
        // under way with the helm set, so one warp and two frames leaves her
        // drifting off the spot before the landing test runs. Re-place her and
        // ask again for a second or two, which is what a player does anyway.
        bool landedAgain = false; string why2 = "never tried";
        float tryUntil = Time.realtimeSinceStartup + 4f;
        while (!landedAgain && Time.realtimeSinceStartup < tryUntil)
        {
            Warp(motor, standOff, Quaternion.LookRotation(target.transform.position - standOff));
            yield return new WaitForFixedUpdate();
            yield return null;
            landedAgain = anchor.TryLand(out why2);
        }
        yield return null;

        outpost.CatchUp();
        int drawnBack = 0;
        foreach (var a in outpost.Parked())
            if (a != null && a.gameObject.activeSelf && outpost.HandNamed(a.DisplayName) != null) drawnBack++;

        sb.AppendLine();
        sb.AppendLine($"BACK AFTER THREE GAME DAYS ({(landedAgain ? "landed" : "could not land: " + why2)}):");
        sb.AppendLine($"  pile {l.timber} / {l.ceiling}   standing {l.standing:F0}   "
            + $"bodies drawn {drawnBack} of {left}");
        Gate(sb, ref fails, "work-was-done-while-she-was-away", l.timber > 0,
            $"{l.timber} logs after three days away");
        Gate(sb, ref fails, "the-hands-are-still-there", l.hands.Count == left,
            $"{l.hands.Count} of {left} still in the ledger");
        // **Not `!landedAgain || ...`.** That is how a gate passes because the
        // thing it was meant to test never ran -- this exact line once went
        // green on a voyage that never left the beach. If she could not get
        // back, that IS the failure.
        Gate(sb, ref fails, "she-got-back", landedAgain, why2);
        Gate(sb, ref fails, "and-they-are-drawn-again-on-arrival",
            landedAgain && drawnBack == left, $"{drawnBack} of {left} drawn");

        // And you can take them back.
        var first = outpost.Parked().Length > 0 ? System.Array.Find(outpost.Parked(),
            a => a != null && outpost.HandNamed(a.DisplayName) != null) : null;
        bool tookBack = first != null && outpost.Recall(first, anchor.transform);
        roster.Refresh();
        sb.AppendLine($"  recalled one: {tookBack}, ledger now {l.hands.Count}");
        Gate(sb, ref fails, "a-hand-can-come-back-aboard",
            tookBack && l.hands.Count == left - 1,
            $"recall {tookBack}, {l.hands.Count} left in the ledger");

        Finish(sb, fails);
    }

    void Finish(StringBuilder sb, int fails)
    {
        sb.AppendLine();
        sb.AppendLine(fails == 0 ? "PASS — she lands, the ground is surveyed, the camp is made"
                                 : $"{fails} GATE(S) FAILED");
        Report(sb.ToString());
        Destroy(gameObject);
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

    static void Gate(StringBuilder sb, ref int fails, string name, bool ok, string detail)
    {
        if (!ok) fails++;
        sb.AppendLine($"  [{(ok ? "ok  " : "FAIL")}] {name}   {(ok ? "" : detail)}");
    }

    static void Report(string text)
    {
        Debug.Log("CampProbe\n" + text);
        var path = System.IO.Path.Combine(Application.dataPath, "../Logs/CampProbe.txt");
        try { System.IO.File.WriteAllText(path, text); } catch { }
    }
}
