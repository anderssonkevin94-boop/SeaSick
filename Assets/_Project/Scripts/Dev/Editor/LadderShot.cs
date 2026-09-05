using UnityEditor;
using UnityEngine;
using SeaSick.Ship;

/// Put a chosen rung under the camera and grab the Game view. IMGUI and
/// runtime-spawned visuals never appear in scene-object captures, so a
/// screenshot is the only honest way to look at what the player sees.
public static class LadderShot
{
    public const string Path = "/tmp/seasick-rung.png";

    public static void Execute() => Shoot(0);

    [MenuItem("SeaSick/Shipyard/Shot — skiff")]      public static void Skiff() => Shoot(0);
    [MenuItem("SeaSick/Shipyard/Shot — brig")]       public static void Brig() => Shoot(12);
    [MenuItem("SeaSick/Shipyard/Shot — three-decker")] public static void Big() => Shoot(19);

    static void Shoot(int node)
    {
        if (!Application.isPlaying) { Debug.LogError("play mode only"); return; }
        var yard = Object.FindFirstObjectByType<Shipyard>();
        if (yard == null) { Debug.LogError("no Shipyard"); return; }
        yard.Apply(node);
        var n = yard.Node;

        // Stand off her beam at a distance that frames her whatever her size,
        // and a little above the rail so the deck reads.
        //
        // ChaseCamera re-posts the camera every LateUpdate, so a position set
        // here is overwritten before the frame is drawn — the first attempt
        // photographed the island the chase camera was already looking at.
        // Every IMGUI panel off. They are drawn over the whole screen and the
        // yard panel alone covers half of it, which is fine to play with and
        // useless to judge a hull by.
        foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(
                     FindObjectsSortMode.None))
        {
            if (mb == null) continue;
            var t = mb.GetType().Name;
            if (t == "ShipyardPanel" || t == "StatusHUD" || t == "MiniMap"
                || t == "NavigationAid" || t == "HelmInput" || t == "GunneryReadout"
                || t == "TargetHUD" || t == "WindArrow" || t == "SmoothnessMeter")
                mb.enabled = false;
        }

        var cam = Camera.main;
        var chase = cam != null ? cam.GetComponent<SeaSick.CameraRig.ChaseCamera>() : null;
        if (chase != null) chase.enabled = false;
        if (cam != null)
        {
            Vector3 c = yard.transform.position;
            float d = n.length * 1.15f;
            cam.transform.position = c + yard.transform.right * -d
                                       + Vector3.up * (n.RailY + n.length * 0.22f);
            cam.transform.LookAt(c + Vector3.up * n.RailY * 0.4f);
        }
        ScreenCapture.CaptureScreenshot(Path, 1);
        Debug.Log($"LadderShot: {n.label} ({n.length:F1} m, {n.masts} masts) -> {Path}");
    }
}
