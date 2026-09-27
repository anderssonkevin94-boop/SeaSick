using System.Collections.Generic;
using UnityEngine;
using SeaSick.World.Economy;

namespace SeaSick.World
{
    public enum PlotState
    {
        Empty = 0,
        Growing = 1,
        Ripe = 2,
    }

    /// **One farm plot (food rework, 2026-09-27).** A crop, a real-time
    /// growth timer (held in game days so `Step` stays path independent),
    /// and a repeat flag: a plot on repeat is replanted by the farmhand after
    /// every harvest. `crop` empty = the plot stands empty ("tap to plant").
    [System.Serializable]
    public class FarmPlot
    {
        /// Which copy of the Farm (ordinal) this plot belongs to.
        public int farm;
        public int slot;
        public string crop = "";
        public bool repeat = true;
        public PlotState state = PlotState.Empty;
        /// Game days grown since planting.
        public float grown;
        /// Seconds of the farmhand's plant/harvest work done on it so far.
        public float work;

        public float Grow01 => state == PlotState.Ripe ? 1f
            : state == PlotState.Growing ? Mathf.Clamp01(grown / Mathf.Max(1e-5f, FoodBook.GrowDays(crop))) : 0f;

        /// Real seconds until ripe (0 when ripe or empty).
        public float SecondsLeft => state != PlotState.Growing ? 0f
            : Mathf.Max(0f, (FoodBook.GrowDays(crop) - grown) * TimeOfDay.DayLength);
    }

    /// **Food: plots, eating by fill, the food total (2026-09-27).** The
    /// approved Melvor-style rework (docs/GDD.md "Food"): crops grow per plot
    /// on real-minute timers and the farmhand carries each harvest to the
    /// store; the kitchen cooks dishes by order; a hand whose fullness drops
    /// below `EconomyTuning.HungryBelow` walks to the store and eats the best
    /// dish there (raw at a quarter only if nothing cooked is left). Goods
    /// count on arrival -- the meal is picked up at the store and eaten there.
    ///
    /// **`Res.Food` is no longer "the food".** Read `FoodFill` (fill units in
    /// store, one = a hand-day) or `DaysOfFood` for any gauge.
    public partial class OutpostLedger
    {
        public List<FarmPlot> plots = new List<FarmPlot>();
        /// Dishes the player has switched off in the larder ("save for the
        /// ship", "feast for later"): never eaten while on this list.
        public List<string> savedDishes = new List<string>();
        /// The one-off old-save conversion has run (Food pile -> biscuit).
        public bool foodMigrated;

        // --- the total, for every gauge --------------------------------------

        /// **Fill units of food in the store** -- one is a hand fed for a
        /// day. Saved dishes are left out unless asked for.
        public float FoodFill(bool includeSaved = false)
        {
            float f = 0f;
            foreach (var e in FoodBook.Edibles)
            {
                if (!includeSaved && DishSaved(e.res)) continue;
                var s = Store(e.res);
                if (s == null) continue;
                f += (s.whole + s.part) * FoodBook.Fill(e.res);
            }
            return f;
        }

        /// Fill of cooked dishes only.
        public float CookedFill()
        {
            float f = 0f;
            foreach (var e in FoodBook.Edibles)
            {
                if (e.raw || DishSaved(e.res)) continue;
                var s = Store(e.res);
                if (s != null) f += (s.whole + s.part) * FoodBook.Fill(e.res);
            }
            return f;
        }

        /// What the camp eats in a game day at the current rations.
        public float FillPerDay => hands.Count * EatPerHandPerDay * (rations == Rations.Half ? 0.5f : 1f);

        /// Game days the store feeds everybody here.
        public float DaysOfFood => FillPerDay > 0f ? FoodFill() / FillPerDay : float.PositiveInfinity;

        public bool DishSaved(string res) => savedDishes != null && res != null && savedDishes.Contains(res);

        public void SetDishSaved(string res, bool saved)
        {
            if (savedDishes == null) savedDishes = new List<string>();
            if (saved) { if (!savedDishes.Contains(res)) savedDishes.Add(res); }
            else savedDishes.Remove(res);
        }

        /// **Spend `fill` worth of food, worst first** (a recruit's welcome):
        /// raw before cooked, saved dishes never.
        public float TakeFill(float fill)
        {
            float paid = 0f;
            for (int pass = 0; pass < 2 && paid < fill - 1e-4f; pass++)
                for (int i = FoodBook.Edibles.Length - 1; i >= 0 && paid < fill - 1e-4f; i--)
                {
                    var e = FoodBook.Edibles[i];
                    if (e.raw != (pass == 0) || DishSaved(e.res)) continue;
                    float per = FoodBook.Fill(e.res);
                    while (paid < fill - 1e-4f && StoreFree(e.res) > 0)
                    {
                        Take(e.res, 1);
                        paid += per;
                    }
                }
            return paid;
        }

        // --- the old save ------------------------------------------------------

        /// **Once per ledger**: an old save's generic Food becomes ship's
        /// biscuit at two per Food (biscuit is 0.5 fill, old Food fed a hand
        /// a day -- so the camp keeps exactly the days of food it had and
        /// nobody starves on load). Over the ceiling if need be: it exists.
        /// Station rows of Food (the old kitchen's bay, the fishing hut's
        /// rack) become potatoes / fish.
        void MigrateFood()
        {
            if (foodMigrated) return;
            foodMigrated = true;
            var old = Store(Res.Food);
            if (old != null && (old.whole > 0 || old.part > 0f))
            {
                float units = (old.whole + old.part) * 2f;
                var b = Store(Res.Meals, true);
                b.whole += Mathf.FloorToInt(units);
                b.part += units - Mathf.Floor(units);
                while (b.part >= 1f) { b.whole++; b.part -= 1f; }
                old.whole = 0;
                old.part = 0f;
            }
            if (stations != null)
                foreach (var s in stations)
                {
                    if (s == null) continue;
                    string into = s.planId == BuildPlans.FishingHut.id ? Res.Fish : Res.Potato;
                    MoveRow(s.bay, into);
                    MoveRow(s.rack, into);
                }
        }

        static void MoveRow(List<OutpostStore> rows, string into)
        {
            if (rows == null) return;
            OutpostStore from = null, to = null;
            foreach (var r in rows)
            {
                if (r == null) continue;
                if (r.resource == Res.Food) from = r;
                else if (r.resource == into) to = r;
            }
            if (from == null) return;
            if (to == null) { from.resource = into; return; }
            to.whole += from.whole;
            to.part += from.part;
            rows.Remove(from);
        }

        // --- eating ------------------------------------------------------------

        /// **The meal a hungry hand walks to the store for**: the best
        /// allowed dish (highest fill, then its bonus) with a unit nobody is
        /// already walking to take; raw only when no dish is left. Null when
        /// the store has nothing edible for him.
        public string BestMeal()
        {
            string best = null;
            float bestScore = -1f;
            for (int pass = 0; pass < 2 && best == null; pass++)
                foreach (var e in FoodBook.Edibles)
                {
                    if (e.raw != (pass == 1) || DishSaved(e.res)) continue;
                    if (StoreFree(e.res) <= 0) continue;
                    float score = FoodBook.Fill(e.res) + 0.01f * (FoodBook.MoodPerDay(e.res) + FoodBook.WorkBonus(e.res));
                    if (score > bestScore) { bestScore = score; best = e.res; }
                }
            return best;
        }

        /// The eating pass of `Step`, replacing the old settle-from-the-pile:
        /// everybody drains; a hungry hand with nothing in his arms starts a
        /// store -> store trip for one unit of `BestMeal` (`eating`), eaten on
        /// arrival in `DepositHaul`. Mood keeps its old shape: an empty
        /// stomach drops it, a fed hand climbs back (half rations never
        /// recover), plus the last meal's bonus while he is fed.
        void EatStep(float days)
        {
            if (hands == null || hands.Count == 0) return;
            bool starved = rations == Rations.None;
            float drain = EatPerHandPerDay * (rations == Rations.Half ? 0.5f : 1f) * days;
            float hungryBelow = EconomyTuning.HungryBelow;
            bool anyEmpty = false;
            for (int hi = 0; hi < hands.Count; hi++)
            {
                var h = hands[hi];
                if (h == null) continue;
                h.full = Mathf.Max(0f, h.full - drain);

                if (!starved && h.full < hungryBelow && !h.eating && !h.walkingIn
                    && (!h.Hauling || !h.haulPicked))
                {
                    string meal = BestMeal();
                    if (meal != null)
                    {
                        // A trip only planned (walking out empty-handed) is
                        // dropped for the meal; a load in his arms is
                        // finished first (he is caught on a later step).
                        if (h.Hauling) CancelPlanned(h);
                        StartTimedTrip(h, meal, 1, HaulPlace.Store, -1, HaulPlace.Store, -1);
                        h.eating = true;
                    }
                }

                bool empty = h.full <= 0f;
                if (starved || empty)
                {
                    anyEmpty = true;
                    h.mood = Mathf.Max(0f, h.mood - MoodDropPerHungryDay * days);
                }
                else
                {
                    h.mood = rations == Rations.Half
                        ? Mathf.Max(0f, h.mood - MoodDropPerHungryDay * 0.5f * days)
                        : Mathf.Min(1f, h.mood + MoodRecoverPerFedDay * days);
                    float bonus = FoodBook.MoodPerDay(h.lastMeal);
                    if (bonus != 0f) h.mood = Mathf.Clamp01(h.mood + bonus * days);
                }

                // **Warmth, 2026-09-27.** A hand in a Hut within
                // `WarmHutRadius` climbs a little further toward content.
                if (IsHandWarm(hi))
                    h.mood = Mathf.Min(1f, h.mood + WarmMoodBonusPerDay * days);
            }
            if (anyEmpty) { hungerDays += days; away.hungryDays += days; }
        }

        /// **The meal is eaten where it was picked up** (the store), on
        /// arrival: fullness up by its fill, its bonus now his. A meal that
        /// was never picked up (somebody beat him to it) is simply no meal.
        void EatMeal(OutpostHand h)
        {
            if (!h.haulPicked) { CancelPlanned(h); h.eating = false; return; }
            float fill = FoodBook.Fill(h.haulRes) * h.haulCount;
            h.full = Mathf.Min(MaxFull, h.full + fill);
            h.lastMeal = h.haulRes;
            foodEaten += fill;
            away.eaten += fill;
            ClearHaul(h);
            h.eating = false;
        }

        /// Fullness a rich dish can take a hand to (a stew tops him over 1).
        public const float MaxFull = 1.5f;

        /// Work pace bonus from the last meal while he is fed.
        public static float MealWorkBonus(OutpostHand h) =>
            h == null || h.full <= 0f ? 0f : FoodBook.WorkBonus(h.lastMeal);

        // --- farm plots --------------------------------------------------------

        /// Every standing farm has its plots (6/9/12 by its level); a farm
        /// pulled down loses its plots. New plots start on potato, repeat,
        /// so a raised farm with a farmhand feeds the camp without a menu.
        void EnsurePlots()
        {
            if (plots == null) plots = new List<FarmPlot>();
            int farms = CountBuilt(BuildPlans.Farm.id);
            for (int i = plots.Count - 1; i >= 0; i--)
                if (plots[i] == null || plots[i].farm >= farms) plots.RemoveAt(i);
            for (int f = 0; f < farms; f++)
            {
                int want = EconomyTuning.PlotsAt(LevelOf(BuildPlans.Farm.id, f));
                int have = 0;
                foreach (var p in plots) if (p.farm == f) have++;
                for (int k = have; k < want; k++)
                    plots.Add(new FarmPlot { farm = f, slot = k, crop = Res.Potato, repeat = true });
            }
        }

        public List<FarmPlot> PlotsOf(int farm)
        {
            var list = new List<FarmPlot>();
            if (plots == null) return list;
            foreach (var p in plots) if (p != null && p.farm == farm) list.Add(p);
            list.Sort((a, b) => a.slot.CompareTo(b.slot));
            return list;
        }

        void GrowPlots(float days)
        {
            if (plots == null) return;
            foreach (var p in plots)
            {
                if (p == null || p.state != PlotState.Growing) continue;
                p.grown += days;
                if (p.grown >= FoodBook.GrowDays(p.crop)) p.state = PlotState.Ripe;
            }
        }

        /// Can this plot's farm grow `crop` yet (farm level gate).
        public bool CropUnlocked(FarmPlot p, string crop, out string why)
        {
            why = null;
            var c = FoodBook.Crop(crop);
            if (c == null) { why = "not a crop"; return false; }
            int lv = LevelOf(BuildPlans.Farm.id, p != null ? p.farm : 0);
            if (lv < c.farmLevel) { why = $"farm {Roman(c.farmLevel)}"; return false; }
            return true;
        }

        static string Roman(int n) => n switch { 1 => "I", 2 => "II", 3 => "III", 4 => "IV", _ => n.ToString() };

        /// **The player picks a crop for a plot.** A growing plot is dug up
        /// (nothing to harvest yet); a ripe one keeps its harvest and takes
        /// the new crop after. Empty string clears the plot.
        public bool SetPlotCrop(FarmPlot p, string crop, bool repeat)
        {
            if (p == null) return false;
            if (!string.IsNullOrEmpty(crop) && !CropUnlocked(p, crop, out _)) return false;
            if (p.state == PlotState.Growing && p.crop != crop) { p.state = PlotState.Empty; p.grown = 0f; }
            if (p.state != PlotState.Ripe) p.work = 0f;
            p.crop = crop ?? "";
            p.repeat = repeat;
            return true;
        }

        FarmPlot NextPlotJob(int farm)
        {
            FarmPlot plant = null;
            foreach (var p in plots)
            {
                if (p == null || p.farm != farm) continue;
                if (p.state == PlotState.Ripe && RoomFor(p.crop) > 0) return p;
                if (plant == null && p.state == PlotState.Empty && !string.IsNullOrEmpty(p.crop)
                    && CropUnlocked(p, p.crop, out _)) plant = p;
            }
            return plant;
        }

        /// **A farmhand's quantum**: at the farm, harvest the ripe plots (the
        /// store has room) and plant the empty ones that have a crop, each a
        /// few seconds of stationary work; every harvest goes into his arms
        /// and is walked to the store, counting there. Walking is not
        /// scaled; the plot work is paid like the old harvest (food work is
        /// never docked by hunger).
        void FarmDay(OutpostHand h, float days)
        {
            int f = Mathf.Max(0, OrdinalOfHand(h));
            float scale = WorkFactorOn(h, Res.Potato) * PriorityMultiplier(Res.Potato) * (1f + MealWorkBonus(h));
            float budget = days * scale;
            bool hasPost = PlanPlace(h.target, f, out var postAt);
            if (hasPost && !WalkTo(h, postAt, ref budget, scale)) return;
            for (int guard = 0; guard < 32 && budget > Eps; guard++)
            {
                var p = NextPlotJob(f);
                if (p == null) return;
                bool ripe = p.state == PlotState.Ripe;
                float secs = (ripe ? EconomyTuning.HarvestSeconds : EconomyTuning.PlantSeconds) - p.work;
                float need = SecondsToDays(Mathf.Max(0f, secs));
                if (budget < need - Eps)
                {
                    p.work += budget * TimeOfDay.DayLength;
                    return;
                }
                budget -= need;
                p.work = 0f;
                if (!ripe)
                {
                    p.state = PlotState.Growing;
                    p.grown = 0f;
                    continue;
                }
                string crop = p.crop;
                var def = FoodBook.Crop(crop);
                p.state = PlotState.Empty;
                p.grown = 0f;
                if (!p.repeat) p.crop = "";
                int n = def != null ? def.yield : 1;
                h.basket = n;
                CarryBasket(h, crop, hasPost, postAt);
                if (h.Hauling) return;
            }
        }

        /// Why the farmhand is standing about, or null.
        public string FarmStallCause(OutpostHand h)
        {
            int f = Mathf.Max(0, OrdinalOfHand(h));
            bool any = false, ripeFull = false;
            foreach (var p in plots)
            {
                if (p == null || p.farm != f) continue;
                if (!string.IsNullOrEmpty(p.crop) || p.state != PlotState.Empty) any = true;
                if (p.state == PlotState.Ripe && RoomFor(p.crop) <= 0) ripeFull = true;
            }
            if (!any) return "no crops picked: choose one on the farm sheet";
            if (NextPlotJob(f) != null || h.Hauling) return null;
            if (ripeFull) return "store is full; the harvest waits";
            return null;   // waiting on growth is not a stall
        }
    }
}
