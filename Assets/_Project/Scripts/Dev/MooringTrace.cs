using SeaSick.Ship;
using UnityEngine;

namespace SeaSick.Dev
{
    /// **Does she hold her berth, and does she still ride?** (2026-10-01,
    /// the pier catwalk.) Added at runtime by eval: every physics step it
    /// samples her flat distance from the berth her `AnchorController`
    /// chose, her heading error against the berth heading, her height, roll
    /// and pitch, and keeps min/max/RMS over the run. `MooringTrace.Begin()`,
    /// wait, `MooringTrace.Report()`.
    public class MooringTrace : MonoBehaviour
    {
        AnchorController anchor;
        Rigidbody rb;
        int n;
        float maxDrift, sumDrift2, maxYawErr;
        float yMin = float.MaxValue, yMax = float.MinValue, ySum, ySum2;
        float rollMax, rollSum2, pitchMax, pitchSum2;
        float t0;

        public static MooringTrace Begin()
        {
            var old = FindFirstObjectByType<MooringTrace>();
            if (old != null) Destroy(old.gameObject);
            return new GameObject("MooringTrace").AddComponent<MooringTrace>();
        }

        void Start()
        {
            anchor = FindFirstObjectByType<AnchorController>();
            rb = anchor != null ? anchor.GetComponent<Rigidbody>() : null;
            t0 = Time.time;
        }

        static float Signed(float a) => a > 180f ? a - 360f : a;

        void FixedUpdate()
        {
            if (anchor == null || anchor.CurrentDock == null) return;
            var t = anchor.transform;
            Vector3 d = anchor.BerthPosition - t.position; d.y = 0f;
            float drift = d.magnitude;
            n++;
            maxDrift = Mathf.Max(maxDrift, drift);
            sumDrift2 += drift * drift;
            maxYawErr = Mathf.Max(maxYawErr, Mathf.Abs(Mathf.DeltaAngle(t.eulerAngles.y,
                anchor.BerthHeading.eulerAngles.y)));
            float y = t.position.y;
            yMin = Mathf.Min(yMin, y); yMax = Mathf.Max(yMax, y); ySum += y; ySum2 += y * y;
            float roll = Signed(t.eulerAngles.z), pitch = Signed(t.eulerAngles.x);
            rollMax = Mathf.Max(rollMax, Mathf.Abs(roll)); rollSum2 += roll * roll;
            pitchMax = Mathf.Max(pitchMax, Mathf.Abs(pitch)); pitchSum2 += pitch * pitch;
        }

        public static string Report()
        {
            var m = FindFirstObjectByType<MooringTrace>();
            if (m == null) return "no trace";
            if (m.n == 0) return "no samples (not at a dock?)";
            float mean = m.ySum / m.n;
            float ySd = Mathf.Sqrt(Mathf.Max(0f, m.ySum2 / m.n - mean * mean));
            return $"{Time.time - m.t0:F1}s n={m.n} drift max {m.maxDrift:F3} rms {Mathf.Sqrt(m.sumDrift2 / m.n):F3} m"
                + $" | yawErr max {m.maxYawErr:F2} deg | heave range {m.yMax - m.yMin:F3} sd {ySd:F3} m"
                + $" | roll max {m.rollMax:F2} rms {Mathf.Sqrt(m.rollSum2 / m.n):F2} | pitch max {m.pitchMax:F2} rms {Mathf.Sqrt(m.pitchSum2 / m.n):F2}";
        }
    }
}
