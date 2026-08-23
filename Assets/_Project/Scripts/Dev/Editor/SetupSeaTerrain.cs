using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SeaSick.Terrain;
using SeaSick.World;
using SeaSick.Voyage;

/// Puts the procedural islands into Sea.unity (visual-first cutover):
/// - ArchipelagoGenerator disabled, Island_Home's authored meshes and Island
///   component off (Island.All stays empty; every consumer null-checks).
/// - TerrainStreamer under World, streaming around the ship.
/// - TerrainSettings.worldOffset chosen so the land point nearest the old
///   home position slides under it, with open water left at the spawn.
/// Idempotent; re-run after changing terrain defaults.
public static class SetupSeaTerrain
{
    const string ScenePath = "Assets/_Project/Scenes/Sea.unity";
    const string SettingsPath = "Assets/_Project/Settings/Terrain/TerrainSettings.asset";
    const string MaterialPath = "Assets/_Project/Materials/TerrainVertexColor.mat";
    const string WorldPath = "Assets/_Project/Settings/Terrain/WorldSettings.asset";

    public static string Execute()
    {
        EditorSceneManager.OpenScene(ScenePath);
        // Load after the scene switch: opening a scene can unload assets only
        // the previous scene referenced.
        var settings = AssetDatabase.LoadAssetAtPath<TerrainSettings>(SettingsPath);
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (settings == null || mat == null) return "run SetupTerrainLab first (settings/material missing)";

        // The legacy World object (ArchipelagoGenerator, now deleted) goes.
        var legacyWorld = GameObject.Find("World");
        if (legacyWorld != null && legacyWorld.GetComponent<TerrainStreamer>() == null) Object.DestroyImmediate(legacyWorld);

        // The authored home island becomes just the HomePoint transform that
        // VoyageManager/RegionField reference; the populator makes the real
        // home Island (with Stockpile) on the terrain.
        var home = GameObject.Find("Island_Home");
        if (home != null)
        {
            var isle = home.GetComponent<Island>();
            if (isle != null) Object.DestroyImmediate(isle);
            var pile = home.GetComponent<Stockpile>();
            if (pile != null) Object.DestroyImmediate(pile);
            for (int i = home.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(home.transform.GetChild(i).gameObject);
            home.name = "HomePoint";
        }
        var worldSettings = AssetDatabase.LoadAssetAtPath<WorldSettings>(WorldPath);
        if (worldSettings == null)
        {
            worldSettings = ScriptableObject.CreateInstance<WorldSettings>();
            AssetDatabase.CreateAsset(worldSettings, WorldPath);
        }

        var ship = Object.FindFirstObjectByType<SeaSick.Ship.ShipMotor>();
        var streamer = Object.FindFirstObjectByType<TerrainStreamer>(FindObjectsInactive.Include);
        if (streamer == null) streamer = new GameObject("Terrain").AddComponent<TerrainStreamer>();
        streamer.transform.SetParent(null, true);
        streamer.gameObject.SetActive(true);
        streamer.settings = settings;
        streamer.material = mat;
        streamer.target = ship != null ? ship.transform : null;

        var shoreField = streamer.GetComponent<TerrainShoreField>();
        if (shoreField == null) shoreField = streamer.gameObject.AddComponent<TerrainShoreField>();
        shoreField.settings = settings;
        shoreField.target = streamer.target;

        var populator = streamer.GetComponent<TerrainWorldPopulator>();
        if (populator == null) populator = streamer.gameObject.AddComponent<TerrainWorldPopulator>();
        populator.terrain = settings;
        populator.world = worldSettings;
        var voyage = Object.FindFirstObjectByType<VoyageManager>();
        populator.homePoint = voyage != null ? voyage.HomePoint : (home != null ? home.transform : null);

        string offsetNote = ChooseHomeOffset(settings, home != null ? home.transform.position : new Vector3(0f, 0f, -75f));
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
        return "Sea.unity: archipelago off, streamer on " + (ship != null ? ship.name : "<no ship>") + "; " + offsetNote;
    }

    /// Slide the world so that the nearest solid land point ends up 140 m
    /// beyond the old home position (away from the spawn), keeping the spawn
    /// itself in water deeper than 2 m.
    static string ChooseHomeOffset(TerrainSettings s, Vector3 homePos)
    {
        s.worldOffset = Vector2.zero;
        var prm = TerrainParams.From(s);
        var lut = TerrainCurveLut.Bake(s.terraceCurve, Allocator.Temp);
        float2 spawn = float2.zero;
        float2 want = new float2(homePos.x, homePos.z) + new float2(0f, -140f);

        var candidates = new List<float2>();
        const float R = 6000f, step = 48f;
        for (float z = -R; z <= R; z += step)
            for (float x = -R; x <= R; x += step)
            {
                float2 p = new float2(x, z);
                if (TerrainHeight.Mask(p, prm) > 0.97f) candidates.Add(p);
            }
        candidates.Sort((a, b) => math.lengthsq(a - want).CompareTo(math.lengthsq(b - want)));

        foreach (var land in candidates)
        {
            float2 offset = land - want; // sampling at `want` must read the noise at `land`
            var test = prm; test.worldOffset = offset;
            float hSpawn = TerrainHeight.Height(spawn, test, lut);
            float hWant = TerrainHeight.Height(want, test, lut);
            if (hSpawn < -2f && hWant > 2f)
            {
                s.worldOffset = new Vector2(offset.x, offset.y);
                lut.Dispose();
                return "worldOffset=" + offset + " (land " + land + " -> " + want + "), spawn depth " + hSpawn.ToString("F1") + " m";
            }
        }
        lut.Dispose();
        return "no suitable home land found within " + R + " m; worldOffset left at zero";
    }
}
