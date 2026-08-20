using UnityEditor;
using UnityEngine;

/// The standing answer to this project's most expensive recurring trap.
///
/// Unity serialises a component's field values into the .unity file the moment
/// the component is added, and those values beat the C# initialisers forever
/// after — so re-tuning a default in code does nothing at all, silently. It has
/// cost time on freeboard, on the duplicate WaveField, on the storm sky, and on
/// `waveCount`, which sat at 14 in the scene against 10 in the source.
///
/// `ResetToCodeDefaults` copies every serialised value off a freshly
/// constructed instance of the same type — i.e. exactly the C# field
/// initialisers — onto the scene component, so the source becomes the truth
/// again. Object references are skipped so scene wiring survives.
public static class SceneDefaults
{
    public static void ResetToCodeDefaults(Component target)
    {
        if (target == null) return;
        var temp = new GameObject("~defaults") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var fresh = temp.AddComponent(target.GetType());
            var src = new SerializedObject(fresh);
            var dst = new SerializedObject(target);
            var it = src.GetIterator();
            bool enter = true;
            while (it.NextVisible(enter))
            {
                enter = false;
                if (it.propertyPath == "m_Script") continue;
                if (it.propertyType == SerializedPropertyType.ObjectReference) continue;
                dst.CopyFromSerializedProperty(it);
            }
            dst.ApplyModifiedPropertiesWithoutUndo();
        }
        finally
        {
            Object.DestroyImmediate(temp);
        }
    }
}
