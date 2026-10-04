using SeaSick.Combat;
using SeaSick.World;
using UnityEngine;

namespace SeaSick.UI.Sheets
{
    /// <summary>
    /// **Edge markers for the sea view** (Kevin, 2026-10-02: "when I turn I'm
    /// often turning blind, not seeing my target"). Portrait shows ~35 degrees
    /// of horizontal view, so the things you are sailing toward spend most of
    /// their time off screen. One chevron on the screen edge per off-screen
    /// target, pointing the way to turn, with its distance underneath:
    /// gold = the course (the set camp or the way home), ember = a hostile
    /// ship inside 400 m (the locked one bigger and brighter), ice = the
    /// nearest island you have glimpsed but not yet landed on. The kraken
    /// (2026-10-03) is ember too, bigger, bold and pulsing while its warning
    /// runs: it points at the darkening water (`KrakenDirector.WarningPoint`)
    /// and then at the beast while it is up and not sinking away, first in
    /// the list so nothing pushes it out.
    ///
    /// IMGUI, the same family as `RescueHud` / `SquallHud` (self-installing,
    /// Repaint-only drawing, clamped to a band that avoids the helm). Where
    /// those two clamp to the top half, this one clamps to everything above
    /// the bottom ~35% of the safe area (and above the combat/helm rows when
    /// those say they are higher), below the top bar and the chip, and steps
    /// around the chart dial. Markers are look-only: no tap zone, nothing
    /// registered with `UIBlocker`, so a thumb on one still starts the stick.
    ///
    /// Visible exactly when the helm row is: `SeaHud.HelmShowing` already
    /// means sea view, not lying at anything, no sheet, no island camera;
    /// `ChartData.UnderWay` adds "not docked". Targets are rescanned 4 times
    /// a second (distance strings cached); only the projection runs per frame.
    /// </summary>
    public class SeaEdgeMarkers : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (FindAnyObjectByType<SeaEdgeMarkers>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("SeaEdgeMarkers");
            go.AddComponent<SeaEdgeMarkers>();
            DontDestroyOnLoad(go);
        }

        /// Hostile ships further than this (flat metres) get no marker.
        const float HostileRange = 400f;
        /// An unlanded island further than this to its shore gets none.
        const float IslandRange = 700f;
        /// Closer than this the thing is on top of you: no marker.
        const float ArrivedMetres = 30f;
        const int MaxHostiles = 4;
        const int MaxMarkers = MaxHostiles + 3;   // + course, island, kraken
        const float RefreshSeconds = 0.25f;
        /// Share of the safe height kept clear at the bottom for the stick.
        const float BottomClearShare = 0.35f;

        enum Kind { Hostile, Course, Island, Kraken }

        struct Marker
        {
            public Kind kind;
            public EnemyShip ship;      // hostiles read their live position each frame
            public Vector3 pos;         // course / island
            public string label;
            public bool locked;
        }

        static readonly Color Gold = new Color32(244, 198, 82, 255);
        static readonly Color Ember = new Color32(240, 78, 56, 255);
        static readonly Color EmberLocked = new Color32(255, 120, 84, 255);
        static readonly Color Ice = new Color32(164, 210, 232, 255);
        static readonly Color Shadow = new Color(0.04f, 0.07f, 0.10f, 0.9f);

        readonly Marker[] markers = new Marker[MaxMarkers];
        int count;
        float nextRefresh;

        // Hostile pick scratch, nearest first (no per-refresh allocation).
        readonly EnemyShip[] hShip = new EnemyShip[MaxHostiles];
        readonly float[] hDist = new float[MaxHostiles];

        // Rects already claimed this frame, so two markers never stack.
        readonly Rect[] placed = new Rect[MaxMarkers];

        Texture2D arrow;
        GUIStyle labelStyle, labelStyleBold;
        int styleUnit = -1;

        void Update()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + RefreshSeconds;
            Refresh();
        }

        // ------------------------------------------------------- scan (4 Hz)

        void Refresh()
        {
            count = 0;
            var motor = SheetBits.Motor;
            if (motor == null || !SeaHud.HelmShowing || !ChartData.UnderWay) return;
            Vector3 me = motor.transform.position;

            // The kraken first, so no other marker can push it out: the
            // water darkening off the bow, then the beast itself. No range:
            // it is only ever ~55 m off, and a warning is worth showing.
            if (TryKraken(out Vector3 krakenPos))
                markers[count++] = new Marker { kind = Kind.Kraken, label = Fmt(Flat(krakenPos, me)) };

            // Hostiles: the nearest few inside range, the locked one always.
            IHittable locked = CombatHud.Source != null ? CombatHud.Source.Locked : null;
            int n = 0;
            foreach (var e in EnemyShip.All)
            {
                if (e == null || !e.Alive) continue;
                float d = Flat(e.transform.position, me);
                bool isLocked = ReferenceEquals(locked, e);
                if (d > HostileRange && !isLocked) continue;
                if (isLocked) d = -1f;   // sorts first and cannot be pushed out
                if (n == MaxHostiles && d >= hDist[n - 1]) continue;
                int i = n < MaxHostiles ? n++ : n - 1;
                while (i > 0 && hDist[i - 1] > d) { hShip[i] = hShip[i - 1]; hDist[i] = hDist[i - 1]; i--; }
                hShip[i] = e; hDist[i] = d;
            }
            for (int i = 0; i < n; i++)
            {
                var e = hShip[i];
                markers[count++] = new Marker
                {
                    kind = Kind.Hostile,
                    ship = e,
                    label = Fmt(Flat(e.transform.position, me)),
                    locked = ReferenceEquals(locked, e),
                };
                hShip[i] = null;
            }

            // The course: set camp, else the way home.
            if (ChartData.TryCourse(out Vector2 target, out _, out float dist) && dist > ArrivedMetres)
                markers[count++] = new Marker
                {
                    kind = Kind.Course,
                    pos = new Vector3(target.x, 0f, target.y),
                    label = Fmt(dist),
                };

            // The nearest island she has glimpsed but not landed on.
            var isles = ChartData.Islands();
            if (isles != null)
            {
                int best = -1;
                float bestD = IslandRange;
                var p = new Vector2(me.x, me.z);
                for (int i = 0; i < isles.Count; i++)
                {
                    if (isles[i].seen != Seen.Glimpsed) continue;
                    float d = Vector2.Distance(p, isles[i].centre) - isles[i].meanRadius;
                    if (d < bestD && d > ArrivedMetres) { bestD = d; best = i; }
                }
                if (best >= 0)
                    markers[count++] = new Marker
                    {
                        kind = Kind.Island,
                        pos = new Vector3(isles[best].centre.x, 0f, isles[best].centre.y),
                        label = Fmt(bestD),
                    };
            }
        }

        /// Where the kraken threat is, flat (y = 0): the warning point while
        /// the water darkens, else the live kraken unless it is sinking away.
        static bool TryKraken(out Vector3 pos)
        {
            var warn = KrakenDirector.WarningPoint;
            if (warn.HasValue) { pos = warn.Value; pos.y = 0f; return true; }
            var k = Kraken.Active;
            if (k != null && !k.Retreating)
            {
                pos = k.transform.position;
                pos.y = 0f;
                return true;
            }
            pos = default;
            return false;
        }

        static float Flat(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        static string Fmt(float metres) => metres < 1000f
            ? Mathf.RoundToInt(metres) + " m"
            : (metres / 1000f).ToString("0.0") + " km";

        // ------------------------------------------------------------- draw

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || count == 0) return;
            // The same suppressions as the other sea HUD pieces (CombatLock).
            if (!SeaHud.HelmShowing || MidnightLandHud.Active) return;
            if (SeaSick.Ship.Modular.ShipyardSession.WorldInputBlocked) return;
            if (SeaSick.UI.ModularYard.ShipyardModal.IsOpen) return;
            if (SeaSick.UI.Menus.GameMenus.Current != SeaSick.UI.Menus.GameMenus.Mode.None) return;
            // Not over the loading screen (seen drawn on it, 2026-10-02).
            var loading = SeaSick.UI.Menus.LoadingScreen.Instance;
            if (loading != null && !loading.Finished) return;
            if (SheetHost.FrameOpen || Sheets.Current != null) return;
            var cam = Camera.main;
            if (cam == null) return;

            int u = HudLayout.Unit;
            EnsureAssets(u);

            // --- the band markers may sit in (GUI space, origin top-left)
            Rect safe = HudLayout.Safe;
            float pad = u * 0.8f;
            float top = safe.yMin + pad;
            top = Mathf.Max(top, SeaHud.TopRect.yMax + pad);
            top = Mathf.Max(top, SeaHud.AlertRect.yMax + pad);
            if (CombatHud.Visible && CombatHud.ChipRect.height > 0f) top = Mathf.Max(top, CombatHud.ChipRect.yMax + pad);
            float bottom = safe.yMax - safe.height * BottomClearShare;
            bottom = Mathf.Min(bottom, BottomStackTop() - pad);
            if (bottom < top + u * 6f) return;   // no room worth marking

            Rect chart = ChartInstrument.ScreenRect;
            if (chart.width > 0f) chart = new Rect(chart.x - u * 0.5f, chart.y - u * 0.5f, chart.width + u, chart.height + u);

            Vector2 screenC = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            float onMargin = u * 1.5f;
            int placedN = 0;
            var prevMatrix = GUI.matrix;
            var prevColor = GUI.color;

            for (int i = 0; i < count; i++)
            {
                ref Marker m = ref markers[i];
                Vector3 world;
                if (m.kind == Kind.Hostile)
                {
                    if (m.ship == null || !m.ship.Alive) continue;
                    world = m.ship.transform.position;
                }
                else if (m.kind == Kind.Kraken)
                {
                    // Live, like a hostile: the shadow glides and the beast turns.
                    if (!TryKraken(out world)) continue;
                }
                else world = m.pos;

                // --- on screen already? then the thing speaks for itself
                Vector3 sp = cam.WorldToScreenPoint(world);
                Vector2 dir;
                if (sp.z > 0f)
                {
                    float gx = sp.x, gy = Screen.height - sp.y;
                    if (gx > onMargin && gx < Screen.width - onMargin && gy > onMargin && gy < Screen.height - onMargin)
                    {
#if UNITY_EDITOR
                        if (m.kind == Kind.Kraken) DevKraken("on screen");
#endif
                        continue;
                    }
                    dir = new Vector2(gx, gy) - screenC;
                    if (dir.sqrMagnitude < 1f) dir = Vector2.up;
                }
                else
                {
                    // Behind the camera the projection mirrors through the
                    // centre; the useful answer is simply "turn that way".
                    float side = cam.transform.InverseTransformPoint(world).x;
                    dir = new Vector2(side >= 0f ? 1f : -1f, 0f);
                }
                dir.Normalize();

                float size = u * (m.locked ? 3.9f : m.kind == Kind.Kraken ? 4.2f : m.kind == Kind.Hostile ? 3.3f : m.kind == Kind.Course ? 3.3f : 2.7f);
                // It breathes while the water is still only darkening.
                if (m.kind == Kind.Kraken && KrakenDirector.Warning)
                    size *= 1f + 0.12f * Mathf.Sin(Time.unscaledTime * 7f);
                float half = size * 0.62f;   // a rotated square's worst reach
                var band = new Rect(safe.xMin + pad + half, top + half,
                    safe.width - (pad + half) * 2f, bottom - top - half * 2f);
                if (band.width < 1f || band.height < 1f) continue;
                Vector2 at = ClampToRectEdge(band.center, dir, band);

                // The chart dial owns the top-left; slide along the edge
                // past it rather than draw over it.
                if (chart.width > 0f && chart.Overlaps(new Rect(at.x - half, at.y - half, half * 2f, half * 2f)))
                {
                    if (at.x - band.xMin < at.y - band.yMin) at.y = chart.yMax + half;
                    else at.x = chart.xMax + half;
                    at.x = Mathf.Clamp(at.x, band.xMin, band.xMax);
                    at.y = Mathf.Clamp(at.y, band.yMin, band.yMax);
                }

                var box = new Rect(at.x - half, at.y - half, half * 2f, half * 2f);
                if (SeaHud.Overlaps(box))
                {
#if UNITY_EDITOR
                    if (m.kind == Kind.Kraken) DevKraken("skipped: the box touches the sea HUD");
#endif
                    continue;
                }
                bool taken = false;
                for (int j = 0; j < placedN; j++) if (placed[j].Overlaps(box)) { taken = true; break; }
                if (taken)
                {
#if UNITY_EDITOR
                    if (m.kind == Kind.Kraken) DevKraken("skipped: another marker holds the spot");
#endif
                    continue;
                }
                placed[placedN++] = box;

                Color col = m.kind == Kind.Course ? Gold
                    : m.kind == Kind.Island ? Ice
                    : m.locked || m.kind == Kind.Kraken ? EmberLocked : Ember;

                // The chevron: a tinted white kite with its own dark outline.
                float ang = Mathf.Atan2(dir.x, -dir.y) * Mathf.Rad2Deg;
                GUI.matrix = prevMatrix;
                GUIUtility.RotateAroundPivot(ang, at);
                GUI.color = col;
                GUI.DrawTexture(new Rect(at.x - size * 0.5f, at.y - size * 0.5f, size, size), arrow, ScaleMode.ScaleToFit, true);
                GUI.matrix = prevMatrix;

                var labelRect = DrawLabel(at, size, band, bottom, m.label, col, m.locked || m.kind == Kind.Kraken);
#if UNITY_EDITOR
                if (m.kind == Kind.Kraken) { DevKraken("drawn"); DevKrakenBox = box; DevKrakenLabel = labelRect; }
#endif
            }

            GUI.matrix = prevMatrix;
            GUI.color = prevColor;
        }

        /// Top edge (GUI y) of the highest thing in the bottom stack: every
        /// bottom-centre slot `HudLayout` has issued (the wheel reserve), the
        /// helm row with its harpoon button, and the combat row while it is
        /// up. Markers and their labels stay above it. Measured 2026-10-04
        /// on 1080x2340: the locked kraken's chevron sat at y 1241..1399, over
        /// the wheel reserve that starts at 1368.
        static float BottomStackTop()
        {
            Rect safe = HudLayout.Safe;
            float top = safe.yMax;
            top = Mathf.Min(top, HudLayout.BottomClustersTop);
            var issued = HudLayout.Issued;
            var names = HudLayout.IssuedTo;
            string wheel = HudLayout.Slot.Wheel.ToString();
            for (int i = 0; i < issued.Count && i < names.Count; i++)
                if (names[i] == wheel && issued[i].height > 0f) top = Mathf.Min(top, issued[i].yMin);
            top = MinTop(top, SeaHud.HelmRect);
            top = MinTop(top, SeaHud.HarpoonRect);
            top = MinTop(top, SeaHud.HarpoonDrawnRect);
            if (CombatHud.Visible) top = MinTop(top, CombatHud.Rect);
            return top;
        }

        static float MinTop(float top, Rect r) => r.width > 0f && r.height > 0f ? Mathf.Min(top, r.yMin) : top;

        /// The distance, on the inward side of the chevron so it never runs
        /// off the screen, with a dark drop shadow for bright water.
        Rect DrawLabel(Vector2 at, float size, Rect band, float bottom, string text, Color col, bool bold)
        {
            int u = HudLayout.Unit;
            float w = u * 5.4f, h = u * 1.5f;
            Vector2 c = band.center;
            float nx = (at.x - c.x) / Mathf.Max(1f, band.width), ny = (at.y - c.y) / Mathf.Max(1f, band.height);
            Rect r;
            if (Mathf.Abs(nx) >= Mathf.Abs(ny))
            {
                float x = at.x + (nx > 0f ? -(size * 0.5f + w * 0.5f) : (size * 0.5f + w * 0.5f));
                r = new Rect(x - w * 0.5f, at.y - h * 0.5f, w, h);
            }
            else
            {
                float y = at.y + (ny > 0f ? -(size * 0.5f + h * 0.5f) : (size * 0.5f + h * 0.5f));
                r = new Rect(at.x - w * 0.5f, y - h * 0.5f, w, h);
            }
            Rect safe = HudLayout.Safe;
            r.x = Mathf.Clamp(r.x, safe.xMin, safe.xMax - w);
            // Never into the bottom stack either: a chevron on the top edge
            // puts its label under it, which can reach down that far.
            r.y = Mathf.Min(r.y, bottom - h);

            var style = bold ? labelStyleBold : labelStyle;
            float o = Mathf.Max(1.5f, u * 0.08f);
            GUI.color = Shadow;
            style.normal.textColor = Color.white;
            GUI.Label(new Rect(r.x + o, r.y + o, r.width, r.height), text, style);
            GUI.color = Color.white;
            style.normal.textColor = col;
            GUI.Label(r, text, style);
            return r;
        }

#if UNITY_EDITOR
        /// Dev seam (`Dev/SweepCheck` "chevron"): what the kraken's marker did
        /// on the last repaint that reached it, and where it drew (GUI space).
        internal static string DevKrakenWhy = "";
        internal static int DevKrakenFrame = -1;
        internal static Rect DevKrakenBox, DevKrakenLabel;

        static void DevKraken(string why)
        {
            DevKrakenWhy = why;
            DevKrakenFrame = Time.frameCount;
            if (why != "drawn") { DevKrakenBox = new Rect(); DevKrakenLabel = new Rect(); }
        }
#endif

        static Vector2 ClampToRectEdge(Vector2 origin, Vector2 dir, Rect r)
        {
            float t = float.MaxValue;
            if (dir.x > 1e-4f) t = Mathf.Min(t, (r.xMax - origin.x) / dir.x);
            else if (dir.x < -1e-4f) t = Mathf.Min(t, (r.xMin - origin.x) / dir.x);
            if (dir.y > 1e-4f) t = Mathf.Min(t, (r.yMax - origin.y) / dir.y);
            else if (dir.y < -1e-4f) t = Mathf.Min(t, (r.yMin - origin.y) / dir.y);
            if (t <= 0f || t == float.MaxValue) t = 100f;
            return origin + dir * t;
        }

        // ----------------------------------------------------------- assets

        void EnsureAssets(int u)
        {
            if (arrow == null) arrow = BuildArrow();
            if (styleUnit == u && labelStyle != null) return;
            styleUnit = u;
            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(u * 1.15f),
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
            };
            labelStyleBold = new GUIStyle(labelStyle) { fontStyle = FontStyle.Bold, fontSize = Mathf.RoundToInt(u * 1.3f) };
        }

        /// A 96 px arrowhead pointing up: white kite, dark outline, soft
        /// edges. Built once; the white takes `GUI.color`, the outline stays
        /// dark.
        static Texture2D BuildArrow()
        {
            const int N = 96;
            var poly = new[] { new Vector2(0.5f, 0.88f), new Vector2(0.86f, 0.14f), new Vector2(0.5f, 0.31f), new Vector2(0.14f, 0.14f) };
            const float outline = 0.075f;
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    var p = new Vector2((x + 0.5f) / N, (y + 0.5f) / N);
                    bool inside = false;
                    float d = float.MaxValue;
                    for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                    {
                        Vector2 a = poly[j], b = poly[i];
                        if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
                        Vector2 ab = b - a;
                        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                        d = Mathf.Min(d, Vector2.Distance(p, a + ab * t));
                    }
                    if (inside && d > 0.012f) px[y * N + x] = new Color32(255, 255, 255, 255);
                    else
                    {
                        // The edge band, inside and out, fades to clear past the outline.
                        float a = Mathf.Clamp01((outline - (inside ? 0f : d)) * N * 0.5f);
                        px[y * N + x] = new Color32(14, 22, 30, (byte)Mathf.RoundToInt(a * 255f));
                    }
                }
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        void OnDestroy()
        {
            if (arrow != null) Destroy(arrow);
        }
    }
}
