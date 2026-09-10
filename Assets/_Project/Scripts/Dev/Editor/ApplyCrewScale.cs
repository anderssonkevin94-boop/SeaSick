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

    /// Height from the MESHES, not from Renderer.bounds.
    ///
    /// A SkinnedMeshRenderer's bounds are padded so a deforming mesh cannot
    /// pop out of them, so a crew member authored at exactly 1.700 measures
    /// 1.703 -- and a tool that scales until the measurement equals 1.700
    /// then shrinks him by 0.2% every single time it is run, chasing padding
    /// that is not part of the asset. Mesh bounds in the bind pose are the
    /// real thing, and they make this idempotent: run it twice and the second
    /// run changes nothing.
    static float MeasureHeight(Transform t)
    {
        bool any = false;
        float lo = float.MaxValue, hi = float.MinValue;

        void Eat(Mesh m, Transform x)
        {
            if (m == null) return;
            Bounds lb = m.bounds;
            for (int i = 0; i < 8; i++)
            {
                var c = lb.center + Vector3.Scale(lb.extents, new Vector3(
                    (i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                float y = x.localToWorldMatrix.MultiplyPoint3x4(c).y;
                lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y); any = true;
            }
        }

        foreach (var smr in t.GetComponentsInChildren<SkinnedMeshRenderer>())
            Eat(smr.sharedMesh, smr.transform);
        foreach (var mf in t.GetComponentsInChildren<MeshFilter>())
            Eat(mf.sharedMesh, mf.transform);
        return any ? hi - lo : 0f;
    }

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

            float was = MeasureHeight(t);
            if (was < 0.05f) continue;

            float factor = WorldScale.Person / was;
            var so = new SerializedObject(t);
            var sp = so.FindProperty("m_LocalScale");
            sp.vector3Value = sp.vector3Value * factor;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Re-measure: bounds are stale until the transform updates.
            sb.AppendLine($"  {t.name}: {was:F3} m -> {MeasureHeight(t):F3} m "
                + $"(scale {sp.vector3Value.y:F4})");
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
