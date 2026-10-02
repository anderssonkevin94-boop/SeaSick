using SeaSick.Combat;
using SeaSick.Crew;
using SeaSick.World;
using Unity.Collections;
using UnityEngine;
using RegionField = SeaSick.Ocean.RegionField;
using RegionFieldParams = SeaSick.Ocean.RegionFieldParams;

namespace SeaSick.Ship
{
    /// Hull condition, running aground, and repairs. Islands are solid now:
    /// hit one and you lose speed, ride rougher, and terrify the crew.
    [RequireComponent(typeof(ShipMotor))]
    public class HullIntegrity : MonoBehaviour
    {
        [Header("Hull")]
        [Range(0f, 1f)] [SerializeField] float integrity = 1f;
        [SerializeField] float speedPenaltyAtWreck = 0.4f;   // -40% top speed at 0 hull
        [SerializeField] float wallowAtWreck = 0.25f;        // extra roughness at 0 hull

        [Header("Grounding")]
        [SerializeField] float hullMargin = 7f;      // ship half-length-ish clearance
        [SerializeField] float freeImpactSpeed = 2.5f; // gentle nudges cost nothing
        [SerializeField] float damagePerImpactSpeed = 0.035f;
        [SerializeField] float crewShock = 0.06f;      // permanent sickness jolt on a bad hit
        [SerializeField] float reefDamageScale = 1f; // a full-speed reef hit costs ~37% hull
        // Another hull gives where rock does not, so ramming costs less than
        // a reef — but it still costs, and it still stops you dead.
        [SerializeField] float ramDamageScale = 0.55f;
        [SerializeField] float enemyHullRadius = 9f;

        [Header("Hull to hull")]
        [Tooltip("Share of the closing speed she comes back off a raider's hull with. 0 = dead stop against it, 1 = a billiard ball.")]
        [Range(0f, 1f)] [SerializeField] float ramRestitution = 0.3f;
        [Tooltip("Seconds over which an overlap with a raider's hull is eased out, as velocity rather than a teleport.")]
        [SerializeField] float ramResolveSeconds = 0.3f;
        [Tooltip("Cap on that easing-out speed, m/s, so a deep overlap never launches her.")]
        [SerializeField] float ramMaxSeparation = 4f;
        [Tooltip("Metres of overlap past which she is snapped back out as before: a safety for a raider shoved straight into her, not the normal contact.")]
        [SerializeField] float ramDeepOverlap = 5f;

        [Header("Under fire")]
        // ~12 hits to wreck a hull. Deliberately slow: the damage is not what
        // ends a voyage — the wallow it adds and the crew it terrifies are,
        // and those compound long before the timber runs out.
        [SerializeField] float damagePerShot = 0.085f;
        [SerializeField] float shotShock = 0.05f;

        [Header("Repair")]
        [SerializeField] float repairRate = 0.05f;        // hull per second
        [SerializeField] float timberPerHullPoint = 12f;  // timber for a full rebuild

        public float Integrity01 => integrity;
        public float SpeedMultiplier => 1f - speedPenaltyAtWreck * (1f - integrity);
        /// Broken timber and water aboard both make her lie over and roll.
        /// The bilge's share is capped inside Bilge itself, so a flooded ship
        /// is crippled rather than doomed.
        public float Wallow01
        {
            get
            {
                if (bilge == null) bilge = GetComponent<Bilge>();
                float water = bilge != null ? bilge.WallowFromWater : 0f;
                return wallowAtWreck * (1f - integrity) + water;
            }
        }

        Bilge bilge;
        public bool NeedsRepair => integrity < 0.995f;
        public float LastImpactTime { get; private set; } = -99f;
        public float LastImpactSpeed { get; private set; }
        public float LastShotTime { get; private set; } = -99f;

        /// **Every hull loss, with where it came from** (2026-09-30). The
        /// "ground on the breakers" / "hull bleeds at anchor" hunt had no way
        /// to tell a surf `Batter` from a reef `Bill` from a round shot; this
        /// is that way. Args: the hull, the source ("land", "reef", "ram",
        /// "surf", "shot", "batter"), the fraction lost, the contact point
        /// and the closing speed (0 where there is none). Dev/probes only
        /// subscribe; nothing in the game does.
        public static event System.Action<HullIntegrity, string, float, Vector3, float> Damaged;

        void Report(string source, float before, Vector3 at, float speed)
        {
            float lost = before - integrity;
            if (lost > 0f) Damaged?.Invoke(this, source, lost, at, speed);
        }

        ShipMotor motor;
        CrewAgent[] crew;
        SeaSick.Ocean.BuoyantBody buoyancy;

        void Start()
        {
            motor = GetComponent<ShipMotor>();
            buoyancy = GetComponent<SeaSick.Ocean.BuoyantBody>();
            body = GetComponent<Rigidbody>();
            crew = GetComponentsInChildren<CrewAgent>(true);

            // The hull ignores the ground's colliders from the first frame.
            // `Shipyard.Refit` sets this too, but only when a rung is
            // applied -- a ship that boots on the collider the scene saved
            // never passes through it (measured: excludeLayers was 0 at
            // play start), so the bootstrap lives with the grounding it
            // makes room for.
            ExcludeLand(gameObject);
        }

        // A refit/load rebuilds her colliders (gun boxes, deck-ramp and navy
        // wall boxes, crew capsules) after Start ran: exclude Land again.
        void OnEnable() => SeaSick.Ship.Modular.ShipyardService.PlayerShipReplaced += OnShipReplaced;
        void OnDisable() => SeaSick.Ship.Modular.ShipyardService.PlayerShipReplaced -= OnShipReplaced;
        void OnShipReplaced(GameObject oldShip, GameObject newShip)
        {
            if (newShip == gameObject) ExcludeLand(gameObject);
        }

        /// **Every collider on the ship ignores the Land layer**, not just the
        /// ones present at Start. Anything added later (Coaster gun boxes,
        /// `CoasterNavigation`'s deck-ramp / navy-wall boxes, crew tap
        /// capsules) otherwise collides with the terrain and, being part of
        /// the ship's compound body, levers her over on a sloped beach. Same
        /// rule as the hull box (`Shipyard.Refit`, `Start` above): the depth
        /// field in `HoldOffTheLand` is what stops her at a shore. Contacts
        /// only -- raycasts/overlaps still see both sides. Idempotent.
        public static void ExcludeLand(GameObject ship)
        {
            if (ship == null) return;
            LayerMask land = SeaSick.Terrain.LandLayer.Mask;
            if (land.value == 0) return;
            foreach (var c in ship.GetComponentsInChildren<Collider>(true))
                c.excludeLayers = c.excludeLayers | land;
        }

        Rigidbody body;

        [Header("Shore")]
        [Tooltip("m/s² of seaward push while the keel is over the sand, so she slides off rather than sits on it.")]
        [SerializeField] float seawardPush = 1.2f;

        /// **Land is a wall, not a ramp.**
        ///
        /// The terrain's colliders are on the Land layer and the hull's
        /// collider excludes it (`LandLayer`), so PhysX no longer lets her
        /// climb a beach. This is the ONLY thing that stops her at the shore
        /// now, and it runs every physics step, on the rigidbody, the way a
        /// wall would: the velocity component INTO the land is cancelled --
        /// what is left runs along the shore, so she slides to a stop on the
        /// beach instead of up it -- and a gentle seaward push eases her back
        /// into water. Her position is never written. Contact below
        /// `freeImpactSpeed` costs nothing; faster contact bills the hull as
        /// a reef would (`Aground` did both before, with a teleport).
        ///
        /// Three points along the keel, not one: the centre clears a beach
        /// the bow is already twenty metres up, and it was the bow the
        /// colliders used to lift. The first point over the sand wins, and
        /// the push comes from ITS gradient, so a bow on the beach is pushed
        /// off the beach the bow is on.
        void FixedUpdate()
        {
            if (motor == null || body == null) return;
            HoldOffRaiders();
            if (motor.Anchored) return;
            if (Island.TerrainHeight == null) return;
            HoldOffTheLand();
        }

        /// **A raider's hull is a fender, not a wall** (2026-10-02). Kevin:
        /// "when I engage with the enemy I have a real hard time controlling
        /// the ship." Contact used to go through `Aground`: her transform
        /// snapped 16 m out from the raider's centre and her speed into it
        /// killed -- a stop-dead teleport, every frame she touched, in the
        /// middle of the fight. Now it is done on the rigidbody each physics
        /// step, like the land: the closing speed is reflected at
        /// `ramRestitution`, the overlap is eased out as a capped seaward
        /// velocity over `ramResolveSeconds`, and only an overlap deeper than
        /// `ramDeepOverlap` is snapped (to that depth, not all the way).
        /// Billing, crew jolt and overboard are `Bill`, unchanged: on impact
        /// above `freeImpactSpeed`, once per `billCooldown`.
        void HoldOffRaiders()
        {
            // Locked at a pier nothing shoves her (see `Aground`), and a
            // kinematic body ignores velocity: `Update` snaps her instead.
            if (body.isKinematic || motor.StationLock01 > 0f) return;

            float solid = enemyHullRadius + hullMargin;
            Vector3 pos = body.position;
            foreach (var raider in EnemyShip.All)
            {
                if (raider == null || !raider.Alive) continue;
                Vector3 d = pos - raider.transform.position;
                d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq >= solid * solid || sq < 1e-4f) continue;

                float dist = Mathf.Sqrt(sq);
                Vector3 outward = d / dist;
                float overlap = solid - dist;

                if (overlap > ramDeepOverlap)
                {
                    pos += outward * (overlap - ramDeepOverlap);
                    body.position = pos;
                    overlap = ramDeepOverlap;
                }

                Vector3 v = body.linearVelocity;
                float into = Vector3.Dot(v, outward);
                float closingSpeed = -into;
                // Bounce: the inward part comes back out at the restitution.
                if (into < 0f) v -= outward * (into * (1f + ramRestitution));
                // Ease out of the overlap: at least this much seaward way
                // until clear, which shrinks as she comes clear.
                float ease = Mathf.Min(overlap / Mathf.Max(0.05f, ramResolveSeconds), ramMaxSeparation);
                float outNow = Vector3.Dot(v, outward);
                if (outNow < ease) v += outward * (ease - outNow);
                body.linearVelocity = v;

                if (closingSpeed > freeImpactSpeed && Time.time - lastBillTime > billCooldown)
                    Bill(closingSpeed, ramDamageScale, "ram");
            }
        }

        void HoldOffTheLand()
        {
            float half = motor.HullLength * 0.4f;
            Vector3 fwd = transform.forward; fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            Vector3 c = transform.position;

            Vector3 outward = Vector3.zero;
            bool grounded = GroundedAt(c + fwd * half, out outward)
                         || GroundedAt(c, out outward)
                         || GroundedAt(c - fwd * half, out outward);
            if (!grounded) return;

            Vector3 v = body.linearVelocity;
            Vector3 flatV = v; flatV.y = 0f;
            float closingSpeed = -Vector3.Dot(flatV, outward);

            // The wall: nothing gets through it. Whatever ran along it stays.
            if (closingSpeed > 0f) v += outward * closingSpeed;
            // And off it, gently. Capped so the push never becomes a launch:
            // once she is moving seaward at walking pace it stops adding.
            float seawardNow = Vector3.Dot(v, outward);
            if (seawardNow < 1.5f)
                v += outward * Mathf.Min(seawardPush * Time.fixedDeltaTime, 1.5f - seawardNow);
            body.linearVelocity = v;

            if (closingSpeed > freeImpactSpeed && Time.time - lastBillTime > billCooldown)
                Bill(closingSpeed, 1f, "land");
        }

        /// **A contact bills on impact, once.** (2026-09-30.) Only closing
        /// speed above `freeImpactSpeed` bills, and not again for
        /// `billCooldown` seconds, so a hull RESTING on a shore or a reef
        /// (the wall cancels her closing speed every step) costs nothing
        /// while a ram at speed still costs what it did. The cooldown used
        /// to read `LastImpactTime`, which the surf's `Batter` and every
        /// round shot also stamp -- so a ship in the white water was never
        /// billed for driving onto the rocks at all. Its own clock now.
        float lastBillTime = -99f;
        const float billCooldown = 0.5f;

        /// The hull's share of a contact at `closingSpeed`, and the crew's.
        void Bill(float closingSpeed, float damageScale, string source)
        {
            lastBillTime = Time.time;
            float excess = closingSpeed - freeImpactSpeed;
            float before = integrity;
            integrity = Mathf.Clamp01(integrity - excess * damagePerImpactSpeed * damageScale);
            Report(source, before, transform.position, closingSpeed);
            LastImpactTime = Time.time;
            LastImpactSpeed = closingSpeed;

            // Everyone gets thrown across the deck.
            float shock = crewShock * Mathf.Clamp01(excess / 8f);
            if (crew == null) crew = GetComponentsInChildren<CrewAgent>(true);
            foreach (var c in crew) if (c != null) c.Jolt(shock);

            // 2026-09-30 Kevin: a hard collision (not sailing) is what sends
            // crew and cargo overboard. See `HitOverboard`.
            SeaSick.Ship.Overboard.HitOverboard.FromCollision(transform, transform.position, closingSpeed);
        }

        /// Is there sand under the keel at `p`, and which way is deep water?
        /// The grid cheaply rejects open water and gives the gradient; the
        /// exact field confirms the touch. See `GroundedOnLand` for the
        /// budget this shape keeps.
        bool GroundedAt(Vector3 p, out Vector3 outward)
        {
            outward = Vector3.zero;
            float surface = buoyancy != null ? buoyancy.MeanWaterHeight : 0f;
            float need = surface - groundingDraft;

            bool grid = ShoreGrid(out var prm, out var shore);
            if (grid)
            {
                float g = GridGround(p, prm, shore);
                if (!float.IsNaN(g) && g <= need - gridTrustDepth) return false;
            }
            if (Ground(p) <= need) return false;

            outward = Downhill(p, grid, prm, shore);
            return true;
        }

        /// Straight downhill on the height field at `p`, flat and unit.
        Vector3 Downhill(Vector3 p, bool grid, in RegionFieldParams prm, NativeArray<float> shore)
        {
            const float E = 4f;
            float gx, gz;
            if (!grid || !GridSlope(p, E, prm, shore, out gx, out gz))
            {
                gx = Ground(p + Vector3.right * E) - Ground(p - Vector3.right * E);
                gz = Ground(p + Vector3.forward * E) - Ground(p - Vector3.forward * E);
            }
            Vector3 down = new Vector3(-gx, 0f, -gz);
            if (down.sqrMagnitude < 1e-6f)
            {
                // Flat shallows: no gradient to follow, so fall back to away
                // from the nearest island's centre.
                var isle = Island.Nearest(p);
                down = isle != null ? p - isle.transform.position : Vector3.forward;
                down.y = 0f;
                if (down.sqrMagnitude < 1e-6f) down = Vector3.forward;
            }
            return down.normalized;
        }

        void Update()
        {
            var reef = Reef.Nearest(transform.position);

            Vector3 obstaclePos = Vector3.zero;
            float obstacleRadius = 0f;
            float damageScale = 1f;
            bool intruding = false;

            // **Land is the height field, not a radius.**
            //
            // This used to be `Island.RadiusToward` — a 46-sector radial
            // profile that was the hull's ONLY collision boundary. It cannot
            // describe a bay or a lobe: measured against the real waterline
            // on 360 bearings, it was out by a mean of 1–23 m and a worst of
            // **934 m out to sea** on the biggest island, with stretches of
            // real land **690 m inside it** that you could sail straight
            // through. Invisible walls and phantom water.
            //
            // The height field has no such problem: it is what the coastline
            // is drawn FROM, and it agrees with the rendered mesh to 7 mm.
            // The push-off direction comes from its gradient — downhill is
            // deeper water — which needs no centre and so does not care what
            // shape the island is.
            //
            // Skipped while she is anchored: deliberately putting her ashore
            // is not running aground, and the shore party moors her 11 m off
            // a beach where the water is inches deep.
            // Land itself is handled in `FixedUpdate` -- see `HoldOffTheLand`.
            // Reefs and other hulls keep the shove below.
            if (reef != null && Intrudes(reef.transform.position, reef.Radius))
            {
                obstaclePos = reef.transform.position;
                obstacleRadius = reef.Radius;
                damageScale = reefDamageScale; // jagged rock bites harder
                intruding = true;
            }
            // Enemy hulls are solid as well. Without this the player sails
            // clean through a raider, which reads as the raider having no
            // hit box at all. A dynamic body bounces off them in
            // `HoldOffRaiders` (FixedUpdate); this snap is only the fallback
            // for a kinematic one, which ignores velocity.
            if (!intruding && (body == null || body.isKinematic))
                foreach (var raider in EnemyShip.All)
                {
                    if (raider == null || !raider.Alive) continue;
                    if (!Intrudes(raider.transform.position, enemyHullRadius)) continue;
                    obstaclePos = raider.transform.position;
                    obstacleRadius = enemyHullRadius;
                    damageScale = ramDamageScale;
                    intruding = true;
                    break;
                }

            if (!intruding) return;

            Vector3 toShip = transform.position - obstaclePos;
            toShip.y = 0f;
            float dist = toShip.magnitude;
            float solidRadius = obstacleRadius + hullMargin;
            if (dist < 0.01f) return;

            Aground(toShip / dist, obstaclePos + (toShip / dist) * solidRadius, damageScale,
                    damageScale == ramDamageScale ? "ram" : "reef");
        }

        static float Ground(Vector3 p)
            => Island.TerrainHeight != null ? Island.TerrainHeight(p.x, p.z) : -999f;

        /// **The seabed is expensive to ask, and the ocean already asked it.**
        ///
        /// `Ground` is `Island.TerrainHeight` is `TerrainHeight.Height` — the
        /// whole multi-octave fBm with erosion, mask, skerry, ridge and shore
        /// terms — in plain managed C#, not Burst (HeightBench: the managed
        /// path is pessimistic against the jobbed one, and it is the path
        /// this class was taking). Aground, this method used to spend
        /// **1 + 4 + up to 24 = up to 29 of them in a single frame**: the
        /// touch test, the gradient, and the 24-step walk-out. A guaranteed
        /// multi-millisecond hitch landing on exactly the frame the player
        /// runs onto a beach, which is the worst frame in the game to spend.
        ///
        /// `TerrainShoreField` already rebuilds the same height function on a
        /// 4096 m grid of 256² texels — 16 m apart — centred on the ship, in
        /// a Burst job, because the ocean needs it to shoal. Same
        /// `TerrainSettings` asset as the populator that installed
        /// `Island.TerrainHeight`, so this is the exact seabed sampled
        /// coarsely, not a second opinion. Reading it is a bilinear fetch.
        ///
        /// So the grid does the searching and the exact field does the
        /// deciding: **0 evaluations in open water, 1 for the touch test,
        /// and at most 6 when she grounds** (the confirmation plus a capped
        /// creep). Nothing the rest of the class consumes changed meaning —
        /// `grounded` is still the exact field's answer, `outward` is still
        /// straight downhill, `fixedPos` is still the first place along it
        /// with water under the keel, and it is still confirmed exactly.
        ///
        /// With no grid (probe scenes, no RegionField) every path falls back
        /// to what it did before, at what it cost before.
        bool ShoreGrid(out RegionFieldParams prm, out NativeArray<float> shore)
        {
            prm = default;
            shore = default;
            var rf = RegionField.Instance;
            if (rf == null || rf.ShoreN <= 0) return false;
            shore = rf.Shore;
            if (!shore.IsCreated || shore.Length < rf.ShoreN * rf.ShoreN) return false;
            prm = rf.Params;
            return prm.shoreN > 0;
        }

        /// Seabed height from the grid, or NaN where the grid does not reach.
        /// `ShoreWetDepth` returns depth (positive down) and 1e9 outside its
        /// rect, so height is its negation and the sentinel is the miss.
        static float GridGround(Vector3 p, in RegionFieldParams prm, NativeArray<float> shore)
        {
            float depth = prm.ShoreWetDepth(new Unity.Mathematics.float2(p.x, p.z), shore).z;
            return depth >= 1e8f ? float.NaN : -depth;
        }

        /// Central differences on the grid. False if any of the four samples
        /// falls outside it, so the caller pays the exact four instead.
        static bool GridSlope(Vector3 p, float e, in RegionFieldParams prm, NativeArray<float> shore,
                              out float gx, out float gz)
        {
            gx = gz = 0f;
            float xp = GridGround(p + Vector3.right * e, prm, shore);
            float xm = GridGround(p - Vector3.right * e, prm, shore);
            float zp = GridGround(p + Vector3.forward * e, prm, shore);
            float zm = GridGround(p - Vector3.forward * e, prm, shore);
            if (float.IsNaN(xp) || float.IsNaN(xm) || float.IsNaN(zp) || float.IsNaN(zm)) return false;
            gx = xp - xm;
            gz = zp - zm;
            return true;
        }

        /// How much water the grid must report before its word is taken for
        /// "not aground" with no exact sample at all. 16 m texels cannot see
        /// a pinnacle standing between their centres, so this is the height
        /// of the sub-texel rock the cheap reject is willing to miss — and it
        /// is the same 8 m at which the ocean stops shoaling, i.e. water the
        /// sea itself already calls open. Inside that band she gets the exact
        /// test, which is what she got every frame before.
        const float gridTrustDepth = 8f;

        /// Is there ground under her, and which way is deep water?
        ///
        /// `outward` is straight downhill on the height field. `fixedPos` is
        /// the first place along it with enough water under the keel, found
        /// by walking out — a single step would leave her still aground on
        /// anything steeper than the step size.
        bool GroundedOnLand(out Vector3 outward, out Vector3 fixedPos)
        {
            outward = Vector3.zero;
            fixedPos = transform.position;
            if (Island.TerrainHeight == null) return false;

            Vector3 p = transform.position;
            // **The bar she clears on a crest is the bar she hits in the
            // trough.** This test used to compare the seabed against a
            // constant -0.3, which is mean water — so the wave the whole game
            // is built on had no say in whether she touched. In a six-metre
            // sea that is three metres of depth the pilot cannot see and the
            // game was not charging for, and it is the reason the shallows
            // were the SAFEST water here: the depth limit lays the sea down
            // inshore, and nothing else inshore could hurt her.
            //
            // `MeanWaterHeight` is the surface under the hull, already
            // measured by the batched buoyancy query every physics step, so
            // this costs no sample out of the immediate budget.
            float surface = buoyancy != null ? buoyancy.MeanWaterHeight : 0f;
            float need = surface - groundingDraft;

            bool grid = ShoreGrid(out var prm, out var shore);

            // Open water, settled without touching the fBm at all: the grid
            // says the bottom is a clear `gridTrustDepth` under the keel and
            // nothing that small hides from it.
            if (grid)
            {
                float g = GridGround(p, prm, shore);
                if (!float.IsNaN(g) && g <= need - gridTrustDepth) return false;
            }

            if (Ground(p) <= need) return false;

            outward = Downhill(p, grid, prm, shore);

            Vector3 q = p;
            bool approx = false;
            for (int i = 0; i < 24; i++)
            {
                q += outward * 4f;
                float g = grid ? GridGround(q, prm, shore) : float.NaN;
                if (float.IsNaN(g)) { if (Ground(q) <= need) break; }
                else { approx = true; if (g <= need) break; }
            }
            // The walk was run on 16 m texels, so the place it stopped is
            // where the INTERPOLATED bottom drops away. Confirm it on the
            // real field and, if a sub-texel shelf is still holding her,
            // creep on with the same 4 m step — capped, because five more
            // steps is 20 m and anything that needs more than that is a
            // gradient pointing the wrong way, not a longer walk.
            if (approx)
                for (int i = 0; i < 5 && Ground(q) > need; i++) q += outward * 4f;
            fixedPos = q;
            return true;
        }

        /// Shove her clear, kill the closing speed, and bill the hull for it.
        void Aground(Vector3 outward, Vector3 fixedPos, float damageScale, string source)
        {
            // **Tied up and locked at a pier** (`ShipMotor.HoldStation`,
            // 2026-10-01): nothing shoves her. A raider beached at the camp
            // sits inside her 9 m hull radius at the pier head (Kevin's
            // Island_6), and this write to an interpolated body's TRANSFORM
            // then fought the lock every frame -- the hull drawn 10 m off
            // while the body sat on the berth, the catwalk stretched to
            // nothing.
            if (motor != null && motor.StationLock01 > 0f) return;
            Vector3 pos = transform.position;
            transform.position = new Vector3(fixedPos.x, pos.y, fixedPos.z);

            float closingSpeed = -Vector3.Dot(motor.Velocity, outward);
            motor.KillVelocityAlong(outward);

            if (closingSpeed > freeImpactSpeed && Time.time - lastBillTime > billCooldown)
                Bill(closingSpeed, damageScale, source);
        }

        /// Metres of water she needs under her before she touches.
        ///
        /// **Deliberately tiny — it is a waterline, not a draft.** At a
        /// realistic 1.0 m the depth contour she is stopped at sits a mean of
        /// 55 m off the home island's visible shore and 798 m off it at
        /// worst, because these islands have broad shallow aprons. That is
        /// honest water and a dishonest game: the landing prompt only reaches
        /// 30 m past the shoreline, so she would be walled out of her own
        /// beaches on most bearings — the same symptom as the bug this
        /// replaced, arrived at from the opposite direction.
        ///
        /// The defect actually being fixed is sailing THROUGH LAND and
        /// hitting walls in deep water. Stopping her essentially at the
        /// waterline fixes both and leaves the shallows navigable, which is
        /// what a shallow-draught paddle steamer should do anyway.
        ///
        /// **A const, not a `[SerializeField]`.** As a serialized field it
        /// never took the value written here: the scene has no entry for it,
        /// yet the component kept reporting the old 1.0 through recompiles
        /// AND through fresh play sessions, because Unity restores a
        /// component's serialized state over a changed initializer. That is
        /// the same trap `SetupStormSky.ResetToCodeDefaults` exists for, and
        /// it cost two byte-identical measurement runs that I nearly
        /// believed. Nothing needs to tune this per scene, so the safest
        /// thing is for it not to be tunable per scene.
        const float groundingDraft = 0.3f;

        bool Intrudes(Vector3 centre, float radius)
        {
            Vector3 d = transform.position - centre;
            d.y = 0f;
            return d.sqrMagnitude < (radius + hullMargin) * (radius + hullMargin);
        }

        /// A round shot comes aboard. This is the whole point of arming the
        /// raiders: the damage lands on HullIntegrity, which already costs
        /// speed and adds wallow, and the shock lands on the crew, who already
        /// get sicker and angrier for it. A fight therefore spends the voyage
        /// clock rather than running beside it.
        /// Direct hull loss as a fraction, for things that are not shot: a
        /// mountain sea, rocks. **`TakeShot`'s `amount` is a multiplier on
        /// `damagePerShot`, not a fraction** — passing 0.30 there costs about
        /// 2.5% hull, not 30%, which is exactly the trap this method exists to
        /// avoid. No crew jolt: the caller owns that, or it gets applied twice.
        public void Batter(Vector3 point, float fraction, string source = "batter")
        {
            float before = integrity;
            integrity = Mathf.Clamp01(integrity - Mathf.Max(0f, fraction));
            Report(source, before, point, 0f);
            // **Wear, not an impact** (2026-09-30). `Breakers` calls this
            // every physics step while she is in the white water. It used to
            // stamp `LastImpactTime` each time -- which the crew and the
            // cargo lashings read as "a slam in the last 0.5 s" and scaled by
            // the STALE `LastImpactSpeed` of whatever she last hit -- and to
            // spawn a fresh particle system with a new material on every one
            // of those steps: fifty a second on the phone (measured: 1574
            // splinter bursts in 35 s of surf). A burst at most every
            // `splinterEvery` seconds reads the same and costs nothing.
            if (Time.time - lastSplinters >= splinterEvery)
            {
                lastSplinters = Time.time;
                Splinters(point);
            }
        }

        float lastSplinters = -99f;
        const float splinterEvery = 0.6f;

        public void TakeShot(Vector3 point, float amount)
        {
            float before = integrity;
            integrity = Mathf.Clamp01(integrity - damagePerShot * Mathf.Max(0.01f, amount));
            Report("shot", before, point, 0f);
            LastShotTime = Time.time;
            LastImpactTime = Time.time;

            if (crew == null) crew = GetComponentsInChildren<CrewAgent>(true);
            foreach (var c in crew) if (c != null) c.Jolt(shotShock);

            // 2026-09-30 Kevin: so is an enemy hit. See `HitOverboard`.
            SeaSick.Ship.Overboard.HitOverboard.FromShot(transform, point, amount);

            Splinters(point);
        }

        /// Timber off the rail where the shot went in.
        static void Splinters(Vector3 at)
        {
            var go = new GameObject("HullHit");
            go.transform.position = at;

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 12f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 1.1f);
            main.gravityModifier = 2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 80;
            main.playOnAwake = false;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.62f, 0.48f, 0.30f), new Color(0.30f, 0.24f, 0.18f));

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.5f;

            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial =
                new Material(Shader.Find("Universal Render Pipeline/Particles/Lit"));

            ps.Emit(28);
            Destroy(go, 2.5f);
        }

        /// Returns the timber consumed this frame (0 if nothing to do).
        public float RepairStep(float dt, float timberAvailable)
        {
            if (!NeedsRepair || timberAvailable <= 0f) return 0f;
            float hullWanted = repairRate * dt;
            float timberWanted = hullWanted * timberPerHullPoint;
            if (timberWanted > timberAvailable)
            {
                timberWanted = timberAvailable;
                hullWanted = timberWanted / timberPerHullPoint;
            }
            integrity = Mathf.Clamp01(integrity + hullWanted);
            return timberWanted;
        }

        public void FullRepair() => integrity = 1f;
    }
}
