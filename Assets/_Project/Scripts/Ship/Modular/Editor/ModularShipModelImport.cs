using UnityEditor;

namespace SeaSick.Ship.Modular
{
    /// Applies the modular-ship importer settings the FIRST time an FBX under
    /// Resources/ShipModules/Meshes is imported, so a fresh delivery from
    /// Astra is right without anyone running a menu item. Touches no other
    /// folder.
    public class ModularShipModelImport : AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(ModularShipTestSceneSetup.MeshFolder + "/")) return;
            if (assetImporter is ModelImporter mi) Apply(mi);
        }

        /// Returns true if anything changed.
        public static bool Apply(ModelImporter mi)
        {
            bool changed = false;
            void Set<T>(T current, T wanted, System.Action<T> set)
            {
                if (!Equals(current, wanted)) { set(wanted); changed = true; }
            }
            Set(mi.globalScale, 1f, v => mi.globalScale = v);
            Set(mi.useFileScale, true, v => mi.useFileScale = v);
            Set(mi.materialImportMode, ModelImporterMaterialImportMode.None, v => mi.materialImportMode = v);
            Set(mi.importCameras, false, v => mi.importCameras = v);
            Set(mi.importLights, false, v => mi.importLights = v);
            Set(mi.importAnimation, false, v => mi.importAnimation = v);
            Set(mi.animationType, ModelImporterAnimationType.None, v => mi.animationType = v);
            Set(mi.importBlendShapes, false, v => mi.importBlendShapes = v);
            Set(mi.importNormals, ModelImporterNormals.Import, v => mi.importNormals = v);
            Set(mi.isReadable, false, v => mi.isReadable = v);
            return changed;
        }
    }
}
