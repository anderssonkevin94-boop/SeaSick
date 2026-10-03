using System;
using System.Collections.Generic;
using SeaSick.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **The camp's Problems list (2026-10-03, villager review group 4).**
    /// Kevin's review asked for ONE place that says what is stuck right now
    /// -- "Sawmill · needs logs (Yara)", "Kitchen · no cook", "Store full of
    /// boards", "Finch lost the sawmill post", "Builders short of stone" --
    /// so he can catch a stall without watching every villager. The alert
    /// strip's "N problems" chip opens it (`AlertStrip`).
    ///
    /// **Reads, never decides:** the rows are exactly `CampAlerts.Collect`
    /// (the facts the strip, the Next card and Camp › NEEDS YOU already
    /// show), so the three can never disagree. One 56-unit row per problem,
    /// the sentence wrapping in full (never "…", 2026-10-02), its fix
    /// under it; the whole row is the tap and runs the alert's `open` --
    /// the station's sheet (the camera frames the building,
    /// `BuildingSheetFocus`), the hand's sheet, or the build list on the
    /// fix. A short list: the frame hugs it, at most half the phone screen
    /// (`SheetHost.HugsContent`, Kevin 2026-09-30); more rows scroll inside.
    /// Thumb row: Camp (the full hub) · Close.
    ///
    /// **Garbage:** rows are pooled and re-texted only on change; the list
    /// is re-collected twice a second, not on the host's 4 Hz refresh.
    public sealed class ProblemsSheet : ISheetFramed
    {
        readonly Outpost camp;

        public ProblemsSheet(Outpost camp) { this.camp = camp; }

        public static bool IsOpen => Sheets.Current is ProblemsSheet;

        // --- ISheet / ISheetFramed ---------------------------------------------

        public string Title => "Problems";
        public Color Accent => SheetTheme.Ember;
        public bool WantsTallSheet => false;
        public string[] TabLabels => null;
        public int Tab => 0;
        public void SetTab(int index) { }
        public VisualElement BuildActions() => null;
        public Vector3 AnchorWorld => camp != null ? camp.CampCentre : Vector3.zero;
        public bool StillValid =>
            camp != null && camp.Ledger != null && (camp.HasCamp || camp.Building);

        WatchTiles.Head head;

        public VisualElement BuildHeader()
        {
            head = CardKit.Head("clock", Title);
            head.SetPill("", StationPage.PillWait);
            // Whichever the host builds first, the next refresh writes the
            // subtitle ("3 things stuck right now").
            shownCount = -1;
            nextCollect = 0f;
            return head.Root;
        }

        // --- the page ------------------------------------------------------------------

        sealed class Row
        {
            public Button root;
            public Label text, fix;
            public Func<ISheet> open;
            public string lastText, lastFix;
            public CampAlerts.Tone lastTone = (CampAlerts.Tone)(-1);
        }

        readonly List<CampAlerts.Alert> alerts = new List<CampAlerts.Alert>(8);
        readonly List<Row> rows = new List<Row>(8);
        VisualElement list;
        Label none;
        float nextCollect;
        int shownCount = -1;

        public VisualElement Build()
        {
            rows.Clear();
            shownCount = -1;
            nextCollect = 0f;
            var root = CardKit.Page(out var col);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped;
            scroll.style.flexShrink = 1f;
            scroll.style.minHeight = 0f;
            // Bounded while the frame hugs (the page is measured at its
            // natural height); else it takes the rest of the band.
            if (StationPage.Hugging) scroll.style.maxHeight = Mathf.Max(120f, SheetHost.HugBodyBudget(false) - 70f);
            else scroll.style.flexGrow = 1f;
            col.Add(scroll);
            list = scroll;

            none = StationPage.Text("Nothing is stuck right now.", "st-line");
            none.AddToClassList("st-muted");
            none.style.marginTop = 10f;
            none.style.whiteSpace = WhiteSpace.Normal;
            col.Add(none);

            var acts = CardKit.Acts(root);
            CardKit.Act(acts, "Camp", OpenCamp);
            CardKit.Act(acts, "Close", () => Sheets.Close(), 1);
            Refresh();
            return root;
        }

        void OpenCamp()
        {
            if (camp != null && camp.Ledger != null) Sheets.Open(new CampSheet(camp));
        }

        public void Refresh()
        {
            if (list == null || camp == null) return;
            if (Time.unscaledTime < nextCollect) return;
            nextCollect = Time.unscaledTime + .5f;
            CampAlerts.Collect(camp, alerts);
            int n = alerts.Count;
            if (n != shownCount)
            {
                shownCount = n;
                head?.SetSub(n == 0 ? "Nothing stuck" : n == 1 ? "1 thing stuck right now" : n + " things stuck right now");
                none.style.display = n == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
            for (int i = 0; i < n; i++)
            {
                if (i >= rows.Count) rows.Add(MakeRow());
                var r = rows[i];
                var a = alerts[i];
                r.open = a.open;
                if (r.root.style.display == DisplayStyle.None) r.root.style.display = DisplayStyle.Flex;
                if (r.lastText != a.text) { r.lastText = a.text; r.text.text = a.text ?? ""; }
                string fix = FixWords(a.fixLabel);
                if (r.lastFix != fix) { r.lastFix = fix; r.fix.text = fix; }
                if (r.lastTone != a.tone)
                {
                    r.lastTone = a.tone;
                    r.text.style.color = a.tone == CampAlerts.Tone.Warn ? CardKit.Amber : CardKit.Ember;
                }
            }
            for (int i = n; i < rows.Count; i++)
            {
                rows[i].open = null;
                if (rows[i].root.style.display != DisplayStyle.None) rows[i].root.style.display = DisplayStyle.None;
            }
        }

        /// "Tap: Build storage", "Tap: Assign Bo"; a bare "Open"/"Fix" says
        /// where the tap goes instead.
        static string FixWords(string label) =>
            string.IsNullOrEmpty(label) || label == "Fix" || label == "Open" ? "Tap to open" : "Tap: " + label;

        Row MakeRow()
        {
            var r = new Row();
            r.root = new Button(() => Run(r)) { text = "" };
            r.root.AddToClassList("gr-row");
            var words = WatchTiles.Box("hs-tile-words");
            r.text = StationPage.Text("", "gr-name");
            // A sentence, not a name: a size under the grave list's 17 so a
            // long problem stays two lines on the phone. Wraps, never cut.
            r.text.style.fontSize = 15f;
            r.text.style.whiteSpace = WhiteSpace.Normal;
            r.fix = StationPage.Text("", "gr-sub");
            r.fix.style.whiteSpace = WhiteSpace.Normal;
            words.Add(r.text);
            words.Add(r.fix);
            r.root.Add(words);
            r.root.Add(StationPage.Text("›", "gr-go"));
            list.Add(r.root);
            return r;
        }

        static void Run(Row r)
        {
            var make = r.open;
            var sheet = make != null ? make() : null;
            if (sheet != null) Sheets.Open(sheet);
        }
    }
}
