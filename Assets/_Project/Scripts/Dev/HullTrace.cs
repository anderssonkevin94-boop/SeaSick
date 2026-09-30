using System.IO;
using System.Text;
using SeaSick.Combat;
using SeaSick.Ocean;
using SeaSick.Ship;
using SeaSick.World;
using UnityEngine;

namespace SeaSick.Dev
{
    /// **Where is the hull going?** (2026-09-30.) Added at runtime by eval;
    /// logs every `HullIntegrity.Damaged` event with its source plus the
    /// ship's position, depth under the keel, local Hs, surf level, anchor
    /// state and the nearest reef/raider, and a once-a-second trace of the
    /// same so a loss with no event (a path that skips `Report`) still shows
    /// as a step in the integrity column. Written for the "ground on the
    /// breakers" / "hull bleeds at anchor" hunt; `HullTrace.Start(path)`.
    public class HullTrace : MonoBehaviour
    {
        public static string Path = "/tmp/seasick-hulltrace.txt";
        readonly StringBuilder sb = new StringBuilder();
        HullIntegrity hull;
        float next;
        float t0;

        public static HullTrace Begin(string path)
        {
            var old = FindFirstObjectByType<HullTrace>();
            if (old != null) Destroy(old.gameObject);
            Path = path;
            var go = new GameObject("HullTrace");
            return go.AddComponent<HullTrace>();
        }

        void OnEnable() { HullIntegrity.Damaged += OnDamaged; t0 = Time.time; }
        void OnDisable() { HullIntegrity.Damaged -= OnDamaged; Flush(); }

        public void Mark(string note) { sb.AppendLine($"[{Time.time - t0,7:F2}] MARK {note} | {State()}"); Flush(); }

        string State()
        {
            if (hull == null) hull = FindFirstObjectByType<ShipMotor>()?.GetComponent<HullIntegrity>();
            if (hull == null) return "no ship";
            var m = hull.GetComponent<ShipMotor>();
            var b = hull.GetComponent<BuoyantBody>();
            var br = hull.GetComponent<Breakers>();
            var ac = hull.GetComponent<AnchorController>();
            Vector3 p = hull.transform.position;
            float surf = b != null ? b.MeanWaterHeight : 0f;
            float ground = Island.TerrainHeight != null ? Island.TerrainHeight(p.x, p.z) : float.NaN;
            float hs = SeaStateController.Instance != null ? SeaStateController.Instance.SeaHsAt(new Vector2(p.x, p.z)) : -1f;
            var reef = Reef.Nearest(p);
            float reefD = reef != null ? Vector3.Distance(Flat(reef.transform.position), Flat(p)) - reef.Radius : -1f;
            float raidD = 1e9f;
            foreach (var r in EnemyShip.All) if (r != null && r.Alive) raidD = Mathf.Min(raidD, Vector3.Distance(Flat(r.transform.position), Flat(p)));
            var rb = hull.GetComponent<Rigidbody>();
            return $"hull={hull.Integrity01:F4} pos=({p.x:F1},{p.y:F2},{p.z:F1}) depth={surf - ground:F2} hs={hs:F2} surf01={(br != null ? br.Breaking01 : -1f):F2} " +
                   $"v={(rb != null ? rb.linearVelocity.magnitude : 0f):F2} anch={(m != null && m.Anchored)} st={(ac != null ? ac.CurrentState.ToString() : "?")} " +
                   $"reefEdge={reefD:F1}{(reef != null ? "(" + reef.name + " r" + reef.Radius.ToString("F1") + ")" : "")} raider={raidD:F0}";
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        void OnDamaged(HullIntegrity h, string src, float lost, Vector3 at, float speed)
        {
            sb.AppendLine($"[{Time.time - t0,7:F2}] DMG {src} lost={lost:F5} speed={speed:F2} at=({at.x:F1},{at.z:F1}) | {State()}");
        }

        void Update()
        {
            if (Time.time < next) return;
            next = Time.time + 1f;
            sb.AppendLine($"[{Time.time - t0,7:F2}] tick | {State()}");
            Flush();
        }

        void Flush()
        {
            if (sb.Length == 0) return;
            File.AppendAllText(Path, sb.ToString());
            sb.Clear();
        }
    }
}
