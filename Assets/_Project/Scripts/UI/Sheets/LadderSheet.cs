using SeaSick.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **A ladder chain up a cliff, and the one thing you can do to it
    /// (2026-09-27).** How high it goes, how many flights and landings, how
    /// long a climb takes -- and "Tear down". Small on purpose, like
    /// `WallSheet`: a chain has no job, no level and no state.
    public class LadderSheet : ISheetFramed
    {
        public string[] TabLabels => null;
        public int Tab => 0;
        public void SetTab(int index) { }
        public Color Accent => SheetTheme.Timber;

        readonly Outpost outpost;
        readonly Ladder ladder;

        public LadderSheet(Outpost camp, Ladder chain)
        {
            outpost = camp;
            ladder = chain;
        }

        public string Title => "ladder";

        public Vector3 AnchorWorld => ladder != null
            ? Vector3.Lerp(ladder.Foot, ladder.Top, 0.5f)
            : (outpost != null ? outpost.CampCentre : Vector3.zero);

        public bool StillValid => outpost != null && ladder != null;

        WatchTiles.Head head;

        /// **Midnight card, 2026-09-27** (audit #10): ladder glyph, island,
        /// a "standing" pill; Up / Flights / Climb chips; the one thing worth
        /// knowing (raiders climb it too); Tear down (tap twice) · Close.
        public VisualElement BuildHeader()
        {
            head = CardKit.Head("ladder", "Ladder");
            head.SetSub(StationPage.Cap(StationPage.IslandName(outpost)));
            head.SetPill("standing", StationPage.PillGood);
            return head.Root;
        }

        public VisualElement BuildActions() => null;

        public VisualElement Build()
        {
            var root = CardKit.Page(out var col);
            if (ladder == null) return root;
            var chips = CardKit.Chips(col);
            WatchTiles.Set(WatchTiles.Chip(chips, "UP", true), $"{ladder.Rise:0} m");
            int f = ladder.Flights, l = ladder.Landings;
            WatchTiles.Set(WatchTiles.Chip(chips, "FLIGHTS", false),
                l > 0 ? $"{f} · {l} landing{(l == 1 ? "" : "s")}" : f.ToString());
            WatchTiles.Set(WatchTiles.Chip(chips, "CLIMB", false), $"{ladder.ClimbSeconds:0} s");

            var now = new CardKit.Now(col, new WatchTiles.Glyph("sword"));
            now.Set("Raiders climb it too",
                "Your people take it when it's the shorter way. A ladder round a wall is a way in. Animals can't use it.");
            now.Tone(1);

            var acts = CardKit.Acts(root);
            new CardKit.Confirm(acts, "Tear down", "Tap again · logs lost", TearDown);
            CardKit.Act(acts, "Close", () => Sheets.Close());
            return root;
        }

        public void Refresh() { }

        /// Refunds nothing, like a wall: the logs went into it.
        void TearDown()
        {
            if (ladder == null) return;
            ladder.TearDown();
            Sheets.Close();
        }
    }
}
