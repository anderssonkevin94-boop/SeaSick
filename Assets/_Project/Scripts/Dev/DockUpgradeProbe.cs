using System.Collections;
using System.Text;
using UnityEngine;
using SeaSick.Ship;

/// Does upgrading at the DOCK work?
///
/// It matters because the dock is the only place the yard panel is ever
/// pressed. Every other ladder probe so far — the float gate, the handling
/// walk, the framing — put her at sea first, so a hull swap against a mooring
/// has never actually been measured.
///
/// The question is narrow: after `Apply`, is she still floating where she was,
/// and is she still alongside?
///
/// Walking DOWN the ladder is in here on purpose. `Undo` is a dev convenience
/// and no yard ever shortened a ship, but the probes use it constantly, and a
/// probe that drops the ship through the world quietly poisons every number it
/// prints afterwards.
public class DockUpgradeProbe : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("DockUpgradeProbe: not in play mode"); return; }
        var old = FindAnyObjectByType<DockUpgradeProbe>();
        if (old != null) Destroy(old.gameObject);
        new GameObject("DockUpgradeProbe").AddComponent<DockUpgradeProbe>();
    }

    IEnumerator Start()
    {
        yield return new WaitForSeconds(2f);
        var yard = FindFirstObjectByType<Shipyard>();
        var anchor = yard != null ? yard.GetComponent<AnchorController>() : null;
        var rb = yard != null ? yard.GetComponent<Rigidbody>() : null;
        if (yard == null || rb == null)
        { Debug.LogError("DockUpgradeProbe: no ship"); Destroy(gameObject); yield break; }

        var sb = new StringBuilder("DockUpgradeProbe — pressing the yard buttons at the pier:\n");
        sb.AppendLine($"at the home dock: {(anchor != null ? anchor.AtHomeDock.ToString() : "no AnchorController")}");
        sb.AppendLine("rung  hull                 y before   y +1s   y +4s   drift m   "
                    + "speed m/s   cam y   cam fov");

        foreach (int i in new[] { 12, 0, 4, 8, 12, 19 })
        {
            float before = yard.transform.position.y;
            Vector3 wasAt = yard.transform.position;
            yard.Apply(i);
            yield return new WaitForSeconds(1f);
            float at1 = yard.transform.position.y;
            yield return new WaitForSeconds(3f);
            float at4 = yard.transform.position.y;
            float drift = Vector3.Distance(
                new Vector3(wasAt.x, 0f, wasAt.z),
                new Vector3(yard.transform.position.x, 0f, yard.transform.position.z));

            var cam = Camera.main;
            sb.AppendLine($"{i,4}  {ShipLadder.Node(i).label,-18} {before,9:F2} "
                        + $"{at1,7:F2} {at4,7:F2} {drift,9:F2} {rb.linearVelocity.magnitude,10:F2} "
                        + $"{(cam != null ? cam.transform.position.y : 0f),8:F1} "
                        + $"{(cam != null ? cam.fieldOfView : 0f),8:F1}");
        }
        sb.AppendLine($"still at the home dock: "
                    + $"{(anchor != null ? anchor.AtHomeDock.ToString() : "?")}");
        Debug.Log(sb.ToString());
        Destroy(gameObject);
    }
}
