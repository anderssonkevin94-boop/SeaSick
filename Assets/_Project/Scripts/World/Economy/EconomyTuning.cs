using System;
using UnityEngine;

namespace SeaSick.World.Economy
{
    /// <summary>
    /// **The one economy tuning file (Kevin, 2026-09-27: "one tuning file +
    /// the main ones in FEEL").** Every camp number that used to be a literal
    /// in `BuildPlans`, `Techs`, `Recipes`, `Res` and `Playtest` has a row
    /// here: building prices and hammer time per plan, the fire's level-up
    /// price, upgrade prices and rate multipliers, the copy caps per fire
    /// level, the per-copy price step, recipe rates/yields/tool wear, gather
    /// cut seconds, rock yields, meat/hide per animal, spear wear, warmth.
    ///
    /// **Lives at `Assets/_Project/Settings/Resources/EconomyTuning.asset`**
    /// and is loaded with `Resources.Load` so the iOS build carries it with
    /// no scene reference. **Missing asset = the code's own defaults**, which
    /// are the same numbers (the static tables were set to them in the same
    /// commit), so nothing breaks if it is deleted or fails to load.
    ///
    /// Tables are PUSHED into the static tables once (`EnsureApplied`), by
    /// id: a row the asset does not name keeps its code value, a row the code
    /// does not know is ignored. Plan prices and hammer time are READ per
    /// call (`PlanRow`) because `BuildPlan` is a struct copied everywhere.
    ///
    /// The five live multipliers are NOT here: they are `EconomyFeel`'s
    /// static floats, which the on-phone FEEL panel (`Dev/FeelLab`) tunes.
    /// All numbers: **first pass, tune by play** (GDD "Economy numbers").
    /// </summary>
    [CreateAssetMenu(menuName = "SeaSick/Economy Tuning", fileName = "EconomyTuning")]
    public class EconomyTuning : ScriptableObject
    {
        [Serializable]
        public class PlanRow
        {
            public string planId;
            public int timber;
            public int stone;
            public int brick;
            /// Seconds ONE builder hammers once every material is in and the
            /// plot is clear. More builders = faster with diminishing
            /// returns (`EconomyTuning.CrewSpeed`).
            public float hammerSeconds;
        }

        [Serializable]
        public class FireRow
        {
            public int level;
            public Ingredient[] cost;
        }

        [Serializable]
        public class UpgradeRow
        {
            public string planId;
            public int toLevel;
            public Ingredient[] cost;
            public float rateMul = 1.5f;
        }

        [Serializable]
        public class CapRow
        {
            public string planId;
            /// Copies allowed at fire I, II, III, IV.
            public int[] copies;
        }

        [Serializable]
        public class RecipeRow
        {
            public string recipeId;
            public int yield = 1;
            public float ratePerDay;
            public float toolWear;
            public Ingredient[] takes;
        }

        [Header("Buildings")]
        public PlanRow[] plans = new PlanRow[0];
        [Tooltip("Each extra copy of a building costs this much more of the base (0.25 = +25%).")]
        public float copyPriceStep = 0.25f;
        [Tooltip("Palisade: metres of wall one log buys.")]
        public float metresPerPalisadeLog = 2f;
        [Tooltip("Ladder chain: timber per metre of rise.")]
        public float ladderTimberPerMetre = 2f;
        [Tooltip("Road: metres of road one stone buys (PROVISIONAL 2026-09-27).")]
        public float roadMetresPerStone = 4f;
        [Tooltip("Road: how much faster a villager walks on a road (1.3 = 30% faster). Pathing prices a road cell at 1/this.")]
        public float roadSpeedMultiplier = 1.3f;
        [Tooltip("Hammer seconds per log of a wall/gate/ladder row (they have no plan row).")]
        public float hammerSecondsPerLineLog = 3f;
        [Tooltip("Crew exponent: N builders hammer N^x times as fast as one (1 = linear, 0.75 = diminishing).")]
        public float crewExponent = 0.75f;

        [Header("Fire, upgrades, caps")]
        public FireRow[] fire = new FireRow[0];
        public UpgradeRow[] upgrades = new UpgradeRow[0];
        public CapRow[] caps = new CapRow[0];

        [Header("Stations")]
        public RecipeRow[] recipes = new RecipeRow[0];

        [Header("Gathering")]
        public float cutSecondsTimber = 5f;
        public float cutSecondsStone = 8f;
        public float cutSecondsOre = 10f;
        public float cutSecondsSpice = 6.67f;
        public float clearSecondsPerTree = 5f;
        public float clearSecondsPerRock = 8f;
        [Tooltip("Stone in a small / medium / large scenery rock.")]
        public int rockSmall = 2;
        public int rockMedium = 4;
        public int rockLarge = 8;

        [Header("Hunting and food")]
        public float meatPerAnimal = 4f;
        public int hidePerAnimal = 1;
        [Tooltip("Animals one stone spear lasts.")]
        public float stoneSpearAnimals = 4f;
        [Tooltip("Animals one iron spear lasts.")]
        public float ironSpearAnimals = 12f;

        [Header("Warmth")]
        public float warmHutRadius = 30f;
        public float warmMoodBonusPerDay = 0.1f;

        // --- loading ---------------------------------------------------------

        public const string ResourcePath = "EconomyTuning";

        static EconomyTuning active;
        static bool looked, applied;

        /// The asset, or null when there is none (the code defaults then
        /// stand). Loaded once; safe to call from edit-mode tools.
        public static EconomyTuning Active
        {
            get
            {
                if (looked) return active;
                looked = true;
                try { active = Resources.Load<EconomyTuning>(ResourcePath); }
                catch (Exception) { active = null; }
                return active;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            // Domain reload may be off in the editor: forget the last run.
            looked = false; applied = false; active = null;
            EnsureApplied();
        }

        /// Push the asset's tables into `Techs` / `Recipes` once. Idempotent.
        public static void EnsureApplied()
        {
            if (applied) return;
            applied = true;
            var t = Active;
            if (t != null) t.ApplyTables();
        }

        void ApplyTables()
        {
            if (fire != null)
                foreach (var f in fire)
                {
                    var c = f != null ? Techs.CampfireAt(f.level) : null;
                    if (c != null && f.cost != null && f.cost.Length > 0) c.cost = f.cost;
                }
            if (upgrades != null)
                foreach (var u in upgrades)
                {
                    var s = u != null ? Techs.Upgrade(u.planId, u.toLevel) : null;
                    if (s == null) continue;
                    if (u.cost != null && u.cost.Length > 0) s.cost = u.cost;
                    if (u.rateMul > 0f) s.rateMul = u.rateMul;
                }
            if (caps != null)
                foreach (var row in caps)
                {
                    if (row == null || row.copies == null || row.copies.Length == 0) continue;
                    foreach (var c in Techs.Caps)
                        if (c.planId == row.planId) c.copies = row.copies;
                }
            if (recipes != null)
                foreach (var row in recipes)
                {
                    var r = row != null ? Recipes.Named(row.recipeId) : null;
                    if (r == null) continue;
                    if (row.yield > 0) r.yield = row.yield;
                    if (row.ratePerDay > 0f) r.ratePerDay = row.ratePerDay;
                    if (r.tool != null && row.toolWear > 0f) r.toolWear = row.toolWear;
                    if (row.takes != null && row.takes.Length > 0) r.takes = row.takes;
                }
        }

        // --- per-call reads ----------------------------------------------------

        /// The asset's row for a plan, or null (use the plan's own numbers).
        public static PlanRow Plan(string planId)
        {
            var t = Active;
            if (t == null || t.plans == null || string.IsNullOrEmpty(planId)) return null;
            foreach (var p in t.plans) if (p != null && p.planId == planId) return p;
            return null;
        }

        static float F(Func<EconomyTuning, float> pick, float fallback)
        {
            var t = Active;
            return t != null ? pick(t) : fallback;
        }

        public static float CopyPriceStep => F(t => t.copyPriceStep, 0.25f);
        public static float MetresPerPalisadeLog => Mathf.Max(0.1f, F(t => t.metresPerPalisadeLog, 2f));
        public static float LadderTimberPerMetre => F(t => t.ladderTimberPerMetre, 2f);
        public static float RoadMetresPerStone => Mathf.Max(0.5f, F(t => t.roadMetresPerStone, 4f));
        public static float RoadSpeedMultiplier => Mathf.Clamp(F(t => t.roadSpeedMultiplier, 1.3f), 1f, 3f);
        public static float HammerSecondsPerLineLog => F(t => t.hammerSecondsPerLineLog, 3f);
        public static float CrewExponent => Mathf.Clamp(F(t => t.crewExponent, 0.75f), 0.1f, 1f);
        public static float ClearSecondsPerTree => F(t => t.clearSecondsPerTree, 5f);
        public static float ClearSecondsPerRock => F(t => t.clearSecondsPerRock, 8f);
        public static float WarmHutRadius => F(t => t.warmHutRadius, 30f);
        public static float WarmMoodBonusPerDay => F(t => t.warmMoodBonusPerDay, 0.1f);

        /// Seconds to cut/quarry/pick ONE unit at the source, before FEEL's
        /// gather speed.
        public static float CutSeconds(string res)
        {
            var t = Active;
            if (res == Res.Timber) return t != null ? t.cutSecondsTimber : 5f;
            if (res == Res.Stone) return t != null ? t.cutSecondsStone : 8f;
            if (res == Res.Ore) return t != null ? t.cutSecondsOre : 10f;
            if (res == Res.Spice) return t != null ? t.cutSecondsSpice : 6.67f;
            // Anything else keeps `Res.GatherRate`'s relation to timber.
            float timber = t != null ? t.cutSecondsTimber : 5f;
            return timber * Res.GatherRate(Res.Timber) / Mathf.Max(0.01f, Res.GatherRate(res));
        }

        public static int RockUnits(int sizeClass)
        {
            var t = Active;
            if (t == null) return sizeClass <= 0 ? 2 : sizeClass == 1 ? 4 : 8;
            return Mathf.Max(1, sizeClass <= 0 ? t.rockSmall : sizeClass == 1 ? t.rockMedium : t.rockLarge);
        }

        public static float MeatPerAnimal => F(t => t.meatPerAnimal, 4f);
        public static int HidePerAnimal { get { var t = Active; return t != null ? Mathf.Max(0, t.hidePerAnimal) : 1; } }
        public static float SpearAnimals(bool iron) =>
            Mathf.Max(1f, iron ? F(t => t.ironSpearAnimals, 12f) : F(t => t.stoneSpearAnimals, 4f));

        /// **How much faster N builders hammer than one**: N^crewExponent.
        /// 1 → 1, 2 → 1.68, 3 → 2.28, 4 → 2.83 at 0.75. Diminishing on
        /// purpose: four men on one hut get in each other's way.
        public static float CrewSpeed(int builders) =>
            builders <= 1 ? 1f : Mathf.Pow(builders, CrewExponent);

#if UNITY_EDITOR
        /// Rewrite every table from the code's current statics -- the way to
        /// refresh the asset after a code-side change. Right-click the asset.
        [ContextMenu("Capture tables from code")]
        void CaptureFromCode()
        {
            var ps = new System.Collections.Generic.List<PlanRow>();
            foreach (var p in BuildPlans.AtACamp)
                ps.Add(new PlanRow { planId = p.id, timber = p.baseCost, stone = p.baseStoneCost,
                    brick = p.baseBrickCost, hammerSeconds = BuildPlans.DefaultHammerSeconds(p.id) });
            foreach (var p in BuildPlans.All)
                ps.Add(new PlanRow { planId = p.id, timber = p.baseCost, stone = p.baseStoneCost,
                    brick = p.baseBrickCost, hammerSeconds = BuildPlans.DefaultHammerSeconds(p.id) });
            plans = ps.ToArray();
            var fs = new System.Collections.Generic.List<FireRow>();
            foreach (var c in Techs.Campfire) fs.Add(new FireRow { level = c.level, cost = c.baseCost });
            fire = fs.ToArray();
            var us = new System.Collections.Generic.List<UpgradeRow>();
            foreach (var u in Techs.Upgrades)
                us.Add(new UpgradeRow { planId = u.planId, toLevel = u.toLevel, cost = u.baseCost, rateMul = u.rateMul });
            upgrades = us.ToArray();
            var cs = new System.Collections.Generic.List<CapRow>();
            foreach (var c in Techs.Caps) cs.Add(new CapRow { planId = c.planId, copies = c.copies });
            caps = cs.ToArray();
            var rs = new System.Collections.Generic.List<RecipeRow>();
            foreach (var r in Recipes.All)
                rs.Add(new RecipeRow { recipeId = r.id, yield = r.yield, ratePerDay = r.baseRatePerDay,
                    toolWear = r.toolWear, takes = r.takes });
            recipes = rs.ToArray();
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
