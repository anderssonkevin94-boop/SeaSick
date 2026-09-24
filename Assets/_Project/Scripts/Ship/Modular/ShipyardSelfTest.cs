using System;
using System.Collections.Generic;
using SeaSick.Steamer;
using UnityEngine;

namespace SeaSick.Ship.Modular
{
    /// Pure-C# gates for the prototype shipyard (policy, reshaped physics,
    /// capacity, retention, save field). Run as part of ModularShipSelfTest,
    /// headless (tools/modular-selftest.sh) or in the editor.
    public static class ShipyardSelfTest
    {
        /// Mirrors SteamerBootstrap.PlaytestScale (not compiled headlessly).
        /// The gate `reference-matches-bootstrap-scale` in the editor-only
        /// probe checks the two agree.
        public const float PlaytestScale = 0.42f;

        /// ShipSave's shape for the round-trip gates (the real one lives in
        /// Save/, which drags the world in and is not compiled headlessly).
        [Serializable]
        public class ShipSaveShape
        {
            public int rung;
            public List<int> fit = new List<int>();
            public float x, y, z, yaw;
            public int anchor;
            public List<string> crewNames = new List<string>();
            public string modular = "";
        }

        [Serializable]
        class OldShipSaveShape
        {
            public int rung;
            public float x, y, z, yaw;
            public int anchor;
            public List<string> crewNames = new List<string>();
        }

        public delegate void GateFn(string name, bool ok, string detail);

        public static HullFormData ReferenceFrom(string hullFormJson)
        {
            if (string.IsNullOrEmpty(hullFormJson)) return null;
            var d = ModularJson.From<HullFormData>(hullFormJson);
            if (d == null || !d.Validate(out _)) return null;
            d = d.Scaled(PlaytestScale);
            d.rudderArea = 0.018f * d.lwl * d.draft;
            return d;
        }

        static string F(float v) => ShipyardPlanner.F(v);
        static bool Rel(float a, float b, float tol = 1e-5f) => Mathf.Abs(a - b) <= tol * Mathf.Max(1f, Mathf.Abs(b));

        public static void Body(string stdJson, IList<string> mods, IList<string> names, string hullFormJson,
            Func<string, string> readResourceText, GateFn Gate)
        {
            var lib = ModuleLibrary.FromJson(stdJson, mods, names);
            if (readResourceText != null) lib.LoadHydrostatics(readResourceText);
            var reference = ReferenceFrom(hullFormJson);
            Gate("shipyard-reference-hull-loads", reference != null, reference != null
                ? $"lwl {F(reference.lwl)} m, beam {F(reference.beam)}, draft {F(reference.draft)}, {F(reference.massKg / 1000f)} t"
                : "no hullform.json");
            if (reference == null || !lib.Usable) return;
            var empty = new LiveShipSnapshot { config = ShipConfiguration.Long(), totalHeld = 0, kindsOnDeck = 0, crewAboard = 0 };

            // ---- policy ----------------------------------------------------
            var accepted = new List<string>();
            bool allOk = true;
            foreach (var rotor in new[] { ShipConfiguration.TimberRotor, ShipConfiguration.ReinforcedRotor })
                for (int n = 0; n <= 3; n++)
                {
                    var c = ShipConfiguration.WithMiddles(n); c.rotorId = rotor;
                    var v = ShipyardPlanner.Validate(c, lib, reference, empty);
                    allOk &= v.ok;
                    accepted.Add($"{n}x{(rotor == ShipConfiguration.TimberRotor ? "T" : "R")}:{(v.ok ? "ok" : v.Summary())}");
                }
            Gate("policy-accepts-0-3-bays-timber-reinforced", allOk, string.Join(" ", accepted));

            void Rejects(string name, ShipConfiguration c, params string[] codes)
            {
                var v = ShipyardPlanner.Validate(c, lib, reference, empty);
                bool ok = !v.ok && v.plan == null;
                foreach (var code in codes) ok &= v.HasCode(code);
                var got = new List<string>();
                foreach (var i in v.issues) got.Add(i.code);
                Gate(name, ok, string.Join(", ", got) + (v.issues.Count > 0 ? " | " + v.issues[0].message : ""));
            }
            var up = ShipConfiguration.Short();
            up.fittings.Add(new FittingChoice { socketId = "stern/UpperDeckMount", moduleId = "deck.upper.partial.placeholder" });
            Rejects("policy-rejects-upper-deck", up, ShipyardCodes.NotInPrototype);
            var w2 = ShipConfiguration.Short(); w2.middleIds.Add("hull.middle.w2broad.placeholder");
            Rejects("policy-rejects-w2", w2, ShipyardCodes.NotInPrototype);
            var big = ShipConfiguration.Short(); big.rotorId = ShipConfiguration.OversizedRotor;
            Rejects("policy-rejects-oversized-wheel", big, ShipyardCodes.NotInPrototype, "WHEEL_MOUNT_MISMATCH");
            var gun = ShipConfiguration.Long();
            gun.equipment.Add(new EquipmentChoice { slotId = "middle[0]/DeckSlot_0_1", moduleId = "equipment.cannon.placeholder" });
            Rejects("policy-rejects-equipment", gun, ShipyardCodes.NotInPrototype);
            var v1 = ShipConfiguration.Short(); v1.middleIds.Add("hull.middle.w1.v1");
            Rejects("policy-rejects-incompatible-reference", v1, ShipyardCodes.NotInPrototype, "JOIN_PROFILE_MISMATCH");
            Rejects("policy-rejects-four-bays", ShipConfiguration.WithMiddles(4), "TOO_MANY_MIDDLES");
            var noWheel = ShipConfiguration.Long(); noWheel.rotorId = ""; noWheel.carrierId = "";
            Rejects("policy-rejects-no-wheel", noWheel, ShipyardCodes.WheelRequired);
            var unknown = ShipConfiguration.Long(); unknown.middleIds[0] = "hull.middle.future.v9";
            Rejects("policy-rejects-unknown-id", unknown, "UNKNOWN_MODULE");
            Gate("allowed-ids-for-pickers",
                ShipyardPolicy.AllowedModuleIds(ModuleKind.Rotor).Count == 2 && ShipyardPolicy.AllowedModuleIds(ModuleKind.UpperDeck).Count == 0
                && ShipyardPolicy.AllowedModuleIds(ModuleKind.Equipment).Count == 0 && ShipyardPolicy.AllowedModuleIds(ModuleKind.Middle).Count == 1,
                "rotors " + string.Join(",", ShipyardPolicy.AllowedModuleIds(ModuleKind.Rotor)));

            // ---- reshape ---------------------------------------------------
            string refJson = ModularJson.To(reference);
            var same = reference.Reshaped(1f, 1f, 1f);
            Gate("reshaped-1-1-1-identical", ModularJson.To(same) == refJson && !ReferenceEquals(same.stations, reference.stations),
                $"{refJson.Length} chars of JSON compared (deep copy)");

            float sL = 1.5f, sB = 1.2f, sD = 0.8f;
            var r = reference.Reshaped(sL, sB, sD);
            var st = 3; var lv = 5;
            var checks = new List<(string, bool)>
            {
                ("lwl*sL", Rel(r.lwl, reference.lwl * sL)), ("loa*sL", Rel(r.loa, reference.loa * sL)),
                ("beam*sB", Rel(r.beam, reference.beam * sB)), ("draft*sD", Rel(r.draft, reference.draft * sD)),
                ("depth*sD", Rel(r.depth, reference.depth * sD)),
                ("volume*LBD", Rel(r.volume, reference.volume * sL * sB * sD)), ("mass*LBD", Rel(r.massKg, reference.massKg * sL * sB * sD)),
                ("waterplane*LB", Rel(r.waterplane, reference.waterplane * sL * sB)),
                ("bm*B2/D", Rel(r.bm, reference.bm * sB * sB / sD)), ("kb*D", Rel(r.kb, reference.kb * sD)), ("kg*D", Rel(r.kg, reference.kg * sD)),
                ("gm=def", Rel(r.gm - (r.kb + r.bm - r.kg), reference.gm - (reference.kb + reference.bm - reference.kg), 1e-4f)),
                ("lcb*L", Rel(r.lcbZ, reference.lcbZ * sL)),
                ("kRoll*B", Rel(r.gyradiusRoll, reference.gyradiusRoll * sB)), ("kPitch*L", Rel(r.gyradiusPitch, reference.gyradiusPitch * sL)),
                ("kYaw*L", Rel(r.gyradiusYaw, reference.gyradiusYaw * sL)),
                ("station.z*L", Rel(r.stations[st].z, reference.stations[st].z * sL)), ("station.dz*L", Rel(r.stations[st].dz, reference.stations[st].dz * sL)),
                ("halfBreadth*B", Rel(r.stations[st].halfBreadth[lv], reference.stations[st].halfBreadth[lv] * sB)),
                ("area*BD", Rel(r.stations[st].area[lv], reference.stations[st].area[lv] * sB * sD)),
                ("momentY*BD2", Rel(r.stations[st].momentY[lv], reference.stations[st].momentY[lv] * sB * sD * sD)),
                ("y*D", Rel(r.stations[st].y[lv], reference.stations[st].y[lv] * sD)),
                ("axle.y same", r.wheelAxle.y == reference.wheelAxle.y), ("axle.z*L", Rel(r.wheelAxle.z, reference.wheelAxle.z * sL)),
                ("wheelRadius same", r.wheelRadius == reference.wheelRadius), ("wheelDip same", r.wheelDesignDip == reference.wheelDesignDip),
                ("rudderArea same", r.rudderArea == reference.rudderArea),
                ("gun*(B,D,L)", Rel(r.gunSockets[0].x, reference.gunSockets[0].x * sB) && Rel(r.gunSockets[0].z, reference.gunSockets[0].z * sL)),
                ("source untouched", ModularJson.To(reference) == refJson),
            };
            // The integrated volume from the reshaped tables must match the
            // reshaped book volume (the tables and the book stay consistent).
            reference.Rederive(out float v0, out _, out _, out _, out _, out float iL0);
            r.Rederive(out float v1r, out _, out _, out _, out _, out float iL1);
            checks.Add(("tables volume*LBD", Rel(v1r, v0 * sL * sB * sD, 1e-3f)));
            checks.Add(("inertiaL*L3B", Rel(iL1, iL0 * sL * sL * sL * sB, 1e-3f)));
            var badRules = new List<string>();
            foreach (var (n, ok) in checks) if (!ok) badRules.Add(n);
            Gate("reshaped-rules", badRules.Count == 0, badRules.Count == 0 ? $"{checks.Count} field rules hold at (1.5, 1.2, 0.8)" : "broken: " + string.Join(", ", badRules));

            // ---- plans: Long is the reference, exactly ---------------------
            var longPlan = ShipyardPlanner.PlanFor(ShipConfiguration.Long(), lib, reference, null, out _);
            var longAgain = ShipyardPlanner.PlanFor(ShipConfiguration.Long(), lib, reference, longPlan, out _);
            Gate("long-is-reference-exactly", longPlan != null && longAgain != null && longAgain.sLength == 1f && longAgain.sBeam == 1f
                && longAgain.sDepth == 1f && longAgain.sternShiftM == 0f && ModularJson.To(longAgain.data) == refJson
                && longAgain.capacity.holdCells == 16 && longAgain.capacity.crewStations == 8,
                longAgain != null ? $"s=({longAgain.sLength}, {longAgain.sBeam}, {longAgain.sDepth}) shift {longAgain.sternShiftM} {longAgain.capacity}"
                    + $" measured L {F(longAgain.measure.waterlineLengthU)} u B {F(longAgain.measure.beamU)} u D {F(longAgain.measure.depthU)} u stem={longAgain.measure.stemFound}" : "no plan");

            var shortPlan = ShipyardPlanner.PlanFor(ShipConfiguration.Short(), lib, reference, longPlan, out _);
            var threePlan = ShipyardPlanner.PlanFor(ShipConfiguration.WithMiddles(3), lib, reference, longPlan, out _);
            string Line(string n, ShipyardPlan p) => p == null ? n + " none" :
                $"{n}: sL {F(p.sLength)} lwl {F(p.data.lwl)} m mass {F(p.data.massKg / 1000f)} t axle z {F(p.data.wheelAxle.z)} " +
                $"(drawn {F(p.viewOffset.z + p.assembly.wheelAxleM.z)}) helm z {F(p.data.helm.z)} view z {F(p.viewOffset.z)} {p.capacity}";
            Gate("short-long-three-plans", shortPlan != null && threePlan != null
                && shortPlan.data.lwl < longPlan.data.lwl && threePlan.data.lwl > longPlan.data.lwl
                && shortPlan.capacity.holdCells < 16 && threePlan.capacity.holdCells > 16
                && shortPlan.sBeam == 1f && shortPlan.sDepth == 1f && threePlan.sBeam == 1f && threePlan.sDepth == 1f
                && Mathf.Abs(shortPlan.viewOffset.z + shortPlan.assembly.wheelAxleM.z - shortPlan.data.wheelAxle.z) < 1e-4f
                && shortPlan.data.wheelAxle.y == reference.wheelAxle.y && threePlan.data.wheelRadius == reference.wheelRadius,
                Line("short", shortPlan) + " || " + Line("long", longAgain) + " || " + Line("3-bay", threePlan));
            Gate("deck-height-mismatch-reported", longPlan != null,
                $"drawn deck 1.76 u x {F(lib.MetresPerUnit)} = {F(1.76f * lib.MetresPerUnit)} m above the waterline; physics deck (depth - draft) {F(reference.depth - reference.draft)} m; " +
                $"drawn axle y {F(longPlan.assembly.wheelAxleM.y)} vs physics {F(reference.wheelAxle.y)}");

            // ---- hydrostatics from Astra's module tables (H2/H3/H6) -------------
            var hs = lib.Hydrostatics(ShipConfiguration.V3Stern); var hm = lib.Hydrostatics(ShipConfiguration.V3Middle); var hb = lib.Hydrostatics(ShipConfiguration.V3Bow);
            Gate("hydro-tables-load", hs != null && hm != null && hb != null && lib.hydrostaticsErrors.Count == 0,
                hs != null && hm != null && hb != null ? $"stern {hs.waterlineZU.Length} levels, middle {hm.waterlineZU.Length}, bow {hb.waterlineZU.Length}"
                    : "errors: " + string.Join(" | ", new List<string>(lib.hydrostaticsErrors.Values)));
            if (hs != null && hm != null && hb != null && longPlan.hydro.Ok)
            {
                Gate("prow-never-counted", hb.hullXRangeU[1] < 9.8f && hb.hullXRangeU[1] > 9.7f,
                    $"bow table shell to X {F(hb.hullXRangeU[1])} u (prow socket 10.98)");
                float sumDeck = (hs.integratedVolumeU3[hs.integratedVolumeU3.Length - 1] + hm.integratedVolumeU3[hm.integratedVolumeU3.Length - 1]
                    + hb.integratedVolumeU3[hb.integratedVolumeU3.Length - 1]) * 0.125f;
                Gate("long-volume-to-deck-is-sum-of-tables", Rel(longPlan.hydro.DeckVolumeM3, sumDeck, 1e-4f),
                    $"{F(longPlan.hydro.DeckVolumeM3)} m3 = sum of integratedVolumeU3[last] x 0.125 = {F(sumDeck)} m3");
                bool mono = true; float prev = -1f; var drafts = new List<string>();
                for (float m = 5000f; m <= 75000f; m += 10000f)
                {
                    bool okd = longPlan.hydro.SolveWaterline(m, HullFormData.SeaWaterDensity, out _, out float dr);
                    mono &= okd && dr > prev; prev = dr; drafts.Add($"{m / 1000f:0}t:{F(dr)}");
                }
                Gate("draft-solve-monotonic", mono, string.Join(" ", drafts));
                bool over = !longPlan.hydro.SolveWaterline(longPlan.hydro.DeckVolumeM3 * HullFormData.SeaWaterDensity * 1.01f, HullFormData.SeaWaterDensity, out _, out _);
                var ov = ShipyardPlanner.Validate(ShipConfiguration.Long(), lib, reference,
                    new LiveShipSnapshot { config = ShipConfiguration.Long(), totalHeld = 100, kindsOnDeck = 1, crewAboard = 8 });
                Gate("overload-is-blocking", over && ov.HasCode(ShipyardCodes.Overloaded), ov.issues.Find(i => i.code == ShipyardCodes.Overloaded)?.message ?? "no OVERLOADED");
                longPlan.hydro.SolveWaterline(0f, HullFormData.SeaWaterDensity, out _, out float d0);
                Gate("below-keel-is-zero", hm.VolumeAt(-5f) == 0f && hm.VolumeAt(hm.KeelZU) == 0f && d0 == 0f && float.IsNaN(hm.VolumeAt(2.5f)),
                    "volume 0 at/below the keel, NaN (never clamped) above the deck line");

                // H3: the sailing model vs the tables, for today's ship.
                longPlan.hydro.SolveWaterline(reference.massKg, HullFormData.SeaWaterDensity, out float zT, out float draftT);
                float zPhys = longPlan.hydro.KeelZU + reference.draft / lib.MetresPerUnit;
                float volAtPhys = longPlan.hydro.VolumeM3At(zPhys);
                float dDraft = (draftT - reference.draft) / reference.draft, dVol = (volAtPhys - reference.volume) / reference.volume;
                Gate("cross-check-sailing-model-vs-tables", !float.IsNaN(draftT) && zT < longPlan.hydro.DeckZU,
                    $"today's {F(reference.massKg / 1000f)} t floats at {F(draftT)} m on the tables vs {F(reference.draft)} m in the sailing model ({dDraft * 100f:+0.0;-0.0}%); "
                    + $"table volume at the model's draft {F(volAtPhys)} m3 vs model {F(reference.volume)} m3 ({dVol * 100f:+0.0;-0.0}%); "
                    + $"table volume to the deck {F(longPlan.hydro.DeckVolumeM3)} m3; the drawing's datum (Z 0) sits {F((zT - 0f) * lib.MetresPerUnit)} m from the table waterline"
                    + (Mathf.Abs(dDraft) > 0.15f || Mathf.Abs(dVol) > 0.15f ? " -- DISAGREE by more than 15%" : " -- agree within 15%"));
                Gate("lightship-seeds-sum-to-today", Mathf.Abs(longPlan.lightshipKg - reference.massKg) < 1f,
                    $"stern+middle+bow lightship {F(longPlan.lightshipKg)} kg vs today's {F(reference.massKg)} kg");
                float perM = longPlan.lightshipKg / (longPlan.measure.waterlineLengthU * lib.MetresPerUnit);
                var eq = new List<string>();
                foreach (var (n, pl) in new[] { ("short", shortPlan), ("long", longPlan), ("3-bay", threePlan) })
                {
                    float mass = perM * pl.measure.waterlineLengthU * lib.MetresPerUnit;
                    pl.hydro.SolveWaterline(mass, HullFormData.SeaWaterDensity, out _, out float dr);
                    eq.Add($"{n} {F(mass / 1000f)} t -> {F(dr)} m (module lightship {F(pl.lightshipKg / 1000f)} t -> {(ShipyardPlanner.TableDraft(pl, pl.lightshipKg, out float dl) ? F(dl) : "over")} m)");
                }
                Gate("drafts-at-equal-lightship-per-length", true, string.Join("; ", eq));

                // PROVISIONAL: two hydrostatic models, every supported length.
                // Timber and reinforced rotors carry no mass of their own, so
                // one row per length covers both.
                var rows = new List<string> { "config | lightship t | table draft m | sim static draft m | delta m | delta % | sim own mass t -> sim draft m" };
                bool allFinite = true;
                for (int n = 0; n <= 3; n++)
                {
                    var pl = ShipyardPlanner.PlanFor(ShipConfiguration.WithMiddles(n), lib, reference, longPlan, out _);
                    bool a = ShipyardPlanner.TableDraft(pl, pl.lightshipKg, out float td);
                    bool b = ShipyardPlanner.SimStaticDraft(pl.data, pl.lightshipKg, out float sd);
                    bool c = ShipyardPlanner.SimStaticDraft(pl.data, pl.data.massKg, out float own);
                    allFinite &= a && b && c;
                    rows.Add($"{n} bays | {F(pl.lightshipKg / 1000f)} | {F(td)} | {F(sd)} | {F(sd - td)} | {(sd - td) / td * 100f:+0.0;-0.0} | {F(pl.data.massKg / 1000f)} -> {F(own)}");
                }
                Gate("two-hydrostatic-models-table", allFinite, "\n      " + string.Join("\n      ", rows));
            }

            // ---- retention -----------------------------------------------------
            var loaded = new LiveShipSnapshot { config = ShipConfiguration.Long(), totalHeld = 16, kindsOnDeck = 4, crewAboard = 8 };
            var toLong = ShipyardPlanner.Validate(ShipConfiguration.Long(), lib, reference, loaded);
            Gate("todays-full-ship-fits-long", toLong.ok, toLong.ok ? toLong.Summary() : toLong.Summary());
            var toShort = ShipyardPlanner.Validate(ShipConfiguration.Short(), lib, reference, loaded);
            Gate("cargo-would-not-fit", !toShort.ok && toShort.HasCode(ShipyardCodes.CargoWouldNotFit), toShort.Summary());
            Gate("crew-would-not-fit", !toShort.ok && toShort.HasCode(ShipyardCodes.CrewWouldNotFit), $"crew 8 vs short {toShort.capacityDraft.crewStations} stations");
            var noChimney = ShipConfiguration.Long(); noChimney.fittings.Clear();
            var nc = ShipyardPlanner.Validate(noChimney, lib, reference, empty);
            Gate("funnel-would-be-lost", !nc.ok && nc.HasCode(ShipyardCodes.EquipmentWouldBeLost), nc.Summary());
            // Synthetic: a draft whose second gun socket stands outboard of its
            // planking (no current hull does this -- sockets stretch with her).
            var synth = ShipyardPlanner.PlanFor(ShipConfiguration.Long(), lib, reference, longPlan, out _);
            synth.data = synth.data.Reshaped(1f, 1f, 1f);
            synth.data.gunSockets[1] = new Vector3(synth.data.beam, synth.data.gunSockets[1].y, synth.data.gunSockets[1].z);
            var lost = ShipyardPlanner.EquipmentLost(new LiveShipSnapshot { hasChimney = true }, longPlan, synth);
            Gate("gun-would-be-lost-synthetic", lost.Count == 1 && lost[0].id == "gun1", lost.Count > 0 ? lost[0].label : "nothing lost");
            var pileSynth = new ShipyardPlan { assembly = longPlan.assembly, data = longPlan.data,
                deckLoad = new DeckLoadPlan { abreast = 2, rows = new[] { new Vector3(0f, 0f, 60f), new Vector3(0f, 0f, 62f), new Vector3(0f, 0f, 64f) } } };
            var lostPiles = ShipyardPlanner.EquipmentLost(new LiveShipSnapshot { hasChimney = true, kindsOnDeck = 3 }, longPlan, pileSynth);
            Gate("deck-load-would-be-lost-synthetic", lostPiles.Count == 3 && lostPiles[0].id == "deckload0", $"{lostPiles.Count} piles");
            bool longPilesOnDeck = true;
            for (int i = 0; i < 6; i++) longPilesOnDeck &= ShipyardPlanner.OnDeck(longPlan.data, longPlan.deckLoad.Slot(i), 0.2f);
            Gate("long-deck-load-six-piles-on-deck", longPilesOnDeck, $"rows z {F(longPlan.deckLoad.rows[0].z)} / {F(longPlan.deckLoad.rows[1].z)} / {F(longPlan.deckLoad.rows[2].z)}, {longPlan.deckLoad.abreast} abreast");

            // ---- weight (A2) ---------------------------------------------------------
            var wm = WeightModel.Default;
            float longRoom = longPlan.WeightAllowanceKg(8, wm);
            Gate("long-weight-allowance-is-todays-full-hold", Mathf.Abs(longRoom - 16 * wm.cargoUnitKg) < 5f,
                $"load line {F(longPlan.loadLineMarginU * lib.MetresPerUnit)} m below the deck line; lightship {F(longPlan.lightshipKg / 1000f)} t, "
                + $"at load line {F(longPlan.loadDisplacementKg / 1000f)} t; cargo room with 8 hands {F(longRoom / 1000f)} t");
            float shortRoom = shortPlan.WeightAllowanceKg(4, wm), threeRoom = threePlan.WeightAllowanceKg(8, wm);
            var heavy = new LiveShipSnapshot { config = ShipConfiguration.Long(), totalHeld = shortPlan.capacity.holdCells, kindsOnDeck = 1, crewAboard = 4 };
            var hv = ShipyardPlanner.Validate(ShipConfiguration.Short(), lib, reference, heavy);
            bool weightBinds = shortPlan.capacity.holdCells * wm.cargoUnitKg > shortRoom;
            Gate("weight-and-volume-are-separate-limits", weightBinds == hv.HasCode(ShipyardCodes.CargoWouldNotFit)
                && (!weightBinds || hv.issues.Exists(i => i.partId == "weight")),
                $"short: volume {shortPlan.capacity.holdCells} cells = {F(shortPlan.capacity.holdCells * wm.cargoUnitKg / 1000f)} t vs weight room {F(shortRoom / 1000f)} t with 4 hands "
                + $"-> {(weightBinds ? "weight binds first: " + hv.Summary() : "volume binds first")}; 3-bay room with 8 hands {F(threeRoom / 1000f)} t for {threePlan.capacity.holdCells} cells");
            var rep = ShipyardReport.From(toShort, toShort.currentPlan, toShort.draftPlan, lib.MetresPerUnit);
            var hl = rep.Figure("holdCells"); var wa = rep.Figure("weightAllowance");
            Gate("report-current-vs-proposed", rep.figures.Count >= 14 && hl != null && hl.current == 16 && hl.proposed == shortPlan.capacity.holdCells
                && hl.provisional && wa != null && wa.available && !rep.ok && rep.blocking.Count > 0,
                string.Join("; ", rep.figures.ConvertAll(f => f.ToString())));

            // ---- per-section occupancy (removalBlocker) ------------------------------
            var occ = ShipyardPlanner.Validate(ShipConfiguration.Long(), lib, reference, loaded).sections;
            int cells = 0, cargo = 0, berths = 0, crewN = 0;
            foreach (var o in occ) { cells += o.holdCells; cargo += o.cargoCells; berths += o.berths; crewN += o.crew; }
            var mid = occ.Find(o => o.sectionKey == "middle[0]");
            var light = new LiveShipSnapshot { config = ShipConfiguration.WithMiddles(3), totalHeld = 2, kindsOnDeck = 1, crewAboard = 4 };
            var occ3 = ShipyardPlanner.Validate(ShipConfiguration.WithMiddles(3), lib, reference, light).sections;
            var mid3 = occ3.Find(o => o.sectionKey == "middle[1]");
            Gate("section-occupancy", occ.Count == 3 && cells == 16 && cargo == 16 && berths == 8 && crewN == 8 && mid != null && !mid.canRemove
                && !string.IsNullOrEmpty(mid.reason) && !occ[0].canRemove && mid3 != null && mid3.canRemove,
                string.Join(" | ", occ.ConvertAll(o => $"{o.sectionKey}: hold {o.holdCells} cargo {o.cargoCells} berths {o.berths} crew {o.crew} [{string.Join(", ", o.equipment)}] remove={o.canRemove} {o.reason}"))
                + $" || 3-bay light: middle[1] remove={mid3?.canRemove}");

            // ---- purity ------------------------------------------------------------
            var draft = ShipConfiguration.WithMiddles(2); string draftJson = draft.ToJson();
            var snap = new LiveShipSnapshot { config = ShipConfiguration.Long(), totalHeld = 5, kindsOnDeck = 2, crewAboard = 6 };
            ShipyardPlanner.Validate(draft, lib, reference, snap);
            ShipyardPlanner.Validate(draft, lib, reference, snap);
            Gate("validate-is-side-effect-free", draft.ToJson() == draftJson && ModularJson.To(reference) == refJson
                && snap.totalHeld == 5 && snap.crewAboard == 6 && snap.config.ValueEquals(ShipConfiguration.Long()),
                "draft, reference hull and snapshot unchanged after two validations");

            // ---- save field --------------------------------------------------------
            var save = new ShipSaveShape { rung = 3, x = 1, crewNames = new List<string> { "Pip" } };
            var three = ShipConfiguration.WithMiddles(3); three.rotorId = ShipConfiguration.TimberRotor;
            save.modular = ModularSave.Encode(three);
            string saveJson = ModularJson.To(save);
            var back = ModularJson.From<ShipSaveShape>(saveJson);
            var cfgBack = ModularSave.Decode(back.modular, lib, out bool has, out string warn);
            Gate("save-field-round-trip", has && warn == null && cfgBack.ValueEquals(three), saveJson.Length + " chars");

            string oldJson = ModularJson.To(new OldShipSaveShape { rung = 2, crewNames = new List<string> { "Bo" } });
            var old = ModularJson.From<ShipSaveShape>(oldJson);
            var oldCfg = ModularSave.Decode(old.modular, lib, out bool oldHas, out string oldWarn);
            Gate("old-save-without-field-is-long", old.modular == "" && !oldHas && oldWarn == null && oldCfg.ValueEquals(ShipConfiguration.Long()),
                oldJson);

            var future = ShipConfiguration.Long(); future.middleIds[0] = "hull.middle.w1r2.v9";
            var fut = new ShipSaveShape { modular = ModularSave.Encode(future) };
            var futBack = ModularJson.From<ShipSaveShape>(ModularJson.To(fut));
            var futCfg = ModularSave.Decode(futBack.modular, lib, out bool futHas, out string futWarn);
            Gate("save-unknown-module-falls-back-with-warning", futHas && futWarn != null && futCfg.ValueEquals(ShipConfiguration.Long()), futWarn ?? "no warning");
            var junk = ModularSave.Decode("{not json", lib, out bool junkHas, out string junkWarn);
            Gate("save-unreadable-field-falls-back", junkHas && junkWarn != null && junk.ValueEquals(ShipConfiguration.Long()), junkWarn ?? "no warning");
        }
    }
}
