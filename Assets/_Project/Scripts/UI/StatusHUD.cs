using SeaSick.Crew;
using SeaSick.Ship;
using SeaSick.Voyage;
using SeaSick.World;
using UnityEngine;
using SheetsHud = global::SeaSick.UI.Sheets.Sheets;

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
        float nextNavRefresh;

        /// The ship panel's height in UITheme units at its TALLEST — the
        /// flooding layout. Kept only because the panel has to ask for a
        /// height before it knows whether the flood row is in it; nothing
        /// outside this file reads it any more. Anything stacking below asks
        /// `HudLayout` instead, and gets the height this panel ACTUALLY
        /// reported rather than its worst case.
        const float ShipPanelUnitsMax = 5.3f;

        void Start()
        {
            motor = FindFirstObjectByType<ShipMotor>();
            hull = motor != null ? motor.GetComponent<HullIntegrity>() : null;
            bilge = motor != null ? motor.GetComponent<Bilge>() : null;
            crew = motor != null ? motor.GetComponentsInChildren<CrewAgent>(true) : new CrewAgent[0];
            voyage = FindFirstObjectByType<VoyageManager>();
        }

        void OnEnable() => SeaSick.Ship.Modular.ShipyardService.PlayerShipReplaced += Rebind;
        void OnDisable() => SeaSick.Ship.Modular.ShipyardService.PlayerShipReplaced -= Rebind;
        void Rebind(GameObject oldShip, GameObject newShip)
        {
            motor = newShip != null ? newShip.GetComponent<ShipMotor>() : null;
            hull = newShip != null ? newShip.GetComponent<HullIntegrity>() : null;
            bilge = newShip != null ? newShip.GetComponent<Bilge>() : null;
            crew = newShip != null ? newShip.GetComponentsInChildren<CrewAgent>(true) : new CrewAgent[0];
            nextNavRefresh = 0;
        }

        void OnGUI()
        {
            if (SeaSick.Ship.Modular.ShipyardSession.WorldInputBlocked) return;
            if (SeaSick.UI.Sheets.MidnightLandHud.Active) return;
            if (SeaSick.UI.Sheets.SeaLedger.IsOpen) return;   // IMGUI would draw over the sea drawer
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
            if (motor == null) return;
            int u = HudLayout.Unit;

            // **Reserve above the guard, draw below it.** Claiming a rect is
            // two float compares; it is the string formatting and the text
            // meshes that the guard exists to skip. Reserving only on Repaint
            // made this panel's place in the column depend on how often the
            // view repainted -- and `HudOverlapProbe` duly caught it drawing
            // inside the minimap at (916..1066, 14..86).
            // The sheet HUD owns the screen while she lies at a camp. The crew
            // pips stay -- they are the one readout that is about the people
            // aboard rather than about the voyage -- and the ship panel and the
            // nav line stand down, because the sheet the player opens on the
            // hull says both, better.
            bool sheets = SheetsHud.SuppressLegacy;
            // The nav line answered "how fast, how far home, how full" -- the
            // first two are on the chart's tab and rim now, so it goes with
            // the minimap and the compass tape rather than outliving them.
            bool navGone = sheets || SheetsHud.ChartActive;
            var crewRect = HudVisibility.Crew ? ReserveCrew(u) : Rect.zero;
            var shipRect = sheets ? Rect.zero : ReserveShip(u);
            var navRect = navGone ? Rect.zero : ReserveNav(u);

            if (Event.current.type != EventType.Repaint) return;

            if (HudVisibility.Crew) DrawCrew(crewRect, u);
            if (!sheets) DrawShip(shipRect, u);
            if (!navGone) DrawNav(navRect, u);
        }

        /// Top-left: one slim bar per crew member. Colour is how green the sea
        /// has made them — pure flavour — and a pip above the bar means that
        /// person is off their post on a bucket, which is not.
        Rect ReserveCrew(int u)
        {
            if (crew == null || crew.Length == 0) return Rect.zero;
            float barW = u * 0.55f;
            float gap = u * 0.45f;
            float w = crew.Length * (barW + gap) + gap;
            return HudLayout.Place(HudLayout.Slot.Crew, w, u * 3.4f + gap * 2f);
        }

        void DrawCrew(Rect panel, int u)
        {
            if (crew == null || crew.Length == 0 || panel.width <= 0f) return;
            float barW = u * 0.55f;
            float barH = u * 3.4f;
            float gap = u * 0.45f;

            float x = panel.x, y = panel.y;
            UITheme.Rect(panel, UITheme.Panel);
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
        Rect ReserveShip(int u)
        {
            // Water aboard reads here, under the hull bar, rather than as a
            // panel over the boat. The row only exists when there is water --
            // so the height is state, and it is cheap state.
            bool wet = bilge != null && bilge.Flooding;
            return HudLayout.Place(HudLayout.Slot.Ship, u * 7.5f,
                                   u * (wet ? ShipPanelUnitsMax : 3.6f));
        }

        void DrawShip(Rect panel, int u)
        {
            float w = panel.width;
            bool wet = bilge != null && bilge.Flooding;
            float h = panel.height;
            float x = panel.x, top = panel.y;
            UITheme.Rect(panel, UITheme.Panel);

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
        /// The nav line's WIDTH follows its text, which cannot be measured
        /// without building the text -- and building it on every event is the
        /// allocation this panel's repaint guard exists to prevent. Its
        /// HEIGHT is constant, and height is the only thing the column stacks
        /// on, so the reservation carries last frame's width and the true one
        /// lands on the next repaint. Nothing sits below it to notice.
        float navWidth;

        Rect ReserveNav(int u)
        {
            if (voyage == null || voyage.HomePoint == null) return Rect.zero;
            return HudLayout.Place(HudLayout.Slot.Nav,
                                   Mathf.Max(navWidth, u * 8f), u * 1.8f);
        }

        void DrawNav(Rect reserved, int u)
        {
            if (voyage == null || voyage.HomePoint == null) return;
            float dist = Island.FlatDistance(motor.transform.position, voyage.HomePoint.position);
            // Overload shows as "18/24+" rather than a number past the marked
            // line, so a loaded ship reads as loaded at a glance.
            // Keyed on exactly what is printed: speed to a tenth, distance to
            // the metre, the hold counts. Sailing at a steady speed the line
            // now rebuilds only when the metre ticks over, instead of sixty
            // times a second.
            // ...and at most four times a second. Speed to a tenth moves on
            // nearly every frame in any sea, and at 6 m/s the metre ticks six
            // times a second, so the line was regenerating its text mesh on
            // most frames -- ~60 KB each in GUIStyle.GetMeshInfo, the whole
            // per-frame GC floor once everything else was quiet (measured
            // 2026-09-11: GC p50 56 KB/frame, StatusHUD.OnGUI 60-74 KB on the
            // worst frames). Four a second is what PerfHUD settled on.
            bool due = Time.unscaledTime >= nextNavRefresh;
            if (due) nextNavRefresh = Time.unscaledTime + 0.25f;
            if (due && navText.Changed(HudLabel.Key(
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
            navWidth = size.x + u;
            var r = new Rect(reserved.x, reserved.y, navWidth, reserved.height);
            UITheme.Rect(r, UITheme.Panel);
            GUI.Label(new Rect(r.x + u * 0.5f, r.y, r.width, r.height),
                navText.Content, UITheme.Small);
        }
    }
}
