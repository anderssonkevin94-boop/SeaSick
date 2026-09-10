using System.Text;
using UnityEditor;
using UnityEngine;

/// Every MeshFilter or MeshRenderer in the open scene whose asset no longer
/// resolves, with its full hierarchy path. Deleting an FBX leaves these
/// behind silently -- the object still exists, it just draws nothing -- so a
/// deletion is not finished until this reports clean.
public static class FindBrokenMeshes
{
    public static string Execute()
    {
        var sb = new StringBuilder("=== FindBrokenMeshes ===\n");
        int bad = 0;
        foreach (var mf in Object.FindObjectsByType<MeshFilter>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (mf.sharedMesh != null) continue;
            bad++;
            sb.AppendLine("  missing MESH   " + Path(mf.transform));
        }
        foreach (var mr in Object.FindObjectsByType<MeshRenderer>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var mats = mr.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] == null)
                {
                    bad++;
                    sb.AppendLine($"  missing MAT[{i}] " + Path(mr.transform));
                }
        }
        sb.AppendLine(bad == 0 ? "  clean" : $"  {bad} broken reference(s)");
        return sb.ToString();
    }

    static string Path(Transform t)
    {
        string s = t.name;
        while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }
}
