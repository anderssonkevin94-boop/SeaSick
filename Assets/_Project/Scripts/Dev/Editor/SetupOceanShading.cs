using UnityEditor;
using UnityEngine;

/// Points the game's ocean material at the real surface shader (the material
/// asset survives; only its shader and palette move). Re-run after palette
/// changes — a .mat snapshots property defaults at assignment time and never
/// follows the shader's defaults afterwards (the recorded material trap).
public static class SetupOceanShading
{
    public static string Execute()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/_Project/Materials/OceanSurface.mat");
        if (mat == null) return "FAIL: OceanSurface.mat missing (run SetupOceanScene)";
        var shader = Shader.Find("SeaSick/Ocean");
        if (shader == null) return "FAIL: SeaSick/Ocean shader not found";
        mat.shader = shader;

        mat.SetColor("_DeepColor", new Color(0.028f, 0.14f, 0.21f));
        mat.SetColor("_ShallowColor", new Color(0.09f, 0.42f, 0.45f));
        mat.SetColor("_SubsurfaceColor", new Color(0.10f, 0.65f, 0.45f));
        mat.SetColor("_StormDeep", new Color(0.058f, 0.086f, 0.098f));
        mat.SetColor("_StormShallow", new Color(0.10f, 0.14f, 0.15f));
        mat.SetColor("_StormSubsurface", new Color(0.16f, 0.30f, 0.24f));
        mat.SetColor("_FoamColor", new Color(0.92f, 0.96f, 0.97f));

        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        return "OceanSurface.mat -> SeaSick/Ocean, palette pushed";
    }
}
