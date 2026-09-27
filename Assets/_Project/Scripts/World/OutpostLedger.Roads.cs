using System.Collections.Generic;

namespace SeaSick.World
{
    /// **A length of road the player laid, in the record (2026-09-27).**
    ///
    /// Kevin: *"I'd rather place it myself, give the villagers a slight
    /// speed boost when using it."* Two points, like a wall segment or a
    /// ladder: a tap-to-tap run is saved as the segments it was confirmed
    /// in, and the drawing (`CampRoads`) joins segments that share an end
    /// back into one smooth line. Heights are re-read from the ground.
    ///
    /// The worn roads of 2026-09-26 (`roadCell` / `roadWear`) are gone: a
    /// save that still carries those rows loads with them ignored
    /// (`JsonUtility` skips fields the class no longer has), and a save
    /// written before player roads has no `builtRoads`, which reads as none.
    [System.Serializable]
    public class BuiltRoad
    {
        public float ax, az, bx, bz;

        public UnityEngine.Vector3 A => new UnityEngine.Vector3(ax, 0f, az);
        public UnityEngine.Vector3 B => new UnityEngine.Vector3(bx, 0f, bz);
    }

    public partial class OutpostLedger
    {
        /// Standing road segments. A queued one is an ordinary row in
        /// `sites` (`planId == BuildPlans.Road.id`, `postA`/`postB` its ends)
        /// until it is raised.
        public List<BuiltRoad> builtRoads = new List<BuiltRoad>();
    }
}
