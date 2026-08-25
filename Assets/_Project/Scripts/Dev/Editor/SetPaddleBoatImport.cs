using UnityEditor;
using UnityEngine;

/// Import settings for the paddle boat FBX. The model is authored at 7.1 m
/// overall; the game wants a boat that reads correctly against 1.6 m crew,
/// which means waist-to-chest-high railings rather than knee-high ones. At
/// 1.7 the railing lands at 1.04 m and she comes out 12.1 m overall.
public static class SetPaddleBoatImport
{
    const string Path = "Assets/_Project/Art/Ship/paddle_boat.fbx";
    /// Owned by SetupPaddleBoat so the boat has exactly one size constant.
    static float Scale { get { return SetupPaddleBoat.Scale; } }

    public static string Execute()
    {
        var mi = AssetImporter.GetAtPath(Path) as ModelImporter;
        if (mi == null) return "no ModelImporter at " + Path;
        mi.useFileScale = true;
        mi.globalScale = Scale;
        mi.importNormals = ModelImporterNormals.Import;
        mi.importCameras = false;
        mi.importLights = false;
        mi.importAnimation = false;
        // Readable: SetupPaddleBoat samples the deck mesh to stand the crew
        // and guns on the actual planking. The deck has camber and sheer and
        // spans 0.67 m in height, so one deck constant floats them amidships.
        mi.isReadable = true;
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
        mi.meshCompression = ModelImporterMeshCompression.Off;
        EditorUtility.SetDirty(mi);
        mi.SaveAndReimport();
        return "paddle_boat import scale -> " + Scale;
    }
}
