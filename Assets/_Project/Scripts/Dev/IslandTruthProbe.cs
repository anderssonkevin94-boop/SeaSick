using System.Collections;
using System.Text;
using UnityEngine;
using SeaSick.World;

/// How solid is an island, really?
///
/// Three questions, each answered against the SHIPPED ARTEFACT rather than
/// the rule that produced it:
///
///  1. **Is the drawn ground where the height function says it is?** A
///     raycast hits the streamed chunk's MeshCollider -- the triangles that
///     are actually there -- and that is compared against
///     `TerrainHeight(x, z)`, which is what every gameplay system samples.
///     If those two disagree, everything standing on the analytic surface is
///     floating or buried by the difference.
///  2. **Where are the crew's feet?** Measured off the same raycast, not off
///     the height function they were placed with. Asking the placement rule
///     where the crew are standing is how six of them were once certified as
///     standing perfectly on planking they were 7 cm above.
///  3. **How much of the wood can be cut?** Scenery trees are counted off the
///     baked mesh by trunk colour; harvestable ones are `ResourceNode`s.
public class IslandTruthProbe : MonoBehaviour
{
    static readonly Color32 Trunk = new Color32(92, 64, 40, 255);

    public static void Execute() => new GameObject("IslandTruthProbe").AddComponent<IslandTruthProbe>();

    StringBuilder sb = new StringBuilder();

    IEnumerator Start()
    {
        var motor = FindFirstObjectByType<SeaSick.Ship.ShipMotor>();
        var anchor = motor != null ? motor.GetComponent<SeaSick.Ship.AnchorController>() : null;
        var crew = motor != null ? motor.GetComponentsInChildren<SeaSick.Crew.CrewAgent>(true) : null;
        if (anchor == null) { Debug.LogError("IslandTruthProbe: no ship"); yield break; }

        Island target = null; float best = float.MaxValue;
        foreach (var i in Island.All)
        {
            if (i == null || i.IsHome || !i.HasResources) continue;
            float d = Island.FlatDistance(i.transform.position, motor.transform.position);
            if (d < best) { best = d; target = i; }
        }
        if (target == null) { Debug.LogError("IslandTruthProbe: nothing to land on"); yield break; }

        // --- 3. the wood -------------------------------------------------
        int scenery = 0;
        foreach (var t in target.GetComponentsInChildren<Transform>())
            if (t.name == "Scenery")
            {
                var mesh = t.GetComponent<MeshFilter>().sharedMesh;
                var cols = mesh.colors32;
                bool run = false;
                for (int i = 0; i < cols.Length; i++)
                {
                    bool brown = cols[i].r == Trunk.r && cols[i].g == Trunk.g && cols[i].b == Trunk.b;
                    if (brown && !run) { scenery++; run = true; }
                    else if (!brown) run = false;
                }
            }
        int harvestable = ResourceNode.CountFree(target);
        sb.AppendLine($"{target.name}: {scenery} scenery trees, {harvestable} harvestable "
            + $"({(scenery > 0 ? 100f * harvestable / (scenery + harvestable) : 0f):F1}% of the wood can be cut)");

        // --- get a party ashore ------------------------------------------
        anchor.CastOff();
        yield return null;
        Vector3 c = target.transform.position;
        Vector3 dir = (motor.transform.position - c); dir.y = 0f; dir.Normalize();
        float ang = Mathf.Atan2(dir.x, dir.z);
        Vector3 stand = c + dir * (target.RadiusAt(ang) + 20f);
        stand.y = motor.transform.position.y;
        var rb = motor.GetComponent<Rigidbody>();
        motor.transform.SetPositionAndRotation(stand, Quaternion.LookRotation(-dir));
        if (rb != null) { rb.position = stand; rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        motor.AnchorPoint = stand;
        for (int i = 0; i < 4; i++) yield return null;
        anchor.TryLand(out _);
        yield return new WaitForSeconds(6f);

        // --- 1. analytic surface vs the triangles that are drawn ----------
        Vector3 shore = c + dir * target.RadiusAt(ang);
        float worstMesh = 0f, sumMesh = 0f; int nMesh = 0, missed = 0;
        for (int i = 0; i < 60; i++)
        {
            Vector3 p = shore - dir * (i * 1.5f);        // a line inland
            if (!Ground(p, out float meshY)) { missed++; continue; }
            float analytic = Island.TerrainHeight(p.x, p.z);
            float e = Mathf.Abs(meshY - analytic);
            sumMesh += e; nMesh++;
            if (e > worstMesh) worstMesh = e;
        }
        sb.AppendLine($"drawn ground vs height function, 90 m inland: mean {sumMesh / Mathf.Max(1, nMesh):F3} m, "
            + $"worst {worstMesh:F3} m ({nMesh} hits, {missed} no collider)");

        // --- 2. where the crew's feet actually are ------------------------
        for (float t = 0f; t < 16f; t += 4f)
        {
            float worst = 0f, sum = 0f; int n = 0, air = 0, buried = 0;
            foreach (var cr in crew)
            {
                if (cr == null || cr.IsAboard) continue;
                if (!Ground(cr.transform.position, out float g)) continue;
                float gap = cr.transform.position.y - g;
                sum += gap; n++;
                if (gap > 0.15f) air++;
                if (gap < -0.15f) buried++;
                if (Mathf.Abs(gap) > Mathf.Abs(worst)) worst = gap;
            }
            sb.AppendLine($"  t+{t,2:F0}s  crew ashore {n}   mean gap {(n > 0 ? sum / n : 0f):+0.00;-0.00} m   "
                + $"worst {worst:+0.00;-0.00} m   {air} in the air, {buried} in the ground");
            yield return new WaitForSeconds(4f);
        }

        Debug.Log("ISLAND TRUTH\n" + sb);
        System.IO.File.WriteAllText("/tmp/island-truth.txt", sb.ToString());
        Destroy(gameObject);
    }

    /// The ground as DRAWN: a ray onto the streamed chunk's mesh collider.
    static bool Ground(Vector3 at, out float y)
    {
        y = 0f;
        var from = new Vector3(at.x, at.y + 250f, at.z);
        if (!Physics.Raycast(from, Vector3.down, out var hit, 600f)) return false;
        y = hit.point.y;
        return true;
    }
}
