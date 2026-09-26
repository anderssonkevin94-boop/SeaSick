using System.Collections.Generic;
using System.Text;
using SeaSick.Combat;
using SeaSick.World;
using UnityEngine;

/// **Do raiders stay off the land, and do the landing party stand on it?**
/// Kevin, 2026-09-26 (phone): *"enemy ships are sailing through my island,
/// and I never see the raiders walking on land."*
///
/// Samples every raider's hull (bow, centre, stern at the keel) and every
/// raid walker against `Island.TerrainHeight` a few times a second and
/// counts the two things the complaint is about: a keel point with ground
/// above it (a hull inside an island) and a walker whose feet are under the
/// sea. A beached raider is exempt from the hull count only near her own
/// landing point, which is the one place she is meant to touch bottom.
///
/// `RaidTrace.Begin()` starts it, `RaidTrace.Raid("Island_2")` sends that
/// island's raider at its camp now (the 25 s clock and the watched check
/// skipped), `RaidTrace.Report()` returns the counts and writes the log to
/// `Logs/raid-trace.txt`. Play mode, Sea.unity.
public class RaidTrace : MonoBehaviour
{
    const float Every = 0.2f;
    const float Draught = 1.5f;
    const float BeachZone = 30f;

    static RaidTrace live;

    readonly StringBuilder log = new StringBuilder();
    float next;
    int hullSamples, hullAground, raidHullSamples, raidHullAground;
    int walkerSamples, walkerOnLand, walkerUnderSea;
    float worstRise = float.NegativeInfinity;
    string worstAt = "";
    float started;

    public static string Begin()
    {
        if (!Application.isPlaying) return "not playing";
        if (live != null) Destroy(live.gameObject);
        live = new GameObject("RaidTrace").AddComponent<RaidTrace>();
        live.started = Time.time;
        return "tracing " + EnemyShip.All.Count + " raiders";
    }

    /// Send `islandName`'s raider at the camp on it, now.
    public static string Raid(string islandName)
    {
        foreach (var camp in Outpost.All)
        {
            if (camp == null || camp.Island == null || camp.Island.name != islandName) continue;
            if (!camp.HasCamp) return islandName + ": no camp";
            var ship = EnemyShip.IdleAt(camp.Island);
            if (ship == null) return islandName + ": no idle raider homed here";
            if (!camp.ShoreNear(camp.CampCentre, out Vector3 shore, out Vector3 water))
                return islandName + ": no shore near the camp";
            ship.BeginRaid(new RaidSite { camp = camp, water = water, shore = shore });
            return $"{ship.name} raiding {islandName}: from {ship.transform.position} to water {water} shore {shore}";
        }
        return islandName + ": no outpost";
    }

    public static string Report()
    {
        if (live == null) return "not tracing";
        var t = live;
        string head =
            $"RaidTrace {Time.time - t.started:F0}s: hull aground {t.hullAground}/{t.hullSamples} (patrol), " +
            $"{t.raidHullAground}/{t.raidHullSamples} (raiding, outside the beach zone); " +
            $"walkers on land {t.walkerOnLand}/{t.walkerSamples}, under the sea {t.walkerUnderSea}; " +
            $"worst ground above keel {t.worstRise:F1} m {t.worstAt}";
        System.IO.Directory.CreateDirectory("Logs");
        System.IO.File.WriteAllText("Logs/raid-trace.txt", head + "\n" + t.log);
        return head;
    }

    void Update()
    {
        var h = Island.TerrainHeight;
        if (h == null || Time.time < next) return;
        next = Time.time + Every;

        foreach (var r in EnemyShip.All)
        {
            if (r == null || !r.Alive) continue;
            Vector3 p = r.transform.position;
            Vector3 f = r.transform.forward; f.y = 0f;
            f = f.sqrMagnitude > 0.01f ? f.normalized : Vector3.forward;
            float half = r.HitAxis.magnitude;
            float keel = p.y - Draught;
            float rise = float.NegativeInfinity;
            foreach (float k in new[] { -1f, 0f, 1f })
            {
                Vector3 q = p + f * (half * k);
                rise = Mathf.Max(rise, h(q.x, q.z) - keel);
            }
            bool nearBeach = r.Raiding
                && Vector2.Distance(new Vector2(p.x, p.z), new Vector2(r.Site.water.x, r.Site.water.z)) < BeachZone;
            bool aground = rise > 0f;
            if (r.Raiding) { if (!nearBeach) { raidHullSamples++; if (aground) raidHullAground++; } }
            else { hullSamples++; if (aground) hullAground++; }
            if (!nearBeach && rise > worstRise) { worstRise = rise; worstAt = $"{r.name} at {p} ({r.Current})"; }
            log.AppendLine($"{Time.time:F1} {r.name} {r.Current} beached={r.Beached} pos=({p.x:F1},{p.y:F2},{p.z:F1}) groundAboveKel={rise:F2}{(aground && !nearBeach ? " AGROUND" : "")}");
        }

        foreach (var w in FindObjectsByType<RaidWalker>(FindObjectsSortMode.None))
        {
            Vector3 p = w.transform.position;
            float g = h(p.x, p.z);
            walkerSamples++;
            if (g >= -0.3f) walkerOnLand++; else walkerUnderSea++;
            float toCamp = w.camp != null ? Vector3.Distance(Flat(p), Flat(w.camp.CampCentre)) : -1f;
            log.AppendLine($"{Time.time:F1} walker {w.GetInstanceID()} {w.phase} pos=({p.x:F1},{p.y:F2},{p.z:F1}) ground={g:F2} toCamp={toCamp:F0}");
        }
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
