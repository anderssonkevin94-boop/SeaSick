using System.Collections.Generic;
using System.Text;

namespace SeaSick.World.Economy
{
    /// **The one sentence for "this station cannot make that, because ..."
    /// (2026-10-04, Kevin: 'it's set to fine boards but I don't have a saw
    /// blade').** His alert read "Sawmill · out of boards · pick boards here
    /// to make more (Yara)" while the sawmill was on fine boards and he ALSO
    /// had no saw blade: the old line stopped at the first thing it found
    /// short and named only that. Every starved, paused or locked line the
    /// stations print now comes from here, so none of them can name one gap
    /// and hide the other.
    ///
    /// **The rule.** Name EVERY missing lock, tool and input, MOST BLOCKING
    /// FIRST, each with where to get it:
    ///
    ///  0. a LOCK (the fire or this building is not high enough yet);
    ///  1. a TOOL not in the pile (a recipe cannot run at all without it);
    ///  2. an input NOBODY on the island can supply right now (worked out
    ///     of the ground, its maker not built, only won at sea);
    ///  3. an input somebody CAN make or grow or hunt, just short;
    ///  4. an input standing in the ground to be gathered (never a stall on
    ///     its own -- a gatherer fetches it -- but named when the line is
    ///     being said anyway, so it is not left off a list that claims to
    ///     be whole).
    ///
    /// "Where to get it" is one of: made at another station ("make one at
    /// the forge"), made at this same bench by switching recipe ("switch to
    /// Boards here"), made at the same building's other spot ("run Iron on
    /// the smelter"), gatherable ("gather on the island"), grown ("grow it
    /// at the farm"), hunted ("hunt for it"), or "none left on this
    /// island" / "build a sawmill to saw some" when nothing can supply it.
    ///
    /// Example: "fine boards need a saw blade (make one at the forge) and
    /// boards (switch to Boards here)".
    ///
    /// **Pure C#, no scene**: the camp's numbers come in through `IView`
    /// (the ledger's implementation lives in `OutpostLedger.Spots.cs`; the
    /// self-test's is a fake), so `MissingWordsSelfTest` pins the wording.
    /// The line is a clause with its own subject, lower-case ("fine boards
    /// need ..."): sheets capitalise it, the alert chip prefixes the station
    /// and suffixes the worker. It wraps; it is never cut (2026-10-02 rule).
    public static class MissingWords
    {
        /// What the line needs to know about the camp.
        public interface IView
        {
            int FireLevel { get; }
            /// This building's own level.
            int StationLevel { get; }
            /// A tool is in the pile (a worn saw blade at 0.03 still counts).
            bool HasTool(string res);
            /// What this bench can have without anyone making it: its bay,
            /// what is already being carried to it, the store, the racks.
            int Have(string res);
            /// A gatherable still standing in the ground.
            bool GatherLeft(string res);
            /// A station of this plan stands.
            bool Built(string planId);
        }

        /// One thing the recipe lacks.
        public struct Gap
        {
            public int rank;
            public bool blocking;
            public string text;
        }

        const int RankLock = 0, RankTool = 1, RankNobody = 2, RankShort = 3, RankGather = 4;

        static readonly List<Gap> scratch = new List<Gap>(4);

        /// The whole line for `r` made at `stationId`, or null when nothing
        /// is missing. `blocking` is true when at least one gap actually
        /// stops the bench (a lock, a tool, an input nobody is already
        /// gathering); false when the only gaps are gatherables standing in
        /// the ground, which are named but are not a stall.
        public static string Line<V>(V view, string stationId, Recipe r, out bool blocking) where V : IView =>
            Line(view, stationId, r, out blocking, out _);

        /// As `Line`, and `worstRank`: the most blocking gap's rank (0 lock,
        /// 1 tool, 2 nobody can supply, 3 just short, 4 gatherable standing;
        /// `int.MaxValue` when nothing is missing). A bench with several
        /// spots uses it to say the WORST spot first (2026-10-04).
        public static string Line<V>(V view, string stationId, Recipe r, out bool blocking, out int worstRank) where V : IView
        {
            blocking = false;
            worstRank = int.MaxValue;
            if (r == null) return null;
            var gaps = scratch;
            gaps.Clear();

            if (view.FireLevel < r.campfireLevel)
                gaps.Add(new Gap { rank = RankLock, blocking = true,
                    text = $"the fire at {RecipeGraph.Roman(r.campfireLevel)} (raise it at the campfire)" });
            if (view.StationLevel < r.stationLevel)
                gaps.Add(new Gap { rank = RankLock, blocking = true,
                    text = $"the {PlanLabel(r.station)} at level {r.stationLevel} (upgrade it)" });

            if (r.tool != null && !view.HasTool(r.tool))
            {
                string where = Where(view, r.tool, stationId, r, out _, out _);
                gaps.Add(new Gap { rank = RankTool, blocking = true,
                    text = $"{Noun(r.tool, false)} ({where})" });
            }

            foreach (var line in r.takes)
            {
                if (line.n <= 0) continue;
                int have = view.Have(line.res);
                if (have >= line.n) continue;
                string where = Where(view, line.res, stationId, r, out int rank, out bool gatherStanding);
                gaps.Add(new Gap { rank = rank, blocking = !gatherStanding,
                    text = $"{Noun(line.res, have > 0)} ({where})" });
            }
            if (gaps.Count == 0) return null;

            // Stable insertion sort by rank: a handful of gaps, no allocation.
            for (int i = 1; i < gaps.Count; i++)
            {
                var g = gaps[i];
                int j = i - 1;
                while (j >= 0 && gaps[j].rank > g.rank) { gaps[j + 1] = gaps[j]; j--; }
                gaps[j + 1] = g;
            }
            worstRank = gaps[0].rank;
            var sb = new StringBuilder(96);
            string label = r.label;
            sb.Append(label).Append(EndsWithS(label) ? " need " : " needs ");
            for (int i = 0; i < gaps.Count; i++)
            {
                if (i > 0) sb.Append(i == gaps.Count - 1 ? " and " : ", ");
                sb.Append(gaps[i].text);
                if (gaps[i].blocking) blocking = true;
            }
            return sb.ToString();
        }

        /// Where to get `res`, in the player's words, and how stuck it is.
        static string Where<V>(V view, string res, string stationId, Recipe user,
            out int rank, out bool gatherStanding) where V : IView
        {
            gatherStanding = false;
            if (Res.IsGatherable(res))
            {
                if (view.GatherLeft(res))
                {
                    rank = RankGather; gatherStanding = true;
                    return "gather on the island";
                }
                rank = RankNobody;
                return "none left on this island";
            }

            var makers = Recipes.Making(res);
            if (makers.Count > 0)
            {
                // A maker whose station stands beats one that is merely
                // listed; this same bench beats any other.
                Recipe m = makers[0];
                foreach (var c in makers)
                {
                    if (c.station == stationId) { m = c; break; }
                    if (view.Built(c.station) && !view.Built(m.station)) m = c;
                }
                string one = IsMass(ResDefs.Label(res)) || EndsWithS(ResDefs.Label(res)) ? "some" : "one";
                if (m.station == stationId)
                {
                    rank = RankShort;
                    string mySpot = StationSpots.SpotOf(user), theirSpot = StationSpots.SpotOf(m);
                    return mySpot == theirSpot || theirSpot == null
                        ? $"switch to {Cap(m.label)} here"
                        : $"run {Cap(m.label)} on the {theirSpot.ToLowerInvariant()}";
                }
                string plan = PlanLabel(m.station);
                string verb = Verb(m);
                if (view.Built(m.station)) { rank = RankShort; return $"{verb} {one} at the {plan}"; }
                rank = RankNobody;
                return $"build a {plan} to {verb} {one}";
            }

            if (ResDefs.TryGet(res, out var def))
            {
                switch (def.source)
                {
                    case ResSource.Grown:
                        if (view.Built(BuildPlans.Farm.id)) { rank = RankShort; return "grow it at the farm"; }
                        rank = RankNobody;
                        return "build a farm plot to grow it";
                    case ResSource.Hunted:
                    case ResSource.Drop:
                        rank = RankShort;
                        return "hunt for it";
                    case ResSource.Salvaged:
                        rank = RankNobody;
                        return "won at sea, not on the island";
                }
            }
            rank = RankNobody;
            return "none to be had on the island";
        }

        /// "make" is the default; the ones with a better word.
        static string Verb(Recipe m)
        {
            switch (m.station)
            {
                case "Blacksmith": return StationSpots.SpotOf(m) == "Smelter" ? "smelt" : "make";
                case "Kitchen": return "cook";
                case "Mill": return "grind";
                case "Sawmill": return "saw";
                case "Quarry": return "cut";
                case "FishingHut": return "catch";
                case "Mine": return "dig";
                default: return "make";
            }
        }

        static string PlanLabel(string planId)
        {
            string label = BuildPlans.Named(planId).label;
            return string.IsNullOrEmpty(label) ? "station" : label;
        }

        /// "a saw blade", "boards", "iron", "more boards" (some are in).
        static string Noun(string res, bool more)
        {
            string label = ResDefs.Label(res);
            if (more) return "more " + label;
            if (IsMass(label) || EndsWithS(label)) return label;
            return (IsVowel(label[0]) ? "an " : "a ") + label;
        }

        static bool IsVowel(char c) => "aeiou".IndexOf(char.ToLowerInvariant(c)) >= 0;

        static bool EndsWithS(string s) => !string.IsNullOrEmpty(s) && s[s.Length - 1] == 's';

        static string Cap(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        /// Goods that read as a mass, not a count ("some iron", not "an iron").
        public static bool IsMass(string label)
        {
            switch (label)
            {
                case "timber": case "stone": case "ore": case "flour": case "meat": case "fish":
                case "wheat": case "iron": case "food": case "game": case "hide": case "spice":
                case "brick": case "bread": case "kraken ink":
                    return true;
            }
            return false;
        }

        /// A line is one of these when it is the `Line` shape ("... need(s)
        /// ..."): the building status chip and the tests read it as "short of
        /// supplies".
        public static bool IsSupplyLine(string s) =>
            !string.IsNullOrEmpty(s) && !IsWalledOff(s) && (s.Contains(" needs ") || s.Contains(" need "));

        // --- cut off from the store (2026-10-05) -----------------------------
        //
        // **Kevin's Day 853 save: the fishing hut and pier outside the
        // palisade, below the cliffs.** Runners were booked trips there over
        // and over and stood at the store hut with no way to go. Now no haul
        // is booked to or from a station the store cannot walk to
        // (`OutpostLedger.StationReachable`), and these say why, in the
        // same family as the camp's "Walled off · needs a gate" chip.

        /// A station worker whose walk to his own station has no way round a
        /// wall (`CampWorker`, his row's `bodyBlocked`).
        public const string CutOffByWall = "cut off by the wall · needs a gate";

        /// A station the store's runners cannot walk to because of a wall:
        /// its stall line ("Fishing hut · walled off from the store").
        public const string WalledOffFromStore = "walled off from the store";

        /// The same with no wall to blame (a cliff band, the sea).
        public const string NoWayFromStore = "no way there from the store";

        /// One of the station-side lines above.
        public static bool IsStationCutOff(string s) => s == WalledOffFromStore || s == NoWayFromStore;

        /// **A "walled off" line**, body or station: the body's own "walled
        /// off — no way round, needs a gate", `CutOffByWall`, and
        /// `WalledOffFromStore`. The camp's walled-off chip covers these, and
        /// none of them is a supply line (", needs a gate" read as "Needs
        /// supplies" on the building's status chip).
        public static bool IsWalledOff(string s) =>
            !string.IsNullOrEmpty(s)
            && (s.StartsWith("walled off", System.StringComparison.Ordinal)
                || s.StartsWith(CutOffByWall, System.StringComparison.Ordinal));
    }
}
