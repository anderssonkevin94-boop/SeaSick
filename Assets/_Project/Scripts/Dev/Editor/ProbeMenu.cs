using UnityEditor;
using UnityEngine;

/// Clickable entries for the play-mode probes.
///
/// They had none: every probe was driven through Coplay's `execute_script`,
/// which works fine for an agent and leaves Kevin with nothing to press. A
/// probe nobody can run by hand is a probe that only gets run when someone
/// else is at the keyboard.
///
/// Kept OUT of RunProbe.cs on purpose — that file's header explains that it is
/// deliberately reference-light because `execute_script` recompiles it into a
/// fresh assembly, and `using UnityEditor` there would put that at risk.
///
/// Nearly all of these need PLAY MODE, so each one says so rather than failing
/// quietly into the console.
public static class ProbeMenu
{
    const string Root = "SeaSick/Probes/";

    [MenuItem(Root + "Ocean/Step — offshore (the water-stepping bug)", false, 10)]
    static void StepOffshore() => Play(() => RunProbe.StepOffshore(),
        "Sails a 10 m/s track in deep water 3 km out and charges every surface\n"
        + "jump to the term that moved. Takes ~100 s. Writes /tmp/seasick-step.txt.\n\n"
        + "IMPORTANT: click into the Game view first and leave it focused, or the\n"
        + "editor throttles play mode to 10 fps and the run is worthless (it will\n"
        + "say so in its own output).");

    [MenuItem(Root + "Ocean/Smooth — DO THE WAVES STUTTER? (start here)", false, 1)]
    static void Smooth() => Play(() => RunProbe.Smooth(),
        "Measures the PICTURE: how much the water changes frame to frame, against\n"
        + "a land+sky control in the same frames. If the water is lumpy and the\n"
        + "control is steady, the ocean is stepping and the camera is not.\n\n"
        + "Takes ~10 s. Writes /tmp/seasick-smooth.txt.\n\n"
        + "THIS ONE ONLY WORKS AT 60 fps. Click into the Game view and leave it\n"
        + "frontmost. If the editor throttles to 10 fps the probe says so at the\n"
        + "top of its own output and the run means nothing.");

    [MenuItem(Root + "Ocean/Rebuild-rate sweep (sliced vs whole)", false, 2)]
    static void Sweep() => Play(() => RunProbe.StepRebuildSweep(),
        "Compares the size of the surface step against the spectrum rebuild rate.\n"
        + "Takes ~140 s. Writes /tmp/seasick-step.txt.");

    [MenuItem(Root + "Ocean/Step — offshore, pinned STORM", false, 11)]
    static void StepOffshoreStorm() => Play(() => RunProbe.StepOffshoreStorm(),
        "As above, with the sea pinned to full storm.");

    [MenuItem(Root + "Ocean/Read islands the ocean bound", false, 12)]
    static void Islands() => Play(() => Run("ReadIslands"),
        "How many islands the world has vs how many the ocean is sheltering\n"
        + "water with. Writes /tmp/seasick-islands.txt.");

    [MenuItem(Root + "Ocean/Divergence — the parity gate", false, 13)]
    static void Divergence() => Play(() => RunProbe.Divergence(),
        "CPU sampler vs the rendered surface. Open OceanLab first.\n"
        + "Writes /tmp/seasick-divergence.txt.");

    [MenuItem(Root + "Ocean/Hitch — frame vs sampler clock", false, 14)]
    static void Hitch() => Play(() => RunProbe.HitchStorm(),
        "Writes /tmp/seasick-hitch.txt.");

    [MenuItem(Root + "Open the probe output folder", false, 40)]
    static void OpenOutputs() => EditorUtility.RevealInFinder("/tmp/seasick-step.txt");

    [MenuItem(Root + "Force script refresh (Auto Refresh is off)", false, 41)]
    static void Refresh() => ForceRefresh.Execute();

    static void Run(string type)
    {
        var t = System.Type.GetType(type);
        if (t == null) { Debug.LogError($"ProbeMenu: no type '{type}'"); return; }
        t.GetMethod("Execute", System.Reflection.BindingFlags.Public
                             | System.Reflection.BindingFlags.Static)?.Invoke(null, null);
    }

    static void Play(System.Action go, string what)
    {
        if (!Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Press Play first",
                "This probe runs in play mode.\n\n" + what, "OK");
            return;
        }
        Debug.Log("ProbeMenu: started. " + what);
        go();
    }
}
