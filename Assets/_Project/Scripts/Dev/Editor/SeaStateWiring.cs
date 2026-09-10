using UnityEditor;
using UnityEngine;
using SeaSick.Ocean;

/// **Which sea-state assets is the controller actually blending?**
///
/// `SeaStateController` holds its four anchors as serialized references, and
/// every probe and tool in this project reaches them through
/// `Resources.Load("Ocean/SeaState_*")`. Those are only the same objects if the
/// scene happens to be wired to the Resources copies — and nothing enforces it.
/// If they diverge, a tool edits one set and the game blends the other, and
/// every symptom is "the number I set has no effect", which is indistinguishable
/// from a broken feature.
///
/// It prints the asset PATH of each anchor next to what `Resources.Load` finds,
/// and the live blend's own values, so the three can be compared rather than
/// assumed equal.
public static class SeaStateWiring
{
    static readonly string[] Names =
        { "calm", "normal", "rough", "stormy" };

    public static string Execute()
    {
        var sb = new System.Text.StringBuilder();
        var ssc = Object.FindAnyObjectByType<SeaStateController>(FindObjectsInactive.Include);
        if (ssc == null) return "no SeaStateController in the scene";

        var so = new SerializedObject(ssc);
        foreach (var n in Names)
        {
            var p = so.FindProperty(n);
            var asset = p != null ? p.objectReferenceValue as OceanSpectrumSettings : null;
            string path = asset != null ? AssetDatabase.GetAssetPath(asset) : "(none)";
            var res = Resources.Load<OceanSpectrumSettings>(
                "Ocean/SeaState_" + char.ToUpper(n[0]) + n.Substring(1));
            string resPath = res != null ? AssetDatabase.GetAssetPath(res) : "(not in Resources)";
            bool same = asset != null && res != null && asset == res;
            sb.AppendLine($"{n,-7} wired: {path}");
            sb.AppendLine($"        Resources: {resPath}   SAME OBJECT: {same}");
            if (asset != null)
                sb.AppendLine($"        foamThreshold {asset.foamThreshold:F2}  "
                            + $"injection {asset.foamInjection:F2}  "
                            + $"halflife {asset.foamHalflife:F1}  "
                            + $"crestSharpen {asset.crestSharpen}");
        }

        var ocean = OceanRenderer.Instance;
        var live = ocean != null ? ocean.Settings : null;
        sb.AppendLine(live == null
            ? "live blend: none (not in play mode?)"
            : $"live blend '{live.name}': nominalHs {live.nominalHs:F1}  "
            + $"foamThreshold {live.foamThreshold:F2}  injection {live.foamInjection:F2}  "
            + $"halflife {live.foamHalflife:F1}  crestSharpen {live.crestSharpen}");

        var scene = new SerializedObject(Object.FindAnyObjectByType<OceanRenderer>(
            FindObjectsInactive.Include));
        var sp = scene.FindProperty("settings");
        var sa = sp != null ? sp.objectReferenceValue as OceanSpectrumSettings : null;
        sb.AppendLine("OceanRenderer's SERIALIZED settings asset: "
                    + (sa != null ? AssetDatabase.GetAssetPath(sa) : "(none)"));

        var s = sb.ToString();
        Debug.Log("SeaStateWiring:\n" + s);
        return s;
    }
}
