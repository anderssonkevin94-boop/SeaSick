namespace SeaSick.World
{
    /// **Where this camp's fire cache stands (2026-10-03), saved.**
    ///
    /// `Outpost.PlaceFireCache` picks the spot once -- the first of a
    /// fixed list of bearings round the fire that is clear of the supper
    /// ring, every building, every drawing, wall and road -- and records it
    /// here, so every later load stands it at exactly the same place
    /// without asking again (buildings-never-move, applied to the one
    /// piece of camp furniture the player never placed). The spot is only
    /// trusted while the fire is where it was chosen for (`fireX/fireZ`):
    /// a moved fire picks again.
    ///
    /// JsonUtility leaves a field its JSON does not mention at its
    /// constructed value, so a save from before this has `set == false`
    /// and simply picks on its first load. Nothing else reads these.
    [System.Serializable]
    public class FireCacheSpot
    {
        /// A spot was chosen (false = never chosen, or nothing was clear).
        public bool set;
        /// The fire it was chosen beside (its root x/z).
        public float fireX, fireZ;
        /// The cache root, world x/z, and its yaw (front +Z toward the fire).
        public float x, z, yaw;
        /// Which bearing of the list won (0 = straight inland), for reports.
        public int bearing;
    }

    public partial class OutpostLedger
    {
        /// See `FireCacheSpot`.
        public FireCacheSpot fireCache = new FireCacheSpot();
    }
}
