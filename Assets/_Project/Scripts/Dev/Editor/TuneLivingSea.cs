#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SeaSick.Ocean;

/// Pushes the living-sea weather values into the scene through
/// SerializedObject and reads them back.
///
/// Necessary because of the trap this project keeps re-learning: a component
/// already in a .unity file keeps the value it was DESERIALISED with, and a
/// changed C# field initialiser never reaches it. `setDepth` stayed at 0.32
/// for a whole debugging session that way. Fields absent from the YAML do take
/// the code default, but `rough` is an object reference that has to be wired
/// either way, so everything goes through one push and one read-back.
public static class TuneLivingSea
{
    [MenuItem("SeaSick/Ocean/Tune Living Sea")]
    public static void Run()
    {
        var ctrl = Object.FindFirstObjectByType<SeaStateController>(FindObjectsInactive.Include);
        if (ctrl == null) { Debug.LogError("TuneLivingSea: no SeaStateController in the open scene"); return; }

        var roughAsset = Resources.Load<OceanSpectrumSettings>("Ocean/SeaState_Rough");
        if (roughAsset == null) { Debug.LogError("TuneLivingSea: Resources/Ocean/SeaState_Rough not found"); return; }

        var so = new SerializedObject(ctrl);
        so.FindProperty("rough").objectReferenceValue = roughAsset;
        so.FindProperty("shelfCalmHs").floatValue = 1.6f;
        so.FindProperty("shelfLivelyHs").floatValue = 4.5f;
        so.FindProperty("blendTime").floatValue = 45f;
        so.FindProperty("wanderPeriod").floatValue = 620f;
        so.FindProperty("setDepth").floatValue = 0.16f;
        so.FindProperty("setPeriod").floatValue = 95f;
        so.FindProperty("skyHsStart").floatValue = 8f;
        so.FindProperty("skyHsFull").floatValue = 55f;
        so.FindProperty("squallHs").floatValue = 9f;
        so.FindProperty("stormSteady").floatValue = 0.88f;
        so.FindProperty("cellShape").floatValue = 1.6f;
        so.ApplyModifiedPropertiesWithoutUndo();

        // The weather field itself. Lives beside the RegionField that carries
        // it to the shader and the jobs.
        var region0 = Object.FindFirstObjectByType<RegionField>(FindObjectsInactive.Include);
        if (region0 != null && region0.GetComponent<WeatherField>() == null)
        {
            Undo.AddComponent<WeatherField>(region0.gameObject);
            Debug.Log("TuneLivingSea: added WeatherField to " + region0.gameObject.name);
        }

        // The weather field's own values, for the same reason everything else
        // here goes through SerializedObject: the component was added to the
        // scene with one set of defaults and has kept them ever since. This
        // bit already: the tile stayed 6144 m and the drift 2.7 m/s through
        // two rounds of retuning, and only the values that live in code (the
        // octave periods) actually moved.
        var wf = region0 != null ? region0.GetComponent<WeatherField>() : null;
        if (wf != null)
        {
            var wso = new SerializedObject(wf);
            wso.FindProperty("tileTexels").intValue = 256;
            wso.FindProperty("tileMetres").floatValue = 4096f;
            wso.FindProperty("driftSpeed").floatValue = 4.2f;
            wso.FindProperty("driftHeadingDeg").floatValue = 200f;
            wso.FindProperty("meanderMetres").floatValue = 350f;
            wso.FindProperty("meanderPeriod").floatValue = 430f;
            // Per-cascade patch ranges. The swell's high end is 1 and must
            // stay 1; everything else is character. Note these REPLACE the old
            // float patchLo / Vector3 patchCoupling -- the scene's values for
            // those are dead weight in the .unity file and are not read.
            wso.FindProperty("patchRangeLo").vector3Value = new Vector3(0.90f, 0.55f, 0.25f);
            wso.FindProperty("patchRangeHi").vector3Value = new Vector3(1.00f, 1.30f, 1.55f);
            wso.FindProperty("chopMeanTarget").floatValue = 1f;
            wso.FindProperty("cellMetres").floatValue = 20000f;
            wso.FindProperty("cellDriftSpeed").floatValue = 5f;
            wso.FindProperty("cellHeadingDeg").floatValue = 195f;
            wso.FindProperty("contrast").floatValue = 1.7f;
            wso.ApplyModifiedPropertiesWithoutUndo();
        }

        // The storm ramp. 450 -> 1250 m made the sea grow by x1.82 every 100 m
        // sailed (LivingSeaTrace) and then pinned it at 65 m for the rest of
        // the world: 800 m is not a gradient, it is a doorway. Widened until
        // the steepest step is about x1.2 per 100 m, which reads as water
        // building rather than a wall arriving.
        var rf = region0;
        if (rf != null)
        {
            var rso = new SerializedObject(rf);
            rso.FindProperty("stormNear").floatValue = 500f;
            rso.FindProperty("stormFar").floatValue = 2800f;
            rso.ApplyModifiedPropertiesWithoutUndo();
        }

        EditorSceneManager.MarkSceneDirty(ctrl.gameObject.scene);
        EditorSceneManager.SaveScene(ctrl.gameObject.scene);

        // Read back off a FRESH SerializedObject — the point of the exercise is
        // to prove the scene holds these, not that we just wrote them.
        var check = new SerializedObject(ctrl);
        var sb = new System.Text.StringBuilder("TuneLivingSea, read back from the scene:\n");
        foreach (var n in new[] { "shelfCalmHs", "shelfLivelyHs", "blendTime",
                                  "wanderPeriod", "setDepth", "setPeriod",
                                  "skyHsStart", "skyHsFull",
                                  "squallHs", "stormSteady", "cellShape" })
            sb.AppendLine($"  {n,-16} {check.FindProperty(n).floatValue}");
        var r = check.FindProperty("rough").objectReferenceValue;
        sb.AppendLine($"  rough            {(r != null ? r.name : "NULL")}");
        if (rf != null)
        {
            var rcheck = new SerializedObject(rf);
            sb.AppendLine($"  stormNear        {rcheck.FindProperty("stormNear").floatValue}");
            sb.AppendLine($"  stormFar         {rcheck.FindProperty("stormFar").floatValue}");
            sb.AppendLine($"  WeatherField     {(wf != null ? "present" : "MISSING")}");
            if (wf != null)
            {
                var wcheck = new SerializedObject(wf);
                foreach (var n in new[] { "tileTexels", "tileMetres", "driftSpeed",
                                          "chopMeanTarget", "contrast", "cellMetres", "cellDriftSpeed" })
                {
                    var pr = wcheck.FindProperty(n);
                    sb.AppendLine($"    {n,-16} {(pr.propertyType == SerializedPropertyType.Integer ? pr.intValue.ToString() : pr.floatValue.ToString())}");
                }
                foreach (var n in new[] { "patchRangeLo", "patchRangeHi" })
                    sb.AppendLine($"    {n,-16} {wcheck.FindProperty(n).vector3Value}");
            }
        }

        // And the ladder the whole design turns on, so it is in the log next
        // to the values that produced it.
        sb.AppendLine("  severity -> Hs:");
        foreach (var s in new[] { 0f, 0.2f, 0.4f, 0.55f, 0.7f, 0.8f, 0.9f, 1f })
            sb.AppendLine($"    {s:F2}  {ctrl.HsAt(s),6:F1} m");
        Debug.Log(sb.ToString());
    }
}
#endif
