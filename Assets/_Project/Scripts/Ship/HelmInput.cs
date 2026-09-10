using SeaSick.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SeaSick.Ship
{
    /// The helm. WASD at a desk, one thumb on a phone.
    ///
    /// W and S drive: hold for ahead or astern, release and the telegraph
    /// takes back over. A and D steer. The sail steps are gone -- she is a
    /// paddle steamer, windDriven has been off in the scene the whole time,
    /// and "furled / half / full" was a sailing rig's vocabulary bolted onto
    /// an engine that PaddleDrive was already reading as a throttle. The
    /// telegraph underneath is the same control for a thumb, and it now has
    /// an astern notch, which canvas could never have.
    ///
    /// Touch: hold anywhere in the lower steering zone; horizontal position
    /// maps to absolute tiller position, and releasing eases the rudder back
    /// to centre.
    [RequireComponent(typeof(ShipMotor))]
    public class HelmInput : MonoBehaviour
    {
        [SerializeField, Range(0.1f, 1f)] float steerZoneHeight = 0.45f;
        [SerializeField] float engageSpeed = 3.5f;   // rudder units/s while steering
        [SerializeField] float recenterSpeed = 1.2f; // rudder units/s on release
        [SerializeField, Range(-1f, 1f)] float testRudder = 0f; // editor/testing override

        // The engine telegraph, for thumbs. One astern notch, because
        // backing a paddle wheel is a manoeuvre, not a way to travel.
        static readonly float[] Orders = { -1f, 0f, 0.35f, 0.7f, 1f };
        static readonly string[] OrderNames = { "full astern", "stop", "slow ahead", "half ahead", "full ahead" };
        const int StopOrder = 1;

        ShipMotor motor;
        Breakers breakers;
        float rudder;
        int order = StopOrder;

        void Awake()
        {
            motor = GetComponent<ShipMotor>();
            breakers = GetComponent<Breakers>();
        }

        void Update()
        {
            float target = 0f;
            bool steering = false;
            float? drive = null;

            var pointer = Pointer.current;
            if (pointer != null && pointer.press.isPressed)
            {
                Vector2 p = pointer.position.ReadValue();
                if (p.y < Screen.height * steerZoneHeight && !UIBlocker.Blocked(p))
                {
                    // Slight overdrive (x2.2) so full rudder doesn't need the screen edge.
                    target = Mathf.Clamp((p.x / Screen.width - 0.5f) * 2.2f, -1f, 1f);
                    steering = true;
                }
            }

            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) { target = -1f; steering = true; }
                else if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) { target = 1f; steering = true; }

                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) drive = 1f;
                else if (kb.sKey.isPressed || kb.downArrowKey.isPressed) drive = -1f;
                // R, not space — space already dismisses the voyage tally.
                if (kb.rKey.wasPressedThisFrame) motor.Rowing = !motor.Rowing;
            }

            if (!Mathf.Approximately(testRudder, 0f)) { target = testRudder; steering = true; }

            rudder = Mathf.MoveTowards(
                rudder, steering ? target : 0f,
                (steering ? engageSpeed : recenterSpeed) * Time.deltaTime);
            motor.Rudder = rudder;
            // A held key OVERRIDES the telegraph and hands it straight back
            // on release, so the two never fight: the keys are for driving,
            // the telegraph is for setting her going and leaving her there.
            // The engine's ramp in ShipMotor does the smoothing, and the
            // crew's condition sets how fast it ramps -- an order is still
            // only as good as whoever is below to answer it.
            motor.ThrottleOrder = drive ?? Orders[order];
        }

        /// What she is being asked to do RIGHT NOW, which is the held key if
        /// there is one -- the panel must never read "stop" while a finger on
        /// W has her making way.
        string Label()
        {
            float o = motor.ThrottleOrder;
            if (Mathf.Approximately(o, Orders[order])) return OrderNames[order];
            if (o > 0.5f) return "full ahead";
            if (o > 0.05f) return "ahead";
            if (o < -0.5f) return "full astern";
            if (o < -0.05f) return "astern";
            return "stop";
        }

        void StepOrder(int delta) => order = Mathf.Clamp(order + delta, 0, Orders.Length - 1);

        /// Ring down STOP from outside, and centre the rudder.
        ///
        /// The telegraph is re-asserted into `motor.ThrottleOrder` every
        /// Update, so anything that wants her stopped has to move the ORDER,
        /// not the value the order produces — writing the value lasts exactly
        /// one frame and then the helm quietly puts it back.
        public void AllStop() { order = StopOrder; rudder = 0f; }

        void OnGUI()
        {
            int u = HudLayout.Unit;

            // --- Point of sail: the readout that teaches the whole system ---
            float panelW = u * 10f;
            // Taller by one bar than it was: the way gauge below the telegraph
            // is where surfing and broaching are read, and neither had anywhere
            // on screen to be.
            float panelH = u * 5.7f;
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

            // --- Sail control ---
            float bw = (panelW - u * 1.8f) * 0.5f;
            var less = new Rect(px + u * 0.6f, py + u * 2.7f, bw, u * 1.6f);
            var more = new Rect(less.xMax + u * 0.6f, less.y, bw, u * 1.6f);
            UIBlocker.Block(less);
            UIBlocker.Block(more);
            if (GUI.Button(less, "▼", UITheme.Button)) StepOrder(-1);
            if (GUI.Button(more, "▲", UITheme.Button)) StepOrder(1);
            // The order is what you asked for; the bar under it is what the
            // crew have actually managed. When they're sick the two disagree,
            // and that gap IS the mechanic — it has to be on screen.
            GUI.Label(new Rect(px, py + u * 2.9f, panelW, u * 1.2f),
                motor.ThrottleMoving ? Label() + " …" : Label(),
                UITheme.Small2Centered);
            // Astern fills the same bar backwards from a centre mark, so
            // which way she is being driven reads without being read.
            var barRect = new Rect(px + u * 0.6f, py + u * 4.25f, panelW - u * 1.2f, u * 0.22f);
            float t = Mathf.Clamp(motor.Throttle, -1f, 1f);
            float mid = barRect.x + barRect.width * 0.32f;
            if (t >= 0f)
                UITheme.Bar(new Rect(mid, barRect.y, (barRect.xMax - mid) * t, barRect.height),
                    1f, motor.ThrottleMoving ? UITheme.Warn : UITheme.Sea);
            else
                UITheme.Bar(new Rect(mid + (mid - barRect.x) * t, barRect.y,
                    (mid - barRect.x) * -t, barRect.height), 1f, UITheme.Warn);

            DrawWayGauge(new Rect(px + u * 0.6f, py + u * 4.9f,
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
            string easeLabel = motor.Easing
                ? $"◉ easing −{Mathf.RoundToInt(motor.EaseCost01 * 100f)}%"
                : "◎ ease her";
            if (GUI.Button(easeRect, easeLabel, easeStyle)) motor.Easing = !motor.Easing;
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
