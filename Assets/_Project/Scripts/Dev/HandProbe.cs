using System.Text;
using UnityEngine;
using SeaSick.Ship;
using SeaSick.UI;
using SeaSick.World;

/// **Does the Hand give the order the cursor promised, and nothing else?**
///
/// Sails her to a real island, lands, makes a camp the dev way, stands a
/// sawmill and a shelter in it, sites a blueprint, leaves a hand — and then
/// drives ONLY the Hand's public API, with screen points obtained by
/// projecting known world positions through `Camera.main`. That is exactly
/// what `IslandInput` hands it, so nothing here is testing a private copy of
/// the feature.
///
/// Three families of gate:
///
/// 1. **The pick.** Projection finds the right body at a working zoom, and
///    REFUSES at 520 m of ground — where a villager is four pixels tall and a
///    grab radius with a floor under it would swallow every attempt to pan.
/// 2. **The order.** Each kind of drop writes its row in the frame it
///    happens, a refusal leaves the row alone, and `Preview` says beforehand
///    exactly what the drop then does.
/// 3. **D2.** Two hundred and forty re-drops of the same order across one game
///    day pay what one drop and a day of absence pays, and holding a man in
///    the air for half a day changes nothing at all. This is the rule the
///    whole outpost design rests on and the Hand is the easiest way to break
///    it.
///
/// Plus: a steady hold allocates nothing, measured inside a single frame so
/// the rest of the game cannot contribute to the number.
///
/// Plain C#, no editor references. Play mode, `Sea.unity`. Writes
/// `Logs/HandProbe.txt`.
public class HandProbe : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("HandProbe: not in play mode"); return; }
        var old = FindAnyObjectByType<HandProbe>();
        if (old != null) Destroy(old.gameObject);
        var runner = new GameObject("HandProbeRunner").AddComponent<HandProbe>();
        runner.StartCoroutine(runner.Run());
    }

    StringBuilder sb = new StringBuilder();
    int fails;

    // The last preview/commit pair, so `Do` can hand them back out of a
    // coroutine, which cannot return a value.
    HandTarget pv;
    bool dropOk;
    string dropWhy;

    System.Collections.IEnumerator Run()
    {
        var anchor = Object.FindFirstObjectByType<AnchorController>();
        var motor = Object.FindFirstObjectByType<ShipMotor>();
        var roster = Object.FindFirstObjectByType<SeaSick.Crew.CrewRoster>();
        if (anchor == null || motor == null || roster == null)
        {
            Report("FAIL: no ship in the scene\n");
            Destroy(gameObject);
            yield break;
        }

        // --- somewhere with a beach and some wood on it ----------------------
        //
        // Ranked by shore gap, never by centre distance, and asking the same
        // three questions `AnchorController` asks in the same order --
        // `CampProbe` learned both the hard way.
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
        if (camp == null) { Finish($"{target.name} will not take a camp — this probe needs one that will"); yield break; }

        // Wait out the anchor drop so she is properly Anchored: the pick will
        // not take a body off a ship that is still coming to rest.
        float settle = Time.realtimeSinceStartup + 8f;
        while (anchor.CurrentState == AnchorController.State.Dropping
               && Time.realtimeSinceStartup < settle) yield return null;

        // --- a camp, the dev way ---------------------------------------------
        //
        // `MakeCamp` sites at the surveyed clearing and pays for it outright.
        // The slow path -- site, leave a hand, wait out the build -- is what
        // `CampProbe` gates; a probe about the Hand that waited for a camp to
        // be built would be measuring the build every time it ran.
        if (!camp.HasCamp)
        {
            camp.MakeCamp(out string campWhy);
            if (!camp.HasCamp) { Finish("could not make a camp: " + campWhy); yield break; }
        }
        camp.CatchUp();
        camp.ShowHands(true);
        yield return null;

        var mill = RaiseNear(camp, BuildPlans.Sawmill, 11f);
        if (mill != null) camp.Ledger.built.Add(BuildPlans.Sawmill.id);
        var hut = RaiseNear(camp, BuildPlans.Hut, 17f);
        if (hut != null) camp.Ledger.built.Add(BuildPlans.Hut.id);

        // A drawing to build. `Site` puts every hand on to it, which is why
        // the blueprint gate comes after the ones that set a different order.
        Vector3 bpAt = FindSpot(camp, BuildPlans.Storage, 22f);
        int wants = camp.Site(BuildPlans.Storage, bpAt, out string siteWhy);
        yield return null;

        sb.AppendLine($"camp at {camp.CampCentre.x:F0},{camp.CampCentre.z:F0}   "
            + $"sawmill {(mill != null ? "yes" : "NO")}   shelter {(hut != null ? "yes" : "NO")}   "
            + $"blueprint {(wants >= 0 ? BuildPlans.Storage.id + " wants " + wants : "REFUSED: " + siteWhy)}");

        // --- one hand ashore --------------------------------------------------
        SeaSick.Crew.CrewAgent resident = null;
        foreach (var c in roster.All)
        {
            if (c == null || !c.IsAboard) continue;
            if (camp.Station(c)) { resident = c; break; }
        }
        roster.Refresh();
        camp.ShowHands(true);
        if (resident == null) { Finish("nobody aboard could be left ashore"); yield break; }
        string who = resident.DisplayName;
        yield return null;

        var hand = Hand.Instance;
        if (hand == null) hand = anchor.GetComponent<Hand>();
        if (hand == null) hand = anchor.gameObject.AddComponent<Hand>();
        Hand.TouchOffset01 = 0f;          // a mouse: the drop is under the cursor

        var icam = Object.FindFirstObjectByType<SeaSick.CameraRig.IslandCam>();
        if (icam == null) { Finish("no IslandCam — the view is not engaged"); yield break; }

        // --- a working zoom ---------------------------------------------------
        icam.LookAt(camp.CampCentre, 70f);
        icam.SnapToTarget();
        for (int f = 0; f < 120; f++) yield return null;

        var lens = Camera.main;
        if (lens == null) { Finish("no main camera"); yield break; }

        sb.AppendLine();
        sb.AppendLine($"THE VIEW: {icam.Ground:F0} m of ground, lens "
            + $"{lens.transform.position.y - camp.CampCentre.y:F0} m above the fire");

        // =====================================================================
        // 1. The pick
        // =====================================================================

        sb.AppendLine();
        sb.AppendLine("THE PICK:");

        Vector3 chest = resident.transform.position + Vector3.up * 0.9f;
        Vector2 sHand = ToScreen(lens, chest, out bool inFront);
        float tall = ProjectedHeight(lens, resident.transform.position);
        var picked = inFront ? hand.PickAt(sHand, true) : null;
        sb.AppendLine($"  {who} is {tall / Screen.height * 100f:F2}% of screen height tall; "
            + $"pick returned {(picked != null ? picked.DisplayName : "nobody")}");
        Gate("the-projection-pick-finds-the-hand", picked == resident,
            picked != null ? picked.DisplayName : "nobody, in front " + inFront);

        // And the follow radius is the generous one: a tap a little wide of
        // somebody still means them.
        var nearMiss = hand.PickAt(sHand + new Vector2(0f, Screen.height * 0.045f), false);
        Gate("the-follow-radius-is-the-generous-one", nearMiss == resident,
            nearMiss != null ? nearMiss.DisplayName : "nobody 4.5% of screen away");

        // Right out: a body four pixels tall is not something anybody aimed at.
        icam.ZoomTo(520f);
        icam.SnapToTarget();
        for (int f = 0; f < 150; f++) yield return null;
        lens = Camera.main;
        Vector2 sWide = ToScreen(lens, resident.transform.position + Vector3.up * 0.9f, out _);
        float tallWide = ProjectedHeight(lens, resident.transform.position);
        var wide = hand.PickAt(sWide, true);
        sb.AppendLine($"  at {icam.Ground:F0} m of ground they are "
            + $"{tallWide / Screen.height * 100f:F2}% tall; pick returned "
            + $"{(wide != null ? wide.DisplayName : "nobody")}");
        Gate("a-wide-zoom-refuses-a-pickup", wide == null,
            wide != null ? "grabbed " + wide.DisplayName + " at 520 m of ground" : "");

        icam.LookAt(camp.CampCentre, 70f);
        icam.SnapToTarget();
        for (int f = 0; f < 150; f++) yield return null;
        lens = Camera.main;

        // =====================================================================
        // 2. Lifting writes nothing
        // =====================================================================
        //
        // In ONE frame, with no yield in it. The camp sheets call
        // `Outpost.CatchUp` on every GUI event while she lies at an island, so
        // a leg that spanned frames would be comparing a ledger the sheet had
        // ticked underneath it.

        sHand = ToScreen(lens, resident.transform.position + Vector3.up * 0.9f, out _);
        Vector3 wasAt = resident.transform.position;
        Quaternion wasFacing = resident.transform.rotation;
        Transform wasParent = resident.transform.parent;
        string before = JsonUtility.ToJson(camp.Ledger);
        bool lifted = hand.PickUp(resident);
        hand.HoldAt(sHand);
        hand.HoldAt(sHand + new Vector2(30f, 20f));
        bool heldOk = hand.Holding;
        float lifted_m = Vector3.Distance(resident.transform.position, wasAt);
        hand.Cancel();
        string after = JsonUtility.ToJson(camp.Ledger);
        float backErr = Vector3.Distance(resident.transform.position, wasAt);
        float faceErr = Quaternion.Angle(resident.transform.rotation, wasFacing);

        sb.AppendLine();
        sb.AppendLine("LIFTING AND PUTTING BACK:");
        sb.AppendLine($"  lifted {lifted}, held {heldOk}, moved {lifted_m:F2} m while held; "
            + $"back to within {backErr * 100f:F2} cm and {faceErr:F2}°");
        Gate("lifting-and-cancelling-writes-nothing", lifted && heldOk && before == after,
            before == after ? "" : "the ledger changed under a pick-up and a cancel");
        Gate("cancel-puts-the-body-back-exactly",
            !hand.Holding && backErr < 0.01f && faceErr < 0.5f
            && resident.transform.parent == wasParent,
            $"{backErr * 100f:F2} cm and {faceErr:F2}° out, parent "
            + (resident.transform.parent == wasParent ? "kept" : "CHANGED"));

        // =====================================================================
        // 3. What each drop writes
        // =====================================================================
        //
        // The clock stops here. Everything below is about what a drop WRITES,
        // and a ledger ticking between two reads is noise in every one of them.
        bool wasPaused = TimeOfDay.Paused;
        TimeOfDay.Paused = true;

        sb.AppendLine();
        sb.AppendLine("WHAT A DROP WRITES:");

        // --- a tree ----------------------------------------------------------
        Vector3 treeAt;
        bool haveTree = FindTree(camp, out treeAt);
        if (haveTree)
        {
            Vector2 s = ToScreen(lens, treeAt, out _);
            yield return Do(hand, resident, s);
            var row = camp.HandNamed(who);
            sb.AppendLine($"  tree  : preview {pv.kind} \"{pv.verb}\"  ->  drop {dropOk}, "
                + $"row {row?.order} {row?.target}");
            Gate("a-drop-on-a-tree-orders-timber",
                dropOk && row != null && row.order == OutpostOrder.Gather
                && row.target == Res.Timber,
                row != null ? row.order + " " + row.target : "no row");
            Gate("the-preview-of-a-tree-is-a-tree",
                pv.kind == HandTarget.Kind.Tree && pv.Allowed == dropOk,
                $"{pv.kind}, allowed {pv.Allowed} vs drop {dropOk}");
        }
        else sb.AppendLine("  tree  : SKIPPED — no standing tree clear of the camp's buildings");

        // --- ore, stone or spice ---------------------------------------------
        var node = FindNode(camp);
        if (node != null)
        {
            Vector2 s = ToScreen(lens, node.transform.position, out _);
            string res = node.Resource;
            yield return Do(hand, resident, s);
            var row = camp.HandNamed(who);
            sb.AppendLine($"  prop  : {res}  preview {pv.kind} \"{pv.verb}\"  ->  drop {dropOk}, "
                + $"row {row?.order} {row?.target}");
            Gate("a-drop-on-a-prop-orders-that-resource",
                dropOk && row != null && row.order == OutpostOrder.Gather && row.target == res,
                row != null ? row.order + " " + row.target + " for a " + res + " prop" : "no row");
            Gate("the-preview-of-a-prop-is-a-prop",
                pv.kind == HandTarget.Kind.Node && pv.resource == res,
                $"{pv.kind} {pv.resource}");
        }
        else sb.AppendLine("  prop  : SKIPPED — this island has no ore, stone or spice "
            + "standing clear of its wood");

        // --- the sawmill -------------------------------------------------------
        if (mill != null)
        {
            Vector2 s = ToScreen(lens, mill.transform.position, out _);
            yield return Do(hand, resident, s);
            var row = camp.HandNamed(who);
            sb.AppendLine($"  mill  : preview {pv.kind} \"{pv.verb}\"  ->  drop {dropOk}, "
                + $"row {row?.order} {row?.target}");
            Gate("a-drop-on-a-sawmill-makes-a-sawyer",
                dropOk && row != null && row.order == OutpostOrder.Work
                && row.target == BuildPlans.Sawmill.id,
                row != null ? row.order + " " + row.target : "no row");
            Gate("the-preview-of-a-workplace-is-a-workplace",
                pv.kind == HandTarget.Kind.Workplace && pv.planId == BuildPlans.Sawmill.id,
                $"{pv.kind} {pv.planId}");
        }
        else sb.AppendLine("  mill  : SKIPPED — nowhere near the camp would take a sawmill");

        // --- a shelter, which nobody works in ---------------------------------
        if (hut != null)
        {
            var was = camp.HandNamed(who);
            OutpostOrder wasOrder = was != null ? was.order : OutpostOrder.Idle;
            string wasTarget = was != null ? was.target : "";
            Vector2 s = ToScreen(lens, hut.transform.position, out _);
            yield return Do(hand, resident, s);
            var row = camp.HandNamed(who);
            sb.AppendLine($"  hut   : preview {pv.kind} refusal \"{pv.refusal}\"  ->  "
                + $"drop {dropOk} \"{dropWhy}\", row {row?.order} {row?.target}");
            Gate("a-drop-on-a-shelter-is-refused-with-a-reason",
                !dropOk && !string.IsNullOrEmpty(dropWhy), "refused " + !dropOk + " why \"" + dropWhy + "\"");
            Gate("and-leaves-the-row-alone",
                row != null && row.order == wasOrder && row.target == wasTarget,
                row != null ? $"{wasOrder} {wasTarget} -> {row.order} {row.target}" : "no row");
            Gate("the-preview-refuses-the-same-thing",
                !pv.Allowed && pv.refusal == dropWhy, $"\"{pv.refusal}\" vs \"{dropWhy}\"");
        }
        else sb.AppendLine("  hut   : SKIPPED — nowhere near the camp would take a shelter");

        // --- the fire ----------------------------------------------------------
        {
            Vector2 s = ToScreen(lens, camp.CampCentre, out _);
            yield return Do(hand, resident, s);
            var row = camp.HandNamed(who);
            sb.AppendLine($"  fire  : preview {pv.kind} \"{pv.verb}\"  ->  drop {dropOk}, "
                + $"row {row?.order} {row?.target}");
            Gate("a-drop-on-the-fire-stands-them-down",
                dropOk && row != null && row.order == OutpostOrder.Idle,
                row != null ? row.order.ToString() : "no row");
            Gate("the-preview-of-the-fire-is-the-fire",
                pv.kind == HandTarget.Kind.Fire, pv.kind.ToString());
        }

        // --- the blueprint ------------------------------------------------------
        if (camp.Building)
        {
            Vector3 at = camp.Ledger.Pending.At;
            at.y = camp.GroundAt(at);
            Vector2 s = ToScreen(lens, at, out _);
            yield return Do(hand, resident, s);
            var row = camp.HandNamed(who);
            sb.AppendLine($"  plan  : preview {pv.kind} \"{pv.verb}\"  ->  drop {dropOk}, "
                + $"row {row?.order} {row?.target}");
            Gate("a-drop-on-a-blueprint-puts-them-on-it",
                dropOk && row != null && row.order == OutpostOrder.Build,
                row != null ? row.order.ToString() : "no row");
            Gate("the-preview-of-a-blueprint-is-a-blueprint",
                pv.kind == HandTarget.Kind.Blueprint, pv.kind.ToString());
        }
        else sb.AppendLine("  plan  : SKIPPED — nothing is sited here");

        // --- bare ground --------------------------------------------------------
        Vector3 bare;
        bool haveBare = FindBare(camp, hand, lens, out bare);
        if (haveBare)
        {
            var was = camp.HandNamed(who);
            OutpostOrder wasOrder = was.order;
            string wasTarget = was.target;
            Vector2 s = ToScreen(lens, bare, out _);
            yield return Do(hand, resident, s);
            var row = camp.HandNamed(who);
            float moved = Island.FlatDistance(resident.transform.position, bare);
            sb.AppendLine($"  ground: preview {pv.kind} \"{pv.verb}\"  ->  drop {dropOk}, "
                + $"row {row?.order} {row?.target}, body {moved:F2} m from the spot");
            Gate("setting-somebody-down-writes-no-order",
                dropOk && row != null && row.order == wasOrder && row.target == wasTarget,
                row != null ? $"{wasOrder} {wasTarget} -> {row.order} {row.target}" : "no row");
            Gate("and-leaves-the-body-where-it-was-put", moved < 2.5f, $"{moved:F2} m out");
        }
        else sb.AppendLine("  ground: SKIPPED — no clear ground found near the camp");

        // =====================================================================
        // 3b. THE THROW
        // =====================================================================
        //
        // Kevin, 2026-09-20: *"i want villagers / items to retain some
        // momentum if i drop them mid grab."*
        //
        // The whole risk in this feature is that it is a SHOW change sitting
        // on top of the one rule the Hand exists to keep: the order is what
        // the cursor promised at the release point, written in the release
        // frame. So every gate below is a pair — he flies, AND the books say
        // what they would have said if he had been set down like a chess
        // piece. The drops in every other section of this probe are made with
        // a STILL hand and must behave exactly as they always did, which the
        // first gate here is the explicit statement of.

        sb.AppendLine();
        sb.AppendLine("THE THROW:");

        if (haveBare)
        {
            Vector2 s = ToScreen(lens, bare, out _);
            hand.PickUp(resident);
            for (int f = 0; f < 10; f++) { hand.HoldAt(s); yield return null; }
            var stillPv = hand.Preview(s);
            bool stillOk = hand.DropAt(s, out string stillWhy);
            float stillErr = Island.FlatDistance(resident.transform.position, bare);
            if (hand.Holding) hand.Cancel();
            sb.AppendLine($"  still : ten frames on one spot -> drop {stillOk}, "
                + $"body {stillErr * 100f:F1} cm from the spot ({stillPv.kind})");
            Gate("a-still-drop-still-lands-where-it-was-let-go",
                stillOk && stillErr < 0.3f,
                stillOk ? $"{stillErr:F2} m out" : "refused: " + stillWhy);
        }
        else sb.AppendLine("  still : SKIPPED — no clear ground found near the camp");

        // --- a moving release, aimed at a tree, carrying inland ---------------
        //
        // The sweep ENDS on the tree, so the order is a concrete one that a
        // landing spot twenty metres further on could not have produced. The
        // direction is toward the island's middle, so the arc has land under
        // it whatever the shape of the place.
        if (haveTree)
        {
            Vector3 inland = target.transform.position - treeAt;
            inland.y = 0f;
            inland = inland.sqrMagnitude > 1f ? inland.normalized : Vector3.forward;
            Vector3 runUp = treeAt - inland * 26f;
            runUp.y = camp.GroundAt(runUp);

            // Stand him down somewhere harmless first, so "gathering timber"
            // afterwards is a change and not a coincidence.
            {
                Vector2 sFire = ToScreen(lens, camp.CampCentre, out _);
                yield return Do(hand, resident, sFire);
            }

            Vector2 sFrom = ToScreen(lens, runUp, out bool fromSeen);
            Vector2 sTo = ToScreen(lens, treeAt, out bool toSeen);
            if (!fromSeen || !toSeen)
            {
                sb.AppendLine("  throw : SKIPPED — the run-up is not on screen at this shot");
            }
            else
            {
                string beforeJson = JsonUtility.ToJson(camp.Ledger);
                yield return Sling(hand, resident, sFrom, sTo, 16);

                var atDrop = camp.HandNamed(who);
                OutpostOrder orderAtDrop = atDrop != null ? atDrop.order : OutpostOrder.Idle;
                string targetAtDrop = atDrop != null ? atDrop.target : "";
                string dropJson = JsonUtility.ToJson(camp.Ledger);
                var flier = CampWorker.Of(resident);
                bool flew = flier != null && flier.PhaseName == "Flying";

                yield return Land(resident);
                Vector3 restAt = resident.transform.position;
                string landJson = JsonUtility.ToJson(camp.Ledger);
                var afterLand = camp.HandNamed(who);

                float carried = Island.FlatDistance(restAt, releaseAt);
                Vector3 went = restAt - releaseAt;
                went.y = 0f;
                float along = went.sqrMagnitude > 0.01f
                    ? Vector3.Dot(went.normalized, inland) : 0f;
                float restGround = camp.GroundAt(restAt);
                float fromMiddle = Island.FlatDistance(restAt, target.transform.position);
                bool onLand = restGround > 0.5f && fromMiddle <= target.RadiusToward(restAt);

                sb.AppendLine($"  throw : preview {pv.kind} -> drop {dropOk}, flying {flew}; "
                    + $"carried {carried:F1} m, {along * 100f:F0}% along the sweep");
                sb.AppendLine($"          came to rest on ground {restGround:F1} m up, "
                    + $"{fromMiddle:F0} m from the middle of a {target.Radius:F0} m island");
                sb.AppendLine($"          row at the drop {orderAtDrop} {targetAtDrop}, "
                    + $"after landing {afterLand?.order} {afterLand?.target}");

                Gate("a-moving-release-throws-them",
                    dropOk && flew && carried > 3f && along > 0.5f,
                    $"drop {dropOk}, flying {flew}, {carried:F1} m at {along * 100f:F0}% along");
                Gate("and-they-come-to-rest-on-land", onLand,
                    $"ground {restGround:F1} m, {fromMiddle:F0} m out of {target.RadiusToward(restAt):F0}");
                // **The order is the cursor's, not the landing spot's.** He
                // was let go over a tree and came down twenty metres past it
                // on bare ground; a second resolve on touchdown would read
                // that ground and quietly stand him down.
                Gate("a-throw-writes-the-order-the-cursor-promised",
                    pv.kind == HandTarget.Kind.Tree
                    && orderAtDrop == OutpostOrder.Gather && targetAtDrop == Res.Timber,
                    $"preview {pv.kind}, row {orderAtDrop} {targetAtDrop}");
                Gate("and-writes-nothing-at-all-when-he-lands",
                    dropJson == landJson && afterLand != null
                    && afterLand.order == orderAtDrop && afterLand.target == targetAtDrop,
                    dropJson == landJson ? "the row changed on touchdown"
                                         : "the ledger moved while he was in the air");

                // He has an order. He must get on with it.
                Vector3 settled = resident.transform.position;
                float moved = 0f;
                float workBy = Time.realtimeSinceStartup + 3f;
                while (Time.realtimeSinceStartup < workBy)
                {
                    yield return null;
                    moved = Mathf.Max(moved, Island.FlatDistance(resident.transform.position, settled));
                }
                sb.AppendLine($"          {moved:F2} m walked in the three seconds after he got up");
                Gate("and-he-picks-himself-up-and-gets-on-with-it", moved > 0.8f,
                    $"{moved:F2} m in 3 s (phase {CampWorker.Of(resident)?.PhaseName})");

                // --- and the books cannot tell the two apart -----------------
                //
                // Same order, same target, one set down and one thrown, both
                // from the same restored ledger. Restored the way the D2
                // section below does it.
                JsonUtility.FromJsonOverwrite(beforeJson, camp.Ledger);
                yield return Do(hand, resident, sTo);
                string placedJson = JsonUtility.ToJson(camp.Ledger);

                JsonUtility.FromJsonOverwrite(beforeJson, camp.Ledger);
                yield return Sling(hand, resident, sFrom, sTo, 16);
                yield return Land(resident);
                string thrownJson = JsonUtility.ToJson(camp.Ledger);

                sb.AppendLine($"          set down vs thrown: "
                    + $"{(placedJson == thrownJson ? "identical books" : "DIFFERENT")}");
                if (placedJson != thrownJson)
                {
                    sb.AppendLine("            placed: " + (placedJson.Length > 300
                        ? placedJson.Substring(0, 300) + "…" : placedJson));
                    sb.AppendLine("            thrown: " + (thrownJson.Length > 300
                        ? thrownJson.Substring(0, 300) + "…" : thrownJson));
                }
                Gate("a-thrown-order-pays-exactly-what-a-placed-one-pays",
                    placedJson == thrownJson,
                    "the same order written two ways came out with different books");
            }
        }
        else sb.AppendLine("  throw : SKIPPED — no standing tree clear of the camp's buildings");

        // --- thrown at the sea, which is not somewhere he may land ------------
        //
        // A villager who could be thrown into the water would be a villager
        // the player can delete by accident, and his ledger row would go on
        // producing from a body floating off the beach. The drop itself is on
        // LAND — the sea is refused outright, gated below — so what is being
        // tested here is the ARC: let go at the shore, moving seaward, and he
        // must stop over the last of the island and come down on it.
        {
            Vector3 out0 = anchor.transform.position - target.transform.position;
            out0.y = 0f;
            out0 = out0.sqrMagnitude > 1f ? out0.normalized : Vector3.forward;

            // The beach she landed on lies on THIS bearing -- and so does she.
            // `HandTargets.Resolve` tests her deck first, within a hull radius
            // of the ray, so a shore point a few metres short of an anchored
            // hull answers "back aboard" -- and a drop that RECALLS him is not
            // a throw at all (2026-09-21: this gate read "rests on ground
            // -2.2 m, 103 m out of a 100 m shore", which was his station on
            // her deck, and the ship gate after it read able 8 -> 8 because he
            // was already aboard). The resolver's answer is required to be
            // land here, and if the shore under her is all deck the search
            // swings round to the next bearing.
            Vector3 shoreAt = default;
            Vector2 sShore = default;
            bool shoreOk = false;
            HandTarget shorePv = default;
            Vector3 outDir = out0;
            float[] swings = { 0f, 40f, -40f, 80f, -80f, 120f, -120f };
            foreach (float swing in swings)
            {
                if (shoreOk) break;
                outDir = Quaternion.Euler(0f, swing, 0f) * out0;
                Vector3 lastLand = default;
                bool haveShore = false;
                for (float r = 6f; r < target.Radius + 40f; r += 2f)
                {
                    Vector3 p = target.transform.position + outDir * r;
                    p.y = camp.GroundAt(p);
                    if (p.y > 1.5f) { lastLand = p; haveShore = true; continue; }
                    if (haveShore) break;          // the last land on this bearing
                }

                // Back off until the resolver will actually take a drop there
                // -- on LAND, not on her deck.
                for (int back = 0; back < 10 && haveShore; back++)
                {
                    Vector3 p = lastLand - outDir * (back * 2f);
                    p.y = camp.GroundAt(p);
                    sShore = ToScreen(lens, p, out bool seen);
                    if (!seen) continue;
                    var probe = hand.Preview(sShore);
                    if (!probe.Allowed || probe.kind == HandTarget.Kind.Water
                        || probe.kind == HandTarget.Kind.Ship) continue;
                    shoreAt = p;
                    shorePv = probe;
                    shoreOk = true;
                    break;
                }
            }

            if (!shoreOk)
            {
                sb.AppendLine("  seaward: SKIPPED — no shore point on screen that will take a drop");
            }
            else
            {
                Vector3 runUp = shoreAt - outDir * 26f;
                runUp.y = camp.GroundAt(runUp);
                Vector2 sFrom = ToScreen(lens, runUp, out bool seen2);
                if (!seen2)
                {
                    sb.AppendLine("  seaward: SKIPPED — the run-up is not on screen");
                }
                else
                {
                    yield return Sling(hand, resident, sFrom, sShore, 16);
                    yield return Land(resident);
                    Vector3 restAt = resident.transform.position;
                    float restGround = camp.GroundAt(restAt);
                    float fromMiddle = Island.FlatDistance(restAt, target.transform.position);
                    float shoreHere = target.RadiusToward(restAt);
                    float carried = Island.FlatDistance(restAt, releaseAt);
                    sb.AppendLine($"  seaward: let go over {shorePv.kind}, drop resolved {pv.kind} "
                        + $"({(dropOk ? "taken" : "refused: " + dropWhy)}); thrown {carried:F1} m at the water — "
                        + $"rests on ground {restGround:F1} m up, {fromMiddle:F0} m out "
                        + $"of a {shoreHere:F0} m shore");
                    Gate("a-throw-at-the-sea-comes-to-rest-on-land",
                        dropOk && restGround > 0.5f && fromMiddle <= shoreHere,
                        $"drop {dropOk}, ground {restGround:F1} m, {fromMiddle:F0} m against {shoreHere:F0}");
                }
            }
        }

        // --- the sea ------------------------------------------------------------
        //
        // On a bearing WELL AWAY from where she is lying. A point chosen out
        // past the ship is a point whose ray grazes her masthead on the way,
        // and "back aboard" is the answer the resolver would rightly give --
        // which is not what this gate is asking about.
        {
            Vector2 sSea = default;
            bool haveSea = false;
            for (int i = 0; i < 24 && !haveSea; i++)
            {
                float a = i * Mathf.PI * 2f / 24f;
                var p = target.transform.position
                    + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (target.Radius + 130f);
                p.y = 0f;
                if (Island.FlatDistance(p, anchor.transform.position) < 110f) continue;
                if (Island.TerrainHeight != null && Island.TerrainHeight(p.x, p.z) > -1f) continue;
                sSea = ToScreen(lens, p, out bool f);
                if (f) haveSea = true;
            }
            if (haveSea)
            {
                yield return Do(hand, resident, sSea);
                sb.AppendLine($"  sea   : preview {pv.kind} refusal \"{pv.refusal}\"  ->  "
                    + $"drop {dropOk} \"{dropWhy}\"");
                Gate("the-sea-is-refused", !dropOk && pv.kind == HandTarget.Kind.Water,
                    $"{pv.kind}, drop {dropOk}");
            }
            else sb.AppendLine("  sea   : SKIPPED — no open water in front of the lens at this shot");
        }

        // --- back aboard ---------------------------------------------------------
        {
            roster.Refresh();
            int ableBefore = roster.AbleCount;
            Vector2 s = ToScreen(lens, anchor.transform.position + Vector3.up * 2f, out bool shipInFront);
            HandTarget shipPv = default;
            bool recalled = false;
            if (shipInFront)
            {
                yield return Do(hand, resident, s);
                shipPv = pv;
                recalled = dropOk;
            }
            roster.Refresh();
            int ableAfter = roster.AbleCount;
            sb.AppendLine($"  ship  : preview {shipPv.kind} \"{shipPv.verb}\"  ->  drop {recalled}, "
                + $"able {ableBefore} -> {ableAfter}, row "
                + (camp.HandNamed(who) == null ? "gone" : "STILL THERE"));
            Gate("a-drop-on-the-ship-recalls-them",
                recalled && camp.HandNamed(who) == null && ableAfter == ableBefore + 1,
                $"drop {recalled}, able {ableBefore} -> {ableAfter}");
            Gate("the-preview-of-the-ship-is-the-ship",
                shipPv.kind == HandTarget.Kind.Ship, shipPv.kind.ToString());
        }

        // --- somebody off the deck, put ashore ------------------------------------
        if (haveBare)
        {
            SeaSick.Crew.CrewAgent fresh = null;
            foreach (var c in roster.All)
                if (c != null && c.IsAboard && camp.HandNamed(c.DisplayName) == null) { fresh = c; break; }
            if (fresh != null)
            {
                string freshName = fresh.DisplayName;
                Vector2 s = ToScreen(lens, bare, out _);
                yield return Do(hand, fresh, s);
                var row = camp.HandNamed(freshName);
                sb.AppendLine($"  aboard-> ground: {freshName} preview {pv.kind} -> drop {dropOk}, "
                    + $"row {(row != null ? row.order.ToString() : "none")}");
                Gate("a-hand-off-the-deck-can-be-put-ashore", dropOk && row != null,
                    dropOk ? "no row was written" : "the drop was refused: " + dropWhy);

                // And put them back, so the crew count is what it was.
                Vector2 sShip = ToScreen(lens, anchor.transform.position + Vector3.up * 2f, out bool ok2);
                if (ok2) { yield return Do(hand, fresh, sShip); }
                roster.Refresh();
            }
            else sb.AppendLine("  aboard-> ground: SKIPPED — nobody left aboard");
        }

        // =====================================================================
        // 4. D2 — watching must not beat leaving
        // =====================================================================
        //
        // Both legs go through `Hand.DropAt`. A leg that wrote the order by
        // calling `Outpost.OrderGather` directly would be measuring a route
        // the player cannot take.

        sb.AppendLine();
        sb.AppendLine("D2 — THE LEDGER IS THE TRUTH:");

        if (!haveTree)
        {
            sb.AppendLine("  SKIPPED — no tree to order anybody on to");
        }
        else
        {
            // Put the resident back ashore, and clear the ground so the
            // ceiling cannot clip either leg.
            Vector2 sTree = ToScreen(lens, treeAt, out _);
            yield return Do(hand, resident, sTree);

            var L = camp.Ledger;
            L.stores.Clear();
            double tA = TimeOfDay.Seconds;
            L.lastTicked = tA;
            camp.CatchUp();
            string mid = JsonUtility.ToJson(L);

            // Leg A: ordered once, then a day with nobody looking.
            TimeOfDay.Scrub(tA + TimeOfDay.DayLength);
            camp.CatchUp();
            float once = Held(L, Res.Timber);

            // Leg B: the same day, with the same order re-issued 240 times.
            JsonUtility.FromJsonOverwrite(mid, L);
            TimeOfDay.Scrub(tA);
            L.lastTicked = tA;
            for (int i = 1; i <= 240; i++)
            {
                TimeOfDay.Scrub(tA + (double)i / 240.0 * TimeOfDay.DayLength);
                hand.PickUp(resident);
                hand.DropAt(sTree, out _);
            }
            camp.CatchUp();
            float fussed = Held(L, Res.Timber);

            float quantum = Res.GatherRate(Res.Timber) * OutpostLedger.QuantumDays;
            sb.AppendLine($"  one order + a day away : {once:F4} timber");
            sb.AppendLine($"  240 re-drops, same day : {fussed:F4} timber   "
                + $"(one quantum is {quantum:F2})");
            Gate("fussing-over-a-camp-pays-what-leaving-it-pays",
                Mathf.Abs(once - fussed) <= quantum + 0.001f,
                $"{once:F4} against {fussed:F4}");

            // Leg C: half a day with a man in the air, against half a day
            // with nobody touched. A row in the ledger is a row in the
            // ledger; being held is not a state the arithmetic knows about.
            L.stores.Clear();
            double tC = TimeOfDay.Seconds;
            L.lastTicked = tC;
            string start = JsonUtility.ToJson(L);

            TimeOfDay.Scrub(tC + 0.5 * TimeOfDay.DayLength);
            camp.CatchUp();
            float untouched = Held(L, Res.Timber);

            JsonUtility.FromJsonOverwrite(start, L);
            TimeOfDay.Scrub(tC);
            L.lastTicked = tC;
            hand.PickUp(resident);
            TimeOfDay.Scrub(tC + 0.5 * TimeOfDay.DayLength);
            camp.CatchUp();
            float whileHeld = Held(L, Res.Timber);
            hand.Cancel();

            sb.AppendLine($"  half a day untouched   : {untouched:F4} timber");
            sb.AppendLine($"  half a day in the air  : {whileHeld:F4} timber");
            Gate("holding-somebody-changes-nothing",
                Mathf.Abs(untouched - whileHeld) < 0.0005f,
                $"{untouched:F4} against {whileHeld:F4}");
        }

        // =====================================================================
        // 5. A steady hold costs nothing
        // =====================================================================
        //
        // Measured inside ONE frame, with nothing else running between the two
        // readings, because over 120 frames of a live game every other system
        // in the scene would be charged to the Hand.

        sb.AppendLine();
        sb.AppendLine("WHAT HOVERING COSTS:");
        {
            Vector2 s0 = haveTree ? ToScreen(lens, treeAt, out _) : sHand;
            hand.PickUp(resident);
            for (int i = 0; i < 24; i++)
            {
                var s = s0 + new Vector2(i * 0.01f, 0f);
                hand.HoldAt(s);
                hand.Preview(s);
            }
            System.GC.Collect();
            long b0 = System.GC.GetTotalMemory(false);
            for (int i = 0; i < 120; i++)
            {
                // Nudged by a hundredth of a pixel each time, which defeats
                // the per-frame memo without ever changing what is under the
                // cursor -- a hover that crossed from a tree to bare ground
                // would honestly rebuild a string, and that is not what this
                // gate is about.
                var s = s0 + new Vector2(i * 0.01f, 0f);
                hand.HoldAt(s);
                hand.Preview(s);
            }
            long b1 = System.GC.GetTotalMemory(false);
            hand.Cancel();
            long bytes = b1 - b0;
            sb.AppendLine($"  120 holds + 120 previews: {bytes} bytes");
            Gate("a-steady-hold-allocates-nothing", bytes >= 0 && bytes < 2048,
                $"{bytes} bytes over 120 frames' worth of holding");
        }

        // =====================================================================
        // AND THE THING HE WAS DROPPED ON GETS BUILT
        //
        // Kevin, first play of the Hand: *"i dropped several workers on it and
        // they gathered logs for it but it never built."* Every gate above
        // stops at the ORDER being written. `LandProbe`'s lesson again: the
        // half after the prompt is the half that was broken. Last, because
        // finishing the build changes the camp the sections above measured.
        // =====================================================================
        if (camp.Building)
        {
            var p = camp.Ledger.Pending;
            string planId = p.planId;
            var plan = BuildPlans.Named(planId);
            int stoodBefore = camp.CountOf(planId);
            var builder = camp.HandNamed(who);
            if (builder != null) camp.OrderBuild(builder);

            float days = Mathf.Max(2f, p.needed / OutpostLedger.TimberPerHandPerDay * 1.5f);
            camp.Ledger.lastTicked = TimeOfDay.Seconds;
            camp.Ledger.Tick(TimeOfDay.Seconds + days * TimeOfDay.WorkDaySeconds);
            camp.CatchUp();
            yield return null;
            camp.CatchUp();
            yield return null;

            int stoodAfter = camp.CountOf(planId);
            sb.AppendLine();
            sb.AppendLine($"BUILDING THE {plan.label.ToUpperInvariant()} ({p.needed} logs, one hand, {days:F1} days):");
            if (camp.Ledger.Pending != null)
            {
                var q = camp.Ledger.Pending;
                Vector3 at = q.At; at.y = camp.GroundAt(at);
                bool could = camp.CanPlace(plan, at, q.yaw, out string whyNot);
                sb.AppendLine($"  STILL A DRAWING: {q.done} / {q.needed} logs in it, "
                    + $"{camp.Ledger.Wood.standing:F1} timber left standing, "
                    + $"{camp.Ledger.HandsOn(OutpostOrder.Build)} hand(s) building; "
                    + $"CanPlace now says {(could ? "yes" : "NO — " + whyNot)}");
            }
            else sb.AppendLine($"  it stands: {stoodBefore} -> {stoodAfter}, "
                + $"camp still at {camp.CampCentre.x:F0},{camp.CampCentre.z:F0}");
            Gate("a-blueprint-somebody-was-dropped-on-gets-built",
                camp.Ledger.Pending == null && stoodAfter == stoodBefore + 1,
                camp.Ledger.Pending != null ? "still a drawing" : $"{stoodBefore} -> {stoodAfter}");
        }

        TimeOfDay.Paused = wasPaused;
        Finish(null);
    }

    // =====================================================================
    // The one way this probe presses a button
    // =====================================================================

    /// Lift, look, let a frame pass, look again, let go.
    ///
    /// **The preview and the drop are deliberately a frame apart.** Inside one
    /// frame the Hand answers both out of the same memo, so "preview equals
    /// commit" would be comparing a value with itself; across a frame it is
    /// two independent resolves of the same ray, which is the thing worth
    /// gating.
    System.Collections.IEnumerator Do(Hand hand, SeaSick.Crew.CrewAgent who, Vector2 screen)
    {
        pv = default;
        dropOk = false;
        dropWhy = "";
        if (!hand.PickUp(who)) { dropWhy = "could not lift them"; yield break; }
        hand.HoldAt(screen);
        pv = hand.Preview(screen);
        yield return null;
        hand.HoldAt(screen);
        dropOk = hand.DropAt(screen, out dropWhy);
        if (hand.Holding) hand.Cancel();
    }

    /// Where the body was at the instant the hand let go. Not the same point
    /// as the cursor's: the body is spring-chased and lags a metre or so at a
    /// hard sweep, and the distance a throw CARRIES has to be measured from
    /// where he actually was, not from where he was being aimed.
    Vector3 releaseAt;

    /// **Lift, sweep the cursor across the ground, and let go at speed.**
    ///
    /// The straight screen path over several frames is exactly what
    /// `IslandInput` feeds the Hand when a finger or a mouse drags, so this is
    /// not a private copy of the gesture. The six frames on the start point
    /// first are not padding: the body is a spring chasing the cursor, and
    /// without them the sweep would begin mid-snap.
    System.Collections.IEnumerator Sling(Hand hand, SeaSick.Crew.CrewAgent who,
        Vector2 from, Vector2 to, int frames)
    {
        pv = default;
        dropOk = false;
        dropWhy = "";
        releaseAt = Vector3.zero;
        if (!hand.PickUp(who)) { dropWhy = "could not lift them"; yield break; }
        for (int f = 0; f < 6; f++) { hand.HoldAt(from); yield return null; }
        for (int f = 1; f <= frames; f++)
        {
            hand.HoldAt(Vector2.Lerp(from, to, f / (float)frames));
            yield return null;
        }
        pv = hand.Preview(to);
        releaseAt = who.transform.position;
        dropOk = hand.DropAt(to, out dropWhy);
        if (hand.Holding) hand.Cancel();
    }

    /// Wait out the arc. Six seconds is three times the longest a clamped
    /// throw can stay up.
    System.Collections.IEnumerator Land(SeaSick.Crew.CrewAgent who)
    {
        float by = Time.realtimeSinceStartup + 6f;
        while (Time.realtimeSinceStartup < by)
        {
            var w = CampWorker.Of(who);
            if (w == null || w.PhaseName != "Flying") yield break;
            yield return null;
        }
    }

    // =====================================================================
    // Finding things to point at
    // =====================================================================

    static Vector2 ToScreen(Camera cam, Vector3 at, out bool inFront)
    {
        Vector3 p = cam.WorldToScreenPoint(at);
        inFront = p.z > 0f;
        return new Vector2(p.x, p.y);
    }

    static float ProjectedHeight(Camera cam, Vector3 footAt)
    {
        Vector3 a = cam.WorldToScreenPoint(footAt);
        Vector3 b = cam.WorldToScreenPoint(footAt + Vector3.up * WorldScale.Person);
        return a.z > 0f && b.z > 0f ? Mathf.Abs(b.y - a.y) : 0f;
    }

    /// A tree that is only a tree: clear of every footprint the camp has, and
    /// far enough out that the resolver cannot mistake it for the fire.
    static bool FindTree(Outpost camp, out Vector3 at)
    {
        at = default;
        var ix = TreeIndex.For(camp);
        if (ix == null || ix.Wood == null) return false;
        var wood = ix.Wood;
        float best = float.MaxValue;
        bool any = false;
        for (int i = 0; i < wood.TreeCount; i++)
        {
            var t = wood.TreeAt(i);
            if (t.felled) continue;
            float d = Island.FlatDistance(t.baseAt, camp.CampCentre);
            if (d < 14f || d > 60f) continue;
            if (NearAnyBuilding(camp, t.baseAt, 6f)) continue;
            if (d < best) { best = d; at = t.baseAt; any = true; }
        }
        return any;
    }

    /// A prop of something that is not timber, with no tree standing close
    /// enough to win the resolver's ordering.
    static ResourceNode FindNode(Outpost camp)
    {
        var ix = TreeIndex.For(camp);
        var all = ResourceNode.All;
        ResourceNode best = null;
        float bestD = float.MaxValue;
        for (int i = 0; i < all.Count; i++)
        {
            var n = all[i];
            if (n == null || n.Harvested) continue;
            if (n.Home != camp.Island) continue;
            if (n.Resource == Res.Timber) continue;
            if (!HandTargets.Gatherable(camp, n.Resource)) continue;
            Vector3 p = n.transform.position;
            float d = Island.FlatDistance(p, camp.CampCentre);
            if (d > 90f) continue;
            if (NearAnyBuilding(camp, p, 6f)) continue;
            // The resolver tests trees before props, so a prop standing in the
            // wood is not a test of props.
            if (ix != null && ix.NearestStanding(p, 6f) >= 0) continue;
            if (d < bestD) { bestD = d; best = n; }
        }
        return best;
    }

    /// Ground that is nothing else. Asked of the resolver itself, which is
    /// fine: this is choosing a spot, not measuring one.
    static bool FindBare(Outpost camp, Hand hand, Camera lens, out Vector3 at)
    {
        at = default;
        for (int ring = 0; ring < 5; ring++)
            for (int i = 0; i < 16; i++)
            {
                float a = i * Mathf.PI * 2f / 16f + ring * 0.4f;
                float r = 10f + ring * 4f;
                var p = camp.CampCentre + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                p.y = camp.GroundAt(p);
                if (p.y <= 0.5f) continue;
                Vector2 s = ToScreen(lens, p, out bool inFront);
                if (!inFront) continue;
                if (hand.Preview(s).kind != HandTarget.Kind.Ground) continue;
                at = p;
                return true;
            }
        return false;
    }

    static bool NearAnyBuilding(Outpost camp, Vector3 p, float slack)
    {
        var built = camp.Built;
        for (int i = 0; i < built.Count; i++)
        {
            var b = built[i];
            if (b == null) continue;
            var plan = BuildPlans.Named(b.Id);
            if (HandTargets.InFootprint(p, b.transform.position,
                b.transform.eulerAngles.y, plan.footprint, slack)) return true;
        }
        var pend = camp.Ledger != null ? camp.Ledger.Pending : null;
        if (pend != null)
        {
            var plan = BuildPlans.Named(pend.planId);
            if (HandTargets.InFootprint(p, pend.At, pend.yaw, plan.footprint, slack)) return true;
        }
        return false;
    }

    static Vector3 FindSpot(Outpost camp, BuildPlan plan, float startRadius)
    {
        for (int ring = 0; ring < 7; ring++)
            for (int i = 0; i < 14; i++)
            {
                float a = i * Mathf.PI * 2f / 14f + ring * 0.9f;
                float r = startRadius + ring * 4f;
                var p = camp.CampCentre + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                p.y = camp.GroundAt(p);
                if (camp.CanPlace(plan, p, out _)) return p;
            }
        return camp.CampCentre;
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

    static float Held(OutpostLedger l, string resource)
    {
        var s = l.Store(resource);
        return s != null ? s.whole + s.part : 0f;
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
            ? "PASS — the Hand gives the order the cursor promised, and writes nothing else"
            : $"{fails} GATE(S) FAILED");
        Report(sb.ToString());
        Destroy(gameObject);
    }

    static void Report(string text)
    {
        Debug.Log("HandProbe\n" + text);
        var path = System.IO.Path.Combine(Application.dataPath, "../Logs/HandProbe.txt");
        try { System.IO.File.WriteAllText(path, text); } catch { }
    }
}
