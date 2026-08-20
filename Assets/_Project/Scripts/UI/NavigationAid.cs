using SeaSick.Ship;
using SeaSick.Voyage;
using SeaSick.World;
using UnityEngine;

namespace SeaSick.UI
{
    /// A bearing tape across the top of the screen: where you're pointed, where
    /// home is, and where the seas are running.
    ///
    /// There is no no-go zone any more and no laylines to compute — every
    /// course is sailable, so the tape's job shrank to orientation. The sea
    /// mark is advisory, not a wall: crossing it costs speed in heavy water and
    /// nothing at all in calm.
    public class NavigationAid : MonoBehaviour
    {
        [SerializeField] float visibleSpan = 90f;   // degrees either side of the bow

        ShipMotor motor;
        VoyageManager voyage;

        void Start()
        {
            motor = FindFirstObjectByType<ShipMotor>();
            voyage = FindFirstObjectByType<VoyageManager>();
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
            float seasFrom = heading + Mathf.DeltaAngle(0f, motor.SeaAngleDeg);
            float homeBearing = BearingTo(motor.transform.position, voyage.HomePoint.position);

            // Shade where the seas are coming from, weighted by how heavy they
            // are — a hint about the slow direction, not a forbidden zone. In
            // calm water it fades out entirely, because then it costs nothing.
            float sev = motor.SeaSeverity01;
            if (sev > 0.15f)
            {
                float rel = Mathf.DeltaAngle(heading, seasFrom);
                DrawSpan(tape, rel - 35f, rel + 35f,
                    new Color(0.85f, 0.55f, 0.18f, 0.10f + 0.20f * sev));
            }

            DrawPip(tape, Mathf.DeltaAngle(heading, homeBearing), UITheme.Sea, "home", true);

            // Bow marker.
            UITheme.Rect(new Rect(tape.center.x - 1f, tape.y, 2f, tape.height), UITheme.Text);
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
