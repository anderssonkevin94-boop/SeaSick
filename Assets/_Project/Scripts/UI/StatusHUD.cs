using SeaSick.Ship;
using UnityEngine;
using SheetsHud = global::SeaSick.UI.Sheets.Sheets;

namespace SeaSick.UI
{
    /// The ship's damage panel, top-right, and nothing else: a hull bar and,
    /// while water is aboard, a bilge bar. It draws ONLY when there is
    /// something to say -- hull below 100 % or water in the bilge -- so a
    /// healthy dry ship shows nothing here at all.
    ///
    /// This used to also carry the crew sickness bars, a cargo bar and a
    /// speed / home / hold line. The crew bars were an old-UI relic (the
    /// mechanic, `CrewAgent.Sickness01`, lives on untouched), the cargo bar is
    /// what the hold chip (`HoldChip`) says at sea, and the nav line was
    /// hidden by `SheetsHud.ChartActive` every frame once the chart replaced
    /// the minimap, so it was removed rather than left dead.
    public class StatusHUD : MonoBehaviour
    {
        ShipMotor motor;
        HullIntegrity hull;
        Bilge bilge;

        // Cached readouts: formatting these every event was per-frame garbage.
        readonly HudLabel hullText = new HudLabel();
        readonly HudLabel waterText = new HudLabel();

        void Start()
        {
            motor = FindFirstObjectByType<ShipMotor>();
            hull = motor != null ? motor.GetComponent<HullIntegrity>() : null;
            bilge = motor != null ? motor.GetComponent<Bilge>() : null;
        }

        void OnEnable() => SeaSick.Ship.Modular.ShipyardService.PlayerShipReplaced += Rebind;
        void OnDisable() => SeaSick.Ship.Modular.ShipyardService.PlayerShipReplaced -= Rebind;
        void Rebind(GameObject oldShip, GameObject newShip)
        {
            motor = newShip != null ? newShip.GetComponent<ShipMotor>() : null;
            hull = newShip != null ? newShip.GetComponent<HullIntegrity>() : null;
            bilge = newShip != null ? newShip.GetComponent<Bilge>() : null;
        }

        void OnGUI()
        {
            if (SeaSick.Ship.Modular.ShipyardSession.WorldInputBlocked) return;
            if (SeaSick.UI.Sheets.MidnightLandHud.Active) return;
            if (SeaSick.UI.Sheets.SeaLedger.IsOpen) return;   // IMGUI would draw over the sea drawer
            // The sheet HUD owns the screen while she lies at a camp, and the
            // sheet opened on the hull says all of this better.
            if (SheetsHud.SuppressLegacy) return;
            if (motor == null) return;

            bool wet = bilge != null && bilge.Flooding;
            float integrity = hull != null ? hull.Integrity01 : 1f;
            // Compared on the whole percent the label shows, so a hull that
            // reads 100 % never flickers a panel on for a float's last digit.
            bool damaged = Mathf.RoundToInt(integrity * 100f) < 100;
            if (!damaged && !wet) return;

            int u = HudLayout.Unit;

            // **Reserve above the Repaint guard, draw below it.** Claiming a
            // rect is two float compares; the string formatting and text
            // meshes are what the guard skips. IMGUI calls OnGUI once per
            // EVENT (Layout, Repaint, every mouse move), so anything done on
            // a non-Repaint event is computed and thrown away as garbage.
            var panel = HudLayout.Place(HudLayout.Slot.Ship, u * 7.5f, u * (wet ? 4.7f : 3f));
            if (Event.current.type != EventType.Repaint) return;
            DrawShip(panel, u, integrity, wet);
        }

        void DrawShip(Rect panel, int u, float integrity, bool wet)
        {
            float w = panel.width;
            float x = panel.x, top = panel.y;
            UITheme.Rect(panel, UITheme.Panel);

            float inner = u * 0.6f;
            var hullRect = new Rect(x + inner, top + inner, w - inner * 2f, u * 0.5f);
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
        }
    }
}
