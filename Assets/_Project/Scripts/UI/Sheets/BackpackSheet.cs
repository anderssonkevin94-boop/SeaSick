using System.Collections.Generic;
using SeaSick.Voyage;
using SeaSick.World;
using SeaSick.World.Economy;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// <summary>
    /// **The backpack, 2026-09-29** -- the first chip on the land top bar.
    /// Kevin, iPhone: *"a backpack emblem- shows me what i have in storage on
    /// the island, then a seperate section under it there should be what i
    /// have on the ship. a plus or minus under each item / resource on each
    /// does the equivelent on the other."*
    ///
    /// Two sections, each a 3-to-a-row grid of tiles (icon, count, name, a
    /// pending line, and a -/+ pair of thumb-size buttons). **Both list the
    /// same items in the same order** (the union of store, hold and pending
    /// orders, by category then name), so the grids line up and a kind one
    /// side has none of still shows there as a dimmed 0 tile whose + pulls
    /// from the other side:
    /// <list type="bullet">
    /// <item>**On the island** -- the camp STORE (`StoreCountOf`; station
    ///   bays and racks are not the store and cannot be carried off).</item>
    /// <item>**On the ship** -- her hold (`VoyageManager.HeldOf`), with
    ///   "held / capacity" in the section head.</item>
    /// </list>
    /// **+ on a ship tile = one more unit island -> ship; - = ship -> island.
    /// On an island tile the other way round.** A press never moves a unit:
    /// goods count only when a hand carries them (the "deliveries on arrival"
    /// rule), so a press edits the standing carry order
    /// (`OutpostLedger.OrderTransfer`, one per resource and direction). A
    /// press against an order already standing the OTHER way takes one off
    /// that order first (and cancels it at zero) before any reverse order
    /// starts -- so "+ then -" is always a no-op, never two walks.
    ///
    /// Orders only start while she lies alongside (`ShipHere`); otherwise
    /// every button is disabled and one line says why. + is disabled when
    /// the hold has no unpromised room (her `Room` less armfuls walking and
    /// units already ordered aboard) or the store has no room for that kind
    /// (`RoomFor`, less units already ordered ashore). Built once; the 0.25 s
    /// refresh only re-texts, re-enables, and re-lays out when the set of
    /// items shown changes (DEV-TOOLS: "Buttons are built once and re-texted
    /// on the 0.25 s refresh").
    /// </summary>
    public sealed class BackpackSheet : ISheetFramed
    {
        readonly Outpost camp;

        public BackpackSheet(Outpost o) { camp = o; }

        // --- ISheet / ISheetFramed -------------------------------------------

        public string Title => "Backpack";
        public Vector3 AnchorWorld => camp != null ? camp.CampCentre : Vector3.zero;
        public bool StillValid => camp != null && camp.Ledger != null && (camp.HasCamp || camp.Building);
        public Color Accent => MidnightLandHud.Ice;
        public string[] TabLabels => null;
        public int Tab => 0;
        public void SetTab(int index) { }
        public bool WantsTallSheet => true;
        public VisualElement BuildActions() => null;

        OutpostLedger L => camp != null ? camp.Ledger : null;

        // --- built once ----------------------------------------------------------

        sealed class Tile
        {
            public VisualElement root;
            public Label count, pending;
            public Button minus, plus;
        }

        Label sub;
        Label awayLine, orderLine;
        Label islandMeta, shipMeta, islandEmpty, shipEmpty;
        VisualElement islandGrid, shipGrid;
        readonly Dictionary<string, Tile> islandTiles = new Dictionary<string, Tile>();
        readonly Dictionary<string, Tile> shipTiles = new Dictionary<string, Tile>();
        readonly List<string> islandShown = new List<string>();
        readonly List<string> shipShown = new List<string>();
        readonly List<string> scratch = new List<string>();
        long orderKey = long.MinValue;

        public VisualElement BuildHeader()
        {
            var glyph = new HudGlyph(HudGlyph.Kind.Backpack);
            glyph.style.width = 22f; glyph.style.height = 22f;
            return CampPages.IconHeader(Title, glyph, out sub);
        }

        public VisualElement Build()
        {
            islandTiles.Clear(); shipTiles.Clear();
            islandShown.Clear(); shipShown.Clear();
            orderKey = long.MinValue;

            var root = new VisualElement();
            root.AddToClassList("pack-body");

            awayLine = new Label("Bring her alongside the pier to move goods");
            awayLine.AddToClassList("pack-away");
            root.Add(awayLine);
            orderLine = new Label();
            orderLine.AddToClassList("pack-empty");
            orderLine.style.whiteSpace = WhiteSpace.Normal;
            root.Add(orderLine);

            // Scrolls only when both lists outgrow the tall card; tiles are
            // three to a row to keep that rare.
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("pack-scroll");
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            root.Add(scroll);

            scroll.Add(SectionHead("On the island", out islandMeta));
            islandEmpty = new Label("Nothing in the store or the hold.");
            islandEmpty.AddToClassList("pack-empty");
            scroll.Add(islandEmpty);
            islandGrid = new VisualElement();
            islandGrid.AddToClassList("pack-grid");
            scroll.Add(islandGrid);

            scroll.Add(SectionHead("On the ship", out shipMeta));
            shipEmpty = new Label("Nothing in the store or the hold.");
            shipEmpty.AddToClassList("pack-empty");
            scroll.Add(shipEmpty);
            shipGrid = new VisualElement();
            shipGrid.AddToClassList("pack-grid");
            scroll.Add(shipGrid);

            Refresh();
            return root;
        }

        static VisualElement SectionHead(string title, out Label meta)
        {
            var head = new VisualElement();
            head.AddToClassList("pack-section-head");
            var t = new Label(title);
            t.AddToClassList("pack-section-title");
            head.Add(t);
            meta = new Label();
            meta.AddToClassList("pack-section-meta");
            head.Add(meta);
            return head;
        }

        /// One tile, built the first time its id is shown on that side and
        /// kept for the sheet's life. `onShip` picks which way each button
        /// carries (see the class summary).
        Tile MakeTile(string res, bool onShip)
        {
            var t = new Tile();
            t.root = new VisualElement();
            t.root.AddToClassList("pack-tile");

            var top = new VisualElement { pickingMode = PickingMode.Ignore };
            top.AddToClassList("pack-tile-top");
            var tex = ItemIconSet.Get(res);
            if (tex != null)
            {
                var icon = new Image { image = tex, pickingMode = PickingMode.Ignore };
                icon.AddToClassList("pack-tile-icon");
                top.Add(icon);
            }
            t.count = new Label { pickingMode = PickingMode.Ignore };
            t.count.AddToClassList("pack-tile-count");
            top.Add(t.count);
            t.root.Add(top);

            var name = new Label(StationPage.Cap(ResDefs.Label(res))) { pickingMode = PickingMode.Ignore };
            name.AddToClassList("pack-tile-name");
            t.root.Add(name);

            t.pending = new Label { pickingMode = PickingMode.Ignore };
            t.pending.AddToClassList("pack-tile-pending");
            t.root.Add(t.pending);

            var btns = new VisualElement();
            btns.AddToClassList("pack-tile-btns");
            // Ship tile: + pulls from the island, - sends back ashore.
            // Island tile: + pulls from the ship, - sends aboard.
            t.minus = SheetKit.Btn("−", () => Press(res, toShip: !onShip));
            t.minus.AddToClassList("pack-btn");
            t.minus.AddToClassList("pack-btn--minus");
            t.plus = SheetKit.Btn("+", () => Press(res, toShip: onShip));
            t.plus.AddToClassList("pack-btn");
            btns.Add(t.minus);
            btns.Add(t.plus);
            t.root.Add(btns);
            return t;
        }

        // --- the verbs -------------------------------------------------------------

        /// **One unit `toShip`'s way.** Takes one off an order standing the
        /// other way first; otherwise grows (or starts) this way's order by
        /// one, if there is something left to send and room to receive it.
        void Press(string res, bool toShip)
        {
            var l = L;
            if (l == null || !l.ShipHere) return;
            int against = l.TransferLeft(res, !toShip);
            if (against > 0)
            {
                if (against == OutpostLedger.TransferAll || against <= 1) l.CancelTransfer(res, !toShip);
                else l.OrderTransfer(res, against - 1, !toShip);
            }
            else if (CanGrow(l, res, toShip))
            {
                l.OrderTransfer(res, l.TransferLeft(res, toShip) + 1, toShip);
            }
            Refresh();
        }

        /// Is there one more unit to send `toShip`'s way, and room for it?
        bool CanGrow(OutpostLedger l, string res, bool toShip)
        {
            int left = l.TransferLeft(res, toShip);
            if (left == OutpostLedger.TransferAll) return false;
            if (toShip) return IslandCount(l, res) - left > 0 && HoldRoomUnpromised(l) > 0;
            return ShipCount(l, res) - left > 0 && l.RoomFor(res) - left > 0;
        }

        bool CanPress(OutpostLedger l, string res, bool toShip) =>
            l.ShipHere && (l.TransferLeft(res, !toShip) > 0 || CanGrow(l, res, toShip));

        /// The hold's room less armfuls already walking to her and units
        /// still ordered aboard (every kind shares one hold).
        static int HoldRoomUnpromised(OutpostLedger l)
        {
            int room = l.cargo != null ? l.cargo.Room : CampLoading.RoomAboard(SheetBits.Voyage);
            room -= l.CarryingTransfer(null, true);
            if (l.transfers != null)
                foreach (var o in l.transfers)
                    if (o != null && o.toShip && o.left > 0 && o.left != OutpostLedger.TransferAll) room -= o.left;
            return room;
        }

        static int IslandCount(OutpostLedger l, string res) => l.StoreCountOf(res);

        static int ShipCount(OutpostLedger l, string res)
        {
            var v = SheetBits.Voyage;
            if (v != null) return v.HeldOf(res);
            return l.cargo != null ? l.cargo.HeldOf(res) : 0;
        }

        // --- refresh -----------------------------------------------------------------

        public void Refresh()
        {
            var l = L;
            if (l == null || islandGrid == null) return;
            bool here = l.ShipHere;
            var v = SheetBits.Voyage;

            if (sub != null) sub.text = camp.Island != null ? camp.Island.name : "the camp";
            awayLine.style.display = here ? DisplayStyle.None : DisplayStyle.Flex;

            long key = l.TransferKey();
            if (key != orderKey)
            {
                orderKey = key;
                string summary = l.TransferSummary();
                orderLine.text = summary.Length > 0 ? "Carrying: " + summary : "";
                orderLine.style.display = summary.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }

            islandMeta.text = "holds " + l.ceilingPer + " of each";
            shipMeta.text = v != null ? v.TotalHeld + " / " + v.HoldCapacity : "";

            // **Both sections list the SAME set in the SAME order** (the
            // union: anything in the store or the hold, or with an order or
            // an armful pending), so the two grids line up and "+ under food
            // on the boat" works while the boat has none. A side's 0 tile is
            // dimmed.
            scratch.Clear();
            foreach (var id in AllIds(l, v))
            {
                if (IslandCount(l, id) > 0 || ShipCount(l, id) > 0
                    || l.TransferPending(id, true) || l.TransferPending(id, false))
                    scratch.Add(id);
            }
            scratch.Sort(ByCategoryThenName);
            Relayout(islandGrid, islandTiles, islandShown, scratch, false);
            Relayout(shipGrid, shipTiles, shipShown, scratch, true);
            islandEmpty.style.display = islandShown.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            // One "nothing" line is enough: the sets are the same.
            shipEmpty.style.display = DisplayStyle.None;

            foreach (var id in islandShown) Fill(l, islandTiles[id], id, false);
            foreach (var id in shipShown) Fill(l, shipTiles[id], id, true);
        }

        /// Category (the `ResCategory` order: raw, material, food, ...), then
        /// the player-facing name. Stable, so a tick never shuffles tiles.
        static int ByCategoryThenName(string a, string b)
        {
            int c = ((int)ResDefs.Category(a)).CompareTo((int)ResDefs.Category(b));
            if (c != 0) return c;
            c = string.CompareOrdinal(ResDefs.Label(a), ResDefs.Label(b));
            return c != 0 ? c : string.CompareOrdinal(a, b);
        }

        /// Every id either side might hold, in the resource table's order,
        /// then anything off the table that the store or hold still carries.
        readonly List<string> ids = new List<string>();
        List<string> AllIds(OutpostLedger l, VoyageManager v)
        {
            ids.Clear();
            foreach (var d in ResDefs.All) ids.Add(d.id);
            if (l.stores != null)
                foreach (var s in l.stores)
                    if (s != null && !string.IsNullOrEmpty(s.resource) && !ids.Contains(s.resource)) ids.Add(s.resource);
            if (v != null)
                foreach (var kv in v.HeldStores)
                    if (!string.IsNullOrEmpty(kv.Key) && !ids.Contains(kv.Key)) ids.Add(kv.Key);
            return ids;
        }

        /// Re-parent tiles only when the shown SET or its order changed; a
        /// plain tick leaves the tree alone.
        void Relayout(VisualElement grid, Dictionary<string, Tile> tiles, List<string> shown,
                      List<string> want, bool onShip)
        {
            bool same = shown.Count == want.Count;
            for (int i = 0; same && i < want.Count; i++) same = shown[i] == want[i];
            if (same) return;
            shown.Clear(); shown.AddRange(want);
            grid.Clear();
            for (int i = 0; i < shown.Count; i++)
            {
                if (!tiles.TryGetValue(shown[i], out var t)) tiles[shown[i]] = t = MakeTile(shown[i], onShip);
                t.root.EnableInClassList("pack-tile--end", i % 3 == 2);
                grid.Add(t.root);
            }
        }

        void Fill(OutpostLedger l, Tile t, string res, bool onShip)
        {
            int have = onShip ? ShipCount(l, res) : IslandCount(l, res);
            t.count.text = MidnightLandHud.CompactCount(have);
            t.root.EnableInClassList("pack-tile--dim", have <= 0);

            // Coming here: the order the other side is sending plus armfuls
            // already walking this way. Leaving: this side's standing order.
            int toShipLeft = l.TransferLeft(res, true), ashoreLeft = l.TransferLeft(res, false);
            int walkingAboard = l.CarryingTransfer(res, true), walkingAshore = l.CarryingTransfer(res, false);
            int inOrder = onShip ? toShipLeft : ashoreLeft;
            int inWalking = onShip ? walkingAboard : walkingAshore;
            int outOrder = onShip ? ashoreLeft : toShipLeft;
            string pend = "";
            if (inOrder > 0 || inWalking > 0)
                pend = "+" + (inOrder == OutpostLedger.TransferAll ? "all" : (inOrder + inWalking).ToString());
            if (outOrder > 0)
                pend += (pend.Length > 0 ? " " : "") + "−" + (outOrder == OutpostLedger.TransferAll ? "all" : outOrder.ToString());
            t.pending.text = pend;

            string stall = l.TransferStall(res, true) ?? l.TransferStall(res, false);
            t.pending.tooltip = stall ?? "";

            // Ship tile: + = aboard, - = ashore. Island tile: the reverse.
            bool plusToShip = onShip;
            t.plus.SetEnabled(CanPress(l, res, plusToShip));
            t.minus.SetEnabled(CanPress(l, res, !plusToShip));
            t.plus.tooltip = plusToShip ? "Carry one more aboard" : "Carry one more ashore";
            t.minus.tooltip = plusToShip ? "Carry one more ashore" : "Carry one more aboard";
        }
    }
}
