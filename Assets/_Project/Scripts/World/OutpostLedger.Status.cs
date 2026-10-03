using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// <summary>
    /// **What every hand is up to, in words a label can hold, and the camp's
    /// spare-hand errand (2026-09-28, designer call).** Read-only getters
    /// for the UI side (Astra), plus `TopUpDay`, the one piece of new
    /// behaviour here.
    ///
    /// For the UI:
    /// - `StatusWord(h)`   -- one or two words: "Building", "Hauling",
    ///   "Gathering", "Hunting", "Working", "Reserve", "No job",
    ///   "Builder — waiting for stone" (prefix `BuilderWaitPrefix`), "Stuck",
    ///   "Sleeping", "Supper", "Evening",
    ///   "Downed", "Fighting", "Hiding", "Rescuing", "Pouting"; and since the
    ///   runners (2026-10-02, OutpostLedger.Runners.cs) "Runner, waiting",
    ///   "Running 6 boards to Sawmill" (prefix "Running ") and, for a
    ///   stationed worker whose bench waits on a barrow, "Waiting for a
    ///   runner".
    /// - `StatusReason(h)` -- the longer why ("walled off", "store is full
    ///   of timber", "! No stone" ...), "" when there is nothing to add.
    /// - `Tally()`         -- `CampTally`: how many hands fall in each of
    ///   building / hauling / working / gathering / reserve / noWork /
    ///   builderWaiting / stuck / downed / runners. Sleep and evening count
    ///   by the job underneath.
    ///
    /// **"Idle" is gone as a word (2026-10-03, villager review group 3).**
    /// It meant three different things -- the player's reserve, a hand
    /// with nothing to do, and a builder held up for a material -- so the
    /// player could not tell a choice from a problem. Now: "Reserve" (the
    /// player stood him down, `Reserve`), "No job" (no order, or a gather
    /// order with nothing picked, and the idle ladder has nothing for him)
    /// and "Builder — waiting for stone" (a builder with no plot to work
    /// while a site still lacks a material, `BuilderWaitRes`). Only "No
    /// job" counts as idle (`IdleCount`, the idle chip, the alert).
    /// - `UnmannedStations()` / `StationUnmanned(s)` -- stations with an
    ///   order standing (or queued) and no Work hand on them.
    /// - `FreeHandFor(planId)` -- the best hand with no job to put there
    ///   (null when there is none); give him the job with
    ///   `Outpost.Assign(hand, planId)`.
    /// - Food draft (OutpostLedger.FoodDraft.cs): `FoodDraftNotice`,
    ///   `FoodDrafted`, `UndoFoodDraft(h)`.
    ///
    /// **The stock top-up** (designer approved extending "idle never
    /// gathers"): a hand with no job -- Idle (not the player's reserve), or
    /// Build with nothing on the sites -- who found no station or transfer
    /// chore cuts timber or quarries stone, whichever the store holds less
    /// of, while it holds under `EconomyTuning.CampStockReserve`. An
    /// ordinary Field -> Store trip, so it counts on arrival; his order is
    /// untouched and `OutpostHand.Doing` says "gathering timber for the
    /// store" while it lasts.
    /// </summary>
    public partial class OutpostLedger
    {
        // --- the stock top-up ----------------------------------------------------

        /// **A free hand's top-up trips**, trip after trip while the day
        /// lasts and the store is under its reserve. Only called once his
        /// real chores came up empty.
        void TopUpDay(OutpostHand h, ref float budget)
        {
            if (h == null || h.Busy || Reserve(h)) return;
            for (int guard = 0; guard < 16 && budget > Eps; guard++)
            {
                if (h.Hauling)
                {
                    // Only walk what this errand started; anything else in
                    // his arms is its own pass's business.
                    if (!h.TopUpTrip || !AdvanceHaul(h, ref budget)) break;
                    continue;
                }
                if (!StartTopUpTrip(h)) break;
            }
        }

        bool StartTopUpTrip(OutpostHand h)
        {
            int target = Economy.EconomyTuning.CampStockReserve;
            if (target <= 0) return false;
            // Off the clock: nobody starts a new trip (`FinishTripsOffHours`).
            if (!h.orderOverride && DayNightWorkScale <= 0f) return false;
            string best = null;
            int bestHave = int.MaxValue, bestN = 0;
            for (int k = 0; k < 2; k++)
            {
                string res = k == 0 ? Res.Timber : Res.Stone;
                var pile = Store(res);
                int have = (pile != null ? pile.whole : 0) + InFlightTo(HaulPlace.Store, -1, res);
                if (have >= target) continue;
                int n = Mathf.Min(Res.Armful(res), Mathf.Min(target - have, RoomFor(res)));
                n = Mathf.Min(n, TopUpFieldFree(res));
                if (n <= 0) continue;
                if (have < bestHave) { best = res; bestHave = have; bestN = n; }
            }
            if (best == null) return false;
            h.topUpRes = best;
            StartTimedTrip(h, best, bestN, HaulPlace.Field, -1, HaulPlace.Store, -1);
            return true;
        }

        /// What stands on the island for a top-up, **after one armful per
        /// player-ordered gatherer of it who is between trips** -- his next
        /// armful is his, the errand never takes the trees he was sent for.
        /// (A gatherer mid-trip already holds his claim in `FieldFree`.)
        int TopUpFieldFree(string res)
        {
            int free = FieldFree(res);
            if (hands != null)
                foreach (var o in hands)
                    if (o != null && !o.Hauling && o.order == OutpostOrder.Gather && o.target == res && !o.autoFood)
                        free -= Res.Armful(res);
            return Mathf.Max(0, free);
        }

        // --- the words ---------------------------------------------------------

        /// **One or two words for what this hand is doing**, for a label or
        /// a list row. See the class doc for the set.
        public string StatusWord(OutpostHand h) => Word(h, true);

        /// `routine` false = what he does when he is up (sleep and evening
        /// read as the job underneath) -- the tally's view.
        string Word(OutpostHand h, bool routine)
        {
            if (h == null) return "";
            if (h.downed || h.recovering || h.dragged) return "Downed";
            if (!string.IsNullOrEmpty(h.rescuing)) return "Rescuing";
            if (h.pouting) return "Pouting";
            if (h.hidingHut || h.hidingCrouch) return "Hiding";
            if (h.defending || h.fetchingSpear || h.alarmed || h.raidLookout || h.returningSpear) return "Fighting";
            if (!string.IsNullOrEmpty(h.bodyBlocked)) return "Stuck";
            if (routine && !h.orderOverride)
            {
                var phase = Life.CampLifeTuning.PhaseAtHour(TimeOfDay.Hour);
                if (phase == Life.CampLifeTuning.RoutinePhase.Sleep) return "Sleeping";
                // **Supper (2026-10-02)** until he has sat down to it at the
                // fire (or the bell has not been booked yet); "Evening" after,
                // and for a hand the supper had nothing for.
                if (phase == Life.CampLifeTuning.RoutinePhase.Evening)
                    return supperDay < TimeOfDay.Day || h.SupperWaiting(TimeOfDay.Day) ? "Supper" : "Evening";
            }
            if (Reserve(h)) return h.Hauling ? "Hauling" : "Reserve";
            if (h.TopUpTrip) return "Gathering";
            // **Runners (2026-10-02)**: "Running 6 boards to Sawmill", or
            // waiting at the store -- never idle.
            if (IsRunner(h)) return h.Hauling && !h.eating ? RunWords(h) : "Runner, waiting";
            // "No work" became "Idle" (2026-10-02, the runners' UI) and
            // "Idle" became "No job" / "Builder — waiting for X" (2026-10-03,
            // see the class doc): a hand with nothing to do rests at the fire.
            switch (h.order)
            {
                case OutpostOrder.Gather:
                    if (string.IsNullOrEmpty(h.target)) return NoJobWord;
                    return h.target == Res.Game ? "Hunting" : "Gathering";
                case OutpostOrder.Work:
                    // **No recipe chosen (2026-10-02 play check):** Edda
                    // and Nye read "Working | no order given" at benches
                    // with nothing selected. Not "Idle" -- he keeps his
                    // post (assignments are permanent) and the fix is a
                    // recipe at the station, not a new job.
                    if (BenchUnordered(h)) return "Idle at the bench";
                    return WaitingForRunner(h) ? "Waiting for a runner" : "Working";
                case OutpostOrder.Build:
                    if (BuildSiteFor(h) != null) return "Building";
                    if (h.Hauling) return "Hauling";
                    return BuilderWaitPrefix + BuilderWaitWhat(h);
                default:
                    return h.Hauling ? "Hauling" : NoJobWord;
            }
        }

        /// **What the word reads while he sleeps or sups**: the tally's view
        /// (`Word` with `routine` false), for a readout that sorts hands by
        /// their job rather than by the hour (`CampReadouts.KindOf`).
        public string JobWord(OutpostHand h) => Word(h, false);

        /// "No job": no order (not the player's reserve), or a gather order
        /// with nothing picked -- and nothing in his arms (2026-10-03).
        public const string NoJobWord = "No job";

        /// "Builder — waiting for stone" (2026-10-03): `Word`'s prefix for a
        /// builder with no plot to work and nothing in his arms.
        public const string BuilderWaitPrefix = "Builder — waiting for ";

        public static bool IsBuilderWait(string word) =>
            word != null && word.StartsWith(BuilderWaitPrefix, System.StringComparison.Ordinal);

        /// **The material a builder with no plot is held up for** (2026-10-03),
        /// or null: the first of timber / stone / brick still owed to a site,
        /// his own last plot first, then the queue oldest first -- whether
        /// nobody can supply it (`SiteShortfall`) or it is in somebody
        /// else's arms on the way. Null while every unfinished site has all
        /// its materials in (he waits for a plot with room in its crew).
        public string BuilderWaitRes(OutpostHand h)
        {
            if (h == null || h.order != OutpostOrder.Build || sites == null) return null;
            lastSite.TryGetValue(h, out var mine);
            for (int si = -1; si < sites.Count; si++)
            {
                var s = si < 0 ? mine : sites[si];
                if (si >= 0 && s == mine) continue;
                if (s == null || s.Complete || s.Stocked || !sites.Contains(s)) continue;
                for (int k = 0; k < 3; k++)
                {
                    string res = k == 0 ? Res.Timber : k == 1 ? Res.Stone : Res.Brick;
                    if (RemainingOf(s, res) > 0) return res;
                }
            }
            return null;
        }

        /// "stone", "timber", "brick" -- or "a plot" while every site has its
        /// materials and its full crew (`EconomyTuning.CrewCap`).
        string BuilderWaitWhat(OutpostHand h)
        {
            string res = BuilderWaitRes(h);
            return res != null ? Friendly(res) : "a plot";
        }

        /// **Why a waiting builder waits**, for his reason line: nothing can
        /// supply the material (`SiteShortfall`'s sentence), or it is on its
        /// way in somebody's arms, or every plot has its crew.
        string BuilderWaitReason(OutpostHand h)
        {
            string res = BuilderWaitRes(h);
            if (res == null) return "every site has its materials and its full crew";
            string none = SiteShortfall();
            if (!string.IsNullOrEmpty(none)) return none;
            if (InFlightTo(HaulPlace.Site, -1, res) > 0) return $"{Friendly(res)} on its way to the site";
            return $"waiting for {Friendly(res)} to reach the site";
        }

        /// **The longer why**, "" when the word says it all: the body's
        /// own block, the ledger's stall reason, the top-up errand, or the
        /// short issue of the site he is on.
        public string StatusReason(OutpostHand h)
        {
            if (h == null) return "";
            if (!string.IsNullOrEmpty(h.bodyBlocked)) return h.bodyBlocked;
            if (Reserve(h)) return "held in reserve";
            if (h.TopUpTrip) return "topping up the store's " + ResLabel(h.topUpRes);
            // The runners (2026-10-02).
            if (IsRunner(h) && !h.Hauling) return "at the store hut, nothing to carry";
            // Ahead of the runner's words: with nothing chosen, a recipe is
            // the fix, whatever is on the rack.
            if (BenchUnordered(h)) return NoRecipeWords(StationOfHand(h));
            if (WaitingForRunner(h))
            {
                // "waiting for hide · runners bringing it" (2026-10-02): he
                // stays at his bench while the runners carry.
                var ws = StationOfHand(h);
                int wsi = stations.IndexOf(ws);
                // Only while a runner can take it somewhere: a rack whose
                // goods have no room anywhere says so, with the fix, in the
                // words of the store-full chip (2026-10-02 play check).
                if (RackBlocking(ws))
                {
                    if (RackOutbound(ws, wsi) != null) return "rack full · runners taking it away";
                    string held = RackHeldMost(ws);
                    if (held != null) return StoreFullWords(held);
                }
                string item = RunnerWaitItem(ws, wsi);
                return item != null
                    ? $"waiting for {Friendly(item)} · runners bringing it"
                    : RunnerBound(wsi) ? "a runner is on the way" : "waiting on the barrows";
            }
            // **Only while food really is short (`FoodShort`, 2026-10-02):**
            // Pip read "food emergency" beside a store at its ceiling of
            // potatoes, baked potatoes, fish and forage. Above `FedDays` the
            // emergency's hand stays on until `FoodSafeDays` (or until the
            // store can take no more of what he makes) and says that.
            // **One food draft, one wording (2026-10-03)**: farmhand, cook,
            // hunter or forager alike.
            bool shortOfFood = FoodShort;
            if ((h.autoStation && FoodStationOrder(h)) || (h.autoFood && FoodDraftOrder(h)))
            {
                if (shortOfFood) return $"food draft · under {FedDays:0} days of food";
                string stall = StallReason(h);
                if (!string.IsNullOrEmpty(stall)) return stall;
                return $"camp is fed · staying on food until {FoodSafeDays:0} days are stored";
            }
            string why = StallReason(h);
            if (!string.IsNullOrEmpty(why)) return why;
            if (h.order == OutpostOrder.Build && !h.Hauling && !h.TopUpTrip && BuildSiteFor(h) == null)
                return BuilderWaitReason(h);
            if (h.order == OutpostOrder.Build)
            {
                var site = BuildSiteFor(h);
                string issue = site != null ? SiteIssueShort(site) : null;
                if (!string.IsNullOrEmpty(issue)) return issue;
            }
            return "";
        }

        /// **A stationed worker at a bench with no recipe chosen** (2026-10-02):
        /// on Work at a station that stands, no spot selected, none mid-batch
        /// or holding a finished batch, and nothing in his arms. "Idle at the
        /// bench" -- he keeps his post; the fix is a recipe at the station.
        public bool BenchUnordered(OutpostHand h)
        {
            if (h == null || h.Hauling || IsRunner(h)) return false;
            var s = StationOfHand(h);
            if (s == null) return false;
            s.EnsureSpotRows();
            foreach (var sp in s.spots)
                if (sp != null && (sp.Selected || sp.BenchBusy || sp.benchState == BenchState.Finished)) return false;
            return true;
        }

        /// "no recipe chosen · open the hunting lodge and pick one": the
        /// station's player-facing name (`BuildPlan.label`).
        internal static string NoRecipeWords(StationStock s)
        {
            string name = s != null ? BuildPlans.Named(s.planId).label : null;
            if (string.IsNullOrEmpty(name)) name = "station";
            return $"no recipe chosen · open the {name} and pick one";
        }

        /// "rack and store are full of fish · build or upgrade a store hut"
        /// -- the store-full chip's fix (`CampAlerts.StoreFullText`), whole.
        internal static string StoreFullWords(string res) =>
            $"rack and store are full of {Friendly(res)} · build or upgrade a store hut";

        /// **Heads per activity, for the camp tally.**
        public struct CampTally
        {
            public int building, hauling, working, gathering, reserve, noWork, stuck, downed;
            /// Builders with no plot, held up for a material (2026-10-03).
            public int builderWaiting;
            /// Runners, running or waiting at the store (2026-10-02).
            public int runners;
        }

        /// Every hand counted once, by `StatusWord` with sleep/evening read
        /// as the job underneath. Hunting counts as gathering; raid states
        /// (fighting, hiding), rescuing and pouting are left out.
        public CampTally Tally()
        {
            var t = new CampTally();
            if (hands == null) return t;
            foreach (var h in hands)
            {
                string w = Word(h, false);
                switch (w)
                {
                    case "Building": t.building++; break;
                    case "Hauling": t.hauling++; break;
                    case "Working":
                    case "Waiting for a runner": t.working++; break;
                    case "Gathering":
                    case "Hunting": t.gathering++; break;
                    case "Reserve": t.reserve++; break;
                    case NoJobWord:
                    // A bench with no recipe chosen makes nothing (2026-10-02).
                    case "Idle at the bench": t.noWork++; break;
                    case "Stuck": t.stuck++; break;
                    case "Downed": t.downed++; break;
                    case "Runner, waiting": t.runners++; break;
                    default:
                        if (w.StartsWith("Running ", System.StringComparison.Ordinal)) t.runners++;
                        else if (IsBuilderWait(w)) t.builderWaiting++;
                        break;
                }
            }
            return t;
        }

        // --- stations nobody works ---------------------------------------------

        /// **A station with work waiting and nobody at it**: an order
        /// standing (or queued) and no Work hand dealt to it.
        public bool StationUnmanned(StationStock s)
        {
            if (s == null || s.removed) return false;
            if (!s.HasOrder && (s.queue == null || s.queue.Count == 0)) return false;
            if (hands != null)
                foreach (var h in hands)
                    if (h != null && StationOfHand(h) == s) return false;
            return true;
        }

        /// Every station `StationUnmanned` says yes to. A fresh list.
        public List<StationStock> UnmannedStations()
        {
            var list = new List<StationStock>();
            if (stations == null) return list;
            foreach (var s in stations)
                if (StationUnmanned(s)) list.Add(s);
            return list;
        }

        /// **The hand to send to `planId`**: up, not the player's reserve,
        /// with no job -- Idle, or Build with no plot -- empty-handed first,
        /// then the nearest to the building. Null when nobody is free.
        public OutpostHand FreeHandFor(string planId)
        {
            if (hands == null) return null;
            bool hasAt = PlanPlace(planId, 0, out var at);
            OutpostHand best = null;
            float bestScore = float.MaxValue;
            foreach (var h in hands)
            {
                if (h == null || h.Busy || Reserve(h)) continue;
                bool free = h.order == OutpostOrder.Idle
                    || (h.order == OutpostOrder.Build && BuildSiteFor(h) == null);
                if (!free) continue;
                float score = (h.Hauling ? 10000f : 0f) + (h.order == OutpostOrder.Build ? 1000f : 0f);
                if (hasAt) score += Mathf.Min(999f, Vector3.Distance(HandAt(h), at));
                if (score < bestScore) { best = h; bestScore = score; }
            }
            return best;
        }
    }
}
