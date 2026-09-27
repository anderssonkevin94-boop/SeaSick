using SeaSick.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **The pier's own card (2026-09-27, menu rework #8).**
    ///
    /// A pier has no recipes and no upgrade, so a tap on it used to fall
    /// through to `FireSheet` -- the legacy camp mega-sheet, the fire's
    /// rations and priority controls shown on a berth that has nothing to
    /// do with any of that. `SheetBootstrap` now sends her own hull here
    /// when the ship is alongside THIS pier (the manifest, `ShipSheet`);
    /// this small card is what a tap on it gets otherwise -- which berth
    /// this is, whether it is her home, and nothing else, because a pier
    /// has nothing else.
    public class PierSheet : ISheetFramed
    {
        public string[] TabLabels => null;
        public int Tab => 0;
        public void SetTab(int index) { }
        public Color Accent => SheetTheme.Sea;

        readonly Outpost outpost;
        readonly Building building;
        readonly Dock dock;

        public PierSheet(Outpost camp, Building pier)
        {
            outpost = camp;
            building = pier;
            dock = pier != null ? pier.GetComponent<Dock>() : null;
        }

        public string Title => "Pier";

        public Vector3 AnchorWorld => building != null
            ? building.transform.position
            : (outpost != null ? outpost.CampCentre : Vector3.zero);

        public bool StillValid => building != null;

        public VisualElement BuildActions() => null;

        Label sub;

        public VisualElement BuildHeader()
        {
            var icon = new StationPage.Glyph("pier", MidnightLandHud.Ice, "cp-glyph");
            return CampPages.IconHeader(Title, icon, out sub);
        }

        public VisualElement Build()
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Column;

            bool home = dock != null && dock.IsHome;
            root.Add(SheetKit.Text(home ? "Home berth" : "A working berth", true, false, 20f));
            root.Add(SheetKit.Note(home
                ? "She spawns here, refits here, and this is where every voyage closes."
                : "Moor here to load, unload, or come ashore. Set her home berth from the "
                    + "manifest while she is lying alongside."));

            Refresh();
            return root;
        }

        public void Refresh()
        {
            if (sub == null) return;
            var isle = outpost != null ? outpost.Island : null;
            sub.text = isle != null ? (isle.IsHome ? "home island" : isle.gameObject.name) : "";
        }
    }
}
