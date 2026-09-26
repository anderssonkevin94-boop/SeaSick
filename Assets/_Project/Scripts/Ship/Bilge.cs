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
        [Tooltip("Bilge filled per second per metre she is BURIED past the rail cap. Green-water ingress is capped at maxEffectiveImmersion so a swell cannot swamp her in a second -- but that cap also means going completely under costs exactly as much as a 45 cm splash, which is why burying her had no consequences at all. This is the separate, steeper channel for actually going under.")]
        [SerializeField] float ingressPerMetreBuried = 0.42f;
        [Tooltip("Ceiling on the burial channel, metres. Uncapped, a freak wave ends the voyage outright.")]
        [SerializeField] float maxEffectiveBurial = 1.5f;

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
        SeaSick.Ocean.BuoyantBody buoyancy;

        void Awake()
        {
            motor = GetComponent<ShipMotor>();
            meter = GetComponent<SmoothnessMeter>();
            roster = GetComponent<Crew.CrewRoster>();
            buoyancy = GetComponent<SeaSick.Ocean.BuoyantBody>();
        }

        void Update()
        {
            FindVoyage();

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // --- in ---
            float rough = meter != null ? meter.Roughness01 : 0f;
            // Capped: a 7.5m swell front can put metres over the rail, and
            // uncapped that swamps her faster than anyone can react to. At the
            // ceiling five hands bailing still lose ground slowly — you have to
            // jettison or find shelter, which is the intended emergency.
            float green = Mathf.Min(motor.RailImmersion, maxEffectiveImmersion);
            // Burying her is now the storm's one real consequence, so it has to
            // cost something the green-water cap cannot express. Past the cap
            // the water comes aboard faster, not at the same rate forever.
            float buried = buoyancy != null
                ? Mathf.Min(buoyancy.BurialDepth, maxEffectiveBurial) : 0f;
            float gained = green * ingressPerMetre
                + buried * ingressPerMetreBuried
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
        float nextVoyageLookup;

        // One string, and its only input is a serialized field — but it was
        // being built on every IMGUI event while she was flooding, which is
        // exactly when the frame can least afford it. Built once, rebuilt only
        // if the Inspector moves it.
        string jettisonLabel;
        int jettisonLabelFor = int.MinValue;

        /// Out of OnGUI: a scene scan per EVENT, for an object that never
        /// moves. Same shape as `Shipyard.Update` — cache it, and retry at
        /// 1 Hz while it is still null, because script order is not guaranteed
        /// and the manager is not always up before the first draw.
        void FindVoyage()
        {
            if (voyage != null || Time.unscaledTime < nextVoyageLookup) return;
            voyage = FindAnyObjectByType<Voyage.VoyageManager>();
            nextVoyageLookup = Time.unscaledTime + 1f;
        }

        /// Contextual, like the rest of this game's HUD. The water level
        /// itself now reads on the ship panel under the hull bar (StatusHUD) —
        /// a panel appearing over the boat mid-storm is the thing you are
        /// trying to look past. What stays here is the jettison BUTTON, which
        /// has to be under a thumb.
        void OnGUI()
        {
            bool overloaded = voyage != null && voyage.Overloaded;
            if (!Flooding && !overloaded) return;

            if (voyage == null || voyage.TotalHeld <= 0) return;
            // Same IMGUI-blind-spot suppression as the rest of the HUD
            // (2026-09-26 review): a "jettison cargo" prompt is exactly the
            // kind of thumb-reachable button that must not poke through a
            // menu card.
            if (SeaSick.UI.ModularYard.ShipyardModal.IsOpen
                || SeaSick.UI.Menus.GameMenus.Current != SeaSick.UI.Menus.GameMenus.Mode.None) return;

            // The shared prompt slot, at the lowest rank of anything you can
            // press: throwing cargo over the side matters, but it never
            // matters more than coming alongside or a gun that bears. It used
            // to be pinned at 0.60 of screen height -- the middle of the sea,
            // 0.02 above where SalvageSpawner was putting its toast.
            if (!Prompts.Claim(Prompts.Rank.Jettison)) return;

            int u = HudLayout.Unit;
            var btn = Prompts.Begin().Next(u * 2.1f, u * 14f);
            UIBlocker.Block(btn);
            if (jettisonLabelFor != jettisonPerTap)
            {
                jettisonLabelFor = jettisonPerTap;
                jettisonLabel = $"over the side  ·  {jettisonPerTap}";
            }
            if (GUI.Button(btn, jettisonLabel, UITheme.Button))
                voyage.Jettison(jettisonPerTap);
        }
    }
}
