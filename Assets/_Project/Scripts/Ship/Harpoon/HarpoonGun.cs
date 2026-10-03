using System.Collections.Generic;
using SeaSick.Ship.Overboard;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SeaSick.Ship.Harpoon
{
    /// **The bow harpoon, phase 1: hook and reel** (docs/PLAN-harpoon.md).
    /// Added to the player's ship by `Combat.PlayerHull` (every hull has one
    /// from the start), it sits on the bow stem outside the slot grid and
    /// follows the hull through every shipyard refit.
    ///
    /// The loop: the nearest harpoonable thing in the bow arc is the target
    /// (a tapped marker overrides it, `SelectTarget`); `FireOrCut` winds up
    /// and fires a self-leading barb; a bite puts a live line on it, which
    /// the winch reels on a spring-damper while the tension reads slack →
    /// taut → strained; held strain snaps it, a second tap cuts it, both cost
    /// a reload; at the rail it comes aboard through the target's own
    /// `IOverboardTarget.OnHauled`, so the reward is exactly the sail-over
    /// one. A load the hold has no room for is kept alongside (`HoldFull`).
    ///
    /// The UI (🪝 button, world markers, hints) is `UI/Sheets/SeaHud` and
    /// `HarpoonMarkers`, reading this class only.
    public class HarpoonGun : MonoBehaviour
    {
        public static HarpoonGun Player { get; private set; }

        /// Who mans the gun. Null = the captain, always (PLAN §8.7). The
        /// villager side sets this to its harpooner.
        public static IHarpoonCrewSource CrewSource { get; set; }

        // --- state the HUD reads ------------------------------------------
        public HarpoonState State { get; private set; } = HarpoonState.Ready;
        public bool LineOut => State == HarpoonState.Flying || State == HarpoonState.Hooked
                               || State == HarpoonState.Returning;
        /// Sailing: not anchored, not ashore, not in the shipyard or a menu
        /// (the same gates as `HelmInput`'s keys).
        public bool Available { get; private set; }
        public bool CanFire => Available && State == HarpoonState.Ready && Target != null && !pendingShot;
        /// The gun has work: a valid target in the arc, or the line is out.
        public bool Demand => LineOut || (Available && Target != null);
        public Transform BowPost => stand;

        /// Null once the load is destroyed (boarded, sunk): an interface
        /// reference does not go null with the Unity object, and the HUD
        /// reads this the same frame a bottle comes aboard.
        public IHarpoonable Target { get => Gone(target) ? null : target; private set => target = value; }
        IHarpoonable target;
        public IReadOnlyList<IHarpoonable> InArc => inArc;
        public string TargetLabel => Target != null ? Target.HarpoonLabel : "";
        public float TargetDistance => Target != null ? FlatDistance(MuzzlePos, Target.HookPoint) : 0f;

        public float Tension01 { get; private set; }
        public TensionBand Band =>
            Tension01 >= HarpoonTuning.strainBand ? TensionBand.Strained
            : Tension01 >= HarpoonTuning.tautBand ? TensionBand.Taut : TensionBand.Slack;
        public float Reload01 => State == HarpoonState.Reloading
            ? Mathf.Clamp01(reloadT / Mathf.Max(0.01f, HarpoonTuning.reloadSeconds)) : 1f;
        public float ReloadSecondsLeft => State == HarpoonState.Reloading
            ? Mathf.Max(0f, HarpoonTuning.reloadSeconds - reloadT) : 0f;
        /// A load is held at the rail because the hold is full.
        public bool HoldFull { get; private set; }
        /// "Missed", "Snapped", "Cut", "Aboard", "Hold full".
        public string LastEventWord { get; private set; } = "";
        public float LastEventAt { get; private set; } = -99f;
        /// Whoever works the gun right now (the captain when nobody does,
        /// or while the hand is still walking to the bow).
        public HarpoonCrew Crew => crew.atGun ? crew : HarpoonCrew.Captain;

        // --- parts ----------------------------------------------------------
        ShipMotor motor;
        SeaSick.Voyage.VoyageManager voyage;
        Transform mount, swivel, muzzle, drum, stand;
        Vector3 stemLocal;
        bool hasStem;
        Transform barb, barbAttach;
        HarpoonLine line;
        SeaSick.Steamer.SteamerShip steamer;
        SeaSick.Steamer.HullFormData fitData;
        SeaSick.Ship.Modular.ModularShipView fitView;
        SeaSick.Ship.Modular.AssemblyResult fitAssembly;
        float fitLength;

        // --- targeting ------------------------------------------------------
        readonly List<IHarpoonable> inArc = new List<IHarpoonable>();
        readonly List<float> inArcDist = new List<float>();
        IHarpoonable selected;
        IHarpoonable trackOf;
        Vector3 trackLast, targetVel;

        // --- crew -----------------------------------------------------------
        HarpoonCrew crew = HarpoonCrew.Captain;
        bool manned;
        float idleFor;

        // --- shot -----------------------------------------------------------
        bool pendingShot;
        float pendingSince, windup01;
        Vector3 flyFrom, flyTo;
        float flyT, flyTime, flyArc;

        // --- line -----------------------------------------------------------
        IHarpoonable hooked;
        Vector3 loadVel;       // the load's flat velocity, m/s
        float lineLen;         // rest length, m
        float reelVel;         // m/s the winch is hauling at
        float prevDist;
        float rawTension01;
        float strainFor;
        Vector3 pull;          // eased acceleration on the ship, m/s²
        Vector3 returnFrom;
        float returnT;
        float reloadT;
        float drumAngle;

        Vector3 MuzzlePos => muzzle != null ? muzzle.position : transform.position;

        /// m the rope clears the stem cap by.
        const float StemClearance = 0.12f;
        /// No hull data: the fairlead stands this far ahead of the muzzle,
        /// 0.3 m below it.
        const float FallbackStemAhead = 1.2f;
        const float FallbackStemDrop = 0.3f;

        /// **The rope's fairlead**: the top of the hull's bow stem cap plus a
        /// hand of clearance, in world space (refreshed with the fitting on
        /// every refit). Without hull data, a point ahead of the muzzle.
        public Vector3 StemTopWorld
        {
            get
            {
                if (hasStem) return transform.TransformPoint(stemLocal) + Vector3.up * StemClearance;
                Vector3 fwd = transform.forward;
                fwd.y = 0f;
                fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;
                return MuzzlePos + fwd * FallbackStemAhead + Vector3.down * FallbackStemDrop;
            }
        }

        void Awake()
        {
            motor = GetComponent<ShipMotor>();
        }

        void OnEnable() { Player = this; }

        void OnDisable()
        {
            if (Player == this) Player = null;
            ReleaseLoad();
            if (motor != null) { motor.ExternalPull = Vector3.zero; motor.ExternalPullYaw = 0f; }
            pull = Vector3.zero;
            if (manned) { CrewSource?.Release(this); manned = false; }
        }

        void OnDestroy()
        {
            if (barb != null) Destroy(barb.gameObject);
        }

        void Start()
        {
            voyage = Object.FindFirstObjectByType<SeaSick.Voyage.VoyageManager>();
            line = HarpoonLine.Create(transform);
            var b = HarpoonMount.BuildBarb();
            barb = b.transform;
            barbAttach = HarpoonMount.Find(barb, "Line_Attach") ?? barb;
            b.SetActive(false);
            Fit();
        }

        /// (Re)build the fitting on the bow stem of the hull she wears now.
        void Fit()
        {
            if (steamer == null) steamer = GetComponent<SeaSick.Steamer.SteamerShip>();
            if (mount != null) Destroy(mount.gameObject);
            mount = HarpoonMount.BuildMount(transform).transform;
            mount.localPosition = HarpoonMount.BowStem(transform);
            mount.localRotation = Quaternion.identity;
            hasStem = HarpoonMount.StemTop(transform, out stemLocal);
            swivel = HarpoonMount.Find(mount, "Swivel") ?? mount;
            muzzle = HarpoonMount.Find(mount, "Barb_Muzzle") ?? swivel;
            drum = HarpoonMount.Find(mount, "Winch_Drum");
            stand = HarpoonMount.Find(mount, "Harpooner_Stand") ?? mount;
            fitData = steamer != null ? steamer.Data : null;
            fitView = HarpoonMount.ModularView(transform);
            fitAssembly = fitView != null ? fitView.Current : null;
            fitLength = motor != null ? motor.HullLength : 0f;
        }

        /// A different hull: a refit rebinds the steamer's form data and
        /// rebuilds (or replaces) the modular view's assembly, each a new
        /// reference; a hull with neither changes length.
        bool HullChanged()
        {
            if (steamer == null) steamer = GetComponent<SeaSick.Steamer.SteamerShip>();
            var data = steamer != null ? steamer.Data : null;
            if (data != fitData) return true;
            if (fitView != null ? fitView.Current != fitAssembly : fitAssembly != null) return true;
            float len = motor != null ? motor.HullLength : 0f;
            return Mathf.Abs(len - fitLength) > 0.05f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || muzzle == null) return;
            if (HullChanged()) Fit();

            Available = ComputeAvailable();
            Scan();
            TickCrew(dt);
            ReadKeys();

            switch (State)
            {
                case HarpoonState.Ready: TickReady(dt); break;
                case HarpoonState.Flying: TickFlying(dt); break;
                case HarpoonState.Hooked: TickHooked(dt); break;
                case HarpoonState.Returning: TickReturning(dt); break;
                case HarpoonState.Reloading: TickReloading(dt); break;
            }
            if (State != HarpoonState.Hooked) EaseTension(0f, dt);

            if (line.Whipping) line.TickWhip(MuzzlePos, dt);
            TickPull(dt);
            TickMount(dt);
        }

        bool ComputeAvailable()
        {
            if (motor == null || motor.Anchored || Time.timeScale <= 0f) return false;
            if (voyage != null && voyage.AtHome) return false;
            if (SeaSick.CameraRig.IslandCam.Engaged || SeaSick.Ship.Modular.ShipyardSession.WorldInputBlocked)
                return false;
            if (SeaSick.UI.ModularYard.ShipyardModal.IsOpen) return false;
            return SeaSick.UI.Menus.GameMenus.Current == SeaSick.UI.Menus.GameMenus.Mode.None;
        }

        // --- targeting ------------------------------------------------------

        void Scan()
        {
            inArc.Clear();
            inArcDist.Clear();
            Vector3 from = MuzzlePos;
            Vector3 fwd = transform.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            fwd.Normalize();
            var all = HarpoonRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                var t = all[i];
                if (!Valid(t)) continue;
                float d;
                if (!InReach(t, from, fwd, out d)) continue;
                int at = inArcDist.Count;
                while (at > 0 && inArcDist[at - 1] > d) at--;
                inArc.Insert(at, t);
                inArcDist.Insert(at, d);
            }

            if (LineOut) return;   // the line's own target stays the target
            if (selected != null && !inArc.Contains(selected)) selected = null;
            var pick = selected ?? (inArc.Count > 0 ? inArc[0] : null);
            if (pick != Target) { Target = pick; pendingShot = false; }
            Track(Target);
        }

        static bool Valid(IHarpoonable t) => !Gone(t) && t.CanBeHarpooned;

        /// Null, or a destroyed Unity object behind the interface (whose
        /// `transform` would throw).
        public static bool Gone(IHarpoonable t) => t == null || (t is Object o && o == null);

        static bool InReach(IHarpoonable t, Vector3 from, Vector3 fwd, out float d)
        {
            Vector3 to = t.HookPoint - from; to.y = 0f;
            d = to.magnitude;
            if (d > HarpoonTuning.range) return false;
            if (d < 0.01f) return true;
            return Vector3.Angle(fwd, to) <= HarpoonTuning.arcHalfDeg;
        }

        /// The tapped marker's target. Holds until it leaves the arc or
        /// stops being valid; the nearest is the pick again after that.
        public void SelectTarget(IHarpoonable t)
        {
            if (!Valid(t)) return;
            Vector3 fwd = transform.forward; fwd.y = 0f;
            if (!InReach(t, MuzzlePos, fwd.normalized, out _)) return;
            selected = t;
            if (!LineOut && Target != t) { Target = t; pendingShot = false; Track(t); }
        }

        /// The target's own velocity, measured: the lead needs it and the
        /// targets do not publish one.
        void Track(IHarpoonable t)
        {
            if (t == null) { trackOf = null; targetVel = Vector3.zero; return; }
            Vector3 p = t.HookPoint;
            float dt = Time.deltaTime;
            if (trackOf != t) { trackOf = t; trackLast = p; targetVel = Vector3.zero; return; }
            Vector3 v = (p - trackLast) / Mathf.Max(1e-4f, dt); v.y = 0f;
            targetVel = Vector3.Lerp(targetVel, v, 1f - Mathf.Exp(-dt / 0.25f));
            trackLast = p;
        }

        // --- crew -----------------------------------------------------------

        void TickCrew(float dt)
        {
            if (Demand)
            {
                idleFor = 0f;
                crew = CrewSource != null ? CrewSource.Man(this) : HarpoonCrew.Captain;
                if (crew.workRate <= 0f) crew.workRate = HarpoonTuning.captainWorkRate;
                manned = CrewSource != null;
                return;
            }
            idleFor += dt;
            if (manned && idleFor >= HarpoonTuning.releaseIdleSeconds)
            {
                CrewSource?.Release(this);
                manned = false;
                crew = HarpoonCrew.Captain;
            }
        }

        // --- input ----------------------------------------------------------

        void ReadKeys()
        {
            var kb = Keyboard.current;
            if (kb == null || !kb.fKey.wasPressedThisFrame) return;
            if (SeaSick.CameraRig.IslandCam.Engaged || SeaSick.Ship.Modular.ShipyardSession.WorldInputBlocked)
                return;
            FireOrCut();
        }

        /// **The one button.** Ready with a target: wind up and fire. Line
        /// out on a load (or the barb in the air): cut it.
        public void FireOrCut()
        {
            if (State == HarpoonState.Hooked || State == HarpoonState.Flying) { Cut(); return; }
            if (!CanFire) return;
            pendingShot = true;
            pendingSince = Time.time;
            windup01 = 0f;
        }

        // --- states ---------------------------------------------------------

        void TickReady(float dt)
        {
            if (!pendingShot) return;
            if (!Available || Target == null) { pendingShot = false; return; }
            // A hand still walking to the bow gets a moment; then the captain.
            if (!crew.atGun && Time.time - pendingSince < HarpoonTuning.crewWalkWaitSeconds) return;
            float rate = Mathf.Max(0.05f, Crew.workRate);
            windup01 += dt * rate / Mathf.Max(0.01f, HarpoonTuning.windupSeconds);
            if (windup01 >= 1f) Launch();
        }

        void Launch()
        {
            pendingShot = false;
            hooked = Target;
            flyFrom = MuzzlePos;
            // Lead: where the hook point will be when a barb at barbSpeed
            // gets there (three fixed-point passes converge at these speeds).
            Vector3 p0 = hooked.HookPoint;
            float speed = Mathf.Max(5f, HarpoonTuning.barbSpeed);
            float t = Vector3.Distance(flyFrom, p0) / speed;
            Vector3 aim = p0;
            for (int i = 0; i < 3; i++)
            {
                aim = p0 + targetVel * t;
                t = Vector3.Distance(flyFrom, aim) / speed;
            }
            float err = HarpoonTuning.leadErrorMetres * (1f - Mathf.Clamp01(Crew.accuracy01));
            if (err > 0f)
            {
                Vector2 e = Random.insideUnitCircle * err;
                aim += new Vector3(e.x, 0f, e.y);
            }
            flyTo = aim;
            flyTime = Mathf.Max(0.05f, t);
            flyT = 0f;
            flyArc = HarpoonTuning.barbArcMetres * Mathf.Clamp01(FlatDistance(flyFrom, aim) / Mathf.Max(1f, HarpoonTuning.range));
            barb.gameObject.SetActive(true);
            barb.position = flyFrom;
            State = HarpoonState.Flying;
        }

        void TickFlying(float dt)
        {
            flyT += dt;
            float s = Mathf.Clamp01(flyT / flyTime);
            Vector3 p = BarbAt(s);
            Vector3 ahead = BarbAt(Mathf.Min(1f, s + 0.02f)) - p;
            barb.position = p;
            if (ahead.sqrMagnitude > 1e-6f) barb.rotation = Quaternion.LookRotation(ahead);
            line.Draw(MuzzlePos, barbAttach.position, 0f, 0.1f, StemTopWorld);
            if (s < 1f) return;

            bool bite = Valid(hooked)
                        && FlatDistance(flyTo, hooked.HookPoint) <= HarpoonTuning.biteRadius;
            if (bite) Bite();
            else Miss(flyTo);
        }

        Vector3 BarbAt(float s)
        {
            Vector3 p = Vector3.Lerp(flyFrom, flyTo, s);
            p.y += flyArc * 4f * s * (1f - s);
            return p;
        }

        void Bite()
        {
            var ov = hooked as IOverboardTarget;
            if (ov != null) { ov.BeingHauled = true; ov.HaulAnchor = hooked.Transform.position; }
            Vector3 load = Flat(hooked.Transform.position);
            prevDist = Vector3.Distance(Flat(MuzzlePos), load);
            lineLen = prevDist + 0.5f;   // lands with a little slack
            reelVel = 0f;
            loadVel = targetVel;
            strainFor = 0f;
            HoldFull = false;
            prevBow = MuzzlePos;
            State = HarpoonState.Hooked;
        }

        void Miss(Vector3 at)
        {
            hooked = null;
            float water = at.y;
            if (SeaSick.Ocean.OceanSampler.Ready) water = SeaSick.Ocean.OceanSampler.SampleImmediate(at).height;
            at.y = water;
            SeaSick.Combat.KrakenFx.Splash(at, 0.6f, 0.15f, "HarpoonSplash");
            Say("Missed");
            returnFrom = at;
            returnT = 0f;
            State = HarpoonState.Returning;
        }

        void TickReturning(float dt)
        {
            float secs = Mathf.Max(0.1f, HarpoonTuning.missReelSeconds);
            returnT += dt;
            float s = Mathf.Clamp01(returnT / secs);
            float e = s * s * (3f - 2f * s);
            Vector3 p = Vector3.Lerp(returnFrom, MuzzlePos, e);
            // Skims the water most of the way, lifts to the muzzle at the end.
            float water = p.y;
            if (SeaSick.Ocean.OceanSampler.Ready) water = SeaSick.Ocean.OceanSampler.SampleImmediate(p).height;
            p.y = Mathf.Lerp(water, p.y, Mathf.SmoothStep(0.6f, 1f, s));
            Vector3 dir = MuzzlePos - p;
            barb.position = p;
            if (dir.sqrMagnitude > 1e-4f) barb.rotation = Quaternion.LookRotation(-dir);
            reelVel = FlatDistance(returnFrom, MuzzlePos) / secs;
            line.Draw(MuzzlePos, barbAttach.position, 0.05f, 0.3f * (1f - s), StemTopWorld);
            if (s < 1f) return;
            barb.gameObject.SetActive(false);
            line.Hide();
            reelVel = 0f;
            State = HarpoonState.Ready;
        }

        void TickReloading(float dt)
        {
            reelVel = 0f;
            reloadT += dt;
            if (reloadT >= HarpoonTuning.reloadSeconds) State = HarpoonState.Ready;
        }

        /// **The live line.** The winch shortens the rest length at an eased
        /// rate (it stalls under strain, so easing off lets it take up again);
        /// the line is a spring-damper on its stretch; the load is dragged
        /// through the water by it against its own drag and inertia, moved
        /// kinematically along the surface (its own Update rides the swell).
        /// Sub-stepped at 120 Hz so a long frame cannot ring the spring.
        void TickHooked(float dt)
        {
            var ov = hooked as IOverboardTarget;
            if (Gone(hooked) || (ov != null && ov.Resolved))
            {
                // Taken out of our hands (rescued another way, sank): the
                // line comes home empty, nothing to cost.
                hooked = null;
                HoldFull = false;
                returnFrom = barb.position;
                returnT = 0f;
                State = HarpoonState.Returning;
                return;
            }

            Vector3 bow = Flat(MuzzlePos);
            Vector3 load = Flat(hooked.Transform.position);
            float m = Mathf.Max(0.05f, hooked.HarpoonMass);

            if (HoldFull)
            {
                // Held alongside at the rail until there is room.
                Vector3 off = load - bow;
                off = off.sqrMagnitude > 1e-4f ? off.normalized : -transform.forward;
                load = bow + off * HarpoonTuning.railMetres;
                loadVel = Flat(motor != null ? motor.Velocity : Vector3.zero);
                MoveLoad(ov, load);
                EaseTension(0.1f, dt);
                reelVel = 0f;
                DrawHooked(0f);
                if (Fits()) Deliver(ov);
                return;
            }

            float k = Mathf.Max(0.1f, HarpoonTuning.reelSpring);
            float c = Mathf.Max(0f, HarpoonTuning.reelDamper) * 2f * Mathf.Sqrt(k * m);
            float drag = Mathf.Max(0f, HarpoonTuning.waterDrag);
            float wantReel = HarpoonTuning.reelSpeed * Mathf.Max(0.05f, Crew.workRate) / Mathf.Sqrt(m)
                * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(HarpoonTuning.winchStallStart,
                    Mathf.Max(HarpoonTuning.winchStallStart + 0.01f, HarpoonTuning.strainBand), rawTension01)));
            float ease = Mathf.Max(0.02f, HarpoonTuning.reelEaseSeconds);
            float minLen = HarpoonTuning.railMetres * 0.5f;

            int steps = Mathf.Clamp(Mathf.CeilToInt(dt * 120f), 1, 12);
            float h = dt / steps;
            Vector3 bowWas = Flat(prevBow);
            float force = 0f;
            for (int i = 0; i < steps; i++)
            {
                Vector3 b = Vector3.Lerp(bowWas, bow, (i + 1) / (float)steps);
                reelVel += (wantReel - reelVel) * (1f - Mathf.Exp(-h / ease));
                lineLen = Mathf.Max(minLen, lineLen - reelVel * h);

                Vector3 toBow = b - load;
                float d = toBow.magnitude;
                Vector3 dir = d > 1e-4f ? toBow / d : Vector3.zero;
                float stretch = d - lineLen;
                float stretchRate = (d - prevDist) / h + reelVel;
                prevDist = d;
                force = stretch > 0f ? Mathf.Max(0f, k * stretch + c * stretchRate) : 0f;
                // A slack line never pays out more than a few metres of bight.
                lineLen = Mathf.Min(lineLen, d + 4f);

                loadVel = (loadVel + dir * (force / m) * h) / (1f + drag * h);
                load += loadVel * h;
            }
            prevBow = MuzzlePos;
            MoveLoad(ov, load);

            rawTension01 = force / Mathf.Max(0.1f, HarpoonTuning.snapForce);
            EaseTension(rawTension01, dt);
            lineForce = force;

            if (Tension01 >= HarpoonTuning.snapTension)
            {
                strainFor += dt;
                if (strainFor >= HarpoonTuning.snapHoldSeconds) { Snap(); return; }
            }
            else strainFor = 0f;

            float slack = Mathf.Max(0f, lineLen - Vector3.Distance(bow, load));
            DrawHooked(slack);

            if (Vector3.Distance(bow, load) <= HarpoonTuning.railMetres)
            {
                if (Fits()) Deliver(ov);
                else
                {
                    HoldFull = true;
                    Say("Hold full");
                }
            }
        }

        Vector3 prevBow;
        float lineForce;

        void DrawHooked(float slack)
        {
            barb.position = hooked.HookPoint;
            Vector3 dir = MuzzlePos - barb.position;
            if (dir.sqrMagnitude > 1e-4f) barb.rotation = Quaternion.LookRotation(-dir);
            line.Draw(MuzzlePos, barbAttach.position, Tension01, slack, StemTopWorld);
        }

        /// Kinematic: put it there, and park its own haul anchor on the same
        /// spot so its `MoveTowards(HaulAnchor)` is a no-op whichever Update
        /// runs first; its own Update still rides it on the swell.
        void MoveLoad(IOverboardTarget ov, Vector3 flat)
        {
            var tr = hooked.Transform;
            var p = new Vector3(flat.x, tr.position.y, flat.z);
            tr.position = p;
            if (ov != null) { ov.BeingHauled = true; ov.HaulAnchor = p; }
        }

        bool Fits()
        {
            int units = hooked.HarpoonHoldUnits;
            if (units <= 0) return true;
            if (voyage == null) voyage = Object.FindFirstObjectByType<SeaSick.Voyage.VoyageManager>();
            return voyage == null || voyage.CargoFits(units);
        }

        void Deliver(IOverboardTarget ov)
        {
            var who = Crew.hand;
            var load = hooked;
            hooked = null;
            HoldFull = false;
            lineForce = 0f;
            barb.gameObject.SetActive(false);
            line.Hide();
            reelVel = 0f;
            State = HarpoonState.Ready;
            if (ov != null)
            {
                ov.BeingHauled = false;
                ov.OnHauled(who != null ? who.DisplayName : "");
            }
            if (load != null) Say("Aboard");
        }

        void Snap()
        {
            Vector3 end = barbAttach.position;
            ReleaseLoad();
            line.Whip(end, true);
            Say("Snapped");
            StartReload();
        }

        void Cut()
        {
            Vector3 end = barbAttach.position;
            pendingShot = false;
            ReleaseLoad();
            line.Whip(end, false);
            Say("Cut");
            StartReload();
        }

        void StartReload()
        {
            barb.gameObject.SetActive(false);
            reloadT = 0f;
            reelVel = 0f;
            State = HarpoonState.Reloading;
        }

        /// Let the load go where it is; it drifts on its own again.
        void ReleaseLoad()
        {
            if (!Gone(hooked) && hooked is IOverboardTarget ov) ov.BeingHauled = false;
            hooked = null;
            HoldFull = false;
            lineForce = 0f;
            strainFor = 0f;
            rawTension01 = 0f;
        }

        void EaseTension(float want, float dt)
        {
            float tau = Mathf.Max(0.001f, HarpoonTuning.tensionSmoothSeconds);
            Tension01 = Mathf.Clamp01(Mathf.Lerp(Tension01, want, 1f - Mathf.Exp(-dt / tau)));
        }

        void Say(string word)
        {
            LastEventWord = word;
            LastEventAt = Time.time;
        }

        // --- the pull on the ship ----------------------------------------------

        /// The line's force, as an acceleration on her, eased (0.25 s) so a
        /// snap or a cut releases her without a step. The servo hull turns it
        /// into a lower target speed and a little yaw; the steamer takes it
        /// as a real force at the bow (`ShipMotor.ExternalPull`).
        void TickPull(float dt)
        {
            Vector3 want = Vector3.zero;
            if (State == HarpoonState.Hooked && !Gone(hooked) && !HoldFull)
            {
                Vector3 toLoad = Flat(hooked.Transform.position) - Flat(MuzzlePos);
                if (toLoad.sqrMagnitude > 1e-4f)
                    want = toLoad.normalized * (Mathf.Min(lineForce, HarpoonTuning.snapForce * 1.05f)
                                                * HarpoonTuning.pullScale);
            }
            pull = Vector3.Lerp(pull, want, 1f - Mathf.Exp(-dt / 0.25f));
            if (pull.sqrMagnitude < 1e-8f) pull = Vector3.zero;
            if (motor == null) return;
            motor.ExternalPull = pull;
            motor.ExternalPullPoint = MuzzlePos;
            Vector3 right = transform.right; right.y = 0f;
            motor.ExternalPullYaw = Vector3.Dot(pull, right.normalized) * HarpoonTuning.servoPullYaw;
        }

        // --- the fitting -----------------------------------------------------

        void TickMount(float dt)
        {
            Vector3? aimAt = null;
            if (State == HarpoonState.Flying || State == HarpoonState.Returning || State == HarpoonState.Hooked)
                aimAt = barb.position;
            else if (Target != null && Available) aimAt = Target.HookPoint;

            float want = 0f;
            if (aimAt.HasValue)
            {
                Vector3 local = mount.InverseTransformPoint(aimAt.Value);
                want = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg,
                    -HarpoonTuning.arcHalfDeg, HarpoonTuning.arcHalfDeg);
            }
            float have = swivel.localEulerAngles.y;
            if (have > 180f) have -= 360f;
            // Eased into the last few degrees so it settles instead of ticking,
            // and never faster than the swivel's own rate.
            float eased = Mathf.LerpAngle(have, want, 1f - Mathf.Exp(-dt / 0.12f));
            float next = Mathf.MoveTowardsAngle(have, eased, HarpoonTuning.swivelDegPerSec * dt);
            if (swivel != mount) swivel.localRotation = Quaternion.Euler(0f, next, 0f);

            if (drum != null && reelVel > 0.01f)
            {
                drumAngle = Mathf.Repeat(drumAngle + reelVel * HarpoonTuning.drumTurnsPerMetre * 360f * dt, 360f);
                drum.localRotation = Quaternion.Euler(drumAngle, 0f, 0f);
            }
        }

#if UNITY_EDITOR
        /// **Dev only, for editor evals** (docs/DEV-TOOLS.md "Bow harpoon"):
        /// floats a target `metres` dead ahead of the bow so a play test does
        /// not wait on luck. `kind`: "crate" (2 timber), "heavy" (4 stone,
        /// mass ~2), "loot" (3 kraken meat), "flotsam", "bottle". Returns what
        /// it did, or why not.
        public static string DevSpawnTargetAhead(string kind, float metres)
        {
            var gun = Player;
            if (gun == null) return "no HarpoonGun on the player ship (not in play mode?)";
            Vector3 fwd = gun.transform.forward; fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;
            float d = Mathf.Clamp(metres, 3f, HarpoonTuning.range);
            Vector3 at = Flat(gun.MuzzlePos + fwd * d);
            Transform hull = gun.transform;
            switch ((kind ?? "").ToLowerInvariant())
            {
                case "crate": FloatingCargo.Spawn(SeaSick.World.Res.Timber, 2, hull, at); break;
                case "heavy": FloatingCargo.Spawn(SeaSick.World.Res.Stone, 4, hull, at); break;
                case "loot":
                    var loot = FloatingCargo.Spawn(SeaSick.World.Res.Meat, 3, hull, at);
                    if (loot != null) loot.HarpoonKind = "loot";
                    break;
                case "flotsam": SeaSick.Ship.SeaLife.FlotsamCrate.Spawn(hull, at); break;
                case "bottle": SeaSick.Ship.SeaLife.MessageBottle.Spawn(hull, at); break;
                default: return "kind is one of: crate, heavy, loot, flotsam, bottle";
            }
            return kind + " " + d.ToString("F0") + " m ahead";
        }
#endif

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        static float FlatDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
