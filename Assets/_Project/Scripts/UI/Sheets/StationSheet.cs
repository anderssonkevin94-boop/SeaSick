using System.Collections.Generic;
using SeaSick.World;
using SeaSick.World.Economy;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **The crafting menu.**
    ///
    /// Kevin, 2026-09-23: *"make sure the ui makes clear what is needed. im
    /// thinking a crafting menu in the appropriate buildings."* Tap a raised
    /// station and this is what it has to say: what it can make (`Make`) and
    /// what raising it further would buy (`Upgrade`) -- both read straight
    /// out of the ledger's own tables (`SeaSick.World.Economy.Recipes` /
    /// `Techs`), so a new recipe or a re-priced upgrade shows up here without
    /// this file changing.
    ///
    /// **It reads and it calls; it never decides.** Every have/need number
    /// comes off `OutpostLedger`; the only arithmetic here is the rate
    /// note's multiplication, the same sum `OutpostLedger.Step` does per
    /// hand per day.
    ///
    /// A building with nothing to make and nowhere to go (a pier) has no
    /// sheet at all -- `SheetBootstrap` returns null for it rather than
    /// opening an empty card.
    public class StationSheet : ISheetFramed
    {
        const int TabMake = 0;
        const int TabUpgrade = 1;

        readonly Outpost outpost;
        readonly Building building;
        readonly string planId;
        readonly BuildPlan plan;
        readonly bool hasMake;
        readonly bool hasUpgrade;

        int tab = -1;

        /// **This built instance's index into `ledger.raised`** (and, in the
        /// same breath, into `Outpost.Built` -- `Outpost.Raise` appends to
        /// both lists together and `Demolish` removes the matching pair, so
        /// the two stay parallel). `OutpostLedger.StationForRaised` turns it
        /// into the one `StationStock` this sheet is about; without it every
        /// tap here would land on "the first station of this plan" rather
        /// than the one the player actually opened.
        ///
        /// **Re-resolved every `Refresh`, not just once.** `building` is
        /// THIS sheet's own instance and never changes, but its INDEX into
        /// `Outpost.Built` moves whenever an earlier station of any plan is
        /// demolished while this card is open -- a stale index then reads
        /// somebody else's bay and bench. Resolving it fresh off `building`
        /// each tick costs a linear scan of a handful of buildings, cheap
        /// next to the ledger step this same tick already pays for.
        int raisedIndex = -1;

        /// The recipe a tap has picked but not yet ordered -- local to the
        /// card, cleared on close/rebuild. Reveals the amount chips under
        /// that one row.
        string pickedRecipe;

        public StationSheet(Outpost o, Building b)
        {
            outpost = o;
            building = b;
            planId = b != null ? b.Id : null;
            plan = BuildPlans.Named(planId);
            hasMake = Recipes.StationHasRecipes(planId);
            hasUpgrade = Techs.MaxLevel(planId) > 1;
            ResolveRaisedIndex();
        }

        /// See the field doc on `raisedIndex`. Leaves it at -1 (and the
        /// station lookup null) when `building` is no longer among
        /// `outpost.Built` -- gone this frame, but `Destroy` has not landed
        /// yet, so `StillValid` has not closed the card for us.
        void ResolveRaisedIndex()
        {
            raisedIndex = -1;
            if (outpost == null || building == null) return;
            var built = outpost.Built;
            for (int i = 0; i < built.Count; i++)
                if (built[i] == building) { raisedIndex = i; break; }
        }

        /// The one `StationStock` this card is about, or null for a plan
        /// with no station stock at all (build-only levels, a farm).
        StationStock Station(OutpostLedger l) =>
            l != null && raisedIndex >= 0 ? l.StationForRaised(raisedIndex) : null;

        OutpostLedger L => outpost != null ? outpost.Ledger : null;

        // --- the frame -------------------------------------------------------

        public Color Accent => SheetTheme.Timber;

        /// Two tabs only when there is a decision on each; one section is a
        /// single page with no strip -- `WallSheet`'s rule.
        public string[] TabLabels => hasMake && hasUpgrade ? TwoTabs : null;
        static readonly string[] TwoTabs = { "Make", "Upgrade" };

        /// **`Tab` stays -1 until the host sets it**, exactly as
        /// `ISheetFramed` asks: -1 means "no preference", and that is what
        /// lets the host restore the tab this sheet TYPE was left on last
        /// session (`Sheets.RecallTab`). `Live` is what the body actually
        /// reads, and clamps a remembered tab that does not exist on a
        /// station with only one section.
        public int Tab => tab;
        public void SetTab(int index) { tab = index; }

        int Live => hasMake && hasUpgrade ? Mathf.Clamp(tab < 0 ? 0 : tab, TabMake, TabUpgrade)
            : (hasMake ? TabMake : TabUpgrade);

        public string Title => plan.label;

        public Vector3 AnchorWorld => building != null
            ? building.transform.position
            : (outpost != null ? outpost.CampCentre : Vector3.zero);

        public bool StillValid => outpost != null && outpost.Ledger != null && building != null;

        public VisualElement BuildHeader()
        {
            var l = L;
            int level = l != null ? l.LevelOf(planId) : 1;
            string where = string.IsNullOrEmpty(plan.position) ? plan.label : plan.position;
            return SheetKit.Header($"level {level} · {where}", Title, SheetTheme.Timber, "⚒",
                () => Sheets.Close());
        }

        Button upgradeBtn;

        /// **The upgrade tab's alone.** Make's rows are their own actions --
        /// tap one to choose it -- so a pinned row under them would be a
        /// second place to look for the same decision.
        public VisualElement BuildActions()
        {
            if (Live != TabUpgrade) return null;
            var l = L;
            if (l == null || l.NextUpgrade(planId) == null) return null;
            int next = l.LevelOf(planId) + 1;
            upgradeBtn = SheetKit.Btn($"Raise to level {next}", DoUpgrade, true);
            upgradeBtn.SetEnabled(l.CanUpgrade(planId, out _));
            return SheetKit.Actions(upgradeBtn);
        }

        // --- the pieces kept between refreshes --------------------------------

        VisualElement makeHolder;
        VisualElement orderHolder;
        VisualElement stockHolder;
        VisualElement rateNote;

        /// One row per recipe, built once per `Build()` and updated in place
        /// on every `Refresh` after that -- see `RecipeRowRefs`.
        readonly Dictionary<string, RecipeRowRefs> recipeRows = new Dictionary<string, RecipeRowRefs>();

        /// The active-order row, built only when an order exists and kept
        /// across refreshes while it does (its STOP button is a live touch
        /// target -- see the class doc on `RecipeRowRefs` for why rebuilding
        /// it on a timer loses taps).
        VisualElement orderRow;
        Label orderTitle;
        Label orderSub;
        Button orderStop;
        StationStock orderStopStation;

        /// The stock line, built once and re-texted in place.
        VisualElement stockCol;
        Label stockLine;
        Label stockStall;

        Label upgradeLevel;
        VisualElement upgradeHolder;
        long upgradeKey = long.MinValue;

        public VisualElement Build()
        {
            makeHolder = null; orderHolder = null; stockHolder = null; rateNote = null;
            upgradeHolder = null; upgradeLevel = null;
            pickedRecipe = null;
            recipeRows.Clear();
            orderRow = null; orderTitle = null; orderSub = null; orderStop = null; orderStopStation = null;
            stockCol = null; stockLine = null; stockStall = null;
            upgradeKey = long.MinValue;

            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Column;

            if (Live == TabMake) BuildMake(root);
            else BuildUpgrade(root);

            Refresh();
            return root;
        }

        public void Refresh()
        {
            var l = L;
            if (outpost == null || l == null) return;
            outpost.CatchUp();
            ResolveRaisedIndex();
            if (building != null && raisedIndex < 0)
            {
                // The building itself is still standing (`StillValid` has no
                // reason to close the card) but it is no longer among
                // `outpost.Built` -- a frame-order gap around a demolish, or
                // a station torn down some other way. Nothing here is about
                // anything real; blank the card rather than show a stale or
                // borrowed station.
                SheetBits.Swap(makeHolder, null);
                SheetBits.Swap(orderHolder, null);
                SheetBits.Swap(stockHolder, null);
                SheetBits.Swap(rateNote, null);
                orderRow = null; orderTitle = null; orderSub = null; orderStop = null; orderStopStation = null;
                stockCol = null; stockLine = null; stockStall = null;
                recipeRows.Clear();
                return;
            }

            if (Live == TabMake)
            {
                FillMake(l);
                // The order and the bay/bench/rack line move every tick a
                // hand is on this station -- a hauler filling the bay, the
                // bench climbing through `benchProgress` -- so unlike the
                // recipe list these two redraw their text on every refresh.
                // Neither rebuilds its element tree unless the thing it is
                // showing changes shape (an order starting/stopping), so a
                // press that started before a refresh still lands on the
                // same button after it.
                FillOrder(l);
                FillStock(l);
            }
            else
            {
                UpgradeTab(l);
            }

            if (upgradeBtn != null && Live == TabUpgrade)
                upgradeBtn.SetEnabled(l.CanUpgrade(planId, out _));
        }

        // --- tab: make ---------------------------------------------------------

        void BuildMake(VisualElement root)
        {
            root.Add(SheetKit.Eyebrow("make"));
            // **The running order and its STOP come first, 2026-09-23.** A
            // page that does not fit the band is cut at the bottom, and
            // below two recipe rows on a phone is already past it: STOP was
            // laid out 13 units under the page's edge, visible to nobody.
            orderHolder = SheetBits.Holder();
            root.Add(orderHolder);
            makeHolder = SheetBits.Holder();
            root.Add(makeHolder);
            stockHolder = SheetBits.Holder();
            root.Add(stockHolder);
            rateNote = SheetBits.Holder();
            root.Add(rateNote);
        }

        /// Add a row for any recipe this station can make that does not have
        /// one yet, in `Recipes.At(planId)`'s own order, and drop any row for
        /// a recipe that plan no longer lists. The list is effectively fixed
        /// for a station's whole life, so in practice this runs its "nothing
        /// to do" path every time after the first.
        void EnsureRecipeRows(IReadOnlyList<Recipe> recipes)
        {
            if (makeHolder == null) return;
            for (int i = 0; i < recipes.Count; i++)
            {
                var r = recipes[i];
                if (!recipeRows.TryGetValue(r.id, out var refs))
                {
                    refs = BuildRecipeRow(r);
                    recipeRows[r.id] = refs;
                    makeHolder.Add(refs.row);
                }
            }
            // The common case every refresh after the first: every recipe
            // in `recipes` already has a row and the counts match, so there
            // is nothing stale to find -- skip the scan (and its allocation)
            // entirely rather than pay it four times a second.
            if (recipeRows.Count == recipes.Count) return;

            List<string> stale = null;
            foreach (var kv in recipeRows)
            {
                bool present = false;
                for (int i = 0; i < recipes.Count; i++)
                    if (recipes[i].id == kv.Key) { present = true; break; }
                if (!present) (stale ??= new List<string>()).Add(kv.Key);
            }
            if (stale != null)
                foreach (var id in stale)
                {
                    recipeRows[id].row.RemoveFromHierarchy();
                    recipeRows.Remove(id);
                }
        }

        void FillMake(OutpostLedger l)
        {
            if (makeHolder == null) return;
            var station = Station(l);
            var recipes = Recipes.At(planId);
            EnsureRecipeRows(recipes);
            for (int i = 0; i < recipes.Count; i++)
                UpdateRecipeRow(recipeRows[recipes[i].id], recipes[i], l, station);

            var chosen = l.RecipeAt(planId);
            if (chosen == null)
            {
                SheetBits.Swap(rateNote, null);
                return;
            }
            int hands = 0;
            foreach (var h in l.hands)
                if (h != null && h.order == OutpostOrder.Work && h.target == planId) hands++;
            float rate = chosen.ratePerDay * Techs.RateMul(planId, l.LevelOf(planId)) * hands;
            string s = hands == 1 ? "1 hand" : $"{hands} hands";
            SheetBits.Swap(rateNote,
                SheetKit.Note($"+{rate:0.#} {ResDefs.Label(chosen.makes)}/day with {s}"));
        }

        /// **One recipe row's stable parts.** Everything a tap can land on
        /// -- the row itself and, while it is showing, the amount chips
        /// under it -- is built exactly once and kept for the life of the
        /// card; `Refresh` only ever re-texts labels and toggles which
        /// pre-built child is visible. A UI Toolkit `ClickEvent` needs its
        /// press and its release on the SAME element, and this sheet's host
        /// (`SheetHost`) calls `Refresh` on a 0.25 s timer regardless of
        /// what the player's thumb is doing -- rebuilding a row (the old
        /// `makeHolder.Clear()` + re-add) between those two events swaps the
        /// element out from under the finger and the tap is silently lost.
        /// STOP and the amount chips are the buttons that made this show up
        /// on the phone; this fixes the whole list so a station with slow
        /// ingredient counts (a bay filling under a hauler's feet -- the
        /// thing that used to force a rebuild every tick) does not
        /// reintroduce the same bug.
        class RecipeRowRefs
        {
            public VisualElement row;
            public Label[] ingLabels;
            public Label toolLine;
            public VisualElement rightHolder;
            public Label lockLabel;
            public VisualElement waitingChip;
            public VisualElement makingChip;
            public VisualElement amountHolder;
            public bool chipsBuilt;
            public bool available;
        }

        RecipeRowRefs BuildRecipeRow(Recipe r)
        {
            var refs = new RecipeRowRefs();

            var left = new VisualElement();
            left.style.flexDirection = FlexDirection.Column;
            left.style.flexGrow = 1f;
            left.Add(SheetKit.Text(r.label, true, false, 14f));

            var ingRow = new VisualElement();
            ingRow.style.flexDirection = FlexDirection.Row;
            ingRow.style.flexWrap = Wrap.Wrap;
            var ingLabels = new Label[r.takes.Length];
            for (int i = 0; i < r.takes.Length; i++)
            {
                if (i > 0) ingRow.Add(SheetKit.Text(", ", false, true, 12f));
                var lab = SheetKit.Text("", false, false, 12f);
                ingLabels[i] = lab;
                ingRow.Add(lab);
            }
            ingRow.Add(SheetKit.Text($" → {r.yield} {r.label}", false, true, 12f));
            left.Add(ingRow);
            refs.ingLabels = ingLabels;

            if (r.tool != null)
            {
                var toolLine = SheetKit.Text("", false, false, 11f);
                left.Add(toolLine);
                refs.toolLine = toolLine;
            }

            refs.amountHolder = SheetBits.Holder();
            left.Add(refs.amountHolder);

            refs.rightHolder = SheetBits.Holder();
            refs.lockLabel = SheetKit.Text("", false, true, 11f);
            refs.lockLabel.style.display = DisplayStyle.None;
            refs.rightHolder.Add(refs.lockLabel);
            refs.waitingChip = SheetKit.Chip("waiting", "", SheetTheme.Ember);
            refs.waitingChip.style.display = DisplayStyle.None;
            refs.rightHolder.Add(refs.waitingChip);
            refs.makingChip = SheetKit.Chip("state", "making", SheetTheme.Moss);
            refs.makingChip.style.display = DisplayStyle.None;
            refs.rightHolder.Add(refs.makingChip);

            var row = SheetKit.Row(left, refs.rightHolder);
            row.style.paddingTop = 6f;
            row.style.paddingBottom = 6f;
            // Locked/unlocked toggles opacity and clickability in
            // `UpdateRecipeRow`; the callback itself is registered once and
            // gated on `refs.available` so a locked row still does nothing
            // without needing to be rebuilt to lose the handler.
            row.RegisterCallback<ClickEvent>(_ =>
            {
                // A picked row can always be un-picked, even if it has since
                // locked -- it is the only row left on the page (see
                // `UpdateRecipeRow`), and a page with no way back is a trap.
                if (!refs.available && pickedRecipe != r.id) return;
                pickedRecipe = pickedRecipe == r.id ? null : r.id;
                Refresh();
            });
            refs.row = row;
            return refs;
        }

        void UpdateRecipeRow(RecipeRowRefs refs, Recipe r, OutpostLedger l, StationStock station)
        {
            bool available = l.RecipeAvailable(r, out string lockWhy);
            var order = station != null ? l.OrderAt(planId, station.ordinal) : default;
            bool isActive = order.Active && order.recipe != null && order.recipe.id == r.id;
            bool isPicked = pickedRecipe == r.id;

            for (int i = 0; i < r.takes.Length; i++)
            {
                int have = l.CountOf(r.takes[i].res);
                int need = r.takes[i].n;
                refs.ingLabels[i].text = $"{ResDefs.Label(r.takes[i].res)} {have}/{need}";
                refs.ingLabels[i].style.color = have >= need ? SheetTheme.Moss : SheetTheme.Ember;
            }

            if (refs.toolLine != null)
            {
                bool hasTool = l.CountOf(r.tool) > 0;
                refs.toolLine.text = $"needs a {ResDefs.Label(r.tool)} (wears)";
                refs.toolLine.style.color = hasTool ? SheetTheme.Moss : SheetTheme.Ember;
            }

            // The amount chips are buttons of their own -- built once when
            // the row is first picked and left alone while it stays picked,
            // exactly like the row above. They only come down when picking
            // is undone, and that always happens on a real tap (this row, or
            // a chip inside it), never mid-refresh.
            if (available && isPicked && station != null)
            {
                if (!refs.chipsBuilt)
                {
                    SheetBits.Swap(refs.amountHolder, AmountChips(l, station, r.id));
                    refs.chipsBuilt = true;
                }
            }
            else if (refs.chipsBuilt)
            {
                SheetBits.Swap(refs.amountHolder, null);
                refs.chipsBuilt = false;
            }

            refs.lockLabel.style.display = DisplayStyle.None;
            refs.waitingChip.style.display = DisplayStyle.None;
            refs.makingChip.style.display = DisplayStyle.None;
            if (!available)
            {
                refs.lockLabel.text = lockWhy;
                refs.lockLabel.style.display = DisplayStyle.Flex;
            }
            else if (isActive)
            {
                var missing = Cost.Missing(r.takes, res => l.CountOf(res));
                if (missing.Count > 0)
                {
                    SheetKit.SetChip(refs.waitingChip, ResDefs.Label(missing[0].res));
                    refs.waitingChip.style.display = DisplayStyle.Flex;
                }
                else
                {
                    refs.makingChip.style.display = DisplayStyle.Flex;
                }
            }

            // **The picked recipe has the page to itself, 2026-09-23.** Its
            // amount chips add a whole row, and on a phone-height band the
            // chips, the order line and STOP only fit if the other recipes
            // step aside; they come back the moment the pick is undone (the
            // row again, or a chip). No scrolling -- Kevin's rule for the
            // sheet -- so the page shrinks what it asks, not how it shows it.
            bool focusing = pickedRecipe != null && station != null;
            refs.row.style.display = !focusing || isPicked ? DisplayStyle.Flex : DisplayStyle.None;

            refs.row.style.opacity = available ? 1f : 0.5f;
            refs.row.EnableInClassList("sheet-clickable", available);
            if (isPicked)
                refs.row.style.backgroundColor = new Color(
                    SheetTheme.Brass.r, SheetTheme.Brass.g, SheetTheme.Brass.b, 0.14f);
            else
                refs.row.style.backgroundColor = StyleKeyword.Null;
            refs.available = available;
        }

        static readonly (string label, int count)[] AmountOptions =
        {
            ("∞", OutpostLedger.RepeatOrder),
            ("5", 5), ("10", 10), ("20", 20), ("50", 50), ("100", 100),
        };

        /// **"make an order and keep making that order until you tell it to
        /// stop, and/or set amounts like 5, 10, 20, 50, 100"** -- Kevin,
        /// verbatim. One tap places the order outright; the STOP button
        /// under the active-order block below is the only confirm this
        /// needs, because it is also the undo.
        VisualElement AmountChips(OutpostLedger l, StationStock station, string recipeId)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            row.style.marginTop = 6f;
            foreach (var (label, count) in AmountOptions)
            {
                var b = SheetKit.Btn(label, () =>
                {
                    l.PlaceOrder(planId, recipeId, count, station.ordinal);
                    pickedRecipe = null;
                    Refresh();
                });
                // **44 pt floor.** `SheetKit.Btn` ships at 38 px, tuned for a
                // button sitting among other rows on a card; an amount chip
                // IS the decision Kevin's rule is about, so it gets the one
                // number this project treats as a floor on anything a thumb
                // aims at (`SheetKit.Tabs`' own rule, applied here).
                b.style.minHeight = 44f;
                b.style.minWidth = 44f;
                b.style.flexGrow = 1f;
                b.style.marginRight = 6f;
                b.style.marginBottom = 6f;
                row.Add(b);
            }
            // Stop the row's own click (picking/un-picking the recipe) from
            // firing when a chip inside it is tapped.
            row.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            return row;
        }

        /// **The active order, and the STOP button** -- Kevin's rule again:
        /// a station keeps making what it was told until the player says
        /// otherwise, so the one thing this block has to do is make "what"
        /// and "stop" impossible to miss.
        ///
        /// The row (and STOP itself) is built once, the first refresh an
        /// order is active, and kept until the order ends -- not rebuilt on
        /// the 0.25 s tick that used to clear and re-add it, which could
        /// swallow a tap that landed between one refresh and the next.
        void FillOrder(OutpostLedger l)
        {
            if (orderHolder == null) return;
            var station = Station(l);
            var order = station != null ? l.OrderAt(planId, station.ordinal) : default;
            bool active = station != null && order.Active;

            if (!active)
            {
                if (orderRow != null)
                {
                    SheetBits.Swap(orderHolder, null);
                    orderRow = null; orderTitle = null; orderSub = null; orderStop = null; orderStopStation = null;
                }
                return;
            }

            if (orderRow == null)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginTop = 4f;

                var left = new VisualElement();
                left.style.flexDirection = FlexDirection.Column;
                left.style.flexGrow = 1f;
                orderTitle = SheetKit.Text("", true, false, 14f);
                left.Add(orderTitle);
                orderSub = SheetKit.Text("", false, true, 12f);
                left.Add(orderSub);
                row.Add(left);

                orderStop = SheetKit.Btn("STOP", () =>
                {
                    if (orderStopStation == null) return;
                    l.StopOrder(planId, orderStopStation.ordinal);
                    Refresh();
                }, true);
                orderStop.style.minHeight = 44f;
                orderStop.style.minWidth = 96f;
                orderStop.style.flexGrow = 0f;
                orderStop.style.backgroundColor = SheetTheme.Ember;
                orderStop.style.borderTopColor = orderStop.style.borderBottomColor =
                    orderStop.style.borderLeftColor = orderStop.style.borderRightColor = SheetTheme.Ember;
                row.Add(orderStop);

                orderRow = row;
                SheetBits.Swap(orderHolder, orderRow);
            }

            // The station this instance's order lives on can shift ordinal
            // under a demolish elsewhere (see `ResolveRaisedIndex`); STOP's
            // closure reads this field fresh rather than a station captured
            // when the button was built.
            orderStopStation = station;
            orderTitle.text = order.recipe.label;
            orderSub.text = order.repeat ? "∞ until stopped" : $"{order.remaining} left";
        }

        static string BenchWord(BenchState s) => s switch
        {
            BenchState.Empty => "empty",
            BenchState.Loaded => "loaded",
            BenchState.Working => "working",
            BenchState.Finished => "finished",
            _ => "",
        };

        /// The Work hand the ledger has dealt to THIS built instance, if
        /// any -- `StationOfHand` is the same round-robin the ledger itself
        /// works from (`OutpostLedger.Stations.cs`), read backwards to find
        /// the hand for a station rather than the station for a hand.
        static OutpostHand HandOn(OutpostLedger l, StationStock station)
        {
            if (l == null || station == null || l.hands == null) return null;
            foreach (var h in l.hands)
            {
                if (h == null || h.order != OutpostOrder.Work || h.target != station.planId) continue;
                if (l.StationOfHand(h) == station) return h;
            }
            return null;
        }

        /// **The stock line**: bay per ingredient, the bench's own state,
        /// the rack -- what the player would otherwise have to walk over
        /// and look at. The stall reason rides under it when the hand
        /// working here has one (`OutpostLedger.StallReason`, the same
        /// sentence the hand's own token would show).
        ///
        /// No buttons live here, but it is built once and re-texted the same
        /// way as `FillOrder` -- there is no reason to churn two labels and
        /// throw the old ones away four times a second when setting their
        /// text does the same job for nothing.
        void FillStock(OutpostLedger l)
        {
            if (stockHolder == null) return;
            var station = Station(l);
            if (station == null)
            {
                if (stockCol != null) { SheetBits.Swap(stockHolder, null); stockCol = null; stockLine = null; stockStall = null; }
                return;
            }

            if (stockCol == null)
            {
                stockCol = new VisualElement();
                stockCol.style.flexDirection = FlexDirection.Column;
                stockCol.style.marginTop = 4f;
                stockLine = SheetKit.Text("", false, true, 11f);
                stockCol.Add(stockLine);
                stockStall = SheetKit.Text("", false, false, 11f);
                stockStall.style.color = SheetTheme.Ember;
                stockStall.style.marginTop = 2f;
                stockStall.style.display = DisplayStyle.None;
                stockCol.Add(stockStall);
                SheetBits.Swap(stockHolder, stockCol);
            }

            var parts = new List<string>(4);
            var recipe = station.OrderRecipe ?? l.RecipeAt(planId);
            if (recipe != null)
                foreach (var t in recipe.takes)
                    parts.Add($"bay {ResDefs.Label(t.res)} {station.BayCount(t.res)}/{station.InputCap}");
            parts.Add($"bench {BenchWord(station.benchState)}");
            parts.Add($"rack {station.RackTotal}/{station.OutputCap}");
            stockLine.text = string.Join(" · ", parts);

            var hand = HandOn(l, station);
            string stall = hand != null ? l.StallReason(hand) : null;
            if (!string.IsNullOrEmpty(stall))
            {
                stockStall.text = stall;
                stockStall.style.display = DisplayStyle.Flex;
            }
            else
            {
                stockStall.style.display = DisplayStyle.None;
            }
        }

        // --- tab: upgrade --------------------------------------------------------

        void BuildUpgrade(VisualElement root)
        {
            upgradeLevel = SheetKit.Text("", true, false, 16f);
            root.Add(upgradeLevel);
            upgradeHolder = SheetBits.Holder();
            root.Add(upgradeHolder);
        }

        void UpgradeTab(OutpostLedger l)
        {
            int level = l.LevelOf(planId);
            int max = Techs.MaxLevel(planId);
            if (upgradeLevel != null) upgradeLevel.text = $"level {level} of {max}";

            var next = l.NextUpgrade(planId);
            long key = level * 1000003L;
            if (next != null)
                foreach (var c in next.cost) key = key * 31 + l.CountOf(c.res);
            if (key == upgradeKey) return;
            upgradeKey = key;

            var col = new VisualElement();
            col.style.flexDirection = FlexDirection.Column;
            if (next == null)
            {
                col.Add(SheetKit.Note("as good as it gets"));
            }
            else
            {
                var costRow = new VisualElement();
                costRow.style.flexDirection = FlexDirection.Row;
                costRow.style.flexWrap = Wrap.Wrap;
                for (int i = 0; i < next.cost.Length; i++)
                {
                    if (i > 0) costRow.Add(SheetKit.Text(", ", false, true, 12f));
                    costRow.Add(IngredientLine(l, next.cost[i].res, next.cost[i].n));
                }
                col.Add(costRow);
                string effect = EffectLine(next);
                if (effect.Length > 0) col.Add(SheetKit.Text(effect, false, true, 12f));
                if (!l.CanUpgrade(planId, out string why))
                    col.Add(SheetKit.Note(why));
            }
            SheetBits.Swap(upgradeHolder, col);
        }

        static string EffectLine(UpgradeStep step)
        {
            var parts = new List<string>(2);
            if (step.storeBonus != 0) parts.Add($"+{step.storeBonus} stores");
            if (step.housesBonus != 0) parts.Add(step.housesBonus == 1 ? "+1 bed" : $"+{step.housesBonus} beds");
            if (Mathf.Abs(step.rateMul - 1f) > 0.001f) parts.Add($"×{step.rateMul:0.#} rate");
            return string.Join(", ", parts);
        }

        void DoUpgrade()
        {
            var l = L;
            if (l == null) return;
            if (l.Upgrade(planId))
            {
                upgradeKey = long.MinValue;
                Refresh();
            }
        }

        // --- shared with FireSheet's own fire-level block -----------------------

        /// "boards 0/1" -- one ingredient, coloured green when the pile
        /// covers it and red when it does not. The one place the have/need
        /// arithmetic for a single line is written, so the fire's own next-
        /// level cost (`FireSheet`) cannot say something different about the
        /// same pile.
        internal static VisualElement IngredientLine(OutpostLedger l, string res, int need)
        {
            int have = l != null ? l.CountOf(res) : 0;
            var lab = SheetKit.Text($"{ResDefs.Label(res)} {have}/{need}", false, false, 12f);
            lab.style.color = have >= need ? SheetTheme.Moss : SheetTheme.Ember;
            return lab;
        }
    }
}
