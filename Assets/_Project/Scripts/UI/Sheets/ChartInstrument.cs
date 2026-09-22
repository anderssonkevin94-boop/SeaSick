using System.Collections.Generic;
using SeaSick.Ship;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **One instrument in the top-left corner, instead of three.**
    ///
    /// It replaces the square minimap, the compass tape across the top and
    /// the nav line at the bottom — which between them said where you are,
    /// which way you point and how far home is, in three different visual
    /// languages in three different corners. A chart says all three at once
    /// because that is what a chart is for, and it says them in the one place
    /// the eye already goes.
    ///
    /// ## Why it is drawn rather than laid out
    ///
    /// Everything on it is polar: ticks, arcs, island silhouettes, the range
    /// ring. Expressing that as a tree of positioned elements would be a
    /// hundred `VisualElement`s rebuilt whenever the ship moved. `Painter2D`
    /// in `generateVisualContent` is one mesh, rebuilt on demand, and the
    /// trigonometry stays trigonometry instead of becoming style writes.
    ///
    /// ## Two orientations, and the turn between them matters
    ///
    /// Under way the chart is SHIP-UP: the bow is at the top and the world
    /// turns under it, which is the only orientation in which "steer off the
    /// purple arc" is a thing you can do without arithmetic. At anchor it is
    /// NORTH-UP, because a chart you are planning on should not swing about
    /// when the moored hull yaws. Flipping instantly between the two reads as
    /// a glitch, so the rotation is eased over about six tenths of a second
    /// and the cardinals ride with it.
    ///
    /// ## It is never hidden
    ///
    /// Unlike the sheets, this is up at sea as well as at anchor. The sheets
    /// are about a decision you have walked up to; the chart is about where
    /// you are, and there is no moment in this game when that stops mattering.
    public class ChartInstrument
    {
        // ---- geometry, in panel units ----
        const float Diameter = 210f;
        const float R = Diameter * 0.5f;
        /// The paper rim's width: everything between `R` and `SeaR` is chart
        /// border — ticks, weather arcs, the course pointer, the cardinals.
        const float Rim = 18f;
        const float SeaR = R - Rim;
        const float TabTop = Diameter - 4f;

        // ---- the look, straight off the mockup ----
        static readonly Color SeaDisc = Rgb(0x8F, 0xC3, 0xD6);
        static readonly Color Storm = Rgb(0x6B, 0x5B, 0x9E);
        static readonly Color Easing = Rgb(0x9F, 0xB3, 0xC8);
        static readonly Color Hungry = Rgb(0xB7, 0x77, 0x2E);
        static readonly Color RaidedFlame = Rgb(0x8A, 0x2E, 0x1E);

        static Color Rgb(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f);

        // ---- the weather rule, taken from NavigationAid ----
        /// How far up a bearing to look. NavigationAid's own `lookahead`: the
        /// storm gradient is about x1.20 in Hs per 100 m, so 1.5 km is roughly
        /// one sea state — far enough to be worth turning for, near enough
        /// that you will be in it soon.
        const float Lookahead = 1500f;
        /// How much bigger the water has to be up a bearing before the rim
        /// says so. NavigationAid's `callRatio`. Below it, it is the same
        /// weather and an arc would be noise.
        const float CallRatio = 1.25f;
        const int WeatherBearings = 12;

        readonly VisualElement root;
        readonly VisualElement sea;
        readonly VisualElement marks;
        readonly VisualElement tab;
        readonly Label tabCourse;
        readonly Label tabWeather;

        // Orientation, eased. `shown` chases `target` so the flip between
        // ship-up and north-up is a turn rather than a jump.
        float shownRot, targetRot;

        // Range, eased the same way and for the same reason.
        float shownRange = 600f, targetRange = 600f;
        float wideUntil = -1f;

        bool lastUnderWay;
        float nextData;

        readonly float[] weatherRatio = new float[WeatherBearings];

        public ChartInstrument(VisualElement parent)
        {
            root = new VisualElement();
            root.AddToClassList("chart");
            root.style.width = Diameter;
            root.style.height = Diameter + 34f;
            root.generateVisualContent += Paint;

            // **The sea is a child element, because Painter2D cannot clip.**
            // Drawn straight onto the dial, a 200 m island at 250 m range
            // covered the ticks, the cardinals and a quarter of the screen
            // beyond the instrument, and a raider's patrol ring ran clean off
            // the corner as a dashed ellipse across the world. UI Toolkit will
            // clip a child to a rounded rect, and a rounded rect whose radius
            // is half its side is a circle — so the whole world layer goes in
            // one and is cut to the disc for free.
            sea = new VisualElement();
            sea.style.position = Position.Absolute;
            sea.style.left = Rim;
            sea.style.top = Rim;
            sea.style.width = SeaR * 2f;
            sea.style.height = SeaR * 2f;
            sea.style.borderTopLeftRadius = sea.style.borderTopRightRadius =
                sea.style.borderBottomLeftRadius = sea.style.borderBottomRightRadius = SeaR;
            sea.style.overflow = Overflow.Hidden;
            sea.style.backgroundColor = SeaDisc;
            sea.pickingMode = PickingMode.Ignore;
            sea.generateVisualContent += PaintSea;
            root.Add(sea);

            // **The ship goes in a child ADDED AFTER the sea, not on the
            // dial.** A UI Toolkit element paints its own content first and
            // its children over the top, so the arrow — drawn on the parent
            // at the dial's centre — was buried under the sea disc the moment
            // the sea became a child. It came up as a chart of an island with
            // nothing on it. The rim keeps working because it is outside the
            // sea's bounds, which is exactly why this was easy to miss.
            marks = new VisualElement();
            marks.style.position = Position.Absolute;
            marks.style.left = 0;
            marks.style.top = 0;
            marks.style.width = Diameter;
            marks.style.height = Diameter;
            marks.pickingMode = PickingMode.Ignore;
            marks.generateVisualContent += PaintMarks;
            root.Add(marks);
            // `ChartHook` is the chart sheet's own door; `Sheets.TryOpenChart`
            // is the registry this foundation offers. Either may be the one
            // that is wired on a given build, so try the sheet's door first
            // and fall back — the instrument must stay tappable whichever of
            // the two the chart happens to have registered through.
            root.RegisterCallback<ClickEvent>(_ =>
            {
                if (!ChartHook.TryOpen()) Sheets.TryOpenChart();
            });
            parent.Add(root);

            tab = new VisualElement();
            tab.AddToClassList("chart-tab");
            tab.pickingMode = PickingMode.Ignore;
            tab.style.top = TabTop;
            root.Add(tab);

            tabCourse = new Label("");
            tabCourse.AddToClassList("chart-tab-text");
            tab.Add(tabCourse);

            tabWeather = new Label("");
            tabWeather.AddToClassList("chart-tab-text");
            tabWeather.AddToClassList("chart-tab-weather");
            tab.Add(tabWeather);

            LayOutCardinals(root);
        }

        public void Tick(VisualElement panelRoot)
        {
            // Top-left, inside the safe area. The place label sits to its
            // right and is positioned off the same corner.
            float scale = panelRoot.resolvedStyle.width / Mathf.Max(1f, Screen.width);
            var safe = Screen.safeArea;
            root.style.left = safe.xMin * scale + 14f;
            root.style.top = (Screen.height - safe.yMax) * scale + 14f;

            bool under = ChartData.UnderWay;

            // **Cast off opens the view to the whole archipelago for a beat.**
            // Leaving a camp is the one moment the player is choosing where to
            // go next, so the chart answers that question before being asked
            // and then settles back to the sailing range.
            if (under && !lastUnderWay) wideUntil = Time.unscaledTime + 2f;
            lastUnderWay = under;

            targetRot = under ? -ChartData.ShipHeadingDeg : 0f;
            // Always turn the short way: easing from 350 to 10 degrees the
            // long way round spins the whole chart backwards through south.
            shownRot = Mathf.LerpAngle(shownRot, targetRot,
                1f - Mathf.Exp(-Time.unscaledDeltaTime / 0.18f));

            targetRange = RangeNow(under);
            shownRange = Mathf.Lerp(shownRange, targetRange,
                1f - Mathf.Exp(-Time.unscaledDeltaTime / 0.35f));

            if (Time.unscaledTime >= nextData)
            {
                nextData = Time.unscaledTime + 0.33f;
                ScanWeather();
                UpdateTab(under);
            }

            PlaceCardinals();
            root.MarkDirtyRepaint();
            sea.MarkDirtyRepaint();
            marks.MarkDirtyRepaint();
        }

        /// How much sea is in the disc.
        float RangeNow(bool under)
        {
            if (!under) return 250f;
            if (Time.unscaledTime < wideUntil)
            {
                // Fit every island she has actually landed on, so the beat
                // after cast off shows the archipelago she knows.
                float far = 600f;
                var isles = ChartData.Islands();
                var me = ChartData.ShipPos;
                if (isles != null)
                    for (int i = 0; i < isles.Count; i++)
                        if (isles[i].seen == Seen.Landed)
                            far = Mathf.Max(far, (isles[i].centre - me).magnitude
                                                 + isles[i].meanRadius);
                return Mathf.Min(far * 1.12f, 12000f);
            }
            // Close in when she is nearly on top of something: at 300 m the
            // question stops being "where is the island" and becomes "where
            // on it am I going".
            var near = NearestLandedDistance();
            return near < 300f ? 250f : 600f;
        }

        float NearestLandedDistance()
        {
            var isles = ChartData.Islands();
            if (isles == null) return float.MaxValue;
            var me = ChartData.ShipPos;
            float best = float.MaxValue;
            for (int i = 0; i < isles.Count; i++)
            {
                if (isles[i].seen == Seen.Never) continue;
                float d = (isles[i].centre - me).magnitude - isles[i].meanRadius;
                if (d < best) best = d;
            }
            return best;
        }

        /// Where the water gets worse, on twelve bearings all round.
        ///
        /// The compass tape asked this question across the ninety degrees
        /// either side of the bow; a dial has a whole rim, so it asks all
        /// round — the arc astern is how you find out the way you came is
        /// closing behind you. Same lookahead and same ratio as the tape, so
        /// the two never disagreed about what "heavy" meant while both existed.
        void ScanWeather()
        {
            for (int i = 0; i < WeatherBearings; i++) weatherRatio[i] = 1f;
            var ctrl = SeaSick.Ocean.SeaStateController.Instance;
            if (ctrl == null) return;
            var p = ChartData.ShipPos;
            float here = Mathf.Max(0.2f, ctrl.SeaHsAt(p));
            for (int i = 0; i < WeatherBearings; i++)
            {
                float bearing = i * (360f / WeatherBearings) * Mathf.Deg2Rad;
                var q = new Vector2(p.x + Mathf.Sin(bearing) * Lookahead,
                                    p.y + Mathf.Cos(bearing) * Lookahead);
                weatherRatio[i] = ctrl.SeaHsAt(q) / here;
            }
        }

        void UpdateTab(bool under)
        {
            // Nothing at anchor: the sheets are the interface there, and a
            // course line under the dial would be a second one.
            if (!under) { tab.style.display = DisplayStyle.None; return; }

            string course = null;
            if (ChartData.TryCourse(out _, out string label, out float dist))
                course = label + " · " + Mathf.RoundToInt(dist) + " m";

            string weather = null;
            int worst = -1;
            float worstR = CallRatio;
            for (int i = 0; i < WeatherBearings; i++)
                if (weatherRatio[i] > worstR) { worstR = weatherRatio[i]; worst = i; }
            if (worst >= 0) weather = "heavy to the " + Compass(worst * (360f / WeatherBearings));

            tab.style.display = course == null && weather == null
                ? DisplayStyle.None : DisplayStyle.Flex;
            tabCourse.text = course ?? "";
            tabCourse.style.display = course == null ? DisplayStyle.None : DisplayStyle.Flex;
            tabWeather.text = weather ?? "";
            tabWeather.style.display = weather == null ? DisplayStyle.None : DisplayStyle.Flex;
        }

        static readonly string[] Points =
        {
            "north", "north-east", "east", "south-east",
            "south", "south-west", "west", "north-west",
        };

        static string Compass(float deg)
        {
            int i = Mathf.RoundToInt(Mathf.Repeat(deg, 360f) / 45f) % 8;
            return Points[i];
        }

        // ------------------------------------------------------------------
        // Painting
        // ------------------------------------------------------------------

        /// Bearing in degrees to a point on the dial at radius `r`.
        /// Screen y grows downward, so north (0) is -90 in painter angles.
        Vector2 At(float bearingDeg, float r)
        {
            float a = (bearingDeg + shownRot - 90f) * Mathf.Deg2Rad;
            return new Vector2(R + Mathf.Cos(a) * r, R + Mathf.Sin(a) * r);
        }

        /// A world point to a point on the dial, or off it.
        Vector2 World(Vector2 world)
        {
            var d = world - ChartData.ShipPos;
            float m = SeaR / Mathf.Max(1f, shownRange);
            float a = shownRot * Mathf.Deg2Rad;
            float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
            // World XZ: +x east, +y (z) north. North goes UP, so z maps to -y.
            float ex = d.x * m, ny = d.y * m;
            return new Vector2(SeaR + ex * ca - (-ny) * sa, SeaR + ex * sa + (-ny) * ca);
        }

        void Paint(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;

            // --- the paper disc and its rim ---
            p.fillColor = SheetTheme.Paper;
            p.strokeColor = SheetTheme.Ink;
            p.lineWidth = 2.5f;
            p.BeginPath();
            p.Arc(new Vector2(R, R), R - 1.25f, new Angle(0f, AngleUnit.Degree), new Angle(360f, AngleUnit.Degree));
            p.Fill();
            p.Stroke();

            // --- ten-degree graduations ---
            p.lineWidth = 1f;
            p.strokeColor = SheetTheme.Ink;
            p.BeginPath();
            for (int i = 0; i < 360; i += 10)
            {
                float len = (i % 90 == 0) ? 6f : 3f;
                p.MoveTo(At(i, R - 2f));
                p.LineTo(At(i, R - 2f - len));
            }
            p.Stroke();

            // The sea itself, its contents and its clip are the `sea` child;
            // this is only its edge, drawn over the top of it.
            p.strokeColor = SheetTheme.Ink;
            p.lineWidth = 1.5f;
            p.BeginPath();
            p.Arc(new Vector2(R, R), SeaR, new Angle(0f, AngleUnit.Degree), new Angle(360f, AngleUnit.Degree));
            p.Stroke();

        }

        /// The marks that sit OVER the sea: the rim's weather and course, and
        /// the ship at the centre.
        void PaintMarks(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            PaintWeather(p);
            PaintCourse(p);
            PaintShip(p);
        }

        /// Everything inside the sea disc, in the clipped child's own frame:
        /// its centre is (SeaR, SeaR), not (R, R).
        void PaintSea(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            PaintIslands(p);
            PaintRangeRing(p);
            PaintRaiders(p);
            PaintCamps(p);
        }

        // --- islands ---

        void PaintIslands(Painter2D p)
        {
            var isles = ChartData.Islands();
            if (isles == null) return;
            for (int i = 0; i < isles.Count; i++)
            {
                var isle = isles[i];
                if (isle.seen == Seen.Never) continue;
                var outline = ChartData.OutlineOf(isle.island);
                if (outline == null) continue;

                // 48 samples: enough that a bay reads as a bay at 210 px, few
                // enough that the whole archipelago is a few hundred points.
                const int N = 48;
                var pts = new Vector2[N];
                bool any = false;
                for (int k = 0; k < N; k++)
                {
                    float bearing = k * (360f / N);
                    float rad = outline(bearing);
                    var w = new Vector2(
                        isle.centre.x + Mathf.Sin(bearing * Mathf.Deg2Rad) * rad,
                        isle.centre.y + Mathf.Cos(bearing * Mathf.Deg2Rad) * rad);
                    pts[k] = World(w);
                    if ((pts[k] - new Vector2(SeaR, SeaR)).sqrMagnitude < (SeaR + 40f) * (SeaR + 40f))
                        any = true;
                }
                if (!any) continue;

                bool landed = isle.seen == Seen.Landed;
                var tint = isle.tint;
                if (tint.a <= 0f) tint = Rgb(0x9D, 0xC4, 0x6A);

                p.BeginPath();
                p.MoveTo(pts[0]);
                for (int k = 1; k < N; k++) p.LineTo(pts[k]);
                p.ClosePath();

                if (landed)
                {
                    p.fillColor = tint;
                    p.Fill();
                    p.strokeColor = SheetTheme.Ink;
                    p.lineWidth = 1.2f;
                    p.Stroke();
                }
                else
                {
                    // Glimpsed: a pale wash and a broken outline. Painter2D has
                    // no dash and no pattern fill, so "hatched" is expressed as
                    // a light fill plus a dashed edge drawn as segments — the
                    // same reading (this one is not filled in yet) with the
                    // tools the runtime actually has.
                    var wash = tint; wash.a = 0.30f;
                    p.fillColor = wash;
                    p.Fill();
                    p.strokeColor = SheetTheme.Ink;
                    p.lineWidth = 1.2f;
                    p.BeginPath();
                    for (int k = 0; k < N; k += 2)
                    {
                        p.MoveTo(pts[k]);
                        p.LineTo(pts[(k + 1) % N]);
                    }
                    p.Stroke();
                }
            }
        }

        // --- the 250 m ring ---

        void PaintRangeRing(Painter2D p)
        {
            float r = SeaR * (250f / Mathf.Max(1f, shownRange));
            if (r < 8f || r > SeaR - 3f) return;
            p.strokeColor = new Color(SheetTheme.Ink.r, SheetTheme.Ink.g, SheetTheme.Ink.b, 0.6f);
            p.lineWidth = 0.8f;
            Dashed(p, new Vector2(SeaR, SeaR), r, 2.5f, 4f);
        }

        /// A dashed circle, as short arcs. Painter2D has no dash array, and a
        /// dashed ring is the difference between "this is a measured distance"
        /// and "this is an edge of something".
        static void Dashed(Painter2D p, Vector2 c, float r, float on, float off)
        {
            float step = (on + off) / Mathf.Max(1f, r) * Mathf.Rad2Deg;
            float onDeg = on / Mathf.Max(1f, r) * Mathf.Rad2Deg;
            p.BeginPath();
            for (float a = 0f; a < 360f; a += step)
            {
                p.MoveTo(new Vector2(c.x + Mathf.Cos(a * Mathf.Deg2Rad) * r,
                                     c.y + Mathf.Sin(a * Mathf.Deg2Rad) * r));
                float b = a + onDeg;
                p.LineTo(new Vector2(c.x + Mathf.Cos(b * Mathf.Deg2Rad) * r,
                                     c.y + Mathf.Sin(b * Mathf.Deg2Rad) * r));
            }
            p.Stroke();
        }

        static void DashedLine(Painter2D p, Vector2 a, Vector2 b, float on, float off)
        {
            float len = (b - a).magnitude;
            if (len < 0.01f) return;
            var dir = (b - a) / len;
            p.BeginPath();
            for (float t = 0f; t < len; t += on + off)
            {
                p.MoveTo(a + dir * t);
                p.LineTo(a + dir * Mathf.Min(t + on, len));
            }
            p.Stroke();
        }

        // --- raiders ---

        void PaintRaiders(Painter2D p)
        {
            var raiders = ChartData.Raiders();
            if (raiders == null) return;
            for (int i = 0; i < raiders.Count; i++)
            {
                var r = raiders[i];
                var at = World(r.pos);
                if ((at - new Vector2(SeaR, SeaR)).magnitude > SeaR + 30f) continue;

                // **The ring is around what they are patrolling, not around
                // them.** A ring centred on a moving raider says "this is how
                // close he is"; a ring on his home island says "this is the
                // water he owns", which is the thing you are deciding whether
                // to sail into.
                float m = SeaR / Mathf.Max(1f, shownRange);
                p.strokeColor = SheetTheme.Ember;
                p.lineWidth = 1.5f;
                if (r.patrolRadius > 1f)
                    Dashed(p, World(r.patrolCentre), r.patrolRadius * m, 3f, 3f);

                // The line to what they are making for. This is the whole
                // point of putting raiders on the chart: a patrol you can sail
                // round is scenery, a patrol heading for your camp is a clock.
                if (r.hasTarget)
                {
                    p.lineWidth = 1.5f;
                    DashedLine(p, at, World(r.target), 2f, 3f);
                }

                // A small arrow in their heading.
                p.fillColor = SheetTheme.Ember;
                float a = (r.headingDeg + shownRot - 90f) * Mathf.Deg2Rad;
                var f = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                var s = new Vector2(-f.y, f.x);
                p.BeginPath();
                p.MoveTo(at + f * 5f);
                p.LineTo(at - f * 3f + s * 3f);
                p.LineTo(at - f * 1f);
                p.LineTo(at - f * 3f - s * 3f);
                p.ClosePath();
                p.Fill();
            }
        }

        // --- camps ---

        void PaintCamps(Painter2D p)
        {
            var isles = ChartData.Islands();
            if (isles == null) return;
            for (int i = 0; i < isles.Count; i++)
            {
                var isle = isles[i];
                if (isle.flame == FlameState.None) continue;
                var at = World(isle.centre);
                if ((at - new Vector2(SeaR, SeaR)).magnitude > SeaR - 2f) continue;

                Color col = isle.flame == FlameState.Fed ? SheetTheme.Ember
                          : isle.flame == FlameState.Hungry ? Hungry : RaidedFlame;

                // A flame: a teardrop with a curl, at chart scale. Small
                // enough to be a mark, shaped enough to be a fire.
                p.fillColor = col;
                p.strokeColor = SheetTheme.Ink;
                p.lineWidth = 1.2f;
                p.BeginPath();
                p.MoveTo(new Vector2(at.x, at.y - 6f));
                p.BezierCurveTo(new Vector2(at.x + 5f, at.y - 2f),
                                new Vector2(at.x + 4f, at.y + 4f),
                                new Vector2(at.x, at.y + 5f));
                p.BezierCurveTo(new Vector2(at.x - 4f, at.y + 4f),
                                new Vector2(at.x - 5f, at.y - 2f),
                                new Vector2(at.x, at.y - 6f));
                p.ClosePath();
                p.Fill();
                p.Stroke();

                if (isle.flame == FlameState.Raided)
                {
                    // Crossed out. A raided camp has to read as raided from
                    // across the screen, not from its colour alone.
                    p.strokeColor = SheetTheme.Ember;
                    p.lineWidth = 2f;
                    p.BeginPath();
                    p.MoveTo(new Vector2(at.x - 7f, at.y - 7f));
                    p.LineTo(new Vector2(at.x + 7f, at.y + 6f));
                    p.MoveTo(new Vector2(at.x + 7f, at.y - 7f));
                    p.LineTo(new Vector2(at.x - 7f, at.y + 6f));
                    p.Stroke();
                }
            }
        }

        // --- weather on the rim ---

        void PaintWeather(Painter2D p)
        {
            // Only under way. At anchor the lookahead reaches 1.5 km out past
            // the shelter the island is giving her, so nearly every bearing
            // reads "heavier than here" and the rim came up a solid purple
            // ring — true, and useless, and it buried the ticks. The arc is
            // advice about a course, so it belongs to the state that has one.
            if (!ChartData.UnderWay) return;
            float step = 360f / WeatherBearings;
            for (int i = 0; i < WeatherBearings; i++)
            {
                bool heavy = weatherRatio[i] > CallRatio;
                bool easing = weatherRatio[i] < 1f / CallRatio;
                if (!heavy && !easing) continue;
                p.strokeColor = heavy ? Storm : Easing;
                p.lineWidth = heavy ? 6f : 4f;
                float a0 = i * step - step * 0.45f;
                float a1 = i * step + step * 0.45f;
                p.BeginPath();
                p.MoveTo(At(a0, R - 5f));
                // Arc drawn as a short polyline: `Painter2D.Arc` starts a new
                // sub-path from the circle's own centre, which is the wrong
                // shape for a band on the rim.
                for (float a = a0; a <= a1; a += 2f) p.LineTo(At(a, R - 5f));
                p.LineTo(At(a1, R - 5f));
                p.Stroke();
            }
        }

        // --- the course pointer ---

        void PaintCourse(Painter2D p)
        {
            if (!ChartData.TryCourse(out Vector2 target, out _, out _)) return;
            var d = target - ChartData.ShipPos;
            if (d.sqrMagnitude < 1f) return;
            float bearing = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;

            var tip = At(bearing, R - 4f);
            var back = At(bearing, R - 12f);
            var dir = (tip - back).normalized;
            var side = new Vector2(-dir.y, dir.x);

            p.fillColor = SheetTheme.Brass;
            p.strokeColor = SheetTheme.Ink;
            p.lineWidth = 1f;
            p.BeginPath();
            p.MoveTo(tip);
            p.LineTo(back + side * 4.5f);
            p.LineTo(back - side * 4.5f);
            p.ClosePath();
            p.Fill();
            p.Stroke();
        }

        // --- the ship ---

        void PaintShip(Painter2D p)
        {
            // Dead centre, and under way it points straight up because the
            // whole chart has turned under it. At anchor it carries her real
            // heading against a north-up world.
            float a = (ChartData.ShipHeadingDeg + shownRot - 90f) * Mathf.Deg2Rad;
            var f = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            var s = new Vector2(-f.y, f.x);
            var c = new Vector2(R, R);

            p.fillColor = SheetTheme.Ink;
            p.BeginPath();
            p.MoveTo(c + f * 9f);
            p.LineTo(c - f * 7f + s * 6f);
            p.LineTo(c - f * 3f);
            p.LineTo(c - f * 7f - s * 6f);
            p.ClosePath();
            p.Fill();
        }

        /// The cardinal letters. Text cannot be drawn by `Painter2D`, so these
        /// are four real labels riding the rotation — which is also what lets
        /// them use the storybook serif the rest of the HUD is set in.
        public void LayOutCardinals(VisualElement parent)
        {
            for (int i = 0; i < 4; i++)
            {
                var l = new Label(new[] { "N", "E", "S", "W" }[i]);
                l.AddToClassList("chart-cardinal");
                if (i == 0) l.AddToClassList("chart-cardinal--n");
                l.pickingMode = PickingMode.Ignore;
                cardinals[i] = l;
                parent.Add(l);
            }
        }

        readonly Label[] cardinals = new Label[4];

        /// Re-place the four letters each frame. They are absolutely
        /// positioned inside the dial, so this is four style writes rather
        /// than a layout pass.
        public void PlaceCardinals()
        {
            for (int i = 0; i < 4; i++)
            {
                var l = cardinals[i];
                if (l == null) continue;
                var at = At(i * 90f, R - 12f);
                l.style.left = at.x - 9f;
                l.style.top = at.y - 9f;
            }
        }

        public VisualElement Element => root;
    }
}
