using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace SeaSick.Ship.Modular
{
    /// Self-test gates for the upper-deck layers (UpperDeckLayers, 2026-09-27):
    /// Astra's third deck on connected raised sections and the gun foredeck on
    /// the V3 bow -- assembly, sockets, capacity, physics, warnings and the
    /// draft edits. Pure; run headlessly by tools/modular-selftest.sh.
    public static class UpperDeckValidation
    {
        public delegate void GateFn(string name, bool ok, string detail);

        static string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);

        static ShipConfiguration Bare(string sternId, IList<string> middleIds, string bowId)
        {
            var c = new ShipConfiguration { sternId = sternId, bowId = bowId,
                rotorId = ShipConfiguration.ReinforcedRotor, carrierId = ShipConfiguration.M1Carrier };
            if (middleIds != null) c.middleIds.AddRange(middleIds);
            c.fittings.Add(new FittingChoice { socketId = ShipConfiguration.ChimneySocket, moduleId = ShipConfiguration.V3Chimney });
            return c;
        }

        static string Codes(AssemblyResult r)
        {
            var s = new List<string>();
            foreach (var x in r.rejections) s.Add(x.code);
            return s.Count == 0 ? "ok" : string.Join(",", s);
        }

        static ShipConfiguration Fit(ShipConfiguration c, string socket, string module)
        {
            var n = c.Clone();
            n.fittings.Add(new FittingChoice { socketId = socket, moduleId = module });
            return n;
        }

        static ShipConfiguration FullThird(ShipConfiguration raised)
        {
            var c = Fit(raised, "stern/ThirdDeckMount", UpperDeckLayers.ThirdStern);
            for (int i = 0; i < c.middleIds.Count; i++) c = Fit(c, $"middle[{i}]/ThirdDeckMount", UpperDeckLayers.ThirdMiddle);
            return Fit(c, "bow/ThirdDeckMount", UpperDeckLayers.ThirdBow);
        }

        public static void Body(string stdJson, IList<string> mods, IList<string> names, string hullFormJson, GateFn Gate)
        {
            var lib = ModuleLibrary.FromJson(stdJson, mods, names);
            if (!lib.Usable) { Gate("upper-deck-library-usable", false, string.Join(" | ", lib.errors)); return; }
            float k = lib.MetresPerUnit;

            // ---- data ----------------------------------------------------
            var ids = new[] { UpperDeckLayers.ThirdStern, UpperDeckLayers.ThirdMiddle, UpperDeckLayers.ThirdBow, UpperDeckLayers.Foredeck };
            var badIds = new List<string>();
            foreach (var id in ids)
                if (!lib.TryGet(id, out var d) || d.kind != ModuleKind.UpperDeck || d.fitting == null || d.lightship == null
                    || d.upperStructure == null || d.capacity?.holdCells == null || string.IsNullOrEmpty(d.fitting.unreviewedNote)
                    || d.visuals == null || d.visuals.Length == 0)
                    badIds.Add(id);
            Gate("upper-deck-modules-load", badIds.Count == 0, badIds.Count == 0 ? "4 UpperDeck modules with fitting/lightship/upperStructure/capacity/visuals" : "bad: " + string.Join(", ", badIds));

            var sockBad = new List<string>();
            foreach (var (hull, cls) in new[] { (RaisedPresets.RaisedStern, "deck.third.w1xr.stern"), (RaisedPresets.RaisedMiddle, "deck.third.w1xr.middle"),
                         (RaisedPresets.RaisedBow, "deck.third.w1xr.bow"), (ShipConfiguration.V3Bow, "deck.foredeck.w1r2") })
            {
                if (!lib.TryGet(hull, out var hd)) { sockBad.Add(hull + " missing"); continue; }
                string opt = UpperDeckLayers.OptionFor(hd, lib, out var local);
                if (opt == null || !lib.TryGet(opt, out var od) || od.fitting.socketClass != cls) sockBad.Add($"{hull}->{opt}");
            }
            foreach (var noLayer in new[] { RaisedSections.SternRaisedWallFwd, RaisedSections.MiddleRaisedWallAft, RaisedSections.MiddleRaisedWallBoth,
                         RaisedSections.BowRaisedWallAft, ExpandedPresets.ExpandedBow, ShipConfiguration.V3Middle })
                if (lib.TryGet(noLayer, out var nd) && UpperDeckLayers.OptionFor(nd, lib, out _) != null) sockBad.Add(noLayer + " offers a layer");
            Gate("upper-deck-sockets-only-where-art-fits", sockBad.Count == 0,
                sockBad.Count == 0 ? "connected W1xR stern/middle/bow -> third deck, V3 bow -> foredeck; wall/low variants none" : string.Join(" | ", sockBad));

            // ---- third deck: assembly -----------------------------------
            var raisedLong = RaisedPresets.RaisedLong();
            var third = FullThird(raisedLong);
            var a3 = ShipAssembler.Assemble(third, lib);
            var mid = a3.ok ? a3.Find("fitting:middle[0]/ThirdDeckMount") : null;
            Gate("third-deck-full-assembles", a3.ok && mid != null && Mathf.Abs(mid.positionU.z - 4.2f) < 1e-3f,
                a3.ok ? $"{a3.placed.Count} modules, middle layer origin {mid?.positionU}" : Codes(a3));
            var ch = a3.ok ? a3.Find("fitting:" + ShipConfiguration.ChimneySocket) : null;
            Gate("third-deck-lifts-chimney-to-6.64", ch != null && Mathf.Abs(ch.positionU.z - 6.64f) < 1e-3f && Mathf.Abs(ch.positionU.x - 12.3f) < 1e-3f,
                ch != null ? $"chimney at ({F(ch.positionU.x)}, {F(ch.positionU.z)}) u, manifest (12.3, 6.64)" : "no chimney");
            bool hidden = false, keptShell = false;
            var midHull = a3.ok ? a3.Find("middle[0]") : null;
            if (midHull != null)
            {
                hidden = true;
                foreach (var v in midHull.visuals) { if (v.id == "RaisedMiddle__UpperRails") hidden = false; if (v.id == "Midship_W1__Hull_Shell") keptShell = true; }
            }
            Gate("third-deck-hides-covered-host-visuals", hidden && keptShell, $"middle[0] visuals {midHull?.visuals.Length}, rails hidden {hidden}, shell kept {keptShell}");
            var twoBay = FullThird(RaisedPresets.RaisedTwoBay());
            var a32 = ShipAssembler.Assemble(twoBay, lib);
            Gate("third-deck-two-bays-assembles", a32.ok, Codes(a32));

            var onWall = Fit(Bare(RaisedSections.SternRaisedWallFwd, new[] { RaisedSections.MiddleLow }, RaisedSections.BowLow), "stern/ThirdDeckMount", UpperDeckLayers.ThirdStern);
            var aw = ShipAssembler.Assemble(onWall, lib);
            Gate("third-deck-refused-on-wall-variant", !aw.ok && aw.HasCode("FITTING_SOCKET_UNKNOWN"), Codes(aw));
            var wrong = Fit(raisedLong, "stern/ThirdDeckMount", UpperDeckLayers.ThirdBow);
            var awr = ShipAssembler.Assemble(wrong, lib);
            Gate("third-deck-wrong-piece-refused", !awr.ok && awr.HasCode("FITTING_CLASS_MISMATCH"), Codes(awr));
            var twice = Fit(Fit(raisedLong, "middle[0]/ThirdDeckMount", UpperDeckLayers.ThirdMiddle), "middle[0]/ThirdDeckMount", UpperDeckLayers.ThirdMiddle);
            var atw = ShipAssembler.Assemble(twice, lib);
            Gate("third-deck-no-stacking", !atw.ok && atw.HasCode("FITTING_SOCKET_TAKEN"), Codes(atw));

            // ---- foredeck: assembly + guns ------------------------------
            var fore = Fit(ShipConfiguration.Long(), "bow/ForedeckMount", UpperDeckLayers.Foredeck);
            string portSlot = "fitting:bow/ForedeckMount/Foredeck_Cannon_Port", starSlot = "fitting:bow/ForedeckMount/Foredeck_Cannon_Starboard";
            var af = ShipAssembler.Assemble(fore, lib);
            int foreSlots = 0;
            if (af.ok) foreach (var s in af.slots) if (s.qualifiedId == portSlot || s.qualifiedId == starSlot) foreSlots++;
            Gate("foredeck-assembles-with-two-gun-slots", af.ok && foreSlots == 2, af.ok ? $"{foreSlots} foredeck slots resolved" : Codes(af));
            var foreGuns = fore.Clone();
            foreGuns.equipment.Add(new EquipmentChoice { slotId = portSlot, moduleId = ShipConfiguration.EquipmentCannon });
            foreGuns.equipment.Add(new EquipmentChoice { slotId = starSlot, moduleId = ShipConfiguration.EquipmentCannon });
            var afg = ShipAssembler.Assemble(foreGuns, lib);
            Gate("foredeck-takes-two-cannons", afg.ok, Codes(afg));
            var shortFore = Fit(ShipConfiguration.Short(), "bow/ForedeckMount", UpperDeckLayers.Foredeck);
            var asf = ShipAssembler.Assemble(shortFore, lib);
            Gate("foredeck-short-ship-assembles", asf.ok, Codes(asf));
            var wideFore = Fit(ExpandedPresets.ExpandedLong(), "bow/ForedeckMount", UpperDeckLayers.Foredeck);
            var awf = ShipAssembler.Assemble(wideFore, lib);
            Gate("foredeck-refused-on-wide-bow", !awf.ok && awf.HasCode("FITTING_SOCKET_UNKNOWN"), Codes(awf));
            var fore2 = Fit(fore, "bow/ForedeckMount", UpperDeckLayers.Foredeck);
            var af2 = ShipAssembler.Assemble(fore2, lib);
            Gate("foredeck-no-second-foredeck", !af2.ok && af2.HasCode("FITTING_SOCKET_TAKEN"), Codes(af2));

            // ---- policy --------------------------------------------------
            var polBad = new List<string>();
            foreach (var (name, c) in new[] { ("third", third), ("third-2bay", twoBay), ("foredeck", foreGuns) })
                foreach (var r in ShipyardPolicy.Check(c, lib)) polBad.Add($"{name}: {r.code} {r.message}");
            var ph = Fit(ShipConfiguration.Long(), "stern/Chimney", "deck.upper.partial.placeholder");
            bool phRefused = false;
            foreach (var r in ShipyardPolicy.Check(ph, lib)) if (r.code == ShipyardCodes.NotInPrototype) phRefused = true;
            Gate("upper-deck-policy-offers-layers-not-placeholder", polBad.Count == 0 && phRefused,
                polBad.Count == 0 ? $"layers pass policy; placeholder refused {phRefused}" : string.Join(" | ", polBad));

            // ---- plan: capacity + physics -------------------------------
            var reference = ShipyardSelfTest.ReferenceFrom(hullFormJson);
            if (reference == null) { Gate("upper-deck-reference-hull-loads", false, "no hullform.json"); return; }
            var refPlan = ShipyardPlanner.PlanFor(ShipConfiguration.Long(), lib, reference, null, out _);
            var basePlan = ShipyardPlanner.PlanFor(raisedLong, lib, reference, refPlan, out _);
            var thirdPlan = ShipyardPlanner.PlanFor(third, lib, reference, refPlan, out _);
            if (basePlan == null || thirdPlan == null) { Gate("third-deck-plans", false, "no plan"); return; }
            SectionCapacity Sec(ShipyardPlan p, string key) { foreach (var s in p.sections) if (s.sectionKey == key) return s; return null; }
            var bm = Sec(basePlan, "middle[0]"); var tm = Sec(thirdPlan, "middle[0]");
            var bs = Sec(basePlan, "stern"); var ts = Sec(thirdPlan, "stern");
            var bb = Sec(basePlan, "bow"); var tb = Sec(thirdPlan, "bow");
            Gate("third-deck-adds-section-capacity",
                tm.holdCells - bm.holdCells == 3 && tm.berths - bm.berths == 1 && ts.holdCells - bs.holdCells == 5 && ts.berths - bs.berths == 2
                && tb.holdCells - bb.holdCells == 4 && tb.berths - bb.berths == 1,
                $"hold {basePlan.capacity.holdCells} -> {thirdPlan.capacity.holdCells}, berths {basePlan.capacity.crewStations} -> {thirdPlan.capacity.crewStations} (stern +{ts.holdCells - bs.holdCells}/+{ts.berths - bs.berths}, middle +{tm.holdCells - bm.holdCells}/+{tm.berths - bm.berths}, bow +{tb.holdCells - bb.holdCells}/+{tb.berths - bb.berths})");
            var space = ShipyardInterior.SectionSpace(third, "middle[0]", lib);
            Gate("third-deck-interior-budget-matches-plan", string.IsNullOrEmpty(space.reason) && space.holdCells == tm.holdCells && space.berths == tm.berths,
                $"interior {space.holdCells}/{space.berths} vs plan {tm.holdCells}/{tm.berths} {space.reason}");

            float layersKg = 0f;
            foreach (var id in new[] { UpperDeckLayers.ThirdStern, UpperDeckLayers.ThirdMiddle, UpperDeckLayers.ThirdBow })
                if (lib.TryGet(id, out var d)) layersKg += d.lightship.massKg;
            float dGm = thirdPlan.data.gm - basePlan.data.gm, dKg = thirdPlan.data.kg - basePlan.data.kg;
            Gate("third-deck-weighs-and-raises-kg", Mathf.Abs(thirdPlan.lightshipKg - basePlan.lightshipKg - layersKg) < 1f && dKg > 0f && dGm < 0f,
                $"lightship {F(basePlan.lightshipKg / 1000f)} -> {F(thirdPlan.lightshipKg / 1000f)} t; KG {F(basePlan.data.kg)} -> {F(thirdPlan.data.kg)} m; GM {F(basePlan.data.gm)} -> {F(thirdPlan.data.gm)} m ({F(dGm)}); roll gyradius {F(basePlan.data.gyradiusRoll)} -> {F(thirdPlan.data.gyradiusRoll)} m");
            Gate("third-deck-gm-above-0.3m", thirdPlan.data.gm > 0.3f, $"GM {F(thirdPlan.data.gm)} m");
            float midZ = 0.5f * (tm.aftZ + tm.fwdZ);
            float bDeck = float.NaN, tDeck = float.NaN, best = float.MaxValue;
            for (int i = 0; i < thirdPlan.data.stations.Length; i++)
            {
                float dz = Mathf.Abs(thirdPlan.data.stations[i].z - midZ);
                if (dz < best) { best = dz; tDeck = thirdPlan.data.stations[i].deckY; bDeck = basePlan.data.stations[i].deckY; }
            }
            Gate("third-deck-raises-station-walk-deck", Mathf.Abs(tDeck - bDeck - 2.44f * k) < 1e-3f && Mathf.Abs(thirdPlan.walkDeckZU - 6.64f) < 1e-3f,
                $"amidships deckY {F(bDeck)} -> {F(tDeck)} m (expect +{F(2.44f * k)}); walkDeckZU {F(thirdPlan.walkDeckZU)}");

            var forePlan = ShipyardPlanner.PlanFor(foreGuns, lib, reference, refPlan, out _);
            var longGuns = ShipyardPlanner.PlanFor(ShipConfiguration.Long(), lib, reference, refPlan, out _);
            if (forePlan == null || longGuns == null) { Gate("foredeck-plans", false, "no plan"); return; }
            lib.TryGet(UpperDeckLayers.Foredeck, out var fdDef);
            var fb = Sec(forePlan, "bow"); var lb = Sec(longGuns, "bow");
            Gate("foredeck-adds-two-gun-slots-and-guns", fb.gunSlotIds.Count - lb.gunSlotIds.Count == 2 && forePlan.capacity.guns - longGuns.capacity.guns == 2
                && fb.holdCells == lb.holdCells && fb.berths == lb.berths,
                $"bow gun slots {lb.gunSlotIds.Count} -> {fb.gunSlotIds.Count}, guns {longGuns.capacity.guns} -> {forePlan.capacity.guns}, hold/berths unchanged {fb.holdCells}/{fb.berths}");
            Gate("foredeck-weighs-and-raises-kg", Mathf.Abs(forePlan.lightshipKg - longGuns.lightshipKg - fdDef.lightship.massKg) < 1f
                && forePlan.data.kg > longGuns.data.kg && forePlan.data.gm > 0.3f,
                $"lightship +{F(fdDef.lightship.massKg)} kg; KG {F(longGuns.data.kg)} -> {F(forePlan.data.kg)} m; GM {F(longGuns.data.gm)} -> {F(forePlan.data.gm)} m");
            bool stationsSame = true;
            for (int i = 0; i < forePlan.data.stations.Length; i++)
                if (Mathf.Abs(forePlan.data.stations[i].deckY - longGuns.data.stations[i].deckY) > 1e-5f) stationsSame = false;
            Gate("foredeck-open-platform-keeps-freeboard", stationsSame, "open platform: no station deckY change");

            var slots = ShipyardEquipment.Slots(foreGuns, lib, reference);
            int onBow = 0; string lab = "";
            foreach (var s in slots) if (s.slotId == portSlot || s.slotId == starSlot) { if (s.sectionKey == "bow") onBow++; lab = s.label; }
            Gate("foredeck-guns-listed-on-bow-page", onBow == 2, $"{onBow} on 'bow', e.g. '{lab}'");

            // ---- warnings ------------------------------------------------
            bool Has(List<ShipyardNote> n, string code) { foreach (var x in n) if (x.code == code) return true; return false; }
            var wFull = UpperDeckLayers.Warnings(ShipAssembler.Assemble(third, lib), lib);
            var wMid = UpperDeckLayers.Warnings(ShipAssembler.Assemble(Fit(raisedLong, "middle[0]/ThirdDeckMount", UpperDeckLayers.ThirdMiddle), lib), lib);
            var wFore = UpperDeckLayers.Warnings(af, lib);
            var wNone = UpperDeckLayers.Warnings(ShipAssembler.Assemble(raisedLong, lib), lib);
            Gate("upper-deck-report-warns-art-unreviewed",
                Has(wFull, UpperDeckLayers.CodeArtUnreviewed) && !Has(wFull, UpperDeckLayers.CodeThirdDeckPartial)
                && Has(wMid, UpperDeckLayers.CodeThirdDeckPartial) && Has(wMid, UpperDeckLayers.CodeThirdDeckNoAccess)
                && Has(wFore, UpperDeckLayers.CodeArtUnreviewed) && wNone.Count == 0,
                $"full {wFull.Count}, middle-only {wMid.Count}, foredeck {wFore.Count}, none {wNone.Count}");

            // ---- draft edits ---------------------------------------------
            var on = UpperDeckLayers.With(raisedLong, "middle[0]", true, lib);
            var off = UpperDeckLayers.With(on, "middle[0]", false, lib);
            Gate("upper-deck-with-toggles", UpperDeckLayers.Has(on, "middle[0]") && !UpperDeckLayers.Has(off, "middle[0]") && off.ValueEquals(raisedLong)
                && !UpperDeckLayers.Has(UpperDeckLayers.With(raisedLong, "middle[0]", true, lib), "stern"),
                $"on {on.fittings.Count} fittings, off {off.fittings.Count}");
            var foreOff = UpperDeckLayers.With(foreGuns, "bow", false, lib);
            Gate("upper-deck-removing-foredeck-drops-its-guns", foreOff.equipment.Count == foreGuns.equipment.Count - 2 && ShipAssembler.Assemble(foreOff, lib).ok,
                $"equipment {foreGuns.equipment.Count} -> {foreOff.equipment.Count}");
            var lowered = third.Clone();
            var (sId, mIds, bId) = RaisedSections.ToIds(DeckLevel.Raised, new[] { DeckLevel.Low }, DeckLevel.Raised);
            lowered.sternId = sId; lowered.middleIds = new List<string>(mIds); lowered.bowId = bId;
            lowered.equipment.Clear(); // the draft drops orphaned guns separately (ShipyardDraft.DropOrphanedGuns)
            int dropped = UpperDeckLayers.DropOrphaned(lowered, lib);
            var alow = ShipAssembler.Assemble(lowered, lib);
            Gate("upper-deck-orphans-dropped-on-level-change", dropped == 3 && alow.ok, $"dropped {dropped}; then {Codes(alow)}");
            var shifted = FullThird(RaisedPresets.RaisedTwoBay());
            ShipConfiguration.ShiftMiddleKeys(shifted, 1, 1);
            bool movedOk = false;
            foreach (var f in shifted.fittings) if (f.socketId == "middle[2]/ThirdDeckMount") movedOk = true;
            foreach (var f in shifted.fittings) if (f.socketId == "middle[1]/ThirdDeckMount") movedOk = false;
            Gate("upper-deck-shift-middle-keys-moves-layers", movedOk, string.Join(", ", shifted.fittings.ConvertAll(f => f.socketId)));
        }
    }
}
