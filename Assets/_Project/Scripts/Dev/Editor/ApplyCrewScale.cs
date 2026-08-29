using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using SeaSick.World;
using System.Text;

/// Brings the crew onto the charter: 1.70 m, measured.
///
/// They were 1.50 m, which made her 16.2 crew long where a real vessel her
/// length is about 14. That is the ONE thing in the world genuinely off the
/// reality scale -- not the trees, and not the ocean, which cannot be
/// rescaled at all: it is physically simulated, so gravity fixes how fast a
/// wave of a given length travels. The sea is therefore what defines a metre
/// here, the ship is pinned to the sea, and the crew are pinned to the ship.
///
/// Scales each crew root by the ratio its own measured height needs, rather
/// than by one shared number, so a figure built differently still lands on
/// 1.70. Serialised in the scene, so it has to be pushed and saved.
public static class ApplyCrewScale
{
    const string ScenePath = "Assets/_Project/Scenes/Sea.unity";

    public static string Execute()
    {
        if (Application.isPlaying) return "stop play mode first — this saves the scene";
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath);

        var sb = new StringBuilder();
        int done = 0;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            bool isCrew = t.name == "Helmsman" || t.GetComponent<SeaSick.Crew.CrewAgent>() != null;
            if (!isCrew) continue;

            var rs = t.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) continue;
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            float was = b.size.y;
            if (was < 0.05f) continue;

            float factor = WorldScale.Person / was;
            var so = new SerializedObject(t);
            var sp = so.FindProperty("m_LocalScale");
            sp.vector3Value = sp.vector3Value * factor;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Re-measure: bounds are stale until the transform updates.
            rs = t.GetComponentsInChildren<Renderer>();
            Bounds b2 = rs[0].bounds;
            foreach (var r in rs) b2.Encapsulate(r.bounds);
            sb.AppendLine($"  {t.name}: {was:F2} m -> {b2.size.y:F2} m");
            done++;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        sb.Insert(0, $"crew scaled to WorldScale.Person = {WorldScale.Person} m ({done} of them)\n");
        sb.AppendLine($"she is now {WorldScale.ShipLength / WorldScale.Person:F1} crew long "
            + "(a real vessel her length is about 14)");
        return sb.ToString();
    }
}
