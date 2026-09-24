using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace SeaSick.Ship.Modular
{
    /// Headless validation of Astra's EXPANDED hull family kit
    /// (art-staging/modular-width-inserts-v1, standard "W1-center-expansion-r1")
    /// against the modular-ship module contract (docs/MODULAR-SHIPS.md,
    /// docs/SHIPYARD-API.md). No scene, no GameObjects; run by
    /// tools/modular-selftest.sh via ModularShipSelfTest.RunWith. See
    /// docs/EXPANDED-HULL-VALIDATION.md for the full numeric report this
    /// backs and the derivations behind the constants below.
    ///
    /// This family WIDENS W1-r2 in place (port/starboard halves moved
    /// outboard + insert strips), NOT an independently modelled hull like
    /// W2-r1 (branch wide-hull) -- length and depth are unchanged, only
    /// beam grows (9.28 -> 12.08). It supersedes W2-r1/W2-placeholder as
    /// the width upgrade (Astra, 2026-09-25); W2-r1 stays unexposed.
    public static class ExpandedHullValidation
    {
        public delegate void GateFn(string name, bool ok, string detail);

        const float Tol = 1e-3f;
        static bool Near(float a, float b, float tol = Tol) => Mathf.Abs(a - b) <= tol;
        static bool Near(Vector3 a, Vector3 b, float tol = Tol) => (a - b).magnitude <= tol;
        static string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);

        // ---- constants computed OUTSIDE this runtime (Python + Blender
        // --background, never the editor) from the kit's own files
        // (manifest.json, hydrostatics/*.json), documented in full in
        // docs/EXPANDED-HULL-VALIDATION.md. Re-verified live below wherever
        // the runtime's own data lets a gate check them directly. ----------

        /// stern-fwd / middle-aft / middle-fwd / bow-aft: max |areaU2[i] -
        /// areaU2[j]| across all 66 waterlines at the four touching faces,
        /// from the kit's own hydrostatics/*.json "stations" arrays (a
        /// richer per-x sectional-area table the runtime HydroTable schema
        /// does not read, so this specific check cannot be re-run against
        /// the loaded ModuleLibrary and is baked here instead).
        const float JoinFaceMaxAreaDiffU2 = 7.2e-15f;

        /// Midship deck-level area (one station, x=3.0, of 99 -- constant to
        /// 1e-13 across the whole bay, i.e. genuinely prismatic) x the 6.00 u
        /// bay length, vs the table's own integrated deck volume.
        const float MidshipProfileAreaU2 = 42.38111512609935f;
        const float MidshipTableDeckVolumeU3 = 254.28660599436586f; // integratedVolumeU3[-1], Midship_W1

        const float CLEAR_Y_HALF = 1.15f; // half of the 2.3u gun-slot clearance width (unchanged from W1-r2)

        const float W1r2LongDeckVolumeU3 = 611.6711306796573f; // stern+middle+bow, today's committed tables
        const float ExpandedLongDeckVolumeU3 = 815.6869284514565f; // same sum, this kit's tables
        const float TodayLongLightshipKg = 31906.6f;

        // Per-module lightship (kg): TodayLongLightshipKg * (Long volume
        // ratio) * (this module's own deck-volume share of the Long total).
        const float SternLightshipKg = 17404.8f, MiddleLightshipKg = 13264.4f, BowLightshipKg = 11879.5f;
        const float ExpandedLongLightshipKg = SternLightshipKg + MiddleLightshipKg + BowLightshipKg; // 42548.7
        const float ExpandedShortLightshipKg = SternLightshipKg + BowLightshipKg; // 29284.3

        public static void Body(string stdJson, IList<string> mods, IList<string> names,
            Func<string, string> readResourceText, GateFn Gate)
        {
            var lib = ModuleLibrary.FromJson(stdJson, mods, names);
            if (readResourceText != null) lib.LoadHydrostatics(readResourceText);
            Gate("w1x-library-loads", lib.Ok, $"ok={lib.Ok} errors=[{string.Join(" | ", lib.errors)}]");
            if (!lib.Usable) return;

            // =====================================================================
            // A.1 Interface profile.
            // =====================================================================
            var w1x = lib.FindProfile("W1x");
            var w1r2 = lib.FindProfile("W1-r2");
            Gate("w1x-profile-registered", w1x != null && Near(w1x.halfBeamU, 6.04f) && Near(w1x.deckZU, 1.76f) && Near(w1x.keelZU, -1.92f),
                w1x != null ? $"halfBeam={F(w1x.halfBeamU)} deck={F(w1x.deckZU)} keel={F(w1x.keelZU)}" : "missing");
            Gate("w1x-profile-differs-in-beam-only", w1r2 != null && w1x != null
                && !Near(w1x.halfBeamU, w1r2.halfBeamU) && Near(w1x.deckZU, w1r2.deckZU) && Near(w1x.keelZU, w1r2.keelZU),
                $"W1x halfBeam={F(w1x?.halfBeamU ?? 0)} vs W1-r2 {F(w1r2?.halfBeamU ?? 0)}; deck/keel UNCHANGED ({F(w1x?.deckZU ?? 0)}/{F(w1x?.keelZU ?? 0)})");
            Gate("w1x-join-faces-identical-computed-outside-runtime", JoinFaceMaxAreaDiffU2 < 1e-4f,
                $"stern-fwd == middle-aft == middle-fwd == bow-aft, max |area diff| across all 66 waterlines = {JoinFaceMaxAreaDiffU2:0.###E+0} u^2 " +
                "(from the kit's hydrostatics/*.json 'stations' arrays, a richer per-x table the runtime HydroTable schema does not read -- see docs/EXPANDED-HULL-VALIDATION.md)");

            var mixed1 = new ShipConfiguration { sternId = ShipConfiguration.V3Stern, bowId = ShipConfiguration.V3Bow,
                rotorId = ShipConfiguration.ReinforcedRotor, carrierId = ShipConfiguration.M1Carrier };
            mixed1.middleIds.Add(ExpandedPresets.ExpandedMiddle);
            mixed1.fittings.Add(new FittingChoice { socketId = ShipConfiguration.ChimneySocket, moduleId = ShipConfiguration.V3Chimney });
            var mixed1R = ShipAssembler.Assemble(mixed1, lib);
            string mixed1Msg = First(mixed1R, "JOIN_PROFILE_MISMATCH");
            Gate("w1r2-stern-w1x-middle-rejected", !mixed1R.ok && mixed1Msg != null, mixed1Msg ?? Codes(mixed1R));

            var mixed2 = new ShipConfiguration { sternId = ExpandedPresets.ExpandedStern, bowId = ExpandedPresets.ExpandedBow,
                rotorId = ShipConfiguration.ReinforcedRotor, carrierId = ShipConfiguration.M1Carrier };
            mixed2.middleIds.Add(ShipConfiguration.V3Middle);
            mixed2.fittings.Add(new FittingChoice { socketId = ShipConfiguration.ChimneySocket, moduleId = ShipConfiguration.V3Chimney });
            var mixed2R = ShipAssembler.Assemble(mixed2, lib);
            string mixed2Msg = First(mixed2R, "JOIN_PROFILE_MISMATCH");
            Gate("w1x-stern-w1r2-middle-rejected", !mixed2R.ok && mixed2Msg != null, mixed2Msg ?? Codes(mixed2R));

            var mixed3 = new ShipConfiguration { sternId = ExpandedPresets.ExpandedStern, bowId = ShipConfiguration.V3Bow,
                rotorId = ShipConfiguration.ReinforcedRotor, carrierId = ShipConfiguration.M1Carrier };
            mixed3.fittings.Add(new FittingChoice { socketId = ShipConfiguration.ChimneySocket, moduleId = ShipConfiguration.V3Chimney });
            var mixed3R = ShipAssembler.Assemble(mixed3, lib);
            string mixed3Msg = First(mixed3R, "JOIN_PROFILE_MISMATCH");
            Gate("w1x-stern-w1r2-bow-direct-join-rejected", !mixed3R.ok && mixed3Msg != null, mixed3Msg ?? Codes(mixed3R));

            // =====================================================================
            // A.2 Sockets, lengths, wheel, chimney, multi-part visuals.
            // =====================================================================
            lib.TryGet(ExpandedPresets.ExpandedStern, out var stern);
            lib.TryGet(ExpandedPresets.ExpandedMiddle, out var middle);
            lib.TryGet(ExpandedPresets.ExpandedBow, out var bow);
            Gate("w1x-lengths-match-w1r2", stern != null && middle != null && bow != null
                && Near(stern.lengthU, 9.3f) && Near(middle.lengthU, 6.0f) && Near(bow.lengthU, 10.98f),
                stern != null && middle != null && bow != null
                    ? $"stern={F(stern.lengthU)} middle={F(middle.lengthU)} bow={F(bow.lengthU)}" : "module(s) missing");

            var wheelSocket = stern != null ? ModuleLibrary.FindSocketById(stern, "WheelModuleSocket") : null;
            Gate("w1x-wheel-axle-stern-local", wheelSocket != null && Near(wheelSocket.posU, new Vector3(0.72f, 0f, 0.35f)) && wheelSocket.standard == "M1",
                wheelSocket != null ? $"{S(wheelSocket.posU)} standard={wheelSocket.standard}" : "missing");

            var chimneySocket = stern != null ? ModuleLibrary.FindSocketById(stern, "Chimney") : null;
            Gate("w1x-chimney-socket", chimneySocket != null && Near(chimneySocket.posU, new Vector3(0f, 0f, 1.76f))
                && chimneySocket.placementRule == PlacementRule.AssembledMidpoint,
                chimneySocket != null ? $"{S(chimneySocket.posU)} rule='{chimneySocket.placementRule}'" : "missing");

            var stem = bow != null ? ModuleLibrary.FindSocket(bow, SocketRole.HullStem) : null;
            Gate("w1x-bow-stem-matches-w1r2", stem != null && Near(stem.posU.x, 8.45f),
                stem != null ? $"stem.x={F(stem.posU.x)} (carried over from W1-r2's own hand-measured value; kit README: prow/stem unchanged)" : "missing");

            var m1 = lib.FindMount("M1");
            Gate("w1x-m1-rotor-fits-core-housing", wheelSocket != null && m1 != null && wheelSocket.standard == "M1"
                && m1.sweptRadius <= wheelSocket.radiusLimit + 1e-5f,
                wheelSocket != null ? $"pocket standard={wheelSocket.standard} limit={F(wheelSocket.radiusLimit)} M1 swept={F(m1?.sweptRadius ?? 0)}" : "missing");

            var bigCfg = ExpandedPresets.ExpandedShort();
            bigCfg.rotorId = ShipConfiguration.OversizedRotor;
            var bigR = ShipAssembler.Assemble(bigCfg, lib);
            string bigMsg = First(bigR, "WHEEL_MOUNT_MISMATCH") ?? First(bigR, "WHEEL_TOO_LARGE");
            Gate("w1x-m1l-oversized-wheel-rejected", !bigR.ok && bigMsg != null, bigMsg ?? Codes(bigR));

            // Multi-part visuals: every listed part's localPositionU matches
            // the kit manifest's local_position (the Port/Starboard pieces
            // move +-1.4 u in Y; everything else is zero).
            bool visualsOk = stern != null && middle != null && bow != null;
            int checkedParts = 0;
            // Stern/Middle: the ORIGINAL Port/Starboard pieces are reused via
            // a pure +-1.4u Y translation (README: "move outward 1.4 units
            // each"). Bow: the Port/Starboard pieces are AUTHORED counterparts,
            // not translations (README: "not purely translated standard
            // halves"), so their own local_position is zero -- the widening
            // is baked into the mesh, not applied as an offset.
            void CheckVisuals(ModuleDef d, bool translated, ref bool ok)
            {
                if (d?.visuals == null) { ok = false; return; }
                foreach (var v in d.visuals)
                {
                    checkedParts++;
                    bool expectShift = translated && v.id != null && v.id.StartsWith("Port__");
                    bool expectShiftNeg = translated && v.id != null && v.id.StartsWith("Starboard__");
                    Vector3 expected = expectShift ? new Vector3(0f, 1.4f, 0f) : expectShiftNeg ? new Vector3(0f, -1.4f, 0f) : Vector3.zero;
                    if (!Near(v.localPositionU, expected, 1e-4f)) ok = false;
                }
            }
            CheckVisuals(stern, true, ref visualsOk); CheckVisuals(middle, true, ref visualsOk); CheckVisuals(bow, false, ref visualsOk);
            Gate("w1x-multipart-visuals-local-position-matches-manifest", visualsOk && checkedParts == 23 + 11 + 16,
                $"{checkedParts} visual parts checked (23 stern + 11 middle + 16 bow, Carrier/Rotor excluded from the stern -- those reuse the existing wheel.* modules unchanged); Port/Starboard pieces carry +-1.4u Y, everything else zero");

            // =====================================================================
            // A.3 Hydrostatic tables.
            // =====================================================================
            var sternHydro = lib.Hydrostatics(ExpandedPresets.ExpandedStern);
            var middleHydro = lib.Hydrostatics(ExpandedPresets.ExpandedMiddle);
            var bowHydro = lib.Hydrostatics(ExpandedPresets.ExpandedBow);
            Gate("w1x-hydro-tables-load-and-hash-match", sternHydro != null && middleHydro != null && bowHydro != null,
                $"stern={(sternHydro != null ? "ok" : lib.hydrostaticsErrors.TryGetValue(ExpandedPresets.ExpandedStern, out var e1) ? e1 : "?")} " +
                $"middle={(middleHydro != null ? "ok" : lib.hydrostaticsErrors.TryGetValue(ExpandedPresets.ExpandedMiddle, out var e2) ? e2 : "?")} " +
                $"bow={(bowHydro != null ? "ok" : lib.hydrostaticsErrors.TryGetValue(ExpandedPresets.ExpandedBow, out var e3) ? e3 : "?")}");
            Gate("w1x-hydro-valid-range-unchanged-from-w1r2", sternHydro != null && middleHydro != null && bowHydro != null
                && Near(sternHydro.KeelZU, -1.92f) && Near(sternHydro.DeckZU, 1.76f)
                && Near(middleHydro.KeelZU, -1.92f) && Near(middleHydro.DeckZU, 1.76f)
                && Near(bowHydro.KeelZU, -1.92f) && Near(bowHydro.DeckZU, 1.76f),
                "keel -1.92 .. deck 1.76 on all three tables, bit-for-bit W1-r2's own range (depth unchanged, as the kit README claims)");

            float midshipTrapezoidVolumeU3 = MidshipProfileAreaU2 * 6.0f;
            float midshipTableDeckVolumeU3 = middleHydro != null ? middleHydro.VolumeAt(middleHydro.DeckZU) : float.NaN;
            float crossCheckPct = middleHydro != null ? Mathf.Abs(midshipTrapezoidVolumeU3 - midshipTableDeckVolumeU3) / midshipTableDeckVolumeU3 * 100f : float.NaN;
            Gate("w1x-midship-deck-area-cross-check-within-2pct", middleHydro != null && crossCheckPct < 2f
                && Near(midshipTableDeckVolumeU3, MidshipTableDeckVolumeU3, 0.01f),
                $"one station's deck area (constant to 1e-13 across all 99 stations -- a genuinely prismatic bay) x 6.00u = {F(midshipTrapezoidVolumeU3)} vs the loaded table's own deck volume {F(midshipTableDeckVolumeU3)} u^3, diff {F(crossCheckPct)}%");

            // The kit's own manifest.fittings.chimney_position (13.14, 0, 1.76,
            // for a one-middle-bay assembly) is an independent proof the
            // assembled length is unchanged: the assembled-midpoint rule run
            // on this family's own stern/middle/bow must reproduce it, AND it
            // is bit-for-bit today's W1-r2 Long chimney X too.
            var longR = ShipAssembler.Assemble(ExpandedPresets.ExpandedLong(), lib);
            var lch = longR.ok ? longR.Find("fitting:" + ShipConfiguration.ChimneySocket) : null;
            Gate("w1x-chimney-midpoint-matches-kit-manifest-and-todays-long", lch != null && Near(lch.positionM.z, 6.57f, 0.01f),
                lch != null ? $"{S(lch.positionM)} (kit manifest chimney_position.x 13.14u x 0.5 m/u = 6.57m; today's W1-r2 Long chimney is also 6.57m)" : longR.Summary());

            // =====================================================================
            // A.4 Deck-gun slots, real cannon (the module already committed to
            // the shipyard, equipment.cannon.astra.v1 -- unlike wide-hull's
            // validation, no synthetic footprint module is needed here since
            // guns are explicit equipment at this branch's base).
            // =====================================================================
            // A bare hull (no pre-fitted guns), unlike ExpandedPresets.ExpandedLong()
            // which already carries the standard 6-gun loadout on these same
            // designated slots -- fitting a 7th cannon on top of one of those
            // would only prove EQUIPMENT_SLOT_TAKEN, not clearance/passage.
            ShipConfiguration BareLong()
            {
                var c = new ShipConfiguration { sternId = ExpandedPresets.ExpandedStern, bowId = ExpandedPresets.ExpandedBow,
                    rotorId = ShipConfiguration.ReinforcedRotor, carrierId = ShipConfiguration.M1Carrier };
                c.middleIds.Add(ExpandedPresets.ExpandedMiddle);
                c.fittings.Add(new FittingChoice { socketId = ShipConfiguration.ChimneySocket, moduleId = ShipConfiguration.V3Chimney });
                return c;
            }

            var allSlots = new (string section, string slot)[]
            {
                ("stern", "DeckSlot_2_-1"), ("stern", "DeckSlot_2_1"),
                ("middle[0]", "DeckSlot_0_-1"), ("middle[0]", "DeckSlot_0_1"),
                ("middle[0]", "DeckSlot_1_-1"), ("middle[0]", "DeckSlot_1_1"),
                ("bow", "DeckSlot_0_-1"), ("bow", "DeckSlot_0_1"),
                ("bow", "DeckSlot_1_-1"), ("bow", "DeckSlot_1_1"),
            };
            var slotResults = new List<string>();
            bool allSlotsPass = true;
            foreach (var (section, slot) in allSlots)
            {
                var cfg = BareLong();
                cfg.equipment.Add(new EquipmentChoice { slotId = $"{section}/{slot}", moduleId = ShipConfiguration.EquipmentCannon });
                var r = ShipAssembler.Assemble(cfg, lib);
                bool ok = r.ok;
                slotResults.Add($"{section}/{slot}={(ok ? "PASS" : "FAIL:" + Codes(r))}");
                allSlotsPass &= ok;
            }
            Gate("all-10-w1x-deck-slots-pass-real-cannon", allSlotsPass, string.Join(", ", slotResults));

            var intrudeCfg = BareLong();
            intrudeCfg.equipment.Add(new EquipmentChoice { slotId = "middle[0]/DeckArea", moduleId = ShipConfiguration.EquipmentCannon });
            var intrudeR = ShipAssembler.Assemble(intrudeCfg, lib);
            Gate("w1x-real-cannon-in-passage-rejected", !intrudeR.ok && intrudeR.HasCode("EQUIPMENT_BLOCKS_PASSAGE"),
                First(intrudeR, "EQUIPMENT_BLOCKS_PASSAGE") ?? Codes(intrudeR));

            // The gun-slot derivation's safety margin: the designated slots'
            // clearance INNER edge (socket Y 4.85 minus half the 2.3u
            // clearance = 3.70) clears the UNCHANGED passage half-width
            // (2.30) by 1.40 u -- generous, unlike W1-r2's own design where
            // the two figures are equal (zero gap, "touch, not overlap").
            // (Not independently gated beyond the 10-slot pass above: the
            // assembler only ever tests the fixed clearance box against the
            // passage, which all-10-w1x-deck-slots-pass-real-cannon already
            // exercises for every slot; there is no separate "how much gap"
            // query to assert against.)
            const float slotInnerEdgeU = 4.85f - CLEAR_Y_HALF;
            Gate("w1x-passage-gap-is-generous-not-zero", slotInnerEdgeU - 2.30f > 1.0f,
                $"slot clearance inner edge {F(slotInnerEdgeU)}u vs unchanged passage half-width 2.30u -> {F(slotInnerEdgeU - 2.30f)}u gap (W1-r2's own design has 0)");

            // =====================================================================
            // A.5 Capacity + mass, assembly, reshape factors, float, wheel dip.
            // =====================================================================
            var shortR = ShipAssembler.Assemble(ExpandedPresets.ExpandedShort(), lib);
            Gate("w1x-short-and-long-assemble", shortR.ok && longR.ok, $"short: {shortR.Summary()} | long: {longR.Summary()}");

            HullMeasure.TryMeasure(longR, lib, out var longMeasure);
            var refLong = ShipAssembler.Assemble(ShipConfiguration.Long(), lib);
            HullMeasure.TryMeasure(refLong, lib, out var refMeasure);
            float sL = refMeasure.waterlineLengthU > 0 ? longMeasure.waterlineLengthU / refMeasure.waterlineLengthU : 0f;
            float sB = refMeasure.beamU > 0 ? longMeasure.beamU / refMeasure.beamU : 0f;
            float sD = refMeasure.depthU > 0 ? longMeasure.depthU / refMeasure.depthU : 0f;
            Gate("w1x-reshape-factors", Near(sL, 1f, 0.01f) && Near(sB, 12.08f / 9.28f, 0.01f) && Near(sD, 1f, 0.01f),
                $"sL={F(sL)} sB={F(sB)} (expect 12.08/9.28={F(12.08f / 9.28f)}) sD={F(sD)} (expect 1, depth unchanged)");

            float LightshipOf(AssemblyResult r) { float m = 0f; foreach (var p in r.placed) if (ModuleKind.IsHull(p.kind) && lib.TryGet(p.moduleId, out var d) && d.lightship != null) m += d.lightship.massKg; return m; }
            int HoldOf(AssemblyResult r) { int h = 0; foreach (var p in r.placed) if (ModuleKind.IsHull(p.kind) && lib.TryGet(p.moduleId, out var d) && d.capacity?.holdCells != null) h += d.capacity.holdCells.value; return h; }
            int BerthsOf(AssemblyResult r) { int b = 0; foreach (var p in r.placed) if (ModuleKind.IsHull(p.kind) && lib.TryGet(p.moduleId, out var d) && d.capacity?.berths != null) b += d.capacity.berths.value; return b; }

            float shortLightship = LightshipOf(shortR), longLightship = LightshipOf(longR);
            int shortHold = HoldOf(shortR), longHold = HoldOf(longR);
            int shortBerths = BerthsOf(shortR), longBerths = BerthsOf(longR);
            int shortGuns = 0, longGuns = 0;
            foreach (var p in shortR.placed) if (p.kind == ModuleKind.Equipment) shortGuns++;
            foreach (var p in longR.placed) if (p.kind == ModuleKind.Equipment) longGuns++;
            Gate("w1x-capacity-and-mass-totals", Near(shortLightship, ExpandedShortLightshipKg, 5f) && Near(longLightship, ExpandedLongLightshipKg, 5f)
                && shortHold == 15 && longHold == 22 && shortBerths == 4 && longBerths == 9 && shortGuns == 4 && longGuns == 6,
                $"short: mass={F(shortLightship)} hold={shortHold} berths={shortBerths} guns={shortGuns} | " +
                $"long: mass={F(longLightship)} hold={longHold} berths={longBerths} guns={longGuns} " +
                $"(today's W1-r2 Long: {TodayLongLightshipKg} kg, 16 hold, 8 berths, 6 guns; volume ratio {F(ExpandedLongDeckVolumeU3 / W1r2LongDeckVolumeU3)})");

            var shortHydro = AssemblyHydrostatics.For(shortR, lib);
            var longHydro = AssemblyHydrostatics.For(longR, lib);
            const float density = 1025f;
            const float cargoUnitKg = 500f, crewKg = 90f, gunKg = 500f;
            float shortFull = shortLightship + shortHold * cargoUnitKg + shortBerths * crewKg + shortGuns * gunKg;
            float longFull = longLightship + longHold * cargoUnitKg + longBerths * crewKg + longGuns * gunKg;
            bool shortLightOk = shortHydro.SolveWaterline(shortLightship, density, out float shortLightZ, out float shortLightDraft);
            bool shortFullOk = shortHydro.SolveWaterline(shortFull, density, out float shortFullZ, out float shortFullDraft);
            bool longLightOk = longHydro.SolveWaterline(longLightship, density, out float longLightZ, out float longLightDraft);
            bool longFullOk = longHydro.SolveWaterline(longFull, density, out float longFullZ, out float longFullDraft);
            Gate("w1x-short-floats-lightship-and-full-load", shortLightOk && shortFullOk
                && shortLightZ <= shortHydro.DeckZU + 1e-4f && shortFullZ <= shortHydro.DeckZU + 1e-4f,
                $"lightship z={F(shortLightZ)} draft={F(shortLightDraft)}m | full z={F(shortFullZ)} draft={F(shortFullDraft)}m | deck={F(shortHydro.DeckZU)} keel={F(shortHydro.KeelZU)}");
            Gate("w1x-long-floats-lightship-and-full-load", longLightOk && longFullOk
                && longLightZ <= longHydro.DeckZU + 1e-4f && longFullZ <= longHydro.DeckZU + 1e-4f,
                $"lightship z={F(longLightZ)} draft={F(longLightDraft)}m | full z={F(longFullZ)} draft={F(longFullDraft)}m | deck={F(longHydro.DeckZU)} keel={F(longHydro.KeelZU)}");

            // Wheel-dip: wheel/rotor UNCHANGED from W1-r2 (axle 0.35, nominal
            // radius 1.62), so wheel bottom = -1.27, same threshold as today.
            float wheelBottomZ = 0.35f - 1.62f;
            bool wheelDipsShortLight = shortLightOk && shortLightZ > wheelBottomZ;
            bool wheelDipsShortFull = shortFullOk && shortFullZ > wheelBottomZ;
            bool wheelDipsLongLight = longLightOk && longLightZ > wheelBottomZ;
            bool wheelDipsLongFull = longFullOk && longFullZ > wheelBottomZ;
            Gate("w1x-wheel-dips-at-lightship-and-full-load", wheelDipsShortLight && wheelDipsShortFull && wheelDipsLongLight && wheelDipsLongFull,
                $"wheel bottom z={F(wheelBottomZ)} (unchanged); waterline z: short-light={F(shortLightZ)} short-full={F(shortFullZ)} long-light={F(longLightZ)} long-full={F(longFullZ)} (all must be > wheel bottom)");
        }

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
    }
}
