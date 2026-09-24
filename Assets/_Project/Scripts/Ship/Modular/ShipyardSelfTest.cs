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
                    // The MODULE POLICY accepts every length; only equipment
                    // retention may refuse (Short: a gun pair without a slot,
                    // gated separately in short-refused-for-a-gun-pair-without-a-slot).
                    bool policyOk = v.issues.TrueForAll(i => i.code == ShipyardCodes.EquipmentWouldBeLost);
                    allOk &= policyOk;
                    accepted.Add($"{n}x{(rotor == ShipConfiguration.TimberRotor ? "T" : "R")}:{(v.ok ? "ok" : policyOk ? "policy ok, equipment-refused" : v.Summary())}");
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
                && ShipyardPolicy.AllowedModuleIds(ModuleKind.Equipment).Count == 0
                && ShipyardPolicy.AllowedModuleIds(ModuleKind.Stern).Count == 2 && ShipyardPolicy.AllowedModuleIds(ModuleKind.Middle).Count == 2
                && ShipyardPolicy.AllowedModuleIds(ModuleKind.Bow).Count == 2,
                "rotors " + string.Join(",", ShipyardPolicy.AllowedModuleIds(ModuleKind.Rotor))
                + " middles " + string.Join(",", ShipyardPolicy.AllowedModuleIds(ModuleKind.Middle)));

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
            // Field for field, except massKg: that is now the module lightship
            // sum (one mass source), gated separately to today's 31 906.6 kg.
            string longJsonSameMass = null;
            if (longAgain != null)
            {
                var c = longAgain.data.Reshaped(1f, 1f, 1f); c.massKg = reference.massKg;
                longJsonSameMass = ModularJson.To(c);
            }
            Gate("long-is-reference-exactly", longPlan != null && longAgain != null && longAgain.sLength == 1f && longAgain.sBeam == 1f
                && longAgain.sDepth == 1f && longAgain.sternShiftM == 0f && longJsonSameMass == refJson && longAgain.gunIdx == null
                && longAgain.capacity.holdCells == 16 && longAgain.capacity.crewStations == 8 && longAgain.capacity.guns == 6 && longAgain.capacity.gunSlots == 6,
                longAgain != null ? $"s=({longAgain.sLength}, {longAgain.sBeam}, {longAgain.sDepth}) shift {longAgain.sternShiftM} {longAgain.capacity}"
                    + $" measured L {F(longAgain.measure.waterlineLengthU)} u B {F(longAgain.measure.beamU)} u D {F(longAgain.measure.depthU)} u stem={longAgain.measure.stemFound}" : "no plan");

            Gate("long-mass-is-todays", longAgain != null && longAgain.massMissing == null && longAgain.data.massKg == longAgain.lightshipKg
                && Mathf.Abs(longAgain.data.massKg - reference.massKg) < 0.05f && longAgain.data.massKg.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) == "31906.6",
                longAgain != null ? $"sim mass = module lightship sum {longAgain.data.massKg:0.000} kg vs today's {reference.massKg:0.000} kg (delta {longAgain.data.massKg - reference.massKg:0.000} kg)" : "no plan");
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
                // The sailing model outside its keel..deck range: a refusal, never a clamp.
                float simDeckKg = 0f;
                {
                    float deck = float.MaxValue; foreach (var st2 in longPlan.data.stations) deck = Mathf.Min(deck, st2.deckY);
                    simDeckKg = ShipyardPlanner.VolumeTo(longPlan.data, deck) * HullFormData.SeaWaterDensity;
                }
                bool simOver = !ShipyardPlanner.SimStaticDraft(longPlan.data, simDeckKg * 1.01f, out float simNaN) && float.IsNaN(simNaN);
                int cargoToSimDeck = Mathf.CeilToInt((simDeckKg * 1.01f - longPlan.lightshipKg - 8 * 90f - 6 * 500f) / 500f);
                var so = ShipyardPlanner.Validate(ShipConfiguration.Long(), lib, reference,
                    new LiveShipSnapshot { config = ShipConfiguration.Long(), totalHeld = cargoToSimDeck, kindsOnDeck = 1, crewAboard = 8 });
                Gate("sim-out-of-range-is-blocking", simOver && so.HasCode(ShipyardCodes.SimOutOfRange) && float.IsNaN(so.simDraftDraftM),
                    $"sim deck limit {F(simDeckKg / 1000f)} t; {cargoToSimDeck} loads -> {string.Join(", ", so.issues.ConvertAll(x => x.code))}");
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

                // PROVISIONAL: two hydrostatic models, every supported length,
                // ONE mass: the module lightship sum, which is also what the
                // sailing model is weighed with (data.massKg). Timber and
                // reinforced rotors carry no mass of their own, so one row per
                // length covers both. "sim design" = the reshaped form's own
                // design draft, where rho*V of the stretched form would float.
                var rows = new List<string> { "config | mass t | table draft m | sim static draft m | delta m | delta % | sim design draft m" };
                bool allFinite = true, oneMass = true;
                for (int n = 0; n <= 3; n++)
                {
                    var pl = ShipyardPlanner.PlanFor(ShipConfiguration.WithMiddles(n), lib, reference, longPlan, out _);
                    oneMass &= pl.data.massKg == pl.lightshipKg && pl.massMissing == null;
                    bool a = ShipyardPlanner.TableDraft(pl, pl.lightshipKg, out float td);
                    bool b = ShipyardPlanner.SimStaticDraft(pl.data, pl.data.massKg, out float sd);
                    allFinite &= a && b;
                    rows.Add($"{n} bays | {F(pl.data.massKg / 1000f)} | {F(td)} | {F(sd)} | {F(sd - td)} | {(sd - td) / td * 100f:+0.0;-0.0} | {F(pl.data.draft)}");
                }
                Gate("two-hydrostatic-models-table", allFinite && oneMass, (oneMass ? "" : "SIM MASS != MODULE LIGHTSHIP; ") + "\n      " + string.Join("\n      ", rows));
            }

            // ---- authored capacity (A4) ------------------------------------------
            {
                var capRows = new List<string>();
                bool authoredOk = true;
                foreach (var id in new[] { ShipConfiguration.V3Stern, ShipConfiguration.V3Middle, ShipConfiguration.V3Bow })
                {
                    var d = lib.Get(id); var c = d?.capacity;
                    bool ok = c != null && c.holdCells != null && c.berths != null && c.gunSlots?.ids != null
                        && c.holdCells.provisional && c.berths.provisional && c.gunSlots.provisional;
                    authoredOk &= ok;
                    capRows.Add(ok ? $"{id}: hold {c.holdCells.value} berths {c.berths.value} gun slots [{string.Join(", ", c.gunSlots.ids)}]" : id + ": MISSING/not provisional");
                }
                var lsc = longPlan.sections;
                var probs = new List<string>(); int sumHold = 0, sumBerths = 0, sumSlots = 0;
                foreach (var sc in lsc) { probs.AddRange(sc.gunSlotProblems); sumHold += sc.holdCells; sumBerths += sc.berths; sumSlots += sc.gunSlotIds.Count; }
                Gate("capacity-authored-per-module", authoredOk && lsc.Count == 3 && sumHold == 16 && sumBerths == 8 && sumSlots == 6 && probs.Count == 0
                    && longPlan.capacity.holdCells == 16 && longPlan.capacity.crewStations == 8 && longPlan.capacity.guns == 6,
                    string.Join(" | ", capRows) + $" || Long sum: hold {sumHold}, berths {sumBerths}, gun slots {sumSlots}, guns {longPlan.capacity.guns}" + (probs.Count > 0 ? " PROBLEMS " + string.Join("; ", probs) : ""));

                // Where today's guns and berths stand on Long (the seeding rule).
                var where = new List<string>();
                for (int i = 0; i < reference.gunSockets.Length; i++)
                {
                    float z = reference.gunSockets[i].z;
                    var sc = lsc.Find(x => z >= x.aftZ && z < x.fwdZ);
                    where.Add($"gun pair {i + 1} z {F(z)} -> {sc?.sectionKey} (slot pairs at z {string.Join("/", sc?.gunPairZs.ConvertAll(F) ?? new List<string>())})");
                }
                var berthAt = new Dictionary<string, int>();
                foreach (var z in ShipyardPlanner.CrewStationZs(longPlan.data, 8))
                {
                    var sc = lsc.Find(x => z >= x.aftZ && z < x.fwdZ);
                    string k = sc?.sectionKey ?? "?"; berthAt[k] = (berthAt.TryGetValue(k, out int b) ? b : 0) + 1;
                }
                bool seedsMatch = true;
                foreach (var sc in lsc) seedsMatch &= (berthAt.TryGetValue(sc.sectionKey, out int b) ? b : 0) == sc.berths;
                Gate("capacity-seeds-match-todays-deck", seedsMatch,
                    string.Join("; ", where) + " || today's 8 stations: " + string.Join(", ", new List<string>(berthAt.Keys).ConvertAll(k => k + " " + berthAt[k])));

                // Every standard length: the sum over installed sections.
                var cfgRows = new List<string> { "config | hold | berths | gun slots | guns (pairs kept) | mass t | table draft m | sim draft m | weight room t (8 hands; Short 4)" };
                bool sums = true;
                for (int n = 0; n <= 3; n++)
                {
                    var pl = ShipyardPlanner.PlanFor(ShipConfiguration.WithMiddles(n), lib, reference, longPlan, out _);
                    int h = 0, b = 0, g = 0;
                    foreach (var sc in pl.sections) { h += sc.holdCells; b += sc.berths; g += sc.gunSlotIds.Count; }
                    sums &= pl.capacity.holdCells == h && pl.capacity.crewStations == b && pl.capacity.gunSlots == g
                        && pl.capacity.guns <= g && pl.capacity.guns <= b * 1 && pl.capacity.guns <= 2 * reference.gunSockets.Length && pl.capacityMissing == null;
                    ShipyardPlanner.TableDraft(pl, pl.lightshipKg, out float td);
                    ShipyardPlanner.SimStaticDraft(pl.data, pl.data.massKg, out float sd);
                    string kept = pl.gunIdx == null ? "all" : string.Join(",", Array.ConvertAll(pl.gunIdx, x => (x + 1).ToString()));
                    cfgRows.Add($"{n} bays | {pl.capacity.holdCells} | {pl.capacity.crewStations} | {pl.capacity.gunSlots} | {pl.capacity.guns} ({kept}) | {F(pl.data.massKg / 1000f)} | {F(td)} | {F(sd)} | {F(pl.WeightAllowanceKg(n == 0 ? 4 : 8, WeightModel.Default) / 1000f)}");
                }
                Gate("capacity-is-sum-of-sections", sums, "\n      " + string.Join("\n      ", cfgRows));

                var threeP = ShipyardPlanner.PlanFor(ShipConfiguration.WithMiddles(3), lib, reference, longPlan, out _);
                Gate("extra-slots-grant-no-guns", threeP.capacity.gunSlots > longPlan.capacity.gunSlots && threeP.capacity.guns == longPlan.capacity.guns,
                    $"3 bays: {threeP.capacity.gunSlots} gun slots but {threeP.capacity.guns} guns (the hull's 3 pairs); weight room is the shared limit");

                // Synthetic data: a slot in the passage, a free-area slot, an
                // unknown id -- none counts; and berths cap guns.
                string midJson = null; int midAt = -1;
                for (int i = 0; i < mods.Count; i++) if (mods[i].Contains("\"id\": \"hull.middle.w1r2.v3\"")) { midJson = mods[i]; midAt = i; }
                if (midJson != null)
                {
                    string bad = midJson.Replace("\"ids\": [\"DeckSlot_1_-1\", \"DeckSlot_1_1\"]", "\"ids\": [\"DeckSlot_1_-1\", \"DeckSlot_1_1\", \"DeckSlot_0_1\", \"DeckArea\", \"DeckSlot_9\"]")
                        .Replace("\"id\": \"DeckSlot_0_1\",\n            \"socketId\": \"DeckSlot_0_1\",\n            \"clearanceSizeU\": {\"x\": 1.8, \"y\": 1.55,",
                                 "\"id\": \"DeckSlot_0_1\",\n            \"socketId\": \"DeckSlot_0_1\",\n            \"clearanceSizeU\": {\"x\": 1.8, \"y\": 2.2,");
                    var badMods = new List<string>(mods); badMods[midAt] = bad;
                    var badLib = ModuleLibrary.FromJson(stdJson, badMods, names);
                    if (readResourceText != null) badLib.LoadHydrostatics(readResourceText);
                    var bp = ShipyardPlanner.PlanFor(ShipConfiguration.Long(), badLib, reference, null, out _);
                    var mp = bp?.sections.Find(x => x.sectionKey == "middle[0]");
                    Gate("gun-slot-needs-clearance-and-clear-passage", bad != midJson && mp != null && mp.gunSlotIds.Count == 2 && mp.gunSlotProblems.Count == 3
                        && mp.gunSlotProblems.Exists(x => x.Contains("passage")) && bp.capacity.guns == 6,
                        mp != null ? string.Join("; ", mp.gunSlotProblems) : "no plan");

                    string noBerths = midJson.Replace("\"berths\": {\"value\": 4,", "\"berths\": {\"value\": 0,");
                    var nbMods = new List<string>(mods); nbMods[midAt] = noBerths;
                    var nbLib = ModuleLibrary.FromJson(stdJson, nbMods, names);
                    if (readResourceText != null) nbLib.LoadHydrostatics(readResourceText);
                    var np = ShipyardPlanner.PlanFor(ShipConfiguration.Long(), nbLib, reference, null, out _);
                    Gate("guns-need-berths-for-their-crew", noBerths != midJson && np != null && np.capacity.crewStations == 4 && np.capacity.guns == 4
                        && np.gunIdx != null && np.gunIdx.Length == 2 && np.gunIdx[1] == 1 && np.data.gunSockets.Length == 2,
                        np != null ? $"middle berths 0 -> berths {np.capacity.crewStations}, gun slots {np.capacity.gunSlots}, guns {np.capacity.guns} (pairs kept {string.Join(",", Array.ConvertAll(np.gunIdx ?? new int[0], x => (x + 1).ToString()))}; {WeightModel.CrewPerGun} hand per gun)" : "no plan");
                }
                else Gate("gun-slot-needs-clearance-and-clear-passage", false, "middle module JSON not found");

                // Short: 4 slots (stern pair + bow pair) for the hull's 3 pairs.
                var fewHands = new LiveShipSnapshot { config = ShipConfiguration.Long(), totalHeld = 0, kindsOnDeck = 0, crewAboard = 4 };
                var sv = ShipyardPlanner.Validate(ShipConfiguration.Short(), lib, reference, fewHands);
                var srep = ShipyardReport.From(sv, sv.currentPlan, sv.draftPlan, lib.MetresPerUnit);
                Gate("short-refused-for-a-gun-pair-without-a-slot", !sv.ok && sv.gunsStruck.Count == 1 && sv.capacityDraft.guns == 4
                    && srep.blocking.Exists(x => x.code == "EQUIPMENT_WOULD_BE_LOST") && !srep.warnings.Exists(x => x.code == "GUNS_STRUCK"),
                    $"{(sv.ok ? "OK" : sv.Summary())}; struck: {string.Join(", ", sv.gunsStruck.ConvertAll(x => x.label))}; guns {sv.capacityCurrent.guns} -> {sv.capacityDraft.guns}");

                // Cross-check only: the old DERIVED capacity (volume / crew-strip
                // ratios) next to the authored sums. Printed, never gated.
                var diff = new List<string>();
                for (int n = 0; n <= 3; n++)
                {
                    var pl = ShipyardPlanner.PlanFor(ShipConfiguration.WithMiddles(n), lib, reference, longPlan, out _);
                    int dh = Mathf.Max(4, Mathf.FloorToInt(16 * (pl.data.VolumeBelowDeck() / reference.VolumeBelowDeck()) + 1e-4f));
                    int dc = 2 * Mathf.Max(1, Mathf.FloorToInt(4 * (ShipyardPlanner.CrewStrip(pl.data) / ShipyardPlanner.CrewStrip(reference)) + 1e-4f));
                    diff.Add($"{n} bays: hold authored {pl.capacity.holdCells} vs derived {dh} ({pl.capacity.holdCells - dh:+0;-0;0}), berths {pl.capacity.crewStations} vs {dc} ({pl.capacity.crewStations - dc:+0;-0;0})");
                }
                Gate("derived-capacity-cross-check", true, string.Join("; ", diff));
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
            int occGuns = 0, occSlots = 0; foreach (var o in occ) { occGuns += o.guns; occSlots += o.gunSlots; }
            Gate("section-occupancy", occ.Count == 3 && cells == 16 && cargo == 16 && berths == 8 && crewN == 8 && occGuns == 6 && occSlots == 6
                && occ.TrueForAll(o => o.holdCells == lib.Get(o.moduleId).capacity.holdCells.value && o.berths == lib.Get(o.moduleId).capacity.berths.value)
                && mid != null && !mid.canRemove
                && !string.IsNullOrEmpty(mid.reason) && !occ[0].canRemove && mid3 != null && mid3.canRemove,
                string.Join(" | ", occ.ConvertAll(o => $"{o.sectionKey}: hold {o.holdCells} cargo {o.cargoCells} berths {o.berths} crew {o.crew} gun slots {o.gunSlots} guns {o.guns} [{string.Join(", ", o.equipment)}] remove={o.canRemove} {o.reason}"))
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
