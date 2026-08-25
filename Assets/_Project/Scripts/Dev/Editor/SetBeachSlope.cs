using UnityEditor;
using UnityEngine;
using SeaSick.Terrain;

/// One-off push of WorldSettings.beachMaxSlope into the asset. Changing the
/// C# default does nothing to an asset that already has the field, so the
/// value has to be written here. Edit the constant and run it.
///
/// The number is chosen off BeachProbe's distribution, not by feel-and-repeat:
/// at 0.70 93 % of all shore was landable, which is why every island felt like
/// one long beach.
public static class SetBeachSlope
{
    const float Slope = 0.50f;

    public static string Execute()
    {
        var path = "Assets/_Project/Settings/Terrain/WorldSettings.asset";
        var world = AssetDatabase.LoadAssetAtPath<WorldSettings>(path);
        if (world == null) return "no WorldSettings at " + path;
        float was = world.beachMaxSlope;
        world.beachMaxSlope = Slope;
        EditorUtility.SetDirty(world);
        AssetDatabase.SaveAssets();
        return "beachMaxSlope " + was.ToString("F2") + " -> " + world.beachMaxSlope.ToString("F2");
    }
}
