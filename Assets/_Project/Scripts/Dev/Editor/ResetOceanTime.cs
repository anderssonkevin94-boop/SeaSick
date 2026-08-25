using UnityEngine;
using SeaSick.Ocean;

/// Undo whatever the last probe left pinned. OceanTime.Paused, OceanTime.Scale
/// and a forced sea state are all STATIC and survive leaving play mode, so a
/// probe that stops early — or that a human stops early — silently poisons
/// every run after it. DivergenceProbe reported "readback never caught up" on
/// every timestamp with the stamp frozen, purely because StormDeckShot had
/// left the clock paused. Run this between probes.
public static class ResetOceanTime
{
    public static string Execute()
    {
        bool wasPaused = OceanTime.Paused;
        double scale = OceanTime.Scale;
        OceanTime.Paused = false;
        OceanTime.Scale = 1.0;

        string sea = "no controller";
        var ctrl = Object.FindFirstObjectByType<SeaStateController>();
        if (ctrl != null) { ctrl.ReleaseForce(); sea = "severity released"; }

        return "OceanTime.Paused was " + wasPaused + " (now false), Scale was "
            + scale + " (now 1); " + sea;
    }
}
