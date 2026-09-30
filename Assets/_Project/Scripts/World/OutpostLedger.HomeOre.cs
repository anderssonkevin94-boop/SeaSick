namespace SeaSick.World
{
    /// **The first camp's ore outcrop, saved (2026-09-30).** Kevin: *"Give
    /// Island_6 ore too."* The island of the player's FIRST camp always has a
    /// small ore outcrop, laid at camp time (`Outpost.EnsureHomeOre`) on
    /// ground clear of everything the camp has put there, and the spots are
    /// kept here so a later build, a new building or a reload puts the same
    /// rocks in the same places. All additive: a save without these fields
    /// reads them empty and is decided on its next `CatchUp`.
    public partial class OutpostLedger
    {
        /// The first-camp question has been asked and answered (once, ever).
        public bool firstCampChecked;
        /// This is the first camp the player made (the "home camp"): the
        /// first ledger to ask, while no other camp already held the flag.
        public bool firstCamp;
        /// World XZ of each ore rock laid for the first camp. Empty until
        /// laid; never rewritten once there.
        public float[] homeOreX = new float[0];
        public float[] homeOreZ = new float[0];

        /// Rocks were laid for this camp (`Outpost.EnsureHomeOre`).
        public bool HasHomeOre => homeOreX != null && homeOreX.Length > 0;
    }
}
