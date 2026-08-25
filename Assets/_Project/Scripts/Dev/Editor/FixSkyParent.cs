using SeaSick.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// The storm sky has been switched off since the 2026-08-23 world rebuild, and
/// nobody noticed because nothing errors when it happens.
///
/// SetupSeaTerrain tried to remove the legacy `World` object (the deleted
/// ArchipelagoGenerator) with GameObject.Find, which only sees ACTIVE objects.
/// `World` had been deactivated instead of destroyed, so the find missed it and
/// it stayed in the scene, inactive — and `Sky` was a CHILD of it. An inactive
/// parent means SkyDirector.Awake never runs, so SkyDirector.Instance is null,
/// so nothing ever writes _SS_Storminess, so the ocean shader reads 0.000 and
/// renders a full storm in fair-weather turquoise. Measured: SeaStateController
/// said storminess 1.00 while the GPU global said 0.000.
///
/// This lifts Sky to the scene root and removes the corpse. Idempotent.
public static class FixSkyParent
{
    const string ScenePath = "Assets/_Project/Scenes/Sea.unity";

    public static string Execute()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath);

        // Find it by TYPE including inactive — GameObject.Find is exactly the
        // call that caused this, and it would miss it again.
        var sky = Object.FindFirstObjectByType<SkyDirector>(FindObjectsInactive.Include);
        if (sky == null) return "no SkyDirector in the scene at all";

        string before = Describe(sky);

        Transform parent = sky.transform.parent;
        if (parent != null)
        {
            sky.transform.SetParent(null, true);
            // SetParent has failed silently in this project before (prefab
            // instances). Confirm rather than assume.
            if (sky.transform.parent != null)
                return "SetParent did NOT take — Sky is still under " + sky.transform.parent.name;
        }

        if (!sky.gameObject.activeSelf) sky.gameObject.SetActive(true);
        sky.enabled = true;

        // The old World object is a corpse: an inactive holder for a script
        // that no longer exists. Leaving it is how this trap gets re-set.
        string removed = "none";
        if (parent != null && parent.name == "World" && parent.childCount == 0)
        {
            removed = parent.name;
            Object.DestroyImmediate(parent.gameObject);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);

        return "was [" + before + "] -> now [" + Describe(sky) + "], removed: " + removed;
    }

    static string Describe(SkyDirector sky)
    {
        return "parent=" + (sky.transform.parent != null ? sky.transform.parent.name : "<root>")
             + " activeInHierarchy=" + sky.gameObject.activeInHierarchy
             + " enabled=" + sky.enabled;
    }
}
