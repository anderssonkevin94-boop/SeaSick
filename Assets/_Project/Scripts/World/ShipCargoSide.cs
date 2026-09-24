using SeaSick.Ship;
using SeaSick.Voyage;
using UnityEngine;

namespace SeaSick.World
{
    /// **The game's `ICargoSide`**: the real ship, as seen from one camp.
    ///
    /// Bound onto the camp's ledger by `Outpost.CatchUp` (`BindTo`). Plain
    /// C#; the scene objects are found once and re-found when Unity's fake
    /// null says the cached one is gone (a new play session -- domain reload
    /// is off in this project).
    public sealed class ShipCargoSide : ICargoSide
    {
        readonly Outpost camp;

        public ShipCargoSide(Outpost camp) { this.camp = camp; }

        /// Put a ship side on this camp's ledger if it has none (or one for
        /// a different camp -- a ledger swapped in by a load).
        public static void BindTo(Outpost camp)
        {
            var l = camp != null ? camp.Ledger : null;
            if (l == null) return;
            if (l.cargo is ShipCargoSide s && s.camp == camp) return;
            l.cargo = new ShipCargoSide(camp);
        }

        static VoyageManager voyage;
        static ShipHold hold;
        static AnchorController anchor;
        static Gangway gangway;

        static VoyageManager V => voyage != null ? voyage : (voyage = Object.FindFirstObjectByType<VoyageManager>());
        static ShipHold H => hold != null ? hold : (hold = Object.FindFirstObjectByType<ShipHold>());
        static AnchorController A => anchor != null ? anchor : (anchor = Object.FindFirstObjectByType<AnchorController>());
        static Gangway G => gangway != null ? gangway : (gangway = Object.FindFirstObjectByType<Gangway>());

        public bool Present => CampLoading.Alongside(camp) && V != null && !V.AtHome;

        public int Room => CampLoading.RoomAboard(V);

        public int HeldOf(string res) => V != null ? V.HeldOf(res) : 0;

        public int Take(string res, int n)
        {
            var v = V;
            if (v == null || n <= 0) return 0;
            int got = v.RemoveLoot(n, res);
            // The stack on deck comes down with the weight. `ShipHold`'s
            // LateUpdate would trim it anyway; doing it here makes it the same
            // frame, and only while the stack shows more than she holds (it
            // caps at `maxVisible`, so a big hold loses no visual early).
            var h = H;
            if (h != null)
                for (int i = 0; i < got && h.VisibleCount > v.TotalHeld; i++) h.RemoveVisual();
            return got;
        }

        public int Give(string res, int n)
        {
            var v = V;
            if (v == null || n <= 0) return 0;
            n = Mathf.Min(n, Room);
            if (n <= 0) return 0;
            // Measured, not assumed (the `LoadNow` rule): `AddLoot` refuses
            // silently at home or stuffed.
            int before = v.TotalHeld;
            v.AddLoot(n, res);
            int got = Mathf.Max(0, v.TotalHeld - before);
            var h = H;
            // Once per unit, or the stack never grows (LateUpdate only removes).
            if (h != null) for (int i = 0; i < got; i++) h.AddVisual(res);
            return got;
        }

        /// The foot of the plank if it is down (at a camp pier that is the
        /// pier's ROOT -- `AnchorController` extends it to `Dock.Landing`),
        /// else the dock she is tied to, else the nearest dock to the camp,
        /// else her own position. The land end, never the pier head: the
        /// camp's walk grid (`CampPath`) has no pier deck, and a body routed
        /// over water would walk on the sea floor.
        public bool GangwayAt(out Vector3 at)
        {
            var g = G;
            if (g != null && g.Ready) { at = g.LandingPoint; return true; }
            var a = A;
            if (a != null && a.CurrentDock != null) { at = a.CurrentDock.Landing; return true; }
            var d = camp != null ? Dock.Nearest(camp.CampCentre) : Dock.Home;
            if (d == null) d = Dock.Home;
            if (d != null) { at = d.Landing; return true; }
            if (a != null) { at = a.transform.position; return true; }
            at = default;
            return false;
        }

        /// Where the ship herself is, for a body to face when it sets a
        /// load down at the plank.
        public static bool ShipAt(out Vector3 at)
        {
            var a = A;
            if (a != null) { at = a.transform.position; return true; }
            at = default;
            return false;
        }
    }
}
