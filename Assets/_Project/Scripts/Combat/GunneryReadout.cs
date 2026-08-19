using SeaSick.Ship;
using UnityEngine;

namespace SeaSick.Combat
{
    /// Running tally of what the guns have actually done. Static because shots
    /// report in from wherever they die, and a session total is the only way to
    /// tell a good broadside from a lucky one.
    public static class GunneryStats
    {
        public static int Shots { get; private set; }
        public static int Hits { get; private set; }
        public static int Misses { get; private set; }
        public static float LastHitAt { get; private set; } = -99f;

        public static float HitRate01 => Shots > 0 ? (float)Hits / Shots : 0f;
        /// Shots still in the air.
        public static int InFlight => Mathf.Max(0, Shots - Hits - Misses);

        public static void RecordShot() => Shots++;
        public static void RecordMiss() => Misses++;

        public static void RecordHit()
        {
            Hits++;
            LastHitAt = Time.time;
        }

        public static void Reset()
        {
            Shots = Hits = Misses = 0;
            LastHitAt = -99f;
        }
    }

    /// Diagnostic overlay for the guns: reach, bearing, and whether the shots
    /// are landing. G toggles it, H resets the tally.
    ///
    /// This exists because "aiming is the tiller" is only a good idea if the
    /// player can tell when a side bears. Everything here is a number the
    /// player should eventually feel instead of read — it is scaffolding for
    /// tuning the guns, not the shipped interface.
    [RequireComponent(typeof(ShipMotor), typeof(CannonBattery))]
    public class GunneryReadout : MonoBehaviour
    {
        [SerializeField] bool visible = false;
        [SerializeField] float onTargetDeg = 12f;   // how close to abeam counts as bearing

        ShipMotor motor;
        CannonBattery battery;
        GUIStyle label;

        void Awake()
        {
            motor = GetComponent<ShipMotor>();
            battery = GetComponent<CannonBattery>();
        }

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;
            // Letter keys, not the function row: laptops without a physical
            // F-row can't reach F2 without a modifier.
            if (kb.gKey.wasPressedThisFrame) visible = !visible;
            if (visible && kb.hKey.wasPressedThisFrame) GunneryStats.Reset();
        }

        void OnGUI()
        {
            if (!visible) return;
            const int FontSize = 16; // matches DevHUD: view-derived sizes proved unreliable
            if (label == null || label.fontSize != FontSize)
                label = new GUIStyle(GUI.skin.label)
                { fontSize = FontSize, fontStyle = FontStyle.Bold };

            const float line = FontSize * 1.7f;
            float w = Screen.width * 0.62f;
            float x = 12f;
            float y = Screen.height * 0.34f;

            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(new Rect(6f, y - 6f, w + 12f, line * 6.4f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            float range = battery.GunRange;
            GUI.Label(new Rect(x, y, w, line),
                $"GUNNERY   reach {range:F0} m   {battery.GunsPerSide} guns/side", label);

            // Shot tally.
            GUI.color = GunneryStats.Hits > 0 && Time.time - GunneryStats.LastHitAt < 0.6f
                ? new Color(1f, 0.85f, 0.4f) : Color.white;
            GUI.Label(new Rect(x, y += line, w, line),
                $"shots {GunneryStats.Shots}   hits {GunneryStats.Hits}   " +
                $"({GunneryStats.HitRate01:P0})   in air {GunneryStats.InFlight}", label);
            GUI.color = Color.white;

            var target = HitTargets.Nearest(transform.position, out float dist);
            if (target == null)
            {
                GUI.Label(new Rect(x, y += line, w, line), "no target", label);
                GUI.Label(new Rect(x, y += line * 2f, w, line), "H resets the tally", label);
                return;
            }

            // Where the target sits relative to the ship. Positive is starboard.
            Vector3 toTarget = target.HitCentre - transform.position;
            toTarget.y = 0f;
            float rel = Vector3.SignedAngle(transform.forward, toTarget, Vector3.up);

            float offStarboard = Mathf.Abs(Mathf.DeltaAngle(rel, 90f));
            float offPort = Mathf.Abs(Mathf.DeltaAngle(rel, -90f));
            bool starboardBears = offStarboard <= offPort;
            float offBeam = starboardBears ? offStarboard : offPort;

            bool inRange = dist <= range;
            bool bearing = offBeam <= onTargetDeg;

            GUI.color = inRange ? new Color(0.5f, 0.95f, 0.55f) : new Color(0.95f, 0.6f, 0.35f);
            GUI.Label(new Rect(x, y += line, w, line),
                $"target {dist:F0} m  ({(inRange ? "IN RANGE" : $"{dist - range:F0} m short")})   " +
                $"health {target.HealthText()}", label);

            GUI.color = bearing ? new Color(0.5f, 0.95f, 0.55f) : Color.white;
            GUI.Label(new Rect(x, y += line, w, line),
                $"{offBeam:F0}° off the {(starboardBears ? "starboard" : "port")} beam" +
                $"{(bearing && inRange ? "   ← FIRE" : "")}", label);
            GUI.color = Color.white;

            // What raising velocityInheritance would cost, as a live number.
            float wouldDeflect = Mathf.Atan2(motor.CurrentSpeed, 42f) * Mathf.Rad2Deg;
            GUI.Label(new Rect(x, y += line, w, line),
                $"speed {motor.CurrentSpeed:F1} m/s   inherit {battery.VelocityInheritance:F2} " +
                $"→ {battery.DeflectionDeg:F0}° (full would be {wouldDeflect:F0}°)   H resets", label);
        }
    }

    static class HittableUI
    {
        /// Monsters report hit points; anything else just reports alive.
        public static string HealthText(this IHittable t)
        {
            if (t is SeaMonster m) return $"{m.HitPoints - m.DamageTaken}/{m.HitPoints}";
            return t.Alive ? "alive" : "dead";
        }
    }
}
