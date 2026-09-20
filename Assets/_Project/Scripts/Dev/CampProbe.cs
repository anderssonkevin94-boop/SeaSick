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

        // --- the player sites it, the crew build it --------------------------
        //
        // Kevin's flow, 2026-09-19: the button no longer makes a camp, it puts
        // a BLUEPRINT on the ground where the player points, and the hands
        // left behind cut the wood that builds it. So the probe does what the
        // player does -- picks a spot off the ground, sites there, leaves
        // somebody, and lets the clock do the rest.

        // Somewhere off the surveyed clearing, so this cannot pass by
        // accidentally doing what the old spiral did. Walk out until the
        // ground says yes.
        Vector3 spot = outpost.ClearingCentre;
        string placeWhy = "";
        bool found = false;
        for (int ring = 0; ring < 6 && !found; ring++)
        {
            float rad = 6f + ring * 5f;
            for (int i = 0; i < 12 && !found; i++)
            {
                float a = i * Mathf.PI * 2f / 12f + ring;
                var p = outpost.ClearingCentre
                    + new Vector3(Mathf.Cos(a) * rad, 0f, Mathf.Sin(a) * rad);
                p.y = SeaSick.CameraRig.GroundPick.Height != null
                    ? SeaSick.CameraRig.GroundPick.Height(p.x, p.z) : p.y;
                if (outpost.CanPlace(BuildPlans.Campfire, p, out placeWhy))
                {
                    spot = p;
                    found = true;
                }
            }
        }
        float offClearing = Vector3.Distance(
            new Vector3(spot.x, 0f, spot.z),
            new Vector3(outpost.ClearingCentre.x, 0f, outpost.ClearingCentre.z));

        // A ray straight down on to that spot has to land on it. This is the
        // pick the player's thumb goes through, and it is analytic against the
        // height field rather than a Physics.Raycast because colliders only
        // exist within one chunk of the ship -- the far half of any island in
        // the bird's-eye has none.
        bool picked = SeaSick.CameraRig.GroundPick.Along(
            new Ray(spot + Vector3.up * 400f, Vector3.down), out Vector3 pickHit);
        float pickErr = picked ? Vector3.Distance(pickHit, spot) : 999f;

        int wanted = outpost.Site(BuildPlans.Campfire, spot, out string whyNot);
        if (wanted < 0) sb.AppendLine($"  Site refused: {whyNot}");
        yield return null;

        var pending = outpost.Ledger != null ? outpost.Ledger.pending : null;
        int standingAtSiting = wood != null ? wood.TreeCount - wood.FelledInMesh() : -1;

        sb.AppendLine();
        sb.AppendLine($"SITE A CAMP at a point {offClearing:F1} m off the surveyed clearing:");
        sb.AppendLine($"  ground pick from 400 m up: "
            + (picked ? $"hit, {pickErr * 100f:F1} cm from the spot" : "MISSED"));
        sb.AppendLine($"  blueprint wants {wanted} logs; "
            + $"trees standing in the MESH {standingBefore} -> {standingAtSiting}");

        Gate(sb, ref fails, "a-spot-off-the-clearing-will-take-a-camp", found,
            found ? $"{offClearing:F1} m out" : $"nowhere in 30 m: {placeWhy}");
        Gate(sb, ref fails, "the-ground-pick-lands-on-the-ground",
            picked && pickErr < 0.25f,
            picked ? $"{pickErr * 100f:F1} cm" : "ray missed the height field");
        Gate(sb, ref fails, "siting-writes-a-blueprint",
            pending != null && pending.planId == BuildPlans.Campfire.id
            && pending.needed == BuildPlans.Campfire.cost,
            pending != null ? $"{pending.planId}, {pending.needed} logs" : "no pending row");
        Gate(sb, ref fails, "the-blueprint-is-where-it-was-put",
            pending != null && Mathf.Abs(pending.x - spot.x) < 0.01f
            && Mathf.Abs(pending.z - spot.z) < 0.01f,
            pending != null
                ? $"({pending.x:F2}, {pending.z:F2}) against ({spot.x:F2}, {spot.z:F2})"
                : "no pending row");
        Gate(sb, ref fails, "a-blueprint-is-not-a-camp",
            !outpost.HasCamp && outpost.Building,
            $"HasCamp {outpost.HasCamp}, Building {outpost.Building}");
        Gate(sb, ref fails, "a-blueprint-is-drawn-on-the-ground",
            target.GetComponentInChildren<BuildSite>() != null,
            "no BuildSite under the island");
        Gate(sb, ref fails, "an-unbuilt-camp-keeps-nothing",
            outpost.KeepsOfEach == 0,
            $"keeps {outpost.KeepsOfEach} before the fire is lit");

        // **The blueprint does not clear its own ground**, and an earlier
        // version did: the probe's spot had four trees on it, a campfire costs
        // four logs, and the camp finished the frame it was sited. A blueprint
        // that can pay for itself is not a blueprint.
        Gate(sb, ref fails, "siting-fells-nothing",
            wood == null || standingAtSiting == standingBefore,
            $"{standingBefore - standingAtSiting} trees came down at siting");
        Gate(sb, ref fails, "a-sited-camp-is-not-a-built-one",
            wanted == BuildPlans.Campfire.cost,
            $"wants {wanted} of {BuildPlans.Campfire.cost} logs the moment it is placed");

        // Somebody has to build it, and a hand must be leavable at a camp that
        // is only a drawing -- that IS the flow. A hand left here goes on to
        // the build, not on to a pile that does not exist yet.
        var builder = new OutpostHand { name = "probe-builder", order = OutpostOrder.Build };
        outpost.Ledger.hands.Add(builder);
        outpost.Ledger.lastTicked = TimeOfDay.Seconds;

        int logsAtSiting = pending != null ? pending.done : 0;
        outpost.Ledger.Tick(TimeOfDay.Seconds + 2.0 * TimeOfDay.DayLength);
        outpost.CatchUp();
        yield return null;

        var fireGo = target.GetComponentInChildren<Campfire>();
        Vector3 firePos = fireGo != null ? fireGo.transform.position : Vector3.zero;
        float fireOff = fireGo != null
            ? Vector3.Distance(new Vector3(firePos.x, 0f, firePos.z),
                               new Vector3(spot.x, 0f, spot.z))
            : 999f;

        sb.AppendLine($"  one hand, two game days later: "
            + (outpost.HasCamp ? "the fire is lit" : "still a blueprint"));
        Gate(sb, ref fails, "the-crew-build-what-was-sited", outpost.HasCamp,
            outpost.Ledger.pending != null
                ? $"{outpost.Ledger.pending.done}/{outpost.Ledger.pending.needed} logs"
                : "no pending row and no fire");
        Gate(sb, ref fails, "the-fire-stands-where-it-was-sited", fireOff < 1.5f,
            $"{fireOff:F2} m from the spot");
        Gate(sb, ref fails, "the-blueprint-comes-down-when-it-is-built",
            target.GetComponentInChildren<BuildSite>() == null,
            "a BuildSite is still standing under the island");
        Gate(sb, ref fails, "building-costs-what-it-says",
            logsAtSiting == 0,
            $"{logsAtSiting} logs were in it before anybody worked on it");

        // Everybody goes back to cutting once there is somewhere to cut into.
        Gate(sb, ref fails, "a-finished-camp-puts-its-hands-back-on-the-wood",
            builder.order == OutpostOrder.Gather,
            $"the builder is on {builder.order}");
        outpost.Ledger.hands.Remove(builder);

        // --- the view drops on to what was sited ------------------------------
        //
        // Kevin, 2026-09-19: once the blueprint is down the camera goes to it,
        // 35 m above. Driven through the SAME call the tap makes, so this
        // cannot pass against a copy of the behaviour.

        SeaSick.UI.CampSiting.DropTheViewOn(outpost);
        for (int f = 0; f < 90; f++) yield return null;      // let the rig fly in

        Vector3 campAt = outpost.CampCentre;
        float camHeight = Camera.main != null
            ? Camera.main.transform.position.y - campAt.y : -1f;
        Vector3 vp = Camera.main != null
            ? Camera.main.WorldToViewportPoint(campAt) : Vector3.zero;
        float offCentre = Vector2.Distance(new Vector2(vp.x, vp.y), new Vector2(0.5f, 0.5f));

        sb.AppendLine();
        sb.AppendLine("THE VIEW, ONCE THE CAMP IS SITED:");
        sb.AppendLine($"  asked for {SeaSick.CameraRig.IslandCam.BlueprintHeight:F0} m above it; "
            + $"the lens is {camHeight:F1} m above it");
        sb.AppendLine($"  it sits at viewport ({vp.x:F2}, {vp.y:F2}), "
            + $"{offCentre * 100f:F0}% of the frame off centre");
        sb.AppendLine($"  readout: {(icam != null ? icam.Readout : "no IslandCam")}");

        Gate(sb, ref fails, "the-view-goes-to-what-was-sited", offCentre < 0.14f,
            $"{offCentre * 100f:F0}% off the middle of the frame");
        Gate(sb, ref fails, "the-view-is-35-m-above-it",
            Mathf.Abs(camHeight - SeaSick.CameraRig.IslandCam.BlueprintHeight) < 4f,
            $"{camHeight:F1} m against {SeaSick.CameraRig.IslandCam.BlueprintHeight:F0}");

        // --- and it follows whoever you press on ------------------------------
        //
        // The pick itself is a projection against a tap and cannot be pressed
        // from here; what is gated is the thing the pick DOES, which is the
        // half that can silently stop working.

        // Somebody has to be standing there to be pressed on. Station one for
        // the duration -- that IS the villager case: a hand who lives at the
        // camp, parked between visits and drawn while the ship is here.
        var someone = FirstAshore(outpost);
        bool borrowed = false;
        if (someone == null)
        {
            var crewRoster = Object.FindFirstObjectByType<SeaSick.Crew.CrewRoster>();
            if (crewRoster != null)
                foreach (var hand in crewRoster.All)
                {
                    if (hand == null || !hand.IsAboard) continue;
                    if (!outpost.Station(hand)) continue;
                    outpost.ShowHands(true);
                    crewRoster.Refresh();
                    someone = hand;
                    borrowed = true;
                    break;
                }
        }

        if (someone != null && icam != null)
        {
            icam.FollowThis(someone.transform);
            for (int f = 0; f < 60; f++) yield return null;
            Vector3 vp1 = Camera.main.WorldToViewportPoint(someone.transform.position);
            float off1 = Vector2.Distance(new Vector2(vp1.x, vp1.y), new Vector2(0.5f, 0.5f));

            // Move them, and the frame has to come too.
            Vector3 was = someone.transform.position;
            someone.transform.position = was + new Vector3(18f, 0f, 14f);
            for (int f = 0; f < 60; f++) yield return null;
            Vector3 vp2 = Camera.main.WorldToViewportPoint(someone.transform.position);
            float off2 = Vector2.Distance(new Vector2(vp2.x, vp2.y), new Vector2(0.5f, 0.5f));
            someone.transform.position = was;

            sb.AppendLine($"  following {someone.DisplayName}: "
                + $"{off1 * 100f:F0}% off centre, and {off2 * 100f:F0}% after they walked 23 m");
            Gate(sb, ref fails, "the-view-follows-who-you-press-on", off1 < 0.14f,
                $"{off1 * 100f:F0}% off the middle");
            Gate(sb, ref fails, "and-keeps-following-when-they-move", off2 < 0.14f,
                $"{off2 * 100f:F0}% off the middle after they moved");

            icam.Release();
            for (int f = 0; f < 60; f++) yield return null;
            Gate(sb, ref fails, "letting-go-hands-the-shot-back",
                icam.Following == null && !icam.FocusPoint.HasValue,
                "still holding a focus after Release");

            // Put them back aboard, or the gates that follow are counting a
            // crew this section quietly took a man out of.
            if (borrowed && anchor != null)
            {
                outpost.Recall(someone, anchor.transform);
                var r2 = Object.FindFirstObjectByType<SeaSick.Crew.CrewRoster>();
                if (r2 != null) r2.Refresh();
            }
        }
        else sb.AppendLine("  (nobody ashore to follow -- the follow gates did not run)");

        int standingAfter = wood != null ? wood.TreeCount - wood.FelledInMesh() : -1;
        int downInMesh = standingAtSiting - standingAfter;
        int felled = outpost.LastClearingFelled;

        sb.AppendLine();
        sb.AppendLine($"THE CAMP, ONCE IT IS BUILT, on {target.name}:");
        sb.AppendLine($"  reported {felled} logs out of the clearing it stands in");
        sb.AppendLine($"  trees standing in the MESH: {standingBefore} -> {standingAfter} "
            + $"({downInMesh} came down)");
        sb.AppendLine($"  keeps {outpost.KeepsOfEach}, ledger ceiling {outpost.Ledger?.ceilingPer}, "
            + $"pile {outpost.Ledger?.Timber}");

        Gate(sb, ref fails, "the-camp-exists", outpost.HasCamp, "no campfire raised");
        Gate(sb, ref fails, "the-fire-is-on-the-ground",
            target.GetComponentInChildren<Campfire>() != null, "no Campfire in the scene");
        // **Counted inside the clearing, not across the whole island.**
        // Since the crew cut real trees for the wood they build with, the
        // total change in the mesh over a build is the clearing PLUS whatever
        // they felled to pay for it. What this gate is about is the clearing,
        // so it counts the stumps that are actually in it.
        int downInClearing = 0;
        if (wood != null)
            for (int i = 0; i < wood.TreeCount; i++)
                if (wood.TreeAt(i).felled
                    && SeaSick.World.Island.FlatDistance(
                        wood.TreeAt(i).baseAt, outpost.CampCentre) <= Outpost.CampClearingRadius)
                    downInClearing++;
        Gate(sb, ref fails, "making-camp-fells-its-own-site",
            wood == null || downInClearing == felled,
            $"reported {felled}, {downInClearing} stumps inside the {Outpost.CampClearingRadius:F1} m clearing "
            + $"({downInMesh} came down on the island altogether)");
        Gate(sb, ref fails, "a-camp-gives-the-place-a-ceiling",
            outpost.KeepsOfEach == OutpostLedger.CampfireCeiling,
            $"keeps {outpost.KeepsOfEach}, expected {OutpostLedger.CampfireCeiling}");
        Gate(sb, ref fails, "one-definition-of-the-ceiling",
            outpost.Ledger != null && outpost.Ledger.ceilingPer == outpost.KeepsOfEach,
            $"ledger {outpost.Ledger?.ceilingPer} vs outpost {outpost.KeepsOfEach}");

        // Making camp twice must not raise a second fire.
        int again = outpost.MakeCamp();
        Gate(sb, ref fails, "camp-is-made-once", again < 0 && outpost.CountOf("Campfire") == 1,
            $"second attempt returned {again}, {outpost.CountOf("Campfire")} fires");

        // --- the camp as a PLACE: models, people, piles ----------------------
        //
        // Kevin, 2026-09-19: the crew should stand round the fire once it is
        // built, and what they gather should be piled beside it. Both are
        // things you would see and neither is a number, so both are measured
        // off the scene rather than off the ledger.

        var fireB = fireGo != null ? fireGo.GetComponentInParent<Building>() : null;
        var model = fireB != null ? fireB.transform.Find("Model") : null;
        int modelRenderers = model != null
            ? model.GetComponentsInChildren<MeshRenderer>(true).Length : 0;

        sb.AppendLine();
        sb.AppendLine("THE CAMP AS A PLACE:");
        sb.AppendLine($"  the fire wears {(model != null ? $"the kit model, {modelRenderers} renderers" : "extruded primitives")}");
        Gate(sb, ref fails, "the-fire-wears-the-authored-kit",
            model != null && modelRenderers > 0,
            model != null ? $"{modelRenderers} renderers under Model"
                          : "no Model child -- Resources/Settlement did not load");

        // Station three and look at where they are standing. A ring, facing
        // in, evenly spaced -- not a random scatter round wherever the camp
        // centre happened to be when each of them was left.
        var roster3 = Object.FindFirstObjectByType<SeaSick.Crew.CrewRoster>();
        var stood = new System.Collections.Generic.List<SeaSick.Crew.CrewAgent>();
        if (roster3 != null)
            foreach (var hand in roster3.All)
            {
                if (stood.Count >= 3) break;
                if (hand == null || !hand.IsAboard) continue;
                if (outpost.Station(hand)) stood.Add(hand);
            }
        // **No `ShowHands(true)` here, and that is the point.** The first
        // version of this called it right after stationing, so the gate below
        // passed because the PROBE had drawn them -- not because the game
        // would. It is the game's own arrival path that has to have woken this
        // camp, which is what the gate is really about.
        roster3?.Refresh();
        for (int f = 0; f < 4; f++) yield return null;

        float nearest = float.MaxValue, furthest = 0f, minGap = float.MaxValue;
        foreach (var a in stood)
        {
            float d = SeaSick.World.Island.FlatDistance(a.transform.position, outpost.CampCentre);
            nearest = Mathf.Min(nearest, d);
            furthest = Mathf.Max(furthest, d);
            foreach (var b2 in stood)
                if (b2 != a)
                    minGap = Mathf.Min(minGap,
                        SeaSick.World.Island.FlatDistance(a.transform.position, b2.transform.position));
        }

        sb.AppendLine($"  {stood.Count} hands stationed: {nearest:F2}-{furthest:F2} m from the fire "
            + $"(ring is {Outpost.FireRingRadius:F1} m), closest pair {minGap:F2} m apart");
        Gate(sb, ref fails, "they-stand-round-the-fire",
            stood.Count >= 2 && Mathf.Abs(nearest - Outpost.FireRingRadius) < 0.6f
            && Mathf.Abs(furthest - Outpost.FireRingRadius) < 0.6f,
            $"{nearest:F2}-{furthest:F2} m against a {Outpost.FireRingRadius:F1} m ring");
        Gate(sb, ref fails, "and-not-on-top-of-each-other",
            stood.Count < 2 || minGap > 1.2f,
            $"closest pair {minGap:F2} m apart");

        // Piles: put something on the ground and see it appear beside the fire.
        //
        // **Against what the ledger HOLDS, not against what was added.** The
        // camp already had the four logs its own clearing gave it, so the
        // first version of this gate asked for six and got the correct ten.
        outpost.Ledger.Add(Res.Timber, 6);
        int held = outpost.Ledger.CountOf(Res.Timber);
        var piles = CampPiles.EnsureOn(outpost);
        piles.Refresh();
        yield return null;

        var pile = outpost.GetComponentInChildren<CampPiles>(true);
        Transform stack = pile != null ? pile.transform.Find("Pile_" + Res.Timber) : null;
        int logsDrawn = stack != null ? stack.childCount : 0;
        float pileGap = stack != null
            ? SeaSick.World.Island.FlatDistance(stack.position, outpost.CampCentre) : -1f;
        sb.AppendLine($"  {held} timber on the ground: {logsDrawn} drawn, "
            + $"{pileGap:F1} m from the fire");
        Gate(sb, ref fails, "what-is-gathered-is-piled-by-the-fire",
            logsDrawn == held && pileGap > Outpost.FireRingRadius && pileGap < 9f,
            $"{logsDrawn} drawn of {held} held, at {pileGap:F1} m");

        // --- the three things Kevin found by playing it ----------------------
        //
        // 1. "i never saw the people on the island"
        // 2. "the trees never disappear. i assume they would since they're cut down"
        // 3. rotating a building before it is placed

        sb.AppendLine();
        sb.AppendLine("WHAT PLAY FOUND:");

        int drawnNow = 0, withBody = 0;
        foreach (var a in outpost.Parked())
        {
            if (a == null || outpost.HandNamed(a.DisplayName) == null) continue;
            if (a.gameObject.activeInHierarchy) drawnNow++;
            if (a.GetComponent<CampWorker>() != null) withBody++;
        }
        sb.AppendLine($"  stationed while she lies here: {drawnNow} of {stood.Count} drawn, "
            + $"{withBody} with a body to walk it");
        Gate(sb, ref fails, "a-hand-left-in-front-of-you-stays-visible",
            drawnNow == stood.Count && stood.Count > 0,
            $"{drawnNow} of {stood.Count} still drawn after being stationed");
        Gate(sb, ref fails, "and-has-something-driving-them",
            withBody == stood.Count,
            $"{withBody} of {stood.Count} carry a CampWorker");

        // They must actually MOVE. A ring of statues was the complaint.
        var mover = stood.Count > 0 ? stood[0] : null;
        Vector3 wasAt = mover != null ? mover.transform.position : Vector3.zero;
        if (mover != null)
        {
            outpost.OrderGather(outpost.HandNamed(mover.DisplayName), Res.Timber);
            for (int f = 0; f < 240; f++) yield return null;   // a few seconds of walking
        }
        float wandered = mover != null
            ? SeaSick.World.Island.FlatDistance(mover.transform.position, wasAt) : 0f;
        sb.AppendLine($"  {(mover != null ? mover.DisplayName : "nobody")} moved {wandered:F2} m "
            + "in four seconds of being watched");
        Gate(sb, ref fails, "they-are-seen-to-be-working", wandered > 1.5f,
            $"{wandered:F2} m walked");

        // --- the wood thins as it is cut -------------------------------------
        //
        // Felling is driven by `ledger.timberTaken`, so it is a pure function
        // of the ledger and happens on a visit whatever the terrain did while
        // the player was away.

        int treesBefore = wood != null ? wood.TreeCount - wood.FelledInMesh() : -1;
        float takenBefore = outpost.Ledger.timberTaken;
        outpost.Ledger.stores.Clear();                 // room to cut into
        outpost.Ledger.lastTicked = TimeOfDay.Seconds;
        outpost.Ledger.Tick(TimeOfDay.Seconds + 2.0 * TimeOfDay.DayLength);
        outpost.CatchUp();
        yield return null;

        int treesAfter = wood != null ? wood.TreeCount - wood.FelledInMesh() : -1;
        float cut = outpost.Ledger.timberTaken - takenBefore;
        int down = treesBefore - treesAfter;
        sb.AppendLine($"  two days of cutting: {cut:F1} logs out of the ground, "
            + $"{down} trees came down in the MESH ({treesBefore} -> {treesAfter})");
        Gate(sb, ref fails, "cutting-timber-takes-trees-down", down > 0,
            $"{down} trees for {cut:F1} logs");
        Gate(sb, ref fails, "one-tree-per-log",
            wood == null || Mathf.Abs(down - Mathf.FloorToInt(outpost.Ledger.timberTaken)
                + Mathf.FloorToInt(takenBefore)) <= 1,
            $"{down} down against {cut:F1} cut");

        // Nearest the camp outward, so the clearing widens rather than the
        // island going patchy at random.
        float furthestDown = 0f, nearestStanding = float.MaxValue;
        if (wood != null)
            for (int i = 0; i < wood.TreeCount; i++)
            {
                var t = wood.TreeAt(i);
                float d = SeaSick.World.Island.FlatDistance(t.baseAt, outpost.CampCentre);
                if (t.felled) furthestDown = Mathf.Max(furthestDown, d);
                else nearestStanding = Mathf.Min(nearestStanding, d);
            }
        sb.AppendLine($"  furthest stump {furthestDown:F1} m, nearest tree still standing "
            + $"{nearestStanding:F1} m");
        Gate(sb, ref fails, "the-clearing-widens-from-the-camp",
            wood == null || nearestStanding >= furthestDown - 6f,
            $"stumps out to {furthestDown:F1} m, standing wood from {nearestStanding:F1} m");

        // --- a position, and somebody in it ----------------------------------
        //
        // Raise a sawmill the fast way (the blueprint path is already gated
        // above), assign a hand to it, and check the two halves: they STAND
        // there, and the ledger turns timber into boards.

        Vector3 millAt = outpost.CampCentre + new Vector3(11f, 0f, 4f);
        millAt.y = outpost.GroundAt(millAt);
        bool millPlaced = false;
        for (int ring = 0; ring < 6 && !millPlaced; ring++)
            for (int i = 0; i < 12 && !millPlaced; i++)
            {
                float a3 = i * Mathf.PI * 2f / 12f + ring * 0.7f;
                float rad = 10f + ring * 4f;
                var q = outpost.CampCentre + new Vector3(Mathf.Cos(a3) * rad, 0f, Mathf.Sin(a3) * rad);
                q.y = outpost.GroundAt(q);
                if (outpost.CanPlace(BuildPlans.Sawmill, q, out _)) { millAt = q; millPlaced = true; }
            }

        var millB = millPlaced ? outpost.Raise(BuildPlans.Sawmill, millAt) : null;
        if (millB != null) outpost.Ledger.built.Add(BuildPlans.Sawmill.id);
        yield return null;

        var sawyer = stood.Count > 0 ? outpost.HandNamed(stood[0].DisplayName) : null;
        bool assigned = sawyer != null && outpost.Assign(sawyer, BuildPlans.Sawmill.id);
        yield return null;

        // **He WALKS there now.** Until 2026-09-20 every order write snapped
        // the whole camp to its spots, so one frame later he was standing at
        // the mill and this measured a teleport. A hand who is on his feet and
        // being watched is now given somewhere to go instead, so the gate has
        // to give him the time to get there -- at camp pace (2.6 m/s) across
        // at most the 30 m the mill was sited within. Same thresholds as
        // before: where he ENDS UP has not moved.
        for (float walked = 0f; walked < 16f && millB != null && stood.Count > 0; walked += Time.unscaledDeltaTime)
        {
            if (SeaSick.World.Island.FlatDistance(
                    stood[0].transform.position, millB.transform.position) < 5f) break;
            yield return null;
        }

        float toMill = millB != null && stood.Count > 0
            ? SeaSick.World.Island.FlatDistance(stood[0].transform.position, millB.transform.position)
            : -1f;
        float toFire = stood.Count > 0
            ? SeaSick.World.Island.FlatDistance(stood[0].transform.position, outpost.CampCentre)
            : -1f;

        sb.AppendLine();
        sb.AppendLine("A POSITION, AND SOMEBODY IN IT:");
        sb.AppendLine($"  sawmill raised {(millB != null ? "yes" : "NO")} at "
            + $"{SeaSick.World.Island.FlatDistance(millAt, outpost.CampCentre):F0} m from the fire");
        sb.AppendLine($"  {(assigned ? sawyer.name + " assigned: " + sawyer.Doing : "nobody assigned")}"
            + $"   ·   {toMill:F1} m from the mill, {toFire:F1} m from the fire");

        Gate(sb, ref fails, "a-second-building-goes-up", millB != null,
            millPlaced ? "raised" : "nowhere within 30 m would take a sawmill");

        // **Turned by hand, 45 degrees at a time.** Sited with an explicit
        // yaw and checked on the thing that actually stood up -- a rotation
        // that lived only in the ghost would look right and build wrong.
        float wantYaw = Mathf.Repeat(outpost.AutoYaw(millAt) + 135f, 360f);
        Vector3 hutAt = millAt;
        bool hutPlaced = false;
        for (int ring = 0; ring < 6 && !hutPlaced; ring++)
            for (int i = 0; i < 12 && !hutPlaced; i++)
            {
                float a4 = i * Mathf.PI * 2f / 12f + ring * 1.3f;
                float rad = 12f + ring * 4f;
                var q = outpost.CampCentre + new Vector3(Mathf.Cos(a4) * rad, 0f, Mathf.Sin(a4) * rad);
                q.y = outpost.GroundAt(q);
                if (outpost.CanPlace(BuildPlans.Hut, q, wantYaw, out _)) { hutAt = q; hutPlaced = true; }
            }
        var hutB = hutPlaced ? outpost.Raise(BuildPlans.Hut, hutAt, wantYaw) : null;
        float gotYaw = hutB != null ? hutB.transform.eulerAngles.y : -1f;
        float yawErr = hutB != null ? Mathf.Abs(Mathf.DeltaAngle(gotYaw, wantYaw)) : 999f;
        sb.AppendLine($"  a shelter turned to {wantYaw:F0}°: it stands at {gotYaw:F0}° "
            + $"({yawErr:F1}° out)");
        Gate(sb, ref fails, "a-building-stands-the-way-it-was-turned",
            hutB != null && yawErr < 0.5f,
            hutB != null ? $"{yawErr:F1}° out" : "nowhere would take a shelter at that angle");
        Gate(sb, ref fails, "a-hand-can-be-assigned-to-it",
            assigned && sawyer.order == OutpostOrder.Work,
            assigned ? $"{sawyer.name} is {sawyer.Doing}" : "Assign refused");
        Gate(sb, ref fails, "and-goes-and-stands-there",
            millB != null && toMill < toFire && toMill < 7f,
            $"{toMill:F1} m from the mill against {toFire:F1} m from the fire");

        // And the arithmetic half: boards, out of the timber on the ground.
        outpost.Ledger.Add(Res.Timber, 8);
        int timberBefore = outpost.Ledger.CountOf(Res.Timber);
        outpost.Ledger.lastTicked = TimeOfDay.Seconds;
        outpost.Ledger.Tick(TimeOfDay.Seconds + 2.0 * TimeOfDay.DayLength);
        sb.AppendLine($"  two game days later: timber {timberBefore} -> "
            + $"{outpost.Ledger.CountOf(Res.Timber)}, boards {outpost.Ledger.CountOf(Res.Boards)}");
        Gate(sb, ref fails, "an-assigned-hand-produces",
            outpost.Ledger.CountOf(Res.Boards) > 0,
            $"{outpost.Ledger.CountOf(Res.Boards)} boards in two days");

        // Put the borrowed hands back before the rest of the probe counts crew.
        foreach (var a in stood)
            if (a != null && anchor != null) outpost.Recall(a, anchor.transform);
        roster3?.Refresh();
        yield return null;

        // --- and it has to actually produce ----------------------------------
        //
        // Put two hands on the wood and wind the clock. The ledger is the only
        // thing that produces; nobody has to be standing here.

        var l = outpost.Ledger;
        l.stores.Clear();
        l.hands.Clear();
        l.hands.Add(new OutpostHand { name = "probe-1", order = OutpostOrder.Gather, target = Res.Timber });
        l.hands.Add(new OutpostHand { name = "probe-2", order = OutpostOrder.Gather, target = Res.Timber });
        l.lastTicked = TimeOfDay.Seconds;

        double twoDays = TimeOfDay.Seconds + 2.0 * TimeOfDay.DayLength;
        l.ceilingPer = outpost.KeepsOfEach;
        l.Tick(twoDays);

        sb.AppendLine();
        sb.AppendLine($"TWO HANDS, TWO GAME DAYS with nobody watching: "
            + $"{l.Timber} logs of a possible {l.ceilingPer}, {l.Wood.standing:F0} still standing");
        Gate(sb, ref fails, "an-absent-camp-produces", l.Timber > 0,
            $"{l.Timber} logs after two days");

        // --- leave two hands, sail away, come back ---------------------------
        //
        // The whole point of the design, driven end to end. Nothing here asks
        // whether it WOULD work: it stations real crew, gets her underway,
        // moves her a kilometre off, brings her back and reads what is there.

        var roster = Object.FindFirstObjectByType<SeaSick.Crew.CrewRoster>();
        int aboardBefore = 0;
        foreach (var c in roster.All) if (c != null && c.IsAboard) aboardBefore++;

        l.hands.Clear();
        l.stores.Clear();
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
        sb.AppendLine($"  pile {l.Timber} / {l.ceilingPer}   standing {l.Wood.standing:F0}   "
            + $"bodies drawn {drawnBack} of {left}");
        Gate(sb, ref fails, "work-was-done-while-she-was-away", l.Timber > 0,
            $"{l.Timber} logs after three days away");
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

    /// Anybody standing on this island: a hand who lives here, or one of the
    /// shore party off the ship. Both read as villagers to somebody looking
    /// down at them, which is what the pick has to agree with.
    static SeaSick.Crew.CrewAgent FirstAshore(Outpost outpost)
    {
        foreach (var a in outpost.Parked())
            if (a != null && a.gameObject.activeInHierarchy) return a;
        var all = Object.FindObjectsByType<SeaSick.Crew.CrewAgent>(FindObjectsSortMode.None);
        foreach (var a in all)
            if (a != null && a.gameObject.activeInHierarchy && a.IsAshore) return a;
        return null;
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
