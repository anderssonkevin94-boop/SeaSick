using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **What a villager's body is doing**, over the top of the walk cycle.
    ///
    /// The crew rig has two clips, Idle and Walk, and that is all it needs to
    /// have: a sawyer sawing and a man dangling from the Hand are arm, hip and
    /// head poses laid over whichever clip is playing, plus a primitive tool.
    ///
    /// Pure show. Touches no ledger, no order, no store.
    ///
    /// **Why a bone layer and not more clips.** The rig is generated
    /// (`tools/blender/seasick_crew.py`) and its two clips are baked sine
    /// tracks over eight bones: `root, hips, chest, head, arm_L, arm_R, leg_L,
    /// leg_R`, generic, no knee and no elbow. Adding six more takes and
    /// re-authoring a controller buys nothing a sine over the same eight bones
    /// does not — and every new clip would have to be re-exported the next
    /// time the proportions move. So this runs in `LateUpdate`, after the
    /// Animator has written its pose, and bends the result.
    ///
    /// **Rotations are applied about the CHARACTER's axes, never the bone's.**
    /// The generator's own `swing()` carries the same warning in Blender: a
    /// bone hanging straight down has whatever roll the exporter chose, so
    /// "rotate about local X" is unpredictable and is how a walk cycle comes
    /// out swinging sideways. `A = body · Euler(d) · body⁻¹` is an honest
    /// world rotation about the body's right/up/forward, and it survives any
    /// re-export.
    ///
    /// In the Unity frame the body faces +Z, so a POSITIVE pitch about +X
    /// swings a hanging arm BACKWARD, -90° puts it straight out in front, and
    /// -180° puts it overhead. Everything below is written in those terms.
    ///
    /// Null-safe by construction: a missing bone is a bone that is not bent,
    /// never an exception. A rig with none of them makes this component a
    /// no-op rather than a crash at a camp.
    public class VillagerActing : MonoBehaviour
    {
        /// `Bend` (2026-09-24): stooping over a stack -- picking a load up
        /// off a store pile or a station's bay/rack, or setting one down.
        /// Appended last so nothing that stored a mode by number moves.
        ///
        /// `Mine` and `Lookout` (2026-10-01) arrived with the v15 clips:
        /// appended for the same reason.
        ///
        /// The rest (2026-10-01, "import and implement all the animations")
        /// are the v15 clips' own jobs, appended likewise: each plays its
        /// clip, and on a rig without the state falls back to the code pose
        /// it used to be (`CodePose`).
        public enum Mode { None, Chop, Saw, Hammer, Hoe, Stir, Carry, Dangle, Land, Bend, Mine, Lookout,
            Forage, Build, PickUp, SetDown, HuntWalk, Hunt, Farm, Smith, Cook, Mill, Quarry, Fletcher, Fisher,
            Reach, Eat, Crank }

        /// **The code pose a clip mode falls back to** on a rig whose
        /// controller lacks the state (what each job looked like before the
        /// v15 clips).
        public static Mode CodePose(Mode m)
        {
            switch (m)
            {
                case Mode.Mine: return Mode.Hammer;
                case Mode.Lookout: return Mode.None;
                case Mode.Forage: return Mode.Hoe;
                case Mode.Build: return Mode.Hammer;
                case Mode.PickUp: case Mode.SetDown: return Mode.Bend;
                case Mode.HuntWalk: return Mode.None;
                case Mode.Hunt: return Mode.Bend;
                case Mode.Farm: return Mode.Hoe;
                case Mode.Smith: return Mode.Hammer;
                case Mode.Cook: return Mode.Stir;
                case Mode.Mill: case Mode.Quarry: case Mode.Fletcher: return Mode.Hammer;
                case Mode.Fisher: return Mode.Bend;
                // The level 2 sawmill's crank: the saw pose on a rig without it.
                case Mode.Crank: return Mode.Saw;
                default: return m;
            }
        }

        /// What the body actually shows for `m`: its clip, or its code pose.
        Mode Resolve(Mode m) => UsesClip(m) ? m : CodePose(m);

        /// **How he walks when no job clip owns the body** (v15 gaits,
        /// 2026-10-01): `Stroll` = Walk, `Errand` = WalkBrisk, `Tired` =
        /// WalkTired (low mood), `Run` (raid alarm spear fetch, defence,
        /// rescue, raiders), `Scared` = RunScared (running to hide). Set by
        /// whoever drives the body; a rig without the state walks `Walk`.
        public enum Gait { Errand, Stroll, Tired, Run, Scared }
        public Gait WalkGait { get; set; }

        static readonly int WalkBriskId = Animator.StringToHash("WalkBrisk"),
            WalkTiredId = Animator.StringToHash("WalkTired"), WalkDeckId = Animator.StringToHash("WalkDeck"),
            RunId = Animator.StringToHash("Run"), RunScaredId = Animator.StringToHash("RunScared"),
            SickWalkId = Animator.StringToHash("SickWalk"), GangwayId = Animator.StringToHash("Gangway");
        public static readonly int WalkRateId = Animator.StringToHash("WalkRate");

        /// A gait state's ground speed at playback rate 1, m/s at game size
        /// (measured off the clips, `VillagerGaits`): play it at speed / this
        /// and the planted foot does not skate.
        public static float GaitSpeed(int state)
        {
            if (state == WalkBriskId) return VillagerGaits.BriskClip;
            if (state == WalkTiredId) return VillagerGaits.TiredClip;
            if (state == WalkDeckId) return VillagerGaits.DeckClip;
            if (state == RunId) return VillagerGaits.RunClip;
            if (state == RunScaredId) return VillagerGaits.ScaredClip;
            if (state == SickWalkId) return VillagerGaits.SickClip;
            if (state == GangwayId) return VillagerGaits.GangwayClip;
            if (state == CarryId) return VillagerGaits.CarryClip;
            if (state == HuntWalkId) return VillagerGaits.StalkClip;
            return VillagerGaits.WalkClip;   // Walk
        }

        /// **The speed a gait state is walked at** (2026-10-01): its clip's
        /// own ground speed times the rate it is shown at -- the body moves
        /// at THIS, so the foot stays planted.
        public static float GaitCruise(int state)
        {
            if (state == GangwayId) return GaitSpeed(state) * VillagerGaits.GangwayRate;
            return VillagerGaits.Cruise(GaitSpeed(state));
        }

        /// Playback rate for a body moving at `speed`: speed / the clip's
        /// own speed, so the stance foot moves with the ground. Clamped to
        /// `VillagerGaits.RateMin..RateMax` (the start/stop ramp below, a
        /// road on top).
        public static float GaitRate(int state, float speed)
            => Mathf.Clamp(speed / GaitSpeed(state), VillagerGaits.RateMin, VillagerGaits.RateMax);

        static readonly int CarryId = Animator.StringToHash("Carry"), HuntWalkId = Animator.StringToHash("HuntWalk");

        int locoPlaying;

        // **The walker's own speed** (2026-10-01): whoever moves the body
        // (`CampWorker.Walk`) says how fast, every frame it steps. The rate
        // follows that rather than a smoothed transform velocity, which
        // lagged a start and a stop by a quarter second -- a skate at each.
        float cmdSpeed;
        int cmdFrame = -10;
        public void Commanded(float speed) { cmdSpeed = speed; cmdFrame = Time.frameCount; }
        bool HasCommand => Time.frameCount - cmdFrame <= 1;
        float GroundSpeed => HasCommand ? cmdSpeed : Vector3.ProjectOnPlane(velocity, transform.up).magnitude;

        /// The state his walk shows right now (Carry/HuntWalk when that clip
        /// owns the body, else the `WalkGait` walk).
        int GaitState()
        {
            Mode m = Resolve(Current);
            if (m == Mode.Carry && UsesClip(Mode.Carry)) return CarryId;
            if (m == Mode.HuntWalk && UsesClip(Mode.HuntWalk)) return HuntWalkId;
            int want = WalkGait == Gait.Errand ? WalkBriskId : WalkGait == Gait.Tired ? WalkTiredId
                : WalkGait == Gait.Run ? RunId : WalkGait == Gait.Scared ? RunScaredId : WalkId;
            Bind();
            if (anim == null || !anim.HasState(0, want)) want = WalkId;
            return want;
        }

        /// **How fast this body walks right now**, m/s: the gait it shows,
        /// at the speed that keeps its feet planted (`GaitCruise`).
        public float CruiseSpeed() => GaitCruise(GaitState());

        /// Idle or the `WalkGait` walk, at the rate his speed asks for.
        void DriveGait()
        {
            if (anim == null || !anim.isActiveAndEnabled || !hasLocomotion) return;
            float v = GroundSpeed;
            bool walking = locoPlaying != 0 && locoPlaying != IdleId;
            bool moving = HasCommand ? (walking ? v > 0.02f : v > 0.05f)
                : (walking ? v > 0.18f : v > 0.3f);
            int want = IdleId;
            if (moving)
            {
                want = WalkGait == Gait.Errand ? WalkBriskId : WalkGait == Gait.Tired ? WalkTiredId
                    : WalkGait == Gait.Run ? RunId : WalkGait == Gait.Scared ? RunScaredId : WalkId;
                if (!anim.HasState(0, want)) want = WalkId;
            }
            if (hasWalkRate) anim.SetFloat(WalkRateId, GaitRate(want, v));
            if (want == locoPlaying) return;
            locoPlaying = want;
            var cur = anim.GetCurrentAnimatorStateInfo(0);
            if (!anim.IsInTransition(0) && cur.shortNameHash == want) return;
            if (anim.IsInTransition(0) && anim.GetNextAnimatorStateInfo(0).shortNameHash == want) return;
            anim.CrossFadeInFixedTime(want, ClipFadeSeconds, 0);
        }

        /// True when this body plays `m` as an authored clip.
        public bool Plays(Mode m) => UsesClip(m);

        /// Seconds since the shown pose/clip last changed (a one-shot's clock).
        public float ShownSeconds => modeClock;

        public Mode Current { get; private set; }

        /// The acting component on this body, added if it has none.
        public static VillagerActing On(Crew.CrewAgent hand)
        {
            if (hand == null) return null;
            var a = hand.GetComponent<VillagerActing>();
            return a != null ? a : hand.gameObject.AddComponent<VillagerActing>();
        }

        /// Change what the body is doing. `carrying` names the resource on
        /// the shoulder for `Carry` (a log for timber, a flat bundle for
        /// boards, a stack for stone or brick, else a sack), and `count` is
        /// how many units are in his arms — Kevin, 2026-09-23: *"if they
        /// carry 3 logs, you see three logs."* Kept at 1 for every other
        /// mode's tool, which never varies.
        ///
        /// Takes effect immediately as far as `Current` is concerned — the
        /// probes and the camp read it as the answer to "what is he doing" —
        /// but the POSE cross-fades, so a mode change is a movement rather
        /// than a snap.
        public void Set(Mode mode, string carrying = null) => Set(mode, carrying, 1);

        public void Set(Mode mode, string carrying, int count)
        {
            count = Mathf.Max(1, count);
            if (Current == mode && load == carrying && loadCount == count) return;
            // A work point belongs to the job it was given for: an anvil
            // must not pull the axe of the next job toward it.
            if (Current != mode) hasWork = false;
            Current = mode;
            load = carrying;
            loadCount = count;
        }

        /// **Where the tool lands**, in world space: the anvil face, the top
        /// of the log, the ground in front of the hoe, the middle of the pot.
        /// Optional -- without it each tool strikes a nominal spot in front
        /// of the body (`NominalWork`), which is where a worker standing at
        /// his `Worker_Stand` facing the `Bench_Anchor` has his bench. Call
        /// it AFTER `Set` (a mode change forgets it), every frame or once.
        /// A point more than `MaxWorkReach` away is ignored, so a stale or
        /// wrong anchor can never wrench the arm across the camp.
        public void WorkAt(Vector3 world)
        {
            workAt = world;
            hasWork = true;
        }

        public void ClearWork() => hasWork = false;

        // --- tunables (runtime-added component: these consts ARE the dials) --

        /// Seconds to fade a pose in or out. Long enough that a change reads
        /// as a movement, short enough that a chop that becomes a carry does
        /// not look like a stumble.
        public static float FadeSeconds = 0.15f;
        /// How long the stagger after the Hand sets somebody down lasts.
        public static float LandSeconds = 0.6f;

        const float Chop_Period = 1.25f;
        const float Saw_Period = 1.05f;
        const float Hammer_Period = 0.85f;
        const float Hoe_Period = 1.5f;
        const float Stir_Period = 1.6f;
        /// Down and up once. Timed from the moment the pose is shown
        /// (`modeClock`), so a set-down that lasts half a period is a single
        /// stoop down onto the stack, never a random slice of a cycle.
        const float Bend_Period = 1.2f;

        /// Shoulder to fingertip, metres. Only for a rig with no hand bone
        /// (the old generated crew): the grip is then this far down the arm.
        const float HandDrop = 0.60f;
        /// Wrist to the middle of the fist, metres along the forearm: where
        /// a haft actually sits in a closed hand.
        const float PalmReach = 0.07f;
        /// A `WorkAt` point further than this from the body is ignored.
        const float MaxWorkReach = 2.5f;
        const float TAU = Mathf.PI * 2f;

        // --- the tools, in BODY metres (the tool frame: grip at the origin,
        // +Y up the haft to the head, +Z the side that strikes) ---------------

        // Smith's hammer: a 34 cm haft gripped at the butt, the head across
        // its top with the face 10 cm out on the strike side.
        const float Hammer_Reach = 0.30f, Hammer_Face = 0.10f;
        // Felling axe: 67 cm haft, blade edge 13 cm out from the haft.
        const float Axe_Reach = 0.56f, Axe_Face = 0.13f;
        // Hoe: 1.1 m haft, a blade hanging 14 cm back toward the man.
        const float Hoe_Reach = 1.02f, Hoe_Face = 0.14f;
        // Handsaw: grip to the middle of the blade, and to its teeth.
        const float Saw_Reach = 0.33f, Saw_Face = 0.055f;

        // --- state ----------------------------------------------------------

        string load;                 // what Set() was last told to carry
        int loadCount = 1;           // how many units Set() was last told
        Mode shown = Mode.None;      // what the POSE is doing, which lags Current
        string shownLoad;
        int shownLoadCount = 1;
        float weight;                // 0..1 blend of `shown`
        float clock;                 // free-running, for the sine cycles
        float landTimer;
        float modeClock;             // seconds since `shown` last changed

        Transform hips, chest, head, armL, armR, legL, legR;
        Transform handL, handR;      // the wrists, when the rig has them
        Vector3 gripRestL, gripRestR; // arm-local grip for a rig with no hand bone
        Vector3 hipsRest;
        float side = 1f;             // +1 if arm_L sits on the body's +X side
        bool bound, tried;

        Vector3 workAt;              // see WorkAt
        bool hasWork;

        /// One prop per mode, built on first use and thereafter toggled. Kept
        /// by mode index so there is no dictionary and no per-frame lookup.
        /// One slot per `Mode`. They hang off THIS object, not a bone, and
        /// are placed in world space every frame (`PoseTool`).
        readonly GameObject[] tools = new GameObject[ModeCount];
        const int ModeCount = 32;
        GameObject carryProp;
        string carryPropFor;

        // Velocity, for the Dangle swing. Sampled off the transform rather
        // than asked of a Rigidbody: a parked hand has none.
        Vector3 lastAt;
        Vector3 velocity;
        bool sampled;

        void LateUpdate() => Step(Time.deltaTime);

        /// One frame of acting. Separate from `LateUpdate` so an edit-mode
        /// render (`VillagerToolShot`) can drive it with a fixed `dt`.
        void Step(float dt)
        {
            Bind();
            UndoUnkeyedBends();

            clock += dt;
            modeClock += dt;
            TrackVelocity(dt);

            // **A mode with an authored clip is the Animator's** (v15, see
            // `UsesClip`): no bone is bent, the state is cross-faded in
            // straight away and the props ride the bones. A clip mode on a
            // rig without the state shows its old code pose (`CodePose`).
            Mode want = Resolve(Current);
            if (UsesClip(want) || UsesClip(shown))
            {
                if (shown != want || shownLoad != load || shownLoadCount != loadCount)
                {
                    shown = want;
                    shownLoad = load;
                    shownLoadCount = loadCount;
                    weight = 0f;
                    landTimer = 0f;
                    modeClock = 0f;
                    RefreshProps();
                }
            }
            PlayClip(UsesClip(shown) ? shown : Mode.None);
            if (!UsesClip(shown)) DriveGait();
            else locoPlaying = 0;
            if (UsesClip(shown))
            {
                PlaceClipProps();
                return;
            }

            if (shown == Mode.Reach && want == Mode.Eat)
            {
                // The dish hand is already where eating holds it: straight
                // on, no drop of the arm between the two.
                shown = want;
                shownLoad = load;
                shownLoadCount = loadCount;
                modeClock = 0f;
                RefreshProps();
            }
            if (shown != want)
            {
                // Out of the old pose before into the new one. One fade, not
                // two overlapping ones — two arms interpolating between three
                // poses is how a sawyer ends up waving.
                weight -= dt / Mathf.Max(0.01f, FadeSeconds);
                if (weight <= 0f)
                {
                    weight = 0f;
                    shown = want;
                    shownLoad = load;
                    shownLoadCount = loadCount;
                    landTimer = 0f;
                    modeClock = 0f;
                    RefreshProps();
                }
            }
            else
            {
                if (shownLoad != load || shownLoadCount != loadCount)
                {
                    shownLoad = load;
                    shownLoadCount = loadCount;
                    RefreshProps();
                }
                weight = Mathf.MoveTowards(weight, shown == Mode.None ? 0f : 1f,
                    dt / Mathf.Max(0.01f, FadeSeconds));
            }

            // The stagger runs itself out and hands the body back. Nothing
            // else has to remember to clear it, which is what makes it safe
            // for the Hand to fire and forget.
            if (shown == Mode.Land && Current == Mode.Land)
            {
                landTimer += dt;
                if (landTimer >= LandSeconds) Current = Mode.None;
            }

            if (!bound) return;
            // A shown tool is placed even at zero weight (the first frame of
            // a fade-in), or it would hang wherever it was last put.
            if (weight <= 0.0001f && !HasTool(shown)) { PlaceDish(); return; }
            Pose(weight);
            PlaceDish();
        }

        // --- the level 2 sawmill's crank (2026-10-01) ------------------------

        static readonly List<VillagerActing> live = new List<VillagerActing>();
        static readonly int CrankId = Animator.StringToHash("Crank");
        void OnEnable() { if (!live.Contains(this)) live.Add(this); }

        /// **Where in his `Crank` loop a body standing within `radius` of
        /// `at` is (0..1)**, for `MillCrankWheels`: the clip turns the crank
        /// once a loop, so the wheels read their angle off this and stay in
        /// step with his fists. False when nobody there is playing it.
        public static bool CrankPhaseNear(Vector3 at, float radius, out float phase)
        {
            phase = 0f;
            for (int i = 0; i < live.Count; i++)
            {
                var a = live[i];
                if (a == null || a.anim == null || !a.anim.isActiveAndEnabled) continue;
                Vector3 d = a.transform.position - at;
                d.y = 0f;
                if (d.sqrMagnitude > radius * radius) continue;
                var st = a.anim.GetCurrentAnimatorStateInfo(0);
                if (st.shortNameHash != CrankId && a.anim.IsInTransition(0))
                    st = a.anim.GetNextAnimatorStateInfo(0);
                if (st.shortNameHash != CrankId) continue;
                phase = st.normalizedTime - Mathf.Floor(st.normalizedTime);
                return true;
            }
            return false;
        }

        void OnDisable()
        {
            live.Remove(this);
            // Switched off with the camp. The pose stops where it is; the
            // Animator owns the body again from the next frame.
            weight = 0f;
            shown = Mode.None;
            for (int i = 0; i < tools.Length; i++)
                if (tools[i] != null) tools[i].SetActive(false);
            if (carryProp != null) carryProp.SetActive(false);
            if (dishProp != null) dishProp.SetActive(false);
            // A work clip has no transition out of its state: hand the body
            // back to Idle/Walk, or it saws on with nobody to stop it.
            PlayClip(Mode.None);
        }

        void OnDestroy()
        {
            // The props live on the body, not on this component, so they
            // outlive it (`CampWorker` destroys just the component) unless
            // they are taken down by hand. (In edit mode -- the tool shot --
            // they go with the body, and Destroy is not allowed there.)
            if (!Application.isPlaying) return;
            for (int i = 0; i < tools.Length; i++)
                if (tools[i] != null) Destroy(tools[i]);
            if (carryProp != null) Destroy(carryProp);
            if (dishProp != null) Destroy(dishProp);
        }

        // --- the rig --------------------------------------------------------

        /// Find the eight bones ONCE. Names are the generator's, matched
        /// case-insensitively with a `contains` fallback, so a re-export that
        /// renames `chest` to `spine_01` still bends a spine.
        void Bind()
        {
            if (tried) return;
            tried = true;

            var all = GetComponentsInChildren<Transform>(true);
            hips = Bone(all, "hips", "hip", "pelvis");
            chest = Bone(all, "chest", "chest", "spine");
            head = Bone(all, "head", "head");
            armL = Bone(all, "arm_L", "upper_arm.L", "arm_l");
            armR = Bone(all, "arm_R", "upper_arm.R", "arm_r");
            legL = Bone(all, "leg_L", "thigh.L", "leg_l");
            legR = Bone(all, "leg_R", "thigh.R", "leg_r");

            if (hips != null) hipsRest = hips.localPosition;

            // The wrists. Astra's deckhand has `upper_arm -> forearm -> hand`
            // and the Idle/Walk clips bend the elbow, so the grip is read off
            // the live hand bone every frame, never assumed from the shoulder.
            handL = HandUnder(armL);
            handR = HandUnder(armR);
            // No hand bone (the old generated crew: one bone per arm): the
            // grip is a fingertip's drop below the shoulder, frozen into the
            // arm's frame so it swings with it.
            if (armL != null)
                gripRestL = armL.InverseTransformPoint(armL.position - transform.up * HandDrop * BodyScale);
            if (armR != null)
                gripRestR = armR.InverseTransformPoint(armR.position - transform.up * HandDrop * BodyScale);

            // WHICH SIDE arm_L is actually on, read off the rig rather than
            // assumed from the letter. The generator authors forward as +X and
            // yaws it into Blender's -Y on the way out, so the L/R of the name
            // is a mirror of the source, not a promise about the game's +X.
            if (armL != null)
            {
                float x = transform.InverseTransformPoint(armL.position).x;
                if (Mathf.Abs(x) > 0.001f) side = Mathf.Sign(x);
            }

            bound = armL != null || armR != null || chest != null || hips != null;

            spine = Bone(all, "spine", "spine", "chest");
            anim = GetComponentInChildren<Animator>(true);
            if (anim != null && anim.runtimeAnimatorController != null)
            {
                for (int i = 0; i < ClipModes.Length; i++)
                {
                    int h = Animator.StringToHash(ClipModes[i].ToString());
                    clipHash[(int)ClipModes[i]] = anim.HasState(0, h) ? h : 0;
                }
                foreach (var p in anim.parameters)
                {
                    if (p.nameHash == ClipRateId && p.type == AnimatorControllerParameterType.Float) hasClipRate = true;
                    if (p.nameHash == WalkRateId && p.type == AnimatorControllerParameterType.Float) hasWalkRate = true;
                }
                hasLocomotion = anim.HasState(0, IdleId) && anim.HasState(0, WalkId);
            }
        }

        // --- authored clips (v15 deckhand, 2026-10-01) -------------------------
        //
        // `Chop`, `Saw`, `Mine`, `Carry` and `Lookout` are animation clips
        // (`CrewClipsV15Import` adds them to `CrewAnimator` as states of the
        // same names), authored against the real tree, mill bench, rock and
        // load. A body whose controller has the state plays it and nothing
        // here bends a bone; one without (a different rig) falls back to the
        // code poses below (`Mine` as the hammer swing, `Lookout` as none).

        static readonly Mode[] ClipModes = { Mode.Chop, Mode.Saw, Mode.Mine, Mode.Carry, Mode.Lookout,
            Mode.Forage, Mode.Build, Mode.PickUp, Mode.SetDown, Mode.HuntWalk, Mode.Hunt, Mode.Farm,
            Mode.Smith, Mode.Cook, Mode.Mill, Mode.Quarry, Mode.Fletcher, Mode.Fisher, Mode.Crank };
        static readonly int ClipRateId = Animator.StringToHash("ClipRate");
        static readonly int IdleId = Animator.StringToHash("Idle");
        static readonly int WalkId = Animator.StringToHash("Walk");
        /// Cross-fade into and out of a clip, seconds.
        public static float ClipFadeSeconds = 0.2f;

        // **The one-shots' prop timing** (clips.json, 30 fps, game metres
        // from his root: x right, y up, z forward).
        const float PickUpGrabFrame = 26f, PickUpSocketFrame = 45f;
        static readonly Vector3 PickUpLoadAt = new Vector3(0f, 0f, 0.628f);
        const float SetDownReleaseFrame = 3f, SetDownLandFrame = 16f;
        static readonly Vector3 SetDownLoadAt = new Vector3(0f, 0f, 0.68f);
        const float SetDownLoadYaw = 4f;
        Vector3 propFrom;            // a one-shot's captured prop pose
        Quaternion propFromRot = Quaternion.identity;
        bool propCaptured;

        // **Where the props ride**, in the BONE's own local units, read off
        // the v15 rest pose (`CrewClipsV15Import` re-measures and logs them).
        // Placed with TransformPoint every frame from a prop parented to the
        // BODY, never parented to the bone: the bones carry the rig's x100
        // unit scale, and a bone-parented prop with a local offset is how the
        // tools exploded before (memory: astra-rig-scale-trap).
        //
        // The tool frame (origin mid-fist, +Y up the haft, +Z the working
        // face) on the bone named `hand.L`, his right hand.
        public static readonly Vector3 ToolGripLocal = new Vector3(0f, 0.00101768528f, -0.000174226137f);
        public static readonly Quaternion ToolGripRot = new Quaternion(0.5f, 0.5f, 0.5f, -0.5f);
        // The load's bottom centre on the `spine` bone, body axes (the clip
        // keeps the fists on its underside on every frame).
        public static readonly Vector3 CarrySocketLocal = new Vector3(0f, 0.00105134305f, 0.0025f);

        Animator anim;
        Transform spine;
        readonly int[] clipHash = new int[ModeCount];
        int playing;                 // the clip state cross-faded to, 0 = locomotion
        bool hasClipRate, hasLocomotion, hasWalkRate;

        bool UsesClip(Mode m)
        {
            int i = (int)m;
            if (i <= 0 || i >= clipHash.Length) return false;
            Bind();
            return clipHash[i] != 0 && anim != null && anim.isActiveAndEnabled;
        }

        void PlayClip(Mode m)
        {
            int want = m == Mode.None ? 0 : clipHash[(int)m];
            if (want == playing) return;
            playing = want;
            if (anim == null || !anim.isActiveAndEnabled) return;
            locoPlaying = 0;     // DriveGait picks the walk from here
            if (want != 0) anim.CrossFadeInFixedTime(want, ClipFadeSeconds, 0);
            else if (hasLocomotion)
                anim.CrossFadeInFixedTime(velocity.magnitude > 0.3f ? WalkId : IdleId, ClipFadeSeconds, 0);
            if (want == 0 && hasClipRate) anim.SetFloat(ClipRateId, 1f);
        }

        /// The hand holding the tool (the wrist bone under `ToolArm`).
        Transform ToolHand => ToolArm == armL ? handL : ToolArm == armR ? handR : null;

        void PlaceClipProps()
        {
            if (anim != null && hasClipRate)
            {
                // Carry and HuntWalk walk in place like the gaits: the rate
                // is his speed over the clip's own (feet planted), and a
                // carrier standing still stands still.
                float v = GroundSpeed;
                if (shown == Mode.Carry)
                    anim.SetFloat(ClipRateId, v < 0.03f ? 0f : GaitRate(CarryId, v));
                else if (shown == Mode.HuntWalk)
                    anim.SetFloat(ClipRateId, v < 0.03f ? 0.5f : GaitRate(HuntWalkId, v));
            }
            var tool = tools[(int)shown];
            var hand = ToolHand;
            if (tool != null && tool.activeSelf && hand != null)
                tool.transform.SetPositionAndRotation(hand.TransformPoint(ToolGripLocal), hand.rotation * ToolGripRot);
            if (carryProp == null || spine == null) return;
            Vector3 sockAt = spine.TransformPoint(CarrySocketLocal);
            Quaternion sockRot = spine.rotation;
            float f = modeClock * 30f;
            if (shown == Mode.Carry)
                carryProp.transform.SetPositionAndRotation(sockAt, sockRot);
            else if (shown == Mode.PickUp)
            {
                // On the ground 0.63 m ahead until the grip (frame 26), then
                // in his fists (held at the offset it had from them at the
                // grip), seated on the carry socket from frame 45.
                Vector3 ground = transform.TransformPoint(PickUpLoadAt);
                Vector3 fists = FistsMid();
                if (f < PickUpGrabFrame || !propCaptured && handL == null)
                {
                    carryProp.transform.SetPositionAndRotation(ground, transform.rotation);
                    propCaptured = false;
                    return;
                }
                if (!propCaptured)
                {
                    propFrom = Quaternion.Inverse(transform.rotation) * (ground - fists);
                    propCaptured = true;
                }
                Vector3 inHands = fists + transform.rotation * propFrom;
                float k = Mathf.SmoothStep(0f, 1f, (f - (PickUpSocketFrame - 6f)) / 6f);
                carryProp.transform.SetPositionAndRotation(Vector3.Lerp(inHands, sockAt, k),
                    Quaternion.Slerp(transform.rotation, sockRot, k));
            }
            else if (shown == Mode.SetDown)
            {
                // On the socket until he lets go (frame 3), then it falls
                // and lands 0.68 m ahead, turned 4 degrees, on frame 16.
                if (f < SetDownReleaseFrame)
                {
                    carryProp.transform.SetPositionAndRotation(sockAt, sockRot);
                    propFrom = sockAt; propFromRot = sockRot; propCaptured = true;
                    return;
                }
                if (!propCaptured) { propFrom = sockAt; propFromRot = sockRot; propCaptured = true; }
                Vector3 rest = transform.TransformPoint(SetDownLoadAt);
                Quaternion restRot = transform.rotation * Quaternion.Euler(0f, SetDownLoadYaw, 0f);
                float t = Mathf.Clamp01((f - SetDownReleaseFrame) / (SetDownLandFrame - SetDownReleaseFrame));
                Vector3 at = Vector3.Lerp(propFrom, rest, t);
                // Falls: the drop is t^2 (gravity), the forward drift linear.
                at.y = Mathf.Lerp(propFrom.y, rest.y, t * t);
                carryProp.transform.SetPositionAndRotation(at, Quaternion.Slerp(propFromRot, restRot, t));
            }
        }

        /// Between his two fists (the middle of each closed hand).
        Vector3 FistsMid()
        {
            if (handL == null || handR == null) return transform.TransformPoint(PickUpLoadAt);
            return 0.5f * (handL.TransformPoint(ToolGripLocal) + handR.TransformPoint(ToolGripLocal));
        }

        static Transform Bone(Transform[] all, string exact, params string[] contains)
        {
            for (int i = 0; i < all.Length; i++)
                if (string.Equals(all[i].name, exact, System.StringComparison.OrdinalIgnoreCase))
                    return all[i];
            for (int c = 0; c < contains.Length; c++)
                for (int i = 0; i < all.Length; i++)
                    if (all[i].name.IndexOf(contains[c],
                            System.StringComparison.OrdinalIgnoreCase) >= 0)
                        return all[i];
            return null;
        }

        /// The shallowest bone under `arm` with "hand" in its name -- the
        /// wrist, not a finger (`hand_index_01`) that happens to share it.
        static Transform HandUnder(Transform arm)
        {
            if (arm == null) return null;
            Transform best = null;
            int bestDepth = int.MaxValue;
            foreach (var t in arm.GetComponentsInChildren<Transform>(true))
            {
                if (t == arm || t.name.IndexOf("hand", System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                int depth = 0;
                for (var p = t; p != null && p != arm; p = p.parent) depth++;
                if (depth < bestDepth) { best = t; bestDepth = depth; }
            }
            return best;
        }

        /// The body's own scale: every tool length and nominal work spot is
        /// in BODY metres. The bones' lossy scale (~92x on the deckhand) is
        /// never used for anything.
        float BodyScale
        {
            get
            {
                float s = transform.lossyScale.y;
                return s > 1e-4f ? s : 1f;
            }
        }

        /// The middle of the fist on `arm`, in world space, as the rig is
        /// posed RIGHT NOW (clip plus whatever has been bent this frame).
        Vector3 GripOf(Transform arm)
        {
            Transform hand = arm == armL ? handL : arm == armR ? handR : null;
            if (hand != null)
            {
                Vector3 wrist = hand.position;
                Transform fore = hand.parent;
                Vector3 along = fore != null && fore != transform ? wrist - fore.position : -transform.up;
                if (along.sqrMagnitude < 1e-10f) along = -transform.up;
                return wrist + along.normalized * (PalmReach * BodyScale);
            }
            return arm.TransformPoint(arm == armL ? gripRestL : gripRestR);
        }

        void TrackVelocity(float dt)
        {
            if (dt <= 0f) return;
            Vector3 here = transform.position;
            if (sampled) velocity = Vector3.Lerp(velocity, (here - lastAt) / dt,
                1f - Mathf.Exp(-8f * dt));
            lastAt = here;
            sampled = true;
        }

        // --- the poses ------------------------------------------------------

        void Pose(float w)
        {
            // Everything each mode wants to say, in one flat set of numbers.
            // Degrees about the BODY's axes: pitch fore-and-aft, yaw about the
            // spine, roll sideways. `armIn` is positive toward the centreline,
            // so it reads the same for both arms whichever side they are on.
            float armLPitch = 0f, armRPitch = 0f, armLIn = 0f, armRIn = 0f;
            float chestPitch = 0f, chestRoll = 0f, chestYaw = 0f;
            float headPitch = 0f, headYaw = 0f;
            float hipsPitch = 0f, hipsYaw = 0f, hipsDrop = 0f;
            float legLPitch = 0f, legRPitch = 0f;
            bool movesLegs = false;   // modes that own the legs and the hips' height
            // Tool modes: the body leans and bobs from the numbers here, but
            // the ARMS are aimed afterwards by `PoseTool` so the tool lands
            // on the work, and `stroke` is where in the stroke he is (0 = the
            // blow landing, 1 = the top of the lift).
            float stroke = 0f;

            switch (shown)
            {
                case Mode.Chop:
                {
                    // Two-handed, overhead, and the DOWN-stroke is the fast
                    // half — a symmetric sine reads as scrubbing, not felling.
                    float k = Strike(clock / Chop_Period, 0.62f);
                    stroke = k;
                    chestPitch = Mathf.Lerp(24f, -7f, k);
                    hipsPitch = Mathf.Lerp(9f, -3f, k);
                    headPitch = Mathf.Lerp(15f, 1f, k);
                    break;
                }

                case Mode.Saw:
                {
                    // `stroke` here is the push (+1) and pull (-1) along the
                    // cut, not a lift.
                    float s = Mathf.Sin(TAU * clock / Saw_Period);
                    stroke = s;
                    chestPitch = 15f + 7f * s;
                    hipsYaw = 8f * s;
                    hipsPitch = 7f;
                    headPitch = 17f;
                    break;
                }

                case Mode.Hammer:
                case Mode.Mine:      // no clip on this rig: the hammer swing
                {
                    // One arm swings; the other holds the work on the anvil,
                    // which is what makes it read as a smith rather than a
                    // man waving.
                    float k = Strike(clock / Hammer_Period, 0.58f);
                    stroke = k;
                    chestPitch = Mathf.Lerp(16f, 4f, k);
                    headPitch = 16f;
                    hipsPitch = 6f;
                    break;
                }

                case Mode.Hoe:
                {
                    // Lift, then chop the blade into the ground and bend
                    // into it. The bend is in the hips as well as the chest:
                    // a knee-less rig has nothing else to fold at.
                    float k = Strike(clock / Hoe_Period, 0.55f);
                    stroke = k;
                    chestPitch = Mathf.Lerp(36f, 17f, k);
                    hipsPitch = Mathf.Lerp(17f, 6f, k);
                    hipsDrop = -0.05f * (1f - k);
                    headPitch = 9f;
                    movesLegs = true;
                    break;
                }

                case Mode.Stir:
                {
                    // `stroke` is the angle round the pot, in radians.
                    float p = TAU * clock / Stir_Period;
                    stroke = p;
                    chestPitch = 21f;
                    chestYaw = 4f * Mathf.Sin(p);
                    headPitch = 19f;
                    break;
                }

                case Mode.Carry:
                {
                    // Both arms up and in, reading as holding a stack rather
                    // than steadying a single shoulder load — 2026-09-23,
                    // now that `Carry` shows a whole armful. The LEGS are
                    // deliberately untouched: this is the one acting mode
                    // that plays while they are walking, and the walk cycle
                    // is the thing that sells the weight.
                    armLPitch = -150f;
                    armLIn = 18f;
                    armRPitch = -46f;
                    armRIn = 20f;
                    chestRoll = -5f * side;
                    chestPitch = 6f;
                    headPitch = 4f;
                    break;
                }

                case Mode.Bend:
                {
                    // A stoop over a stack: both hands down and forward,
                    // folded at the chest and hips (a knee-less rig has
                    // nothing else to fold at). Held long, it loops -- a man
                    // sorting a load off a rack; held for a set-down, it is
                    // one stoop. No hip drop: with no knees that would sink
                    // the feet into the ground.
                    float k = 0.5f - 0.5f * Mathf.Cos(TAU * modeClock / Bend_Period);
                    armLPitch = armRPitch = Mathf.Lerp(-24f, -66f, k);
                    armLIn = armRIn = 12f;
                    chestPitch = Mathf.Lerp(10f, 38f, k);
                    hipsPitch = Mathf.Lerp(4f, 18f, k);
                    headPitch = Mathf.Lerp(6f, 16f, k);
                    break;
                }

                case Mode.Reach:
                {
                    // A lean into the reach, back up with the dish (the arm
                    // itself is `PoseMeal`'s).
                    float u = Mathf.Clamp01(modeClock / ReachSeconds);
                    float lean = Mathf.Sin(Mathf.PI * Mathf.Clamp01(u / (ReachGrabAt * 2f)));
                    chestPitch = 12f * lean;
                    hipsPitch = 4f * lean;
                    headPitch = 6f + 8f * lean;
                    break;
                }

                case Mode.Eat:
                {
                    // Looks down into the dish, up as the bite comes to him.
                    float c = BiteLift(modeClock);
                    chestPitch = Mathf.Lerp(6f, 2f, c);
                    headPitch = Mathf.Lerp(16f, -2f, c);
                    break;
                }

                case Mode.Dangle:
                {
                    // Held by the scruff: arms up, legs swinging out of step
                    // with each other, the whole body trailing whichever way
                    // the Hand is dragging them.
                    float t = clock;
                    armLPitch = -166f + 9f * Mathf.Sin(t * 2.2f);
                    armRPitch = -170f + 9f * Mathf.Sin(t * 2.2f + 1.3f);
                    armLIn = armRIn = -6f;
                    legLPitch = 24f * Mathf.Sin(t * 2.6f);
                    legRPitch = 24f * Mathf.Sin(t * 2.6f + 2.1f);
                    hipsPitch = -9f;
                    chestPitch = -13f + 5f * Mathf.Sin(t * 1.4f);
                    headPitch = 17f + 6f * Mathf.Sin(t * 1.9f);
                    movesLegs = true;

                    Vector3 v = transform.InverseTransformDirection(velocity);
                    chestPitch += Mathf.Clamp(-v.z * 4f, -26f, 26f);
                    chestRoll += Mathf.Clamp(v.x * 4f, -26f, 26f);
                    hipsYaw += Mathf.Clamp(v.x * 2f, -14f, 14f);
                    break;
                }

                case Mode.Land:
                {
                    // Down hard and up slowly, once. `Pow` front-loads it so
                    // the crouch has already happened by the time the player
                    // has let go.
                    float q = Mathf.Clamp01(landTimer / Mathf.Max(0.05f, LandSeconds));
                    float c = Mathf.Sin(Mathf.PI * Mathf.Pow(q, 0.55f));
                    hipsDrop = -0.26f * c;
                    hipsPitch = 14f * c;
                    legLPitch = -28f * c;
                    legRPitch = 26f * c;
                    armLPitch = armRPitch = -64f * c;
                    armLIn = armRIn = -22f * c;
                    chestPitch = 27f * c;
                    headPitch = -12f * c;
                    movesLegs = true;
                    break;
                }
            }

            // `armIn` is toward the centreline, which is -X for the arm on the
            // +X side. A positive roll about +Z swings a hanging arm toward
            // +X, so the sign of "in" flips with the side the arm is on.
            bool tool = HasTool(shown);
            if (!tool)
            {
                Turn(armL, armLPitch, 0f, -armLIn * side, w);
                Turn(armR, armRPitch, 0f, armRIn * side, w);
            }
            Turn(chest, chestPitch, chestYaw, chestRoll, w);
            Turn(head, headPitch, headYaw, 0f, w);
            Turn(hips, hipsPitch, hipsYaw, 0f, w);
            if (movesLegs)
            {
                Turn(legL, legLPitch, 0f, 0f, w);
                Turn(legR, legRPitch, 0f, 0f, w);
            }

            if (hips != null && hipsDrop != 0f)
            {
                // **Toward the REST position, not away from the current one.**
                // Both clips key every bone's location every frame, so adding
                // to what the Animator wrote would be safe today and would
                // silently integrate to infinity the day a clip stops doing
                // that. Lerping from the live pose keeps the blend smooth
                // without ever accumulating.
                // The drop is metres along the BODY's up; converted into the
                // hips' parent space, because a rig can carry its unit scale
                // on the bones (Astra's deckhand: ~92x) and a raw local
                // offset then sank a crouching villager 24 m.
                Vector3 drop = hips.parent != null
                    ? hips.parent.InverseTransformVector(transform.up * hipsDrop)
                    : new Vector3(0f, hipsDrop, 0f);
                Vector3 want = hipsRest + drop;
                Vector3 hipsBefore = hips.localPosition;
                hips.localPosition = Vector3.Lerp(hips.localPosition, want, w);
                hipsWritten = true; hipsBeforePos = hipsBefore; hipsAfterPos = hips.localPosition;
            }

            // Arms LAST for a tool: aimed from where the shoulders are after
            // the lean, so the blow lands where it is meant to.
            if (tool) PoseTool(shown, stroke, w);
            if (shown == Mode.Reach || shown == Mode.Eat) PoseMeal(w);
        }

        // --- taking a meal and eating it (2026-10-01) ------------------------
        //
        // Kevin: *"when villagers are performing interaction actions like
        // taking a meal to eat it i notice they do the 'pick up' animation
        // from the ground for a split second, holding a box that isn't
        // relevant."* A meal is not a haul: no squat, no carried stack. He
        // REACHES to the counter / store at waist height with his left hand
        // (`Reach`, code-bent on Idle, the dish appears in the hand at the
        // grab), then EATS (`Eat`): the dish held at his chest, the right
        // hand to his mouth and back, the food shrinking, the dish gone when
        // the pose ends. No clip: there is no Eat/Reach take yet.

        /// Seconds of the reach: out to the counter, the grab, back.
        public static float ReachSeconds = 0.6f;
        /// Share of the reach at which the hand closes on the dish.
        const float ReachGrabAt = 0.45f;
        /// How long a hand stands eating (`CampWorker` holds him this long).
        public static float EatSeconds = 3.4f;
        /// One bite: dish to mouth and back.
        const float BitePeriod = 1.1f;
        /// The first bite starts after this (the dish settles first).
        const float BiteLead = 0.3f;
        /// How far the food goes down by the last bite (0 = all of it).
        const float FoodLeft = 0.25f;

        // Body-local metres (x his right, y up from the feet, z forward):
        // the counter he reaches to, where he holds the dish, his mouth.
        // Read off the v15 deckhand: shoulders (+-0.29, 1.00, 0), arm to the
        // fist ~0.45 m, the head from y 1.10 with its face ~0.2 m forward.
        static readonly Vector3 CounterAt = new Vector3(0.17f, 0.80f, 0.42f);
        static readonly Vector3 DishHoldAt = new Vector3(0.12f, 0.82f, 0.36f);
        static readonly Vector3 DishTopAt = new Vector3(0.04f, 0.93f, 0.36f);
        static readonly Vector3 MouthAt = new Vector3(-0.02f, 1.20f, 0.24f);

        /// 0 with the hand at the dish, 1 at his mouth.
        static float BiteLift(float t)
        {
            if (t < BiteLead) return 0f;
            return 0.5f - 0.5f * Mathf.Cos(TAU * (t - BiteLead) / BitePeriod);
        }

        /// A body-local point with its x on the dish hand's side (the arm
        /// that does NOT hold tools: his left).
        Vector3 DishSide(Vector3 v) => transform.TransformPoint(new Vector3(-side * v.x, v.y, v.z));

        void PoseMeal(float w)
        {
            Transform dishArm = OffArm, eatArm = ToolArm;
            Vector3 hold = DishSide(DishHoldAt);
            if (shown == Mode.Reach)
            {
                float u = Mathf.Clamp01(modeClock / ReachSeconds);
                Vector3 rest = dishArm != null ? GripOf(dishArm) : hold;
                Vector3 counter = DishSide(CounterAt);
                Vector3 target = u < ReachGrabAt
                    ? Vector3.Lerp(rest, counter, Mathf.SmoothStep(0f, 1f, u / ReachGrabAt))
                    : Vector3.Lerp(counter, hold, Mathf.SmoothStep(0f, 1f, (u - ReachGrabAt) / (1f - ReachGrabAt)));
                TwoBone(dishArm, target, w);
                return;
            }
            TwoBone(dishArm, hold, w);
            float c = BiteLift(modeClock);
            Vector3 bite = Vector3.Lerp(DishSide(DishTopAt), transform.TransformPoint(new Vector3(MouthAt.x * side, MouthAt.y, MouthAt.z)), c);
            TwoBone(eatArm, bite, w);
        }

        /// **Two-bone reach**: bend the upper arm and forearm so the middle
        /// of the fist lands on `target` (clamped to the arm's length), the
        /// elbow falling down and out. Blended by `w` over the clip, and
        /// recorded so the next frame starts from the clip again. A rig with
        /// no forearm bone just points the arm (`Aim`).
        void TwoBone(Transform upper, Vector3 target, float w)
        {
            if (upper == null || w <= 0f) return;
            Transform hand = upper == armL ? handL : upper == armR ? handR : null;
            Transform fore = hand != null ? hand.parent : null;
            if (fore == null || fore == upper || fore.parent != upper) { Aim(upper, target, w); return; }

            Vector3 S = upper.position, E = fore.position, F = GripOf(upper);
            float a = (E - S).magnitude, b = (F - E).magnitude;
            Vector3 toT = target - S;
            if (a < 1e-5f || b < 1e-5f || toT.sqrMagnitude < 1e-10f) return;
            float d = Mathf.Clamp(toT.magnitude, Mathf.Abs(a - b) + 1e-4f, a + b - 1e-4f);
            Vector3 dir = toT.normalized;
            float outSign = Mathf.Sign(transform.InverseTransformPoint(S).x);
            Vector3 pole = -transform.up + transform.right * (0.7f * outSign) - transform.forward * 0.2f;
            Vector3 perp = Vector3.ProjectOnPlane(pole, dir);
            if (perp.sqrMagnitude < 1e-8f) perp = Vector3.ProjectOnPlane(-transform.up, dir);
            perp.Normalize();
            float cosA = Mathf.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f);
            Vector3 elbow = S + a * (dir * cosA + perp * Mathf.Sqrt(1f - cosA * cosA));

            Quaternion before = upper.localRotation;
            Quaternion to = Quaternion.FromToRotation(E - S, elbow - S) * upper.rotation;
            upper.rotation = Quaternion.Slerp(upper.rotation, to, w);
            Record(upper, before);

            E = fore.position;
            F = GripOf(upper);
            Vector3 fist = S + dir * d;
            before = fore.localRotation;
            to = Quaternion.FromToRotation(F - E, fist - E) * fore.rotation;
            fore.rotation = Quaternion.Slerp(fore.rotation, to, w);
            Record(fore, before);
        }

        /// Destroy that also works in an edit-mode render (`VillagerCarryShot`).
        static void Kill(GameObject go)
        {
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }

        GameObject dishProp;
        string dishPropFor;
        Transform dishFood;

        void EnsureDish()
        {
            string what = string.IsNullOrEmpty(shownLoad) ? Res.Meals : shownLoad;
            if (dishProp != null && dishPropFor == what) return;
            if (dishProp != null) Kill(dishProp);
            dishPropFor = what;
            dishProp = new GameObject("Dish_" + what);
            dishProp.transform.SetParent(transform, false);
            // Every meal in a bowl, 0.20 m across (the chibi's fists are
            // ~0.12 m): a raw one sits in it as itself, a cooked one as its
            // dish -- a bare apple in his hand vanished behind his fingers.
            CarryLook.Dish(what, dishProp.transform, 0.20f);
            dishFood = dishProp.transform.Find("Food");
        }

        /// The dish rides the dish hand's fist, level, every frame; hidden
        /// until the grab, its food going down bite by bite.
        void PlaceDish()
        {
            if (dishProp == null) return;
            bool meal = shown == Mode.Reach || shown == Mode.Eat;
            bool inHand = meal && (shown == Mode.Eat || modeClock >= ReachSeconds * ReachGrabAt);
            if (dishProp.activeSelf != inHand) dishProp.SetActive(inHand);
            if (!inHand) return;
            var arm = OffArm;
            Vector3 fist = arm != null ? GripOf(arm) : DishSide(DishHoldAt);
            dishProp.transform.SetPositionAndRotation(fist + transform.up * (0.07f * BodyScale),
                transform.rotation * Quaternion.Euler(0f, 20f * side, 0f));
            if (dishFood != null)
            {
                float k = shown == Mode.Eat
                    ? Mathf.Lerp(1f, FoodLeft, Mathf.Clamp01((modeClock - BiteLead) / Mathf.Max(0.1f, EatSeconds - BiteLead)))
                    : 1f;
                dishFood.localScale = new Vector3(1f, k, 1f) * Mathf.Lerp(1f, 0.85f, 1f - k);
            }
        }

        /// A strike cycle: 0 at the bottom of the stroke, 1 at the top, with
        /// `lift` of the period spent going up and the rest coming down.
        static float Strike(float cycles, float lift)
        {
            float u = Mathf.Repeat(cycles, 1f);
            return u < lift
                ? Mathf.SmoothStep(0f, 1f, u / lift)
                : 1f - Mathf.SmoothStep(0f, 1f, (u - lift) / (1f - lift));
        }

        /// Bend a bone about the BODY's axes, blended over whatever the
        /// Animator just wrote. See the class comment for why this is not a
        /// local-space Euler.
        void Turn(Transform b, float pitch, float yaw, float roll, float w)
        {
            if (b == null) return;
            if (pitch == 0f && yaw == 0f && roll == 0f) return;
            Quaternion body = transform.rotation;
            Quaternion a = body * Quaternion.Euler(pitch, yaw, roll) * Quaternion.Inverse(body);
            Quaternion before = b.localRotation;
            b.rotation = Quaternion.Slerp(b.rotation, a * b.rotation, w);
            Record(b, before);
        }

        /// Remember a bone this frame bent: the FIRST `before` (what the
        /// Animator left) and the LAST `after`, so two bends of one bone in
        /// a frame still undo to the clip.
        void Record(Transform b, Quaternion before)
        {
            if (written.TryGetValue(b, out var had)) before = had.before;
            written[b] = new Written { before = before, after = b.localRotation };
        }

        // --- the tool arm ---------------------------------------------------
        //
        // **Why the old tools struck the air** (Kevin, 2026-09-26: "the
        // villagers are holding the tools wrong"). The prop was parented to
        // the UPPER arm with a grip "0.6 m straight down from the shoulder"
        // and its haft squared to the BODY's up, all frozen at the frame it
        // was built. On Astra's deckhand the arm hangs out at ~33 degrees
        // and the clips bend the elbow, so that grip sat a hand's width
        // inside the real fist, and a haft pointing "up" at build time is a
        // haft lying ALONG the arm -- raise the arm and the hammer swings
        // with its head beside the shoulder, lower it and the hammer sticks
        // out sideways past the anvil. The arm angles were also plain
        // numbers, so nothing tied the bottom of the stroke to the work.
        //
        // Now: the grip is read off the live hand bone; the tool is placed
        // in WORLD space from the body's axes every frame (no bone roll, no
        // bone scale); and the swing is a two-link reach -- arm (shoulder
        // to fist, measured) plus tool (fist to striking face) -- solved in
        // the vertical plane through the shoulder and the work, so at the
        // bottom of every stroke the hammer face, the axe edge, the hoe
        // blade is ON the work point.

        /// The arm that holds the tool: the one on the body's +X, which is
        /// the right hand (the body faces +Z). On the deckhand that is the
        /// bone NAMED `upper_arm.L` -- names are no promise about sides.
        Transform ToolArm => side > 0f ? (armL != null ? armL : armR) : (armR != null ? armR : armL);
        Transform OffArm { get { var t = ToolArm; return t == armL ? armR : armL; } }

        /// Body-local work spot for each tool, metres: where a worker at his
        /// stand, facing his bench, has the thing he is working on.
        static Vector3 NominalWork(Mode m)
        {
            switch (m)
            {
                case Mode.Hammer:
                case Mode.Mine:   return new Vector3(0.06f, 0.88f, 0.55f); // anvil face
                case Mode.Chop:   return new Vector3(0.05f, 0.40f, 0.72f); // log on the block
                case Mode.Saw:    return new Vector3(0.08f, 0.74f, 0.55f); // top of the log on the horse
                case Mode.Hoe:    return new Vector3(0.05f, 0.02f, 0.95f); // the ground
                case Mode.Stir:   return new Vector3(0.05f, 0.55f, 0.50f); // the middle of the pot
            }
            return new Vector3(0f, 0.9f, 0.5f);
        }

        Vector3 WorkPoint(Mode m)
        {
            if (hasWork && (workAt - transform.position).sqrMagnitude
                    <= MaxWorkReach * MaxWorkReach * BodyScale * BodyScale)
                return workAt;
            return transform.TransformPoint(NominalWork(m));
        }

        /// Point `arm`'s shoulder-to-fist line at `target`, blended by `w`.
        /// Only the direction is set: the arm's length (and the clip's elbow
        /// bend) is whatever the rig has.
        void Aim(Transform arm, Vector3 target, float w)
        {
            if (arm == null || w <= 0f) return;
            Vector3 s = arm.position;
            Vector3 have = GripOf(arm) - s, want = target - s;
            if (have.sqrMagnitude < 1e-10f || want.sqrMagnitude < 1e-10f) return;
            Quaternion before = arm.localRotation;
            Quaternion to = Quaternion.FromToRotation(have, want) * arm.rotation;
            arm.rotation = Quaternion.Slerp(arm.rotation, to, w);
            Record(arm, before);
        }

        /// A direction in a swing plane, `deg` degrees from straight down
        /// toward `fwd`: 0 hanging, 90 out in front, 180 overhead.
        static Vector3 Dir(float deg, Vector3 down, Vector3 fwd)
        {
            float r = deg * Mathf.Deg2Rad;
            return Mathf.Cos(r) * down + Mathf.Sin(r) * fwd;
        }

        /// **A strike that lands on `work`.** Arm (length R, measured) and
        /// tool (grip to striking face: `reach` up the haft, `face` out to
        /// the strike side) are two links in the vertical plane through the
        /// shoulder and the work. The wrist angle `alpha` (tool against arm)
        /// is solved so the face reaches the work at `k = 0`, clamped to what
        /// a wrist does; `k` then lifts the arm `swingDeg` back up and cocks
        /// the wrist `cockDeg`. Returns the tool's haft axis `t` and strike
        /// side `n` from the arm as it ACTUALLY ended up, so a half-faded
        /// pose still has the tool in the fist.
        void Swing(Transform arm, Vector3 work, float k, float swingDeg, float cockDeg,
            float reach, float face, float minAlpha, float maxAlpha, float w,
            out Vector3 t, out Vector3 n)
        {
            float bs = BodyScale;
            Vector3 up = transform.up, down = -up;
            Vector3 sh = arm.position;
            Vector3 fwd = Vector3.ProjectOnPlane(work - sh, up);
            if (fwd.sqrMagnitude < 1e-6f * bs * bs) fwd = Vector3.ProjectOnPlane(transform.forward, up);
            fwd.Normalize();

            float r = Mathf.Max(0.05f * bs, (GripOf(arm) - sh).magnitude);
            float l = reach * bs, f = face * bs;
            float l2 = Mathf.Sqrt(l * l + f * f);
            float gamma = Mathf.Atan2(f, l) * Mathf.Rad2Deg;

            Vector3 d = work - sh;
            float dd = Vector3.Dot(d, down), dz = Vector3.Dot(d, fwd);
            float dist2 = dd * dd + dz * dz;
            float cosA = (dist2 - r * r - l2 * l2) / (2f * r * l2);
            float alpha = Mathf.Acos(Mathf.Clamp(cosA, -1f, 1f)) * Mathf.Rad2Deg + gamma;
            alpha = Mathf.Clamp(alpha, minAlpha, maxAlpha);
            float a2 = (alpha - gamma) * Mathf.Deg2Rad;
            float beta = Mathf.Atan2(l2 * Mathf.Sin(a2), r + l2 * Mathf.Cos(a2)) * Mathf.Rad2Deg;
            float theta0 = Mathf.Atan2(dz, dd) * Mathf.Rad2Deg - beta;
            // Never further back than just past overhead.
            float lift = Mathf.Max(0f, Mathf.Min(swingDeg, 185f - theta0));
            Aim(arm, sh + Dir(theta0 + lift * k, down, fwd) * r, w);

            Vector3 c = GripOf(arm) - sh;
            float thetaNow = Mathf.Atan2(Vector3.Dot(c, fwd), Vector3.Dot(c, down)) * Mathf.Rad2Deg;
            float psi = thetaNow + alpha + cockDeg * k;
            t = Dir(psi, down, fwd);
            n = Dir(psi - 90f, down, fwd);
        }

        /// Aim both arms for the shown tool and put the tool in the fist.
        void PoseTool(Mode m, float stroke, float w)
        {
            Transform arm = ToolArm, off = OffArm;
            GameObject tool = tools[(int)m];
            if (arm == null) { if (tool != null) tool.SetActive(false); return; }

            float bs = BodyScale;
            Vector3 up = transform.up, down = -up;
            Vector3 right = transform.right;   // toward the tool hand
            Vector3 work = WorkPoint(m);
            Vector3 t, n;
            float offUpHaft = -1f;             // >= 0: off hand on the haft, that far up it
            Vector3 offAt = work;

            switch (m)
            {
                case Mode.Hammer:
                case Mode.Mine:
                    // Short wrist-and-elbow blow from above the shoulder,
                    // face flat onto the anvil; the other hand holds the work
                    // on the anvil beside it.
                    Swing(arm, work, stroke, 100f, 25f, Hammer_Reach, Hammer_Face, 60f, 125f, w, out t, out n);
                    offAt = work - right * (0.20f * bs) + up * (0.04f * bs);
                    break;

                case Mode.Chop:
                    // Overhead to the log, arms and haft nearly in line at
                    // the blow; both hands at the butt.
                    Swing(arm, work, stroke, 140f, 35f, Axe_Reach, Axe_Face, 20f, 120f, w, out t, out n);
                    offUpHaft = 0.11f;
                    break;

                case Mode.Hoe:
                    // Up to the chest and down into the ground well out in
                    // front, the front hand a third of the way down the haft.
                    Swing(arm, work, stroke, 70f, 12f, Hoe_Reach, Hoe_Face, 15f, 110f, w, out t, out n);
                    offUpHaft = 0.42f;
                    break;

                case Mode.Saw:
                {
                    // The blade runs along the cut, tipped 22 degrees nose
                    // down, teeth on the log; the stroke slides it along
                    // itself. The other hand holds the log beside the cut.
                    Vector3 sh = arm.position;
                    Vector3 fwd = Vector3.ProjectOnPlane(work - sh, up);
                    if (fwd.sqrMagnitude < 1e-6f * bs * bs) fwd = Vector3.ProjectOnPlane(transform.forward, up);
                    fwd.Normalize();
                    t = Dir(68f, down, fwd);
                    n = Dir(-22f, down, fwd);
                    Vector3 cut = work + fwd * (0.13f * bs * stroke);
                    Aim(arm, cut - t * (Saw_Reach * bs) - n * (Saw_Face * bs), w);
                    offAt = work - right * (0.26f * bs) + up * (0.03f * bs);
                    break;
                }

                case Mode.Stir:
                {
                    // Both hands on a paddle, the blade circling in the pot
                    // and the hands circling above it, smaller.
                    Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, up).normalized;
                    Vector3 side2 = Vector3.ProjectOnPlane(right, up).normalized;
                    Vector3 ring = fwd * Mathf.Cos(stroke) + side2 * Mathf.Sin(stroke);
                    Vector3 blade = work + ring * (0.09f * bs);
                    Aim(arm, work + up * (0.50f * bs) - fwd * (0.10f * bs) + ring * (0.05f * bs), w);
                    Vector3 toBlade = blade - GripOf(arm);
                    t = toBlade.sqrMagnitude > 1e-8f ? toBlade.normalized : down;
                    n = Vector3.ProjectOnPlane(fwd, t);
                    if (n.sqrMagnitude < 1e-6f) n = Vector3.ProjectOnPlane(up, t);
                    n.Normalize();
                    offUpHaft = 0.22f;
                    break;
                }

                default:
                    return;
            }

            Vector3 grip = GripOf(arm);
            if (offUpHaft >= 0f) offAt = grip + t * (offUpHaft * bs);
            Aim(off, offAt, w);

            if (tool != null)
                tool.transform.SetPositionAndRotation(grip, Quaternion.LookRotation(n, t));
        }

        /// **A bone the Animator does not key keeps last frame's bend.** The
        /// old kit's clips keyed every bone every frame, so bending "over
        /// what the Animator just wrote" was safe; Astra's deckhand clips key
        /// only the limbs, and a bend on top of last frame's bend folded the
        /// spine and hips over within a second. So: any bone still exactly as
        /// we left it was not rewritten -- put it back the way we found it
        /// before this frame bends it again. Bones the Animator does write are
        /// untouched by this.
        struct Written { public Quaternion before, after; }
        readonly Dictionary<Transform, Written> written = new Dictionary<Transform, Written>();

        void UndoUnkeyedBends()
        {
            foreach (var kv in written)
                if (kv.Key != null && kv.Key.localRotation == kv.Value.after)
                    kv.Key.localRotation = kv.Value.before;
            written.Clear();
            if (hipsWritten && hips != null && hips.localPosition == hipsAfterPos)
                hips.localPosition = hipsBeforePos;
            hipsWritten = false;
        }

        bool hipsWritten;
        Vector3 hipsBeforePos, hipsAfterPos;

        // --- props ----------------------------------------------------------

        static readonly Dictionary<string, Material> propMats
            = new Dictionary<string, Material>();

        /// Shared across every villager in the world. One material per colour,
        /// never one per body — `CampPiles` learnt the same lesson about the
        /// stacks beside the fire.
        static Material Mat(string key, Color c)
        {
            if (propMats.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(Shader.Find(
                WorldArtStyle.Instance != null
                    ? "SeaSick/Environment Toon"
                    : "Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.05f);
            propMats[key] = m;
            return m;
        }

        void RefreshProps()
        {
            for (int i = 0; i < tools.Length; i++)
                if (tools[i] != null) tools[i].SetActive(false);
            if (carryProp != null) carryProp.SetActive(false);
            if (dishProp != null) dishProp.SetActive(false);

            propCaptured = false;
            if (shown == Mode.Reach || shown == Mode.Eat)
            {
                EnsureDish();     // shown from the grab on (`PlaceDish`)
                return;
            }
            if (shown == Mode.Carry || (shown == Mode.PickUp || shown == Mode.SetDown) && UsesClip(shown) && !string.IsNullOrEmpty(shownLoad))
            {
                EnsureCarry();
                if (carryProp != null) carryProp.SetActive(true);
                return;
            }

            int m = (int)shown;
            if (m <= 0 || m >= tools.Length) return;
            if (!HasTool(shown)) return;
            if (tools[m] == null) tools[m] = BuildTool(shown);
            if (tools[m] != null) tools[m].SetActive(true);
        }

        static bool HasTool(Mode m) =>
            m == Mode.Chop || m == Mode.Saw || m == Mode.Hammer
            || m == Mode.Hoe || m == Mode.Stir || m == Mode.Mine
            // v15 clips holding a tool the game has (README: Build/Smith
            // hammer, Quarry's mallet -> the hammer as its stand-in, Farm
            // hoe, Cook paddle). Knife, peg, basket, chisel: none yet.
            || m == Mode.Build || m == Mode.Smith || m == Mode.Quarry
            || m == Mode.Farm || m == Mode.Cook;

        /// Highest visible count in a bundle. **Kevin, 2026-09-23:** *"if
        /// they carry 3 logs, you see three logs"* — but a haul can be a
        /// dozen units, and a dozen cubes on one body is polygons and draw
        /// calls nobody asked for. The real number stays the ledger's; this
        /// is only ever the picture of "several."
        const int MaxVisibleCarry = 6;

        void EnsureCarry()
        {
            // Held out on both arms (the `Carry` clip) or shouldered (the
            // code pose): different stacks, so a different key.
            bool held = UsesClip(Mode.Carry);
            string what = string.IsNullOrEmpty(shownLoad) ? Res.Timber : shownLoad;
            int n = held ? CarryLook.Shown(what, shownLoadCount) : Mathf.Clamp(shownLoadCount, 1, MaxVisibleCarry);
            string key = what + "x" + n + (held ? "h" : "");
            if (carryProp != null && carryPropFor == key) return;
            if (carryProp != null) Kill(carryProp);
            carryPropFor = key;

            var root = new GameObject("Carry_" + what + "_" + n);
            root.transform.SetParent(transform, false);
            root.transform.localPosition = new Vector3(0.16f * side, 1.42f, 0.10f);
            root.transform.localRotation = Quaternion.Euler(0f, 6f * side, 0f);
            carryProp = root;

            // **Held (2026-10-01): the carry table.** The root is the clip's
            // carry socket (`PlaceClipProps` puts it on the spine every
            // frame); `CarryLook` lays the load out at real size against
            // the clip's arms -- logs and planks across the forearms, a
            // stack forward of the chest, small goods in the open crate.
            if (held)
            {
                CarryLook.Build(what, shownLoadCount, root.transform);
                return;
            }

            // The code pose (a rig without the clip): the old shoulder loads.
            if (what == Res.Timber)
                BuildLogs(root.transform, n);
            else if (what == Res.Boards || what == Res.FineBoards)
                BuildPlanks(root.transform, n, what == Res.FineBoards);
            else if (what == Res.Stone)
                BuildStones(root.transform, n);
            else if (what == Res.Brick)
                BuildBricks(root.transform, n);
            else
                BuildSacks(root.transform, n, what);
        }

        /// Round logs, shouldered side by side with just enough stagger
        /// that three of them read as three and not as one thick trunk.
        static void BuildLogs(Transform root, int n)
        {
            var mat = Mat("log", Res.Colour(Res.Timber));
            const float spacing = 0.15f;
            for (int i = 0; i < n; i++)
            {
                float x = (i - (n - 1) * 0.5f) * spacing;
                float y = (i % 2 == 0) ? 0f : 0.05f;
                float z = ((i % 3) - 1) * 0.02f;
                // Astra resource kit v1, Kevin approved 2026-09-30: the real
                // carried log (`Timber_CarryUnit`, 1.16 m, centre grip, long
                // axis z), at the same spacing, stagger and lean the cylinder
                // had (the lean is a yaw of the log about the shoulder). The
                // cylinder below is the fallback.
                if (ResourceKit.Spawn(Res.Timber, true, root, new Vector3(x, y, z),
                        Quaternion.Euler(0f, (i % 2 == 0 ? -1f : 1f) * (3f + i), 0f)) != null)
                    continue;
                var log = Prim(PrimitiveType.Cylinder, root,
                    new Vector3(0.16f, 0.58f, 0.16f), mat);
                log.transform.localPosition = new Vector3(x, y, z);
                log.transform.localRotation =
                    Quaternion.Euler(90f, 0f, (i % 2 == 0 ? 1f : -1f) * (3f + i));
            }
        }

        /// A flat bundle of milled planks, stacked one on the next rather
        /// than side by side — the far side of a sawyer, not a tree.
        static void BuildPlanks(Transform root, int n, bool fine)
        {
            var mat = Mat(fine ? "planks_fine" : "planks", Res.Colour(Res.Boards));
            for (int i = 0; i < n; i++)
            {
                // Astra resource kit v1, Kevin approved 2026-09-30: the real
                // carried board (`Boards_CarryUnit`, 0.46 x 0.13 x 0.035 m,
                // centre grip, carried across the arms), stacked exactly as
                // the cube was. Fine boards wear the same mesh. The cube
                // below is the fallback.
                if (ResourceKit.Spawn(Res.Boards, true, root,
                        new Vector3(0.015f * (i % 2 == 0 ? 1 : -1), 0.045f * i, 0f),
                        Quaternion.Euler(0f, (i % 2 == 0 ? 2f : -2f), 0f)) != null)
                    continue;
                var plank = Prim(PrimitiveType.Cube, root,
                    new Vector3(0.46f, 0.035f, 0.13f), mat);
                plank.transform.localPosition =
                    new Vector3(0.015f * (i % 2 == 0 ? 1 : -1), 0.045f * i, 0f);
                plank.transform.localRotation =
                    Quaternion.Euler(0f, (i % 2 == 0 ? 2f : -2f), 0f);
            }
        }

        /// Rough stone, piled in the arms rather than on the shoulder — a
        /// quarryman carries it in front of his chest, not slung.
        static void BuildStones(Transform root, int n)
        {
            var mat = Mat("stones", Res.Colour(Res.Stone));
            for (int i = 0; i < n; i++)
            {
                int row = i / 2, col = i % 2;
                var at = new Vector3((col - 0.5f) * 0.17f, row * 0.16f, -0.02f * row);
                var tilt = Quaternion.Euler(
                    (i % 2 == 0 ? 8f : -6f), 10f * (i % 3 - 1), (i % 2 == 0 ? -5f : 7f));
                // Astra resource kit v1, Kevin approved 2026-09-30: the real
                // carried stone (`Stone_CarryUnit`, about 0.2 m, centre
                // grip), in the same places and tilts. The cube below is the
                // fallback.
                if (ResourceKit.Spawn(Res.Stone, true, root, at, tilt) != null) continue;
                var stone = Prim(PrimitiveType.Cube, root, Vector3.one * 0.19f, mat);
                stone.transform.localPosition = at;
                stone.transform.localRotation = tilt;
            }
        }

        /// Squared bricks, small and stacked true — the far side of a
        /// quarryman, not the rough stone that went in.
        static void BuildBricks(Transform root, int n)
        {
            var mat = Mat("bricks", Res.Colour(Res.Brick));
            for (int i = 0; i < n; i++)
            {
                var at = new Vector3(0f, 0.09f * i, 0.01f * i);
                var yaw = Quaternion.Euler(0f, (i % 2) * 4f, 0f);
                // Astra resource kit v1, Kevin approved 2026-09-30: the real
                // carried brick (`Brick_CarryUnit`, 0.17 x 0.085 x 0.10 m,
                // centre grip), stacked true as the cube was. The cube below
                // is the fallback.
                if (ResourceKit.Spawn(Res.Brick, true, root, at, yaw) != null) continue;
                var brick = Prim(PrimitiveType.Cube, root,
                    new Vector3(0.17f, 0.085f, 0.10f), mat);
                brick.transform.localPosition = at;
                brick.transform.localRotation = yaw;
            }
        }

        /// Everything else: a sack per unit, the way one sack always looked.
        static void BuildSacks(Transform root, int n, string what)
        {
            var mat = Mat("sack_" + what, Res.Colour(what));
            float scale = n <= 1 ? 1f : 0.82f;

            // Astra food kit v1 (2026-09-30): a food that has a model
            // (`Resources/Kits/Food/<Name>_Unit`, base origin) is carried as
            // itself, two to a row in the sack layout's rows; dishes, flour,
            // ore and the rest keep the sack. Ore stays in sacks on purpose.
            Vector3 foodSize = ResourceKit.FoodSize(what);
            if (foodSize.sqrMagnitude > 0f)
            {
                float k = n <= 1 ? 0.8f : 0.65f;
                float cellX = Mathf.Clamp(Mathf.Max(foodSize.x, foodSize.z) * k * 1.05f, 0.14f, 0.32f);
                float cellY = Mathf.Clamp(foodSize.y * k * 0.9f, 0.08f, 0.2f);
                bool all = true;
                for (int i = 0; i < n; i++)
                {
                    int row = i / 2, col = i % 2;
                    var at = new Vector3((col - 0.5f) * cellX, -0.05f + row * cellY, 0f);
                    var yaw = Quaternion.Euler(0f, 18f + i * 6f, 0f);
                    if (ResourceKit.SpawnFood(what, root, at, yaw, k) == null) { all = false; break; }
                }
                if (all) return;
                // A model that would not load: clear the partial load and
                // fall through to the sacks, so nothing is ever invisible.
                for (int c = root.childCount - 1; c >= 0; c--) Destroy(root.GetChild(c).gameObject);
            }

            for (int i = 0; i < n; i++)
            {
                var sack = Prim(PrimitiveType.Cube, root,
                    new Vector3(0.34f, 0.30f, 0.30f) * scale, mat);
                int row = i / 2, col = i % 2;
                sack.transform.localPosition = new Vector3(
                    (col - 0.5f) * 0.22f * scale, 0.10f + row * 0.20f * scale, 0f);
                sack.transform.localRotation = Quaternion.Euler(0f, 18f + i * 6f, 9f - i * 3f);
            }
        }

        /// A tool in the hand. **Astra's worker tools v1, Kevin approved
        /// 2026-09-30:** each tool is her single mesh (`ToolKit`, authored in
        /// the tool frame below at true metres, so it is hung at identity and
        /// never scaled -- the `Hammer_Reach` ... `Saw_Face` numbers ARE the
        /// mesh's grip-to-face distances). The boxes below are the FALLBACK,
        /// built only if a mesh fails to load: primitive on purpose, since at
        /// the zoom a camp is read from a haft and a head is a recognisable
        /// axe.
        ///
        /// **The tool frame** (what `PoseTool` places): the origin is the
        /// middle of the fist, +Y runs up the haft to the head, +Z is the
        /// side that does the work (hammer face, axe edge, hoe blade, saw
        /// teeth), X is the axis the swing turns about. Hung off the BODY at
        /// unit scale, so a tool is sized in body metres whatever the bones'
        /// scale (Astra's deckhand arm reads ~92x).
        GameObject BuildTool(Mode m)
        {
            if (ToolArm == null) return null;

            var root = new GameObject("Tool_" + m);
            root.transform.SetParent(transform, false);
            root.transform.localScale = Vector3.one;
            var tr = root.transform;

            // No clip for `Mine` on this rig: the hammer, as before.
            if (m == Mode.Mine && !UsesClip(Mode.Mine)) m = Mode.Hammer;
            if (m == Mode.Build || m == Mode.Smith || m == Mode.Quarry) m = Mode.Hammer;
            else if (m == Mode.Farm) m = Mode.Hoe;
            else if (m == Mode.Cook) m = Mode.Stir;
            string kit = m == Mode.Hammer ? ToolKit.Hammer : m == Mode.Chop ? ToolKit.Axe
                : m == Mode.Saw ? ToolKit.Saw : m == Mode.Hoe ? ToolKit.Hoe
                : m == Mode.Stir ? ToolKit.StirPaddle
                : m == Mode.Mine ? ToolKit.Pickaxe : null;
            if (kit != null && ToolKit.Attach(kit, tr, true))
            {
                // The `Saw` clip holds the saw like a real handsaw, blade
                // running on out of the fist along the forearm: the mesh
                // (blade up +Y) turns +90 degrees about X (blade to +Z,
                // teeth to -Y), as authored.
                if (m == Mode.Saw && UsesClip(Mode.Saw))
                    tr.GetChild(0).localRotation = Quaternion.Euler(90f, 0f, 0f);
                return root;
            }

            var wood = Mat("tool_haft", new Color(0.44f, 0.31f, 0.19f));
            var iron = Mat("tool_iron", new Color(0.42f, 0.44f, 0.48f));

            switch (m)
            {
                case Mode.Hammer:
                {
                    // 34 cm haft, 4 cm of butt below the fist; the head across
                    // the top, face 10 cm out on +Z, a short peen behind.
                    Box(tr, wood, new Vector3(0.035f, 0.34f, 0.035f), new Vector3(0f, 0.13f, 0f));
                    Box(tr, iron, new Vector3(0.055f, 0.06f, 0.16f), new Vector3(0f, Hammer_Reach, 0.02f));
                    break;
                }
                case Mode.Mine:
                {
                    // Fallback if the pickaxe mesh is missing: 66 cm haft, a
                    // 46 cm head across its top, point 23 cm out on +Z.
                    Box(tr, wood, new Vector3(0.04f, 0.66f, 0.04f), new Vector3(0f, 0.28f, 0f));
                    Box(tr, iron, new Vector3(0.05f, 0.05f, 0.46f), new Vector3(0f, 0.58f, 0f));
                    break;
                }
                case Mode.Chop:
                {
                    // 67 cm haft; the blade a thin cheek in the swing plane,
                    // edge 13 cm out on +Z, a stubby poll behind.
                    Box(tr, wood, new Vector3(0.04f, 0.67f, 0.04f), new Vector3(0f, 0.285f, 0f));
                    Box(tr, iron, new Vector3(0.03f, 0.14f, 0.12f), new Vector3(0f, Axe_Reach, 0.07f));
                    Box(tr, iron, new Vector3(0.05f, 0.08f, 0.06f), new Vector3(0f, Axe_Reach, -0.02f));
                    break;
                }
                case Mode.Saw:
                {
                    // A 50 cm handsaw blade running up +Y from the grip,
                    // teeth on the +Z edge (a dark strip), the handle behind.
                    Box(tr, iron, new Vector3(0.008f, 0.50f, 0.11f), new Vector3(0f, Saw_Reach, 0f));
                    Box(tr, Mat("tool_teeth", new Color(0.22f, 0.23f, 0.25f)),
                        new Vector3(0.012f, 0.50f, 0.012f), new Vector3(0f, Saw_Reach, Saw_Face));
                    Box(tr, wood, new Vector3(0.035f, 0.13f, 0.10f), new Vector3(0f, 0.02f, -0.015f));
                    break;
                }
                case Mode.Hoe:
                {
                    // 1.13 m haft, 8 cm of butt below the rear fist; the blade
                    // a wide plate hanging from the top toward +Z.
                    Box(tr, wood, new Vector3(0.038f, 1.13f, 0.038f), new Vector3(0f, 0.485f, 0f));
                    Box(tr, iron, new Vector3(0.17f, 0.025f, 0.15f), new Vector3(0f, Hoe_Reach, 0.07f));
                    break;
                }
                case Mode.Stir:
                {
                    // A long paddle, grip near the top, blade in the pot.
                    Box(tr, wood, new Vector3(0.03f, 0.66f, 0.03f), new Vector3(0f, 0.27f, 0f));
                    Box(tr, wood, new Vector3(0.09f, 0.17f, 0.02f), new Vector3(0f, 0.66f, 0f));
                    break;
                }
            }
            return root;
        }

        static GameObject Box(Transform parent, Material mat, Vector3 size, Vector3 at)
        {
            var go = Prim(PrimitiveType.Cube, parent, size, mat);
            go.transform.localPosition = at;
            return go;
        }

        static GameObject Prim(PrimitiveType type, Transform parent, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);   // the edit-mode tool shot
            }
            go.transform.SetParent(parent, false);
            go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            if (r != null) r.sharedMaterial = mat;
            return go;
        }
    }
}
