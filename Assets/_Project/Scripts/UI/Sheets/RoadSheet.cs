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

        /// **Midnight header, 2026-09-27** (audit #10): a drawn road glyph
        /// replaces the "☰" that used to sit here and read like a menu.
        public VisualElement BuildHeader()
        {
            var icon = new StationPage.Glyph("road", MidnightLandHud.Ice, "cp-glyph");
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
            if (road == null) return root;
            root.Add(SheetKit.Text($"{road.Length:0} m of road", true, false, 22f));
            int pct = Mathf.RoundToInt((CampRoads.SpeedMultiplier - 1f) * 100f);
            root.Add(SheetKit.Text($"{pct}% faster on foot", false, true, 12f));
            root.Add(SheetKit.Note("Your people take a road whenever it gets them there sooner, "
                + "and join or leave it wherever suits them."));
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
