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
    public class GunneryReadout : MonoBehaviour, SeaSick.UI.IDevTool
    {
        [SerializeField] bool visible = false;

        // --- IDevTool ---
        public string ToolName => "Gunnery";
        public string ToolBlurb => "reach, hit rate, what bears and what raising inheritance would cost (G)";
        public bool ToolActive { get => visible; set => visible = value; }
        void OnEnable() => SeaSick.UI.DevTools.Register(this);
        void OnDisable() => SeaSick.UI.DevTools.Unregister(this);
        [SerializeField] float onTargetDeg = 12f;   // how close to abeam counts as bearing

        ShipMotor motor;
        CannonBattery battery;
        IHittable self;
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
            if (kb.gKey.wasPressedThisFrame) SeaSick.UI.SettingsPanel.Toggle(this);
            if (visible && kb.hKey.wasPressedThisFrame) GunneryStats.Reset();
        }

        /// Drawn inside the settings drawer. It used to paint a black slab
        /// across 62 % of the screen at 0.34 of its height — which on a
        /// portrait phone is the open Yard panel, and on a desk is the middle
        /// of the sea.
        public void DrawTool(Rect body)
        {
            const int FontSize = 16; // matches DevHUD: view-derived sizes proved unreliable
            if (label == null || label.fontSize != FontSize)
                label = new GUIStyle(GUI.skin.label)
                { fontSize = FontSize, fontStyle = FontStyle.Bold };

            const float line = FontSize * 1.7f;
            float w = body.width;
            float x = body.x;
            float y = body.y;

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

            if (self == null) self = GetComponent<PlayerHull>();
            var target = HitTargets.Nearest(transform.position, out float dist, self);
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
