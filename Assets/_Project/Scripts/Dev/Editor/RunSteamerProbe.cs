using UnityEngine;

// Launchers for the paddle steamer's sea trials (`Scripts/Dev/SteamerProbe.cs`).
//
// Its own file rather than more entries in RunProbe, and for RunProbe's own
// reason: `execute_script` compiles what it is handed into a FRESH assembly
// without the project's reference set, so this names no project type at all
// and reaches the probe by reflection in the already-compiled assembly.
//
// One mode per play session. Sea.unity, steamer selected BEFORE Play:
//   RunSteamerProbe.Select()   (edit mode)  ->  Play  ->  RunSteamerProbe.Float()
// Each mode writes /tmp/seasick-steamer-MODE.txt and ends it with DONE.
//
//   Float   ~40 s    on her marks: draft, trim, heel, wheel dips, book vs tables
//   Decay   ~85 s    roll and pitch kick: period and damping ratio
//   Drive   ~100 s   0 to full ahead, coast-down, full astern
//   Turn    ~120 s   the pivot at rest, then the turn at full ahead
//   Sway    ~210 s   local Hs 3.5, with / beam-on to / into the swell
//   Storm   ~210 s   the same at local Hs 9, green water in freeboards
// Game seconds: an unfocused editor takes longer on the wall clock.
public static class RunSteamerProbe
{
    public static void Float() { Call("SteamerProbe", "Float"); }
    public static void Decay() { Call("SteamerProbe", "Decay"); }
    public static void Drive() { Call("SteamerProbe", "Drive"); }
    public static void Turn() { Call("SteamerProbe", "Turn"); }
    public static void Sway() { Call("SteamerProbe", "Sway"); }
    public static void Storm() { Call("SteamerProbe", "Storm"); }
    public static void Execute() { Call("SteamerProbe", "Float"); }
    /// Not a trial: five look shots in the game's own light, for the eye.
    public static void Shot() { Call("SteamerShot", "Execute"); }

    // The switch SteamerBootstrap reads at play start. The key is spelled out
    // because this file must not name SteamerBootstrap; Status() prints what
    // the project's own constant says so a rename cannot hide.
    const string PrefKey = "SeaSick.Steamer";

    public static void Select() { SetSelected(1); }
    public static void Deselect() { SetSelected(0); }

    public static void Status()
    {
        string theirs = "not found";
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType("SeaSick.Steamer.SteamerBootstrap");
            if (t == null) continue;
            var f = t.GetField("PrefKey",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (f != null) theirs = (string)f.GetValue(null);
            break;
        }
        Debug.Log("RunSteamerProbe: PlayerPrefs[" + PrefKey + "] = " + PlayerPrefs.GetInt(PrefKey, 0)
            + "   (SteamerBootstrap.PrefKey is \"" + theirs + "\")   playing " + Application.isPlaying);
    }

    static void SetSelected(int value)
    {
        PlayerPrefs.SetInt(PrefKey, value);
        PlayerPrefs.Save();
        Debug.Log("RunSteamerProbe: PlayerPrefs[" + PrefKey + "] = " + value
            + (Application.isPlaying ? "   -- takes effect on the NEXT Play; the conversion only happens at play start" : ""));
    }

    static void Call(string type, string method)
    {
        if (!Application.isPlaying) { Debug.LogError("RunSteamerProbe: not in play mode"); return; }
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType(type);
            if (t == null) continue;
            var m = t.GetMethod(method,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (m == null) continue;
            try
            {
                m.Invoke(null, null);
            }
            catch (System.Reflection.TargetInvocationException e)
            {
                // Unwrapped: the bridge hands back only the outer reflection
                // wrapper and the real exception never reaches the log.
                Debug.LogError("RunSteamerProbe: " + type + "." + method + " threw: " + e.InnerException);
                throw;
            }
            return;
        }
        Debug.LogError("RunSteamerProbe: could not find " + type + "." + method);
    }
}
