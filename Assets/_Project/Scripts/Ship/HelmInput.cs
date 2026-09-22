using SeaSick.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;

namespace SeaSick.Ship
{
    /// The helm. WASD at a desk, two thumbs on a phone.
    ///
    /// W and S drive: hold for ahead or astern, release and the lever takes
    /// back over. A and D steer, and while either is held the wheel is drawn
    /// hard over to show it. The sail steps are gone -- she is a paddle
    /// steamer, and "furled / half / full" was a sailing rig's vocabulary
    /// bolted onto an engine that PaddleDrive was already reading as a
    /// throttle.
    ///
    /// **Touch is a wheel and a lever now** (`TouchHelm`), not a drag zone and
    /// two arrows. The old scheme put an ABSOLUTE tiller under any finger in
    /// the bottom 45% of the screen -- so the rudder snapped back to midships
    /// the moment you let go to do anything else -- and rang the engine up and
    /// down through a pair of fingernail-sized ▲/▼ buttons in the corner. Both
    /// are gone. The wheel HOLDS its angle and the lever LATCHES, which is
    /// what a helm does and what leaves a thumb free.
    ///
    /// This class still owns the POLICY: the wheel's angle is an ORDER, and
    /// the rudder is eased toward it at `engageSpeed` so it never snaps.
    [RequireComponent(typeof(ShipMotor))]
    public class HelmInput : MonoBehaviour
    {
        [SerializeField] float engageSpeed = 3.5f;   // rudder units/s toward the order
        [SerializeField, Range(-1f, 1f)] float testRudder = 0f; // editor/testing override

        ShipMotor motor;
        Breakers breakers;
        float rudder;

        /// The wheel and the lever. A plain object, not a component: it has no
        /// lifetime of its own and nothing else should be able to find it.
        readonly TouchHelm helm = new TouchHelm();

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
            // that view is engaged, so nothing moves -- but the wheel would
            // still take a drag meant for the ground (siting a building,
            // pressing on a crewman) and HOLD it, and she would sail off it
            // the moment the view closed. The same reason the arrows and WASD
            // are guarded: R would otherwise ring down rowing at the exact
            // moment it is also `CampSiting`'s rotate-the-ghost key.
            bool ashore = SeaSick.CameraRig.IslandCam.Engaged;
            helm.Sample(!ashore);

            float target = helm.Rudder;
            float? drive = null;
            float? shown = null;

            var kb = Keyboard.current;
            if (kb != null && !ashore)
            {
                // A held key OVERRIDES the wheel and hands it straight back on
                // release -- the wheel is still sitting wherever it was left,
                // so letting go of D returns the helm to the angle you set,
                // not to midships. `shown` is what the wheel is DRAWN at, so
                // the instrument never lies about which way she is going over.
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) { target = -1f; shown = -1f; }
                else if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) { target = 1f; shown = 1f; }

                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) drive = 1f;
                else if (kb.sKey.isPressed || kb.downArrowKey.isPressed) drive = -1f;
                // R, not space — space already dismisses the voyage tally.
                if (kb.rKey.wasPressedThisFrame) motor.Rowing = !motor.Rowing;
            }

            if (!Mathf.Approximately(testRudder, 0f)) { target = testRudder; shown = testRudder; }
            helm.DisplayOverride = shown;

            // The angle on the wheel is the ORDER; the rudder is eased toward
            // it so it never snaps, and it no longer creeps back to midships
            // on its own. There is nothing to recentre to: a wheel that is not
            // being touched is a wheel that has been LEFT somewhere.
            rudder = Mathf.MoveTowards(rudder, target, engageSpeed * Time.deltaTime);
            motor.Rudder = rudder;
            // The engine's ramp in ShipMotor does the smoothing, and the
            // crew's condition sets how fast it ramps -- an order is still
            // only as good as whoever is below to answer it.
            motor.ThrottleOrder = drive ?? helm.Throttle;
        }

        /// What she is being asked to do RIGHT NOW, which is the held key if
        /// there is one -- the lever must never read "stop" while a finger on
        /// W has her making way.
        ///
        /// The lever is continuous, so the words are bands rather than notches
        /// on an array. Every branch returns an interned literal, which is
        /// what lets `OrderText` cache on reference equality.
        string Label()
        {
            float o = motor.ThrottleOrder;
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

        /// Ring down STOP from outside, and put the wheel amidships.
        ///
        /// The lever is re-asserted into `motor.ThrottleOrder` every Update,
        /// so anything that wants her stopped has to move the CONTROL, not the
        /// value the control produces — writing the value lasts exactly one
        /// frame and then the helm quietly puts it back. Same for the rudder:
        /// the wheel holds its angle now, so zeroing `rudder` alone would ease
        /// straight back to whatever the wheel was left at.
        public void AllStop() { helm.Centre(); rudder = 0f; }

        void OnGUI()
        {
            // The helm is not on screen while she lies at a camp: the island
            // sheet docks to the bottom of a portrait phone and the wheel
            // would be drawn under it, on a ship that is anchored anyway.
            if (SeaSick.CameraRig.IslandCam.Engaged) return;

            int u = HudLayout.Unit;

            // The wheel and the lever, bottom centre where a thumb is. They
            // place themselves (`HudLayout.Slot.Wheel`) and claim their own
            // rects with `UIBlocker`, so a tap on the helm never reaches the
            // water underneath it.
            helm.Draw(motor.Throttle, motor.ThrottleMoving, OrderText());

            // --- Point of sail: the readout that teaches the whole system ---
            float panelW = u * 10f;
            // Shorter by the two rows the ▲/▼ buttons and the order caption
            // used: the order lives on the lever now, where the thumb that
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
            // the ACHIEVED throttle; the order is on the lever.
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
