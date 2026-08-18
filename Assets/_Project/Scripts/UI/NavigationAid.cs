using SeaSick.Ship;
using SeaSick.Voyage;
using SeaSick.World;
using UnityEngine;

namespace SeaSick.UI
{
    /// A bearing tape across the top of the screen: where you're pointed, where
    /// home is, where the wind comes from, and — crucially — the best course to
    /// actually make ground toward your target.
    ///
    /// Without this the game gives you a no-go zone but no way to know that the
    /// answer to "home is upwind" is "steer 50 degrees off the wind and zigzag".
    /// The physics always allowed it; nothing communicated it.
    public class NavigationAid : MonoBehaviour
    {
        [SerializeField] float visibleSpan = 90f;   // degrees either side of the bow

        ShipMotor motor;
        VoyageManager voyage;

        float bestUpwindAngle = 50f;
        float recomputeTimer;

        void Start()
        {
            motor = FindFirstObjectByType<ShipMotor>();
            voyage = FindFirstObjectByType<VoyageManager>();
            RecomputeBestAngle();
        }

        /// The angle off the wind that maximises velocity made good upwind —
        /// fast enough to matter, close enough to the wind to gain ground.
        void RecomputeBestAngle()
        {
            float best = -999f;
            for (float a = 20f; a <= 90f; a += 2f)
            {
                float vmg = ShipMotor.SailPolar(a) * Mathf.Cos(a * Mathf.Deg2Rad);
                if (vmg > best) { best = vmg; bestUpwindAngle = a; }
            }
        }

        void Update()
        {
            recomputeTimer -= Time.deltaTime;
            if (recomputeTimer <= 0f) { recomputeTimer = 5f; RecomputeBestAngle(); }
        }

        static float BearingTo(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        }

        void OnGUI()
        {
            if (motor == null || voyage == null || voyage.HomePoint == null) return;

            int u = UITheme.Unit;
            float w = Mathf.Min(Screen.width * 0.86f, u * 26f);
            float h = u * 2.3f;
            float x = (Screen.width - w) * 0.5f;
            float y = u * 2f;   // leaves room for the 'home' label above it
            var tape = new Rect(x, y, w, h);

            UITheme.Rect(tape, UITheme.Panel);

            float heading = motor.Heading;
            Vector2 wind = motor.WindDirection;
            float windFrom = Mathf.Atan2(-wind.x, -wind.y) * Mathf.Rad2Deg;
            float homeBearing = BearingTo(motor.transform.position, voyage.HomePoint.position);

            // Shade the no-go zone so the wall is visible, not just felt.
            float noGoCentre = Mathf.DeltaAngle(heading, windFrom);
            DrawSpan(tape, noGoCentre - ShipMotor.NoGoDegrees, noGoCentre + ShipMotor.NoGoDegrees,
                new Color(0.85f, 0.22f, 0.18f, 0.28f));

            // Is home inside the no-go zone? Then we must beat up to it.
            float homeOffWind = Mathf.Abs(Mathf.DeltaAngle(homeBearing, windFrom));
            bool homeUpwind = homeOffWind < ShipMotor.NoGoDegrees + 4f;

            float course;
            if (homeUpwind)
            {
                // Two laylines either side of the wind; take whichever is the
                // smaller turn from where the bow already points.
                float a = windFrom + bestUpwindAngle;
                float b = windFrom - bestUpwindAngle;
                course = Mathf.Abs(Mathf.DeltaAngle(heading, a)) <= Mathf.Abs(Mathf.DeltaAngle(heading, b)) ? a : b;
            }
            else
            {
                course = homeBearing;
            }

            // The red band already says where the wind is, so that pip goes
            // unlabelled; home and steer sit on opposite sides of the tape so
            // their labels can never collide when the marks converge.
            DrawPip(tape, Mathf.DeltaAngle(heading, windFrom), UITheme.Warn, null, false);
            DrawPip(tape, Mathf.DeltaAngle(heading, homeBearing), UITheme.Sea, "home", true);
            DrawPip(tape, Mathf.DeltaAngle(heading, course), UITheme.Good, "steer", false);

            // Bow marker.
            UITheme.Rect(new Rect(tape.center.x - 1f, tape.y, 2f, tape.height), UITheme.Text);

            if (homeUpwind)
            {
                float dist = Island.FlatDistance(motor.transform.position, voyage.HomePoint.position);
                GUI.Label(new Rect(tape.x, tape.yMax + u * 1.4f, tape.width, u * 1.5f),
                    $"home is upwind ({dist:F0} m) — follow the green mark, then swap sides",
                    UITheme.Small2Centered);
            }
        }

        void DrawPip(Rect tape, float relativeBearing, Color colour, string label, bool labelAbove)
        {
            bool offScreen = Mathf.Abs(relativeBearing) > visibleSpan;
            float clamped = Mathf.Clamp(relativeBearing, -visibleSpan, visibleSpan);
            float px = tape.center.x + (clamped / visibleSpan) * (tape.width * 0.5f - 6f);

            UITheme.Rect(new Rect(px - 2f, tape.y + 2f, 4f, tape.height - 2f), colour);
            if (string.IsNullOrEmpty(label)) return;

            int u = UITheme.Unit;
            var style = new GUIStyle(UITheme.Small2Centered) { normal = { textColor = colour } };
            // An arrow when the mark is behind you — otherwise it silently pins
            // to the edge and reads as though it's dead ahead.
            string text = offScreen ? (relativeBearing > 0 ? label + " ▸" : "◂ " + label) : label;
            float ly = labelAbove ? tape.y - u * 1.25f : tape.yMax + 1f;
            GUI.Label(new Rect(px - u * 2.2f, ly, u * 4.4f, u * 1.3f), text, style);
        }

        void DrawSpan(Rect tape, float fromDeg, float toDeg, Color colour)
        {
            float a = Mathf.Clamp(fromDeg, -visibleSpan, visibleSpan);
            float b = Mathf.Clamp(toDeg, -visibleSpan, visibleSpan);
            if (Mathf.Approximately(a, b)) return;
            float half = tape.width * 0.5f - 6f;
            float xa = tape.center.x + (a / visibleSpan) * half;
            float xb = tape.center.x + (b / visibleSpan) * half;
            UITheme.Rect(new Rect(Mathf.Min(xa, xb), tape.y + 2f, Mathf.Abs(xb - xa), tape.height - 2f), colour);
        }
    }
}
