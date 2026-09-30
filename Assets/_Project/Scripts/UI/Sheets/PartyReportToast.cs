using SeaSick.Combat;
using SeaSick.Ship;
using SeaSick.Ship.Overboard;
using SeaSick.UI;
using SeaSick.World;
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
    /// **Also the game's one notice toast (2026-09-30):** `Banner.Show`
    /// messages ("Bo is aboard", "MAN OVERBOARD!", squalls) draw here, label
    /// hidden, instead of the IMGUI box that sat over the minimap and the
    /// hull chip. A party report wins while it is fresh.
    ///
    /// **Camp news joined it (2026-09-30, island UI phase 6).** Two more
    /// cards of the same shape replace the IMGUI ones `CampToasts` hosted:
    /// the raid's verdict after the raiders have gone ("RAID" in ember:
    /// "The raiders fled with nothing" / "All clear.", `RaidDirector.
    /// LastResult`) and "WHILE YOU WERE GONE · 2.3 DAYS" on the ship's
    /// return (`ReturnSummary.Fresh`; a tap opens the report on `AwaySheet`).
    /// Order of precedence: party report, raid verdict, notice, return.
    /// It also owns the carry pill (`CarryPill`: what letting go of the hand
    /// does), which is drawn by the same tick.
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

        enum Mode { Party, Notice, Verdict, Return }

        readonly Button card;
        readonly Label title;
        readonly Label label;
        readonly Label reason;
        readonly CarryPill carry;
        /// The one-time gesture hints (2026-09-30), in the carry pill's spot.
        readonly GestureHintPill hints;
        bool shown = true;
        float textFor = -999f;
        float bannerFor = -999f;
        Mode mode = Mode.Party;
        bool modeSet;
        string modeKey;
        Outpost modeCamp;
        /// The verdict text the player waved away (reference; a new raid
        /// reports a new string).
        string verdictDismissed;

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
            label = new Label("LANDING PARTY") { pickingMode = PickingMode.Ignore };
            label.AddToClassList("next-label");
            body.Add(label);
            title = new Label { pickingMode = PickingMode.Ignore };
            title.AddToClassList("next-title");
            title.style.fontSize = 18f;
            body.Add(title);
            reason = new Label { pickingMode = PickingMode.Ignore };
            reason.AddToClassList("next-reason");
            reason.style.display = DisplayStyle.None;
            body.Add(reason);
            card.Add(body);

            root.Add(card);
            carry = new CarryPill(root);
            hints = new GestureHintPill(root);
            Hide();
        }

        void Dismiss()
        {
            switch (mode)
            {
                case Mode.Notice: Banner.Dismiss(); break;
                case Mode.Verdict: verdictDismissed = modeKey; break;
                case Mode.Return: ReturnSummary.Open(modeCamp, TimeOfDay.Seconds); break;
                default: GatherParty.DismissReport(); break;
            }
        }

        void Hide()
        {
            Showing = false;
            if (!shown) return;
            shown = false;
            card.style.display = DisplayStyle.None;
        }

        public void Tick(VisualElement root)
        {
            Banner.UiDrawing = Time.unscaledTime;
            carry.Tick(root);
            hints.Tick(root, carry.Shown);

            var camp = CampToasts.Here();
            bool party = GatherParty.ReportFresh
                         && Time.unscaledTime - GatherParty.LastReportAt < ShowSeconds
                         && !LandingPartySheet.IsOpen;
            string verdict = !party && !(Sheets.Current is RaidSheet) ? Verdict(camp) : null;
            bool notice = !party && verdict == null && Banner.Fresh;
            string retLabel = null, retLine = null;
            bool retRaided = false;
            bool ret = !party && verdict == null && !notice && !(Sheets.Current is AwaySheet)
                       && ReturnSummary.Fresh(camp, TimeOfDay.Seconds, out retLabel, out retLine, out retRaided);
            bool want = (party || verdict != null || notice || ret) && !ThumbBar.PlacementActive;
            if (!want) { Hide(); return; }

            Mode now = party ? Mode.Party : verdict != null ? Mode.Verdict : notice ? Mode.Notice : Mode.Return;
            if (party)
            {
                if (!modeSet || mode != now || textFor != GatherParty.LastReportAt)
                {
                    Apply(now, "LANDING PARTY", GatherParty.LastReport, null, false, null, null);
                    textFor = GatherParty.LastReportAt;
                }
            }
            else if (verdict != null)
            {
                if (!modeSet || mode != now || modeKey != verdict)
                {
                    int nl = verdict.IndexOf('\n');
                    string head = nl >= 0 ? verdict.Substring(0, nl) : verdict;
                    string rest = nl >= 0 ? verdict.Substring(nl + 1).Replace("\n", " · ") : null;
                    Apply(now, "RAID", Cap(head), rest, true, verdict, camp);
                }
            }
            else if (notice)
            {
                if (!modeSet || mode != now || bannerFor != Banner.ShownAt || title.text != Banner.Text)
                {
                    bannerFor = Banner.ShownAt;
                    Apply(now, null, Banner.Text, null, false, null, null);
                }
            }
            else
            {
                // `retLine` is built once per return, so a reference compare is
                // the "same card" test (no string built per frame).
                if (!modeSet || mode != now || !ReferenceEquals(modeKey, retLine) || modeCamp != camp)
                    Apply(now, retLabel, retLine, "Tap for the full report", retRaided, retLine, camp);
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

        /// The raid's verdict while it is still fresh at this camp and has
        /// not been waved away; null otherwise. `RaidDirector.LastResult`
        /// ends it on its own a few seconds after the raiders are gone.
        string Verdict(Outpost camp)
        {
            if (camp == null) return null;
            string v = RaidDirector.LastResult(camp);
            if (string.IsNullOrEmpty(v)) return null;
            return ReferenceEquals(v, verdictDismissed) ? null : v;
        }

        /// Sets the card's words and rim for a mode. `label` null hides the
        /// small caps line; `reasonText` null hides the grey line; `ember`
        /// puts the raid rim on (a verdict, a return with raiders in it).
        void Apply(Mode m, string labelText, string titleText, string reasonText, bool ember,
            string key, Outpost camp)
        {
            mode = m;
            modeSet = true;
            modeKey = key;
            modeCamp = camp;
            card.tooltip = m == Mode.Return ? "While you were gone: tap for the report" : "Tap to dismiss";
            card.EnableInClassList("next-card--raid", ember);
            label.style.display = labelText == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (labelText != null) label.text = labelText;
            title.text = titleText;
            reason.style.display = string.IsNullOrEmpty(reasonText) ? DisplayStyle.None : DisplayStyle.Flex;
            if (!string.IsNullOrEmpty(reasonText)) reason.text = reasonText;
        }

        static string Cap(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
