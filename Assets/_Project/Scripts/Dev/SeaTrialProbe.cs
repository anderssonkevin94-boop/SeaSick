using System.Collections;
using System.Text;
using SeaSick.Ocean;
using SeaSick.Ship;
using SeaSick.World;
using UnityEngine;

namespace SeaSick.Dev
{
    /// The two gates the 2026-09-09 water-gameplay pass shipped without.
    ///
    /// `LandProbe` stands a VIRTUAL ship on 36 bearings and runs no physics,
    /// `HarbourProbe` reads her already anchored, and `LoopProbe` WARPS 400 m
    /// off the berth. All three pass and none of them puts a floating hull in
    /// shallow water or on a steep following face, which is exactly where the
    /// two new rules live. A gate that cannot fail is not a gate.
    public class SeaTrialProbe : MonoBehaviour
    {
        public static void Broach() => Spawn(Mode.Broach);
        public static void Shallow() => Spawn(Mode.Shallow);
        public static void Execute() => Spawn(Mode.Broach);

        enum Mode { Broach, Shallow }
        static Mode mode;

        static void Spawn(Mode m)
        {
            mode = m;
            var old = FindAnyObjectByType<SeaTrialProbe>();
            if (old != null) Destroy(old.gameObject);
            new GameObject("SeaTrialProbe").AddComponent<SeaTrialProbe>();
        }

        static float Ground(Vector3 p) =>
            Island.TerrainHeight != null ? Island.TerrainHeight(p.x, p.z) : -1000f;

        IEnumerator Start()
        {
            var motor = FindAnyObjectByType<ShipMotor>();
            var ctrl = SeaStateController.Instance;
            if (motor == null || ctrl == null || !OceanSampler.Ready)
            {
                Debug.LogError("SeaTrialProbe: no ship / controller / sampler");
                yield break;
            }
            if (mode == Mode.Broach) yield return BroachRun(motor, ctrl);
            else yield return ShallowRun(motor, ctrl);
        }

        // ---------------------------------------------------------------
        /// Does the broach FIRE, and can the rudder catch it?
        ///
        /// Two runs over the same water: rudder centred, then full opposite
        /// rudder applied the moment it passes 0.3. The pair is the point --
        /// "it fires" and "it is catchable" are different claims and one run
        /// cannot separate them.
        IEnumerator BroachRun(ShipMotor motor, SeaStateController ctrl)
        {
            var sb = new StringBuilder("SEA TRIAL - BROACH\n");
            var rb = motor.GetComponent<Rigidbody>();
            var buoy = motor.GetComponent<BuoyantBody>();

            // Deep water, west, the way PlaytestStart finds it -- a shallow
            // spawn caps the sea by breakFraction x depth / Hs and measures a
            // storm that was never there.
            Vector3 home = motor.transform.position;
            Vector3 deep = home;
            for (float d = 400f; d <= 6000f; d += 200f)
            {
                Vector3 p = home + Vector3.left * d;
                if (Ground(p) < -130f) { deep = p; break; }
            }
            sb.AppendLine($"spawn {deep.x:F0},{deep.z:F0}  seabed {Ground(deep):F0} m");

            ctrl.ForceHs(30f);
            motor.Anchored = false;
            motor.transform.position = new Vector3(deep.x, 2f, deep.z);
            if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            yield return new WaitForSeconds(4f);
            sb.AppendLine($"sea pinned Hs {ctrl.CurrentHs:F1} m ({ctrl.CurrentStateName}), " +
                          $"local {motor.SeaHs:F1} m");

            for (int pass = 0; pass < 2; pass++)
            {
                bool counter = pass == 1;
                // Seas from astern: her heading IS the direction they run.
                motor.transform.rotation = Quaternion.Euler(0f, motor.SeasFromDeg + 180f, 0f);
                motor.ThrottleOrder = 1f;
                yield return new WaitForSeconds(6f);      // let her get way on

                float start = motor.transform.eulerAngles.y;
                float peak = 0f, over = 0f, n = 0f, worstYaw = 0f, t = 0f;
                while (t < 40f)
                {
                    float dt = Time.deltaTime;
                    t += dt; n++;
                    float b = motor.Broach01;
                    peak = Mathf.Max(peak, b);
                    if (b > 0.3f) over += dt;
                    worstYaw = Mathf.Max(worstYaw,
                        Mathf.Abs(Mathf.DeltaAngle(start, motor.transform.eulerAngles.y)));
                    motor.Rudder = counter && b > 0.3f
                        ? -Mathf.Sign(rb.angularVelocity.y) : 0f;
                    yield return null;
                }
                motor.Rudder = 0f;
                sb.AppendLine($"\n-- {(counter ? "COUNTER-RUDDER" : "RUDDER CENTRED")}, 40 s --");
                sb.AppendLine($"   peak broach          {peak:F2}");
                sb.AppendLine($"   time over 0.3        {over:F1} s  ({over / 40f:P0})");
                sb.AppendLine($"   worst heading swing  {worstYaw:F0} deg from the fall line");
                sb.AppendLine($"   speed {motor.CurrentSpeed:F1} m/s, overspeed {motor.Overspeed01:F2}, " +
                              $"submersion {(buoy != null ? buoy.Submersion : 0f):F2}");
            }

            sb.AppendLine("\nREAD IT LIKE THIS");
            sb.AppendLine("  peak 0.00 centred          -> it never fires. Lower broachOnsetSlope (0.20),");
            sb.AppendLine("                                or she never got above 45% of max speed.");
            sb.AppendLine("  swing < 20 deg centred     -> fires but does nothing. Raise broachYawRate (22).");
            sb.AppendLine("  swing similar in both      -> the rudder is irrelevant, which is the one");
            sb.AppendLine("                                outcome with no gameplay in it at all.");
            sb.AppendLine("  centred swings, counter holds -> this is what it is supposed to do.");
            ctrl.ReleaseForce();
            motor.ThrottleOrder = 0f;
            Debug.Log(sb.ToString());
        }

        // ---------------------------------------------------------------
        /// How much further out does she ground now that the wave is in the
        /// test? Answered ANALYTICALLY along a real approach line, because
        /// the honest quantity is the difference between two rules over the
        /// same seabed and a sailed run only ever samples one of them.
        IEnumerator ShallowRun(ShipMotor motor, SeaStateController ctrl)
        {
            var sb = new StringBuilder("SEA TRIAL - SHALLOW APPROACH\n");
            var buoy = motor.GetComponent<BuoyantBody>();
            var isle = Island.Nearest(motor.transform.position);
            if (isle == null) { Debug.LogError("SeaTrialProbe: no island"); yield break; }

            ctrl.ForceHs(6f);
            yield return new WaitForSeconds(3f);
            sb.AppendLine($"'{isle.name}', sea pinned Hs {ctrl.CurrentHs:F1} m " +
                          $"({ctrl.CurrentStateName})\n");
            sb.AppendLine("   Walking in along 8 bearings. `old` is the shipped rule (seabed vs a");
            sb.AppendLine("   constant -0.3); `new` also subtracts the wave trough. The gap is what");
            sb.AppendLine("   the change actually cost her.\n");
            sb.AppendLine("   bearing    old stop    new stop    further out    surf starts");

            const float Draft = 0.3f;
            float worst = 0f;
            for (int b = 0; b < 8; b++)
            {
                float ang = b * 45f * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang));
                float oldStop = -1f, newStop = -1f, surfAt = -1f;
                // Seaward -> shoreward, so the FIRST crossing is the one she
                // meets coming in.
                for (float d = 600f; d > 0f; d -= 2f)
                {
                    Vector3 p = isle.transform.position + dir * d;
                    float g = Ground(p);
                    float surface = OceanSampler.SampleImmediate(p).height;
                    if (oldStop < 0f && g > -Draft) oldStop = d;
                    if (newStop < 0f && g > surface - Draft) newStop = d;
                    float depth = surface - g;
                    float hs = ctrl.SeaHsAt(new Vector2(p.x, p.z));
                    if (surfAt < 0f && depth > 0.05f && hs / depth > 0.35f) surfAt = d;
                    if (oldStop >= 0f && newStop >= 0f && surfAt >= 0f) break;
                }
                float gap = newStop - oldStop;
                worst = Mathf.Max(worst, gap);
                sb.AppendLine($"   {b * 45,5} deg  {oldStop,8:F0} m  {newStop,8:F0} m  " +
                              $"{gap,10:F0} m  {(surfAt < 0f ? "     none" : surfAt.ToString("F0") + " m"),12}");
            }

            sb.AppendLine($"\n   worst case: she now stops {worst:F0} m further out.");
            sb.AppendLine("   Under about 15 m that is the wave being honest. Over about 40 m she is");
            sb.AppendLine("   being walled out of her own beaches -- subtract a FRACTION of the trough");
            sb.AppendLine("   in HullIntegrity.GroundedOnLand rather than all of it.");
            sb.AppendLine($"\n   for reference, the landing prompt reaches {Island.BeachMaxSlope:F2} slope,");
            sb.AppendLine($"   and she is drawing {(buoy != null ? buoy.MeanWaterHeight : 0f):F2} m of surface right now.");
            ctrl.ReleaseForce();
            Debug.Log(sb.ToString());
        }
    }
}
