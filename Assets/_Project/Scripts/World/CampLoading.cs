using System.Collections.Generic;
using System.Text;
using SeaSick.Ship;
using SeaSick.Voyage;
using UnityEngine;

namespace SeaSick.World
{
    /// **Carrying a camp's stores down to the boat, one unit at a time.**
    ///
    /// Until 2026-09-20 there was no way at all to get a camp's pile into the
    /// hold. `OutpostLedger.Take` had zero callers; the only goods that ever
    /// reached the ship were a shore party's own logs overflowing a camp pile
    /// that was already full. So the loop's fourth step -- *return and load*
    /// (GDD §6) -- was a sentence in a design document and nothing else, and
    /// Phase 2 cannot price a ship rung in camp-made goods until the goods can
    /// physically be sailed home.
    ///
    /// **The player loads. A camp never ships home by itself.** That is the
    /// plan's defence against risk #2 -- "the sailing becomes transport" -- and
    /// it is why everything here hangs off a button and nothing off the tick.
    /// Nothing in this file runs unless somebody pressed something: there is no
    /// Update. Since 2026-09-24 a press places transfer ORDERS on the camp's
    /// ledger and the camp's hands carry them (see "what a load is doing").
    ///
    /// **The hold is the bottleneck, and the line is the player's greed.**
    /// `VoyageManager.AddLoot` clamps only to the PHYSICAL limit (`MaxHold`),
    /// because jettison, salvage and the shore party all reach it by different
    /// routes; the marked line is a decision, not a wall. So the caller is what
    /// respects it, and `RoomAboard` is the one place that knows the
    /// difference. Loading past the line is reachable only with deck cargo
    /// switched on, which is a deliberate act at the helm.
    ///
    /// **Units leaving the ledger equal units entering the hold, exactly.**
    /// One unit at a time, `Take` then `AddLoot` then `AddVisual`, and if
    /// `AddLoot` did not take it the unit goes straight back on the ground. A
    /// loader that produced or destroyed a single log would be the kind of
    /// fault that only ever shows up as a balance problem three features later.
    /// `CampLoadProbe` gates it per resource across a run that fills the hold
    /// mid-way.
    ///
    /// Still a MonoBehaviour for its callers' sake; since 2026-09-24 it has no
    /// coroutine and is never added to anything -- everything is static.
    public class CampLoading : MonoBehaviour
    {
        // --- the cadence ------------------------------------------------------

        /// Seconds between one unit and the next -- of the RETIRED coroutine
        /// (2026-09-24: hands carry armfuls now). Kept for `CampLoadProbe`.
        ///
        /// A shade quicker than the 0.18 s the retired `VoyageManager.UnloadAshore` used
        /// coming the other way, because a hold takes more than a beach keeps
        /// and nobody should have to watch sixty of them. **Scaled time, the
        /// same clock the unload uses** -- a dev tool that slows the world
        /// should slow the men carrying boxes too, or the two directions of the
        /// same errand stop agreeing.
        public const float Interval = 0.15f;

        /// **Most valuable first, and every one of these is a guess.**
        ///
        /// Nothing prices a resource yet -- `Ship/ShipPrices` is being written
        /// beside this -- so the order is the chain's own: the things a
        /// building MADE out of something else come before the something else,
        /// and the raw materials are ranked by how slow they are to gather
        /// (`Res.GatherRate`). When there is a real price table this array
        /// should be sorted by it and this comment should go.
        public static readonly string[] BestFirst =
        {
            // Brick sits right behind Boards for the same reason Boards sits
            // ahead of Timber: a brick is stone that has already had a day's
            // work put into it, so carrying it home beats carrying the rock.
            // Arrows sit behind Tools and ahead of everything else made:
            // they are the smallest, dearest thing the camp produces and the
            // only one the ship would sail home to spend somewhere else.
            Res.Tools, Res.Arrows, Res.Spice, Res.Boards, Res.Brick, Res.Ore,
            Res.Stone, Res.Meals, Res.Food, Res.Timber,
        };

        // --- per-kind caps, 2026-09-22 -----------------------------------------
        //
        // Kevin, 2026-09-22: a "stop at" per resource, so the player can load
        // "6 boards and no more" without babysitting the run. Keyed by the
        // same resource name strings `BestFirst` uses, because that is the
        // vocabulary the rest of this file already speaks. -1 is "no cap" and
        // is the default for anything nobody has capped -- an uncapped load
        // behaves exactly as it always did.
        static readonly Dictionary<string, int> stopAt = new Dictionary<string, int>();

        /// **What has actually gone aboard THIS VISIT**, per resource. Reset
        /// on `Cancel()` and whenever a caller discovers she is no longer
        /// `Alongside` -- see `ResetVisit`. Separate from `stopAt` so the caps
        /// themselves survive a visit; only the count against them does not.
        static readonly Dictionary<string, int> loadedThisVisit = new Dictionary<string, int>();

        /// The cap set for this resource, or -1 for none.
        public static int StopAt(string resource)
        {
            if (string.IsNullOrEmpty(resource)) return -1;
            return stopAt.TryGetValue(resource, out int cap) ? cap : -1;
        }

        /// Set (or clear, with a negative `cap`) the per-visit cap for this
        /// resource.
        public static void SetStopAt(string resource, int cap)
        {
            if (string.IsNullOrEmpty(resource)) return;
            if (cap < 0) stopAt.Remove(resource);
            else stopAt[resource] = cap;
        }

        /// Units of this resource moved aboard since the last visit reset.
        public static int LoadedThisVisit(string resource)
        {
            if (string.IsNullOrEmpty(resource)) return 0;
            return loadedThisVisit.TryGetValue(resource, out int n) ? n : 0;
        }

        /// **A new visit starts.** Clears what has been carried so far --
        /// never the caps themselves, which are a standing player choice, not
        /// a per-trip one. Called from `Cancel()` and from the two places
        /// that notice mid-carry she is no longer `Alongside`, which is this
        /// file's only signal that she has cast off (there is deliberately no
        /// `Update` here to poll for it any other way).
        static void ResetVisit() => loadedThisVisit.Clear();

        // --- what a load is doing right now -----------------------------------
        //
        // **Carried by hands since 2026-09-24.** Kevin: *"These things should
        // be physically carried from where they are to where they have been
        // designated."* `Begin` no longer runs a coroutine that teleported one
        // unit per 0.15 s; it places store -> ship TRANSFER ORDERS on the
        // camp's ledger (`OutpostLedger.OrderTransfer`), and the camp's free
        // hands walk them down to the gangway an armful at a time as timed
        // trips. Everything below reads those orders, so the ship sheet's
        // surface (Busy / Moved / Loading / Cancel) is unchanged.

        /// True while store -> ship orders stand (or armfuls are walking)
        /// at the camp being loaded, and she is still alongside it. The
        /// sheet's Load becomes Stop on this.
        public static bool Busy
        {
            get
            {
                var l = Camp != null ? Camp.Ledger : null;
                return l != null && l.AnyTransferPending(true) && Alongside(Camp);
            }
        }

        /// Units the hands have set down aboard since this load began.
        public static int Moved
        {
            get
            {
                var l = Camp != null ? Camp.Ledger : null;
                return l != null ? Mathf.Max(0, l.transferredAboard - movedBase) : 0;
            }
        }

        static int movedBase;

        /// What is being carried aboard at this moment (the first armful
        /// walking to her, else the first standing order), or null.
        public static string Loading
        {
            get
            {
                var l = Camp != null ? Camp.Ledger : null;
                if (l == null) return null;
                foreach (var h in l.hands)
                    if (h != null && h.Hauling && h.haulTo == HaulPlace.Ship) return h.haulRes;
                if (l.transfers != null)
                    foreach (var o in l.transfers)
                        if (o != null && o.toShip && o.left > 0) return o.resource;
                return null;
            }
        }

        /// The camp being loaded, or null.
        public static Outpost Camp { get; private set; }

        /// Is this the camp currently being loaded? The sheet asks, because a
        /// load is about ONE place and she can only be at one.
        public static bool LoadingFrom(Outpost camp) =>
            Busy && camp != null && Camp == camp;

        // --- is she actually here ----------------------------------------------

        static AnchorController anchor;

        /// The ship's anchor controller, found once. The `!= null` test is a
        /// real one: a cached reference from a previous play session is a
        /// destroyed object, and Unity's fake null is what catches it.
        static AnchorController Anchor =>
            anchor != null ? anchor : (anchor = Object.FindFirstObjectByType<AnchorController>());

        /// **Is she lying at THIS camp?**
        ///
        /// Asked before every single unit, not once at the start. Loading is a
        /// thing that takes seconds of real time, and "she weighed anchor
        /// half-way through" must stop the carry rather than teleport the rest
        /// of the pile aboard from a kilometre away.
        public static bool Alongside(Outpost camp)
        {
            if (camp == null) return false;
            var a = Anchor;
            if (a == null) return false;
            if (a.CurrentState != AnchorController.State.Anchored
                && a.CurrentState != AnchorController.State.Ashore) return false;
            var isle = a.CurrentIsland;
            return isle != null && Outpost.Of(isle) == camp;
        }

        // --- the arithmetic ----------------------------------------------------

        /// **Units the hold will take right now**: to the marked line, or to
        /// the physical limit when the player has said they will carry deck
        /// cargo.
        ///
        /// This is the whole of the line's meaning. `AddLoot` knows only
        /// `MaxHold`, so a caller that did not ask this would quietly fill her
        /// past her marks with nobody having chosen to.
        public static int RoomAboard(VoyageManager v)
        {
            // No `AtHome` refusal since 2026-10-04: home is a camp, and its
            // hands load and unload her like any camp's.
            if (v == null) return 0;
            int limit = v.TakeDeckCargo ? v.MaxHold : v.HoldCapacity;
            return Mathf.Max(0, limit - v.TotalHeld);
        }

        /// **Move up to `want` units of one kind from the ground to the hold,
        /// right now, and return how many actually went.**
        ///
        /// **The instant path, kept for probes and dev tools only**
        /// (`CampLoadProbe`). The game's load is carried by hands since
        /// 2026-09-24 (`Begin` -> transfer orders); this one teleports. Both go through the same three steps in the
        /// same order, so the instant path and the carried path cannot drift.
        ///
        /// The ledger is settled first (`CatchUp`), because the pile is the
        /// truth only once it has been brought up to now -- reading a stale
        /// count and then taking from it is how a camp ends up owing goods it
        /// never had.
        public static int LoadNow(Outpost camp, VoyageManager v, ShipHold hold,
            string resource, int want)
        {
            if (camp == null || v == null || string.IsNullOrEmpty(resource) || want <= 0)
                return 0;
            // A camp never ships home by itself, and that includes shipping to
            // a boat that is not there. This is the rule, not a safety check.
            // Her not being here any more is also this file's only signal
            // that a visit has ended, so it closes one out.
            if (!Alongside(camp)) { ResetVisit(); return 0; }

            camp.CatchUp();
            var l = camp.Ledger;
            if (l == null) return 0;

            int take = Mathf.Min(want, l.CountOf(resource));
            take = Mathf.Min(take, RoomAboard(v));
            // **The per-kind cap, 2026-09-22.** -1 (the default) never limits
            // anything; a real cap only lets through what this visit has not
            // already carried against it, so a kind sitting at its cap is
            // skipped rather than counted twice.
            int cap = StopAt(resource);
            if (cap >= 0) take = Mathf.Min(take, Mathf.Max(0, cap - LoadedThisVisit(resource)));
            if (take <= 0) return 0;

            int moved = 0;
            for (int i = 0; i < take; i++)
            {
                if (l.Take(resource, 1) != 1) break;      // the pile ran out under us

                // **Measured, not assumed.** `AddLoot` refuses silently -- at
                // home, or with the hull genuinely stuffed -- and a unit taken
                // off the ground for a refusal is a unit destroyed. So the
                // hold is read before and after, and anything that did not
                // land goes back where it came from.
                int before = v.TotalHeld;
                v.AddLoot(1, resource);
                if (v.TotalHeld != before + 1) { l.Add(resource, 1); break; }

                // Once per unit, or the stack on deck never grows: `ShipHold`'s
                // LateUpdate only ever REMOVES.
                if (hold != null) hold.AddVisual(resource);
                moved++;
                loadedThisVisit[resource] = LoadedThisVisit(resource) + 1;
            }
            return moved;
        }

        // --- the carried version ---------------------------------------------------

        /// **Order the camp's hands to carry the store down to the boat**,
        /// kind by kind in `plan` order (null: `BestFirst`), which is also
        /// the order armfuls are taken in.
        ///
        /// Each kind with anything in the STORE gets a store -> ship transfer
        /// order: "all" when the kind has no stop-at, else everything above
        /// the stop-at floor the ship sheet set (`SetStopAt` keeps that many
        /// ashore). The hold's line is still the limit: a hand only takes an
        /// armful the hold has room for (`RoomAboard`, net of armfuls already
        /// walking), and the order waits, saying "hold full", when there is
        /// none. Returns false when there is nothing to do -- she is not
        /// here, the store is empty, or the hold is at the player's limit.
        public static bool Begin(Outpost camp, VoyageManager v, ShipHold hold,
            string[] plan = null)
        {
            if (camp == null || v == null) return false;
            if (!Alongside(camp)) { ResetVisit(); return false; }

            camp.CatchUp();
            var l = camp.Ledger;
            if (l == null) return false;
            ShipCargoSide.BindTo(camp);
            if (RoomAboard(v) <= 0) return false;

            // A fresh load (not a second press while one runs) counts from 0.
            if (Camp != camp || !l.AnyTransferPending(true)) movedBase = l.transferredAboard;
            Camp = camp;

            bool any = false;
            foreach (var res in plan ?? BestFirst)
            {
                if (string.IsNullOrEmpty(res)) continue;
                int have = l.StoreCountOf(res);
                if (have <= 0) continue;
                int floor = StopAt(res);
                int count = floor < 0 ? OutpostLedger.TransferAll : Mathf.Max(0, have - floor);
                if (count <= 0) continue;
                any |= l.OrderTransfer(res, count, true);
            }
            return any && Busy;
        }

        /// One kind only -- what the per-resource `load` button in the open
        /// sheet presses.
        static readonly string[] oneKind = new string[1];

        public static bool BeginOne(Outpost camp, VoyageManager v, ShipHold hold,
            string resource)
        {
            if (string.IsNullOrEmpty(resource)) return false;
            oneKind[0] = resource;
            return Begin(camp, v, hold, oneKind);
        }

        /// **Stop.** Every transfer order at the camp being loaded is struck
        /// (`OutpostLedger.CancelTransfers`), so no hand picks up another
        /// armful. Armfuls already on a shoulder finish their walk and are set
        /// down where they were going -- a unit is never dropped on the path.
        public static void Cancel()
        {
            var l = Camp != null ? Camp.Ledger : null;
            if (l != null) l.CancelTransfers();
            ResetVisit();
        }

        // --- what the camp is holding, as a line of text -------------------------

        /// **"timber 10 / 10   ·   boards 6 / 10   ·   ore 3 / 10".**
        ///
        /// Built at most once per change, because `OnGUI` runs several times a
        /// frame -- Layout, Repaint, and one more for every mouse move -- and
        /// the headline this feeds is drawn on every one of them. The key is
        /// the camp's identity plus the whole-unit counts and the ceiling,
        /// which is exactly what the line prints: the sub-unit accrual moves
        /// constantly and changes nothing anybody can read.
        public static string Summary(OutpostLedger l)
        {
            if (l == null) return "";
            long key = CountsKey(l);
            if (key == summaryKey && summaryText != null) return summaryText;
            summaryKey = key;

            summarySb.Clear();
            foreach (var s in l.stores)
            {
                if (s == null || s.whole <= 0 || string.IsNullOrEmpty(s.resource)) continue;
                if (summarySb.Length > 0) summarySb.Append("   ·   ");
                // True count only: no ceiling since 2026-10-03 (infinite stacking).
                summarySb.Append(Lower(s.resource)).Append(' ').Append(s.whole);
            }
            if (summarySb.Length == 0) summarySb.Append("nothing gathered yet");
            summaryText = summarySb.ToString();
            return summaryText;
        }

        static readonly StringBuilder summarySb = new StringBuilder();
        static long summaryKey = long.MinValue;
        static string summaryText;

        /// A cheap hash of everything a reader of the stores can see: which
        /// camp, what it holds, and what it may hold. Anything that moves one
        /// of those rebuilds the line; nothing else does.
        public static long CountsKey(OutpostLedger l)
        {
            if (l == null) return 0L;
            long k = ((long)l.keyX * 73856093) ^ ((long)l.keyZ * 19349663)
                     ^ ((long)l.ceilingPer * 83492791);
            // The slot layout (2026-10-03) is what "may hold" means now.
            if (l.storeSlots != null)
                for (int f = 0; f < l.storeSlots.Length; f++) k = k * 31 + l.storeSlots[f];
            foreach (var s in l.stores)
            {
                if (s == null || string.IsNullOrEmpty(s.resource)) continue;
                k = k * 31 + s.resource.GetHashCode();
                k = k * 31 + s.whole;
            }
            return k;
        }

        /// A resource's name in lower case, remembered.
        ///
        /// `ToLowerInvariant` allocates a fresh string every call, and there
        /// are eight resource names in the whole game. Cached here rather than
        /// at the four call sites, so the sheet, the rows and the summary all
        /// print the same word.
        public static string Lower(string resource)
        {
            if (string.IsNullOrEmpty(resource)) return "";
            if (lowered.TryGetValue(resource, out string s)) return s;
            s = resource.ToLowerInvariant();
            lowered[resource] = s;
            return s;
        }

        static readonly Dictionary<string, string> lowered = new Dictionary<string, string>();
    }
}
