using System.Collections.Generic;
using UnityEngine;
using SeaSick.World.Economy;

namespace SeaSick.World
{
    /// <summary>
    /// **The hunger draft, made visible and undoable (2026-09-28, designer
    /// call).** `FeedFirst` (OutpostLedger.cs) drafts free hands onto food
    /// when the pile is under a day's eating: hunting when a hunt can start,
    /// else foraging (`Res.Food`, the wild-forage Gather order). Before this
    /// the player only noticed when his builders were suddenly "hunting";
    /// now each draft batch fires `FoodDraftNotice` with a ready sentence
    /// ("Food low — Finch and Gale sent hunting"), `FoodDrafted` lists who
    /// is out on the draft, and `UndoFoodDraft` sends one back, with a
    /// one-game-day veto so the very next quantum does not re-draft him.
    /// </summary>
    public partial class OutpostLedger
    {
        /// **Fired once per draft batch** with the sentence for a banner.
        /// Raised from `Step` (the ledger tick), on the main thread.
        [field: System.NonSerialized]
        public event System.Action<string> FoodDraftNotice;

        /// Names drafted in the batch `FeedFirst` is making.
        [System.NonSerialized] readonly List<string> draftNames = new List<string>();
        [System.NonSerialized] readonly List<OutpostHand> foodDrafted = new List<OutpostHand>();

        /// Hands the player sent back from a draft -> game-days left before
        /// `FeedFirst` may draft them again. Not saved: a reload forgets it,
        /// which only means one early re-draft.
        [System.NonSerialized] readonly Dictionary<OutpostHand, float> foodVeto =
            new Dictionary<OutpostHand, float>();
        [System.NonSerialized] readonly List<OutpostHand> foodVetoScratch = new List<OutpostHand>();

        /// Game-days an undone draft keeps a hand off the next one.
        public const float FoodVetoDays = 1f;

        /// **The hands out on the hunger draft right now** (`autoFood`):
        /// hunting or foraging on their own initiative. Rebuilt per call.
        public IReadOnlyList<OutpostHand> FoodDrafted
        {
            get
            {
                foodDrafted.Clear();
                if (hands != null)
                    foreach (var h in hands)
                        if (h != null && ((h.autoFood && FoodDraftOrder(h))
                                          || (h.autoStation && FoodStationOrder(h)))) foodDrafted.Add(h);
                return foodDrafted;
            }
        }

        /// **The player says "not him"**: back to "no job" (system Idle,
        /// NOT the player's reserve, so the idle ladder takes him on), and
        /// `FeedFirst` leaves him alone for `FoodVetoDays`. A beast he was
        /// only stalking is let be; a carcass already on his shoulders, or
        /// an armful of forage, is still carried home (goods count on
        /// arrival, and the hauler pass walks a load in any idle hand's arms).
        public void UndoFoodDraft(OutpostHand h)
        {
            if (h == null) return;
            // The food emergency's farmhand or cook (2026-10-02), same undo.
            if (h.autoStation)
            {
                h.autoStation = false;
                if (FoodStationOrder(h))
                { h.order = OutpostOrder.Idle; h.target = ""; h.workPin = 0; h.playerIdle = false; }
                foodVeto[h] = FoodVetoDays;
                return;
            }
            if (!h.autoFood) return;
            h.autoFood = false;
            if (FoodDraftOrder(h))
            {
                if (h.HuntTrip && !h.huntKilled) ClearHaul(h);
                h.order = OutpostOrder.Idle;
                h.target = "";
                h.playerIdle = false;
            }
            foodVeto[h] = FoodVetoDays;
        }

        /// A food-draft order: Gather on the hunt or on wild forage.
        static bool FoodDraftOrder(OutpostHand h) =>
            h != null && h.order == OutpostOrder.Gather && (h.target == Res.Game || h.target == Res.Food);

        bool FoodVetoed(OutpostHand h) => h != null && foodVeto.ContainsKey(h);

        /// One `Step` quantum off every veto; spent ones go.
        void AgeFoodVetoes(float days)
        {
            if (foodVeto.Count == 0) return;
            foodVetoScratch.Clear();
            foodVetoScratch.AddRange(foodVeto.Keys);
            foreach (var h in foodVetoScratch)
            {
                float left = foodVeto[h] - days;
                if (left <= 0f || hands == null || !hands.Contains(h)) foodVeto.Remove(h);
                else foodVeto[h] = left;
            }
        }

        /// **Could a forage trip start right now** -- `StartGatherTrip`'s
        /// own arithmetic for `Res.Food`: something standing nobody is
        /// already walking to pick, and room in the store for it.
        bool ForageCanStart() =>
            Res.IsGatherable(Res.Food) && FieldFree(Res.Food) >= 1 && RoomFor(Res.Food) >= 1;

        // --- the food emergency (2026-10-02) ------------------------------------
        //
        // Kevin: *"so often I'll leave with food going up, and come back to 0
        // food, and it not going up and everyone pouting, and it's like damage
        // control every time I start the game."* The trace (Dev/Editor/
        // FoodCollapseTrace) found the camp nobody farmed: `FeedFirst` only
        // ever sent one forager, and wild forage regrows ~1.6 fill a sky day
        // against the 5 five hands eat -- mood 0 by sky day 13 of a 12 h
        // absence, for good. With a farmhand and a cook the same camp holds
        // its food and its mood for all 90 sky days. So under `FedDays` the
        // free hands staff the Farm first, then the Kitchen (with an order
        // it can cook), one per copy; forage stays the fallback. Same code
        // live and in a catch-up (it runs from `Step`).

        /// Days of food at which the emergency's farmhands and cooks go back
        /// to "no job" -- twice `FedDays`, so they are not swapped in and out
        /// every quantum around the line (the hunters' hysteresis, 1 -> 3).
        public const float FoodSafeDays = 6f;

        /// On the Farm or at the Kitchen (the posts the emergency fills).
        static bool FoodStationOrder(OutpostHand h) =>
            h != null && h.order == OutpostOrder.Work
            && (h.target == BuildPlans.Farm.id || h.target == BuildPlans.Kitchen.id);

        /// Fed again: every hand the emergency put on a food post goes back
        /// to "no job" (a SYSTEM release, so `EnlistFree` and the idle ladder
        /// take him at once). One the player has re-ordered since keeps his
        /// order; one with a load in his arms lands it first.
        void ReleaseFoodStations()
        {
            foreach (var h in hands)
            {
                if (h == null || !h.autoStation) continue;
                if (!FoodStationOrder(h)) { h.autoStation = false; continue; }
                if (h.Hauling) continue;
                h.autoStation = false;
                h.order = OutpostOrder.Idle;
                h.target = "";
                h.workPin = 0;
                h.playerIdle = false;
            }
        }

        /// Under `FedDays`: a free hand on every Farm copy with no worker
        /// (if it has a crop picked), then every Kitchen copy with no cook
        /// (if it has, or can be given, something to cook). Free = no job,
        /// a builder, or a hand the draft sent foraging/hunting -- never the
        /// player's reserve, a runner, a hand the player put on other work,
        /// one he just sent back (`UndoFoodDraft`), busy, or carrying.
        void StaffFoodStations()
        {
            draftNames.Clear();
            int farms = CountBuilt(BuildPlans.Farm.id);
            for (int f = 0; f < farms; f++)
            {
                if (WorkersAt(BuildPlans.Farm.id, f) >= StationCapacity(BuildPlans.Farm.id, f)) continue;
                if (!FarmHasCrop(f)) continue;
                var h = FreeFoodHand();
                if (h == null) break;
                PutOnFoodPost(h, BuildPlans.Farm.id, f);
                draftNames.Add(h.name + " is farming");
            }
            bool farmed = HandsOn(OutpostOrder.Work, BuildPlans.Farm.id) > 0;
            int kitchens = CountBuilt(BuildPlans.Kitchen.id);
            for (int k = 0; k < kitchens; k++)
            {
                // The emergency's own cook stood idle beside 30 potatoes when
                // the grilled meat it had chosen ran out (trace 2026-10-02):
                // at a kitchen IT staffed, an order with nothing to cook is
                // swapped for one whose inputs are here.
                if (EmergencyCookAt(k)) { KitchenCanCook(k, farmed, true); continue; }
                if (WorkersAt(BuildPlans.Kitchen.id, k) >= StationCapacity(BuildPlans.Kitchen.id, k)) continue;
                var h = FreeFoodHand();
                if (h == null) break;
                if (!KitchenCanCook(k, farmed)) continue;
                PutOnFoodPost(h, BuildPlans.Kitchen.id, k);
                draftNames.Add(h.name + " is cooking");
            }
            if (draftNames.Count == 0) return;
            string msg = "Food emergency: " + string.Join(", ", draftNames);
            try { FoodDraftNotice?.Invoke(msg); }
            catch (System.Exception e) { Debug.LogException(e); }
        }

        bool FarmHasCrop(int farm)
        {
            if (plots == null) return false;
            foreach (var p in plots)
                if (p != null && p.farm == farm && (!string.IsNullOrEmpty(p.crop) || p.state != PlotState.Empty))
                    return true;
            return false;
        }

        /// The next free hand for a food post, in the order the passes
        /// prefer: no job, a builder with nothing to build, a drafted
        /// forager or hunter, then any builder. Null: nobody.
        OutpostHand FreeFoodHand()
        {
            for (int pass = 0; pass < 4; pass++)
                foreach (var h in hands)
                {
                    if (h == null || h.Busy || h.downed || h.Hauling || Reserve(h) || FoodVetoed(h)) continue;
                    bool ok = pass switch
                    {
                        0 => h.order == OutpostOrder.Idle,
                        1 => h.order == OutpostOrder.Build && BuildSiteFor(h) == null,
                        2 => h.autoFood && FoodDraftOrder(h),
                        _ => h.order == OutpostOrder.Build,
                    };
                    if (ok) return h;
                }
            return null;
        }

        /// The ledger's own `Outpost.Assign`: everybody already on the plan
        /// is pinned where he stands, then this hand to copy `ordinal`.
        void PutOnFoodPost(OutpostHand h, string planId, int ordinal)
        {
            foreach (var x in hands)
            {
                if (x == null || x == h || x.order != OutpostOrder.Work || x.target != planId) continue;
                int at = OrdinalOfHand(x);
                if (at >= 0) x.workPin = at + 1;
            }
            h.order = OutpostOrder.Work;
            h.target = planId;
            h.workPin = ordinal + 1;
            h.autoFood = false;
            h.autoStation = true;
            h.playerIdle = false;
        }

        /// **Does Kitchen copy `k` have something to cook** -- giving it an
        /// order if it has none. The player's own order stands (worth a cook
        /// when its inputs are here, or the farm is about to bring potatoes);
        /// with no order the best dish whose inputs are already held is put
        /// on repeat, else baked potato when somebody is farming.
        bool KitchenCanCook(int k, bool farmed, bool needInputs = false)
        {
            int si = StationIndex(BuildPlans.Kitchen.id, k);
            var st = StationAt(si);
            if (st == null) return false;
            int level = LevelOf(BuildPlans.Kitchen.id, k);
            foreach (var sp in st.Spots)
            {
                var r = sp != null ? sp.Recipe : null;
                if (r == null || !CanCookHere(r, level)) continue;
                if (InputsHeld(r) || (farmed && !needInputs)) return true;
                // (Mid-batch: leave the bench be.)
                if (sp.BenchBusy) return true;
            }
            Economy.Recipe best = null;
            float bestFill = -1f;
            foreach (var r in Economy.Recipes.At(BuildPlans.Kitchen.id))
            {
                if (!CanCookHere(r, level) || !InputsHeld(r)) continue;
                float fill = FoodBook.Fill(r.makes) * r.yield;
                if (fill > bestFill) { bestFill = fill; best = r; }
            }
            if (best == null && farmed && !needInputs)
            {
                var potato = Economy.Recipes.Named("baked-potato");
                if (potato != null && CanCookHere(potato, level)) best = potato;
            }
            return best != null && PlaceOrder(si, best.id, RepeatOrder);
        }

        /// Kitchen copy `k`'s cook is the emergency's (`autoStation`).
        bool EmergencyCookAt(int k)
        {
            foreach (var h in hands)
                if (h != null && h.autoStation && h.order == OutpostOrder.Work
                    && h.target == BuildPlans.Kitchen.id && OrdinalOfHand(h) == k) return true;
            return false;
        }

        bool CanCookHere(Economy.Recipe r, int level) =>
            r != null && FoodBook.IsDish(r.makes) && level >= r.stationLevel && RecipeAvailable(r, out _);

        bool InputsHeld(Economy.Recipe r)
        {
            if (r.takes == null) return true;
            foreach (var line in r.takes)
                if (line.n > 0 && HeldOf(line.res) < line.n) return false;
            return true;
        }

        void AnnounceFoodDraft(bool hunting)
        {
            if (draftNames.Count == 0) return;
            string who;
            int n = draftNames.Count;
            if (n == 1) who = draftNames[0];
            else if (n == 2) who = draftNames[0] + " and " + draftNames[1];
            else who = string.Join(", ", draftNames.GetRange(0, n - 1)) + " and " + draftNames[n - 1];
            string msg = "Food low — " + who + " sent " + (hunting ? "hunting" : "foraging");
            try { FoodDraftNotice?.Invoke(msg); }
            catch (System.Exception e) { Debug.LogException(e); }
        }
    }
}
