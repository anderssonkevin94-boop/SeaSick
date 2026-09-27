using UnityEngine;

/// Thin launchers for the play-mode probes that live in `Scripts/Dev/`.
///
/// `execute_script` compiles the file it is given into a FRESH assembly, and
/// that assembly does not get the same reference set the project's own does --
/// SurfProbe pulls in SeaSick.Terrain, SeaSick.World and Unity.Collections and
/// the ad-hoc compile failed on it while `check_compile_errors` reported the
/// project perfectly clean. The failure surfaces as a Roslyn resource-loading
/// exception rather than as an error message, so there is nothing to read.
///
/// A launcher sidesteps it entirely: this file has almost no references of its
/// own, and the probe it calls comes from the already-compiled project
/// assembly rather than being recompiled.
public static class RunProbe
{
    public static void Surf() => Call("SurfProbe");
    public static void Crest() => Call("CrestProbe");
    public static void SprayRig() => Call("SprayRigCheck");
    public static void HomeTab() => Call("HomeTabProbe");
    public static void Look() => Call("IslandLook");
    public static void Ripple() => Call("RippleStressProbe");
    // What heavy weather COSTS her, over an Hs ladder of 2/5/9/14/22/35/50 m.
    // Not a survival gate -- PlayerHull.Alive is hardcoded true and roll is
    // spring-clamped, so "does she live" can only come back green. This reads
    // the price instead: broach, overspeed, surf, bilge, hull, roughness,
    // heel, way, sea resistance and crew labour, per sea state, running
    // down-sea in searched-for deep water. Play mode, Sea.unity, ~9 min.
    public static void StormCost() => Call("StormCostProbe");
    /// The needle gate under a jittering frame time, old sub-step vs fixed.
    public static void RippleJitter() => Call("RippleStressProbe", "Jitter");

    /// The same sheet, framed on the ROCKIEST island in reach rather than the
    /// nearest, and tagged so a before cannot be mistaken for an after. The
    /// carved style is judged on the outcrops, and home is a rock-poor islet.
    /// Set through reflection like every other launcher here: this file is
    /// recompiled on its own by `execute_script` and must not name a type
    /// from the runtime assembly.
    public static void LookRocky() => LookAt("rock", true, false);
    public static void LookNear() => LookAt("run", false, false);

    /// The same sheet on the island with the most WHEAT on it. Farming is a
    /// roll inside the scenery bake, so there is no other way to photograph
    /// a field without sailing about until one turns up.
    public static void LookFarm() => LookAt("farm", false, true);

    static void LookAt(string tag, bool rocky, bool farmed)
    {
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType("IslandLook");
            if (t == null) continue;
            t.GetField("Tag").SetValue(null, tag);
            t.GetField("PreferRocky").SetValue(null, rocky);
            t.GetField("PreferFarmed").SetValue(null, farmed);
            break;
        }
        Call("IslandLook");
    }
    public static void Peek() => Call("ChunkPeek");
    public static void Cost() => Call("CostProbe");
    public static void Shadows() => Call("ShadowCostProbe");
    public static void Drive() => Call("DriveProbe");
    // How she HANDLES: one launcher per play session. Sway = roll/pitch decay
    // and the three-heading roughness table; Helm = turn build, circle, speed
    // cost, drift, heel, time to speed; Controls = rungs 0 and 19 plus the
    // brig beam-on at severity 0.75. /tmp/seasick-handling-<mode>.txt.
    public static void HandlingSway() => Call("HandlingProbe", "Sway");
    public static void HandlingHelm() => Call("HandlingProbe", "Helm");
    public static void HandlingTrace() => Call("HandlingProbe", "Trace");
    public static void HandlingControls() => Call("HandlingProbe", "Controls");
    public static void Scale() => Call("ScaleCheck");
    public static void Ruler() => Call("ScaleRuler");
    public static void Float() => Call("HullFloatProbe");
    public static void LadderFloat() => Call("LadderFloatProbe");
    public static void Heel() => Call("HeelProbe");
    public static void Framing() => Call("FramingProbe");
    public static void Upgrades() => Call("UpgradeProbe");
    public static void DockUpgrade() => Call("DockUpgradeProbe");
    public static void Lids() => Call("LidProbe");
    public static void Sails() => Call("CanvasShot");
    public static void Stow() => Call("StowProbe");
    public static void Bury() => Call("BuryProbe");
    public static void Broach() => Call("SeaSick.Dev.SeaTrialProbe", "Broach");
    public static void Shallow() => Call("SeaSick.Dev.SeaTrialProbe", "Shallow");
    public static void Shoal() => Call("ShoalShot");
    public static void Horizon() => Call("HorizonShot");
    public static void Stand() => Call("DeckStandProbe");
    public static void Wood() => Call("WoodProbe");
    public static void Harbour() => Call("HarbourProbe");
    public static void Village() => Call("VillageProbe");
    /// The D1 gate: home unchanged by the Village -> Outpost generalisation,
    /// and every other island surveyable on demand at a measured cost.
    public static void Outpost() => Call("OutpostProbe");
    /// The D2 gate: the ledger's arithmetic is path-independent, idempotent,
    /// and survives a save. **`CallEditor`, not `Call`** -- this one is pure
    /// arithmetic over a ledger built in the probe, so it needs no scene, no
    /// island and no play mode. That is the point of it.
    public static void Ledger() => CallEditor("LedgerProbe");
    /// Sail to a real island, land, make camp, and count what came down.
    public static void Camp() => Call("CampProbe");
    // --- the god's-eye island (2026-09-20) -----------------------------------
    /// The hands-on view: a grabbed point stays under the cursor, zoom holds
    /// what it is aimed at, an orbit keeps its pivot, the lens never goes
    /// under the ground. Driven through the methods `IslandInput` calls.
    public static void IslandCam() => Call("IslandCamProbe");
    /// Two-finger maths and the tap/drag/long-press classifier. **`CallEditor`**
    /// -- pure functions, no scene; fingers cannot be faked in the editor, so
    /// the arithmetic is what CAN be gated.
    public static void IslandInput() => CallEditor("IslandInputProbe");
    /// Every kind of drop writes the row it promised, in the frame it
    /// happens -- and D2 survives the Hand: fidgeting and holding pay nothing.
    public static void Hand() => Call("HandProbe");
    /// The people at a camp walk to their work, act it, carry to the pile,
    /// and change no number by doing so.
    public static void CampLife() => Call("CampLifeProbe");
    // --- the sink (2026-09-20) -----------------------------------------------
    /// A camp's stores go into the hold unit for unit -- nothing made, nothing
    /// lost, never past the line unless deck cargo is on, and never by itself.
    public static void CampLoad() => Call("CampLoadProbe");
    // --- save and load (2026-09-21) -------------------------------------------
    /// Everything a playtest builds -- rung, fit, bays, hold, stores, a camp
    /// with a raised building, a blueprint, hands and felled trees -- goes to
    /// a temp file and comes back where it stood. Play mode, Sea.unity.
    /// Logs/SaveProbe.txt.
    public static void Save() => Call("SaveProbe");
    // --- modular shipyard (2026-09-24) ------------------------------------------
    /// Refits at the berth keep her identity, damage, hold and crew; refused
    /// refits change nothing; save/load carries the configuration (and an old
    /// save is the standard steamer); Long/Short/3-bay sea trials. Needs the
    /// steamer. Play mode, Sea.unity, ~2.5 min. Logs/ShipyardRefitProbe.txt.
    public static void ShipyardRefit() => Call("ShipyardRefitProbe");
    /// The shipyard UI path on the live steamer: refits through Astra's
    /// ShipyardDraft + ShipyardLiveBridge with cargo and crew aboard, stale
    /// baseline, the modal's input blocking (virtual keyboard/mouse), rollback
    /// on a failed save and on injected build faults, save/load. Needs the
    /// steamer. Play mode, Sea.unity, ~1 min. Logs/ShipyardUiProbe.txt.
    public static void ShipyardUi() => Call("ShipyardUiProbe");
    /// A rung costs goods that were sailed home: refused at sea, refused short,
    /// banked per resource, paid exactly, and `Apply` stays free for the rig.
    public static void Sink() => Call("SinkProbe");
    public static void VillageBuild() => Call("VillageProbe", "Build");
    public static void VillageShot() => Call("VillageProbe", "Shot");
    public static void Loop() => Call("LoopProbe");
    public static void Land() => Call("LandProbe");
    public static void LandSail() => Call("LandProbe", "Sail");
    public static void Truth() => Call("IslandTruthProbe");
    public static void Variety() => Call("IslandVariety");
    public static void Tune() => Call("IslandTuner");

    // Edit-mode island measurement. TuneIslands has no menu items and is
    // driven only through execute_script, which recompiles it in a fresh
    // assembly and dies on the reference set -- the exact failure this file
    // exists to route around. Through the project assembly it just works.
    public static void Isles() => CallEditor("TuneIslands", "Survey");
    public static void IsleFlats() => CallEditor("TuneIslands", "Flats");
    public static void IsleBeaches() => CallEditor("TuneIslands", "Beaches");
    public static void IsleWalk() => CallEditor("TuneIslands", "Walkable");
    public static void IslePush() => CallEditor("TuneIslands", "Execute");
    public static void IsleHomes() => CallEditor("TuneIslands", "HomeCandidates");
    public static void IsleMap() => CallEditor("TuneIslands", "Map");
    public static void IsleHomeMap() => CallEditor("TuneIslands", "HomeMap");
    public static void IsleMakeHome() => CallEditor("TuneIslands", "MakeHome");
    public static void IsleGate() => CallEditor("HeightProbe");
    public static void IsleHomeCheck() => CallEditor("TuneIslands", "HomeCheck");
    public static void IsleHomeIsle() => CallEditor("TuneIslands", "HomeIsle");
    public static void IsleHomePush() => CallEditor("TuneIslands", "HomePush");
    public static void IsleMesh() => CallEditor("TuneIslands", "MeshVsField");
    public static void DockFrame() => CallEditor("TuneIslands", "DockFraming");
    public static void Legible() => CallEditor("TuneIslands", "Legibility");
    public static void SailSheet() => Call("SailShots");
    public static void TerrainPerf() => Call("TerrainPerfProbe");
    public static void HitchStorm() => Call("HitchProbe", "Storm");
    public static void Hitch() => Call("HitchProbe");
    // The same complaint as Hitch, but SAILING and with every jump charged to
    // the term that moved. Hitch measures a ship at spawn and reports 0.27 m
    // against the 1.24 m PerfHUD shows while under way.
    public static void Step() => Call("StepProbe");
    public static void StepStorm() => Call("StepProbe", "Storm");
    // The water the complaint actually came from: ~3 km out, deep, envelope
    // near 1. Near home the envelope holds the sea to a third amplitude and
    // the probe finds nothing.
    public static void StepOffshore() => Call("StepProbe", "Offshore");
    public static void StepOffshoreStorm() => Call("StepProbe", "OffshoreStorm");
    public static void Smooth() => Call("SmoothProbe");
    public static void StepRebuildSweep() => Call("StepProbe", "RebuildSweep");
    public static void StepRebuildSweepUnsliced() => Call("StepProbe", "RebuildSweepUnsliced");

    // "If I sail the same route twice, do I meet the same sea?" PURE FIELD
    // MATH at chosen ocean times -- it does not sail, so nothing in it moves
    // with the frame rate and the run is valid on a remote editor throttled
    // to 10 fps. Play mode, Sea.unity. /tmp/seasick-voyagevariety.txt.
    public static void VoyageVariety() => Call("VoyageVarietyProbe");
    // The same measurement at crossings of 180 / 300 / 600 s, side by side.
    // The answer plainly depends on how long a crossing is, so one duration
    // cannot settle it.
    public static void VoyageVarietySweep() => Call("VoyageVarietyProbe", "Sweep");
    // The ocean's parity gate and its cost ledger. Both name SeaSick.Ocean
    // and Unity.Collections, which is the reference set the ad-hoc compile
    // does not have; through the project assembly they run. Play mode,
    // OceanLab for Divergence.
    public static void Divergence() => Call("DivergenceProbe");

    /// The parity gate with the shoreward band forced off and on. The band is
    /// added in BOTH twins and in OceanVerify, so the gate is what proves the
    /// three agree -- run the off leg first and check it reproduces the last
    /// recorded numbers before reading anything into the on leg.
    /// Voyage variety, one leg per change, so the two can be told apart.
    /// Baseline is the shipped expressions character for character.
    public static void VoyageBaseline() => VoyageWith(false, false);
    public static void VoyageCells()    => VoyageWith(true, false);
    public static void VoyageStorm()    => VoyageWith(false, true);
    public static void VoyageBoth()     => VoyageWith(true, true);

    static void VoyageWith(bool cells, bool storm)
    {
        SetStatic("SeaSick.Ocean.WeatherField", "EvolvingCells", cells);
        SetStatic("SeaSick.Ocean.RegionField", "WanderingStorm", storm);
        Debug.Log($"RunProbe: EvolvingCells={cells}  WanderingStorm={storm}");
        Call("VoyageVarietyProbe");
    }

    static void SetStatic(string type, string field, bool value)
    {
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            var ty = asm.GetType(type);
            if (ty == null) continue;
            var f = ty.GetField(field,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (f == null) continue;
            f.SetValue(null, value);
            return;
        }
        Debug.LogError("RunProbe: could not set " + type + "." + field);
    }

    /// The parity gate with the shoreward band forced off and on. The band is
    /// added in BOTH twins and in OceanVerify, so the gate is what proves the
    /// three agree -- run the off leg first and check it reproduces the last
    /// recorded numbers before reading anything into the on leg.
    /// The same voyage-variety measurement with the storm cells rigid (today)
    /// and evolving (the change). The OFF leg is the shipped expression
    /// character for character, so it is a true baseline.
    public static void VoyageRigid() => VoyageWith(false);
    public static void VoyageEvolving() => VoyageWith(true);

    static void VoyageWith(bool on)
    {
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType("SeaSick.Ocean.WeatherField");
            if (t == null) continue;
            var f = t.GetField("EvolvingCells",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (f == null) { Debug.LogError("RunProbe: no WeatherField.EvolvingCells"); return; }
            f.SetValue(null, on);
            Debug.Log("RunProbe: EvolvingCells = " + on);
            break;
        }
        Call("VoyageVarietyProbe");
    }

    public static void DivergenceNoBand() => DivergenceWith(false);
    public static void DivergenceBand() => DivergenceWith(true);

    static void DivergenceWith(bool on)
    {
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType("SeaSick.Ocean.RegionField");
            if (t == null) continue;
            var f = t.GetField("ShorewardEnabled",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (f == null) continue;
            f.SetValue(null, on);
            Debug.Log("RunProbe: ShorewardEnabled = " + on);
            break;
        }
        Call("DivergenceProbe");
    }
    public static void Perf() => Call("PerfProbe");
    public static void FFT() => CallEditor("FFTUnit");
    // The water shader's contact sheets. docs/DEV-TOOLS.md said to hand the
    // file to execute_script; it names SeaSick.Ocean and SeaSick.Ship and
    // dies there.
    public static void Strip() => Call("ShaderStrip");
    public static void StripSun() => Call("ShaderStrip", "SunAngles");
    public static void StripDay() => Call("ShaderStrip", "DaySheet");
    public static void WeatherAxes() => Call("WeatherSheet", "Axes");
    public static void WeatherAxesLively() => Call("WeatherSheet", "AxesLively");
    public static void WeatherPatches() => Call("WeatherSheet", "Patches");
    public static void WeatherPatchesLively() => Call("WeatherSheet", "PatchesLively");

    /// Edit-mode launcher for the hull lab: reimports the five FBX, measures
    /// every one against `WorldScale.Fleet` and rebuilds HullLab.unity.
    /// Goes through the project assembly for the same reason the probes do --
    /// handing `SetupHullLab.cs` straight to `execute_script` compiles a
    /// SECOND copy into a fresh assembly, which runs against different
    /// references and logs nothing you can read.
    public static void Lab() => CallEditor("SetupHullLab");

    // Computer mode: landscape orientation, a 1920x1080 standalone default,
    // and ChaseCamera.narrowestAspect pushed to 16:9 through SerializedObject.
    // Through the launcher because SetLandscapeMode names ChaseCamera and
    // UnityEditor.SceneManagement, and a fresh ad-hoc assembly has neither --
    // handed straight to execute_script it timed out three times and logged
    // nothing at all.
    public static void Landscape() => CallEditor("SetLandscapeMode");
    public static void ViewDesk() => CallEditor("DesktopGameView");
    public static void ViewPhone() => CallEditor("PortraitGameView");

    // --- time away (2026-09-27) --------------------------------------------
    // In Play with a game loaded: play `hours` of app-closed time through
    // every camp (AwayProgress), log wall time + per-camp summary, write a
    // scratch save (persistentDataPath/away-scratch.json), open the card.
    public static void AwayHours(float hours) => SeaSick.Save.AwayProgress.Simulate(hours);

    // --- save-folder test redirect (2026-09-26) ------------------------------
    // Set BEFORE `editor_play`, never after: `GameBoot` can read/migrate
    // through `SaveSlots` on the very first frames of Play, before any
    // `unity cmd eval` sent once play has started gets a turn. Backed by
    // `UnityEditor.SessionState` (`SaveSlots.EditorTestDirectory`), which
    // -- unlike a runtime static -- is already in place by then. Clear it
    // when done so the next ordinary session is not left pointed at a
    // scratch folder.
    public static void SetSaveDirOverride(string dir) => SeaSick.Save.SaveSlots.EditorTestDirectory = dir;
    public static void ClearSaveDirOverride() => SeaSick.Save.SaveSlots.EditorTestDirectory = null;
    public static string GetSaveDirOverride() => SeaSick.Save.SaveSlots.EditorTestDirectory;
    public static void Quality() => CallEditor("ReadQuality");
    // Saves the OPEN scene in place. Coplay's own save_scene takes a NAME
    // and writes Assets/[name].unity, so calling it on Sea.unity forks a
    // copy at the project root instead of saving the scene.
    //
    // A PLAIN comment, and the angle brackets are gone on purpose: a ///
    // comment is parsed as XML, an unknown tag is a malformed-XML warning,
    // and `execute_script` cannot render a Roslyn diagnostic -- it fails
    // with a resource-loading exception instead, which says nothing about
    // the line that caused it.
    public static void SaveOpenScene() => CallEditor("LadderCheck");
    public static void Rig() => CallEditor("LadderCheck", "Rig");

    // --- wear a progression hull, and sail her -----------------------------
    // Edit mode. Run one of these, then press Play.
    public static void T1() => WearHull(1);
    public static void T2() => WearHull(2);
    public static void T3() => WearHull(3);
    public static void T4() => WearHull(4);
    public static void T5() => WearHull(5);

    static void WearHull(int tier)
    {
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType("SetupFleetShip");
            if (t == null) continue;
            var m = t.GetMethod("Wear", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (m == null) continue;
            Debug.Log("Wear T" + tier + ":\n" + m.Invoke(null, new object[] { tier }));
            return;
        }
        Debug.LogError("RunProbe: could not find SetupFleetShip.Wear");
    }

    /// What the PLAYER sees, rendered from Camera.main. Every other probe
    /// here photographs the scene view, which is not the shot that is broken
    /// when somebody says they cannot see their boat.
    ///
    /// Only UnityEngine types, so this survives being recompiled on its own
    /// by execute_script.
    public static void Shot() => Shoot(900, 1500);        // the real game: PORTRAIT
    public static void ShotWide() => Shoot(1280, 720);    // what the editor shows you

    /// The aspect is not a detail. The editor Game view is landscape and
    /// the game is not, so a boat that sits comfortably in a 16:9 frame
    /// can be off the side of the phone -- this project has been caught
    /// by that before. Default to portrait, because portrait is the game.
    static void Shoot(int w, int h)
    {
        var cam = Camera.main;
        if (cam == null) { Debug.LogError("RunProbe.Shot: no Camera.main"); return; }
        var rt = new RenderTexture(w, h, 24) { antiAliasing = 2 };
        var prev = cam.targetTexture;
        var prevActive = RenderTexture.active;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        cam.targetTexture = prev;
        RenderTexture.active = prevActive;

        string dir = System.IO.Path.Combine(
            System.IO.Directory.GetParent(Application.dataPath).FullName, "Temp", "IslandMaps");
        System.IO.Directory.CreateDirectory(dir);
        string path = System.IO.Path.Combine(dir,
            w < h ? "gameview_portrait.png" : "gameview_wide.png");
        System.IO.File.WriteAllBytes(path, ImageConversion.EncodeToPNG(tex));
        Object.DestroyImmediate(tex);
        rt.Release(); Object.DestroyImmediate(rt);

        var ship = GameObject.Find("PlayerShip");
        string where = "no PlayerShip";
        if (ship != null)
        {
            var v = cam.WorldToViewportPoint(ship.transform.position);
            bool onScreen = v.z > 0f && v.x > 0.02f && v.x < 0.98f && v.y > 0.02f && v.y < 0.98f;
            where = "ship " + Vector3.Distance(cam.transform.position, ship.transform.position)
                        .ToString("F0") + " m from camera, viewport " + v.ToString("F2")
                  + (v.z <= 0f ? "  -- BEHIND THE CAMERA"
                              : onScreen ? "  -- in frame" : "  -- OFF FRAME");
        }
        Debug.Log("RunProbe.Shot " + w + "x" + h + ": " + path + "\n  " + where);
    }

    static void CallEditorStr(string type, string method)
    {
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType(type);
            if (t == null) continue;
            var m = t.GetMethod(method, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (m == null) continue;
            Debug.Log(type + "." + method + ":\n" + m.Invoke(null, null));
            return;
        }
        Debug.LogError("RunProbe: could not find " + type + "." + method);
    }

    static void CallEditor(string type, string method = "Execute")
    {
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType(type);
            if (t == null) continue;
            var m = t.GetMethod(method, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (m == null) continue;
            // Unwrap the reflection wrapper: Coplay hands back only the outer
            // "Exception has been thrown by the target of an invocation" and
            // the real one never reaches the log.
            try
            {
                var r = m.Invoke(null, null);
                Debug.Log(type + "." + method + ":\n" + r);
            }
            catch (System.Reflection.TargetInvocationException e)
            {
                Debug.LogError(type + "." + method + " threw: " + e.InnerException);
                throw;
            }
            return;
        }
        Debug.LogError("RunProbe: could not find " + type + "." + method);
    }

    /// The New / Continue overlay freezes the game until somebody picks, and
    /// a probe is not somebody. Every play-mode launcher goes through here,
    /// so every probe finds the world running, on a fresh voyage, with the
    /// player's own save file untouched (a skipped boot never autosaves).
    static void SkipBoot()
    {
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType("SeaSick.Save.GameBoot");
            if (t == null) continue;
            var m = t.GetMethod("Skip",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (m != null) m.Invoke(null, null);
            return;
        }
    }

    static void Call(string type, string method = "Execute")
    {
        if (!Application.isPlaying) { Debug.LogError("RunProbe: not in play mode"); return; }
        SkipBoot();
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType(type);
            if (t == null) continue;
            var m = t.GetMethod(method, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (m == null) continue;
            m.Invoke(null, null);
            return;
        }
        Debug.LogError($"RunProbe: could not find {type}.{method}");
    }
}
