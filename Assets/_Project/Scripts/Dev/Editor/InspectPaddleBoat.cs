using System.Text;
using UnityEditor;
using UnityEngine;

/// Reads the imported paddle_boat FBX and reports what Unity actually made of
/// it: hierarchy, local transforms, and world bounds per part. Orientation and
/// scale are measured here rather than reasoned about, because Blender's axis
/// conversion is exactly the kind of thing that is faster to look at than to
/// derive. Edit mode. Writes /tmp/seasick-paddleboat.txt.
public static class InspectPaddleBoat
{
    const string Path = "Assets/_Project/Art/Ship/paddle_boat.fbx";

    public static string Execute() { return Dump(Path, "/tmp/seasick-paddleboat.txt"); }

    /// The old cannon is built from Unity primitives; before swapping it for
    /// the FBX we need to know whether the FBX has a separate barrel to hang
    /// the recoil pivot on.
    public static string ExecuteCannon()
    {
        return Dump("Assets/_Project/Art/Ship/player_ship_cannon.fbx", "/tmp/seasick-cannon.txt");
    }

    static string Dump(string Path, string outPath)
    {
        var importer = AssetImporter.GetAtPath(Path) as ModelImporter;
        if (importer == null) return "no ModelImporter at " + Path;

        var go = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
        if (go == null) return "FBX not imported yet at " + Path;

        var sb = new StringBuilder();
        sb.AppendLine("import scale factor " + importer.globalScale
            + ", useFileScale " + importer.useFileScale
            + ", materials " + importer.materialImportMode);

        var inst = Object.Instantiate(go);
        inst.transform.position = Vector3.zero;
        inst.transform.rotation = Quaternion.identity;
        inst.transform.localScale = Vector3.one;

        var all = inst.GetComponentsInChildren<Transform>(true);
        Bounds total = new Bounds();
        bool first = true;
        sb.AppendLine();
        sb.AppendLine(string.Format("{0,-22} {1,-14} {2,-28} {3}", "NAME", "PARENT", "LOCAL POS", "WORLD BOUNDS size / centre"));
        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            string parent = t.parent != null ? t.parent.name : "-";
            string b = "";
            var mr = t.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                b = string.Format("{0} @ {1}", V(mr.bounds.size), V(mr.bounds.center));
                if (first) { total = mr.bounds; first = false; }
                else total.Encapsulate(mr.bounds);
            }
            sb.AppendLine(string.Format("{0,-22} {1,-14} {2,-28} {3}", t.name, parent, V(t.localPosition), b));
        }

        sb.AppendLine();
        sb.AppendLine("WHOLE BOAT size " + V(total.size) + " centre " + V(total.center));
        sb.AppendLine("  length along X " + F(total.size.x) + ", along Z " + F(total.size.z) + ", height Y " + F(total.size.y));

        // Which way is the bow? The bow lantern is the marker.
        Transform bow = Find(inst.transform, "LanternBow");
        Transform stern = Find(inst.transform, "LanternStern");
        if (bow != null && stern != null)
        {
            Vector3 axis = bow.position - stern.position;
            sb.AppendLine("  bow minus stern = " + V(axis) + "  (bow direction in Unity axes)");
        }
        Transform port = Find(inst.transform, "PaddleWheel_Port");
        Transform stbd = Find(inst.transform, "PaddleWheel_Stbd");
        if (port != null && stbd != null)
        {
            sb.AppendLine("  port wheel at " + V(port.position) + ", stbd wheel at " + V(stbd.position));
            sb.AppendLine("  wheel separation vector = " + V(stbd.position - port.position));
        }

        Object.DestroyImmediate(inst);
        System.IO.File.WriteAllText(outPath, sb.ToString());
        Debug.Log("InspectPaddleBoat:\n" + sb);
        return "wrote " + outPath;
    }

    static Transform Find(Transform root, string name)
    {
        var all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++) if (all[i].name == name) return all[i];
        return null;
    }

    static string V(Vector3 v) { return string.Format("({0}, {1}, {2})", F(v.x), F(v.y), F(v.z)); }
    static string F(float f) { return f.ToString("F3"); }
}
