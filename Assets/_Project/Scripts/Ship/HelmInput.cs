using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;

namespace SeaSick.Ship
{
    /// The helm. W/A/S/D at a desk, ONE THUMB on a phone — DREDGE's model
    /// (2026-10-03, docs/PLAN-dredge-controls.md §3-§4, Kevin's decisions).
    ///
    /// Touch is a floating analog stick (`SeaStick`) that appears under the
    /// thumb in the lower part of the screen. It is **boat-relative**:
    /// - stick Y is SPEED, in proportion to the push: up = ahead 0..1, down
    ///   = astern 0..`SailControlTuning.reverseCap`;
    /// - stick X is the TURN (the rudder, through `turnCurve`). A pure
    ///   sideways push at rest pivots her (the motor's rest turn).
    /// **Nothing latches.** Let go and the throttle order and the rudder are
    /// both zero; she glides to a stop on the coast-down physics. There is no
    /// camera frame anywhere in it, no heading autopilot on the stick, no
    /// tap = stop and no past-the-rim burn.
    ///
    /// **Boost** is a Haste-style toggle (`ToggleBoost`, the sea HUD's ⚡
    /// button): one thumb can't hold the stick and a button, so a tap arms
    /// it and, while armed, a push ahead reaches up to `motor.Overdrive`
    /// (the burn tier, `ShipMotor.Burning`). It disarms on a second tap, on
    /// `AllStop`, or once she has had no push ahead for
    /// `boostIdleSeconds` and is near stopped. At a desk Shift HELD boosts.
    ///
    /// Keys: W/S or ↑/↓ = ahead/astern, A/D or ←/→ = turn, each ramped over
    /// `keyRampSeconds` so they read analog; R toggles rowing. The stick
    /// wins while a thumb (or the mouse) holds it.
    ///
    /// **Taps.** A touch only starts the stick where `UIBlocker` says it is
    /// free, so RescueHud's swimmer tap zones and every HUD control keep
    /// their taps (they block their own rects). A tap on an enemy ship is
    /// NOT excluded: it may start the stick for a moment, but with nothing
    /// latched a tap orders nothing, and `CombatLock` still reads the same
    /// touch-up as a lock. Excluding it at touch-down would only stop a
    /// thumb landing on an enemy from sailing (`CombatLock.WouldLock` is
    /// no longer needed here).
    [RequireComponent(typeof(ShipMotor))]
    public class HelmInput : MonoBehaviour
    {
        [Header("Steer toward (man overboard)")]
        [Tooltip("Rudder order per degree of heading error while steering toward a swimmer.")]
        [SerializeField] float steerKp = 0.04f;
        [Tooltip("Rudder order per degree/second of yaw rate, SUBTRACTED from the Kp term so the turn brakes itself into the bearing instead of hunting.")]
        [SerializeField] float steerKd = 0.02f;
        [Tooltip("Rudder units/s the swimmer steer's output is eased at.")]
        [SerializeField] float rudderEaseSpeed = 3.5f;
        [SerializeField, Range(-1f, 1f)] float testRudder = 0f; // editor/testing override

        ShipMotor motor;
        Breakers breakers;

        /// The stick. A plain object, not a component: it has no lifetime of
        /// its own and nothing else should be able to find it.
        readonly SeaStick stick = new SeaStick();

        Vector2 keyAxes;          // W/S/A/D, ramped (see keyRampSeconds)
        float rudder;             // what went to motor.Rudder
        float throttleOut;        // what went to motor.ThrottleOrder
        bool boostArmed;          // the ⚡ toggle
        bool shiftBoost;          // Shift held this frame
        bool pushingAhead;        // stick/keys asking for headway this frame
        float boostIdle;          // s with no push ahead while armed
        float prevHeadingDeg;
        bool prevHeadingValid;

        // --- boost ------------------------------------------------------------

        /// Boost is ready: the ⚡ toggle is armed, or Shift is held at a desk.
        public bool BoostArmed => boostArmed || shiftBoost;
        /// Tap the ⚡ button: arm boost, or disarm it.
        public void ToggleBoost() { boostArmed = !boostArmed; boostIdle = 0f; }
        /// Armed AND actually pushing ahead, so the order is reaching into
        /// the burn tier.
        public bool Boosting => BoostArmed && pushingAhead;

        /// `Time.time` of the last boost ENGAGE (`Boosting` false -> true,
        /// after at least `BoostTuning.reengageGuardSeconds` off), or
        /// -infinity. The punch's camera reads this (compare to the last value
        /// seen); the haptic tick and the surge fire from here.
        public float BoostEngagedAt { get; private set; } = float.NegativeInfinity;
        /// Fired once per engage, after `BoostEngagedAt` is stamped.
        public event System.Action BoostEngaged;
        bool wasBoosting;
        float boostOffSince = float.NegativeInfinity; // Time.time boost last went off

        /// The engage edge and the surge envelope. The envelope (1 -> 0 over
        /// `BoostTuning.surgeSeconds`, cut the moment boost ends) goes to
        /// `motor.BoostSurge01`, which `PaddleDrive`/`ShipMotor` turn into a
        /// push and `PaddleSound` into a chug-up.
        void UpdateBoostPunch()
        {
            bool now = Boosting;
            float t = Time.time;
            if (now && !wasBoosting
                && t - boostOffSince >= System.Math.Max(0f, BoostTuning.reengageGuardSeconds))
            {
                BoostEngagedAt = t;
                if (BoostTuning.hapticOnEngage && BoostTuning.punch > 0f) OverboardHaptics.Boost();
                BoostEngaged?.Invoke();
            }
            if (!now && wasBoosting) boostOffSince = t;
            wasBoosting = now;

            float since = t - BoostEngagedAt;
            float dur = Mathf.Max(0.05f, BoostTuning.surgeSeconds);
            motor.BoostSurge01 = now && since < dur ? 1f - since / dur : 0f;
        }

        // --- steer-toward (man overboard, phase 5b) --------------------------
        Transform steerTarget;
        string steerTargetName;
        /// A turn (0..1, after the curve) above this takes the helm back from
        /// the swimmer steer. Below it the stick only sets the speed.
        const float SteerKeepTurn = 0.35f;

        /// **Tap a swimmer / the edge arrow -> autopilot toward them.** The
        /// bow is kept on `target` every frame. A push on the stick (or W/S)
        /// sets the speed; with no push she sails at the modest
        /// `SailControlTuning.steerTowardThrottle`, easing off as she closes
        /// (release no longer holds a speed, so the steer has to). Ends when
        /// the player turns past `SteerKeepTurn`, when `target` resolves
        /// (its GameObject destroyed — a Unity "fake null", caught by the
        /// plain `!= null`), or on `CancelSteerToward()`. Called by
        /// `RescueHud` (its tap zones block `SeaStick` first) and `SeaHud`.
        public void SteerToward(Transform target, string label)
        {
            steerTarget = target;
            steerTargetName = label;
        }

        public void CancelSteerToward() { steerTarget = null; }
        public bool SteeringToward => steerTarget != null;
        public string SteerTargetName => steerTargetName;

        void Awake()
        {
            motor = GetComponent<ShipMotor>();
            breakers = GetComponent<Breakers>();
        }

        // `Touch.activeTouches` is empty until this is on, and it is
        // ref-counted, so enabling it per-component is safe even if something
        // else in the project starts doing the same.
        void OnEnable() => EnhancedTouchSupport.Enable();

        void OnDisable()
        {
            EnhancedTouchSupport.Disable();
            // Probes disable the helm and drive the motor themselves: no
            // stale "nobody is steering" left behind for the heading hold.
            if (motor != null) { motor.HoldAllowed = false; motor.BoostSurge01 = 0f; }
            wasBoosting = false;
        }

        void Update()
        {
            float dt = Time.deltaTime;

            // **Not while the island view or the shipyard modal is up.** She
            // is anchored then, but the stick would still take a drag meant
            // for the ground (siting a building, pressing on a crewman), and
            // R is also `CampSiting`'s rotate-the-ghost key.
            bool ashore = SeaSick.CameraRig.IslandCam.Engaged
                || SeaSick.Ship.Modular.ShipyardSession.WorldInputBlocked;
            stick.Sample(!ashore);

            // --- keys: held, ramped, the desktop override ---
            Vector2 keyTarget = Vector2.zero;
            shiftBoost = false;
            var kb = Keyboard.current;
            if (kb != null && !ashore)
            {
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) keyTarget.x -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) keyTarget.x += 1f;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) keyTarget.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) keyTarget.y -= 1f;
                shiftBoost = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                // R, not space — space already dismisses the voyage tally.
                if (kb.rKey.wasPressedThisFrame) motor.Rowing = !motor.Rowing;
            }
            float ramp = SailControlTuning.keyRampSeconds;
            float step = ramp > 1e-3f ? dt / ramp : 1f;
            keyAxes.x = Mathf.MoveTowards(keyAxes.x, keyTarget.x, step);
            keyAxes.y = Mathf.MoveTowards(keyAxes.y, keyTarget.y, step);
            if (ashore) keyAxes = Vector2.zero;

            Vector2 axes = stick.Held ? stick.Axes : keyAxes;

            // --- what the axes mean ---
            float turn = Curve(axes.x, SailControlTuning.turnCurve);
            if (!Mathf.Approximately(testRudder, 0f)) turn = testRudder;
            float push = Mathf.Clamp(axes.y, -1f, 1f);
            float shaped = Curve(push, SailControlTuning.throttleCurve);
            pushingAhead = shaped > 0f;

            // Boost: disarms on its own once she has sat with no push ahead
            // and come (nearly) to a stop.
            if (ashore) boostArmed = false;
            if (boostArmed)
            {
                boostIdle = pushingAhead ? 0f : boostIdle + dt;
                if (boostIdle >= SailControlTuning.boostIdleSeconds
                    && motor.CurrentSpeed < SailControlTuning.boostStopSpeed)
                    boostArmed = false;
            }
            UpdateBoostPunch();
            float aheadTop = BoostArmed ? Mathf.Max(1f, motor.Overdrive) : 1f;
            float throttle = shaped >= 0f
                ? shaped * aheadTop
                : shaped * Mathf.Clamp01(SailControlTuning.reverseCap);

            // --- man overboard: the swimmer has the bow until a real turn ---
            bool steering = false;
            float rudderTarget = turn;
            float rudderRate = SailControlTuning.rudderSlewPerSec;
            if (steerTarget != null && Mathf.Abs(turn) > SteerKeepTurn) steerTarget = null;
            if (steerTarget != null)
            {
                steering = true;
                Vector3 d = steerTarget.position - transform.position; d.y = 0f;
                float bearing = d.sqrMagnitude > 0.01f ? Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg : motor.Heading;
                rudderTarget = SteerFor(bearing, dt);
                rudderRate = rudderEaseSpeed;
                if (Mathf.Approximately(push, 0f) && !motor.Anchored)
                {
                    float slowR = Mathf.Max(1f, SailControlTuning.steerTowardSlowHulls * motor.HullLength);
                    throttle = Mathf.Lerp(SailControlTuning.steerTowardSlowThrottle,
                        SailControlTuning.steerTowardThrottle, Mathf.Clamp01(d.magnitude / slowR));
                }
            }
            prevHeadingDeg = motor.Heading;
            prevHeadingValid = true;

            rudder = Mathf.MoveTowards(rudder, rudderTarget, Mathf.Max(0f, rudderRate) * dt);
            motor.Rudder = rudder;
            // The engine's ramp in ShipMotor does the smoothing, and the
            // crew's condition sets how fast it ramps.
            throttleOut = throttle;
            motor.ThrottleOrder = throttleOut;
            // Heading hold (HeadingHold): the drive may hold the course while
            // nobody is turning her. It still waits for the blade to centre
            // and the turn to die before it captures.
            motor.HoldAllowed = !steering && Mathf.Approximately(turn, 0f);
        }

        /// sign(a) * |a|^curve, so the rim is still 1 and the centre is finer.
        static float Curve(float a, float curve)
        {
            float m = Mathf.Min(1f, Mathf.Abs(a));
            return Mathf.Sign(a) * Mathf.Pow(m, Mathf.Max(0.05f, curve));
        }

        /// The swimmer steer's rudder: Kp on the heading error, Kd on the yaw
        /// rate (negated: it BRAKES the turn rather than chasing the error).
        float SteerFor(float bearingDeg, float dt)
        {
            float err = Mathf.DeltaAngle(motor.Heading, bearingDeg);
            float yawRate = 0f;
            if (prevHeadingValid && dt > 1e-5f)
                yawRate = Mathf.DeltaAngle(prevHeadingDeg, motor.Heading) / dt;
            float steer = Mathf.Clamp(steerKp * err + steerKd * -yawRate, -1f, 1f);
            // Going astern the same rudder swings her the other way, so flip
            // on which way she is actually moving through the water (not the
            // order: the two disagree right at the ahead/astern change).
            float fwdSpeed = Vector3.Dot(motor.Velocity, transform.forward);
            if (fwdSpeed < -0.05f) steer = -steer;
            return steer;
        }

        /// The live order in one short word. Every branch returns an interned
        /// literal, so `OrderWord` can be compared by reference (the sea
        /// HUD's order strip re-texts only when it moves).
        string Label()
        {
            float o = motor.ThrottleOrder;
            if (o > 1.02f) return "Burn";
            if (o > 0.05f) return "Ahead";
            if (o < -0.05f) return "Astern";
            return "Stop";
        }

        /// Stop from outside (berthing): drop the stick, the ramped keys and
        /// boost, and zero the orders. The helm re-asserts the motor's orders
        /// every Update, so it is the INPUT that has to go to zero; the motor
        /// is zeroed too so this frame agrees. (A swimmer steer is kept, as it
        /// always was; its throttle stands down while she is anchored.)
        public void AllStop()
        {
            stick.CancelDrag();
            keyAxes = Vector2.zero;
            boostArmed = false;
            boostIdle = 0f;
            pushingAhead = false;
            if (motor != null) motor.BoostSurge01 = 0f;
            rudder = 0f;
            throttleOut = 0f;
            if (motor != null) { motor.ThrottleOrder = 0f; motor.Rudder = 0f; }
        }

        /// True while a thumb (or the mouse) holds the sea stick. The sea
        /// HUD's first-use hint goes for good the first frame this is true
        /// (`GestureHints`).
        public bool StickInUse => stick.Held;

        /// The ship's surf-zone reader, for the sea HUD's sea-state word
        /// ("BREAKERS" outranks the sea's name). Null on a ship without one.
        public Breakers Breakers => breakers;

        /// The live order word ("Stop", "Ahead", "Astern", "Burn"): one of
        /// `Label()`'s interned literals, so a caller can compare it by
        /// reference and never allocates.
        public string OrderWord => motor != null ? Label() : "Stop";

        /// Only the floating stick is IMGUI (the order strip and the ⚡
        /// button are the UI Toolkit sea HUD, `UI/Sheets/SeaHud.cs`): the ring
        /// follows the finger at 60 Hz with rotated rects.
        void OnGUI()
        {
            // The island sheet docks to the bottom of a portrait phone; the
            // stick would be drawn under it, on a ship that is anchored anyway.
            if (SeaSick.CameraRig.IslandCam.Engaged) return;
            // Same IMGUI-blind-spot suppression as the rest of the HUD.
            if (SeaSick.UI.ModularYard.ShipyardModal.IsOpen
                || SeaSick.UI.Menus.GameMenus.Current != SeaSick.UI.Menus.GameMenus.Mode.None) return;
            if (Event.current.type != EventType.Repaint) return;

            stick.Draw(throttleOut < -0.05f, Boosting || (motor != null && motor.Burning));
        }
    }
}
