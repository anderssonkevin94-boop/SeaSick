using UnityEditor;
using UnityEngine;

/// Bring the Game view forward and give it focus.
///
/// Driven from Coplay, play mode runs at a dead-steady 10 fps whenever the
/// Game view is not the frontmost tab, and neither `Application.runInBackground`
/// nor re-activating the app from a shell beats it. Five probe runs in a row
/// were measured at 100 ms/frame because of it, and any artefact that only
/// appears at 60 fps is invisible at 10.
public static class FocusGameView
{
    public static void Execute()
    {
        var t = System.Type.GetType("UnityEditor.GameView,UnityEditor");
        if (t == null) { Debug.LogError("FocusGameView: no GameView type"); return; }
        var w = EditorWindow.GetWindow(t, false, "Game", true);
        if (w == null) { Debug.LogError("FocusGameView: could not get the window"); return; }
        w.Show();
        w.Focus();
        EditorApplication.ExecuteMenuItem("Window/General/Game");
        Debug.Log($"FocusGameView: focused={EditorWindow.focusedWindow?.GetType().Name}, "
            + $"isPlaying={Application.isPlaying}");
    }
}
