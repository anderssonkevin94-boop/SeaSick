using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **A reusable tile grid, built once for Stores (2026-09-26), meant for
    /// the ship-loading manifest and anywhere else a sheet shows "some of
    /// these, some of those, tap one" (see `Reuse.dc.html` in the mockup
    /// handoff).**
    ///
    /// One tile per id, square, icon at ~58% with the count bottom-right --
    /// zero tiles dim, 1-3 in ember, the selected tile ringed. A tile is a
    /// `Button` built the first time its id is ever shown and never rebuilt
    /// after that (the project's "buttons built once, re-texted on the
    /// 0.25 s refresh" rule -- a rebuilt element drops the tap it was in
    /// the middle of): `Layout` moves existing tiles into a new row order
    /// when the VISIBLE SET changes (a category or scope switch), and
    /// `SetCell` only re-texts and re-classes an already-placed tile, which
    /// is all a plain refresh tick ever needs to do.
    public sealed class ItemGrid : VisualElement
    {
        readonly int columns;
        readonly Action<string> onTap;
        readonly ScrollView scroll;
        readonly VisualElement rows;
        readonly VisualElement emptyNote;

        readonly Dictionary<string, Button> tiles = new Dictionary<string, Button>();
        readonly Dictionary<string, Image> icons = new Dictionary<string, Image>();
        readonly Dictionary<string, Label> qtyLabels = new Dictionary<string, Label>();

        List<string> layoutIds = new List<string>();

        /// **Scrolls, unlike the rest of this HUD.** Every other sheet in
        /// this game trades scrolling for swipeable pages (Kevin, 2026-09-22:
        /// *"you need to scroll down to see all the options and that's a
        /// huge no no"*) -- but that ruling was about a page of BUTTONS you
        /// act on one at a time. The Stores bank is a browse-everything grid
        /// (the mockup's own `overflow-y: auto`), the tap target is the
        /// whole tile rather than a strip of text, and paginating it would
        /// mean guessing how many of twenty-odd resources fit a phone width
        /// and hiding the rest behind "and 12 more" -- exactly the miss the
        /// task called out ("All tab = all resources"). A `ScrollView`
        /// confined to this element's own box, with the header/scope/tabs
        /// and the detail card all OUTSIDE it, keeps the one no-scroll rule
        /// that actually matters: nothing the player needs to reach a
        /// standing decision through is ever below the fold.
        public ItemGrid(int columns, Action<string> onTap)
        {
            this.columns = Mathf.Max(1, columns);
            this.onTap = onTap;
            AddToClassList("stores-grid-wrap");
            style.flexDirection = FlexDirection.Column;
            style.overflow = Overflow.Hidden;

            scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            scroll.style.flexShrink = 1f;
            // Touch/drag scrolling stays (ScrollView handles that itself);
            // only the desktop-style scrollbar-with-arrow-buttons chrome is
            // hidden -- the mockup's own `overflow-y: auto` never drew one.
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            Add(scroll);

            rows = new VisualElement();
            rows.AddToClassList("stores-grid");
            scroll.Add(rows);

            emptyNote = SheetKit.Note("Nothing here yet.");
            emptyNote.style.display = DisplayStyle.None;
            Add(emptyNote);

            // **Square tiles without CSS `aspect-ratio`.** A row's tiles
            // share its width by `flex-grow`; this reads that width back
            // after every layout pass (a rotate, a phone-vs-desk switch,
            // the safe area changing) and sets an explicit height to match,
            // so a tile is a square on either shape rather than whatever
            // its row happens to be tall.
            rows.RegisterCallback<GeometryChangedEvent>(_ => SquareTiles());
        }

        const float RowGapPx = 6f;

        void SquareTiles()
        {
            foreach (var row in rows.Children())
            {
                int n = row.childCount;
                float w = row.resolvedStyle.width;
                if (n == 0 || w <= 1f) continue;
                float side = Mathf.Max(20f, (w - RowGapPx * (n - 1)) / n);
                for (int i = 0; i < n; i++) row[i].style.height = side;
            }
        }

        Button EnsureTile(string id)
        {
            if (tiles.TryGetValue(id, out var existing)) return existing;
            var tile = new Button(() => onTap?.Invoke(id));
            tile.AddToClassList("stores-tile");

            var icon = new Image();
            icon.AddToClassList("stores-tile-icon");
            icon.image = ItemIconSet.Get(id);
            icon.pickingMode = PickingMode.Ignore;
            tile.Add(icon);

            var qty = new Label();
            qty.AddToClassList("stores-qty");
            qty.pickingMode = PickingMode.Ignore;
            tile.Add(qty);

            tiles[id] = tile;
            icons[id] = icon;
            qtyLabels[id] = qty;
            return tile;
        }

        /// **The VISIBLE set and its order** -- called only when either
        /// changes (a category tab, a scope, a sort), never on a plain
        /// refresh tick. Existing tiles are moved into fresh row wrappers
        /// (row wrappers themselves are cheap and carry no state, so they
        /// are rebuilt every time); a tile this grid has never shown before
        /// is built here, once, for the rest of the sheet's life.
        public void Layout(List<string> idsInOrder)
        {
            layoutIds = idsInOrder;
            rows.Clear();
            emptyNote.style.display = idsInOrder.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            scroll.style.display = idsInOrder.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;

            VisualElement row = null;
            for (int i = 0; i < idsInOrder.Count; i++)
            {
                if (i % columns == 0)
                {
                    row = new VisualElement();
                    row.AddToClassList("stores-grid-row");
                    rows.Add(row);
                }
                row.Add(EnsureTile(idsInOrder[i]));
            }
            SquareTiles();
        }

        /// Re-text and re-class one already-laid-out tile. A no-op for an
        /// id this grid is not currently showing (a stale call from a
        /// refresh that raced a category switch).
        public void SetCell(string id, int qty, bool future, bool selected)
        {
            if (!tiles.TryGetValue(id, out var tile)) return;
            bool dim = future || qty <= 0;
            bool low = !dim && qty <= 3;
            tile.EnableInClassList("stores-tile--dim", dim);
            tile.EnableInClassList("stores-tile--on", selected);
            icons[id].EnableInClassList("stores-tile-icon--dim", dim);
            var q = qtyLabels[id];
            q.text = future ? "—" : qty.ToString();
            q.EnableInClassList("stores-qty--dim", dim);
            q.EnableInClassList("stores-qty--low", low);
            tile.tooltip = id + ", " + (future ? "not in the game yet" : qty.ToString());
        }

        public bool Showing(string id) => tiles.ContainsKey(id) && layoutIds.Contains(id);
    }
}
