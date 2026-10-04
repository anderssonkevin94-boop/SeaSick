using SeaSick.CameraRig;
using SeaSick.Ocean;
using SeaSick.Ship;
using UnityEngine;

namespace SeaSick.Combat
{
    /// How a kraken encounter ended (`Kraken.Ended`). Step 2 only ever
    /// reports `Dismissed`; step 3 adds the fight's two real endings.
    public enum KrakenOutcome { DrivenOff, Escaped, Dismissed }

    /// **The kraken's lifecycle** (GDD §6 "The Kraken (deep-water monster)").
    /// Step 1 surfaces it; step 2's attack lives in `KrakenSwat` (the swat)
    /// and `KrakenTell` (the foam ring), which this class owns and stops.
    ///
    /// Rising -> Surfaced -> Sinking -> destroyed. It comes up from
    /// `KrakenTuning.sinkDepth` below its surfaced height with the arms still
    /// tucked, throws a breach splash and a foam ring the moment the head
    /// breaks the water, raises the arms into Kevin's approved P1 as it
    /// settles, then idles riding the swell and turning slowly to face the
    /// ship while `KrakenArms` keeps every arm moving and `KrakenSwat`
    /// attacks inside its reach. A DEV summon sinks on its own after
    /// `surfacedSeconds`; a real encounter has no timer -- it ends by escape
    /// or drive-off (step 3) through `Retreat`. Once under it raises `Ended`
    /// exactly once and is destroyed.
    ///
    /// **Transient.** Spawned at runtime from `Resources/Creatures/Octopus`,
    /// registered with nothing the save walks, so it is never written and a
    /// load simply never sees it -- the GDD's "cleared and rebuilt on load
    /// like raiders". One at a time: `Active`.
    ///
    /// Runs after the default order so the camera framing below sees what
    /// the default-order writers (`CombatLock`) already set this frame.
    [DefaultExecutionOrder(100)]
    public class Kraken : MonoBehaviour
    {
        public const string PrefabPath = "Creatures/Octopus";

        /// Never dead ahead: it surfaces this far either side of the bow, so
        /// there is always open water to turn away into (GDD).
        const float MinBowAngle = 35f, MaxBowAngle = 62f;
        /// Root depth under its surfaced height at which the head dome breaks
        /// the water. Measured at scale 49 (BakeMesh, 2026-10-03): the dome
        /// top stands 13.5 m over the root with the arms at rest (16.0 in
        /// P1), so ~12 m over the -1.75 waterline.
        const float HeadBreaksAt = 12f;
        /// Most impulses this beast queues into the ripple sim in one frame.
        /// The sim's queue holds 64 for everything (hulls, wakes, shot); this
        /// keeps the kraken a fifth of it.
        const int MaxStampsPerFrame = 12;
        /// Seconds the water boils over it before it starts up -- long enough
        /// for the camera's lock framing (`JuiceTuning.camLockSeconds`) to
        /// swing round, so the breach lands in the frame, not off its edge.
        const float BoilSeconds = 1.6f;

        public enum Phase { Rising, Surfaced, Sinking }

        public static Kraken Active { get; private set; }

        /// Raised exactly once per kraken, when it has finished sinking (the
        /// frame before it is destroyed), with how the encounter ended.
        public static event System.Action<Kraken, KrakenOutcome> Ended;

        public Phase State { get; private set; }
        public KrakenArms Arms => arms;
        public KrakenSwat Swat => swat;
        /// 0..1: 1 - damage / `KrakenTuning.hitPoints`. At 0 it is driven off.
        public float Health01 => Mathf.Clamp01(1f - damage / Mathf.Max(1f, KrakenTuning.hitPoints));
        public int HitPoints => Mathf.Max(1, Mathf.RoundToInt(KrakenTuning.hitPoints));
        public int DamageTaken => Mathf.RoundToInt(damage);
        /// The head: THE hit target, what the lock picks (step 3).
        public IHittable BodyTarget => bodyTarget;
        /// The head has broken the water (it can be shot from here on).
        public bool Breached => breached;
        /// How this encounter is ending (meaningful once `Retreating`).
        public KrakenOutcome Outcome => outcome;
        /// True from the moment it starts going back down.
        public bool Retreating => State == Phase.Sinking;
        /// Summoned from the dev panel / eval: sinks on its own after
        /// `surfacedSeconds`. A real encounter never times out.
        public bool DevSummoned { get; private set; }
        /// Seconds since it finished surfacing (0 before).
        public float SurfacedFor => State == Phase.Surfaced ? phaseT : 0f;
        /// The sea height it is riding, for the swat's rings and splashes.
        public float WaterY => waterY;
        public Transform Ship => ship;

        KrakenOutcome outcome = KrakenOutcome.Dismissed;
        KrakenBodyTarget bodyTarget;
        float damage;
        float lastHitAt = -99f;
        float farFor;            // seconds the ship has been past escapeDistance
        float fightFor;          // seconds surfaced, for the safety timeout
        SkinnedMeshRenderer[] skin;
        Color[] skinColor;
        MaterialPropertyBlock mpb;
        bool flashClear = true;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly Color HitFlash = new Color(1f, 0.35f, 0.28f);
        bool endedRaised;
        KrakenSwat swat;

        KrakenArms arms;
        Transform ship;
        ChaseCamera chaseCam;
        ParticleSystem skirt;

        OceanProbeRegistry.Handle seaProbe;
        float waterY;
        bool haveWater;

        float phaseT;
        float depth;             // metres under the surfaced height, <= 0
        bool breached;
        bool sinkSplashed;
        float bobSeed;

        float yaw;

        int stampCursor;         // round-robin start over the arms
        float foamEmitDebt;      // particle foam owed, in puffs
        bool ownsPoi;
        Vector3 ourPoi;          // what we last wrote, to tell it from anyone else's

        // Per-frame scratch for the waterline crossings, never reallocated.
        readonly Vector3[] crossings = new Vector3[KrakenArms.ArmCount * 2];

        // ------------------------------------------------------------- spawning

        /// Surface one near the ship, off a random bow quarter, facing her.
        /// Replaces nothing: if one is already up, that one is returned.
        public static Kraken Summon(Vector3 nearShipPos, Vector3 shipForward, bool devSummon = false)
        {
            if (Active != null) return Active;
            Vector3 fwd = new Vector3(shipForward.x, 0f, shipForward.z);
            fwd = fwd.sqrMagnitude < 1e-4f ? Vector3.forward : fwd.normalized;
            float side = Random.value < 0.5f ? -1f : 1f;
            Vector3 dir = Quaternion.Euler(0f, side * Random.Range(MinBowAngle, MaxBowAngle), 0f) * fwd;
            Vector3 at = nearShipPos + dir * KrakenTuning.spawnDistance;
            return SummonAt(at, nearShipPos, devSummon);
        }

        /// Surface one with its body at `surfacePos` (y ignored), facing
        /// `facePos`. The spawner's entry point (step 4 picks the spot).
        /// If one is already up, that one is returned and nothing moves.
        public static Kraken SummonAt(Vector3 surfacePos, Vector3 facePos, bool devSummon = false)
        {
            if (Active != null) return Active;

            var prefab = Resources.Load<GameObject>(PrefabPath);
            if (prefab == null)
            {
                Debug.LogError("[Kraken] prefab missing at Resources/" + PrefabPath);
                return null;
            }

            Vector3 at = surfacePos;
            at.y = -KrakenTuning.sinkDepth + KrakenTuning.waterlineOffset;
            Vector3 toFace = facePos - at;
            toFace.y = 0f;
            if (toFace.sqrMagnitude < 1e-4f) toFace = Vector3.forward;
            var go = Instantiate(prefab, at, Quaternion.LookRotation(toFace.normalized, Vector3.up));
            go.name = "Kraken";
            var k = go.GetComponent<Kraken>();
            if (k == null) k = go.AddComponent<Kraken>();
            k.DevSummoned = devSummon;
            return k;
        }

        /// Dev: surface one off the player's ship. Returns a line for a banner.
        /// Also the eval entry point: `SeaSick.Combat.Kraken.DevSummon()`.
        public static string DevSummon()
        {
            if (Active != null) return "The kraken is already up.";
            var motor = FindAnyObjectByType<ShipMotor>();
            if (motor == null) return "No ship to summon it to.";
            var k = Summon(motor.transform.position, motor.transform.forward, devSummon: true);
            return k != null ? "Something huge rises..." : "Kraken prefab missing.";
        }

        public static string DevDismiss()
        {
            if (Active == null) return "No kraken up.";
            Active.Dismiss();
            return "The kraken sinks away.";
        }

        /// Send it back down now, from wherever it is in its rise.
        public void Dismiss() => Retreat(KrakenOutcome.Dismissed);

        /// Send it down with how the encounter ended (`Ended` reports it).
        /// The first call wins; a second while it is already sinking is
        /// ignored. Any swat in flight is called off.
        public void Retreat(KrakenOutcome how)
        {
            if (State == Phase.Sinking) return;
            outcome = how;
            if (swat != null) swat.CancelAll();
            // Sink from the current height and arm raise, not from a jump to
            // fully surfaced: a dismiss mid-rise reverses smoothly.
            float raised = arms != null ? arms.Raise01 : 1f;
            State = Phase.Sinking;
            phaseT = SinkTimeFor(depth, raised);
        }

        // ------------------------------------------------------------ lifecycle

        void Awake()
        {
            if (Active != null && Active != this) { Destroy(gameObject); return; }
            Active = this;
            arms = GetComponent<KrakenArms>();
            if (arms != null) arms.Raise01 = 0f;
            bobSeed = Random.Range(0f, 100f);
            yaw = transform.eulerAngles.y;
            State = Phase.Rising;
            depth = -KrakenTuning.sinkDepth;
            skirt = KrakenFx.FoamSkirt(transform);
            swat = GetComponent<KrakenSwat>();
            if (swat == null) swat = gameObject.AddComponent<KrakenSwat>();
            bodyTarget = KrakenBodyTarget.Create(this);
            skin = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            skinColor = new Color[skin.Length];
            for (int i = 0; i < skin.Length; i++)
                skinColor[i] = skin[i] != null && skin[i].sharedMaterial != null
                    && skin[i].sharedMaterial.HasProperty(BaseColorId)
                    ? skin[i].sharedMaterial.GetColor(BaseColorId) : Color.white;
            mpb = new MaterialPropertyBlock();
        }

        // ------------------------------------------------------------- damage

        /// A hit landed (`KrakenBodyTarget`, or a fraction of one through an
        /// arm). Flash, writhe, a burst of spray where it went in; at zero
        /// health it is driven off.
        public void TakeDamage(Vector3 point, float amount)
        {
            if (Retreating || amount <= 0f) return;
            damage += amount;
            lastHitAt = Time.time;
            if (arms != null) arms.Writhe = 1f;
            KrakenFx.Splash(point, 1.6f, 0.25f, "KrakenHit");
            if (damage >= KrakenTuning.hitPoints) DriveOff();
        }

        /// Health gone: it goes under with a great splash, the loot floats up
        /// where it sank (`RaiseEnded`), `Ended(DrivenOff)`.
        void DriveOff()
        {
            if (Retreating) return;
            Vector3 at = WaterPoint();
            KrakenFx.Splash(at + Vector3.up * 0.3f, KrakenTuning.breachSplashRadius * 0.6f, 2f, "KrakenDrivenOff");
            DynamicWaterSim.Splash(at, KrakenTuning.breachSplashRadius, KrakenTuning.breachSplashStrength);
            Retreat(KrakenOutcome.DrivenOff);
        }

        void TickFlash()
        {
            const float FlashTime = 0.35f;
            float since = Time.time - lastHitAt;
            if (since < FlashTime) { Tint(1f - since / FlashTime); flashClear = false; }
            else if (!flashClear) { Tint(0f); flashClear = true; }
        }

        void Tint(float flash)
        {
            if (skin == null) return;
            for (int i = 0; i < skin.Length; i++)
            {
                var r = skin[i];
                if (r == null) continue;
                r.GetPropertyBlock(mpb);
                mpb.SetColor(BaseColorId, flash > 0f ? Color.Lerp(skinColor[i], HitFlash, flash * 0.75f) : skinColor[i]);
                r.SetPropertyBlock(mpb);
            }
        }

        /// The fight's two non-violent endings: the ship got clear (past
        /// `escapeDistance` for `escapeSeconds`), or the safety timeout.
        void TickEscape(float dt)
        {
            if (ship == null) return;
            fightFor += dt;
            Vector3 d = ship.position - transform.position;
            d.y = 0f;
            farFor = d.magnitude > KrakenTuning.escapeDistance ? farFor + dt : 0f;
            if (farFor >= KrakenTuning.escapeSeconds) { Retreat(KrakenOutcome.Escaped); return; }
            if (KrakenTuning.maxFightSeconds > 0f && fightFor >= KrakenTuning.maxFightSeconds)
                Retreat(KrakenOutcome.Escaped);
        }

        void OnEnable() => EnsureProbe();

        void OnDisable()
        {
            OceanProbeRegistry.Unregister(seaProbe);
            seaProbe = null;
            ReleaseCamera();
        }

        void OnDestroy()
        {
            if (Active == this) Active = null;
            ReleaseCamera();
        }

        /// Re-registered rather than assumed, as in `SeaMonster.EnsureProbe`:
        /// a domain reload mid-play empties the registry's static list while
        /// the component and this field survive.
        void EnsureProbe()
        {
            if (seaProbe != null && OceanProbeRegistry.Handles.Count > 0) return;
            seaProbe = OceanProbeRegistry.Register(transform.position);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            FindShip();
            RideSea(dt);

            phaseT += dt;
            switch (State)
            {
                case Phase.Rising: TickRise(); break;
                case Phase.Surfaced: TickSurfaced(dt); break;
                case Phase.Sinking:
                    if (TickSink())
                    {
                        RaiseEnded();
                        Destroy(gameObject);
                        return;
                    }
                    break;
            }

            // Bob on top of the swell the probe already follows: a slow,
            // heavy heave and a little roll, so a 65 m animal never looks
            // nailed to the water.
            float t = Time.time + bobSeed;
            float heave = Mathf.Sin(t * 0.55f) * 0.35f;
            Vector3 p = transform.position;
            p.y = waterY + KrakenTuning.waterlineOffset + depth + heave;
            transform.position = p;

            FaceShip(dt);
            TickFlash();
            transform.rotation = Quaternion.Euler(Mathf.Sin(t * 0.43f) * 1.2f,
                yaw, Mathf.Sin(t * 0.37f + 1f) * 1.4f);

            if (State != Phase.Sinking || depth > -HeadBreaksAt) MarkWater(dt);
            FrameCamera();
        }

        void RaiseEnded()
        {
            if (endedRaised) return;
            endedRaised = true;
            ReleaseCamera();
            if (Active == this) Active = null;
            if (outcome == KrakenOutcome.DrivenOff)
            {
                try { KrakenLoot.Drop(WaterPoint()); }
                catch (System.Exception e) { Debug.LogException(e); }
            }
            try { Ended?.Invoke(this, outcome); }
            catch (System.Exception e) { Debug.LogException(e); }
        }

        void FindShip()
        {
            if (ship != null) return;
            var motor = FindAnyObjectByType<ShipMotor>();
            if (motor != null) ship = motor.transform;
            if (chaseCam == null) chaseCam = FindAnyObjectByType<ChaseCamera>();
        }

        /// Off the ONE batched query, never a per-frame `SampleImmediate`:
        /// the same move `SeaMonster.Update` made (see the cost numbers
        /// there). The handle never moves sideways -- the beast holds its
        /// spot -- so a step-old height lerped at 4/s is plenty.
        void RideSea(float dt)
        {
            EnsureProbe();
            if (seaProbe.sampledFrame != 0)
            {
                if (!haveWater) { waterY = seaProbe.sample.height; haveWater = true; }
                waterY = Mathf.Lerp(waterY, seaProbe.sample.height, 1f - Mathf.Exp(-4f * dt));
            }
            else if (!haveWater && OceanSampler.Ready)
            {
                // First frame only -- nothing has been batched for it yet.
                waterY = OceanSampler.SampleImmediate(transform.position).height;
                haveWater = true;
            }
            seaProbe.position = transform.position;
        }

        void TickRise()
        {
            if (phaseT < BoilSeconds)
            {
                // Still deep: the patch of sea over it whitens and churns, a
                // ring of foam puffs closing in, before anything breaks it.
                depth = -KrakenTuning.sinkDepth;
                Boil(phaseT / BoilSeconds);
                return;
            }
            float k = Mathf.Clamp01((phaseT - BoilSeconds) / Mathf.Max(0.1f, KrakenTuning.riseSeconds));
            // Fast out of the deep, easing into the surface: a cubic ease-out.
            float u = 1f - k;
            depth = -KrakenTuning.sinkDepth * u * u * u;
            // The arms come up out of the water after the head, unfurling
            // into P1 over the last two thirds of the rise.
            if (arms != null) arms.Raise01 = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.28f, 1f, k));

            if (!breached && depth > -HeadBreaksAt)
            {
                breached = true;
                Breach();
            }
            if (k >= 1f) { State = Phase.Surfaced; phaseT = 0f; depth = 0f; }
        }

        void TickSurfaced(float dt)
        {
            depth = 0f;
            if (arms != null) arms.Raise01 = 1f;
            // Dev summons only: a real encounter ends by escape or drive-off.
            if (DevSummoned && phaseT >= KrakenTuning.surfacedSeconds) { Dismiss(); return; }
            TickEscape(dt);
        }

        /// True once it is all the way under.
        bool TickSink()
        {
            float total = Mathf.Max(0.1f, KrakenTuning.sinkSeconds);
            float k = Mathf.Clamp01(phaseT / total);
            // Arms fold down first, then the body slides under, accelerating.
            if (arms != null) arms.Raise01 = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.6f, k));
            float s = Mathf.InverseLerp(0.15f, 1f, k);
            depth = -KrakenTuning.sinkDepth * s * s;

            if (!sinkSplashed && depth < -HeadBreaksAt * 0.5f)
            {
                sinkSplashed = true;
                Vector3 at = WaterPoint();
                DynamicWaterSim.Splash(at, KrakenTuning.breachSplashRadius * 0.7f,
                    KrakenTuning.breachSplashStrength * 0.5f);
            }
            return k >= 1f;
        }

        /// Where on the sink curve the current state sits, so a dismiss
        /// mid-rise picks up from the same depth and arm raise.
        static float SinkTimeFor(float currentDepth, float raised)
        {
            float total = Mathf.Max(0.1f, KrakenTuning.sinkSeconds);
            // Arms: raise = 1 - smoothstep(k/0.6). Invert roughly with the
            // linear part; depth: -sinkDepth * ((k-0.15)/0.85)^2.
            float kArms = (1f - Mathf.Clamp01(raised)) * 0.6f;
            float d01 = Mathf.Clamp01(-currentDepth / Mathf.Max(1f, KrakenTuning.sinkDepth));
            float kDepth = d01 > 0f ? 0.15f + Mathf.Sqrt(d01) * 0.85f : 0f;
            return Mathf.Max(kArms, kDepth) * total;
        }

        void FaceShip(float dt)
        {
            if (ship == null) return;
            Vector3 to = ship.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 1f) return;
            float want = Quaternion.LookRotation(to).eulerAngles.y;
            yaw = Mathf.MoveTowardsAngle(yaw, want, KrakenTuning.turnDegPerSec * dt);
        }

        void Boil(float k01)
        {
            if (skirt == null) return;
            foamEmitDebt += 22f * Time.deltaTime;
            Vector3 centre = WaterPoint();
            float r = Mathf.Lerp(KrakenTuning.breachSplashRadius, KrakenTuning.breachSplashRadius * 0.3f, k01);
            while (foamEmitDebt >= 1f)
            {
                foamEmitDebt -= 1f;
                float ang = Random.Range(0f, Mathf.PI * 2f);
                Vector3 at = centre + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * r * Random.Range(0.4f, 1f);
                at.y = waterY + 0.25f;
                KrakenFx.EmitFoam(skirt, at, KrakenTuning.foamRadius * 1.4f);
            }
        }

        Vector3 WaterPoint() => new Vector3(transform.position.x, waterY, transform.position.z);

        // ------------------------------------------------------------ the water

        void Breach()
        {
            Vector3 at = WaterPoint();
            DynamicWaterSim.Splash(at, KrakenTuning.breachSplashRadius, KrakenTuning.breachSplashStrength);

            // The foam ring in the sim, as one round of stamps. Twelve fits
            // the frame budget on its own on the breach frame.
            var sim = DynamicWaterSim.Instance;
            if (sim != null)
            {
                float r = KrakenTuning.breachSplashRadius * 1.15f;
                for (int i = 0; i < MaxStampsPerFrame - 1; i++)
                {
                    float a = i * Mathf.PI * 2f / (MaxStampsPerFrame - 1);
                    sim.Stamp(new Vector2(at.x + Mathf.Cos(a) * r, at.z + Mathf.Sin(a) * r),
                        KrakenTuning.foamRadius * 1.6f, 1.6f, -0.6f);
                }
            }
            KrakenFx.Breach(at + Vector3.up * 0.3f, KrakenTuning.breachSplashRadius * 0.7f);
        }

        /// Foam where it breaks the water: round the mantle, and wherever an
        /// arm crosses the waterline between two bones (an S-curve can cross
        /// twice). The sim gets at most `MaxStampsPerFrame`, round-robin from
        /// a moving start so no arm is starved when there are more crossings
        /// than budget; the particle skirt gets a handful of puffs a second
        /// spread over the same points.
        void MarkWater(float dt)
        {
            if (arms == null || !arms.Ready || !breached) return;
            float water = waterY;
            int n = 0;
            for (int a = 0; a < KrakenArms.ArmCount && n < crossings.Length; a++)
            {
                int found = 0;
                Vector3 prev = arms.ArmBone(a, 0).position;
                for (int j = 1; j < KrakenArms.BonesPerArm && found < 2; j++)
                {
                    Vector3 cur = arms.ArmBone(a, j).position;
                    float d0 = prev.y - water, d1 = cur.y - water;
                    if ((d0 < 0f) != (d1 < 0f))
                    {
                        float f = d0 / (d0 - d1);
                        Vector3 c = Vector3.Lerp(prev, cur, f);
                        c.y = water;
                        crossings[n++] = c;
                        found++;
                    }
                    prev = cur;
                }
            }

            var sim = DynamicWaterSim.Instance;
            float foam = KrakenTuning.foam * dt;
            Vector3 centre = WaterPoint();
            if (sim != null)
            {
                sim.Stamp(new Vector2(centre.x, centre.z), KrakenTuning.foamRadius * 2.6f,
                    foam * 1.5f, foam * 0.4f);
                int budget = Mathf.Min(n, MaxStampsPerFrame - 1);
                for (int s = 0; s < budget; s++)
                {
                    var c = crossings[(stampCursor + s) % n];
                    sim.Stamp(new Vector2(c.x, c.z), KrakenTuning.foamRadius, foam, foam * 0.25f);
                }
                if (n > 0) stampCursor = (stampCursor + budget) % n;
            }

            // Particle foam: ~2 puffs a second per crossing plus the mantle.
            if (skirt != null)
            {
                foamEmitDebt += (n + 2) * 2f * dt * Mathf.Clamp01(KrakenTuning.foam);
                int guard = 0;
                while (foamEmitDebt >= 1f && guard++ < 8)
                {
                    foamEmitDebt -= 1f;
                    int pick = Random.Range(-2, n);
                    Vector3 at;
                    float size;
                    if (pick < 0)
                    {
                        // Round the mantle's rim, where the body meets the sea.
                        float ang = Random.Range(0f, Mathf.PI * 2f);
                        at = centre + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * KrakenTuning.foamRadius * 2.2f;
                        size = KrakenTuning.foamRadius * 2.2f;
                    }
                    else
                    {
                        at = crossings[pick];
                        size = KrakenTuning.foamRadius * 1.5f;
                    }
                    at.y = water + 0.25f;
                    KrakenFx.EmitFoam(skirt, at, size);
                }
            }
        }

        // ------------------------------------------------------------- the camera

        /// Frame ship + kraken while it is up, through the chase camera's
        /// `SeaFocus`: the combat lock's own framing (seat swung off the
        /// target's side, backed off to hold both, aim on the midpoint)
        /// without being a lock. Not `PointOfInterest`: that is the shore
        /// party's framing, and with a target 55 m out it sat the lens nearly
        /// over the ship and dropped her off the bottom of the portrait
        /// screen (measured 2026-10-03: ship at viewport y -0.36). Polite: a
        /// real lock (`LockTarget`) outranks it and frames the same way, the
        /// shore party's `PointOfInterest` and the island overview both win
        /// inside `ChaseCamera` anyway, and it only ever clears what it set.
        void FrameCamera()
        {
            if (chaseCam == null) return;
            // Held through the sink until the head is under, so the camera
            // watches it go instead of swinging away the moment it starts.
            bool up = State != Phase.Sinking || depth > -HeadBreaksAt;
            // A lock on its own head is still this shot (the camera composes
            // it for the alias); any other lock outranks it.
            Transform head = bodyTarget != null ? bodyTarget.transform : null;
            chaseCam.SeaFocusLockAlias = head;
            bool want = KrakenTuning.frameCamera && up
                && (chaseCam.LockTarget == null || chaseCam.LockTarget == head);
            if (!want) { ReleaseCamera(); return; }

            var cur = chaseCam.SeaFocus;
            if (cur.HasValue && !(ownsPoi && cur.Value == ourPoi)) { ownsPoi = false; return; }
            ourPoi = WaterPoint();
            chaseCam.SeaFocus = ourPoi;
            ownsPoi = true;

            // What has to fit: everything of it above the water, measured
            // off the bones this frame (a raised windup arm included), and
            // the HUD's top bar as the ceiling when it is up.
            MeasureAboveWater(out float height, out float radius);
            chaseCam.SeaFocusHeight = height;
            chaseCam.SeaFocusRadius = radius;
            chaseCam.SeaFocusShip01 = ShipLine01();
            float top01 = KrakenTuning.camTop01;
            var bar = SeaSick.UI.Sheets.SeaHud.TopRect;
            if (bar.height > 1f && Screen.height > 0)
                top01 = Mathf.Min(top01, 1f - bar.yMax / Screen.height - 0.025f);
            chaseCam.SeaFocusTop01 = top01;
            chaseCam.SeaFocusElevDeg = KrakenTuning.camElevDeg;
            chaseCam.SeaFocusSwingDeg = KrakenTuning.camSwingDeg;
            chaseCam.SeaFocusMaxBack = KrakenTuning.camMaxBack;
        }

        /// Where the ship's deck sits on the screen while the kraken shot is
        /// up, fraction of the height from the bottom: the tuned line for the
        /// screen shape, raised when the bottom stack (the wheel reserve, the
        /// helm row, the combat row) would otherwise stand on her. Measured
        /// 2026-10-04 on the 1080x2340 portrait screen: 0.27 put her deck at
        /// GUI y 1810, under the combat row (top 1711) and inside the wheel
        /// reserve (top 1368). Held below `camShipMax01` so the kraken's own
        /// top limit keeps room; when both cannot fit, the ship wins. The
        /// line clears the stack by `camShipHull01` more, for the hull that
        /// hangs below the deck on the screen.
        public static float ShipLine01()
        {
            float line = SeaSick.UI.HudLayout.Wide ? KrakenTuning.camShip01Desk : KrakenTuning.camShip01;
            float h = Screen.height;
            if (h < 1f) return line;
            float stackTop = SeaSick.UI.HudLayout.BottomClustersTop;
            var helm = SeaSick.UI.Sheets.SeaHud.HelmRect;
            if (helm.height > 1f) stackTop = Mathf.Min(stackTop, helm.yMin);
            if (SeaSick.UI.Sheets.CombatHud.Visible && SeaSick.UI.Sheets.CombatHud.Rect.height > 1f)
                stackTop = Mathf.Min(stackTop, SeaSick.UI.Sheets.CombatHud.Rect.yMin);
            float clear = 1f - (stackTop - SeaSick.UI.HudLayout.Unit) / h + KrakenTuning.camShipHull01;
            return Mathf.Clamp(Mathf.Max(line, clear), line, Mathf.Max(line, KrakenTuning.camShipMax01));
        }

        /// Height of its highest point over the sea and the horizontal
        /// half-span of everything above the sea, metres, each with a small
        /// margin. Off the 70 arm bones (a bone is a joint, so the arm's own
        /// thickness is the margin) and the mantle's measured dome (16 m over
        /// the root in P1 at scale 49).
        void MeasureAboveWater(out float height, out float radius)
        {
            Vector3 c = transform.position;
            float top = c.y + 16f * transform.lossyScale.y / 49f;
            float r2 = 14f * 14f;
            if (arms != null && arms.Ready)
            {
                for (int a = 0; a < KrakenArms.ArmCount; a++)
                for (int j = 1; j < KrakenArms.BonesPerArm; j++)
                {
                    float y = arms.ArmBone(a, j).position.y;
                    if (y > top) top = y;
                }
                // The width that must fit is the RAISED crown: arms lying
                // low along the water (the front pair, a slam in the water)
                // may run off the sides of a portrait frame; fitting them too
                // backed the lens off until the beast was a third of the
                // screen (first step-2 captures).
                float raisedOver = waterY + (top - waterY) * 0.35f;
                for (int a = 0; a < KrakenArms.ArmCount; a++)
                for (int j = 1; j < KrakenArms.BonesPerArm; j++)
                {
                    Vector3 p = arms.ArmBone(a, j).position;
                    if (p.y < raisedOver) continue;
                    float dx = p.x - c.x, dz = p.z - c.z;
                    float d2 = dx * dx + dz * dz;
                    if (d2 > r2) r2 = d2;
                }
            }
            height = Mathf.Max(0f, top - waterY) + 3f;
            radius = Mathf.Sqrt(r2) + 3f;
        }

        void ReleaseCamera()
        {
            if (chaseCam != null && bodyTarget != null && chaseCam.SeaFocusLockAlias == bodyTarget.transform)
                chaseCam.SeaFocusLockAlias = null;
            if (!ownsPoi) return;
            ownsPoi = false;
            if (chaseCam != null && chaseCam.SeaFocus.HasValue && chaseCam.SeaFocus.Value == ourPoi)
                chaseCam.SeaFocus = null;
        }
    }
}
