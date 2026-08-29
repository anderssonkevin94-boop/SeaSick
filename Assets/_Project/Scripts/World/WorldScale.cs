namespace SeaSick.World
{
    /// **Every canonical size in the game, in metres, in one place.**
    ///
    /// This exists because nothing in this project referenced anything else.
    /// The ship's size came from an FBX import number, the trees from a
    /// constant somebody picked, her top speed from "a game number, not a
    /// physical one" -- each decided on its own against whatever happened to
    /// be on screen that afternoon. So they drifted, and the drift is only
    /// ever visible from inside the game, one screenshot at a time:
    ///
    ///   trees were built 5.2-8.6 m beside a 24.2 m ship, which did not read
    ///   as small trees, it read as a small ISLAND;
    ///   then 11-26 m, which is a truthful conifer and nine times the height
    ///   of a building, so a 2.9 m giant standing next to one is a speck;
    ///   and she still makes 20 m/s on a hull that tops out near 11 knots,
    ///   with her wheels drawn at 34 rpm when 20 m/s needs 151 -- the one
    ///   moving part that tells you your speed, turning at a rate that means
    ///   4.5 m/s.
    ///
    /// The rule from here: a size is decided ONCE, here, next to everything
    /// it has to look right beside. Nothing types a size at the call site.
    /// `ScaleRuler` stands the whole set in a row so a new thing can be
    /// checked against the old ones in a single photograph.
    ///
    /// **Readability beats realism where they disagree.** Real conifers are
    /// 26 m and real paddle steamers make 14 knots. We keep the steamer's
    /// speed, because that one reads correctly; we shrink the tree, because
    /// at its true height nothing else in the frame can be seen against it.
    public static class WorldScale
    {
        /// The unit everything is judged in. A crew member is the ruler
        /// because the crew, the giants and the buildings are all made for
        /// each other -- and because a player knows instinctively how big a
        /// person is, which is not true of a boat or a hill.
        public const float Person = 1.7f;

        /// Big enough to read as wrong from across a beach: 1.7 crew.
        public const float Giant = 2.9f;

        // --- what people build -------------------------------------------
        public const float Hut = 3.0f;          // one storey, a door and a roof
        public const float Palisade = 2.5f;     // a wall you cannot see over
        public const float Longhouse = 7.0f;    // ridge height; the hall of an outpost
        public const float WatchTower = 11.0f;  // sees over the trees, just

        // --- what grows ---------------------------------------------------
        /// Trees are 9-14 m: about six crew, twice a longhouse, half the
        /// ship's length. A real coastal conifer is 26 m and that is exactly
        /// the size at which a building stops being visible beside it.
        public const float TreeMin = 9f;
        public const float TreeMax = 14f;
        public const float BoulderMin = 1.0f;
        public const float BoulderMax = 3.2f;

        // --- the ship -------------------------------------------------------
        /// Her overall length. Authored by SetupPaddleBoat.Scale, which stays
        /// the one knob that resizes her; this is here so everything else can
        /// be compared against it without importing an editor script.
        public const float ShipLength = 24.2f;

        /// Set by what her WHEELS can be drawn at, not by hull physics.
        ///
        /// Displacement hull speed for her 20.9 m waterline is 11.1 knots --
        /// 5.7 m/s -- and 7 m/s was tried and is, in the captain's words,
        /// insufferably slow. 15 m/s is 29 knots, which no hull that shape
        /// makes, so this is a game number and the file says so. What it is
        /// NOT is a lie the player can see: 15 m/s needs 57 rpm of a 2.53 m
        /// wheel, just under eight frames per blade at 60 fps, so the wheels
        /// turn at the rate the speed implies instead of at a third of it.
        ///
        /// The rule this encodes: a number may be generous, but nothing on
        /// screen may contradict it.
        public const float ShipTopSpeed = 15.0f;

        /// Oars. Rowing a loaded boat is 1.5-2.5 m/s and always has been;
        /// hers were set to 5.7, which is 11 knots under oar.
        public const float ShipOarSpeed = 2.5f;

        /// How many crew tall (or long) something is — the sanity check.
        public static float InCrew(float metres) => metres / Person;
    }
}
