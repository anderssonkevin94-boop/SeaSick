using SeaSick.Ship;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class AddBilge
{
    public static void Execute()
    {
        var motor = Object.FindAnyObjectByType<ShipMotor>();
        if (motor == null) { Debug.LogError("AddBilge: no ShipMotor"); return; }
        var ship = motor.gameObject;

        if (ship.GetComponent<Bilge>() == null)
        {
            ship.AddComponent<Bilge>();
            Debug.Log("AddBilge: added Bilge to " + ship.name);
        }
        else Debug.Log("AddBilge: Bilge already present");

        EditorUtility.SetDirty(ship);
        EditorSceneManager.MarkSceneDirty(ship.scene);
        EditorSceneManager.SaveScene(ship.scene);
        Debug.Log("AddBilge: scene saved");
    }
}
