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

        /// **Midnight header, 2026-09-27** (audit #10): a drawn ladder glyph
        /// replaces the "☰" that used to sit here and read like a menu.
        public VisualElement BuildHeader()
        {
            var icon = new StationPage.Glyph("ladder", MidnightLandHud.Ice, "cp-glyph");
            var head = CampPages.IconHeader(StationPage.Cap(Title), icon, out var sub);
            sub.text = "standing";
            return head;
        }

        public VisualElement BuildActions()
            => SheetKit.Actions(SheetKit.Btn("Tear down", TearDown, false, true));

        public VisualElement Build()
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Column;
            if (ladder == null) return root;
            root.Add(SheetKit.Text($"{ladder.Rise:0} m up", true, false, 22f));
            int f = ladder.Flights, l = ladder.Landings;
            root.Add(SheetKit.Text(
                $"{f} flight{(f == 1 ? "" : "s")} · {l} landing{(l == 1 ? "" : "s")} · "
                + $"{ladder.ClimbSeconds:0} s to climb", false, true, 12f));
            root.Add(SheetKit.Note("Your people climb it when it's the shorter way. So do "
                + "raiders — a ladder round a wall is a way in. Animals can't."));
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
