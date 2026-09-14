using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SeaSick.Dev
{
    public static class StorybookIslandSetup
    {
        [MenuItem("SeaSick/Art/Build Storybook Island Prefabs")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Stop play first.");
            const string output="Assets/_Project/Resources/IslandAssets";
            Directory.CreateDirectory(output);
            AssetDatabase.Refresh();
            var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/GraphicArt/Scenery.mat");
            material.enableInstancing=true;
            EditorUtility.SetDirty(material);
            foreach (string path in Directory.GetFiles("Assets/_Project/Resources/Flora/Storybook","*.fbx"))
            {
                var model=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model==null) continue;
                string id=Path.GetFileNameWithoutExtension(path);
                var root=Object.Instantiate(model);root.name=id;
                var renderers=root.GetComponentsInChildren<MeshRenderer>(true);
                foreach(var r in renderers) r.sharedMaterials=Enumerable.Repeat(material,r.sharedMaterials.Length).ToArray();
                var high=renderers.Where(r=>!r.name.EndsWith("_LOD1")).Cast<Renderer>().ToArray();
                var low=renderers.Where(r=>r.name.EndsWith("_LOD1")).Cast<Renderer>().ToArray();
                var lod=root.AddComponent<LODGroup>();
                lod.SetLODs(low.Length>0 ? new[]{new LOD(.055f,high),new LOD(.006f,low)} : new[]{new LOD(.006f,high)});
                lod.RecalculateBounds();
                var identity=root.AddComponent<SeaSick.World.IslandAsset>();identity.assetId=id;
                identity.resource=id.StartsWith("Broad")||id.StartsWith("Spruce")||id=="Palm" ? "Timber" : id=="Ore" ? "Ore" : id.StartsWith("Boulder") ? "Stone" : id.StartsWith("Crop") ? "Grain" : "";
                var bounds=high[0].bounds;foreach(var r in high)bounds.Encapsulate(r.bounds);
                identity.authoredHeight=bounds.size.y;
                if(identity.resource.Length>0)
                {
                    var collider=root.AddComponent<BoxCollider>();
                    collider.isTrigger=true;collider.center=root.transform.InverseTransformPoint(bounds.center);
                    collider.size=bounds.size;
                }
                PrefabUtility.SaveAsPrefabAsset(root,output+"/"+id+".prefab");Object.DestroyImmediate(root);
            }
            var settings=AssetDatabase.LoadAssetAtPath<SeaSick.Terrain.TerrainSettings>("Assets/_Project/Materials/GraphicArt/TerrainSettings.asset");
            settings.storybookLandforms=true;settings.individualTrees=true;
            settings.homeIsleRadius=110f;settings.homeIsleShape=19f;
            settings.homeIsleCoveMouth=145f;settings.homeIsleCoveHalfMouth=22f;
            settings.homeIsleShoreRun=23f;
            settings.treeSpacing=9f;settings.treeDensity=.72f;settings.standContrast=3.2f;
            settings.standFloor=.10f;settings.scrubChance=.65f;settings.cragScale=1f;
            settings.sandTreeMargin=.25f;
            EditorUtility.SetDirty(settings);
            var style=Object.FindFirstObjectByType<SeaSick.World.WorldArtStyle>();
            if(style!=null){var so=new SerializedObject(style);so.FindProperty("sceneryResource").stringValue="Flora/storybook_flora";so.ApplyModifiedPropertiesWithoutUndo();}
            AssetDatabase.SaveAssets();
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }
    }
}
