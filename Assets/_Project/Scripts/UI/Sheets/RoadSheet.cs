using SeaSick.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **A length of the player's road, and the one thing you can do to it
    /// (2026-09-27).** How long, how much faster -- and "Tear down". Small
    /// on purpose, like `LadderSheet`.
    public class RoadSheet : ISheetFramed
    {
        public string[] TabLabels => null;
        public int Tab => 0;
        public void SetTab(int index) { }
        public Color Accent => SheetTheme.Timber;

        readonly Outpost outpost;
        readonly RoadSegment road;

        public RoadSheet(Outpost camp, RoadSegment seg)
        {
            outpost = camp;
            road = seg;
        }

        public string Title => "road";

        public Vector3 AnchorWorld => road != null
            ? road.transform.position
            : (outpost != null ? outpost.CampCentre : Vector3.zero);

        public bool StillValid => outpost != null && road != null;

        WatchTiles.Head head;

        /// **Midnight card, 2026-09-27** (audit #10): road glyph, island, a
        /// "standing" pill; Length / On foot chips; how people use it; Tear
        /// down (tap twice) · Close.
        public VisualElement BuildHeader()
        {
            head = CardKit.Head("road", "Road");
            head.SetSub(StationPage.Cap(StationPage.IslandName(outpost)));
            head.SetPill("standing", StationPage.PillGood);
            return head.Root;
        }

        public VisualElement BuildActions() => null;

        public VisualElement Build()
        {
            var root = CardKit.Page(out var col);
            if (road == null) return root;
            var chips = CardKit.Chips(col);
            WatchTiles.Set(WatchTiles.Chip(chips, "LENGTH", true), $"{road.Length:0} m");
            int pct = Mathf.RoundToInt((CampRoads.SpeedMultiplier - 1f) * 100f);
            var foot = WatchTiles.Chip(chips, "ON FOOT", false);
            WatchTiles.Set(foot, $"{pct}% faster");
            WatchTiles.Tone(foot, 0);

            var now = new CardKit.Now(col, CardKit.GlyphIcon("road"));
            now.Set("Used when it's quicker", "People join and leave it wherever suits them.");

            var acts = CardKit.Acts(root);
            new CardKit.Confirm(acts, "Tear down", "Tap again · stone lost", TearDown);
            CardKit.Act(acts, "Close", () => Sheets.Close());
            return root;
        }

        public void Refresh() { }

        /// Refunds nothing, like a wall: the stone went into it.
        void TearDown()
        {
            if (road == null) return;
            road.TearDown();
            Sheets.Close();
        }
    }
}
