using UnityEngine;

namespace SeaSick.World
{
    /// **The fisher fishes at the water (Kevin, 2026-09-30, iPhone playtest).**
    ///
    /// *"I want the fisherman to walk to the closest water when he fishes.
    /// After catching a fish he places it in the product part of his
    /// building. He only walks it to the storage himself if the product box
    /// is full."* And: *"For the fish in particular he will carry all the
    /// fish at once since they aren't that heavy compared to logs."*
    ///
    /// The fishing hut stays a station (order, output box = `rack`,
    /// `OutputCap` = its capacity, counted in camp totals), but its bench is
    /// never used. Its worker's day (`CatchDay`) is trips:
    /// <list type="bullet">
    /// <item>**Catch trip** `Shore -> Station`: walk from wherever he is to
    ///   the hut's shore spot (`ShoreOf`), fish there for the recipe's own
    ///   timer (`CatchSeconds` -- the rate the bench had), carry the catch to
    ///   the hut and put it in the box ON ARRIVAL (`LandCatch`). Only started
    ///   while the box has room for it net of catches already on the way.</item>
    /// <item>**Box full** (or no order, or no way to the water): the box goes
    ///   to the store by the ordinary rack chore (`RackChore`), whose armful
    ///   for fish (`Res.FishArmful`) is at least the box -- one trip. Idle
    ///   hands empty it through the same chore (`FindHaulerChore`).</item>
    /// </list>
    /// The shore spot is the scene's to find (`Outpost.SaveShoreSpots`, the
    /// ledger has no terrain); it is saved on the station row so an unwatched
    /// camp keeps fishing where it last did, and the walker walks there like
    /// any other pickup -- watched == unwatched.
    public partial class OutpostLedger
    {
        /// Is this station worked at the water's edge rather than at a bench?
        public static bool FishesAtShore(StationStock s) =>
            s != null && s.planId == BuildPlans.FishingHut.id;

        /// The `raised` row station `index` stands on (its plan's row of the
        /// same ordinal), or null.
        public BuiltBuilding StationRow(int index)
        {
            if (stations == null || index < 0 || index >= stations.Count || raised == null) return null;
            var s = stations[index];
            if (s == null) return null;
            int k = 0;
            foreach (var r in raised)
            {
                if (r == null || r.planId != s.planId) continue;
                if (k++ == s.ordinal) return r;
            }
            return null;
        }

        /// **Where station `index`'s worker stands to fish, and what he
        /// faces.** Found: the saved spot. Not looked for yet (an old save
        /// before its first catch-up, a probe's ledger): beside the hut
        /// itself, so the trip still works. No reachable water: false.
        public bool ShoreOf(int index, out Vector3 stand, out Vector3 water)
        {
            stand = water = default;
            var s = StationAt(index);
            if (s == null) return false;
            if (s.shore == 1)
            {
                stand = new Vector3(s.shoreX, 0f, s.shoreZ);
                water = new Vector3(s.waterX, 0f, s.waterZ);
                return true;
            }
            if (s.shore == 2) return false;
            if (!StationPlace(index, out stand)) return false;
            water = stand + Vector3.forward;
            return true;
        }

        /// **The scene's answer for station `index`** (`Outpost.SaveShoreSpots`):
        /// found, with the spot and the water it faces; or not, with why.
        /// `hutAt` is where the hut stood when it was asked, `rev` the wall
        /// layer it was checked against.
        public void SetShore(int index, bool found, Vector3 stand, Vector3 water, string why,
            Vector3 hutAt, int rev)
        {
            var s = StationAt(index);
            if (s == null) return;
            s.shore = found ? 1 : 2;
            s.shoreX = stand.x; s.shoreZ = stand.z;
            s.waterX = water.x; s.waterZ = water.z;
            s.shoreWhy = found ? "" : (why ?? "");
            s.shoreForX = hutAt.x; s.shoreForZ = hutAt.z;
            s.shoreRev = rev;
        }

        /// The catch recipe this station is working, or null.
        static Economy.Recipe CatchRecipe(StationStock s)
        {
            var r = s?.OrderRecipe;
            return r != null && Economy.RecipeGraph.IsCatch(r) ? r : null;
        }

        /// Units a day this station catches at: the recipe's rate at this
        /// hut's level and the camp's priority -- exactly what `WorkerDay`
        /// paid the bench at.
        float CatchRate(StationStock s, Economy.Recipe r) =>
            r == null ? 0f
            : r.ratePerDay * Economy.Techs.RateMul(s.planId, LevelOf(s.planId, s.ordinal)) * PriorityMultiplier(r.makes);

        /// **Game seconds of fishing for one catch** at station `index`:
        /// one bench job's time (`yield / rate` days), unchanged from the
        /// bench. Seconds of his effort -- `AdvanceHaul` / `BodyWorked` pay
        /// it at his `WorkFactor`, as the bench was paid.
        float CatchSeconds(int index)
        {
            var s = StationAt(index);
            var r = CatchRecipe(s);
            float rate = CatchRate(s, r);
            if (rate <= 0f) return HandleSeconds;
            return TimeOfDay.WorkDaySeconds * Mathf.Max(1, r.yield) / rate;
        }

        /// **One catch trip, if one may start**: an order for a catch, room
        /// in the box for it net of catches already walking in, a way to the
        /// water. The fish is planned, not caught: it exists at the pickup.
        bool StartCatchTrip(OutpostHand h, StationStock s, int si)
        {
            var r = CatchRecipe(s);
            if (r == null || CatchRate(s, r) <= 0f) return false;
            int yield = Mathf.Max(1, r.yield);
            if (s.RackRoom - CatchesInFlight(si) < yield) return false;
            if (!ShoreOf(si, out _, out _)) return false;
            StartTimedTrip(h, r.makes, yield, HaulPlace.Shore, si, HaulPlace.Station, si);
            return true;
        }

        /// Fish on the way to station `si`'s box: planned or on a line or in
        /// arms (not a meal walked off the box -- that takes, never adds).
        int CatchesInFlight(int si)
        {
            int n = 0;
            if (hands == null) return 0;
            foreach (var o in hands)
                if (o != null && o.Hauling && o.haulFrom == HaulPlace.Shore && o.haulToStation == si) n += o.haulCount;
            return n;
        }

        /// **The fisher's day.** Whatever is in his arms first; then catch
        /// after catch while the box has room; when it cannot go on (box
        /// full, no order, no way to the water) he carries the box to the
        /// store -- all of it, `Res.FishArmful` -- and comes back to fish.
        void CatchDay(OutpostHand h, StationStock s, int si, ref float budget)
        {
            for (int guard = 0; guard < 64 && budget > Eps; guard++)
            {
                if (h.Hauling) { if (!AdvanceHaul(h, ref budget)) break; continue; }
                // An old save's bench: a finished catch goes in the box, a
                // half-done one is let go (a catch takes nothing, so no
                // input is lost).
                // (Per spot since 2026-09-30.)
                s.EnsureSpotRows();
                foreach (var sp in s.spots)
                {
                    if (sp == null) continue;
                    if (sp.benchState == BenchState.Finished) UnloadSpot(s, sp);
                    else if (sp.benchState != BenchState.Empty) sp.EmptyBench();
                }
                if (StartCatchTrip(h, s, si)) continue;
                // With runners on the island the box is theirs to carry
                // (2026-10-02), unless it has waited too long for one.
                if (WorkerFetches(s) && RackChore(si, out var home, h, true)) { BeginChore(h, home); continue; }
                break;
            }
        }

        /// **The catch is put in the box, on arrival** -- counted now, not
        /// when it was caught. Booked like a finished bench job was
        /// (`FinishJob`): what the camp made while away, and the order's
        /// count. Nothing else fills a fishing box, so there is room; if an
        /// old save's bench beat him to it, it goes in over the top rather
        /// than hang in his arms (the box is then full and goes home next).
        void LandCatch(OutpostHand h, StationStock s)
        {
            int n = h.haulCount;
            string res = h.haulRes;
            ClearHaul(h);
            if (n <= 0 || string.IsNullOrEmpty(res)) return;
            s.Rack(res, true).whole += n;
            away.Add(res, n);
            var r = s.OrderRecipe;
            if (r == null || r.makes != res)
            {
                r = null;
                foreach (var m in Economy.Recipes.Making(res))
                    if (m != null && m.station == s.planId) { r = m; break; }
            }
            if (r == null) return;
            // A count order runs down on the catch's own spot (2026-09-30).
            CountDown(s.SpotAt(StationSpots.SpotIndexOf(r)), r, n);
            s.SyncLegacy();
        }

        /// Why a fisher with an order is not fishing, or null.
        string CatchStallCause(StationStock s)
        {
            if (s.shore == 2)
                return string.IsNullOrEmpty(s.shoreWhy) ? "no way to the water" : s.shoreWhy;
            var r = CatchRecipe(s);
            if (r != null && s.RackFull && StoreRoomNet(r.makes) <= 0)
                return $"box and store are full of {Friendly(r.makes)}";
            return null;
        }

        // --- food off the racks (2026-09-30) ------------------------------------
        //
        // Fish in the hut's box is camp food: it counts in `FoodFill` and
        // supper is served from it as from the store (`ServeSupper`, since
        // 2026-10-02; a hungry hand used to walk there) -- else a camp starves
        // with a full box. Racks only, never bays (a kitchen's queued input
        // is not dinner) and never a bench.

        /// Whole units of `res` on every station's output rack.
        int RackCountOf(string res)
        {
            int n = 0;
            if (stations != null) foreach (var s in stations) if (s != null) n += s.RackCount(res);
            return n;
        }

        /// Rack units of `res` nobody is already walking to fetch.
        int RackFree(string res)
        {
            int n = 0;
            if (stations == null) return 0;
            for (int i = 0; i < stations.Count; i++)
            {
                var s = stations[i];
                if (s != null) n += RowFree(i, s.Rack(res), false);
            }
            return n;
        }
    }
}
