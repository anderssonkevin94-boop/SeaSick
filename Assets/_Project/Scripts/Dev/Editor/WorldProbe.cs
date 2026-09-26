using System.Collections;
using System.Text;
using UnityEngine;
using SeaSick.Terrain;
using SeaSick.World;

/// Play-mode gate (Sea.unity) for TerrainWorldPopulator: islands discovered,
/// a home island with a Stockpile, every island centre on land and every
/// outline point at the waterline with land just inside, beaches exist,
/// props stand on the ground above the beach band, reefs and monsters sit
/// in water, raiders posted, and the spawn's nearest island offers a beach.
public class WorldProbe : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("WorldProbe: not in play mode"); return; }
        new GameObject("WorldProbe").AddComponent<WorldProbe>();
    }

    IEnumerator Start()
    {
        StringBuilder sb = new StringBuilder();
        int fails = 0;
        TerrainWorldPopulator pop = FindAnyObjectByType<TerrainWorldPopulator>();
        // Real time, and long: the build is sliced across frames (~25 s in
        // the editor) and the boot overlay holds timeScale at 0.
        float t0 = Time.realtimeSinceStartup;
        while ((pop == null || !pop.Done) && Time.realtimeSinceStartup - t0 < 90f) { yield return null; pop = FindAnyObjectByType<TerrainWorldPopulator>(); }
        if (pop == null || !pop.Done) { Finish(sb, 1, "populator never finished"); yield break; }
        var h = Island.TerrainHeight;

        Gate(sb, ref fails, "islands-found", pop.IslandCount >= 3, pop.IslandCount + " islands within " + pop.world.discoveryRadius + " m");
        Gate(sb, ref fails, "home-island", pop.Home != null && pop.Home.IsHome && pop.Home.GetComponent<Stockpile>() != null,
            pop.Home != null ? pop.Home.name + " at " + pop.Home.transform.position.ToString("F0") + " meanR=" + pop.Home.Radius.ToString("F0") : "none");

        int centreWet = 0, outlineBad = 0, beachSectors = 0, sectors = 0, withRes = 0, propsOff = 0, props = 0, propsLow = 0;
        foreach (var isle in Island.All)
        {
            Vector3 c = isle.transform.position;
            if (h(c.x, c.z) <= 0f) centreWet++;
            for (int s = 0; s < Island.Sectors; s += 3)
            {
                float ang = s / (float)Island.Sectors * Mathf.PI * 2f;
                float r = isle.RadiusAt(ang);
                Vector3 dir = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang));
                float hOut = h(c.x + dir.x * (r + 3f), c.z + dir.z * (r + 3f));
                float hIn = h(c.x + dir.x * (r - 4f), c.z + dir.z * (r - 4f));
                if (!(hOut < 0.5f && hIn > -1.5f)) outlineBad++;
                sectors++;
                if (isle.HasBeachToward(c + dir * (r + 20f))) beachSectors++;
            }
            if (isle.HasResources) withRes++;
            foreach (var node in isle.GetComponentsInChildren<ResourceNode>())
            {
                props++;
                Vector3 p = node.transform.position;
                float ground = h(p.x, p.z);
                if (Mathf.Abs(p.y - ground) > 0.5f) propsOff++;
                if (ground < 1.5f) propsLow++;
            }
        }
        Gate(sb, ref fails, "centres-on-land", centreWet == 0, centreWet + " of " + Island.All.Count + " centres below sea level");
        Gate(sb, ref fails, "outline-at-waterline", outlineBad <= sectors / 20, outlineBad + " of " + sectors + " sampled sectors not at the shoreline");
        Gate(sb, ref fails, "beaches-exist", beachSectors > sectors / 4, beachSectors + " of " + sectors + " sectors landable");
        Gate(sb, ref fails, "resources", withRes >= 1, withRes + " islands with resources, " + props + " props");
        Gate(sb, ref fails, "props-grounded", propsOff == 0 && propsLow == 0, propsOff + " props off the ground, " + propsLow + " in the beach band");

        int reefsDry = 0;
        foreach (var reef in Reef.All) { Vector3 p = reef.transform.position; if (h(p.x, p.z) > -2f) reefsDry++; }
        Gate(sb, ref fails, "reefs-wet", Reef.All.Count > 0 && reefsDry == 0, Reef.All.Count + " reefs, " + reefsDry + " on land");
        int monstersDry = 0;
        foreach (var m in SeaSick.Combat.SeaMonster.All) { Vector3 p = m.transform.position; if (h(p.x, p.z) > -2f) monstersDry++; }
        Gate(sb, ref fails, "monsters-wet", monstersDry == 0, SeaSick.Combat.SeaMonster.All.Count + " monsters, " + monstersDry + " on land");
        var raiders = FindObjectsByType<SeaSick.Combat.EnemyShip>(FindObjectsSortMode.None);
        Gate(sb, ref fails, "raiders", raiders.Length >= 1, raiders.Length + " raiders posted");

        Vector3 spawn = Vector3.zero;
        Island near = Island.Nearest(spawn);
        Gate(sb, ref fails, "spawn-landable", near != null && near.IsHome && near.HasBeachToward(spawn)
            && Island.FlatDistance(spawn, near.transform.position) <= near.RadiusToward(spawn) + 60f,
            near != null ? near.name + " dist=" + Island.FlatDistance(spawn, near.transform.position).ToString("F0") + " shore=" + near.RadiusToward(spawn).ToString("F0") + " beach=" + near.HasBeachToward(spawn) : "none");

        Finish(sb, fails, null);
    }

    static void Gate(StringBuilder sb, ref int fails, string name, bool ok, string detail)
    {
        sb.AppendLine((ok ? "PASS " : "FAIL ") + name + "  " + detail);
        if (!ok) fails++;
    }

    static void Finish(StringBuilder sb, int fails, string err)
    {
        if (err != null) sb.AppendLine(err);
        sb.Insert(0, (fails == 0 ? "ALL PASS" : fails + " FAIL") + "\n");
        System.IO.File.WriteAllText("/tmp/seasick-world.txt", sb.ToString());
        Debug.Log("WorldProbe:\n" + sb);
    }
}
