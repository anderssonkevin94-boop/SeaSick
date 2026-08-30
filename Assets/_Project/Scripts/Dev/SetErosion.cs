using UnityEngine;
using SeaSick.Terrain;

/// Flip erosion off (or back on) and rebuild, so the perf probe can be run
/// as a true A/B rather than against a remembered number.
public static class SetErosion
{
    public static void Off() => Apply(0f);
    public static void On() => Apply(2f);

    static void Apply(float e)
    {
        var streamer = Object.FindAnyObjectByType<TerrainStreamer>();
        if (streamer == null || streamer.settings == null) { Debug.LogError("SetErosion: no streamer"); return; }
        streamer.settings.erosion = e;
        streamer.MarkDirty();
        Debug.Log($"SetErosion: erosion = {e}");
    }
}
