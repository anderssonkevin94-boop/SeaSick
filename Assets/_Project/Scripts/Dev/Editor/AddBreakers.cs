using SeaSick.Ship;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Puts the surf line on the ship. Same shape as AddBilge: one idempotent
/// call, run once per scene, so the component does not have to be dragged on
/// by hand and cannot be forgotten on one of the lab scenes.
public static class AddBreakers
{
    public static void Execute()
    {
        var motor = Object.FindAnyObjectByType<ShipMotor>();
        if (motor == null) { Debug.LogError("AddBreakers: no ShipMotor"); return; }
        var ship = motor.gameObject;

        if (ship.GetComponent<Breakers>() == null)
        {
            ship.AddComponent<Breakers>();
            Debug.Log("AddBreakers: added Breakers to " + ship.name);
        }
        else Debug.Log("AddBreakers: Breakers already present");

        EditorUtility.SetDirty(ship);
        EditorSceneManager.MarkSceneDirty(ship.scene);
        EditorSceneManager.SaveScene(ship.scene);
        Debug.Log("AddBreakers: scene saved");
    }
}
