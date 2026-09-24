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
        [Header("Tap sailing framing")]
        [SerializeField] float pilotDistance = 24f;
        [SerializeField] float pilotHeight = 32f;
        [SerializeField] float pilotLookAhead = 7f;
        float pilotViewBlend;

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

        // The player's own zoom.
        //
        // A pinch while sailing, or the wheel on a desk. Deliberately
        // temporary: it DECAYS back to the authored framing over about
        // twenty seconds, so a look at something on the horizon is a look
        // and not a new camera the player has to undo. The island view has
        // its own pinch (`IslandInput`), so this stands down whenever
        // `IslandCam.Engaged`.
        [Header("Player zoom (pinch / wheel)")]
        [Range(0.2f, 1f)] [SerializeField] float userZoomMin = 0.6f;
        [Range(1f, 3f)] [SerializeField] float userZoomMax = 1.6f;
        [Tooltip("Seconds for a held zoom to fade back to the authored framing.")]
        [SerializeField] float userZoomDecay = 20f;
        [Tooltip("How much one wheel notch zooms, as a fraction.")]
        [SerializeField] float wheelZoomStep = 0.08f;

        // Lock framing.
        //
        // Frames you and the target together. The camera swings toward sitting
        // opposite the enemy, but the swing is CLAMPED off dead-astern: a
        // fully free orbit would invert the helm when a target crosses your
        // stern, and losing the steering is worse than losing sight of them.
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
        /// The player's own zoom, 1 = the authored framing.
        float userZoom = 1f;

        /// What the portrait blend is doing, for the tuner and the probe.
        public float Portrait01 => Mathf.Max(0f, portrait01);
        /// The seat multiplier the dolly and the player's pinch are asking for.
        public float ZoomScale => Mathf.Max(0f, userZoom)
            * Mathf.Lerp(1f, Mathf.Max(0f, dolly), Mathf.Max(0f, portrait01));

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
        public float OverviewFov => overviewFov;

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
            return span * 2f * Mathf.Tan(overviewFov * 0.5f * Mathf.Deg2Rad);
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
            float span = groundMetres / (2f * Mathf.Tan(overviewFov * 0.5f * Mathf.Deg2Rad));
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
        void OnEnable() => ET.EnhancedTouchSupport.Enable();
        void OnDisable() => ET.EnhancedTouchSupport.Disable();

        /// **The player's own zoom, and its decay.**
        ///
        /// A two-finger pinch while sailing, or the wheel on a desk. Both
        /// stand down while the island view is up — that view has its own
        /// pinch and a gesture must belong to exactly one camera.
        ///
        /// Touches that BEGAN on a HUD control are ignored: the sheet covers
        /// a third of the screen and a two-finger scroll inside it is not a
        /// request to move the camera. `UIBlocker.Blocked` wants Input System
        /// screen space (origin bottom-left) and flips to GUI space itself,
        /// so `startScreenPosition` goes in unmodified.
        void PlayerZoom(float dt)
        {
            float mul = 1f;
            // Not under the tuner either: `SailCamTuner` reads the same wheel
            // to fly the seat, and two things zooming one camera off one
            // notch is a camera nobody is steering.
            if (!IslandCam.Engaged && !SailOverride.HasValue
                && !SeaSick.Ship.SailingPilot.BlocksZoom)
            {
                var touches = ET.Touch.activeTouches;
                if (touches.Count == 2)
                {
                    var a = touches[0];
                    var b = touches[1];
                    if (!SeaSick.UI.UIBlocker.Blocked(a.startScreenPosition)
                        && !SeaSick.UI.UIBlocker.Blocked(b.startScreenPosition))
                    {
                        Vector2 a1 = a.screenPosition, b1 = b.screenPosition;
                        // `zoomFactor` is old separation over new, so fingers
                        // spreading give a factor under 1 — which is exactly
                        // what a seat multiplier wants: spread = come closer.
                        TwoFinger.Solve(a1 - a.delta, b1 - b.delta, a1, b1, Screen.height,
                            out float zf, out _, out _, out _);
                        mul *= zf;
                    }
                }

                var mouse = Mouse.current;
                if (mouse != null)
                {
                    float w = mouse.scroll.ReadValue().y;
                    if (Mathf.Abs(w) > 0.01f
                        && !SeaSick.UI.UIBlocker.Blocked(mouse.position.ReadValue()))
                        mul *= 1f - Mathf.Clamp(w / 120f, -1f, 1f) * wheelZoomStep;
                }
            }

            if (Mathf.Abs(mul - 1f) > 1e-4f)
                userZoom = Mathf.Clamp(userZoom * mul, userZoomMin, userZoomMax);
            else
                // Four time constants is 98 % of the way home, so "twenty
                // seconds" is twenty seconds and not an asymptote.
                userZoom = Mathf.Lerp(userZoom, 1f,
                    1f - Mathf.Exp(-(4f / Mathf.Max(0.5f, userZoomDecay)) * dt));
        }

        /// The motor was fetched once in Start, which was fine while there was
        /// one ship. The yard never changes the target, but a shore boat or a
        /// dev rig can, and a stale motor means a stale hull length.
        void Resolve()
        {
            if (target == motorFor) return;
            motorFor = target;
            motor = target != null
                ? target.GetComponent<SeaSick.Ship.ShipMotor>() : null;
        }

        void LateUpdate()
        {
            if (target == null) return;
            float dt = Time.deltaTime;
            Resolve();

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
            float wantDolly = lyingStill
                ? portraitStoppedPull
                : 1f + portraitZoomOut * order01;
            if (dolly < 0f) dolly = wantDolly;
            // Out at the framing rate, in at the slow one: backing off is an
            // answer to an order and should be prompt; coming closer is the
            // sea going quiet and should take its time.
            float dollyRate = wantDolly < dolly ? portraitPullInRate : framingResponse;
            dolly = Mathf.Lerp(dolly, wantDolly, 1f - Mathf.Exp(-dollyRate * dt));

            PlayerZoom(dt);

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

                bool locked = LockTarget != null;

                // Cruise: only while genuinely making way, and never in a fight.
                bool making = motor != null && motor.CurrentSpeed >= motor.MaxSpeed * cruiseSpeed01;
                atSpeedFor = making && !locked ? atSpeedFor + dt : 0f;

                float wantCruise = atSpeedFor > cruiseDelay ? 1f : 0f;
                cruiseLevel = Mathf.Lerp(cruiseLevel, wantCruise,
                    1f - Mathf.Exp(-(wantCruise > cruiseLevel ? cruiseInRate : cruiseOutRate) * dt));

                lockLevel = Mathf.Lerp(lockLevel, locked ? 1f : 0f,
                    1f - Mathf.Exp(-lockResponse * dt));

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
                    pilotViewBlend = Mathf.MoveTowards(pilotViewBlend,
                        Ship.SailingPilot.OwnsWorldInput ? 1f : 0f, dt * 2f);
                    bDist = Mathf.Lerp(bDist, pilotDistance, pilotViewBlend);
                    bHeight = Mathf.Lerp(bHeight, pilotHeight, pilotViewBlend);
                    bAhead = Mathf.Lerp(bAhead, pilotLookAhead, pilotViewBlend);
                }

                // Cruise is a LANDSCAPE move. Upright the preset is already
                // backed off and lifted, and easing further out from there
                // puts her at the bottom of a tall frame with nothing in it.
                float cruise = cruiseLevel * (1f - portrait01) * (1f - pilotViewBlend);
                // The dolly and the player's pinch scale the SEAT only —
                // distance and height together, so the tilt barely moves and
                // the horizon stays where it was put.
                float zoomK = Mathf.Max(0.05f, userZoom)
                            * Mathf.Lerp(1f, dolly, portrait01);

                float back = (bDist + cruiseDistance * cruise) * frameK * zoomK
                           - stormPullIn * stormLevel;
                float up = (bHeight + cruiseHeight * cruise) * frameK * zoomK
                         - stormDrop * stormLevel;
                float ahead = (bAhead + cruiseLookAhead * cruise) * frameK;

                anchor = shipFlat;
                Vector3 sternDir = -flatForward;

                if (lockLevel > 0.001f && LockTarget != null)
                {
                    Vector3 tgt = new Vector3(LockTarget.position.x, 0f, LockTarget.position.z);
                    Vector3 toTarget = tgt - shipFlat;
                    float sep = toTarget.magnitude;
                    Vector3 dirToTarget = sep < 0.5f ? flatForward : toTarget / sep;

                    // Swing toward sitting opposite the target, clamped so the
                    // helm never fully inverts.
                    float sternAz = Mathf.Atan2(sternDir.x, sternDir.z) * Mathf.Rad2Deg;
                    float awayAz = Mathf.Atan2(-dirToTarget.x, -dirToTarget.z) * Mathf.Rad2Deg;
                    float az = sternAz + Mathf.Clamp(
                        Mathf.DeltaAngle(sternAz, awayAz), -lockMaxSwingDeg, lockMaxSwingDeg);

                    Vector3 swung = new Vector3(
                        Mathf.Sin(az * Mathf.Deg2Rad), 0f, Mathf.Cos(az * Mathf.Deg2Rad));
                    sternDir = Vector3.Slerp(sternDir, swung, lockLevel);

                    // Back off enough to hold both hulls, and bias the look
                    // point toward the enemy — but only partly, so your own
                    // ship never leaves the frame.
                    float fit = Mathf.Min(sep * lockPullPerMetre,
                                          lockMaxPull * frameK) * lockLevel;
                    back += fit;
                    up += fit * Mathf.Lerp(0.42f, 1f, pilotViewBlend);
                    anchor = shipFlat + dirToTarget * (sep * lockBias * lockLevel);
                }

                desired = shipFlat + sternDir * back + Vector3.up * up;
                lookPoint = anchor + flatForward * (ahead * (1f - lockLevel))
                          + Vector3.up * (bLookH * frameK + seaY);
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
                float vfov = Mathf.Lerp(sailFov > 0f ? sailFov : fovBase, overviewFov, overviewLevel);
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
                cam.fieldOfView = Mathf.Lerp(sailFov > 0f ? sailFov : fovBase,
                    overviewFov, overviewLevel);

            if (!IslandCam.Engaged && !shot.HasValue && !SailOverride.HasValue)
            {
                Vector3 pan = SeaSick.Ship.SailingPilot.ViewOffset;
                desired += pan;
                lookPoint += pan;
            }
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
            else rigPos = Vector3.Lerp(rigPos, desired, 1f - Mathf.Exp(-positionResponse * dt));

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
            transform.position = rigPos + Vector3.up * seaY;

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
            Quaternion desiredRot = Quaternion.LookRotation(lookPoint - transform.position, Vector3.up);
            // The rotation's leftover is carried the same way as the position's.
            if (direct)
            {
                if (!wasDirect) directTurn = Quaternion.Inverse(desiredRot) * transform.rotation;
                directTurn = Quaternion.Slerp(directTurn, Quaternion.identity,
                    1f - Mathf.Exp(-directSettle * dt));
                transform.rotation = desiredRot * directTurn;
            }
            else transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot,
                    1f - Mathf.Exp(-rotationResponse * dt));

            OverviewDirect = direct && directOffset == Vector3.zero
                             && Quaternion.Angle(directTurn, Quaternion.identity) < 0.01f;
            wasDirect = direct;
        }
    }
}
