using SeaSick.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **A length of wall, and the two things you can do to it.**
    ///
    /// Kevin's own siting design (D5) puts the gate here rather than in the
    /// build list: *"gates are placed on existing walls -- tap a built
    /// segment, its sheet, 'make this a gate'."* So this sheet is not
    /// decoration; it is the only door the gate has, and it is why a
    /// segment carries a collider at all.
    ///
    /// Deliberately small. A wall has one number (how much of it is left),
    /// one state (whole or breached) and no decisions inside it -- a gate
    /// opens and shuts by itself (D3), nobody is posted to a wall, and a
    /// repair queues itself the moment the segment breaks. Anything more on
    /// this sheet would be inventing a decision the design does not have.
    public class WallSheet : ISheetFramed
    {
        // One thing to look at, so no tab strip -- `SiteSheet`'s rule.
        public string[] TabLabels => null;
        public int Tab => 0;
        public void SetTab(int index) { }
        public Color Accent => SheetTheme.Timber;

        readonly Outpost outpost;
        readonly WallSegment wall;

        Label state;
        VisualElement bar;
        VisualElement note;
        Button gateBtn;
        int barKey = -99;

        public WallSheet(Outpost camp, WallSegment segment)
        {
            outpost = camp;
            wall = segment;
        }

        public string Title => wall != null && wall.IsGate ? "gate" : "palisade";

        public Vector3 AnchorWorld => wall != null
            ? wall.Midpoint
            : (outpost != null ? outpost.CampCentre : Vector3.zero);

        public bool StillValid => outpost != null && wall != null;

        public VisualElement BuildHeader() =>
            SheetKit.Header(wall != null && wall.Breached ? "breached" : "standing",
                Title, SheetTheme.Timber, wall != null && wall.IsGate ? "⌸" : "▤",
                () => Sheets.Close());

        /// Two verbs, pinned. "Make this a gate" is not offered on a gate,
        /// and "Tear down" says what it costs you (nothing) and what it
        /// gives you back (nothing) in the note above rather than in the
        /// button, so the button stays a verb.
        public VisualElement BuildActions()
        {
            if (wall != null && wall.IsGate)
                return SheetKit.Actions(
                    SheetKit.Btn("Tear down", TearDown, false, true));

            gateBtn = SheetKit.Btn("Make this a gate", MakeGate, true);
            return SheetKit.Actions(
                gateBtn,
                SheetKit.Btn("Tear down", TearDown, false, true));
        }

        public VisualElement Build()
        {
            state = null; bar = null; note = null;
            barKey = -99;

            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Column;

            state = SheetKit.Text("", true, false, 22f);
            bar = SheetBits.Holder();
            root.Add(SheetKit.Row(state, bar));

            note = SheetBits.Holder();
            root.Add(note);

            Refresh();
            return root;
        }

        public void Refresh()
        {
            if (wall == null) return;
            float fill = wall.MaxHp > 0f ? Mathf.Clamp01(wall.Hp / wall.MaxHp) : 0f;
            int pct = Mathf.RoundToInt(fill * 100f);
            if (pct != barKey)
            {
                barKey = pct;
                if (state != null) state.text = pct + "%";
                SheetBits.Swap(bar, SheetKit.Bar(fill,
                    wall.Breached ? SheetTheme.Ember : SheetTheme.Timber, 10f));
            }

            if (note == null) return;
            note.Clear();
            note.Add(SheetKit.Text($"{wall.Length:0.#} m · {Mathf.CeilToInt(wall.Hp)} of "
                + $"{Mathf.CeilToInt(wall.MaxHp)} left", false, true, 12f));
            note.Add(SheetKit.Note(Story()));

            if (gateBtn != null)
                gateBtn.SetEnabled(!wall.Breached && QueuedGate() == null);
        }

        string Story()
        {
            if (wall.Breached)
                return "Broken through. The hands will stock and rebuild it like "
                    + "any other site.";
            if (QueuedGate() != null)
                return "A gate is on order here. The wall stands until it is built.";
            if (wall.IsGate)
                return "Your people walk through it. Raiders do not — they break the "
                    + "wall instead, or find the way round.";
            return "Raiders break a length of wall to get in. A gate in the right "
                + "place is what keeps them walking.";
        }

        PendingBuild QueuedGate()
        {
            var l = outpost != null ? outpost.Ledger : null;
            if (l == null || l.sites == null || wall == null) return null;
            foreach (var row in l.sites)
                if (row != null && row.isWall && row.planId == BuildPlans.Gate.id
                    && (row.postA - wall.A).sqrMagnitude < 0.05f
                    && (row.postB - wall.B).sqrMagnitude < 0.05f) return row;
            return null;
        }

        void MakeGate()
        {
            if (outpost == null || wall == null) return;
            outpost.MakeGate(wall);
            Refresh();
        }

        /// Refunds nothing -- the logs went into the ground -- and reopens
        /// the cells it was blocking, which `WallSegment.TearDown` does as
        /// part of taking itself off the map.
        void TearDown()
        {
            if (wall == null) return;
            wall.TearDown();
            Sheets.Close();
        }
    }
}
