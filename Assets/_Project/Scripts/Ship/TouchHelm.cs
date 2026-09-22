using SeaSick.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using ET = UnityEngine.InputSystem.EnhancedTouch;

namespace SeaSick.Ship
{
    /// **The wheel and the telegraph lever — the thumb half of the helm.**
    ///
    /// What this replaces: a drag anywhere in the bottom 45% of the screen set
    /// an ABSOLUTE tiller position from the finger's x, and the throttle was
    /// two ▲/▼ buttons the size of a fingernail in the bottom-right corner.
    /// Kevin's verdict off the phone was that the corner buttons "make no
    /// sense" — and they don't: a telegraph is a thing you MOVE, and the
    /// steering was a thing you had to keep a finger pressed against or it
    /// snapped back to midships.
    ///
    /// So: a real wheel at the bottom centre that you turn by dragging round
    /// the hub, and a lever beside it that latches where you leave it. Both
    /// HOLD. Neither needs a finger kept on it. That is what a helm is.
    ///
    /// ## It owns the gesture, not the policy
    ///
    /// This class turns fingers into two numbers — `Rudder` in [-1,1] and
    /// `Throttle` in [-1,1] — and draws them. `HelmInput` decides what those
    /// numbers mean, eases the rudder, and lets a held key override either.
    /// Keeping the split means the wheel has no opinion about the ship.
    ///
    /// ## Two fingers, keyed by touch id
    ///
    /// One thumb on the wheel and one on the lever has to work, which a
    /// `Pointer.current` path can never do: there is one pointer and the
    /// second finger steals it. Every control here remembers the TOUCH ID that
    /// grabbed it and ignores every other contact until that id lifts. A touch
    /// that vanishes without an Ended phase (it happens — app switch, palm
    /// rejection) is released by the not-seen-this-frame sweep rather than by
    /// trusting the phase, so a control can never be left stuck to a finger
    /// that is no longer on the glass.
    ///
    /// `Pointer.current` stays as the editor mouse fallback, and is only
    /// consulted when there are no touches at all — one control at a time with
    /// a mouse is fine.
    public sealed class TouchHelm
    {
        // --- geometry, in HudLayout units -----------------------------------
        const float WheelD = 10f;    // wheel diameter: big enough to turn with a thumb
        const float LeverW = 2.6f;   // the track, not the handle
        const float LeverH = 6.0f;
        const float ColGap = 0.8f;
        const float LabelH = 1.3f;   // the helm-angle readout, above the wheel

        /// Where zero sits on the track, as a fraction of its height from the
        /// bottom. The bottom quarter is the WHOLE astern range on purpose:
        /// backing a paddle wheel is a manoeuvre, not a way to travel, and
        /// three quarters of the throw belongs to the speeds you actually use.
        const float ZeroAt = 0.25f;
        /// Snap-to-stop either side of zero, as a fraction of full throttle.
        /// 0.07 is about a third of a unit of travel — small enough that "dead
        /// slow" is still reachable, wide enough that letting go near the
        /// middle rings down STOP rather than a crawl you cannot see.
        const float DeadBand = 0.07f;
        /// How far out from the hub a finger has to land to be turning the
        /// wheel rather than tapping the hub. Inside this the angle a drag
        /// reports is mostly noise.
        const float HubFrac = 0.22f;
        const float DoubleTapSeconds = 0.35f;

        // --- state ----------------------------------------------------------
        /// The wheel's angle in degrees, ±180 at hard over. It HOLDS: this is
        /// the one number a release does not touch.
        public float WheelDeg { get; private set; }
        /// The latched engine order, -1 (full astern) .. +1 (full ahead).
        public float Throttle { get; private set; }

        public float Rudder => WheelDeg / 180f;
        /// True while a finger is on either control — `HelmInput` uses it to
        /// know a drag is in progress, not to decide the order.
        public bool Dragging => wheelId != NoTouch || leverId != NoTouch;

        /// What the WHEEL should be drawn at when something other than the
        /// wheel is steering — a held A/D. Null the rest of the time, and the
        /// wheel goes back to showing its own latched angle on release, which
        /// is exactly what the ship does.
        public float? DisplayOverride { get; set; }

        const int NoTouch = int.MinValue;
        const int MouseId = NoTouch + 1;

        int wheelId = NoTouch;
        int leverId = NoTouch;
        float wheelLastAngle;       // the finger's bearing from the hub, last frame
        float lastHubTap = -99f;
        bool seenWheel, seenLever;

        // Rects are resolved in Draw (OnGUI owns the layout) and read by
        // Sample (Update). One frame of lag, which is what every slot in
        // HudLayout already carries and is invisible at 60 Hz.
        Rect wheelRect, leverRect;
        bool laidOut;

        readonly HudLabel angleText = new HudLabel();

        /// Midships and stop, from outside. `HelmInput.AllStop` calls this.
        public void Centre() { WheelDeg = 0f; Throttle = 0f; }

        // =====================================================================
        // Gesture
        // =====================================================================

        /// Call once per Update, BEFORE the orders are read.
        ///
        /// `active` is false while the island view is up: the controls are not
        /// drawn then, so they must not be grabbable either — the sheet covers
        /// the bottom of a portrait screen and a tap meant for the ground
        /// would otherwise put the helm over and leave it there.
        public void Sample(bool active)
        {
            if (!active || !laidOut)
            {
                wheelId = leverId = NoTouch;
                return;
            }

            seenWheel = seenLever = false;
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
            // simulated pointer riding alongside real touches cannot claim a
            // second control on a phone.
            if (!anyTouch)
            {
                var p = Pointer.current;
                if (p != null && p.press.isPressed)
                    Feed(MouseId, p.position.ReadValue(), p.press.wasPressedThisFrame);
            }

            // Anything not seen this frame has lifted, whatever its phase said.
            if (!seenWheel) wheelId = NoTouch;
            if (!seenLever) leverId = NoTouch;
        }

        void Feed(int id, Vector2 screenPos, bool began)
        {
            // Input System screen space is origin bottom-left; every rect here
            // is GUI space, origin top-left.
            var g = new Vector2(screenPos.x, Screen.height - screenPos.y);

            if (id == wheelId) { DragWheel(g); seenWheel = true; return; }
            if (id == leverId) { DragLever(g); seenLever = true; return; }
            if (!began) return;

            // The lever is tested first and with a generous margin: it is the
            // narrower target and it sits beside the wheel, so a thumb that
            // lands between them should get the harder thing to hit.
            var lever = Grow(leverRect, HudLayout.Unit * 0.7f);
            if (leverId == NoTouch && lever.Contains(g))
            {
                leverId = id;
                DragLever(g);
                seenLever = true;
                return;
            }

            if (wheelId != NoTouch) return;
            Vector2 hub = wheelRect.center;
            float r = wheelRect.width * 0.5f;
            float d = Vector2.Distance(g, hub);

            if (d <= r * HubFrac)
            {
                // Double-tap the hub for midships. Deliberately two taps: one
                // stray tap in the middle of the wheel must not throw away the
                // helm you are holding mid-turn.
                if (Time.unscaledTime - lastHubTap <= DoubleTapSeconds)
                {
                    WheelDeg = 0f;
                    lastHubTap = -99f;
                }
                else lastHubTap = Time.unscaledTime;
                return;
            }

            // A little past the rim still counts — a thumb pad is wide and the
            // rim is where the grip is.
            if (d <= r * 1.18f)
            {
                wheelId = id;
                wheelLastAngle = Bearing(hub, g);
                seenWheel = true;
            }
        }

        /// The wheel follows the finger by ACCUMULATED angle, not by where the
        /// grab started: whatever bearing you took hold at is zero, and every
        /// frame adds the change. `DeltaAngle` does the ±180 wrap, so dragging
        /// past the bottom of the wheel keeps turning the same way instead of
        /// flipping hard over — which is what an atan2 read straight into the
        /// angle would do, and did, on the first version.
        void DragWheel(Vector2 g)
        {
            float a = Bearing(wheelRect.center, g);
            WheelDeg = Mathf.Clamp(WheelDeg + Mathf.DeltaAngle(wheelLastAngle, a), -180f, 180f);
            wheelLastAngle = a;
        }

        /// In GUI space y grows DOWN, so a bearing that increases is a
        /// clockwise turn on screen — which is starboard helm. No sign flip.
        static float Bearing(Vector2 hub, Vector2 g)
            => Mathf.Atan2(g.y - hub.y, g.x - hub.x) * Mathf.Rad2Deg;

        /// Absolute, not grab-relative: you slam a telegraph to where you want
        /// it. The handle jumping to the thumb is the correct behaviour here
        /// and makes "full ahead" a single confident stab rather than a drag.
        void DragLever(Vector2 g)
        {
            float t01 = Mathf.Clamp01((leverRect.yMax - g.y) / Mathf.Max(1f, leverRect.height));
            Throttle = ThrottleFrom01(t01);
        }

        public static float ThrottleFrom01(float t01)
        {
            float v = t01 >= ZeroAt ? (t01 - ZeroAt) / (1f - ZeroAt)
                                    : (t01 - ZeroAt) / ZeroAt;
            if (Mathf.Abs(v) < DeadBand) return 0f;
            return Mathf.Clamp(v, -1f, 1f);
        }

        static float Pos01From(float throttle)
            => throttle >= 0f ? ZeroAt + throttle * (1f - ZeroAt)
                              : ZeroAt + throttle * ZeroAt;

        static Rect Grow(Rect r, float by)
            => new Rect(r.x - by, r.y - by, r.width + by * 2f, r.height + by * 2f);

        // =====================================================================
        // Drawing
        // =====================================================================

        /// Reserve the slot and claim the controls on EVERY event; draw only on
        /// Repaint. The layout must not depend on frame rate (see HudLayout's
        /// note on slots going stale), and it is the text meshes that cost, not
        /// two float compares.
        public void Draw(float achieved, bool moving, GUIContent orderWord)
        {
            int u = HudLayout.Unit;
            float w = u * (WheelD + ColGap + LeverW);
            float h = u * (LabelH + WheelD);

            var cluster = HudLayout.Place(HudLayout.Slot.Wheel, w, h);

            wheelRect = new Rect(cluster.x, cluster.y + u * LabelH, u * WheelD, u * WheelD);
            leverRect = new Rect(wheelRect.xMax + u * ColGap,
                                 cluster.y + u * (LabelH + 1.1f), u * LeverW, u * LeverH);
            laidOut = true;

            UIBlocker.Block(wheelRect);
            UIBlocker.Block(Grow(leverRect, u * 0.7f));

            if (Event.current.type != EventType.Repaint) return;

            DrawWheel(u);
            DrawLever(u, achieved, moving, orderWord);
        }

        void DrawWheel(int u)
        {
            float deg = DisplayOverride.HasValue ? DisplayOverride.Value * 180f : WheelDeg;
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

            // The hub doubles as the midships control, so it says so: it goes
            // amber the moment the wheel is off centre and there is something
            // for a double-tap to undo.
            bool off = Mathf.Abs(WheelDeg) > 2f;
            Ring(hub, u * 0.75f, u * 0.5f, off ? UITheme.Warn : UITheme.TextDim, 14);

            // The readout above the wheel. Keyed on the WHOLE degree that is
            // displayed, so it rebuilds while a thumb is moving and never
            // otherwise.
            int shown = Mathf.RoundToInt(deg);
            if (angleText.Changed(shown))
                angleText.Set(Mathf.Abs(shown) < 2 ? "midships"
                    : shown < 0 ? $"port {-shown}°" : $"starboard {shown}°");
            GUI.Label(new Rect(wheelRect.x, wheelRect.y - u * 1.3f, wheelRect.width, u * 1.0f),
                      angleText.Content, UITheme.Small2Centered);
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

        void DrawLever(int u, float achieved, bool moving, GUIContent orderWord)
        {
            UITheme.Rect(Grow(leverRect, u * 0.25f), UITheme.Panel);
            UITheme.Rect(leverRect, UITheme.Track);

            float zeroY = leverRect.yMax - leverRect.height * ZeroAt;

            // What the engine has ACTUALLY reached, filling from the stop mark
            // toward the order. When the crew are sick this trails the handle,
            // and that gap is the mechanic — it has to be on the control the
            // player is holding, not only in the panel across the screen.
            float aPos = Pos01From(Mathf.Clamp(achieved, -1f, 1f));
            float aY = leverRect.yMax - leverRect.height * aPos;
            float top = Mathf.Min(aY, zeroY), bot = Mathf.Max(aY, zeroY);
            if (bot - top > 0.5f)
                UITheme.Rect(new Rect(leverRect.x, top, leverRect.width, bot - top),
                             achieved < 0f ? UITheme.Warn
                             : moving ? UITheme.Warn : UITheme.Sea);

            // Stop, and the two ahead notches, so the throw has landmarks a
            // thumb can find without looking down at it.
            UITheme.Rect(new Rect(leverRect.x - u * 0.2f, zeroY - 1f,
                                  leverRect.width + u * 0.4f, 2f), UITheme.Text);
            foreach (float notch in Notches)
            {
                float y = leverRect.yMax - leverRect.height * Pos01From(notch);
                UITheme.Rect(new Rect(leverRect.x, y - 0.5f, leverRect.width * 0.45f, 1f),
                             UITheme.TextDim);
            }

            // The handle, at the ORDER.
            float hY = leverRect.yMax - leverRect.height * Pos01From(Throttle);
            var handle = new Rect(leverRect.x - u * 0.35f, hY - u * 0.45f,
                                  leverRect.width + u * 0.7f, u * 0.9f);
            UITheme.Rect(handle, UITheme.Text);
            UITheme.Rect(new Rect(handle.x, handle.center.y - 1f, handle.width, 2f),
                         UITheme.Panel);

            GUI.Label(new Rect(leverRect.center.x - u * 3.1f, leverRect.yMax + u * 0.3f,
                               u * 6.2f, u * 1.2f), orderWord, UITheme.Small2Centered);
        }

        static readonly float[] Notches = { 0.35f, 0.7f, 1f };
    }
}
