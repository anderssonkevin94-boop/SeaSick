using UnityEngine;

namespace SeaSick.CameraRig
{
    /// Portrait chase camera: follows the ship's yaw only (never inherits
    /// pitch/roll — the horizon stays stable while the ship rocks), looking
    /// past the ship toward the horizon. No manual control by design.
    public class ChaseCamera : MonoBehaviour
    {
        [SerializeField] Transform target;
        // Three-quarter view: tilt = (height - lookHeight) / (distance +
        // lookAhead). Vertical FOV is 60, so the frame spans tilt +/- 30
        // degrees — the horizon is only visible while tilt stays under 30.
        // These give ~22 degrees, which keeps a band of sky at the top while
        // still looking down onto the deck.
        [SerializeField] float distance = 25f;
        [SerializeField] float height = 19f;
        [SerializeField] float lookAhead = 20f;
        [SerializeField] float lookHeight = 0.5f;
        [SerializeField] float positionResponse = 2.2f;
        [SerializeField] float rotationResponse = 3f;

        // FOV motion is the main cause of simulator sickness in a chase cam.
        // Keep the total swing small (a few degrees) and ease it slowly.
        [SerializeField] float fovBase = 58f;
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
        }

        public IslandShot? Overview { get; set; }

        [Header("Island overview (at a dock)")]
        [Tooltip("Degrees above the horizon. 90 is a map; this is high enough to read the whole island and low enough to keep the sky.")]
        [SerializeField] float overviewTilt = 56f;
        [Tooltip("How much wider than the island to frame, so it isn't jammed against the edges.")]
        [SerializeField] float overviewMargin = 1.3f;
        [Tooltip("Seconds-ish to rise into the overview and to come back down.")]
        [SerializeField] float overviewResponse = 0.7f;

        [Tooltip("How tall a 1.7 m crew member must be, as a FRACTION of screen height. 0.0075 is about 8 px on a 1080-tall screen and 17 on a phone. This is what stops the overview backing off to a pretty landform nobody can read: the point of it is watching people move between buildings.")]
        [SerializeField] float minPersonScreenFraction = 0.0075f;
        float overviewLevel;
        float baseFarClip = -1f;

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
            if (target != null) motor = target.GetComponent<SeaSick.Ship.ShipMotor>();
        }

        void LateUpdate()
        {
            if (target == null) return;
            float dt = Time.deltaTime;

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
            if (cam != null && motor != null)
            {
                float s01 = Mathf.Clamp01(motor.CurrentSpeed / motor.MaxSpeed);
                float targetFov = fovBase + fovSpeedBoost * s01 * s01
                                  + fovSurfPunch * motor.SurfBoost01;
                cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, 1f - Mathf.Exp(-fovResponse * dt));
            }

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
                float back = 18f + separation * 0.55f;
                desired = anchor + axis * back + Vector3.up * (14f + separation * 0.35f);
                lookPoint = anchor + Vector3.up * 1.5f;
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

                float back = distance + cruiseDistance * cruiseLevel - stormPullIn * stormLevel;
                float up = height + cruiseHeight * cruiseLevel - stormDrop * stormLevel;
                float ahead = lookAhead + cruiseLookAhead * cruiseLevel;

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
                    float fit = Mathf.Min(sep * lockPullPerMetre, lockMaxPull) * lockLevel;
                    back += fit;
                    up += fit * 0.42f;
                    anchor = shipFlat + dirToTarget * (sep * lockBias * lockLevel);
                }

                desired = shipFlat + sternDir * back + Vector3.up * up;
                lookPoint = anchor + flatForward * (ahead * (1f - lockLevel))
                          + Vector3.up * (lookHeight + seaY);
            }

            // --- the island overview, blended over whatever was framed ----
            float wantOverview = Overview.HasValue ? 1f : 0f;
            overviewLevel = Mathf.Lerp(overviewLevel, wantOverview,
                1f - Mathf.Exp(-overviewResponse * dt));
            if (overviewLevel > 0.001f && Overview.HasValue)
            {
                var ov = Overview.Value;
                float vfov = cam != null ? cam.fieldOfView : 60f;
                // Far enough back that a circle of `radius` fills the frame...
                float span = ov.radius * overviewMargin
                           / Mathf.Tan(vfov * 0.5f * Mathf.Deg2Rad);
                // ...but never so far that the people stop reading. When the
                // ground to cover is bigger than legibility allows,
                // legibility wins and the frame holds the middle of it: an
                // overview you cannot pick a person out of is a map, and the
                // player already has a minimap.
                span = Mathf.Min(span, ReadableDistance(vfov));
                LastOverviewSpan = span;
                Vector3 dir = ov.from;
                dir.y = 0f;
                dir = dir.sqrMagnitude < 0.01f ? Vector3.back : dir.normalized;
                float tilt = overviewTilt * Mathf.Deg2Rad;
                Vector3 seat = ov.centre
                             + dir * (span * Mathf.Cos(tilt))
                             + Vector3.up * (span * Mathf.Sin(tilt));
                LastOverviewSeat = seat;
                desired = Vector3.Lerp(desired, seat, overviewLevel);
                LastDesired = desired;
                lookPoint = Vector3.Lerp(lookPoint, ov.centre, overviewLevel);
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
