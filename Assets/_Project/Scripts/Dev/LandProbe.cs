using System.Text;
using UnityEngine;
using SeaSick.World;

/// Can you actually land on anything out there?
///
/// Walks every island, stands a virtual ship 20 m off its shore on 36
/// bearings, and asks the SAME three questions `AnchorController` asks in
/// that order -- which island does it think it is beside, is the ship inside
/// that island's reach, and does that island have a beach on this bearing.
/// Reporting only the last one would say "beach: yes" on a shore where no
/// prompt ever appears, because the first question already picked a
/// different island.
///
/// The shore slope is ALSO measured straight off the height field here, so
/// the island's own `hasBeach` answer has something independent to disagree
/// with. Disagreement between two places that should agree is the tell.
public class LandProbe : MonoBehaviour
{
    const int Bearings = 36;
    const float StandOff = 20f;

    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("LandProbe: not in play mode"); return; }
        if (Island.TerrainHeight == null) { Debug.LogError("LandProbe: no height function"); return; }
        var sb = new StringBuilder();
        var home = Island.All.Find(i => i.IsHome);
        Vector3 hp = home != null ? home.transform.position : Vector3.zero;

        sb.AppendLine($"beachMaxSlope {Island.BeachMaxSlope}   {Island.All.Count} islands");
        sb.AppendLine("per island: how many of 36 approach bearings give a LAND prompt, and why not");
        sb.AppendLine();

        int anyLandable = 0, anyHarvestable = 0;
        foreach (var isle in Island.All)
        {
            if (isle == null || isle.IsHome) continue;
            int water = 0, wrongIsland = 0, outOfReach = 0, noBeach = 0, ok = 0;
            int slopeSaysBeach = 0;

            for (int b = 0; b < Bearings; b++)
            {
                float ang = b / (float)Bearings * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang));
                Vector3 shore = isle.transform.position + dir * isle.RadiusAt(ang);
                Vector3 ship = isle.transform.position + dir * (isle.RadiusAt(ang) + StandOff);
                if (Island.TerrainHeight(ship.x, ship.z) > -0.3f) continue;   // aground, not a berth
                water++;

                // Straight off the terrain: how fast does it rise over the
                // first 12 m inland of the waterline on this bearing?
                float rise = (Island.TerrainHeight(shore.x - dir.x * 12f, shore.z - dir.z * 12f)
                            - Island.TerrainHeight(shore.x, shore.z)) / 12f;
                if (rise < Island.BeachMaxSlope) slopeSaysBeach++;

                // ...and now exactly what the ship would be offered.
                var think = Island.Nearest(ship);
                if (think != isle) { wrongIsland++; continue; }
                float reach = think.RadiusToward(ship) + 30f;
                if (Island.FlatDistance(ship, think.transform.position) > reach) { outOfReach++; continue; }
                if (!think.HasBeachToward(ship)) { noBeach++; continue; }
                ok++;
            }

            float dist = Island.FlatDistance(isle.transform.position, hp);
            sb.AppendLine($"{isle.name,-14} {dist,5:F0} m out   r{isle.MaxRadius,4:F0}   "
                + $"{isle.ResourceName,-7} {isle.Remaining,3:F0} left   "
                + $"LAND on {ok,2}/{water,2}   "
                + $"(wrong island {wrongIsland}, out of reach {outOfReach}, no beach {noBeach})   "
                + $"terrain says beach on {slopeSaysBeach}");
            if (ok > 0) anyLandable++;
            if (ok > 0 && isle.HasResources) anyHarvestable++;
        }

        sb.AppendLine();
        sb.AppendLine($"islands you can land on at all: {anyLandable}");
        sb.AppendLine($"islands you can land on AND harvest: {anyHarvestable}");
        sb.AppendLine($"resource nodes alive in the world: {ResourceNode.All.Count}");

        Debug.Log("LAND PROBE\n" + sb);
        System.IO.File.WriteAllText("/tmp/land-probe.txt", sb.ToString());
    }

    /// Sail the whole gathering half for real: cast off, stand off the
    /// nearest island with timber, land, and watch for thirty seconds.
    ///
    /// It drives the ship and then presses the same buttons the player
    /// presses -- it never calls `SendAshore` or moves a crew member itself.
    /// The failure being hunted is one where the prompt is offered and
    /// nothing happens after it, which only shows up if the real chain runs.
    public static void Sail() => new GameObject("LandProbeSail").AddComponent<LandProbe>();

    System.Collections.IEnumerator Start()
    {
        var sb = new StringBuilder();
        var motor = FindFirstObjectByType<SeaSick.Ship.ShipMotor>();
        var anchor = motor != null ? motor.GetComponent<SeaSick.Ship.AnchorController>() : null;
        var voyage = FindFirstObjectByType<SeaSick.Voyage.VoyageManager>();
        var gangway = motor != null ? motor.GetComponent<SeaSick.Ship.Gangway>() : null;
        var crew = motor != null ? motor.GetComponentsInChildren<SeaSick.Crew.CrewAgent>(true) : null;
        if (anchor == null || voyage == null) { Debug.LogError("LandProbe.Sail: missing ship"); yield break; }

        // The nearest island that has something on it.
        Island target = null; float best = float.MaxValue;
        foreach (var i in Island.All)
        {
            if (i == null || i.IsHome || !i.HasResources) continue;
            float d = Island.FlatDistance(i.transform.position, motor.transform.position);
            if (d < best) { best = d; target = i; }
        }
        if (target == null) { Debug.LogError("LandProbe.Sail: nothing to harvest"); yield break; }

        sb.AppendLine($"panel up at the start: {voyage.AtHome}   anchor {anchor.CurrentState}");
        anchor.CastOff();
        yield return null;

        // Stand her off a bearing the survey said was landable.
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

        sb.AppendLine($"standing off {target.name} ({target.ResourceName}, {target.Remaining:F0} left)"
            + $" at {Island.FlatDistance(motor.transform.position, c):F0} m,"
            + $" speed {motor.CurrentSpeed:F1}, panel up {voyage.AtHome}");

        bool landed = anchor.TryLand(out string why);
        sb.AppendLine($"press LAND -> {landed}   ({why})");

        int held0 = voyage.TotalHeld;
        for (float t = 0f; t < 30f; t += 1f)
        {
            int ashore = 0;
            if (crew != null) foreach (var cr in crew) if (cr != null && !cr.IsAboard) ashore++;
            sb.AppendLine($"  t+{t,2:F0}s  state {anchor.CurrentState,-9} "
                + $"gangway {(gangway != null ? gangway.Ready.ToString() : "n/a"),-5} "
                + $"crew ashore {ashore}   hold {voyage.TotalHeld}   "
                + $"nodes left on it {ResourceNode.CountFree(target)}");
            yield return new WaitForSeconds(1f);
        }
        sb.AppendLine($"gathered in 30 s: {voyage.TotalHeld - held0}");

        Debug.Log("LAND PROBE — SAIL\n" + sb);
        System.IO.File.WriteAllText("/tmp/land-sail.txt", sb.ToString());
        Destroy(gameObject);
    }
}
