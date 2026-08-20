using SeaSick.Ocean;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class AddMountainSeas
{
    public static void Execute()
    {
        var director = Object.FindAnyObjectByType<MountainSeaDirector>();
        if (director == null)
        {
            var host = Object.FindAnyObjectByType<SwellDirector>();
            GameObject go = host != null ? host.gameObject : new GameObject("MountainSeas");
            go.AddComponent<MountainSeaDirector>();
            Debug.Log("AddMountainSeas: added MountainSeaDirector to " + go.name);
            EditorUtility.SetDirty(go);
            EditorSceneManager.MarkSceneDirty(go.scene);
            EditorSceneManager.SaveScene(go.scene);
        }
        else Debug.Log("AddMountainSeas: already present on " + director.gameObject.name);
    }
}
