using System.Collections.Generic;

namespace SeaSick.World
{
    /// **A ladder chain that STANDS, in the record (2026-09-27).**
    ///
    /// Two points, like a wall segment: the foot and the top as the player
    /// sited them (heights are re-read from the field on load, and the
    /// chain is re-laid from them by `LadderLayout.Plan`, so the flights and
    /// landings are derived, never saved). A save written before ladders
    /// existed has no `builtLadders` at all, which reads as "none" -- old
    /// saves load unchanged.
    [System.Serializable]
    public class BuiltLadder
    {
        public float fx, fz, tx, tz;

        public UnityEngine.Vector3 Foot => new UnityEngine.Vector3(fx, 0f, fz);
        public UnityEngine.Vector3 Top => new UnityEngine.Vector3(tx, 0f, tz);
    }

    public partial class OutpostLedger
    {
        /// Standing ladder chains. A queued one is an ordinary row in
        /// `sites` (`planId == BuildPlans.Ladder.id`, `postA` foot, `postB`
        /// top) until it is raised.
        public List<BuiltLadder> builtLadders = new List<BuiltLadder>();
    }
}
