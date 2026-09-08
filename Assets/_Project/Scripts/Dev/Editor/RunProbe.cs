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
    public static void Look() => Call("IslandLook");
    public static void Peek() => Call("ChunkPeek");
    public static void Drive() => Call("DriveProbe");
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
    public static void Shoal() => Call("ShoalShot");
    public static void Horizon() => Call("HorizonShot");
    public static void Stand() => Call("DeckStandProbe");
    public static void Wood() => Call("WoodProbe");
    public static void Harbour() => Call("HarbourProbe");
    public static void Village() => Call("VillageProbe");
    public static void VillageBuild() => Call("VillageProbe", "Build");
    public static void VillageShot() => Call("VillageProbe", "Shot");
    public static void Loop() => Call("LoopProbe");
    public static void Land() => Call("LandProbe");
    public static void LandSail() => Call("LandProbe", "Sail");
    public static void Truth() => Call("IslandTruthProbe");
    public static void Variety() => Call("IslandVariety");
    public static void Tune() => Call("IslandTuner");
    public static void TerrainPerf() => Call("TerrainPerfProbe");
    public static void HitchStorm() => Call("HitchProbe", "Storm");
    public static void Hitch() => Call("HitchProbe");
    public static void WeatherAxes() => Call("WeatherSheet", "Axes");
    public static void WeatherAxesLively() => Call("WeatherSheet", "AxesLively");
    public static void WeatherPatches() => Call("WeatherSheet", "Patches");
    public static void WeatherPatchesLively() => Call("WeatherSheet", "PatchesLively");

    /// Edit-mode, not a probe: pushes the paddle steamer's measured mass and
    /// the fleet damping law onto the ship in the open scene. Separate from
    /// the full `SetupPaddleBoat.Execute()` cutover on purpose.
    public static void Remass()
    {
        var ship = GameObject.Find("PlayerShip");
        if (ship == null) { Debug.LogError("RunProbe.Remass: no PlayerShip in the open scene"); return; }
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType("SetupPaddleBoat");
            if (t == null) continue;
            var m = t.GetMethod("PushPhysics", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (m == null) continue;
            Debug.Log("Remass:\n" + m.Invoke(null, new object[] { ship }));
            return;
        }
        Debug.LogError("RunProbe.Remass: could not find SetupPaddleBoat.PushPhysics");
    }

    /// Edit-mode launcher for the hull lab: reimports the five FBX, measures
    /// every one against `WorldScale.Fleet` and rebuilds HullLab.unity.
    /// Goes through the project assembly for the same reason the probes do --
    /// handing `SetupHullLab.cs` straight to `execute_script` compiles a
    /// SECOND copy into a fresh assembly, which runs against different
    /// references and logs nothing you can read.
    public static void Lab() => CallEditor("SetupHullLab");
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
    public static void Steamer() => CallEditorStr("SetupFleetShip", "Restore");

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
            var r = m.Invoke(null, null);
            Debug.Log(type + "." + method + ":\n" + r);
            return;
        }
        Debug.LogError("RunProbe: could not find " + type + "." + method);
    }

    static void Call(string type, string method = "Execute")
    {
        if (!Application.isPlaying) { Debug.LogError("RunProbe: not in play mode"); return; }
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
