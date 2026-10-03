using SeaSick.CameraRig;
using SeaSick.Crew;
using SeaSick.Ocean;
using SeaSick.Ship;
using UnityEngine;

namespace SeaSick.Combat
{
    /// **The kraken's swat, build step 2** (GDD §6 "The Kraken").
    ///
    /// While it is surfaced and the ship is inside `KrakenTuning.reach` of its
    /// body, every `swatInterval` (+-25 %) one arm attacks; after
    /// `twoArmAfter` seconds of the fight a second may come at once, on a
    /// ring of its own. An attack:
    ///
    /// 1. **Tell.** A `KrakenTell` foam ring is laid where the ship will be
    ///    at the slam if she holds course and speed (her velocity x the
    ///    windup + slam, x `ringLead`). The arm whose root points closest to
    ///    it rises and coils over it (`KrakenArms.SetArmAim` reaching for a
    ///    point above the ring, a hook curled into its tip) for
    ///    `windupSeconds`.
    /// 2. **Slam.** The tip drives down onto the ring centre in
    ///    `slamSeconds`, accelerating, the hook straightening as it goes.
    /// 3. **Resolve, at the moment it lands.** Her hull footprint (the
    ///    `PlayerHull` capsule: a segment along her keel and half its radius
    ///    as the half-beam) overlapping the ring = HIT, else MISS.
    ///    HIT: `HullIntegrity.Batter` (1/`hitsToCripple` of the hull),
    ///    `ShipMotor.Knockdown`, `HitOverboard.FromCollision`, a crew jolt,
    ///    a camera shake, the heavy haptic, a big splash. MISS: a big splash
    ///    and a roll kick that fades with how near it fell.
    /// 4. The arm holds in the water a beat, then hands itself back.
    ///
    /// Meanwhile the kraken drifts toward the ship at `driftSpeed` (well
    /// under her half ahead) and stops `driftStopAt` metres off.
    ///
    /// **For step 3:** `TryInterrupt(arm)` cancels a windup (the arm flinches
    /// back up, the ring fades), `IsWindingUp(arm)` and `ArmTipPosition(arm)`
    /// tell the guns what to aim at.
    [DefaultExecutionOrder(90)]
    public class KrakenSwat : MonoBehaviour
    {
        enum Stage { None, Windup, Slam, Hold, Recover, Flinch }

        class Attack
        {
            public Stage stage;
            public float t;
            public Vector3 ring;      // centre on the water, y = 0
            public Vector3 coil;      // where the tip hangs during the windup
            public Vector3 flinchFrom;
            public KrakenTell tell;
        }

        const float HoldSeconds = 0.55f;
        const float RecoverSeconds = 1.3f;
        const float FlinchSeconds = 1.0f;

        Kraken kraken;
        KrakenArms arms;
        readonly Attack[] attacks = new Attack[KrakenArms.ArmCount];
        readonly KrakenTell[] tells = new KrakenTell[3];

        ShipMotor motor;
        HullIntegrity hull;
        PlayerHull playerHull;
        CrewRoster roster;
        ChaseCamera chaseCam;

        float nextSwatAt = -1f;
        float fightT;
        bool cancelled;

        /// Hits and misses this kraken has landed, for probes and step 3.
        public int Hits { get; private set; }
        public int Misses { get; private set; }
        /// Fired at each slam's resolve: (point, hit).
        public event System.Action<Vector3, bool> Slammed;

        void Awake()
        {
            kraken = GetComponent<Kraken>();
            arms = GetComponent<KrakenArms>();
            for (int a = 0; a < attacks.Length; a++) attacks[a] = new Attack();
        }

        void OnDestroy()
        {
            foreach (var t in tells) if (t != null) Destroy(t.gameObject);
        }

        // ------------------------------------------------------------ the API

        public bool IsWindingUp(int arm) =>
            arm >= 0 && arm < attacks.Length && attacks[arm].stage == Stage.Windup;

        /// True while the arm is anywhere in an attack (windup to recovery).
        public bool IsAttacking(int arm) =>
            arm >= 0 && arm < attacks.Length && attacks[arm].stage != Stage.None;

        public Vector3 ArmTipPosition(int arm) =>
            arms != null ? arms.ArmTipPosition(arm) : transform.position;

        /// Where an arm's ring lies (y = the sea there), or null when it is
        /// not attacking.
        public Vector3? RingOf(int arm)
        {
            if (!IsAttacking(arm)) return null;
            var r = attacks[arm].ring;
            r.y = kraken != null ? kraken.WaterY : 0f;
            return r;
        }

        /// Step 3's skill shot: cancel this arm's windup. The arm flinches
        /// back up toward the body and the ring fades. True if it was
        /// winding up (and is now cancelled), false otherwise.
        public bool TryInterrupt(int arm)
        {
            if (!IsWindingUp(arm)) return false;
            var at = attacks[arm];
            at.stage = Stage.Flinch;
            at.t = 0f;
            at.flinchFrom = arms != null ? arms.ArmTipPosition(arm) : at.coil;
            if (at.tell != null) at.tell.Cancel();
            return true;
        }

        /// Call everything off (the kraken is going down): every windup
        /// flinches back, every ring fades, nothing more starts.
        public void CancelAll()
        {
            cancelled = true;
            for (int a = 0; a < attacks.Length; a++)
            {
                var at = attacks[a];
                if (at.stage == Stage.Windup) TryInterrupt(a);
                else if (at.stage == Stage.Slam || at.stage == Stage.Hold)
                {
                    at.stage = Stage.Recover;
                    at.t = 0f;
                }
            }
        }

        // ------------------------------------------------------------- update

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || kraken == null || arms == null || !arms.Ready) return;
            FindShip();

            bool fighting = !cancelled && kraken.State == Kraken.Phase.Surfaced && motor != null;
            if (fighting)
            {
                fightT += dt;
                Drift(dt);
                if (nextSwatAt < 0f) nextSwatAt = Time.time + Jittered() * 0.6f;
                if (Time.time >= nextSwatAt && InReach() && !AnyWindingUp())
                {
                    StartSwats();
                    nextSwatAt = Time.time + Jittered();
                }
            }

            for (int a = 0; a < attacks.Length; a++) TickAttack(a, dt);
        }

        static float Jittered() =>
            Mathf.Max(0.5f, KrakenTuning.swatInterval) * Random.Range(0.75f, 1.25f);

        void FindShip()
        {
            if (motor != null) return;
            motor = FindAnyObjectByType<ShipMotor>();
            if (motor == null) return;
            hull = motor.GetComponent<HullIntegrity>();
            playerHull = motor.GetComponent<PlayerHull>();
            roster = motor.GetComponent<CrewRoster>();
            chaseCam = FindAnyObjectByType<ChaseCamera>();
        }

        Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        bool InReach()
        {
            Vector3 d = Flat(motor.transform.position - transform.position);
            return d.magnitude <= KrakenTuning.reach;
        }

        bool AnyWindingUp()
        {
            for (int a = 0; a < attacks.Length; a++) if (attacks[a].stage == Stage.Windup) return true;
            return false;
        }

        /// Slow, steady, straight at her, stopping `driftStopAt` off. It
        /// moves its own transform sideways; `Kraken` owns the height.
        void Drift(float dt)
        {
            Vector3 to = Flat(motor.transform.position - transform.position);
            float dist = to.magnitude;
            float gap = dist - Mathf.Max(5f, KrakenTuning.driftStopAt);
            if (gap <= 0f || dist < 0.01f) return;
            float step = Mathf.Min(gap, Mathf.Max(0f, KrakenTuning.driftSpeed) * dt);
            transform.position += to / dist * step;
        }

        // ------------------------------------------------------------ attacks

        void StartSwats()
        {
            float total = Mathf.Max(0.3f, KrakenTuning.windupSeconds) + Mathf.Max(0.05f, KrakenTuning.slamSeconds);
            Vector3 shipPos = Flat(motor.transform.position);
            Vector3 vel = Flat(motor.Velocity);
            Vector3 ring1 = shipPos + vel * (total * Mathf.Max(0f, KrakenTuning.ringLead));
            ring1 = ClampToReach(ring1);
            int a1 = PickArm(ring1, -1);
            if (a1 < 0) return;
            Begin(a1, ring1);

            if (fightT >= KrakenTuning.twoArmAfter && Random.value < KrakenTuning.twoArmChance)
            {
                // The second ring covers the easy way out: the turn AWAY from
                // the kraken, one ring-width and a bit off the first. Turning
                // toward it (or stopping / burning) still gets her clear.
                Vector3 fwd = Flat(motor.transform.forward).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, fwd);
                Vector3 toKraken = Flat(transform.position - shipPos);
                float away = Vector3.Dot(toKraken, right) > 0f ? -1f : 1f;
                Vector3 ring2 = ring1 + right * (away * KrakenTuning.ringRadius * 2.3f);
                ring2 = ClampToReach(ring2);
                if (Flat(ring2 - ring1).magnitude > KrakenTuning.ringRadius * 1.8f)
                {
                    int a2 = PickArm(ring2, a1);
                    if (a2 >= 0) Begin(a2, ring2);
                }
            }
        }

        /// The arm only reaches so far: a ring past `reach` from the body is
        /// pulled back in, so the tell never promises a slam the arm cannot
        /// deliver.
        Vector3 ClampToReach(Vector3 p)
        {
            Vector3 c = Flat(transform.position);
            Vector3 d = p - c;
            float max = Mathf.Max(5f, KrakenTuning.reach);
            if (d.magnitude > max) p = c + d.normalized * max;
            return p;
        }

        /// The free arm whose root points closest to the spot (the root's
        /// direction is joint 0 -> joint 3, flattened), favouring arms that
        /// already stand raised: the slam is solved from the arm's current
        /// pose, so a towering arm comes down in an arch over the water and
        /// reads as a blow, where the low front pair would slide along the
        /// surface (first step-2 captures).
        int PickArm(Vector3 spot, int except)
        {
            float water = kraken.WaterY;
            int best = -1;
            float bestScore = -2f;
            for (int a = 0; a < KrakenArms.ArmCount; a++)
            {
                if (a == except || attacks[a].stage != Stage.None) continue;
                Vector3 root = arms.ArmBone(a, 0).position;
                Vector3 dir = Flat(arms.ArmBone(a, 3).position - root);
                Vector3 to = Flat(spot - root);
                if (dir.sqrMagnitude < 1e-4f || to.sqrMagnitude < 1e-4f) continue;
                float raised = Mathf.Clamp01((arms.ArmBone(a, 5).position.y - water) / 22f);
                float score = Vector3.Dot(dir.normalized, to.normalized) + 0.6f * raised;
                if (score > bestScore) { bestScore = score; best = a; }
            }
            return best;
        }

        KrakenTell FreeTell()
        {
            for (int i = 0; i < tells.Length; i++)
            {
                if (tells[i] == null) tells[i] = KrakenTell.Create(transform);
                if (!tells[i].Busy) return tells[i];
            }
            return null;
        }

        void Begin(int a, Vector3 ring)
        {
            var at = attacks[a];
            at.stage = Stage.Windup;
            at.t = 0f;
            at.ring = ring;
            // The coil: high over the ring (26 m, a little back toward the
            // body), so from the chase camera the arm stands over the ship.
            Vector3 toBody = Flat(transform.position - ring);
            float dist = toBody.magnitude;
            Vector3 back = dist > 0.1f ? toBody / dist : Vector3.zero;
            float water = kraken.WaterY;
            at.coil = ring + back * Mathf.Min(dist * 0.15f, 4f) + Vector3.up * (water + 26f);
            at.tell = FreeTell();
            if (at.tell != null) at.tell.Show(new Vector3(ring.x, water, ring.z), KrakenTuning.ringRadius);
        }

        void TickAttack(int a, float dt)
        {
            var at = attacks[a];
            if (at.stage == Stage.None) return;
            at.t += dt;
            float water = kraken.WaterY;
            // The tip lands ON the surface (the splash hides it), then sinks a
            // little while it holds.
            Vector3 strike = new Vector3(at.ring.x, water + 0.3f, at.ring.z);
            Vector3 under = new Vector3(at.ring.x, water - 1.5f, at.ring.z);
            float windup = Mathf.Max(0.3f, KrakenTuning.windupSeconds);
            float slam = Mathf.Max(0.05f, KrakenTuning.slamSeconds);

            switch (at.stage)
            {
                case Stage.Windup:
                {
                    float k = at.t / windup;
                    float w = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.35f, k));
                    // A slow tremble as it holds: the coil is alive.
                    float tr = Mathf.Sin(Time.time * 7.3f + a) * 0.6f;
                    Vector3 coil = at.coil + new Vector3(tr, Mathf.Sin(Time.time * 5.1f + a) * 0.8f, -tr);
                    // Rising: from where the arm is toward the coil.
                    arms.SetArmAim(a, coil, w, Mathf.Lerp(20f, 70f, w));
                    arms.SetArmLife(a, Mathf.Lerp(1f, 0.15f, w));
                    if (at.tell != null) at.tell.SetProgress(k);
                    if (k >= 1f) { at.stage = Stage.Slam; at.t = 0f; }
                    break;
                }
                case Stage.Slam:
                {
                    float k = Mathf.Clamp01(at.t / slam);
                    float e = k * k;   // accelerating down: a whip, not a lowering
                    Vector3 tip = Vector3.Lerp(at.coil, strike, e);
                    // Arc it out a little over the ring rather than straight
                    // down the line, the base leading the tip.
                    tip += Vector3.up * (Mathf.Sin(k * Mathf.PI) * 4f);
                    arms.SetArmAim(a, tip, 1f, Mathf.Lerp(70f, -10f, k));
                    arms.SetArmLife(a, 0f);
                    if (k >= 1f)
                    {
                        Resolve(at);
                        at.stage = Stage.Hold;
                        at.t = 0f;
                    }
                    break;
                }
                case Stage.Hold:
                    arms.SetArmAim(a, Vector3.Lerp(strike, under, at.t / HoldSeconds), 1f, -10f);
                    if (at.t >= HoldSeconds) { at.stage = Stage.Recover; at.t = 0f; }
                    break;
                case Stage.Recover:
                {
                    float k = Mathf.Clamp01(at.t / RecoverSeconds);
                    float w = 1f - Mathf.SmoothStep(0f, 1f, k);
                    // Lift out of the water as it lets go.
                    arms.SetArmAim(a, under + Vector3.up * (12f * k), w, -10f * w);
                    arms.SetArmLife(a, 1f - w);
                    if (k >= 1f) Finish(a);
                    break;
                }
                case Stage.Flinch:
                {
                    float k = Mathf.Clamp01(at.t / FlinchSeconds);
                    // Yanked back up and in over the body, then let go.
                    Vector3 body = transform.position + Vector3.up * 26f;
                    Vector3 tip = Vector3.Lerp(at.flinchFrom, body, Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, k * 2f)));
                    float w = 1f - Mathf.SmoothStep(0.4f, 1f, k);
                    arms.SetArmAim(a, tip, w, 90f * w);
                    arms.SetArmLife(a, 1f - w);
                    if (k >= 1f) Finish(a);
                    break;
                }
            }
        }

        void Finish(int a)
        {
            var at = attacks[a];
            at.stage = Stage.None;
            at.t = 0f;
            at.tell = null;
            arms.ClearArmAim(a);
            arms.SetArmLife(a, 1f);
        }

        // ------------------------------------------------------------ resolve

        /// Distance on the water from the ring centre to her hull footprint:
        /// the keel segment less her half-beam. <= ring radius = a hit.
        public float HullGap(Vector3 ringCentre)
        {
            if (motor == null) return float.MaxValue;
            Vector3 c = Flat(motor.transform.position);
            Vector3 axis = Flat(playerHull != null ? playerHull.HitAxis : motor.transform.forward * 9.5f);
            float beamHalf = (playerHull != null ? playerHull.HitRadius : 4.2f) * 0.5f;
            Vector3 p = Flat(ringCentre);
            Vector3 a = c - axis, b = c + axis;
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude > 1e-4f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0.5f;
            Vector3 nearest = a + ab * t;
            return (p - nearest).magnitude - beamHalf;
        }

        void Resolve(Attack at)
        {
            float water = kraken.WaterY;
            Vector3 point = new Vector3(at.ring.x, water, at.ring.z);
            float r = Mathf.Max(0.5f, KrakenTuning.ringRadius);
            float gap = motor != null ? HullGap(at.ring) : float.MaxValue;
            bool hit = gap <= r;
            if (at.tell != null) at.tell.Slam();

            KrakenFx.Splash(point + Vector3.up * 0.2f, r * 0.9f, hit ? 1.3f : 1.1f);
            DynamicWaterSim.Splash(point, KrakenTuning.slamSplashRadius, KrakenTuning.slamSplashStrength);

            if (hit && motor != null)
            {
                Hits++;
                Vector3 shipC = motor.transform.position;
                // Where on her it landed: the hull point nearest the ring.
                Vector3 onHull = shipC + Vector3.ClampMagnitude(Flat(point - shipC), 6f);
                onHull.y = shipC.y + 1.5f;
                float fraction = 1f / Mathf.Max(1f, KrakenTuning.hitsToCripple) + 0.001f;
                if (hull != null) hull.Batter(onHull, fraction, "kraken");
                // The arm comes down ON her: the side it lands on is pressed
                // under. The knockdown is a torque about her forward axis,
                // so positive heels her to port; a starboard hit is negative.
                float side = Vector3.Dot(Flat(point - shipC), motor.transform.right) >= 0f ? -1f : 1f;
                motor.Knockdown(side * Mathf.Abs(KrakenTuning.knockdownDeg), KrakenTuning.knockdownSeconds);
                SeaSick.Ship.Overboard.HitOverboard.FromCollision(motor.transform, onHull, KrakenTuning.overboardSpeed);
                JoltCrew(KrakenTuning.crewJolt);
                if (chaseCam != null) chaseCam.Shake(KrakenTuning.shakeHit);
                OverboardHaptics.Fall();
            }
            else
            {
                Misses++;
                // A near miss rocks her: a roll kick fading out to
                // `missRockRange` past the ring's edge, away from the slam.
                if (motor != null)
                {
                    float past = Mathf.Max(0f, gap - r);
                    float near = 1f - Mathf.Clamp01(past / Mathf.Max(0.1f, KrakenTuning.missRockRange));
                    if (near > 0f)
                    {
                        Vector3 shipC = motor.transform.position;
                        // The splash's wave lifts the near side: a slam to
                        // starboard heels her to port (positive about forward).
                        float side = Vector3.Dot(Flat(point - shipC), motor.transform.right) >= 0f ? 1f : -1f;
                        motor.AddRecoilRoll(side * KrakenTuning.missRoll * near);
                        if (chaseCam != null) chaseCam.Shake(KrakenTuning.shakeMiss * near);
                    }
                }
            }
            Slammed?.Invoke(point, hit);
        }

        void JoltCrew(float amount)
        {
            if (roster == null || amount <= 0f) return;
            foreach (var c in roster.All)
                if (c != null && c.transform.IsChildOf(motor.transform)) c.Jolt(amount);
        }
    }
}
