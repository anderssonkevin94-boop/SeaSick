using SeaSick.Ship;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **The landing party's result toast (2026-09-30).** "Found a cache ·
    /// 100% explored" when the party is back aboard, in the Next card's
    /// house style (`next-card`: the dark slate card, the ice rim, a small
    /// caps label over a bold line), high and centred under the chart
    /// (`HudLayout.ToastRow`'s 22 % line), across the thumb lane's width.
    /// Replaces the IMGUI label `GatherParty.OnGUI` drew, which read as a
    /// blurry stretched ellipse on the phone.
    ///
    /// Up for `ShowSeconds` from the report, tap to dismiss. Not while the
    /// landing party sheet is open: its own card says the same line.
    /// Ticked from `SheetHost.LateUpdate`, so the Home/Pause cards and the
    /// shipyard hide it with the rest of the document.
    internal sealed class PartyReportToast
    {
        public const float ShowSeconds = 4.5f;
        const float MaxWidth = 460f;

        /// True while the toast is up (`SheetHost` scales the panel for it
        /// the way it does for the Next card).
        public static bool Showing { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => Showing = false;

        readonly Button card;
        readonly Label title;
        bool shown = true;
        float textFor = -999f;

        public PartyReportToast(VisualElement root)
        {
            card = new Button(Dismiss) { text = "" };
            card.AddToClassList("next-card");
            card.tooltip = "Landing party report: tap to dismiss";
            card.pickingMode = PickingMode.Position;
            card.style.minHeight = 0f;

            var body = new VisualElement { pickingMode = PickingMode.Ignore };
            body.AddToClassList("next-body");
            body.style.marginRight = 0f;
            var label = new Label("LANDING PARTY") { pickingMode = PickingMode.Ignore };
            label.AddToClassList("next-label");
            body.Add(label);
            title = new Label { pickingMode = PickingMode.Ignore };
            title.AddToClassList("next-title");
            title.style.fontSize = 18f;
            body.Add(title);
            card.Add(body);

            root.Add(card);
            Hide();
        }

        static void Dismiss() => GatherParty.DismissReport();

        void Hide()
        {
            Showing = false;
            if (!shown) return;
            shown = false;
            card.style.display = DisplayStyle.None;
        }

        public void Tick(VisualElement root)
        {
            bool want = GatherParty.ReportFresh
                        && Time.unscaledTime - GatherParty.LastReportAt < ShowSeconds
                        && !LandingPartySheet.IsOpen
                        && !ThumbBar.PlacementActive;
            if (!want) { Hide(); return; }

            if (textFor != GatherParty.LastReportAt)
            {
                textFor = GatherParty.LastReportAt;
                title.text = GatherParty.LastReport;
            }
            if (!shown)
            {
                shown = true;
                card.style.display = DisplayStyle.Flex;
                card.BringToFront();
            }
            Showing = true;

            float scale = SheetHost.PanelScale;
            ThumbBar.Lane(root, out float left, out float width, out _);
            if (width > MaxWidth) { left += (width - MaxWidth) * 0.5f; width = MaxWidth; }
            var safe = HudLayout.Safe;
            card.style.left = left;
            card.style.width = width;
            card.style.top = (safe.y + safe.height * 0.22f) * scale;
        }
    }
}
