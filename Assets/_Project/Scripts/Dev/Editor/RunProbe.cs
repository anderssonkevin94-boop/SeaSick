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
    public static void HitchStorm() => Call("HitchProbe", "Storm");
    public static void Hitch() => Call("HitchProbe");
    public static void WeatherAxes() => Call("WeatherSheet", "Axes");
    public static void WeatherAxesLively() => Call("WeatherSheet", "AxesLively");
    public static void WeatherPatches() => Call("WeatherSheet", "Patches");
    public static void WeatherPatchesLively() => Call("WeatherSheet", "PatchesLively");

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
