using UnityEngine;

namespace SeaSick.World
{
    /// **The ship's end of a camp's transfer orders, 2026-09-24.**
    ///
    /// `OutpostLedger` is plain C# and must stay testable without a scene
    /// (`StationStockSelfTest`), but the hold lives on `VoyageManager`, a
    /// MonoBehaviour. So the ledger talks to the ship only through this: the
    /// game binds `ShipCargoSide` (VoyageManager + ShipHold + the gangway) in
    /// `Outpost.CatchUp`, the self-test binds a fake.
    ///
    /// Every call is exact: `Take`/`Give` return the units that actually
    /// moved, and the ledger books only those, so conservation never rests
    /// on the other side agreeing with a number it was not given.
    public interface ICargoSide
    {
        /// She is lying at THIS camp (`CampLoading.Alongside`). Transfer
        /// orders only start trips while this is true and pause otherwise.
        bool Present { get; }

        /// Units the hold will take right now, all kinds together -- the
        /// player's line, or the physical limit with deck cargo
        /// (`CampLoading.RoomAboard`).
        int Room { get; }

        /// Units of `res` aboard.
        int HeldOf(string res);

        /// Take up to `n` of `res` out of the hold; returns what came out.
        int Take(string res, int n);

        /// Put up to `n` of `res` into the hold (clamped to `Room`); returns
        /// what went in.
        int Give(string res, int n);

        /// World point where a hand picks up / sets down at the ship: the
        /// foot of the gangway. False when there is nowhere to point at.
        bool GangwayAt(out Vector3 at);
    }
}
