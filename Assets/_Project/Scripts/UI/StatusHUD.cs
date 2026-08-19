using SeaSick.Crew;
using SeaSick.Ship;
using SeaSick.Voyage;
using SeaSick.World;
using UnityEngine;

namespace SeaSick.UI
{
    /// The permanent HUD: three small clusters at the screen edges and nothing
    /// else. Everything else in the game is contextual and appears only when
    /// it matters. Deliberately tiny — this is a portrait phone screen.
    public class StatusHUD : MonoBehaviour
    {
        ShipMotor motor;
        HullIntegrity hull;
        CrewAgent[] crew;
        VoyageManager voyage;

        void Start()
        {
            motor = FindFirstObjectByType<ShipMotor>();
            hull = motor != null ? motor.GetComponent<HullIntegrity>() : null;
            crew = motor != null ? motor.GetComponentsInChildren<CrewAgent>(true) : new CrewAgent[0];
            voyage = FindFirstObjectByType<VoyageManager>();
        }

        void OnGUI()
        {
            if (motor == null) return;
            int u = UITheme.Unit;
            float pad = u * 0.7f;

            DrawCrew(pad, pad, u);
            DrawShip(pad, u);
            DrawNav(pad, u);
        }

        /// Top-left: one slim bar per crew member. Colour is how green the sea
        /// has made them — pure flavour — and a pip above the bar means that
        /// person is off their post on a bucket, which is not.
        void DrawCrew(float x, float y, int u)
        {
            if (crew == null || crew.Length == 0) return;
            float barW = u * 0.55f;
            float barH = u * 3.4f;
            float gap = u * 0.45f;
            float w = crew.Length * (barW + gap) + gap;

            UITheme.Rect(new Rect(x, y, w, barH + gap * 2f), UITheme.Panel);
            for (int i = 0; i < crew.Length; i++)
            {
                var c = crew[i];
                if (c == null) continue;
                var r = new Rect(x + gap + i * (barW + gap), y + gap, barW, barH);
                UITheme.Bar(r, c.Sickness01, UITheme.Ramp(c.Sickness01), vertical: true);
                if (c.IsBailing)
                    UITheme.Rect(new Rect(r.x, r.y - gap * 0.6f, r.width, gap * 0.45f),
                        UITheme.Sea);
            }
        }

        /// Top-right, tucked under the minimap.
        void DrawShip(float pad, int u)
        {
            float w = u * 7.5f;
            float h = u * 3.6f;
            float x = Screen.width - w - pad;
            float top = pad + MiniMap.ReservedHeight;
            UITheme.Rect(new Rect(x, top, w, h), UITheme.Panel);

            float inner = u * 0.6f;
            var hullRect = new Rect(x + inner, top + inner, w - inner * 2f, u * 0.5f);
            float integrity = hull != null ? hull.Integrity01 : 1f;
            UITheme.Bar(hullRect, integrity, UITheme.Ramp(1f - integrity));
            GUI.Label(new Rect(x + inner, hullRect.yMax, w, u * 1.2f),
                $"hull {integrity:P0}", UITheme.Small);

            if (voyage != null)
            {
                var cargoRect = new Rect(x + inner, top + h - inner - u * 0.5f, w - inner * 2f, u * 0.5f);
                UITheme.Bar(cargoRect, Mathf.Clamp01(voyage.HoldFill), UITheme.Cargo);
            }
        }

        /// Bottom-left: just how far home is, and the hold count.
        void DrawNav(float pad, int u)
        {
            if (voyage == null || voyage.HomePoint == null) return;
            float dist = Island.FlatDistance(motor.transform.position, voyage.HomePoint.position);
            string text = $"home {dist:F0} m    hold {voyage.TotalHeld}/{voyage.HoldCapacity}";
            var size = UITheme.Small.CalcSize(new GUIContent(text));
            var r = new Rect(pad, Screen.height - pad - u * 1.8f, size.x + u, u * 1.8f);
            UITheme.Rect(r, UITheme.Panel);
            GUI.Label(new Rect(r.x + u * 0.5f, r.y, r.width, r.height), text, UITheme.Small);
        }
    }
}
