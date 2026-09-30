using SeaSick.World;
using SeaSick.World.Life;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **The tombstone's story and the list of every grave (2026-09-30,
    /// island UI phase 6).** Replaces the two IMGUI stand-in cards
    /// `GravePlacementFlow` drew with `GUILayout`. A half-height sheet in the
    /// building-sheet style (`CardKit`, `.st` + Hand.uss + Lookout.uss, and
    /// it hugs its content on the phone so the stone stays in view above it:
    /// `SheetHost.HugsContent`):
    ///
    /// * **The story page** -- the name, "Day 3-9", BORN / DIED / LIVED chips,
    ///   how they died (a card tinted ember), the three `LifeStory` sentences
    ///   (saved at death, never regenerated), and a thumb row "‹ All graves"
    ///   . Close. The selection ring stands round the stone.
    /// * **The list page** -- every grave, newest first, one 56-unit row each
    ///   (initial, name, "Day 3-9 · killed in a raid · Home", ›); a tap opens
    ///   that grave's story. Scrolls inside a fixed height, never off the
    ///   card. Thumb row: Close.
    ///
    /// Opened by a tap on a standing stone (`GravePlacementFlow.
    /// TryOpenStoryAt`), right after a tombstone is laid (`ShowStory`), and
    /// from the dev LIFE panel's "All graves" (`ShowAllGraves`). Reads only:
    /// it decides nothing. A page change is a new sheet (`Sheets.Open`), so
    /// the host rebuilds the card; the placement of the NEXT pending grave
    /// closes it (`GravePlacementFlow.BeginPlacement`).
    public sealed class GraveSheet : ISheetFramed
    {
        public static bool IsOpen => Sheets.Current is GraveSheet;

        /// One grave's story.
        public static void Show(GraveRecord g)
        {
            if (g != null) Sheets.Open(new GraveSheet(g));
        }

        /// The list of every grave.
        public static void ShowAll() => Sheets.Open(new GraveSheet());

        /// Null on the list page.
        readonly GraveRecord grave;

        public GraveSheet() { }

        public GraveSheet(GraveRecord g)
        {
            grave = g;
        }

        // --- ISheet / ISheetFramed ---------------------------------------------

        public string Title => grave != null ? grave.name : "All graves";
        public Color Accent => SheetTheme.Stone;
        public bool WantsTallSheet => false;
        public string[] TabLabels => null;
        public int Tab => 0;
        public void SetTab(int index) { }
        public VisualElement BuildActions() => null;
        public bool StillValid => true;
        public void Refresh() { }

        /// The stone, when there is one (the ring stands round it); the list
        /// stands at the home camp, like the "Welcome back" report.
        public Vector3 AnchorWorld
        {
            get
            {
                if (grave != null && grave.placed)
                {
                    var h = SeaSick.CameraRig.GroundPick.Height;
                    return new Vector3(grave.x, h != null ? h(grave.x, grave.z) : 0f, grave.z);
                }
                return Outpost.Home != null ? Outpost.Home.CampCentre : Vector3.zero;
            }
        }

        // --- header -----------------------------------------------------------------

        public VisualElement BuildHeader()
        {
            var head = CardKit.Head("grave", Title);
            if (grave != null)
            {
                head.SetSub(Days(grave));
            }
            else
            {
                int n = Lives.Graveyard.Count;
                head.SetSub(n == 0 ? "Nobody has died yet" : n == 1 ? "1 laid to rest" : n + " laid to rest");
            }
            // The cause has its own card; a pill here only crowds the name.
            head.SetPill("", StationPage.PillWait);
            return head.Root;
        }

        // --- the page ------------------------------------------------------------------

        public VisualElement Build() => grave != null ? BuildStory() : BuildList();

        VisualElement BuildStory()
        {
            var g = grave;
            var root = CardKit.Page(out var col);

            var chips = CardKit.Chips(col);
            WatchTiles.Set(WatchTiles.Chip(chips, "BORN", true), g.bornDay >= 0 ? "Day " + g.bornDay : "Unknown");
            WatchTiles.Set(WatchTiles.Chip(chips, "DIED", false), "Day " + g.diedDay);
            int lived = g.bornDay >= 0 ? Mathf.Max(0, g.diedDay - g.bornDay) : -1;
            WatchTiles.Set(WatchTiles.Chip(chips, "LIVED", false),
                lived < 0 ? "Unknown" : lived == 1 ? "1 day" : lived + " days");

            var now = new CardKit.Now(col, CardKit.GlyphIcon("grave"));
            now.Set(StationPage.Cap(GravePlacementFlow.CauseLabel(g.cause)),
                g.placed
                    ? (string.IsNullOrEmpty(g.camp) ? "Laid to rest" : "Laid to rest at " + g.camp)
                    : "Not yet laid to rest");
            now.Tone(2);

            // The three sentences. Scrolls in its own box if a phone's half
            // screen cannot hold them; never pushes the thumb row off.
            var scroll = Scroller(col, StationPage.Hugging ? Mathf.Max(90f, SheetHost.HugBodyBudget(false) - 215f) : 0f);
            var card = StationPage.Card();
            card.style.flexDirection = FlexDirection.Column;
            card.style.alignItems = Align.Stretch;
            card.style.marginTop = 10f;
            bool any = false;
            if (g.story != null)
                foreach (var line in g.story)
                {
                    if (string.IsNullOrEmpty(line)) continue;
                    var t = StationPage.Text(line, "st-line");
                    t.AddToClassList("gr-story");
                    card.Add(t);
                    any = true;
                }
            if (!any)
            {
                var t = StationPage.Text("Nothing was written down.", "st-line");
                t.AddToClassList("st-muted");
                card.Add(t);
            }
            scroll.Add(card);

            var acts = CardKit.Acts(root);
            CardKit.Act(acts, "‹ All graves", () => ShowAll());
            CardKit.Act(acts, "Close", () => Sheets.Close(), 1);
            return root;
        }

        VisualElement BuildList()
        {
            var root = CardKit.Page(out var col);
            var scroll = Scroller(col, StationPage.Hugging ? Mathf.Max(120f, SheetHost.HugBodyBudget(false) - 70f) : 0f);

            var all = Lives.Graveyard;
            int shown = 0;
            for (int i = all.Count - 1; i >= 0; i--)   // newest first
            {
                var g = all[i];
                if (g == null) continue;
                scroll.Add(Row(g));
                shown++;
            }
            if (shown == 0)
            {
                var t = StationPage.Text("Nobody has died yet.", "st-line");
                t.AddToClassList("st-muted");
                t.style.marginTop = 10f;
                scroll.Add(t);
            }

            var acts = CardKit.Acts(root);
            CardKit.Act(acts, "Close", () => Sheets.Close(), 1);
            return root;
        }

        /// One grave: initial · name / "Day 3-9 · killed in a raid · Home" · ›.
        /// The whole row is the tap, 56 units tall.
        static VisualElement Row(GraveRecord g)
        {
            var row = new Button(() => Show(g)) { text = "" };
            row.AddToClassList("gr-row");

            var av = WatchTiles.Box("ck-av");
            av.Add(StationPage.Text(SheetBits.Initial(g.name), "ck-av-t"));
            row.Add(av);

            var words = WatchTiles.Box("hs-tile-words");
            words.Add(StationPage.Text(g.name, "gr-name"));
            string sub = DaysShort(g) + " · " + GravePlacementFlow.CauseLabel(g.cause)
                + (string.IsNullOrEmpty(g.camp) ? "" : " · " + g.camp)
                + (g.placed ? "" : " · not laid to rest");
            words.Add(StationPage.Text(sub, "gr-sub"));
            row.Add(words);

            row.Add(StationPage.Text("›", "gr-go"));
            return row;
        }

        /// A scrolling box that never runs off the card: bounded to
        /// `maxHeight` while the frame hugs its content (the page is measured
        /// at its natural height), else it takes the rest of the band.
        static ScrollView Scroller(VisualElement into, float maxHeight)
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped;
            scroll.style.flexShrink = 1f;
            scroll.style.minHeight = 0f;
            if (maxHeight > 0f) scroll.style.maxHeight = maxHeight;
            else scroll.style.flexGrow = 1f;
            into.Add(scroll);
            return scroll;
        }

        // --- words ------------------------------------------------------------------------

        /// "Day 3 - Day 9", or "Day 9" when the birth is not known.
        static string Days(GraveRecord g) =>
            g.bornDay >= 0 ? "Day " + g.bornDay + " – Day " + g.diedDay : "Day " + g.diedDay;

        /// "Day 3–9" for a list row.
        static string DaysShort(GraveRecord g) =>
            g.bornDay >= 0 ? "Day " + g.bornDay + "–" + g.diedDay : "Day " + g.diedDay;
    }
}
