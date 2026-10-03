using SeaSick.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using ET = UnityEngine.InputSystem.EnhancedTouch;

namespace SeaSick.Ship
{
    /// **The sea stick: a floating analog stick under the thumb** (DREDGE's
    /// phone control, 2026-10-03, docs/PLAN-dredge-controls.md §3.1).
    /// Replaces `TouchHelm` and its camera-frame heading drag.
    ///
    /// Put a thumb down anywhere in the BOAT ZONE (the lower
    /// `SailControlTuning.zoneTopFrac` of the screen, minus whatever another
    /// control claimed with `UIBlocker`) and a ring appears under it. The
    /// thumb's offset from where it landed, in ring radii, is the stick:
    /// `Axes` (+x right, +y up the screen, each axis dead-zoned and clamped
    /// to -1..1). The stick is boat-relative and knows nothing about the
    /// camera; what the axes MEAN (speed, turn, boost) is `HelmInput`'s.
    /// Let go and `Axes` is zero: nothing latches.
    ///
    /// ## One finger, its own id
    ///
    /// The stick remembers the touch id that started it and ignores every
    /// other contact, so a second finger (the camera's, phase 2) never
    /// steals or moves it. A touch that vanishes without an `Ended` phase
    /// (app switch, palm rejection) is still released by the
    /// not-seen-this-frame sweep. `Pointer.current` is the editor/desktop
    /// mouse fallback, consulted only when the glass is empty.
    ///
    /// ## Lift grace (Black Salt's "tiny finger lifts" fix)
    ///
    /// A lifted thumb keeps the stick, and its last `Axes`, for
    /// `SailControlTuning.liftGraceSeconds`. A touch that begins within
    /// `liftGraceRadius` ring radii of where it lifted, inside that window,
    /// carries on the same stick from the same anchor; otherwise the stick
    /// lets go when the window closes.
    public sealed class SeaStick
    {
        /// How long the ring takes to fade out after release.
        const float FadeSeconds = 0.30f;

        const int NoTouch = int.MinValue;
        const int MouseId = NoTouch + 1;

        /// True while a finger owns the stick, or a lifted one is still
        /// inside its grace window.
        public bool Held => stickId != NoTouch || inGrace;
        /// The stick, dead-zoned per axis and clamped to -1..1: +x right,
        /// +y up the screen. Zero whenever `Held` is false.
        public Vector2 Axes { get; private set; }
        /// Where the stick's touch went down, in SCREEN space (origin
        /// bottom-left, the `Pointer.position` convention).
        public Vector2 AnchorScreenPos => new Vector2(anchorPoint.x, Screen.height - anchorPoint.y);

        int stickId = NoTouch;
        Vector2 anchorPoint;   // GUI space, where the thumb went down
        Vector2 thumbPoint;    // GUI space, the thumb's last position
        Vector2 knobOffset;    // GUI px from the anchor, clamped to the rim
        bool inGrace;
        float graceUntil;
        float ringRadiusPx;
        float fade;            // 0..1, ring opacity once nothing holds it
        bool seen;

        /// Drop the stick now, grace and all. `HelmInput.AllStop()` calls
        /// this when the anchor goes down.
        public void CancelDrag()
        {
            stickId = NoTouch;
            inGrace = false;
            Axes = Vector2.zero;
            knobOffset = Vector2.zero;
            fade = 0f;
        }

        // =====================================================================
        // Gesture
        // =====================================================================

        /// Call once per Update, before `Axes` is read. `active` false (the
        /// island view, the shipyard modal) drops the stick and refuses new
        /// touches.
        public void Sample(bool active)
        {
            float now = Time.unscaledTime;
            if (!active)
            {
                stickId = NoTouch;
                inGrace = false;
                Axes = Vector2.zero;
                fade = Mathf.Max(0f, fade - Time.unscaledDeltaTime / FadeSeconds);
                return;
            }

            seen = false;
            bool anyTouch = false;
            ringRadiusPx = Mathf.Max(1f,
                Mathf.Clamp(SailControlTuning.ringRadiusFrac, 0.02f, 0.5f) * Mathf.Min(Screen.width, Screen.height));

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

            if (!anyTouch)
            {
                var p = Pointer.current;
                if (p != null && p.press.isPressed)
                    Feed(MouseId, p.position.ReadValue(), p.press.wasPressedThisFrame);
            }

            // Not seen this frame means it has lifted, whatever its phase said.
            if (!seen && stickId != NoTouch) Lift(now);
            if (inGrace && now >= graceUntil) { inGrace = false; Axes = Vector2.zero; }

            fade = Held ? 1f : Mathf.Max(0f, fade - Time.unscaledDeltaTime / FadeSeconds);
        }

        void Feed(int id, Vector2 screenPos, bool began)
        {
            // Input System screen space is origin bottom-left; the drawing is
            // GUI space, origin top-left.
            var g = new Vector2(screenPos.x, Screen.height - screenPos.y);

            if (id == stickId) { Move(g); seen = true; return; }
            if (!began || stickId != NoTouch) return;

            // The same thumb back down after a tiny lift: same stick, same anchor.
            if (inGrace && (g - thumbPoint).magnitude
                <= Mathf.Max(0f, SailControlTuning.liftGraceRadius) * ringRadiusPx)
            {
                stickId = id;
                inGrace = false;
                Move(g);
                seen = true;
                return;
            }

            // A fresh stick: only in the boat zone, and only where no other
            // control claimed the touch. UIBlocker.Blocked wants SCREEN space
            // (it flips to GUI space itself).
            bool inZone = screenPos.y <= Screen.height * Mathf.Clamp01(SailControlTuning.zoneTopFrac)
                          && !UIBlocker.Blocked(screenPos);
            if (!inZone) return;

            stickId = id;
            inGrace = false;
            anchorPoint = g;
            Move(g);
            seen = true;
        }

        void Move(Vector2 g)
        {
            thumbPoint = g;
            Vector2 d = g - anchorPoint;
            // GUI y grows down; the stick's +y is UP the screen.
            Vector2 v = new Vector2(d.x, -d.y) / ringRadiusPx;
            if (v.sqrMagnitude > 1f) v.Normalize();
            float dz = Mathf.Clamp(SailControlTuning.deadZone, 0f, 0.9f);
            Axes = new Vector2(DeadZone(v.x, dz), DeadZone(v.y, dz));
            knobOffset = new Vector2(v.x, -v.y) * ringRadiusPx;
        }

        /// Per-axis dead zone, rescaled so the rim is still 1: a sideways
        /// push whose height wobbles a little orders no speed at all.
        static float DeadZone(float a, float dz)
        {
            float m = Mathf.Abs(a);
            return m <= dz ? 0f : Mathf.Sign(a) * (m - dz) / (1f - dz);
        }

        void Lift(float now)
        {
            stickId = NoTouch;
            float grace = Mathf.Max(0f, SailControlTuning.liftGraceSeconds);
            if (grace > 0f) { inGrace = true; graceUntil = now + grace; }
            else Axes = Vector2.zero;
        }

        // =====================================================================
        // Drawing
        // =====================================================================

        /// Draws the ring at the thumb's anchor while the stick is held or
        /// the release fade hasn't finished. Repaint only. The knob sits at
        /// the thumb (clamped to the rim); `astern`/`boost` colour it.
        public void Draw(bool astern, bool boost)
        {
            if (!Held && fade <= 0.001f) return;
            int u = HudLayout.Unit;
            float R = ringRadiusPx;
            if (R <= 1f) return;
            Vector2 c = DrawCentre(u);

            float a = fade;
            var deadCol = UITheme.TextDim; deadCol.a *= a;
            var rimCol = UITheme.Track; rimCol.a *= a * 1.4f;
            var knobCol = boost ? UITheme.Warn : astern ? UITheme.Bad : UITheme.Sea;
            knobCol.a *= a;

            Ring(c, R * Mathf.Clamp(SailControlTuning.deadZone, 0f, 0.9f), 3f, deadCol, 18);
            Ring(c, R, 4f, rimCol, 30);

            Vector2 knob = c + knobOffset;
            float knobSize = u * 0.9f;
            UITheme.Rect(new Rect(knob.x - knobSize * 0.5f, knob.y - knobSize * 0.5f,
                knobSize, knobSize), knobCol);

            // A pull line from the centre to the knob: direction and amount
            // at a glance.
            float len = Vector2.Distance(c, knob);
            if (len > 1f)
            {
                Vector2 mid = (c + knob) * 0.5f;
                float ang = Mathf.Atan2(knob.x - c.x, -(knob.y - c.y)) * Mathf.Rad2Deg;
                var prev = GUI.matrix;
                GUIUtility.RotateAroundPivot(ang, mid);
                UITheme.Rect(new Rect(mid.x - 1.5f, mid.y - len * 0.5f, 3f, len), knobCol);
                GUI.matrix = prev;
            }
        }

        /// Where the ring is DRAWN: the anchor, shifted just enough to sit
        /// fully on screen (a thumb can land a few px off the bottom edge).
        /// The stick itself stays relative to the real touch point.
        Vector2 DrawCentre(int u)
        {
            float R = ringRadiusPx;
            float m = u * 0.2f;
            float half = R + u * 0.45f;   // the knob can sit on the rim
            float x = Mathf.Clamp(anchorPoint.x, R + m, Mathf.Max(R + m, Screen.width - R - m));
            float y = Mathf.Min(anchorPoint.y, Screen.height - half - m);
            return new Vector2(x, y);
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
