using SeaSick.UI;
using UnityEngine;

namespace SeaSick.CameraRig
{
    /// The player's hands on the island view: take hold of the land and move
    /// it, zoom in on what the cursor is over, swing round a building to see
    /// its other side, or pull back until the whole island is in frame.
    ///
    /// **Why this exists rather than a better automatic shot.** The docked
    /// overview is one hand-tuned composition at one zoom, and it has to serve
    /// two jobs that pull in opposite directions: showing you a place, and
    /// showing you the people in it. A crew member is 1.7 m tall, so a frame
    /// wide enough to hold a 300 m island puts them at a couple of pixels, and
    /// a frame that keeps them legible cannot hold the island. There is no
    /// single number that satisfies both — so the number belongs to the player.
    ///
    /// The zoom is expressed in **metres of ground up the frame**, the same
    /// unit the shipped dock shot is authored in (165 m), so what you see on
    /// screen can be compared with the authored value directly.
    ///
    /// **This component reads no devices.** `IslandInput` is the one reader
    /// for the island view; it decides what a press means and calls the public
    /// methods below, and so do the probes — a gate that drove its own copy of
    /// a gesture would be testing the copy. Two things reading the same
    /// pointer is how the grab and the Hand end up fighting over one press.
    ///
    /// Only live while she is lying at an island, and it stands aside entirely
    /// while a dev tool has the keys — except for its own tuner, which would
    /// otherwise switch off the camera it is there to tune.
    ///
    /// ---
    ///
    /// **The model.** Four numbers describe the shot, and every gesture is a
    /// rigid motion of them:
    ///
    ///   PIVOT      the world point in the middle of the frame. Held as
    ///              `focus + Pan`, because those two already existed and
    ///              `CampProbe` drives both.
    ///   AZIMUTH    which side the lens sits on, in degrees.
    ///   TILT       `AutoTilt(Ground)` plus whatever bias a hand has added.
    ///   GROUND     metres of ground up the frame. The zoom.
    ///
    /// `ChaseCamera.OverviewPose` turns those into a seat and a rotation, and
    /// it is the SAME call the renderer makes — so the pose this component
    /// aims its screen rays at is the pose that gets drawn, to the last bit.
    /// That equality is the whole trick behind a 1:1 grab, and it is also why
    /// `IslandShot.direct` had to exist: through the rig's own low pass the
    /// land would still be sliding a third of a second after the hand stopped.
    ///
    /// **Runs after `IslandInput` (-50) and before everything at the default
    /// order**, which includes `AnchorController` -- the thing that calls
    /// `Apply`. Without a stated order the eased paths (`ZoomTo`, `Nudge`, a
    /// fly-to) were a coin toss: when `Apply` happened to run first it drew
    /// LAST frame's zoom, and the pose this component reported was a frame
    /// ahead of the pose on screen (`IslandCamProbe`: 1.2 m apart in a sweep).
    /// **The locked-angle mode, Kevin on the phone 2026-09-27:** *"I would
    /// like to try to lock in the camera at a certain angle that looks good
    /// when on the island. so I can still go up and down and spin around
    /// but the actual angle is what I would like to be a good set one."*
    ///
    /// A top-level static, not nested in `Feel`, on purpose: `FeelLab`
    /// discovers a tuning section by resolving a whole class, and `Feel`
    /// carries two dozen fields that are not this knob's business to expose
    /// on a phone panel. This class is exactly the two fields Kevin asked
    /// for and nothing else, registered in `FeelLab.TypeFullNames`.
    ///
    /// While `locked`, every shot this component composes — hands-on or
    /// not, including the authored dock shot and a campless landing's ring
    /// — renders at exactly `angleDeg`, at every zoom (see `Compose` and
    /// `Apply`). Zooming still changes distance/height, spinning and
    /// panning still work; only the tilt gesture itself (two-finger shared
    /// vertical, the mouse right-drag's vertical component, PageUp/
    /// PageDown) goes inert — see `OrbitAbout` and `Nudge` — because a tilt
    /// that can still be nudged is a tilt that drifts off the set angle by
    /// the next session. A hill in the way is cleared by standing the seat
    /// further back along the SAME ray (`Compose` grows `span`), never by
    /// tipping the lens at it.
    ///
    /// Default ON. Default 38°: the curve's own middle point is 32° at the
    /// default 165 m of ground (`Feel.tiltMidDeg`, the shipped dock shot),
    /// but a single angle has to also serve the close-in 8 m view (the
    /// curve goes to 20° there) and the far 520 m view (55° there) without
    /// the curve's help — so 38° sits a few degrees steeper than the
    /// authored middle, roughly midway through the hand-tilt range
    /// (`Feel.minTiltDeg` 12°..`Feel.maxTiltDeg` 80°) instead of hugging the
    /// shallow end, which reads better at both the close and the wide
    /// zoom. It is also `DockCamTuner`'s own opening guess (`tilt = 38f`)
    /// for "a good look when nothing else is set" — a second, independent
    /// read landing on the same number.
    public static class IslandCamLock
    {
        public static bool locked = true;
        /// 28°, not 38: Kevin picked "50° with the lower tilt" from the lens
        /// renders on 2026-09-27 — more horizon and sea, less plan view.
        public static float angleDeg = 28f;
        /// The island lens while locked (vertical field of view). 50° against
        /// the old 36°: more depth, the camp still reads (same renders).
        public static float fovDeg = 50f;
    }

    /// **The villager "hero" shot (Kevin picked V2 Split, 2026-09-27).**
    /// While a `HandSheet` is open the island camera follows that villager
    /// and frames him in the middle of whatever part of the screen the
    /// sheet leaves visible -- the top ~40% upright, the left of the docked
    /// column on a desk -- closer in and at a lower pitch than the locked
    /// island angle. No second camera, no render texture: it is the island
    /// camera itself. Registered in `FeelLab.TypeFullNames`.
    ///
    /// **With `IslandCamLock`:** the lock still owns the player's tilt (the
    /// tilt gesture stays inert, the view goes back to `angleDeg` on
    /// close); `pitchDeg` is the one other angle the lens may take, and
    /// only while this shot is on. 0 = keep the locked angle here too.
    public static class IslandHeroCam
    {
        public static bool enabled = true;
        /// Degrees the lens looks DOWN AT HIM (the ray through him, not the
        /// frame's centre). The island's 28° lock is measured at the centre
        /// of the screen; him sitting high in the top 40% at that tilt would
        /// be seen almost edge-on, so the lens tilts itself to whatever puts
        /// this angle on him -- the mockup's over-the-shoulder look.
        public static float pitchDeg = 20f;
        /// Metres from the lens to him, portrait: a 1.7 m man at ~14% of
        /// the screen, about a third of the world visible above the sheet.
        public static float distanceTall = 12f;
        /// The same on a landscape screen, where most of the height shows.
        public static float distanceWide = 9f;
        /// Aim at his middle, not his feet.
        public static float aimHeight = 0.9f;
        /// Where in the visible area he sits, 0 = its bottom, 1 = its top.
        public static float rectY = 0.5f;
        /// Seconds to swing onto him (critically damped).
        public static float easeIn = 0.45f;
        /// Seconds to ease back to the old view when the sheet closes.
        public static float easeOut = 0.5f;
    }

    [DefaultExecutionOrder(-40)]
    public class IslandCam : MonoBehaviour
    {
        [Header("Zoom, in metres of ground up the frame")]
        [Tooltip("Closest. 8 m of ground puts a 1.7 m crewman at a fifth of the frame — close enough to watch one person swing an axe. It was 18, and 18 was chosen when the view could only be zoomed at its centre; with zoom-to-cursor there is something worth going that close TO.")]
        [SerializeField] float minGround = 8f;
        [Tooltip("Furthest. Far enough to hold a whole island of the size the world makes now; raise it if the islands grow.")]
        [SerializeField] float maxGround = 520f;
        [Tooltip("Where it starts, and what it returns to at a new island. The authored dock shot's own zoom.")]
        [SerializeField] float defaultGround = 165f;
        [Tooltip("Fraction of the current zoom added or removed per second of held input. Proportional, so a step feels the same close in and far out.")]
        [SerializeField] float zoomRate = 1.8f;

        [Header("Panning")]
        [Tooltip("Metres per second at the DEFAULT zoom. Scaled by how far out you are, so the frame crosses the view at the same rate however wide it is.")]
        [SerializeField] float panRate = 55f;
        [Tooltip("How far from the middle of the island the frame may be walked, as a multiple of the island's own radius.")]
        [SerializeField] float panReach = 1.15f;

        [Header("Feel")]
        [SerializeField] float smoothing = 9f;

        /// **Everything a hand would want to change, as plain statics.**
        ///
        /// Not `[SerializeField]`: this component is `AddComponent`-ed at
        /// runtime by `AnchorController`, so it never appears in a scene, no
        /// Inspector ever shows these and no serialized value can shadow them.
        /// A tuner writing statics is therefore writing the real thing — and
        /// `IslandCamTuner` dumps them back out as C# initialisers to paste
        /// straight in here, which is how the dock shot's 32°/36°/165 m were
        /// settled and how these will be.
        ///
        /// The fields above stay serialized because they are the shot's
        /// contract with `CampProbe` (`MinGround`, `MaxGround`,
        /// `DefaultGround`) rather than feel.
        public static class Feel
        {
            // --- the ground, and how much air to leave over it ---------------

            /// Metres of clear air between the lens and the real height field.
            /// Handed to `ChaseCamera` as `IslandShot.clearance`, so the yield
            /// this component does and the clamp the rig does are measured
            /// against the same number and the same terrain.
            ///
            /// 4 m, not the rig's long-standing 8: at the closest zoom the
            /// curve asks for a lens about 5 m up at 19°, and 8 m of air
            /// forced the yield to stand that shot up to 41° -- so "down among
            /// them" was a steep look at their hats. 4 m clears a man and a
            /// hut's eaves and still goes through a tree's crown, which is
            /// what B&W2 does too. The composed dock shot sits a hundred
            /// metres up and never meets either number. The tuner's first
            /// slider, because it is a look call.
            public static float clearance = 4f;

            /// How far the pivot may be walked from the SHIP.
            ///
            /// The placeholder for Phase 3's influence ring. On a 2 km island
            /// you govern the side you anchored off, which is both the honest
            /// streaming answer and — Kevin's call — the interesting one.
            public static float reachFromShip = 300f;

            /// Below this much ground in the frame the terrain has to be at
            /// full detail or the picture is a lie...
            public static float nearGround = 60f;
            /// ...and `TerrainStreamer` follows the SHIP, so full detail is a
            /// fixed disc round her and nowhere else. 256 m is LOD0's own
            /// radius; see `IslandCamProbe`, which prints what each streaming
            /// system actually follows rather than trusting this comment.
            public static float nearGroundReach = 256f;

            // --- the tilt curve ---------------------------------------------
            //
            // Log-space, through three points a hand can move. Shallow when
            // you are low (you are looking AT something, and a steep angle on
            // a hut is a roof) and steep when you are high (you are reading a
            // map of a camp, and a shallow angle at 500 m is all horizon).
            //
            // The middle point is the shipped shot and is not negotiable by
            // accident: 165 m of ground must give exactly 32°, or an untouched
            // view stops being the composition Kevin flew to.
            public static float tiltLowGround = 10f, tiltLowDeg = 20f;
            public static float tiltMidGround = 165f, tiltMidDeg = 32f;
            public static float tiltHighGround = 520f, tiltHighDeg = 55f;

            /// What a hand may tilt to. 12° is nearly along the ground and is
            /// where the far clip starts showing; past 80° it is a map.
            public static float minTiltDeg = 12f, maxTiltDeg = 80f;

            /// How far the TERRAIN may tilt it past that, to get the lens out
            /// of a hill. Higher than the manual limit on purpose — this is a
            /// yield, not a choice, and the alternative is a screen full of
            /// green.
            public static float yieldMaxTiltDeg = 85f;

            // --- throwing the land -------------------------------------------

            /// Seconds of pointer history the throw is averaged over. Short
            /// enough that a flick counts and a drag that stopped does not.
            public static float flingSample = 0.08f;
            /// ...but never fewer than the last two, unless the older of them
            /// is this old -- see `MeanVelocity`.
            public static float flingStale = 0.35f;
            /// e-folds per second. 4 means a throw is down to 2% of its speed
            /// in under a second.
            public static float flingDecay = 4f;
            /// ...and it is CUT at this, so "it stops" is a fact and not an
            /// asymptote a gate has to take on trust.
            public static float flingSeconds = 1.2f;
            /// Metres per second below which a glide is just finished.
            public static float flingStop = 0.4f;
            /// The same scheme for a swing, with less gain: an orbit that
            /// keeps going is disorienting in a way a pan is not.
            public static float orbitFlingGain = 0.45f;
            public static float orbitFlingStop = 3f;
            /// **Caps, and they are not paranoia.** A throw's speed is
            /// whatever the pointer did over the last frame or two, and one
            /// frame is a sample of a hand: a dropped frame, a warped cursor
            /// or a probe stepping a gesture in one call all produce a
            /// perfectly arithmetic velocity of several hundred metres a
            /// frame. Without a ceiling the island leaves.
            ///
            /// In multiples of `Ground` per second, so the cap is the same
            /// FRACTION OF THE FRAME however far out you are: 4 means a
            /// throw can coast about one screen height before it dies.
            public static float flingMaxGrounds = 4f;
            public static float orbitFlingMaxDegPerSecond = 200f;

            // --- the gestures themselves -------------------------------------

            /// Cap on one frame's grab step, as a multiple of `Ground`. A ray
            /// that grazes the horizon can solve to a point a kilometre away;
            /// this is what stops one bad frame throwing the island off screen.
            public static float grabStepGrounds = 2f;

            /// How steeply a ray must point down before it is believed. At
            /// 0.03 it is about 1.7° below the horizontal — flatter than that
            /// and the intersection is all error.
            public static float minRayDown = 0.03f;

            /// Where a double-click lands you, in metres of ground. Close
            /// enough to see faces, not so close that you have lost the camp.
            public static float flyToGround = 60f;

            /// Degrees per second of held key.
            public static float orbitKeyDegPerSecond = 70f;
            public static float tiltKeyDegPerSecond = 35f;

            /// How near a tap has to land to count as pressing on somebody, in
            /// fractions of screen HEIGHT. A fraction rather than pixels
            /// because a phone and a desk window do not have the same pixels.
            /// Lives here for the Hand, which does the picking now.
            public static float pickRadius = 0.07f;
        }

        /// Metres of ground up the frame, as asked for.
        public float Ground { get; private set; }
        /// Where the frame has been walked to, relative to the shot's own centre.
        public Vector3 Pan { get; private set; }
        /// True once the player has touched anything — the shot only goes
        /// `free` then, so an untouched view is exactly the shipped one.
        public bool Driven { get; private set; }

        /// Metres above the ground the view drops to when a blueprint goes
        /// down. Kevin's number: close enough to watch the thing being built.
        public const float BlueprintHeight = 35f;

        float wantGround;
        Vector3 wantPan;
        World.Island subject;

        /// A world point the view is centred on INSTEAD of the shot's own
        /// composed centre, and the thing it is tracking if it is tracking
        /// something.
        ///
        /// These are what make the view the player's rather than the dock's:
        /// the composed shot answers "look at this island from the sea", and
        /// once somebody has sited a camp or pressed on a crewman the question
        /// has changed to "look at THAT".
        Vector3? focus;
        Transform following;

        // --- the one eased path besides a fly-to: `PanToWorld` --------------

        bool easingPan;
        Vector3 easeFromFocus;
        Vector3 easeToFocus;
        float easeFromGround;
        float easeToGround;
        float easeElapsed;
        float easeDuration;

        // --- the hands-on state ----------------------------------------------

        /// **Latched the first time a gesture arrives**, and the reason there
        /// is no pop when it happens: see `Latch`.
        ///
        /// Everything below only means anything while this is true, and while
        /// it is false this component behaves exactly as the shipped one did —
        /// which is what keeps `CampProbe`'s 58 gates honest.
        bool handsOn;
        Vector2 orbitScreen;
        bool orbitHeld;
        float azimuthDeg;
        float tiltBiasDeg;

        bool grabbing;
        Vector3 grabPoint;          // the piece of ground under the press
        float grabPlaneY;           // ...and the height it is frozen at

        bool orbiting;
        Vector3 orbitAbout;         // latched at orbit start, held until OrbitEnd

        Vector3 panVel;
        float orbitVel;
        float flingFor;

        ChaseCamera chase;

        /// Is the island view live at all? Read by `HelmInput`, which shares
        /// the lower half of the screen with it.
        public static bool Engaged { get; private set; }

        /// **An extra disc `ClampPivot` may also reach, on top of the
        /// ship/island ones (2026-09-27).** Null and 0 do nothing -- see
        /// `Outpost.WallExtentFromCentre` and `ClampPivot`'s own comment.
        /// `CampSiting` sets this for the life of a siting session and clears
        /// it when the mode ends; nothing else should need it.
        public static Vector3? ExtraReachCentre;
        public static float ExtraReachRadius;

        /// What the view is centred on, for the readout and the probe.
        public Vector3? FocusPoint => focus;
        public Transform Following => following;

        void Awake()
        {
            live = this;
            Ground = wantGround = defaultGround;
            // The view's input and the Hand live beside it, added here for the
            // reason this component is added at runtime itself: nothing about
            // the island view is wired in `Sea.unity`.
            if (GetComponent<IslandInput>() == null) gameObject.AddComponent<IslandInput>();
            if (GetComponent<Hand>() == null) gameObject.AddComponent<Hand>();
            // ...and so is the tuner, for exactly the same reason. Every other
            // dev tool in this project is a component somebody ticked into
            // `Sea.unity`; this view has no scene presence at all to tick it
            // into, so it comes up with the thing it tunes and offers itself
            // in the settings drawer like the rest.
            if (GetComponent<IslandCamTuner>() == null)
                gameObject.AddComponent<IslandCamTuner>();
        }

        void OnDisable() { Engaged = false; if (live == this) live = null; }

        /// The one island camera in the scene, for the static entry points
        /// below. Set in Awake, cleared in OnDisable.
        static IslandCam live;

        /// Follow a hand from anywhere (the sheets: a villager's sheet opening
        /// from the crew list or a world tap, 2026-09-22). No-op unless the
        /// island view is engaged, so a sheet opened at sea moves nothing.
        public static void Follow(Transform who)
        {
            if (live == null || !Engaged) return;
            // V2 Split (2026-09-27): the hand sheet's hero shot -- see
            // `IslandHeroCam` and `HeroFollow`. Off, the old centred follow.
            if (IslandHeroCam.enabled) live.HeroFollow(who);
            else live.FollowThis(who);
        }

        /// Let go from anywhere. The plain follow stays where it is; the
        /// hero shot eases back to the view from before the sheet opened
        /// (unless a hand took the view meanwhile).
        public static void StopFollow()
        {
            if (live == null) return;
            live.StopFollowing();
            live.HeroStop();
        }

        // =====================================================================
        // THE HANDS-ON VIEW
        //
        // `IslandInput` and the probes call exactly these, so a gesture and its
        // gate cannot drift apart.
        // =====================================================================

        /// Set by `IslandCamTuner` while it is open, so the rule "an open dev
        /// tool owns the keys" does not switch off the camera being tuned.
        public static bool TunerAttached { get; set; }

        /// The island in view, or null.
        public World.Island Subject => subject;

        /// True once the overview has finished blending in and the view can
        /// be driven. Input is inert before this: a grab against a camera that
        /// is still flying up from the chase shot has nothing fixed to hold.
        public bool Ready
        {
            get
            {
                var c = Chase();
                // The blend, and NOT `ChaseCamera.OverviewSettled`. Waiting for
                // the rig as well was tried and measured: while she is being
                // warped alongside, the composed seat swings with her (15 m in
                // two frames), "settled" flickers, and a grab that lands in a
                // false frame is simply refused -- a dead click. A hand that
                // arrives early is instead carried by `direct`'s decaying
                // offset: the land follows 1:1 at once and the leftover flight
                // eases out underneath it.
                return subject != null && c != null && c.OverviewLevel > 0.98f;
            }
        }

        /// The view ray through a screen point, **from the pose this component
        /// is ASKING for**, not the one the camera has eased to.
        ///
        /// Two reasons it cannot be `Camera.main.ScreenPointToRay`:
        ///
        /// 1. The rig's transform this frame is last frame's answer plus a low
        ///    pass. Under `direct` the two agree, but the rig has not run yet
        ///    when input reads the pointer — `IslandInput` is at execution
        ///    order −50 — so the camera object still holds the PREVIOUS frame's
        ///    pose either way. Composing the pose here makes that explicit
        ///    rather than accidental.
        /// 2. `cam.aspect` is not always the window. The frustum is built from
        ///    the real `Screen.width`/`Screen.height`, which is the only shape
        ///    the player's cursor actually lives in.
        public Ray ScreenRay(Vector2 screen)
        {
            if (Pose(out Vector3 seat, out Quaternion rot, out float fov))
                return RayFrom(seat, rot, fov, screen);
            var cam = Camera.main;
            return cam != null ? cam.ScreenPointToRay(screen)
                               : new Ray(Vector3.up * 100f, Vector3.down);
        }

        /// The ground under a screen point; over water, the sea plane.
        ///
        /// The sea fallback is not a nicety. `GroundPick` marches the height
        /// field, which goes on returning SEABED out past every shore — so a
        /// ray out to sea either reports a point twenty metres under the water
        /// or, past the marcher's floor, reports nothing at all. Neither is
        /// where the player is pointing. Sea level is y = 0 everywhere in this
        /// project.
        public bool GroundUnder(Vector2 screen, out Vector3 hit)
            => GroundAlong(ScreenRay(screen), out hit);

        static Ray RayFrom(Vector3 seat, Quaternion rot, float fov, Vector2 screen)
        {
            float h = Mathf.Max(1f, Screen.height);
            float w = Mathf.Max(1f, Screen.width);
            float tanHalf = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            float ny = (screen.y / h - 0.5f) * 2f * tanHalf;
            float nx = (screen.x / w - 0.5f) * 2f * tanHalf * (w / h);
            return new Ray(seat, (rot * new Vector3(nx, ny, 1f)).normalized);
        }

        static bool GroundAlong(Ray ray, out Vector3 hit)
        {
            hit = Vector3.zero;
            if (ray.direction.y > -Feel.minRayDown) return false;
            if (GroundPick.Along(ray, out hit) && hit.y > 0f) return true;
            hit = ray.origin + ray.direction * (ray.origin.y / -ray.direction.y);
            hit.y = 0f;
            return true;
        }

        /// Take hold of the land under this screen point...
        public void GrabBegin(Vector2 screen)
        {
            if (!Ready) return;
            KillMotion();
            if (!GroundUnder(screen, out Vector3 g)) return;
            Drive();
            grabPoint = g;
            grabPlaneY = g.y;
            grabbing = true;
            trailN = 0;
            Record(Pivot);
        }

        /// ...and keep that same point of ground under the pointer.
        ///
        /// **Against a frozen plane, not against the terrain.** Re-picking the
        /// ground every frame would move the held point up and down the hill
        /// as the frame slid over it, and the land would appear to slither.
        /// Freezing y at the height the grab started at makes the solve one
        /// ray-plane intersection — exact, one step, no iteration and no
        /// smoothing.
        ///
        /// The arithmetic is worth stating because it looks too easy: moving
        /// the pivot by Δ translates the whole rig by Δ, so the ray through
        /// the same screen point translates by Δ and its intersection with a
        /// HORIZONTAL plane moves by Δ as well. Wanting the intersection to
        /// land back on the grabbed point therefore gives Δ = G − P outright.
        public void GrabMove(Vector2 screen)
        {
            if (!grabbing) return;
            var ray = ScreenRay(screen);
            if (ray.direction.y > -Feel.minRayDown) return;
            float t = (ray.origin.y - grabPlaneY) / -ray.direction.y;
            if (t <= 0f) return;

            Vector3 p = ray.origin + ray.direction * t;
            Vector3 delta = grabPoint - p;
            delta.y = 0f;
            float cap = Feel.grabStepGrounds * Mathf.Max(1f, Ground);
            if (delta.sqrMagnitude > cap * cap) delta = delta.normalized * cap;

            MovePivot(delta);
            HoldUnder(screen, grabPoint, 3);
            Record(Pivot);
        }

        /// **Slide the pivot until `world` sits under `screen` in the pose that
        /// will actually be DRAWN.**
        ///
        /// Every gesture here is solved as a rigid motion of the shot, and the
        /// shot is not rigid: the terrain yield re-tilts it as the seat passes
        /// over rising ground, so the pose after a move is not the pose the
        /// move was solved against. `IslandCamProbe` measured what that is
        /// worth -- the held point strayed 9 % of the screen at 165 m of
        /// ground and 44 % at 20 m, where the yield is most of the tilt --
        /// while zoom, which happened not to cross a hill, held to 0.7 %.
        ///
        /// So the rigid answer is the first guess and this closes the rest:
        /// re-cast the ray through the pose as it now composes, see where the
        /// point landed, move by the difference. The yield is continuous in
        /// the ground under the seat, so two or three passes are inside a
        /// centimetre; a pivot stopped by the reach clamp simply stays short.
        bool HoldUnder(Vector2 screen, Vector3 world, int passes)
        {
            for (int i = 0; i < passes; i++)
            {
                // **`BuildPose`, not `ScreenRay`.** `ScreenRay` answers from the
                // pose cached at the start of the frame, on purpose -- a rigid
                // solve must be cast from what was on screen when the pointer
                // was sampled. This asks the opposite question: where will the
                // point be in the pose that the state composes to NOW. Casting
                // these passes through the cache applies the same correction
                // three times over, which the probe measured as the held point
                // leaving the screen altogether.
                if (!BuildPose(out Vector3 seat, out Quaternion rot, out float fov)) return false;
                var ray = RayFrom(seat, rot, fov, screen);
                if (ray.direction.y > -Feel.minRayDown) return false;
                float t = (ray.origin.y - world.y) / -ray.direction.y;
                if (t <= 0f) return false;
                Vector3 off = world - (ray.origin + ray.direction * t);
                off.y = 0f;
                if (off.sqrMagnitude < 1e-4f) return true;
                MovePivot(off);
            }
            return true;
        }

        /// Let go; the land carries on with the speed it was thrown at.
        public void GrabEnd()
        {
            if (!grabbing) return;
            grabbing = false;
            panVel = Vector3.ClampMagnitude(MeanVelocity(),
                Feel.flingMaxGrounds * Mathf.Max(1f, Ground));
            flingFor = 0f;
            Reground();
        }

        public bool Grabbing => grabbing;

        /// Zoom by `factor` (<1 is in) keeping the ground under `screen` fixed.
        ///
        /// Two rigid motions about that point, both of which leave it exactly
        /// where it is on screen:
        ///
        ///   SCALE    `pivot' = C + s(pivot − C)` with `s = g1/g0`. A uniform
        ///            scaling about C moves the seat and the aim together and
        ///            does not turn the lens, so every ray through C is the
        ///            same ray.
        ///   ROTATE   the tilt the new zoom asks for, applied as a rotation
        ///            about the camera-right axis THROUGH C rather than
        ///            through the pivot. Without this the shot would rock on
        ///            its own middle while the cursor sat off to one side, and
        ///            zoom-to-cursor would drift a little every notch.
        public void ZoomAt(Vector2 screen, float factor)
        {
            if (!Ready || factor <= 0.0001f) return;
            KillMotion();
            bool overGround = GroundUnder(screen, out Vector3 c);
            if (!overGround) c = Pivot;
            Drive();

            float g0 = Ground;
            float g1 = ClampGround(g0 * factor, c);
            if (Mathf.Abs(g1 - g0) < 1e-4f) return;

            Compose(Pivot, g0, azimuthDeg, tiltBiasDeg, out float t0, out _);
            Vector3 scaled = c + (Pivot - c) * (g1 / g0);
            Compose(scaled, g1, azimuthDeg, tiltBiasDeg, out float t1, out _);

            Ground = wantGround = g1;
            SetPivot(RotateAbout(scaled, c, azimuthDeg, t0, azimuthDeg, t1));
            // The rigid answer, then what the yield did to it -- see `HoldUnder`.
            if (overGround) HoldUnder(screen, c, 2);
        }

        /// Turn and tilt the view about the ground under `screen`.
        ///
        /// The point is latched at the START of the gesture and held until
        /// `OrbitEnd`: re-picking it every frame would make the axis crawl
        /// across the island as the picture turned, and a swing round a
        /// building would spiral off it.
        public void OrbitAbout(Vector2 screen, float dAzimuthDeg, float dTiltDeg)
        {
            if (!Ready) return;
            // Locked: the spin half of the gesture still lands, the tilt
            // half is dropped at the door -- see `IslandCamLock`.
            if (IslandCamLock.locked) dTiltDeg = 0f;
            Drive();
            if (!orbiting)
            {
                KillMotion();
                // Put the pivot back on the ground first, so the axis and the
                // frame's middle are the same place to start with.
                Reground();
                orbitHeld = GroundUnder(screen, out orbitAbout);
                if (!orbitHeld) orbitAbout = Pivot;
                orbitScreen = screen;
                orbiting = true;
                trailN = 0;
                Record(Pivot);
            }

            Compose(Pivot, Ground, azimuthDeg, tiltBiasDeg, out float t0, out _);
            float az1 = azimuthDeg + dAzimuthDeg;
            float t1 = Mathf.Clamp(t0 + dTiltDeg, Feel.minTiltDeg, Feel.maxTiltDeg);

            Vector3 moved = RotateAbout(Pivot, orbitAbout, azimuthDeg, t0, az1, t1);
            azimuthDeg = Mathf.Repeat(az1, 360f);
            tiltBiasDeg += t1 - t0;
            SetPivot(moved);
            // What was under the press stays under the press -- see `HoldUnder`.
            if (orbitHeld) HoldUnder(orbitScreen, orbitAbout, 3);
            RecordOrbit(dAzimuthDeg);
        }

        public void OrbitEnd()
        {
            if (!orbiting) return;
            orbiting = false;
            orbitVel = Mathf.Clamp(MeanOrbitSpeed() * Feel.orbitFlingGain,
                -Feel.orbitFlingMaxDegPerSecond, Feel.orbitFlingMaxDegPerSecond);
            flingFor = 0f;
            Reground();
        }

        /// **Put the pivot back on the ground without moving the picture.**
        ///
        /// Tilting about a point that is not the pivot lifts the pivot off the
        /// ground — that is what makes the gesture exact, and it is fine while
        /// it lasts, but a pivot floating fifty metres up is a bad axis for
        /// the NEXT gesture and a meaningless number in the readout. So at the
        /// end of every gesture: cast the centre ray, take what it hits, and
        /// re-express the same shot about that point instead.
        ///
        /// Nothing on screen changes, because the new pivot is on the old view
        /// axis: the direction from it back to the seat is the same direction,
        /// hence the same azimuth and the same tilt. Only `span` — and so
        /// `Ground`, and so `AutoTilt` — is different, which is why the bias is
        /// re-derived rather than kept.
        public void Reground()
        {
            if (!handsOn || subject == null) return;
            if (!BuildPose(out Vector3 seat, out Quaternion rot, out float fov)) return;

            var centre = new Ray(seat, rot * Vector3.forward);
            if (!GroundAlong(centre, out Vector3 h)) return;

            Vector3 back = seat - h;
            float span = back.magnitude;
            if (span < 1f) return;

            float tanHalf = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            float ground = Mathf.Clamp(span * 2f * tanHalf, minGround, maxGround);
            float tilt = Mathf.Asin(Mathf.Clamp(back.y / span, -1f, 1f)) * Mathf.Rad2Deg;

            Vector3 flat = back; flat.y = 0f;
            if (flat.sqrMagnitude > 1e-4f)
                azimuthDeg = Mathf.Repeat(Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg, 360f);

            Ground = wantGround = ground;
            // Locked: the bias is meaningless (`Compose` ignores it), and
            // left at zero rather than a stale reading so lock can be
            // switched off later without a jump.
            tiltBiasDeg = IslandCamLock.locked ? 0f : tilt - AutoTilt(ground);
            SetPivot(h);
        }

        /// Ease to the ground under this screen point. The only eased path.
        ///
        /// Everything else in here is exact and unsmoothed, because a gesture
        /// that lags the hand driving it is not a gesture. A double-click is
        /// not a gesture — it is an instruction to go somewhere — and being
        /// teleported across an island with no travel between is how you lose
        /// track of where you were.
        public void FlyTo(Vector2 screen)
        {
            if (!Ready) return;
            if (!GroundUnder(screen, out Vector3 h)) return;
            KillMotion();
            Drive();
            if (!focus.HasValue) return;
            PanTo(ClampPivot(h) - focus.Value);
            ZoomTo(Mathf.Min(Ground, Feel.flyToGround));
        }

        /// Held-key motion, in the frame's own axes: x right, z up the screen,
        /// each -1..1. `orbit` and `tilt` likewise. About the screen centre.
        ///
        /// "About the screen centre" costs nothing to arrange: the pivot IS
        /// the screen centre (the lens is aimed at it by construction), so a
        /// plain change of azimuth or tilt already turns about it.
        public void Nudge(float x, float z, float orbit, float tilt, float zoom, float dt)
        {
            if (!Ready || dt <= 0f) return;
            if (x == 0f && z == 0f && orbit == 0f && tilt == 0f && zoom == 0f) return;
            Drive();
            KillMotion();

            if (x != 0f || z != 0f)
            {
                // The frame's own axes, not the world's. The overview sits on
                // whatever bearing the ship came in on, so "up" has to mean up
                // the screen or the controls are a puzzle.
                float a = azimuthDeg * Mathf.Deg2Rad;
                Vector3 seatSide = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                Vector3 fwd = -seatSide;                       // away from the lens
                Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);
                // Scale with the zoom so the frame crosses the view at the
                // same rate however wide it is -- at 500 m a fixed 55 m/s
                // crawls.
                float rate = panRate * (Ground / Mathf.Max(1f, defaultGround));
                MovePivot((right * x + fwd * z) * rate * dt);
            }

            if (orbit != 0f)
                azimuthDeg = Mathf.Repeat(
                    azimuthDeg + orbit * Feel.orbitKeyDegPerSecond * dt, 360f);

            // Locked: PageUp/PageDown do nothing -- see `IslandCamLock`.
            if (tilt != 0f && !IslandCamLock.locked)
            {
                Compose(Pivot, Ground, azimuthDeg, tiltBiasDeg, out float t0, out _);
                float t1 = Mathf.Clamp(t0 + tilt * Feel.tiltKeyDegPerSecond * dt,
                                       Feel.minTiltDeg, Feel.maxTiltDeg);
                tiltBiasDeg += t1 - t0;
            }

            if (zoom != 0f)
            {
                Ground = wantGround = ClampGround(
                    Ground * (1f + zoomRate * zoom * dt), Pivot);
            }
        }

        /// Any press kills a fling and a fly-to: a hand on the land stops it.
        public void KillMotion()
        {
            panVel = Vector3.zero;
            orbitVel = 0f;
            flingFor = 0f;
            wantPan = Pan;
            wantGround = Ground;
            easingPan = false;
        }

        /// END: hand the shot back to the composition the dock authored.
        public void Home() { Release(); Driven = true; }

        /// Point it at a new place. Resets the zoom and the pan, because a
        /// frame walked to the north end of one island means nothing at the
        /// next one.
        public void Focus(World.Island isle)
        {
            Engaged = isle != null;
            if (subject == isle) return;
            subject = isle;
            HeroDrop();
            heroBlend = 0f;
            wantGround = Ground = defaultGround;
            wantPan = Pan = Vector3.zero;
            Driven = false;
            focus = null;
            following = null;
            LetGo();
        }

        /// **Centre the view on a point, at a given height above it.**
        ///
        /// Height, not coverage, because that is how the request arrives — "35
        /// m above the campfire" — and `ChaseCamera` owns the triangle that
        /// converts one into the other. Counts as driving: a view somebody has
        /// pointed at something must not also be sliding about to keep the
        /// ship in frame.
        ///
        /// Does NOT count as taking hold of it. The tilt curve and the
        /// azimuth stay the rig's, the shot stays eased, and what this
        /// produces is exactly what it produced before the hands-on view
        /// existed — which `CampProbe` measures to within 4 m.
        public void LookAt(Vector3 point, float heightMetres)
        {
            var c = Chase();
            LookAtGround(point, c != null ? c.OverviewGroundForHeight(heightMetres)
                                          : heightMetres);
        }

        /// The same thing asked for in the unit the shot is AUTHORED in —
        /// metres of ground up the frame — for a caller that worked out its
        /// coverage from something that has to fit in the frame rather than
        /// from a height. `AnchorController` frames the campfire siting ring
        /// this way: the ring's diameter decides the zoom, and converting it
        /// to a height and back would only lose the fit.
        public void LookAtGround(Vector3 point, float groundMetres)
        {
            HeroDrop();
            focus = point;
            following = null;
            wantPan = Pan = Vector3.zero;
            easingPan = false;
            wantGround = Mathf.Clamp(groundMetres, minGround, maxGround);
            Driven = true;
        }

        /// **Ease onto a world point over `seconds`, arriving at `heightMetres`
        /// above it — `LookAt`'s own destination, travelled to smoothly
        /// instead of cut to.**
        ///
        /// For a press that should read as "the camera is going there"
        /// (the ashore rail's tokens): a `LookAt` pop reads as the sheet
        /// simply appearing about somewhere else, which loses the "that
        /// one, over there" the press was asking for.
        ///
        /// The start point is read off whatever is actually on screen right
        /// now — the existing `focus`, or the rig's own last composed aim
        /// when nothing has taken hold of the view yet — the same trick
        /// `Latch` uses and for the same reason: a hand-off with no pop.
        ///
        /// Counts as driving, exactly like `LookAt`, but NOT as taking hold:
        /// `handsOn` stays false, so a gesture that arrives mid-ease still
        /// latches cleanly off the rig's own composition rather than off a
        /// half-finished pan. `KillMotion`, which every gesture entry point
        /// calls first, drops the ease outright — so the player's next drag
        /// takes over from wherever the ease had gotten to, not fights it.
        public void PanToWorld(Vector3 point, float heightMetres, float seconds)
        {
            var c = Chase();
            float targetGround = c != null
                ? Mathf.Clamp(c.OverviewGroundForHeight(heightMetres), minGround, maxGround)
                : Mathf.Clamp(heightMetres, minGround, maxGround);

            Vector3 startFocus;
            if (focus.HasValue) startFocus = focus.Value;
            else if (c != null && c.OverviewLevel > 0.5f) startFocus = c.LastOverviewAim;
            else startFocus = point;

            easeFromFocus = startFocus;
            easeFromGround = Ground;
            easeToFocus = point;
            easeToGround = targetGround;
            easeElapsed = 0f;
            easeDuration = Mathf.Max(0.01f, seconds);
            easingPan = true;
            HeroDrop();

            following = null;
            wantPan = Pan = Vector3.zero;
            focus = startFocus;
            Driven = true;
        }

        /// Keep this transform in the middle of the frame until told otherwise.
        /// The zoom is left alone -- whoever is watching chose it.
        public void FollowThis(Transform who)
        {
            if (who == null) return;
            // A world tap on him both opens his sheet (hero shot) and asks
            // for this; whichever lands second, the hero shot wins.
            if (heroOn && who == heroTarget) return;
            HeroDrop();
            following = who;
            focus = who.position;
            wantPan = Pan = Vector3.zero;
            Driven = true;
        }

        /// Stop following, and stay where the view is rather than snapping
        /// back to the composed shot. Letting go of somebody should not move
        /// the camera.
        public void StopFollowing()
        {
            if (following != null) focus = following.position;
            following = null;
        }

        /// Back to the shot the dock composed.
        public void Release()
        {
            HeroDrop();
            focus = null;
            following = null;
            wantPan = Pan = Vector3.zero;
            LetGo();
        }

        /// Drop the hands-on state. The shot goes back to being composed for
        /// the player rather than by them, which means `direct` comes off and
        /// the rig eases the rest of the way — so this is also the one place
        /// a pop would be acceptable, and it still is not one.
        void LetGo()
        {
            handsOn = false;
            grabbing = false;
            orbiting = false;
            azimuthDeg = 0f;
            tiltBiasDeg = 0f;
            panVel = Vector3.zero;
            orbitVel = 0f;
            trailN = 0;
            poseFrame = -1;
        }

        void Update()
        {
            if (subject == null) return;

            // A dev tool that is open owns the keys. `IslandTuner` says as
            // much on its own switch, and two things reading the same key is
            // how a tuner becomes untunable -- EXCEPT this view's own tuner,
            // which would otherwise switch off the camera it exists to tune.
            if (DevTools.Open != null && !TunerAttached) { Settle(); return; }

            float dt = Time.unscaledDeltaTime;
            Glide(dt);

            // `PanToWorld`'s ease, stepped toward its target. Smoothstepped
            // rather than the exponential `Settle` uses elsewhere: this is
            // the one path with a stated DURATION rather than a decay rate,
            // because it is answering "take me there in about this long"
            // instead of "chase this continuously".
            if (easingPan)
            {
                easeElapsed += dt;
                float u = Mathf.Clamp01(easeElapsed / easeDuration);
                float e = u * u * (3f - 2f * u);
                focus = Vector3.Lerp(easeFromFocus, easeToFocus, e);
                wantGround = Mathf.Lerp(easeFromGround, easeToGround, e);
                if (u >= 1f) easingPan = false;
            }

            HeroStep(dt);

            // Whoever is being followed drags the centre with them.
            if (following != null)
            {
                if (!following.gameObject.activeInHierarchy)
                {
                    // Parked when the ship sailed, or gone ashore and switched
                    // off. Hold the last place they stood rather than snapping
                    // the view across the island.
                    StopFollowing();
                }
                else focus = following.position;
            }

            if (handsOn && focus.HasValue)
            {
                // Both discs, on what the frame is actually centred on.
                Vector3 f = focus.Value;
                wantPan = ClampPivot(f + wantPan) - f;
                Pan = ClampPivot(f + Pan) - f;
            }
            else
            {
                // Keep it over the island. Walking the frame out to sea shows
                // you nothing and is the easiest way to get lost in a top-down
                // view.
                //
                // The clamp is on WHERE THE FRAME ENDS UP, not on how far it
                // has been walked, because since the view can be re-centred on
                // a campfire or a crewman the pan is an offset from something
                // that is already off the island's middle.
                float reach = Mathf.Max(60f, subject.Radius * panReach);
                Vector3 islandCentre = subject.transform.position;
                if (focus.HasValue)
                {
                    Vector3 centre = focus.Value + wantPan;
                    Vector3 off = centre - islandCentre;
                    off.y = 0f;
                    if (off.sqrMagnitude > reach * reach)
                        wantPan = islandCentre + off.normalized * reach - focus.Value;
                }
                else if (wantPan.sqrMagnitude > reach * reach)
                    wantPan = wantPan.normalized * reach;
            }

            Settle();
        }

        /// Ask for a zoom directly, in metres of ground.
        ///
        /// The same path the keys take, rather than a second one beside it --
        /// a probe that drove its own copy of this would be testing the copy.
        public void ZoomTo(float groundMetres)
        {
            wantGround = Mathf.Clamp(groundMetres, minGround, maxGround);
            Driven = true;
        }

        /// Walk the frame to an offset, clamped to the island exactly as the
        /// keys are.
        public void PanTo(Vector3 offset)
        {
            if (subject != null)
            {
                float reach = Mathf.Max(60f, subject.Radius * panReach);
                if (offset.sqrMagnitude > reach * reach) offset = offset.normalized * reach;
            }
            wantPan = offset;
            Driven = true;
        }

        /// Snap to what has been asked for, skipping the easing. For probes
        /// and for anything that must read a settled value this frame.
        public void SnapToTarget() { Ground = wantGround; Pan = wantPan; }

        public float MinGround => minGround;
        public float MaxGround => maxGround;
        public float DefaultGround => defaultGround;

        void Settle()
        {
            float k = 1f - Mathf.Exp(-smoothing * Time.unscaledDeltaTime);
            Ground = Mathf.Lerp(Ground, wantGround, k);
            Pan = Vector3.Lerp(Pan, wantPan, k);
        }

        /// Apply what the player has asked for to a shot somebody else composed.
        public ChaseCamera.IslandShot Apply(ChaseCamera.IslandShot shot)
        {
            if (subject == null) return shot;
            // A focus REPLACES the composed centre; a pan offsets whichever
            // centre is in force.
            if (focus.HasValue) shot.centre = focus.Value;
            shot.centre += Pan;
            shot.ground = Ground;
            // Only once they have actually touched it: an untouched view must
            // be exactly the shipped composition, legibility clamp and all.
            //
            // A focus counts, and it has to: `free` is also what switches off
            // the slide that drags the frame until the ship is in it, and a
            // view centred on a campfire that then slid to include the ship
            // would not be centred on the campfire.
            shot.free = Driven || focus.HasValue;
            // The clearance is a statement about the TERRAIN, not about who is
            // driving, so it goes on every shot. It carries the rig's own
            // long-standing 8 m by default and only changes which height the
            // clamp measures from -- the real field instead of the 46-sector
            // profile, which is the field this component's own yield uses.
            shot.clearance = Feel.clearance;

            if (handsOn)
            {
                float a = azimuthDeg * Mathf.Deg2Rad;
                shot.from = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                Compose(shot.centre, Ground, azimuthDeg, tiltBiasDeg,
                        out float tiltDeg, out float span);
                shot.tiltDeg = tiltDeg;
                // The span too, so the yield this component did survives into
                // the pose that is drawn. `ground` stays the authored-unit
                // number the readout and `CampProbe` read; the two agree
                // exactly whenever the yield did nothing, which is almost
                // always.
                shot.span = span;
                shot.direct = true;
            }
            else if (IslandCamLock.locked)
            {
                // Nobody has taken hold of the view -- this is still
                // whoever's authored composition (the docked shot, a
                // campless landing's ring, a `LookAt`/`LookAtGround`
                // framing) -- but the set angle overrides whatever tilt
                // THAT shot was authored at, exactly as it would the
                // moment a hand arrived. Nothing else about the shot
                // changes: span/ground, the legibility clamp and the
                // ship-hold slide all stay whoever composed them.
                shot.tiltDeg = Mathf.Clamp(IslandCamLock.angleDeg,
                    Feel.minTiltDeg, Feel.yieldMaxTiltDeg);
            }
            // A hero shot still easing out after something let go of the
            // view (a `Release`, a `LookAt`): same lean as `Compose`.
            if (!handsOn && heroBlend > 0.0005f)
                shot.tiltDeg = HeroTilt(shot.tiltDeg > 0.01f
                    ? shot.tiltDeg : ChaseCamera.DefaultOverviewTilt);
            return shot;
        }

        /// One line for the HUD, in the units the shot is authored in.
        public string Readout =>
            following != null
                ? $"following {FollowName}   ·   {Ground:F0} m of ground"
                : $"view {Ground:F0} m of ground   ·   "
                  + (focus.HasValue
                      ? (Pan.magnitude < 1f ? "on the camp" : $"{Pan.magnitude:F0} m off the camp")
                      : (Pan.magnitude < 1f ? "centred" : $"{Pan.magnitude:F0} m off centre"));

        string FollowName
        {
            get
            {
                var a = following != null ? following.GetComponent<Crew.CrewAgent>() : null;
                return a != null ? a.DisplayName : "somebody";
            }
        }

        // =====================================================================
        // THE SHOT: pivot, azimuth, tilt, ground -> a seat and a rotation
        // =====================================================================

        /// The middle of the frame, in the world.
        public Vector3 Pivot => (focus ?? Vector3.zero) + Pan;

        ChaseCamera Chase()
        {
            if (chase != null) return chase;
            chase = Camera.main != null ? Camera.main.GetComponent<ChaseCamera>() : null;
            if (chase == null) chase = Object.FindFirstObjectByType<ChaseCamera>();
            return chase;
        }

        float Fov()
        {
            var c = Chase();
            return c != null ? c.OverviewFov : 36f;
        }

        /// **The tilt the zoom asks for, through (165 m, 32°) exactly.**
        ///
        /// Log-space because zoom is read as a ratio: 10 to 20 m is the same
        /// step to the eye as 250 to 500, so the angle should move by the same
        /// amount over each. Piecewise through three points rather than a
        /// fitted curve, because the points are what Kevin will move in the
        /// tuner and a fit would make "the 165 m shot is 32°" an approximation
        /// instead of a guarantee.
        public static float AutoTilt(float groundMetres)
        {
            float g = Mathf.Max(0.1f, groundMetres);
            float lo = Mathf.Log(Mathf.Max(0.1f, Feel.tiltLowGround));
            float mid = Mathf.Log(Mathf.Max(0.2f, Feel.tiltMidGround));
            float hi = Mathf.Log(Mathf.Max(0.3f, Feel.tiltHighGround));
            float u = Mathf.Log(g);

            float deg = u <= mid
                ? Mathf.LerpUnclamped(Feel.tiltLowDeg, Feel.tiltMidDeg,
                      Mathf.Abs(mid - lo) < 1e-5f ? 0f : (u - lo) / (mid - lo))
                : Mathf.LerpUnclamped(Feel.tiltMidDeg, Feel.tiltHighDeg,
                      Mathf.Abs(hi - mid) < 1e-5f ? 0f : (u - mid) / (hi - mid));
            return Mathf.Clamp(deg, Feel.minTiltDeg, Feel.maxTiltDeg);
        }

        /// The tilt and distance this state actually renders at, **including
        /// the terrain yield**.
        ///
        /// The yield lives HERE rather than only in `ChaseCamera`'s clamp so
        /// that the pose this component reasons about and the pose that is
        /// drawn are the same pose. The rig's clamp shoves the lens straight
        /// up without telling anybody, which leaves every screen ray aimed
        /// from a place the camera is not — the grab would slip the moment the
        /// shot crossed a ridge. Yielding here keeps them in step and leaves
        /// the rig's clamp as what it should be: a net.
        ///
        /// **Tilt first, then distance.** Standing up over a hill keeps the
        /// same ground in frame; backing off does not, and being pushed
        /// backwards by terrain reads as the zoom slipping.
        void Compose(Vector3 pivot, float ground, float azimuth, float bias,
                     out float tiltDeg, out float span)
        {
            float tanHalf = Mathf.Tan(Fov() * 0.5f * Mathf.Deg2Rad);
            span = Mathf.Max(1f, ground / (2f * tanHalf));

            // **Locked: one angle, at every zoom.** The curve and the hand
            // bias are both ignored outright rather than merely overridden
            // by a bigger number below, so nothing downstream of this line
            // can read "the curve, plus a bit" and be surprised later.
            bool locked = IslandCamLock.locked;
            tiltDeg = locked
                ? Mathf.Clamp(IslandCamLock.angleDeg, Feel.minTiltDeg, Feel.yieldMaxTiltDeg)
                : Mathf.Clamp(AutoTilt(ground) + bias, Feel.minTiltDeg, Feel.maxTiltDeg);
            // The hand sheet's hero shot leans toward its own pitch, and
            // then clears hills by distance like the lock does: tipping the
            // lens up over a ridge would undo the very angle it came for.
            if (heroBlend > 0.0005f)
            {
                tiltDeg = HeroTilt(tiltDeg);
                locked = true;
            }

            // A non-serialisable static, nulled by a play-mode recompile. No
            // height field means no yield, which is the right answer: the
            // rig's own clamp is still underneath.
            var height = GroundPick.Height;
            if (height == null || Feel.clearance <= 0f) return;

            for (int i = 0; i < 4; i++)
            {
                ChaseCamera.OverviewPose(ShotOf(pivot, azimuth, tiltDeg, span), Fov(),
                                         out Vector3 seat, out _);
                float need = height(seat.x, seat.z) + Feel.clearance;
                if (seat.y >= need - 0.001f) return;

                float rise = need - pivot.y;
                if (rise <= 0f) return;                 // the pivot is in the hill

                if (locked)
                {
                    // The angle cannot move, so the only lever left is
                    // distance: stand the seat further back along the SAME
                    // ray, which lifts it exactly as much as raising the
                    // tilt would have -- see the class doc on `IslandCamLock`.
                    float sinT = Mathf.Sin(tiltDeg * Mathf.Deg2Rad);
                    if (sinT <= 1e-3f) return;  // nearly level: distance can't lift the seat
                    span = Mathf.Max(span, rise / sinT);
                }
                else
                {
                    float wantTilt = Mathf.Asin(Mathf.Clamp01(rise / span)) * Mathf.Rad2Deg;
                    if (wantTilt <= Feel.yieldMaxTiltDeg)
                        tiltDeg = Mathf.Max(tiltDeg, wantTilt);
                    else
                    {
                        tiltDeg = Feel.yieldMaxTiltDeg;
                        span = rise / Mathf.Max(1e-3f,
                            Mathf.Sin(Feel.yieldMaxTiltDeg * Mathf.Deg2Rad));
                    }
                }
            }
        }

        static ChaseCamera.IslandShot ShotOf(Vector3 pivot, float azimuthDeg,
                                             float tiltDeg, float span)
        {
            float a = azimuthDeg * Mathf.Deg2Rad;
            return new ChaseCamera.IslandShot
            {
                centre = pivot,
                from = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)),
                tiltDeg = tiltDeg,
                span = span,
                free = true,
            };
        }

        /// The lens's own orientation for an azimuth and a tilt — looking from
        /// the seat back down at the pivot. Exactly what `OverviewPose`
        /// produces, expressed without needing a pivot, so a rigid rotation
        /// can be built out of two of them.
        static Quaternion AimRot(float azimuthDeg, float tiltDeg)
        {
            float a = azimuthDeg * Mathf.Deg2Rad, t = tiltDeg * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            Vector3 fwd = -dir * Mathf.Cos(t) - Vector3.up * Mathf.Sin(t);
            return Quaternion.LookRotation(fwd, Vector3.up);
        }

        /// **Move a pivot so that the whole rig turns about `about` instead of
        /// about itself.**
        ///
        /// Changing the azimuth or the tilt rotates the rig about its OWN
        /// pivot; the gestures want it rotated about the point the finger is
        /// on. The two differ by a translation, and the translation is exactly
        /// what carrying the pivot through the same rotation produces:
        /// `p' = about + Q(p − about)`, with Q the change in the lens's
        /// orientation.
        ///
        /// Q is built by DIVIDING the two orientations rather than by naming
        /// an axis and a sign. An axis-and-sign derivation is three chances to
        /// be wrong — Unity's handedness, the direction of the right vector,
        /// and which way a rising tilt turns — and every one of them looks
        /// almost right on screen.
        static Vector3 RotateAbout(Vector3 p, Vector3 about,
                                   float az0, float tilt0, float az1, float tilt1)
        {
            Quaternion q = AimRot(az1, tilt1) * Quaternion.Inverse(AimRot(az0, tilt0));
            return about + q * (p - about);
        }

        // --- the pose, cached for the frame ----------------------------------
        //
        // Every screen ray in a frame must be cast from the pose that was on
        // screen when the pointer was sampled -- which is the state as it
        // stood at the START of the frame, before any of this frame's gestures
        // moved it. Caching by frame number gets that for free and costs one
        // composition per frame instead of one per ray.

        int poseFrame = -1;
        Vector3 poseSeat;
        Quaternion poseRot = Quaternion.identity;
        float poseFov = 36f;
        bool poseOk;

        bool Pose(out Vector3 seat, out Quaternion rot, out float fov)
        {
            if (poseFrame != Time.frameCount)
            {
                poseFrame = Time.frameCount;
                poseOk = BuildPose(out poseSeat, out poseRot, out poseFov);
            }
            seat = poseSeat; rot = poseRot; fov = poseFov;
            return poseOk;
        }

        bool BuildPose(out Vector3 seat, out Quaternion rot, out float fov)
        {
            var c = Chase();
            fov = c != null ? c.OverviewFov : 36f;
            seat = Vector3.zero;
            rot = Quaternion.identity;
            if (subject == null || c == null) return false;

            if (!handsOn)
            {
                // Nobody is driving, so the shot is the rig's own composition
                // and the rig is the only thing that knows what it came out
                // as -- the legibility clamp and the hold-the-ship slide are
                // both its. Read back what it last put on screen.
                seat = c.LastOverviewSeat;
                Vector3 look = c.LastOverviewAim - seat;
                if (look.sqrMagnitude < 1e-6f) return false;
                rot = Quaternion.LookRotation(look, Vector3.up);
                return c.OverviewLevel > 0.5f;
            }

            Vector3 pivot = Pivot;
            Compose(pivot, Ground, azimuthDeg, tiltBiasDeg, out float tiltDeg, out float span);
            ChaseCamera.OverviewPose(ShotOf(pivot, azimuthDeg, tiltDeg, span), fov,
                                     out seat, out rot);
            return true;
        }

        // =====================================================================
        // TAKING HOLD
        // =====================================================================

        /// A gesture has arrived: stop tracking anybody, and latch if this is
        /// the first one.
        void Drive()
        {
            if (following != null) StopFollowing();
            HeroCancel();
            Latch();
        }

        /// **The first-drive latch, and why there is no pop.**
        ///
        /// Up to this moment the shot is the rig's: a centre somebody else
        /// composed, the authored 32°, a span the legibility clamp may have
        /// pulled in, and a frame slid sideways until the ship was inside it.
        /// The moment a hand arrives, three things change at once — `free`
        /// goes on (killing the clamp and the slide), `direct` goes on
        /// (killing the easing) and this component starts writing the azimuth
        /// and the tilt. Any of them alone would jump the picture.
        ///
        /// So none of them is allowed to decide anything. Everything is READ
        /// OFF what is on screen right now: the aim after the slide, the
        /// azimuth of the seat that was drawn, the span that was drawn, and
        /// the tilt bias that makes `AutoTilt` agree with it. The first frame
        /// after the latch is bit-for-bit the frame before it, and only then
        /// does the gesture move anything.
        ///
        /// It is also what stops `from` wobbling. The composed shot's bearing
        /// is taken from the SHIP, and she swings at anchor — so before the
        /// latch the island slowly rotates under a still camera.
        void Latch()
        {
            if (handsOn || subject == null) return;
            var c = Chase();
            if (c == null || c.OverviewLevel <= 0.5f) return;

            Vector3 aim = c.LastOverviewAim;
            Vector3 seat = c.LastOverviewSeat;
            Vector3 back = seat - aim;
            Vector3 flat = back; flat.y = 0f;
            if (flat.sqrMagnitude < 1e-4f) return;

            azimuthDeg = Mathf.Repeat(Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg, 360f);

            float span = Mathf.Max(1f, c.CurrentSpan);
            float tanHalf = Mathf.Tan(c.OverviewFov * 0.5f * Mathf.Deg2Rad);
            float ground = Mathf.Clamp(span * 2f * tanHalf, minGround, maxGround);
            float tilt = c.CurrentTilt > 0.01f
                ? c.CurrentTilt
                : Mathf.Asin(Mathf.Clamp(back.y / Mathf.Max(1f, back.magnitude), -1f, 1f))
                  * Mathf.Rad2Deg;

            Ground = wantGround = ground;
            // Locked: same reasoning as `Reground` -- see there.
            tiltBiasDeg = IslandCamLock.locked ? 0f : tilt - AutoTilt(ground);
            focus = aim;
            Pan = wantPan = Vector3.zero;
            following = null;
            panVel = Vector3.zero;
            orbitVel = 0f;
            trailN = 0;
            handsOn = true;
            Driven = true;
        }

        /// Move the pivot by a world offset, clamped to the reach. Returns how
        /// far the clamp pushed back, which is what tells a fling it has
        /// arrived at an edge.
        Vector3 MovePivot(Vector3 delta)
        {
            if (!focus.HasValue) return Vector3.zero;
            Vector3 wanted = Pivot + delta;
            Vector3 got = ClampPivot(wanted);
            Pan = wantPan = got - focus.Value;
            return got - wanted;
        }

        void SetPivot(Vector3 p)
        {
            if (!focus.HasValue) return;
            Pan = wantPan = ClampPivot(p) - focus.Value;
        }

        // NOTE: the cached pose is deliberately NOT thrown away when a gesture
        // moves the pivot. Within one frame every ray must still be cast from
        // the pose that was on SCREEN when the pointer was sampled; a gesture
        // that re-aimed the rays it is being solved from would be chasing a
        // target it was itself moving.

        /// **Where the pivot is allowed to be: the island, and the ship.**
        ///
        /// The island disc is the old rule and is about not getting lost at
        /// sea. The ship disc is new and is about the world actually being
        /// there: `TerrainStreamer` follows the SHIP, so on a 2 km island the
        /// far side is unloaded, and a camera sent over it frames nothing.
        ///
        /// Alternating projections, because the intersection of two discs has
        /// no closed form worth writing and they overlap enormously in every
        /// case that occurs (she is anchored a few tens of metres off the
        /// shore of the island she is governing).
        public Vector3 ClampPivot(Vector3 p)
        {
            if (subject == null) return p;
            Vector3 isle = subject.transform.position;
            float islandReach = Mathf.Max(60f, subject.Radius * panReach);
            Vector3 ship = transform.position;
            float shipReach = Mathf.Max(60f, Feel.reachFromShip);
            // **The shot's own centre is always admissible.** It is where the
            // view already was when a hand arrived, and a reach rule that
            // moved the picture at the moment of the first touch would be a
            // pop with a good excuse -- on a big island the composed aim is
            // the island's middle, which can be further from her than this
            // reach. The rule is about how far a hand may WALK the frame, not
            // about refusing the frame it started in.
            if (focus.HasValue)
                shipReach = Mathf.Max(shipReach,
                    World.Island.FlatDistance(focus.Value, ship));
            // **While siting a watchtower onto a wall (2026-09-27):
            // `CampSiting` widens this to the camp's own walls' extent, so a
            // run built past the campfire's `TownRadius` is still on screen
            // to drag or tap at -- see `ExtraReachCentre`. Additive only: it
            // never pulls the reach IN, only ever lets it stand as it was.
            if (ExtraReachCentre.HasValue)
                shipReach = Mathf.Max(shipReach,
                    World.Island.FlatDistance(ExtraReachCentre.Value, ship) + ExtraReachRadius);
            for (int i = 0; i < 8; i++)
            {
                p = ClampDisc(p, ship, shipReach);
                p = ClampDisc(p, isle, islandReach);
            }
            return p;
        }

        static Vector3 ClampDisc(Vector3 p, Vector3 centre, float radius)
        {
            Vector3 off = p - centre;
            off.y = 0f;
            if (off.sqrMagnitude <= radius * radius) return p;
            Vector3 q = centre + off.normalized * radius;
            return new Vector3(q.x, p.y, q.z);
        }

        /// The zoom, clamped — and the one rule that is not about the lens.
        float ClampGround(float g, Vector3 at)
        {
            g = Mathf.Clamp(g, minGround, maxGround);
            if (g < Feel.nearGround)
            {
                float d = World.Island.FlatDistance(at, transform.position);
                if (d > Feel.nearGroundReach) g = Mathf.Max(g, Feel.nearGround);
            }
            return g;
        }

        // --- the throw --------------------------------------------------------

        struct Sample { public float t; public Vector3 p; public float az; }
        readonly Sample[] trail = new Sample[16];
        int trailN;

        void Record(Vector3 p)
        {
            trail[trailN % trail.Length] = new Sample
            { t = Time.unscaledTime, p = p, az = 0f };
            trailN++;
        }

        void RecordOrbit(float dAz)
        {
            float dt = Time.unscaledDeltaTime;
            trail[trailN % trail.Length] = new Sample
            {
                t = Time.unscaledTime,
                p = Pivot,
                az = dt > 1e-4f ? dAz / dt : 0f,
            };
            trailN++;
        }

        /// Mean pointer speed over the last `flingSample` seconds.
        ///
        /// Mean, not the last frame's: one frame is a sample of a hand, and a
        /// hand that has stopped still has one last jittery frame in it. The
        /// window is short enough that a flick survives it and a drag that
        /// came to rest does not.
        Vector3 MeanVelocity()
        {
            int n = Mathf.Min(trailN, trail.Length);
            if (n < 2) return Vector3.zero;
            float now = Time.unscaledTime;
            int newest = (trailN - 1) % trail.Length;
            int oldest = newest;
            for (int i = 1; i < n; i++)
            {
                int k = ((trailN - 1 - i) % trail.Length + trail.Length) % trail.Length;
                // **The sample before the last one is always in**, unless it is
                // genuinely stale. The window alone made the throw depend on
                // the FRAME RATE: at under ~12 fps no second sample is ever
                // inside 80 ms, the span is zero, and letting go of the land
                // did nothing at all -- which `IslandCamProbe` caught only on
                // the runs where the editor happened to be slow. A hand that
                // had stopped before it let go still throws nothing: it goes
                // on recording the same place every frame.
                float age = now - trail[k].t;
                if (i == 1 ? age > Feel.flingStale : age > Feel.flingSample) break;
                oldest = k;
            }
            float dt = trail[newest].t - trail[oldest].t;
            if (dt < 1e-3f) return Vector3.zero;
            Vector3 v = (trail[newest].p - trail[oldest].p) / dt;
            v.y = 0f;
            return v;
        }

        float MeanOrbitSpeed()
        {
            int n = Mathf.Min(trailN, trail.Length);
            if (n < 1) return 0f;
            float now = Time.unscaledTime;
            float sum = 0f; int used = 0;
            for (int i = 0; i < n; i++)
            {
                int k = ((trailN - 1 - i) % trail.Length + trail.Length) % trail.Length;
                // As `MeanVelocity`: the newest step always counts unless stale,
                // or a swing thrown at a low frame rate is not thrown at all.
                float age = now - trail[k].t;
                if (i == 0 ? age > Feel.flingStale : age > Feel.flingSample) break;
                sum += trail[k].az; used++;
            }
            return used > 0 ? sum / used : 0f;
        }

        void Glide(float dt)
        {
            if (grabbing || orbiting || !handsOn) return;
            bool moving = panVel.sqrMagnitude > 1e-6f || Mathf.Abs(orbitVel) > 1e-3f;
            if (!moving) return;

            flingFor += dt;
            if (flingFor > Feel.flingSeconds) { panVel = Vector3.zero; orbitVel = 0f; return; }

            if (panVel.sqrMagnitude > 1e-6f)
            {
                Vector3 pushedBack = MovePivot(panVel * dt);
                // At an edge, take out only the part of the throw that is
                // driving into it. Killing the whole thing would stop a fling
                // that is mostly sliding ALONG the shore.
                if (pushedBack.sqrMagnitude > 1e-6f)
                {
                    Vector3 inward = pushedBack.normalized;
                    float into = Vector3.Dot(panVel, inward);
                    if (into < 0f) panVel -= inward * into;
                }
                panVel *= Mathf.Exp(-Feel.flingDecay * dt);
                if (panVel.magnitude < Feel.flingStop) panVel = Vector3.zero;
            }

            if (Mathf.Abs(orbitVel) > 1e-3f)
            {
                // About the middle of the frame, which after `Reground` is the
                // pivot -- so the swing costs nothing but an angle.
                azimuthDeg = Mathf.Repeat(azimuthDeg + orbitVel * dt, 360f);
                orbitVel *= Mathf.Exp(-Feel.flingDecay * dt);
                if (Mathf.Abs(orbitVel) < Feel.orbitFlingStop) orbitVel = 0f;
            }
        }

        // --- what the tuner and the probe read --------------------------------

        /// The seat and aim this component is asking for, rebuilt now. For the
        /// tuner's readout and for the probe, which must never measure the
        /// camera by the same arithmetic that placed it -- it projects through
        /// the real `Camera.main` instead and compares the two.
        public bool VirtualPose(out Vector3 seat, out Quaternion rot, out float fov)
            => BuildPose(out seat, out rot, out fov);

        /// Degrees above the horizon the shot is composed at right now,
        /// including the terrain yield.
        public float TiltNow
        {
            get
            {
                if (!handsOn)
                {
                    var c = Chase();
                    return c != null && c.CurrentTilt > 0.01f
                        ? c.CurrentTilt : ChaseCamera.DefaultOverviewTilt;
                }
                Compose(Pivot, Ground, azimuthDeg, tiltBiasDeg, out float t, out _);
                return t;
            }
        }

        public float AzimuthDeg => azimuthDeg;
        public float TiltBiasDeg => tiltBiasDeg;
        /// True once a gesture has taken hold of the view. `Driven` also goes
        /// true for a `LookAt` or a `ZoomTo`, which are NOT hands on the land.
        public bool HandsOn => handsOn;

        // =====================================================================
        // THE HERO SHOT: a hand sheet's villager, framed above the sheet
        //
        // V2 Split (Kevin, 2026-09-27). `Sheets.Open` -> `Follow` -> here.
        // Takes hold of the view the way a gesture does (`Latch`, so the
        // first frame is the frame before it), then eases the pivot, the
        // zoom and a tilt lean toward a pose that puts him in the middle of
        // the screen the sheet leaves visible, re-solved every frame so the
        // sheet's own top edge (any height) and his walking both carry
        // through. Any gesture cancels it until a hand sheet opens again
        // (`Drive` -> `HeroCancel`); closing the sheet eases back to the
        // view from before it opened (`HeroStop`).
        // =====================================================================

        Transform heroTarget;       // who; null (or an unlit body) = hold still
        bool heroOn;                // a hand sheet is open and asked for this
        bool heroCancelled;         // a hand took the view -- hold until reopened
        bool heroRestoring;         // easing back to `saved*` after the close
        float heroBlend;            // 0 = the island's own tilt, 1 = the hero lean
        float heroCamTiltDeg = 28f; // the lens tilt that puts `pitchDeg` on him
        float heroBlendVel, heroGroundVel;
        Vector3 heroPivotVel;
        float heroRestoreFor;

        bool savedValid, savedPoseValid, savedHandsOn, savedDriven;
        Vector3? savedFocus;
        Vector3 savedPan, savedPivot;
        float savedGround, savedRawGround;
        Transform savedFollowing;

        /// True while the hand sheet's hero shot is driving the view.
        public bool HeroActive => heroOn && !heroCancelled;
        public float HeroBlend => heroBlend;

        float HeroTilt(float baseTilt)
        {
            if (IslandHeroCam.pitchDeg <= 0.01f) return baseTilt;
            return Mathf.Lerp(baseTilt, heroCamTiltDeg, Mathf.Clamp01(heroBlend));
        }

        /// Frame this villager above the sheet. Switching villager just
        /// re-targets: the damped pivot swings over from where it is, and
        /// the view to go back to is still the one from before the FIRST
        /// sheet. A body that is switched off (an unwatched camp's invisible
        /// walker) is not tracked -- the camera holds where it is.
        public void HeroFollow(Transform who)
        {
            if (subject == null) return;
            if (!heroOn && !heroRestoring)
            {
                savedValid = true;
                savedHandsOn = handsOn;
                savedDriven = Driven;
                savedFocus = focus;
                savedPan = Pan;
                savedRawGround = Ground;
                savedFollowing = following == who ? null : following;
                savedPoseValid = handsOn;
                if (handsOn) { savedPivot = Pivot; savedGround = Ground; }
            }
            if (!heroOn) { heroPivotVel = Vector3.zero; heroGroundVel = 0f; }
            heroOn = true;
            heroCancelled = false;
            heroRestoring = false;
            heroTarget = who;
            following = null;
            if (handsOn) KillMotion();
        }

        /// The sheet closed (or another kind opened): ease back, unless a
        /// hand took the view meanwhile -- then only the tilt lean eases out.
        public void HeroStop()
        {
            if (!heroOn) return;
            heroOn = false;
            heroTarget = null;
            if (!heroCancelled && savedValid) { heroRestoring = true; heroRestoreFor = 0f; }
            else savedValid = false;
            heroCancelled = false;
        }

        /// A gesture arrived: the player has the view now.
        void HeroCancel()
        {
            if (heroOn) { heroCancelled = true; heroTarget = null; }
            heroRestoring = false;
            savedValid = false;
        }

        /// Something else aimed the view (a `LookAt`, a fly, a new island).
        void HeroDrop()
        {
            heroOn = false;
            heroCancelled = false;
            heroRestoring = false;
            heroTarget = null;
            savedValid = false;
        }

        void HeroStep(float dt)
        {
            if (dt <= 0f) return;
            bool live = heroOn && !heroCancelled && heroTarget != null
                        && heroTarget.gameObject.activeInHierarchy;
            if (live)
            {
                if (!handsOn)
                {
                    if (!Ready) return;
                    Latch();
                    if (!handsOn) return;
                    if (savedValid && !savedPoseValid)
                    {
                        savedPivot = Pivot;
                        savedGround = Ground;
                        savedPoseValid = true;
                    }
                }
                float ease = Mathf.Max(0.05f, IslandHeroCam.easeIn);
                Vector3 him = heroTarget.position + Vector3.up * IslandHeroCam.aimHeight;
                float tanHalf = Mathf.Tan(Fov() * 0.5f * Mathf.Deg2Rad);
                Vector2 uv = HeroScreenSpot();
                // The lean: whatever lens tilt puts `pitchDeg` on the ray
                // through `uv` (exact on the vertical centre line).
                float ny = (uv.y - 0.5f) * 2f * tanHalf;
                heroCamTiltDeg = Mathf.Clamp(IslandHeroCam.pitchDeg + Mathf.Atan(ny) * Mathf.Rad2Deg,
                                             Feel.minTiltDeg, Feel.yieldMaxTiltDeg);
                heroBlend = Mathf.SmoothDamp(heroBlend, 1f, ref heroBlendVel, ease, Mathf.Infinity, dt);

                float dist = HudLayout.Wide ? IslandHeroCam.distanceWide : IslandHeroCam.distanceTall;
                float goalGround = ClampGround(Mathf.Max(1f, dist) * 2f * tanHalf, him);
                Ground = wantGround = Mathf.SmoothDamp(Ground, goalGround, ref heroGroundVel,
                                                       ease, Mathf.Infinity, dt);

                Vector3 goal = HeroPivot(him, uv, tanHalf);
                SetPivot(Vector3.SmoothDamp(Pivot, goal, ref heroPivotVel, ease, Mathf.Infinity, dt));
                return;
            }
            if (heroOn) { heroPivotVel = Vector3.zero; heroGroundVel = 0f; return; }  // hold

            if (heroRestoring)
            {
                if (!savedPoseValid || !handsOn) { HeroFinishRestore(); return; }
                heroRestoreFor += dt;
                float ease = Mathf.Max(0.05f, IslandHeroCam.easeOut);
                heroBlend = Mathf.SmoothDamp(heroBlend, 0f, ref heroBlendVel, ease, Mathf.Infinity, dt);
                Ground = wantGround = Mathf.SmoothDamp(Ground, savedGround, ref heroGroundVel,
                                                       ease, Mathf.Infinity, dt);
                SetPivot(Vector3.SmoothDamp(Pivot, savedPivot, ref heroPivotVel, ease, Mathf.Infinity, dt));
                bool there = (Pivot - savedPivot).sqrMagnitude < 0.0025f
                             && Mathf.Abs(Ground - savedGround) < 0.05f && heroBlend < 0.002f;
                if (there || heroRestoreFor > ease * 8f + 1f) HeroFinishRestore();
                return;
            }

            if (heroBlend > 0f)
            {
                heroBlend = Mathf.SmoothDamp(heroBlend, 0f, ref heroBlendVel,
                    Mathf.Max(0.05f, IslandHeroCam.easeOut), Mathf.Infinity, dt);
                if (heroBlend < 0.0005f) { heroBlend = 0f; heroBlendVel = 0f; }
            }
        }

        /// Back to exactly the state before the sheet opened. A view nobody
        /// had taken hold of goes back to being the rig's composition (the
        /// pivot has just been eased onto that composition's own aim, so
        /// letting go is not a pop).
        void HeroFinishRestore()
        {
            heroRestoring = false;
            heroBlend = 0f;
            heroBlendVel = heroGroundVel = 0f;
            heroPivotVel = Vector3.zero;
            if (savedValid && !savedHandsOn && handsOn)
            {
                LetGo();
                focus = savedFocus;
                Pan = wantPan = savedPan;
                Ground = wantGround = savedRawGround;
                Driven = savedDriven;
                following = savedFollowing != null && savedFollowing.gameObject.activeInHierarchy
                    ? savedFollowing : null;
            }
            savedValid = false;
        }

        /// Where on the screen he should sit (0..1 viewport): the middle of
        /// what the sheet leaves -- above its top edge upright, left of it
        /// on a desk -- under the resource bar.
        Vector2 HeroScreenSpot()
        {
            float W = Mathf.Max(1f, Screen.width), H = Mathf.Max(1f, Screen.height);
            var safe = Screen.safeArea;
            if (safe.width < 1f || safe.height < 1f) safe = new Rect(0f, 0f, W, H);
            // Screen space, origin bottom-left.
            float left = safe.xMin, right = safe.xMax, bottom = safe.yMin, top = safe.yMax;
            if (UI.Sheets.SheetHost.FrameOpen)
            {
                Rect f = UI.Sheets.SheetHost.FrameRect;      // GUI space, top-left
                if (HudLayout.Wide) right = Mathf.Min(right, f.xMin);
                else bottom = Mathf.Max(bottom, H - f.yMin);   // the sheet's top edge
            }
            if (UI.Sheets.MidnightLandHud.Active)
            {
                Rect bar = UI.Sheets.MidnightLandHud.ResourcesRect;
                if (bar.height > 0f) top = Mathf.Min(top, H - bar.yMax);
            }
            if (right - left < 1f) { left = safe.xMin; right = safe.xMax; }
            if (top - bottom < 1f) { bottom = safe.yMin; top = safe.yMax; }
            float x = (left + right) * 0.5f;
            float y = Mathf.Lerp(bottom, top, Mathf.Clamp01(IslandHeroCam.rectY));
            return new Vector2(x / W, y / H);
        }

        /// The pivot that puts `him` at viewport `uv`, `span` from the lens,
        /// at the tilt the current state composes to. The lens is stood on
        /// the ray through `uv`; the pivot is then its own centre ray's
        /// point at the same span, which is what `OverviewPose` inverts.
        Vector3 HeroPivot(Vector3 him, Vector2 uv, float tanHalf)
        {
            float span = Mathf.Max(1f, Ground / (2f * tanHalf));
            Compose(him, Ground, azimuthDeg, tiltBiasDeg, out float tilt, out _);
            Quaternion rot = AimRot(azimuthDeg, tilt);
            float aspect = Mathf.Max(1f, Screen.width) / Mathf.Max(1f, Screen.height);
            Vector3 ray = (rot * new Vector3((uv.x - 0.5f) * 2f * tanHalf * aspect,
                                             (uv.y - 0.5f) * 2f * tanHalf, 1f)).normalized;
            Vector3 seat = him - ray * span;
            return seat + rot * Vector3.forward * span;
        }
    }
}
