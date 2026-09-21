using SeaSick.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SeaSick.CameraRig
{
    /// **The one reader of devices for the island view.**
    ///
    /// Mouse, keyboard and touch all arrive here and leave as calls on
    /// `IslandCam` (grab, zoom, orbit, fly-to) and `UI.Hand` (pick up, hold,
    /// drop) — the same public methods the probes call, so a gate cannot pass
    /// against a copy of the behaviour.
    ///
    /// **The classifier is separate from the reading.** `PressSample` and
    /// `GestureClassifier` below turn a press's raw numbers (where it went
    /// down, where it is now, how long) into Tap / Drag / LongPress /
    /// DoubleTap, and do it with every threshold expressed as a FRACTION of
    /// screen height — never a pixel count — so `IslandInputProbe` can ask
    /// the same fractional question at a phone's 422 px and a desk's 2340
    /// and get the same answer. This component's own job is small once that
    /// exists: read the device, hand the numbers to the classifier, and act
    /// on what comes back.
    ///
    /// Added by `IslandCam.Awake`; runs before it (`DefaultExecutionOrder`
    /// -50), so `TapThisFrame`/`TapAt` are already set for the frame by the
    /// time anything else — `CampSiting` included — reads them.
    [DefaultExecutionOrder(-50)]
    public class IslandInput : MonoBehaviour
    {
        /// A press that went down and came up inside the slop, on the world
        /// (not on UI), this frame. `CampSiting` commits on this rather than
        /// on press, so that dragging the land about while siting does not
        /// also place the building.
        public static bool TapThisFrame { get; private set; }
        public static Vector2 TapAt { get; private set; }

        /// Is the Hand currently holding somebody? Exposed for anything that
        /// wants to know without reaching into `Hand` itself (the prompt
        /// line, chiefly).
        public static bool Holding => Hand.Instance != null && Hand.Instance.Holding;

        /// **Every dial this file turns.** The component is added at
        /// runtime, so a `[SerializeField]` here would never be reachable in
        /// the Inspector — plain static fields are the ones that are
        /// actually live, and Kevin can poke them from a tuner or the
        /// console exactly as easily.
        public static class Feel
        {
            /// Fraction of screen height a press has to travel before it
            /// counts as a drag rather than a tap (mouse) or a long-press
            /// candidate (touch). 1.2%: small enough that a genuine tap
            /// never reads as a drag on a shaky hand, large enough that a
            /// phone's own jitter never reads as one either.
            public static float slopFrac = 0.012f;

            /// How long a touch has to sit still, inside the slop, over a
            /// villager before it counts as a pick-up rather than the start
            /// of a drag that never came.
            public static float longPressSeconds = 0.35f;

            /// Two taps closer together than this are one double-tap.
            public static float doubleTapSeconds = 0.3f;

            /// ...and no further apart than this fraction of screen height.
            public static float doubleTapFrac = 0.02f;

            /// A full-height mouse drag on RMB, MMB or Alt+LMB turns the
            /// view this many degrees of azimuth. Tilt is half as
            /// sensitive — spinning round is meant to feel quicker than
            /// nodding. See `Dev/DockCamTuner.cs`'s own
            /// `orbitPerPixel`/`tiltPerPixel`, tuned the same way, just in
            /// raw pixels rather than a screen-height fraction.
            public static float orbitDegPerHeightMouse = 180f;
            public static float tiltDegPerHeightMouse = 90f;

            /// Two fingers' AGREED vertical motion (see `TwoFinger.Solve`),
            /// as a fraction of screen height, converted to degrees of tilt
            /// the same way the mouse orbit converts pixels.
            public static float tiltDegPerHeightTouch = 90f;

            /// Zoom multiplier per wheel detent (Input System reports 120
            /// units per detent on a notched mouse). Above 1 so that
            /// `GestureClassifier.WheelFactor` zooms IN — factor below 1 —
            /// as the wheel rolls the positive way, matching
            /// `IslandCam.ZoomAt`'s own convention.
            public static float wheelStep = 1.22f;
            /// Most notches honoured in a single frame. See `HandleWheel`.
            public static float wheelMaxPerFrame = 4f;

            /// Within this fraction of a screen edge, a held villager drags
            /// the frame along with the finger, so the far side of an
            /// island is reachable one-handed without letting go.
            public static float edgePanFrac = 0.06f;
        }

        /// The raw numbers behind one press: where it went down, where it
        /// is (or ended) now, when each of those happened, and how tall the
        /// screen is — everything `GestureClassifier` needs and nothing it
        /// has to go and fetch itself, which is what makes the classifier
        /// probeable without a device or a scene.
        public readonly struct PressSample
        {
            public readonly Vector2 downPos;
            public readonly Vector2 pos;
            public readonly float downTime;
            public readonly float now;
            public readonly float screenH;

            public PressSample(Vector2 downPos, Vector2 pos, float downTime, float now, float screenH)
            {
                this.downPos = downPos;
                this.pos = pos;
                this.downTime = downTime;
                this.now = now;
                this.screenH = Mathf.Max(1f, screenH);
            }
        }

        IslandCam cam;
        Hand hand;

        // --- mouse / single-pointer press state --------------------------

        enum PMode { Idle, UILatched, Pending, GrabbingLand, HoldingVillager }
        PMode pmode = PMode.Idle;
        Vector2 pressDownPos;
        float pressDownTime;
        Crew.CrewAgent pickupCandidate;

        // --- RMB / MMB / Alt+LMB orbit ------------------------------------

        enum OrbitSource { None, Right, Middle, AltLeft }
        OrbitSource orbitSource = OrbitSource.None;
        Vector2 orbitAnchor;

        // --- shared tap / double-tap bookkeeping (mouse AND touch) -------

        Vector2 lastTapPos;
        float lastTapTime = -100f;

        // --- touch ----------------------------------------------------------

        struct TouchPoint { public int id; public Vector2 pos; }
        readonly System.Collections.Generic.List<TouchPoint> liveTouches = new System.Collections.Generic.List<TouchPoint>(4);
        readonly System.Collections.Generic.List<TouchPoint> endedTouches = new System.Collections.Generic.List<TouchPoint>(4);

        enum TouchMode { None, OneFinger, TwoFinger, WaitLift }
        TouchMode touchMode = TouchMode.None;

        enum OneFingerSub { Pending, Grabbing, Holding }
        int oneFingerId = -1;
        Vector2 oneFingerDown;
        float oneFingerDownTime;
        bool oneFingerUILatched;
        Crew.CrewAgent oneFingerCandidate;
        OneFingerSub oneSub;

        int twoFingerAId = -1, twoFingerBId = -1;
        Vector2 twoFingerPrevA, twoFingerPrevB;

        void Awake()
        {
            cam = GetComponent<IslandCam>();
            hand = GetComponent<Hand>();
        }

        void Update()
        {
            TapThisFrame = false;

            if (cam == null) return;
            if (hand == null) hand = Hand.Instance;

            // 1. A dev tool that is open owns the view, unless it is the
            // tuner attached to THIS camera.
            if (DevTools.Open != null && !IslandCam.TunerAttached) { CancelGesture(); return; }

            // 2. Stay inert until the overview has actually settled — a grab
            // against a shot still flying up from the chase camera has
            // nothing fixed under it to hold.
            if (!IslandCam.Engaged || !cam.Ready) { CancelGesture(); return; }

            if (hand == null) return; // Stream C's Hand not on this object yet

            GatherTouches();
            bool touchLive = liveTouches.Count > 0 || touchMode != TouchMode.None;
            if (touchLive) HandleTouch();
            else HandleMouseAndKeys();
        }

        // =====================================================================
        // DESKTOP
        // =====================================================================

        void HandleMouseAndKeys()
        {
            var mouse = Mouse.current;
            var keys = Keyboard.current;
            bool altHeld = keys != null && (keys.leftAltKey.isPressed || keys.rightAltKey.isPressed);

            if (mouse != null)
            {
                // Alt+LMB is claimed by the orbit gesture below; the normal
                // grab/pick-up press never starts while Alt is down, so the
                // same button press cannot mean two things at once.
                if (mouse.leftButton.wasPressedThisFrame && !altHeld)
                    BeginPress(mouse.position.ReadValue());
                else if (pmode != PMode.Idle)
                {
                    if (mouse.leftButton.isPressed) ContinuePress(mouse.position.ReadValue());
                    else EndPress(mouse.position.ReadValue());
                }

                HandleOrbit(mouse, altHeld);
                HandleWheel(mouse);
            }

            HandleKeys(keys, Time.unscaledDeltaTime);
        }

        void BeginPress(Vector2 pos)
        {
            // Rule 3: a press that BEGINS over UI is UI-owned until release
            // — tested once, here, and latched, rather than re-tested every
            // frame of the drag (which would let a drag that started on a
            // button "escape" onto the land the moment it crossed the edge).
            if (UIBlocker.Blocked(pos)) { pmode = PMode.UILatched; return; }

            // Rule 5: any press-down kills a fling or a fly-to in progress.
            cam.KillMotion();

            pressDownPos = pos;
            pressDownTime = Time.unscaledTime;
            // Rule 4: while siting a building, Hand pick-up is off — every
            // LMB drag during that mode grabs the land, never a villager.
            pickupCandidate = CampSiting.Placing ? null : hand.PickAt(pos, forPickup: true);
            pmode = PMode.Pending;
        }

        void ContinuePress(Vector2 pos)
        {
            switch (pmode)
            {
                case PMode.UILatched:
                    return;

                case PMode.Pending:
                {
                    var sample = new PressSample(pressDownPos, pos, pressDownTime, Time.unscaledTime, Screen.height);
                    if (!GestureClassifier.PastSlop(sample)) return;

                    if (pickupCandidate != null && hand.PickUp(pickupCandidate))
                        pmode = PMode.HoldingVillager;
                    else
                    {
                        pmode = PMode.GrabbingLand;
                        cam.GrabBegin(pos);
                    }
                    return;
                }

                case PMode.GrabbingLand:
                    cam.GrabMove(pos);
                    return;

                case PMode.HoldingVillager:
                {
                    hand.HoldAt(pos);
                    var mouse = Mouse.current;
                    var keys = Keyboard.current;
                    bool cancel = (mouse != null && mouse.rightButton.wasPressedThisFrame)
                        || (keys != null && keys.escapeKey.wasPressedThisFrame);
                    if (cancel) { hand.Cancel(); pmode = PMode.Idle; }
                    return;
                }
            }
        }

        void EndPress(Vector2 pos)
        {
            switch (pmode)
            {
                case PMode.Pending:
                    // Never travelled past the slop: a tap. Left for
                    // `CampSiting` to consume while it is placing; otherwise
                    // acted on here (follow / fly-to).
                    RegisterTap(pos, allowConsequence: !CampSiting.Placing);
                    break;

                case PMode.GrabbingLand:
                    cam.GrabEnd();
                    break;

                case PMode.HoldingVillager:
                    // A refused drop leaves the hand held, per `Hand.DropAt`
                    // — for a pointer that hovers, so the player can try
                    // another spot without having lost the press. A mouse
                    // release is not that: the button is already up, so
                    // there is nothing left held down to try again with.
                    // Cancelling here is the deliberate exception, so the
                    // player is never left silently stuck holding somebody.
                    if (!hand.DropAt(pos, out _)) hand.Cancel();
                    break;
            }
            pmode = PMode.Idle;
            pickupCandidate = null;
        }

        void RegisterTap(Vector2 pos, bool allowConsequence)
        {
            TapThisFrame = true;
            TapAt = pos;

            if (allowConsequence)
            {
                bool isDouble = GestureClassifier.IsDoubleTap(lastTapPos, lastTapTime, pos, Time.unscaledTime, Screen.height);
                if (isDouble) cam.FlyTo(pos);
                else
                {
                    var who = hand.PickAt(pos, forPickup: false);
                    if (who != null) cam.FollowThis(who.transform);
                    else cam.StopFollowing();
                }
            }

            lastTapPos = pos;
            lastTapTime = Time.unscaledTime;
        }

        void HandleOrbit(Mouse mouse, bool altHeld)
        {
            if (orbitSource == OrbitSource.None)
            {
                if (mouse.rightButton.wasPressedThisFrame) BeginOrbit(mouse, OrbitSource.Right);
                else if (mouse.middleButton.wasPressedThisFrame) BeginOrbit(mouse, OrbitSource.Middle);
                else if (altHeld && mouse.leftButton.wasPressedThisFrame) BeginOrbit(mouse, OrbitSource.AltLeft);
                return;
            }

            bool stillHeld = orbitSource switch
            {
                OrbitSource.Right => mouse.rightButton.isPressed,
                OrbitSource.Middle => mouse.middleButton.isPressed,
                OrbitSource.AltLeft => mouse.leftButton.isPressed && altHeld,
                _ => false,
            };

            if (!stillHeld)
            {
                cam.OrbitEnd();
                orbitSource = OrbitSource.None;
                return;
            }

            Vector2 delta = mouse.delta.ReadValue();
            float screenH = Mathf.Max(1f, Screen.height);
            // Same sign convention as `Dev/DockCamTuner.cs`'s right-drag
            // orbit: dragging right turns azimuth negative, dragging "up"
            // (positive delta.y) adds tilt.
            float dAz = -(delta.x / screenH) * Feel.orbitDegPerHeightMouse;
            float dTilt = (delta.y / screenH) * Feel.tiltDegPerHeightMouse;
            if (Mathf.Abs(dAz) > 0.00001f || Mathf.Abs(dTilt) > 0.00001f)
                cam.OrbitAbout(orbitAnchor, dAz, dTilt);
        }

        void BeginOrbit(Mouse mouse, OrbitSource source)
        {
            cam.KillMotion();
            orbitAnchor = mouse.position.ReadValue();
            orbitSource = source;
        }

        void HandleWheel(Mouse mouse)
        {
            Vector2 pos = mouse.position.ReadValue();
            if (UIBlocker.Blocked(pos)) return; // wheel over UI is ignored too

            float scrollY = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scrollY) < 0.01f) return;

            // **One notch is 1, not 120.** Input System 1.11 and later default
            // to `ScrollDeltaBehavior.UniformAcrossAllPlatforms`, which hands
            // over about one unit a notch everywhere; 120 a notch is the old
            // Windows-native range and only comes back if somebody asks for
            // `KeepPlatformSpecificInputRange`. The first version divided by
            // 120 regardless, so the zoom ran at a hundredth of its speed and
            // was only usable at all because a Mac's smooth scrolling sends a
            // great many events. Kevin: *"works but its a bit too slow."*
            bool uniform = InputSystem.settings.scrollDeltaBehavior
                           == InputSettings.ScrollDeltaBehavior.UniformAcrossAllPlatforms;
            float detents = uniform ? scrollY : scrollY / 120f;
            // A flick on a trackpad can report a dozen notches in one frame;
            // more than a few at once is a lurch rather than a zoom.
            detents = Mathf.Clamp(detents, -Feel.wheelMaxPerFrame, Feel.wheelMaxPerFrame);
            cam.ZoomAt(pos, GestureClassifier.WheelFactor(detents));
        }

        void HandleKeys(Keyboard keys, float dt)
        {
            if (keys == null) return;

            float x = 0f, z = 0f, orbit = 0f, tilt = 0f, zoom = 0f;
            if (keys.leftArrowKey.isPressed) x -= 1f;
            if (keys.rightArrowKey.isPressed) x += 1f;
            if (keys.upArrowKey.isPressed) z += 1f;
            if (keys.downArrowKey.isPressed) z -= 1f;
            if (keys.qKey.isPressed) orbit -= 1f;
            if (keys.eKey.isPressed) orbit += 1f;
            if (keys.pageUpKey.isPressed) tilt += 1f;
            if (keys.pageDownKey.isPressed) tilt -= 1f;
            // Equals rather than a shifted "+", so the un-shifted key on the
            // top row zooms in without a modifier — same key `IslandCam`'s
            // own (soon-to-be-replaced) key reading already used.
            if (keys.equalsKey.isPressed || keys.numpadPlusKey.isPressed) zoom -= 1f;
            if (keys.minusKey.isPressed || keys.numpadMinusKey.isPressed) zoom += 1f;

            if (x != 0f || z != 0f || orbit != 0f || tilt != 0f || zoom != 0f)
                cam.Nudge(x, z, orbit, tilt, zoom, dt);

            // R is CampSiting's rotate key while a building is being sited;
            // left alone here on purpose.
            if (keys.endKey.wasPressedThisFrame) cam.Home();
        }

        // =====================================================================
        // TOUCH
        // =====================================================================

        void GatherTouches()
        {
            liveTouches.Clear();
            endedTouches.Clear();
            var ts = Touchscreen.current;
            if (ts == null) return;

            foreach (var t in ts.touches)
            {
                // Fully qualified: `UnityEngine.TouchPhase` (the legacy
                // Input Manager's enum) is in scope too, and the two are
                // not the same type.
                var phase = t.phase.ReadValue();
                int id = t.touchId.ReadValue();
                Vector2 pos = t.position.ReadValue();
                if (phase == UnityEngine.InputSystem.TouchPhase.Began
                    || phase == UnityEngine.InputSystem.TouchPhase.Moved
                    || phase == UnityEngine.InputSystem.TouchPhase.Stationary)
                    liveTouches.Add(new TouchPoint { id = id, pos = pos });
                else if (phase == UnityEngine.InputSystem.TouchPhase.Ended
                    || phase == UnityEngine.InputSystem.TouchPhase.Canceled)
                    endedTouches.Add(new TouchPoint { id = id, pos = pos });
            }
        }

        bool TryFindLive(int id, out Vector2 pos)
        {
            for (int i = 0; i < liveTouches.Count; i++)
                if (liveTouches[i].id == id) { pos = liveTouches[i].pos; return true; }
            pos = default;
            return false;
        }

        bool TryFindEnded(int id, out Vector2 pos)
        {
            for (int i = 0; i < endedTouches.Count; i++)
                if (endedTouches[i].id == id) { pos = endedTouches[i].pos; return true; }
            pos = default;
            return false;
        }

        void HandleTouch()
        {
            switch (touchMode)
            {
                case TouchMode.None:
                    if (liveTouches.Count >= 2) BeginTwoFinger();
                    else if (liveTouches.Count == 1) BeginOneFinger(liveTouches[0]);
                    break;

                case TouchMode.OneFinger:
                    UpdateOneFinger();
                    break;

                case TouchMode.TwoFinger:
                    UpdateTwoFinger();
                    break;

                case TouchMode.WaitLift:
                    // 2 -> 1 does NOT start a new grab: wait for every
                    // finger to lift so the remaining one cannot jump the
                    // view the instant its partner comes off the glass.
                    if (liveTouches.Count == 0) touchMode = TouchMode.None;
                    break;
            }
        }

        void BeginOneFinger(TouchPoint tp)
        {
            oneFingerId = tp.id;
            oneFingerDown = tp.pos;
            oneFingerDownTime = Time.unscaledTime;
            oneFingerUILatched = UIBlocker.Blocked(tp.pos);
            oneSub = OneFingerSub.Pending;
            touchMode = TouchMode.OneFinger;

            if (oneFingerUILatched) return;
            cam.KillMotion();
            oneFingerCandidate = CampSiting.Placing ? null : hand.PickAt(tp.pos, forPickup: true);
        }

        void UpdateOneFinger()
        {
            if (TryFindLive(oneFingerId, out Vector2 pos))
            {
                if (!oneFingerUILatched)
                {
                    if (oneSub == OneFingerSub.Pending)
                    {
                        var sample = new PressSample(oneFingerDown, pos, oneFingerDownTime, Time.unscaledTime, Screen.height);
                        if (oneFingerCandidate != null && GestureClassifier.IsLongPress(sample))
                        {
                            if (hand.PickUp(oneFingerCandidate)) oneSub = OneFingerSub.Holding;
                            else oneFingerCandidate = null; // refused; a drag can still grab the land
                        }
                        else if (GestureClassifier.PastSlop(sample))
                        {
                            oneSub = OneFingerSub.Grabbing;
                            cam.GrabBegin(pos);
                        }
                    }
                    else if (oneSub == OneFingerSub.Grabbing)
                    {
                        cam.GrabMove(pos);
                    }
                    else if (oneSub == OneFingerSub.Holding)
                    {
                        hand.HoldAt(pos);
                        EdgePan(pos);
                        return; // a second finger cannot interrupt a hold
                    }

                    // A second finger landing while this one is still
                    // undecided, or already panning the land, hands off to
                    // the two-finger gesture. `1 -> 2 ends the grab`.
                    if (liveTouches.Count >= 2)
                    {
                        if (oneSub == OneFingerSub.Grabbing) cam.GrabEnd();
                        touchMode = TouchMode.None;
                        BeginTwoFinger();
                        return;
                    }
                }
                return;
            }

            // The tracked finger itself lifted.
            Vector2 releasePos = TryFindEnded(oneFingerId, out Vector2 ep) ? ep : oneFingerDown;
            if (!oneFingerUILatched)
            {
                switch (oneSub)
                {
                    case OneFingerSub.Pending:
                        RegisterTap(releasePos, allowConsequence: !CampSiting.Placing);
                        break;
                    case OneFingerSub.Grabbing:
                        cam.GrabEnd();
                        break;
                    case OneFingerSub.Holding:
                        // No hover on touch, so a refused drop cannot wait
                        // for a better spot the way `Hand.DropAt` otherwise
                        // allows — same reasoning as the mouse path above.
                        if (!hand.DropAt(releasePos, out _)) hand.Cancel();
                        break;
                }
            }
            touchMode = TouchMode.None;
        }

        void EdgePan(Vector2 pos)
        {
            float h = Mathf.Max(1f, Screen.height);
            float edge = Feel.edgePanFrac * h;
            float x = 0f, z = 0f;
            if (pos.x < edge) x -= 1f - pos.x / edge;
            else if (pos.x > Screen.width - edge) x += 1f - (Screen.width - pos.x) / edge;
            if (pos.y < edge) z -= 1f - pos.y / edge;
            else if (pos.y > Screen.height - edge) z += 1f - (Screen.height - pos.y) / edge;
            if (x != 0f || z != 0f) cam.Nudge(x, z, 0f, 0f, 0f, Time.unscaledDeltaTime);
        }

        void BeginTwoFinger()
        {
            if (liveTouches.Count < 2) { touchMode = TouchMode.None; return; }
            twoFingerAId = liveTouches[0].id;
            twoFingerBId = liveTouches[1].id;
            twoFingerPrevA = liveTouches[0].pos;
            twoFingerPrevB = liveTouches[1].pos;
            cam.KillMotion();
            touchMode = TouchMode.TwoFinger;
        }

        void UpdateTwoFinger()
        {
            bool haveA = TryFindLive(twoFingerAId, out Vector2 curA);
            bool haveB = TryFindLive(twoFingerBId, out Vector2 curB);

            if (haveA && haveB)
            {
                float screenH = Mathf.Max(1f, Screen.height);
                TwoFinger.Solve(twoFingerPrevA, twoFingerPrevB, curA, curB, screenH,
                    out float zoomFactor, out float twistDeg, out _, out float sharedVertical);

                Vector2 mid = (curA + curB) * 0.5f;
                if (!Mathf.Approximately(zoomFactor, 1f)) cam.ZoomAt(mid, zoomFactor);

                float tiltDeg = sharedVertical * Feel.tiltDegPerHeightTouch;
                if (Mathf.Abs(twistDeg) > 0.0001f || Mathf.Abs(tiltDeg) > 0.0001f)
                    cam.OrbitAbout(mid, twistDeg, tiltDeg);

                twoFingerPrevA = curA;
                twoFingerPrevB = curB;
                return;
            }

            // One or both fingers lifted.
            cam.OrbitEnd();
            touchMode = liveTouches.Count == 0 ? TouchMode.None : TouchMode.WaitLift;
        }

        // =====================================================================
        // CLEANUP
        // =====================================================================

        /// Yielding to a dev tool, or the view not being engaged, must not
        /// leave a gesture hanging: a grab the camera never hears the end
        /// of is a camera that drifts, and a hand nobody told to let go is
        /// a villager stuck floating over the settings drawer.
        void CancelGesture()
        {
            if (pmode == PMode.GrabbingLand) cam.GrabEnd();
            else if (pmode == PMode.HoldingVillager && hand != null) hand.Cancel();
            pmode = PMode.Idle;
            pickupCandidate = null;

            if (orbitSource != OrbitSource.None)
            {
                cam.OrbitEnd();
                orbitSource = OrbitSource.None;
            }

            if (touchMode == TouchMode.OneFinger)
            {
                if (oneSub == OneFingerSub.Grabbing) cam.GrabEnd();
                else if (oneSub == OneFingerSub.Holding && hand != null) hand.Cancel();
            }
            else if (touchMode == TouchMode.TwoFinger) cam.OrbitEnd();
            touchMode = TouchMode.None;

            TapThisFrame = false;
        }
    }

    /// **The one place a press turns into Tap / Drag / LongPress /
    /// DoubleTap.** Pure and static so `IslandInputProbe` can drive it
    /// directly — no scene, no device — and get the SAME answer at a
    /// phone's 422 px of height and a desk's 2340, because every threshold
    /// here is a fraction of `screenH`, never a pixel count.
    public static class GestureClassifier
    {
        public static float DragFrac(Vector2 a, Vector2 b, float screenH)
            => screenH > 0f ? Vector2.Distance(a, b) / screenH : 0f;

        public static bool PastSlop(in IslandInput.PressSample s)
            => DragFrac(s.downPos, s.pos, s.screenH) > IslandInput.Feel.slopFrac;

        public static bool IsLongPress(in IslandInput.PressSample s)
            => !PastSlop(s) && (s.now - s.downTime) >= IslandInput.Feel.longPressSeconds;

        public static bool IsDoubleTap(Vector2 aPos, float aTime, Vector2 bPos, float bTime, float screenH)
            => (bTime - aTime) >= 0f && (bTime - aTime) < IslandInput.Feel.doubleTapSeconds
            && DragFrac(aPos, bPos, screenH) < IslandInput.Feel.doubleTapFrac;

        /// `n` detents in, `n` out, lands back on exactly 1 — because it is
        /// the same exponential run forwards and backwards, not a lookup
        /// table that could drift out of step with itself. See
        /// `IslandCam.ZoomAt`: a factor below 1 zooms in.
        public static float WheelFactor(float detents)
            => Mathf.Exp(-detents * Mathf.Log(IslandInput.Feel.wheelStep));
    }
}
