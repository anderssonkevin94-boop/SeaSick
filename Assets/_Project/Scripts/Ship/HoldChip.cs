using System.Collections.Generic;
using UnityEngine;
using SeaSick.UI;
using SeaSick.UI.Sheets;
using SeaSick.Voyage;
using SeaSick.World.Economy;

namespace SeaSick.Ship
{
    /// **Always-visible hold readout at sea** (2026-09-28, Kevin: "I need
    /// some way of easily seeing my resources while I'm at sea. Maybe a
    /// weight number always visible on screen and next to it is a backpack
    /// that shows what I'm carrying.") IMGUI stand-in, same spirit as
    /// `Ship/Overboard/RescueHud` / `Ocean/Weather/SquallHud` — Astra owns
    /// `Scripts/UI`, this lives here because it reads voyage state directly
    /// rather than being a menu.
    ///
    /// Two pieces:
    /// 1. **The chip** — a pill under the ship panel showing units held over
    ///    hold capacity, coloured by how full she is.
    /// 2. **The backpack** — tapping the chip opens a panel listing what's
    ///    in the hold; tapping the chip again, or anywhere outside, closes
    ///    it. Only the chip/panel rects are consumed (via `UIBlocker`); an
    ///    outside tap is left alone so it still reaches the helm/world.
    public class HoldChip : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (FindAnyObjectByType<HoldChip>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("HoldChip");
            go.AddComponent<HoldChip>();
            DontDestroyOnLoad(go);
        }

        VoyageManager voyage;
        float nextVoyageLookup;
        bool open;

        readonly HudLabel chipText = new HudLabel();
        readonly HudLabel footerText = new HudLabel();
        GUIStyle chipStyle;
        readonly List<KeyValuePair<string, int>> rows = new List<KeyValuePair<string, int>>();
        readonly List<string> rowLabels = new List<string>();
        int rowsHash = int.MinValue;
        float nextRowRefresh;

        const int MaxRows = 8;

        void Update()
        {
            if (Time.unscaledTime >= nextVoyageLookup)
            {
                if (voyage == null) voyage = FindFirstObjectByType<VoyageManager>();
                nextVoyageLookup = Time.unscaledTime + 1f;
            }
        }

        void OnGUI()
        {
            if (voyage == null) return;
            if (SeaSick.Ship.Modular.ShipyardSession.WorldInputBlocked) return;
            if (MidnightLandHud.Active) return;   // land HUD owns the screen
            if (SeaLedger.IsOpen) return;          // sea drawer's own scrim

            int u = HudLayout.Unit;
            var chipRect = HudLayout.Place(HudLayout.Slot.Hold, u * 7.5f, u * 2.4f);

            Rect panelRect = default;
            bool showPanel = open;
            if (showPanel)
            {
                RefreshRows();
                float rowH = u * 1.9f;
                int shown = Mathf.Min(rows.Count, MaxRows);
                int extraRow = rows.Count > MaxRows ? 1 : 0;
                int lineCount = rows.Count == 0 ? 1 : shown + extraRow;
                float pad = u * 0.6f;
                float panelH = pad * 2f + lineCount * rowH + rowH * 1.1f; // + footer
                float panelW = Mathf.Max(chipRect.width, u * 11f);
                panelRect = new Rect(chipRect.xMax - panelW, chipRect.yMax + HudLayout.Gap, panelW, panelH);
                panelRect = HudLayout.Declare("HoldPanel", panelRect);
            }

            // Close-on-outside-tap: checked on the raw event, never consumed,
            // so a tap that isn't ours still reaches the helm/world exactly
            // as if this component didn't exist.
            if (open && Event.current.type == EventType.MouseDown)
            {
                var m = Event.current.mousePosition;
                if (!chipRect.Contains(m) && !panelRect.Contains(m)) open = false;
            }

            UIBlocker.Block(chipRect);
            if (showPanel) UIBlocker.Block(panelRect);

            if (GUI.Button(chipRect, GUIContent.none, GUIStyle.none)) open = !open;

            if (Event.current.type != EventType.Repaint) return;

            DrawChip(chipRect, u);
            if (showPanel) DrawPanel(panelRect, u);
        }

        // ------------------------------------------------------------ chip

        void DrawChip(Rect r, int u)
        {
            UITheme.Rect(r, UITheme.Panel);

            bool overloaded = voyage.Overloaded;
            float fill01 = voyage.HoldCapacity > 0 ? (float)voyage.TotalHeld / voyage.HoldCapacity : 0f;
            Color state = overloaded ? UITheme.Bad : fill01 >= 0.8f ? UITheme.Warn : UITheme.Text;

            float pad = u * 0.6f;
            float iconSize = r.height - pad * 2f;
            var iconRect = new Rect(r.x + pad, r.y + pad, iconSize, iconSize);
            DrawBag(iconRect, state);

            if (chipText.Changed(HudLabel.Key(voyage.TotalHeld, voyage.HoldCapacity, overloaded ? 1 : 0)))
                chipText.Set(overloaded
                    ? $"{voyage.TotalHeld} / {voyage.HoldCapacity}+"
                    : $"{voyage.TotalHeld} / {voyage.HoldCapacity}");

            var textRect = new Rect(iconRect.xMax + pad * 0.8f, r.y, r.width - iconSize - pad * 2.6f, r.height);
            // One GUIStyle instance kept for the chip's life and re-coloured
            // in place — a style rebuilt every Repaint would force its text
            // mesh to regenerate even when the string hasn't changed (see
            // `HudLabel`'s note on why StatusHUD stopped doing this).
            if (chipStyle == null) chipStyle = new GUIStyle(UITheme.Body);
            chipStyle.normal.textColor = state;
            GUI.Label(textRect, chipText.Content, chipStyle);
        }

        /// A simple bag shape drawn from flat rects — no reliance on an
        /// emoji glyph, which this project's IMGUI font does not render as
        /// colour art (it would fall back to a missing-glyph box).
        static void DrawBag(Rect r, Color c)
        {
            float strapW = r.width * 0.18f;
            var strapL = new Rect(r.x + r.width * 0.22f, r.y, strapW, r.height * 0.35f);
            var strapR = new Rect(r.xMax - r.width * 0.22f - strapW, r.y, strapW, r.height * 0.35f);
            UITheme.Rect(strapL, c);
            UITheme.Rect(strapR, c);
            var body = new Rect(r.x, r.y + r.height * 0.28f, r.width, r.height * 0.72f);
            UITheme.Rect(body, c);
            var flap = new Rect(r.x + r.width * 0.14f, r.y + r.height * 0.28f, r.width * 0.72f, r.height * 0.18f);
            UITheme.Rect(flap, new Color(0f, 0f, 0f, 0.25f));
        }

        // ---------------------------------------------------------- panel

        void RefreshRows()
        {
            if (Time.unscaledTime < nextRowRefresh) return;
            nextRowRefresh = Time.unscaledTime + 0.25f;

            int hash = voyage.HeldStores.Count * 397 ^ voyage.TotalHeld * 131 ^ voyage.HoldCapacity;
            foreach (var kv in voyage.HeldStores) hash = hash * 397 ^ (kv.Key.GetHashCode() * 31 + kv.Value);
            if (hash == rowsHash) return;
            rowsHash = hash;

            rows.Clear();
            rowLabels.Clear();
            foreach (var kv in voyage.HeldStores)
                if (kv.Value > 0) rows.Add(kv);
            rows.Sort((a, b) => b.Value.CompareTo(a.Value));
            for (int i = 0; i < rows.Count && i < MaxRows; i++)
                rowLabels.Add($"{ResDefs.Label(rows[i].Key)} {rows[i].Value}");

            footerText.Set(voyage.Overloaded
                ? $"{voyage.HoldCapacity} of {voyage.HoldCapacity} in the hold — {voyage.TotalHeld - voyage.HoldCapacity} on deck"
                : $"{voyage.TotalHeld} of {voyage.HoldCapacity} in the hold");
        }

        void DrawPanel(Rect r, int u)
        {
            UITheme.Rect(r, UITheme.PanelSolid);
            float pad = u * 0.6f;
            float rowH = u * 1.9f;
            float y = r.y + pad;
            float iconSize = rowH * 0.72f;

            if (rows.Count == 0)
            {
                GUI.Label(new Rect(r.x + pad, y, r.width - pad * 2f, rowH), "The hold is empty", UITheme.Body);
                y += rowH;
            }
            else
            {
                int shown = Mathf.Min(rows.Count, MaxRows);
                for (int i = 0; i < shown; i++)
                {
                    var icon = ItemIconSet.Get(rows[i].Key);
                    float textX = r.x + pad;
                    if (icon != null)
                    {
                        GUI.DrawTexture(new Rect(r.x + pad, y + (rowH - iconSize) * 0.5f, iconSize, iconSize), icon);
                        textX += iconSize + pad * 0.6f;
                    }
                    GUI.Label(new Rect(textX, y, r.width - (textX - r.x) - pad, rowH), rowLabels[i], UITheme.Body);
                    y += rowH;
                }
                if (rows.Count > MaxRows)
                {
                    GUI.Label(new Rect(r.x + pad, y, r.width - pad * 2f, rowH),
                        $"+{rows.Count - MaxRows} more", UITheme.Small);
                    y += rowH;
                }
            }

            var footRect = new Rect(r.x + pad, r.yMax - pad - rowH, r.width - pad * 2f, rowH);
            GUI.Label(footRect, footerText.Content, UITheme.Small);
        }
    }
}
