#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// The surf term's three values, pushed onto OceanSurface.mat explicitly.
///
/// Same reason as every other Tune script here: **a .mat snapshots a shader's
/// property defaults and never follows them again.** New properties are safe
/// on the day they are added -- they are absent from the asset, so the
/// material takes the shader's default -- and they stop being safe the moment
/// anyone edits the material in the inspector or the defaults in the shader
/// move. Writing them once, and reading them back off the asset, is what makes
/// the difference between "the shader says 0.95" and "the game is using 0.95".
///
/// SeaSick/Ocean/Tune Surf, or execute_script.
public static class TuneSurf
{
    const float Strength = 0.95f;
    /// Where the breaker ramp starts, as a fraction of RegionField's
    /// breakFraction. The ratio of local wave height to depth can only reach
    /// breakFraction (that is what the depth cap enforces), so this is
    /// "how nearly the cap has to be binding before there is white water".
    /// 0.78 puts the ramp over the last fifth of it.
    const float BreakFrac = 0.78f;
    /// Metres of depth over which the swash band fades out. A count of metres
    /// on purpose, and the only one here: the swash is set by the beach, not
    /// by the sea.
    const float SwashDepth = 3f;

    [MenuItem("SeaSick/Ocean/Tune Surf")]
    public static void Execute()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/_Project/Materials/OceanSurface.mat");
        if (mat == null) { Debug.LogError("TuneSurf: OceanSurface.mat missing"); return; }
        if (mat.shader == null || mat.shader.name != "SeaSick/Ocean")
        { Debug.LogError("TuneSurf: OceanSurface.mat is not on SeaSick/Ocean (run SetupOceanShading)"); return; }

        mat.SetFloat("_SurfStrength", Strength);
        mat.SetFloat("_SurfBreakFrac", BreakFrac);
        mat.SetFloat("_SurfSwashDepth", SwashDepth);
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();

        // Read back off a freshly loaded asset -- the point is to prove the
        // material holds these, not that we just wrote them.
        AssetDatabase.Refresh();
        var check = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/_Project/Materials/OceanSurface.mat");
        Debug.Log($"TuneSurf, read back from OceanSurface.mat:\n" +
                  $"  _SurfStrength    {check.GetFloat("_SurfStrength")}\n" +
                  $"  _SurfBreakFrac   {check.GetFloat("_SurfBreakFrac")}\n" +
                  $"  _SurfSwashDepth  {check.GetFloat("_SurfSwashDepth")}");
    }
}
#endif
