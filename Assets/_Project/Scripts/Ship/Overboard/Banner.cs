using UnityEngine;

namespace SeaSick.Ship.Overboard
{
    /// **IMGUI stand-in banner** for man-overboard events -- "MAN OVERBOARD!"
    /// on the fall, "<name> washed ashore..."/"lost at sea" on the outcomes.
    ///
    /// The GDD's HUD rule ("no full-width banners, ever") is about the
    /// PERMANENT HUD; this is a rare, few-second emergency call-out the same
    /// way `GravePlacementFlow`'s placement bar is a rare blocking flow --
    /// both are throwaway `Scripts/Dev`-style IMGUI, not `Scripts/UI`, and
    /// Astra restyles or replaces them later. Top of the screen, per the
    /// build brief, which is deliberately the one place nothing else in the
    /// HUD claims (the helm lives at the bottom).
    public class Banner : MonoBehaviour
    {
        static Banner instance;
        string text = "";
        float showUntil;

        /// **The UI Toolkit toast draws this now (2026-09-30).** Kevin: the
        /// castaway banner ("Bo is aboard") sat over the minimap and the hull
        /// chip. `SeaSick.UI.Sheets.PartyReportToast` (under the chart, the
        /// thumb lane's width) shows `Text` for as long as `Fresh`, and
        /// stamps `UiDrawing` every frame it ticks; while that stamp is
        /// recent this IMGUI box stays quiet, and it is only the fallback
        /// when no sheet host is running.
        public static string Text => instance != null ? instance.text : "";
        public static bool Fresh => instance != null && !string.IsNullOrEmpty(instance.text)
                                    && Time.unscaledTime < instance.showUntil;
        /// When the current message was shown (unscaled), to tell a new one.
        public static float ShownAt { get; private set; } = -999f;
        public static float UiDrawing = -999f;
        public static void Dismiss() { if (instance != null) instance.showUntil = 0f; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (FindAnyObjectByType<Banner>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("OverboardBanner");
            instance = go.AddComponent<Banner>();
            DontDestroyOnLoad(go);
        }

        void Awake() => instance = this;

        public static void Show(string message, float seconds = 3.5f)
        {
            if (instance == null) return;
            instance.text = message;
            instance.showUntil = Time.unscaledTime + seconds;
            ShownAt = Time.unscaledTime;
        }

        void OnGUI()
        {
            if (Time.unscaledTime >= showUntil || string.IsNullOrEmpty(text)) return;
            if (Time.unscaledTime - UiDrawing < 0.5f) return;

            float scale = Mathf.Clamp(Screen.dpi > 0 ? Screen.dpi / 160f : 2f, 1f, 3f);
            var old = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float w = Screen.width / scale;

            var big = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
            big.normal.textColor = Color.white;
            var r = new Rect(16, 28, w - 32, 56);
            GUI.Box(r, "");
            GUI.Label(r, text, big);

            GUI.matrix = old;
        }
    }
}
