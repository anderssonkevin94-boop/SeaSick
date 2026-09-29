using System;
using System.Collections.Generic;

namespace SeaSick.Ship.Modular
{
    /// Low or Raised, per section. The player only ever picks this --
    /// docs/RAISED-SECTIONS.md sec 1: "the module id for each section
    /// follows from its own level and its neighbours' levels".
    public enum DeckLevel { Low, Raised }

    /// Per-section deck level (docs/RAISED-SECTIONS.md sec 1/2) <-> module
    /// id, both ways. Pure; no ModuleLibrary, no assembly -- ShipAssembler
    /// is what actually refuses a bad combination (JOIN_PROFILE_MISMATCH,
    /// RAISED_DECK_BAYS), same as it refuses anything else built by hand.
    /// Operates entirely in the wide-beam (W1x/W1xR) id space -- "raised
    /// needs wide beam" (sec 5) means Low here is already the W1x low id,
    /// never the standard-beam W1-r2 one; switching beam width at all is a
    /// separate choice made elsewhere (ShipyardDraft.IsWideBeam).
    public static class RaisedSections
    {
        public const string SternLow = ExpandedPresets.ExpandedStern;             // hull.stern.w1x.v1
        public const string SternRaisedConnected = RaisedPresets.RaisedStern;     // hull.stern.w1xr.v1
        public const string SternRaisedWallFwd = "hull.stern.w1xr.wf.v1";

        public const string MiddleLow = ExpandedPresets.ExpandedMiddle;           // hull.middle.w1x.v1
        public const string MiddleRaisedConnected = RaisedPresets.RaisedMiddle;   // hull.middle.w1xr.v1
        public const string MiddleRaisedWallAft = "hull.middle.w1xr.wa.v1";
        public const string MiddleRaisedWallFwd = "hull.middle.w1xr.wf.v1";
        public const string MiddleRaisedWallBoth = "hull.middle.w1xr.wb.v1";

        public const string BowLow = ExpandedPresets.ExpandedBow;                 // hull.bow.w1x.v1
        public const string BowRaisedConnected = RaisedPresets.RaisedBow;         // hull.bow.w1xr.v1
        public const string BowRaisedWallAft = "hull.bow.w1xr.wa.v1";

        /// A read configuration's levels, one per section, for the UI to show
        /// what is currently raised (`FromIds`'s result).
        public struct SectionLevels
        {
            public DeckLevel stern;
            public DeckLevel[] middles; // length == bay count, 0-3
            public DeckLevel bow;
        }

        /// Levels -> ids (docs/RAISED-SECTIONS.md sec 2, joint rule sec 1).
        /// `middles.Length` is the bay count (0-3, sec 5's "max middles 3" --
        /// the library's own `MaxMiddles` is what ShipAssembler actually
        /// enforces via TOO_MANY_MIDDLES; nothing here re-caps it). Every
        /// section's id follows from its own level and its immediate
        /// neighbour's (both-raised faces join connected/W1xR with no wall;
        /// otherwise the raised face closes with an end wall) -- it does NOT
        /// itself refuse a bad combination (e.g. 0 middles with both ends
        /// raised): that is RAISED_DECK_BAYS, enforced by ShipAssembler
        /// against whatever ids this returns, same as every other assembly
        /// rule (never duplicated here).
        public static (string sternId, string[] middleIds, string bowId) ToIds(DeckLevel stern, IList<DeckLevel> middles, DeckLevel bow)
        {
            middles ??= Array.Empty<DeckLevel>();
            int n = middles.Count;

            DeckLevel SternFwdNeighbour() => n > 0 ? middles[0] : bow;
            DeckLevel BowAftNeighbour() => n > 0 ? middles[n - 1] : stern;
            DeckLevel MiddleAftNeighbour(int i) => i == 0 ? stern : middles[i - 1];
            DeckLevel MiddleFwdNeighbour(int i) => i == n - 1 ? bow : middles[i + 1];

            string sternId = stern == DeckLevel.Low ? SternLow
                : (SternFwdNeighbour() == DeckLevel.Raised ? SternRaisedConnected : SternRaisedWallFwd);

            var middleIds = new string[n];
            for (int i = 0; i < n; i++)
            {
                if (middles[i] == DeckLevel.Low) { middleIds[i] = MiddleLow; continue; }
                bool aftRaised = MiddleAftNeighbour(i) == DeckLevel.Raised;
                bool fwdRaised = MiddleFwdNeighbour(i) == DeckLevel.Raised;
                if (aftRaised && fwdRaised) middleIds[i] = MiddleRaisedConnected;
                else if (aftRaised) middleIds[i] = MiddleRaisedWallFwd;   // aft face open, fwd face walled
                else if (fwdRaised) middleIds[i] = MiddleRaisedWallAft;   // fwd face open, aft face walled
                else middleIds[i] = MiddleRaisedWallBoth;
            }

            string bowId = bow == DeckLevel.Low ? BowLow
                : (BowAftNeighbour() == DeckLevel.Raised ? BowRaisedConnected : BowRaisedWallAft);

            return (sternId, middleIds, bowId);
        }

        /// A single section's level from its own id -- any `hull.*.w1xr*`
        /// id (connected or a wall variant) reads as Raised, everything else
        /// (including an id this build does not recognise) as Low, so a
        /// foreign/future/blank id degrades to "not raised" rather than
        /// throwing.
        public static DeckLevel LevelOf(string moduleId) =>
            CoasterFamily.Raised(moduleId) || (!string.IsNullOrEmpty(moduleId) && moduleId.Contains(".w1xr.")) ? DeckLevel.Raised : DeckLevel.Low;

        /// Inverse of `ToIds`: read a configuration's ids back into levels
        /// (docs/RAISED-SECTIONS.md task item 1's "inverse"). Round-trips
        /// with `ToIds` whenever the ids given were themselves produced by
        /// `ToIds` (or are assembler-valid by the same joint rule): the wall
        /// variant is discarded here (it is redundant with the neighbours'
        /// own levels) and `ToIds` derives the identical variant back from
        /// the levels alone.
        public static SectionLevels FromIds(string sternId, IList<string> middleIds, string bowId)
        {
            middleIds ??= Array.Empty<string>();
            var levels = new DeckLevel[middleIds.Count];
            for (int i = 0; i < middleIds.Count; i++) levels[i] = LevelOf(middleIds[i]);
            return new SectionLevels { stern = LevelOf(sternId), middles = levels, bow = LevelOf(bowId) };
        }

        /// Convenience: ids straight from a read configuration's own levels,
        /// i.e. `ToIds(FromIds(...))` -- what the UI calls after a tap
        /// toggles one section, to recompute every id at once (task item 4).
        public static (string sternId, string[] middleIds, string bowId) RecomputeIds(SectionLevels levels) =>
            ToIds(levels.stern, levels.middles, levels.bow);
    }
}
