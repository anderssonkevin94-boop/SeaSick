using System.Collections.Generic;
using SeaSick.World.Economy;

namespace SeaSick.World
{
    /// **Station spots (Kevin, 2026-09-30, Melvor-style stations).** Every
    /// crafting station has one or more SPOTS, each running its own selected
    /// recipe AT THE SAME TIME, all tended by the station's one worker:
    /// <list type="bullet">
    /// <item>Kitchen: "Grill" (grilled fish, grilled meat, roast carrots,
    ///   baked potato, ship's biscuit) and "Cauldron" (veg stew, hunter's
    ///   stew, fish pie, bread).</item>
    /// <item>Blacksmith (the forge): "Smelter" (iron) and "Forge" (spear, saw
    ///   blade, tools, iron spear).</item>
    /// <item>Every other station: one spot, named after the station.</item>
    /// </list>
    /// A recipe's spot is `Recipe.spot`; a recipe of a multi-spot station
    /// with no (or an unknown) spot goes on the station's FIRST spot, so a
    /// new recipe row is never orphaned. The per-spot state is `SpotState`
    /// on `StationStock.Spots`; the ledger's write API is
    /// `OutpostLedger.SelectRecipe` / `StopSpot` (`OutpostLedger.Spots.cs`).
    public static class StationSpots
    {
        /// The stations with more than one spot. Everything else has one.
        static readonly Dictionary<string, string[]> Multi = new Dictionary<string, string[]>
        {
            { "Kitchen", new[] { "Grill", "Cauldron" } },
            { "Blacksmith", new[] { "Smelter", "Forge" } },
        };

        static readonly Dictionary<string, IReadOnlyList<string>> spotCache =
            new Dictionary<string, IReadOnlyList<string>>();
        static readonly Dictionary<string, IReadOnlyList<Recipe>> recipeCache =
            new Dictionary<string, IReadOnlyList<Recipe>>();

        /// The spots of a station plan, in the order the screen lists them
        /// and `StationStock.Spots` holds them: `{"Grill","Cauldron"}` for
        /// the kitchen, `{"Sawmill"}` for a sawmill. Empty for a plan with
        /// no recipes (a farm, a watchtower).
        public static IReadOnlyList<string> SpotsFor(string stationPlanId)
        {
            if (string.IsNullOrEmpty(stationPlanId)) return System.Array.Empty<string>();
            if (spotCache.TryGetValue(stationPlanId, out var cached)) return cached;
            IReadOnlyList<string> list;
            if (!Recipes.StationHasRecipes(stationPlanId)) list = System.Array.Empty<string>();
            else if (Multi.TryGetValue(stationPlanId, out var names)) list = names;
            else list = new[] { SingleName(stationPlanId) };
            spotCache[stationPlanId] = list;
            return list;
        }

        /// Every recipe worked on `spot` of this station, locked ones
        /// included, in `Recipes.At` order.
        public static IReadOnlyList<Recipe> RecipesFor(string stationPlanId, string spot)
        {
            if (string.IsNullOrEmpty(stationPlanId) || string.IsNullOrEmpty(spot))
                return System.Array.Empty<Recipe>();
            string key = stationPlanId + "/" + spot;
            if (recipeCache.TryGetValue(key, out var cached)) return cached;
            var list = new List<Recipe>();
            foreach (var r in Recipes.At(stationPlanId))
                if (r != null && SpotOf(r) == spot) list.Add(r);
            recipeCache[key] = list;
            return list;
        }

        /// The spot a recipe is worked on (see the class doc for the
        /// fallback), or null for a recipe of no station.
        public static string SpotOf(Recipe r)
        {
            if (r == null) return null;
            var spots = SpotsFor(r.station);
            if (spots.Count == 0) return null;
            if (!string.IsNullOrEmpty(r.spot))
                for (int i = 0; i < spots.Count; i++)
                    if (spots[i] == r.spot) return spots[i];
            return spots[0];
        }

        /// `SpotOf` as an index into `SpotsFor(r.station)`, or -1.
        public static int SpotIndexOf(Recipe r)
        {
            string spot = SpotOf(r);
            return spot == null ? -1 : IndexOf(r.station, spot);
        }

        /// Index of `spot` in `SpotsFor(stationPlanId)`, or -1.
        public static int IndexOf(string stationPlanId, string spot)
        {
            var spots = SpotsFor(stationPlanId);
            for (int i = 0; i < spots.Count; i++) if (spots[i] == spot) return i;
            return -1;
        }

        /// Who tends a station, for "no cook" / "no smith".
        public static string WorkerNoun(string stationPlanId) => stationPlanId switch
        {
            "Kitchen" => "cook",
            "Blacksmith" => "smith",
            "FishingHut" => "fisher",
            _ => "worker",
        };

        /// A single-spot station's spot: its label, capitalised ("Sawmill").
        static string SingleName(string planId)
        {
            string label = BuildPlans.Named(planId).label;
            if (string.IsNullOrEmpty(label)) label = planId;
            return char.ToUpperInvariant(label[0]) + label.Substring(1);
        }
    }

    /// **One spot's state (2026-09-30).** Saved inside its `StationStock`
    /// (`spots`), so JsonUtility-shaped: public fields, no dictionaries.
    /// Each spot has its OWN bench (a batch of `benchRecipe` loaded, worked,
    /// finished); the input bay and the output rack are the station's and
    /// SHARED by every spot (see `StationStock`).
    [System.Serializable]
    public sealed class SpotState
    {
        /// The spot's name, one of `StationSpots.SpotsFor(planId)`.
        public string spot = "";
        /// The selected recipe; null = idle. (JsonUtility loads "" for a
        /// null string -- `StationStock.EnsureSpotRows` turns it back.)
        public string recipeId;
        /// Units still to make, or 0 = until stopped (what `SelectRecipe`
        /// gives). Only the legacy `PlaceOrder(count)` path sets a count;
        /// the spot clears itself when it runs out.
        public int count;

        /// What the batch on this spot's bench is (may differ from
        /// `recipeId` for one batch after a re-select), "" when empty.
        public string benchRecipe = "";
        public BenchState benchState = BenchState.Empty;
        /// 0..1 through the batch on the bench.
        public float progress01;
        /// Output units sitting on the bench when `benchState == Finished`
        /// (the rack had no room for them).
        public int benchOut;

        /// **Why a selected spot is not running right now, or null.**
        /// Worked out by the ledger every step (`OutpostLedger.RefreshSpots`)
        /// -- never saved: "waiting for onion", "store full of grilled fish",
        /// "no cook", "needs the fire at II". The spot resumes on its own
        /// the moment the reason clears.
        [System.NonSerialized] public string pauseReason;
        /// Real seconds until the current batch comes off the bench at the
        /// worker's current pace (a whole batch when nothing is loaded yet).
        /// Refreshed with `pauseReason`.
        [System.NonSerialized] public float SecondsLeft;

        public bool Selected => !string.IsNullOrEmpty(recipeId);
        public bool Running => Selected && pauseReason == null;
        public bool BenchBusy => benchState == BenchState.Loaded || benchState == BenchState.Working;

        public Recipe Recipe => Selected ? Recipes.Named(recipeId) : null;
        public Recipe BenchRecipe => string.IsNullOrEmpty(benchRecipe) ? null : Recipes.Named(benchRecipe);
        public string BenchMakes => BenchRecipe?.makes;

        public void EmptyBench()
        {
            benchState = BenchState.Empty;
            benchRecipe = "";
            progress01 = 0f;
            benchOut = 0;
        }

        public void Stop()
        {
            recipeId = null;
            count = 0;
        }
    }
}
