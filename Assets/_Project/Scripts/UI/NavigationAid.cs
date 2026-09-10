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
            // Draw-only panel: skip the non-Repaint events. See StatusHUD for
            // the measurement — IMGUI runs OnGUI once per event, and the
            // discarded passes were the game's biggest source of GC garbage.
            if (motor == null || voyage == null || voyage.HomePoint == null) return;
            if (!HudVisibility.Compass) return;

            int u = HudLayout.Unit;
            // Centred at the top, narrowed to clear the crew pips and the
            // minimap rather than running under them. It used to take 0.86 of
            // the screen unconditionally, which is fine on a phone and lands
            // on both columns in the editor's landscape view -- where every
            // framing in this project gets judged at least once.
            // Declared above the repaint guard so the tape is on the record
            // every frame -- otherwise `HudOverlapProbe` cannot see it, and a
            // panel the check cannot see is a panel the check cannot clear.
            var tape = HudLayout.TopCentre(u * 26f, u * 2.3f);

            if (Event.current.type != EventType.Repaint) return;

            UITheme.Rect(tape, UITheme.Panel);

            float heading = motor.Heading;
            float homeBearing = BearingTo(motor.transform.position, voyage.HomePoint.position);

            // Shade where the seas are coming from, weighted by how heavy they
            // are — a hint about the slow direction, not a forbidden zone. In
            // calm water it fades out entirely, because then it costs nothing.
            // Drawn back to front: the shading is a wash, then the marks, and
            // the cardinals last so nothing ever crosses a letter.
            //
            // **Two trains, two washes.** The wind sea and the swell run on
            // their own clocks and they cross; where they cross there is no
            // clean heading, and the ONLY way for a player to see that coming
            // is for both to be on the instrument. The wind sea's wash is wide
            // and warm (it is short, steep and hits over a broad arc); the
            // swell's is narrow and cool (it is long and comes from one place).
            // Where the two overlap on the tape is the water with no answer.
            float sev = motor.SeaSeverity01;
            if (sev > 0.15f)
            {
                float rel = Mathf.DeltaAngle(heading, motor.SeasFromDeg);
                DrawSpan(tape, rel - 35f, rel + 35f,
                    new Color(0.95f, 0.62f, 0.22f, 0.30f + 0.40f * sev));

                float swellRel = Mathf.DeltaAngle(heading, motor.SwellFromDeg);
                DrawSpan(tape, swellRel - 18f, swellRel + 18f,
                    new Color(0.55f, 0.72f, 0.95f, 0.35f + 0.45f * sev));
            }

            DrawWeatherAhead(tape, heading);
            DrawPip(tape, Mathf.DeltaAngle(heading, homeBearing), UITheme.Sea, "home", true);

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

        [Header("Weather ahead")]
        [Tooltip("How far up the course to look, metres. The storm gradient measures about x1.20 in Hs per 100 m sailed, so a kilometre and a half is roughly the difference between one sea state and the next -- far enough to be worth turning for, near enough that you will be in it soon.")]
        [SerializeField] float lookahead = 1500f;
        [Tooltip("How much bigger or smaller the water has to be up that bearing before the tape says so, as a ratio. Below this it is the same weather and a mark would be noise.")]
        [SerializeField] float callRatio = 1.25f;
        [Tooltip("Scans per second. The weather moves in kilometres and minutes; asking it sixty times a second is sixty envelope evaluations a frame for an answer that cannot have changed.")]
        [SerializeField] float scanHz = 3f;

        // Cached scan. Sampled on a slow timer rather than per repaint,
        // because SeaHsAt walks the whole region envelope.
        float worstRel, bestRel, worstRatio = 1f, bestRatio = 1f;
        float nextScan;

        /// Where the water gets worse, and where it gets better.
        ///
        /// **This is what turns weather from an event into a decision.** The
        /// field already knows the sea at any point — SkyDirector asks it on
        /// eight bearings every frame to tint the sky — but nothing asked it
        /// on the player's behalf, so a storm was something that arrived. A
        /// mark on the compass makes the same storm a choice between the short
        /// way through and the long way round, which is the only thing that
        /// makes a weather system gameplay rather than scenery.
        void DrawWeatherAhead(Rect tape, float heading)
        {
            var ctrl = SeaSick.Ocean.SeaStateController.Instance;
            if (ctrl == null) return;

            if (Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + 1f / Mathf.Max(0.5f, scanHz);
                Vector3 p = motor.transform.position;
                float here = Mathf.Max(0.2f, ctrl.SeaHsAt(new Vector2(p.x, p.z)));
                worstRatio = 1f; bestRatio = 1f; worstRel = 0f; bestRel = 0f;
                // Every 15 degrees across the visible tape. Astern is left out
                // on purpose: it is where she has just been, and a mark there
                // is advice about the past.
                for (float rel = -visibleSpan; rel <= visibleSpan + 0.1f; rel += 15f)
                {
                    float bearing = (heading + rel) * Mathf.Deg2Rad;
                    Vector2 q = new Vector2(
                        p.x + Mathf.Sin(bearing) * lookahead,
                        p.z + Mathf.Cos(bearing) * lookahead);
                    float ratio = ctrl.SeaHsAt(q) / here;
                    if (ratio > worstRatio) { worstRatio = ratio; worstRel = rel; }
                    if (ratio < bestRatio) { bestRatio = ratio; bestRel = rel; }
                }
            }

            if (worstRatio > callRatio)
                DrawPip(tape, worstRel, UITheme.Bad, "heavy", false);
            // Only offer the clear water when there is something to get away
            // from. In settled weather "easing" is a mark that means nothing
            // and trains the player to ignore the row it lives in.
            if (bestRatio < 1f / callRatio && worstRatio > callRatio)
                DrawPip(tape, bestRel, UITheme.Good, "easing", false);
        }

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
            // GUI.contentColor tints the text without building a style. A
            // per-frame `new GUIStyle` allocates AND invalidates IMGUI's
            // cached text mesh for everything drawn with it.
            var prev = GUI.contentColor;
            GUI.contentColor = colour;
            GUI.Label(new Rect(px - u * 2f, tape.y + tape.height * 0.02f, u * 4f, u * 1.4f),
                label, UITheme.Small2Centered);
            GUI.contentColor = prev;
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
            var prev = GUI.contentColor;
            GUI.contentColor = colour;
            // An arrow when the mark is behind you — otherwise it silently pins
            // to the edge and reads as though it's dead ahead.
            string text = offScreen ? (relativeBearing > 0 ? label + " ▸" : "◂ " + label) : label;
            float ly = labelAbove ? tape.y - u * 1.25f : tape.yMax + 1f;
            GUI.Label(new Rect(px - u * 2.2f, ly, u * 4.4f, u * 1.3f), text, UITheme.Small2Centered);
            GUI.contentColor = prev;
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
