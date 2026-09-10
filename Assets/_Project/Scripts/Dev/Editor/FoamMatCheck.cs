using UnityEditor;
using UnityEngine;

/// Read the ocean material's foam values back OFF THE MATERIAL at runtime,
/// rather than trusting what the shader declares.
///
/// This exists because of a fault this project has hit before: a `.mat`
/// snapshots a shader property's default when the property is created, and a
/// later change to that default never reaches it — even when the property does
/// not appear in the `.mat` text at all. `_StormDeep` read a stale colour for a
/// whole session that way. When a shading term measures as doing nothing, the
/// first question is whether the number you think you set ever arrived.
public static class FoamMatCheck
{
    static readonly string[] Floats =
    {
        "_FoamJThreshold", "_FoamSnap", "_FoamCrestGain", "_FoamRelief",
        "_FoamSparkle", "_FoamNoiseScale", "_FoamBandLift", "_FoamLiftFar",
        "_FoamTrailFloor", "_FoamTrailGain", "_SurfStrength",
    };

    public static string Execute()
    {
        var sb = new System.Text.StringBuilder();
        Material mat = null;
        foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            if (mr.sharedMaterial != null && mr.sharedMaterial.shader != null
                && mr.sharedMaterial.shader.name == "SeaSick/Ocean")
            { mat = mr.sharedMaterial; break; }
        if (mat == null)
        {
            mat = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/_Project/Materials/OceanSurface.mat");
            sb.AppendLine("(no ocean renderer in the scene — read off the asset)");
        }
        if (mat == null) return "no ocean material found";

        sb.AppendLine("material: " + mat.name + "   shader: " + mat.shader.name);
        foreach (var n in Floats)
            sb.AppendLine($"  {n,-18} hasProperty={mat.HasProperty(n),-5} "
                        + $"value={(mat.HasProperty(n) ? mat.GetFloat(n) : float.NaN):F3}");
        var s = sb.ToString();
        Debug.Log("FoamMatCheck:\n" + s);
        return s;
    }
}
