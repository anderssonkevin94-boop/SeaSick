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

        static GUIStyle small, body, strong, title, button, toast;
        static int builtFor = -1;

        /// Type scale derived from the shorter screen edge so it reads the
        /// same on a phone and in a wide editor Game view.
        public static int Unit => Mathf.Clamp(Mathf.RoundToInt(Mathf.Min(Screen.width, Screen.height) * 0.030f), 11, 20);

        static void Build()
        {
            if (builtFor == Unit && small != null) return;
            builtFor = Unit;

            small = new GUIStyle(GUI.skin.label)
            { fontSize = Unit - 2, normal = { textColor = TextDim }, alignment = TextAnchor.MiddleLeft };
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
        }

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
                if (small2Centered == null || small2Centered.fontSize != Unit - 2)
                    small2Centered = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter };
                return small2Centered;
            }
        }
    }
}
