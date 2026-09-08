using UnityEditor;

namespace SeaSick.Terrain.EditorTools
{
    /// Import settings for the Blender scenery kit, applied on import so
    /// nobody has to remember to tick them.
    ///
    /// `SceneryKit` reads the vertices, normals, colours and triangles out of
    /// the meshes at runtime and stamps them into the welded island meshes,
    /// so the meshes MUST be readable -- with the default (unreadable) import
    /// `mesh.vertices` throws and every island comes out bare. Materials are
    /// not imported: the kit carries its colour in the vertices and is drawn
    /// with the terrain's own vertex-colour shader. Vertices are not welded,
    /// because the flat facets are the style.
    public class FloraImport : AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if (!assetPath.Replace('\\', '/').Contains("/Resources/Flora/")) return;
            var mi = (ModelImporter)assetImporter;
            mi.isReadable = true;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.importNormals = ModelImporterNormals.Import;
            mi.weldVertices = false;
            mi.importBlendShapes = false;
            mi.importCameras = false;
            mi.importLights = false;
            mi.useFileScale = true;
            mi.globalScale = 1f;
        }
    }
}
