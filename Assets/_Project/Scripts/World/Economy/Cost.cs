using System.Collections.Generic;
using System.Text;

namespace SeaSick.World.Economy
{
    /// One line of a price: so many of one resource.
    ///
    /// **A list of these is every price in the game from here on** -- a
    /// recipe's inputs, a fire's next level, a building's upgrade. The
    /// blueprint's timber/stone/brick trio and `ShipPrices.Price` predate
    /// this and keep their own shape for now; `RecipeGraph` reads them
    /// through `Cost.Of` so the validator sees one graph.
    ///
    /// A struct of two fields, serialisable by `JsonUtility`, so a saved
    /// choice can carry one without a converter.
    [System.Serializable]
    public struct Ingredient
    {
        public string res;
        public int n;

        public Ingredient(string res, int n) { this.res = res; this.n = n; }

        public override string ToString() => $"{n} {ResDefs.Label(res)}";
    }

    /// Helpers over `Ingredient[]`. Static, allocation-light, and the only
    /// place the "have / need" arithmetic is written, so a sheet, the
    /// ledger and the validator cannot disagree on whether a camp can pay.
    public static class Cost
    {
        public static readonly Ingredient[] None = new Ingredient[0];

        public static Ingredient[] Of(params Ingredient[] lines) => lines ?? None;

        public static Ingredient I(string res, int n) => new Ingredient(res, n);

        /// `cost` through FEEL's `costMultiplier` (each line rounded up, never
        /// to zero). The same array back when the multiplier is 1, so the
        /// common case allocates nothing.
        public static Ingredient[] Scaled(Ingredient[] cost)
        {
            if (cost == null || cost.Length == 0 || UnityEngine.Mathf.Approximately(EconomyFeel.CostMul, 1f)) return cost ?? None;
            var o = new Ingredient[cost.Length];
            for (int i = 0; i < cost.Length; i++) o[i] = new Ingredient(cost[i].res, EconomyFeel.Price(cost[i].n));
            return o;
        }

        /// Every line met by the pile. `count` is the ledger's `CountOf`.
        public static bool Affordable(Ingredient[] cost, System.Func<string, int> count)
        {
            if (cost == null) return true;
            foreach (var line in cost)
                if (line.n > 0 && count(line.res) < line.n) return false;
            return true;
        }

        /// The lines the pile is short on, with how many short. Empty when
        /// affordable. This is what a sheet prints in red.
        public static List<Ingredient> Missing(Ingredient[] cost, System.Func<string, int> count)
        {
            var missing = new List<Ingredient>();
            if (cost == null) return missing;
            foreach (var line in cost)
            {
                int short_ = line.n - count(line.res);
                if (line.n > 0 && short_ > 0) missing.Add(new Ingredient(line.res, short_));
            }
            return missing;
        }

        /// "2 boards, 1 stone" -- the price as a sentence fragment.
        public static string Describe(Ingredient[] cost)
        {
            if (cost == null || cost.Length == 0) return "nothing";
            var sb = new StringBuilder();
            for (int i = 0; i < cost.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(cost[i].n).Append(' ').Append(ResDefs.Label(cost[i].res));
            }
            return sb.ToString();
        }

        /// Sum of every line -- the "how much stuff" number the build
        /// labour formula already uses for blueprints.
        public static int Units(Ingredient[] cost)
        {
            if (cost == null) return 0;
            int n = 0;
            foreach (var line in cost) n += line.n;
            return n;
        }

        public static bool Mentions(Ingredient[] cost, string res)
        {
            if (cost == null) return false;
            foreach (var line in cost) if (line.res == res && line.n > 0) return true;
            return false;
        }
    }
}
