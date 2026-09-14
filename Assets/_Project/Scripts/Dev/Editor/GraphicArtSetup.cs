using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SeaSick.Dev
{
    /// Repeatable look-development scene. Production material assets are not
    /// edited: the lab owns copies and remaps serialized scene references.
    public static class GraphicArtSetup
    {
        public const string Lab = "Assets/_Project/Scenes/ArtDirectionLab.unity";
        const string Materials = "Assets/_Project/Materials/GraphicArt";

        [MenuItem("SeaSick/Art/Create Option B Lab")]
        public static void CreateLab() => Configure(false);

        [MenuItem("SeaSick/Art/Apply Option B To Sea")]
        public static void ApplyProduction() => Configure(true);

        static void Configure(bool production)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop play mode before creating the art lab.");
            var scene = SceneManager.GetActiveScene();
            if (scene.isDirty)
                throw new InvalidOperationException("Save the current scene before creating the art lab.");
            if (scene.path != "Assets/_Project/Scenes/Sea.unity" && scene.path != Lab)
                throw new InvalidOperationException("Open Sea.unity or the existing art lab first.");
            if (production && scene.path != "Assets/_Project/Scenes/Sea.unity")
                scene = EditorSceneManager.OpenScene("Assets/_Project/Scenes/Sea.unity");
            if (!production && scene.path != Lab)
            {
                if (File.Exists(Lab))
                    scene = EditorSceneManager.OpenScene(Lab);
                else
                {
                    EditorSceneManager.SaveScene(scene, Lab, true);
                    scene = EditorSceneManager.OpenScene(Lab);
                }
            }
            Directory.CreateDirectory(Materials);
            AssetDatabase.Refresh();
            var replacements = new Dictionary<UnityEngine.Object, UnityEngine.Object>();
            var ocean = CopyMaterial("Assets/_Project/Materials/OceanSurface.mat", replacements);
            var terrain = CopyMaterial("Assets/_Project/Materials/TerrainVertexColor.mat", replacements);
            const string terrainSource = "Assets/_Project/Settings/Terrain/TerrainSettings.asset";
            string terrainCopy = Materials + "/TerrainSettings.asset";
            if (AssetDatabase.LoadAssetAtPath<SeaSick.Terrain.TerrainSettings>(terrainCopy) == null)
                AssetDatabase.CopyAsset(terrainSource, terrainCopy);
            var settings = AssetDatabase.LoadAssetAtPath<SeaSick.Terrain.TerrainSettings>(terrainCopy);
            settings.graphicArtPalette = true;
            EditorUtility.SetDirty(settings);
            replacements[AssetDatabase.LoadAssetAtPath<SeaSick.Terrain.TerrainSettings>(terrainSource)] = settings;
            foreach (var root in scene.GetRootGameObjects())
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue;
                var so = new SerializedObject(component);
                var p = so.GetIterator();
                while (p.Next(true))
                    if (p.propertyType == SerializedPropertyType.ObjectReference
                        && p.objectReferenceValue != null
                        && replacements.TryGetValue(p.objectReferenceValue, out var replacement))
                        p.objectReferenceValue = replacement;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            ApplyOcean(ocean);
            ApplyTerrain(terrain);
            var sky = UnityEngine.Object.FindFirstObjectByType<SeaSick.World.SkyDirector>();
            if (sky != null)
            {
                var so = new SerializedObject(sky);
                SetColour(so, "clearZenith", new Color(0.18f, 0.43f, 0.76f));
                SetColour(so, "clearHorizon", new Color(0.65f, 0.80f, 0.91f));
                SetColour(so, "clearGround", new Color(0.27f, 0.35f, 0.43f));
                SetColour(so, "clearSun", new Color(1f, 0.94f, 0.82f));
                SetColour(so, "clearAmbientEquator", new Color(0.57f, 0.63f, 0.72f));
                SetColour(so, "clearAmbientGround", new Color(0.29f, 0.34f, 0.42f));
                so.FindProperty("clearSunIntensity").floatValue = 1.25f;
                so.FindProperty("clearOvercast").floatValue = 0.04f;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            const string palettePath = "Assets/_Project/Art/GraphicArt/ShipPalette.png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(palettePath);
            if (importer == null) throw new FileNotFoundException(palettePath);
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.sRGBTexture = true;
            importer.SaveAndReimport();
            var sceneryPath = Materials + "/Scenery.mat";
            var scenery = AssetDatabase.LoadAssetAtPath<Material>(sceneryPath);
            if (scenery == null)
            {
                scenery = new Material(terrain);
                AssetDatabase.CreateAsset(scenery, sceneryPath);
            }
            ApplyTerrain(scenery);
            scenery.SetColor("_Tint", Color.white);
            EditorUtility.SetDirty(scenery);
            var style = UnityEngine.Object.FindFirstObjectByType<SeaSick.World.WorldArtStyle>();
            if (style == null) style = new GameObject("World Art Style").AddComponent<SeaSick.World.WorldArtStyle>();
            var styleData = new SerializedObject(style);
            styleData.FindProperty("shipPalette").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(palettePath);
            styleData.FindProperty("sceneryMaterial").objectReferenceValue = scenery;
            styleData.FindProperty("sceneryResource").stringValue = "Flora/graphic_flora";
            styleData.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene);
            Debug.Log("Option B art applied: " + scene.path);
        }

        static Material CopyMaterial(string source, Dictionary<UnityEngine.Object, UnityEngine.Object> replacements)
        {
            var original = AssetDatabase.LoadAssetAtPath<Material>(source);
            if (original == null) throw new FileNotFoundException(source);
            var destination = Materials + "/" + Path.GetFileName(source);
            var copy = AssetDatabase.LoadAssetAtPath<Material>(destination);
            if (copy == null)
            {
                copy = new Material(original);
                AssetDatabase.CreateAsset(copy, destination);
            }
            replacements[original] = copy;
            return copy;
        }

        static void SetColour(SerializedObject so, string name, Color colour)
        {
            var property = so.FindProperty(name);
            if (property == null) throw new MissingFieldException(name);
            property.colorValue = colour;
        }

        public static void ApplyOcean(Material m)
        {
            m.SetColor("_DeepColor", new Color(0.025f, 0.17f, 0.40f));
            m.SetColor("_ShallowColor", new Color(0.045f, 0.37f, 0.64f));
            m.SetColor("_SubsurfaceColor", new Color(0.06f, 0.43f, 0.49f));
            m.SetColor("_ShoalColor", new Color(0.08f, 0.65f, 0.65f));
            m.SetColor("_StormDeep", new Color(0.035f, 0.07f, 0.16f));
            m.SetColor("_StormShallow", new Color(0.07f, 0.18f, 0.28f));
            m.SetColor("_StormSubsurface", new Color(0.08f, 0.30f, 0.34f));
            m.SetColor("_FoamColor", new Color(0.94f, 0.97f, 0.94f));
            m.SetFloat("_GraphicLight", 0.8f);
            m.SetFloat("_ReflectionStrength", 0.26f);
            m.SetFloat("_SurfaceDetail", 0.30f);
            m.SetFloat("_SpecPowerNear", 64f);
            m.SetFloat("_SpecPowerFar", 24f);
            m.SetFloat("_SpecStrength", 0.25f);
            m.SetFloat("_SubsurfaceStrength", 0.65f);
            m.SetFloat("_FoamBreakup", 0.18f);
            m.SetFloat("_FoamEdge", 0.7f);
            m.SetFloat("_FoamRelief", 0f);
            m.SetFloat("_FoamSparkle", 0f);
            m.SetFloat("_ShoalStrength", 0.58f);
            m.SetFloat("_RefractStrength", 0.22f);
            m.SetFloat("_SurfSwashDepth", 0.8f);
            m.SetFloat("_SurfStrength", 0.7f);
            EditorUtility.SetDirty(m);
        }

        public static void ApplyTerrain(Material m)
        {
            m.SetFloat("_GraphicLight", 0.8f);
            m.SetFloat("_DetailStrength", 0.025f);
            m.SetFloat("_NormalStrength", 0.04f);
            m.SetFloat("_StriationStrength", 0f);
            m.SetColor("_ShadowTint", new Color(0.56f, 0.67f, 0.88f));
            EditorUtility.SetDirty(m);
        }
    }
}
