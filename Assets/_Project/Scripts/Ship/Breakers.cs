using SeaSick.Ocean;

using SeaSick.World;
using UnityEngine;

namespace SeaSick.Ship
{
    /// The surf line, and the lee shore behind it.
    ///
    /// **This exists to invert the safest water in the game.** The envelope's
    /// depth limit (`env = min(env, breakFraction x depth / Hs)`) lays the sea
    /// down as she comes in off the deep, which is right — that is what
    /// shoaling does — but the consequence was that the shallows became the
    /// calmest and least punishing place to be. Real sailing is the other way
    /// round: the open sea is tiring and the beach is where you lose the ship.
    ///
    /// Nothing new is simulated here. The clamp already knows exactly where
    /// waves are breaking; this reads the same ratio the envelope reads and
    /// bills for it. Where the clamp binds, the water is white, it carries her
    /// bodily toward the beach, and it works the hull.
    [RequireComponent(typeof(ShipMotor), typeof(Rigidbody))]
    public class Breakers : MonoBehaviour
    {
        [Header("Where the water is breaking")]
        [Tooltip("Wave height as a fraction of the water depth at which the surf starts to bite.")]
        [SerializeField] float onsetRatio = 0.35f;
        [Tooltip("Ratio at which she is fully in the breakers. RegionField clamps the envelope at breakFraction 0.55 x depth / Hs, so the local sea can never exceed 0.55 of the depth -- that saturation IS the surf line, and matching it here means the two agree by construction rather than by a copied number.")]
        [SerializeField] float fullRatio = 0.55f;
        [Tooltip("Sea below this is too small to break on anything, metres of Hs. Without it a millpond over a sandbar reads as surf.")]
        [SerializeField] float minSeaHs = 0.8f;

        [Header("What it does")]
        [Tooltip("Shoreward acceleration at full surf, m/s^2. This is the LEE SHORE: the water is going that way and so is she, and getting off it costs engine and searoom. Set below her acceleration so she can always drive out -- but not by much, and not while she is loaded.")]
        [SerializeField] float shoreSet = 1.8f;
        [Tooltip("Hull lost per second at full surf in a sea her own size. Breaking water works a hull the way open sea never does.")]
        [SerializeField] float batterPerSecond = 0.02f;
        [Tooltip("Sea height as a fraction of her length at which the battering reaches its full rate.")]
        [SerializeField] float batterFullAtHullFraction = 0.25f;

        /// 0 clear, 1 in the white water. Instruments read this.
        public float Breaking01 { get; private set; }
        /// Which way the surf is setting her, flat and unit length.
        public Vector3 SetDirection { get; private set; }
        /// Metres of water under her, as the wave has it right now.
        public float DepthUnderKeel { get; private set; }

        /// **The water she lay in does not turn on her when she lets go.**
        /// (2026-09-30, Kevin: "ground on the breakers" casting off at his
        /// camp, hull 47 % -> 0 % in about a minute.)
        ///
        /// A berth or an anchorage is chosen inside the surf line more often
        /// than not: landing is offered anywhere under 12 m of water, and at
        /// the sea near these islands (local Hs 1.7-2.9 m) the surf starts
        /// at 5-8 m of depth. Measured on his save: his camp's pier berth
        /// lies in 6.6 m (onset), his beach anchorage in 1.7 m (full surf).
        /// Anchored, she is exempt (above). The instant she was cast off she
        /// was at a standstill in white water, the set drove her shoreward at
        /// up to 1.8 m/s^2 before the engine had way on, and the batter took
        /// ~1-2 % a second while she was pinned against the land wall:
        /// 1.00 -> 0.69 in 35 s off the beach, 1.00 -> 0.91 in 40 s off the
        /// pier (she was set in under the pier root, 0.9 m of water) --
        /// every point of it `surf`, no land/reef contact at all.
        ///
        /// So from the moment the anchor comes up she has a clear path out:
        /// no set and no battering until she has been in water the surf does
        /// not reach for `clearToEndLeaving` seconds running, or has gone
        /// `leavingRadius` from where she lay (so running along a beach in
        /// the breakers still costs what it always did).
        /// Sailing INTO the surf is unchanged.
        bool leaving = true;   // also covers the very first frames of a boot
        Vector3 leftFrom;
        const float leavingRadius = 150f;
        float clearFor;
        const float clearToEndLeaving = 4f;

        ShipMotor motor;
        Rigidbody rb;
        BuoyantBody buoyancy;
        HullIntegrity hull;

        void Awake()
        {
            motor = GetComponent<ShipMotor>();
            rb = GetComponent<Rigidbody>();
            buoyancy = GetComponent<BuoyantBody>();
            hull = GetComponent<HullIntegrity>();
            leftFrom = transform.position;
        }

        static float Ground(Vector3 p) =>
            Island.TerrainHeight != null ? Island.TerrainHeight(p.x, p.z) : -1000f;

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            if (dt <= 0f) return;

            // Anchored is a decision, not an accident: the shore party moors
            // her in inches of water on purpose and must not be billed for it.
            // Nor is a load: `SaveGame.Restore` stands her at the saved spot
            // UNDER WAY and only re-anchors her after the outposts are
            // rebuilt, seconds later (measured 2026-09-30: 3.8 % of hull lost
            // on every Continue off a beach camp, before the player touched
            // anything).
            var ctrl = SeaStateController.Instance;
            if (motor.Anchored || SeaSick.Save.SaveGame.Restoring
                || ctrl == null || Island.TerrainHeight == null)
            {
                if (motor.Anchored) { leaving = true; leftFrom = transform.position; clearFor = 0f; }
                Breaking01 = Mathf.MoveTowards(Breaking01, 0f, dt);
                return;
            }

            Vector3 p = transform.position;
            float surface = buoyancy != null ? buoyancy.MeanWaterHeight : 0f;
            DepthUnderKeel = surface - Ground(p);

            float hs = ctrl.SeaHsAt(new Vector2(p.x, p.z));
            float ratio = hs < minSeaHs || DepthUnderKeel <= 0.05f
                ? 0f : hs / DepthUnderKeel;
            float want = Mathf.Clamp01(Mathf.InverseLerp(onsetRatio, fullRatio, ratio));
            // Eased rather than snapped: she sails into a surf line over a few
            // seconds and the instrument has to be able to warn before it is
            // already happening.
            Breaking01 = Mathf.MoveTowards(Breaking01, want, 1.5f * dt);

            // **Leaving the berth she chose** (2026-09-30). See `leaving`.
            if (leaving)
            {
                // Clear for a few seconds running, not for one step: the local
                // Hs this reads moves by a metre across a few metres of water
                // (measured 2.06 -> 2.51 m within 5 s of the pier berth), so a
                // berth ON the surf line reads "clear" for a moment on
                // cast-off and the grace was gone before she had way on.
                // And clear WITH WAY ON: a ship with nobody aboard to work
                // the engine (Kevin's save: 0 hands, `Labour01` 0, the
                // throttle never leaves 0) drifts, and a lull in the Hs field
                // must not hand a derelict back to the surf.
                clearFor = want <= 0f && motor.CurrentSpeed > 1.5f ? clearFor + dt : 0f;
                Vector3 off = p - leftFrom; off.y = 0f;
                if (clearFor >= clearToEndLeaving || off.sqrMagnitude > leavingRadius * leavingRadius)
                    leaving = false;
                else { SetDirection = Vector3.zero; return; }
            }

            if (Breaking01 <= 0.001f) { SetDirection = Vector3.zero; return; }

            // Shoreward is UPHILL on the seabed, which needs no island centre
            // and so does not care what shape the coast is -- the same reason
            // HullIntegrity's push-off follows the gradient downhill.
            const float E = 6f;
            float gx = Ground(p + Vector3.right * E) - Ground(p - Vector3.right * E);
            float gz = Ground(p + Vector3.forward * E) - Ground(p - Vector3.forward * E);
            Vector3 up = new Vector3(gx, 0f, gz);
            if (up.sqrMagnitude < 1e-6f) return;
            SetDirection = up.normalized;

            rb.AddForce(SetDirection * (shoreSet * Breaking01 * rb.mass), ForceMode.Force);

            if (hull != null)
            {
                float size = Mathf.Clamp01(hs / Mathf.Max(1f,
                    batterFullAtHullFraction * motor.HullLength));
                float loss = batterPerSecond * Breaking01 * Breaking01 * size * dt;
                if (loss > 0f) hull.Batter(p, loss, "surf");
            }
        }
    }
}
