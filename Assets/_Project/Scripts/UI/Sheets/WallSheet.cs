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
    /// one state (whole or breached) and one decision on top of that --
    /// a gate opens and shuts by itself (D3), nobody is posted to a wall,
    /// and since 2026-09-26 a repair is no longer automatic (Kevin: "if I
    /// press on it I should have the option to repair it"), so this sheet
    /// is where that press lands. Anything more on it would be inventing a
    /// decision the design does not have.
    public class WallSheet : ISheetFramed
    {
        // One thing to look at, so no tab strip -- `SiteSheet`'s rule.
        public string[] TabLabels => null;
        public int Tab => 0;
        public void SetTab(int index) { }
        public Color Accent => SheetTheme.Timber;

        readonly Outpost outpost;
        readonly WallSegment wall;

        WatchTiles.Head head;
        Label hpV, lenV, kindV;
        CardKit.Bar bar;
        CardKit.Now now;
        Button mainBtn;
        /// "Raise to level 2" (2026-10-03): the stations' own upgrade card,
        /// fed this segment's length-priced step (`Outpost.WallUpgradeOf`).
        StationPage.UpgradeCard upgrade;

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

        /// **Midnight card, 2026-09-27** (audit #10): wall glyph, island,
        /// a standing / breached pill; Strength / Length / Kind chips over a
        /// strength bar; the story card; the thumb row -- Tear down (tap
        /// twice) · the one verb this wall has right now (Make a gate,
        /// Repair, or Close when neither applies).
        public VisualElement BuildHeader()
        {
            head = CardKit.Head("wall", StationPage.Cap(Title));
            head.SetSub(StationPage.Cap(StationPage.IslandName(outpost)));
            return head.Root;
        }

        public VisualElement BuildActions() => null;

        public VisualElement Build()
        {
            var root = CardKit.Page(out var col);
            var chips = CardKit.Chips(col);
            hpV = WatchTiles.Chip(chips, "STRENGTH", true);
            lenV = WatchTiles.Chip(chips, "LENGTH", false);
            kindV = WatchTiles.Chip(chips, "KIND", false);
            bar = new CardKit.Bar(col);
            now = new CardKit.Now(col, CardKit.GlyphIcon("wall"));
            upgrade = new StationPage.UpgradeCard(DoUpgrade, null, outpost);
            upgrade.Root.style.marginTop = 12;
            col.Add(upgrade.Root);

            var acts = CardKit.Acts(root);
            new CardKit.Confirm(acts, "Tear down", "Tap again · logs lost", TearDown);
            mainBtn = CardKit.Act(acts, "", Main, 1);
            Refresh();
            return root;
        }

        public void Refresh()
        {
            if (wall == null) return;
            bool breached = wall.Breached;
            var repair = QueuedRepair();
            var gate = QueuedGate();
            if (head != null)
            {
                if (breached) head.SetPill(repair != null ? "Repair ordered" : "Breached",
                    repair != null ? StationPage.PillWait : StationPage.PillBad);
                else if (gate != null) head.SetPill("Gate ordered", StationPage.PillWait);
                else head.SetPill("standing", StationPage.PillGood);
            }
            if (hpV == null) return;

            float fill = wall.MaxHp > 0f ? Mathf.Clamp01(wall.Hp / wall.MaxHp) : 0f;
            WatchTiles.Set(hpV, $"{Mathf.RoundToInt(fill * 100f)}%");
            WatchTiles.Tone(hpV, breached ? 2 : fill < 0.5f ? 1 : 0);
            WatchTiles.Set(lenV, $"{wall.Length:0.#} m");
            WatchTiles.Set(kindV, wall.Level >= 2
                ? (wall.IsGate ? "Gate II" : "Wall II")
                : (wall.IsGate ? "Gate" : "Palisade"));
            bar.Set(fill, breached ? CardKit.Ember : fill < 0.5f ? CardKit.Amber : (Color?)null);

            now.Set(breached ? "Broken through" : wall.IsGate ? "Your people walk through" : "Raiders break it to get in",
                Story());
            now.Tone(breached ? 2 : gate != null || repair != null ? 1 : -1);

            string text;
            bool enabled = true, pri = true;
            if (breached) { text = repair != null ? "Repair ordered" : "Repair"; enabled = repair == null; }
            else if (!wall.IsGate) { text = gate != null ? "Gate ordered" : "Make a gate"; enabled = gate == null; }
            else { text = "Close"; pri = false; }
            if (mainBtn.text != text) mainBtn.text = text;
            mainBtn.SetEnabled(enabled);
            CardKit.Primary(mainBtn, pri);
            RefreshUpgrade(breached, repair != null || gate != null);
        }

        /// The upgrade card shows on a standing wall with no work ordered on
        /// it (a breach is repaired first, a gate built first) -- one
        /// decision at a time, and the sheet stays short.
        void RefreshUpgrade(bool breached, bool workOrdered)
        {
            if (upgrade == null || outpost == null) return;
            var l = outpost.Ledger;
            bool show = l != null && !breached && !workOrdered;
            upgrade.Root.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;
            var next = outpost.WallUpgradeOf(wall);
            bool can = outpost.CanUpgradeWall(wall, out string why);
            upgrade.Show(l, next, wall.Level, can, why);
        }

        void DoUpgrade()
        {
            if (outpost == null || wall == null) return;
            if (outpost.UpgradeWall(wall)) Refresh();
        }

        void Main()
        {
            if (wall == null) return;
            if (wall.Breached) Repair();
            else if (!wall.IsGate) MakeGate();
            else Sheets.Close();
        }

        string Story()
        {
            if (wall.Breached)
                return QueuedRepair() != null
                    ? "Repair ordered. The hands will stock and rebuild the gap."
                    : "Broken through. Order a repair and the hands will rebuild "
                        + "the gap.";
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

        /// A repair queued on this segment's own posts, or null. Mirrors
        /// `Outpost.QueueRepair`'s own dedup check -- a breached segment
        /// standing here can only have a repair row on these posts, never
        /// a fresh site (the ground already refuses one on top of it).
        PendingBuild QueuedRepair()
        {
            var l = outpost != null ? outpost.Ledger : null;
            if (l == null || l.sites == null || wall == null) return null;
            foreach (var row in l.sites)
                if (row != null && row.isWall
                    && (row.postA - wall.A).sqrMagnitude < 0.05f
                    && (row.postB - wall.B).sqrMagnitude < 0.05f) return row;
            return null;
        }

        /// **The button Kevin asked for (2026-09-26).** "If I press on it
        /// I should have the option to repair it." Queues the partial site
        /// on THIS segment's posts -- `Outpost.QueueRepair` dedups, so a
        /// second press before the sheet refreshes is harmless.
        void Repair()
        {
            if (outpost == null || wall == null) return;
            outpost.QueueRepair(wall);
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
