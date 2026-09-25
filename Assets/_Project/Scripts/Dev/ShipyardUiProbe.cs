using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;
using SeaSick.Crew;
using SeaSick.Save;
using SeaSick.Ship;
using SeaSick.Ship.Modular;
using SeaSick.Steamer;
using SeaSick.UI.ModularYard;
using SeaSick.Voyage;
using SeaSick.World;
using SheetsApi = SeaSick.UI.Sheets.Sheets;

/// **Does the shipyard UI refit her safely, and does the modal own input?**
/// (modular shipyard, 2026-09-24; docs/SHIPYARD-UI-INTEGRATION.md,
/// docs/SHIPYARD-API.md)
///
/// On the live player steamer in Sea.unity, with a known hold (timber 5 +
/// stone 2), damage and all hands aboard:
///
/// 1. live refits through Astra's `ShipyardDraft` built exactly as
///    `ShipyardModal.Open` builds it (library, `bridge.ReadCurrent()`, the
///    `ShipyardLiveBridge` as backend, `bridge.RemovalBlocker`,
///    `bridge.Allowed`): Long -> 2 bays -> timber wheel -> 3 bays -> Long ->
///    reinforced wheel. After each confirm: live = draft, hold/crew/damage
///    kept, hands on the deck, one player ship, HullLength and mass = plan,
///    drawn modules = configuration. Plus RemoveMiddle refused by the
///    draft when the report says the section cannot go (a hold that would
///    not fit the shorter ship).
/// 2. stale baseline: another refit lands between ReadCurrent and TryApply
///    -> STALE_DRAFT, ship unchanged (bridge and draft paths).
/// 3. input blocking: the real modal (`ShipyardLiveBridge.Open`, what the
///    ship sheet's Shipyard button calls), virtual Keyboard + Mouse through
///    the Input System; helm/sheets/camera zoom/combat lock/spacebar/
///    broadside ignore it while blocked; the helm answers W again after the
///    modal closes (the control).
/// 4. rollback on save failure (persist path under an existing FILE).
/// 5. rollback on build failure at the three `ShipyardService.TestFaultStage`
///    points, from a refitted ship and from the untouched standard steamer.
/// 6. save/load round trip through the UI path (2 bays -> save -> Long ->
///    load -> 2 bays, same hold and crew).
///
/// Never touches the player's save: `PersistPathOverride` points at a temp
/// file for the whole run (asserted) and the real save's timestamp is
/// checked at the end. Play mode, Sea.unity, steamer selected; ~1 min.
/// Writes Logs/ShipyardUiProbe.txt. Launch: RunProbe.ShipyardUi() or
/// ShipyardProbeBatch (-shipyardProbe ui|both).
///
/// Never uses WaitForEndOfFrame: it never resumes in batch mode.
public class ShipyardUiProbe : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("ShipyardUiProbe: not in play mode"); return; }
        var old = FindAnyObjectByType<ShipyardUiProbe>();
        if (old != null) Destroy(old.gameObject);
        var runner = new GameObject("ShipyardUiProbeRunner").AddComponent<ShipyardUiProbe>();
        runner.StartCoroutine(runner.Run());
    }

    readonly StringBuilder sb = new StringBuilder();
    int fails, passes, untested;
    bool finished;

    ShipyardService yard;
    ShipMotor motor;
    Rigidbody rb;
    AnchorController anchor;
    VoyageManager voyage;
    HullIntegrity hull;
    int shipId, bodyId, listeners;

    string tmp, persistOk;
    string realSavePath; bool realSaveExisted; DateTime realSaveTime; long realSaveLen;
    readonly List<string> overrideNullAt = new List<string>();
    int overrideChecks;
    readonly List<string> tempFiles = new List<string>();

    // Input System state to put back.
    Keyboard kb; Mouse mouse;
    bool inputSettingsChanged;
    InputSettings.BackgroundBehavior prevBackground;
    InputSettings.EditorInputBehaviorInPlayMode prevEditorBehaviour;
    bool prevRunInBackground;

    IEnumerator Run()
    {
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
        if (motor == null || rb == null || anchor == null || voyage == null) { Finish("ship parts missing"); yield break; }
        t0 = Time.realtimeSinceStartup;
        while (!anchor.StartedDocked && Time.realtimeSinceStartup - t0 < 10f) yield return null;

        // Never the player's save, from the first line that could write.
        tmp = Application.temporaryCachePath;
        persistOk = Path.Combine(tmp, "ShipyardUiProbe-persist.json");
        tempFiles.Add(persistOk);
        ShipyardService.PersistPathOverride = persistOk;
        realSavePath = SaveGame.Path;
        realSaveExisted = File.Exists(realSavePath);
        if (realSaveExisted) { realSaveTime = File.GetLastWriteTimeUtc(realSavePath); realSaveLen = new FileInfo(realSavePath).Length; }
        yield return new WaitForSeconds(1f);

        shipId = yard.gameObject.GetInstanceID();
        bodyId = rb.GetInstanceID();
        listeners = FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length;

        // A deterministic start: the untouched standard steamer, as in a
        // fresh session (a previous probe in the same play session may have
        // left her refitted). ApplyFromSave("") is the load path for an
        // old-format save -- the real revert.
        if (yard.ModularActive)
        {
            sb.AppendLine($"start: she was refitted ({yard.Current.middleIds.Count} bays), reverted to the standard steamer through ApplyFromSave(\"\")");
            yard.ApplyFromSave("");
            yield return new WaitForSeconds(0.6f);
        }
        string why0;
        if (!anchor.AtHomeDock && !anchor.BerthAtHome(out why0)) sb.AppendLine("could not berth at the start: " + why0);
        yield return new WaitForSeconds(0.5f);
        if (hull != null) hull.Batter(yard.transform.position, 0.15f);
        SetHold(5, 2);
        yield return null;
        int hands = Crew().Count;
        Gate("setup: standard steamer, 8 hands aboard, hold timber 5 + stone 2, damaged",
            !yard.ModularActive && yard.Current.ValueEquals(ShipConfiguration.Long()) && hands == 8
            && HoldPerKind().Count == 2 && Integrity() < 1f,
            $"modular {yard.ModularActive}, {hands} hands [{Names()}], hold {Fmt(HoldPerKind())} ({voyage.TotalHeld}/{voyage.HoldCapacity}), "
            + $"integrity {Integrity():F3}, can refit: {yard.CanRefitNow(out string r0)} {r0}");

        yield return Safe("1 live refits", LiveRefits());
        yield return Safe("2 stale baseline", StaleBaseline());
        yield return Safe("3 input blocking", InputBlocking());
        CleanupInput();
        yield return Safe("4 save failure", SaveFailure());
        yield return Safe("5 build faults", BuildFaults());
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        ShipyardService.TestFaultStage = null;
#endif
        yield return Safe("6 save/load", SaveLoad());
        yield return Safe("7 deck toggle", DeckToggle());
        yield return Safe("8 per-section toggle", SectionToggle());

        Gate("persist override never null at any apply", overrideNullAt.Count == 0,
            $"{overrideChecks} applies checked" + (overrideNullAt.Count > 0 ? "; NULL at " + string.Join(", ", overrideNullAt) : ""));
        Gate("the player's real save untouched", RealSaveSame(), RealSaveLine());
        Finish(null);
    }

    // ---- 1: live refits through the draft --------------------------------------

    IEnumerator LiveRefits()
    {
        sb.AppendLine("1. live refits through ShipyardDraft + ShipyardLiveBridge:");
        var lng = ShipConfiguration.Long();

        var d = NewDraft(out _);
        Gate("1a draft: AddMiddle (Long -> 2 bays)", d.AddMiddle() && d.Count == 2, Msg(d));
        yield return ConfirmStep("1a 2 bays", d, ShipConfiguration.WithMiddles(2));

        d = NewDraft(out _);
        Gate("1b draft: ChooseWheel(timber)", d.ChooseWheel(ShipConfiguration.TimberRotor) && d.Rotor == ShipConfiguration.TimberRotor, Msg(d));
        yield return ConfirmStep("1b 2 bays timber", d, Mod(ShipConfiguration.WithMiddles(2), c => c.rotorId = ShipConfiguration.TimberRotor));

        d = NewDraft(out _);
        Gate("1c draft: AddMiddle (2 -> 3 bays)", d.AddMiddle() && d.Count == 3, Msg(d));
        yield return ConfirmStep("1c 3 bays timber", d, Mod(ShipConfiguration.WithMiddles(3), c => c.rotorId = ShipConfiguration.TimberRotor));

        // RemoveMiddle refused while the report says the section cannot go:
        // a hold one load over what the shorter ship takes.
        var bd = NewDraft(out var bridge);
        var without = bd.Snapshot();
        without.middleIds.RemoveAt(without.middleIds.Count - 1);
        int capShort = yard.Validate(without).capacityDraft.holdCells;
        int load = capShort + 1;
        if (load < 3 || load > 400)
            Untested("1d RemoveMiddle refused by the report", $"not reachable in prototype: shorter hull takes {capShort} loads");
        else
        {
            SetHold(load - 2, 2);
            yield return null;
            string blocker = bridge.RemovalBlocker(bd.Snapshot(), bd.Count - 1);
            string reason = bd.RemovalReason();
            bool removed = bd.RemoveMiddle();
            Gate("1d RemoveMiddle refused while the report says the bay cannot go", !removed && bd.Count == 3
                && !string.IsNullOrEmpty(blocker) && reason == blocker && bd.Message == blocker,
                $"hold {voyage.TotalHeld} vs {capShort} in {without.middleIds.Count} bays; blocker \"{blocker}\"; draft message \"{bd.Message}\"; removed {removed}");
            SetHold(5, 2);
            yield return null;
        }

        d = NewDraft(out _);
        bool r1 = d.RemoveMiddle(), r2 = d.RemoveMiddle();
        Gate("1e draft: RemoveMiddle x2 (3 -> 1 bay)", r1 && r2 && d.Count == 1, Msg(d));
        yield return ConfirmStep("1e Long timber", d, Mod(lng, c => c.rotorId = ShipConfiguration.TimberRotor));

        d = NewDraft(out _);
        Gate("1f draft: ChooseWheel(reinforced)", d.ChooseWheel(ShipConfiguration.ReinforcedRotor), Msg(d));
        yield return ConfirmStep("1f Long reinforced", d, lng);
    }

    /// Exactly what ShipyardModal.Open does with a ShipyardLiveBridge.
    ShipyardDraft NewDraft(out ShipyardLiveBridge bridge)
    {
        bridge = new ShipyardLiveBridge();
        var library = ModuleLibrary.LoadFromResources();
        return new ShipyardDraft(library, bridge.ReadCurrent(), bridge, bridge.RemovalBlocker, bridge.Allowed);
    }

    IEnumerator ConfirmStep(string name, ShipyardDraft d, ShipConfiguration expect)
    {
        yield return EnsureRefittable();
        var before = Capture();
        CheckOverride(name);
        bool ok = d.Confirm();
        // Same frame: the refit is synchronous.
        var now = Capture();
        Gate(name + ": confirmed", ok && d.Committed, $"Confirm {ok}, message \"{d.Message}\"");
        var live = new ShipyardLiveBridge().ReadCurrent();
        Gate(name + ": live = draft", yard.Current.ValueEquals(d.Snapshot()) && live.ValueEquals(expect) && yard.ModularActive,
            $"live {Describe(yard.Current)}, draft {Describe(d.Snapshot())}, expected {Describe(expect)}");
        Gate(name + ": hold, crew, damage kept", SameHold(before.hold, now.hold) && before.names == now.names
            && before.crew == now.crew && before.integ == now.integ,
            $"hold {Fmt(now.hold)} (was {Fmt(before.hold)}); crew {now.crew} (was {before.crew}); integrity {before.integ:F4}->{now.integ:F4}");
        Gate(name + ": exactly one player ship", OneShip(out string one), one);
        yield return new WaitForSeconds(0.6f);
        CheckForm(name);
    }

    /// After a refit has settled: hull form and mass from the service's plan,
    /// hands on the deck, the drawing matches the configuration.
    void CheckForm(string name)
    {
        var v = yard.Validate(yard.Current);
        var p = v.draftPlan ?? v.plan;
        var data = yard.ActiveData;
        bool form = p != null && p.data != null && Mathf.Approximately(motor.HullLength, p.data.lwl)
            && Mathf.Approximately(rb.mass, p.lightshipKg) && Mathf.Approximately(motor.HullLength, data.lwl)
            && Mathf.Approximately(rb.mass, data.massKg);
        Gate(name + ": HullLength and mass = the service's plan", form,
            $"HullLength {motor.HullLength:F3} (plan {(p?.data != null ? p.data.lwl.ToString("F3") : "--")}, active {data.lwl:F3}); "
            + $"mass {rb.mass:F0} kg (plan lightship {(p != null ? p.lightshipKg.ToString("F0") : "--")}, active {data.massKg:F0})");
        Gate(name + ": every hand on the deck", HandsOnDeck(out string deck), deck);
        Gate(name + ": drawing = configuration", VisualMatchesConfig(out string vis), vis);
    }

    // ---- 2: stale baseline ----------------------------------------------------------

    IEnumerator StaleBaseline()
    {
        sb.AppendLine("2. stale baseline:");
        yield return EnsureRefittable();
        var d = NewDraft(out var bridge);
        d.AddMiddle();                                    // the player's pending plan
        var expected = bridge.ReadCurrent();              // what the modal captured
        var other = Mod(yard.Current, c => c.rotorId = c.rotorId == ShipConfiguration.TimberRotor
            ? ShipConfiguration.ReinforcedRotor : ShipConfiguration.TimberRotor);
        CheckOverride("2 setup");
        var r = yard.ApplyRefit(yard.Current, other);     // someone else's refit lands first
        Gate("2 setup: a different refit applied through the service", r.ok, r.ToString().Replace("\n", " | "));
        yield return new WaitForSeconds(0.3f);

        var s0 = Capture();
        CheckOverride("2 stale TryApply");
        bool ok = bridge.TryApply(expected, d.Snapshot(), out string reason);
        var s1 = Capture();
        Gate("2 bridge.TryApply(stale expected) refused with STALE_DRAFT", !ok && reason != null && reason.Contains(ShipyardCodes.StaleDraft), reason);
        Gate("2 live ship unchanged after the stale apply", Diff(s0, s1, out string diff), diff);

        CheckOverride("2 stale Confirm");
        bool c2 = d.Confirm();
        var s2 = Capture();
        Gate("2 draft.Confirm on the stale baseline refused, reason kept for the modal", !c2 && !d.Committed
            && d.Message.Contains(ShipyardCodes.StaleDraft), $"Confirm {c2}, message \"{d.Message}\"");
        Gate("2 live ship unchanged after the stale confirm", Diff(s0, s2, out string diff2), diff2);
    }

    // ---- 3: input blocking -------------------------------------------------------------

    IEnumerator InputBlocking()
    {
        sb.AppendLine("3. input blocking (virtual Keyboard + Mouse through the Input System):");
        yield return EnsureRefittable();
        SheetsApi.Close();
        var helm = yard.GetComponent<HelmInput>();
        var chase = FindFirstObjectByType<SeaSick.CameraRig.ChaseCamera>();
        var combat = yard.GetComponent<SeaSick.Combat.CombatLock>();
        var battery = yard.GetComponent<CannonBattery>();
        sb.AppendLine($"  context: batch {Application.isBatchMode}, focused {Application.isFocused}, screen {Screen.width}x{Screen.height}, "
            + $"helm {(helm != null ? helm.enabled.ToString() : "missing")}, IslandCam.Engaged {SeaSick.CameraRig.IslandCam.Engaged}, "
            + $"voyage.AtHome {voyage.AtHome}, anchor {anchor.CurrentState}/home dock {anchor.AtHomeDock}");

        // Keyboard and pointer events go to the game even without game-view
        // focus (batch mode has none). Put back in CleanupInput.
        var st = InputSystem.settings;
        prevBackground = st.backgroundBehavior;
        prevEditorBehaviour = st.editorInputBehaviorInPlayMode;
        prevRunInBackground = Application.runInBackground;
        inputSettingsChanged = true;
        st.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        st.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        Application.runInBackground = true;

        string addError = null;
        try
        {
            kb = InputSystem.AddDevice<Keyboard>("ProbeKeyboard");
            mouse = InputSystem.AddDevice<Mouse>("ProbeMouse");
        }
        catch (Exception e) { addError = e.GetType().Name + ": " + e.Message; }
        if (kb == null || mouse == null)
        {
            Gate("3 virtual devices added", false, addError ?? "AddDevice returned null");
            yield break;
        }
        kb.MakeCurrent(); mouse.MakeCurrent();
        yield return null;
        sb.AppendLine($"  devices: keyboard enabled {kb.enabled} current {Keyboard.current == kb}, mouse enabled {mouse.enabled} current {Mouse.current == mouse}");

        var sheet = SheetsApi.TryCreateFor(motor);
        int cameras0 = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
        int previews0 = CountPreviews();

        // ---- open the real modal, the way the ship sheet's button does
        string openError = null;
        try { ShipyardLiveBridge.Open(); }
        catch (Exception e) { openError = e.GetType().Name + ": " + e.Message; }
        yield return null;
        bool modal = ShipyardModal.IsOpen;
        Gate("3 modal opened by ShipyardLiveBridge.Open; WorldInputBlocked and ShipyardModal.IsOpen", openError == null && modal
            && ShipyardSession.WorldInputBlocked && FindAnyObjectByType<ShipyardModal>() != null,
            $"IsOpen {modal}, blocked {ShipyardSession.WorldInputBlocked}" + (openError != null ? ", threw " + openError : ""));
        string mode = "";
        if (!modal)
        {
            // Degraded: still test the gameplay gates on the session flag.
            ShipyardSession.SetWorldInputBlocked(true);
            mode = " (session flag only: the modal did not open)";
        }

        // ---- an idle window first: how much the helm's own autopilot moves
        // the rudder with no input at all (a held course keeps steering).
        float thrIdle0 = motor.ThrottleOrder, rudIdle0 = motor.Rudder, thrIdle = 0f, rudIdle = 0f;
        for (float end = Time.realtimeSinceStartup + 0.5f; Time.realtimeSinceStartup < end;)
        {
            yield return null;
            thrIdle = Mathf.Max(thrIdle, Mathf.Abs(motor.ThrottleOrder - thrIdle0));
            rudIdle = Mathf.Max(rudIdle, motor.Rudder - rudIdle0);
        }

        // ---- baselines, then W + D held ~1 s, a click at the centre, a wheel notch
        float thr0 = motor.ThrottleOrder, rud0 = motor.Rudder;
        float zoom0 = chase != null ? chase.ZoomScale : float.NaN;
        var state0 = anchor.CurrentState; bool dock0 = anchor.AtHomeDock;
        int port0 = battery != null ? battery.PortReady : -1, stbd0 = battery != null ? battery.StarboardReady : -1;
        var centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        bool sawKeys = false, sawClick = false, sawScroll = false, sheetOpened = false;
        float thrDev = 0f, rudUp = 0f, zoomDev = 0f;
        int frame = 0;
        for (float end = Time.realtimeSinceStartup + 1f; Time.realtimeSinceStartup < end; frame++)
        {
            kb.MakeCurrent(); mouse.MakeCurrent();
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.W, Key.D));
            var ms = new MouseState { position = centre };
            if (frame < 8) ms = ms.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left);
            if (frame == 12) ms.scroll = new Vector2(0f, 120f);
            InputSystem.QueueStateEvent(mouse, ms);
            yield return null;
            sawKeys |= kb.wKey.isPressed && kb.dKey.isPressed;
            sawClick |= mouse.leftButton.isPressed;
            sawScroll |= Mathf.Abs(mouse.scroll.ReadValue().y) > 0.01f;
            sheetOpened |= SheetsApi.IsOpen;
            thrDev = Mathf.Max(thrDev, Mathf.Abs(motor.ThrottleOrder - thr0));
            rudUp = Mathf.Max(rudUp, motor.Rudder - rud0);
            if (chase != null) zoomDev = Mathf.Max(zoomDev, Mathf.Abs(chase.ZoomScale - zoom0));
        }
        // Space, Q and E pressed for one frame, then everything released.
        InputSystem.QueueStateEvent(kb, new KeyboardState(Key.Space, Key.Q, Key.E));
        InputSystem.QueueStateEvent(mouse, new MouseState { position = centre });
        yield return null;
        bool sawSpace = kb.spaceKey.isPressed;
        InputSystem.QueueStateEvent(kb, new KeyboardState());
        yield return null;
        yield return null;

        Gate("3 the virtual keyboard reached the Input System (W+D, then Space)", sawKeys && sawSpace,
            $"W+D seen {sawKeys}, Space seen {sawSpace}, frames {frame}");
        Gate("3 the virtual mouse reached the Input System (click, wheel)", sawClick && sawScroll, $"click {sawClick}, wheel {sawScroll}");
        if (!sawKeys)
            Untested("3 blocked: helm ignores W/D", "queued keyboard events never reached the device; nothing to block");
        else
            Gate("3 blocked: helm ignores W/D" + mode, thrDev < 1e-4f && rudUp <= rudIdle + 0.05f,
                $"ThrottleOrder {thr0:F3} max dev {thrDev:F4}; Rudder {rud0:F3} max rise {rudUp:F3} "
                + $"(idle window with no input: throttle dev {thrIdle:F4}, rudder rise {rudIdle:F3})");
        if (!sawClick)
            Untested("3 blocked: a click opens no sheet", "the click never reached the device");
        else
            Gate("3 blocked: a click at the centre opens no sheet" + mode, !sheetOpened, $"Sheets.IsOpen during input {sheetOpened}");
        bool refused = false;
        if (sheet == null) Untested("3 blocked: Sheets.Open refuses", "no ship sheet registered (Sheets.TryCreateFor(ShipMotor) = null)");
        else
        {
            SheetsApi.Open(sheet);
            refused = !SheetsApi.IsOpen;
            Gate("3 blocked: Sheets.Open(ship sheet) refuses" + mode, refused, $"IsOpen after Open {SheetsApi.IsOpen}");
            if (!refused) SheetsApi.Close();
        }
        if (chase == null || !sawScroll) Untested("3 blocked: camera zoom", chase == null ? "no ChaseCamera" : "the wheel never reached the device");
        else Gate("3 blocked: chase camera zoom unchanged by the wheel" + mode, zoomDev < 1e-3f, $"ZoomScale {zoom0:F4} max dev {zoomDev:F5}");
        Gate("3 blocked: combat lock not engaged by Space" + mode, combat == null || combat.Locked == null,
            combat == null ? "no CombatLock" : $"Locked {combat.Locked}");
        Gate("3 blocked: anchor spacebar command ignored" + mode, anchor.CurrentState == state0 && anchor.AtHomeDock == dock0,
            $"state {state0}->{anchor.CurrentState}, home dock {dock0}->{anchor.AtHomeDock}"
            + (voyage.AtHome ? " (NOTE voyage.AtHome also swallows Space here, so this gate is weak at the home berth)" : ""));
        if (battery == null || port0 + stbd0 <= 0)
            Untested("3 blocked: broadside keys", battery == null ? "no CannonBattery" : "no gun loaded, so a broadside would show nothing");
        else
            Gate("3 blocked: Q/E fire no broadside" + mode, battery.PortReady >= port0 && battery.StarboardReady >= stbd0,
                $"ready port {port0}->{battery.PortReady}, starboard {stbd0}->{battery.StarboardReady}");
        Gate("3 still blocked after the input", ShipyardSession.WorldInputBlocked && (ShipyardModal.IsOpen || !modal),
            $"blocked {ShipyardSession.WorldInputBlocked}, open {ShipyardModal.IsOpen}");

        // ---- close it the way the screen's Cancel does
        string how = "session flag cleared (degraded mode)";
        if (modal)
        {
            var closer = new string[1];
            yield return CloseModal(closer);
            how = closer[0];
        }
        else ShipyardSession.SetWorldInputBlocked(false);
        yield return null;
        yield return null;
        Gate("3 closed: WorldInputBlocked false, no modal left", !ShipyardSession.WorldInputBlocked && !ShipyardModal.IsOpen
            && FindAnyObjectByType<ShipyardModal>() == null, $"via {how}; blocked {ShipyardSession.WorldInputBlocked}, IsOpen {ShipyardModal.IsOpen}");
        int cameras1 = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
        Gate("3 closed: no preview ship or camera left behind", CountPreviews() == previews0 && cameras1 == cameras0,
            $"previews {previews0}->{CountPreviews()}, cameras {cameras0}->{cameras1}");

        // ---- the control: the same keyboard now drives the helm
        thr0 = motor.ThrottleOrder;
        float thrMax = thr0;
        bool helmOn = helm != null && helm.enabled;
        for (float end = Time.realtimeSinceStartup + 1f; Time.realtimeSinceStartup < end;)
        {
            kb.MakeCurrent();
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.W));
            yield return null;
            thrMax = Mathf.Max(thrMax, motor.ThrottleOrder);
        }
        InputSystem.QueueStateEvent(kb, new KeyboardState());
        yield return null;
        if (!sawKeys) Untested("3 unblocked: W drives the helm (control)", "the keyboard never reached the Input System");
        // At the home berth the island camera takes the helm by design, so W
        // cannot prove anything there; the Sheets.Open control below still
        // proves the gate lifted (2026-09-24, run #4).
        else if (SeaSick.CameraRig.IslandCam.Engaged)
            Untested("3 unblocked: W drives the helm (control)", "the island camera is engaged at the berth, so the helm ignores W by design; see the Sheets.Open control");
        else Gate("3 unblocked: W drives the helm again (the control)", thrMax > thr0 + 0.5f,
            $"ThrottleOrder {thr0:F3} -> max {thrMax:F3}; helm enabled {helmOn}, IslandCam.Engaged {SeaSick.CameraRig.IslandCam.Engaged}");
        if (sheet != null)
        {
            SheetsApi.Open(sheet);
            bool opened = SheetsApi.IsOpen;
            SheetsApi.Close();
            Gate("3 unblocked: Sheets.Open works again (control)", opened, $"IsOpen after Open {opened}");
        }
        if (chase != null)
        {
            float z0 = chase.ZoomScale, zMax = 0f;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = centre, scroll = new Vector2(0f, 120f) });
            for (int i = 0; i < 5; i++) { yield return null; zMax = Mathf.Max(zMax, Mathf.Abs(chase.ZoomScale - z0)); }
            sb.AppendLine($"  INFO unblocked wheel moved the chase zoom by {zMax:F4} (not gated: the island view / tuner can own the wheel)");
        }

        CleanupInput();
        yield return new WaitForSeconds(1f);
        yield return EnsureRefittable();
    }

    /// The screen's Cancel button: first a real UI Toolkit submit event,
    /// then the button's own click delegate (the same ShipyardScreen.Close),
    /// last the close callback's own action (destroying the modal object).
    IEnumerator CloseModal(string[] how)
    {
        var modal = FindAnyObjectByType<ShipyardModal>();
        if (modal == null) { how[0] = "nothing to close"; yield break; }
        var doc = modal.GetComponent<UIDocument>();
        Button cancel = null;
        if (doc != null && doc.rootVisualElement != null)
            doc.rootVisualElement.Query<Button>().ForEach(b => { if (cancel == null && b.text == "Cancel") cancel = b; });
        if (cancel != null)
        {
            try
            {
                using (var e = NavigationSubmitEvent.GetPooled())
                {
                    e.target = cancel;
                    cancel.SendEvent(e);
                }
            }
            catch (Exception ex) { sb.AppendLine("  note: submit event threw " + ex.Message); }
            yield return null;
            yield return null;
            if (!ShipyardModal.IsOpen) { how[0] = "a NavigationSubmitEvent on the Cancel button"; yield break; }
            int invoked = 0;
            if (cancel.clickable != null)
                foreach (var f in typeof(Clickable).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    if (f.FieldType == typeof(Action) && f.GetValue(cancel.clickable) is Action a) { a(); invoked++; }
            yield return null;
            yield return null;
            if (!ShipyardModal.IsOpen) { how[0] = $"the Cancel button's click delegate ({invoked} invoked; submit event did not close it in this mode)"; yield break; }
        }
        modal = FindAnyObjectByType<ShipyardModal>();
        if (modal != null) Destroy(modal.gameObject);
        yield return null;
        how[0] = (cancel == null ? "Cancel button not found; " : "the button did not close it; ")
            + "destroyed the modal GameObject (what the screen's close callback does)";
    }

    int CountPreviews()
    {
        int n = 0;
        foreach (var v in FindObjectsByType<ModularShipView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (v != null && v.gameObject.name == "ShipyardPreview") n++;
        return n;
    }

    void CleanupInput()
    {
        try
        {
            if (kb != null) { InputSystem.RemoveDevice(kb); kb = null; }
            if (mouse != null) { InputSystem.RemoveDevice(mouse); mouse = null; }
        }
        catch (Exception e) { sb.AppendLine("  note: removing the virtual devices threw " + e.Message); }
        if (inputSettingsChanged)
        {
            inputSettingsChanged = false;
            var st = InputSystem.settings;
            st.backgroundBehavior = prevBackground;
            st.editorInputBehaviorInPlayMode = prevEditorBehaviour;
            Application.runInBackground = prevRunInBackground;
        }
        if (ShipyardModal.IsOpen)
        {
            var m = FindAnyObjectByType<ShipyardModal>();
            if (m != null) Destroy(m.gameObject);
        }
        if (ShipyardSession.WorldInputBlocked && !ShipyardModal.IsOpen) ShipyardSession.SetWorldInputBlocked(false);
    }

    // ---- 4: rollback on save failure ----------------------------------------------------

    IEnumerator SaveFailure()
    {
        sb.AppendLine("4. rollback on save failure:");
        yield return EnsureRefittable();
        yield return null;
        string notADir = Path.Combine(tmp, "ShipyardUiProbe-notadir");
        File.WriteAllText(notADir, "a file where the save's folder would be");
        tempFiles.Add(notADir);
        string bad = Path.Combine(notADir, "persist.json");

        var target = OtherValid();
        var bridge = new ShipyardLiveBridge();
        var expected = bridge.ReadCurrent();
        int views0 = AllViews(), kids0 = yard.transform.childCount;
        var s0 = Capture();
        ShipyardService.PersistPathOverride = bad;
        CheckOverride("4 TryApply");
        bool ok;
        string reason;
        try { ok = bridge.TryApply(expected, target, out reason); }
        finally { ShipyardService.PersistPathOverride = persistOk; }
        var s1 = Capture();
        Gate("4 TryApply with an unwritable save refused with SAVE_FAILED", !ok && reason.Contains(ShipyardCodes.SaveFailed),
            $"target {Describe(target)}; {reason}");
        Gate("4 ship bit-identical after the failed save", Diff(s0, s1, out string diff), diff);
        Gate("4 exactly one player ship", OneShip(out string one), one);

        // The UI path: the modal stays open with the reason, draft not committed.
        var d = NewDraft(out _);
        bool changed = target.middleIds.Count > d.Count ? d.AddMiddle() : d.RemoveMiddle();
        ShipyardService.PersistPathOverride = bad;
        CheckOverride("4 Confirm");
        bool c;
        try { c = d.Confirm(); }
        finally { ShipyardService.PersistPathOverride = persistOk; }
        var s2 = Capture();
        Gate("4 draft.Confirm with an unwritable save refused, reason kept, not committed", changed && !c && !d.Committed
            && d.Message.Contains(ShipyardCodes.SaveFailed), $"Confirm {c}, message \"{d.Message}\"");
        Gate("4 ship bit-identical after the failed confirm", Diff(s0, s2, out string diff2), diff2);
        yield return null;
        yield return null;
        Gate("4 no leaked objects, drawing = old configuration", AllViews() == views0 && yard.transform.childCount == kids0
            && VisualMatchesConfig(out string vis) && Visual() == s0.visual,
            $"ModularShipViews {views0}->{AllViews()}, ship children {kids0}->{yard.transform.childCount}; {Visual()}");
        Gate("4 nothing written at the bad path, the real save untouched", !File.Exists(bad) && RealSaveSame(), RealSaveLine());
    }

    // ---- 5: rollback on build failure ------------------------------------------------------

    IEnumerator BuildFaults()
    {
        sb.AppendLine("5. rollback on build failure (ShipyardService.TestFaultStage):");
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var stages = new[] { ShipyardService.FaultAfterSnapshot, ShipyardService.FaultAfterAssemble, ShipyardService.FaultBeforePersist };
        foreach (bool fromModular in new[] { true, false })
        {
            string from = fromModular ? "refitted ship" : "standard steamer";
            if (fromModular && !yard.ModularActive)
            {
                var r = yard.ApplyRefit(yard.Current, Mod(ShipConfiguration.Long(), c => c.rotorId = ShipConfiguration.TimberRotor));
                sb.AppendLine("  setup refit: " + r.ToString().Replace("\n", " | "));
                yield return new WaitForSeconds(0.6f);
            }
            if (!fromModular)
            {
                yard.ApplyFromSave("");
                yield return new WaitForSeconds(0.6f);
                Gate("5 setup: back to the standard steamer (ApplyFromSave \"\")", !yard.ModularActive && V8Active(),
                    $"modular {yard.ModularActive}, V8 drawn {V8Active()}; {Visual()}");
            }
            foreach (var stage in stages)
            {
                string name = $"5 {stage} from the {from}";
                yield return EnsureRefittable();
                yield return null;
                var target = OtherValid();
                var bridge = new ShipyardLiveBridge();
                var expected = bridge.ReadCurrent();
                int views0 = AllViews(), kids0 = yard.transform.childCount, roots0 = RootCount();
                var s0 = Capture();
                bool ok = false; string reason = ""; Exception thrown = null;
                CheckOverride(name);
                ShipyardService.TestFaultStage = stage;
                try { ok = bridge.TryApply(expected, target, out reason); }
                catch (Exception e) { thrown = e; }
                finally { ShipyardService.TestFaultStage = null; }
                var s1 = Capture();
                string code = stage == ShipyardService.FaultBeforePersist ? ShipyardCodes.SaveFailed : ShipyardCodes.ApplyFailed;
                Gate(name + ": refused with " + code, thrown == null && !ok && reason.Contains(code) && reason.Contains("TEST FAULT"),
                    thrown != null ? "EXCEPTION ESCAPED ApplyRefit: " + thrown.GetType().Name + ": " + thrown.Message : $"target {Describe(target)}; {reason}");
                Gate(name + ": old ship bit-identical", Diff(s0, s1, out string diff), diff);
                Gate(name + ": exactly one player ship", OneShip(out string one), one);
                yield return null;
                yield return null;
                Gate(name + ": no leaked objects", AllViews() == views0 && yard.transform.childCount == kids0,
                    $"ModularShipViews {views0}->{AllViews()}, ship children {kids0}->{yard.transform.childCount}, scene roots {roots0}->{RootCount()} (roots not gated)");
                bool drawn = VisualMatchesConfig(out string vis) && Visual() == s0.visual && (fromModular || V8Active());
                Gate(name + ": drawing = old configuration", drawn, $"{vis}; before [{s0.visual}] after [{Visual()}]");
                yield return new WaitForSeconds(0.3f);
                Gate(name + ": HullLength and mass still the old form", Mathf.Approximately(motor.HullLength, yard.ActiveData.lwl)
                    && Mathf.Approximately(rb.mass, yard.ActiveData.massKg) && rb.mass == s0.mass,
                    $"HullLength {motor.HullLength:F3}/{yard.ActiveData.lwl:F3}, mass {rb.mass:F0}/{yard.ActiveData.massKg:F0} (was {s0.mass:F0})");
            }
        }
        Gate("5 fault hook cleared", ShipyardService.TestFaultStage == null, "TestFaultStage " + (ShipyardService.TestFaultStage ?? "null"));
#else
        Untested("5 build faults", "the fault hook is compiled only in the editor and development builds");
        yield break;
#endif
    }

    // ---- 6: save/load through the UI path -----------------------------------------------------

    IEnumerator SaveLoad()
    {
        sb.AppendLine("6. save/load round trip through the UI path:");
        yield return EnsureRefittable();
        var d = NewDraft(out _);
        var two = d.Snapshot();
        two.middleIds.Add(ShipConfiguration.V3Middle);
        Gate("6 draft: AddMiddle (1 -> 2 bays)", d.AddMiddle() && d.Count == 2, Msg(d));
        yield return ConfirmStep("6 2 bays", d, two);
        var savedCfg = yard.Current;
        var savedHold = HoldPerKind();
        string savedNames = Names();
        int savedCrew = Crew().Count;
        float savedInteg = Integrity();

        string saveA = Path.Combine(tmp, "ShipyardUiProbe-save.json");
        tempFiles.Add(saveA);
        bool wrote = SaveGame.SaveTo(saveA, "ShipyardUiProbe");

        d = NewDraft(out _);
        var one = d.Snapshot();
        one.middleIds.RemoveAt(one.middleIds.Count - 1);
        Gate("6 draft: RemoveMiddle (2 -> 1 bay)", d.RemoveMiddle() && d.Count == 1, Msg(d));
        yield return ConfirmStep("6 back to 1 bay", d, one);

        var data = SaveGame.Read(saveA);
        Gate("6 temp save written and carries the modular field", wrote && data != null && !string.IsNullOrEmpty(data.ship.modular),
            data != null ? data.ship.modular : "unreadable");
        if (data == null) yield break;
        yield return SaveGame.Restore(data, this);
        yield return new WaitForSeconds(0.6f);
        Gate("6 load brings back 2 bays with the same hold and crew", SaveGame.LastRestoreOk && yard.ModularActive
            && yard.Current.ValueEquals(savedCfg) && SameHold(savedHold, HoldPerKind()) && Names() == savedNames && Crew().Count == savedCrew,
            $"{SaveGame.LastRestoreNote}; {Describe(yard.Current)}; hold {Fmt(HoldPerKind())} vs {Fmt(savedHold)}; crew {Crew().Count}/{savedCrew} [{Names()}]");
        sb.AppendLine($"  INFO integrity saved {savedInteg:F4}, after load {Integrity():F4}");
        Gate("6 after load: exactly one player ship", OneShip(out string oneShip), oneShip);
        CheckForm("6 after load");
    }

    // ---- 7: deck toggle (Single/Raised), docs/RAISED-DECK.md sec 3/8 -------------------------

    /// Draft-only (never `Confirm`s, so the live ship is never touched):
    /// disabled-with-reason on standard beam and at 0/3 middles, enabled at
    /// 1-2 middles on wide beam; toggling to raised swaps stern/bow/middle
    /// ids to the W1xR family; while raised the beam toggle is refused with
    /// its own reason and +/- bays are blocked at the 1/2 bounds; toggling
    /// back restores the single W1x ids. Guns are untouched by either
    /// toggle (`SetRaisedDeck`/`SetWideBeam` only swap hull ids), checked
    /// each way.
    IEnumerator DeckToggle()
    {
        sb.AppendLine("7. deck toggle (Single/Raised), mirrors the beam toggle one level up:");
        yield return EnsureRefittable();
        var d = NewDraft(out _);

        // Standard beam: disabled with its reason, and SetRaisedDeck(true) itself refuses.
        if (d.IsWideBeam) d.SetWideBeam(false);
        string reasonStd = d.RaisedDeckUnavailableReason();
        Gate("7a deck disabled with reason on standard beam", !d.IsRaisedDeck && reasonStd != null && reasonStd.Contains("wide beam"),
            reasonStd ?? "null");
        bool triedStd = d.SetRaisedDeck(true);
        Gate("7a SetRaisedDeck(true) refused on standard beam", !triedStd && !d.IsRaisedDeck && d.Message == reasonStd,
            $"ok {triedStd}, message \"{d.Message}\"");

        // Wide beam, 0 middles: still disabled, reason names the bay bound.
        Gate("7b draft: SetWideBeam(true)", d.SetWideBeam(true) && d.IsWideBeam, Msg(d));
        while (d.Count > 0) if (!d.RemoveMiddle()) break;
        Gate("7b setup: 0 middles", d.Count == 0, Msg(d));
        string reasonZero = d.RaisedDeckUnavailableReason();
        Gate("7b deck disabled with reason at 0 middles", reasonZero != null && reasonZero.ToLower().Contains("bay"), reasonZero ?? "null");

        // 1-2 middles, wide beam: enabled (no reason).
        Gate("7c draft: AddMiddle to 1", d.AddMiddle() && d.Count == 1, Msg(d));
        Gate("7c deck enabled at 1 middle", d.RaisedDeckUnavailableReason() == null, d.RaisedDeckUnavailableReason() ?? "null");
        Gate("7c draft: AddMiddle to 2", d.AddMiddle() && d.Count == 2, Msg(d));
        Gate("7c deck enabled at 2 middles", d.RaisedDeckUnavailableReason() == null, d.RaisedDeckUnavailableReason() ?? "null");

        // 3 middles: STILL enabled (docs/RAISED-SECTIONS.md sec 5 relaxed
        // the old all-or-nothing cap of 1-2 middles to the library's
        // general bound; 3 fully-connected raised middles is now
        // data-legal, unverified against Astra's art -- ModularShipPreview
        // renders it for Kevin to look at).
        Gate("7d draft: AddMiddle to 3", d.AddMiddle() && d.Count == 3, Msg(d));
        Gate("7d deck still enabled at 3 middles (old cap removed)", d.RaisedDeckUnavailableReason() == null,
            d.RaisedDeckUnavailableReason() ?? "null");
        Gate("7d draft: RemoveMiddle back to 2", d.RemoveMiddle() && d.Count == 2, Msg(d));

        // Toggle to raised: swaps all three hull ids, guns kept.
        string equipBefore = EquipSlots(d.Snapshot());
        bool toRaised = d.SetRaisedDeck(true);
        var snap = d.Snapshot();
        Gate("7e SetRaisedDeck(true) swaps all three hull ids", toRaised && d.IsRaisedDeck
            && snap.sternId == RaisedPresets.RaisedStern && snap.bowId == RaisedPresets.RaisedBow
            && snap.middleIds.TrueForAll(m => m == RaisedPresets.RaisedMiddle),
            $"ok {toRaised}, stern {snap.sternId}, bow {snap.bowId}, middles [{string.Join(",", snap.middleIds)}]");
        Gate("7e guns kept across the toggle to raised", EquipSlots(snap) == equipBefore, $"before [{equipBefore}] after [{EquipSlots(snap)}]");

        // While raised: beam toggle refused with its own reason.
        const string beamReason = "A raised deck needs the wide beam.";
        bool triedBeam = d.SetWideBeam(false);
        Gate("7f beam toggle refused with its reason while raised", !triedBeam && d.IsRaisedDeck && d.Message == beamReason,
            $"ok {triedBeam}, message \"{d.Message}\"");

        // +/- bays: the raised-only max of 2 is GONE (docs/RAISED-SECTIONS.md
        // sec 5 relaxed the old all-or-nothing kit's own cap to the
        // library's general bound, 3) -- AddMiddle now succeeds to 3 while
        // raised, same as unraised; the minimum (1, RAISED_DECK_BAYS: 0
        // middles can never have both ends raised) is unchanged.
        Gate("7g setup: raised at 2 middles", d.Count == 2, Msg(d));
        bool addPastOldMax = d.AddMiddle();
        Gate("7g +bays now reaches 3 while raised (old cap of 2 removed)", addPastOldMax && d.Count == 3, Msg(d));
        bool addAtRealMax = d.AddMiddle();
        Gate("7g +bays blocked at the library's real max (3)", !addAtRealMax && d.Count == 3, Msg(d));
        Gate("7g draft: RemoveMiddle to 2", d.RemoveMiddle() && d.Count == 2, Msg(d));
        Gate("7g draft: RemoveMiddle to 1 (raised minimum)", d.RemoveMiddle() && d.Count == 1, Msg(d));
        bool removeAtMin = d.RemoveMiddle();
        Gate("7g -bays blocked at the raised minimum (1)", !removeAtMin && d.Count == 1, Msg(d));
        Gate("7g draft: AddMiddle back to 2", d.AddMiddle() && d.Count == 2, Msg(d));

        // Toggle back: restores single W1x ids, guns kept.
        string equipRaised = EquipSlots(d.Snapshot());
        bool toSingle = d.SetRaisedDeck(false);
        var snapBack = d.Snapshot();
        Gate("7h SetRaisedDeck(false) restores single W1x hull ids", toSingle && !d.IsRaisedDeck
            && snapBack.sternId == ExpandedPresets.ExpandedStern && snapBack.bowId == ExpandedPresets.ExpandedBow
            && snapBack.middleIds.TrueForAll(m => m == ExpandedPresets.ExpandedMiddle),
            $"ok {toSingle}, stern {snapBack.sternId}, bow {snapBack.bowId}, middles [{string.Join(",", snapBack.middleIds)}]");
        Gate("7h guns kept across the toggle back to single", EquipSlots(snapBack) == equipRaised,
            $"before [{equipRaised}] after [{EquipSlots(snapBack)}]");
    }

    /// Draft-only: per-section ToggleSection (docs/RAISED-SECTIONS.md task
    /// item 4/6) -- disabled with its reason on standard beam and when the
    /// only remaining refusal (0 middles, both ends already raised) would
    /// hit RAISED_DECK_BAYS; ids recomputed via RaisedSections.ToIds after
    /// each toggle (checked against the SAME function, since ShipyardDraft
    /// is meant to never duplicate that rule); RaiseAll/LowerAll aliasing
    /// SetRaisedDeck.
    IEnumerator SectionToggle()
    {
        sb.AppendLine("8. per-section toggle (tap-a-section), on top of the whole-hull toggle in 7:");
        yield return EnsureRefittable();
        var d = NewDraft(out _);

        // Standard beam: every section disabled with the wide-beam reason.
        if (d.IsWideBeam) d.SetWideBeam(false);
        string reasonStd = d.SectionUnavailableReason(ShipAssembler.StdKeyStern);
        Gate("8a section disabled with reason on standard beam", reasonStd != null && reasonStd.Contains("wide beam"), reasonStd ?? "null");
        bool triedStd = d.ToggleSection(ShipAssembler.StdKeyStern);
        Gate("8a ToggleSection refused on standard beam", !triedStd && !d.IsSectionRaised(ShipAssembler.StdKeyStern), $"ok {triedStd}");

        // Wide beam, 0 middles: ONE end can raise on its own now (the
        // relaxed rule, docs/RAISED-SECTIONS.md sec 5); the OTHER end then
        // refuses with the RAISED_DECK_BAYS reason (0 middles, both ends).
        d.SetWideBeam(true);
        while (d.Count > 0) if (!d.RemoveMiddle()) break;
        Gate("8b setup: wide beam, 0 middles", d.IsWideBeam && d.Count == 0, Msg(d));
        bool sternUp = d.ToggleSection(ShipAssembler.StdKeyStern);
        var (wantSternId, _, wantBowId) = RaisedSections.ToIds(DeckLevel.Raised, System.Array.Empty<DeckLevel>(), DeckLevel.Low);
        Gate("8b stern alone raises at 0 middles", sternUp && d.IsSectionRaised(ShipAssembler.StdKeyStern)
            && d.Snapshot().sternId == wantSternId && d.Snapshot().bowId == wantBowId,
            $"ok {sternUp}, sternId {d.Snapshot().sternId}, bowId {d.Snapshot().bowId} (want {wantSternId}/{wantBowId})");
        string reasonBow = d.SectionUnavailableReason(ShipAssembler.StdKeyBow);
        Gate("8b bow refused (0 middles, both ends would be raised)", reasonBow != null, reasonBow ?? "null");
        bool bowUp = d.ToggleSection(ShipAssembler.StdKeyBow);
        Gate("8b ToggleSection(bow) itself refused", !bowUp && !d.IsSectionRaised(ShipAssembler.StdKeyBow), $"ok {bowUp}");
        d.ToggleSection(ShipAssembler.StdKeyStern); // back down
        Gate("8b stern back down", !d.IsSectionRaised(ShipAssembler.StdKeyStern), Msg(d));

        // Wide beam, 2 middles: toggle every section one at a time; each
        // toggle's resulting ids match RaisedSections.ToIds independently
        // computed from IsSectionRaised (never the same call re-used).
        d.AddMiddle(); d.AddMiddle();
        Gate("8c setup: 2 middles", d.Count == 2, Msg(d));
        var keys = d.SectionKeys();
        Gate("8c SectionKeys is stern, middle[0], middle[1], bow", keys.Count == 4 && keys[0] == "stern"
            && keys[1] == "middle[0]" && keys[2] == "middle[1]" && keys[3] == "bow", string.Join(",", keys));
        foreach (var key in keys)
        {
            bool before = d.IsSectionRaised(key);
            string reason = before ? null : d.SectionUnavailableReason(key);
            bool applied = d.ToggleSection(key);
            bool expectOk = before || reason == null;
            bool stateOk = expectOk ? (applied && d.IsSectionRaised(key) != before) : (!applied && d.IsSectionRaised(key) == before);
            Gate($"8c ToggleSection({key}) {(expectOk ? "applies" : "refused")}", stateOk,
                $"before {before}, reason \"{reason}\", applied {applied}, after {d.IsSectionRaised(key)}");
            if (applied)
            {
                var lv = RaisedSections.FromIds(d.Snapshot().sternId, d.Snapshot().middleIds, d.Snapshot().bowId);
                var (wantS, wantM, wantB) = RaisedSections.ToIds(lv.stern, lv.middles, lv.bow);
                Gate($"8c ids recomputed after {key}", d.Snapshot().sternId == wantS && d.Snapshot().bowId == wantB
                    && string.Join(",", d.Snapshot().middleIds) == string.Join(",", wantM),
                    $"stern {d.Snapshot().sternId}, middles [{string.Join(",", d.Snapshot().middleIds)}], bow {d.Snapshot().bowId}");
            }
        }

        // RaiseAll/LowerAll alias SetRaisedDeck exactly.
        var freshDraft = NewDraft(out _);
        freshDraft.SetWideBeam(true); freshDraft.AddMiddle();
        bool raisedAll = freshDraft.RaiseAll();
        Gate("8d RaiseAll raises every section", raisedAll && freshDraft.IsRaisedDeck
            && AllSectionsRaised(freshDraft, true), Msg(freshDraft));
        bool loweredAll = freshDraft.LowerAll();
        Gate("8d LowerAll lowers every section", loweredAll && !freshDraft.IsRaisedDeck
            && AllSectionsRaised(freshDraft, false), Msg(freshDraft));
    }

    static bool AllSectionsRaised(ShipyardDraft d, bool raised)
    {
        foreach (var k in d.SectionKeys()) if (d.IsSectionRaised(k) != raised) return false;
        return true;
    }

    static string EquipSlots(ShipConfiguration c)
    {
        var ids = new List<string>();
        foreach (var e in c.equipment) if (e != null) ids.Add(e.slotId + ":" + e.moduleId);
        ids.Sort();
        return string.Join(",", ids);
    }

    // ---- the ship's state, and comparing it ----------------------------------------------------

    class ShipState
    {
        public string cfg, names, visual;
        public bool modular;
        public Dictionary<string, int> hold;
        public int crew, holdCap;
        public float integ, hullLength, mass;
        public Vector3 pos, com;
        public Quaternion rot;
        public HullFormData data;
    }

    ShipState Capture() => new ShipState
    {
        cfg = yard.Current.ToJson(),
        modular = yard.ModularActive,
        hold = HoldPerKind(),
        names = Names(),
        crew = Crew().Count,
        holdCap = voyage.HoldCapacity,
        integ = Integrity(),
        hullLength = motor.HullLength,
        mass = rb.mass,
        com = rb.centerOfMass,
        pos = yard.transform.position,
        rot = yard.transform.rotation,
        data = yard.GetComponent<SteamerShip>() != null ? yard.GetComponent<SteamerShip>().Data : null,
        visual = Visual(),
    };

    /// Exact comparison (floats bit for bit); `diff` lists what changed.
    static bool Diff(ShipState a, ShipState b, out string diff)
    {
        var d = new List<string>();
        if (a.cfg != b.cfg) d.Add("configuration");
        if (a.modular != b.modular) d.Add($"modular {a.modular}->{b.modular}");
        if (!SameHold(a.hold, b.hold)) d.Add($"hold {Fmt(a.hold)}->{Fmt(b.hold)}");
        if (a.names != b.names || a.crew != b.crew) d.Add($"crew {a.crew}->{b.crew}");
        if (a.holdCap != b.holdCap) d.Add($"hold capacity {a.holdCap}->{b.holdCap}");
        if (a.integ != b.integ) d.Add($"integrity {a.integ:R}->{b.integ:R}");
        if (a.hullLength != b.hullLength) d.Add($"HullLength {a.hullLength:R}->{b.hullLength:R}");
        if (a.mass != b.mass) d.Add($"mass {a.mass:R}->{b.mass:R}");
        if (!Same(a.com, b.com)) d.Add($"centre of mass {a.com:F4}->{b.com:F4}");
        if (!Same(a.pos, b.pos)) d.Add($"position {a.pos:F4}->{b.pos:F4}");
        if (!(a.rot.x == b.rot.x && a.rot.y == b.rot.y && a.rot.z == b.rot.z && a.rot.w == b.rot.w)) d.Add("rotation");
        if (!ReferenceEquals(a.data, b.data)) d.Add("hull form object");
        if (a.visual != b.visual) d.Add($"drawing [{a.visual}]->[{b.visual}]");
        diff = d.Count == 0 ? $"identical ({a.hullLength:F3} m, {a.mass:F0} kg, hold {Fmt(a.hold)}, {a.crew} hands, integrity {a.integ:F4})"
            : "CHANGED: " + string.Join("; ", d);
        return d.Count == 0;
    }

    static bool Same(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;

    GameObject ReferenceHull()
    {
        var f = typeof(ShipyardService).GetField("referenceHull", BindingFlags.Instance | BindingFlags.NonPublic);
        return f != null ? f.GetValue(yard) as GameObject : null;
    }

    bool V8Active() { var g = ReferenceHull(); return g != null && g.activeSelf; }

    /// The ship's live drawing: the active module views (inactive = being
    /// destroyed) and whether the V8 art is on.
    string Visual()
    {
        var views = new List<ModularShipView>();
        foreach (var v in yard.GetComponentsInChildren<ModularShipView>(false)) if (v != null) views.Add(v);
        string v8 = V8Active() ? "V8 on" : "V8 off";
        if (views.Count == 0) return v8 + ", no module view";
        var parts = new List<string>();
        foreach (var v in views)
        {
            var ids = new List<string>();
            if (v.Current != null) foreach (var p in v.Current.placed) ids.Add(p.moduleId);
            parts.Add($"{ids.Count} modules/{v.transform.childCount} children [{string.Join(",", ids)}]");
        }
        return v8 + ", " + views.Count + " view(s): " + string.Join(" + ", parts);
    }

    /// Standard steamer: V8 drawn, no module view. Refitted: V8 hidden,
    /// exactly one active view whose modules are what the configuration
    /// assembles to.
    bool VisualMatchesConfig(out string detail)
    {
        var views = new List<ModularShipView>();
        foreach (var v in yard.GetComponentsInChildren<ModularShipView>(false)) if (v != null) views.Add(v);
        if (!yard.ModularActive)
        {
            detail = "standard: " + Visual();
            return V8Active() && views.Count == 0;
        }
        var want = ShipAssembler.Assemble(yard.Current, yard.Library);
        var wantIds = new List<string>();
        foreach (var p in want.placed) wantIds.Add(p.moduleId);
        bool ok = !V8Active() && views.Count == 1 && views[0].Current != null && views[0].transform.childCount == wantIds.Count;
        if (ok)
        {
            var have = new List<string>();
            foreach (var p in views[0].Current.placed) have.Add(p.moduleId);
            ok = string.Join(",", have) == string.Join(",", wantIds);
        }
        detail = $"{Describe(yard.Current)} expects {wantIds.Count} modules; drawn: {Visual()}";
        return ok;
    }

    int AllViews() => FindObjectsByType<ModularShipView>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
    static int RootCount() => UnityEngine.SceneManagement.SceneManager.GetActiveScene().rootCount;

    bool OneShip(out string detail)
    {
        int yards = FindObjectsByType<ShipyardService>(FindObjectsSortMode.None).Length;
        int motors = FindObjectsByType<ShipMotor>(FindObjectsSortMode.None).Length;
        int hulls = FindObjectsByType<SeaSick.Combat.PlayerHull>(FindObjectsSortMode.None).Length;
        int ears = FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length;
        bool same = ShipyardService.Player == yard && yard.gameObject.GetInstanceID() == shipId && rb != null && rb.GetInstanceID() == bodyId;
        detail = $"same object/body {same}, ShipyardServices {yards}, ShipMotors {motors}, PlayerHull bodies {hulls}, listeners {ears}/{listeners}";
        return same && yards == 1 && motors == 1 && hulls <= 1 && ears == listeners;
    }

    bool HandsOnDeck(out string detail)
    {
        var d = yard.ActiveData;
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
        detail = $"{onDeck}/{standing} standing hands on deck ({Crew().Count} aboard) {worst}";
        return onDeck == standing && standing > 0;
    }

    // ---- helpers ------------------------------------------------------------------------

    /// A valid refit different from what she is now: one more bay (one fewer
    /// at 3), same wheel.
    ShipConfiguration OtherValid()
    {
        var c = yard.Current;
        int n = c.middleIds.Count;
        var t = ShipConfiguration.WithMiddles(n >= 2 ? n - 1 : n + 1);
        t.rotorId = c.rotorId;
        return t;
    }

    IEnumerator EnsureRefittable()
    {
        if (yard.CanRefitNow(out _)) yield break;
        if (!anchor.AtHomeDock) anchor.BerthAtHome(out _);
        float t0 = Time.realtimeSinceStartup;
        while (!yard.CanRefitNow(out _) && Time.realtimeSinceStartup - t0 < 6f) yield return null;
        if (!yard.CanRefitNow(out string why)) sb.AppendLine("  note: cannot refit now: " + why);
    }

    void CheckOverride(string where)
    {
        overrideChecks++;
        if (ShipyardService.PersistPathOverride == null) overrideNullAt.Add(where);
    }

    bool RealSaveSame()
    {
        bool exists = File.Exists(realSavePath);
        if (exists != realSaveExisted) return false;
        if (!exists) return true;
        return File.GetLastWriteTimeUtc(realSavePath) == realSaveTime && new FileInfo(realSavePath).Length == realSaveLen;
    }

    string RealSaveLine() => $"{realSavePath}: existed {realSaveExisted}"
        + (File.Exists(realSavePath) ? $", now {File.GetLastWriteTimeUtc(realSavePath):O} {new FileInfo(realSavePath).Length} B (was {realSaveTime:O} {realSaveLen} B)" : ", absent now");

    void SetHold(int timber, int stone) =>
        voyage.RestoreStores(new[] { Pair(Res.Timber, timber), Pair(Res.Stone, stone) }, Banked());

    static string Msg(ShipyardDraft d) => $"count {d.Count}, rotor {d.Rotor}, message \"{d.Message}\"";
    static string Describe(ShipConfiguration c) =>
        c == null ? "null" : $"{c.middleIds.Count} bays, {(c.rotorId == ShipConfiguration.TimberRotor ? "timber" : c.rotorId == ShipConfiguration.ReinforcedRotor ? "reinforced" : c.rotorId)}";
    static ShipConfiguration Mod(ShipConfiguration c, Action<ShipConfiguration> f) { var x = c.Clone(); f(x); return x; }
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

    /// Runs a section (and every IEnumerator it yields) by hand so an
    /// exception anywhere in it becomes a FAIL line instead of a dead
    /// coroutine and a batch run that waits out its timeout.
    IEnumerator Safe(string name, IEnumerator body)
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(body);
        while (stack.Count > 0)
        {
            var top = stack.Peek();
            object cur;
            try
            {
                if (!top.MoveNext()) { stack.Pop(); continue; }
                cur = top.Current;
            }
            catch (Exception e)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                ShipyardService.TestFaultStage = null;
#endif
                if (ShipyardService.PersistPathOverride != persistOk) ShipyardService.PersistPathOverride = persistOk;
                string at = e.StackTrace != null ? e.StackTrace.Split('\n')[0].Trim() : "";
                Gate("section " + name + " ran to the end", false, $"{e.GetType().Name}: {e.Message} at {at}");
                yield break;
            }
            if (cur is IEnumerator nested) { stack.Push(nested); continue; }
            yield return cur;
        }
    }

    void Gate(string name, bool ok, string detail)
    {
        if (ok) passes++; else fails++;
        sb.Append(ok ? "  PASS " : "  FAIL ").Append(name).Append(" -- ").AppendLine(detail);
    }

    void Untested(string name, string why)
    {
        untested++;
        sb.Append("  NOT TESTED ").Append(name).Append(" -- ").AppendLine(why);
    }

    void OnDestroy()
    {
        // A runner destroyed mid-run must not leave the game's input or the
        // fault hook in a probe state.
        if (finished) return;
        CleanupInput();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        ShipyardService.TestFaultStage = null;
#endif
    }

    void Finish(string stopped)
    {
        finished = true;
        CleanupInput();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        ShipyardService.TestFaultStage = null;
#endif
        ShipyardService.PersistPathOverride = null;
        foreach (var f in tempFiles) { try { if (File.Exists(f)) File.Delete(f); } catch { } }
        if (stopped != null) { fails++; sb.AppendLine("  STOPPED -- " + stopped); }
        sb.AppendLine($"ShipyardUiProbe: {passes} PASS, {fails} FAIL, {untested} NOT TESTED");
        string text = "[ShipyardUiProbe]\n" + sb;
        if (fails == 0) Debug.Log(text); else Debug.LogError(text);
        var path = Path.Combine(Application.dataPath, "../Logs/ShipyardUiProbe.txt");
        try { File.WriteAllText(path, text); } catch { }
        Destroy(gameObject);
    }
}
