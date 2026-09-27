using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SeaSick.World
{
    /// One standing order to move a resource between the camp store and the
    /// ship lying alongside. Saved inside the ledger (JsonUtility-shaped).
    [System.Serializable]
    public class TransferOrder
    {
        public string resource = "";
        /// Store -> ship when true; ship -> store when false.
        public bool toShip;
        /// Units still to start carrying. `OutpostLedger.TransferAll`
        /// (int.MaxValue) = "all of it", never counted down.
        public int left;
    }

    /// <summary>
    /// **Carrying cargo between the ship and the store, 2026-09-24.** Kevin,
    /// iPhone playtest: *"when returning to the island I should have the
    /// option (in the store house, or camp fire) to unload things from my
    /// ship to the island and vice versa. These things should be physically
    /// carried from where they are to where they have been designated."*
    ///
    /// **An order, then trips.** The player places a transfer order
    /// (`OrderTransfer(res, n, toShip)`); nothing moves by itself. Free hands
    /// -- Idle hands, gatherers the store has no room for, and Build hands
    /// with nothing to build -- take it an armful (`Res.Armful`) at a time as
    /// an ordinary timed trip (`StartTimedTrip`) between `HaulPlace.Store`
    /// and `HaulPlace.Ship`, so its time is the walked distance store <->
    /// gangway plus the 1 s handle, like every other trip.
    ///
    /// **The ship end is an `ICargoSide`** (`cargo`, not saved), bound by the
    /// game to `ShipCargoSide` and faked by `StationStockSelfTest`. Trips
    /// only START while `cargo.Present` (she is at this camp); orders PAUSE,
    /// not cancel, while she is away.
    ///
    /// **Conservation (gated, section (n) of the self-test).** Units leave
    /// their source at pickup (trip start -- the same moment as every other
    /// haul) and ride in the hand's arms (`CarriedOf`) until the deposit:
    /// <list type="bullet">
    /// <item>store -> ship: `Store.whole -= n` at start; at the deposit
    ///   `cargo.Give` books what went in; ANY remainder (no room after all,
    ///   she has gone) goes back to the store it came from -- over the
    ///   ceiling if it must, it exists -- and back onto the order's count.</item>
    /// <item>ship -> store: `cargo.Take` at start books exactly what came out;
    ///   the deposit is `DepositHaul`'s store path (the ceiling holds; room
    ///   was reserved at pickup by `RoomFor`'s in-flight count). What cannot
    ///   go in goes back aboard if she is still here, else waits in the arms
    ///   at the store (as any store-bound load does) until room comes.</item>
    /// <item>She casts off mid-trip: a store -> ship armful goes straight back
    ///   to the store at the top of the next step (`SettleTransfers`); a
    ///   ship -> store armful is already off the hold and carries on to the
    ///   store, which is still there.</item>
    /// <item>A hand re-ordered mid-trip: a Build hand carries it on (the
    ///   builder pass walks it); an Idle / station / trip-gatherer hand
    ///   carries it on in his own day; anyone else (farm, hunt) puts it down
    ///   at once where it was heading (`DepositHaul`, same rules as above).
    ///   A hand leaving the camp (`RemoveHand`) puts it down with force.</item>
    /// <item>`CancelTransfers` stops new trips; loads already in arms finish
    ///   their walk and land where they were going.</item>
    /// </list>
    /// An order ends when its count is carried or its source is dry (nothing
    /// of it left in the store / aboard -- checked only while she is here).
    /// </summary>
    public partial class OutpostLedger
    {
        /// "Carry all of it": a transfer order's count that never counts down.
        public const int TransferAll = int.MaxValue;

        /// The standing orders, one per (resource, direction). Saved.
        public List<TransferOrder> transfers = new List<TransferOrder>();

        /// The ship's end (see `ICargoSide`). Not saved; the game re-binds it
        /// on every `Outpost.CatchUp`.
        [System.NonSerialized] public ICargoSide cargo;

        /// Units put into the hold / onto the store by transfer trips since
        /// the ledger was loaded. Not saved: `CampLoading.Moved` reads the
        /// difference since its own start.
        [System.NonSerialized] public int transferredAboard, transferredAshore;

        /// She is at this camp's landing, and trips may start.
        public bool ShipHere => cargo != null && cargo.Present;

        // --- write API ---------------------------------------------------------

        /// **Order `count` units of `res` carried to the ship (`toShip`) or
        /// off her into the store.** `count` = `TransferAll` for "all".
        /// Replaces any order for the same resource and direction, and
        /// cancels one the OTHER way (no carrying the same logs round in a
        /// circle). Refused (false) when she is not alongside or the count
        /// is not positive.
        public bool OrderTransfer(string res, int count, bool toShip)
        {
            if (string.IsNullOrEmpty(res) || count <= 0 || !ShipHere) return false;
            if (transfers == null) transfers = new List<TransferOrder>();
            TransferOrder mine = null;
            for (int i = transfers.Count - 1; i >= 0; i--)
            {
                var o = transfers[i];
                if (o == null || o.resource != res) continue;
                if (o.toShip == toShip) mine = o;
                else transfers.RemoveAt(i);
            }
            if (mine == null) transfers.Add(mine = new TransferOrder { resource = res, toShip = toShip });
            mine.left = count;
            return true;
        }

        /// Stop every transfer order. Loads already in arms finish their walk.
        public void CancelTransfers()
        {
            if (transfers != null) transfers.Clear();
        }

        /// Stop one resource's order in one direction.
        public void CancelTransfer(string res, bool toShip)
        {
            if (transfers == null) return;
            for (int i = transfers.Count - 1; i >= 0; i--)
                if (transfers[i] == null || (transfers[i].resource == res && transfers[i].toShip == toShip))
                    transfers.RemoveAt(i);
        }

        // --- read API ------------------------------------------------------------

        /// An order for `res` this way is standing, or an armful of it is
        /// still walking that way.
        public bool TransferPending(string res, bool toShip)
        {
            var o = TransferOf(res, toShip);
            if (o != null && o.left > 0) return true;
            return CarryingTransfer(res, toShip) > 0;
        }

        /// Any order in `toShip`'s direction standing, or any armful walking.
        public bool AnyTransferPending(bool toShip)
        {
            if (transfers != null)
                foreach (var o in transfers) if (o != null && o.toShip == toShip && o.left > 0) return true;
            return CarryingTransfer(null, toShip) > 0;
        }

        /// Any order either way, or any armful walking either way.
        public bool AnyTransferPending() => AnyTransferPending(true) || AnyTransferPending(false);

        /// Units still to start of this order (`TransferAll` for "all"), 0 for none.
        public int TransferLeft(string res, bool toShip)
        {
            var o = TransferOf(res, toShip);
            return o != null ? Mathf.Max(0, o.left) : 0;
        }

        /// Units of `res` (null: any) in hands' arms on transfer trips `toShip`'s way.
        public int CarryingTransfer(string res, bool toShip)
        {
            int n = 0;
            if (hands == null) return 0;
            foreach (var h in hands)
            {
                if (h == null || !h.Hauling) continue;
                if (res != null && h.haulRes != res) continue;
                if (toShip ? h.haulTo == HaulPlace.Ship : h.haulFrom == HaulPlace.Ship) n += h.haulCount;
            }
            return n;
        }

        /// **Why an order is not moving**, or null while it can: "the ship is
        /// not alongside", "hold full", "store full", "no free hands".
        public string TransferStall(string res, bool toShip)
        {
            var o = TransferOf(res, toShip);
            if (o == null || o.left <= 0) return null;
            if (!ShipHere) return StallAway;
            if (toShip && ShipRoomNet() <= 0) return StallHoldFull;
            if (!toShip && RoomFor(res) <= 0) return StallStoreFull;
            if (CarryingTransfer(res, toShip) <= 0 && !AnyFreeHand()) return StallNoHands;
            return null;
        }

        public const string StallAway = "the ship is not alongside";
        public const string StallHoldFull = "hold full";
        public const string StallStoreFull = "store full";
        public const string StallNoHands = "no free hands";

        /// **One line for the sheet**: "timber 3 to the ship · stone 5 ashore
        /// (store full)", "" when there is nothing ordered or walking.
        /// Allocates: a caller that draws every frame keys it on
        /// `TransferKey()`.
        public string TransferSummary()
        {
            var sb = new StringBuilder();
            if (transfers != null)
                foreach (var o in transfers)
                {
                    if (o == null || o.left <= 0) continue;
                    if (sb.Length > 0) sb.Append(" · ");
                    sb.Append(Friendly(o.resource)).Append(' ')
                      .Append(o.left == TransferAll ? "all" : o.left.ToString())
                      .Append(o.toShip ? " to the ship" : " ashore");
                    string why = TransferStall(o.resource, o.toShip);
                    if (why != null) sb.Append(" (").Append(why).Append(')');
                }
            int aboard = CarryingTransfer(null, true), ashore = CarryingTransfer(null, false);
            if (aboard + ashore > 0)
            {
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append(aboard + ashore).Append(" being carried");
            }
            return sb.ToString();
        }

        /// Changes whenever `TransferSummary` could.
        public long TransferKey()
        {
            long k = (ShipHere ? 1 : 0) + 2L * (cargo != null ? cargo.Room : 0);
            if (transfers != null)
                foreach (var o in transfers)
                {
                    if (o == null) continue;
                    k = k * 31 + (o.resource ?? "").GetHashCode();
                    k = k * 31 + o.left + (o.toShip ? 7 : 0);
                    k = k * 31 + RoomFor(o.resource);
                }
            return k * 31 + CarryingTransfer(null, true) * 1009 + CarryingTransfer(null, false);
        }

        // --- the books -------------------------------------------------------------

        TransferOrder TransferOf(string res, bool toShip)
        {
            if (transfers == null || string.IsNullOrEmpty(res)) return null;
            foreach (var o in transfers)
                if (o != null && o.resource == res && o.toShip == toShip) return o;
            return null;
        }

        /// A load on a transfer trip (either end the ship).
        static bool IsTransferHaul(OutpostHand h) =>
            h != null && h.Hauling && (h.haulTo == HaulPlace.Ship || h.haulFrom == HaulPlace.Ship);

        /// Hold room net of armfuls already walking to her (all kinds share it).
        int ShipRoomNet() =>
            cargo == null ? 0 : Mathf.Max(0, cargo.Room - CarryingTransfer(null, true));

        /// A hand who could take a transfer armful this step.
        bool AnyFreeHand()
        {
            if (hands == null) return false;
            foreach (var h in hands)
            {
                if (h == null || h.Hauling) continue;
                if (h.order == OutpostOrder.Idle || h.order == OutpostOrder.Build || GatherBlocked(h)) return true;
            }
            return false;
        }

        /// Undo `n` of a started armful on its order, if the order stands.
        void ReturnToOrder(string res, bool toShip, int n)
        {
            var o = TransferOf(res, toShip);
            if (o != null && o.left != TransferAll && n > 0) o.left += n;
        }

        /// **Top of every `Step`.** She is gone: every store -> ship armful
        /// goes back to the store now (the orders stay, paused). She is here:
        /// orders that are done, or whose source is dry, are struck.
        void SettleTransfers()
        {
            if (!ShipHere)
            {
                if (hands != null)
                    foreach (var h in hands)
                        if (h != null && h.Hauling && h.haulTo == HaulPlace.Ship)
                        {
                            // Not yet picked up: nothing to take back.
                            if (!h.haulPicked) CancelPlanned(h);
                            else DepositToShip(h, false);
                        }
                return;
            }
            if (transfers == null) return;
            for (int i = transfers.Count - 1; i >= 0; i--)
            {
                var o = transfers[i];
                if (o == null || string.IsNullOrEmpty(o.resource) || o.left <= 0) { transfers.RemoveAt(i); continue; }
                int source = o.toShip ? StoreCountOf(o.resource) : cargo.HeldOf(o.resource);
                if (source <= 0) transfers.RemoveAt(i);
            }
        }

        /// **Start one transfer armful**, oldest order first. The source gives
        /// the armful up now, exactly (`Store.whole -=` / `cargo.Take`).
        bool StartTransferTrip(OutpostHand h)
        {
            if (transfers == null || transfers.Count == 0 || !ShipHere) return false;
            foreach (var o in transfers)
            {
                if (o == null || o.left <= 0 || string.IsNullOrEmpty(o.resource)) continue;
                string res = o.resource;
                int n = Mathf.Min(Mathf.Max(1, Res.Armful(res)), o.left);
                if (o.toShip)
                {
                    n = Mathf.Min(n, Mathf.Min(StoreFree(res), ShipRoomNet()));
                    if (n <= 0) continue;
                    // The store gives it up at the PICKUP (2026-09-27).
                    StartTimedTrip(h, res, n, HaulPlace.Store, -1, HaulPlace.Ship, -1);
                }
                else
                {
                    n = Mathf.Min(n, Mathf.Min(cargo.HeldOf(res) - Claimed(HaulPlace.Ship, -1, res, false), RoomFor(res)));
                    if (n <= 0) continue;
                    // The hold gives it up at the PICKUP (the gangway foot).
                    StartTimedTrip(h, res, n, HaulPlace.Ship, -1, HaulPlace.Store, -1);
                }
                if (o.left != TransferAll) o.left -= n;
                return true;
            }
            return false;
        }

        /// **A transfer carrier's day**: walk the armful in his arms, then
        /// armful after armful while there is one to take. Returns with the
        /// budget unspent when there is nothing (the caller may use it).
        void TransferDay(OutpostHand h, ref float budget)
        {
            for (int guard = 0; guard < 64 && budget > Eps; guard++)
            {
                if (h.Hauling)
                {
                    // Only a transfer armful is this day's to walk; any other
                    // load is its own pass's (the builder's site trip, a
                    // station load `StepStations` puts down).
                    if (!IsTransferHaul(h)) return;
                    if (!AdvanceHaul(h, ref budget)) return;
                    continue;
                }
                if (!StartTransferTrip(h)) return;
            }
        }

        /// **A store -> ship armful is put down.** Into the hold as far as she
        /// takes it (she must be here); the rest back to the store it came
        /// from -- over the ceiling if need be, it exists -- and back on the
        /// order. Never drops anything.
        void DepositToShip(OutpostHand h, bool force)
        {
            string res = h.haulRes;
            int n = h.haulCount;
            int put = ShipHere ? Mathf.Clamp(cargo.Give(res, n), 0, n) : 0;
            transferredAboard += put;
            int back = n - put;
            if (back > 0)
            {
                Store(res, true).whole += back;
                ReturnToOrder(res, true, back);
            }
            ClearHaul(h);
        }
    }
}
