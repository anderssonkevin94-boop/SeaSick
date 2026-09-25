using System;
using System.Collections.Generic;
using System.Globalization;
using SeaSick.Steamer;
using UnityEngine;

namespace SeaSick.Ship.Modular
{
    /// Headless validation of the raised-deck family (W1xR,
    /// art-staging/modular-raised-middle-v1) against docs/RAISED-DECK.md and
    /// the modular-ship module contract. No scene, no GameObjects; run by
    /// tools/modular-selftest.sh via ModularShipSelfTest.RunWith.
    public static class RaisedDeckValidation
    {
        public delegate void GateFn(string name, bool ok, string detail);

        static string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);
        static string S(Vector3 v) => $"({v.x:0.###}, {v.y:0.###}, {v.z:0.###})";

        static string First(AssemblyResult r, string code)
        {
            foreach (var x in r.rejections) if (x.code == code) return x.message;
            return null;
        }

        static string Codes(AssemblyResult r)
        {
            if (r.ok) return "unexpectedly OK";
            var c = new List<string>();
            foreach (var x in r.rejections) c.Add(x.code);
            return string.Join(", ", c);
        }

        public static void Body(string stdJson, IList<string> mods, IList<string> names, string hullFormJson,
            Func<string, string> readResourceText, GateFn Gate)
        {
            var lib = ModuleLibrary.FromJson(stdJson, mods, names);
            if (readResourceText != null) lib.LoadHydrostatics(readResourceText);
            Gate("raised-library-loads", lib.Ok, $"ok={lib.Ok} errors=[{string.Join(" | ", lib.errors)}]");
            if (!lib.Usable) return;

            // =============================================================
            // sec 2/3: the W1xR join profile, upperDeckZU carried by the
            // schema, and the family never mixing with W1x/W1-r2.
            // =============================================================
            var w1xr = lib.FindProfile("W1xR");
            Gate("w1xr-profile-registered", w1xr != null && Near(w1xr.halfBeamU, 6.04f) && Near(w1xr.deckZU, 1.76f)
                && Near(w1xr.keelZU, -1.92f) && Near(w1xr.upperDeckZU, 4.2f),
                w1xr != null ? $"halfBeam={F(w1xr.halfBeamU)} deck={F(w1xr.deckZU)} keel={F(w1xr.keelZU)} upperDeck={F(w1xr.upperDeckZU)}" : "missing");

            lib.TryGet(RaisedPresets.RaisedStern, out var rStern);
            lib.TryGet(RaisedPresets.RaisedMiddle, out var rMiddle);
            lib.TryGet(RaisedPresets.RaisedBow, out var rBow);
            Gate("w1xr-modules-load", rStern != null && rMiddle != null && rBow != null
                && rStern.family == "W1xR" && rMiddle.family == "W1xR" && rBow.family == "W1xR",
                $"stern={(rStern != null ? rStern.family : "missing")} middle={(rMiddle != null ? rMiddle.family : "missing")} bow={(rBow != null ? rBow.family : "missing")}");

            void RejectMix(string name, ShipConfiguration cfg)
            {
                var r = ShipAssembler.Assemble(cfg, lib);
                string msg = First(r, "JOIN_PROFILE_MISMATCH");
                Gate(name, !r.ok && msg != null, msg ?? Codes(r));
            }
            var raisedEndW1xMiddle = new ShipConfiguration { sternId = RaisedPresets.RaisedStern, bowId = RaisedPresets.RaisedBow,
                rotorId = ShipConfiguration.ReinforcedRotor, carrierId = ShipConfiguration.M1Carrier };
            raisedEndW1xMiddle.middleIds.Add(ExpandedPresets.ExpandedMiddle);
            RejectMix("raised-ends-w1x-middle-rejected", raisedEndW1xMiddle);

            var w1xEndsRaisedMiddle = new ShipConfiguration { sternId = ExpandedPresets.ExpandedStern, bowId = ExpandedPresets.ExpandedBow,
                rotorId = ShipConfiguration.ReinforcedRotor, carrierId = ShipConfiguration.M1Carrier };
            w1xEndsRaisedMiddle.middleIds.Add(RaisedPresets.RaisedMiddle);
            RejectMix("w1x-ends-raised-middle-rejected", w1xEndsRaisedMiddle);

            var raisedSternW1xBow = new ShipConfiguration { sternId = RaisedPresets.RaisedStern, bowId = ExpandedPresets.ExpandedBow,
                rotorId = ShipConfiguration.ReinforcedRotor, carrierId = ShipConfiguration.M1Carrier };
            raisedSternW1xBow.middleIds.Add(RaisedPresets.RaisedMiddle);
            RejectMix("raised-stern-w1x-bow-direct-join-rejected", raisedSternW1xBow);

            var raisedW1r2Anything = new ShipConfiguration { sternId = RaisedPresets.RaisedStern, bowId = ShipConfiguration.V3Bow,
                rotorId = ShipConfiguration.ReinforcedRotor, carrierId = ShipConfiguration.M1Carrier };
            raisedW1r2Anything.middleIds.Add(RaisedPresets.RaisedMiddle);
            RejectMix("raised-w1r2-bow-rejected", raisedW1r2Anything);

            // =============================================================
            // sec 3/docs/RAISED-SECTIONS.md sec 5: RAISED_DECK_BAYS only
            // blocks 0 middles with BOTH ends raised (no room for either
            // end's own wall) -- the old "1-2 middles only" cap is gone
            // (RaisedSectionsValidation enumerates every level combination
            // for 0-3 middles against this same rule); 3 fully-connected
            // raised middles now assembles too, though it is UNVERIFIED
            // against Astra's art (only 1-2 bays were ever rendered).
            // =============================================================
            var bays0 = RaisedPresets.WithMiddles(0);
            var bays0R = ShipAssembler.Assemble(bays0, lib);
            Gate("raised-zero-middles-rejected-RAISED_DECK_BAYS", !bays0R.ok && bays0R.HasCode("RAISED_DECK_BAYS"), Codes(bays0R));

            var bays3 = RaisedPresets.WithMiddles(3);
            var bays3R = ShipAssembler.Assemble(bays3, lib);
            Gate("raised-three-middles-assembles-unverified-art", bays3R.ok, bays3R.Summary());

            var longR = ShipAssembler.Assemble(RaisedPresets.RaisedLong(), lib);
            Gate("raised-long-one-middle-assembles", longR.ok, longR.Summary());
            var twoBayR = ShipAssembler.Assemble(RaisedPresets.RaisedTwoBay(), lib);
            Gate("raised-two-bay-assembles", twoBayR.ok, twoBayR.Summary());

            // =============================================================
            // sec 5: every W1x gun slot id exists on W1xR (guns carry across
            // the Deck toggle), same pattern as the width toggle's own gate.
            // =============================================================
            var w1xLong = ExpandedPresets.ExpandedLong();
            var w1xLongR = ShipAssembler.Assemble(w1xLong, lib);
            bool gunSlotsCarry = w1xLongR.ok && longR.ok;
            var missingSlots = new List<string>();
            if (gunSlotsCarry)
                foreach (var e in w1xLong.equipment)
                    if (!HasDeckGunSlot(longR, e.slotId)) { gunSlotsCarry = false; missingSlots.Add(e.slotId); }
            Gate("deck-toggle-every-w1x-gun-slot-exists-on-raised", gunSlotsCarry,
                gunSlotsCarry ? $"every W1x-Long fitted slot ({w1xLong.equipment.Count}) resolves on raised Long"
                    : (missingSlots.Count > 0 ? "missing on raised: " + string.Join(", ", missingSlots) : $"w1x ok={w1xLongR.ok}, raised ok={longR.ok}"));

            // The two NEW DeckSlot_0 pairs (middle, bow) pass the assembler's
            // real clearance/passage check against the real cannon -- the
            // spec's "keep the ones that pass, drop the ones that fail"
            // gate. Both already ship in the module JSON; this proves they
            // belong there rather than trusting the data pass's geometry.
            var newSlots = new (string section, string slot)[]
            {
                ("middle[0]", "DeckSlot_0_-1"), ("middle[0]", "DeckSlot_0_1"),
                ("bow", "DeckSlot_0_-1"), ("bow", "DeckSlot_0_1"),
            };
            var newSlotResults = new List<string>();
            bool allNewSlotsPass = true;
            foreach (var (section, slot) in newSlots)
            {
                var cfg = RaisedPresets.WithMiddles(1);
                cfg.equipment.Clear(); // bare hull: only this one slot under test
                cfg.equipment.Add(new EquipmentChoice { slotId = $"{section}/{slot}", moduleId = ShipConfiguration.EquipmentCannon });
                var r = ShipAssembler.Assemble(cfg, lib);
                newSlotResults.Add($"{section}/{slot}={(r.ok ? "PASS" : "FAIL:" + Codes(r))}");
                allNewSlotsPass &= r.ok;
            }
            Gate("raised-new-decksot0-pairs-pass-real-cannon", allNewSlotsPass, string.Join(", ", newSlotResults));

            // =============================================================
            // sec 4: nothing walkable below the flush deck (Z 4.20) -- the
            // between-deck is enclosed and never a passage/slot/area.
            // =============================================================
            bool nothingBelow = true;
            var offenders = new List<string>();
            void CheckModule(ModuleDef d)
            {
                if (d == null) return;
                if (d.sockets != null)
                    foreach (var s in d.sockets)
                    {
                        if (s == null) continue;
                        bool walkRole = s.role == SocketRole.DeckSlot || s.role == SocketRole.DeckArea || s.role == SocketRole.DeckUpper;
                        if (walkRole && s.posU.z < 4.2f - 1e-4f) { nothingBelow = false; offenders.Add($"{d.id}/{s.id} z={F(s.posU.z)}"); }
                    }
                if (d.passages != null)
                    foreach (var p in d.passages)
                    {
                        if (p == null) continue;
                        float bottomZ = p.centreU.z - p.sizeU.z * 0.5f;
                        if (bottomZ < 4.2f - 1e-4f) { nothingBelow = false; offenders.Add($"{d.id}/{p.id} bottomZ={F(bottomZ)}"); }
                    }
            }
            CheckModule(rStern); CheckModule(rMiddle); CheckModule(rBow);
            Gate("raised-nothing-walkable-below-4.20", nothingBelow, nothingBelow ? "no deck.slot/deck.area/deck.upper socket or passage below Z 4.20" : string.Join(", ", offenders));

            // Passages: |y| >= 2.30 (never narrowed from the W1x passage) and
            // 3.4 u tall, centred at 4.20 + 1.70 = 5.90.
            bool passagesOk = true;
            var passageDetail = new List<string>();
            void CheckPassage(ModuleDef d)
            {
                if (d?.passages == null) return;
                foreach (var p in d.passages)
                {
                    if (p == null) continue;
                    float halfY = p.sizeU.y * 0.5f;
                    bool ok = halfY >= 2.3f - 1e-4f && Near(p.sizeU.z, 3.4f) && Near(p.centreU.z, 5.9f);
                    passagesOk &= ok;
                    passageDetail.Add($"{d.id}/{p.id} halfY={F(halfY)} height={F(p.sizeU.z)} centreZ={F(p.centreU.z)} {(ok ? "ok" : "BAD")}");
                }
            }
            CheckPassage(rStern); CheckPassage(rMiddle); CheckPassage(rBow);
            Gate("raised-passages-unnarrowed-and-3.4-tall-above-4.20", passagesOk, string.Join(" | ", passageDetail));

            // =============================================================
            // sec 6: sDepth stays 1, mass/CoM rise, GM gate.
            // =============================================================
            var reference = ShipyardSelfTest.ReferenceFrom(hullFormJson);
            Gate("raised-reference-hull-loads", reference != null, reference != null ? $"lwl {F(reference.lwl)} m" : "no hullform.json");
            if (reference == null) return;

            var refPlan = ShipyardPlanner.PlanFor(ShipConfiguration.Long(), lib, reference, null, out var refAsm);
            Gate("raised-w1r2-reference-plans", refPlan != null && refAsm.ok, refAsm.Summary());
            if (refPlan == null) return;

            var w1xPlan = ShipyardPlanner.PlanFor(w1xLong, lib, reference, refPlan, out var w1xAsm);
            Gate("raised-w1x-long-plans", w1xPlan != null && w1xAsm.ok, w1xAsm.Summary());

            var raisedLongPlan = ShipyardPlanner.PlanFor(RaisedPresets.RaisedLong(), lib, reference, refPlan, out var raisedAsm);
            Gate("raised-long-plans", raisedLongPlan != null && raisedAsm.ok, raisedAsm.Summary());
            var raisedTwoBayPlan = ShipyardPlanner.PlanFor(RaisedPresets.RaisedTwoBay(), lib, reference, refPlan, out var raisedTwoAsm);
            Gate("raised-two-bay-plans", raisedTwoBayPlan != null && raisedTwoAsm.ok, raisedTwoAsm.Summary());
            if (raisedLongPlan == null || w1xPlan == null || raisedTwoBayPlan == null) return;

            Gate("raised-sDepth-stays-1", Near(raisedLongPlan.sDepth, 1f, 0.01f) && Near(raisedTwoBayPlan.sDepth, 1f, 0.01f),
                $"long sDepth={F(raisedLongPlan.sDepth)}, two-bay sDepth={F(raisedTwoBayPlan.sDepth)} (deckZU 1.76 unchanged, underwater form is the W1x form)");

            Gate("raised-walkDeckZU-is-4.20", Near(raisedLongPlan.walkDeckZU, 4.2f) && Near(w1xPlan.walkDeckZU, 1.76f),
                $"raised long walkDeckZU={F(raisedLongPlan.walkDeckZU)}, W1x long walkDeckZU={F(w1xPlan.walkDeckZU)}");

            float stationDeckY = raisedLongPlan.data.stations[raisedLongPlan.data.stations.Length / 2].deckY;
            float w1xStationDeckY = w1xPlan.data.stations[w1xPlan.data.stations.Length / 2].deckY;
            Gate("raised-station-deckY-raised-for-crew-and-deck-load", stationDeckY > w1xStationDeckY + 1.0f,
                $"raised mid-station deckY={F(stationDeckY)} m vs W1x's {F(w1xStationDeckY)} m (expect ~1.22 m higher, the between-deck height)");

            float sternMass = 23125.38f, middleMass = 16215.20f, bowMass = 16265.25f; // hull.*.w1xr.v1.json lightship.massKg
            float expectedLongMass = sternMass + middleMass + bowMass;
            Gate("raised-long-mass-matches-authored-lightship", Near(raisedLongPlan.lightshipKg, expectedLongMass, 5f),
                $"plan lightshipKg={F(raisedLongPlan.lightshipKg)} vs authored sum {F(expectedLongMass)}");

            Gate("raised-CoM-rises-vs-w1x", raisedLongPlan.data.kg > w1xPlan.data.kg + 0.05f,
                $"raised long kg={F(raisedLongPlan.data.kg)} m vs W1x long kg={F(w1xPlan.data.kg)} m");

            // Hard gate (docs/RAISED-DECK.md sec 6): GM > 0.3 m on the raised
            // Long. Checked at her design (lightship) waterline, where
            // `data.gm` is solved -- there is no separate loaded-draft
            // KB/BM solver in this pure-C# layer (HullFormBody re-derives
            // KB/BM only from whatever draft the LIVE rigidbody sits at, at
            // runtime); "loaded" is Kevin's own playtest, not a headless gate.
            Gate("raised-long-GM-above-0.3m-lightship", raisedLongPlan.data.gm > 0.3f,
                $"raised long GM={F(raisedLongPlan.data.gm)} m (W1x long GM={F(w1xPlan.data.gm)} m, delta={F(raisedLongPlan.data.gm - w1xPlan.data.gm)})");
            Gate("raised-two-bay-GM-above-0.3m-lightship", raisedTwoBayPlan.data.gm > 0.3f,
                $"raised two-bay GM={F(raisedTwoBayPlan.data.gm)} m");

            Gate("raised-roll-gyradius-computed", raisedLongPlan.data.gyradiusRoll > 0f && !float.IsNaN(raisedLongPlan.data.gyradiusRoll),
                $"raised long gyradiusRoll={F(raisedLongPlan.data.gyradiusRoll)} m (W1x long {F(w1xPlan.data.gyradiusRoll)} m)");

            // Report line (docs/RAISED-DECK.md sec 6/10): mass, draft, KG, GM,
            // roll period T = 2 pi k / sqrt(g GM), single vs raised vs two-bay.
            void Report(string label, ShipyardPlan p)
            {
                float g = 9.81f;
                float t = p.data.gm > 1e-4f ? 2f * Mathf.PI * p.data.gyradiusRoll / Mathf.Sqrt(g * p.data.gm) : float.NaN;
                Gate($"raised-report-{label}", true,
                    $"mass={F(p.lightshipKg / 1000f)}t draft={F(p.data.draft)}m KG={F(p.data.kg)}m GM={F(p.data.gm)}m rollPeriod={F(t)}s");
            }
            Report("w1x-long", w1xPlan);
            Report("raised-long", raisedLongPlan);
            Report("raised-two-bay", raisedTwoBayPlan);
        }

        static bool Near(float a, float b, float tol = 1e-3f) => Mathf.Abs(a - b) <= tol;

        static bool HasDeckGunSlot(AssemblyResult asm, string qualifiedId)
        {
            if (asm?.slots == null) return false;
            foreach (var s in asm.slots)
                if (s != null && s.qualifiedId == qualifiedId && s.role == SocketRole.DeckSlot
                    && s.classes != null && Array.IndexOf(s.classes, "equipment.deck-gun") >= 0)
                    return true;
            return false;
        }
    }
}
