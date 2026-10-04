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
        /// **Units still standing on a part-harvested ripe plot**; 0 = not
        /// touched yet (the crop's whole `yield`). 2026-10-03 (review fix
        /// C): a farmhand only lifts what the store has room for and the
        /// rest stays in the field, ripe, until there is room -- nothing is
        /// carried that cannot be put down, nothing is lost. Old saves: 0.
        /// **Legacy since 2026-10-03** (Kevin: infinite stacking): the
        /// harvest now lifts the whole yield and `FarmDay` writes 0, so a
        /// fresh game never sets it. Kept (and honoured by `RipeUnits`) only
        /// so a save written in the few hours it was live still shows and
        /// picks the units that are really standing, not the full yield.
        public int left;

        /// **The crop queued behind a ripe harvest (2026-10-04, Kevin's farm
        /// bug 2).** Picking another crop on a ripe (or part-ripe) plot used
        /// to rewrite `crop` at once, so the standing wheat turned into
        /// potatoes (same units, new label: goods invented/lost). Now `crop`
        /// is only ever what is physically standing or growing; the pick
        /// waits here and `Harvested()` swaps it in the moment the harvest is
        /// picked, ready to be planted. Empty = nothing queued (every old
        /// save: JsonUtility leaves a missing string at the initialiser).
        public string nextCrop = "";

        public bool HasNext => !string.IsNullOrEmpty(nextCrop);

        /// **The harvest has been picked: bookkeeping for the bare plot.**
        /// One door for `FarmDay` (and the self-test). A queued crop takes
        /// the plot over (even with repeat off: the player asked for it);
        /// otherwise a plot not on repeat is cleared, a plot on repeat keeps
        /// its crop to be replanted. Returns the crop that was standing.
        public string Harvested()
        {
            string was = crop;
            left = 0;
            state = PlotState.Empty;
            grown = 0f;
            work = 0f;
            if (HasNext) { crop = nextCrop; nextCrop = ""; }
            else if (!repeat) crop = "";
            return was;
        }

        /// **The plot tile's second line** (and the one place any readout
        /// of a plot's yield comes from, 2026-10-04, farm bug 1): a ripe
        /// plot shows `RipeUnits`, what is actually still standing, never
        /// the crop's full yield; a queued crop adds "next: potato".
        public string StateLine(System.Func<string, string> label, string growClock)
        {
            string s = state == PlotState.Ripe ? $"ripe · {RipeUnits} to pick"
                : state == PlotState.Growing ? $"{growClock} left"
                : string.IsNullOrEmpty(crop) ? "tap to plant" : "to plant";
            if (HasNext && label != null) s += $"\nnext: {label(nextCrop)}";
            return s;
        }

        /// What a harvest of this ripe plot still gives (`left`, or the
        /// crop's whole yield when untouched).
        public int RipeUnits
        {
            get
            {
                if (left > 0) return left;
                var def = FoodBook.Crop(crop);
                return def != null ? Mathf.Max(1, def.yield) : 1;
            }
        }

        public float Grow01 => state == PlotState.Ripe ? 1f
            : state == PlotState.Growing ? Mathf.Clamp01(grown / Mathf.Max(1e-5f, FoodBook.GrowDays(crop))) : 0f;

        /// Real seconds until ripe (0 when ripe or empty).
        public float SecondsLeft => state != PlotState.Growing ? 0f
            : Mathf.Max(0f, (FoodBook.GrowDays(crop) - grown) * TimeOfDay.WorkDaySeconds);
    }

    /// **Food: plots, eating by fill, the food total (2026-09-27).** The
    /// approved Melvor-style rework (docs/GDD.md "Food"): crops grow per plot
    /// on real-minute timers and the farmhand carries each harvest to the
    /// store; the kitchen cooks dishes by order; the camp eats once a day,
    /// at supper by the fire (2026-10-02, OutpostLedger.Supper.cs): the best
    /// dishes first, raw at a quarter only once nothing cooked is left.
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

        /// **Fill units of food the camp can eat** -- one is a hand fed for a
        /// day. The store, and **station output racks since 2026-09-30**
        /// (fish in the fishing hut's box, a dish on the kitchen's rack:
        /// supper is served from either, `ServeSupper`), never a bay.
        /// Saved dishes are left out unless asked for. `storeOnly`: the
        /// store's own, for a view drawing the store (`StoreStockView`).
        public float FoodFill(bool includeSaved = false, bool storeOnly = false)
        {
            float f = 0f;
            foreach (var e in FoodBook.Edibles)
            {
                if (!includeSaved && DishSaved(e.res)) continue;
                var s = Store(e.res);
                float n = s != null ? s.whole + s.part : 0f;
                if (!storeOnly) n += RackCountOf(e.res);
                f += n * FoodBook.Fill(e.res);
            }
            return f;
        }

        /// Fill of cooked dishes only (store and racks, as `FoodFill`).
        public float CookedFill()
        {
            float f = 0f;
            foreach (var e in FoodBook.Edibles)
            {
                if (e.raw || DishSaved(e.res)) continue;
                var s = Store(e.res);
                float n = (s != null ? s.whole + s.part : 0f) + RackCountOf(e.res);
                f += n * FoodBook.Fill(e.res);
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
                    // Store, then racks (`Take`'s own order), 2026-09-30.
                    while (paid < fill - 1e-4f && StoreFree(e.res) + RackFree(e.res) > 0)
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

        /// **The next dish supper serves** (from the store or a rack): the
        /// best allowed dish (highest fill, then its bonus) with a unit nobody
        /// is already walking to take; raw only when no dish is left. Null
        /// when there is nothing edible left.
        public string BestMeal()
        {
            string best = null;
            float bestScore = -1f;
            for (int pass = 0; pass < 2 && best == null; pass++)
                foreach (var e in FoodBook.Edibles)
                {
                    if (e.raw != (pass == 1) || DishSaved(e.res)) continue;
                    // A free unit in the store or on a rack (2026-09-30).
                    if (StoreFree(e.res) + RackFree(e.res) <= 0) continue;
                    float score = MealScore(e.res);
                    if (score > bestScore) { bestScore = score; best = e.res; }
                }
            return best;
        }

        /// How good a dish is: its fill, then its bonuses as the tie-break.
        static float MealScore(string res) =>
            FoodBook.Fill(res) + 0.01f * (FoodBook.MoodPerDay(res) + FoodBook.WorkBonus(res));

        /// **The eating pass of `Step`** (supper since 2026-10-02, Kevin:
        /// "they all eat one time during the day, supper time together by
        /// the fire before they go to sleep"). Nobody walks to the store to
        /// eat any more: the supper bell (`SupperBellAt`) serves the whole
        /// camp out of the books (`ServeSupper`), and every quantum drains
        /// the stomachs and drifts the mood.
        ///
        /// **Mood keys off the supper, not the stomach.** With one meal a
        /// day `full` runs down to about 0 right as the next bell rings, so
        /// an empty-stomach test would dock a perfectly fed camp every
        /// evening. Instead `supperHunger` (what the last supper fell short
        /// of a full day, ration cut included) bites through the day after
        /// it, in proportion: `MoodDropPerHungryDay x supperHunger` a sky
        /// day, recovery only when he was fed in full. That reproduces the
        /// old rates exactly -- half rations 0.25 a day, none 0.5, a fed
        /// camp climbs 0.25 -- and an under-fed supper costs what that much
        /// empty time used to. `full` stays as the gauge the sheets draw.
        void EatStep(float workDays, double atSeconds = double.NaN)
        {
            if (hands == null || hands.Count == 0) return;
            SettleOldMealTrips();
            if (SupperBellAt(workDays, atSeconds, out int bellDay)) ServeSupper(bellDay);

            // **Needs run on the SKY's day (2026-09-29).** `workDays` is the
            // ledger's step (fixed 180 s days, the unit production is priced
            // in); eating and mood are "per day" of the sun, which is
            // `TimeOfDay.DayLength` (480 s) -- so a hand eats one fill a sky
            // day and his mood drifts per sky day, while every bench and
            // field keeps its real-time pace. `hungerDays`/`hungryDays` are
            // sky days too (the sheets turn them into spans with DayLength).
            float days = workDays * TimeOfDay.SkyDaysPerWorkDay;
            bool starved = rations == Rations.None;
            float rationCut = 1f - EatMultiplier;
            float drain = EatPerHandPerDay * (rations == Rations.Half ? 0.5f : 1f) * days;
            bool anyHungry = false;
            for (int hi = 0; hi < hands.Count; hi++)
            {
                var h = hands[hi];
                if (h == null) continue;
                // **Downed (death/rescue phase 1): no eating, no hunger
                // drain.** A hand lying where he fell is not at supper, and
                // his stomach is not the camp's problem right now --
                // docs/PLAN-DEATH-RESCUE.md, "Deaths".
                if (h.downed) continue;
                h.full = Mathf.Max(0f, h.full - drain);

                float hunger = starved ? 1f : Mathf.Clamp01(h.supperHunger);
                if (hunger > 1e-3f)
                    h.mood = Mathf.Max(0f, h.mood - MoodDropPerHungryDay * hunger * days);
                else
                    h.mood = Mathf.Min(1f, h.mood + MoodRecoverPerFedDay * days);
                if (hunger < 1f - 1e-3f)
                {
                    float bonus = FoodBook.MoodPerDay(h.lastMeal);
                    if (bonus != 0f) h.mood = Mathf.Clamp01(h.mood + bonus * days);
                }
                // Hungry for the away card = short of what the rations
                // promise (half rations by choice is not "went hungry").
                if (starved || hunger - rationCut > 0.01f) anyHungry = true;

                // **Warmth, 2026-09-27.** A hand in a Hut within
                // `WarmHutRadius` climbs a little further toward content.
                if (IsHandWarm(hi))
                    h.mood = Mathf.Min(1f, h.mood + WarmMoodBonusPerDay * days);
            }
            if (anyHungry) { hungerDays += days; away.hungryDays += days; }
        }

        /// **An old save's daytime meal trip** (2026-10-02): nothing starts
        /// one any more, so one found in flight is settled on the spot -- a
        /// meal already in his arms is eaten (`EatMeal`), a planned one is
        /// dropped (the store still has it). Then `eating` is never set again.
        void SettleOldMealTrips()
        {
            for (int i = 0; i < hands.Count; i++)
            {
                var h = hands[i];
                if (h == null || !h.eating) continue;
                if (h.Hauling) EatMeal(h);
                else h.eating = false;
            }
        }

        /// **An old save's meal trip, eaten where it was picked up** (also
        /// `DepositHaul`'s branch for it): fullness up by its fill, its bonus
        /// now his. A meal that was never picked up is simply no meal.
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
        /// Fed = got something at the last supper (2026-10-02; was "stomach
        /// not empty", which one meal a day empties every evening).
        public static float MealWorkBonus(OutpostHand h) =>
            h == null || h.supperHunger >= 1f - 1e-3f ? 0f : FoodBook.WorkBonus(h.lastMeal);

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
        /// (nothing to harvest yet, nothing lost). **A ripe (or part-ripe)
        /// plot never converts its standing harvest (2026-10-04 rule):** the
        /// ripe crop keeps its identity and units, the pick is QUEUED in
        /// `nextCrop` ("next: potato" on the tile) and is planted after the
        /// farmhand picks the harvest (`FarmPlot.Harvested`). Picking the
        /// crop that is already standing cancels a queued one. Empty string
        /// = clear: instant on a bare/growing plot; on a ripe one the
        /// harvest is still picked, then the plot is left bare (a queued
        /// crop is dropped and repeat goes off).
        public bool SetPlotCrop(FarmPlot p, string crop, bool repeat)
        {
            if (p == null) return false;
            crop = crop ?? "";
            if (crop.Length > 0 && !CropUnlocked(p, crop, out _)) return false;
            if (p.state == PlotState.Ripe)
            {
                if (crop.Length == 0) { p.nextCrop = ""; p.repeat = false; }
                else if (crop == p.crop) p.nextCrop = "";
                else p.nextCrop = crop;
                if (crop.Length > 0) p.repeat = repeat;
                return true;
            }
            p.nextCrop = "";
            if (p.state == PlotState.Growing && p.crop != crop) { p.state = PlotState.Empty; p.grown = 0f; }
            p.work = 0f;
            p.crop = crop;
            p.repeat = repeat;
            return true;
        }

        /// The "replant on repeat" switch alone (2026-10-04): it used to ride
        /// on `SetPlotCrop(p, p.crop, ..)`, which now means "cancel the
        /// queued crop" on a ripe plot, so the toggle has its own door.
        public void SetPlotRepeat(FarmPlot p, bool repeat)
        {
            if (p != null) p.repeat = repeat;
        }

        FarmPlot NextPlotJob(int farm)
        {
            FarmPlot plant = null;
            foreach (var p in plots)
            {
                if (p == null || p.farm != farm) continue;
                if (p.state == PlotState.Ripe && KeepsAnything) return p;   // no store cap (2026-10-03)
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
                    p.work += budget * TimeOfDay.WorkDaySeconds;
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
                // **The whole yield (2026-10-03, Kevin: infinite stacking).**
                // Review fix C had him lift only what the store had room for,
                // leaving the rest ripe on the plot (`left`); the island store
                // never fills now, so he harvests all of it and `left` stays 0.
                int n = p.RipeUnits;
                // `Harvested()` returns the crop that was STANDING (the
                // basket's crop) and then applies a queued `nextCrop`
                // (2026-10-04) before the plot is replanted.
                string crop = p.Harvested();
                if (n <= 0) continue;
                h.basket = n;
                CarryBasket(h, crop, hasPost, postAt);
                if (h.Hauling) return;
            }
        }

        /// Why the farmhand is standing about, or null.
        public string FarmStallCause(OutpostHand h)
        {
            int f = Mathf.Max(0, OrdinalOfHand(h));
            bool any = false;
            string fullOf = null;
            foreach (var p in plots)
            {
                if (p == null || p.farm != f) continue;
                if (!string.IsNullOrEmpty(p.crop) || p.state != PlotState.Empty) any = true;
                // Dormant since 2026-10-03 (no store cap): only bare ground.
                if (fullOf == null && p.state == PlotState.Ripe && !KeepsAnything) fullOf = p.crop;
            }
            if (!any) return "no crops picked: choose one on the farm sheet";
            if (NextPlotJob(f) != null || h.Hauling) return null;
            // Name the crop (2026-10-03): which store row is full is the
            // thing the player can act on (cook it, eat it, raise the store).
            if (fullOf != null)
                return $"store is full of {Friendly(fullOf)}; the harvest waits in the field";
            return null;   // waiting on growth is not a stall
        }
    }
}
