using SeaSick.Ship;
using SeaSick.Voyage;
using SeaSick.World;
using UnityEngine;

namespace SeaSick.UI
{
    /// The compass: a bearing tape across the top of the screen carrying the
    /// cardinal points, where home is, and where the seas are running.
    ///
    /// The cardinals matter more than they look. The world is laid out by
    /// direction — cold to the north, heat to the south, dry ground east and
    /// open ocean west — so "which way am I pointing" is the same question as
    /// "what am I sailing into". There is no no-go zone and no laylines: every
    /// course is sailable, and the sea mark is advice about speed, not a wall.
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
            // Drawn back to front: the shading is a wash, then the marks, and
            // the cardinals last so nothing ever crosses a letter.
            float sev = motor.SeaSeverity01;
            if (sev > 0.15f)
            {
                float rel = Mathf.DeltaAngle(heading, seasFrom);
                // A whisper, not a slab: this is a hint about which way is
                // slower, and in calm water it says nothing at all.
                DrawSpan(tape, rel - 35f, rel + 35f,
                    new Color(0.95f, 0.62f, 0.22f, 0.35f + 0.45f * sev));
            }

            DrawPip(tape, Mathf.DeltaAngle(heading, homeBearing), UITheme.Sea, "home", true);

            // Where the swell front is bearing down from, if one is running.
            var waves = Ocean.WaveField.Instance;
            if (waves != null && waves.SwellActive)
            {
                Vector2 sd = waves.SwellDirection;
                float swellFrom = Mathf.Atan2(-sd.x, -sd.y) * Mathf.Rad2Deg;
                DrawPip(tape, Mathf.DeltaAngle(heading, swellFrom), UITheme.Bad, null, false);
            }

            // A mountain sea, if one is running. Drawn heavier than the swell
            // because it is the one that can end a voyage's worth of cargo.
            var mountains = FindFirstObjectByType<Ocean.MountainSeaDirector>();
            var threat = mountains != null ? mountains.Threat : null;
            if (threat != null)
            {
                float from = Mathf.Atan2(-threat.Travel.x, -threat.Travel.y) * Mathf.Rad2Deg;
                DrawPip(tape, Mathf.DeltaAngle(heading, from), UITheme.Bad, null, false);
            }

            // Cardinals and their halves. These are the map: north is cold,
            // south is hot, east is dry, west is the open ocean.
            for (int i = 0; i < 8; i++)
            {
                float bearing = i * 45f;
                float rel = Mathf.DeltaAngle(heading, bearing);
                if (Mathf.Abs(rel) > visibleSpan) continue;
                DrawTick(tape, rel, CardinalNames[i], (i % 2) == 0);
            }

            // Bow marker: a lubber line under the bar. It used to be a rule
            // through the full height of the tape, which meant it sat on top of
            // whichever cardinal you happened to be steering at.
            float lub = u * 0.55f;
            UITheme.Rect(new Rect(tape.center.x - 1.5f, tape.yMax, 3f, lub), UITheme.Text);
            UITheme.Rect(new Rect(tape.center.x - lub * 0.9f, tape.yMax + lub - 2f,
                lub * 1.8f, 2f), UITheme.Text);
        }

        static readonly string[] CardinalNames =
            { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        /// A compass graduation. Majors carry their letter, minors are a stub —
        /// enough to read rate of turn without crowding the tape.
        void DrawTick(Rect tape, float relativeBearing, string label, bool major)
        {
            float px = tape.center.x + (relativeBearing / visibleSpan) * (tape.width * 0.5f - 6f);
            // Graduations live in the bottom third only. They used to run half
            // the height and struck straight through the letters.
            float h = tape.height * (major ? 0.34f : 0.20f);
            var colour = major ? UITheme.Text : UITheme.TextDim;
            UITheme.Rect(new Rect(px - 1f, tape.yMax - h, 2f, h), colour);
            if (!major) return;

            int u = UITheme.Unit;
            var style = new GUIStyle(UITheme.Small2Centered) { normal = { textColor = colour } };
            GUI.Label(new Rect(px - u * 2f, tape.y + tape.height * 0.02f, u * 4f, u * 1.4f),
                label, style);
        }

        void DrawPip(Rect tape, float relativeBearing, Color colour, string label, bool labelAbove)
        {
            bool offScreen = Mathf.Abs(relativeBearing) > visibleSpan;
            float clamped = Mathf.Clamp(relativeBearing, -visibleSpan, visibleSpan);
            float px = tape.center.x + (clamped / visibleSpan) * (tape.width * 0.5f - 6f);

            // Lower half only: a full-height mark is guaranteed to strike a
            // cardinal sooner or later, and the letters are what the compass
            // is FOR. This keeps every mark findable and nothing overlapping.
            float markH = tape.height * 0.52f;
            UITheme.Rect(new Rect(px - 2f, tape.yMax - markH, 4f, markH), colour);
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
            // A band along the bottom edge, not a fill. Filling the tape put a
            // grey slab over the cardinals; this says "this arc is the slow one"
            // and leaves the instrument readable.
            float band = Mathf.Max(2f, tape.height * 0.16f);
            UITheme.Rect(new Rect(Mathf.Min(xa, xb), tape.yMax - band, Mathf.Abs(xb - xa), band),
                colour);
        }
    }
}
