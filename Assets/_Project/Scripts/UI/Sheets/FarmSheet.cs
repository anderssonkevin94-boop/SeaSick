using System.Collections.Generic;
using SeaSick.World;
using SeaSick.World.Economy;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **The farm's sheet, 2026-09-23 -- `StationSheet`'s template, farm
    /// rows.** Top to bottom as the food flows:
    ///
    /// 1. **the farmhand** -- the same `WorkerSlot` every building has;
    /// 2. **the beds** -- this farm's six, ripe or cut, straight off the
    ///    field (`Outpost.FarmBeds`, the beds `FarmBedView` dresses), and
    ///    when the next one stands again (`Outpost.NextBedDays`);
    /// 3. **the yield** -- `OutpostLedger.FoodPerHandPerDay` a hand, into
    ///    the store. No order and no chips: a field is not told what to make;
    /// 4. **what is kept** -- food in store and how long it lasts at the
    ///    ration they are on (`SheetBits.FoodDays`);
    /// 5. **why it's stopped** -- the farmhand's `StallReason`.
    ///
    /// A farm has no upgrade today (`Techs.MaxLevel("Farm") == 1`), so row 6
    /// is skipped. One page: the rows cost ~150 panel units and a phone band
    /// is ~175 (`StationSheet.Band`, measured at 1080x2340).
    public class FarmSheet : ISheetFramed
    {
        readonly Outpost outpost;
        readonly Building building;
        readonly string planId;
        readonly BuildPlan plan;
        int tab = -1;

        public FarmSheet(Outpost o, Building b)
        {
            outpost = o;
            building = b;
            planId = b != null ? b.Id : BuildPlans.Farm.id;
            plan = BuildPlans.Named(planId);
        }

        OutpostLedger L => outpost != null ? outpost.Ledger : null;

        public string Title => plan.label;
        public Color Accent => SheetTheme.Moss;
        public string[] TabLabels => null;
        public int Tab => tab;
        public void SetTab(int index) { tab = index; }

        public Vector3 AnchorWorld => building != null
            ? building.transform.position
            : (outpost != null ? outpost.CampCentre : Vector3.zero);

        public bool StillValid => outpost != null && outpost.Ledger != null && building != null;

        public VisualElement BuildHeader()
        {
            var l = L;
            int level = outpost != null ? outpost.LevelOfBuilding(building) : 1;
            return SheetKit.Header($"level {level} · {plan.position}", Title, SheetTheme.Moss, "🌾",
                () => Sheets.Close());
        }

        public VisualElement BuildActions() => null;

        // --- built once -------------------------------------------------------

        WorkerSlot worker;
        VisualElement pips;
        Label bedsLine;
        Label yieldLine;
        Label keptLine;
        Label stallLine;
        readonly List<bool> beds = new List<bool>();

        const float PipPx = 22f;

        public VisualElement Build()
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Column;

            worker = new WorkerSlot(outpost, planId, () => Refresh());
            root.Add(worker.Root);

            var bedRow = new VisualElement();
            bedRow.style.flexDirection = FlexDirection.Row;
            bedRow.style.alignItems = Align.Center;
            var lead = SheetKit.Eyebrow("the beds");
            lead.style.width = StationSheet.LeadPx;
            lead.style.flexShrink = 0f;
            lead.style.marginBottom = 0f;
            bedRow.Add(lead);
            pips = new VisualElement();
            pips.style.flexDirection = FlexDirection.Row;
            pips.style.height = PipPx;
            pips.style.marginBottom = 2f;
            pips.pickingMode = PickingMode.Ignore;
            for (int i = 0; i < Mathf.Max(1, plan.beds); i++)
            {
                var p = new VisualElement();
                p.style.width = PipPx;
                p.style.height = PipPx;
                p.style.marginRight = 6f;
                p.style.borderTopLeftRadius = p.style.borderTopRightRadius =
                    p.style.borderBottomLeftRadius = p.style.borderBottomRightRadius = 4f;
                p.style.borderTopWidth = p.style.borderBottomWidth =
                    p.style.borderLeftWidth = p.style.borderRightWidth = 1f;
                p.style.borderTopColor = p.style.borderBottomColor =
                    p.style.borderLeftColor = p.style.borderRightColor = SheetTheme.InkDim;
                p.pickingMode = PickingMode.Ignore;
                pips.Add(p);
            }
            bedRow.Add(pips);
            root.Add(bedRow);
            bedsLine = StationSheet.LeadLine(root, "");
            yieldLine = StationSheet.LeadLine(root, "making");
            keptLine = StationSheet.LeadLine(root, "going out");
            stallLine = StationSheet.LeadLine(root, "");
            stallLine.style.color = SheetTheme.Ember;

            Refresh();
            return root;
        }

        public void Refresh()
        {
            var l = L;
            if (outpost == null || l == null || worker == null) return;
            outpost.CatchUp();

            // Farmhands are dealt round the farms (2026-09-27, the same
            // deal as the stations -- `OutpostLedger.OrdinalOfHand`): this
            // sheet counts the ones at THIS plot. One farm = every farmhand.
            int mine = outpost.OrdinalOf(building);
            OutpostHand first = null;
            int hands = 0;
            foreach (var h in l.hands)
            {
                if (h == null || h.order != OutpostOrder.Work || h.target != planId) continue;
                if (mine >= 0 && l.OrdinalOfHand(h) != mine) continue;
                if (first == null) first = h;
                hands++;
            }
            worker.Update(l, first, Mathf.Max(0, hands - 1));

            // 2. the beds
            int found = outpost.FarmBeds(building, beds);
            int ripe = 0;
            for (int i = 0; i < pips.childCount; i++)
            {
                bool known = i < found;
                bool up = known && beds[i];
                if (up) ripe++;
                pips[i].style.backgroundColor = !known ? new Color(0f, 0f, 0f, 0f)
                    : up ? SheetTheme.Moss : new Color(SheetTheme.Timber.r, SheetTheme.Timber.g, SheetTheme.Timber.b, 0.35f);
            }
            if (found == 0)
            {
                bedsLine.text = "the beds are not in sight";
            }
            else
            {
                string next = ripe >= found ? "all standing" : "next in " + Days(outpost.NextBedDays);
                bedsLine.text = $"{ripe} of {found} beds ripe · {next}";
            }

            // 3. the yield -- the ledger's per-hand figure, times the hands.
            float per = OutpostLedger.FoodPerHandPerDay * Techs.RateMul(planId, outpost.LevelOfBuilding(building));
            yieldLine.text = hands <= 1
                ? $"{per:0.#} food a day with one hand · to the store"
                : $"{per * hands:0.#} food a day with {hands} hands · to the store";

            // 4. what is kept
            float stored = l.CountOf(Res.Food) + (l.Store(Res.Food)?.part ?? 0f);
            float days = SheetBits.FoodDays(l);
            keptLine.text = days < 0f
                ? $"{stored:0.#} food stored · {SheetBits.FoodDaysLine(l)}"
                : $"{stored:0.#} food stored · lasts {days:0.#} days";

            // 5. why it's stopped
            stallLine.text = StationSheet.StallText(l, first, false);
        }

        /// "~0.5 day", "~3 days", "any moment" -- never "Infinity".
        static string Days(float d)
        {
            if (float.IsInfinity(d) || float.IsNaN(d)) return "— (not regrowing)";
            if (d < 0.05f) return "any moment";
            return d < 1f ? $"~{d:0.#} day" : $"~{d:0.#} days";
        }
    }
}
