using System.Collections.Generic;
using System.Text;
using SeaSick.World;
using SeaSick.World.Economy;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **The Campfire card (2026-09-27, replaces `FireSheet`).**
    ///
    /// The old camp mega-sheet's jobs all have homes now -- stores in
    /// Stores, rations and the kitchen in the Larder, priority and recruits
    /// in People, the watch in the Lookout card, the ship in the Manifest,
    /// building in Build -- so a tap on the fire gets only what is the
    /// FIRE's, in the Midnight `.st` look of `LookoutSheet`:
    ///
    /// 1. **header** -- flame glyph, "Level II hamlet · island", a pill: Can
    ///    raise (moss) / Short (amber) / Top level;
    /// 2. **chips** -- WARMTH (`OutpostLedger.WarmHutRadius`), WARM HUTS
    ///    (huts in the ring / huts), WARM BEDS (`WarmBedCapacity` / hands);
    /// 3. **Raise to N** -- the price as have/need item tiles, and what the
    ///    level opens (its plans and every `Techs.Caps` row that grows);
    ///    at the top, the caps the fire allows now;
    /// 4. **food** -- one wide tile, days of food, tap = `LarderSheet`;
    /// 5. **thumb row** -- Overview · Raise to N.
    ///
    /// Nothing about cooking lives here: the Kitchen is the only kitchen.
    /// A view only: the one verb is `OutpostLedger.RaiseCampfire`.
    public sealed class CampfireSheet : ISheetFramed
    {
        readonly Outpost outpost;

        public CampfireSheet(Outpost camp) { outpost = camp; }

        OutpostLedger L => outpost != null ? outpost.Ledger : null;

        public string Title => "Campfire";
        public Color Accent => SheetTheme.Ember;
        public bool WantsTallSheet => true;
        public string[] TabLabels => null;
        public int Tab => 0;
        public void SetTab(int index) { }
        public VisualElement BuildActions() => null;

        public bool StillValid =>
            outpost != null && outpost.Ledger != null && (outpost.HasCamp || outpost.Building);

        public Vector3 AnchorWorld => outpost != null ? outpost.CampCentre : Vector3.zero;

        // --- header -------------------------------------------------------------

        WatchTiles.Head head;

        public VisualElement BuildHeader()
        {
            head = CardKit.Head("fire", "Campfire");
            FillHeader(L);
            return head.Root;
        }

        void FillHeader(OutpostLedger l)
        {
            if (head == null || l == null) return;
            var cur = Techs.CampfireAt(l.CampfireLevel);
            string lvl = cur != null ? $"Level {RecipeGraph.Roman(cur.level)} {cur.name}" : $"Level {RecipeGraph.Roman(l.CampfireLevel)}";
            head.SetSub(lvl + " · " + StationPage.Cap(StationPage.IslandName(outpost)));
            if (l.NextCampfire == null) head.SetPill("Top level", StationPage.PillGood);
            else if (l.CanRaiseCampfire(out _)) head.SetPill("Can raise", StationPage.PillGood);
            else head.SetPill("Short", StationPage.PillWait);
        }

        // --- the page -------------------------------------------------------------

        Label warmthV, hutsV, bedsV, raiseEye;
        VisualElement costGrid;
        readonly List<CardKit.Tile> costTiles = new List<CardKit.Tile>();
        CardKit.Now opens;
        CardKit.Tile food;
        Button overviewBtn, raiseBtn;

        /// **The fix for a short raise (2026-09-30, island UI rule 2):** one
        /// button under the have/need tiles for the item the pile lacks most
        /// ("Gather Timber", "Make Boards"...). Built once in `Build`, shown,
        /// hidden and re-labelled by `FillRaise` on every refresh.
        ShortFix.Slot fixSlot;
        long costKey = long.MinValue;

        public VisualElement Build()
        {
            var root = CardKit.Page(out var col);
            costTiles.Clear();
            costKey = long.MinValue;

            var chips = CardKit.Chips(col);
            warmthV = WatchTiles.Chip(chips, "WARMTH", true);
            hutsV = WatchTiles.Chip(chips, "WARM HUTS", false);
            bedsV = WatchTiles.Chip(chips, "WARM BEDS", false);

            raiseEye = CardKit.Eye(col, "RAISE", "have / need");
            costGrid = CardKit.Grid(col);
            fixSlot = new ShortFix.Slot(() => { costKey = long.MinValue; Refresh(); });
            col.Add(fixSlot.button);
            opens = new CardKit.Now(col, CardKit.GlyphIcon("fire"));

            CardKit.Eye(col, "FOOD", "tap for the larder");
            var foodGrid = CardKit.Grid(col);
            food = new CardKit.Tile(_ => OpenLarder(), false);
            food.Root.AddToClassList("ck-tile--wide");
            food.SetItem(Res.Food);
            foodGrid.Add(food.Root);

            var acts = CardKit.Acts(root);
            overviewBtn = CardKit.Act(acts, "Overview", OpenOverview);
            raiseBtn = CardKit.Act(acts, "Raise", Raise, 1);

            Refresh();
            return root;
        }

        public void Refresh()
        {
            var l = L;
            if (l == null) return;
            FillHeader(l);
            if (warmthV == null) return;

            // Warmth: the ring, the huts in it, the beds they give.
            WatchTiles.Set(warmthV, $"{OutpostLedger.WarmHutRadius:0} m");
            int huts = l.CountBuilt(BuildPlans.Hut.id);
            int warm = l.WarmHutCount;
            WatchTiles.Set(hutsV, $"{warm} / {huts}");
            WatchTiles.Tone(hutsV, huts == 0 ? 1 : warm >= huts ? 0 : 1);
            int beds = l.WarmBedCapacity, hands = l.hands.Count;
            WatchTiles.Set(bedsV, $"{Mathf.Min(beds, hands)} / {hands}");
            WatchTiles.Tone(bedsV, hands == 0 || beds >= hands ? 0 : beds > 0 ? 1 : 2);

            FillRaise(l);

            // Food: one tile, days of it.
            float days = SheetBits.FoodDays(l);
            string big = hands == 0 ? "Nobody to feed" : days < 0f ? "They eat nothing" : $"{days:0.#} days of food";
            food.Set(big, $"{l.FoodFill():0} meals kept · {hands} to feed");
            food.Root.EnableInClassList("ck-tile--short", hands > 0 && days >= 0f && days < 1.5f);
        }

        void FillRaise(OutpostLedger l)
        {
            var next = l.NextCampfire;
            long key = l.CampfireLevel * 1000003L;
            if (next != null) foreach (var line in next.cost) key = key * 31 + l.SpendableOf(line.res);
            bool can = next != null && l.CanRaiseCampfire(out _);
            raiseBtn.SetEnabled(can);
            raiseBtn.text = next != null ? $"Raise to {RecipeGraph.Roman(next.level)}" : "Top level";
            CardKit.Primary(raiseBtn, next != null);

            // The item the raise is most short of, a gatherable first.
            if (fixSlot != null)
            {
                var most = new ShortFix.Most();
                if (next != null)
                    foreach (var line in next.cost)
                        most.Add(line.res, line.n - l.SpendableOf(line.res));
                fixSlot.Bind(outpost, most.Res);
            }
            if (key == costKey) return;
            costKey = key;

            costGrid.Clear();
            costTiles.Clear();
            int lvl = l.CampfireLevel;
            if (next == null)
            {
                WatchTiles.Set(raiseEye, "AS HIGH AS IT GOES");
                WatchTiles.Show(costGrid, false);
                opens.Set("The fire allows", CapsLine(lvl, lvl));
                opens.Tone(0);
                return;
            }

            WatchTiles.Set(raiseEye, $"RAISE TO {RecipeGraph.Roman(next.level)} {next.name.ToUpperInvariant()}");
            WatchTiles.Show(costGrid, true);
            for (int i = 0; i < next.cost.Length; i++)
            {
                var line = next.cost[i];
                int have = l.SpendableOf(line.res);
                var t = new CardKit.Tile(null, false).Col3(i);
                t.SetItem(line.res);
                t.Set(StationPage.Cap(ResDefs.Label(line.res)), $"{have} / {line.n}");
                t.Root.EnableInClassList("ck-tile--ok", have >= line.n);
                t.Root.EnableInClassList("ck-tile--short", have < line.n);
                t.Root.pickingMode = PickingMode.Ignore;
                costTiles.Add(t);
                costGrid.Add(t.Root);
            }

            var sb = new StringBuilder();
            if (next.unlocksPlans != null && next.unlocksPlans.Length > 0)
            {
                sb.Append("New: ");
                for (int i = 0; i < next.unlocksPlans.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(PlanLabel(next.unlocksPlans[i]));
                }
            }
            string caps = CapsLine(lvl, next.level);
            if (caps.Length > 0) { if (sb.Length > 0) sb.Append(" · "); sb.Append(caps); }
            opens.Set(StationPage.Cap(next.blurb ?? "Opens more"), sb.ToString());
            opens.Tone(-1);
        }

        /// "Huts 2→3 · Storage 1→2 ..." for every cap row that grows between
        /// the two levels, or (same level twice) the caps as they stand.
        static string CapsLine(int from, int to)
        {
            var sb = new StringBuilder();
            foreach (var c in Techs.Caps)
            {
                int a = Techs.MaxCopies(c.planId, from), b = Techs.MaxCopies(c.planId, to);
                if (from != to && b <= a) continue;
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append(PlanLabel(c.planId)).Append(' ');
                if (from == to) sb.Append(a); else sb.Append(a).Append('→').Append(b);
            }
            return sb.ToString();
        }

        static string PlanLabel(string planId)
        {
            var p = BuildPlans.Named(planId);
            return StationPage.Cap(!string.IsNullOrEmpty(p.label) ? p.label : planId);
        }

        // --- verbs ------------------------------------------------------------------

        void Raise()
        {
            var l = L;
            if (l == null) return;
            if (l.RaiseCampfire()) { costKey = long.MinValue; Refresh(); }
        }

        void OpenLarder()
        {
            if (outpost != null) Sheets.Open(new LarderSheet(outpost));
        }

        void OpenOverview()
        {
            if (outpost != null) Sheets.Open(new CampOverviewSheet(outpost));
        }
    }
}
