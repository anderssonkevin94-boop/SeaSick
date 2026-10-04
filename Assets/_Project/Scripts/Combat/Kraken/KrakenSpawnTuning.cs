namespace SeaSick.Combat
{
    /// **The kraken in the wild: when, where and how it is announced**
    /// (GDD §6 "The Kraken", build step 4).
    ///
    /// Plain static fields, floats and bools only, so the feel lab writes
    /// them by reflection on these exact names (`FeelLab.TypeFullNames`) and
    /// `KrakenDirector` / `KrakenLoot` read them EVERY time they use them,
    /// never cached -- the same contract as `KrakenTuning`. Counts are floats
    /// because the lab has no integer slider; they are rounded where used.
    ///
    /// All numbers: first pass, tune by play. The shape of the odds is the
    /// approved design ("distance is the difficulty"): nothing inside
    /// `minDistanceFromHome`, then a small base chance per check that climbs
    /// with every kilometre beyond it, multiplied up at night and in a storm,
    /// at most one per voyage, with a cooldown after it has gone.
    public static class KrakenSpawnTuning
    {
        // ------------------------------------------------------------- the odds
        /// Master switch for wild krakens. Off = never rolls; a dev summon and
        /// `KrakenDirector.DevWarnNow` still work.
        public static bool enabled = true;
        /// Nothing inside this, flat metres from home (the start point the
        /// raider rings are laid from). The calm shelf stays calm.            0..3000
        public static float minDistanceFromHome = 700f;
        /// Real seconds between rolls while she is sailing out there.         3..120
        public static float checkIntervalSeconds = 20f;
        /// Chance per roll the moment she is past `minDistanceFromHome`.       0..0.3
        public static float baseChancePerCheck = 0.01f;
        /// Added to that chance for every further kilometre from home.        0..0.2
        public static float chancePerExtraKm = 0.02f;
        /// The chance never goes above this, whatever the sky and distance.   0..1
        public static float maxChancePerCheck = 0.35f;
        /// Chance multiplier at night (`Sailing.IsNight`).                    1..5
        public static float nightMultiplier = 1.6f;
        /// Chance multiplier at full storm (`Sailing.Storminess01` = 1),
        /// scaled down to 1 in calm weather.                                  1..6
        public static float stormMultiplier = 2.5f;
        /// Real seconds after one has gone (driven off, escaped, dismissed)
        /// before another can be rolled.                                      0..1800
        public static float cooldownSeconds = 600f;

        // ------------------------------------------------------- the warning
        /// Seconds from the first sign (toast, vibration, dark water) to the
        /// breach. Never an ambush: Kevin asked for ~10.                      4..20
        public static float warningSeconds = 10f;
        /// How far off the ship the water darkens and it surfaces, metres.
        /// Outside the ~40 m arm reach, same as `KrakenTuning.spawnDistance`.  45..120
        public static float spawnDistance = 55f;

        // ------------------------------------------------------- deep water only
        /// Open water it needs under it, metres: at the spot AND on a ring
        /// round it, so the body never overhangs a shelf. The shelf floor is
        /// -12 m (`TerrainSettings.seabedDepth`), the open ocean -180 m.      12..150
        public static float minDeepDepth = 60f;
        /// Clearance it keeps from every island's widest shore and from
        /// every reef's rock, metres, at the spot and on its ring.            0..400
        public static float minIslandClearance = 150f;

        // ------------------------------------------------------------------ loot
        /// Units of meat in the haul it leaves when driven off, split over
        /// `lootMeatCrates` crates that float on the water.                   1..60
        public static float lootMeatUnits = 24f;
        public static float lootMeatCrates = 3f;
        /// Units of kraken ink: the rare trophy, one crate of its own.        1..5
        public static float lootInkUnits = 1f;
        /// How wide each loot crate is DRAWN, metres (the reward is the units
        /// above, whatever the size). Measured 2026-10-04: the meat crates
        /// drew 0.81 m and the ink 0.45 m against 1.61 m for an ordinary lost
        /// crate, and the ink could not be seen from the sailing camera.   0.5..4
        public static float lootMeatSpan = 1.6f;
        public static float lootInkSpan = 0.9f;
    }
}
