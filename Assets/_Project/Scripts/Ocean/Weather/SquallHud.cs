using UnityEngine;
using SeaSick.UI;

namespace SeaSick.Ocean
{
    /// <summary>
    /// The edge arrow toward a live squall while it is off screen -- IMGUI
    /// stand-in, same spirit as `Ship/Overboard/RescueHud`'s own edge arrow
    /// (clamped to the top/sides, never the bottom where the helm stick
    /// lives). Astra owns `Scripts/UI`; this lives here instead because it
    /// is a gameplay warning, not a menu. The banner text itself is
    /// `Ship/Overboard/Banner`, already called by `SquallDirector`; this is
    /// only the persistent "which way" pointer while it is not yet in view.
    /// </summary>
    public class SquallHud : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (FindAnyObjectByType<SquallHud>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("SquallHud");
            go.AddComponent<SquallHud>();
            DontDestroyOnLoad(go);
        }

        const float EdgeMarginUnits = 2.6f;

        void OnGUI()
        {
            if (SeaSick.UI.Sheets.MidnightLandHud.Active) return;   // sea view only
            var dir = SquallDirector.Instance;
            if (dir == null || !dir.Active) return;
            var cam = Camera.main;
            if (cam == null) return;

            int u = HudLayout.Unit;
            Vector3 sp = cam.WorldToScreenPoint(dir.Centre);
            bool behind = sp.z < 0f;
            if (behind) { sp.x = Screen.width - sp.x; sp.y = Screen.height - sp.y; }
            Vector2 gui = new Vector2(sp.x, Screen.height - sp.y);

            float margin = u * EdgeMarginUnits;
            bool offscreen = behind || gui.x < margin || gui.x > Screen.width - margin
                || gui.y < margin || gui.y > Screen.height - margin;
            if (!offscreen) return;   // in frame: the cloud bank and rain speak for themselves

            Vector2 centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector2 rel = gui - centre;
            if (rel.sqrMagnitude < 1f) rel = Vector2.up;
            rel.Normalize();

            // Same band RescueHud's own arrow prefers: top and sides, never
            // low over the helm stick.
            var bounds = new Rect(margin, margin, Screen.width - margin * 2f, Screen.height * 0.5f - margin);
            Vector2 edgePoint = ClampToRectEdge(centre, rel, bounds);

            if (Event.current.type != EventType.Repaint)
            {
                DrawLabel(edgePoint, dir, cam, u);
                return;
            }

            float ang = Mathf.Atan2(rel.x, -rel.y) * Mathf.Rad2Deg;
            float size = u * 1.15f;
            var col = new Color(0.55f, 0.58f, 0.63f);

            var prev = GUI.matrix;
            GUIUtility.RotateAroundPivot(ang, edgePoint);
            var head = new Rect(edgePoint.x - size * 0.5f, edgePoint.y - size * 0.9f, size, size * 1.2f);
            UITheme.Rect(head, col);
            GUI.matrix = prev;

            DrawLabel(edgePoint, dir, cam, u);
        }

        static void DrawLabel(Vector2 at, SquallDirector dir, Camera cam, int u)
        {
            if (Event.current.type != EventType.Repaint) return;
            float dist = Vector3.Distance(cam.transform.position, dir.Centre);
            string label = "squall " + Mathf.RoundToInt(dist) + "m";
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(u * 0.85f),
                alignment = TextAnchor.MiddleCenter,
            };
            style.normal.textColor = new Color(0.72f, 0.75f, 0.80f);
            GUI.Label(new Rect(at.x - u * 2.5f, at.y + u * 1.3f, u * 5f, u * 1.3f), label, style);
        }

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
    }
}
