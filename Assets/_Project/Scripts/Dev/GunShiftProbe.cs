using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using SeaSick.Combat;
using SeaSick.Ship;

namespace SeaSick.Dev
{
    /// **Do the gunners walk to the engaged side? (2026-10-03, play mode.)**
    ///
    /// Checks Kevin's "gunners walk to the engaged side" on the live deck
    /// (`CannonBattery.RebalanceCrews`, `CrewAgent.RelocateStation`; the
    /// pure rules are `GunCrewShift.SelfTest`). Needs a running voyage
    /// (`GameBoot.Skip()` first; keep `SaveGame.Suppressed`).
    ///
    /// 1. Short-hands the battery: puts named gunners on jolly-boat duty
    ///    (`BeginJollyBoatDuty`, the game's own "off the roster" state; they
    ///    stand where they are and `CanCrewGun` goes false) until
    ///    guns-per-side + 1 gunners are left, starboard-homed hands first,
    ///    so the starboard side is the short one.
    /// 2. A raider pinned abeam to STARBOARD (`EnemyShip`, built like
    ///    `EnemyShip.Spawn`, held in place every LateUpdate, its own guns
    ///    held and its damage zeroed so it neither shoots nor sinks).
    /// 3. A second raider to PORT and the starboard one LOCKED (`CombatLock`).
    /// 4. Lock released, both removed: walk home.
    /// Sampled once a second: engaged sides, Port/StarboardManned, and per
    /// gunner his state and the gun he is at. Restores the hands at the end.
    ///
    /// `GunShiftProbe.Run()` launches; `GunShiftProbe.Report()` reads the
    /// timeline so far (also written to /tmp/seasick-gunshift.txt).
    public class GunShiftProbe : MonoBehaviour
    {
        const string OutPath = "/tmp/seasick-gunshift.txt";
        static GunShiftProbe inst;
        static readonly StringBuilder log = new StringBuilder();
        public static bool Done { get; private set; }
        public static string ShotPath = "";

        CannonBattery battery;
        Crew.CrewRoster roster;
        readonly List<Crew.CrewAgent> gunners = new List<Crew.CrewAgent>();
        readonly Dictionary<Crew.CrewAgent, int> home = new Dictionary<Crew.CrewAgent, int>();
        readonly List<Crew.CrewAgent> benched = new List<Crew.CrewAgent>();
        readonly List<(EnemyShip ship, Vector3 offset)> pinned = new List<(EnemyShip, Vector3)>();
        readonly Dictionary<Crew.CrewAgent, string> lastState = new Dictionary<Crew.CrewAgent, string>();
        readonly Dictionary<Crew.CrewAgent, float> relocStart = new Dictionary<Crew.CrewAgent, float>();
        int compareApprox, errors, exceptions;
        string firstError = "";
        float phaseStart;

        static readonly FieldInfo ReadyAt = typeof(EnemyShip).GetField("readyAt", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo Damage = typeof(EnemyShip).GetField("damage", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo Authored = typeof(CannonBattery).GetField("authoredBattery", BindingFlags.Instance | BindingFlags.NonPublic);

        public static string Run(string shotPath = "")
        {
            if (inst != null) Destroy(inst.gameObject);
            log.Clear();
            Done = false;
            ShotPath = shotPath ?? "";
            var go = new GameObject("GunShiftProbe");
            inst = go.AddComponent<GunShiftProbe>();
            return "started";
        }

        public static string Report() => (Done ? "[done]\n" : "[running]\n") + log.ToString();

        void OnEnable() => Application.logMessageReceived += OnLog;
        void OnDisable() => Application.logMessageReceived -= OnLog;

        void OnLog(string msg, string stack, LogType type)
        {
            if (msg != null && msg.Contains("CompareApproximately")) { compareApprox++; return; }
            if (type == LogType.Exception) { exceptions++; if (firstError == "") firstError = Trim(msg); }
            else if (type == LogType.Error || type == LogType.Assert) { errors++; if (firstError == "") firstError = Trim(msg); }
        }

        static string Trim(string s) => s == null ? "" : (s.Length > 160 ? s.Substring(0, 160) : s);

        void Line(string s)
        {
            log.AppendLine(s);
            try { System.IO.File.WriteAllText(OutPath, log.ToString()); } catch { }
        }

        void Start() => StartCoroutine(Go());

        IEnumerator Go()
        {
            // ---- find the player's battery and crew
            float give = Time.time + 15f;
            while (Time.time < give)
            {
                battery = FindFirstObjectByType<CannonBattery>();
                if (battery != null) roster = battery.GetComponent<Crew.CrewRoster>();
                if (battery != null && roster != null && battery.TotalGuns > 0 && roster.All.Length > 0) break;
                yield return new WaitForSeconds(0.5f);
            }
            if (battery == null || roster == null || battery.TotalGuns == 0)
            {
                Line("ABORT: no battery/roster/guns (battery=" + (battery != null) + ")");
                Done = true; yield break;
            }

            bool authored = Authored != null && (bool)Authored.GetValue(battery);
            Line($"ship={battery.name} guns={battery.TotalGuns} (P{battery.PortCount}/S{battery.StarboardCount}) authored={authored} crew={roster.All.Length} range={battery.GunRange:F0}m");

            var all = roster.All;
            int homeGuns = authored ? (battery.TotalGuns + 1) / 2 : battery.TotalGuns;
            for (int i = 0; i < homeGuns; i++)
            {
                var h = roster.GunCrew(i);
                if (h == null) continue;
                gunners.Add(h);
                home[h] = authored ? Mathf.Min(2 * i + 1, battery.TotalGuns - 1) : i;
            }
            var sb = new StringBuilder("gunners:");
            foreach (var h in gunners)
                sb.Append($" {Tag(h)}=home {GunName(home[h])} {h.StateName}{(h.CanCrewGun ? "" : "(!crew)")}");
            Line(sb.ToString());

            // Wait for everyone to settle at his post (fresh boot).
            give = Time.time + 8f;
            while (Time.time < give)
            {
                bool ok = true;
                foreach (var h in gunners) if (!h.Available && h.CanCrewGun) ok = false;
                if (ok) break;
                yield return new WaitForSeconds(0.5f);
            }

            // ---- short-hand the battery: starboard-homed first
            int keep = Mathf.Min(battery.GunsPerSide + 1, gunners.Count - 1);
            if (authored) keep = gunners.Count; // pairs: one hand per two guns already
            var order = new List<Crew.CrewAgent>(gunners);
            order.Sort((a, b) => SideOfGun(home[b]).CompareTo(SideOfGun(home[a]))); // starboard (1) first
            int eligible = 0;
            foreach (var h in gunners) if (h.CanCrewGun) eligible++;
            foreach (var h in order)
            {
                if (eligible <= keep) break;
                if (!h.CanCrewGun) continue;
                if (h.BeginJollyBoatDuty()) { benched.Add(h); eligible--; }
            }
            sb.Clear(); sb.Append($"benched (jolly-boat duty) {benched.Count}:");
            foreach (var h in benched) sb.Append(" " + Tag(h));
            sb.Append($" -> {eligible} eligible gunners for {battery.TotalGuns} guns");
            Line(sb.ToString());
            yield return new WaitForSeconds(1.5f);   // a rebalance tick with no fight: nobody moves
            Sample("pre");

            float reach = Mathf.Max(battery.GunRange, 40f) * 1.5f;
            float abeam = Mathf.Clamp(battery.GunRange * 0.6f, 20f, reach * 0.6f);
            Line($"raiders pinned {abeam:F0} m abeam (engage reach {reach:F0} m)");

            // ---- A: raider to starboard
            var star = MakeRaider("ProbeRaider_Stbd", new Vector3(abeam, 0f, 0f));
            Line("== A: raider STARBOARD (no lock)");
            phaseStart = Time.time;
            bool shot = false;
            for (int s = 0; s <= 16; s++)
            {
                Sample("A");
                if (!shot && ShotPath.Length > 0 && AnyRelocating())
                {
                    shot = true;
                    ScreenCapture.CaptureScreenshot(ShotPath);
                    Line("  (screenshot " + ShotPath + ")");
                }
                yield return new WaitForSeconds(1f);
            }

            // ---- B: second raider to port, lock the starboard one
            var portR = MakeRaider("ProbeRaider_Port", new Vector3(-abeam, 0f, 0f));
            var cl = battery.GetComponent<CombatLock>();
            if (cl == null) cl = FindFirstObjectByType<CombatLock>();
            bool locked = cl != null && cl.LockOn(star);
            Line($"== B: raider PORT too, lock STARBOARD ({(locked ? "locked" : "LOCK FAILED")})");
            phaseStart = Time.time;
            for (int s = 0; s <= 12; s++) { Sample("B"); yield return new WaitForSeconds(1f); }

            // ---- C: fight over
            if (cl != null && cl.Locked != null) cl.ToggleLock();
            foreach (var p in pinned) if (p.ship != null) Destroy(p.ship.gameObject);
            pinned.Clear();
            Line("== C: lock released, both raiders removed");
            phaseStart = Time.time;
            for (int s = 0; s <= 14; s++) { Sample("C"); yield return new WaitForSeconds(1f); }

            // ---- restore
            foreach (var h in benched) if (h != null) h.EndJollyBoatDuty();
            Line($"restored {benched.Count} hands; managed console during run (native asserts such as CompareApproximately bypass logMessageReceived -- read `unity cmd console`): CompareApproximately={compareApprox} errors={errors} exceptions={exceptions} first='{firstError}'");
            Done = true;
            Destroy(gameObject, 0.1f);
        }

        EnemyShip MakeRaider(string n, Vector3 offset)
        {
            var go = new GameObject(n);
            go.transform.position = battery.transform.TransformPoint(offset);
            go.transform.rotation = Quaternion.Euler(0f, battery.transform.eulerAngles.y, 0f);
            var e = go.AddComponent<EnemyShip>();
            e.Configure(null, 100f, 1);
            pinned.Add((e, offset));
            Hold(e);
            return e;
        }

        void Hold(EnemyShip e)
        {
            ReadyAt?.SetValue(e, float.MaxValue);
            Damage?.SetValue(e, 0);
        }

        void LateUpdate()
        {
            if (battery == null) return;
            foreach (var p in pinned)
            {
                if (p.ship == null) continue;
                Vector3 w = battery.transform.TransformPoint(p.offset);
                var t = p.ship.transform;
                t.position = new Vector3(w.x, t.position.y, w.z);
                t.rotation = Quaternion.Euler(t.eulerAngles.x, battery.transform.eulerAngles.y, t.eulerAngles.z);
                Hold(p.ship);
            }

            // Walk timing: Relocating start -> back to Station.
            foreach (var h in gunners)
            {
                if (h == null) continue;
                string st = h.StateName;
                lastState.TryGetValue(h, out string was);
                if (st != was)
                {
                    if (st == "Relocating") relocStart[h] = Time.time;
                    else if (was == "Relocating" && relocStart.TryGetValue(h, out float t0))
                        Line($"  [{Time.time - phaseStart:F1}s] {Tag(h)} arrived {GunName(GunIndex(h))} after {Time.time - t0:F1}s walk -> {st}");
                    lastState[h] = st;
                }
            }
        }

        bool AnyRelocating()
        {
            foreach (var h in gunners) if (h != null && h.IsRelocating) return true;
            return false;
        }

        void Sample(string phase)
        {
            string Side(int s) => s == GunCrewShift.Port ? "P" : s == GunCrewShift.Starboard ? "S" : "-";
            var sb = new StringBuilder();
            sb.Append($"{phase} t={Time.time - phaseStart,4:F0} eng={Side(battery.EngagedSide)}/{Side(battery.EngagedSecondSide)} manned P{battery.PortManned} S{battery.StarboardManned} |");
            foreach (var h in gunners)
            {
                if (h == null) continue;
                string st = h.StateName;
                st = st == "Station" ? "Sta" : st == "Relocating" ? "REL" : st == "JollyBoatDuty" ? "jb" : st;
                sb.Append($" {Tag(h)}:{st}@{GunName(GunIndex(h))}");
            }
            Line(sb.ToString());
        }

        static string Tag(Crew.CrewAgent h)
        {
            string n = h.DisplayName;
            return string.IsNullOrEmpty(n) ? "?" : (n.Length > 4 ? n.Substring(0, 4) : n);
        }

        int GunIndex(Crew.CrewAgent h)
        {
            var g = h.Gun;
            if (g == null) return -1;
            var guns = battery.Guns;
            for (int i = 0; i < guns.Count; i++) if (guns[i] == g) return i;
            return -1;
        }

        int SideOfGun(int i)
        {
            var guns = battery.Guns;
            if (i < 0 || i >= guns.Count || guns[i] == null) return GunCrewShift.None;
            return battery.transform.InverseTransformPoint(guns[i].transform.position).x >= 0f
                ? GunCrewShift.Starboard : GunCrewShift.Port;
        }

        string GunName(int i) => i < 0 ? "none" : (SideOfGun(i) == GunCrewShift.Starboard ? "S" : "P") + i;
    }
}
