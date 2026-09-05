using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SeaSick.Ship;
using SeaSick.UI;

/// Puts the modular yard on the player's ship so the ladder can be walked
/// in-game rather than measured in a lab.
///
/// Adds `Shipyard` and `ShipyardPanel` to PlayerShip and starts her on the
/// brig, which is the rung the world was built around. Press play and the
/// panel is at the left edge; F6 hides it.
public static class SetupShipyard
{
    [MenuItem("SeaSick/Shipyard/Install on PlayerShip")]
    public static void Install()
    {
        var ship = GameObject.Find("PlayerShip");
        if (ship == null)
        {
            Debug.LogError("SetupShipyard: no PlayerShip in the open scene.");
            return;
        }

        var yard = ship.GetComponent<Shipyard>();
        if (yard == null) yard = Undo.AddComponent<Shipyard>(ship);

        // The panel goes on the ship too, so one object carries the whole
        // test rig and removing it removes all of it.
        var panel = ship.GetComponent<ShipyardPanel>();
        if (panel == null) panel = Undo.AddComponent<ShipyardPanel>(ship);
        var so = new SerializedObject(panel);
        so.FindProperty("yard").objectReferenceValue = yard;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(ship);
        EditorSceneManager.MarkSceneDirty(ship.scene);

        int n = ShipLadder.Count;
        Debug.Log(n == 0
            ? "SetupShipyard: installed, but the manifest did not load — check "
              + "Assets/_Project/Resources/Ladder/ladder.txt"
            : $"SetupShipyard: installed. {n} rungs, "
              + $"{ShipLadder.Node(0).label} to {ShipLadder.Node(n - 1).label}. "
              + "Press play; the yard panel is at the left edge (F6 toggles).");
    }

    [MenuItem("SeaSick/Shipyard/Report the ladder")]
    public static void Report()
    {
        int n = ShipLadder.Count;
        if (n == 0) { Debug.LogError("no ladder manifest"); return; }
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("rung  label              L      B      D   draft    mass t  bays tiers cells");
        for (int i = 0; i < n; i++)
        {
            var d = ShipLadder.Node(i);
            sb.AppendLine($"{i,4}  {d.label,-17} {d.length,6:F2} {d.beam,6:F2} "
                        + $"{d.depth,6:F2} {d.draft,6:F2} {d.mass_kg / 1000f,8:F1} "
                        + $"{d.bays,5} {d.tiers,5} {d.cells,5}");
        }
        Debug.Log(sb.ToString());
    }
}
