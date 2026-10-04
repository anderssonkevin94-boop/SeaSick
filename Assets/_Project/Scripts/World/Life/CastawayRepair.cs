using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World.Life
{
    /// **Who a castaway on a beach really is, and what happens to them**
    /// (2026-10-04, Kevin's phone: "often when trying to dock I get this
    /// message" -- "IN THE WATER · No free berth" at his own home pier).
    ///
    /// His save had two of his own crew filed as castaways 36 m and 52 m from
    /// the home berth: Mabel and Mara went over the side near home on day
    /// 649/650 and `Swimmer.WashAshore` filed them as castaways waiting on a
    /// ship, on an island with his own camp on it. Mara was ALSO aboard: the
    /// steamer brings back the authored crew bodies every load and, unlike
    /// the ladder brig's `Shipyard.ManCrew`, never re-christened one whose
    /// name a castaway or a grave holds. That body went on living her life
    /// (overboard and rescued on day 680, ferried on day 708), so the
    /// castaway record was a stale copy of a person who is aboard.
    ///
    /// **Identity, not a name match.** The life registry keys a person by
    /// name (`LifeRecord`, `OutpostHand`, `CrewAgent` all share it, and
    /// `CrewNames` makes sure no two living people share one), so the
    /// person is their `LifeRecord`, and what a record says decides:
    ///   * A **stranger** (`Lives.IsStranger`: `CastawayField` made them up
    ///     from the pool) is never one of his people, whatever name a body
    ///     somewhere wears. Stays a rescuable castaway: the recruit reward.
    ///   * **One of his** = the castaway record was written by a swimmer off
    ///     his own ship (`CastawayRecord.exCrew`, from 2026-10-04), or, for
    ///     an older record, the person's own life log says they washed
    ///     ashore on that island (`WashedAshore` is logged only by a swimmer
    ///     off the ship, or the dev panel's stand-in for one).
    ///   * **A duplicate** = one of his whose person is living elsewhere: a
    ///     ledger row holds them, or a body aboard wears them AND their life
    ///     log carries on after the day they washed up (they went on being
    ///     that person, as Mara did). A body wearing the name with nothing
    ///     logged since is an authored body that should have been renamed;
    ///     the castaway is the person and keeps the record.
    ///   * One of his, not living elsewhere, on an island with a camp of his:
    ///     **walks up to that camp as a hand** (`Outpost.TakeInCastaway`)
    ///     instead of waiting on a ship that has to come and fetch them from
    ///     its own pier.
    /// Anything else stays as it is.
    ///
    /// The decisions are plain C# (`Decide`, `Plan`) so `CastawayFixSelfTest`
    /// runs them outside the editor; `RunOnLoad` is the scene half.
    public static class CastawayRepair
    {
        public enum Verdict { Keep, DropDuplicate, WalkToCamp }

        /// One of Kevin's own people, by the record's provenance or the
        /// person's own life log -- never by the name alone.
        public static bool IsOneOfOurs(CastawayRecord c, LifeRecord life, bool stranger)
        {
            if (c == null || stranger) return false;
            if (c.exCrew) return true;
            return WashedAshoreDay(life, c.island) >= 0;
        }

        /// The (last) day their life log says they washed up on `island`,
        /// or -1 when it never says so.
        public static int WashedAshoreDay(LifeRecord life, string island)
        {
            if (life == null || life.events == null) return -1;
            foreach (var e in life.events)
                if (e != null && e.kind == LifeEvents.WashedAshore && e.camp == (island ?? ""))
                    return e.day;
            return -1;
        }

        /// Did this person's life go on after `day` -- anything at all
        /// logged later than washing up?
        public static bool LivedOnAfter(LifeRecord life, int day)
        {
            if (life == null || life.events == null || day < 0) return false;
            foreach (var e in life.events)
                if (e != null && e.kind != LifeEvents.WashedAshore && e.day > day) return true;
            return false;
        }

        /// What happens to one castaway record. `inLedger`: a camp's ledger
        /// row holds this person. `bodyAboard`: a live body on the ship
        /// wears them. `campOnIsland`: their island has a camp of his.
        public static Verdict Decide(CastawayRecord c, LifeRecord life, bool stranger,
                                     bool inLedger, bool bodyAboard, bool campOnIsland)
        {
            if (c == null || string.IsNullOrEmpty(c.name)) return Verdict.Keep;
            if (!IsOneOfOurs(c, life, stranger)) return Verdict.Keep;
            if (inLedger) return Verdict.DropDuplicate;
            if (bodyAboard && LivedOnAfter(life, WashedAshoreDay(life, c.island))) return Verdict.DropDuplicate;
            return campOnIsland ? Verdict.WalkToCamp : Verdict.Keep;
        }

        /// What the world looks like to `Plan`, as plain lookups.
        public interface IWorld
        {
            LifeRecord Life(string name);
            bool IsStranger(string name);
            bool InLedger(string name);
            bool BodyAboard(string name);
            bool CampOnIsland(string island);
        }

        /// A verdict for every castaway record, in list order.
        public static List<KeyValuePair<CastawayRecord, Verdict>> Plan(
            IReadOnlyList<CastawayRecord> castaways, IWorld w)
        {
            var plan = new List<KeyValuePair<CastawayRecord, Verdict>>();
            if (castaways == null || w == null) return plan;
            foreach (var c in castaways)
            {
                if (c == null || string.IsNullOrEmpty(c.name)) continue;
                var v = Decide(c, w.Life(c.name), w.IsStranger(c.name), w.InLedger(c.name),
                               w.BodyAboard(c.name), w.CampOnIsland(c.island));
                plan.Add(new KeyValuePair<CastawayRecord, Verdict>(c, v));
            }
            return plan;
        }

        // ---------------------------------------------------- the scene half --

        /// **The load repair.** Called by `SaveGame.Restore` once the camps,
        /// the ship's hands and their names are back (step 5a2). Idempotent:
        /// a dropped or walked-home record is gone from `Lives.Castaways`, and
        /// a record left over beside the row it already became is a ledger
        /// duplicate the next run drops -- so it runs on every load and only
        /// ever does anything once. Returns a line for the log, or "".
        public static string RunOnLoad(Transform ship)
        {
            if (Lives.Castaways.Count == 0) return "";
            var world = new SceneWorld(ship);
            var plan = Plan(Lives.Castaways, world);
            var sb = new System.Text.StringBuilder();
            foreach (var kv in plan)
            {
                var c = kv.Key;
                switch (kv.Value)
                {
                    case Verdict.DropDuplicate:
                        Lives.RemoveCastaway(c.name);
                        sb.Append(c.name).Append(" (already living elsewhere: castaway copy dropped) ");
                        break;
                    case Verdict.WalkToCamp:
                    {
                        var camp = CampOn(c.island);
                        if (camp == null) break;
                        // A body aboard wearing the name with no life since
                        // she washed up is an authored body the steamer never
                        // re-christened, not her: it takes a free name first,
                        // so the camp's new row is the only one answering.
                        var impostor = world.BodyAboardNamed(c.name);
                        if (impostor != null)
                            Crew.CrewNames.Christen(impostor, (int)LifeStory.Fnv32(c.name), Crew.CrewNames.InUse());
                        if (camp.TakeInCastaway(c.name, new Vector3(c.x, 0f, c.z)))
                        {
                            Lives.RemoveCastaway(c.name);
                            sb.Append(c.name).Append(" (walked up to the camp on ").Append(c.island).Append(") ");
                        }
                        break;
                    }
                }
            }
            return sb.ToString();
        }

        /// The camp of his on the island with this GameObject name, or null.
        public static Outpost CampOn(string island)
        {
            if (string.IsNullOrEmpty(island)) return null;
            foreach (var isle in Island.All)
            {
                if (isle == null || isle.gameObject.name != island) continue;
                var o = Outpost.Of(isle);
                return o != null && (o.HasCamp || o.Building) ? o : null;
            }
            return null;
        }

        sealed class SceneWorld : IWorld
        {
            readonly HashSet<string> rows = new HashSet<string>();
            readonly Dictionary<string, Crew.CrewAgent> aboard = new Dictionary<string, Crew.CrewAgent>();

            public SceneWorld(Transform ship)
            {
                foreach (var o in Outpost.All)
                {
                    if (o == null || o.Ledger == null || o.Ledger.hands == null) continue;
                    foreach (var h in o.Ledger.hands)
                        if (h != null && !string.IsNullOrEmpty(h.name)) rows.Add(h.name);
                }
                if (ship != null)
                    foreach (var a in ship.GetComponentsInChildren<Crew.CrewAgent>(false))
                    {
                        if (a == null || CastawayField.IsFigure(a)) continue;
                        if (a.GetComponent<SeaSick.Combat.RaidWalker>() != null) continue;
                        if (!string.IsNullOrEmpty(a.DisplayName)) aboard[a.DisplayName] = a;
                    }
            }

            public LifeRecord Life(string name) =>
                Lives.Records.TryGetValue(name, out var r) ? r : null;
            public bool IsStranger(string name) => Lives.IsStranger(name);
            public bool InLedger(string name) => rows.Contains(name);
            public bool BodyAboard(string name) => aboard.ContainsKey(name);
            public Crew.CrewAgent BodyAboardNamed(string name) =>
                aboard.TryGetValue(name, out var a) ? a : null;
            public bool CampOnIsland(string island) => CampOn(island) != null;
        }
    }
}
