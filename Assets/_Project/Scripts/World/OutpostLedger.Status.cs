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
    ///   "Gathering", "Hunting", "Working", "Reserve", "Idle" (was "No
    ///   work" until 2026-10-02), "Stuck", "Sleeping", "Evening", "Downed",
    ///   "Fighting", "Hiding", "Rescuing", "Pouting"; and since the
    ///   runners (2026-10-02, OutpostLedger.Runners.cs) "Runner, waiting",
    ///   "Running 6 boards to Sawmill" (prefix "Running ") and, for a
    ///   stationed worker whose bench waits on a barrow, "Waiting for a
    ///   runner".
    /// - `StatusReason(h)` -- the longer why ("walled off", "store is full
    ///   of timber", "! No stone" ...), "" when there is nothing to add.
    /// - `Tally()`         -- `CampTally`: how many hands fall in each of
    ///   building / hauling / working / gathering / reserve / noWork /
    ///   stuck / downed / runners. Sleep and evening count by the job underneath.
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
                if (phase == Life.CampLifeTuning.RoutinePhase.Evening) return "Evening";
            }
            if (Reserve(h)) return h.Hauling ? "Hauling" : "Reserve";
            if (h.TopUpTrip) return "Gathering";
            // **Runners (2026-10-02)**: "Running 6 boards to Sawmill", or
            // waiting at the store -- never idle.
            if (IsRunner(h)) return h.Hauling && !h.eating ? RunWords(h) : "Runner, waiting";
            // "No work" became "Idle" (2026-10-02, the runners' UI): a hand
            // with nothing to do rests at the fire.
            switch (h.order)
            {
                case OutpostOrder.Gather:
                    if (string.IsNullOrEmpty(h.target)) return "Idle";
                    return h.target == Res.Game ? "Hunting" : "Gathering";
                case OutpostOrder.Work:
                    return WaitingForRunner(h) ? "Waiting for a runner" : "Working";
                case OutpostOrder.Build:
                    if (BuildSiteFor(h) != null) return "Building";
                    return h.Hauling ? "Hauling" : "Idle";
                default:
                    return h.Hauling ? "Hauling" : "Idle";
            }
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
            if (WaitingForRunner(h))
                return RunnerBound(stations.IndexOf(StationOfHand(h))) ? "a runner is on the way" : "waiting on the barrows";
            if (h.autoFood && FoodDraftOrder(h)) return "food is low";
            string why = StallReason(h);
            if (!string.IsNullOrEmpty(why)) return why;
            if (h.order == OutpostOrder.Build)
            {
                var site = BuildSiteFor(h);
                string issue = site != null ? SiteIssueShort(site) : null;
                if (!string.IsNullOrEmpty(issue)) return issue;
            }
            return "";
        }

        /// **Heads per activity, for the camp tally.**
        public struct CampTally
        {
            public int building, hauling, working, gathering, reserve, noWork, stuck, downed;
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
                    case "Idle": t.noWork++; break;
                    case "Stuck": t.stuck++; break;
                    case "Downed": t.downed++; break;
                    case "Runner, waiting": t.runners++; break;
                    default:
                        if (w.StartsWith("Running ", System.StringComparison.Ordinal)) t.runners++;
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
