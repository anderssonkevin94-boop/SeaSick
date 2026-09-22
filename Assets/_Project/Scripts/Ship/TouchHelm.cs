using SeaSick.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using ET = UnityEngine.InputSystem.EnhancedTouch;

namespace SeaSick.Ship
{
    /// **One wheel, one thumb: swipe across to steer, swipe up and down to
    /// change speed.**
    ///
    /// Kevin, 2026-09-22, after playing the wheel-and-lever version on the
    /// phone: *"swipe up to speed up, swipe down to slow down. bottom of the
    /// slow down tier is reverse and top of the swipe up tier is burn for
    /// extra speed. swiping left and right is steering where it snaps /
    /// rubber bands back to center when you let go."* Ideal: playable with
    /// one hand.
    ///
    /// What that replaced, and why: the wheel you turned by dragging round
    /// the hub HELD its angle, so every turn had to be unwound by hand, and
    /// the telegraph lever beside it meant choosing a speed with a second
    /// thumb while the first one was busy keeping her off a rock. Holding is
    /// right for a ship and wrong for a phone.
    ///
    /// ## One gesture zone, axis-locked
    ///
    /// A touch that lands on the wheel is undecided until it has moved
    /// `AxisLockPx`; the axis it moved further along then owns it until the
    /// finger lifts. No diagonal ever does two things at once, and a tap that
    /// never moves does nothing at all.
    ///
    /// * **Across = rudder.** The order is the finger's x offset from where it
    ///   went DOWN — not from the hub — so grabbing anywhere on the wheel
    ///   works, and full helm is about one wheel-radius of travel. On release
    ///   the order goes to zero and `HelmInput` eases the real rudder back:
    ///   the rubber band. She keeps whatever heading she has when it centres;
    ///   there is no auto-heading anywhere in here.
    /// * **Up/down = telegraph notches.** Travel accumulates and steps one
    ///   notch every `NotchTravelPx`, rebasing each time, so a flick steps one
    ///   and a long drag steps several without ever being a continuous slider.
    ///
    /// ## It owns the gesture, not the policy
    ///
    /// This class turns one finger into two numbers — `Rudder` in [-1,1] and
    /// `Throttle` (a notch order, up to `BurnOrder` above 1) — and draws them.
    /// `HelmInput` decides what they mean, eases the rudder, and lets a held
    /// key override either.
    ///
    /// ## Multitouch, keyed by touch id
    ///
    /// The wheel remembers the TOUCH ID that grabbed it and ignores every
    /// other contact until that id lifts, so a second finger anywhere else on
    /// the glass cannot disturb a turn in progress. A touch that vanishes
    /// without an Ended phase (app switch, palm rejection) is released by the
    /// not-seen-this-frame sweep rather than by trusting the phase.
    ///
    /// `Pointer.current` stays as the editor mouse fallback, and is only
    /// consulted when there are no touches at all.
    public sealed class TouchHelm
    {
        // --- geometry, in HudLayout units -----------------------------------
        /// The one size tunable. Diameter: big enough to find with a thumb.
        const float WheelD = 10f;
        const float LadderW = 1.2f;  // the notch ladder beside the wheel
        const float LadderH = 6.0f;
        const float ColGap = 0.8f;
        const float LabelH = 1.3f;   // the order readout, above the wheel

        // --- gesture constants, in SCREEN PIXELS ----------------------------
        // Kept in the same space as the wheel's own radius (u * WheelD / 2,
        // which is 100 px at the unit size every phone and desktop window this
        // game runs at resolves to), so a gesture distance and a drawn
        // distance mean the same thing.

        /// How far a finger must travel before the gesture commits to an axis.
        const float AxisLockPx = 12f;
        /// Travel along the locked vertical axis that rings the telegraph on
        /// one notch. A flick is one; 200 px is three.
        const float NotchTravelPx = 55f;
        /// Full rudder at this fraction of the wheel's radius of sideways
        /// travel, measured from the touch-down point.
        const float RudderTravelFrac = 0.9f;
        /// How far past the rim still counts as a grab — a thumb pad is wide
        /// and the rim is where the grip is.
        const float GrabFrac = 1.18f;
        /// The gesture zone is the whole lower part of the screen, not just
        /// the wheel (Kevin, 2026-09-22, phone: "the steering needs to be
        /// applicable on the whole bottom half of the screen, it's way too
        /// small of an area right now"). The wheel is the picture of the
        /// helm; a thumb anywhere below this fraction of the screen height
        /// that is not on another control is on the helm.
        const float ZoneFrac = 0.5f;
        /// Degrees the DRAWN wheel turns at full rudder. Purely visual.
        const float MaxWheelDeg = 120f;

        // --- the telegraph ---------------------------------------------------
        /// Bottom to top: full astern, stop, slow ahead, full ahead, burn.
        /// Burn is index `BurnNotch` and takes its value from `BurnOrder`,
        /// because how far past "full" the overdrive reaches belongs to the
        /// hull, not to the control.
        static readonly float[] NotchOrders = { -1f, 0f, 0.35f, 1f, 1f };
        public const int NotchCount = 5;
        public const int StopNotch = 1;
        public const int BurnNotch = 4;

        // --- state ----------------------------------------------------------
        /// Which notch the telegraph is at. Stop on a cold start.
        public int Notch { get; private set; } = StopNotch;
        public bool Burning => Notch == BurnNotch;

        /// The overdrive order, set from `ShipMotor.Overdrive` each frame.
        public float BurnOrder { get; set; } = 1.35f;

        /// The engine order the notch stands for.
        public float Throttle => Notch == BurnNotch ? BurnOrder : NotchOrders[Notch];

        /// The rudder ORDER. Zero unless a finger is actively steering — the
        /// release is what makes it rubber-band, and `HelmInput` owns the ease.
        public float Rudder { get; private set; }
        /// True while a finger is locked to the across axis. `HelmInput` uses
        /// it to pick the engage rate over the recentre rate.
        public bool Steering => wheelId != NoTouch && axis == Axis.Across;
        /// True while any finger owns the wheel.
        public bool Dragging => wheelId != NoTouch;

        /// What the wheel is DRAWN at, in rudder units: the eased, real rudder,
        /// written by `HelmInput` every frame. The wheel therefore shows the
        /// rubber band unwinding and a held A/D, and never lies about which way
        /// she is going over.
        public float ShownRudder { get; set; }

        const int NoTouch = int.MinValue;
        const int MouseId = NoTouch + 1;

        enum Axis { Undecided, Across, Along }

        int wheelId = NoTouch;
        Axis axis = Axis.Undecided;
        Vector2 grabPoint;      // where the finger went down, GUI space
        float notchAnchorY;     // rebased every time a notch steps
        bool seenWheel;

        // Rects are resolved in Draw (OnGUI owns the layout) and read by
        // Sample (Update). One frame of lag, which is what every slot in
        // HudLayout already carries and is invisible at 60 Hz.
        Rect wheelRect, ladderRect;
        bool laidOut;

        /// Midships and stop, from outside. `HelmInput.AllStop` calls this.
        public void Centre() { Notch = StopNotch; Rudder = 0f; }

        // =====================================================================
        // Gesture
        // =====================================================================

        /// Call once per Update, BEFORE the orders are read.
        ///
        /// `active` is false while the island view is up: the wheel is not
        /// drawn then, so it must not be grabbable either — the sheet covers
        /// the bottom of a portrait screen and a drag meant for the ground
        /// would otherwise ring the engine up.
        public void Sample(bool active)
        {
            if (!active || !laidOut)
            {
                wheelId = NoTouch;
                axis = Axis.Undecided;
                Rudder = 0f;
                return;
            }

            seenWheel = false;
            bool anyTouch = false;

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

            // Editor mouse. Deliberately only when the glass is empty, so a
            // simulated pointer riding alongside real touches cannot fight a
            // real thumb on a phone.
            if (!anyTouch)
            {
                var p = Pointer.current;
                if (p != null && p.press.isPressed)
                    Feed(MouseId, p.position.ReadValue(), p.press.wasPressedThisFrame);
            }

            // Not seen this frame means it has lifted, whatever its phase said.
            // Letting go is what starts the rubber band, so the order drops to
            // midships here and `HelmInput` walks the rudder home.
            if (!seenWheel)
            {
                wheelId = NoTouch;
                axis = Axis.Undecided;
                Rudder = 0f;
            }
        }

        void Feed(int id, Vector2 screenPos, bool began)
        {
            // Input System screen space is origin bottom-left; every rect here
            // is GUI space, origin top-left.
            var g = new Vector2(screenPos.x, Screen.height - screenPos.y);

            // The owning finger is served first and WITHOUT a rect test: a
            // drag that wanders off the wheel keeps steering until it lifts,
            // which is the only way a one-radius throw can reach the edge of
            // the wheel and stay there.
            if (id == wheelId) { Drag(g); seenWheel = true; return; }
            if (!began || wheelId != NoTouch) return;

            // On the wheel itself, or anywhere in the bottom half that no
            // other HUD control has claimed (the ladder, the helm panel, the
            // sheets and the rail all register with UIBlocker; the wheel does
            // too, which is why it is tested first).
            float r = wheelRect.width * 0.5f;
            bool onWheel = Vector2.Distance(g, wheelRect.center) <= r * GrabFrac;
            bool inZone = g.y >= Screen.height * (1f - ZoneFrac) && !UIBlocker.Blocked(g);
            if (!onWheel && !inZone) return;

            wheelId = id;
            axis = Axis.Undecided;
            grabPoint = g;
            notchAnchorY = g.y;
            seenWheel = true;
        }

        void Drag(Vector2 g)
        {
            Vector2 d = g - grabPoint;

            if (axis == Axis.Undecided)
            {
                // A press that never moves is not a gesture: it changes
                // nothing and it is not an accidental hard-over either.
                if (d.magnitude < AxisLockPx) return;
                axis = Mathf.Abs(d.x) >= Mathf.Abs(d.y) ? Axis.Across : Axis.Along;
                // The notch ladder counts from where the axis committed, so
                // the 12 px it took to decide is not also a twelfth of a notch.
                if (axis == Axis.Along) notchAnchorY = g.y;
            }

            if (axis == Axis.Across)
            {
                float r = wheelRect.width * 0.5f;
                Rudder = Mathf.Clamp(d.x / Mathf.Max(1f, r * RudderTravelFrac), -1f, 1f);
                return;
            }

            // GUI y grows DOWN, so up-the-screen is a falling y.
            float up = notchAnchorY - g.y;
            // Rebase per step rather than mapping position to a notch: a long
            // drag steps several, and reversing direction costs one full notch
            // of travel instead of flickering on the boundary. Travel is eaten
            // even when the notch is already at an end, so coming back down off
            // burn takes exactly one notch of travel, not all of it back.
            while (up >= NotchTravelPx)
            {
                Step(1);
                notchAnchorY -= NotchTravelPx;
                up -= NotchTravelPx;
            }
            while (up <= -NotchTravelPx)
            {
                Step(-1);
                notchAnchorY += NotchTravelPx;
                up += NotchTravelPx;
            }
        }

        void Step(int by) => Notch = Mathf.Clamp(Notch + by, 0, NotchCount - 1);

        static Rect Grow(Rect r, float by)
            => new Rect(r.x - by, r.y - by, r.width + by * 2f, r.height + by * 2f);

        // =====================================================================
        // Drawing
        // =====================================================================

        /// Reserve the slot and claim the rect on EVERY event; draw only on
        /// Repaint. The layout must not depend on frame rate (see HudLayout's
        /// note on slots going stale), and it is the text meshes that cost, not
        /// two float compares.
        ///
        /// `orderWord` is built and cached by `HelmInput` — nothing here
        /// allocates a string.
        public void Draw(GUIContent orderWord, bool burning)
        {
            int u = HudLayout.Unit;
            float w = u * (WheelD + ColGap + LadderW);
            float h = u * (LabelH + WheelD);

            var cluster = HudLayout.Place(HudLayout.Slot.Wheel, w, h);

            wheelRect = new Rect(cluster.x, cluster.y + u * LabelH, u * WheelD, u * WheelD);
            ladderRect = new Rect(wheelRect.xMax + u * ColGap,
                                  wheelRect.center.y - u * LadderH * 0.5f,
                                  u * LadderW, u * LadderH);
            laidOut = true;

            // The gesture zone is the wheel plus its grab margin, so a swipe
            // that starts on the rim never also reaches the water underneath.
            UIBlocker.Block(wheelRect);

            if (Event.current.type != EventType.Repaint) return;

            DrawWheel(u);
            DrawLadder(u);

            var prev = GUI.contentColor;
            if (burning) GUI.contentColor = UITheme.Warn;
            GUI.Label(new Rect(cluster.x, cluster.y, u * WheelD, u * LabelH),
                      orderWord, UITheme.Small2Centered);
            GUI.contentColor = prev;
        }

        void DrawWheel(int u)
        {
            float deg = Mathf.Clamp(ShownRudder, -1f, 1f) * MaxWheelDeg;
            Vector2 hub = wheelRect.center;
            float r = wheelRect.width * 0.5f;

            // The amidships mark, OUTSIDE the rim and fixed to the screen: the
            // wheel turns against it, which is the whole reason a helm is
            // readable at a glance.
            UITheme.Rect(new Rect(hub.x - 1.5f, wheelRect.y - u * 0.28f, 3f, u * 0.62f),
                         UITheme.Warn);

            Ring(hub, r - u * 0.35f, u * 0.5f, UITheme.Panel, 30);
            Ring(hub, r - u * 0.35f, u * 0.22f, UITheme.TextDim, 30);

            var prev = GUI.matrix;
            const int Spokes = 8;
            float inner = u * 0.7f;
            float outer = r - u * 0.3f;
            for (int i = 0; i < Spokes; i++)
            {
                // The king spoke is the one you read the angle off, so it is
                // brighter, thicker and wears a knob at the rim.
                bool king = i == 0;
                GUI.matrix = prev;
                GUIUtility.RotateAroundPivot(deg + i * (360f / Spokes), hub);
                float sw = king ? u * 0.42f : u * 0.22f;
                UITheme.Rect(new Rect(hub.x - sw * 0.5f, hub.y - outer, sw, outer - inner),
                             king ? UITheme.Text : UITheme.TextDim);
                if (king)
                    UITheme.Rect(new Rect(hub.x - u * 0.45f, hub.y - r - u * 0.1f,
                                          u * 0.9f, u * 0.55f), UITheme.Sea);
            }
            GUI.matrix = prev;

            // The hub goes amber while she is over: the one thing on the wheel
            // that says "this is unwinding by itself" as the band pulls it back.
            bool off = Mathf.Abs(ShownRudder) > 0.02f;
            Ring(hub, u * 0.75f, u * 0.5f, off ? UITheme.Warn : UITheme.TextDim, 14);
        }

        /// The telegraph, as five rungs rather than words: which notch she is
        /// on has to be readable without reading, because the thumb setting it
        /// is also the thumb steering and the eyes are on the water.
        void DrawLadder(int u)
        {
            UITheme.Rect(Grow(ladderRect, u * 0.2f), UITheme.Panel);

            float step = ladderRect.height / NotchCount;
            for (int i = 0; i < NotchCount; i++)
            {
                // Index 0 is full astern and belongs at the BOTTOM.
                var rung = new Rect(ladderRect.x,
                                    ladderRect.yMax - step * (i + 1) + step * 0.2f,
                                    ladderRect.width, step * 0.6f);
                if (i == Notch)
                    UITheme.Rect(rung, i == BurnNotch ? UITheme.Warn
                                     : i == 0 ? UITheme.Warn : UITheme.Sea);
                else
                    UITheme.Rect(rung, UITheme.Track);
            }

            // Stop is the landmark you count from, so it wears a tick that is
            // there whether or not she is sitting on it.
            float stopY = ladderRect.yMax - step * (StopNotch + 0.5f);
            UITheme.Rect(new Rect(ladderRect.x - u * 0.25f, stopY - 1f,
                                  ladderRect.width + u * 0.5f, 2f), UITheme.Text);
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
