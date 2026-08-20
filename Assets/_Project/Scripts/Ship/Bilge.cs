using SeaSick.UI;
using UnityEngine;

namespace SeaSick.Ship
{
    /// Water aboard. The centre of the new loop: cargo makes her sit lower,
    /// sitting lower lets the sea aboard, water aboard makes her sit lower
    /// still and roll worse — and the only answers are hands on the buckets
    /// (which are hands off the guns) or cargo over the side.
    ///
    /// The spiral is real but deliberately survivable: the bilge's own
    /// contribution to wallow is capped, so a flooded ship is a crippled ship
    /// and never a doomed one.
    [RequireComponent(typeof(ShipMotor))]
    public class Bilge : MonoBehaviour
    {
        [Header("Taking it aboard")]
        [Tooltip("Bilge filled per second per metre of green water over the rail.")]
        [SerializeField] float ingressPerMetre = 0.28f;
        [Tooltip("Ceiling on how much green water counts at once. Without it a big swell swamps her in a second.")]
        [SerializeField] float maxEffectiveImmersion = 0.45f;
        [Tooltip("Slow seep once she's working hard, even when nothing green comes over.")]
        [SerializeField] float seepAtFullRoughness = 0.006f;

        [Header("Getting it out")]
        [Tooltip("Bilge emptied per second by one healthy hand on a bucket.")]
        [SerializeField] float bailPerHand = 0.021f;
        [Tooltip("Below this nobody bothers — she's always a bit damp.")]
        [SerializeField] float ignoreBelow = 0.06f;
        [Tooltip("Bilge at which every hand aboard is on a bucket.")]
        [SerializeField] float allHandsAt = 0.55f;

        [Header("What it costs")]
        [Tooltip("Most wallow the water alone can add. Capped so the spiral stays survivable.")]
        [SerializeField] float maxWallowFromWater = 0.35f;

        /// 0 dry, 1 swamped.
        public float Bilge01 { get; private set; }
        /// What the water is adding to the hull's motion, for SmoothnessMeter.
        public float WallowFromWater => maxWallowFromWater * Bilge01 * Bilge01;
        /// How many hands are on the buckets right now.
        public int Bailers { get; private set; }
        /// True once she's carrying enough water to be worth shouting about.
        public bool Flooding => Bilge01 >= ignoreBelow;

        ShipMotor motor;
        SmoothnessMeter meter;
        Crew.CrewRoster roster;

        void Awake()
        {
            motor = GetComponent<ShipMotor>();
            meter = GetComponent<SmoothnessMeter>();
            roster = GetComponent<Crew.CrewRoster>();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // --- in ---
            float rough = meter != null ? meter.Roughness01 : 0f;
            // Capped: a 7.5m swell front can put metres over the rail, and
            // uncapped that swamps her faster than anyone can react to. At the
            // ceiling five hands bailing still lose ground slowly — you have to
            // jettison or find shelter, which is the intended emergency.
            float green = Mathf.Min(motor.RailImmersion, maxEffectiveImmersion);
            float gained = green * ingressPerMetre
                + seepAtFullRoughness * rough * rough;

            // --- out ---
            int wanted = HandsWanted();
            if (roster != null) Bailers = roster.AssignBailers(wanted);
            float bailed = Bailers * bailPerHand;

            Bilge01 = Mathf.Clamp01(Bilge01 + (gained - bailed) * dt);

            // Hand it back to the hull: she settles on it, and she rolls on it.
            motor.BilgeLoad01 = Bilge01;
        }

        /// One hand for a puddle, everyone for a flood. Ramped rather than
        /// stepped so losing the guns happens gradually and visibly.
        int HandsWanted()
        {
            if (roster == null || Bilge01 < ignoreBelow) return 0;
            int crew = roster.CrewCount;
            if (crew == 0) return 0;
            float t = Mathf.InverseLerp(ignoreBelow, allHandsAt, Bilge01);
            return Mathf.Clamp(Mathf.CeilToInt(t * crew), 1, crew);
        }

        /// Water taken on from something other than the sea — a shot below the
        /// waterline, grounding. Hooked up in a later milestone.
        public void Breach(float amount) => Bilge01 = Mathf.Clamp01(Bilge01 + amount);

        /// Pumped out alongside at home.
        public void Dry() { Bilge01 = 0f; if (motor != null) motor.BilgeLoad01 = 0f; }

        [Header("Jettison")]
        [SerializeField] int jettisonPerTap = 5;

        Voyage.VoyageManager voyage;

        /// Contextual, like the rest of this game's HUD: the water level and
        /// the way out of it only appear once there is water to worry about.
        void OnGUI()
        {
            if (voyage == null) voyage = FindAnyObjectByType<Voyage.VoyageManager>();
            bool overloaded = voyage != null && voyage.Overloaded;
            if (!Flooding && !overloaded) return;

            int u = UITheme.Unit;
            float w = u * 15f;
            float x = (Screen.width - w) * 0.5f;
            // Low and central: in the thumb's reach on a portrait phone, and
            // out of the middle of the sea where it was sitting on the horizon.
            float y = Screen.height * 0.60f;

            if (Flooding)
            {
                UITheme.Rect(new Rect(x, y, w, u * 1.9f), UITheme.Panel);
                var bar = new Rect(x + u * 0.5f, y + u * 0.45f, w - u * 1f, u * 0.5f);
                UITheme.Bar(bar, Bilge01, UITheme.Ramp(Bilge01));
                GUI.Label(new Rect(x, y + u * 0.95f, w, u * 1.2f),
                    Bailers > 0 ? $"water aboard — {Bailers} bailing" : "water aboard",
                    UITheme.Small2Centered);
                y += u * 2.3f;
            }

            if (voyage != null && voyage.TotalHeld > 0)
            {
                var btn = new Rect(x + u * 0.5f, y, w - u * 1f, u * 2.1f);
                UIBlocker.Block(btn);
                var style = new GUIStyle(UITheme.Button);
                if (GUI.Button(btn, $"over the side  ·  {jettisonPerTap}", style))
                    voyage.Jettison(jettisonPerTap);
            }
        }
    }
}
