using UnityEngine;

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

        [Header("Island overview (at a dock)")]
        [Tooltip("Degrees above the horizon. 90 is a map and 56 still reads as one; 32 is the three-quarter angle Kevin flew to with DockCamTuner, low enough that a building shows its WALLS and not just its roof.")]
        [SerializeField] float overviewTilt = 32f;
        [Tooltip("How much wider than the island to frame, so it isn't jammed against the edges. Only used when no explicit ground coverage is set.")]
        [SerializeField] float overviewMargin = 1.3f;

        [Tooltip("Lens for the overview. Narrow on purpose: at 36 degrees the perspective flattens toward isometric, which is what makes a cluster of buildings read as a plan you can act on rather than a photograph of a hillside. Chosen by hand with DockCamTuner.")]
        [SerializeField] float overviewFov = 36f;

        [Tooltip("Metres of ground across the frame's HEIGHT. This is the zoom, expressed so it stays the same picture whatever the lens and the screen are: 165 m puts a crew member at about 1% of screen height. Chosen by hand with DockCamTuner.")]
        [SerializeField] float overviewGroundMetres = 165f;
        [Tooltip("Seconds-ish to rise into the overview and to come back down.")]
        [SerializeField] float overviewResponse = 0.7f;

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
        float sailFovOverride = -1f;
        float baseFarClip = -1f;
        float sailFov = -1f;

        /// Diagnostic only: the distance the overview last asked for.
        public float LastOverviewSpan { get; private set; }
        public Vector3 LastOverviewSeat { get; private set; }
        public Vector3 LastDesired { get; private set; }

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
            if (motor != null)
            {
                float s01 = Mathf.Clamp01(motor.CurrentSpeed / motor.MaxSpeed);
                float targetFov = fovBase + fovSpeedBoost * s01 * s01
                                  + fovSurfPunch * motor.SurfBoost01;
                sailFov = Mathf.Lerp(sailFov <= 0f ? targetFov : sailFov, targetFov,
                    1f - Mathf.Exp(-fovResponse * dt));
            }
            else if (sailFov <= 0f) sailFov = fovBase;

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
                else sailFovOverride = -1f;

                float back = (bDist + cruiseDistance * cruiseLevel) * frameK
                           - stormPullIn * stormLevel;
                float up = (bHeight + cruiseHeight * cruiseLevel) * frameK
                         - stormDrop * stormLevel;
                float ahead = (bAhead + cruiseLookAhead * cruiseLevel) * frameK;

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
                    up += fit * 0.42f;
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
                Vector3 dir = ov.from;
                dir.y = 0f;
                dir = dir.sqrMagnitude < 0.01f ? Vector3.back : dir.normalized;
                float tiltDeg = ov.tiltDeg > 0.01f ? ov.tiltDeg : overviewTilt;
                CurrentTilt = tiltDeg;
                float tilt = tiltDeg * Mathf.Deg2Rad;
                Vector3 seat = aim
                             + dir * (span * Mathf.Cos(tilt))
                             + Vector3.up * (span * Mathf.Sin(tilt));

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
                    Quaternion rot = Quaternion.LookRotation(aim - seat, Vector3.up);
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

            if (!rigSeeded) { rigPos = transform.position; rigSeeded = true; }
            rigPos = Vector3.Lerp(rigPos, desired, 1f - Mathf.Exp(-positionResponse * dt));

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
                    float ground = isle.SurfacePoint(ang, dist).y;
                    if (cp.y < ground + terrainClearance)
                        transform.position = new Vector3(cp.x, ground + terrainClearance, cp.z);
                }
            }
            Quaternion desiredRot = Quaternion.LookRotation(lookPoint - transform.position, Vector3.up);
            transform.rotation = Quaternion.Slerp(
                transform.rotation, desiredRot, 1f - Mathf.Exp(-rotationResponse * dt));
        }
    }
}
