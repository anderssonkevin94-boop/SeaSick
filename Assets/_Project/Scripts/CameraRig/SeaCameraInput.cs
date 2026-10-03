using SeaSick.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using ET = UnityEngine.InputSystem.EnhancedTouch;

namespace SeaSick.CameraRig
{
    /// **The sea camera's look input** (DREDGE controls step 2, 2026-10-03,
    /// docs/PLAN-dredge-controls.md §3.1-3.3). A plain class `ChaseCamera`
    /// owns and samples once per LateUpdate, the way `HelmInput` owns
    /// `SeaStick`. It only turns gestures into numbers: this frame's
    /// `YawDeltaDeg` / `PitchDeltaDeg` and a `RecenterRequested` edge. What
    /// they do to the shot is `ChaseCamera`'s.
    ///
    /// ## Phone: a second floating stick, rate control
    ///
    /// A touch that BEGINS above the boat zone (`SailControlTuning.zoneTopFrac`)
    /// and not on a `UIBlocker` rect owns the camera stick. Its offset from
    /// where it landed, in ring radii, past `SeaCameraTuning.deadZone`, is a
    /// yaw rate (x) and a pitch rate (y). DREDGE's right stick: the view
    /// turns while the thumb is pushed and stays where it got to on release.
    /// It tracks its own finger id, so the boat stick's thumb and this one
    /// work at once. Stick right = look right, stick up = look up (toward
    /// the horizon); `SeaCameraPrefs.InvertX/InvertY` flip them.
    ///
    /// A short touch that never left the dead zone is a TAP and looks
    /// nowhere, so `CombatLock`'s tap-to-lock (which reads `Pointer` on its
    /// own) is untouched. Two taps within `doubleTapSeconds` near the same
    /// spot recenter, unless either one would have locked a ship
    /// (`CombatLock.WouldLock`): double-tapping a raider locks it, it never
    /// also swings the view home.
    ///
    /// ## Desktop: the mouse as a stick
    ///
    /// Right-button drag anywhere off the HUD, or left-button drag that
    /// starts in the camera zone. The pixel delta becomes degrees
    /// (`mouseDegPerPixel`) capped at the stick's full-tilt rate, so a fling
    /// can't whip the view round (DREDGE's "max tilt"). C or the middle
    /// button recenters; a left double-click in the zone does too. The mouse
    /// is read only while the glass is empty.
    public sealed class SeaCameraInput
    {
        const string UsedKey = "seasick.seacam.used";
        const int NoTouch = int.MinValue;
        const int MouseId = NoTouch + 1;
        /// Longest a touch may be held and still count as a tap, seconds.
        const float TapMaxSeconds = 0.25f;
        /// Pixels of mouse travel before a held button counts as a look.
        const float MouseLookPx = 6f;
        const float FadeSeconds = 0.30f;

        /// True once the player has made a real look drag (ever, any device).
        /// Persisted, so the first-use hint shows only until they've found it.
        public static bool EverUsed
        {
            get { if (!usedLoaded) { usedLoaded = true; everUsed = PlayerPrefs.GetInt(UsedKey, 0) != 0; } return everUsed; }
            private set { usedLoaded = true; if (everUsed == value) return; everUsed = value; PlayerPrefs.SetInt(UsedKey, value ? 1 : 0); }
        }

        static bool usedLoaded, everUsed;

        /// Degrees to add to the look offsets this frame. +yaw = look right,
        /// +pitch = look further DOWN on her (the seat climbs).
        public float YawDeltaDeg { get; private set; }
        public float PitchDeltaDeg { get; private set; }
        /// True for the one frame a recenter was asked for.
        public bool RecenterRequested { get; private set; }
        /// A finger or button is actively looking (past the dead zone / drag
        /// threshold), for anything that wants to know the player is steering
        /// the view.
        public bool Looking => (stickId != NoTouch && engaged) || (mouseButton != 0 && mouseMoved);

        int stickId = NoTouch;
        Vector2 anchor;        // SCREEN space (origin bottom-left), where it went down
        Vector2 thumb;         // SCREEN space, last position
        float downTime;
        bool engaged;          // left the dead zone at least once: no longer a tap
        bool seen;
        float ringRadiusPx;
        float fade;

        int mouseButton;       // 0 none, 1 left, 2 right
        bool mouseMoved;
        float mouseTravel;

        float lastTapEnd = -10f;
        Vector2 lastTapPos;
        bool lastTapLocked;

        /// Drop every gesture now (a stand-down gate came on).
        public void Cancel()
        {
            stickId = NoTouch;
            engaged = false;
            mouseButton = 0;
            mouseMoved = false;
            lastTapEnd = -10f;
            YawDeltaDeg = PitchDeltaDeg = 0f;
            RecenterRequested = false;
        }

        /// Call once per frame before the deltas are read. `active` false
        /// (island view, shipyard, a menu) drops everything and refuses new
        /// touches. `lookLive` false (a lock / kraken / overview shot fully
        /// in) keeps the gestures tracked but turns nothing. `combat` may be
        /// null (no tap-to-lock on this hull).
        public void Sample(bool active, bool lookLive, SeaSick.Combat.CombatLock combat, float dt)
        {
            YawDeltaDeg = PitchDeltaDeg = 0f;
            RecenterRequested = false;
            if (!active)
            {
                Cancel();
                fade = Mathf.Max(0f, fade - Time.unscaledDeltaTime / FadeSeconds);
                return;
            }

            ringRadiusPx = Mathf.Max(1f,
                Mathf.Clamp(SeaCameraTuning.ringRadiusFrac, 0.02f, 0.5f) * Mathf.Min(Screen.width, Screen.height));
            float sens = SeaCameraPrefs.Sensitivity;
            float sx = SeaCameraPrefs.InvertX ? -1f : 1f;
            float sy = SeaCameraPrefs.InvertY ? -1f : 1f;
            float yawMax = Mathf.Max(0f, SeaCameraTuning.maxYawDegPerSec) * sens;
            float pitchMax = Mathf.Max(0f, SeaCameraTuning.maxPitchDegPerSec) * sens;

            seen = false;
            bool anyTouch = false;
            if (ET.EnhancedTouchSupport.enabled && Touchscreen.current != null)
            {
                var touches = ET.Touch.activeTouches;
                for (int i = 0; i < touches.Count; i++)
                {
                    var t = touches[i];
                    var ph = t.phase;
                    bool ended = ph == UnityEngine.InputSystem.TouchPhase.Ended
                              || ph == UnityEngine.InputSystem.TouchPhase.Canceled;
                    if (!ended) anyTouch = true;
                    if (t.touchId == stickId)
                    {
                        seen = true;
                        thumb = t.screenPosition;
                        if (ended) Release(ph == UnityEngine.InputSystem.TouchPhase.Ended, combat);
                        continue;
                    }
                    if (!ended && ph == UnityEngine.InputSystem.TouchPhase.Began && stickId == NoTouch)
                        Begin(t.touchId, t.screenPosition);
                }
                // Not seen this frame means it has lifted, whatever its phase said.
                if (!seen && stickId != NoTouch && stickId != MouseId) Release(false, combat);
            }

            if (stickId != NoTouch && stickId != MouseId)
            {
                Vector2 v = (thumb - anchor) / ringRadiusPx;
                if (v.sqrMagnitude > 1f) v.Normalize();
                float dz = Mathf.Clamp(SeaCameraTuning.deadZone, 0f, 0.9f);
                float ax = DeadZone(v.x, dz), ay = DeadZone(v.y, dz);
                if (ax != 0f || ay != 0f) engaged = true;
                if (engaged && lookLive)
                {
                    YawDeltaDeg = ax * yawMax * sx * dt;
                    // Stick up = look up = a flatter view = pitch offset DOWN.
                    PitchDeltaDeg = -ay * pitchMax * sy * dt;
                    if (ax != 0f || ay != 0f) EverUsed = true;
                }
            }

            if (!anyTouch && stickId == NoTouch) SampleMouse(lookLive, combat, dt, sens, sx, sy, yawMax, pitchMax);
            else mouseButton = 0;

            var kb = Keyboard.current;
            if (!anyTouch && kb != null && kb.cKey.wasPressedThisFrame) RecenterRequested = true;

            bool showRing = stickId != NoTouch && stickId != MouseId && engaged && lookLive;
            fade = showRing ? 1f : Mathf.Max(0f, fade - Time.unscaledDeltaTime / FadeSeconds);
        }

        void Begin(int id, Vector2 screenPos)
        {
            // Only above the boat zone, and only where no HUD control claimed
            // it. UIBlocker.Blocked wants SCREEN space (it flips itself).
            if (!InCameraZone(screenPos)) return;
            stickId = id;
            anchor = thumb = screenPos;
            downTime = Time.unscaledTime;
            engaged = false;
            seen = true;
        }

        static bool InCameraZone(Vector2 screenPos)
            => screenPos.y > Screen.height * Mathf.Clamp01(SeaSick.Ship.SailControlTuning.zoneTopFrac)
               && !UIBlocker.Blocked(screenPos);

        /// `lifted` is a real finger-up (not a cancel or a lost touch): only
        /// those can be taps.
        void Release(bool lifted, SeaSick.Combat.CombatLock combat)
        {
            bool tap = lifted && !engaged && Time.unscaledTime - downTime <= TapMaxSeconds;
            stickId = NoTouch;
            engaged = false;
            if (tap) Tap(thumb, combat);
        }

        /// A tap in the camera zone: the second of a close pair recenters,
        /// unless either tap was on a ship CombatLock would lock.
        void Tap(Vector2 pos, SeaSick.Combat.CombatLock combat)
        {
            float now = Time.unscaledTime;
            bool locks = combat != null && combat.isActiveAndEnabled && combat.WouldLock(pos);
            // Measured from the first tap's lift to the second's touch-down.
            bool pair = downTime - lastTapEnd <= Mathf.Max(0.05f, SeaCameraTuning.doubleTapSeconds)
                     && (pos - lastTapPos).magnitude <= ringRadiusPx;
            if (pair && !locks && !lastTapLocked)
            {
                RecenterRequested = true;
                lastTapEnd = -10f;
                return;
            }
            lastTapEnd = now;
            lastTapPos = pos;
            lastTapLocked = locks;
        }

        void SampleMouse(bool lookLive, SeaSick.Combat.CombatLock combat, float dt,
                         float sens, float sx, float sy, float yawMax, float pitchMax)
        {
            var m = Mouse.current;
            if (m == null) { mouseButton = 0; return; }
            Vector2 pos = m.position.ReadValue();

            if (m.middleButton.wasPressedThisFrame && !UIBlocker.Blocked(pos)) RecenterRequested = true;

            if (mouseButton == 0)
            {
                if (m.rightButton.wasPressedThisFrame && !UIBlocker.Blocked(pos)) StartMouse(2);
                else if (m.leftButton.wasPressedThisFrame && InCameraZone(pos)) StartMouse(1);
            }
            if (mouseButton == 0) return;

            bool held = mouseButton == 2 ? m.rightButton.isPressed : m.leftButton.isPressed;
            if (!held)
            {
                // A left click that never moved is a tap (double-click recenters).
                if (mouseButton == 1 && !mouseMoved && Time.unscaledTime - downTime <= TapMaxSeconds)
                    Tap(pos, combat);
                mouseButton = 0;
                return;
            }

            Vector2 d = m.delta.ReadValue();
            mouseTravel += d.magnitude;
            if (mouseTravel >= MouseLookPx) mouseMoved = true;
            if (!mouseMoved || !lookLive) return;

            // Mouse-as-stick: pixels -> degrees, but never faster than full tilt.
            float k = Mathf.Max(0f, SeaCameraTuning.mouseDegPerPixel) * sens;
            YawDeltaDeg = Mathf.Clamp(d.x * k, -yawMax * dt, yawMax * dt) * sx;
            PitchDeltaDeg = -Mathf.Clamp(d.y * k, -pitchMax * dt, pitchMax * dt) * sy;
            if (d.sqrMagnitude > 0f) EverUsed = true;
        }

        void StartMouse(int button)
        {
            mouseButton = button;
            mouseMoved = false;
            mouseTravel = 0f;
            downTime = Time.unscaledTime;
        }

        /// Per-axis dead zone, rescaled so the rim is still 1.
        static float DeadZone(float a, float dz)
        {
            float m = Mathf.Abs(a);
            return m <= dz ? 0f : Mathf.Sign(a) * (m - dz) / (1f - dz);
        }

        // =====================================================================
        // Drawing
        // =====================================================================

        /// A faint ring at the camera thumb's anchor while it is looking, the
        /// knob at the thumb (clamped to the rim). Repaint only; the caller
        /// owns the suppression guards.
        public void Draw()
        {
            if (fade <= 0.001f) return;
            float R = ringRadiusPx;
            if (R <= 1f) return;
            int u = HudLayout.Unit;
            // GUI space is origin top-left.
            Vector2 a = new Vector2(anchor.x, Screen.height - anchor.y);
            Vector2 c = new Vector2(
                Mathf.Clamp(a.x, R, Mathf.Max(R, Screen.width - R)),
                Mathf.Clamp(a.y, R, Mathf.Max(R, Screen.height - R)));
            Vector2 off = new Vector2(thumb.x - anchor.x, anchor.y - thumb.y);
            if (off.magnitude > R) off = off.normalized * R;

            float al = fade * 0.6f;   // fainter than the boat stick: it's the secondary control
            var deadCol = UITheme.TextDim; deadCol.a *= al;
            var rimCol = UITheme.Track; rimCol.a *= al * 1.4f;
            var knobCol = UITheme.TextDim; knobCol.a *= al;

            Ring(c, R * Mathf.Clamp(SeaCameraTuning.deadZone, 0f, 0.9f), 3f, deadCol, 16);
            Ring(c, R, 3f, rimCol, 28);
            Vector2 knob = c + off;
            float ks = u * 0.7f;
            UITheme.Rect(new Rect(knob.x - ks * 0.5f, knob.y - ks * 0.5f, ks, ks), knobCol);
        }

        /// A circle from IMGUI rects; the matrix is reset per segment because
        /// `RotateAroundPivot` multiplies (same trap `SeaStick.Ring` notes).
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
