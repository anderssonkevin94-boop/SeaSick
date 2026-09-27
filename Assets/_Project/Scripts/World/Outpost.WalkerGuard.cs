using UnityEngine;

namespace SeaSick.World
{
    /// **A walker stands on his own island, on its ground (2026-09-27).**
    ///
    /// The pre-build check berthed her at home and the whole camp on
    /// Island_2 snapped into a deck-shaped cluster 5 m under the ground by the
    /// island's origin (`CrewAgent.Rest` writing deck `stationLocal` into a
    /// camp body; fixed there). The bodies report where they stand into the
    /// books every frame (`OutpostLedger.BodyAt`), so the bad spot went into
    /// `wx/wz` and would have gone into Kevin's save. Two guards, whatever
    /// moves a body next time:
    ///
    /// * **Write-back**: a body's spot is only booked when it is on this
    ///   camp's island and not buried (more than `BuriedMetres` under the
    ///   ground). Otherwise the walker keeps his last good spot.
    /// * **Put-down**: a body placed from its walker (`ArrangeHands`, i.e.
    ///   every arrival and every load) whose booked spot is off the island
    ///   goes to the stores instead (it is put at ground height anyway) -- a save already written
    ///   with a bad spot heals on load.
    ///
    /// Above the ground is allowed: a hand on a ladder platform, a wall walk
    /// or the pier is standing on something, not in something.
    public partial class Outpost
    {
        /// Deeper than this under the terrain = buried, not standing.
        const float BuriedMetres = 2f;
        /// Slack past the island's shoreline radius, so a pier or a beach
        /// errand still counts as on the island.
        const float ShoreSlackMetres = 60f;

        /// Is `p` a spot a walker of this camp may be booked at?
        /// `checkY` false = only the plan position (a saved `wx/wz`).
        public bool WalkerSpotOk(Vector3 p, bool checkY)
        {
            if (float.IsNaN(p.x) || float.IsNaN(p.z) || float.IsInfinity(p.x) || float.IsInfinity(p.z))
                return false;
            var isle = Island;
            if (isle != null)
            {
                Vector3 d = p - isle.transform.position;
                d.y = 0f;
                if (d.magnitude > isle.RadiusToward(p) + ShoreSlackMetres) return false;
            }
            if (!checkY || height == null) return true;
            return p.y >= height(p.x, p.z) - BuriedMetres;
        }

        /// Wire the ledger's write-back guard. Cheap; called from `CatchUp`
        /// and `ArrangeHands`.
        void WireWalkerGuard()
        {
            if (ledger != null && ledger.walkerOk == null)
                ledger.walkerOk = p => WalkerSpotOk(p, true);
        }

        /// Where a body with a bad booked spot is put: the stores, else the
        /// camp centre.
        Vector3 WalkerFallback()
        {
            if (ledger != null && ledger.StoreAt(out var s)) return s;
            return CampCentre;
        }
    }
}
