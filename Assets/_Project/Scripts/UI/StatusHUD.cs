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
        Bilge bilge;
        CrewAgent[] crew;
        VoyageManager voyage;

        // Cached readouts. Formatting these every frame was 3.62 KB of text
        // mesh regeneration per frame — the HUD's remaining allocation once
        // the per-frame GUIStyles were gone.
        readonly HudLabel hullText = new HudLabel();
        readonly HudLabel waterText = new HudLabel();
        readonly HudLabel navText = new HudLabel();

        void Start()
        {
            motor = FindFirstObjectByType<ShipMotor>();
            hull = motor != null ? motor.GetComponent<HullIntegrity>() : null;
            bilge = motor != null ? motor.GetComponent<Bilge>() : null;
            crew = motor != null ? motor.GetComponentsInChildren<CrewAgent>(true) : new CrewAgent[0];
            voyage = FindFirstObjectByType<VoyageManager>();
        }

        void OnGUI()
        {
            // IMGUI calls OnGUI once per EVENT, not once per frame: Layout,
            // Repaint, and one more for every MouseMove the editor or the
            // player generates. This panel only draws, so everything it does
            // on a non-Repaint event is computed and then thrown away — and
            // the interpolated strings and GUIContent it builds on the way are
            // garbage that has to be collected.
            //
            // Measured on 2026-08-28, before this guard: GUI.Repaint was 70.46
            // of a 72.97 ms frame, StatusHUD.OnGUI alone cost 6.04 ms and
            // allocated 14.4 KB EVERY FRAME — the largest single allocator in
            // the game. That is what the jerky waves were: a GC pause drops a
            // whole frame, and the water is the fastest-moving thing on screen
            // so it shows it first. It also explains why the stutter was
            // irregular rather than steady — it tracked the mouse moving.
            if (Event.current.type != EventType.Repaint) return;
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
            // Water aboard reads here, under the hull bar, rather than as a
            // panel over the boat. The row only exists when there is water.
            bool wet = bilge != null && bilge.Flooding;
            float h = u * (wet ? 5.3f : 3.6f);
            float x = Screen.width - w - pad;
            float top = pad + MiniMap.ReservedHeight;
            UITheme.Rect(new Rect(x, top, w, h), UITheme.Panel);

            float inner = u * 0.6f;
            var hullRect = new Rect(x + inner, top + inner, w - inner * 2f, u * 0.5f);
            float integrity = hull != null ? hull.Integrity01 : 1f;
            UITheme.Bar(hullRect, integrity, UITheme.Ramp(1f - integrity));
            // Keyed on the whole percent the label actually shows, so a hull
            // sitting at 87 % costs nothing however much the float twitches.
            if (hullText.Changed(Mathf.RoundToInt(integrity * 100f)))
                hullText.Set($"hull {integrity:P0}");
            GUI.Label(new Rect(x + inner, hullRect.yMax, w, u * 1.2f),
                hullText.Content, UITheme.Small);

            if (wet)
            {
                float wy = hullRect.yMax + u * 1.25f;
                var waterRect = new Rect(x + inner, wy, w - inner * 2f, u * 0.5f);
                UITheme.Bar(waterRect, bilge.Bilge01, UITheme.Ramp(bilge.Bilge01));
                if (waterText.Changed(HudLabel.Key(
                        Mathf.RoundToInt(bilge.Bilge01 * 100f), bilge.Bailers)))
                    waterText.Set(bilge.Bailers > 0
                        ? $"water {bilge.Bilge01:P0} — {bilge.Bailers} bailing"
                        : $"water {bilge.Bilge01:P0}");
                GUI.Label(new Rect(x + inner, waterRect.yMax, w, u * 1.2f),
                    waterText.Content, UITheme.Small);
            }

            if (voyage != null)
            {
                var cargoRect = new Rect(x + inner, top + h - inner - u * 0.5f, w - inner * 2f, u * 0.5f);
                UITheme.Bar(cargoRect, Mathf.Clamp01(voyage.HoldFill), UITheme.Cargo);
            }
        }

        /// Bottom-left: speed, how far home is, and the hold count.
        void DrawNav(float pad, int u)
        {
            if (voyage == null || voyage.HomePoint == null) return;
            float dist = Island.FlatDistance(motor.transform.position, voyage.HomePoint.position);
            // Overload shows as "18/24+" rather than a number past the marked
            // line, so a loaded ship reads as loaded at a glance.
            // Keyed on exactly what is printed: speed to a tenth, distance to
            // the metre, the hold counts. Sailing at a steady speed the line
            // now rebuilds only when the metre ticks over, instead of sixty
            // times a second.
            if (navText.Changed(HudLabel.Key(
                    Mathf.RoundToInt(motor.CurrentSpeed * 10f),
                    Mathf.RoundToInt(dist),
                    voyage.TotalHeld,
                    voyage.HoldCapacity * (voyage.Overloaded ? -1 : 1))))
            {
                string hold = voyage.Overloaded
                    ? $"{voyage.TotalHeld}/{voyage.HoldCapacity}+"
                    : $"{voyage.TotalHeld}/{voyage.HoldCapacity}";
                navText.Set($"{motor.CurrentSpeed:F1} m/s    home {dist:F0} m    hold {hold}");
            }
            var size = navText.Size(UITheme.Small);
            var r = new Rect(pad, Screen.height - pad - u * 1.8f, size.x + u, u * 1.8f);
            UITheme.Rect(r, UITheme.Panel);
            GUI.Label(new Rect(r.x + u * 0.5f, r.y, r.width, r.height),
                navText.Content, UITheme.Small);
        }
    }
}
