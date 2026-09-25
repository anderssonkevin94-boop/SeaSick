using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

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

        static string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);

        public static void Body(string stdJson, IList<string> mods, IList<string> names, string hullFormJson, GateFn Gate)
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

            // =============================================================
            // docs/RAISED-SECTIONS.md sec 6: a MIXED ship (raised stern,
            // low middle, low bow) must raise ONLY the stern's stations --
            // this is what tells apart the per-section RaiseDeck from the
            // old all-or-nothing one (every existing raised-deck gate used
            // an all-raised ship, which the per-section version also
            // handles, but would not by itself prove the range logic works).
            // =============================================================
            var reference = ShipyardSelfTest.ReferenceFrom(hullFormJson);
            Gate("raised-sections-reference-hull-loads", reference != null, reference != null ? $"lwl {F(reference.lwl)} m" : "no hullform.json");
            if (reference == null) return;

            var refPlan = ShipyardPlanner.PlanFor(ShipConfiguration.Long(), lib, reference, null, out var refAsm);
            if (refPlan == null || !refAsm.ok) { Gate("raised-sections-reference-plans", false, refAsm.Summary()); return; }

            var (mixSternId, mixMiddleIds, mixBowId) = RaisedSections.ToIds(DeckLevel.Raised, new[] { DeckLevel.Low }, DeckLevel.Low);
            var mixCfg = Bare(mixSternId, mixMiddleIds, mixBowId);
            var mixPlan = ShipyardPlanner.PlanFor(mixCfg, lib, reference, refPlan, out var mixAsm);
            Gate("raised-sections-mixed-raised-stern-plans", mixPlan != null && mixAsm.ok, mixAsm.Summary());
            if (mixPlan == null) return;

            var stations = mixPlan.data.stations;
            float lowDeckY = mixPlan.data.stations[stations.Length - 1].deckY; // bow tip, always low in this mix
            float sternDeckY = mixPlan.data.stations[0].deckY; // stern-most station, raised in this mix
            int midIdx = stations.Length / 2; // roughly amidships -- inside the low middle bay
            float midDeckY = mixPlan.data.stations[midIdx].deckY;

            Gate("raised-sections-mixed-stern-station-raised", sternDeckY > lowDeckY + 1.0f,
                $"stern station[0] deckY={F(sternDeckY)} m vs bow-tip deckY={F(lowDeckY)} m (expect stern ~1.22 m higher)");
            // Tolerance is the hull's own sheer (deckY already varies gently
            // along an UNraised hull, e.g. higher at the bow tip than
            // amidships) -- well under the ~1.22 m a raised section adds, so
            // this still tells "not raised" apart from "raised" cleanly.
            Gate("raised-sections-mixed-middle-and-bow-stay-low", Mathf.Abs(midDeckY - lowDeckY) < 0.5f,
                $"amidships (low middle) station[{midIdx}] deckY={F(midDeckY)} m vs bow-tip deckY={F(lowDeckY)} m (expect close -- sheer only, neither raised)");

            // Sanity floor: the all-raised Long (every station raised) has a
            // HIGHER minimum deckY than the mixed ship (only the stern
            // raised) -- proves the range restriction actually did
            // something, not just that it never fires.
            var longPlan = ShipyardPlanner.PlanFor(RaisedPresets.RaisedLong(), lib, reference, refPlan, out var longAsm);
            if (longPlan != null && longAsm.ok)
            {
                float longMinDeckY = float.PositiveInfinity;
                foreach (var s in longPlan.data.stations) if (s != null) longMinDeckY = Mathf.Min(longMinDeckY, s.deckY);
                float mixMinDeckY = float.PositiveInfinity;
                foreach (var s in mixPlan.data.stations) if (s != null) mixMinDeckY = Mathf.Min(mixMinDeckY, s.deckY);
                Gate("raised-sections-mixed-ship-lower-min-deck-than-all-raised", mixMinDeckY < longMinDeckY - 0.5f,
                    $"mixed min deckY={F(mixMinDeckY)} m vs all-raised-long min deckY={F(longMinDeckY)} m");
            }

            // GM report across a small set of mixed combinations (task's
            // "GM table, min GM combo"): the hard gate GM > 0.3 m, same as
            // the all-raised report already asserts, over a few mixes with
            // only PART of the ship raised (expected to sit BETWEEN the
            // all-low and all-raised GM, never below the all-raised one,
            // since less upper mass stands high the fewer sections raise).
            void ReportMix(string label, DeckLevel stern, DeckLevel[] mids, DeckLevel bow)
            {
                var (sId, mIds, bId) = RaisedSections.ToIds(stern, mids, bow);
                var plan = ShipyardPlanner.PlanFor(Bare(sId, mIds, bId), lib, reference, refPlan, out var a);
                if (plan == null || !a.ok) { Gate($"raised-sections-gm-{label}", false, a.Summary()); return; }
                Gate($"raised-sections-gm-{label}-above-0.3m", plan.data.gm > 0.3f,
                    $"mass={F(plan.lightshipKg / 1000f)}t KG={F(plan.data.kg)}m GM={F(plan.data.gm)}m");
            }
            ReportMix("stern-only", DeckLevel.Raised, new[] { DeckLevel.Low }, DeckLevel.Low);
            ReportMix("bow-only", DeckLevel.Low, new[] { DeckLevel.Low }, DeckLevel.Raised);
            ReportMix("stern-and-bow-no-middle-raise", DeckLevel.Raised, Array.Empty<DeckLevel>(), DeckLevel.Low);
            ReportMix("all-three-two-middles", DeckLevel.Raised, new[] { DeckLevel.Raised, DeckLevel.Raised }, DeckLevel.Raised);

            // Chimney X (docs/RAISED-SECTIONS.md sec 5): the -0.84 u
            // midpoint offset is an ALL-raised correction; a mixed ship
            // (raised stern, low rest) uses the plain midpoint even though
            // its stern alone is raised.
            var mixCfgWithChimney = Bare(mixSternId, mixMiddleIds, mixBowId);
            mixCfgWithChimney.fittings.Add(new FittingChoice { socketId = ShipConfiguration.ChimneySocket, moduleId = ShipConfiguration.V3Chimney });
            var mixChimneyAsm = ShipAssembler.Assemble(mixCfgWithChimney, lib);
            var mixChimney = mixChimneyAsm.Find("fitting:" + ShipConfiguration.ChimneySocket);

            var allRaisedCfg = RaisedPresets.RaisedLong();
            var allRaisedAsm = ShipAssembler.Assemble(allRaisedCfg, lib);
            var allRaisedChimney = allRaisedAsm.Find("fitting:" + ShipConfiguration.ChimneySocket);

            Gate("raised-sections-chimney-offset-only-when-all-raised",
                mixChimneyAsm.ok && allRaisedAsm.ok && mixChimney != null && allRaisedChimney != null
                    && Mathf.Abs(mixChimney.positionU.x - allRaisedChimney.positionU.x - 0.84f) < 1e-3f,
                mixChimney != null && allRaisedChimney != null
                    ? $"mixed (stern-only raised) chimney x={F(mixChimney.positionU.x)} u; all-raised chimney x={F(allRaisedChimney.positionU.x)} u (expect +0.84 u apart)"
                    : $"mixed ok={mixChimneyAsm.ok}, all-raised ok={allRaisedAsm.ok}");
        }
    }
}
