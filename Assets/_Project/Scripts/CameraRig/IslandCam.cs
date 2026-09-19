using SeaSick.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SeaSick.CameraRig
{
    /// The player's hands on the island view: zoom in on the people, or pull
    /// back until the whole island is in frame, and walk the frame about with
    /// the arrow keys.
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
    /// Only live while she is lying at an island, and it stands aside entirely
    /// while a dev tool has the arrow keys.
    public class IslandCam : MonoBehaviour
    {
        [Header("Zoom, in metres of ground up the frame")]
        [Tooltip("Closest. At 18 m the lens is about 15 m above the ground and a 1.7 m crewman is a tenth of the frame — close enough to watch one person work. It was 40 m, which stopped being a sensible floor the day the view started dropping to 35 m above a campfire: that shot is 43 m of ground, so 'zoom in' bought 7% and read as broken.")]
        [SerializeField] float minGround = 18f;
        [Tooltip("Furthest. Far enough to hold a whole island of the size the world makes now; raise it if the islands grow.")]
        [SerializeField] float maxGround = 520f;
        [Tooltip("Where it starts, and what it returns to at a new island. The authored dock shot's own zoom.")]
        [SerializeField] float defaultGround = 165f;
        [Tooltip("Fraction of the current zoom added or removed per second of held input. Proportional, so a step feels the same close in and far out.")]
        [SerializeField] float zoomRate = 0.9f;

        [Header("Panning")]
        [Tooltip("Metres per second at the DEFAULT zoom. Scaled by how far out you are, so the frame crosses the view at the same rate however wide it is.")]
        [SerializeField] float panRate = 55f;
        [Tooltip("How far from the middle of the island the frame may be walked, as a multiple of the island's own radius.")]
        [SerializeField] float panReach = 1.15f;

        [Header("Feel")]
        [SerializeField] float smoothing = 9f;

        /// Metres of ground up the frame, as asked for.
        public float Ground { get; private set; }
        /// Where the frame has been walked to, relative to the shot's own centre.
        public Vector3 Pan { get; private set; }
        /// True once the player has touched anything — the shot only goes
        /// `free` then, so an untouched view is exactly the shipped one.
        public bool Driven { get; private set; }

        [Header("Following somebody")]
        [Tooltip("How near the tap has to land, in fractions of screen HEIGHT, to count as pressing on a crewman. A fraction rather than pixels because a phone and a desk window do not have the same pixels.")]
        [SerializeField, Range(0.02f, 0.2f)] float pickRadius = 0.07f;

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

        /// Is the island view live at all? Read by `HelmInput`, which shares
        /// the lower half of the screen with it.
        public static bool Engaged { get; private set; }

        /// What the view is centred on, for the readout and the probe.
        public Vector3? FocusPoint => focus;
        public Transform Following => following;

        void Awake() { Ground = wantGround = defaultGround; }

        void OnDisable() { Engaged = false; }

        /// Point it at a new place. Resets the zoom and the pan, because a
        /// frame walked to the north end of one island means nothing at the
        /// next one.
        public void Focus(World.Island isle)
        {
            Engaged = isle != null;
            if (subject == isle) return;
            subject = isle;
            wantGround = Ground = defaultGround;
            wantPan = Pan = Vector3.zero;
            Driven = false;
            focus = null;
            following = null;
        }

        /// **Centre the view on a point, at a given height above it.**
        ///
        /// Height, not coverage, because that is how the request arrives — "35
        /// m above the campfire" — and `ChaseCamera` owns the triangle that
        /// converts one into the other. Counts as driving: a view somebody has
        /// pointed at something must not also be sliding about to keep the
        /// ship in frame.
        public void LookAt(Vector3 point, float heightMetres)
        {
            focus = point;
            following = null;
            wantPan = Pan = Vector3.zero;
            var chase = Camera.main != null ? Camera.main.GetComponent<ChaseCamera>() : null;
            if (chase == null) chase = Object.FindFirstObjectByType<ChaseCamera>();
            if (chase != null) wantGround = Mathf.Clamp(
                chase.OverviewGroundForHeight(heightMetres), minGround, maxGround);
            Driven = true;
        }

        /// Keep this transform in the middle of the frame until told otherwise.
        /// The zoom is left alone -- whoever is watching chose it.
        public void FollowThis(Transform who)
        {
            if (who == null) return;
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
            focus = null;
            following = null;
            wantPan = Pan = Vector3.zero;
        }

        void Update()
        {
            if (subject == null) return;

            // A dev tool that is open owns the arrow keys. `IslandTuner` says
            // as much on its own switch, and two things reading the same key
            // is how a tuner becomes untunable.
            if (DevTools.Open != null) { Settle(); return; }

            float dt = Time.unscaledDeltaTime;

            // --- zoom ---------------------------------------------------------
            // Proportional, not additive: 10 m a second is a lurch at 40 m and
            // imperceptible at 500, and the eye reads zoom as a ratio anyway.
            float zoom = 0f;
            var keys = Keyboard.current;
            if (keys != null && (keys.equalsKey.isPressed || keys.numpadPlusKey.isPressed)) zoom -= 1f;
            if (keys != null && (keys.minusKey.isPressed || keys.numpadMinusKey.isPressed)) zoom += 1f;
            // Input System reports wheel motion in native units (120 per
            // detent), whereas the old API returned detents.
            float wheel = Mouse.current != null ? Mouse.current.scroll.ReadValue().y / 120f : 0f;
            if (Mathf.Abs(wheel) > 0.01f) zoom -= wheel * 3.5f;

            if (Mathf.Abs(zoom) > 0.0001f)
            {
                wantGround *= 1f + zoomRate * zoom * dt;
                wantGround = Mathf.Clamp(wantGround, minGround, maxGround);
                Driven = true;
            }

            // --- pan ----------------------------------------------------------
            float x = 0f, z = 0f;
            if (keys != null && keys.leftArrowKey.isPressed) x -= 1f;
            if (keys != null && keys.rightArrowKey.isPressed) x += 1f;
            if (keys != null && keys.upArrowKey.isPressed) z += 1f;
            if (keys != null && keys.downArrowKey.isPressed) z -= 1f;

            if (x != 0f || z != 0f)
            {
                // Pan in the frame's own axes, not the world's. The overview
                // sits on whatever bearing the ship came in on, so "up" has to
                // mean up the screen or the controls are a puzzle.
                var cam = Camera.main;
                Vector3 fwd = cam != null ? cam.transform.forward : Vector3.forward;
                Vector3 right = cam != null ? cam.transform.right : Vector3.right;
                fwd.y = 0f; right.y = 0f;
                if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
                fwd.Normalize(); right.Normalize();

                // Scale with the zoom so the frame crosses the view at the same
                // rate however wide it is -- at 500 m a fixed 55 m/s crawls.
                float rate = panRate * (wantGround / Mathf.Max(1f, defaultGround));
                wantPan += (right * x + fwd * z) * rate * dt;
                Driven = true;
            }

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

            // Keep it over the island. Walking the frame out to sea shows you
            // nothing and is the easiest way to get lost in a top-down view.
            //
            // The clamp is on WHERE THE FRAME ENDS UP, not on how far it has
            // been walked, because since the view can be re-centred on a
            // campfire or a crewman the pan is an offset from something that
            // is already off the island's middle.
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

            // Home the frame with the END key -- cheaper than panning back,
            // and the one way back to the shot the dock composed.
            if (keys != null && keys.endKey.wasPressedThisFrame)
            {
                Release();
                Driven = true;
            }

            PickCrew();
            Settle();
        }

        /// **Press on a crewman and the view follows them.**
        ///
        /// Picked by projecting each body to the screen and taking the nearest
        /// within `pickRadius` of the tap, NOT by raycasting a collider: the
        /// crew are 388-triangle characters with no colliders on them, and
        /// giving twenty of them colliders so that a camera can be aimed would
        /// be paying physics for an interface. Projection costs one transform
        /// per body on the frame a finger goes down.
        void PickCrew()
        {
            var pointer = Pointer.current;
            if (pointer == null || !pointer.press.wasPressedThisFrame) return;
            if (UI.CampSiting.Placing) return;          // that tap is siting a building
            Vector2 p = pointer.position.ReadValue();
            if (UIBlocker.Blocked(p)) return;

            var cam = Camera.main;
            if (cam == null) return;

            float best = float.MaxValue;
            Transform picked = null;
            float limit = pickRadius * Screen.height;
            float limitSq = limit * limit;

            // Everybody on their feet near this island: the hands who live
            // here and the shore party off the ship both read as villagers to
            // somebody looking down at them.
            var all = Object.FindObjectsByType<Crew.CrewAgent>(FindObjectsSortMode.None);
            foreach (var a in all)
            {
                if (a == null || !a.gameObject.activeInHierarchy) continue;
                Vector3 at = a.transform.position + Vector3.up * 0.9f;
                if (World.Island.FlatDistance(at, subject.transform.position)
                    > subject.Radius + 120f) continue;
                Vector3 sp = cam.WorldToScreenPoint(at);
                if (sp.z <= 0f) continue;              // behind the lens
                float d = (new Vector2(sp.x, sp.y) - p).sqrMagnitude;
                if (d < limitSq && d < best) { best = d; picked = a.transform; }
            }

            if (picked != null) FollowThis(picked);
            else StopFollowing();       // a tap on empty ground lets them go
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
    }
}
