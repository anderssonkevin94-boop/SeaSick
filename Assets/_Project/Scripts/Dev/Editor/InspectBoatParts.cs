using System.Text;
using UnityEngine;

/// What the boat's parts are actually doing in the scene: local rotation,
/// world bounds size, and position in ship space. The lanterns hanging
/// sideways and the helm wheel lying flat are both ROTATION faults, and a
/// position dump cannot see them — bounds size is what tells a wheel standing
/// up from one lying down. Edit mode. Writes /tmp/seasick-boatparts.txt.
public static class InspectBoatParts
{
    public static string Execute()
    {
        GameObject ship = GameObject.Find("PlayerShip");
        if (ship == null) return "no PlayerShip";
        var sb = new StringBuilder();

        string[] names =
        {
            "HelmWheel", "HelmStand",
            "LanternBow", "LanternStern", "LanternBow_Pivot", "LanternStern_Pivot",
            "DeckPlanks", "Railing", "HullPlanks", "Margin",
        };

        sb.AppendLine(string.Format("{0,-20} {1,-26} {2,-26} {3}",
            "NAME", "LOCAL EULER", "BOUNDS SIZE", "SHIP-LOCAL CENTRE"));
        for (int i = 0; i < names.Length; i++)
        {
            Transform t = Find(ship.transform, names[i]);
            if (t == null) { sb.AppendLine(names[i] + "  (not found)"); continue; }
            Renderer r = t.GetComponent<Renderer>();
            string size = "-", centre = "-";
            if (r != null)
            {
                size = V(r.bounds.size);
                centre = V(ship.transform.InverseTransformPoint(r.bounds.center));
            }
            sb.AppendLine(string.Format("{0,-20} {1,-26} {2,-26} {3}",
                t.name, V(t.localEulerAngles), size, centre));
        }

        // Where does everything that should be STANDING on the deck sit?
        sb.AppendLine();
        sb.AppendLine("THINGS THAT SHOULD TOUCH THE DECK (ship-local y of their base)");
        Transform[] all = ship.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            bool crew = t.name.StartsWith("Crew_") || t.name == "Helmsman";
            if (!crew) continue;
            Vector3 p = ship.transform.InverseTransformPoint(t.position);
            sb.AppendLine(string.Format("  {0,-16} local {1}", t.name, V(p)));
        }

        // The deck surface is not flat: it has camber and sheer, so a single
        // deck height puts anything amidships in the air.
        Transform deck = Find(ship.transform, "DeckPlanks");
        if (deck != null)
        {
            MeshFilter mf = deck.GetComponent<MeshFilter>();
            sb.AppendLine();
            if (mf == null || mf.sharedMesh == null) sb.AppendLine("DeckPlanks has no mesh");
            else if (!mf.sharedMesh.isReadable)
                sb.AppendLine("DeckPlanks mesh is NOT readable — enable Read/Write on the FBX to sample the deck surface");
            else
            {
                Mesh m = mf.sharedMesh;
                Vector3[] v = m.vertices;
                float lo = float.MaxValue, hi = float.MinValue;
                for (int k = 0; k < v.Length; k++)
                {
                    float y = ship.transform.InverseTransformPoint(deck.TransformPoint(v[k])).y;
                    if (y < lo) lo = y;
                    if (y > hi) hi = y;
                }
                sb.AppendLine(string.Format("deck surface spans ship-local y {0:F2} .. {1:F2} ({2} verts)",
                    lo, hi, v.Length));
            }
        }

        System.IO.File.WriteAllText("/tmp/seasick-boatparts.txt", sb.ToString());
        Debug.Log("InspectBoatParts:\n" + sb);
        return "wrote /tmp/seasick-boatparts.txt";
    }

    static Transform Find(Transform root, string name)
    {
        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++) if (all[i].name == name) return all[i];
        return null;
    }

    static string V(Vector3 v) { return string.Format("({0:F2}, {1:F2}, {2:F2})", v.x, v.y, v.z); }
}
