using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace SeaSick.Ship.Modular
{
    /// Headless validation of Astra's W2-r1 "wide hull" kit
    /// (art-staging/modular-hull-wide-v1) against the modular-ship module
    /// contract (docs/MODULAR-SHIPS.md, docs/SHIPYARD-API.md), PLUS the
    /// Task-C proof that the schema/assembler already support a width
    /// TRANSITION module bridging W1-r2 and W2-r1. No scene, no
    /// GameObjects; run by tools/modular-selftest.sh via
    /// ModularShipSelfTest.RunWith. See docs/WIDE-HULL-VALIDATION.md for
    /// the full numeric report this backs.
    public static class WideHullValidation
    {
        public delegate void GateFn(string name, bool ok, string detail);

        const float Tol = 1e-3f;
        static bool Near(float a, float b, float tol = Tol) => Mathf.Abs(a - b) <= tol;
        static bool Near(Vector3 a, Vector3 b, float tol = Tol) => (a - b).magnitude <= tol;
        static string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);

        public const string WideCannonId = "equipment.cannon.real-footprint.wide-validation";

        /// `stdJson`/`mods`/`names`: the SAME Resources/ShipModules data the
        /// rest of the self-test loads (so this runs against whatever is
        /// actually committed, W1-r2 AND W2-r1 modules together).
        /// `readResourceText`: resolves a hydrostatics resourcePath to its
        /// JSON text (Resources.Load in Unity, disk in tools/modular-selftest).
        public static void Body(string stdJson, IList<string> mods, IList<string> names,
            Func<string, string> readResourceText, GateFn Gate)
        {
            var lib = ModuleLibrary.FromJson(stdJson, mods, names);
            if (readResourceText != null) lib.LoadHydrostatics(readResourceText);
            Gate("w2-library-loads", lib.Ok, $"ok={lib.Ok} errors=[{string.Join(" | ", lib.errors)}]");
            if (!lib.Usable) return;

            // =====================================================================
            // A.1 Interface profile: Stern_W2 fwd == Midship_W2 aft == Midship_W2
            // fwd == Bow_W2 aft (verified point-for-point from interfaces.json
            // OUTSIDE this runtime, maxdiff 0.0 across all 4 loops -- see the
            // report). standards.json's "W2" entry is the transcription of that
            // single shared profile; check it is registered correctly and that
            // it is NOT the W1-r2 profile.
            // =====================================================================
            var w2 = lib.FindProfile("W2");
            var w1r2 = lib.FindProfile("W1-r2");
            Gate("w2-profile-registered", w2 != null && w2.profilePoints == 22 && Near(w2.halfBeamU, 5.8f)
                && Near(w2.deckZU, 1.76f) && Near(w2.keelZU, -2.75f),
                w2 != null ? $"points={w2.profilePoints} halfBeam={F(w2.halfBeamU)} deck={F(w2.deckZU)} keel={F(w2.keelZU)}" : "missing");
            Gate("w2-profile-differs-from-w1r2", w1r2 != null && w2 != null
                && !Near(w2.halfBeamU, w1r2.halfBeamU) && !Near(w2.keelZU, w1r2.keelZU),
                $"W2 halfBeam={F(w2?.halfBeamU ?? 0)} keel={F(w2?.keelZU ?? 0)} vs W1-r2 halfBeam={F(w1r2?.halfBeamU ?? 0)} keel={F(w1r2?.keelZU ?? 0)}");

            var mixed1 = new ShipConfiguration { sternId = ShipConfiguration.V3Stern, bowId = ShipConfiguration.V3Bow,
                rotorId = ShipConfiguration.ReinforcedRotor, carrierId = ShipConfiguration.M1Carrier };
            mixed1.middleIds.Add(WidePresets.WideMiddle);
            mixed1.fittings.Add(new FittingChoice { socketId = ShipConfiguration.ChimneySocket, moduleId = ShipConfiguration.V3Chimney });
            var mixed1R = ShipAssembler.Assemble(mixed1, lib);
            string mixed1Msg = First(mixed1R, "JOIN_PROFILE_MISMATCH");
            Gate("w1-stern-w2-middle-rejected", !mixed1R.ok && mixed1Msg != null, mixed1Msg ?? Codes(mixed1R));

            var mixed2 = WidePresets.WithMiddles(0);
            mixed2.middleIds.Add(ShipConfiguration.V3Middle);
            var mixed2R = ShipAssembler.Assemble(mixed2, lib);
            string mixed2Msg = First(mixed2R, "JOIN_PROFILE_MISMATCH");
            Gate("w2-stern-w1-middle-rejected", !mixed2R.ok && mixed2Msg != null, mixed2Msg ?? Codes(mixed2R));

            // =====================================================================
            // A.2 Sockets and lengths.
            // =====================================================================
            lib.TryGet(WidePresets.WideStern, out var stern);
            lib.TryGet(WidePresets.WideMiddle, out var middle);
            lib.TryGet(WidePresets.WideBow, out var bow);
            Gate("w2-lengths-match-manifest", stern != null && middle != null && bow != null
                && Near(stern.lengthU, 9.3f) && Near(middle.lengthU, 6.0f) && Near(bow.lengthU, 10.98f),
                stern != null && middle != null && bow != null
                    ? $"stern={F(stern.lengthU)} middle={F(middle.lengthU)} bow={F(bow.lengthU)}" : "module(s) missing");

            var wheelSocket = stern != null ? ModuleLibrary.FindSocketById(stern, "WheelModuleSocket") : null;
            Gate("w2-wheel-axle-stern-local", wheelSocket != null && Near(wheelSocket.posU, new Vector3(0.72f, 0f, 0.35f)),
                wheelSocket != null ? S(wheelSocket.posU) : "missing");

            var chimneySocket = stern != null ? ModuleLibrary.FindSocketById(stern, "Chimney") : null;
            Gate("w2-chimney-socket", chimneySocket != null && Near(chimneySocket.posU, new Vector3(0f, 0f, 1.76f))
                && chimneySocket.placementRule == PlacementRule.AssembledMidpoint,
                chimneySocket != null ? $"{S(chimneySocket.posU)} rule='{chimneySocket.placementRule}'" : "missing");

            var stem = bow != null ? ModuleLibrary.FindSocket(bow, SocketRole.HullStem) : null;
            Gate("w2-bow-stem-matches-w1r2", stem != null && Near(stem.posU.x, 8.45f),
                stem != null ? $"stem.x={F(stem.posU.x)} (measured via Blender re-import, see report; W1-r2's is also 8.45)" : "missing");

            var m1 = lib.FindMount("M1");
            Gate("w2-m1-rotor-fits-w2-housing", wheelSocket != null && m1 != null
                && Near(wheelSocket.standard == "M1" ? m1.sweptRadius : -1f, m1.sweptRadius)
                && m1.sweptRadius <= wheelSocket.radiusLimit + 1e-5f,
                wheelSocket != null ? $"pocket standard={wheelSocket.standard} limit={F(wheelSocket.radiusLimit)} M1 swept={F(m1?.sweptRadius ?? 0)}" : "missing");

            var bigCfg = WidePresets.WideShort();
            bigCfg.rotorId = ShipConfiguration.OversizedRotor;
            var bigR = ShipAssembler.Assemble(bigCfg, lib);
            string bigMsg = First(bigR, "WHEEL_MOUNT_MISMATCH") ?? First(bigR, "WHEEL_TOO_LARGE");
            Gate("w2-m1l-oversized-wheel-rejected", !bigR.ok && bigMsg != null, bigMsg ?? Codes(bigR));

            // =====================================================================
            // A.3 Hydrostatic tables: schema, hash, range, monotonic volume
            // (all enforced by HydroTable.Valid()/ModuleLibrary.LoadHydrostatics
            // already -- these gates prove the W2 tables actually pass those
            // checks, not just that the schema exists), and the independent
            // cross-check: Midship_W2's deck volume vs the trapezoid area of its
            // interfaces.json Hull_Shell profile x 6.00 u.
            // =====================================================================
            var sternHydro = lib.Hydrostatics(WidePresets.WideStern);
            var middleHydro = lib.Hydrostatics(WidePresets.WideMiddle);
            var bowHydro = lib.Hydrostatics(WidePresets.WideBow);
            Gate("w2-hydro-tables-load-and-hash-match", sternHydro != null && middleHydro != null && bowHydro != null,
                $"stern={(sternHydro != null ? "ok" : lib.hydrostaticsErrors.TryGetValue(WidePresets.WideStern, out var e1) ? e1 : "?")} " +
                $"middle={(middleHydro != null ? "ok" : lib.hydrostaticsErrors.TryGetValue(WidePresets.WideMiddle, out var e2) ? e2 : "?")} " +
                $"bow={(bowHydro != null ? "ok" : lib.hydrostaticsErrors.TryGetValue(WidePresets.WideBow, out var e3) ? e3 : "?")}");
            Gate("w2-hydro-valid-range", sternHydro != null && middleHydro != null && bowHydro != null
                && Near(sternHydro.KeelZU, -2.75f) && Near(sternHydro.DeckZU, 1.76f)
                && Near(middleHydro.KeelZU, -2.75f) && Near(middleHydro.DeckZU, 1.76f)
                && Near(bowHydro.KeelZU, -2.75f) && Near(bowHydro.DeckZU, 1.76f),
                "keel -2.75 .. deck 1.76 on all three tables");

            // Independent cross-check (computed OUTSIDE this runtime from
            // interfaces.json's Midship_W2_aft Hull_Shell loop -- shoelace area
            // of the 21-point ring after the ring's leading duplicate/attachment
            // point is dropped, closing back through the deck line -- see the
            // report for the full derivation): 49.1374094413 u^2 x 6.00 u =
            // 294.824456... u^3, vs the table's integrated deck volume.
            const float midshipProfileAreaU2 = 49.1374094413f;
            float midshipTrapezoidVolumeU3 = midshipProfileAreaU2 * 6.0f;
            float midshipTableDeckVolumeU3 = middleHydro != null ? middleHydro.VolumeAt(middleHydro.DeckZU) : float.NaN;
            float crossCheckPct = middleHydro != null ? Mathf.Abs(midshipTrapezoidVolumeU3 - midshipTableDeckVolumeU3) / midshipTableDeckVolumeU3 * 100f : float.NaN;
            Gate("midship-deck-area-cross-check-within-2pct", middleHydro != null && crossCheckPct < 2f,
                $"trapezoid={F(midshipTrapezoidVolumeU3)} table={F(midshipTableDeckVolumeU3)} diff={F(crossCheckPct)}%");

            // =====================================================================
            // A.4 Deck-gun slots: every empty in the kit, tested with the REAL
            // cannon footprint (art-staging/cannon-astra-v1: 1.22 wide x 2.18
            // long x 1.28 high) against the section bounds and the derived crew
            // passages, using the assembler's own equipment placement checks.
            // =====================================================================
            var cannonDef = new ModuleDef
            {
                id = WideCannonId, version = 1, kind = ModuleKind.Equipment, status = ModuleStatus.Prototype,
                displayName = "Real cannon footprint (validation only)",
                boundsMinU = new Vector3(-1.09f, -0.61f, 0f), boundsMaxU = new Vector3(1.09f, 0.61f, 1.28f),
                equipment = new EquipmentSpec { equipmentClass = "equipment.deck-gun", footprintU = new Vector3(2.18f, 1.22f, 1.28f) },
            };
            var withCannonMods = new List<string>(mods) { ModularJson.To(cannonDef) };
            var withCannonNames = names != null ? new List<string>(names) { "wide-cannon" } : null;
            var cannonLib = ModuleLibrary.FromJson(stdJson, withCannonMods, withCannonNames);
            Gate("real-cannon-module-loads", cannonLib.Ok, string.Join(" | ", cannonLib.errors));

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
                var cfg = WidePresets.WideLong();
                cfg.equipment.Add(new EquipmentChoice { slotId = $"{section}/{slot}", moduleId = WideCannonId });
                var r = ShipAssembler.Assemble(cfg, cannonLib);
                bool ok = r.ok;
                slotResults.Add($"{section}/{slot}={(ok ? "PASS" : "FAIL:" + Codes(r))}");
                allSlotsPass &= ok;
            }
            Gate("all-10-w2-deck-slots-pass-real-cannon", allSlotsPass, string.Join(", ", slotResults));

            // Negative control: the derived passage actually blocks an
            // intruding cannon (not a no-op). A nudged FIXED slot cannon hits
            // EQUIPMENT_EXCEEDS_CLEARANCE first (the clearance box IS the real
            // footprint, zero margin), so this uses the middle bay's free
            // DeckArea placement, centred on the passage, exactly as the
            // existing W1-r2 "cannon-in-passage-rejected" gate does.
            var intrudeCfg = WidePresets.WideLong();
            intrudeCfg.equipment.Add(new EquipmentChoice { slotId = "middle[0]/DeckArea", moduleId = WideCannonId });
            var intrudeR = ShipAssembler.Assemble(intrudeCfg, cannonLib);
            Gate("real-cannon-in-passage-rejected", !intrudeR.ok && intrudeR.HasCode("EQUIPMENT_BLOCKS_PASSAGE"),
                First(intrudeR, "EQUIPMENT_BLOCKS_PASSAGE") ?? Codes(intrudeR));

            // Documented caveat, proven: the real cannon (2.18 u long) is longer
            // than the 2.0 u pitch between DeckSlot_0 and DeckSlot_1 on the same
            // side, so both cannot be manned at once in one bay (module JSON
            // notes this; only one pair per module is offered in `capacity`).
            var overlapCfg = WidePresets.WideLong();
            overlapCfg.equipment.Add(new EquipmentChoice { slotId = "middle[0]/DeckSlot_0_1", moduleId = WideCannonId });
            overlapCfg.equipment.Add(new EquipmentChoice { slotId = "middle[0]/DeckSlot_1_1", moduleId = WideCannonId });
            var overlapR = ShipAssembler.Assemble(overlapCfg, cannonLib);
            Gate("same-side-adjacent-slots-overlap-with-real-cannon", !overlapR.ok && overlapR.HasCode("EQUIPMENT_OVERLAP"),
                First(overlapR, "EQUIPMENT_OVERLAP") ?? Codes(overlapR));

            // =====================================================================
            // A.5 Capacity + mass, assembly, reshape factors, float and wheel dip.
            // =====================================================================
            var shortR = ShipAssembler.Assemble(WidePresets.WideShort(), lib);
            var longR = ShipAssembler.Assemble(WidePresets.WideLong(), lib);
            Gate("w2-short-and-long-assemble", shortR.ok && longR.ok, $"short: {shortR.Summary()} | long: {longR.Summary()}");

            HullMeasure.TryMeasure(longR, lib, out var longMeasure);
            var refLong = ShipAssembler.Assemble(ShipConfiguration.Long(), lib);
            HullMeasure.TryMeasure(refLong, lib, out var refMeasure);
            float sL = refMeasure.waterlineLengthU > 0 ? longMeasure.waterlineLengthU / refMeasure.waterlineLengthU : 0f;
            float sB = refMeasure.beamU > 0 ? longMeasure.beamU / refMeasure.beamU : 0f;
            float sD = refMeasure.depthU > 0 ? longMeasure.depthU / refMeasure.depthU : 0f;
            Gate("w2-reshape-factors", Near(sL, 1f, 0.01f) && Near(sB, 1.25f, 0.01f) && Near(sD, 1.2255f, 0.01f),
                $"sL={F(sL)} sB={F(sB)} (expect 1.25) sD={F(sD)} (expect (1.76+2.75)/(1.76+1.92)={F((1.76f + 2.75f) / (1.76f + 1.92f))})");

            float LightshipOf(AssemblyResult r) { float m = 0f; foreach (var p in r.placed) if (ModuleKind.IsHull(p.kind) && lib.TryGet(p.moduleId, out var d) && d.lightship != null) m += d.lightship.massKg; return m; }
            int HoldOf(AssemblyResult r) { int h = 0; foreach (var p in r.placed) if (ModuleKind.IsHull(p.kind) && lib.TryGet(p.moduleId, out var d) && d.capacity?.holdCells != null) h += d.capacity.holdCells.value; return h; }
            int BerthsOf(AssemblyResult r) { int b = 0; foreach (var p in r.placed) if (ModuleKind.IsHull(p.kind) && lib.TryGet(p.moduleId, out var d) && d.capacity?.berths != null) b += d.capacity.berths.value; return b; }
            int GunPairsOf(AssemblyResult r) { int g = 0; foreach (var p in r.placed) if (ModuleKind.IsHull(p.kind) && lib.TryGet(p.moduleId, out var d) && d.capacity?.gunSlots?.ids != null && d.capacity.gunSlots.ids.Length > 0) g++; return g; }

            float shortLightship = LightshipOf(shortR), longLightship = LightshipOf(longR);
            int shortHold = HoldOf(shortR), longHold = HoldOf(longR);
            int shortBerths = BerthsOf(shortR), longBerths = BerthsOf(longR);
            int shortGuns = GunPairsOf(shortR) * 2, longGuns = GunPairsOf(longR) * 2;
            Gate("w2-capacity-and-mass-totals", Near(shortLightship, 33681.9f, 5f) && Near(longLightship, 49061.6f, 5f)
                && shortHold == 17 && longHold == 25 && shortBerths == 4 && longBerths == 9 && shortGuns == 4 && longGuns == 6,
                $"short: mass={F(shortLightship)} hold={shortHold} berths={shortBerths} guns={shortGuns} | " +
                $"long: mass={F(longLightship)} hold={longHold} berths={longBerths} guns={longGuns}");

            const float density = 1025f;
            const float cargoUnitKg = 500f, crewKg = 90f, gunKg = 500f;
            float shortFull = shortLightship + shortHold * cargoUnitKg + shortBerths * crewKg + shortGuns * gunKg;
            float longFull = longLightship + longHold * cargoUnitKg + longBerths * crewKg + longGuns * gunKg;

            var shortHydro = AssemblyHydrostatics.For(shortR, lib);
            var longHydro = AssemblyHydrostatics.For(longR, lib);
            bool shortLightOk = shortHydro.SolveWaterline(shortLightship, density, out float shortLightZ, out float shortLightDraft);
            bool shortFullOk = shortHydro.SolveWaterline(shortFull, density, out float shortFullZ, out float shortFullDraft);
            bool longLightOk = longHydro.SolveWaterline(longLightship, density, out float longLightZ, out float longLightDraft);
            bool longFullOk = longHydro.SolveWaterline(longFull, density, out float longFullZ, out float longFullDraft);
            Gate("w2-short-floats-lightship-and-full-load", shortLightOk && shortFullOk
                && shortLightZ <= shortHydro.DeckZU + 1e-4f && shortFullZ <= shortHydro.DeckZU + 1e-4f,
                $"lightship z={F(shortLightZ)} draft={F(shortLightDraft)}m | full z={F(shortFullZ)} draft={F(shortFullDraft)}m | deck={F(shortHydro.DeckZU)} keel={F(shortHydro.KeelZU)}");
            Gate("w2-long-floats-lightship-and-full-load", longLightOk && longFullOk
                && longLightZ <= longHydro.DeckZU + 1e-4f && longFullZ <= longHydro.DeckZU + 1e-4f,
                $"lightship z={F(longLightZ)} draft={F(longLightDraft)}m | full z={F(longFullZ)} draft={F(longFullDraft)}m | deck={F(longHydro.DeckZU)} keel={F(longHydro.KeelZU)}");

            // Wheel-dip finding: wheel bottom = axle Z (0.35) - rotor nominal
            // radius (1.62) = -1.27 (module datum, same on Short and Long: the
            // wheel is on the stern only). Compare to every solved waterline.
            float wheelBottomZ = 0.35f - 1.62f;
            bool wheelDipsShortLight = shortLightOk && shortLightZ > wheelBottomZ;
            bool wheelDipsShortFull = shortFullOk && shortFullZ > wheelBottomZ;
            bool wheelDipsLongLight = longLightOk && longLightZ > wheelBottomZ;
            bool wheelDipsLongFull = longFullOk && longFullZ > wheelBottomZ;
            Gate("w2-wheel-dips-at-lightship-and-full-load", wheelDipsShortLight && wheelDipsShortFull && wheelDipsLongLight && wheelDipsLongFull,
                $"wheel bottom z={F(wheelBottomZ)}; waterline z: short-light={F(shortLightZ)} short-full={F(shortFullZ)} long-light={F(longLightZ)} long-full={F(longFullZ)} (all must be > wheel bottom)");

            // =====================================================================
            // Task C: width-transition module. The schema and assembler ALREADY
            // support this (SocketDef.standard is per-socket, and
            // ShipAssembler only ever compares the two TOUCHING sockets), so no
            // schema or assembler change was needed -- these gates prove it with
            // a SYNTHETIC in-memory transition module, and prove the REAL
            // (placeholder, no-mesh) module committed to Resources is refused by
            // the shipyard policy until Astra's model arrives.
            // =====================================================================
            var synthFull = new ModuleDef
            {
                id = "test.transition.synthetic.full", version = 1, kind = ModuleKind.Middle, status = ModuleStatus.Prototype,
                displayName = "Synthetic W1-r2 -> W2 transition (full data, validation only)",
                lengthU = 6.0f,
                boundsMinU = new Vector3(0f, -6.01f, -2.75f), boundsMaxU = new Vector3(6f, 6.01f, 2.47f),
                sockets = new[]
                {
                    new SocketDef { id = "AftSocket", role = SocketRole.HullAft, standard = "W1-r2", posU = Vector3.zero },
                    new SocketDef { id = "ForwardSocket", role = SocketRole.HullFwd, standard = "W2", posU = new Vector3(6f, 0f, 0f) },
                },
                // Borrows Midship_W1's real hydrostatic table just so the
                // resourcePath/hash resolve and the pipeline has something to
                // load; the NUMBERS are not claimed to describe a real
                // transition hull (Astra's delivery replaces this).
                hydrostatics = new HydrostaticsRef { resourcePath = "ShipModules/Hydrostatics/HullW1r2_v3/Midship_W1",
                    sourceGeometrySha256 = "9cb49be5b4941aacaa8a3751ea71e0f08dc214f6b70ee49342b736a9688eea3f", validWaterlineZU = new[] { -1.92f, 1.76f } },
                lightship = new LightshipSpec { massKg = 10000f, rule = "synthetic, validation only" },
                capacity = new CapacitySpec
                {
                    holdCells = new ProvisionalInt { value = 5, source = "synthetic" },
                    berths = new ProvisionalInt { value = 4, source = "synthetic" },
                    gunSlots = new ProvisionalSlots { ids = new string[0], source = "synthetic" },
                    rule = "synthetic, validation only",
                },
            };
            // Hand-written JSON (not ModuleDef -> ModularJson.To(), whose
            // shim writes a null nested field as a DEFAULT instance rather
            // than omitting it -- that would round-trip `capacity: null`
            // into a non-null-but-empty CapacitySpec and defeat this gate):
            // omitting "capacity"/"hydrostatics"/"lightship" entirely leaves
            // them genuinely null after Bind(), same as the real placeholder
            // module before Astra's delivery.
            string synthBareJson = "{\"schemaVersion\":1,\"id\":\"test.transition.synthetic.bare\",\"version\":1,\"kind\":\"Middle\"," +
                "\"status\":\"prototype\",\"displayName\":\"Synthetic W1-r2 -> W2 transition (no capacity/hydrostatics, validation only)\"," +
                "\"lengthU\":6.0,\"sockets\":[" +
                "{\"id\":\"AftSocket\",\"role\":\"hull.aft\",\"standard\":\"W1-r2\",\"posU\":{\"x\":0,\"y\":0,\"z\":0}}," +
                "{\"id\":\"ForwardSocket\",\"role\":\"hull.fwd\",\"standard\":\"W2\",\"posU\":{\"x\":6,\"y\":0,\"z\":0}}]}";
            var synthReversed = new ModuleDef
            {
                id = "test.transition.synthetic.reversed", version = 1, kind = ModuleKind.Middle, status = ModuleStatus.Prototype,
                displayName = "Synthetic W2 -> W1-r2 transition (validation only)",
                lengthU = 6.0f,
                boundsMinU = new Vector3(0f, -6.01f, -2.75f), boundsMaxU = new Vector3(6f, 6.01f, 2.47f),
                sockets = new[]
                {
                    new SocketDef { id = "AftSocket", role = SocketRole.HullAft, standard = "W2", posU = Vector3.zero },
                    new SocketDef { id = "ForwardSocket", role = SocketRole.HullFwd, standard = "W1-r2", posU = new Vector3(6f, 0f, 0f) },
                },
            };
            var transitionMods = new List<string>(mods) { ModularJson.To(synthFull), synthBareJson, ModularJson.To(synthReversed) };
            var transitionNames = names != null ? new List<string>(names) { "synth-full", "synth-bare", "synth-reversed" } : null;
            var transitionLib = ModuleLibrary.FromJson(stdJson, transitionMods, transitionNames);
            if (readResourceText != null) transitionLib.LoadHydrostatics(readResourceText);
            Gate("transition-synthetics-load", transitionLib.Ok, string.Join(" | ", transitionLib.errors));

            var bridged = new ShipConfiguration { sternId = ShipConfiguration.V3Stern, bowId = WidePresets.WideBow,
                rotorId = ShipConfiguration.ReinforcedRotor, carrierId = ShipConfiguration.M1Carrier };
            bridged.middleIds.Add("test.transition.synthetic.full");
            bridged.middleIds.Add(WidePresets.WideMiddle);
            bridged.fittings.Add(new FittingChoice { socketId = ShipConfiguration.ChimneySocket, moduleId = ShipConfiguration.V3Chimney });
            var bridgedR = ShipAssembler.Assemble(bridged, transitionLib);
            Gate("w1-transition-w2-w2-assembles", bridgedR.ok, bridgedR.ok ? bridgedR.Summary() : bridgedR.Summary());

            var reverseBridged = new ShipConfiguration { sternId = WidePresets.WideStern, bowId = ShipConfiguration.V3Bow,
                rotorId = ShipConfiguration.ReinforcedRotor, carrierId = ShipConfiguration.M1Carrier };
            reverseBridged.middleIds.Add("test.transition.synthetic.reversed");
            reverseBridged.middleIds.Add(ShipConfiguration.V3Middle);
            reverseBridged.fittings.Add(new FittingChoice { socketId = ShipConfiguration.ChimneySocket, moduleId = ShipConfiguration.V3Chimney });
            var reverseBridgedR = ShipAssembler.Assemble(reverseBridged, transitionLib);
            Gate("w2-transition-reversed-w1-assembles", reverseBridgedR.ok, reverseBridgedR.ok ? reverseBridgedR.Summary() : reverseBridgedR.Summary());

            // Wrong orientation: the (W1-r2 -> W2) transition used the OTHER
            // way round -- W2 stern feeding straight into its W1-r2 aft socket
            // -- is refused, same JOIN_PROFILE_MISMATCH rule as any mismatched
            // pair.
            var wrongWay = new ShipConfiguration { sternId = WidePresets.WideStern, bowId = ShipConfiguration.V3Bow,
                rotorId = ShipConfiguration.ReinforcedRotor, carrierId = ShipConfiguration.M1Carrier };
            wrongWay.middleIds.Add("test.transition.synthetic.full"); // aft=W1-r2, but the stern ahead of it is W2
            var wrongWayR = ShipAssembler.Assemble(wrongWay, transitionLib);
            string wrongWayMsg = First(wrongWayR, "JOIN_PROFILE_MISMATCH");
            Gate("transition-wrong-orientation-rejected", !wrongWayR.ok && wrongWayMsg != null, wrongWayMsg ?? Codes(wrongWayR));

            // Capacity/hydrostatics requirements still apply to a transition
            // module like any other hull section (ShipyardPlanner.* already
            // loop over every ModuleKind.IsHull() placed module generically --
            // no special-casing was added or is needed): the BARE synthetic
            // transition (no lightship/capacity/hydrostatics) is flagged
            // missing by name.
            var bareCfg = new ShipConfiguration { sternId = ShipConfiguration.V3Stern, bowId = WidePresets.WideBow,
                rotorId = ShipConfiguration.ReinforcedRotor, carrierId = ShipConfiguration.M1Carrier };
            bareCfg.middleIds.Add("test.transition.synthetic.bare");
            bareCfg.middleIds.Add(WidePresets.WideMiddle);
            var bareR = ShipAssembler.Assemble(bareCfg, transitionLib);
            string massMissing = null, capMissing = null;
            if (bareR.ok)
            {
                ShipyardPlanner.ModuleLightshipKg(bareR, transitionLib, out massMissing);
                ShipyardPlanner.SectionCapacities(bareR, transitionLib, 0f, out capMissing);
            }
            var bareHydro = bareR.ok ? AssemblyHydrostatics.For(bareR, transitionLib) : null;
            Gate("transition-without-data-flags-missing-capacity-and-mass", bareR.ok && massMissing != null && capMissing != null
                && bareHydro != null && !bareHydro.Ok,
                bareR.ok ? $"massMissing='{massMissing}' capMissing='{capMissing}' hydroOk={bareHydro?.Ok} hydroMissing='{bareHydro?.missing}'" : bareR.Summary());

            // The REAL placeholder module committed to Resources (no mesh, no
            // capacity/hydrostatics yet) is refused by the shipyard's prototype
            // policy -- not offered to players until Astra's model arrives.
            var realTransitionCfg = new ShipConfiguration { sternId = ShipConfiguration.V3Stern, bowId = WidePresets.WideBow,
                rotorId = ShipConfiguration.ReinforcedRotor, carrierId = ShipConfiguration.M1Carrier };
            realTransitionCfg.middleIds.Add("hull.transition.w1r2-w2r1.v1");
            realTransitionCfg.middleIds.Add(WidePresets.WideMiddle);
            var policyIssues = ShipyardPolicy.Check(realTransitionCfg, lib);
            bool placeholderRefused = false; string placeholderMsg = null;
            foreach (var iss in policyIssues)
                if (iss.code == "NOT_IN_PROTOTYPE" && iss.message != null && iss.message.Contains("placeholder"))
                { placeholderRefused = true; placeholderMsg = iss.message; break; }
            Gate("real-transition-placeholder-refused-not-in-prototype", placeholderRefused, placeholderMsg ?? "not found among policy issues");
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
