using UnityEngine;

namespace SeaSick.World
{
    /// **Several of one building, 2026-09-27.** Kevin: *"you can unlock the
    /// quantity of some buildings based on campfire. so like lvl 1 campfire
    /// is 2 houses, level 2 is 3 houses, etc. maybe second sawmill in lvl 3
    /// etc. and they have their own levels, always. new houses cost a little
    /// bit more maybe."*
    ///
    /// - How many: `Economy.Techs.Caps` by fire level, read here as
    ///   `CopyLimit`; built AND queued copies count (`CopiesHeld`).
    /// - What the next one costs: `BuildPlans.PriceForCopy` (+25% a copy).
    /// - Own levels: `BuiltBuilding.level`, read through `LevelOf(planId,
    ///   ordinal)` / `LevelAtRaised` (OutpostLedger.cs, "building levels").
    /// - Which copy a Work hand is at: `OrdinalOfHand`, the same deal
    ///   `StationOfHand` makes for the stations, for the farm and the
    ///   watchtower too.
    public partial class OutpostLedger
    {
        /// How many of `planId` this camp may hold at its fire's level.
        public int CopyLimit(string planId) => Economy.Techs.MaxCopies(planId, CampfireLevel);

        /// Standing plus drawn: what the cap and the price count.
        public int CopiesHeld(string planId) => CountBuilt(planId) + QueuedCount(planId);

        /// "2nd", "3rd", "11th".
        public static string Nth(int n)
        {
            int t = n % 100;
            string suf = t >= 11 && t <= 13 ? "th"
                : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
            return n + suf;
        }

        /// **May one more `planId` be sited here?** `why` names what is in
        /// the way -- for a copy the fire will open later, the level that
        /// opens it ("a 3rd shelter needs campfire II"). A plan that stays
        /// at one keeps its old refusals word for word.
        public bool CanAddCopy(string planId, out string why)
        {
            why = null;
            int have = CopiesHeld(planId);
            int cap = CopyLimit(planId);
            if (have < cap) return true;
            var plan = BuildPlans.Named(planId);
            int unlock = Economy.Techs.FireLevelForCopies(planId, have + 1);
            if (unlock > CampfireLevel)
            {
                why = $"a {Nth(have + 1)} {plan.label} needs campfire {Economy.RecipeGraph.Roman(unlock)}";
                return false;
            }
            if (cap <= 1)
            {
                why = CountBuilt(planId) > 0
                    ? $"there is already a {plan.label} here"
                    : $"a {plan.label} is already going up here";
                return false;
            }
            why = $"this camp holds {cap} {plan.label}s at most";
            return false;
        }

        /// What the NEXT copy of `plan` costs here (`PriceForCopy` at the
        /// count standing and drawn).
        public BuildPlan PriceOfNext(BuildPlan plan) => BuildPlans.PriceForCopy(plan, CopiesHeld(plan.id));

        /// **One build-list row, truthful about copies**: "shelter ×1 of 2 —
        /// 7 timber 5 stone", "shelter ×2 of 2 — a 3rd needs campfire II",
        /// "quarry — needs the fire at II". `enabled` false when a tap
        /// could only be refused. Kept to one line; the layout is the
        /// sheet's (a UI rework is coming).
        public string BuildRowText(BuildPlan p, out bool enabled)
        {
            if (!PlanUnlocked(p.id))
            {
                enabled = false;
                return $"{p.label} — {PlanLockReason(p.id)}";
            }
            int have = CopiesHeld(p.id);
            int cap = CopyLimit(p.id);
            int top = Economy.Techs.MaxCopies(p.id, Economy.Techs.CapTableLevels);
            // Only a plan that can ever be more than one says "×n of m".
            string count = top > 1 ? $" ×{have} of {cap}" : "";
            if (!CanAddCopy(p.id, out string why))
            {
                enabled = false;
                // The copy line already names the building; the refusal's
                // own "a 3rd shelter" is shortened to "a 3rd".
                string shortWhy = why.Replace($" {p.label} needs", " needs");
                return $"{p.label}{count} — {shortWhy}";
            }
            enabled = true;
            var priced = PriceOfNext(p);
            string price = priced.stoneCost > 0
                ? $"{priced.cost} timber {priced.stoneCost} stone"
                : $"{priced.cost} timber";
            // The next unlock, when this is the last copy the fire allows now.
            int unlock = Economy.Techs.FireLevelForCopies(p.id, cap + 1);
            string next = top > 1 && have + 1 == cap && unlock > CampfireLevel
                ? $" · {Nth(cap + 1)} at fire {Economy.RecipeGraph.Roman(unlock)}" : "";
            return $"{p.label}{count} — {price}{next}";
        }

        /// **Which copy of its building a Work hand is at**: Work hands on
        /// one plan are dealt round its copies in hand-list order -- the
        /// same deal `StationOfHand` makes, so a farm's or a watchtower's
        /// nth copy is manned when more than n hands work that plan. -1 for
        /// a hand not on Work or on a plan with nothing standing.
        public int OrdinalOfHand(OutpostHand h)
        {
            if (h == null || h.order != OutpostOrder.Work || string.IsNullOrEmpty(h.target)) return -1;
            int n = CountBuilt(h.target);
            if (n <= 0) return -1;
            // A pinned hand stands at his own copy (2026-09-28, `workPin`);
            // the rest are dealt round the copies as before, counting only
            // the unpinned -- so with no pins this is the old deal exactly.
            if (h.workPin > 0 && h.workPin <= n) return h.workPin - 1;
            int k = 0;
            foreach (var x in hands)
            {
                if (x == h) break;
                if (x != null && x.order == OutpostOrder.Work && x.target == h.target
                    && !(x.workPin > 0 && x.workPin <= n)) k++;
            }
            return k % n;
        }

        /// Copies of `planId` with at least one Work hand at them (never more
        /// than stand). For the watchtower: every manned tower counts.
        public int MannedCopies(string planId)
            => Mathf.Min(CountBuilt(planId), HandsOn(OutpostOrder.Work, planId));

        // --- one worker per station (2026-10-01) ----------------------------

        /// Kevin: *"only one at a station. potential to add more in upgraded
        /// versions but for now it's one to a station."* The refusal the UI
        /// shows.
        public const string OneWorkerReason = "One worker per station";

        /// **How many Work hands one copy of a building takes.** The one
        /// hook for the cap: 1 at every level today; a level that should
        /// take more raises it here (`LevelOf(planId, ordinal)`). Every
        /// station's spots (the kitchen's cauldron and grill, the quarry's
        /// bench and bays) share that one worker.
        ///
        /// **Runners (2026-10-02, approved design):** a store hut takes
        /// `RunnersPerStoreL1` runners at level 1 and `RunnersPerStoreL2`
        /// from level 2 -- the barrow crew, see OutpostLedger.Runners.cs.
        public int StationCapacity(string planId, int ordinal)
        {
            if (planId == StorageId)
                return LevelOf(planId, ordinal) >= 2 ? RunnersPerStoreL2 : RunnersPerStoreL1;
            return 1;
        }

        /// The refusal for a full copy of `planId`, in the UI's words: the
        /// store hut's barrows, or one worker per station.
        public string CapacityReason(string planId) =>
            planId == StorageId ? RunnersFullReason : OneWorkerReason;

        /// Work hands standing at copy `ordinal` of `planId`, `except` aside.
        public int WorkersAt(string planId, int ordinal, OutpostHand except = null)
        {
            int k = 0;
            foreach (var x in hands)
                if (x != null && x != except && x.order == OutpostOrder.Work && x.target == planId
                    && OrdinalOfHand(x) == ordinal) k++;
            return k;
        }

        /// The first copy of `planId` with room for one more (`h` not
        /// counted, so a hand already there has room at his own), or -1.
        public int FreeCopyFor(string planId, OutpostHand h)
        {
            int n = CountBuilt(planId);
            for (int o = 0; o < n; o++)
                if (WorkersAt(planId, o, h) < StationCapacity(planId, o)) return o;
            return -1;
        }

        /// **Over the cap -> free hands (a save from before the rule, a
        /// copy demolished under its worker).** The first hand at each copy
        /// keeps it and is pinned there; the rest go back to the idle
        /// ladder (`playerIdle` off), anything planned in their arms is
        /// dropped the ledger's own way -- no building moves, no stock is
        /// lost. Returns how many were freed.
        public int EnforceStationCaps()
        {
            int freed = 0;
            var seen = new System.Collections.Generic.Dictionary<string, int>();
            var ords = new int[hands.Count];
            for (int i = 0; i < hands.Count; i++) ords[i] = hands[i] != null ? OrdinalOfHand(hands[i]) : -1;
            for (int i = 0; i < hands.Count; i++)
            {
                var h = hands[i];
                if (h == null || h.order != OutpostOrder.Work || ords[i] < 0) continue;
                if (!BuildPlans.HasPosition(h.target)) continue;
                string key = h.target + "#" + ords[i];
                seen.TryGetValue(key, out int k);
                if (k < StationCapacity(h.target, ords[i]))
                {
                    seen[key] = k + 1;
                    h.workPin = ords[i] + 1;      // stays where he stands
                    continue;
                }
                DropCarriedLoadNow(h);
                h.order = OutpostOrder.Idle;
                h.target = "";
                h.workPin = 0;
                h.playerIdle = false;
                freed++;
                Debug.Log($"[Ledger] {h.name}: freed from a full station (one worker per station).");
            }
            return freed;
        }
    }
}
