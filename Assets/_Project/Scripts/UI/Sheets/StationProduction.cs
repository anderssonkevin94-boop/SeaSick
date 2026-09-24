using System.Collections.Generic;
using SeaSick.World;
using SeaSick.World.Economy;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    public partial class StationSheet
    {
        string productionRecipe;
        int productionAmount = OutpostLedger.RepeatOrder;
        bool orderEdited;
        Label productionStatus, productionDetail;
        DropdownField productChoice, amountChoice;
        Button productionStart, productionStop;
        readonly List<Recipe> productOptions = new List<Recipe>();

        void ResetProductionElements()
        {
            productionStatus = productionDetail = null;
            productChoice = amountChoice = null;
            productionStart = productionStop = null;
        }

        Recipe SelectedProduct(OutpostLedger l, StationStock station)
        {
            if (productionRecipe == null)
            {
                var initial = Feeding(l, station);
                productionRecipe = initial?.id;
                if (station != null && l.OrderAt(planId, station.ordinal).Active)
                    productionAmount = station.orderRepeat ? OutpostLedger.RepeatOrder : station.orderLeft;
            }
            return Recipes.Named(productionRecipe);
        }

        void BuildProduction()
        {
            root.AddToClassList("land-production");
            var l = L;
            var selected = SelectedProduct(l, Station(l));
            productionStatus = SheetKit.Text("", true, false, 13f);
            productionStatus.AddToClassList("land-production-status");
            root.Add(productionStatus);
            BuildFlow();
            worker = new WorkerSlot(outpost, planId, Refresh, true);
            root.Add(worker.Root);

            productOptions.Clear();
            var choices = new List<string>();
            foreach (var recipe in Recipes.At(planId))
            {
                productOptions.Add(recipe);
                choices.Add(ProductName(recipe));
            }
            int index = Mathf.Max(0, productOptions.IndexOf(selected));
            productChoice = new DropdownField(choices, index);
            productChoice.AddToClassList("land-product-choice");
            productChoice.tooltip = "Product";
            productChoice.RegisterValueChangedCallback(e =>
            {
                int picked = productChoice.index;
                if (picked < 0 || picked >= productOptions.Count) return;
                productionRecipe = productOptions[picked].id;
                orderEdited = true;
                Refresh();
            });
            root.Add(productChoice);
            productionDetail = SheetKit.Text("", false, true, 11f);
            productionDetail.AddToClassList("land-production-detail");
            root.Add(productionDetail);
        }

        static string ProductName(Recipe recipe)
        {
            string name = recipe.label;
            return string.IsNullOrEmpty(name) ? "Product" : char.ToUpperInvariant(name[0]) + name.Substring(1);
        }

        VisualElement BuildProductionActions()
        {
            var l = L;
            SelectedProduct(l, Station(l));
            var choices = new List<string>();
            var amounts = new List<int>();
            foreach (var option in AmountOptions)
            {
                choices.Add(option.count < 0 ? "Repeat" : "Make " + option.count);
                amounts.Add(option.count);
            }
            int amountIndex = 0;
            for (int i = 0; i < AmountOptions.Length; i++)
                if (AmountOptions[i].count == productionAmount) amountIndex = i;
            // An in-progress finite order may have a remainder outside the presets.
            if (productionAmount > 0 && amountIndex == 0)
            {
                choices.Add("Make " + productionAmount);
                amounts.Add(productionAmount);
                amountIndex = choices.Count - 1;
            }
            amountChoice = new DropdownField(choices, amountIndex);
            amountChoice.AddToClassList("land-amount-choice");
            amountChoice.tooltip = "Output quantity";
            amountChoice.RegisterValueChangedCallback(e =>
            {
                if (amountChoice.index >= 0 && amountChoice.index < amounts.Count)
                    productionAmount = amounts[amountChoice.index];
                orderEdited = true;
                Refresh();
            });
            productionStart = SheetKit.Btn("Start", () =>
            {
                ResolveRaisedIndex();
                var station = Station(L);
                if (station == null) return;
                if (L.PlaceOrder(planId, productionRecipe, productionAmount, station.ordinal)) orderEdited = false;
                Refresh();
            }, true);
            productionStart.AddToClassList("land-production-start");
            productionStop = SheetKit.Btn("Stop", () =>
            {
                ResolveRaisedIndex();
                var station = Station(L);
                if (station != null) L.StopOrder(planId, station.ordinal);
                orderEdited = false;
                Refresh();
            });
            productionStop.AddToClassList("land-production-stop");
            var actions = SheetKit.Actions(amountChoice, productionStart, productionStop);
            actions.AddToClassList("land-production-actions");
            Refresh();
            return actions;
        }

        void RefreshProduction(OutpostLedger l, StationStock station, OutpostHand hand)
        {
            if (productionStatus == null) return;
            var recipe = SelectedProduct(l, station);
            var order = station != null ? l.OrderAt(planId, station.ordinal) : default;
            bool active = order.Active && order.recipe != null;
            string blocked = hand != null ? l.StallReason(hand) : null;
            productionStatus.text = station == null ? "Station unavailable"
                : hand == null ? "Needs a worker"
                : !string.IsNullOrEmpty(blocked) ? blocked
                : !active ? (station.benchState != BenchState.Empty ? "Finishing current batch" : "Ready for an order")
                : station.RackFull ? "Output rack full"
                : station.benchState == BenchState.Working ? "Production running"
                : "Preparing the next batch";
            productionStatus.style.color = hand == null || !string.IsNullOrEmpty(blocked) || station == null || station.RackFull
                ? SheetTheme.Ember : SheetTheme.Moss;

            bool available = recipe != null && l.RecipeAvailable(recipe, out _);
            if (recipe != null)
            {
                var parts = new List<string>();
                foreach (var input in recipe.takes) parts.Add($"{input.n} {ResDefs.Label(input.res)}");
                productionDetail.text = string.Join(" + ", parts) + $" → {recipe.yield} {recipe.label}";
                if (recipe.tool != null) productionDetail.text += $" · {ResDefs.Label(recipe.tool)} wears";
                if (!available && !l.RecipeAvailable(recipe, out string why)) productionDetail.text = why;
            }
            productionDetail.style.color = available ? SheetTheme.InkDim : SheetTheme.Ember;
            if (active)
                productionStatus.text += "\n" + ProductName(order.recipe)
                    + (order.repeat ? " · repeating" : $" · {order.remaining} left");
            if (productionStart != null)
            {
                productionStart.text = active ? (orderEdited ? "Update order" : "Ordered") : "Start order";
                productionStart.SetEnabled(station != null && available && (!active || orderEdited));
                productionStop.SetEnabled(active);
                productionStop.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }
    }
}
