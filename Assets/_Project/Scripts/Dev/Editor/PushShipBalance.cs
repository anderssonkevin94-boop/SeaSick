using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Push tuned balance values onto the ship's scene components and SAVE.
///
/// A serialized scene value wins over the C# field initialiser forever, so a
/// number "fixed" in source is not fixed in the game. `docs/GDD.md` records
/// crewShock being halved 0.12 to 0.06 and shotShock 0.10 to 0.05 on
/// 2026-08-19; the scene was never re-saved, so the game has been running the
/// old double-strength values ever since. This is the push that lands them.
///
/// Names no project type -- every component is found by type NAME through
/// reflection -- because execute_script compiles this file into a fresh
/// assembly that does not get the project's reference set, and naming a
/// project type there fails with an unreadable Roslyn resource exception.
public static class PushShipBalance
{
    public static void Execute()
    {
        if (Application.isPlaying)
        {
            Debug.LogError("PushShipBalance: stop play mode first; a SerializedObject "
                + "write in play mode does not persist to the scene.");
            return;
        }
        var ship = GameObject.Find("PlayerShip");
        if (ship == null) { Debug.LogError("PushShipBalance: no PlayerShip"); return; }

        int changed = 0;
        changed += Push(ship, "HullIntegrity", "crewShock", 0.06f);
        changed += Push(ship, "HullIntegrity", "shotShock", 0.05f);

        // Broach window, moved onto the sea the game actually makes.
        // StormCostProbe: broach maxed at 0.48 and spent 2% of its time above
        // 0.3 at Hs 50 RUNNING DOWN-SEA -- the one condition the mechanic
        // exists for. The window was the reason: steep = InverseLerp(0.20,
        // 0.50, downSlope), while the measured sea has a median face of 15.4
        // deg (slope 0.28) and its biggest swell faces at 24.9 deg (0.46). A
        // median face therefore scored 0.27 on a window calibrated above the
        // water underneath it.
        changed += Push(ship, "ShipMotor", "broachOnsetSlope", 0.14f);
        changed += Push(ship, "ShipMotor", "broachFullSlope", 0.35f);

        // Overspeed threshold. Overspeed01 = (forwardWay - effMaxSpeed) /
        // (effMaxSpeed * (surfOvershoot - 1)), and the probe measured her
        // peaking at 1.10x her NOMINAL max while Overspeed01 stayed at 0.002 --
        // the surf reward never fires at any sea state. 1.12 is both a
        // plausible balance value and a test: if the mechanic is merely set out
        // of reach this makes it live, and if it still reads zero the fault is
        // in forwardWay or effMaxSpeed and not in the threshold.
        changed += Push(ship, "ShipMotor", "surfOvershoot", 1.12f);

        if (changed > 0)
        {
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            Debug.Log($"PushShipBalance: {changed} value(s) pushed and scene saved.");
        }
        else Debug.Log("PushShipBalance: nothing to change.");
    }

    static int Push(GameObject go, string typeName, string field, float value)
    {
        foreach (var c in go.GetComponents<MonoBehaviour>())
        {
            if (c == null || c.GetType().Name != typeName) continue;
            var so = new SerializedObject(c);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogError($"PushShipBalance: no {typeName}.{field}"); return 0; }
            float was = p.floatValue;
            if (Mathf.Approximately(was, value))
            {
                Debug.Log($"PushShipBalance: {typeName}.{field} already {value}");
                return 0;
            }
            p.floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
            // Read it back off the LOADED object: this project has lost days to
            // a push that reported success and did not take.
            float now = new SerializedObject(c).FindProperty(field).floatValue;
            Debug.Log($"PushShipBalance: {typeName}.{field} {was} -> {now}");
            return Mathf.Approximately(now, value) ? 1 : 0;
        }
        Debug.LogError($"PushShipBalance: no {typeName} on {go.name}");
        return 0;
    }
}
