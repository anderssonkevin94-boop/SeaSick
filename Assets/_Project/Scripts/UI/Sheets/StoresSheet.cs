using System.Collections.Generic;
using SeaSick.World;
using SeaSick.World.Economy;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **The Melvor-style bank, 2026-09-26.** Kevin approved a mockup on the
    /// phone and asked for exactly that: every resource the camp or the ship
    /// knows about, in one 4-column grid, filtered by scope (camp / ship
    /// hold / everywhere) and by category, sorted by count or by name, with
    /// a tap opening a detail card at the foot of the sheet. It replaces
    /// what the bottom nav's "Stores" button used to open (the camp tab of
    /// `FireSheet`) -- that page is not gone, it is one tap on the campfire
    /// itself (`SheetBootstrap.FireFor`), the same way it always was.
    ///
    /// **It reads and it never decides**, same rule `FireSheet` follows:
    /// every count comes off `OutpostLedger` (camp) or `ICargoSide` (ship,
    /// through the ledger's own `cargo` field -- never a direct reference to
    /// `VoyageManager`, which stays systems-owned). The grid itself is
    /// `ItemGrid`, built to be reused wherever else the game shows "some of
    /// these, tap one" (the ship-loading manifest, per `Reuse.dc.html`).
    public sealed class StoresSheet : ISheetFramed
    {
        enum Scope { Camp, Ship, Everywhere }
        enum SortMode { Count, Name }

        struct TabDef { public string label; public string iconId; public ResCategory cat; }

        // Icon keys for Raw/Material/Food/Gear are the SAME `Res.*` ids the
        // item tiles use (one texture, one key, `timber.png` reached both
        // ways) -- Armor and Ship have no resource behind them yet, so
        // their icons are keyed by the bare file name instead.
        static readonly TabDef[] Tabs =
        {
            new TabDef { label = "All" },
            new TabDef { label = "Raw", iconId = Res.Timber, cat = ResCategory.Raw },
            new TabDef { label = "Material", iconId = Res.Boards, cat = ResCategory.Material },
            new TabDef { label = "Food", iconId = Res.Food, cat = ResCategory.Food },
            new TabDef { label = "Gear", iconId = Res.Spear, cat = ResCategory.Gear },
            new TabDef { label = "Armor", iconId = "armor", cat = ResCategory.Armor },
            new TabDef { label = "Ship", iconId = "cannon", cat = ResCategory.Ship },
        };

        static readonly string[] ScopeLabels = { "Camp", "Ship hold", "Everywhere" };

        readonly Outpost outpost;
        readonly string islandName;

        int tabIndex;
        Scope scope = Scope.Camp;
        SortMode sort = SortMode.Count;
        string selectedId;

        public StoresSheet(Outpost o)
        {
            outpost = o;
            islandName = o != null && o.Island != null ? o.Island.name : "the camp";
        }

        // --- ISheet / ISheetFramed -------------------------------------------

        public string Title => "Stores";
        public Vector3 AnchorWorld => outpost != null ? outpost.CampCentre : Vector3.zero;

        public bool StillValid =>
            outpost != null && outpost.Ledger != null && (outpost.HasCamp || outpost.Building);

        public Color Accent => MidnightLandHud.Ice;

        // One section: no host tab strip. The category strip and the scope
        // control below are this sheet's own content, not the frame's.
        public string[] TabLabels => null;
        public int Tab { get { return 0; } }
        public void SetTab(int index) { }

        // Kevin's mockup verdict, 2026-09-26: the bank fills the screen
        // between the resource bar and the bottom nav (several rows of
        // tiles plus the detail card), not the standard one-row band.
        public bool WantsTallSheet => true;

        OutpostLedger L => outpost != null ? outpost.Ledger : null;

        // --- chrome kept between refreshes -----------------------------------

        Label subtitleLabel;
        Button sortBtn;
        VisualElement scopeSeg;
        VisualElement tabsRow;
        Button[] tabButtons;
        ItemGrid grid;
        VisualElement detailHolder;
        Label detailSplit;
        VisualElement rootEl;
        long visibleKey = long.MinValue;

        public VisualElement BuildHeader()
        {
            var head = new VisualElement();
            head.AddToClassList(SheetTheme.Head);

            var words = new VisualElement();
            words.style.flexDirection = FlexDirection.Column;
            words.style.flexGrow = 1f;
            words.style.flexShrink = 1f;
            words.style.minWidth = 0f;
            var title = new Label("Stores");
            title.AddToClassList("stores-title");
            words.Add(title);
            subtitleLabel = new Label();
            subtitleLabel.AddToClassList("stores-subtitle");
            words.Add(subtitleLabel);
            head.Add(words);

            sortBtn = SheetKit.Btn(InstanceSortLabel(), ToggleSort);
            sortBtn.AddToClassList("stores-sort-btn");
            head.Add(sortBtn);

            var x = new Button(() => Sheets.Close());
            x.AddToClassList(SheetTheme.Close);
            x.text = "";
            x.tooltip = "Close";
            x.style.alignItems = Align.Center;
            x.style.justifyContent = Justify.Center;
            x.Add(new LandIcon("close"));
            head.Add(x);

            return head;
        }

        public VisualElement BuildActions() => null;

        public VisualElement Build()
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Column;
            root.style.flexGrow = 1f;
            root.style.flexShrink = 1f;
            root.style.minHeight = 0f;

            scopeSeg = SheetKit.Segmented(ScopeLabels, (int)scope, i =>
            {
                scope = (Scope)i;
                SheetKit.SetSegmented(scopeSeg, i);
                visibleKey = long.MinValue;
            });
            root.Add(scopeSeg);

            tabsRow = BuildCategoryTabs();
            root.Add(tabsRow);

            grid = new ItemGrid(4, OnTileTap);
            grid.style.flexShrink = 0f;
            grid.style.marginTop = 6f;
            root.Add(grid);

            detailHolder = SheetBits.Holder();
            root.Add(detailHolder);

            rootEl = root;
            // **Measured, not formula-guessed.** `SheetHost.BandHeight` is
            // built from `Screen.width/height`, and this sheet has no
            // reliable way to know those agree with the actual card it is
            // laid out inside (a probe/eval session can leave the Game view
            // a different shape than the one the HUD is actually rendering
            // for -- exactly what happened building this sheet: `BandHeight`
            // came back clamped to its 80-unit floor while the real card was
            // 350+ units tall). `root.parent` (the host's `page`, fixed to
            // the true band) is the ground truth instead: once this is
            // attached, its own geometry and its siblings' settle, and
            // `SizeGrid` gives the grid everything the scope control, the
            // category strip and the detail card are not already using.
            root.RegisterCallback<GeometryChangedEvent>(_ => SizeGrid());
            detailHolder.RegisterCallback<GeometryChangedEvent>(_ => SizeGrid());
            visibleKey = long.MinValue;
            return root;
        }

        void SizeGrid()
        {
            if (grid == null || rootEl == null) return;
            var page = rootEl.parent;
            if (page == null) return;
            float total = page.resolvedStyle.height;
            if (total <= 1f) return;
            float used = scopeSeg.resolvedStyle.height + tabsRow.resolvedStyle.height
                         + detailHolder.resolvedStyle.height + grid.resolvedStyle.marginTop;
            float h = total - used - 6f; // small safety margin
            if (h <= 1f) return; // siblings not measured yet; a later pass fixes this
            grid.style.height = Mathf.Max(64f, h);
        }

        VisualElement BuildCategoryTabs()
        {
            var row = new VisualElement();
            row.AddToClassList("stores-tabs");
            tabButtons = new Button[Tabs.Length];
            for (int i = 0; i < Tabs.Length; i++)
            {
                int index = i;
                var b = new Button(() => { tabIndex = index; visibleKey = long.MinValue; });
                b.AddToClassList("stores-tab");
                b.tooltip = Tabs[i].label;
                if (i == 0)
                {
                    // "All" is the big glyph and nothing else -- the mockup
                    // shows it once, not once big and once small underneath
                    // (2026-09-26 fix: this branch used to fall through to
                    // the shared label below too).
                    var glyph = new Label("All");
                    glyph.AddToClassList("stores-tab-all-glyph");
                    glyph.pickingMode = PickingMode.Ignore;
                    b.Add(glyph);
                }
                else
                {
                    var icon = new Image();
                    icon.AddToClassList("stores-tab-icon");
                    icon.image = ItemIconSet.Get(Tabs[i].iconId);
                    icon.pickingMode = PickingMode.Ignore;
                    b.Add(icon);
                    var lab = new Label(Tabs[i].label);
                    lab.AddToClassList("stores-tab-label");
                    lab.pickingMode = PickingMode.Ignore;
                    b.Add(lab);
                }
                row.Add(b);
                tabButtons[i] = b;
            }
            return row;
        }

        void ToggleSort()
        {
            sort = sort == SortMode.Count ? SortMode.Name : SortMode.Count;
            sortBtn.text = InstanceSortLabel();
            visibleKey = long.MinValue;
        }

        string InstanceSortLabel() => sort == SortMode.Count ? "Sort: most" : "Sort: A–Z";

        void OnTileTap(string id)
        {
            selectedId = id;
            RefreshCells();
            RebuildDetail();
        }

        // --- the plan: which ids show, in which order ------------------------

        readonly List<string> visible = new List<string>();

        void PlanVisible(OutpostLedger l)
        {
            visible.Clear();
            if (tabIndex == 0)
                foreach (var d in ResDefs.All) visible.Add(d.id);
            else
                visible.AddRange(ResDefs.InCategory(Tabs[tabIndex].cat));

            if (sort == SortMode.Count)
                visible.Sort((a, b) =>
                {
                    int qa = QtyFor(l, a), qb = QtyFor(l, b);
                    int byQty = qb.CompareTo(qa);
                    return byQty != 0 ? byQty : string.CompareOrdinal(ResDefs.Label(a), ResDefs.Label(b));
                });
            else
                visible.Sort((a, b) => string.CompareOrdinal(ResDefs.Label(a), ResDefs.Label(b)));

            if (!string.IsNullOrEmpty(selectedId) && !visible.Contains(selectedId))
                selectedId = null;
            if (string.IsNullOrEmpty(selectedId) && visible.Count > 0)
                selectedId = visible[0];
            if (visible.Count == 0)
                selectedId = null;
        }

        int CampQty(OutpostLedger l, string id) => l != null ? l.CountOf(id) : 0;
        int ShipQty(OutpostLedger l, string id) => l != null && l.cargo != null ? l.cargo.HeldOf(id) : 0;

        int QtyFor(OutpostLedger l, string id) => scope switch
        {
            Scope.Camp => CampQty(l, id),
            Scope.Ship => ShipQty(l, id),
            _ => CampQty(l, id) + ShipQty(l, id),
        };

        /// Everything the ship holds, and how much she can hold, read
        /// through `ICargoSide` alone (`HeldOf` per kind, `Room` for what is
        /// left) -- never `VoyageManager` directly, which is systems-owned.
        void ShipTotals(OutpostLedger l, out int held, out int capacity)
        {
            held = 0;
            capacity = 0;
            if (l == null || l.cargo == null) return;
            foreach (var d in ResDefs.All) held += l.cargo.HeldOf(d.id);
            capacity = held + Mathf.Max(0, l.cargo.Room);
        }

        // --- refresh ----------------------------------------------------------

        public void Refresh()
        {
            var l = L;
            if (outpost == null || l == null) return;
            outpost.CatchUp();
            SizeGrid();

            long key = ((long)tabIndex * 7 + (int)scope) * 3 + (int)sort;
            key = key * 1000003L + l.Total * 31L;
            if (l.cargo != null) foreach (var d in ResDefs.All) key = key * 31L + l.cargo.HeldOf(d.id);
            if (key != visibleKey)
            {
                visibleKey = key;
                PlanVisible(l);
                grid.Layout(visible);
                for (int i = 0; i < tabButtons.Length; i++)
                    tabButtons[i].EnableInClassList("stores-tab--on", i == tabIndex);
                RebuildDetail();
            }

            RefreshCells();
            SubtitleLine(l);
            if (sortBtn != null) sortBtn.text = InstanceSortLabel();
            if (detailSplit != null && !string.IsNullOrEmpty(selectedId))
                detailSplit.text = $"Camp {CampQty(l, selectedId)}  ·  Ship {ShipQty(l, selectedId)}";
        }

        void RefreshCells()
        {
            var l = L;
            if (l == null || grid == null) return;
            foreach (var id in visible)
                grid.SetCell(id, QtyFor(l, id), false, id == selectedId);
        }

        void SubtitleLine(OutpostLedger l)
        {
            if (subtitleLabel == null) return;
            if (scope == Scope.Camp)
                subtitleLabel.text = islandName + " camp";
            else if (scope == Scope.Ship)
            {
                ShipTotals(l, out int held, out int cap);
                subtitleLabel.text = $"Ship hold · {held} of {cap} cells";
            }
            else
                subtitleLabel.text = "Camp + ship hold";
        }

        // --- the detail card ---------------------------------------------------

        void RebuildDetail()
        {
            if (detailHolder == null) return;
            if (string.IsNullOrEmpty(selectedId) || !ResDefs.TryGet(selectedId, out var def))
            {
                detailSplit = null;
                SheetBits.Swap(detailHolder, null);
                return;
            }

            var card = new VisualElement();
            card.AddToClassList("stores-detail");

            var iconWrap = new VisualElement();
            iconWrap.AddToClassList("stores-detail-icon-wrap");
            var icon = new Image();
            icon.AddToClassList("stores-detail-icon");
            icon.image = ItemIconSet.Get(def.id);
            iconWrap.Add(icon);
            card.Add(iconWrap);

            var col = new VisualElement();
            col.AddToClassList("stores-detail-col");

            var nameRow = new VisualElement();
            nameRow.AddToClassList("stores-detail-name-row");
            var name = new Label(def.label);
            name.AddToClassList("stores-detail-name");
            nameRow.Add(name);
            var kind = new Label(TierWord(def.tier) + " · " + SourceWord(def.source));
            kind.AddToClassList("stores-detail-kind");
            nameRow.Add(kind);
            col.Add(nameRow);

            var blurb = new Label(def.blurb ?? "");
            blurb.AddToClassList("stores-detail-blurb");
            col.Add(blurb);

            var l = L;
            detailSplit = new Label($"Camp {CampQty(l, def.id)}  ·  Ship {ShipQty(l, def.id)}");
            detailSplit.AddToClassList("stores-detail-split");
            col.Add(detailSplit);

            card.Add(col);
            SheetBits.Swap(detailHolder, card);
        }

        static string TierWord(ResTier t) => t switch
        {
            ResTier.Raw => "raw",
            ResTier.Treated => "treated",
            _ => "item",
        };

        static string SourceWord(ResSource s) => s switch
        {
            ResSource.Gathered => "gathered",
            ResSource.Hunted => "hunted",
            ResSource.Drop => "from hunts",
            _ => "made",
        };
    }
}
