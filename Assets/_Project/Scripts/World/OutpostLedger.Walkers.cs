using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **Carrying is physical (2026-09-27, docs/DELIVERY-ON-ARRIVAL.md).**
    ///
    /// Kevin: *"I don't want walking time to be factored into building
    /// something ... A villager walking with that resource has that resource
    /// on him and it gets where it gets when it gets there. There's no time
    /// or any equation that needs to be made in that regard. Only when a
    /// villager has delivered the object will the object be counted."*
    ///
    /// Every trip is walked, leg by leg (`TripLeg`). The ledger books exactly
    /// two events per trip: the PICKUP (`FinishPickup`: the source gives the
    /// load up, clamped to what is really there) and the DROP-OFF
    /// (`DepositHaul`, on arrival). Between them the load is on the hand.
    ///
    /// Who walks: the body (`CampWorker`) while the camp is watched
    /// (`OutpostHand.driven`; it reports `BodyAt` / `BodyArrived` /
    /// `BodyWorked`), else this file's invisible walker, advanced by `Step`
    /// at `WalkMetresPerSecond` along the leg's measured route. Same events,
    /// same rules -- a watched and an unwatched camp differ only in who moves
    /// the feet.
    public partial class OutpostLedger
    {
        /// Seconds of the spear jab at the animal: a pose, not a schedule.
        public const float JabSeconds = 1.5f;
        /// A headless walker this close to his goal is there.
        const float ArriveMetres = 0.5f;
        /// A DRIVEN body this close to a site / bench counts as on it: the
        /// body stands at its own work spot, a pace or two off the footprint.
        public const float OnSiteMetres = 12f;

        /// **Walked metres from a to b on this camp's path grid**, or < 0 when
        /// no grid is loaded. Set by `Outpost.CatchUp` (a `CampPath` plan);
        /// null for a camp with no scene (a probe's ledger): straight lines.
        [System.NonSerialized] public System.Func<Vector3, Vector3, float> router;

        public static float SecondsToDaysPublic(float s) => SecondsToDays(s);

        /// Where this hand is standing (the walker, or the body's last spot).
        public Vector3 HandAt(OutpostHand h)
        {
            if (h != null && h.wHas) return new Vector3(h.wx, 0f, h.wz);
            if (StoreAt(out var s)) return s;
            return Vector3.zero;
        }

        static void SetHandAt(OutpostHand h, Vector3 p)
        {
            h.wHas = true;
            h.wx = p.x;
            h.wz = p.z;
        }

        float RouteMetres(Vector3 a, Vector3 b)
        {
            a.y = 0f; b.y = 0f;
            float straight = Vector3.Distance(a, b);
            if (straight < 1f || router == null) return straight;
            float r = router(a, b);
            return r >= straight ? r : straight;
        }

        /// **Where the island's `res` is fetched from**, for a walker that
        /// has no body to pick its own tree: `SourceMetres(res)` out from the
        /// centre, on a bearing fixed per resource (a stable hash, so a save
        /// reloads to the same spot).
        public Vector3 FieldPoint(string res)
        {
            CentreAt(out var c);
            uint hsh = 2166136261u;
            if (!string.IsNullOrEmpty(res))
                foreach (char ch in res) { hsh ^= ch; hsh *= 16777619u; }
            float a = (hsh % 360u) * Mathf.Deg2Rad;
            float m = SourceMetres(res);
            return new Vector3(c.x + Mathf.Cos(a) * m, 0f, c.z + Mathf.Sin(a) * m);
        }

        // --- starting a trip --------------------------------------------------

        void StartTrip(OutpostHand h, string res, int n, HaulPlace from, int fromStation,
            HaulPlace to, int toStation, bool fromBay = false)
        {
            h.haulSerial++;
            h.haulFromBay = fromBay;
            h.haulRes = res;
            h.haulCount = n;
            h.haulFrom = from;
            h.haulFromStation = fromStation;
            h.haulTo = to;
            h.haulToStation = toStation;
            h.haulDays = h.haulLeft = 0f;          // no trip timer any more
            h.haulWalkDays = h.haulWorkDays = 0f;
        }

        /// **Start a walked trip.** The load is PLANNED (not taken): he walks
        /// from where he stands to the pickup, works it (`workLeft`: a stoop,
        /// or cutting `n` at the island), picks up what is really there, and
        /// carries it to the drop-off. `picked` = he already has it in his
        /// arms (a log off a plot he just cleared, a farm's basket).
        void StartTimedTrip(OutpostHand h, string res, int n, HaulPlace from, int fromStation,
            HaulPlace to, int toStation, PendingBuild site = null, bool fromBay = false,
            bool picked = false)
        {
            StartTrip(h, res, n, from, fromStation, to, toStation, fromBay);
            Vector3 here = HandAt(h);
            Vector3 fa;
            bool pa;
            if (from == HaulPlace.Field) { fa = FieldPoint(res); pa = true; }
            else pa = PlaceOf(from, fromStation, site, out fa);
            bool pb = PlaceOf(to, toStation, site, out var ta);
            // An end the books cannot place (a probe's hand-written ledger):
            // `DefaultLegMetres` from the other end.
            var off = new Vector3(DefaultLegMetres, 0f, 0f);
            if (!pa && !pb) { fa = here; ta = here + off; }
            else if (!pa) fa = ta + off;
            else if (!pb) ta = fa + off;
            h.haulPlaced = true;
            h.haulFromX = fa.x; h.haulFromZ = fa.z;
            h.haulToX = ta.x; h.haulToZ = ta.z;
            h.workLeft = HandleSeconds + (from == HaulPlace.Field ? n * GatherSecondsPerUnit(res) : 0f);
            h.haulPicked = picked;
            if (picked)
            {
                h.tripLeg = (int)TripLeg.ToDrop;
                h.legLeft = RouteMetres(here, ta);
            }
            else
            {
                h.tripLeg = (int)TripLeg.ToPickup;
                h.legLeft = RouteMetres(here, fa);
            }
        }

        Vector3 PlaceAt(HaulPlace place, int station, PendingBuild site)
        {
            PlaceOf(place, station, site, out var at);
            return at;
        }

        static Vector3 PickPoint(OutpostHand h) => new Vector3(h.haulFromX, 0f, h.haulFromZ);
        static Vector3 DropPoint(OutpostHand h) => new Vector3(h.haulToX, 0f, h.haulToZ);

        /// **A trip from an old save** (booked by the timer, `tripLeg` 0):
        /// the old books took the load at dispatch, so it is on him and he
        /// carries it from where he stands; a hunt not yet killed walks out.
        void MigrateTrip(OutpostHand h)
        {
            if (h == null || !h.Hauling || h.tripLeg != 0) return;
            bool picked = !(h.HuntTrip && !h.huntKilled);
            if (!h.haulPlaced)
            {
                Vector3 fa = h.haulFrom == HaulPlace.Field ? FieldPoint(h.haulRes)
                    : (PlaceOf(h.haulFrom, h.haulFromStation, null, out var a) ? a : HandAt(h));
                Vector3 ta = PlaceOf(h.haulTo, h.haulToStation, SiteWanting(h.haulRes), out var b) ? b : HandAt(h);
                h.haulFromX = fa.x; h.haulFromZ = fa.z;
                h.haulToX = ta.x; h.haulToZ = ta.z;
                h.haulPlaced = true;
            }
            h.haulPicked = picked;
            h.workLeft = h.HuntTrip ? JabSeconds : 0f;
            h.tripLeg = (int)(picked ? TripLeg.ToDrop : TripLeg.ToPickup);
            h.legLeft = RouteMetres(HandAt(h), picked ? DropPoint(h) : PickPoint(h));
        }

        // --- claims on a source (planned, not yet picked up) -----------------

        /// Units of `res` walkers are on their way to pick up from this
        /// source. The source still holds them (it changes only at pickup);
        /// a planner must not send a second hand for the same units.
        int Claimed(HaulPlace from, int station, string res, bool bay)
        {
            int n = 0;
            if (hands == null) return 0;
            foreach (var h in hands)
            {
                if (h == null || !h.Hauling || h.haulPicked || h.HuntTrip) continue;
                if (h.haulFrom != from || h.haulRes != res) continue;
                if (from == HaulPlace.Station && (h.haulFromStation != station || h.haulFromBay != bay)) continue;
                n += h.haulCount;
            }
            return n;
        }

        /// Store units nobody is already walking to fetch.
        int StoreFree(string res) => Mathf.Max(0, StoreCountOf(res) - Claimed(HaulPlace.Store, -1, res, false));

        /// Standing units on the island nobody is already walking to cut.
        int FieldFree(string res)
        {
            var stock = Stock(res);
            int standing = stock != null ? Mathf.FloorToInt(stock.standing + 1e-4f) : 0;
            return Mathf.Max(0, standing - Claimed(HaulPlace.Field, -1, res, false));
        }

        /// A station row's units nobody is already walking to fetch.
        int RowFree(int station, OutpostStore row, bool bay) =>
            row == null ? 0 : Mathf.Max(0, row.whole - Claimed(HaulPlace.Station, station, row.resource, bay));

        // --- the pickup --------------------------------------------------------

        /// **The pickup event.** The source gives up what it really has of
        /// the planned load, now. False (and the trip is over) when it has
        /// none: somebody else took it, a raid, the herd moved on.
        bool PickUp(OutpostHand h)
        {
            string res = h.haulRes;
            int want = h.haulCount;
            int got = 0;
            switch (h.haulFrom)
            {
                case HaulPlace.Store:
                {
                    var st = Store(res);
                    got = st != null ? Mathf.Min(want, st.whole) : 0;
                    if (got > 0) st.whole -= got;
                    if (h.haulTo == HaulPlace.Ship && got < want) ReturnToOrder(res, true, want - got);
                    break;
                }
                case HaulPlace.Station:
                {
                    var s = stations != null && h.haulFromStation >= 0 && h.haulFromStation < stations.Count
                        ? stations[h.haulFromStation] : null;
                    var row = s == null ? null : h.haulFromBay ? s.Bay(res) : s.Rack(res);
                    got = row != null ? Mathf.Min(want, row.whole) : 0;
                    if (got > 0) row.whole -= got;
                    break;
                }
                case HaulPlace.Field:
                {
                    if (res == Res.Game)
                    {
                        if (!HuntKill(h)) return false;
                        got = 1;
                        break;
                    }
                    var stock = Stock(res);
                    int standing = stock != null ? Mathf.FloorToInt(stock.standing + 1e-4f) : 0;
                    got = Mathf.Min(want, standing);
                    if (got > 0)
                    {
                        stock.standing = Mathf.Max(0f, stock.standing - got);
                        // The trees go over as they are cut, not when the
                        // logs land: the scene reads this.
                        if (res == Res.Timber) timberTaken += got;
                    }
                    break;
                }
                case HaulPlace.Ship:
                {
                    got = ShipHere ? Mathf.Clamp(cargo.Take(res, want), 0, want) : 0;
                    if (got < want) ReturnToOrder(res, false, want - got);
                    break;
                }
                default:
                    got = want;          // a site's cleared log: already in hand
                    break;
            }
            if (got <= 0) return false;
            h.haulCount = got;
            h.haulPicked = true;
            return true;
        }

        /// The work at the pickup is done: pick up and turn for home.
        void FinishPickup(OutpostHand h)
        {
            h.workLeft = 0f;
            if (!PickUp(h)) { ClearHaul(h); return; }
            h.tripLeg = (int)TripLeg.ToDrop;
            h.legLeft = RouteMetres(HandAt(h), DropPoint(h));
        }

        /// A leg's end reached (by the walker or by the body).
        void ArriveLeg(OutpostHand h)
        {
            h.legLeft = 0f;
            switch (h.Leg)
            {
                case TripLeg.ToPickup:
                    h.tripLeg = (int)TripLeg.AtPickup;
                    if (h.workLeft <= Eps) FinishPickup(h);
                    break;
                case TripLeg.ToDrop:
                    h.tripLeg = (int)TripLeg.AtDrop;
                    DepositHaul(h);          // the drop-off event
                    break;
            }
        }

        /// A planned load that was never picked up goes nowhere: nothing to
        /// put down. Transfer orders get their units back.
        void CancelPlanned(OutpostHand h)
        {
            if (h.Hauling && !h.haulPicked)
            {
                if (h.haulTo == HaulPlace.Ship) ReturnToOrder(h.haulRes, true, h.haulCount);
                else if (h.haulFrom == HaulPlace.Ship) ReturnToOrder(h.haulRes, false, h.haulCount);
            }
            ClearHaul(h);
        }

        // --- the invisible walker -----------------------------------------------

        /// **Walk / work this hand's trip with `budget`** (game-days of his
        /// effort; `scale` is what one game-day of his time is worth, so
        /// walking -- which hunger does not slow -- costs `seconds x scale`).
        /// Returns false when his day stops here: a body is walking him, or
        /// he stands at a full store holding his load.
        bool AdvanceHaul(OutpostHand h, ref float budget, float scale = -1f)
        {
            if (scale < 0f) scale = WorkFactor(h);
            MigrateTrip(h);
            for (int guard = 0; guard < 16; guard++)
            {
                if (!h.Hauling) { if (h.tripLeg != 0) ClearHaul(h); return true; }
                var leg = h.Leg;
                if (leg == TripLeg.AtDrop)
                {
                    DepositHaul(h);
                    return !h.Hauling ? true : false;
                }
                if (h.driven) return false;          // the body walks it
                if (budget <= Eps) return true;
                if (leg == TripLeg.ToPickup || leg == TripLeg.ToDrop)
                {
                    if (scale <= 0f) return false;
                    float cost = SecondsToDays(h.legLeft / WalkMetresPerSecond) * scale;
                    if (budget + Eps >= cost)
                    {
                        budget = Mathf.Max(0f, budget - cost);
                        SetHandAt(h, leg == TripLeg.ToPickup ? PickPoint(h) : DropPoint(h));
                        ArriveLeg(h);
                        continue;
                    }
                    float metres = budget / scale * TimeOfDay.DayLength * WalkMetresPerSecond;
                    MoveAlong(h, metres, leg == TripLeg.ToPickup ? PickPoint(h) : DropPoint(h));
                    budget = 0f;
                    return true;
                }
                // AtPickup: stationary work (cutting, a stoop, the jab).
                float work = SecondsToDays(h.workLeft);
                if (budget + Eps >= work)
                {
                    budget = Mathf.Max(0f, budget - work);
                    FinishPickup(h);
                    continue;
                }
                h.workLeft -= budget * TimeOfDay.DayLength;
                budget = 0f;
                return true;
            }
            return true;
        }

        void MoveAlong(OutpostHand h, float metres, Vector3 goal)
        {
            if (metres <= 0f) return;
            Vector3 p = HandAt(h);
            float frac = h.legLeft > 1e-4f ? Mathf.Clamp01(metres / h.legLeft) : 1f;
            SetHandAt(h, Vector3.Lerp(p, goal, frac));
            h.legLeft = Mathf.Max(0f, h.legLeft - metres);
        }

        /// **Walk an errand with no load** (a builder to his site, a worker
        /// to his bench): true once he is there. A driven hand is there when
        /// his body is within `OnSiteMetres`; the body walks itself.
        bool WalkTo(OutpostHand h, Vector3 goal, ref float budget, float scale)
        {
            goal.y = 0f;
            if (!h.wHas && !h.driven) SetHandAt(h, HandAt(h));   // at the stores
            Vector3 p = HandAt(h);
            float d = Vector3.Distance(p, goal);
            if (h.driven) return d <= OnSiteMetres;
            if (d <= OnSiteMetres * 0.5f) return true;
            if (scale <= 0f || budget <= Eps) return false;
            float walk = d - OnSiteMetres * 0.25f;
            float cost = SecondsToDays(walk / WalkMetresPerSecond) * scale;
            if (budget + Eps >= cost)
            {
                budget = Mathf.Max(0f, budget - cost);
                SetHandAt(h, Vector3.Lerp(p, goal, walk / d));
                return true;
            }
            float metres = budget / scale * TimeOfDay.DayLength * WalkMetresPerSecond;
            SetHandAt(h, Vector3.Lerp(p, goal, Mathf.Clamp01(metres / d)));
            budget = 0f;
            return false;
        }

        // --- the body's side (watched camps) ------------------------------------

        /// Where the body is standing. Every frame, for every drawn hand.
        public void BodyAt(OutpostHand h, Vector3 p)
        {
            if (h == null) return;
            h.driven = true;
            SetHandAt(h, p);
        }

        /// The body reached the end of its leg (the pickup, or the drop-off).
        public void BodyArrived(OutpostHand h)
        {
            if (h == null || !h.Hauling) return;
            MigrateTrip(h);
            var leg = h.Leg;
            if (leg == TripLeg.ToPickup || leg == TripLeg.ToDrop) ArriveLeg(h);
            else if (leg == TripLeg.AtDrop) DepositHaul(h);
            if (!h.Hauling) Redispatch(h);
        }

        /// The body stood at the pickup working for `gameSeconds` of game
        /// time. The work is paid at the hand's own strength (`TripScale`).
        public void BodyWorked(OutpostHand h, float gameSeconds)
        {
            if (h == null || !h.Hauling || h.Leg != TripLeg.AtPickup) return;
            h.workLeft -= gameSeconds * TripScale(h);
            if (h.workLeft <= 0f) FinishPickup(h);
            if (!h.Hauling) Redispatch(h);
        }

        /// **His next trip now, not at the next step** (a watched body that
        /// just put its load down should not stand about for a quantum):
        /// his own pass of `Step` with a sliver of a day, which is enough to
        /// plan the next trip and no more.
        void Redispatch(OutpostHand h)
        {
            if (h == null || h.Hauling || hands == null || !hands.Contains(h)) return;
            const float Sliver = 1e-4f;
            float b = Sliver * WorkFactor(h);
            switch (h.order)
            {
                case OutpostOrder.Gather:
                    if (!string.IsNullOrEmpty(h.target))
                        GatherDay(h, Sliver, Focus != null && !HasHaulChore());
                    break;
                case OutpostOrder.Build:
                    if (sites != null) BuilderDay(h, ref b);
                    if (b > Eps && !h.Hauling) TransferDay(h, ref b);
                    break;
                case OutpostOrder.Work:
                {
                    if (!IsStation(h.target)) break;
                    var s = StationOfHand(h);
                    if (s != null) WorkerDay(h, s, stations.IndexOf(s), ref b);
                    break;
                }
                default:
                    HaulerDay(h, ref b);
                    break;
            }
        }

        /// The body is gone (the camp is no longer watched): the invisible
        /// walker carries on from where it stood, the leg re-measured.
        public void BodyReleased(OutpostHand h)
        {
            if (h == null) return;
            h.driven = false;
            if (!h.Hauling) return;
            var leg = h.Leg;
            if (leg == TripLeg.ToPickup) h.legLeft = RouteMetres(HandAt(h), PickPoint(h));
            else if (leg == TripLeg.ToDrop) h.legLeft = RouteMetres(HandAt(h), DropPoint(h));
        }

        /// **What one game-second of this hand's time is worth** at the
        /// stationary work of his trip -- the same scale `Step` spends his
        /// day at: a trip gatherer at `WorkFactorOn(target) x
        /// PriorityMultiplier(target)`, a hunter at his meat's, everyone
        /// else at plain `WorkFactor`.
        public float TripScale(OutpostHand h)
        {
            if (h == null) return 0f;
            if (h.order == OutpostOrder.Gather && h.target == Res.Game)
                return WorkFactorOn(h, Res.Food) * PriorityMultiplier(Res.Food);
            if (h.order == OutpostOrder.Gather && !string.IsNullOrEmpty(h.target))
                return WorkFactorOn(h, h.target) * PriorityMultiplier(h.target);
            return WorkFactor(h);
        }

        // --- measured deliveries (display only) ---------------------------------

        /// Days a delivery keeps counting in `DeliveredPerDay`.
        const float DeliveredWindowDays = 0.5f;
        [System.NonSerialized] List<string> deliveredRes;
        [System.NonSerialized] List<float> deliveredRate;
        /// Units of each resource dropped off since this ledger was loaded
        /// (probes: "nothing counted before its drop-off").
        [System.NonSerialized] public int deliveredEvents;

        void NoteDelivered(string res, int n)
        {
            if (string.IsNullOrEmpty(res) || n <= 0) return;
            deliveredEvents += n;
            if (deliveredRes == null) { deliveredRes = new List<string>(); deliveredRate = new List<float>(); }
            int i = deliveredRes.IndexOf(res);
            if (i < 0) { deliveredRes.Add(res); deliveredRate.Add(0f); i = deliveredRes.Count - 1; }
            deliveredRate[i] += n / DeliveredWindowDays;
        }

        void DecayDelivered(float days)
        {
            if (deliveredRate == null) return;
            float k = Mathf.Exp(-days / DeliveredWindowDays);
            for (int i = 0; i < deliveredRate.Count; i++) deliveredRate[i] *= k;
        }

        /// **Measured**: units of `res` actually dropped at the store per
        /// game-day, recently (a moving average). For display; books nothing.
        public float DeliveredPerDay(string res)
        {
            if (deliveredRes == null) return 0f;
            int i = deliveredRes.IndexOf(res);
            return i < 0 ? 0f : deliveredRate[i];
        }
    }
}
