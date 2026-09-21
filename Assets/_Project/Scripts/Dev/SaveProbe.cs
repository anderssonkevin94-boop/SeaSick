using System.Collections.Generic;
using System.Text;
using UnityEngine;
using SeaSick.Crew;
using SeaSick.Save;
using SeaSick.Ship;
using SeaSick.Voyage;
using SeaSick.World;

/// **Does everything a playtest builds come back where it stood?**
///
/// Kevin, 2026-09-21: *"I need to be able to save in game and, when
/// launching the game again, press New or Load. This way I can build
/// buildings, have them tweaked, and see the changes next time I play."*
/// The failure mode of a save is not a crash; it is a hut that came back
/// four metres to the left, a hand who came back idle, a clock that paid
/// out three days of timber on the way in. So every gate below is a field
/// that must be EQUAL to what was saved, in one session: build the state,
/// write it to a temp file, wipe the live scene back to nothing, read the
/// file back through the same `SaveGame.Restore` the CONTINUE button runs,
/// and compare.
///
/// What is built: a rung, a fitting, two bay decisions, a hold with two
/// kinds in it, stores at home, a camp on a real island with a store hut
/// raised at a chosen spot, a hut sited as a blueprint, two hands with two
/// different orders, and three trees felled. Then the tide of the survey
/// is waited out exactly as the CONTINUE path waits it out.
///
/// Plain C#, no editor references. Play mode, `Sea.unity`. Writes
/// `Logs/SaveProbe.txt`. It does NOT touch the player's own save file: the
/// round trip goes through a temp path, and `RunProbe.Call` has already
/// switched autosaves off for the session.
public class SaveProbe : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("SaveProbe: not in play mode"); return; }
        var old = FindAnyObjectByType<SaveProbe>();
        if (old != null) Destroy(old.gameObject);
        var runner = new GameObject("SaveProbeRunner").AddComponent<SaveProbe>();
        runner.StartCoroutine(runner.Run());
    }

    readonly StringBuilder sb = new StringBuilder();
    int fails;

    const int Rung = 14;
    const int RudderLevel = 1;
    const int TimberAboard = 7;
    const int BoardsAboard = 3;
    const int TimberBanked = 9;
    const int TreesToFell = 3;

    System.Collections.IEnumerator Run()
    {
        yield return new WaitForEndOfFrame();
        GameBoot.Skip();

        var anchor = Object.FindFirstObjectByType<AnchorController>();
        var motor = Object.FindFirstObjectByType<ShipMotor>();
        var voyage = Object.FindFirstObjectByType<VoyageManager>();
        if (anchor == null || motor == null || voyage == null)
        { Finish("no ship in the scene"); yield break; }
        var yard = motor.GetComponent<Shipyard>();
        var roster = motor.GetComponent<CrewRoster>();
        if (yard == null) { Finish("no Shipyard on the ship"); yield break; }

        float t0 = Time.realtimeSinceStartup;
        SeaSick.Terrain.TerrainWorldPopulator pop = null;
        while (Time.realtimeSinceStartup - t0 < 60f)
        {
            pop = Object.FindFirstObjectByType<SeaSick.Terrain.TerrainWorldPopulator>();
            if (pop != null && pop.Done) break;
            yield return null;
        }
        if (pop == null || !pop.Done) { Finish("the world never built"); yield break; }
        sb.AppendLine("world seed " + SaveGame.WorldSeed() + "   save path " + SaveGame.Path);
        Gate("the-world-has-a-seed", SaveGame.WorldSeed() != 0,
            "WorldSettings.seed is 0 -- island kinds, props and reefs re-roll every launch");

        // =====================================================================
        // BUILD THE STATE
        // =====================================================================

        // --- the ship ---------------------------------------------------------
        yard.Apply(Rung);
        yard.Fit.SetLevel(FitTrack.Rudder, RudderLevel);
        for (int i = 0; i < 3; i++) yard.AddCell(BayUse.Quarters);
        yard.AddCell(BayUse.Hold);
        yard.Refurnish();
        yield return null;
        int quarters0 = yard.Count(BayUse.Quarters), holdCells0 = yard.Count(BayUse.Hold);
        string cellBay = "", cellTier = "";
        var node = yard.Node;
        if (node != null)
            foreach (var b in node.bay_labels)
            {
                foreach (var t in node.tier_names)
                    if (yard.Use(b, t) == BayUse.Hold) { cellBay = b; cellTier = t; break; }
                if (cellBay != "") break;
            }
        sb.AppendLine("ship: rung " + yard.NodeIndex + "  rudder " + yard.Fit.Level(FitTrack.Rudder)
            + "  quarters " + quarters0 + "  hold cells " + holdCells0
            + "  (one at " + cellBay + "/" + cellTier + ")");

        // --- the hold and the stores ------------------------------------------
        var holdSeed = new List<KeyValuePair<string, int>>();
        holdSeed.Add(new KeyValuePair<string, int>(Res.Timber, TimberAboard));
        holdSeed.Add(new KeyValuePair<string, int>(Res.Boards, BoardsAboard));
        var bankSeed = new List<KeyValuePair<string, int>>();
        bankSeed.Add(new KeyValuePair<string, int>(Res.Timber, TimberBanked));
        voyage.RestoreStores(holdSeed, bankSeed);
        var hold = motor.GetComponent<ShipHold>();
        sb.AppendLine("hold: " + voyage.AmountOf(Res.Timber) + " timber, "
            + voyage.AmountOf(Res.Boards) + " boards, total " + voyage.TotalHeld
            + ", " + (hold != null ? hold.VisibleCount : -1) + " drawn;  banked timber "
            + voyage.Banked(Res.Timber));

        // --- somewhere with a beach on it (as CampLoadProbe finds one) -------
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
        sb.AppendLine("target " + target.name + "  r " + target.Radius.ToString("F0") + " m, "
            + bestGap.ToString("F0") + " m off");

        if (anchor.CurrentState != AnchorController.State.Underway)
        {
            anchor.CastOff();
            float cast = Time.realtimeSinceStartup;
            while (anchor.CurrentState != AnchorController.State.Underway
                   && Time.realtimeSinceStartup - cast < 10f) yield return null;
        }
        // Anchored, not landed: `MoorAt` is the path the load takes, and
        // it sends nobody down the plank, so the crew this probe stations
        // are all aboard and the anchor can be weighed without waiting for
        // a shore party to walk back.
        Quaternion facing = Quaternion.LookRotation(target.transform.position - standOff);
        float shipYaw = facing.eulerAngles.y;
        SaveGame.Warp(motor, standOff, shipYaw);
        yield return new WaitForFixedUpdate();
        yield return null;
        SaveGame.Warp(motor, standOff, shipYaw);
        bool moored = anchor.MoorAt(target);
        Gate("she-can-anchor-there", moored, "MoorAt refused (" + anchor.CurrentState + ")");
        if (!moored) { Finish("could not anchor"); yield break; }

        t0 = Time.realtimeSinceStartup;
        while (Outpost.Surveying(target) && Time.realtimeSinceStartup - t0 < 30f) yield return null;
        var camp = Outpost.Of(target);
        if (camp == null)
        { Finish(target.name + " will not take a camp -- this probe needs one that will"); yield break; }
        float settle = Time.realtimeSinceStartup + 8f;
        while (anchor.CurrentState == AnchorController.State.Dropping
               && Time.realtimeSinceStartup < settle) yield return null;
        yield return null;

        // --- a camp, the dev way ----------------------------------------------
        if (!camp.HasCamp)
        {
            camp.MakeCamp(out string campWhy);
            if (!camp.HasCamp) { Finish("could not make a camp: " + campWhy); yield break; }
        }
        var l = camp.Ledger;
        l.hands.Clear();
        l.lastTicked = TimeOfDay.Seconds;
        camp.CatchUp();

        // A store hut at a chosen spot.
        var store = RaiseNear(camp, BuildPlans.Storage, 12f);
        if (store != null) l.built.Add(BuildPlans.Storage.id);
        Gate("a-store-hut-stands", store != null, "nowhere near the fire would take a store hut");
        Vector3 storeAt = store != null ? store.transform.position : Vector3.zero;
        float storeYaw = store != null ? store.transform.eulerAngles.y : 0f;

        // Three trees down, BEFORE anybody is standing here to cut: with
        // no cutters enrolled, `SyncFelling` settles the whole debt at once
        // rather than waiting for a man to swing (rule (c)).
        var wood = target.GetComponentInChildren<SeaSick.Terrain.SceneryWood>();
        int felled0 = l.treesFelled;
        l.timberTaken = felled0 + TreesToFell;
        camp.SyncFelling();
        yield return null;

        // A hut, sited and unbuilt.
        bool sited = SiteNear(camp, BuildPlans.Hut, 18f, out string siteWhy);
        Gate("a-hut-is-sited", sited && l.pending != null, siteWhy);

        // The clock stops here and starts again at the end. The ledger
        // ticks in 18 s quanta of real time, and a quantum crossing between
        // the snapshot and the save would move the pile under the gates.
        TimeOfDay.Paused = true;

        // Two hands, two orders.
        var aboard = new List<CrewAgent>();
        foreach (var c in motor.GetComponentsInChildren<CrewAgent>(true))
            if (c != null && c.IsAboard && c.gameObject.activeInHierarchy) aboard.Add(c);
        int stationed = 0;
        for (int i = 0; i < aboard.Count && stationed < 2; i++)
            if (camp.Station(aboard[i])) stationed++;
        if (roster != null) roster.Refresh();
        Gate("two-hands-were-left", stationed == 2,
            stationed + " stationed of " + aboard.Count + " aboard (the ship needs berths)");
        if (l.hands.Count >= 1) camp.OrderGather(l.hands[0], Res.Timber);
        if (l.hands.Count >= 2) camp.OrderBuild(l.hands[1]);
        yield return null;
        camp.CatchUp();

        // --- the picture, before -----------------------------------------------
        var before = Snapshot(camp, voyage, yard, motor, anchor);
        // `ManCrew` clones share a displayName, so a stationed name can have
        // twins still aboard. Count them now; the restore must add none.
        int aboardBefore = 0;
        var aboardNames = new StringBuilder();
        foreach (var a in motor.GetComponentsInChildren<CrewAgent>(true))
            if (a != null && camp.HandNamed(a.DisplayName) != null) { aboardBefore++; aboardNames.Append(a.DisplayName).Append(' '); }
        sb.AppendLine("same-named bodies still aboard before the save: " + aboardBefore + "  (" + aboardNames.ToString().Trim() + ")");
        sb.AppendLine();
        sb.AppendLine("BEFORE: " + before.Line());
        Gate("the-camp-has-a-fire", camp.CountOf(BuildPlans.Campfire.id) == 1,
            camp.CountOf(BuildPlans.Campfire.id) + " fires");
        Gate("trees-were-felled", wood == null || l.treesFelled == felled0 + TreesToFell,
            l.treesFelled + " felled, wanted " + (felled0 + TreesToFell));
        Gate("the-ledger-recorded-where-things-stand",
            l.raised.Count == camp.Built.Count,
            l.raised.Count + " spots for " + camp.Built.Count + " buildings");

        // =====================================================================
        // SAVE
        // =====================================================================

        string path = System.IO.Path.Combine(Application.temporaryCachePath, "SaveProbe-roundtrip.json");
        try { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); } catch { }
        bool wrote = SaveGame.SaveTo(path, "SaveProbe");
        long bytes = 0;
        try { bytes = new System.IO.FileInfo(path).Length; } catch { }
        sb.AppendLine();
        sb.AppendLine("SAVED " + bytes + " bytes -> " + path);
        Gate("the-file-was-written", wrote && bytes > 0, "SaveTo returned " + wrote + ", " + bytes + " bytes");
        if (!wrote) { Finish("nothing to load"); yield break; }

        var data = SaveGame.Read(path);
        Gate("the-file-reads-back", data != null, "Read returned null");
        if (data == null) { Finish("unreadable"); yield break; }

        OutpostSave savedCamp = null;
        foreach (var os in data.outposts)
            if (os != null && !os.isHome && os.ledger != null
                && os.ledger.keyX == l.keyX && os.ledger.keyZ == l.keyZ) savedCamp = os;
        Gate("the-camp-is-in-the-file", savedCamp != null, data.outposts.Count + " outposts written, none with the camp's key");
        Gate("home-is-in-the-file", HasHome(data), "no isHome outpost");
        Gate("the-seed-is-in-the-file", data.worldSeed == SaveGame.WorldSeed(), "seed " + data.worldSeed);
        Gate("the-ship-is-in-the-file",
            data.ship.rung == Rung && data.ship.anchor == 1 && data.ship.cells.Count == quarters0 + holdCells0,
            "rung " + data.ship.rung + " anchor " + data.ship.anchor + " cells " + data.ship.cells.Count);
        if (savedCamp != null)
        {
            Gate("the-blueprint-is-in-the-file",
                savedCamp.ledger.pending != null && savedCamp.ledger.pending.planId == BuildPlans.Hut.id,
                "pending " + (savedCamp.ledger.pending != null ? savedCamp.ledger.pending.planId : "null"));
            Gate("the-spots-are-in-the-file", savedCamp.ledger.raised.Count == before.buildings,
                savedCamp.ledger.raised.Count + " spots");
            Gate("the-hands-are-in-the-file", savedCamp.ledger.hands.Count == before.hands,
                savedCamp.ledger.hands.Count + " hands");
        }

        // =====================================================================
        // WIPE
        // =====================================================================
        //
        // Back to nothing, the way a fresh boot is: hands aboard, ledger
        // empty, buildings down, the ship on her default rung with an empty
        // hold, under way somewhere else, the clock at zero.

        foreach (var a in camp.Parked())
            if (a != null && camp.HandNamed(a.DisplayName) != null)
            {
                camp.Recall(a, motor.transform);
                CampWorker.Remove(a);
            }
        if (roster != null) roster.Refresh();
        camp.Adopt(OutpostLedger.For(camp.ClearingCentre, 1f), Vector3.zero, false);
        yield return null;
        yield return null;

        float away = Time.realtimeSinceStartup + 20f;
        while (anchor.CurrentState != AnchorController.State.Underway
               && Time.realtimeSinceStartup < away)
        {
            anchor.CastOff();
            yield return null;
        }
        SaveGame.Warp(motor, standOff + (standOff - target.transform.position).normalized * 300f, 0f);
        yard.Apply(12);
        yard.Fit.SetLevel(FitTrack.Rudder, 0);
        if (node != null)
            foreach (var b in node.bay_labels)
                foreach (var t in node.tier_names)
                    yard.SetUseQuiet(b, t, BayUse.Empty);
        yard.Refurnish();
        voyage.RestoreStores(new List<KeyValuePair<string, int>>(), new List<KeyValuePair<string, int>>());
        double savedClock = data.timeSeconds;
        TimeOfDay.Scrub(0.0);
        yield return null;

        var wiped = Snapshot(camp, voyage, yard, motor, anchor);
        sb.AppendLine();
        sb.AppendLine("WIPED:  " + wiped.Line());
        Gate("the-wipe-took", wiped.fires == 0 && wiped.buildings == 0 && wiped.hands == 0
            && wiped.rung == 12 && wiped.held == 0 && wiped.anchor == 0,
            "something survived the wipe -- the gates below would measure nothing");

        // =====================================================================
        // LOAD
        // =====================================================================

        yield return SaveGame.Restore(data, this);
        Gate("restore-reported-success", SaveGame.LastRestoreOk, SaveGame.LastRestoreNote);
        // Give the anchor and the arrangement a frame to settle.
        yield return null;
        yield return null;

        var after = Snapshot(camp, voyage, yard, motor, anchor);
        var l2 = camp.Ledger;
        sb.AppendLine();
        sb.AppendLine("AFTER:  " + after.Line());

        Gate("the-clock-comes-back",
            System.Math.Abs(TimeOfDay.Seconds - savedClock) < 2.0,
            TimeOfDay.Seconds.ToString("F1") + " against " + savedClock.ToString("F1"));
        Gate("no-phantom-backlog-was-paid",
            l2.CountOf(Res.Timber) == before.timberPile,
            "pile " + before.timberPile + " -> " + l2.CountOf(Res.Timber));

        // With the steamer selected (`SteamerBootstrap.Selected`, a
        // PlayerPref) the yard is stood down and `SaveGame.Restore` leaves
        // the rung, fittings and bays alone BY DESIGN -- an Apply would
        // build a ladder hull over the steamer. Those three gates measure a
        // path the restore does not run, so they are skipped, not failed.
        if (Shipyard.SuppressApplyOnStart)
            sb.AppendLine("  [skip] the-rung / the-fitting / the-bays -- the steamer has the hull, the yard is not restored by design");
        else
        {
            Gate("the-rung-comes-back", after.rung == Rung, "rung " + after.rung);
            Gate("the-fitting-comes-back", yard.Fit.Level(FitTrack.Rudder) == RudderLevel,
                "rudder " + yard.Fit.Level(FitTrack.Rudder));
            Gate("the-bays-come-back",
                yard.Count(BayUse.Quarters) == quarters0 && yard.Count(BayUse.Hold) == holdCells0
                && (cellBay == "" || yard.Use(cellBay, cellTier) == BayUse.Hold),
                "quarters " + yard.Count(BayUse.Quarters) + " hold " + yard.Count(BayUse.Hold));
        }

        Gate("the-hold-comes-back",
            voyage.AmountOf(Res.Timber) == TimberAboard && voyage.AmountOf(Res.Boards) == BoardsAboard
            && voyage.TotalHeld == TimberAboard + BoardsAboard,
            voyage.AmountOf(Res.Timber) + " timber, " + voyage.AmountOf(Res.Boards) + " boards, total " + voyage.TotalHeld);
        Gate("and-the-stack-on-deck-with-it",
            hold == null || hold.VisibleCount == TimberAboard + BoardsAboard,
            (hold != null ? hold.VisibleCount : -1) + " drawn");
        Gate("the-stores-come-back", voyage.Banked(Res.Timber) == TimberBanked,
            voyage.Banked(Res.Timber) + " banked");

        Gate("the-ship-comes-back-where-she-was",
            Island.FlatDistance(motor.transform.position, before.shipAt) < 6f,
            Island.FlatDistance(motor.transform.position, before.shipAt).ToString("F1") + " m off");
        Gate("and-anchored-as-she-was", after.anchor == 1 && anchor.CurrentIsland == target,
            "anchor " + after.anchor + " at " + (anchor.CurrentIsland != null ? anchor.CurrentIsland.name : "nothing"));

        Gate("a-saved-camp-comes-back-on-its-island", Outpost.Of(target) == camp && camp.HasCamp,
            "Of(target) " + (Outpost.Of(target) != null) + " HasCamp " + camp.HasCamp);
        Gate("a-saved-camp-comes-back-where-it-stood",
            camp.HasCampCentre && Island.FlatDistance(camp.CampCentre, before.campAt) < 0.5f,
            Island.FlatDistance(camp.CampCentre, before.campAt).ToString("F2") + " m off, hasCentre " + camp.HasCampCentre);
        Gate("the-key-comes-back", l2.keyX == before.keyX && l2.keyZ == before.keyZ,
            "(" + l2.keyX + "," + l2.keyZ + ") against (" + before.keyX + "," + before.keyZ + ")");
        Gate("the-fire-comes-back", camp.CountOf(BuildPlans.Campfire.id) == 1,
            camp.CountOf(BuildPlans.Campfire.id) + " fires");

        Building store2 = null;
        foreach (var b in camp.Built) if (b != null && b.Id == BuildPlans.Storage.id) store2 = b;
        Gate("the-store-hut-comes-back", store == null || store2 != null, "no store hut standing");
        Gate("the-store-hut-comes-back-where-it-stood",
            store == null || (store2 != null
                && Island.FlatDistance(store2.transform.position, storeAt) < 0.5f
                && Mathf.Abs(Mathf.DeltaAngle(store2.transform.eulerAngles.y, storeYaw)) < 1f),
            store2 != null
                ? Island.FlatDistance(store2.transform.position, storeAt).ToString("F2") + " m off, yaw "
                    + store2.transform.eulerAngles.y.ToString("F0") + " against " + storeYaw.ToString("F0")
                : "not standing");
        Gate("nothing-was-paid-twice", after.buildings == before.buildings && l2.built.Count == before.builtRows,
            after.buildings + " buildings for " + before.buildings + ", built rows " + l2.built.Count + " for " + before.builtRows);

        Gate("the-blueprint-comes-back",
            l2.pending != null && l2.pending.planId == BuildPlans.Hut.id
            && Mathf.Abs(l2.pending.x - before.pendingX) < 0.01f && Mathf.Abs(l2.pending.z - before.pendingZ) < 0.01f
            && Mathf.Abs(l2.pending.yaw - before.pendingYaw) < 0.01f
            && l2.pending.needed == before.pendingNeeded && l2.pending.done == before.pendingDone,
            l2.pending != null ? l2.pending.planId + " at " + l2.pending.x.ToString("F1") + "," + l2.pending.z.ToString("F1")
                : "no pending row");
        var ghost = camp.GetComponentInChildren<BuildSite>();
        Gate("and-the-drawing-with-it", ghost != null && ghost.PlanId == BuildPlans.Hut.id,
            ghost != null ? "drawing " + ghost.PlanId : "no BuildSite under the camp");

        Gate("the-hands-come-back", l2.hands.Count == before.hands, l2.hands.Count + " rows");
        bool ordersOk = l2.hands.Count == before.handNames.Count;
        for (int i = 0; ordersOk && i < l2.hands.Count; i++)
            ordersOk = l2.hands[i].name == before.handNames[i]
                && l2.hands[i].order == before.handOrders[i]
                && l2.hands[i].target == before.handTargets[i];
        Gate("with-their-orders", ordersOk, Orders(l2));
        int bodies = 0;
        foreach (var a in camp.Parked())
            if (a != null && camp.HandNamed(a.DisplayName) != null) bodies++;
        Gate("and-their-bodies", bodies == before.hands, bodies + " bodies for " + before.hands + " rows");
        int aboardNow = 0;
        foreach (var a in motor.GetComponentsInChildren<CrewAgent>(true))
            if (a != null && camp.HandNamed(a.DisplayName) != null) aboardNow++;
        Gate("and-nobody-is-in-two-places", aboardNow == aboardBefore,
            aboardNow + " of the camp's hands under the ship, " + aboardBefore + " before the save (clones share a name)");

        Gate("the-felled-count-comes-back", l2.treesFelled == before.felled && Mathf.Abs(l2.timberTaken - before.taken) < 0.01f,
            l2.treesFelled + " felled / " + l2.timberTaken.ToString("F1") + " taken");
        Gate("and-the-wood-agrees", wood == null || wood.FelledInMesh() >= l2.treesFelled,
            (wood != null ? wood.FelledInMesh() : -1) + " down in the mesh for " + l2.treesFelled);
        Gate("the-stores-on-the-ground-come-back",
            SameStores(before.stores, l2), Stores(l2) + " against " + before.storeLine);
        Gate("the-ceiling-comes-back", l2.ceilingPer == before.ceiling, l2.ceilingPer + " against " + before.ceiling);

        var home = Outpost.Home;
        Gate("home-comes-back-with-its-ledger", home != null && home.Ledger != null,
            "home " + (home != null) + " ledger " + (home != null && home.Ledger != null));

        try { System.IO.File.Delete(path); } catch { }
        Finish(null);
    }

    // --- staging helpers -------------------------------------------------------

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
                // A chosen yaw, so the round trip has to carry it rather
                // than recompute it.
                var b = camp.Raise(plan, p, 45f);
                if (b != null) return b;
            }
        return null;
    }

    static bool SiteNear(Outpost camp, BuildPlan plan, float startRadius, out string why)
    {
        why = "nowhere would take it";
        for (int ring = 0; ring < 7; ring++)
            for (int i = 0; i < 14; i++)
            {
                float a = i * Mathf.PI * 2f / 14f + ring * 0.9f;
                float r = startRadius + ring * 4f;
                var p = camp.CampCentre + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                p.y = camp.GroundAt(p);
                if (!camp.CanPlace(plan, p, 90f, out why)) continue;
                if (camp.Site(plan, p, 90f, out why) >= 0) return true;
            }
        return false;
    }

    static bool HasHome(SaveData d)
    {
        foreach (var o in d.outposts) if (o != null && o.isHome) return true;
        return false;
    }

    static string Orders(OutpostLedger l)
    {
        var s = new StringBuilder();
        foreach (var h in l.hands)
            if (h != null) s.Append(h.name).Append(':').Append(h.order).Append('/').Append(h.target).Append("  ");
        return s.ToString();
    }

    static string Stores(OutpostLedger l)
    {
        var s = new StringBuilder();
        foreach (var st in l.stores)
            if (st != null) s.Append(st.resource).Append(' ').Append(st.whole).Append("  ");
        return s.ToString();
    }

    static bool SameStores(Dictionary<string, int> want, OutpostLedger l)
    {
        foreach (var kv in want) if (l.CountOf(kv.Key) != kv.Value) return false;
        foreach (var st in l.stores)
            if (st != null && st.whole > 0 && !want.ContainsKey(st.resource)) return false;
        return true;
    }

    class Shot
    {
        public int fires, buildings, builtRows, hands, rung, held, anchor, felled, ceiling, keyX, keyZ;
        public float taken;
        public int timberPile;
        public Vector3 shipAt, campAt;
        public float pendingX, pendingZ, pendingYaw;
        public int pendingNeeded, pendingDone;
        public List<string> handNames = new List<string>();
        public List<OutpostOrder> handOrders = new List<OutpostOrder>();
        public List<string> handTargets = new List<string>();
        public Dictionary<string, int> stores = new Dictionary<string, int>();
        public string storeLine = "";

        public string Line()
        {
            return "fires " + fires + "  buildings " + buildings + "  hands " + hands
                + "  felled " + felled + "  rung " + rung + "  held " + held + "  anchor " + anchor
                + "  camp " + campAt.x.ToString("F1") + "," + campAt.z.ToString("F1")
                + "  ship " + shipAt.x.ToString("F0") + "," + shipAt.z.ToString("F0")
                + "  stores " + storeLine;
        }
    }

    static Shot Snapshot(Outpost camp, VoyageManager voyage, Shipyard yard, ShipMotor motor,
        AnchorController anchor)
    {
        var s = new Shot();
        var l = camp.Ledger;
        s.fires = camp.CountOf(BuildPlans.Campfire.id);
        s.buildings = camp.Built.Count;
        s.builtRows = l != null ? l.built.Count : 0;
        s.hands = l != null ? l.hands.Count : 0;
        s.rung = yard.NodeIndex;
        s.held = voyage.TotalHeld;
        s.anchor = anchor.AtHomeDock ? 2
            : (anchor.CurrentState != AnchorController.State.Underway && anchor.CurrentIsland != null ? 1 : 0);
        s.felled = l != null ? l.treesFelled : 0;
        s.taken = l != null ? l.timberTaken : 0f;
        s.ceiling = l != null ? l.ceilingPer : 0;
        s.keyX = l != null ? l.keyX : 0;
        s.keyZ = l != null ? l.keyZ : 0;
        s.timberPile = l != null ? l.CountOf(Res.Timber) : 0;
        s.shipAt = motor.transform.position;
        s.campAt = camp.CampCentre;
        if (l != null && l.pending != null)
        {
            s.pendingX = l.pending.x; s.pendingZ = l.pending.z; s.pendingYaw = l.pending.yaw;
            s.pendingNeeded = l.pending.needed; s.pendingDone = l.pending.done;
        }
        if (l != null)
        {
            foreach (var h in l.hands)
            {
                if (h == null) continue;
                s.handNames.Add(h.name); s.handOrders.Add(h.order); s.handTargets.Add(h.target);
            }
            foreach (var st in l.stores)
                if (st != null && st.whole > 0) s.stores[st.resource] = st.whole;
            s.storeLine = Stores(l);
        }
        return s;
    }

    void Gate(string name, bool ok, string detail)
    {
        if (!ok) fails++;
        sb.AppendLine("  [" + (ok ? "ok  " : "FAIL") + "] " + name + "   " + (ok ? "" : detail));
    }

    void Finish(string stopped)
    {
        TimeOfDay.Paused = false;
        sb.AppendLine();
        if (!string.IsNullOrEmpty(stopped)) sb.AppendLine("STOPPED: " + stopped);
        sb.AppendLine(fails == 0
            ? "PASS -- everything a playtest built came back where it stood"
            : fails + " GATE(S) FAILED");
        Report(sb.ToString());
        Destroy(gameObject);
    }

    static void Report(string text)
    {
        Debug.Log("SaveProbe\n" + text);
        var path = System.IO.Path.Combine(Application.dataPath, "../Logs/SaveProbe.txt");
        try { System.IO.File.WriteAllText(path, text); } catch { }
    }
}
