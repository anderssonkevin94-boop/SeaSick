using System.Collections;
using System.Text;
using SeaSick.Ship;
using UnityEngine;

namespace SeaSick.Combat
{
    /// **Dev: is the swat dodgeable?** (step 2, 2026-10-03). Drives the
    /// player's REAL ship (helm input off, rudder and throttle orders set
    /// directly) from three starts -- stopped, half ahead, full ahead -- and
    /// for each tries the answers a thumb can give the moment a ring
    /// appears: hold course, hard over either way, hard over + burn, burn
    /// straight, all stop. For a range of windup lengths T it records where
    /// the ring would have been laid (her velocity x T x `ringLead`, exactly
    /// as `KrakenSwat` lays it) and how far her hull footprint is from its
    /// centre T seconds later (`KrakenSwat.HullGap`'s arithmetic). Dodged =
    /// gap > ring radius. Run: `SeaSick.Combat.KrakenDodgeProbe.Run()` in
    /// play, then `Report` after ~`EstimatedSeconds`. Nothing is saved; the
    /// helm is handed back at the end.
    public class KrakenDodgeProbe : MonoBehaviour
    {
        static readonly float[] Ts = { 1.5f, 2f, 2.5f, 3f, 3.5f };
        static readonly float[] Orders = { 0f, 0.5f, 1f };
        static readonly string[] OrderNames = { "stopped", "half", "full" };
        static readonly string[] Responses = { "hold", "hardPort", "hardStbd", "hardStbd+burn", "burn", "allStop", "astern" };

        public static string Report = "not run";
        public static bool Done;
        public static float EstimatedSeconds => Orders.Length * Responses.Length * 12f;

        public static string Run(float timeScale = 3f)
        {
            if (!Application.isPlaying) return "play mode only";
            var old = FindAnyObjectByType<KrakenDodgeProbe>();
            if (old != null) Destroy(old.gameObject);
            var go = new GameObject("KrakenDodgeProbe");
            var p = go.AddComponent<KrakenDodgeProbe>();
            p.scale = timeScale;
            Done = false;
            Report = "running";
            return "started; ~" + (EstimatedSeconds / timeScale).ToString("F0") + " s real";
        }

        float scale = 3f;

        void Start() => StartCoroutine(Go());

        IEnumerator Go()
        {
            var motor = FindAnyObjectByType<ShipMotor>();
            if (motor == null) { Report = "no ship"; Done = true; yield break; }
            var helm = motor.GetComponent<HelmInput>();
            var ph = motor.GetComponent<PlayerHull>();
            var anchor = motor.GetComponent<AnchorController>();
            bool helmWas = helm != null && helm.enabled;
            if (helm != null) helm.enabled = false;
            float tsWas = Time.timeScale;
            Time.timeScale = scale;

            var sb = new StringBuilder();
            float halfLen = ph != null ? ph.HitAxis.magnitude : 9.5f;
            float beamHalf = (ph != null ? ph.HitRadius : 4.2f) * 0.5f;
            sb.AppendLine($"ship '{motor.name}' LOA {motor.HullLength:F1} m, footprint half-length {halfLen:F1} m, half-beam {beamHalf:F1} m, " +
                          $"maxSpeed {motor.MaxSpeed:F2} m/s, overdrive {motor.Overdrive:F2}, maxTurn {motor.MaxTurnRate:F1} deg/s, " +
                          $"anchored {(anchor != null && anchor.enabled ? motor.Anchored.ToString() : "n/a")}");
            sb.AppendLine($"ringLead {KrakenTuning.ringLead:F2}. gap = ring centre to hull edge (m); dodged when gap > ring radius.");
            sb.Append("start/response".PadRight(26) + "v0");
            foreach (var t in Ts) sb.Append(("  T=" + t.ToString("F1")).PadLeft(9));
            sb.AppendLine();

            for (int o = 0; o < Orders.Length; o++)
            {
                for (int r = 0; r < Responses.Length; r++)
                {
                    // Settle at the start order, wheel amidships.
                    motor.Rudder = 0f;
                    motor.ThrottleOrder = Orders[o];
                    float settle = Orders[o] <= 0f ? 6f : 10f;
                    float until = Time.time + settle;
                    while (Time.time < until) { motor.Rudder = 0f; yield return null; }

                    Vector3 p0 = Flat(motor.transform.position);
                    Vector3 v0 = Flat(motor.Velocity);
                    float[] gaps = new float[Ts.Length];
                    string resp = Responses[r];
                    float rudder = resp.StartsWith("hardPort") ? -1f : resp.StartsWith("hardStbd") ? 1f : 0f;
                    float order = Orders[o];
                    if (resp.EndsWith("burn")) order = motor.Overdrive;
                    else if (resp == "allStop") order = 0f;
                    else if (resp == "astern") order = -1f;
                    float t0 = Time.time;
                    int next = 0;
                    while (next < Ts.Length)
                    {
                        motor.Rudder = rudder;
                        motor.ThrottleOrder = order;
                        float el = Time.time - t0;
                        while (next < Ts.Length && el >= Ts[next])
                        {
                            Vector3 ring = p0 + v0 * (Ts[next] * KrakenTuning.ringLead);
                            gaps[next] = Gap(motor, ph, ring);
                            next++;
                        }
                        yield return new WaitForFixedUpdate();
                    }
                    sb.Append((OrderNames[o] + "/" + resp).PadRight(26) + v0.magnitude.ToString("F1"));
                    foreach (var g in gaps) sb.Append(g.ToString("F1").PadLeft(9));
                    sb.AppendLine();
                    Report = sb.ToString() + "(running)";
                }
            }
            motor.Rudder = 0f;
            motor.ThrottleOrder = 0f;
            Time.timeScale = tsWas;
            if (helm != null) helm.enabled = helmWas;
            Report = sb.ToString();
            Done = true;
            Destroy(gameObject);
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        /// Dev: screenshots of the swat at the moments that matter, taken by
        /// the game view itself (`ScreenCapture`, by reflection -- editor
        /// only, the module is not referenced by the build): each windup at
        /// 75 %, each slam 0.12 s after it lands (hit_/miss_). Files go to
        /// `dir` with a running number. `StopShots()` ends it.
        public static string Shots(string dir, int maxEach = 2)
        {
            if (Kraken.Active == null || Kraken.Active.Swat == null) return "no kraken";
            var old = FindAnyObjectByType<KrakenShotTaker>();
            if (old != null) Destroy(old.gameObject);
            var go = new GameObject("KrakenShotTaker");
            var t = go.AddComponent<KrakenShotTaker>();
            t.dir = dir; t.maxEach = maxEach;
            return "armed";
        }

        public static string StopShots()
        {
            var old = FindAnyObjectByType<KrakenShotTaker>();
            if (old == null) return "none";
            string log = old.log;
            Destroy(old.gameObject);
            return log;
        }

        public static void Capture(string path)
        {
            var t = System.Type.GetType("UnityEngine.ScreenCapture, UnityEngine.ScreenCaptureModule");
            var m = t?.GetMethod("CaptureScreenshot", new[] { typeof(string) });
            m?.Invoke(null, new object[] { path });
        }

        /// Dev: main-thread cost of the kraken's scripts, ms per call, by
        /// invoking each one's frame method `n` times in a row (Stopwatch).
        /// Repeat calls in one frame advance the lifecycle and swat timers by
        /// n x dt, so run it last.
        public static string Cost(int n = 100)
        {
            var k = Kraken.Active;
            if (k == null) return "no kraken";
            string Time1(Object o, string method)
            {
                if (o == null) return method + " n/a";
                var m = o.GetType().GetMethod(method, System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < n; i++) m.Invoke(o, null);
                sw.Stop();
                return o.GetType().Name + "." + method + " " + (sw.Elapsed.TotalMilliseconds / n).ToString("F3") + " ms";
            }
            var sb = new StringBuilder();
            sb.Append(Time1(k.Arms, "LateUpdate")).Append("; ");
            foreach (var t in FindObjectsByType<KrakenTell>(FindObjectsSortMode.None))
                if (t.isActiveAndEnabled) { sb.Append(Time1(t, "LateUpdate")).Append("; "); break; }
            sb.Append(Time1(k.Swat, "Update")).Append("; ");
            sb.Append(Time1(k, "Update"));
            return sb.ToString();
        }

        static float Gap(ShipMotor motor, PlayerHull ph, Vector3 ring)
        {
            Vector3 c = Flat(motor.transform.position);
            Vector3 axis = Flat(ph != null ? ph.HitAxis : motor.transform.forward * 9.5f);
            float beamHalf = (ph != null ? ph.HitRadius : 4.2f) * 0.5f;
            Vector3 a = c - axis, b = c + axis, ab = b - a;
            float t = ab.sqrMagnitude > 1e-4f ? Mathf.Clamp01(Vector3.Dot(ring - a, ab) / ab.sqrMagnitude) : 0.5f;
            return (ring - (a + ab * t)).magnitude - beamHalf;
        }
    }

    /// The taker behind `KrakenDodgeProbe.Shots`.
    public class KrakenShotTaker : MonoBehaviour
    {
        public string dir;
        public int maxEach = 2;
        public string log = "";
        int windups, hits, misses;
        readonly bool[] shotThisWindup = new bool[KrakenArms.ArmCount];
        readonly float[] windupT = new float[KrakenArms.ArmCount];
        KrakenSwat swat;

        void Update()
        {
            var k = Kraken.Active;
            if (k == null || k.Swat == null) return;
            if (swat != k.Swat)
            {
                if (swat != null) swat.Slammed -= OnSlam;
                swat = k.Swat;
                swat.Slammed += OnSlam;
            }
            for (int a = 0; a < KrakenArms.ArmCount; a++)
            {
                if (!swat.IsWindingUp(a)) { windupT[a] = 0f; shotThisWindup[a] = false; continue; }
                windupT[a] += Time.deltaTime;
                if (!shotThisWindup[a] && windups < maxEach && windupT[a] >= KrakenTuning.windupSeconds * 0.75f)
                {
                    shotThisWindup[a] = true;
                    windups++;
                    string f = dir + "/windup_" + windups + ".png";
                    KrakenDodgeProbe.Capture(f);
                    log += "windup arm " + a + " -> " + f + "; ";
                }
            }
        }

        void OnSlam(Vector3 at, bool hit)
        {
            log += (hit ? "HIT" : "MISS") + " at " + at.ToString("F0") + " gap " + swat.HullGap(at).ToString("F1") + "; ";
            if (hit ? hits >= maxEach : misses >= maxEach) return;
            int n = hit ? ++hits : ++misses;
            StartCoroutine(Later(dir + "/" + (hit ? "hit_" : "miss_") + n + ".png"));
        }

        System.Collections.IEnumerator Later(string f)
        {
            yield return new WaitForSeconds(0.12f);
            KrakenDodgeProbe.Capture(f);
            log += "-> " + f + "; ";
        }

        void OnDestroy() { if (swat != null) swat.Slammed -= OnSlam; }
    }
}
