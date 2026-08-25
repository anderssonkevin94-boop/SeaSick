using UnityEditor;
using UnityEngine;

/// Force the ocean shader through the compiler. check_compile_errors is C#
/// only — a broken shader reports clean and then renders magenta — so this
/// exists to make the shader actually recompile on demand, after which the
/// editor log is the place to look.
public static class ReimportOceanShader
{
    public static string Execute()
    {
        const string path = "Assets/_Project/Art/Shaders/Ocean/Ocean.shader";
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var sh = AssetDatabase.LoadAssetAtPath<Shader>(path);
        if (sh == null) return "shader not found at " + path;
        return "reimported " + path + "; isSupported=" + sh.isSupported;
    }
}
