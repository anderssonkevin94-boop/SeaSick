using UnityEngine;
using UnityEngine.InputSystem;
using ET = UnityEngine.InputSystem.EnhancedTouch;

namespace SeaSick.CameraRig
{
    /// Portrait chase camera: follows the ship's yaw only (never inherits
    /// pitch/roll — the horizon stays stable while the ship rocks), looking
    /// past the ship toward the horizon. No manual control by design.
    public class ChaseCamera : MonoBehaviour
    {
        /// A 1080x2340 phone held upright — what the game actually ships on,
        /// and the shape every framing decision has to be judged in.
        ///
        /// It lives HERE, once, because it was written down three times and
        /// two of them disagreed: `Dock.ViewHalfWidth`'s note says 900x1500
        /// (0.600) while FramingProbe and TerrainPerfProbe both use 1080x2340
        /// (0.462). Composing the docked shot against the wider of the two put
        /// the ship at viewport 0.89 on the desk and 1.01 -- off the screen --
        /// on the phone. A constant that is copied is a constant that drifts.
        public const float PortraitAspect = 1080f / 2340f;

        /// The desk. Landscape became the shape the game runs in on
        /// 2026-09-11 ("computer mode"), and the phone stayed supported rather
        /// than being retired — so there are now two composed aspects and the
        /// rig picks by what the window actually is, instead of always
        /// composing for the narrower one.
        public const float LandscapeAspect = 1920f / 1080f;

        [SerializeField] Transform target;
        // Three-quarter view: tilt = (height - lookHeight) / (distance +
        // lookAhead). Vertical FOV is 60, so the frame spans tilt +/- 30
        // degrees — the horizon is only visible while tilt stays under 30.
        // These give ~22 degrees, which keeps a band of sky at the top while
        // still looking down onto the deck.
        // Kevin picked B2 off the ten-angle sheet (SailShots): 26 m back,
        // 8 m up, 15 degrees above her, 58 mm — low and astern, the whole
        // hull in the frame with sea around it, sailing on a desktop screen
        // where the sides have room. He chose it over the higher angles that
        // read the DECK better, having seen both, so the deck-legibility
        // argument is settled and not to be re-litigated.
        //
        // These are PRE-SCALE: the rig multiplies them by frameK, which was
        // 1.25 on the steamer when the sheet was shot. 26 / 1.25 = 20.8.
        [SerializeField] float distance = 20.8f;
        [SerializeField] float height = 6.4f;      // 8 m up, / 1.25
        [SerializeField] float lookAhead = 14.4f;  // 18 m ahead, / 1.25
        [SerializeField] float lookHeight = 0.5f;
        [SerializeField] float positionResponse = 2.2f;
        [SerializeField] float rotationResponse = 3f;

        // Framing follows the hull.
        //
        // Every number above was authored on the paddle steamer, 24.2 m
        // overall, and then the ladder arrived and the same rig had to hold a
        // 9 m skiff and a 46 m three-decker. Held fixed, the first-rate fills
        // most of the frame and you steer a wall of hull with no sea around
        // it, while the skiff is a speck.
        //
        // The fix is similar triangles and nothing cleverer: scale the seat
        // and the height by the SAME factor and the tilt is unchanged, so the
        // composition is identical on every rung and the ship covers the same
        // fraction of the screen. That is the whole idea — it is a camera
        // distance, not a look-and-feel constant, and a camera distance is
        // decided by what has to fit in the frame.
        //
        // What is NOT scaled: anything measured against the SEA. The storm
        // drop, the minimum height above water and the terrain clearance are
        // metres of water and rock, and water does not get bigger because the
        // ship did.
        [Header("Hull framing")]
        [Tooltip("The hull the framing above was authored on. Not a hull she has to be; the hull she is measured against.")]
        [SerializeField] float framingLoa = SeaSick.Ship.ShipMotor.TunedLoa;
        [Tooltip("1 = the ship covers the same slice of screen on every rung. Lower lets a big ship read big: 0.85 gives the three-decker about 10% more of the frame than the steamer, 0 is the old fixed camera.")]
        [Range(0f, 1f)] [SerializeField] float framingPower = 1f;
        [Tooltip("How fast the framing eases to a new hull. The yard swaps her in one frame; the camera should not.")]
        [SerializeField] float framingResponse = 1.6f;

        // How much room she gets around her, on top of the scaling.
        //
        // Scaling alone equalised the rungs at the framing the brig already
        // had — and MEASURED on a portrait frame that framing put her at 87%
        // of the screen's height and 115% of its width. She was wider than
        // the phone. The Game view is landscape and the game is not, so this
        // had been invisible: the same lens is a third as wide held upright.
        //
        // 1.5 was chosen off that measurement and re-measured, not guessed:
        // it puts her at about 58% of the height and 77% of the width, which
        // leaves sea on both sides and sky above her on every rung.
        [Tooltip("Room around the ship, on top of the per-hull scaling. 1 is the old framing, which measured wider than a portrait screen. Turn it down to sit closer.")]
        [SerializeField] float framingMargin = 1.25f;

        // FOV motion is the main cause of simulator sickness in a chase cam.
        // Keep the total swing small (a few degrees) and ease it slowly.
        [SerializeField] float fovBase = 58f;      // the sheet's lens
        [SerializeField] float fovSpeedBoost = 4f;
        [Tooltip("Extra FOV kick while surfing down a wave face. Keep tiny.")]
        [SerializeField] float fovSurfPunch = 1.5f;
        [SerializeField] float fovResponse = 1.1f;
        [Tooltip("Never let the lens dip under the water surface.")]
        [SerializeField] float minHeightAboveWater = 2.6f;
        [Tooltip("Keeps the camera above island terrain instead of inside it.")]
        [SerializeField] float terrainClearance = 8f;

        // Cruise framing.
        //
        // Settle onto a heading and hold it, and the view eases back and lifts
        // a little — you are travelling, so you get to see more of where you
        // are going. Deliberately slow (about three seconds) so it never reads
        // as a zoom; you should notice the sea got bigger, not the camera
        // moving. Drops away the moment you slow, turn hard or lock a target.
        [Header("Cruise framing")]
        [SerializeField] float cruiseDistance = 10f;
        [SerializeField] float cruiseHeight = 4.5f;
        [SerializeField] float cruiseLookAhead = 5f;    // look further ahead, not less
        [Range(0f, 1f)] [SerializeField] float cruiseSpeed01 = 0.5f;
        [SerializeField] float cruiseDelay = 6f;        // seconds of making way first
        [SerializeField] float cruiseInRate = 0.35f;
        [SerializeField] float cruiseOutRate = 1.4f;

        // Portrait framing.
        //
        // The desk shot (B2 off the sheet: low and astern, 15 degrees above
        // her) is a LANDSCAPE composition — it spends the frame's width on
        // sea either side of the hull. Held upright the width is gone and
        // that same seat gives a strip of sea, a hull filling the middle and
        // no read on where she is going.
        //
        // So the phone gets its own preset, decided 2026-09-22: further
        // back, much higher, looking less far ahead — about 21 degrees of
        // pitch through a 68 degree lens, which puts the horizon a fifth of
        // the way down from the top and the hull in the lower third, with
        // the sea she is crossing filling the tall middle of the frame.
        //
        // These are PRE-SCALE like the landscape set: they are multiplied by
        // frameK, and because the tilt is a RATIO of them the composition is
        // identical on every rung of the ladder.
        //
        // Nothing here has a "mode". `HudLayout.Wide` asks the window what
        // shape it is and the HUD lays itself out on the answer; the camera
        // asks the same question so the two can never disagree about what
        // portrait means, and the blend below means rotating the device is a
        // half-second move rather than a cut.
        [Header("Portrait framing")]
        [Tooltip("Metres astern, upright. Pre-scale, like `distance`.")]
        [SerializeField] float portraitDistance = 24f;
        [Tooltip("Metres up, upright. Pre-scale, like `height`.")]
        [SerializeField] float portraitHeight = 12f;
        [Tooltip("Metres ahead of her the look point sits, upright. Shorter than the desk's, because a tall frame already shows the sea ahead.")]
        [SerializeField] float portraitLookAhead = 6f;
        [Tooltip("Vertical FOV upright. A tall frame needs a taller lens; landscape keeps `fovBase`.")]
        [SerializeField] float portraitFov = 68f;
        [Tooltip("How fast the rig crosses between the two presets. ~2 is half a second, which reads as a move and not a cut when the device turns.")]
        [SerializeField] float portraitBlendRate = 2f;

        // Speed reads as a DOLLY upright, not as a lens.
        //
        // Widening the lens with speed is the classic trick and it is also
        // the classic cause of simulator sickness — and it is worse on a
        // phone, held close, than on a desk. Backing the seat off instead
        // gives the same "she is really going" without touching the
        // projection: distance and height scale TOGETHER, so the tilt is
        // unchanged and the horizon does not move while she accelerates.
        //
        // It keys off the ORDER, not the speed: the telegraph is what the
        // player just did, and a camera that leads the ship out feels like
        // it is answering you. Keyed to the speed it would lag the engine
        // ramp by the several seconds the stokers take.
        [Tooltip("How much further back she sits at the burn notch, upright. 0.30 = 30% further back and 30% higher.")]
        [Range(0f, 1f)] [SerializeField] float portraitZoomOut = 0.30f;
        [Tooltip("Where the seat eases to when she is stopped or anchored. Under 1 = closer in, so lying still is an intimate shot.")]
        [SerializeField] float portraitStoppedPull = 0.85f;
        [Tooltip("How fast the seat pulls IN. Slow on purpose (~3 s): coming closer should be something you notice having happened.")]
        [SerializeField] float portraitPullInRate = 0.33f;

        // The player's own zoom was removed 2026-10-03 (DREDGE controls step
        // 2, Kevin): DREDGE sits at a fixed distance, and a sea pinch would
        // collide with two-thumb play. The sea camera's player input is now
        // the look offset (`SeaCameraInput`, see `UpdateLook`). The island
        // view keeps its own pinch (`IslandInput`).

        // Lock framing.
        //
        // Frames you and the target together. The camera swings toward sitting
        // opposite the enemy, but the swing is CLAMPED off dead-astern. The
        // clamp used to protect the helm (a free orbit inverted it when a
        // target crossed your stern); since DREDGE step 1 the stick is
        // boat-relative and never reads the camera, so the cap is a framing
        // choice only: she stays the readable subject of the shot.
        // Sea response.
        //
        // The rig used to be pinned to mean sea level — a fixed world height,
        // looking at a fixed world height — while a storm threw the ship 23.5m
        // up and back down through it. The horizon stayed nailed and the BOAT
        // bobbed, which is exactly backwards: it is the sea that should move.
        // That single fact was most of why a mountainous sea read as flat.
        //
        // Low-passing the ship's height fixes it. The rig follows the long
        // heave and lets the chop run past — it rides the swell like a second
        // boat rather than twitching on every wavelet.
        [Header("Sea response")]
        [Tooltip("How quickly the rig takes up the ship's heave. Low = only " +
                 "the long swell; high = every wavelet, which is sickening.")]
        [SerializeField] float heaveFollow = 1.5f;
        [Range(0f, 1f)] [SerializeField] float heaveShare = 1f;
        [Tooltip("Metres the rig drops in a full storm. A low camera is what " +
                 "makes a sea loom; a high one looks down on it and flattens " +
                 "it into a bump.")]
        [SerializeField] float stormDrop = 4f;
        [SerializeField] float stormPullIn = 3f;
        [SerializeField] float stormResponse = 1.2f;

        [Header("Lock framing")]
        [SerializeField] float lockMaxSwingDeg = 78f;
        /// Upright, the frame is narrow: at 68° vertical on a 1080x2340
        /// screen the lens sees only ±17° either side. The 78° clamp holds
        /// both hulls for a target on the beam (checked: ship +3.5°, target
        /// −3.1° at 60 m) but a target on the quarter or astern falls out of
        /// the side of the frame (−34° at 60 m dead astern). Steering is
        /// boat-relative now (DREDGE step 1, `SeaStick`), so no swing can
        /// re-map the stick; this cap is a framing choice only. At 140° the
        /// same astern target sits at ship +11.5° / target −10.6°.
        [SerializeField] float lockMaxSwingDegPortrait = 140f;
        [SerializeField] float lockPullPerMetre = 0.42f;
        [SerializeField] float lockMaxPull = 34f;
        [Range(0f, 0.8f)] [SerializeField] float lockBias = 0.34f;  // look point toward the target
        [SerializeField] float lockResponse = 2.4f;

        public Transform Target { get => target; set => target = value; }

        /// When set (e.g. a shore party), the camera backs off and frames both
        /// the ship and this point so the player can watch the crew work.
        public Vector3? PointOfInterest { get; set; }

        Camera cam;
        SeaSick.Ship.ShipMotor motor;
        Transform motorFor;
        float frameK = 1f;
        bool frameSeeded;

        /// What the framing is currently multiplying the rig by, for the probe.
        public float FramingScale => frameK;

        float cruiseLevel, atSpeedFor;
        /// 0 = desk, 1 = phone held upright. Smoothed, seeded on frame one.
        float portrait01 = -1f;
        /// The speed dolly, upright: multiplies distance and height together.
        float dolly = -1f;
        // The player's look offset (DREDGE step 2). `userYaw`/`userPitch` are
        // what the input built; `lookYaw`/`lookPitch` the drawn ones, sprung
        // after them by `SeaCameraTuning.followSeconds`. Degrees: +yaw looks
        // right, +pitch looks further down on her. They persist until a
        // recenter (DREDGE has no auto-decay; that is what recenter is for).
        readonly SeaCameraInput seaInput = new SeaCameraInput();
        SeaSick.Combat.CombatLock combatLock;
        float userYaw, userPitch, lookYaw, lookPitch, lookYawVel, lookPitchVel;
        bool recentering;
        float recenterT, recenterYaw0, recenterPitch0;
        // Detached follow mode: the world azimuth the base seat holds while
        // she turns under it. In Follow mode it just tracks her stern.
        float detachedAz;
        bool detachedSeeded, wasFollow = true;

        /// What the portrait blend is doing, for the tuner and the probe.
        public float Portrait01 => Mathf.Max(0f, portrait01);
        /// The seat multiplier the dolly is asking for (the player's pinch is
        /// gone, so nothing the player does moves it; `ShipyardUiProbe`
        /// asserts exactly that).
        public float ZoomScale => Mathf.Lerp(1f, Mathf.Max(0f, dolly), Mathf.Max(0f, portrait01));
        /// The look offset actually drawn, degrees, for probes and hints.
        public float LookYawDeg => lookYaw;
        public float LookPitchDeg => lookPitch;

        float lockLevel;
        float heaveY;
        bool heaveSeeded;
        float stormLevel;
        // The framing position WITHOUT heave. Heave is added on top of it
        // rather than folded into the lerp target — see below.
        Vector3 rigPos;
        bool rigSeeded;

        /// The thing to keep in frame, or null for the plain chase view.
        /// Set by CombatLock; cleared when the target dies or breaks away.
        public Transform LockTarget { get; set; }

        /// **A point at sea to frame the way a lock frames its target,
        /// without being a lock** (2026-10-03, the kraken's surfacing): no
        /// guns, no reticle, nothing `CombatLock` knows about -- just the
        /// same seat swing, back-off and midpoint aim. A real `LockTarget`
        /// outranks it; `PointOfInterest` (the shore-party framing, which
        /// puts a target 55 m out under the lens and her off the bottom of
        /// a portrait screen) is not used for this. Null = nothing.
        public Vector3? SeaFocus { get; set; }

        /// **How the SeaFocus shot is composed** (2026-10-03, kraken step 1
        /// fix). Kevin's captures: the lock framing (aim on the midpoint)
        /// put the tallest arms behind the top bar and left the lower half
        /// of the phone empty sea. With `SeaFocus` alone (no `LockTarget`)
        /// the camera now sits on the ship's far side from the focus,
        /// looking over her at it (swing capped at `SeaFocusSwingDeg` off
        /// her bow, a framing choice: the stick is boat-relative since DREDGE
        /// step 1 and never reads the view), and solves the seat
        /// distance and pitch so she sits at `SeaFocusShip01` of the screen
        /// height and the thing's full height (`SeaFocusHeight` metres over
        /// the focus point) and width (`SeaFocusRadius`) fit under
        /// `SeaFocusTop01`. Viewport fractions from the bottom. The owner of
        /// `SeaFocus` writes these every frame; nothing here reads them
        /// while `SeaFocus` is null, so the plain chase is untouched.
        public float SeaFocusHeight { get; set; }
        public float SeaFocusRadius { get; set; }
        public float SeaFocusShip01 { get; set; } = 0.28f;
        public float SeaFocusTop01 { get; set; } = 0.84f;
        /// The seat's elevation seen from the ship, degrees.
        public float SeaFocusElevDeg { get; set; } = 24f;
        public float SeaFocusSwingDeg { get; set; } = 65f;
        public float SeaFocusMaxBack { get; set; } = 140f;
        /// A `LockTarget` that IS the SeaFocus thing (the kraken's head):
        /// locking it keeps the SeaFocus composition instead of the lock
        /// framing. Any other lock still outranks SeaFocus.
        public Transform SeaFocusLockAlias { get; set; }
        /// 0..1, how far into the SeaFocus composition the rig is.
        public float SeaFocusLevel => seaLevel;

        float seaLevel, seaLevelVel;
        Vector3 lastSeaPos;
        bool haveSeaPos;

        // ---- shake (2026-10-03, the kraken's slams) ----
        float shake;
        float shakeSeed;
        Quaternion appliedShake = Quaternion.identity;

        /// A short camera shake, `amount` ~ 0..1 (1 = a hard slam). Adds to
        /// what is left of the last one and decays in ~0.5 s. A position
        /// jitter applied after the framing and a small rotation jitter that
        /// is taken off again next frame (like the juice roll), so neither
        /// ever feeds the filters.
        public void Shake(float amount)
        {
            if (amount <= 0f) return;
            shake = Mathf.Min(1.5f, shake + amount);
            if (shakeSeed == 0f) shakeSeed = Random.Range(1f, 100f);
        }

        // ---- boost punch (2026-10-03, DREDGE step 3; BoostTuning) ----
        // The engage kick and the sustained boost feel, all display-time like
        // the juice: the lens term rides on `sailLens`, the rumble on the
        // shake's apply path. Neither feeds `rigPos`/`sailFov`, and both are
        // gated off whenever a higher shot owns the frame.
        SeaSick.Ship.HelmInput helm;
        float boostFov, boostRumble, lastEngageSeen = float.NegativeInfinity;
        Quaternion appliedRumble = Quaternion.identity;
        float rumbleSeed;

        /// The boost lens (degrees on top of the juice FOV) and the rumble
        /// level, eased. `gate` is 0 when the shot is not the plain sailing
        /// chase or `BoostTuning.cameraFx` is off.
        void UpdateBoostFx(float dt, float gate)
        {
            float punch = Mathf.Max(0f, SeaSick.Ship.BoostTuning.punch);
            if (!SeaSick.Ship.BoostTuning.cameraFx) gate = 0f;
            bool boosting = helm != null && helm.isActiveAndEnabled && helm.Boosting;
            float engagedAt = helm != null ? helm.BoostEngagedAt : float.NegativeInfinity;
            // A new engage: the jolt, through the kraken's own Shake (it only
            // ADDS to what is left, so a slam in progress is not cut short).
            if (engagedAt != lastEngageSeen)
            {
                lastEngageSeen = engagedAt;
                if (gate > 0.5f && boosting)
                    Shake(Mathf.Max(0f, SeaSick.Ship.BoostTuning.engageShake) * punch);
            }

            float since = Time.time - engagedAt;
            float inS = Mathf.Max(0.01f, SeaSick.Ship.BoostTuning.engageFovInSeconds);
            float kick = Mathf.Max(0f, SeaSick.Ship.BoostTuning.engageFovKickDeg) * punch;
            float sustain = Mathf.Max(0f, SeaSick.Ship.BoostTuning.sustainFovDeg) * punch;
            float want = !boosting || gate <= 0f ? 0f : since < inS ? kick : sustain;
            want = Mathf.Min(want, Mathf.Max(0f, SeaSick.Ship.BoostTuning.boostFovCapDeg));
            // "In over N s" = ~95% there: an exponential with tau = N/3.
            float tau = want > boostFov + 1e-3f ? inS
                : boosting && gate > 0f ? SeaSick.Ship.BoostTuning.engageFovSettleSeconds
                : SeaSick.Ship.BoostTuning.fovOutSeconds;
            boostFov = SeaSick.Ship.JuiceTuning.Ease(boostFov, want, Mathf.Max(0f, tau) / 3f, dt);

            float wantRumble = boosting && gate > 0f
                ? Mathf.Max(0f, SeaSick.Ship.BoostTuning.rumbleAmp) * punch : 0f;
            boostRumble = SeaSick.Ship.JuiceTuning.Ease(boostRumble, wantRumble,
                Mathf.Max(0f, SeaSick.Ship.BoostTuning.rumbleFadeSeconds) / 3f, dt);
        }

        bool HasLockAim => LockTarget != null || SeaFocus.HasValue;
        Vector3 LockAimPos => LockTarget != null ? LockTarget.position : SeaFocus.GetValueOrDefault();

        /// How far into the lock framing the rig is, 0..1, for `LockOnCheck`.
        public float LockLevel => lockLevel;

        /// A high, steeply-tilted look at a whole island, used when she is
        /// lying at a dock.
        ///
        /// Not a straight overhead: 90 degrees is a map, and a map has no
        /// horizon, no sky and no sense of how tall anything is -- which is
        /// most of what there is to see on an island with a 266 m massif on
        /// it. Tilted, the land still reads as land, the ship stays in the
        /// frame as the thing you know the size of, and the sea and sky give
        /// it somewhere to be.
        public struct IslandShot
        {
            public Vector3 centre;
            public float radius;     // what has to fit in frame
            public Vector3 from;     // horizontal direction the camera sits in

            /// Explicit framing, for the tuner. Zero means "work it out":
            /// `tiltDeg` falls back to the rig's own tilt, `span` to the
            /// distance that fits `radius` with the legibility clamp applied.
            public float tiltDeg;
            public float span;

            /// Metres of ground up the frame, overriding the authored zoom.
            ///
            /// The unit the zoom is AUTHORED in, so a player-driven zoom and
            /// the hand-tuned dock shot are the same kind of number and can be
            /// compared. Zero means "use the authored one".
            public float ground;

            /// The player is driving.
            ///
            /// Two things that are right for an automatic shot are wrong the
            /// moment somebody is steering it: the legibility clamp, which
            /// refuses to back off far enough to see a whole island, and the
            /// slide that drags the frame until the ship is inside it. Both
            /// fight the hands on the controls. A player who has zoomed out
            /// past legibility has said what they want.
            public bool free;

            /// **The rendered pose IS the computed seat, this frame.**
            ///
            /// Every other shot this rig serves is a moving target that the
            /// two response filters chase (`positionResponse` 2.2,
            /// `rotationResponse` 3), and that lag is most of what makes the
            /// sailing camera feel like a camera rather than a cursor. Under a
            /// hand on the land it is fatal: a 1:1 grab means the ground point
            /// the finger went down on stays UNDER the finger, and it cannot,
            /// through a low pass — the land slides on for a third of a second
            /// after the hand stops, which reads as ice.
            ///
            /// So `IslandCam` does its own easing (it has to: it is the thing
            /// that knows a fling from a drag) and asks for the result
            /// verbatim. Only honoured once `overviewLevel > 0.98`, and the
            /// blend is snapped to 1 at that point — at 0.98 the seat is still
            /// 2% of the way back down to the sailing shot, which at a 300 m
            /// span is metres of drift under a "1:1" grab.
            ///
            /// Default false, so nothing that does not ask for it changes.
            public bool direct;

            /// Metres of air to keep between the lens and the real ground.
            ///
            /// Zero means the rig's own `terrainClearance` against the 46-
            /// sector `Island.SurfacePoint`, which is what every shot before
            /// the hands-on view used and what they keep using. Above zero it
            /// switches the clamp to the HEIGHT FIELD — the same function the
            /// picks, the siting rules and `IslandCam`'s own terrain yield all
            /// read — because a clamp that disagrees with the pick is a camera
            /// that dives into a hill the cursor says is not there.
            ///
            /// The water floor is applied either way and is not negotiable.
            public float clearance;
        }

        /// The sailing rig, overridable live so it can be FLOWN rather than
        /// guessed at. Same idea as `OverviewOverride`: SailCamTuner writes
        /// this every frame while it is active, and the numbers it prints drop
        /// straight back into the serialized fields below.
        public struct SailShot
        {
            public float distance, height, lookAhead, lookHeight, fov;
        }

        public SailShot? SailOverride { get; set; }

        public IslandShot? Overview { get; set; }

        /// Set by the dev camera tuner. Takes priority over `Overview` so the
        /// tuner and the game can both write every frame without racing on
        /// script execution order.
        public IslandShot? OverviewOverride { get; set; }

        /// What the overview is actually doing right now, for the tuner's
        /// readout and for the probe.
        public float CurrentTilt { get; private set; }
        public float CurrentSpan { get; private set; }

        /// The three-quarter angle Kevin flew to, in ONE place.
        ///
        /// `overviewTilt` below is serialized and therefore may be overridden
        /// in a scene; this is the authored number the seat triangle falls
        /// back to when a shot carries no explicit tilt of its own, and it is
        /// that field's initializer, so the two cannot drift apart in source.
        public const float DefaultOverviewTilt = 32f;

        [Header("Island overview (at a dock)")]
        [Tooltip("Degrees above the horizon. 90 is a map and 56 still reads as one; 32 is the three-quarter angle Kevin flew to with DockCamTuner, low enough that a building shows its WALLS and not just its roof.")]
        [SerializeField] float overviewTilt = DefaultOverviewTilt;
        [Tooltip("How much wider than the island to frame, so it isn't jammed against the edges. Only used when no explicit ground coverage is set.")]
        [SerializeField] float overviewMargin = 1.3f;

        [Tooltip("Lens for the overview. Narrow on purpose: at 36 degrees the perspective flattens toward isometric, which is what makes a cluster of buildings read as a plan you can act on rather than a photograph of a hillside. Chosen by hand with DockCamTuner.")]
        [SerializeField] float overviewFov = 36f;

        [Tooltip("Metres of ground across the frame's HEIGHT. This is the zoom, expressed so it stays the same picture whatever the lens and the screen are: 165 m puts a crew member at about 1% of screen height. Chosen by hand with DockCamTuner.")]
        [SerializeField] float overviewGroundMetres = 165f;
        [Tooltip("Seconds-ish to rise into the overview and to come back down.")]
        [SerializeField] float overviewResponse = 2f;

        [Tooltip("Back the overview off far enough that the ship is always in the frame, at the aspect the game is actually running. The authored zoom is kept whenever it already holds her, so this changes nothing until it has to.")]
        [SerializeField] bool overviewHoldsShip = true;
        [Tooltip("Metres of clear water to leave outboard of her when the shot has to widen for her. Half her length plus a little.")]
        [SerializeField] float overviewShipMargin = 22f;
        /// The WIDEST aspect worth composing for. The overview frames for
        /// whatever the window actually is, clamped to this — so a wide desk
        /// window frames wide, an ultrawide does not tighten past 16:9, and a
        /// phone still backs off far enough to hold her.
        ///
        /// **It used to be `[SerializeField]`, and that was the bug.** Unity
        /// wrote 0.4615 into `Sea.unity` when the component was added, and a
        /// serialized value wins over the C# field initializer forever — so
        /// switching the game to landscape by editing the default here would
        /// have changed nothing at all, silently, which is the same
        /// serialization trap that made `_StormDeep` read the old colour for a
        /// whole session and that `Dock.ViewHalfWidth` drifted through.
        ///
        /// This is not per-scene tuning. It is a fact about the shape the game
        /// is composed for, and there is exactly one of those, so it lives in
        /// exactly one place. The stale line left in `Sea.unity` is now
        /// ignored and Unity drops it on the next save.
        const float narrowestAspect = LandscapeAspect;

        [Tooltip("How tall a 1.7 m crew member must be, as a FRACTION of screen height. 0.0055 is about 13 px on a phone. This is what stops the overview backing off to a pretty landform nobody can read; it is set as low as it is because the frame also has to hold the pier, and the pier is a village-width away from the village.")]
        [SerializeField] float minPersonScreenFraction = 0.0055f;
        float overviewLevel;
        /// **How far the drawn rig still is from the seat a hand is asking
        /// for, while `direct` is on.** See the note where `rigPos` is written.
        Vector3 directOffset;
        Quaternion directTurn = Quaternion.identity;
        bool wasDirect;
        /// How fast that gap closes, per second. 10 leaves under 1 % of it
        /// after half a second: gone before a drag is a hand's width long.
        const float directSettle = 10f;
        float sailFovOverride = -1f;
        float baseFarClip = -1f;
        float sailFov = -1f;

        // --- speed and turn juice (JuiceTuning, 2026-09-24) -------------------
        //
        // Kevin on the helm: "slow, uneventful, not responsive". Three effects
        // on the AT-SEA chase shot only, each read live from
        // `SeaSick.Ship.JuiceTuning` every frame and eased by its
        // `camLagSeconds` (0 = instant):
        //   * the lens opens by camFovBoostDeg at top speed -- in BOTH shapes.
        //     This deliberately overrides the "portrait lens is fixed" rule
        //     below; the slider goes to 0 if it proves sickening on the phone;
        //   * the seat sinks camDropMeters toward the water at top speed;
        //   * the frame rolls INTO a turn, camLeanPerYawDeg per deg/s of yaw,
        //     capped at JuiceRollCap.
        // All three are display-time offsets: none of them feeds `rigPos` or
        // `sailFov`, so the framing filters underneath are untouched, and all
        // three fade out with the island overview, a shore party and the
        // sail-cam tuner, so the B&W2 view and the tuner's readout never see
        // them.
        const float JuiceRollCap = 8f;
        float juiceFov, juiceDrop, juiceRoll;
        /// The roll written onto the rotation last frame, stripped off again
        /// before the rotation filter runs so it never accumulates.
        float appliedRoll;
        Rigidbody targetBody;
        readonly SailingTurnOrbit turnOrbit = new SailingTurnOrbit();
        Vector3 previousSailingPosition;
        bool wasSailingQuarter;
        /// Filtered camera yaw on the sailing-quarter path (2026-09-29), and whether it is seeded.
        float sailYaw;
        bool sailYawSeeded;
        public float SailingQuarterAngle => turnOrbit.Angle;

        // --- V2 sailing camera (JuiceTuning.camStyleV2, 2026-10-02) ----------
        //
        // Kevin turned blind and lost the helm in a fight. Three moves, all
        // gated by camStyleV2 so false is the old rig untouched:
        //   * an explicit upright pitch, plus a RISE when there is something
        //     to see (slow, hard over, a raider or land near) -- open water at
        //     speed keeps the low close shot;
        //   * the aim leans INTO a turn off the rudder, so it moves first;
        //   * a lock never swings the seat far off her stern, so the view
        //     stays over her bow and stick left is screen left.
        /// Smoothed 0..1 "there is something to see".
        float need01;
        /// The slow-moving part of it (raiders, land), sampled a few times a
        /// second rather than every frame.
        float surroundings01;
        float nextSurroundings;
        const float SurroundingsInterval = 0.25f;
        const float RiseHostileRange = 150f;
        const float RiseLandRange = 120f;
        /// Slower than this share of top speed and she is manoeuvring, not passing through.
        const float RiseSlowFull = 0.2f, RiseSlowNone = 0.35f;
        /// The rise eases back this many times slower than it comes up.
        const float RiseFallSlower = 2.5f;
        /// How much of the lean is the order (anticipates) versus the yaw rate (confirms).
        const float LeadRudderShare = 0.65f;
        /// Highest the horizon may sit, as a fraction of screen height from the bottom.
        const float HorizonMaxFrac = 0.84f;
        /// Smoothed lean into the turn, degrees, +ve to starboard.
        float turnLead;

        // --- V2 easing (2026-10-02, Kevin: "the camera feels too snappy.
        // there needs to be easing.") ----------------------------------------
        //
        // V2 shipped with first-order lags (velocity jumps the instant their
        // target moves), a lean keyed 65% off a rudder that now snaps, and
        // hard clamps the aim ran into. Every V2 follow is a critically
        // damped spring now (`JuiceTuning.Spring`), the limits are soft
        // (`JuiceTuning.SoftLimit`), and the lean rides the yaw rate. All of
        // it is behind camStyleV2; false still runs the old rig line for line.
        float needVel, needHeld, leadVel, rudderSm, rudderSmVel, lockVel;
        /// Seconds the "something to see" want is held before it may fall.
        const float RiseHoldSeconds = 1f;
        /// Spring time on the rudder before it feeds the rise and the lean.
        const float RudderSmoothSeconds = 0.25f;
        /// The coaster sailing view's lean: the ORDER running ahead of the
        /// turn (smoothed rudder minus yaw rate), so the view starts round as
        /// the stick goes over and eases off as she answers. Zero in a steady
        /// turn and negative as the thumb lifts, so it helps the view stop
        /// with her instead of swinging past. `turnLead` is the lock and
        /// other-hull lean.
        float quarterLead, quarterLeadVel;
        /// Her yaw rate, deg/s, through a short spring: the yaw spring's
        /// feed-forward. Her rate rather than the seat's own swing, because
        /// the seat keeps swinging while it catches up after she stops and
        /// feeding that forward carried the view past her heading.
        float yawRateSm, yawRateSmVel;
        const float YawRateSmoothSeconds = 0.25f;
        /// The seat's own velocity relative to her (the spring's state).
        Vector3 seatVel;
        Vector3 prevRigPos, prevShipFlat;
        bool wasV2Follow, wasRigTracked;
        /// The V2 aim: yaw and pitch springs, seeded from the drawn pose.
        float v2Yaw, v2Pitch, yawVel, pitchVel;
        bool v2RotSeeded;
        /// The drawn yaw/pitch last frame and how fast they moved, so a spring
        /// taking over from another mode starts at the speed already on screen.
        float lastDrawnYaw, lastDrawnPitch, drawnYawRate, drawnPitchRate;
        bool haveDrawn;
        /// Where the lock target last was, so the framing eases OUT of a lock
        /// that ended (the target sank or was dropped) instead of snapping.
        Vector3 lastLockPos;
        bool haveLockPos;
        public float Need01 => need01;
        public float TurnLead => turnLead;

        /// What the juice is adding right now, for the lab's readout.
        public float JuiceFov => juiceFov;
        public float JuiceDrop => juiceDrop;
        public float JuiceRoll => juiceRoll;

        /// Diagnostic only: the distance the overview last asked for.
        public float LastOverviewSpan { get; private set; }
        public Vector3 LastOverviewSeat { get; private set; }
        public Vector3 LastDesired { get; private set; }

        /// **Where the overview is actually LOOKING**, after the slide that
        /// drags the frame over until the ship is inside it.
        ///
        /// Not `Overview.centre`: the seat is built from the shifted point, so
        /// the authored centre is not on the axis of the shot that is on
        /// screen. `IslandCam` latches its pivot from THIS the first time
        /// somebody takes hold of the land — latching the authored centre
        /// instead would jump the picture by however far the slide had moved
        /// it, which on a long pier is tens of metres.
        public Vector3 LastOverviewAim { get; private set; }

        /// The lens the overview settles at, for anything that has to build
        /// the same frustum by hand (see `IslandCam.ScreenRay`, which must not
        /// use the camera's own — that one lags).
        public float OverviewFov => IslandCamLock.locked ? IslandCamLock.fovDeg : overviewFov;

        /// 0 = sailing, 1 = fully up at the island. Input stays inert until
        /// this is essentially 1: a grab against a camera still flying up
        /// from the chase shot has nothing fixed to take hold of.
        public float OverviewLevel => overviewLevel;

        /// **The drawn pose IS the asked-for pose, this frame.** True once a
        /// `direct` shot's leftover gap has decayed to nothing. A screen ray
        /// composed against the asked-for pose is exact only while this is
        /// true, which is what `IslandCamProbe` waits on before it measures
        /// whether the land stays under the cursor.
        public bool OverviewDirect { get; private set; }

        /// **The rig has caught up with the overview seat** -- to within 2 % of
        /// the span, or it is being driven `direct`.
        ///
        /// The blend reaching 1 is not this. `overviewLevel` cross-fades the
        /// TARGET; the rig then eases toward that target through a 2.2/s low
        /// pass and is still tens of metres short when the blend says done --
        /// `IslandCamProbe` caught an "untouched, ready" view moving 15 m in two
        /// frames. A hand that grabs then is grabbing a picture in flight.
        /// `IslandCam.Ready` waits for both.
        public bool OverviewSettled { get; private set; }

        /// **The seat triangle, in one place.**
        ///
        /// `IslandCam` has to know exactly where the lens will be BEFORE the
        /// frame is drawn — every screen ray it casts, every ground point it
        /// keeps under the cursor and every terrain yield it makes is built
        /// from that pose. Working it out a second time over there is how the
        /// grab and the picture end up half a degree apart, and half a degree
        /// at 300 m is metres on the ground.
        ///
        /// `span` and `tiltDeg` are expected to be RESOLVED by the caller
        /// (LateUpdate does its own legibility clamp and authored-tilt
        /// fallback first, then hands the resolved shot straight in). The
        /// fallbacks here are guards, not policy.
        public static void OverviewPose(in IslandShot s, float vfovDeg,
                                        out Vector3 seat, out Quaternion rot)
        {
            float tanHalf = Mathf.Tan(Mathf.Max(1f, vfovDeg) * 0.5f * Mathf.Deg2Rad);
            float span = s.span > 0.01f
                ? s.span
                : (s.ground > 0.01f ? s.ground / (2f * tanHalf)
                                    : Mathf.Max(1f, s.radius * 2f));
            float tiltDeg = s.tiltDeg > 0.01f ? s.tiltDeg : DefaultOverviewTilt;

            Vector3 dir = s.from;
            dir.y = 0f;
            dir = dir.sqrMagnitude < 0.01f ? Vector3.back : dir.normalized;

            float tilt = tiltDeg * Mathf.Deg2Rad;
            seat = s.centre
                 + dir * (span * Mathf.Cos(tilt))
                 + Vector3.up * (span * Mathf.Sin(tilt));

            Vector3 look = s.centre - seat;
            rot = look.sqrMagnitude > 1e-6f
                ? Quaternion.LookRotation(look, Vector3.up)
                : Quaternion.identity;
        }

        /// How far back the rig may sit and still draw a person big enough to
        /// see.
        ///
        /// A camera D metres away sees 2 D tan(fov/2) metres up the frame, so
        /// a person of height P covers P / (2 D tan(fov/2)) of the screen's
        /// height. Holding that at or above the floor gives the distance.
        ///
        /// **A FRACTION, not a pixel count.** Keying this to Screen.height
        /// made the framing depend on the size of the window it happened to
        /// be running in: the editor Game view is 422 px tall here and the
        /// phone is 2340, so the same code framed a 78 m view in one and a
        /// 430 m view in the other, and neither was a decision anybody made.
        /// A fraction of screen height is the same picture everywhere.
        /// **Metres of ground up the frame that put the lens `metres` ABOVE
        /// what it is looking at.**
        ///
        /// The overview is authored in ground coverage, not in altitude, for
        /// good reasons (distance means nothing without the lens). But height
        /// is how a person describes a camera — "35 m above the fire" — so the
        /// translation belongs here, beside the geometry it inverts, rather
        /// than as a number somebody worked out once and wrote down.
        ///
        /// The seat is `aim + dir·span·cos(tilt) + up·span·sin(tilt)`, so a
        /// height of h wants `span = h / sin(tilt)`, and coverage is
        /// `span · 2 · tan(fov/2)`. At the shipped 32° tilt and 36° lens,
        /// 35 m up is about 43 m of ground.
        public float OverviewGroundForHeight(float metres)
        {
            float tilt = Mathf.Max(1f, overviewTilt) * Mathf.Deg2Rad;
            float span = Mathf.Max(0.1f, metres) / Mathf.Sin(tilt);
            return span * 2f * Mathf.Tan(OverviewFov * 0.5f * Mathf.Deg2Rad);
        }

        /// **Metres of ground up the frame that hold a circle of `radius`
        /// ACROSS the frame, at whatever shape the window actually is.**
        ///
        /// The overview's zoom is authored as metres up the FRAME HEIGHT, so
        /// on a phone held upright a coverage that comfortably holds a 160 m
        /// ring vertically holds only `ground · 0.46` of it horizontally —
        /// which is how the campfire ring ended up wider than the screen. The
        /// fit therefore has to be taken on the narrow axis, and on a phone
        /// that is the width.
        ///
        /// Same aspect clamp the ship-slide uses (`narrowestAspect`), so an
        /// ultrawide desk window does not tighten the shot past 16:9.
        public float OverviewGroundForRing(float radius, float margin)
        {
            float aspect = Mathf.Min(
                Mathf.Max(0.2f, cam != null ? cam.aspect : 1f), narrowestAspect);
            float across = 2f * Mathf.Max(1f, radius) * Mathf.Max(1f, margin);
            // Up the frame: `across`. Across the frame: `across / aspect`.
            // Whichever is bigger is the one that has to fit.
            return Mathf.Max(across, across / aspect);
        }

        /// And back again, so anything reporting the view can say how high it
        /// is without re-deriving the same triangle.
        public float OverviewHeightForGround(float groundMetres)
        {
            float tilt = Mathf.Max(1f, overviewTilt) * Mathf.Deg2Rad;
            float span = groundMetres / (2f * Mathf.Tan(OverviewFov * 0.5f * Mathf.Deg2Rad));
            return span * Mathf.Sin(tilt);
        }

        public float ReadableDistance(float fov)
        {
            float t = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            return SeaSick.World.WorldScale.Person
                 / Mathf.Max(1e-4f, minPersonScreenFraction * 2f * t);
        }

        /// What a person actually measures from where the rig is now, as a
        /// fraction of screen height -- the number the probe reports, so this
        /// is never a guess.
        public float PersonScreenFraction(float distance, float fov)
        {
            float t = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            return SeaSick.World.WorldScale.Person / Mathf.Max(0.01f, 2f * distance * t);
        }

        void Start()
        {
            cam = GetComponent<Camera>();
            Resolve();

        }

        // `Touch.activeTouches` is empty until this is on, and it is
        // ref-counted, so enabling it here is safe alongside HelmInput's own.
        // No GUILayout in `OnGUI`: skips IMGUI's layout pass every frame.
        void OnEnable() { ET.EnhancedTouchSupport.Enable(); useGUILayout = false; }
        void OnDisable() => ET.EnhancedTouchSupport.Disable();

        /// **The player's look offset** (DREDGE controls step 2, 2026-10-03,
        /// docs/PLAN-dredge-controls.md §3.3). Samples `SeaCameraInput` and
        /// keeps the offset; `ApplyLook` puts it on the plain sea chase.
        ///
        /// Follow (`SeaCameraPrefs.Follow`, the default): the base seat is
        /// behind her stern exactly as before, the offset rides on top, so
        /// the view swings with the hull. Detached: the base holds the world
        /// azimuth it had (`detachedAz`) while she turns under it; the shot
        /// is rotated by her stern-to-held-bearing angle plus the offset, so
        /// the offset absorbs nothing extra and switching modes is seamless
        /// (the held angle is folded into the offset on the way back to
        /// Follow). Recenter eases the offset home over `recenterSeconds`
        /// and, detached, brings the base back behind her with it.
        ///
        /// Stands down with everything else that reads world touches: the
        /// island view, the shipyard, a menu, the shipyard modal. While a
        /// higher shot (lock, SeaFocus, the island overview, a shore party,
        /// the tuner) is fully in, look input is ignored but the offset kept.
        void UpdateLook(float dt)
        {
            bool active = !IslandCam.Engaged
                && !SeaSick.Ship.Modular.ShipyardSession.WorldInputBlocked
                && SeaSick.UI.Menus.GameMenus.Current == SeaSick.UI.Menus.GameMenus.Mode.None
                && !SeaSick.UI.ModularYard.ShipyardModal.IsOpen;
            // Last frame's levels: this runs before the pose works them out.
            float higher = Mathf.Max(Mathf.Max(lockLevel, seaLevel), overviewLevel);
            bool lookLive = higher < 0.99f && !PointOfInterest.HasValue && !SailOverride.HasValue;
            seaInput.Sample(active, lookLive, combatLock, dt);

            float sternAz = SternAz(target.forward);
            bool follow = SeaCameraPrefs.Follow;
            if (!detachedSeeded) { detachedAz = sternAz; detachedSeeded = true; }
            if (follow && !wasFollow)
            {
                float held = Mathf.DeltaAngle(sternAz, detachedAz);
                userYaw += held;
                lookYaw += held;
                detachedAz = sternAz;
            }
            wasFollow = follow;

            // The island view coming up (anchoring, ashore) homes everything,
            // so casting off starts behind her. Eased while the overview
            // still shows the sea under it; hard zero once it covers it.
            if ((OverviewOverride ?? Overview).HasValue)
            {
                if (overviewLevel > 0.9f) ZeroLook(sternAz);
                else if (!recentering && LookOff(sternAz)) StartRecenter(sternAz);
            }
            if (seaInput.RecenterRequested) StartRecenter(sternAz);

            if (recentering)
            {
                if (seaInput.YawDeltaDeg != 0f || seaInput.PitchDeltaDeg != 0f)
                    recentering = false;   // a real look takes the view back
                else
                {
                    recenterT = Mathf.Min(1f, recenterT + dt / Mathf.Max(0.05f, SeaCameraTuning.recenterSeconds));
                    float k = 1f - Mathf.SmoothStep(0f, 1f, recenterT);
                    // The recenter is its own ease: no follow spring on top.
                    lookYaw = userYaw = recenterYaw0 * k;
                    lookPitch = userPitch = recenterPitch0 * k;
                    lookYawVel = lookPitchVel = 0f;
                    if (recenterT >= 1f) recentering = false;
                }
            }
            // Follow, or mid-recenter: the base rides behind her.
            if (follow || recentering) detachedAz = sternAz;

            userYaw += seaInput.YawDeltaDeg;
            userPitch = Mathf.Clamp(userPitch + seaInput.PitchDeltaDeg,
                Mathf.Min(0f, SeaCameraTuning.pitchMinDeg), Mathf.Max(0f, SeaCameraTuning.pitchMaxDeg));
            // Held in -180..180; the drawn value shifts with it, so the
            // spring never sees the wrap.
            if (userYaw > 180f) { userYaw -= 360f; lookYaw -= 360f; }
            else if (userYaw < -180f) { userYaw += 360f; lookYaw += 360f; }
            if (!recentering)
            {
                float ft = Mathf.Max(0f, SeaCameraTuning.followSeconds);
                lookYaw = SeaSick.Ship.JuiceTuning.Spring(lookYaw, userYaw, ref lookYawVel, ft, dt);
                lookPitch = SeaSick.Ship.JuiceTuning.Spring(lookPitch, userPitch, ref lookPitchVel, ft, dt);
            }
        }

        static float SternAz(Vector3 forward)
        {
            forward.y = 0f;
            return forward.sqrMagnitude > 1e-6f ? Mathf.Atan2(-forward.x, -forward.z) * Mathf.Rad2Deg : 180f;
        }

        bool LookOff(float sternAz) => Mathf.Abs(lookYaw) > 0.01f || Mathf.Abs(lookPitch) > 0.01f
            || Mathf.Abs(Mathf.DeltaAngle(sternAz, detachedAz)) > 0.01f;

        /// Start easing home from what is DRAWN (so nothing jumps). Detached,
        /// the held bearing joins the offset and comes home with it.
        void StartRecenter(float sternAz)
        {
            userYaw = Mathf.DeltaAngle(0f, lookYaw + Mathf.DeltaAngle(sternAz, detachedAz));
            userPitch = lookPitch;
            detachedAz = sternAz;
            lookYaw = recenterYaw0 = userYaw;
            lookPitch = recenterPitch0 = userPitch;
            lookYawVel = lookPitchVel = 0f;
            recenterT = 0f;
            recentering = Mathf.Abs(userYaw) > 0.01f || Mathf.Abs(userPitch) > 0.01f;
        }

        void ZeroLook(float sternAz)
        {
            userYaw = userPitch = lookYaw = lookPitch = lookYawVel = lookPitchVel = 0f;
            recentering = false;
            detachedAz = sternAz;
        }

        /// Puts the look offset on the plain sea chase (or lock) pose: the
        /// seat and the look point turn about her by the yaw, then the seat
        /// climbs or drops about the look point by the pitch (distance
        /// kept). Weighted by `1 - lockLevel`, so the lock framing blends it
        /// out and is exactly its old self when fully in; the SeaFocus and
        /// overview blends lerp over the result, which does the same for
        /// them. The tuner's flown numbers never carry it.
        void ApplyLook(ref Vector3 seat, ref Vector3 look, Vector3 shipFlat, Vector3 flatForward, float seaY)
        {
            float w = SailOverride.HasValue ? 0f : 1f - Mathf.Clamp01(lockLevel);
            float yaw = (Mathf.DeltaAngle(SternAz(flatForward), detachedAz) + lookYaw) * w;
            float pitch = lookPitch * w;
            if (Mathf.Abs(yaw) > 1e-3f)
            {
                Quaternion q = Quaternion.AngleAxis(yaw, Vector3.up);
                seat = shipFlat + q * (seat - shipFlat);
                look = shipFlat + q * (look - shipFlat);
            }
            if (Mathf.Abs(pitch) > 1e-3f)
            {
                // `look` carries the heave, the seat does not (it is added
                // after the position filter): take it off for the pivot.
                Vector3 pivot = look - Vector3.up * seaY;
                Vector3 s = seat - pivot;
                Vector3 h = new Vector3(s.x, 0f, s.z);
                float hm = h.magnitude, len = s.magnitude;
                if (len > 0.1f)
                {
                    Vector3 hd = hm > 1e-3f ? h / hm : -flatForward;
                    float elev = Mathf.Atan2(s.y, hm) * Mathf.Rad2Deg;
                    // Never under the water, never past vertical.
                    float e = Mathf.Clamp(elev + pitch, Mathf.Min(elev, 3f), Mathf.Max(elev, 80f)) * Mathf.Deg2Rad;
                    seat = pivot + hd * (Mathf.Cos(e) * len) + Vector3.up * (Mathf.Sin(e) * len);
                }
            }
        }

        /// The camera stick's ring. Same suppression as `HelmInput.OnGUI`.
        void OnGUI()
        {
            if (IslandCam.Engaged) return;
            if (SeaSick.UI.ModularYard.ShipyardModal.IsOpen
                || SeaSick.UI.Menus.GameMenus.Current != SeaSick.UI.Menus.GameMenus.Mode.None) return;
            if (Event.current.type != EventType.Repaint) return;
            seaInput.Draw();
        }

        /// The motor was fetched once in Start, which was fine while there was
        /// one ship. The yard never changes the target, but a shore boat or a
        /// dev rig can, and a stale motor means a stale hull length.
        void Resolve()
        {
            if (target == motorFor) return;
            motorFor = target;
            turnOrbit.Reset();
            wasSailingQuarter = false;
            motor = target != null
                ? target.GetComponent<SeaSick.Ship.ShipMotor>() : null;
            targetBody = target != null ? target.GetComponent<Rigidbody>() : null;
            helm = target != null ? target.GetComponent<SeaSick.Ship.HelmInput>() : null;
            lastEngageSeen = helm != null ? helm.BoostEngagedAt : float.NegativeInfinity;
            combatLock = target != null ? target.GetComponent<SeaSick.Combat.CombatLock>() : null;
            detachedSeeded = false;
        }

        /// The three juice terms, eased toward what speed and yaw ask for.
        /// `gate` is 0 whenever the shot is not the plain sailing chase.
        void UpdateJuice(float dt, float gate)
        {
            // Read every frame: the lab moves these live.
            float lag = Mathf.Max(0f, SeaSick.Ship.JuiceTuning.camLagSeconds);
            float s01 = motor != null ? SeaSick.Ship.JuiceTuning.Speed01(motor) : 0f;
            float yaw = SeaSick.Ship.JuiceTuning.YawRateDeg(targetBody);

            float wantFov = Mathf.Max(0f, SeaSick.Ship.JuiceTuning.camFovBoostDeg) * s01 * gate;
            float wantDrop = Mathf.Max(0f, SeaSick.Ship.JuiceTuning.camDropMeters) * s01 * gate;
            // +yaw is to starboard. Leaning the frame to starboard is a
            // NEGATIVE roll about the camera's forward axis in Unity.
            float wantRoll = Mathf.Clamp(-SeaSick.Ship.JuiceTuning.camLeanPerYawDeg * yaw,
                                         -JuiceRollCap, JuiceRollCap) * gate;

            juiceFov = SeaSick.Ship.JuiceTuning.Ease(juiceFov, wantFov, lag, dt);
            juiceDrop = SeaSick.Ship.JuiceTuning.Ease(juiceDrop, wantDrop, lag, dt);
            juiceRoll = SeaSick.Ship.JuiceTuning.Ease(juiceRoll, wantRoll, lag, dt);
        }

        /// The V2 rise and lean. Worked out every frame whatever the style, so
        /// flipping camStyleV2 mid-voyage eases in instead of popping.
        /// `plainChase` is false for a shore party or the tuner.
        void UpdateSeeing(float dt, bool plainChase)
        {
            float s01 = motor != null ? SeaSick.Ship.JuiceTuning.Speed01(motor) : 0f;
            float yaw = SeaSick.Ship.JuiceTuning.YawRateDeg(targetBody);
            float turn01 = SeaSick.Ship.JuiceTuning.Turn01(motor, yaw);
            float rudder = motor != null ? Mathf.Clamp(motor.Rudder, -1f, 1f) : 0f;

            if (Time.time >= nextSurroundings)
            {
                nextSurroundings = Time.time + SurroundingsInterval;
                surroundings01 = Surroundings(target.position);
            }

            float slow = 1f - Mathf.InverseLerp(RiseSlowFull, RiseSlowNone, s01);
            float peak = motor != null ? Mathf.Max(8f, motor.MaxTurnRate) : 15f;
            float yaw01 = Mathf.Clamp(yaw / peak, -1f, 1f);
            if (SeaSick.Ship.JuiceTuning.camStyleV2)
            {
                // Eased: the rudder is smoothed before anything reads it, the
                // want is HELD a second before it may fall (hysteresis: a
                // helm flicking past the threshold cannot pump it), and both
                // the rise and the lean are springs, so they ease in and out.
                rudderSm = SeaSick.Ship.JuiceTuning.Spring(rudderSm, rudder, ref rudderSmVel,
                    RudderSmoothSeconds, dt);
                yawRateSm = SeaSick.Ship.JuiceTuning.Spring(yawRateSm, yaw, ref yawRateSmVel,
                    YawRateSmoothSeconds, dt);
                float turningSm = Mathf.Max(Mathf.InverseLerp(0.35f, 0.75f, turn01),
                                            Mathf.InverseLerp(0.45f, 0.9f, Mathf.Abs(rudderSm)));
                float wantSm = plainChase ? Mathf.Max(Mathf.Max(slow, turningSm), surroundings01) : 0f;
                needHeld = Mathf.Max(wantSm, needHeld - dt / RiseHoldSeconds);
                if (!plainChase) needHeld = 0f;
                float rise = needHeld > need01
                    ? SeaSick.Ship.JuiceTuning.camRiseSeconds
                    : SeaSick.Ship.JuiceTuning.camRiseFallSeconds;
                need01 = SeaSick.Ship.JuiceTuning.Spring(need01, needHeld, ref needVel,
                    Mathf.Max(0f, rise), dt);
                if (need01 < 0f || need01 > 1f) { need01 = Mathf.Clamp01(need01); needVel = 0f; }

                float share = Mathf.Clamp01(SeaSick.Ship.JuiceTuning.camLeadRudderShare);
                float wantLeadSm = plainChase
                    ? Mathf.Max(0f, SeaSick.Ship.JuiceTuning.camTurnLeadDeg)
                      * Mathf.Clamp(share * rudderSm + (1f - share) * yaw01, -1f, 1f)
                    : 0f;
                turnLead = SeaSick.Ship.JuiceTuning.Spring(turnLead, wantLeadSm, ref leadVel,
                    Mathf.Max(0f, SeaSick.Ship.JuiceTuning.camLeadSeconds), dt);
                float wantQuarter = plainChase
                    ? Mathf.Max(0f, SeaSick.Ship.JuiceTuning.camTurnLeadDeg)
                      * Mathf.Clamp(rudderSm - yaw01, -1f, 1f)
                    : 0f;
                quarterLead = SeaSick.Ship.JuiceTuning.Spring(quarterLead, wantQuarter, ref quarterLeadVel,
                    Mathf.Max(0f, SeaSick.Ship.JuiceTuning.camLeadSeconds), dt);
                return;
            }
            rudderSm = rudder; rudderSmVel = needVel = leadVel = 0f; needHeld = need01;
            float turning = Mathf.Max(Mathf.InverseLerp(0.35f, 0.75f, turn01),
                                      Mathf.InverseLerp(0.45f, 0.9f, Mathf.Abs(rudder)));
            float want = plainChase ? Mathf.Max(Mathf.Max(slow, turning), surroundings01) : 0f;
            float tau = Mathf.Max(0f, SeaSick.Ship.JuiceTuning.camRiseSeconds);
            need01 = SeaSick.Ship.JuiceTuning.Ease(need01, want,
                want > need01 ? tau : tau * RiseFallSlower, dt);

            // The order leads, the yaw rate confirms: the rudder is what the
            // player just asked for, so the view starts round before the hull.
            float wantLead = plainChase
                ? Mathf.Max(0f, SeaSick.Ship.JuiceTuning.camTurnLeadDeg)
                  * Mathf.Clamp(LeadRudderShare * rudder + (1f - LeadRudderShare) * yaw01, -1f, 1f)
                : 0f;
            turnLead = SeaSick.Ship.JuiceTuning.Ease(turnLead, wantLead,
                Mathf.Max(0f, SeaSick.Ship.JuiceTuning.camLeadSeconds), dt);
        }

        /// 0..1: how close the nearest live raider or shore is. Two short list
        /// walks, no allocation, a few times a second.
        static float Surroundings(Vector3 pos)
        {
            float best = 0f;
            var raiders = SeaSick.Combat.EnemyShip.All;
            for (int i = 0; i < raiders.Count; i++)
            {
                var r = raiders[i];
                if (r == null || !r.Alive) continue;
                float d = SeaSick.World.Island.FlatDistance(r.transform.position, pos);
                best = Mathf.Max(best, 1f - Mathf.InverseLerp(
                    RiseHostileRange * 0.7f, RiseHostileRange, d));
            }
            var isle = SeaSick.World.Island.Nearest(pos);
            if (isle != null)
            {
                float gap = SeaSick.World.Island.FlatDistance(isle.transform.position, pos)
                          - isle.RadiusToward(pos);
                best = Mathf.Max(best, 1f - Mathf.InverseLerp(
                    RiseLandRange * 0.6f, RiseLandRange, gap));
            }
            return best;
        }

        /// Highest pitch (deg) that keeps the horizon under `HorizonMaxFrac` of
        /// the screen through a lens of `vfov`. The horizon sits tan(pitch) /
        /// tan(vfov/2) of a half-frame above centre.
        static float HorizonPitchCap(float vfov)
        {
            float t = Mathf.Tan(Mathf.Clamp(vfov, 10f, 120f) * 0.5f * Mathf.Deg2Rad);
            return Mathf.Atan((2f * HorizonMaxFrac - 1f) * t) * Mathf.Rad2Deg;
        }

        void LateUpdate()
        {
            if (target == null) return;
            float dt = Time.deltaTime;
            Resolve();
            var sailingYard = target.GetComponent<SeaSick.Ship.Modular.ShipyardService>();
            bool sailingQuarter = sailingYard != null && sailingYard.IsCoaster
                && !PointOfInterest.HasValue && !SailOverride.HasValue
                && !Overview.HasValue && !OverviewOverride.HasValue && overviewLevel < .001f
                && !HasLockAim && lockLevel < .001f;
            // V2 at sea: the plain chase or a lock, no tuner, shore party or
            // island view. The rig then rides her translation exactly and
            // springs only the seat's swing round her (see the position
            // update below), so the travel step here is not needed.
            bool v2Follow = SeaSick.Ship.JuiceTuning.camStyleV2
                && !PointOfInterest.HasValue && !SailOverride.HasValue
                && !Overview.HasValue && !OverviewOverride.HasValue && overviewLevel < .001f;
            // Follow translation promptly, retaining angular smoothing around the hull.
            if (sailingQuarter && wasSailingQuarter && !v2Follow)
            {
                Vector3 travel = target.position - previousSailingPosition;
                travel.y = 0f;
                if (travel.sqrMagnitude < 100f) rigPos += travel;
            }
            previousSailingPosition = target.position;
            wasSailingQuarter = sailingQuarter;
            float orbitAngle = turnOrbit.Step(SeaSick.Ship.JuiceTuning.YawRateDeg(targetBody),
                motor != null ? Mathf.Abs(motor.CurrentSpeed) : 0f, dt, sailingQuarter,
                SeaSick.Ship.JuiceTuning.camTurnOrbitDeg);

            // What shape is the window? The same question the HUD asks, in
            // the same place, so a frame the camera composes for upright is
            // never a frame the HUD laid out wide.
            float wantPortrait = SeaSick.UI.HudLayout.Wide ? 0f : 1f;
            if (portrait01 < 0f) portrait01 = wantPortrait;   // seed, don't slide
            portrait01 = Mathf.Lerp(portrait01, wantPortrait,
                1f - Mathf.Exp(-portraitBlendRate * dt));

            // The speed dolly, upright. Keyed to the ORDER so the seat leads
            // her out instead of trailing the stokers.
            float order01 = motor != null
                ? Mathf.Clamp01(motor.ThrottleOrder / Mathf.Max(0.01f, motor.Overdrive))
                : 0f;
            bool lyingStill = motor == null
                || motor.Anchored
                || (Mathf.Abs(motor.ThrottleOrder) < 0.02f && motor.CurrentSpeed < 0.5f);
            // 2026-09-29: the far end is capped by JuiceTuning.camDollyMax (live
            // knob; the scene's portraitZoomOut 0.30 would otherwise win) -- the
            // dolly is the one speed effect kept, so it stays modest.
            float wantDolly = lyingStill
                ? portraitStoppedPull
                : Mathf.Min(1f + portraitZoomOut * order01,
                            Mathf.Max(1f, SeaSick.Ship.JuiceTuning.camDollyMax));
            if (dolly < 0f) dolly = wantDolly;
            // Out at the framing rate, in at the slow one: backing off is an
            // answer to an order and should be prompt; coming closer is the
            // sea going quiet and should take its time.
            float dollyRate = wantDolly < dolly ? portraitPullInRate : framingResponse;
            dolly = Mathf.Lerp(dolly, wantDolly, 1f - Mathf.Exp(-dollyRate * dt));

            UpdateLook(dt);

            // How much bigger she is than the hull the framing was written
            // for. Clamped only as a guard against a garbage length — at
            // framingPower 1 the ladder's own ends are 0.37 and 1.90.
            float loa = motor != null ? motor.HullLength : framingLoa;
            float wantK = framingMargin * Mathf.Clamp(
                Mathf.Pow(loa / Mathf.Max(1f, framingLoa), framingPower), 0.25f, 3f);
            if (!frameSeeded) { frameK = wantK; frameSeeded = true; }
            frameK = Mathf.Lerp(frameK, wantK, 1f - Mathf.Exp(-framingResponse * dt));

            // Ride the swell. Seeded on the first frame so the rig does not
            // sweep up from zero when the scene starts on a crest.
            if (!heaveSeeded) { heaveY = target.position.y; heaveSeeded = true; }
            heaveY = Mathf.Lerp(heaveY, target.position.y, 1f - Mathf.Exp(-heaveFollow * dt));
            float seaY = heaveY * heaveShare;

            // One weather number, owned by SkyDirector, so the framing builds
            // with the sky and the spray instead of arguing with them.
            var sky = SeaSick.World.SkyDirector.Instance;
            stormLevel = Mathf.Lerp(stormLevel, sky != null ? sky.Storminess01 : 0f,
                1f - Mathf.Exp(-stormResponse * dt));

            // Speed reads in the lens: FOV opens with speed and punches when
            // the hull drops onto a wave face.
            // The sailing lens is tracked in a FIELD rather than written
            // straight to the camera, because the overview needs a different
            // one. Writing both to cam.fieldOfView made each frame's lerp
            // start from the other's answer, so the two fought and settled
            // somewhere neither had asked for.
            //
            // Upright the lens is FIXED: the speed boost and the surf punch
            // both fade out with the portrait blend, because on a phone held
            // a foot from your face a moving projection is the single
            // sickest thing a chase camera can do — and the dolly above is
            // already saying everything they were saying about speed.
            // (2026-09-24: `JuiceTuning.camFovBoostDeg` now opens it anyway,
            // in both shapes, on top of this -- see the juice note by the
            // fields. Its slider at 0 restores the fixed upright lens.)
            float baseLens = Mathf.Lerp(fovBase, portraitFov, portrait01);
            if (motor != null)
            {
                float s01 = Mathf.Clamp01(motor.CurrentSpeed / motor.MaxSpeed);
                float targetFov = baseLens
                                  + (fovSpeedBoost * s01 * s01
                                     + fovSurfPunch * motor.SurfBoost01) * (1f - portrait01);
                sailFov = Mathf.Lerp(sailFov <= 0f ? targetFov : sailFov, targetFov,
                    1f - Mathf.Exp(-fovResponse * dt));
            }
            else sailFov = sailFov <= 0f
                ? baseLens
                : Mathf.Lerp(sailFov, baseLens, 1f - Mathf.Exp(-fovResponse * dt));

            // The tuner's lens, if it is holding one. Applied after the speed
            // and surf terms so those keep working underneath it.
            if (sailFovOverride > 1f) sailFov = sailFovOverride;

            // The speed and turn juice. Only on the plain chase: a shore
            // party's framing and the tuner's flown numbers stay exact, and
            // the island overview fades it out below with `overviewLevel`.
            UpdateJuice(dt, PointOfInterest.HasValue || SailOverride.HasValue ? 0f : 1f);
            UpdateBoostFx(dt, PointOfInterest.HasValue || SailOverride.HasValue
                              || Overview.HasValue || OverviewOverride.HasValue ? 0f : 1f);
            UpdateSeeing(dt, !PointOfInterest.HasValue && !SailOverride.HasValue
                             && !Overview.HasValue && !OverviewOverride.HasValue);
            bool styleV2 = SeaSick.Ship.JuiceTuning.camStyleV2;
            // How far off centre she may sit sideways and stay wholly on
            // screen, degrees. Set by the sailing branch; only V2 reads it.
            float keepOffDeg = 90f;
            // The lens actually drawn at sea. `sailFov` itself stays the
            // filter's own state, so the juice rides on top of it rather than
            // being low-passed a second time by `fovResponse`.
            float sailLens = (sailFov > 0f ? sailFov : fovBase) + juiceFov + boostFov;

            Vector3 shipFlat = new Vector3(target.position.x, 0f, target.position.z);
            Vector3 anchor, desired, lookPoint;

            if (PointOfInterest.HasValue)
            {
                seaY = 0f;   // the shore party stand on land, not on the sea
                // Frame ship + shore party: sit on the far side of the ship
                // looking past it at the island, pulled back by their spread.
                Vector3 poi = PointOfInterest.Value;
                poi.y = 0f;
                Vector3 axis = shipFlat - poi;
                float separation = axis.magnitude;
                axis = separation < 0.5f ? -target.forward : axis / separation;

                // Bias toward the shore party — they're what the player wants
                // to watch. Framing keys off their spread rather than the
                // chase-cam height, which is deliberately high and would
                // otherwise leave the crew as specks.
                anchor = Vector3.Lerp(poi, shipFlat, 0.38f);
                // The constants frame the SHIP and scale with her; the terms
                // in `separation` frame the ground between her and the party,
                // which is metres of island either way.
                float back = 18f * frameK + separation * 0.55f;
                desired = anchor + axis * back
                        + Vector3.up * (14f * frameK + separation * 0.35f);
                lookPoint = anchor + Vector3.up * (1.5f * frameK);
            }
            else
            {
                Vector3 flatForward = target.forward;
                flatForward.y = 0f;
                flatForward = flatForward.sqrMagnitude < 0.001f ? Vector3.forward : flatForward.normalized;

                bool locked = HasLockAim;

                // Cruise: only while genuinely making way, and never in a fight.
                bool making = motor != null && motor.CurrentSpeed >= motor.MaxSpeed * cruiseSpeed01;
                atSpeedFor = making && !locked ? atSpeedFor + dt : 0f;

                float wantCruise = atSpeedFor > cruiseDelay ? 1f : 0f;
                cruiseLevel = Mathf.Lerp(cruiseLevel, wantCruise,
                    1f - Mathf.Exp(-(wantCruise > cruiseLevel ? cruiseInRate : cruiseOutRate) * dt));

                if (v2Follow)
                {
                    // Eased in AND out: the old lag started at full speed.
                    lockLevel = SeaSick.Ship.JuiceTuning.Spring(lockLevel, locked ? 1f : 0f,
                        ref lockVel, Mathf.Max(0f, SeaSick.Ship.JuiceTuning.camLockSeconds), dt);
                    if (lockLevel < 0f || lockLevel > 1f) { lockLevel = Mathf.Clamp01(lockLevel); lockVel = 0f; }
                }
                else
                {
                    lockLevel = Mathf.Lerp(lockLevel, locked ? 1f : 0f,
                        1f - Mathf.Exp(-lockResponse * dt));
                    lockVel = 0f;
                }
                // Remember where the target was, so a lock that ENDS (sunk,
                // dropped) eases out from where it was rather than snapping
                // the seat and the aim home the frame LockTarget goes null.
                if (locked) { lastLockPos = LockAimPos; haveLockPos = true; }
                else if (lockLevel <= 0.001f) haveLockPos = false;

                // The tuner overrides the BASE numbers, not the seat, so the
                // per-hull scaling still applies and what it prints is what
                // goes back into the fields.
                float bDist = distance, bHeight = height, bAhead = lookAhead, bLookH = lookHeight;
                if (SailOverride.HasValue)
                {
                    var so = SailOverride.Value;
                    bDist = so.distance; bHeight = so.height;
                    bAhead = so.lookAhead; bLookH = so.lookHeight;
                    if (so.fov > 1f) sailFovOverride = so.fov;
                }
                else
                {
                    sailFovOverride = -1f;
                    // The portrait preset, crossfaded. Under the tuner the
                    // blend stands aside: what it is flying is what it must
                    // print, and a hidden second set of numbers underneath
                    // would make its readout a lie.
                    bDist = Mathf.Lerp(bDist, portraitDistance, portrait01);
                    bHeight = Mathf.Lerp(bHeight, portraitHeight, portrait01);
                    bAhead = Mathf.Lerp(bAhead, portraitLookAhead, portrait01);
                }

                // The tall stern must not hide the working deck. Preserve the existing
                // storm, lock-on and portrait behavior; lift only the coaster's base seat.
                var coaster = target != null ? target.GetComponent<SeaSick.Ship.Modular.ShipyardService>() : null;
                if (!SailOverride.HasValue && coaster != null && coaster.IsCoaster)
                {
                    float k = Mathf.Max(.1f,frameK);
                    bHeight = (coaster.ActiveData.helm.y+8.3f)/k;
                    bDist = 23f/k;
                    bAhead = 7f/k;
                    bLookH = 1.5f;
                }

                // Cruise is a LANDSCAPE move. Upright the preset is already
                // backed off and lifted, and easing further out from there
                // puts her at the bottom of a tall frame with nothing in it.
                float cruise = cruiseLevel * (1f - portrait01);
                // The dolly scales the SEAT only — distance and height
                // together, so the tilt barely moves and the horizon stays
                // where it was put.
                float zoomK = Mathf.Lerp(1f, dolly, portrait01);

                float calmBack = (bDist + cruiseDistance * cruise) * frameK * zoomK;
                float calmUp = (bHeight + cruiseHeight * cruise) * frameK * zoomK;
                float back = calmBack - stormPullIn * stormLevel;
                float up = calmUp - stormDrop * stormLevel;
                float ahead = (bAhead + cruiseLookAhead * cruise) * frameK;

                // V2: the pitch is chosen, not implied by the seat numbers.
                // Upright it is camPitchDeg; on the desk it stays what the
                // seat already gave. The rise adds pitch and backs off, and
                // the height is derived from the pitch once the lock below has
                // had its say about `back`, so pulling back never flattens it.
                bool v2Seat = styleV2 && !SailOverride.HasValue;
                float lookRel = bLookH * frameK;
                float pitchTan = 0f;
                if (v2Seat)
                {
                    float authored = Mathf.Atan2(calmUp - lookRel,
                        Mathf.Max(1f, calmBack + ahead)) * Mathf.Rad2Deg;
                    float pitch = Mathf.Lerp(authored, SeaSick.Ship.JuiceTuning.camPitchDeg, portrait01)
                        + Mathf.Max(0f, SeaSick.Ship.JuiceTuning.camRisePitchDeg) * need01;
                    // Capped against the lens WITHOUT the speed boost: the
                    // boost only widens it, so the horizon only gets safer.
                    float lens = sailFov > 0f ? sailFov : fovBase;
                    pitch = Mathf.Clamp(pitch, 2f, Mathf.Max(authored, HorizonPitchCap(lens)));
                    pitchTan = Mathf.Tan(pitch * Mathf.Deg2Rad);
                    back = calmBack * (1f + Mathf.Max(0f, SeaSick.Ship.JuiceTuning.camRiseBackFraction) * need01)
                         - stormPullIn * stormLevel;

                    // How far she may sit off centre and stay whole on
                    // screen: the lens's half-width at the real aspect, less
                    // the half-angle her hull subtends from the seat.
                    float aspect = cam != null ? Mathf.Max(0.2f, cam.aspect) : PortraitAspect;
                    float halfW = Mathf.Atan(Mathf.Tan(lens * 0.5f * Mathf.Deg2Rad) * aspect) * Mathf.Rad2Deg;
                    float hull = motor != null ? motor.HullLength : framingLoa;
                    float hullHalf = Mathf.Atan2(0.3f * hull, Mathf.Max(5f, back)) * Mathf.Rad2Deg;
                    keepOffDeg = Mathf.Max(3f, halfW - hullHalf);
                }

                anchor = shipFlat;
                Vector3 sternDir = -flatForward;
                // Show the carved side and working deck while preserving the horizon.
                // Existing damping follows heading; combat framing retains its own orbit.
                if (!SailOverride.HasValue && coaster != null && coaster.IsCoaster)
                    sternDir = Quaternion.AngleAxis(orbitAngle * (1f - lockLevel), Vector3.up) * sternDir;

                float lockBlend = 0f, lockAim = 0f;
                if (lockLevel > 0.001f && (HasLockAim || haveLockPos) && v2Seat)
                {
                    Vector3 lockPos = HasLockAim ? LockAimPos : lastLockPos;
                    Vector3 tgt = new Vector3(lockPos.x, 0f, lockPos.z);
                    Vector3 toTarget = tgt - shipFlat;
                    float sep = toTarget.magnitude;
                    Vector3 dirToTarget = sep < 0.5f ? flatForward : toTarget / sep;
                    lockBlend = lockLevel;

                    // V2: stay behind her. The old swing (up to 140 deg
                    // upright) could put the lens ahead of her bow. That
                    // once mirrored the stick; steering is boat-relative now
                    // (DREDGE step 1), so keeping behind her is a framing
                    // choice: she stays the subject. A small swing toward sitting
                    // opposite the target turns the view toward its side and
                    // keeps it within camLockSwingDeg of her bow. Scaled by
                    // how far off her side the target is: dead ahead needs no
                    // swing, and dead astern cannot be framed from behind her
                    // and would flip the swing side every frame.
                    Vector3 flatRight = Vector3.Cross(Vector3.up, flatForward);
                    float offSide = Mathf.InverseLerp(0f, 0.5f, Mathf.Abs(Vector3.Dot(dirToTarget, flatRight)));
                    float sternAz = Mathf.Atan2(sternDir.x, sternDir.z) * Mathf.Rad2Deg;
                    float awayAz = Mathf.Atan2(-dirToTarget.x, -dirToTarget.z) * Mathf.Rad2Deg;
                    float swingMax = Mathf.Max(0f, SeaSick.Ship.JuiceTuning.camLockSwingDeg);
                    float az = sternAz + Mathf.Clamp(
                        Mathf.DeltaAngle(sternAz, awayAz), -swingMax, swingMax) * offSide;
                    Vector3 swung = new Vector3(
                        Mathf.Sin(az * Mathf.Deg2Rad), 0f, Mathf.Cos(az * Mathf.Deg2Rad));
                    sternDir = Vector3.Slerp(sternDir, swung, lockLevel);

                    // Back off until both hulls fit across the frame with the
                    // aim on the midpoint (each `keepOffDeg` from centre),
                    // capped so she stays readable: past the cap the target
                    // may leave the frame, she may not.
                    Vector3 view = -sternDir;
                    Vector3 viewRight = Vector3.Cross(Vector3.up, view);
                    float xT = Vector3.Dot(toTarget, viewRight);
                    float fT = Vector3.Dot(toTarget, view);
                    float span = Mathf.Clamp(2f * keepOffDeg, 5f, 80f);
                    float needBack = Mathf.Abs(xT) / Mathf.Tan(span * Mathf.Deg2Rad) - fT;
                    float maxBack = Mathf.Max(back, SeaSick.Ship.JuiceTuning.camLockMaxBack);
                    back = Mathf.Lerp(back, Mathf.Clamp(needBack, back, maxBack), lockLevel);

                    // Aim at the midpoint, clamped so she stays in frame. A
                    // target level with the seat or behind it fades to no aim.
                    float depth = back + fT;
                    float bearing = Mathf.Atan2(xT, Mathf.Max(0.1f, depth)) * Mathf.Rad2Deg
                                  * Mathf.InverseLerp(0f, 10f, depth);
                    lockAim = SeaSick.Ship.JuiceTuning.SoftLimit(bearing * 0.5f + turnLead, keepOffDeg);
                }
                else if (lockLevel > 0.001f && HasLockAim)
                {
                    Vector3 lockPos = LockAimPos;
                    Vector3 tgt = new Vector3(lockPos.x, 0f, lockPos.z);
                    Vector3 toTarget = tgt - shipFlat;
                    float sep = toTarget.magnitude;
                    Vector3 dirToTarget = sep < 0.5f ? flatForward : toTarget / sep;

                    // Swing toward sitting opposite the target, clamped so she
                    // stays the subject (a framing cap; the boat-relative
                    // stick no longer cares where the camera sits).
                    float sternAz = Mathf.Atan2(sternDir.x, sternDir.z) * Mathf.Rad2Deg;
                    float awayAz = Mathf.Atan2(-dirToTarget.x, -dirToTarget.z) * Mathf.Rad2Deg;
                    float swingMax = Mathf.Lerp(lockMaxSwingDeg, lockMaxSwingDegPortrait,
                                                Mathf.Max(0f, portrait01));
                    float az = sternAz + Mathf.Clamp(
                        Mathf.DeltaAngle(sternAz, awayAz), -swingMax, swingMax);

                    Vector3 swung = new Vector3(
                        Mathf.Sin(az * Mathf.Deg2Rad), 0f, Mathf.Cos(az * Mathf.Deg2Rad));
                    sternDir = Vector3.Slerp(sternDir, swung, lockLevel);

                    // Back off enough to hold both hulls, and bias the look
                    // point toward the enemy — but only partly, so your own
                    // ship never leaves the frame.
                    float fit = Mathf.Min(sep * lockPullPerMetre,
                                          lockMaxPull * frameK) * lockLevel;
                    back += fit;
                    up += fit * 0.42f;
                    anchor = shipFlat + dirToTarget * (sep * lockBias * lockLevel);
                }

                if (v2Seat) up = lookRel + (back + ahead) * pitchTan - stormDrop * stormLevel;
                desired = shipFlat + sternDir * back + Vector3.up * up;
                Vector3 lookAheadDir = !SailOverride.HasValue && coaster != null && coaster.IsCoaster
                    ? Quaternion.AngleAxis(orbitAngle * (1f - lockLevel), Vector3.up) * flatForward
                    : flatForward;
                if (v2Seat)
                {
                    // Plain chase: through her to `ahead` past her, leaned
                    // into the turn by turning the aim about the seat. The
                    // coaster's sailing path re-aims along its radius below
                    // and takes the lean in its yaw filter instead.
                    Vector3 seatFlat = shipFlat + sternDir * back;
                    Vector3 plain = shipFlat + lookAheadDir * ahead;
                    if (!sailingQuarter)
                        plain = seatFlat + Quaternion.AngleAxis(
                            SeaSick.Ship.JuiceTuning.SoftLimit(turnLead, keepOffDeg), Vector3.up) * (plain - seatFlat);
                    Vector3 lockLook = seatFlat + Quaternion.AngleAxis(lockAim, Vector3.up)
                                     * (-sternDir * (back + ahead));
                    lookPoint = Vector3.Lerp(plain, lockLook, lockBlend)
                              + Vector3.up * (lookRel + seaY);
                }
                else
                    lookPoint = anchor + lookAheadDir * (ahead * (1f - lockLevel))
                              + Vector3.up * (bLookH * frameK + seaY);

                // The player's look offset, on top of all of the above.
                ApplyLook(ref desired, ref lookPoint, shipFlat, flatForward, seaY);
            }

            // --- the SeaFocus composition, over the chase/lock shot ----------
            // Only when SeaFocus is set and nothing outranks it (a real lock,
            // the shore party). With SeaFocus null and the level home, this
            // whole block is skipped and the frame is exactly what it was.
            {
                bool seaOnly = SeaFocus.HasValue
                    && (LockTarget == null || (SeaFocusLockAlias != null && LockTarget == SeaFocusLockAlias))
                    && !PointOfInterest.HasValue && !SailOverride.HasValue;
                if (seaOnly) { lastSeaPos = SeaFocus.Value; haveSeaPos = true; }
                if (seaOnly || seaLevel > 0f)
                {
                    seaLevel = SeaSick.Ship.JuiceTuning.Spring(seaLevel, seaOnly ? 1f : 0f,
                        ref seaLevelVel, Mathf.Max(0.05f, SeaSick.Ship.JuiceTuning.camLockSeconds * 1.6f), dt);
                    if (seaLevel < 0.001f && !seaOnly) { seaLevel = 0f; seaLevelVel = 0f; }
                    seaLevel = Mathf.Clamp01(seaLevel);
                }
                if (seaLevel > 0f && haveSeaPos)
                {
                    float aspect = cam != null ? Mathf.Max(0.2f, cam.aspect) : PortraitAspect;
                    // Composed WITHOUT the boost lens: its kick would otherwise
                    // re-solve the seat; drawn wider, both still fit.
                    ComposeSeaFocus(shipFlat, target.forward, lastSeaPos, sailLens - boostFov, aspect,
                        out Vector3 seaSeat, out Vector3 seaLook);
                    desired = Vector3.Lerp(desired, seaSeat, seaLevel);
                    lookPoint = Vector3.Lerp(lookPoint, seaLook + Vector3.up * seaY, seaLevel);
                }
                else if (!seaOnly && seaLevel <= 0f) haveSeaPos = false;
            }

            // --- the island overview, blended over whatever was framed ----
            var shot = OverviewOverride ?? Overview;
            float wantOverview = shot.HasValue ? 1f : 0f;
            overviewLevel = Mathf.Lerp(overviewLevel, wantOverview,
                1f - Mathf.Exp(-overviewResponse * dt));

            // **The blend has to ARRIVE, not approach.**
            //
            // An exponential never reaches 1, and "the view has finished
            // coming up" is a fact three separate things now need to know: a
            // grab that takes the rig off its filters, a screen ray composed
            // against the overview's own pose, and the input layer deciding
            // whether it is live at all. At 0.98 the seat is still 2% of the
            // way back down toward the sailing shot — on a normal island
            // that is five metres — so honouring `direct` there would jump
            // the rig by five metres the instant a hand touched the land.
            //
            // Closing the last 2% here instead costs nothing to look at: the
            // position low pass is still running (`direct` cannot be on yet,
            // because nothing can gesture before the view is up) and it eases
            // those five metres away over about half a second, which is what
            // it was already doing to the two hundred metres before them. The
            // descent is untouched — this only fires on the way UP.
            if (wantOverview > 0.5f && overviewLevel > 0.98f) overviewLevel = 1f;

            // `direct` is honoured the frame it is asked for. The first
            // version made it WAIT until the rig had eased to within half a
            // metre of its seat, and `IslandCamProbe` measured what that does:
            // the view reports ready when the BLEND finishes (1.7 s), the
            // position low pass is still tens of metres short of the seat at
            // that moment, and a hand that grabs then moves the seat every
            // frame -- so the rig never gets within half a metre of it, the
            // flag never latches, and the whole drag goes through the filter
            // (the held point strayed 23 % of the screen). A wait on a target
            // that the waiting-for thing keeps moving is not a wait.
            bool direct = shot.HasValue && shot.Value.direct && overviewLevel >= 1f;

            if (overviewLevel > 0.001f && shot.HasValue)
            {
                var ov = shot.Value;
                float vfov = Mathf.Lerp(sailLens, OverviewFov, overviewLevel);
                float tanHalf = Mathf.Tan(vfov * 0.5f * Mathf.Deg2Rad);

                // The zoom is authored as METRES OF GROUND up the frame, not
                // as a distance: distance means nothing without the lens, and
                // the lens changed. Falls back to fitting `radius` when no
                // coverage is set.
                float ground = ov.ground > 0.01f
                    ? ov.ground
                    : (overviewGroundMetres > 0.01f
                        ? overviewGroundMetres
                        : ov.radius * overviewMargin * 2f);

                // THE SHOT MUST CONTAIN THE SHIP -- BY MOVING, NOT BY
                // BACKING OFF.
                //
                // The composition puts her near the edge on purpose: the frame
                // is centred inland of the pier root and off to starboard, so
                // the pier runs in from the corner with her on it. "Near the
                // edge" is a hair from "outside it", and a longer pier at a new
                // island was enough -- she measured INSIDE the wedge
                // Dock.ViewHalfWidth documents and still came out at viewport
                // x = 1.12, off the screen.
                //
                // The first fix widened the coverage until she fitted. It
                // worked and it was wrong: coverage IS the zoom, so buying her
                // way into frame cost legibility everywhere else -- the span
                // went 165 m to 305 m and a crew member fell to 20 px, which
                // is unreadable. Slide the frame CENTRE toward her instead and
                // the zoom is untouched. Only widen if she still will not fit,
                // which for a sane pier she will.
                Vector3 aim = ov.centre;
                float span = ground / (2f * tanHalf);
                // ...but never so far that the people stop reading. When the
                // ground to cover is bigger than legibility allows,
                // legibility wins and the frame holds the middle of it: an
                // overview you cannot pick a person out of is a map, and the
                // player already has a minimap.
                if (!ov.free) span = Mathf.Min(span, ReadableDistance(vfov));
                // Never so far back that the far clip cannot draw the ground.

                if (ov.span > 0.01f) span = ov.span;          // the tuner says exactly
                LastOverviewSpan = span;
                CurrentSpan = span;
                float tiltDeg = ov.tiltDeg > 0.01f ? ov.tiltDeg : overviewTilt;
                CurrentTilt = tiltDeg;

                // The triangle itself is `OverviewPose`, shared with
                // `IslandCam` so the pose it aims its screen rays at and the
                // pose that is drawn cannot be two different pieces of
                // arithmetic. The resolution ABOVE stays here: the legibility
                // clamp and the authored-tilt fallback both read fields that
                // belong to this component.
                var resolved = ov;
                resolved.span = span;
                resolved.tiltDeg = tiltDeg;
                OverviewPose(resolved, vfov, out Vector3 seat, out Quaternion seatRot);

                // Pull the frame over until she is actually inside it.
                //
                // Done by PROJECTING her, not by estimating. The first attempt
                // worked the shift out on the ground plane from half-widths in
                // metres, and it under-corrected every time because the shot is
                // tilted 32 degrees and she sits 70 m nearer the lens than the
                // aim point -- so neither "half the coverage" nor the aim
                // plane's scale describes the frame where she actually is.
                // Projecting costs a dot product and is exact.
                //
                // The NDC is computed by hand rather than with
                // Camera.WorldToViewportPoint, because that uses cam.aspect --
                // the editor's landscape Game view -- and the whole point is to
                // compose for the phone.
                if (overviewHoldsShip && !ov.free && target != null && overviewLevel > 0.001f)
                {
                    float aspect = Mathf.Min(
                        Mathf.Max(0.2f, cam != null ? cam.aspect : 1f), narrowestAspect);
                    Quaternion rot = seatRot;
                    Vector3 f2 = rot * Vector3.forward, r2 = rot * Vector3.right, u2 = rot * Vector3.up;
                    Vector3 rel = target.position - seat;
                    float z = Vector3.Dot(rel, f2);
                    if (z > 1f)
                    {
                        // Half the frame, in metres, AT HER DEPTH.
                        float halfH = z * tanHalf;
                        float halfW = halfH * aspect;
                        float x = Vector3.Dot(rel, r2), y = Vector3.Dot(rel, u2);
                        float overX = Mathf.Abs(x) + overviewShipMargin - halfW;
                        float overY = Mathf.Abs(y) + overviewShipMargin - halfH;
                        Vector3 shift = Vector3.zero;
                        if (overX > 0f) shift += r2 * (Mathf.Sign(x) * overX);
                        if (overY > 0f) shift += u2 * (Mathf.Sign(y) * overY);
                        if (shift.sqrMagnitude > 1e-4f)
                        {
                            // Move the whole shot, seat and aim together, so
                            // the angle Kevin chose is preserved exactly and
                            // only the framing slides.
                            aim += shift;
                            seat += shift;
                        }
                    }
                }
                LastOverviewSeat = seat;
                LastOverviewAim = aim;
                desired = Vector3.Lerp(desired, seat, overviewLevel);
                LastDesired = desired;
                // The AIM, not the authored centre: the seat was built from
                // the shifted point, so looking at the unshifted one aims the
                // camera off its own framing.
                lookPoint = Vector3.Lerp(lookPoint, aim, overviewLevel);
                // Nothing up there rides the swell.
                seaY *= 1f - overviewLevel;

                // The far clip is 600 m, which is right for a camera sitting
                // twenty metres above the water and wrong for one three
                // hundred metres up: the first overview rendered the island
                // correctly and cut the sky off into a flat grey band across
                // the top of the frame, because the sky dome is further away
                // than the water ever is.
                if (cam != null)
                {
                    if (baseFarClip < 0f) baseFarClip = cam.farClipPlane;
                    float need = span + ov.radius * 2f + 900f;
                    cam.farClipPlane = Mathf.Lerp(baseFarClip, Mathf.Max(baseFarClip, need),
                        overviewLevel);
                }
            }
            else if (cam != null && baseFarClip > 0f && cam.farClipPlane != baseFarClip)
                cam.farClipPlane = baseFarClip;

            if (cam != null)
                cam.fieldOfView = Mathf.Lerp(sailLens, OverviewFov, overviewLevel);

            if (!rigSeeded) { rigPos = transform.position; rigSeeded = true; }
            // `direct` is k = 1: the seat, verbatim. Written into `rigPos`
            // rather than round it, so letting go of the land resumes the
            // filter from where the hand left it instead of snapping back to
            // a position the low pass never reached.
            //
            // **What is left of the old gap rides along as an OFFSET and
            // decays on its own.** The frame a hand takes hold, the rig is
            // wherever the filter had got it to -- maybe metres from the seat.
            // Jumping there is a pop; easing there is the lag `direct` exists
            // to remove. So the rig is `seat + offset`: it moves rigidly with
            // the seat from the first frame (the land follows the hand 1:1)
            // while the offset shrinks to nothing underneath, at a rate that
            // does not depend on what the hand is doing.
            if (direct)
            {
                if (!wasDirect) directOffset = rigPos - desired;
                directOffset *= Mathf.Exp(-directSettle * dt);
                if (directOffset.sqrMagnitude < 1e-6f) directOffset = Vector3.zero;
                rigPos = desired + directOffset;
            }
            else if (v2Follow && dt > 0f)
            {
                // V2: ride her translation exactly (so at 12 m/s she cannot
                // trail off the frame) and spring only the seat's offset from
                // her -- its swing round her in a turn, the dolly, the rise,
                // the lock backing off. A spring, so a start or stop of any of
                // those eases in and out instead of starting at full speed.
                if (!wasV2Follow)
                {
                    // Taking over from the lerp: keep the speed the seat
                    // already had relative to her, so the hand-off is seamless.
                    seatVel = wasRigTracked
                        ? ((rigPos - prevRigPos) - (shipFlat - prevShipFlat)) / dt
                        : Vector3.zero;
                }
                Vector3 offset = rigPos - shipFlat;
                Vector3 wantOffset = desired - shipFlat;
                float seatT = Mathf.Max(0f, SeaSick.Ship.JuiceTuning.camSeatSeconds);
                offset = SeaSick.Ship.JuiceTuning.Spring(offset, wantOffset, ref seatVel, seatT, dt);
                rigPos = shipFlat + offset;
            }
            else rigPos = Vector3.Lerp(rigPos, desired, 1f - Mathf.Exp(-(sailingQuarter ? 3.5f : positionResponse) * dt));
            if (dt > 0f)
            {
                wasV2Follow = v2Follow && !direct;
                prevRigPos = rigPos;
                prevShipFlat = shipFlat;
                wasRigTracked = true;
            }

            OverviewSettled = overviewLevel >= 1f && shot.HasValue
                && (direct || (rigPos - desired).magnitude < 0.02f * Mathf.Max(10f, CurrentSpan));

            // Heave is added AFTER the framing lerp rather than folded into the
            // target. Through the lerp it becomes a second low pass stacked on
            // the one that produced it, and the rig lags the very swell it is
            // meant to be riding — measured as the ship still swimming a fifth
            // of the screen height in a storm.
            //
            // The clamps below are display-time corrections and deliberately do
            // NOT feed back into rigPos, so being shoved up by a crest never
            // drags the framing with it.
            // The juice drop, faded out by the overview. Limited so the seat
            // never asks to go under twice the water floor; the floor clamp
            // just below is still the hard guarantee against the real sea.
            float atSea = 1f - overviewLevel;
            float drop = Mathf.Min(juiceDrop * atSea,
                Mathf.Max(0f, rigPos.y - 2f * minHeightAboveWater));
            transform.position = rigPos + Vector3.up * (seaY - drop);

            // A low camera sells speed, but it must never end up underwater.
            if (SeaSick.Ocean.OceanSampler.Ready)
            {
                Vector3 cp = transform.position;
                float surface = SeaSick.Ocean.OceanSampler.SampleImmediate(cp).height;
                if (cp.y < surface + minHeightAboveWater)
                    transform.position = new Vector3(cp.x, surface + minHeightAboveWater, cp.z);
            }

            // Islands are mountains now, and the camera sits well behind the
            // ship — close in to a shore it would otherwise end up inside the
            // hill, filling the screen with green.
            var isle = SeaSick.World.Island.Nearest(transform.position);
            if (isle != null)
            {
                Vector3 cp = transform.position;
                Vector3 d = cp - isle.transform.position;
                d.y = 0f;
                float dist = d.magnitude;
                float ang = Mathf.Atan2(d.x, d.z);
                if (dist < isle.RadiusAt(ang))
                {
                    // **Which ground, and how much air over it.**
                    //
                    // The 46-sector profile is what every shot before the
                    // hands-on view was clamped against, and it stays the
                    // answer for all of them. A shot that asks for a
                    // `clearance` gets the HEIGHT FIELD instead — the same
                    // function the picks and `IslandCam`'s own terrain yield
                    // read, so the yield and this net cannot disagree about
                    // where the hill is and fight each other for the lens.
                    //
                    // `GroundPick.Height` is a non-serialisable static and a
                    // play-mode recompile nulls it. Falling through to the
                    // profile is the right answer then, not an exception.
                    float want = shot.HasValue ? shot.Value.clearance : 0f;
                    var height = want > 0f ? GroundPick.Height : null;
                    float clear = height != null
                        ? Mathf.Lerp(terrainClearance, want, overviewLevel)
                        : terrainClearance;
                    float ground = height != null
                        ? height(cp.x, cp.z)
                        : isle.SurfacePoint(ang, dist).y;
                    if (cp.y < ground + clear)
                        transform.position = new Vector3(cp.x, ground + clear, cp.z);
                }
            }
            if (sailingQuarter)
            {
                // Aim along the actual orbit radius, not the future desired seat.
                // Otherwise heading lag sends the hull sideways out of a portrait frame.
                Vector3 radial = shipFlat - transform.position; radial.y = 0f;
                float aimDistance = Vector3.ProjectOnPlane(lookPoint - shipFlat, Vector3.up).magnitude;
                Vector3 aim = shipFlat + radial.normalized * aimDistance;
                lookPoint = new Vector3(aim.x, lookPoint.y, aim.z);
            }
            Quaternion desiredRot = Quaternion.LookRotation(lookPoint - transform.position, Vector3.up);
            // Last frame's juice roll comes off first, so the rotation filter
            // runs on the un-rolled pose and the roll never accumulates.
            Quaternion unrolled = transform.rotation * Quaternion.Inverse(appliedRumble)
                                  * Quaternion.Inverse(appliedShake)
                                  * Quaternion.Euler(0f, 0f, -appliedRoll);
            // The rotation's leftover is carried the same way as the position's.
            Quaternion framed;
            if (direct)
            {
                if (!wasDirect) directTurn = Quaternion.Inverse(desiredRot) * unrolled;
                directTurn = Quaternion.Slerp(directTurn, Quaternion.identity,
                    1f - Mathf.Exp(-directSettle * dt));
                framed = desiredRot * directTurn;
            }
            else framed = Quaternion.Slerp(unrolled, desiredRot,
                    1f - Mathf.Exp(-rotationResponse * dt));
            // Pitch keeps its swell smoothing; yaw is filtered on its own.
            // 2026-09-29: it used to snap to the direct-at-ship yaw, which pinned
            // her to screen centre and panned the whole world at the hull's turn
            // rate (Kevin: "too rough around the edges and almost too much
            // movement"). Now the aim eases after her with camYawLagSeconds, so
            // she drifts a little off centre in a turn; camMaxOffCentreDeg keeps
            // her inside the portrait frame (half-FOV ~17-20 deg) on a hard one.
            if (v2Follow && !direct)
            {
                // V2: yaw and pitch are springs, so the aim eases into a turn
                // and out of it -- the old lag changed speed the instant she
                // did, and its clamp stopped it dead against a wall.
                //
                // Sailing quarter: the aim trails the line to her by the yaw
                // spring (camYawLagSeconds, the same trail in a steady turn
                // as the old lag) and the lean pulls it back in. Anywhere
                // else (a lock, a non-coaster hull) it springs to the framed
                // look, which already carries the lean and the lock aim.
                // Either way how far she may sit off centre is a SOFT limit
                // about the line to her, so nearing it bends, never bumps.
                Vector3 toShip = target.position - transform.position; toShip.y = 0f;
                float directYaw = toShip.sqrMagnitude > 1e-4f
                    ? Mathf.Atan2(toShip.x, toShip.z) * Mathf.Rad2Deg
                    : desiredRot.eulerAngles.y;
                // The yaw spring is fed her line PLUS her yaw rate x the spring
                // time (velocity feed-forward), so in a steady turn it adds no
                // lag of its own -- the seat spring's swing is the one trail,
                // the same framing the old lag+lean pair netted out to -- but
                // every start and stop is eased.
                float yawT = Mathf.Max(0f, SeaSick.Ship.JuiceTuning.camYawLagSeconds);
                float feedForward = yawRateSm * yawT;
                float wantYaw, limit;
                if (sailingQuarter)
                {
                    float maxOff = Mathf.Max(0f, SeaSick.Ship.JuiceTuning.camMaxOffCentreDeg);
                    wantYaw = directYaw + feedForward + quarterLead;
                    limit = Mathf.Min(
                        Mathf.Max(maxOff, Mathf.Max(0f, SeaSick.Ship.JuiceTuning.camTurnLeadDeg)), keepOffDeg);
                }
                else
                {
                    wantYaw = desiredRot.eulerAngles.y + feedForward;
                    limit = keepOffDeg < 89f ? keepOffDeg + 2f : 89f;
                }
                float wantPitch = Mathf.DeltaAngle(0f, desiredRot.eulerAngles.x);
                if (!v2RotSeeded)
                {
                    float y0 = unrolled.eulerAngles.y, p0 = Mathf.DeltaAngle(0f, unrolled.eulerAngles.x);
                    v2Yaw = y0; v2Pitch = p0;
                    // Start at the speed already on screen, not from rest.
                    yawVel = haveDrawn ? drawnYawRate : 0f;
                    pitchVel = haveDrawn ? drawnPitchRate : 0f;
                    v2RotSeeded = true;
                }
                v2Yaw = SeaSick.Ship.JuiceTuning.SpringAngle(v2Yaw, wantYaw, ref yawVel, yawT, dt);
                v2Pitch = SeaSick.Ship.JuiceTuning.SpringAngle(v2Pitch, wantPitch, ref pitchVel,
                    Mathf.Max(0f, SeaSick.Ship.JuiceTuning.camPitchSeconds), dt);
                float shownYaw = directYaw + SeaSick.Ship.JuiceTuning.SoftLimit(
                    Mathf.DeltaAngle(directYaw, v2Yaw), limit);
                framed = Quaternion.Euler(v2Pitch, shownYaw, 0f);
                // The yaw filter below re-seeds from the drawn pose if V2 is
                // switched off mid-voyage.
                sailYawSeeded = false;
            }
            else if (sailingQuarter)
            {
                v2RotSeeded = false;
                float directYaw = desiredRot.eulerAngles.y;
                if (!sailYawSeeded) { sailYaw = transform.rotation.eulerAngles.y; sailYawSeeded = true; }
                float tau = Mathf.Max(0f, SeaSick.Ship.JuiceTuning.camYawLagSeconds);
                float k = tau <= 1e-4f ? 1f : 1f - Mathf.Exp(-dt / tau);
                sailYaw = Mathf.LerpAngle(sailYaw, directYaw, k);
                float maxOff = Mathf.Max(0f, SeaSick.Ship.JuiceTuning.camMaxOffCentreDeg);
                sailYaw = directYaw + Mathf.Clamp(Mathf.DeltaAngle(directYaw, sailYaw), -maxOff, maxOff);
                float shownYaw = sailYaw;
                // V2: the lean rides on top of the lag rather than through it,
                // so the rudder still moves the view first. The lag alone
                // trails her to the OUTSIDE of a turn; the lean pulls it back
                // in, inside a limit that keeps her whole on a portrait frame.
                if (styleV2)
                {
                    float limit = Mathf.Min(
                        Mathf.Max(maxOff, Mathf.Max(0f, SeaSick.Ship.JuiceTuning.camTurnLeadDeg)), keepOffDeg);
                    shownYaw = directYaw + Mathf.Clamp(
                        Mathf.DeltaAngle(directYaw, sailYaw) + turnLead, -limit, limit);
                }
                framed = Quaternion.Euler(framed.eulerAngles.x, shownYaw, 0f);
            }
            else { sailYawSeeded = false; v2RotSeeded = false; }
            if (dt > 0f)
            {
                float fy = framed.eulerAngles.y, fp = Mathf.DeltaAngle(0f, framed.eulerAngles.x);
                if (haveDrawn)
                {
                    drawnYawRate = Mathf.DeltaAngle(lastDrawnYaw, fy) / dt;
                    drawnPitchRate = Mathf.DeltaAngle(lastDrawnPitch, fp) / dt;
                }
                lastDrawnYaw = fy; lastDrawnPitch = fp; haveDrawn = true;
            }
            appliedRoll = sailingYard != null && sailingYard.IsCoaster ? 0f : juiceRoll * atSea;
            transform.rotation = framed * Quaternion.Euler(0f, 0f, appliedRoll);
            ApplyShake(dt);
            ApplyRumble();

            OverviewDirect = direct && directOffset == Vector3.zero
                             && Quaternion.Angle(directTurn, Quaternion.identity) < 0.01f;
            wasDirect = direct;
        }

        /// The shake on top of the finished pose. Position: added after the
        /// framing and recomputed from `rigPos` next frame, so it never feeds
        /// back. Rotation: remembered and taken off next frame before the
        /// rotation filter reads the pose (see `unrolled`).
        void ApplyShake(float dt)
        {
            if (shake <= 0.001f)
            {
                shake = 0f;
                appliedShake = Quaternion.identity;
                return;
            }
            float t = Time.time * 23f + shakeSeed;
            float nx = Mathf.PerlinNoise(t, 0.1f) * 2f - 1f;
            float ny = Mathf.PerlinNoise(0.3f, t) * 2f - 1f;
            float nz = Mathf.PerlinNoise(t * 0.7f, 5.1f) * 2f - 1f;
            float k = shake * shake;
            transform.position += (transform.right * nx + transform.up * ny) * (0.55f * k);
            appliedShake = Quaternion.Euler(ny * 1.1f * k, nx * 1.1f * k, nz * 1.6f * k);
            transform.rotation *= appliedShake;
            if (dt > 0f) shake *= Mathf.Exp(-5.5f * dt);
        }

        /// The boost's sustained rumble, after the shake and taken off the same
        /// way next frame (`unrolled`). Position plus a hair of pitch/yaw, NO
        /// roll (a rolling horizon is the sick one), at `BoostTuning.rumbleHz`.
        void ApplyRumble()
        {
            float r = boostRumble * (1f - overviewLevel);
            if (r <= 0.0005f) { appliedRumble = Quaternion.identity; return; }
            if (rumbleSeed == 0f) rumbleSeed = Random.Range(101f, 200f);
            float t = Time.time * Mathf.Max(0.1f, SeaSick.Ship.BoostTuning.rumbleHz) + rumbleSeed;
            float nx = Mathf.PerlinNoise(t, 0.7f) * 2f - 1f;
            float ny = Mathf.PerlinNoise(1.9f, t) * 2f - 1f;
            transform.position += (transform.right * nx + transform.up * ny) * (0.55f * r);
            appliedRumble = Quaternion.Euler(ny * 1.1f * r, nx * 1.1f * r, 0f);
            transform.rotation *= appliedRumble;
        }

        /// The SeaFocus shot: seat and look point (sea at y 0, no heave).
        /// See `SeaFocusHeight`. Exact pinhole arithmetic in the vertical
        /// plane of the view, at the camera's own lens and aspect.
        void ComposeSeaFocus(Vector3 shipFlat, Vector3 shipForward, Vector3 focus,
            float vfov, float aspect, out Vector3 seat, out Vector3 look)
        {
            Vector3 fwd = new Vector3(shipForward.x, 0f, shipForward.z);
            fwd = fwd.sqrMagnitude < 1e-4f ? Vector3.forward : fwd.normalized;
            Vector3 k = new Vector3(focus.x, 0f, focus.z);
            Vector3 toK = k - shipFlat;
            float bowAz = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            float kAz = toK.sqrMagnitude > 1f ? Mathf.Atan2(toK.x, toK.z) * Mathf.Rad2Deg : bowAz;
            float swing = Mathf.Clamp(SeaFocusSwingDeg, 0f, 170f);
            float az = bowAz + Mathf.Clamp(Mathf.DeltaAngle(bowAz, kAz), -swing, swing);
            Vector3 view = new Vector3(Mathf.Sin(az * Mathf.Deg2Rad), 0f, Mathf.Cos(az * Mathf.Deg2Rad));
            Vector3 right = Vector3.Cross(Vector3.up, view);

            float tanV = Mathf.Tan(Mathf.Clamp(vfov, 10f, 120f) * 0.5f * Mathf.Deg2Rad);
            float tanH = tanV * aspect;
            float fK = Vector3.Dot(toK, view);
            float xK = Vector3.Dot(toK, right);
            float hull = motor != null ? motor.HullLength : framingLoa;
            float deck = 2f;
            float shipY = Mathf.Clamp(SeaFocusShip01, 0.1f, 0.6f);
            float topY = Mathf.Clamp(SeaFocusTop01, shipY + 0.15f, 0.98f);
            float aShip = Mathf.Atan((2f * shipY - 1f) * tanV);       // angle above the axis
            float aTop = Mathf.Atan((2f * topY - 1f) * tanV);
            float gap = aTop - aShip;
            float tanE = Mathf.Tan(Mathf.Clamp(SeaFocusElevDeg, 5f, 60f) * Mathf.Deg2Rad);
            float height = Mathf.Max(0f, SeaFocusHeight);
            float radius = Mathf.Max(0f, SeaFocusRadius);

            // Closest seat that still shows her whole (a hull length and a
            // half of ground in front of the lens) ...
            float minBack = Mathf.Max(18f, hull * 1.6f);
            float maxBack = Mathf.Max(minBack, SeaFocusMaxBack);
            // ... the width: the focus's half-width (plus where it sits off
            // the view line) inside 95 % of the half-frame at its depth.
            float fitW = (Mathf.Abs(xK) + radius) / Mathf.Max(0.05f, tanH * 0.95f) - fK;
            // ... the height: the angle from her deck to its top must fit the
            // gap between the two screen lines. That angle shrinks as the
            // seat backs off, so bisect for the nearest seat that fits.
            float fitH = minBack;
            if (height > 0f && fK > 1f)
            {
                float lo = minBack, hi = maxBack;
                if (VerticalGap(hi, fK, height, deck, tanE) > gap) fitH = hi;
                else if (VerticalGap(lo, fK, height, deck, tanE) > gap)
                {
                    for (int i = 0; i < 18; i++)
                    {
                        float mid = 0.5f * (lo + hi);
                        if (VerticalGap(mid, fK, height, deck, tanE) > gap) lo = mid; else hi = mid;
                    }
                    fitH = hi;
                }
            }
            float d = Mathf.Clamp(Mathf.Max(minBack, fitW, fitH), minBack, maxBack);
            float h = deck + d * tanE;
            // Pitch so her deck lands on `shipY`.
            float aDeck = Mathf.Atan2(deck - h, d);                    // elevation of her deck
            float pitchUp = aDeck - aShip;                            // axis elevation, radians
            // Turn the aim toward the focus by half its bearing, held so she
            // stays inside the frame.
            float halfW = Mathf.Atan(tanH);
            float hullHalf = Mathf.Atan2(0.5f * hull, d);
            float bearing = Mathf.Atan2(xK, Mathf.Max(1f, d + fK)) * 0.5f;
            float aimYaw = Mathf.Clamp(bearing, -Mathf.Max(0f, halfW - hullHalf - 0.03f),
                Mathf.Max(0f, halfW - hullHalf - 0.03f));
            seat = shipFlat - view * d + Vector3.up * h;
            Vector3 dir = Quaternion.AngleAxis(aimYaw * Mathf.Rad2Deg, Vector3.up) * view;
            look = seat + (dir * Mathf.Cos(pitchUp) + Vector3.up * Mathf.Sin(pitchUp)) * (d + 10f);
        }

        /// Angle from her deck to the focus's top, seen from a seat `d` behind
        /// her at elevation `tanE`.
        static float VerticalGap(float d, float fK, float height, float deck, float tanE)
        {
            float h = deck + d * tanE;
            return Mathf.Atan2(height - h, d + fK) - Mathf.Atan2(deck - h, d);
        }
    }
}
