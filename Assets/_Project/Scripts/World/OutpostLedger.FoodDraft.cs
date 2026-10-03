using System.Collections.Generic;
using UnityEngine;
using SeaSick.World.Economy;

namespace SeaSick.World
{
    /// <summary>
    /// **The food draft: ONE rule (2026-10-03, villager review group 3).**
    /// Under `FedDays` (3 days) of food, free hands -- no job, or a builder
    /// the idle ladder enlisted; never one the player sent, his reserve, a
    /// runner, a hand on other work, one he sent back -- are put on food,
    /// in this order: every Farm copy with a crop and nobody on it, every
    /// Kitchen copy with no cook that has something to cook, then (when
    /// nobody farms, or under a day of food) hunting if a hunt can start,
    /// else wild forage, up to a hand in four on gathering food. One banner
    /// ("Food low — Pip farming, Gale foraging", `FoodDraftNotice`), one
    /// "Undo" (`UndoFoodDraft`, a veto saved on the hand until the draft
    /// ends), one end (`EndFoodDraft`): at `FoodSafeDays`, or fed with
    /// nothing the posts make left room in the store. Then every drafted
    /// hand goes back to the job he had -- he was drafted only from "no
    /// job" or the idle ladder's building, and "no job" is where the ladder
    /// picks him up again (`EnlistFree` runs right after, same `Step`) --
    /// and every kitchen spot the draft re-ordered gets the player's own
    /// recipe back (`SpotState.draftOrder`).
    ///
    /// History: 2026-09-28 the hunger draft (hunt, else forage, under one
    /// day, home at `FedDays`, with a one-game-day unsaved veto);
    /// 2026-10-02 the food emergency (farm, then kitchen, under `FedDays`,
    /// home at `FoodSafeDays`, Kevin: *"so often I'll leave with food going
    /// up, and come back to 0 food"*). They ran side by side with their own
    /// thresholds and banners until they were merged here.
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

        /// **The hands out on the food draft right now**: farming or cooking
        /// (`autoStation`), hunting or foraging (`autoFood`). Rebuilt per call.
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
        /// the draft leaves him alone until it ends (`foodVeto`, saved). A
        /// beast he was only stalking is let be; a carcass already on his
        /// shoulders, or an armful of forage, is still carried home (goods
        /// count on arrival, and the hauler pass walks a load in any idle
        /// hand's arms).
        public void UndoFoodDraft(OutpostHand h)
        {
            if (h == null) return;
            // The draft's farmhand or cook (2026-10-02), same undo.
            if (h.autoStation)
            {
                h.autoStation = false;
                if (FoodStationOrder(h))
                { h.order = OutpostOrder.Idle; h.target = ""; h.workPin = 0; h.playerIdle = false; }
                h.foodVeto = true;
                // His kitchen's dish goes back to the player's (2026-10-03).
                RestoreDraftOrders(true);
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
            h.foodVeto = true;
        }

        /// A food-draft order: Gather on the hunt or on wild forage.
        static bool FoodDraftOrder(OutpostHand h) =>
            h != null && h.order == OutpostOrder.Gather && (h.target == Res.Game || h.target == Res.Food);

        static bool FoodVetoed(OutpostHand h) => h != null && h.foodVeto;

        /// **Could a forage trip start right now** -- `StartGatherTrip`'s
        /// own arithmetic for `Res.Food`: something standing nobody is
        /// already walking to pick, and a store to take it (never full since
        /// 2026-10-03, infinite stacking).
        bool ForageCanStart() =>
            Res.IsGatherable(Res.Food) && FieldFree(Res.Food) >= 1 && KeepsAnything;

        // --- the posts: farm and kitchen (2026-10-02) ---------------------------
        //
        // The trace (Dev/Editor/FoodCollapseTrace) found the camp nobody
        // farmed: the hunger draft only ever sent one forager, and wild
        // forage regrows ~1.6 fill a sky day against the 5 five hands eat --
        // mood 0 by sky day 13 of a 12 h absence, for good. With a farmhand
        // and a cook the same camp holds its food and its mood for all 90 sky
        // days. So the draft fills the Farm first, then the Kitchen (with an
        // order it can cook), one per copy; forage is the fallback. Same code
        // live and in a catch-up (it runs from `Step`).

        /// Days of food at which the draft ends and every drafted hand goes
        /// back -- twice `FedDays`, so nobody is swapped in and out every
        /// quantum around the line.
        public const float FoodSafeDays = 6f;

        /// **Is food really short** -- under `FedDays` of eating in store and
        /// on the racks (`FoodFill`, the top bar's own count: it turns ice
        /// under 3 days). The one test for "Food low" / "food emergency"
        /// words (2026-10-02): a hand the emergency staffed stays on his post
        /// above it (`FoodSafeDays`, hysteresis) but nothing says "low" then.
        public bool FoodShort =>
            hands != null && hands.Count > 0 && FoodFill() < hands.Count * EatPerHandPerDay * FedDays;

        // (`FoodPostsCanBank`, the "fed with no room left" end, was removed
        // 2026-10-03 with the island store's cap -- Kevin: infinite stacking.)

        /// On the Farm or at the Kitchen (the posts the emergency fills).
        static bool FoodStationOrder(OutpostHand h) =>
            h != null && h.order == OutpostOrder.Work
            && (h.target == BuildPlans.Farm.id || h.target == BuildPlans.Kitchen.id);

        /// **The draft is over** (fed, `FeedFirst`): every hand it put on
        /// food goes back to "no job" -- a SYSTEM release, so `EnlistFree`
        /// and the idle ladder take him at once, which is the job he had
        /// (the draft only takes "no job" hands and the ladder's builders).
        /// One the player has re-ordered since keeps his order; one with a
        /// load in his arms lands it first (next quantum). Every veto ends
        /// with the draft, and every kitchen spot the draft re-ordered gets
        /// the player's recipe back (`RestoreDraftOrders`).
        void EndFoodDraft()
        {
            foreach (var h in hands)
            {
                if (h == null) continue;
                h.foodVeto = false;
                if (h.autoStation)
                {
                    if (!FoodStationOrder(h)) h.autoStation = false;
                    else if (!h.Hauling)
                    {
                        h.autoStation = false;
                        h.order = OutpostOrder.Idle;
                        h.target = "";
                        h.workPin = 0;
                        h.playerIdle = false;
                    }
                }
                if (h.autoFood)
                {
                    h.autoFood = false;
                    // Re-ordered by the player since? Their order stands.
                    if (FoodDraftOrder(h))
                    { h.order = OutpostOrder.Idle; h.target = ""; h.playerIdle = false; }
                }
            }
            RestoreDraftOrders();
        }

        /// **A hunter who cannot hunt goes back to "no job" (2026-09-28).**
        /// Drafted when there was a spear and a beast; if either is gone (or
        /// the store has no room for a carcass) and he is not already out on
        /// a trip, he would stand "hunting" at the fire for good. Sent back
        /// as a SYSTEM release, never as the player's reserve, so
        /// `EnlistFree` puts him on the sites this same step. **A drafted
        /// forager the same (2026-09-28)**: nothing left standing or no room.
        void ReleaseStuckFoodGatherers()
        {
            foreach (var h in hands)
                if (h != null && h.autoFood && !h.Hauling && h.order == OutpostOrder.Gather
                    && ((h.target == Res.Game && !HuntCanStart(h))
                        || (h.target == Res.Food && !ForageCanStart())))
                { h.autoFood = false; h.order = OutpostOrder.Idle; h.target = ""; h.playerIdle = false; }
        }

        /// **The fallback: hunt, else forage (2026-09-28)** -- while nobody
        /// works a farm, or under a day of food (`starving`: a farm's first
        /// crop is days off). Up to a hand in four (at least one) on
        /// gathering food, drafted or not. **Only a hunt that can start** --
        /// `StartHuntTrip`'s own checks: a spear in the pile, room for a
        /// carcass, and a beast nobody is already after; one draft per such
        /// beast (a hand drafted without them stood at the fire "hunting"
        /// forever). **No hunt: forage** -- a Gather order on `Res.Food`,
        /// the order a player can give, when something stands to pick and
        /// the store has room. No job first, then the ladder's builders --
        /// never a builder the player sent (`PlayerBuilder`), his reserve,
        /// or a hand he sent back (`foodVeto`).
        void DraftFoodGatherers(bool starving)
        {
            if (!starving && HandsOn(OutpostOrder.Work, BuildPlans.Farm.id) > 0) return;
            int feeding = 0;
            foreach (var h in hands)
                if (h != null && h.order == OutpostOrder.Gather && (h.target == Res.Game || h.target == Res.Food))
                    feeding++;
            int want = Mathf.Max(1, hands.Count / 4);
            if (feeding >= want) return;
            var game = Stock(Res.Game);
            bool hunt = game != null && game.standing >= 1f && HuntCanStart(null);
            if (!hunt && !ForageCanStart()) return;
            string res = hunt ? Res.Game : Res.Food;
            int limit = hunt ? GameUnclaimed(null) : int.MaxValue;
            int drafted = 0;
            for (int pass = 0; pass < 2 && feeding < want && drafted < limit; pass++)
                foreach (var h in hands)
                {
                    if (feeding >= want || drafted >= limit) break;
                    if (h == null || h.Busy || Reserve(h) || FoodVetoed(h)) continue;
                    var from = pass == 0 ? OutpostOrder.Idle : OutpostOrder.Build;
                    if (h.order != from || PlayerBuilder(h)) continue;
                    h.order = OutpostOrder.Gather;
                    h.target = res;
                    h.autoFood = true;
                    h.playerIdle = false;
                    feeding++;
                    drafted++;
                    draftNames.Add(h.name + (hunt ? " hunting" : " foraging"));
                }
        }

        /// Under `FedDays`: a free hand on every Farm copy with no worker
        /// (if it has a crop picked), then every Kitchen copy with no cook
        /// (if it has, or can be given, something to cook). Free = no job,
        /// a builder the idle ladder enlisted, or a hand the draft sent
        /// foraging/hunting -- never a builder the player sent, the
        /// player's reserve, a runner, a hand the player put on other work,
        /// one he sent back (`UndoFoodDraft`), busy, or carrying.
        void StaffFoodStations()
        {
            int farms = CountBuilt(BuildPlans.Farm.id);
            for (int f = 0; f < farms; f++)
            {
                if (WorkersAt(BuildPlans.Farm.id, f) >= StationCapacity(BuildPlans.Farm.id, f)) continue;
                if (!FarmHasCrop(f)) continue;
                var h = FreeFoodHand();
                if (h == null) break;
                PutOnFoodPost(h, BuildPlans.Farm.id, f);
                draftNames.Add(h.name + " farming");
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
                draftNames.Add(h.name + " cooking");
            }
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
        /// **Never a builder the PLAYER sent (2026-10-02, `PlayerBuilder`)**
        /// -- Kevin: *"you assign someone somewhere, thats what they do"*;
        /// only the idle ladder's builders are free for a food post.
        OutpostHand FreeFoodHand()
        {
            for (int pass = 0; pass < 4; pass++)
                foreach (var h in hands)
                {
                    if (h == null || h.Busy || h.downed || h.Hauling || Reserve(h) || PlayerBuilder(h) || FoodVetoed(h)) continue;
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
        /// otherwise the best dish whose inputs are already held is put on
        /// repeat, else baked potato when somebody is farming.
        /// **The player's dish is kept (2026-10-03, villager review):** the
        /// draft picks a dish for an EMPTY spot (or one it filled itself)
        /// first; only when every such dish's spot holds the player's
        /// recipe does it take that spot, and then it remembers his recipe
        /// and gives it back when the draft ends (`DraftSelect`,
        /// `RestoreDraftOrders`).
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
            for (int pass = 0; pass < 2 && best == null; pass++)
            {
                float bestFill = -1f;
                foreach (var r in Economy.Recipes.At(BuildPlans.Kitchen.id))
                {
                    if (!CanCookHere(r, level) || !InputsHeld(r)) continue;
                    if (pass == 0 && !DraftMayUse(st, r)) continue;
                    float fill = FoodBook.Fill(r.makes) * r.yield;
                    if (fill > bestFill) { bestFill = fill; best = r; }
                }
            }
            if (best == null && farmed && !needInputs)
            {
                var potato = Economy.Recipes.Named("baked-potato");
                if (potato != null && CanCookHere(potato, level)) best = potato;
            }
            return best != null && DraftSelect(st, best);
        }

        /// The spot `r` is cooked on is empty, or the draft's own.
        static bool DraftMayUse(StationStock st, Economy.Recipe r)
        {
            var sp = st != null ? st.SpotAt(StationSpots.SpotIndexOf(r)) : null;
            return sp != null && (!sp.Selected || sp.draftOrder);
        }

        /// **Select `r` for the draft**, remembering what the player had on
        /// that spot (once: a spot the draft already holds keeps its first
        /// memory) so `RestoreDraftOrders` can give it back.
        bool DraftSelect(StationStock st, Economy.Recipe r)
        {
            int idx = StationSpots.SpotIndexOf(r);
            var sp = st.SpotAt(idx);
            if (sp == null) return false;
            bool had = sp.draftOrder;
            string kept = had ? sp.draftKept : (sp.recipeId ?? "");
            int keptCount = had ? sp.draftKeptCount : sp.count;
            if (!SetSpot(st, idx, r.id, 0, out _)) return false;
            sp.draftOrder = true;
            sp.draftKept = kept ?? "";
            sp.draftKeptCount = keptCount;
            return true;
        }

        /// **Every kitchen spot the draft still holds gets the player's
        /// recipe back** (or is stopped, if it was empty) -- the draft's end.
        /// A spot anybody re-selected or stopped since is his already.
        /// `idleOnly`: just the kitchens the draft no longer has a cook at
        /// (the player sent him back, `UndoFoodDraft`).
        void RestoreDraftOrders(bool idleOnly = false)
        {
            if (stations == null) return;
            foreach (var st in stations)
            {
                if (st == null || st.removed || st.planId != BuildPlans.Kitchen.id) continue;
                if (idleOnly && EmergencyCookAt(st.ordinal)) continue;
                var spots = st.Spots;
                for (int i = 0; i < spots.Count; i++)
                {
                    var sp = spots[i];
                    if (sp == null || !sp.draftOrder) continue;
                    string kept = sp.draftKept;
                    int keptCount = sp.draftKeptCount;
                    sp.draftOrder = false;
                    sp.draftKept = "";
                    sp.draftKeptCount = 0;
                    if (string.IsNullOrEmpty(kept) || !SetSpot(st, i, kept, keptCount, out _))
                        StopSpot(st, i);
                }
            }
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

        /// **One sentence per draft batch**, the words `CampStatusHud`
        /// rebuilds after a reload: "Food low — Pip farming, Gale foraging".
        void AnnounceFoodDraft()
        {
            if (draftNames.Count == 0) return;
            string msg = "Food low — " + string.Join(", ", draftNames);
            try { FoodDraftNotice?.Invoke(msg); }
            catch (System.Exception e) { Debug.LogException(e); }
        }
    }
}
