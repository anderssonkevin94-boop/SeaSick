using UnityEngine;

namespace SeaSick.UI
{
    /// Shared look for the whole game: one palette, one type scale, and small
    /// drawing helpers so every screen element matches. IMGUI for now — the
    /// point is a clean, tiny footprint on a portrait phone screen.
    public static class UITheme
    {
        public static readonly Color Panel = new Color(0.04f, 0.07f, 0.10f, 0.62f);
        public static readonly Color PanelSolid = new Color(0.04f, 0.07f, 0.10f, 0.92f);
        public static readonly Color Track = new Color(1f, 1f, 1f, 0.16f);
        public static readonly Color Text = new Color(0.96f, 0.97f, 0.98f, 1f);
        public static readonly Color TextDim = new Color(0.96f, 0.97f, 0.98f, 0.65f);
        public static readonly Color Good = new Color(0.42f, 0.82f, 0.45f);
        public static readonly Color Warn = new Color(0.95f, 0.75f, 0.25f);
        public static readonly Color Bad = new Color(0.90f, 0.32f, 0.26f);
        public static readonly Color Sea = new Color(0.45f, 0.78f, 0.92f);
        public static readonly Color Cargo = new Color(0.85f, 0.68f, 0.38f);

        // --- the Ledger palette (Resources/UI/Ledger.uss), for the IMGUI
        // pieces that could not move to UI Toolkit yet (2026-09-27 restyle:
        // siting buttons, anchor prompts, toasts) ---
        public static readonly Color LedgerPanel = new Color32(19, 34, 46, 245);     // #13222E
        public static readonly Color LedgerPanelHi = new Color32(32, 57, 72, 250);
        public static readonly Color LedgerEdge = new Color32(44, 74, 94, 255);
        public static readonly Color LedgerIce = new Color32(164, 210, 232, 255);    // #A4D2E8
        public static readonly Color LedgerPearl = new Color32(232, 242, 246, 255);
        public static readonly Color LedgerMint = new Color32(159, 224, 194, 255);
        public static readonly Color LedgerEmber = new Color32(228, 98, 58, 255);
        public static readonly Color LedgerInk = new Color32(15, 27, 37, 255);

        static GUIStyle small, body, strong, title, button, toast, pill, pillPressed;
        static GUIStyle buttonPressed;
        static int builtFor = -1;

        /// Type scale derived from the shorter screen edge so it reads the
        /// same on a phone and in a wide editor Game view.
        public static int Unit => Mathf.Clamp(Mathf.RoundToInt(Mathf.Min(Screen.width, Screen.height) * 0.030f), 11, 20);

        static void Build()
        {
            if (builtFor == Unit && small != null) return;
            builtFor = Unit;

            small = new GUIStyle(GUI.skin.label)
            { fontSize = Mathf.Max(13, Unit - 2), normal = { textColor = TextDim }, alignment = TextAnchor.MiddleLeft };
            body = new GUIStyle(GUI.skin.label)
            { fontSize = Unit, normal = { textColor = Text }, alignment = TextAnchor.MiddleLeft };
            strong = new GUIStyle(GUI.skin.label)
            { fontSize = Unit, fontStyle = FontStyle.Bold, normal = { textColor = Text }, alignment = TextAnchor.MiddleCenter };
            title = new GUIStyle(GUI.skin.label)
            { fontSize = Unit + 6, fontStyle = FontStyle.Bold, normal = { textColor = Text }, alignment = TextAnchor.MiddleCenter };
            toast = new GUIStyle(GUI.skin.label)
            { fontSize = Unit + 1, fontStyle = FontStyle.Bold, normal = { textColor = Text }, alignment = TextAnchor.MiddleCenter };

            button = new GUIStyle(GUI.skin.button)
            {
                fontSize = Unit + 1,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Text, background = Solid(new Color(0.10f, 0.20f, 0.28f, 0.95f)) },
                hover = { textColor = Text, background = Solid(new Color(0.15f, 0.29f, 0.38f, 0.97f)) },
                active = { textColor = Text, background = Solid(new Color(0.20f, 0.40f, 0.50f, 1f)) },
                border = new RectOffset(2, 2, 2, 2),
                padding = new RectOffset(10, 10, 8, 8),
            };

            // A latched button, drawn as though held down. Built ONCE here
            // rather than as `new GUIStyle(Button) { normal = active }` at the
            // call site every frame: IMGUI keys its cached text meshes on the
            // style INSTANCE, so a style rebuilt per frame silently forces
            // every label and button drawn with it to regenerate its mesh.
            // That is why HelmInput allocated 5.99 KB a frame while drawing
            // almost nothing but two arrows and a fixed caption.
            buttonPressed = new GUIStyle(button) { normal = button.active };

            // The Ledger pill: #13222E, a 1.5 px ice rim, fully round ends.
            // A 9-sliced rounded texture, so any width keeps its round ends.
            int r = PillRadius;
            pill = new GUIStyle(GUI.skin.button)
            {
                fontSize = Unit + 1,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
                normal = { textColor = LedgerPearl, background = Rounded(r, LedgerPanel, LedgerIce, 3) },
                hover = { textColor = LedgerPearl, background = Rounded(r, LedgerPanelHi, LedgerIce, 3) },
                active = { textColor = LedgerInk, background = Rounded(r, LedgerIce, LedgerIce, 3) },
                border = new RectOffset(r, r, r, r),
                padding = new RectOffset(r / 2 + 8, r / 2 + 8, 6, 6),
            };
            pill.onNormal = pill.active;
            pillPressed = new GUIStyle(pill) { normal = pill.active };
        }

        /// Corner radius (px) of the pill texture. Kept under half the
        /// smallest pill (the anchor prompt row, 2.7 units) so the 9-slice
        /// never overlaps itself.
        static int PillRadius => Mathf.Max(8, Mathf.RoundToInt(Unit * 1.1f));

        /// A Ledger-style pill button (anchor prompts, the camp bar).
        public static GUIStyle Pill { get { Build(); return pill; } }
        public static GUIStyle PillPressed { get { Build(); return pillPressed; } }

        static Texture2D cardTex;

        /// **A Ledger toast card**: the drawer's card (#13222E, a 1.5 px
        /// #2C4A5E rim, round corners) with a coloured stripe down its left
        /// edge for what kind of news it is -- ember for a raid, ice for a
        /// return, mint for good news.
        public static void ToastCard(Rect r, Color accent)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (cardTex == null) cardTex = Rounded(14, LedgerPanel, LedgerEdge, 3);
            GUI.DrawTexture(r, cardTex, ScaleMode.StretchToFill, true, 0f, Color.white, 0f, 12f);
            float w = Mathf.Max(3f, Unit * 0.25f);
            var stripe = new Rect(r.x + w, r.y + r.height * 0.2f, w, r.height * 0.6f);
            GUI.DrawTexture(stripe, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, accent, 0f, w * 0.5f);
        }

        /// A rounded rectangle texture (radius `r` px, `edge` px rim),
        /// antialiased, sized `2r+2` so it 9-slices with `border = r`.
        public static Texture2D Rounded(int r, Color fill, Color rim, int edge)
        {
            int n = r * 2 + 2;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            float c = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    // Distance to a rounded-rect edge whose straight parts are
                    // the 2-px middle band (the part the slice stretches).
                    float dx = Mathf.Max(Mathf.Abs(x - c) - 1f, 0f), dy = Mathf.Max(Mathf.Abs(y - c) - 1f, 0f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float outer = Mathf.Clamp01(r - d + 0.5f);
                    float inner = Mathf.Clamp01(r - edge - d + 0.5f);
                    var col = Color.Lerp(rim, fill, inner);
                    col.a *= outer;
                    px[y * n + x] = col;
                }
            tex.SetPixels(px);
            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;
            return tex;
        }

        public static GUIStyle ButtonPressed { get { Build(); return buttonPressed; } }
        public static GUIStyle Small { get { Build(); return small; } }
        public static GUIStyle Body { get { Build(); return body; } }
        public static GUIStyle Strong { get { Build(); return strong; } }
        public static GUIStyle Title { get { Build(); return title; } }
        public static GUIStyle Toast { get { Build(); return toast; } }
        public static GUIStyle Button { get { Build(); return button; } }

        static Texture2D Solid(Color c)
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, c);
            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;
            return tex;
        }

        public static void Rect(Rect r, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
        }

        /// A slim meter. Fills left-to-right, or bottom-to-top when vertical.
        public static void Bar(Rect r, float fill01, Color fillColor, bool vertical = false)
        {
            Rect(r, Track);
            fill01 = Mathf.Clamp01(fill01);
            if (fill01 <= 0f) return;
            Rect(vertical
                    ? new Rect(r.x, r.yMax - r.height * fill01, r.width, r.height * fill01)
                    : new Rect(r.x, r.y, r.width * fill01, r.height),
                fillColor);
        }

        /// Colour ramp for anything that goes from fine to dangerous.
        public static Color Ramp(float t)
        {
            t = Mathf.Clamp01(t);
            return t < 0.5f
                ? Color.Lerp(Good, Warn, t * 2f)
                : Color.Lerp(Warn, Bad, (t - 0.5f) * 2f);
        }

        /// A banner across the screen — used for warnings, never for chrome.
        // Full-width banners are gone on purpose: a band of colour across the
        // middle of the screen is the single most immersion-breaking thing a
        // HUD can do. Warnings belong on the instruments, where you're already
        // looking, or on the water itself.

        static GUIStyle small2Centered;
        public static GUIStyle Small2Centered
        {
            get
            {
                Build();
                // 13 px floor (2026-09-27 text size): Unit bottoms out at 11.
                if (small2Centered == null || small2Centered.fontSize != small.fontSize)
                    small2Centered = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter };
                return small2Centered;
            }
        }
    }
}
