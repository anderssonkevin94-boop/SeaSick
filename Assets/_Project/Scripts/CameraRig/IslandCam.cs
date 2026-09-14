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
        [Tooltip("Closest. About a hut and the people round it — at 40 m a 1.7 m crewman is roughly a twentieth of the frame.")]
        [SerializeField] float minGround = 40f;
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

        float wantGround;
        Vector3 wantPan;
        World.Island subject;

        void Awake() { Ground = wantGround = defaultGround; }

        /// Point it at a new place. Resets the zoom and the pan, because a
        /// frame walked to the north end of one island means nothing at the
        /// next one.
        public void Focus(World.Island isle)
        {
            if (subject == isle) return;
            subject = isle;
            wantGround = Ground = defaultGround;
            wantPan = Pan = Vector3.zero;
            Driven = false;
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

            // Keep it over the island. Walking the frame out to sea shows you
            // nothing and is the easiest way to get lost in a top-down view.
            float reach = Mathf.Max(60f, subject.Radius * panReach);
            if (wantPan.sqrMagnitude > reach * reach)
                wantPan = wantPan.normalized * reach;

            // Home the frame with the END key -- cheaper than panning back.
            if (keys != null && keys.endKey.wasPressedThisFrame) { wantPan = Vector3.zero; Driven = true; }

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
            shot.centre += Pan;
            shot.ground = Ground;
            // Only once they have actually touched it: an untouched view must
            // be exactly the shipped composition, legibility clamp and all.
            shot.free = Driven;
            return shot;
        }

        /// One line for the HUD, in the units the shot is authored in.
        public string Readout =>
            $"view {Ground:F0} m of ground   ·   {(Pan.magnitude < 1f ? "centred" : $"{Pan.magnitude:F0} m off centre")}";
    }
}
