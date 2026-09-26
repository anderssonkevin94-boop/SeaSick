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
            int k = 0;
            foreach (var x in hands)
            {
                if (x == h) break;
                if (x != null && x.order == OutpostOrder.Work && x.target == h.target) k++;
            }
            return k % n;
        }

        /// Copies of `planId` with at least one Work hand at them (never more
        /// than stand). For the watchtower: every manned tower counts.
        public int MannedCopies(string planId)
            => Mathf.Min(CountBuilt(planId), HandsOn(OutpostOrder.Work, planId));
    }
}
