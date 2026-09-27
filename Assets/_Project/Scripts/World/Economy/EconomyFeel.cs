using UnityEngine;

namespace SeaSick.World.Economy
{
    /// **The economy knobs on the phone's FEEL panel (Kevin, 2026-09-27).**
    /// `Dev/FeelLab` finds public static non-readonly floats by reflection
    /// (its `TypeFullNames`), so these are live: move one on the phone and
    /// the next price, hammer tick, bench tick, cut or kill reads it. The
    /// base numbers they multiply live in `EconomyTuning` (the asset); these
    /// are the dials you reach for first.
    ///
    /// Every one defaults to the asset's own value (multipliers 1), so a
    /// build with the FEEL panel never opened plays exactly the asset.
    public static class EconomyFeel
    {
        /// Every building / upgrade / fire price, all materials (rounded up).
        public static float costMultiplier = 1f;
        /// Hammer time of every site. 2 = twice as long.
        public static float buildTimeMultiplier = 1f;
        /// Output rate of every station recipe (sawmill, forge, kitchen,
        /// fletcher, quarry, fishing hut).
        public static float stationSpeedMultiplier = 1f;
        /// Cut / quarry / pick speed at the source. 2 = twice as fast.
        public static float gatherSpeedMultiplier = 1f;
        /// Food per animal a hunter brings home (asset default 4).
        public static float meatPerAnimal = 3f;
        /// Hide per animal (asset default 1; whole units, rounded).
        public static float hidePerAnimal = 1f;

        public static float CostMul => Mathf.Max(0.05f, costMultiplier);
        public static float BuildTimeMul => Mathf.Max(0.05f, buildTimeMultiplier);
        public static float StationSpeed => Mathf.Max(0.05f, stationSpeedMultiplier);
        public static float GatherSpeed => Mathf.Max(0.05f, gatherSpeedMultiplier);

        /// Scale a price, rounding up so nothing positive ever becomes free.
        public static int Price(int n) =>
            n <= 0 || Mathf.Approximately(CostMul, 1f) ? n : Mathf.Max(1, Mathf.CeilToInt(n * CostMul - 1e-4f));

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            // Start from the asset (FeelLab's saved values are applied on top
            // when it first sees this class).
            meatPerAnimal = EconomyTuning.MeatPerAnimal;
            hidePerAnimal = EconomyTuning.HidePerAnimal;
        }
    }
}
