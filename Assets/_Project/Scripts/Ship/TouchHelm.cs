using SeaSick.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using ET = UnityEngine.InputSystem.EnhancedTouch;

namespace SeaSick.Ship
{
    /// **Floating stick, point her where you want to go.**
    ///
    /// Replaces the axis-locked swipe wheel (2026-09-22): Kevin played that
    /// version and called it "slow, uneventful, not responsive" — a rubber-
    /// banding rudder that snapped back the moment you let go, and a
    /// telegraph that only moved in discrete steps on a flick. This control
    /// is the opposite of both: put a thumb down anywhere in the lower half
    /// of the screen and a stick appears under it; the DIRECTION you drag is
    /// the heading you want her pointed, the DISTANCE is how hard, and she
    /// keeps doing it after you let go. One thumb sets a course and a speed
    /// in one gesture and then is free.
    ///
    /// ## This class owns the gesture, not the policy
    ///
    /// `TouchHelm` turns one finger into three numbers — `DragHeadingDeg` (a
    /// WORLD heading, camera-independent), `DragDistance01` (how far out of
    /// the dead zone, unclamped so past-the-rim is visible), and `Tapped` (a
    /// stop order) — and draws the stick. `HelmInput` decides what those mean
    /// (astern vs. come-about, the throttle curve, the Kp/Kd heading
    /// autopilot) and is the thing with `Update()`.
    ///
    /// ## Camera-relative, frozen at touch-down
    ///
    /// "Screen up" has to mean something in the WORLD, and the chase camera
    /// rotates with the ship (`ChaseCamera`), so screen-up can't be read once
    /// and reused — holding the stick "up" while she comes round would have
    /// the camera's up rotate with her and the ship chase her own tail
    /// forever. The camera's flattened forward/right are captured the
    /// instant the thumb goes down and used for the whole drag; the heading
    /// that comes out is a plain world-space number from then on, so once the
    /// thumb lifts nothing about it depends on the camera any more.
    ///
    /// ## Multitouch, keyed by touch id — and released without trusting the
    /// phase
    ///
    /// Same discipline as the old wheel: the stick remembers the touch id
    /// that grabbed it and ignores every other contact, and a touch that
    /// vanishes without an `Ended` phase (app switch, palm rejection) is
    /// still released by the not-seen-this-frame sweep. `Pointer.current` is
    /// the editor/desktop mouse fallback, consulted only when the glass is
    /// empty.
    public sealed class TouchHelm
    {
        // --- gesture geometry, all in SCREEN PIXELS -------------------------

        /// The gesture zone is the whole lower half of the screen (Kevin,
        /// 2026-09-22: "the steering needs to be applicable on the whole
        /// bottom half of the screen"), minus whatever another control has
        /// already claimed with `UIBlocker`.
        const float ZoneFrac = 0.5f;
        /// Ring radius as a fraction of the shorter screen dimension — big
        /// enough to find and move inside of with a thumb, DPI-sane on both
        /// the phone and the desk.
        const float RingRadiusFrac = 0.14f;
        /// Dead zone, as a fraction of the ring radius: inside this the stick
        /// reads centred — no heading, no throttle — so a thumb that isn't
        /// quite still doesn't order a course change.
        public const float DeadZoneFrac = 0.10f;
        /// How far past the rim the drawn knob still tracks the thumb before
        /// visually clamping there. The ORDER (in `HelmInput`) keeps climbing
        /// into the burn tier past 100%; the drawing stops a little further
        /// out so a thumb pushed to the edge of its reach still has somewhere
        /// to go without the knob leaving the ring.
        const float KnobClampFrac = 1.25f;
        /// Touch shorter than this, moved less than the dead zone: a tap, not
        /// a drag. `HelmInput` reads it as "ring the telegraph to stop."
        const float TapMaxSeconds = 0.2f;
        /// How long the ring takes to fade out after release.
        const float FadeSeconds = 0.30f;

        const int NoTouch = int.MinValue;
        const int MouseId = NoTouch + 1;

        // --- state read by HelmInput ----------------------------------------

        /// True while a finger owns the stick.
        public bool Dragging => wheelId != NoTouch;
        /// One-`Sample()`-call pulse: the finger just lifted having barely
        /// moved and barely stayed — ring the telegraph to stop.
        public bool Tapped { get; private set; }
        /// One-`Sample()`-call pulse on ANY release, drag or tap.
        public bool JustReleased { get; private set; }
        /// The WORLD heading (degrees, `Mathf.Atan2` convention matching
        /// `ShipMotor.Heading`) the current drag direction implies. Only
        /// meaningful while `HasDragDirection` is true.
        public float DragHeadingDeg { get; private set; }
        /// False inside the dead zone, or the instant a drag begins before it
        /// has moved anywhere — a direction cannot be read from no movement.
        public bool HasDragDirection { get; private set; }
        /// Drag distance / ring radius. NOT clamped to 1 — past the rim is
        /// exactly how the burn tier is reached, and how "how hard" reads
        /// past "all the way."
        public float DragDistance01 { get; private set; }

        int wheelId = NoTouch;
        Vector2 anchorPoint;   // GUI space, where the thumb went down
        Vector2 knobPoint;     // GUI space, current/last drawn knob position
        Vector3 camRightFlat, camForwardFlat; // frozen at touch-down
        float touchDownTime;
        float ringRadiusPx;
        float fade;            // 0..1, ring opacity once nothing is dragging
        bool seenWheel;

        /// Drop the gesture immediately, no tap/release policy fires. Used
        /// when something OUTSIDE the control needs the wheel to let go right
        /// now — `HelmInput.AllStop()` calls this when the anchor goes down,
        /// so a drag in progress can't keep ordering a course for a ship that
        /// is no longer free to take one.
        public void CancelDrag()
        {
            wheelId = NoTouch;
            HasDragDirection = false;
            DragDistance01 = 0f;
            fade = 0f;
        }

        // =====================================================================
        // Gesture
        // =====================================================================

        /// Call once per Update, BEFORE the orders are read.
        ///
        /// `active` is false while the island view is up: the stick must not
        /// be grabbable then, or a drag meant for the ground (siting a
        /// building, pressing on a crewman) would ring the engine up instead.
        public void Sample(bool active)
        {
            Tapped = false;
            JustReleased = false;

            if (!active)
            {
                wheelId = NoTouch;
                HasDragDirection = false;
                DragDistance01 = 0f;
                fade = Mathf.Max(0f, fade - Time.deltaTime / FadeSeconds);
                return;
            }

            seenWheel = false;
            bool anyTouch = false;
            ringRadiusPx = RingRadiusFrac * Mathf.Min(Screen.width, Screen.height);

            if (ET.EnhancedTouchSupport.enabled && Touchscreen.current != null)
            {
                var touches = ET.Touch.activeTouches;
                for (int i = 0; i < touches.Count; i++)
                {
                    var t = touches[i];
                    var ph = t.phase;
                    if (ph == UnityEngine.InputSystem.TouchPhase.Ended
                        || ph == UnityEngine.InputSystem.TouchPhase.Canceled) continue;
                    anyTouch = true;
                    Feed(t.touchId, t.screenPosition,
                         ph == UnityEngine.InputSystem.TouchPhase.Began);
                }
            }

            // Editor/desktop mouse. Only consulted when the glass is empty,
            // so a simulated pointer riding alongside real touches cannot
            // fight a real thumb on a phone.
            if (!anyTouch)
            {
                var p = Pointer.current;
                if (p != null && p.press.isPressed)
                    Feed(MouseId, p.position.ReadValue(), p.press.wasPressedThisFrame);
            }

            // Not seen this frame means it has lifted, whatever its phase
            // said — the only way a palm rejection or an app switch is ever
            // caught.
            if (!seenWheel && wheelId != NoTouch) Release();

            fade = Dragging ? 1f : Mathf.Max(0f, fade - Time.deltaTime / FadeSeconds);
        }

        void Feed(int id, Vector2 screenPos, bool began)
        {
            // Input System screen space is origin bottom-left; every rect
            // here is GUI space, origin top-left.
            var g = new Vector2(screenPos.x, Screen.height - screenPos.y);

            if (id == wheelId) { Drag(g); seenWheel = true; return; }
            if (!began || wheelId != NoTouch) return;

            // Anywhere in the bottom half that no other HUD control has
            // already claimed this frame (buttons, sheets, the rail all
            // register with UIBlocker).
            // UIBlocker.Blocked wants SCREEN space (origin bottom-left, what
            // it was handed as `screenPos`) — it flips to GUI space itself.
            // Handing it `g` (already GUI space) used to flip a second time
            // and mirror the test vertically, so a tap on the bottom HUD
            // read as inside the helm zone and stopped her instead of
            // pressing the button.
            bool inZone = g.y >= Screen.height * (1f - ZoneFrac) && !UIBlocker.Blocked(screenPos);
            if (!inZone) return;

            wheelId = id;
            anchorPoint = g;
            knobPoint = g;
            touchDownTime = Time.time;
            HasDragDirection = false;
            DragDistance01 = 0f;

            // Freeze the camera's flattened basis for the whole drag — see
            // the class doc: the camera rotates with the ship, the drag must
            // not.
            var cam = Camera.main;
            Vector3 fwd = cam != null ? cam.transform.forward : Vector3.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            camForwardFlat = fwd;
            camRightFlat = Vector3.Cross(Vector3.up, fwd);

            seenWheel = true;
        }

        void Drag(Vector2 g)
        {
            Vector2 d = g - anchorPoint;
            float distPx = d.magnitude;
            DragDistance01 = ringRadiusPx > 0.01f ? distPx / ringRadiusPx : 0f;

            if (DragDistance01 >= DeadZoneFrac)
            {
                // GUI y grows down, so up-the-screen is a falling y.
                Vector3 worldDir = camRightFlat * d.x + camForwardFlat * (-d.y);
                if (worldDir.sqrMagnitude > 1e-6f)
                {
                    DragHeadingDeg = Mathf.Atan2(worldDir.x, worldDir.z) * Mathf.Rad2Deg;
                    HasDragDirection = true;
                }
            }

            // The drawn knob clamps a little past the rim so a thumb pushed
            // to the edge of its reach still has somewhere to go.
            float clampPx = ringRadiusPx * KnobClampFrac;
            knobPoint = distPx <= clampPx || clampPx < 1f ? g : anchorPoint + d.normalized * clampPx;
        }

        void Release()
        {
            float duration = Time.time - touchDownTime;
            Tapped = duration < TapMaxSeconds && DragDistance01 < DeadZoneFrac;
            JustReleased = true;
            wheelId = NoTouch;
            HasDragDirection = false;
            // knobPoint/anchorPoint/DragDistance01 are left as they were, so
            // the ring can fade out from where it was let go of.
        }

        // =====================================================================
        // Drawing
        // =====================================================================

        /// Reserves a small persistent readout slot (order word, a compass
        /// needle for the held heading, a throttle bar) and, while a thumb is
        /// on the stick or the release fade hasn't finished, draws the
        /// floating ring at the thumb's own anchor point rather than at any
        /// fixed screen position.
        ///
        /// `targetHeadingDeg`/`hasTarget`/`throttleOrder`/`astern`/`burning`
        /// are `HelmInput`'s policy state, handed in so nothing here needs to
        /// know about `ShipMotor`. `currentHeadingDeg` is `motor.Heading`,
        /// for the compass needle's relative bearing.
        public void Draw(float targetHeadingDeg, bool hasTarget, float throttleOrder,
            bool astern, bool burning, float currentHeadingDeg, GUIContent orderWord)
        {
            int u = HudLayout.Unit;
            float w = u * 9.2f;
            float h = u * 3.4f;
            var cluster = HudLayout.Place(HudLayout.Slot.Wheel, w, h);
            var readoutRect = new Rect(cluster.x, cluster.y, w, h);
            UIBlocker.Block(readoutRect);

            if (Event.current.type != EventType.Repaint) return;

            DrawReadout(readoutRect, u, targetHeadingDeg, hasTarget, throttleOrder,
                astern, burning, currentHeadingDeg, orderWord);

            if (Dragging || fade > 0.001f)
                DrawStick(u, astern, burning);
        }

        void DrawReadout(Rect r, int u, float targetDeg, bool hasTarget, float throttle,
            bool astern, bool burning, float currentDeg, GUIContent orderWord)
        {
            UITheme.Rect(r, UITheme.Panel);

            GUI.Label(new Rect(r.x, r.y + u * 0.1f, r.width, u * 1.2f),
                orderWord, UITheme.Small2Centered);

            // The compass: a needle at the relative bearing between where
            // she's pointed now and the course she's been given. Amidships
            // (straight up) is dead ahead of her own bow, not of the camera.
            float dialR = u * 1.05f;
            var hub = new Vector2(r.x + dialR + u * 0.6f, r.yMax - dialR - u * 0.3f);
            Ring(hub, dialR, u * 0.24f, UITheme.TextDim, 24);
            UITheme.Rect(new Rect(hub.x - 1.5f, hub.y - dialR - u * 0.22f, 3f, u * 0.4f),
                UITheme.TextDim);
            if (hasTarget)
            {
                float rel = Mathf.DeltaAngle(currentDeg, targetDeg);
                var prev = GUI.matrix;
                GUIUtility.RotateAroundPivot(rel, hub);
                var needleCol = astern ? UITheme.Warn : UITheme.Sea;
                UITheme.Rect(new Rect(hub.x - u * 0.16f, hub.y - dialR + u * 0.1f,
                    u * 0.32f, dialR - u * 0.1f), needleCol);
                GUI.matrix = prev;
            }

            // Throttle: one bar, mid-anchored at stop, astern below the mark
            // and ahead/burn above it — same reading as the achieved-throttle
            // bar on the helm panel, so the two never disagree in shape.
            float barX = hub.x + dialR + u * 0.6f;
            var barRect = new Rect(barX, r.y + u * 1.5f, r.xMax - u * 0.5f - barX, u * 0.55f);
            if (barRect.width > u)
            {
                UITheme.Bar(barRect, 1f, UITheme.Track);
                const float Ceiling = 1.4f; // headroom for the burn tier
                float mid = barRect.x + barRect.width / (1f + Ceiling);
                float t = Mathf.Clamp(throttle, -1f, Ceiling);
                var col = burning ? UITheme.Warn : astern ? UITheme.Warn : UITheme.Sea;
                if (t >= 0f)
                    UITheme.Bar(new Rect(mid, barRect.y, (barRect.xMax - mid) * (t / Ceiling),
                        barRect.height), 1f, col);
                else
                    UITheme.Bar(new Rect(mid + (mid - barRect.x) * t, barRect.y,
                        (mid - barRect.x) * -t, barRect.height), 1f, col);
            }
        }

        void DrawStick(int u, bool astern, bool burning)
        {
            float a = fade;
            var deadCol = UITheme.TextDim; deadCol.a *= a;
            var rimCol = UITheme.Track; rimCol.a *= a * 1.4f;
            var knobCol = burning ? UITheme.Warn : astern ? UITheme.Bad : UITheme.Sea;
            knobCol.a *= a;

            Ring(anchorPoint, ringRadiusPx * DeadZoneFrac, 3f, deadCol, 18);
            Ring(anchorPoint, ringRadiusPx, 4f, rimCol, 30);

            if (ringRadiusPx <= 1f) return;

            Vector2 knob = knobPoint;
            float knobSize = u * 0.9f;
            UITheme.Rect(new Rect(knob.x - knobSize * 0.5f, knob.y - knobSize * 0.5f,
                knobSize, knobSize), knobCol);

            // A pull line from the anchor to the knob, so the direction and
            // the amount both read at a glance.
            float len = Vector2.Distance(anchorPoint, knob);
            if (len > 1f)
            {
                Vector2 mid = (anchorPoint + knob) * 0.5f;
                float ang = Mathf.Atan2(knob.x - anchorPoint.x, -(knob.y - anchorPoint.y)) * Mathf.Rad2Deg;
                var prev = GUI.matrix;
                GUIUtility.RotateAroundPivot(ang, mid);
                UITheme.Rect(new Rect(mid.x - 1.5f, mid.y - len * 0.5f, 3f, len), knobCol);
                GUI.matrix = prev;
            }
        }

        /// A circle, out of the only primitive IMGUI has. Each segment resets
        /// the matrix first: `RotateAroundPivot` MULTIPLIES, so rotating from
        /// the previous segment's frame spirals the ring apart.
        static void Ring(Vector2 c, float r, float thick, Color col, int seg)
        {
            var prev = GUI.matrix;
            float len = 2f * Mathf.PI * r / seg * 1.3f;
            for (int i = 0; i < seg; i++)
            {
                GUI.matrix = prev;
                GUIUtility.RotateAroundPivot(i * (360f / seg), c);
                UITheme.Rect(new Rect(c.x - len * 0.5f, c.y - r - thick * 0.5f, len, thick), col);
            }
            GUI.matrix = prev;
        }
    }
}
