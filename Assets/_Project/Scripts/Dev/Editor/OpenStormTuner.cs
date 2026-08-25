using UnityEditor;
using UnityEngine;

/// Opens the Storm Tuner and forces it to paint.
///
/// An EditorWindow that compiles is not an EditorWindow that works — every
/// OnGUI path is untested until something has actually drawn it, and a stray
/// exception in there just fills the console and leaves a blank panel. This
/// opens the window, pins the weather and repaints it a few times so any
/// throw lands in the log where it can be read.
public static class OpenStormTuner
{
    public static string Execute()
    {
        var w = EditorWindow.GetWindow<StormTuner>("Storm Tuner");
        w.minSize = new Vector2(420f, 640f);
        w.position = new Rect(80f, 80f, 520f, 900f);
        w.Show();
        w.Focus();
        w.Repaint();
        return "Storm Tuner opened at " + w.position
             + " (play mode: " + Application.isPlaying + ")";
    }
}
