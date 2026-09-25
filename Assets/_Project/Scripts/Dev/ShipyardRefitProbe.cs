using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using SeaSick.Crew;
using SeaSick.Ocean;
using SeaSick.Save;
using SeaSick.Ship;
using SeaSick.Ship.Modular;
using SeaSick.Steamer;
using SeaSick.Voyage;
using SeaSick.World;

/// **Does a refit keep her, and does she still sail?** (modular shipyard,
/// 2026-09-24, docs/SHIPYARD-API.md)
///
/// (a) repeated refits at the home berth: Long -> Short -> 3 bays -> timber
///     -> reinforced -> Long; after each: the same GameObject and Rigidbody,
///     exactly one player ship / shipyard / listener, damage, hold per kind
///     and the crew roster unchanged, hands on the new deck, the drawn axle
///     on the physics axle, motor/drive/body lengths from the new hull form,
///     her rigidbody mass = the module lightship sum (one mass source), the
///     hold capacity, berths and guns carried from the plan's authored
///     per-module capacity;
/// (b) refused refits (oversized, W2, raised deck, 4 bays, stale draft,
///     cargo that would not fit, crew that would not fit, under way) leave
///     her bit-identical in the same frame;
/// (b1) an UNPAIRED gun (2026-09-25 fix): remove only the middle PORT gun
///      (starboard kept), apply, check the live battery is honestly
///      asymmetric (5 guns, 2 port, 3 starboard) rather than mirroring a
///      phantom port gun back off the untouched starboard list; restore her;
/// (b2) equipment + dry dock (2026-09-25): live, at the home berth, remove
///      the 2 middle guns (RemoveEquipment), shrink to Short (now valid --
///      guns are explicit equipment, never struck), check the dock holds 2,
///      grow back to Long-shaped without them, fit the 2 back from the dock
///      (FitEquipment), check the battery carries 6 guns again;
/// (c) save/load through a temp file: save, refit to Short, load -> the
///     saved configuration and hold; an old-format save (field removed) ->
///     the standard steamer;
/// (d) sea trial for Long, Short and 3 bays in searched-for open water:
///     20 s full ahead, 10 s full helm. Sanity gates only (no NaN, afloat,
///     moving, |roll| < 25 deg) -- the numbers are printed, not judged.
///
/// Needs the STEAMER (menu SeaSick/Dev/Sail the Steamer, PlayerPrefs
/// SeaSick.Steamer = 1). Play mode, Sea.unity, ~2.5 min. Never touches the
/// player's save: refits persist to a temp file (PersistPathOverride) and
/// the round trip uses temp files. Writes Logs/ShipyardRefitProbe.txt.
// NOTE 2026-09-24, superseded 2026-09-25: Short used to be refused outright
// (a gun pair without a slot, struck guns not yet supported); guns are now
// explicit equipment with a dry dock to hold what does not fit, so Short
// builds once her middle guns are taken off first (b2, below). Lengths
// trialled at sea are still Long, 2 bays and 3 bays (unchanged scope).
public class ShipyardRefitProbe : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("ShipyardRefitProbe: not in play mode"); return; }
        var old = FindAnyObjectByType<ShipyardRefitProbe>();
        if (old != null) Destroy(old.gameObject);
        var runner = new GameObject("ShipyardRefitProbeRunner").AddComponent<ShipyardRefitProbe>();
        runner.StartCoroutine(runner.Run());
    }

    readonly StringBuilder sb = new StringBuilder();
    int fails, passes;

    ShipyardService yard;
    ShipMotor motor;
    Rigidbody rb;
    AnchorController anchor;
    VoyageManager voyage;
    HullIntegrity hull;
    HelmInput helm;
    int shipId, bodyId, listeners;
    readonly List<CrewAgent> landed = new List<CrewAgent>();
    readonly List<string> draftRows = new List<string>();

    IEnumerator Run()
    {
        // Not WaitForEndOfFrame: it never resumes in batch mode (no frame is
        // rendered), which hung the first batch run for its full timeout.
        yield return null;
        GameBoot.Skip();
        float t0 = Time.realtimeSinceStartup;
        SeaSick.Terrain.TerrainWorldPopulator pop = null;
        while (Time.realtimeSinceStartup - t0 < 90f)
        {
            pop = FindFirstObjectByType<SeaSick.Terrain.TerrainWorldPopulator>();
            if (pop != null && pop.Done && ShipyardService.Player != null) break;
            yield return null;
        }
        yard = ShipyardService.Player;
        if (yard == null) { Finish("no ShipyardService: the steamer is not selected (SeaSick/Dev/Sail the Steamer) or she failed to convert"); yield break; }
        if (pop == null || !pop.Done) { Finish("the world never built"); yield break; }
        motor = yard.GetComponent<ShipMotor>();
        rb = yard.GetComponent<Rigidbody>();
        anchor = yard.GetComponent<AnchorController>();
        hull = yard.GetComponent<HullIntegrity>();
        voyage = FindFirstObjectByType<VoyageManager>();
        helm = yard.GetComponent<HelmInput>();
        if (motor == null || rb == null || anchor == null || voyage == null) { Finish("ship parts missing"); yield break; }
        t0 = Time.realtimeSinceStartup;
        while (!anchor.StartedDocked && Time.realtimeSinceStartup - t0 < 10f) yield return null;
        yield return new WaitForSeconds(1f);

        shipId = yard.gameObject.GetInstanceID();
        bodyId = rb.GetInstanceID();
        listeners = FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length;
        var refData = SteamerBootstrap.ReferenceData();
        Gate("reference-matches-bootstrap-scale", ShipyardSelfTest.PlaytestScale == SteamerBootstrap.PlaytestScale,
            $"self-test mirror {ShipyardSelfTest.PlaytestScale} vs bootstrap {SteamerBootstrap.PlaytestScale}");
        var steamer = yard.GetComponent<SteamerShip>();
        Gate("untouched-ship-is-reference", !yard.ModularActive && steamer != null && Mathf.Approximately(steamer.Data.lwl, refData.lwl)
            && Mathf.Approximately(rb.mass, refData.massKg) && Mathf.Approximately(motor.HullLength, refData.lwl),
            $"lwl {steamer?.Data.lwl:F3} mass {rb.mass:F0} kg HullLength {motor.HullLength:F3}");

        // A known state: some damage, two kinds in the hold, the berth.
        string why0;
        if (!anchor.AtHomeDock && !anchor.BerthAtHome(out why0)) sb.AppendLine("could not berth at the start: " + why0);
        yield return new WaitForSeconds(0.5f);
        if (hull != null) hull.Batter(yard.transform.position, 0.15f);
        voyage.RestoreStores(new[] { Pair(Res.Timber, 3), Pair(Res.Stone, 2) }, Banked());
        yield return null;
        sb.AppendLine($"start: {Crew().Count} hands, hold {voyage.TotalHeld}/{voyage.HoldCapacity}, integrity {Integrity():F3}, "
            + $"can refit: {yard.CanRefitNow(out string r0)} {r0}");
        yield return MeasureDraft("standard (untouched V8)", yard.Validate(ShipConfiguration.Long()).draftPlan);

        // ---- (b) refused refits, then (a) repeated refits ----------------
        var lng = ShipConfiguration.Long();
        Refuse("oversized", Mod(lng, c => c.rotorId = ShipConfiguration.OversizedRotor), ShipyardCodes.NotInPrototype);
        Refuse("w2", Mod(lng, c => c.middleIds[0] = "hull.middle.w2broad.placeholder"), ShipyardCodes.NotInPrototype);
        Refuse("raised-deck", Mod(lng, c => c.fittings.Add(new FittingChoice { socketId = "stern/UpperDeckMount", moduleId = "deck.upper.partial.placeholder" })), ShipyardCodes.NotInPrototype);
        Refuse("four-bays", ShipConfiguration.WithMiddles(4), "TOO_MANY_MIDDLES");
        Refuse("stale-draft", ShipConfiguration.Short(), ShipyardCodes.StaleDraft, ShipConfiguration.Short());
        // 2026-09-25 (Kevin): surplus hands are never a refusal any more --
        // they go ashore to the home settlement instead. See the dedicated
        // "hands ashore" gates in the equipment + dry dock block below.
        voyage.RestoreStores(new[] { Pair(Res.Timber, 14), Pair(Res.Stone, 2) }, Banked());
        Refuse("cargo-would-not-fit", ShipConfiguration.Short(), ShipyardCodes.CargoWouldNotFit);
        voyage.RestoreStores(new[] { Pair(Res.Timber, 3), Pair(Res.Stone, 2) }, Banked());


        string tmp = Application.temporaryCachePath;
        ShipyardService.PersistPathOverride = System.IO.Path.Combine(tmp, "ShipyardRefitProbe-persist.json");
        yield return RefitAndCheck("two-bays-first", ShipConfiguration.WithMiddles(2));
        Gate("apply-persisted-through-save-routine", System.IO.File.Exists(ShipyardService.PersistPathOverride)
            && System.IO.File.ReadAllText(ShipyardService.PersistPathOverride).Contains("hull.bow.w1r2.v3"),
            ShipyardService.PersistPathOverride);
        yield return RefitAndCheck("long-between", ShipConfiguration.Long());
        yield return RefitAndCheck("three-bays", ShipConfiguration.WithMiddles(3));
        yield return RefitAndCheck("three-bays-timber", Mod(ShipConfiguration.WithMiddles(3), c => c.rotorId = ShipConfiguration.TimberRotor));
        yield return RefitAndCheck("three-bays-reinforced", ShipConfiguration.WithMiddles(3));
        foreach (var h in landed) if (h != null) h.gameObject.SetActive(true);
        landed.Clear();
        yield return RefitAndCheck("long", ShipConfiguration.Long());

        // Under way: refused.
        anchor.CastOff();
        yield return null;
        motor.Anchored = false;
        if (helm != null) helm.enabled = false;
        motor.ThrottleOrder = 1f;
        yield return new WaitForSeconds(4f);
        Refuse("under-way", ShipConfiguration.Short(), ShipyardCodes.CannotRefitNow);
        motor.ThrottleOrder = 0f;
        if (!anchor.BerthAtHome(out string bw)) sb.AppendLine("could not re-berth: " + bw);
        yield return new WaitForSeconds(1f);

        // ---- (c) save / load ------------------------------------------------
        var saveA = System.IO.Path.Combine(tmp, "ShipyardRefitProbe-a.json");
        var saveOld = System.IO.Path.Combine(tmp, "ShipyardRefitProbe-old.json");
        for (int i = Crew().Count - 1; i >= 4; i--) { var h = Crew()[i]; landed.Add(h); h.gameObject.SetActive(false); }
        yield return RefitAndCheck("three-bays-for-save", ShipConfiguration.WithMiddles(3));
        var savedCfg = yard.Current;
        var savedHold = HoldPerKind();
        bool wrote = SaveGame.SaveTo(saveA, "ShipyardRefitProbe");
        yield return RefitAndCheck("long-before-load", ShipConfiguration.Long());
        var data = SaveGame.Read(saveA);
        Gate("save-has-modular-field", wrote && data != null && !string.IsNullOrEmpty(data.ship.modular), data != null ? data.ship.modular : "unreadable");
        if (data != null)
        {
            yield return SaveGame.Restore(data, this);
            yield return new WaitForSeconds(0.5f);
            Gate("load-brings-back-saved-configuration", SaveGame.LastRestoreOk && yard.ModularActive && yard.Current.ValueEquals(savedCfg)
                && SameHold(savedHold, HoldPerKind()) && Mathf.Approximately(motor.HullLength, yard.ActiveData.lwl),
                $"{SaveGame.LastRestoreNote}; middles {yard.Current.middleIds.Count}; hold {Fmt(HoldPerKind())} vs {Fmt(savedHold)}");
            string json = System.IO.File.ReadAllText(saveA);
            string oldJson = System.Text.RegularExpressions.Regex.Replace(json, ",\\s*\"modular\"\\s*:\\s*\"(?:[^\"\\\\]|\\\\.)*\"", "");
            System.IO.File.WriteAllText(saveOld, oldJson);
            var od = SaveGame.Read(saveOld);
            Gate("old-format-save-has-no-field", od != null && od.ship.modular == "" && !oldJson.Contains("\"modular\""), od != null ? "read" : "unreadable");
            if (od != null)
            {
                yield return SaveGame.Restore(od, this);
                yield return new WaitForSeconds(0.5f);
                var sd = yard.GetComponent<SteamerShip>().Data;
                Gate("old-format-save-loads-as-standard-steamer", !yard.ModularActive && yard.Current.ValueEquals(ShipConfiguration.Long())
                    && Mathf.Approximately(rb.mass, refData.massKg) && Mathf.Approximately(sd.lwl, refData.lwl) && voyage.HoldCapacity == 16,
                    $"modular {yard.ModularActive} mass {rb.mass:F0} lwl {sd.lwl:F3} hold cap {voyage.HoldCapacity}");
            }
        }
        foreach (var h in landed) if (h != null) h.gameObject.SetActive(true);
        landed.Clear();

        // ---- (d) sea trials ------------------------------------------------------
        Vector3 spot = default; bool found = false;
        var th = Island.TerrainHeight;
        if (th != null)
            for (float dist = 800f; dist <= 8000f && !found; dist += 400f)
                for (int b = 0; b < 12 && !found; b++)
                {
                    float a = b / 12f * Mathf.PI * 2f;
                    var c = yard.transform.position + new Vector3(Mathf.Sin(a) * dist, 0f, Mathf.Cos(a) * dist);
                    bool deep = true;
                    for (float x = -300f; x <= 300f && deep; x += 50f)
                        for (float z = -300f; z <= 300f && deep; z += 50f)
                            if (th(c.x + x, c.z + z) > -8f) deep = false;
                    if (deep) { spot = c; found = true; }
                }
        if (!found) { Gate("open-water-found", false, "no 600 m disc deeper than 8 m within 8 km"); Finish(null); yield break; }
        sb.AppendLine($"sea trials at {spot:F0}");
        var trials = new List<string>();
        foreach (var (name, cfg, hands) in new[] {
            ("long", ShipConfiguration.Long(), 8), ("two-bays", ShipConfiguration.WithMiddles(2), 8), ("three-bays", ShipConfiguration.WithMiddles(3), 8) })
        {
            if (!anchor.AtHomeDock && !anchor.BerthAtHome(out string bwhy)) sb.AppendLine("berth: " + bwhy);
            yield return new WaitForSeconds(0.5f);
            var cr = Crew();
            for (int i = cr.Count - 1; i >= hands; i--) { landed.Add(cr[i]); cr[i].gameObject.SetActive(false); }
            if (!yard.Current.ValueEquals(cfg)) yield return RefitAndCheck(name + "-for-trial", cfg);
            yield return SeaTrial(name, spot, trials);
            foreach (var h in landed) if (h != null) h.gameObject.SetActive(true);
            landed.Clear();
        }
        // ---- (e) equipment + dry dock, AFTER the sea trials -------------------
        // It runs last so the trials start when they always have and meet the
        // same sea (the ocean runs on time): placed before them, it shifted
        // every trial (A/B 2026-09-25: roll 6-7 deg -> 9 deg, two-bays green
        // water 1.78 > 1.68 m, identical code). -probeSkipEquipment skips it.
        bool skipEquipment = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-probeSkipEquipment") >= 0;
        if (skipEquipment) sb.AppendLine("equipment block SKIPPED (-probeSkipEquipment)");
        if (!skipEquipment) {
        if (!anchor.AtHomeDock && !anchor.BerthAtHome(out string ebw)) sb.AppendLine("equipment berth: " + ebw);
        yield return new WaitForSeconds(1f);
        if (!yard.Current.ValueEquals(ShipConfiguration.Long())) yield return RefitAndCheck("long-for-equipment", ShipConfiguration.Long());
        // (b1) An unpaired gun: take only the middle PORT gun off and keep
        // starboard fitted. `CannonBattery.Fit` used to mirror a single
        // STARBOARD list to port unconditionally, so this case used to draw
        // a wrong live battery (a phantom port gun, or a mis-positioned
        // one); it must now come out honestly asymmetric.
        var midStar = "middle[0]/DeckSlot_1_-1"; var midPort = "middle[0]/DeckSlot_1_1";
        var eRemovePortOnly = yard.RemoveEquipment(yard.Current, midPort);
        Gate("equipment: remove middle port gun only", eRemovePortOnly.ok, eRemovePortOnly.ok ? "removed" : eRemovePortOnly.message);
        var rPortOnly = yard.ApplyRefit(yard.Current, eRemovePortOnly.draft);
        Gate("hull: apply with only the middle port gun off", rPortOnly.ok && yard.Current.equipment.Count == 5,
            rPortOnly.ToString().Replace("\n", " | "));
        yield return new WaitForSeconds(0.3f);
        var batteryPortOnly = yard.GetComponent<CannonBattery>();
        Gate("battery: 5 guns, 2 port, 3 starboard (no mirrored phantom)",
            batteryPortOnly != null && batteryPortOnly.TotalGuns == 5 && batteryPortOnly.PortCount == 2 && batteryPortOnly.StarboardCount == 3,
            batteryPortOnly != null
                ? $"total {batteryPortOnly.TotalGuns}, port {batteryPortOnly.PortCount}, starboard {batteryPortOnly.StarboardCount}"
                : "no CannonBattery");

        var rPortRestored = yard.ApplyRefit(yard.Current, ShipConfiguration.Long());
        Gate("hull: restore the middle port gun from the dock", rPortRestored.ok && yard.Current.ValueEquals(ShipConfiguration.Long())
            && yard.Dock.Count(ShipConfiguration.EquipmentCannon) == 0, rPortRestored.ToString().Replace("\n", " | "));
        yield return new WaitForSeconds(0.3f);

        // (b2) Equipment + dry dock: take the 2 middle guns off, shrink to
        // Short (now valid), check the dock, grow back without them, fit
        // them back from the dock, check the battery.
        var eRemove1 = yard.RemoveEquipment(yard.Current, midStar);
        Gate("equipment: remove middle starboard gun", eRemove1.ok, eRemove1.ok ? "removed" : eRemove1.message);
        var eRemove2 = yard.RemoveEquipment(eRemove1.draft, midPort);
        Gate("equipment: remove middle port gun", eRemove2.ok, eRemove2.ok ? "removed" : eRemove2.message);
        var rNoMidGuns = yard.ApplyRefit(yard.Current, eRemove2.draft);
        Gate("equipment: apply with the 2 middle guns off", rNoMidGuns.ok && yard.Current.equipment.Count == 4,
            rNoMidGuns.ToString().Replace("\n", " | "));
        yield return new WaitForSeconds(0.3f);

        // With her guns off, Short's berths (4) are the only thing left in
        // the way of 8 hands: since 2026-09-25 (Kevin) that is never a
        // refusal any more -- the surplus go ashore to the HOME settlement
        // as real villagers, atomically with the refit and the save.
        var home = Outpost.Home;
        Gate("hands ashore: home settlement present", home != null, home != null ? "found" : "Outpost.Home is null -- cannot test hands ashore");
        var preShortConfig = yard.Current.Clone();
        var shortValidation = yard.Validate(ShipConfiguration.Short());
        int shortBerths = shortValidation.capacityDraft.crewStations;
        var crewBeforeShort = Crew();
        int crewCountBefore = crewBeforeShort.Count;
        var landingNames = new List<string>();
        for (int i = crewBeforeShort.Count - 1; i >= 0 && i >= shortBerths; i--) landingNames.Add(crewBeforeShort[i].DisplayName);
        int expectedSurplus = Mathf.Max(0, crewCountBefore - shortBerths);

        var rToShort = yard.ApplyRefit(yard.Current, ShipConfiguration.Short());
        Gate("hull: Short refits directly, surplus hands go ashore (no refusal)",
            rToShort.ok && yard.Current.ValueEquals(ShipConfiguration.Short()) && Crew().Count == shortBerths,
            rToShort.ToString().Replace("\n", " | ") + $"; crew now {Crew().Count} (berths {shortBerths}, expected surplus {expectedSurplus})");

        bool allLanded = home != null && landingNames.Count == expectedSurplus; string landMissing = "";
        if (home != null)
            foreach (var n in landingNames)
                if (home.HandNamed(n) == null) { allLanded = false; landMissing += n + " "; }
        Gate("hands ashore: surplus became home-settlement villagers", allLanded,
            home == null ? "no home outpost" : $"landed [{string.Join(",", landingNames)}] (expected {expectedSurplus}), missing [{landMissing.Trim()}]");

        // Recall them (Outpost.Recall, the same path a player uses) so the
        // rollback test below has the full crew to land again.
        if (home != null) foreach (var n in landingNames) { var body = home.BodyNamed(n); if (body != null) home.Recall(body, yard.transform); }
        yield return null;
        Gate("hands ashore: recalled back aboard for the rollback test", Crew().Count == crewCountBefore, $"{Crew().Count}/{crewCountBefore}");

        // Rollback: a forced save failure while landing hands must restore
        // BOTH the crew roster and the home settlement -- hands are never
        // lost, and a failed refit must leave nobody stranded ashore.
        ShipyardService.TestFaultStage = ShipyardService.FaultBeforePersist;
        var rFaultShort = yard.ApplyRefit(yard.Current, ShipConfiguration.Short());
        ShipyardService.TestFaultStage = null;
        bool noStrayVillagers = true; string stray = "";
        if (home != null)
            foreach (var n in landingNames)
                if (home.HandNamed(n) != null) { noStrayVillagers = false; stray += n + " "; }
        Gate("hands ashore: forced save failure rolls back hands + villagers",
            !rFaultShort.ok && yard.Current.ValueEquals(preShortConfig) && Crew().Count == crewCountBefore && noStrayVillagers,
            rFaultShort.ToString().Replace("\n", " | ") + $"; crew {Crew().Count}/{crewCountBefore}; stray villagers: {(noStrayVillagers ? "none" : stray.Trim())}");

        var rToShortAgain = yard.ApplyRefit(yard.Current, ShipConfiguration.Short());
        Gate("hull: shrink to Short after the rollback test", rToShortAgain.ok && yard.Current.ValueEquals(ShipConfiguration.Short())
            && Crew().Count == shortBerths, rToShortAgain.ToString().Replace("\n", " | "));
        yield return new WaitForSeconds(0.3f);
        Gate("dry dock: the 2 middle guns are stored", yard.Dock.Count(ShipConfiguration.EquipmentCannon) == 2, yard.Dock.ToJson());

        var toLongNoMid = ShipConfiguration.Long();
        toLongNoMid.equipment.RemoveAll(x => x != null && x.slotId != null && x.slotId.StartsWith("middle["));
        var rGrowBack = yard.ApplyRefit(yard.Current, toLongNoMid);
        Gate("hull: grow back to Long, guns still in dock", rGrowBack.ok && yard.Dock.Count(ShipConfiguration.EquipmentCannon) == 2,
            rGrowBack.ToString().Replace("\n", " | "));
        if (home != null) foreach (var n in landingNames) { var body = home.BodyNamed(n); if (body != null) home.Recall(body, yard.transform); }
        yield return new WaitForSeconds(0.3f);

        var eFit1 = yard.FitEquipment(yard.Current, midStar, ShipConfiguration.EquipmentCannon);
        Gate("equipment: fit middle starboard gun back", eFit1.ok, eFit1.ok ? "fitted" : eFit1.message);
        var eFit2 = yard.FitEquipment(eFit1.draft, midPort, ShipConfiguration.EquipmentCannon);
        Gate("equipment: fit middle port gun back", eFit2.ok, eFit2.ok ? "fitted" : eFit2.message);
        var rAllGunsBack = yard.ApplyRefit(yard.Current, eFit2.draft);
        Gate("equipment: apply with all 6 guns back", rAllGunsBack.ok && yard.Current.ValueEquals(ShipConfiguration.Long())
            && yard.Dock.Count(ShipConfiguration.EquipmentCannon) == 0, rAllGunsBack.ToString().Replace("\n", " | "));
        yield return new WaitForSeconds(0.3f);
        var batteryAfterDock = yard.GetComponent<CannonBattery>();
        int gunsAfterDock = batteryAfterDock != null ? 2 * batteryAfterDock.GunsPerSide : 0;
        Gate("battery: 6 guns positioned at their slots", gunsAfterDock == 6,
            $"GunsPerSide {(batteryAfterDock != null ? batteryAfterDock.GunsPerSide : 0)} -> {gunsAfterDock} guns");
        }
        sb.AppendLine("PROVISIONAL two hydrostatic models, ONE mass (rb.mass = module lightship sum), moored at rest (not gated):");
        sb.AppendLine("  config                   | mass | table draft | sim static draft | delta (sim - table) | sim design draft | sim dynamic (keel below local sea)");
        foreach (var r in draftRows) sb.AppendLine("  " + r);
        sb.AppendLine("sea trials:");
        foreach (var t in trials) sb.AppendLine("  " + t);
        if (!anchor.BerthAtHome(out string endWhy)) sb.AppendLine("end berth: " + endWhy);
        Finish(null);
    }

    // ---- a refit and everything it must keep ---------------------------------

    IEnumerator RefitAndCheck(string name, ShipConfiguration target)
    {
        if (!yard.CanRefitNow(out string why))
        {
            if (!anchor.AtHomeDock) anchor.BerthAtHome(out _);
            yield return new WaitForSeconds(1f);
        }
        float integ = Integrity();
        var holdBefore = HoldPerKind();
        var namesBefore = Names();
        var result = yard.ApplyRefit(yard.Current, target);
        // Same frame: identity, damage, hold, crew.
        bool same = yard.gameObject.GetInstanceID() == shipId && rb.GetInstanceID() == bodyId;
        int ships = FindObjectsByType<ShipMotor>(FindObjectsSortMode.None).Length;
        int yards = FindObjectsByType<ShipyardService>(FindObjectsSortMode.None).Length;
        int ears = FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length;
        Gate(name + ": applied", result.ok && yard.Current.ValueEquals(target), result.ToString().Replace("\n", " | "));
        Gate(name + ": one ship, same object", same && ships == 1 && yards == 1 && ears == listeners,
            $"ship {same}, ShipMotors {ships}, shipyards {yards}, listeners {ears}/{listeners}");
        Gate(name + ": damage/hold/crew kept", Integrity() == integ && SameHold(holdBefore, HoldPerKind()) && Names() == namesBefore,
            $"integrity {integ:F4}->{Integrity():F4}; hold {Fmt(HoldPerKind())}; crew [{Names()}]");
        yield return new WaitForSeconds(0.6f);
        var d = yard.ActiveData;
        var plan = yard.Validate(yard.Current);
        var pivot = yard.RotorPivot;
        Vector3 axle = pivot != null ? yard.transform.InverseTransformPoint(pivot.position) : new Vector3(float.NaN, 0, 0);
        var paddle = yard.GetComponent<PaddleDrive>();
        var steamer = yard.GetComponent<SteamerShip>();
        bool lengths = Mathf.Approximately(motor.HullLength, d.lwl) && steamer != null && steamer.Data == d
            && Mathf.Approximately(rb.mass, d.massKg) && paddle != null && paddle.Configured;
        var battery = yard.GetComponent<CannonBattery>();
        int gunsFitted = battery != null ? 2 * battery.GunsPerSide : 0;
        float lightKg = plan.draftPlan != null ? plan.draftPlan.lightshipKg : float.NaN;
        Gate(name + ": hull form everywhere", lengths && voyage.HoldCapacity == plan.capacityDraft.holdCells
            && Mathf.Approximately(rb.mass, lightKg) && gunsFitted == plan.capacityDraft.guns,
            $"lwl {d.lwl:F3} HullLength {motor.HullLength:F3} mass {rb.mass:F0}/{d.massKg:F0} (module lightship {lightKg:F0}) "
            + $"guns fitted {gunsFitted} hold cap {voyage.HoldCapacity} (plan {plan.capacityDraft})");
        Gate(name + ": drawn axle on physics axle", pivot != null && Mathf.Abs(axle.z - d.wheelAxle.z) < 0.01f && Mathf.Abs(axle.x) < 0.01f,
            $"drawn {axle.x:F3},{axle.y:F3},{axle.z:F3} physics {d.wheelAxle.x:F3},{d.wheelAxle.y:F3},{d.wheelAxle.z:F3} (y differs by design: {axle.y - d.wheelAxle.y:F3} m)");
        int onDeck = 0, standing = 0; string worst = "";
        foreach (var c in Crew())
        {
            if (!c.Available) continue;
            standing++;
            var p = c.transform.localPosition;
            int si = ShipyardPlanner.NearestStation(d, p.z);
            float deckY = d.stations[si].deckY, half = d.HalfBreadthAt(si, deckY);
            bool ok = Mathf.Abs(p.y - deckY) < 0.1f && Mathf.Abs(p.x) <= half && ShipyardPlanner.OnDeck(d, new Vector3(0f, 0f, p.z), 0f);
            if (ok) onDeck++; else worst = $"{c.DisplayName} at {p.x:F2},{p.y:F2},{p.z:F2} (deck {deckY:F2}, half {half:F2})";
        }
        Gate(name + ": hands on the new deck", onDeck == standing, $"{onDeck}/{standing} at station on deck {worst}");
        sb.AppendLine($"  {name}: {plan.capacityDraft}; lwl {d.lwl:F2} m, {d.massKg / 1000f:F1} t, axle z {d.wheelAxle.z:F3}, helm z {d.helm.z:F3}");
        yield return MeasureDraft(name, plan.draftPlan);
    }

    /// PROVISIONAL, printed not gated: at rest and moored, the keel's depth
    /// below the local sea surface (the ocean sampler the hull uses), next to
    /// the two static predictions for the mass she actually has -- ONE mass,
    /// the rigidbody's, which is the module lightship sum on a refitted ship
    /// (and today's hull-form mass on the untouched one).
    IEnumerator MeasureDraft(string name, ShipyardPlan plan)
    {
        var d = yard.ActiveData;
        motor.ThrottleOrder = 0f;
        float keelY = float.MaxValue;
        foreach (var st in d.stations) keelY = Mathf.Min(keelY, st.keelY);
        float sum = 0f; int n = 0;
        for (float t = 0f; t < 2.5f; t += Time.fixedDeltaTime)
        {
            yield return new WaitForFixedUpdate();
            if (t < 1f || !OceanSampler.Ready) continue;
            Vector3 keel = yard.transform.TransformPoint(new Vector3(0f, keelY, 0f));
            sum += OceanSampler.SampleImmediate(keel).height - keel.y; n++;
        }
        float dyn = n > 0 ? sum / n : float.NaN;
        string table = plan != null && ShipyardPlanner.TableDraft(plan, rb.mass, out float td) ? td.ToString("F3") : "--";
        string stat = ShipyardPlanner.SimStaticDraft(d, rb.mass, out float sd) ? sd.ToString("F3") : "--";
        float td2 = float.NaN, sd2 = float.NaN;
        bool both = plan != null && ShipyardPlanner.TableDraft(plan, rb.mass, out td2) & ShipyardPlanner.SimStaticDraft(d, rb.mass, out sd2);
        string delta = both ? $"{sd2 - td2:+0.000;-0.000} m ({(sd2 - td2) / td2 * 100f:+0.0;-0.0} %)" : "--";
        draftRows.Add($"{name,-24} | {rb.mass / 1000f:F2} t | table {table} | sim static {stat} | delta {delta} | sim design {d.draft:F3} | sim dynamic {dyn:F3} m");
    }

    void Refuse(string name, ShipConfiguration draft, string code, ShipConfiguration expected = null)
    {
        string cfg = yard.Current.ToJson();
        var hold = HoldPerKind();
        float integ = Integrity();
        Vector3 p = yard.transform.position; Quaternion q = yard.transform.rotation;
        float mass = rb.mass; var data = yard.GetComponent<SteamerShip>().Data; int cap = voyage.HoldCapacity;
        var res = yard.ApplyRefit(expected ?? yard.Current, draft);
        bool unchanged = yard.Current.ToJson() == cfg && SameHold(hold, HoldPerKind()) && Integrity() == integ
            && yard.transform.position == p && yard.transform.rotation == q && rb.mass == mass
            && yard.GetComponent<SteamerShip>().Data == data && voyage.HoldCapacity == cap;
        bool coded = !res.ok && res.issues.Exists(i => i.code == code);
        Gate("refused " + name, coded && unchanged, (unchanged ? "unchanged; " : "CHANGED; ") + res.ToString().Replace("\n", " | "));
    }

    // ---- sea trial ---------------------------------------------------------------

    IEnumerator SeaTrial(string name, Vector3 spot, List<string> rows)
    {
        anchor.CastOff();
        yield return null;
        motor.Anchored = false;
        motor.MooringHeading = null;
        motor.AutopilotTarget = null;
        if (helm != null) helm.enabled = false;
        motor.Rudder = 0f; motor.ThrottleOrder = 0f;
        float h = OceanSampler.Ready ? OceanSampler.SampleImmediate(spot).height : 0f;
        SaveGame.Warp(motor, new Vector3(spot.x, h, spot.z), 0f);
        yield return new WaitForSeconds(1.5f);
        var d = yard.ActiveData;
        float vmax = 0f, rollMax = 0f, pitchMax = 0f, draftMin = float.MaxValue, draftMax = float.MinValue, yawSum = 0f;
        int yawN = 0; bool nan = false;
        motor.ThrottleOrder = 1f;
        for (float t = 0f; t < 30f; t += Time.fixedDeltaTime)
        {
            if (t >= 20f) motor.Rudder = 1f;
            yield return new WaitForFixedUpdate();
            var pos = rb.position; var v = rb.linearVelocity;
            if (float.IsNaN(pos.x + pos.y + pos.z + v.x + v.y + v.z)) { nan = true; break; }
            float sp = new Vector3(v.x, 0f, v.z).magnitude;
            if (t < 20f) vmax = Mathf.Max(vmax, sp);
            var e = yard.transform.eulerAngles;
            rollMax = Mathf.Max(rollMax, Mathf.Abs(Mathf.DeltaAngle(0f, e.z)));
            pitchMax = Mathf.Max(pitchMax, Mathf.Abs(Mathf.DeltaAngle(0f, e.x)));
            float sea = OceanSampler.Ready ? OceanSampler.SampleImmediate(pos).height : 0f;
            float draft = sea - pos.y + d.draft; // design draft + sinkage of the origin
            draftMin = Mathf.Min(draftMin, draft); draftMax = Mathf.Max(draftMax, draft);
            if (t >= 22f) { yawSum += Mathf.Abs(rb.angularVelocity.y) * Mathf.Rad2Deg; yawN++; }
        }
        motor.ThrottleOrder = 0f; motor.Rudder = 0f;
        float yaw = yawN > 0 ? yawSum / yawN : 0f;
        bool afloat = draftMax < d.depth && draftMin > -0.5f;
        Gate($"trial {name}: sane", !nan && afloat && vmax > 1f && rollMax < 25f,
            $"top {vmax:F2} m/s, |roll| {rollMax:F1} deg, |pitch| {pitchMax:F1} deg, draft {draftMin:F2}..{draftMax:F2} m (design {d.draft:F2}, depth {d.depth:F2}), yaw rate {yaw:F1} deg/s");
        rows.Add($"{name,-10} L {d.lwl:F2} m  {d.massKg / 1000f:F1} t  top {vmax:F2} m/s  roll {rollMax:F1}  pitch {pitchMax:F1}  yaw {yaw:F1} deg/s  MaxSpeed {motor.MaxSpeed:F2}");
        if (helm != null) helm.enabled = true;
    }

    // ---- helpers ---------------------------------------------------------------

    static ShipConfiguration Mod(ShipConfiguration c, System.Action<ShipConfiguration> f) { var x = c.Clone(); f(x); return x; }
    static KeyValuePair<string, int> Pair(string r, int n) => new KeyValuePair<string, int>(r, n);
    IEnumerable<KeyValuePair<string, int>> Banked()
    {
        var l = new List<KeyValuePair<string, int>>();
        foreach (var kv in voyage.BankedStores) l.Add(kv);
        return l;
    }
    float Integrity() => hull != null ? hull.Integrity01 : 1f;

    List<CrewAgent> Crew()
    {
        var l = new List<CrewAgent>();
        foreach (var c in yard.GetComponentsInChildren<CrewAgent>(false)) l.Add(c);
        return l;
    }

    string Names()
    {
        var n = new List<string>();
        foreach (var c in Crew()) n.Add(c.DisplayName);
        n.Sort();
        return string.Join(",", n);
    }

    Dictionary<string, int> HoldPerKind()
    {
        var d = new Dictionary<string, int>();
        foreach (var kv in voyage.HeldStores) if (kv.Value > 0) d[kv.Key] = kv.Value;
        return d;
    }

    static bool SameHold(Dictionary<string, int> a, Dictionary<string, int> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var kv in a) if (!b.TryGetValue(kv.Key, out int n) || n != kv.Value) return false;
        return true;
    }

    static string Fmt(Dictionary<string, int> d)
    {
        var s = new List<string>();
        foreach (var kv in d) s.Add(kv.Key + " " + kv.Value);
        return string.Join(", ", s);
    }

    void Gate(string name, bool ok, string detail)
    {
        if (ok) passes++; else fails++;
        sb.Append(ok ? "  PASS " : "  FAIL ").Append(name).Append(" -- ").AppendLine(detail);
    }

    void Finish(string stopped)
    {
        foreach (var h in landed) if (h != null) h.gameObject.SetActive(true);
        landed.Clear();
        ShipyardService.PersistPathOverride = null;
        if (helm != null) helm.enabled = true;
        if (stopped != null) { fails++; sb.AppendLine("  STOPPED -- " + stopped); }
        sb.AppendLine($"ShipyardRefitProbe: {passes} PASS, {fails} FAIL");
        string text = "[ShipyardRefitProbe]\n" + sb;
        if (fails == 0) Debug.Log(text); else Debug.LogError(text);
        var path = System.IO.Path.Combine(Application.dataPath, "../Logs/ShipyardRefitProbe.txt");
        try { System.IO.File.WriteAllText(path, text); } catch { }
        Destroy(gameObject);
    }
}
