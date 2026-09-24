using SeaSick.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;

namespace SeaSick.Ship
{
    /// The helm. WASD at a desk, ONE THUMB on a phone.
    ///
    /// Touch is a floating stick (see `TouchHelm`): put a thumb down anywhere
    /// in the lower half of the screen and drag. The DIRECTION is the world
    /// heading she's ordered to steer for; the DISTANCE is the throttle,
    /// continuous from stop out to full ahead and, past the rim, into the
    /// burn tier. Let go and she KEEPS doing it — hands-free cruising — until
    /// the next touch changes the order. A tap (short, barely moved) rings
    /// the telegraph to stop without letting go of the heading. Dragging
    /// behind her while nearly stopped orders astern instead of a heading
    /// change; behind her while still making way just asks her to come
    /// about.
    ///
    /// Replaces the axis-locked swipe wheel (2026-09-22), which rubber-banded
    /// the rudder back to midships on release and stepped the telegraph
    /// through notches on a flick. Kevin, same day, on THAT control: "slow,
    /// uneventful, not responsive." Holding a course is what a real helm
    /// does; a phone thumb can't hold anything and steer at the same time, so
    /// this control holds the course FOR you and gives the thumb back.
    ///
    /// W/S still drive directly (held, not an order that survives release)
    /// and A/D still steer directly, both straight through to the rudder —
    /// the desktop override, not a second version of the touch policy. A/D
    /// also clear the heading target, so letting go of the key doesn't snap
    /// her back toward a stale course.
    ///
    /// ## This class owns the POLICY
    ///
    /// `TouchHelm` turns one finger into a world heading, a distance and a
    /// tap; this class decides what they mean (the throttle curve, the
    /// astern/come-about split) and runs the heading autopilot: a Kp/Kd
    /// controller on heading error and yaw rate, eased into the rudder so it
    /// settles instead of hunting.
    [RequireComponent(typeof(ShipMotor))]
    public class HelmInput : MonoBehaviour
    {
        [Header("Heading autopilot")]
        [Tooltip("Rudder order per degree of heading error. Default (1/25) puts full rudder at the same ~25 degrees the old hard-over lived at. UNTESTED ON DEVICE — first thing to hand-tune against the real hull.")]
        [SerializeField] float steerKp = 0.04f;
        [Tooltip("Rudder order per degree/second of yaw rate, SUBTRACTED from the Kp term so the turn brakes itself into the target heading instead of swinging past it and hunting back. UNTESTED — raise it if she oscillates around a course, lower it if she is sluggish to settle.")]
        [SerializeField] float steerKd = 0.02f;
        [Tooltip("Rudder units/s the autopilot's OUTPUT is eased at, same job engageSpeed did for the old wheel: turns the Kp/Kd command into a gear change rather than a switch.")]
        [SerializeField] float rudderEaseSpeed = 3.5f;
        [SerializeField, Range(-1f, 1f)] float testRudder = 0f; // editor/testing override

        [Header("Floating stick")]
        [Tooltip("Distance past the ring's rim, as a fraction of its radius, at which the order reaches full burn. 1.0 = the rim itself; between 1.0 and this the order ramps from full ahead into burn.")]
        [SerializeField] float burnEngageFrac = 1.10f;
        [Tooltip("Below this speed (m/s) a drag pointed behind her orders astern. At or above it the same drag just asks her to come about — she has to be nearly stopped to back down.")]
        [SerializeField] float asternSpeedThreshold = 1.5f;
        [Tooltip("Degrees off her CURRENT heading a drag has to point before it counts as behind her rather than a wide turn.")]
        [SerializeField] float asternAngleThreshold = 135f;

        ShipMotor motor;
        Breakers breakers;

        /// The stick. A plain object, not a component: it has no lifetime of
        /// its own and nothing else should be able to find it.
        readonly TouchHelm helm = new TouchHelm();

        // --- policy state, held across frames so letting go keeps ordering it ---
        float targetHeadingDeg;
        bool hasTarget;
        float throttleOrder;
        bool astern;

        // --- tap-to-sail (the seam for Astra's cinematic tap, 2026-09-24) ---
        Vector3? sailTo;
        bool sailToStop;

        /// **Sail to a point on the water.** The heading autopilot steers
        /// for it every frame; the throttle keeps whatever was ordered, or
        /// takes `cruise01` if she was stopped so a tap actually moves her.
        /// Arriving (within 1.5 hull lengths, at least 12 m) rings down to
        /// stop when `stopThere`, else just lets the course go. Any thumb on
        /// the stick or a held steering key takes over and cancels it, so
        /// the player always wins over the tap.
        public void SailTo(Vector3 worldPoint, bool stopThere = true, float cruise01 = 0.6f)
        {
            helm.CancelDrag();
            sailTo = worldPoint;
            sailToStop = stopThere;
            astern = false;
            if (throttleOrder <= 0.05f) throttleOrder = Mathf.Clamp01(cruise01);
            SteerForSailTo();
        }

        public void CancelSailTo() { sailTo = null; }
        public bool Sailing => sailTo.HasValue;
        public Vector3? SailTarget => sailTo;

        void SteerForSailTo()
        {
            if (!sailTo.HasValue || motor == null) return;
            Vector3 d = sailTo.Value - transform.position; d.y = 0f;
            float arrive = Mathf.Max(12f, 1.5f * motor.HullLength);
            if (d.magnitude <= arrive)
            {
                if (sailToStop) { throttleOrder = 0f; astern = false; }
                sailTo = null;             // course held, as after a released drag
                return;
            }
            targetHeadingDeg = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            hasTarget = true;
        }
        float rudder;
        float prevHeadingDeg;
        bool prevHeadingValid;

        // IMGUI runs OnGUI once per EVENT, so a string built here is built
        // several times a frame — Layout, Repaint, and one more for every
        // mouse move. See StatusHUD for the measurement. These two are the
        // only strings this panel makes; everything else it draws is a
        // literal. They are rebuilt when the thing they say changes, and not
        // otherwise.
        readonly GUIContent orderText = new GUIContent("");
        string orderTextFrom;          // the Label() literal it was built from
        bool orderTextMoving;
        readonly HudLabel easeText = new HudLabel();

        void Awake()
        {
            motor = GetComponent<ShipMotor>();
            breakers = GetComponent<Breakers>();
        }

        // `Touch.activeTouches` is empty until this is on, and it is
        // ref-counted, so enabling it per-component is safe even if something
        // else in the project starts doing the same.
        void OnEnable() => EnhancedTouchSupport.Enable();
        void OnDisable() => EnhancedTouchSupport.Disable();

        void Update()
        {
            // **Not while the island view is up.** She is anchored whenever
            // that view is engaged, so nothing moves -- but the stick would
            // still take a drag meant for the ground (siting a building,
            // pressing on a crewman) and HOLD it, and she would sail off it
            // the moment the view closed. The same reason the arrows and WASD
            // are guarded: R would otherwise ring down rowing at the exact
            // moment it is also `CampSiting`'s rotate-the-ghost key.
            bool ashore = SeaSick.CameraRig.IslandCam.Engaged;
            helm.Sample(!ashore);

            if (!ashore) ReadStick();

            var kb = Keyboard.current;
            bool manualSteer = false;
            float manualRudder = 0f;
            float? drive = null;

            if (kb != null && !ashore)
            {
                // A held key OVERRIDES the gesture and hands it straight back
                // on release. Unlike the old rubber band there is nothing to
                // hand back TO here except a stale course, so releasing the
                // key hands back whatever heading she is on at that instant.
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) { manualRudder = -1f; manualSteer = true; }
                else if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) { manualRudder = 1f; manualSteer = true; }

                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) drive = 1f;
                else if (kb.sKey.isPressed || kb.downArrowKey.isPressed) drive = -1f;
                // R, not space — space already dismisses the voyage tally.
                if (kb.rKey.wasPressedThisFrame) motor.Rowing = !motor.Rowing;
            }

            if (!Mathf.Approximately(testRudder, 0f)) { manualRudder = testRudder; manualSteer = true; }

            // A tapped destination steers until the player takes the helm
            // back: a thumb on the stick, a tap (which rang her down), or a
            // held key all cancel it.
            if (sailTo.HasValue)
            {
                if (manualSteer || helm.Dragging || helm.Tapped) sailTo = null;
                else SteerForSailTo();
            }

            float rudderTarget;
            if (manualSteer)
            {
                rudderTarget = manualRudder;
                targetHeadingDeg = motor.Heading;
                hasTarget = true;
            }
            else if (!hasTarget)
            {
                rudderTarget = 0f;
            }
            else
            {
                // Kp on the heading error, Kd on the yaw rate (negated: it
                // BRAKES the turn rather than chasing the error), full rudder
                // saturating past the error a real helm would already be hard
                // over at. `DeltaAngle` keeps the error signed and wrapped, so
                // a target 179 degrees away doesn't fight itself over which
                // way is shorter.
                float err = Mathf.DeltaAngle(motor.Heading, targetHeadingDeg);
                float yawRate = 0f;
                float dt = Time.deltaTime;
                if (prevHeadingValid && dt > 1e-5f)
                    yawRate = Mathf.DeltaAngle(prevHeadingDeg, motor.Heading) / dt;

                float steer = steerKp * err + steerKd * -yawRate;
                steer = Mathf.Clamp(steer, -1f, 1f);
                // Going astern, the same rudder swings the stern the other
                // way relative to the bow's heading error, so the correction
                // has to flip with it. This has to key off which way she is
                // actually moving through the water (sign of her forward
                // velocity), not the stick's `astern` order flag — the order
                // and her way disagree right at the astern/ahead transition
                // (ringing ahead while she still carries sternway, or vice
                // versa), and flipping on the wrong signal steers her the
                // wrong way at exactly the moment the autopilot is fighting
                // to bring her round.
                float fwdSpeed = Vector3.Dot(motor.Velocity, transform.forward);
                if (fwdSpeed < -0.05f) steer = -steer;
                rudderTarget = steer;
            }

            prevHeadingDeg = motor.Heading;
            prevHeadingValid = true;

            rudder = Mathf.MoveTowards(rudder, rudderTarget, rudderEaseSpeed * Time.deltaTime);
            motor.Rudder = rudder;

            // The engine's ramp in ShipMotor does the smoothing, and the
            // crew's condition sets how fast it ramps -- an order is still
            // only as good as whoever is below to answer it.
            motor.ThrottleOrder = drive ?? throttleOrder;
        }

        /// Reads the floating stick and turns it into the policy this frame:
        /// a held world heading, a continuous throttle order, and whether
        /// she's being asked to back down rather than steer.
        ///
        /// Nothing here runs while a finger isn't on the stick — the whole
        /// point of "hands-free cruising" is that `targetHeadingDeg` and
        /// `throttleOrder` simply keep their last value until the next touch.
        void ReadStick()
        {
            if (helm.Tapped)
            {
                // Ring down to stop without letting go of the course.
                throttleOrder = 0f;
                astern = false;
                return;
            }

            if (!helm.Dragging) return;

            if (helm.DragDistance01 < TouchHelm.DeadZoneFrac)
            {
                // Thumb is down but still inside the dead zone: not a change
                // of order. A genuine TAP (short, barely moved) already fired
                // `helm.Tapped` above on release; a held-still touch here
                // must not stop her — keep whatever heading/throttle she
                // already had.
                return;
            }

            float mag01 = Mathf.Clamp01(
                (helm.DragDistance01 - TouchHelm.DeadZoneFrac) / (1f - TouchHelm.DeadZoneFrac));

            bool behindHer = helm.HasDragDirection
                && Mathf.Abs(Mathf.DeltaAngle(motor.Heading, helm.DragHeadingDeg)) > asternAngleThreshold;
            // Latched: once astern, a drag that still points behind her keeps
            // her backing down no matter how fast the sternway builds — the
            // speed gate only decides whether astern can be ENTERED, so
            // holding the drag doesn't flip her to full ahead + a 180-degree
            // target the instant she passes the threshold going backwards.
            bool pointsAstern = astern
                ? behindHer
                : behindHer && motor.CurrentSpeed < asternSpeedThreshold;

            if (pointsAstern)
            {
                astern = true;
                throttleOrder = -mag01; // ShipMotor clamps the order to -1 anyway
                // "Don't spin her": while backing, the held heading just
                // tracks whatever she's doing right now, so the autopilot's
                // error stays near zero and only the astern-flipped Kd term
                // is left doing any correcting.
                targetHeadingDeg = motor.Heading;
                hasTarget = true;
            }
            else
            {
                astern = false;
                if (helm.HasDragDirection)
                {
                    targetHeadingDeg = helm.DragHeadingDeg;
                    hasTarget = true;
                }
                throttleOrder = ThrottleFromDistance(helm.DragDistance01);
            }
        }

        /// 0 at the dead zone's edge to 1 at the rim, then a ramp from 1 up
        /// to `motor.Overdrive` between the rim and `burnEngageFrac` — past
        /// that the order clamps at full burn, which is what "the knob
        /// clamps at the rim, with a visible burn state" means in practice:
        /// the drawn knob stops moving but the order it stands for keeps
        /// climbing until burn is fully engaged.
        float ThrottleFromDistance(float distFrac)
        {
            const float Dz = TouchHelm.DeadZoneFrac;
            if (distFrac < Dz) return 0f;
            if (distFrac <= 1f) return Mathf.Clamp01((distFrac - Dz) / (1f - Dz));
            float burnT = Mathf.InverseLerp(1f, burnEngageFrac, distFrac);
            return Mathf.Lerp(1f, motor.Overdrive, Mathf.Clamp01(burnT));
        }

        /// What she is being asked to do RIGHT NOW, which is the held key if
        /// there is one -- the readout must never say "stop" while a finger
        /// on W has her making way.
        ///
        /// The words are bands rather than a lookup on the notch, because a
        /// held W/S is an order too and it must read as one. Every branch
        /// returns an interned literal, which is what lets `OrderText` cache
        /// on reference equality.
        string Label()
        {
            float o = motor.ThrottleOrder;
            if (o > 1.02f) return "BURN";
            if (o > 0.85f) return "full ahead";
            if (o > 0.55f) return "half ahead";
            if (o > 0.05f) return "slow ahead";
            if (o < -0.85f) return "full astern";
            if (o < -0.05f) return "astern";
            return "stop";
        }

        /// `Label()` costs nothing — it returns one of the literals above — but
        /// appending the ellipsis does, so the joined string is kept until one
        /// of its two inputs moves. Reference equality is enough: every branch
        /// of `Label()` hands back an interned literal.
        GUIContent OrderText()
        {
            string l = Label();
            bool moving = motor.ThrottleMoving;
            if (!ReferenceEquals(l, orderTextFrom) || moving != orderTextMoving)
            {
                orderTextFrom = l;
                orderTextMoving = moving;
                orderText.text = moving ? l + " …" : l;
            }
            return orderText;
        }

        /// Ring down STOP from outside, and drop whatever gesture is in
        /// progress. The notch is re-asserted into `motor.ThrottleOrder`
        /// every Update, so anything that wants her stopped has to move the
        /// CONTROL, not the value the control produces — writing the value
        /// lasts exactly one frame and then the helm quietly puts it back.
        public void AllStop()
        {
            helm.CancelDrag();
            throttleOrder = 0f;
            astern = false;
            rudder = 0f;
        }

        /// **Hook, not a mechanic.** How hard the burn notch is eating wood
        /// right now, 0 when she is not burning. Nothing consumes it yet.
        /// TODO(GDD §6 / ship ladder): burn costs wood per distance made —
        /// wire this to the hold and make running out drop her to full ahead.
        public float BurnRate01 => motor != null && motor.Burning ? 1f : 0f;

        void OnGUI()
        {
            // The helm is not on screen while she lies at a camp: the island
            // sheet docks to the bottom of a portrait phone and the stick
            // would be drawn under it, on a ship that is anchored anyway.
            if (SeaSick.CameraRig.IslandCam.Engaged) return;

            int u = HudLayout.Unit;

            // The stick and its readout, bottom of the screen where a thumb
            // is. They place themselves (`HudLayout.Slot.Wheel`) and claim
            // their own rects with `UIBlocker`, so a tap on the helm never
            // reaches the water underneath it.
            helm.Draw(targetHeadingDeg, hasTarget, throttleOrder, astern, motor.Burning,
                motor.Heading, OrderText());

            // --- Point of sail: the readout that teaches the whole system ---
            float panelW = u * 10f;
            // Shorter by the two rows the ▲/▼ buttons and the order caption
            // used: the order lives above the wheel now, where the thumb that
            // sets it is. What is left is the readout that cannot move --
            // what the water is doing, and what she is making of it.
            float panelH = u * 3.9f;
            // The bottom-right cluster, placed rather than pinned to the
            // screen corner -- which on a notched phone was under the home
            // indicator, and which nothing else on screen knew the extent of.
            var panel = HudLayout.Place(HudLayout.Slot.Helm, panelW, panelH);
            float px = panel.x, py = panel.y;
            UITheme.Rect(panel, UITheme.Panel);

            // What the water is doing, and how much of it you're taking on the
            // bow. No point of sail, no no-go: this reads "the sea is heavy and
            // you're driving into it", which is the only heading cost left.
            float strain = motor.HeadSea01 * motor.SeaSeverity01;
            var nameColour = strain > 0.4f ? UITheme.Warn : UITheme.Text;

            // Warnings live ON the instrument, never in a banner over the
            // boat. The sea's name is the line that already describes the
            // water, so the two things the water is doing TO her go here and
            // take the line's colour with them. Breakers outrank a broach:
            // one is a bad few seconds, the other is the beach.
            string seaLine = motor.SeaStateName;
            if (breakers != null && breakers.Breaking01 > 0.3f)
            {
                seaLine = "BREAKERS";
                nameColour = UITheme.Bad;
            }
            else if (motor.Broach01 > 0.35f)
            {
                seaLine = "broaching";
                nameColour = UITheme.Bad;
            }
            // GUI.contentColor tints the text without building a style. A
            // per-frame `new GUIStyle` allocates AND invalidates IMGUI's
            // cached text mesh for everything drawn with it.
            var prevContent = GUI.contentColor;
            GUI.contentColor = nameColour;
            GUI.Label(new Rect(px, py + u * 0.2f, panelW, u * 1.6f),
                seaLine, UITheme.Small2Centered);
            GUI.contentColor = prevContent;

            var effRect = new Rect(px + u * 0.6f, py + u * 1.9f, panelW - u * 1.2f, u * 0.5f);
            UITheme.Bar(effRect, motor.SeaResistance01, UITheme.Ramp(1f - motor.SeaResistance01));

            // Astern fills the same bar backwards from a centre mark, so
            // which way she is being driven reads without being read. It is
            // the ACHIEVED throttle; the order is on the stick's own readout.
            var barRect = new Rect(px + u * 0.6f, py + u * 2.65f, panelW - u * 1.2f, u * 0.22f);
            float t = Mathf.Clamp(motor.Throttle, -1f, 1f);
            float mid = barRect.x + barRect.width * 0.32f;
            if (t >= 0f)
                UITheme.Bar(new Rect(mid, barRect.y, (barRect.xMax - mid) * t, barRect.height),
                    1f, motor.ThrottleMoving ? UITheme.Warn : UITheme.Sea);
            else
                UITheme.Bar(new Rect(mid + (mid - barRect.x) * t, barRect.y,
                    (mid - barRect.x) * -t, barRect.height), 1f, UITheme.Warn);

            DrawWayGauge(new Rect(px + u * 0.6f, py + u * 3.2f,
                panelW - u * 1.2f, u * 0.4f), u);

            // Oars and easing share the band the oars had to themselves, so
            // the panel's footprint on a portrait phone does not grow: two
            // half-width controls where there was one, both still under a
            // thumb.
            float half = (panelW - u * 0.4f) * 0.5f;
            // Its own slot directly above the helm panel, so the two move
            // together and the prompt slot knows how high this cluster reaches.
            float rowY = HudLayout.Place(HudLayout.Slot.HelmActions, panelW, u * 1.8f).y;

            // Oars: wind-independent, but pure labour — a crew at the rail
            // can't pull, so this stops being the guaranteed way home.
            float oars = motor.OarPower01;
            var row = new Rect(px, rowY, half, u * 1.8f);
            UIBlocker.Block(row);
            var rowStyle = motor.Rowing ? UITheme.ButtonPressed : UITheme.Button;
            string oarLabel = oars < 0.02f ? "— oars" : motor.Rowing ? "◉ rowing" : "◎ oars";
            GUI.enabled = oars >= 0.02f;
            if (GUI.Button(row, oarLabel, rowStyle)) motor.Rowing = !motor.Rowing;
            GUI.enabled = true;

            // **The one verb at the helm besides the tiller.** Driving flat
            // out into a head sea does not make her faster, it makes her
            // launch off the crest and land on her forefoot; easing gives up
            // way on purpose so she rides instead. It costs time, and the
            // label says what it is costing right now so the trade is visible
            // rather than folklore — in calm water it reads "0%" and the
            // player learns for themselves that there is nothing to ease for.
            var easeRect = new Rect(px + half + u * 0.4f, rowY, half, u * 1.8f);
            UIBlocker.Block(easeRect);
            var easeStyle = motor.Easing ? UITheme.ButtonPressed : UITheme.Button;
            // Whole percent is what is DISPLAYED, so that — plus the on/off —
            // is the key. The cost twitches every frame; the label does not.
            int easePct = Mathf.RoundToInt(motor.EaseCost01 * 100f);
            if (easeText.Changed(HudLabel.Key(motor.Easing ? easePct : -1)))
                easeText.Set(motor.Easing ? $"◉ easing −{easePct}%" : "◎ ease her");
            if (GUI.Button(easeRect, easeText.Content, easeStyle)) motor.Easing = !motor.Easing;
        }

        /// How much way she has, against her own top speed — and where that
        /// way is coming from.
        ///
        /// The whole point is the region PAST the full-speed tick. Surfing has
        /// been in the physics since the motor was rebuilt and reached exactly
        /// two things: a camera FOV punch and an audio pitch. Nothing told the
        /// player they had done anything, so the best-feeling thing in the game
        /// was invisible. Past the tick the bar goes bright and stays bright
        /// while the run holds; that band is the reward.
        ///
        /// The broach rides the same gauge on purpose. The face that gives her
        /// the overspeed is the face that takes her stern — reward and risk are
        /// the same piece of water, and putting them on one bar is the fastest
        /// way to teach that they are.
        void DrawWayGauge(Rect r, int u)
        {
            float max = Mathf.Max(0.01f, motor.MaxSpeed);
            // The gauge runs to the hull's own overspeed ceiling, so the
            // full-speed tick sits inboard of the end and there is somewhere
            // for a surf run to go. A gauge that ends at 100% cannot show
            // 118%. Read from the motor, never copied: see SurfOvershoot.
            float ceiling = Mathf.Max(1.05f, motor.SurfOvershoot);
            float way01 = Mathf.Clamp01(motor.CurrentSpeed / max / ceiling);
            float tick = 1f / ceiling;

            UITheme.Bar(r, 1f, UITheme.Track);
            var body = new Rect(r.x, r.y, r.width * Mathf.Min(way01, tick), r.height);
            UITheme.Bar(body, 1f, motor.Broach01 > 0.35f ? UITheme.Bad : UITheme.Sea);

            if (way01 > tick)
            {
                float x = r.x + r.width * tick;
                UITheme.Bar(new Rect(x, r.y, r.width * (way01 - tick), r.height),
                    1f, UITheme.Good);
            }
            // The full-speed mark, drawn last so nothing covers it.
            UITheme.Rect(new Rect(r.x + r.width * tick - 1f, r.y - 2f, 2f,
                r.height + 4f), UITheme.Text);
        }
    }
}
