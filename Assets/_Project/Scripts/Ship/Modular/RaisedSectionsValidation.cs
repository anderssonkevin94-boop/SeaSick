using System;
using System.Collections.Generic;
using System.Text;

namespace SeaSick.Ship.Modular
{
    /// Headless validation of `RaisedSections` (docs/RAISED-SECTIONS.md task
    /// item 1) against the real assembler: every per-section level
    /// combination for 0-3 middles either assembles or is refused for the
    /// one documented reason (RAISED_DECK_BAYS), a few intentionally wrong
    /// hand-built pairings are refused by JOIN_PROFILE_MISMATCH, and
    /// `FromIds`/`ToIds` round-trip. No scene; run by
    /// ModularShipSelfTest.RunWith via tools/modular-selftest.sh.
    public static class RaisedSectionsValidation
    {
        public delegate void GateFn(string name, bool ok, string detail);

        static ShipConfiguration Bare(string sternId, IList<string> middleIds, string bowId)
        {
            var c = new ShipConfiguration { sternId = sternId, bowId = bowId,
                rotorId = ShipConfiguration.ReinforcedRotor, carrierId = ShipConfiguration.M1Carrier };
            if (middleIds != null) c.middleIds.AddRange(middleIds);
            return c;
        }

        static string Codes(AssemblyResult r)
        {
            if (r.ok) return "unexpectedly OK";
            var c = new List<string>();
            foreach (var x in r.rejections) c.Add(x.code);
            return string.Join(", ", c);
        }

        static string L(DeckLevel d) => d == DeckLevel.Raised ? "R" : "L";
        static string L(DeckLevel stern, IList<DeckLevel> mids, DeckLevel bow)
        {
            var sb = new StringBuilder(L(stern));
            foreach (var m in mids) sb.Append(L(m));
            sb.Append(L(bow));
            return sb.ToString();
        }

        public static void Body(string stdJson, IList<string> mods, IList<string> names, GateFn Gate)
        {
            var lib = ModuleLibrary.FromJson(stdJson, mods, names);
            if (!lib.Usable) { Gate("raised-sections-library-usable", false, string.Join(" | ", lib.errors)); return; }

            // =============================================================
            // Enumerate EVERY level combination for 0-3 middles, map to ids
            // via RaisedSections.ToIds, and assemble for real. Expect OK
            // unless it is the one documented RAISED_DECK_BAYS case (0
            // middles, both ends raised) -- everything else, any mix of
            // low/raised per section, must assemble.
            // =============================================================
            int total = 0, okCount = 0, rejectedBaysCount = 0;
            var unexpected = new List<string>();
            foreach (var stern in new[] { DeckLevel.Low, DeckLevel.Raised })
            foreach (var bow in new[] { DeckLevel.Low, DeckLevel.Raised })
            for (int n = 0; n <= 3; n++)
            {
                int combos = 1 << n;
                for (int mask = 0; mask < combos; mask++)
                {
                    var mids = new DeckLevel[n];
                    for (int i = 0; i < n; i++) mids[i] = ((mask >> i) & 1) != 0 ? DeckLevel.Raised : DeckLevel.Low;

                    var (sternId, middleIds, bowId) = RaisedSections.ToIds(stern, mids, bow);
                    var cfg = Bare(sternId, middleIds, bowId);
                    var r = ShipAssembler.Assemble(cfg, lib);
                    total++;

                    bool expectBaysReject = n == 0 && stern == DeckLevel.Raised && bow == DeckLevel.Raised;
                    if (expectBaysReject)
                    {
                        if (!r.ok && r.HasCode("RAISED_DECK_BAYS")) rejectedBaysCount++;
                        else unexpected.Add($"{L(stern, mids, bow)}: expected RAISED_DECK_BAYS, got {(r.ok ? "OK" : Codes(r))}");
                    }
                    else
                    {
                        if (r.ok) okCount++;
                        else unexpected.Add($"{L(stern, mids, bow)}: expected OK, got {Codes(r)}");
                    }
                }
            }
            Gate("raised-sections-every-combination-0-3-middles-assembles", unexpected.Count == 0,
                unexpected.Count == 0
                    ? $"{total} combinations: {okCount} assembled, {rejectedBaysCount} refused RAISED_DECK_BAYS (0 middles, both ends raised)"
                    : string.Join(" | ", unexpected));

            // =============================================================
            // Mixed wrong pairings: hand-build ids RaisedSections would
            // never produce together, and confirm ShipAssembler refuses
            // them by JOIN_PROFILE_MISMATCH (the wall variant's walled face
            // meeting a raised-connected neighbour, or vice versa).
            // =============================================================
            void ExpectMismatch(string name, string sternId, IList<string> middleIds, string bowId)
            {
                var r = ShipAssembler.Assemble(Bare(sternId, middleIds, bowId), lib);
                Gate(name, !r.ok && r.HasCode("JOIN_PROFILE_MISMATCH"), Codes(r));
            }
            ExpectMismatch("raised-sections-wrong-pairing-wf-stern-meets-raised-middle-rejected",
                RaisedSections.SternRaisedWallFwd, new[] { RaisedSections.MiddleRaisedConnected }, RaisedSections.BowLow);
            ExpectMismatch("raised-sections-wrong-pairing-connected-stern-meets-walled-middle-rejected",
                RaisedSections.SternRaisedConnected, new[] { RaisedSections.MiddleRaisedWallBoth }, RaisedSections.BowLow);
            ExpectMismatch("raised-sections-wrong-pairing-connected-bow-meets-low-middle-rejected",
                RaisedSections.SternLow, new[] { RaisedSections.MiddleLow }, RaisedSections.BowRaisedConnected);

            // =============================================================
            // FromIds / ToIds round-trip: for a sample of combinations,
            // levels -> ids -> levels -> ids must reproduce the same ids.
            // =============================================================
            int roundTripChecked = 0;
            var roundTripBad = new List<string>();
            foreach (var stern in new[] { DeckLevel.Low, DeckLevel.Raised })
            foreach (var bow in new[] { DeckLevel.Low, DeckLevel.Raised })
            for (int n = 0; n <= 3; n++)
            {
                if (n == 0 && stern == DeckLevel.Raised && bow == DeckLevel.Raised) continue; // never-built combo
                int combos = 1 << n;
                for (int mask = 0; mask < combos; mask++)
                {
                    var mids = new DeckLevel[n];
                    for (int i = 0; i < n; i++) mids[i] = ((mask >> i) & 1) != 0 ? DeckLevel.Raised : DeckLevel.Low;
                    var first = RaisedSections.ToIds(stern, mids, bow);
                    var levels = RaisedSections.FromIds(first.sternId, first.middleIds, first.bowId);
                    var second = RaisedSections.ToIds(levels.stern, levels.middles, levels.bow);
                    roundTripChecked++;
                    if (first.sternId != second.sternId || first.bowId != second.bowId || first.middleIds.Length != second.middleIds.Length)
                    { roundTripBad.Add(L(stern, mids, bow)); continue; }
                    for (int i = 0; i < first.middleIds.Length; i++)
                        if (first.middleIds[i] != second.middleIds[i]) { roundTripBad.Add(L(stern, mids, bow) + $"[{i}]"); break; }
                }
            }
            Gate("raised-sections-fromids-toids-round-trip", roundTripBad.Count == 0,
                roundTripBad.Count == 0 ? $"{roundTripChecked} combinations round-trip" : string.Join(" | ", roundTripBad));
        }
    }
}
