using System.Collections;
using System.Text;
using UnityEngine;
using SeaSick.Ship;

/// **Is her spray thrown from her, or from inside her?**
///
/// `SpeedJuice` placed every emitter at a coordinate typed for the paddle
/// steamer, and nothing resized them when the ladder began swapping hulls — the
/// same fault `HullWaterClip` had. On a big rung the bow and beam emitters end
/// up INSIDE the hull, and a burst born inside the hull comes up through the
/// deck, which is what Kevin sees in heavy water.
///
/// This walks the ladder and measures where the emitters actually are, in the
/// hull's own local frame, against the hull standing in them. It reads the
/// SHIPPED ARTEFACT — the transforms after a real `Shipyard.Apply` — rather
/// than re-running the fractions that placed them, because a check that
/// re-applies the rule under test can only ever agree with it.
///
/// Two gates, both about the same thing:
///   OUTBOARD  every emitter that throws water sideways must be at or beyond
///             the inboard ellipse `HullWaterClip` keeps the sea out of
///             (0.41 x beam, 0.40 x length). Inside it is inside the boat.
///   DRY       every emitter must sit at or above the waterline (local y = 0),
///             so nothing is born under the surface as she settles.
///
/// Play mode, Sea.unity. Writes /tmp/seasick-sprayrig.txt.
public class SprayRigCheck : MonoBehaviour
{
    static bool running;

    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("SprayRigCheck: not in play mode"); return; }
        if (running) { Debug.LogError("SprayRigCheck: already running"); return; }
        running = true;
        new GameObject("SprayRigCheck").AddComponent<SprayRigCheck>();
    }

    // The rungs worth walking: the smallest, a middle one, and the biggest.
    // The fault is at BOTH ends and in opposite directions — on the skiff the
    // old bow emitter was 4.7 m ahead of her stem, on the ship of the line it
    // was amidships — so one rung proves nothing.
    static readonly int[] Rungs = { 0, 6, 12, 19 };

    IEnumerator Start()
    {
        var sb = new StringBuilder();
        System.IO.File.WriteAllText("/tmp/seasick-sprayrig.txt", "SprayRigCheck: did not finish\n");

        var yard = FindAnyObjectByType<Shipyard>();
        var juice = FindAnyObjectByType<SpeedJuice>();
        if (yard == null || juice == null)
        { Finish(sb, "ABORT: no Shipyard or no SpeedJuice in the scene"); yield break; }

        if (ShipLadder.Count == 0)
        { Finish(sb, "ABORT: the ladder did not load"); yield break; }

        int wasNode = yard.NodeIndex;
        sb.AppendLine("SprayRigCheck — where her spray actually leaves her");
        sb.AppendLine();

        int failures = 0;
        foreach (int r in Rungs)
        {
            if (r >= ShipLadder.Count) continue;
            yard.Apply(r);
            yield return new WaitForSeconds(0.5f);
            var n = ShipLadder.Node(r);
            if (n == null) continue;

            sb.AppendLine($"=== rung {r} {n.name}: LOA {n.length:F1} beam {n.beam:F1} "
                        + $"rail {n.RailY:F2} (draft {n.draft:F2}, depth {n.depth:F2}) ===");
            // The ellipse the sea is held out of, in the same local units.
            float ax = n.beam * 0.41f, az = n.length * 0.40f;
            sb.AppendLine($"    inboard ellipse (the hole in the sea): {ax:F2} x {az:F2} m");

            var root = juice.transform.Find("FoamEmitters");
            if (root == null) { sb.AppendLine("    *** no FoamEmitters root ***"); failures++; continue; }

            foreach (Transform e in root)
            {
                Vector3 p = e.localPosition;
                // Position in the hull frame: the emitter root is lifted by
                // however far she has settled, so add that back out.
                float plan = (p.x * p.x) / (ax * ax) + (p.z * p.z) / (az * az);
                bool outboard = plan >= 1f;
                bool dry = p.y + root.localPosition.y >= -0.01f;
                // Only the sideways throwers have to be outboard. The wake and
                // the stern wash sit behind her and are allowed to be on the
                // centreline — what they must not be is under water.
                bool needsOutboard = e.name.Contains("Bow") || e.name.Contains("Shoulder")
                                  || e.name.Contains("Beam");
                bool ok = (!needsOutboard || outboard) && dry;
                if (!ok) failures++;
                sb.AppendLine($"    {(ok ? "ok  " : "FAIL")} {e.name,-14} "
                            + $"local ({p.x,6:F2}, {p.y,5:F2}, {p.z,6:F2})  "
                            + $"plan {plan,5:F2} {(outboard ? "outboard" : "INBOARD")}"
                            + (dry ? "" : "  UNDER WATER"));
            }
            sb.AppendLine();
        }

        yard.Apply(wasNode);
        sb.AppendLine(failures == 0
            ? "PASS: every emitter that throws water sideways is outboard of the planking, "
            + "and none of them starts under the surface."
            : $"FAIL: {failures} emitter placements are wrong.");
        Finish(sb, "");
    }

    static void Finish(StringBuilder sb, string tail)
    {
        if (!string.IsNullOrEmpty(tail)) sb.AppendLine(tail);
        System.IO.File.WriteAllText("/tmp/seasick-sprayrig.txt", sb.ToString());
        Debug.Log("SprayRigCheck:\n" + sb);
        running = false;
    }
}
