using System;
using System.Collections.Generic;
using System.Linq;
using SeaSick.Steamer;
using UnityEngine;

namespace SeaSick.Ship.Modular
{
    /// Self-test gates for the shipyard SLOT model (2026-09-27, phase 1):
    /// Long migrates to identical numbers, stranded modules return to the
    /// store, every blocker fires, the draft commands, save/load round-trip.
    /// Headless (tools/modular-selftest.sh) and in the editor.
    public static class SlotModelValidation
    {
        public delegate void GateFn(string name, bool ok, string detail);

        /// A plain backend for the draft: a "live ship" held in fields,
        /// judged by the real planner, applied the way ShipyardService does.
        sealed class FakeYard : IShipyardSlotsBackend
        {
            public ModuleLibrary lib; public HullFormData reference;
            public ShipConfiguration live; public DryDock store = DryDock.Empty();
            public int crew = 8, cargo = 0, level = DockLimits.Unenforced;
            public ShipConfiguration ReadCurrent() => live.Clone();
            public DryDock ReadStore() => store.Clone();
            public int DockLevel => level;
            public int CrewAboard => crew;
            public int CargoAboard => cargo;
            LiveShipSnapshot Snap() => new LiveShipSnapshot { config = live.Clone(), totalHeld = cargo, crewAboard = crew, kindsOnDeck = cargo > 0 ? 1 : 0, dockLevel = level };
            public ShipyardReport Report(ShipConfiguration draft)
            {
                var v = ShipyardPlanner.Validate(draft, lib, reference, Snap());
                return ShipyardReport.From(v, v.currentPlan, v.draftPlan, lib.MetresPerUnit, lib);
            }
            public bool TryApply(ShipConfiguration expected, ShipConfiguration draft, IReadOnlyList<string> builds, out string reason)
            {
                reason = null;
                if (!expected.ValueEquals(live)) { reason = "STALE_DRAFT"; return false; }
                var n = SlotModel.Normalized(draft, lib);
                var v = ShipyardPlanner.Validate(n, lib, reference, Snap());
                if (!v.ok) { reason = v.issues[0].ToString(); return false; }
                var s = store.Clone();
                foreach (var b in builds) s.Add(b);
                var diff = DryDock.Diff(live, n);
                if (!s.CanApply(diff, out var missing)) { reason = "NOT_IN_DRY_DOCK " + missing; return false; }
                s.Apply(diff);
                store = s; live = n;
                return true;
            }
        }

        static string Counts(ShipConfiguration c) =>
            string.Join(", ", SlotModel.FittedCounts(c).OrderBy(k => k.Key).Select(k => $"{k.Key.Replace("module.", "")} {k.Value}"));

        public static void Body(string stdJson, IList<string> mods, IList<string> names, string hullFormJson,
            Func<string, string> readResourceText, GateFn Gate)
        {
            var lib = ModuleLibrary.FromJson(stdJson, mods, names);
            if (readResourceText != null) { lib.LoadHydrostatics(readResourceText); lib.LoadCatalog(readResourceText); }
            var cat = lib.Catalog;
            Gate("slots-catalog-loads", cat != null && cat.Ok && cat.All.Count == 11 && lib.Standards.slotModel != null,
                cat == null ? "no catalog (no resource reader)" : $"{cat.All.Count} modules; errors: {string.Join("; ", cat.errors)}");
            if (cat == null || !cat.Ok) return;
            var reference = ShipyardSelfTest.ReferenceFrom(hullFormJson);
            Gate("slots-reference-hull", reference != null, "hullform.json");
            if (reference == null) return;

            // ---- grids / dock levels ------------------------------------------------
            var legacy = ShipConfiguration.Long();
            var lng = SlotModel.Migrate(legacy, lib, out var overflow);
            var t = SlotModel.Totals(lng, lib);
            int hold = 0, deck = 0;
            foreach (var s in SlotModel.Layout(lng, lib)) { hold += s.cells[0].Length; deck += s.cells[1].Length; }
            Gate("slots-long-grid-17-cells-6-ports", hold == 8 && deck == 9 && t.cells == 17 && t.gunPorts == 6,
                $"hold {hold}, deck {deck}, cells {t.cells}, ports {t.gunPorts}");
            var l1 = DockLimits.For(lib, 1); var l5 = DockLimits.For(lib, 5); var l3 = DockLimits.For(lib, 3);
            Gate("slots-dock-levels-1-to-5", l1 != null && l1.maxSections == 3 && !l1.wideBeam && l1.maxDeck == 1
                && l3.maxSections == 4 && l3.maxDeck == 2 && l5 != null && l5.level == 5 && l5.maxTopSections == -1 && DockLimits.For(lib, 9).level == 5,
                $"I {l1?.name} {l1?.maxSections}s, III {l3?.name} {l3?.maxSections}s deck {l3?.maxDeck}, V {l5?.name}");

            // ---- migration: Long -> identical numbers --------------------------------
            var refPlan = ShipyardPlanner.PlanFor(legacy, lib, reference, null, out _);
            var v3Plan = ShipyardPlanner.PlanFor(lng, lib, reference, refPlan, out var asm3);
            var fc = SlotModel.FittedCounts(lng);
            Gate("slots-long-migrates-to-modules", overflow.Count == 0 && lng.schemaVersion == 3 && lng.layouts.Count == 0
                && fc.TryGetValue(ModuleCatalog.Cannon, out var nc) && nc == 6 && fc[ModuleCatalog.Crate] == 4 && fc[ModuleCatalog.Bunk] == 3,
                Counts(lng) + $"; overflow {overflow.Count}");
            var legacyGuns = legacy.equipment.Select(e => e.slotId + "=" + e.moduleId).OrderBy(x => x).ToList();
            var v3Guns = lng.equipment.Select(e => e.slotId + "=" + e.moduleId).OrderBy(x => x).ToList();
            Gate("slots-long-identical-numbers", refPlan != null && v3Plan != null
                && v3Plan.capacity.holdCells == 16 && v3Plan.capacity.crewStations == 8 && v3Plan.capacity.guns == 6
                && refPlan.capacity.holdCells == v3Plan.capacity.holdCells && refPlan.capacity.crewStations == v3Plan.capacity.crewStations
                && refPlan.capacity.guns == v3Plan.capacity.guns && legacyGuns.SequenceEqual(v3Guns) && v3Plan.slots.cabinBerths == 2,
                v3Plan == null ? asm3?.Summary() : $"legacy {refPlan.capacity} | slots {v3Plan.capacity}; cabin {v3Plan.slots.cabinBerths}; guns on the same mounts: {legacyGuns.SequenceEqual(v3Guns)}");
            if (v3Plan != null)
            {
                var ga = refPlan.data.gunSockets.OrderBy(g => g.z).ThenBy(g => g.x).ToArray();
                var gb = v3Plan.data.gunSockets.OrderBy(g => g.z).ThenBy(g => g.x).ToArray();
                bool sockets = ga.Length == gb.Length && ga.Length == 3;
                for (int i = 0; sockets && i < ga.Length; i++) sockets = (ga[i] - gb[i]).sqrMagnitude < 1e-8f;
                Gate("slots-long-guns-stand-where-they-did", sockets, $"{v3Plan.data.gunSockets.Length} starboard sockets");
                ShipyardPlanner.TableDraft(refPlan, refPlan.lightshipKg + 8 * 90f + refPlan.gunsWeightKg, out float dr2);
                ShipyardPlanner.TableDraft(v3Plan, v3Plan.lightshipKg + 8 * 90f + v3Plan.gunsWeightKg, out float dr3);
                var emptyHull = lng.Clone(); emptyHull.fits.Clear();
                var ePlan = ShipyardPlanner.PlanFor(emptyHull, lib, reference, refPlan, out _);
                Gate("slots-long-mass-and-draft-identical", v3Plan.lightshipKg == refPlan.lightshipKg && v3Plan.data.massKg == refPlan.data.massKg
                    && v3Plan.loadDisplacementKg == refPlan.loadDisplacementKg && dr2 == dr3 && ePlan != null && ePlan.lightshipKg < refPlan.lightshipKg
                    && v3Plan.fitMassKg > 0f && v3Plan.fitCentreM.y > -2f && v3Plan.fitCentreM.y < 2f,
                    $"lightship {refPlan.lightshipKg:0} -> {v3Plan.lightshipKg:0} kg (standard fit-out {SlotModel.StandardFitOutKg(lib):0} kg is in the hull mass; empty hull {ePlan?.lightshipKg:0} kg); draft (8 hands) {dr2:0.000} -> {dr3:0.000} m; fit mass {v3Plan.fitMassKg:0} kg, centre y {v3Plan.fitCentreM.y:0.00} m");
            }
            var liveSnap = new LiveShipSnapshot { config = lng, crewAboard = 8, totalHeld = 16, kindsOnDeck = 2 };
            var vLong = ShipyardPlanner.Validate(lng, lib, reference, liveSnap);
            Gate("slots-long-validates-full", vLong.ok && vLong.handsAshore == 0, vLong.Summary());
            var again = SlotModel.Migrate(lng, lib, out var ov2);
            Gate("slots-migrate-idempotent", again.ValueEquals(lng) && ov2.Count == 0, Counts(again));

            // Every legacy preset migrates without losing capacity.
            var bad = new List<string>();
            foreach (var (name, c) in new[] { ("short", ShipConfiguration.Short()), ("2 bays", ShipConfiguration.WithMiddles(2)), ("3 bays", ShipConfiguration.WithMiddles(3)),
                         ("wide long", ExpandedPresets.ExpandedLong()), ("raised long", RaisedPresets.RaisedLong()) })
            {
                var oldP = ShipyardPlanner.PlanFor(c, lib, reference, refPlan, out _);
                var m = SlotModel.Migrate(c, lib, out var of);
                var newP = ShipyardPlanner.PlanFor(m, lib, reference, refPlan, out var a2);
                if (oldP == null || newP == null) { bad.Add($"{name}: no plan {a2?.Summary()}"); continue; }
                int cannonsKept = SlotModel.FittedCounts(m).TryGetValue(ModuleCatalog.Cannon, out var k) ? k : 0;
                if (newP.capacity.holdCells + 3 < oldP.capacity.holdCells && of.Count == 0) bad.Add($"{name}: hold {oldP.capacity.holdCells}->{newP.capacity.holdCells}");
                if (newP.capacity.crewStations < oldP.capacity.crewStations && of.Count == 0) bad.Add($"{name}: berths {oldP.capacity.crewStations}->{newP.capacity.crewStations}");
                if (cannonsKept + of.Count(x => x == ModuleCatalog.Cannon) != oldP.capacity.guns) bad.Add($"{name}: guns {oldP.capacity.guns}->{cannonsKept}");
            }
            Gate("slots-legacy-presets-migrate-without-loss", bad.Count == 0, bad.Count == 0 ? "short, 2-3 bays, wide, raised: capacity kept (or overflow to store)" : string.Join("; ", bad));

            // An empty hull sails: the stern cabin's 2 berths.
            var empty = lng.Clone(); empty.fits.Clear();
            var ve = ShipyardPlanner.Validate(empty, lib, reference, new LiveShipSnapshot { config = lng, crewAboard = 2 });
            Gate("slots-empty-hull-has-cabin-and-sails", ve.ok && ve.capacityDraft.crewStations == 2 && ve.capacityDraft.holdCells == 0 && ve.capacityDraft.guns == 0,
                $"{ve.capacityDraft}; {ve.Summary()}");

            // ---- blockers --------------------------------------------------------------
            Rejection Code(ShipConfiguration c, string code, int level = DockLimits.Unenforced, int crew = 8)
            {
                var v = ShipyardPlanner.Validate(c, lib, reference, new LiveShipSnapshot { config = lng, crewAboard = crew, dockLevel = level });
                return v.issues.FirstOrDefault(i => i.code == code);
            }
            var wrong = lng.Clone(); wrong.fits.First(f => f.moduleId == ModuleCatalog.Crate).moduleId = ModuleCatalog.Cannon;
            var wrong2 = lng.Clone(); wrong2.fits.First(f => f.moduleId == ModuleCatalog.Cannon).moduleId = ModuleCatalog.Bunk;
            Gate("slots-blocker-slot-wrong-kind", Code(wrong, SlotCodes.SlotWrongKind) != null && Code(wrong2, SlotCodes.SlotWrongKind) != null,
                Code(wrong, SlotCodes.SlotWrongKind)?.message + " | " + Code(wrong2, SlotCodes.SlotWrongKind)?.message);
            var bench = lng.Clone(); bench.fits.Add(new SlotFit { section = "stern", deck = 1, cell = "P1", moduleId = ModuleCatalog.RepairBench });
            Gate("slots-blocker-module-locked", Code(bench, SlotCodes.ModuleLocked, 1) != null && Code(bench, SlotCodes.ModuleLocked, 4) == null,
                Code(bench, SlotCodes.ModuleLocked, 1)?.message);
            var pumps = lng.Clone();
            pumps.fits.Add(new SlotFit { section = "bow", deck = 0, cell = "S0", moduleId = ModuleCatalog.BilgePump });
            pumps.fits.RemoveAll(f => f.section == "stern" && f.deck == 0 && f.cell == "S0");
            pumps.fits.Add(new SlotFit { section = "stern", deck = 0, cell = "S0", moduleId = ModuleCatalog.BilgePump });
            Gate("slots-blocker-one-per-ship", Code(pumps, SlotCodes.OnePerShip) != null, Code(pumps, SlotCodes.OnePerShip)?.message);
            var three = SlotModel.Migrate(ShipConfiguration.WithMiddles(3), lib, out _);
            var wideL = SlotModel.Migrate(ExpandedPresets.ExpandedLong(), lib, out _);
            Gate("slots-blocker-dock-level", Code(three, SlotCodes.DockLevel, 1) != null && Code(three, SlotCodes.DockLevel, 4) == null
                && Code(wideL, SlotCodes.DockLevel, 1) != null && Code(wideL, SlotCodes.DockLevel, 2) == null && Code(lng, SlotCodes.DockLevel, 1) == null,
                Code(three, SlotCodes.DockLevel, 1)?.message + " | " + Code(wideL, SlotCodes.DockLevel, 1)?.message);
            var vShort = ShipyardPlanner.Validate(lng, lib, reference, new LiveShipSnapshot { config = lng, crewAboard = 3 });
            var shortWarn = vShort.slotWarnings.FirstOrDefault(w => w.code == SlotCodes.GunsShortOfHands);
            Gate("slots-warning-guns-short-of-hands", vShort.ok && shortWarn != null && !vShort.HasCode(ShipyardCodes.GunsNeedCrew),
                shortWarn?.message ?? vShort.Summary());

            // ---- the draft: commands, store, undo -------------------------------------
            var yard = new FakeYard { lib = lib, reference = reference, live = lng.Clone(), crew = 8, cargo = 0 };
            var d = new ShipyardSlotDraft(lib, yard);
            var view = d.View();
            Gate("slots-view-long", view.sections.Count == 3 && view.totals.guns == 6 && view.totals.gunPorts == 6 && view.totals.berths == 8
                && view.totals.cargoCap == 16 && view.blockers.Count == 0 && !view.dirty && view.sections[0].decks.Count == 4
                && view.sections[1].decks[0].used == 4 && !view.sections[0].decks[2].unlocked && view.StoreCount(ModuleCatalog.Bunk) == 0,
                $"guns {view.totals.guns}/{view.totals.gunPorts}, berths {view.totals.berths}, cargo cap {view.totals.cargoCap}, draft {view.totals.draftM:0.000} m, blockers {view.blockers.Count}");
            bool refusedInner = !d.Place("stern", 1, "P1", ModuleCatalog.Cannon) && d.MessageCode == SlotCodes.SlotWrongKind;
            bool refusedStore = !d.Place("stern", 1, "P1", ModuleCatalog.Bunk) && d.MessageCode == SlotCodes.NotInStore;
            bool built = d.BuildAndPlace("stern", 1, "P1", ModuleCatalog.Bunk);
            view = d.View();
            Gate("slots-draft-place-rules-and-build", refusedInner && refusedStore && built && view.totals.berths == 10 && view.builds.Count == 1
                && view.StoreCount(ModuleCatalog.Bunk) == 0 && view.dirty, $"berths {view.totals.berths}, builds {view.builds.Count}; {d.Message}");
            // Move with swap: the stern P1 bunk <-> a middle hold crate.
            var crateCell = d.Snapshot().fits.First(f => f.moduleId == ModuleCatalog.Crate);
            bool swapped = d.Move("stern", 1, "P1", crateCell.section, crateCell.deck, crateCell.cell);
            var after = d.Snapshot();
            Gate("slots-draft-move-swaps", swapped && SlotModel.At(after, "stern", 1, "P1")?.moduleId == ModuleCatalog.Crate
                && SlotModel.At(after, crateCell.section, crateCell.deck, crateCell.cell)?.moduleId == ModuleCatalog.Bunk
                && !d.Move("stern", 1, "P0", "stern", 1, "P1"), d.Message);
            // A cannon off to the store, then back, then undo x3.
            bool removed = d.Remove("bow", 1, "P0");
            int inStore = d.View().StoreCount(ModuleCatalog.Cannon);
            d.Undo(); d.Undo(); d.Undo();
            Gate("slots-draft-remove-and-undo", removed && inStore == 1 && !d.Dirty && d.Snapshot().ValueEquals(lng) && d.Builds.Count == 0,
                $"cannon in store after remove {inStore}; clean after 3 undos {!d.Dirty}");

            // Stranded: add a section, fill it, remove it -> its modules in the store.
            bool added = d.AddSection(1);
            d.BuildAndPlace("middle[1]", 0, "P0", ModuleCatalog.Crate);
            d.BuildAndPlace("middle[1]", 1, "P0", ModuleCatalog.Cannon);
            bool shifted = d.Snapshot().middleIds.Count == 2 && SlotModel.At(d.Snapshot(), "middle[1]", 1, "P0") != null;
            bool gone = d.RemoveSection("middle[1]");
            var sv = d.View();
            Gate("slots-stranded-section-modules-return-to-store", added && shifted && gone && sv.StoreCount(ModuleCatalog.Crate) == 1
                && sv.StoreCount(ModuleCatalog.Cannon) == 1 && d.Snapshot().middleIds.Count == 1 && d.Snapshot().fits.Count == lng.fits.Count,
                $"store crate {sv.StoreCount(ModuleCatalog.Crate)}, cannon {sv.StoreCount(ModuleCatalog.Cannon)}; {d.Message}");
            // Adding a section in FRONT of a filled one renumbers its fits.
            var d2 = new ShipyardSlotDraft(lib, yard);
            d2.AddSection(0);
            var s2 = d2.Snapshot();
            Gate("slots-add-section-renumbers-fits", s2.middleIds.Count == 2 && s2.fits.Count(f => f.section == "middle[1]") == lng.fits.Count(f => f.section == "middle[0]")
                && s2.fits.Count(f => f.section == "middle[0]") == 0, string.Join(" ", s2.fits.Select(f => f.ToString())));

            // Stranded by a hand-edited / stale draft: a fit on a missing section.
            var stale = lng.Clone(); stale.fits.Add(new SlotFit { section = "middle[2]", deck = 0, cell = "P0", moduleId = ModuleCatalog.Crate });
            var norm = SlotModel.Normalized(stale, lib, out var strandedList);
            var ddiff = DryDock.Diff(stale, norm);
            Gate("slots-normalize-strands-to-store", strandedList.Count == 1 && ddiff.Count == 1 && ddiff[0].delta == -1 && ddiff[0].moduleId == ModuleCatalog.Crate,
                string.Join(", ", strandedList.Select(f => f.ToString())));

            // Raise / lower: the foredeck on the standard bow opens an Upper;
            // lowering it sends its cannon to the store.
            var d3 = new ShipyardSlotDraft(lib, yard);
            bool raisedBow = d3.RaiseDeck("bow");
            var bowView = d3.View().sections.Last();
            bool placedUp = d3.BuildAndPlace("bow", 2, "P0", ModuleCatalog.Cannon);
            var upGuns = ShipyardPlanner.PlanFor(d3.Snapshot(), lib, reference, refPlan, out _)?.capacity.guns ?? -1;
            bool lowered = d3.LowerDeck("bow");
            bool sternNoUpper = !d3.RaiseDeck("stern") && d3.MessageCode == SlotCodes.DeckLocked;
            Gate("slots-raise-lower-deck", raisedBow && bowView.decks[2].unlocked && placedUp && upGuns == 7 && lowered
                && d3.View().StoreCount(ModuleCatalog.Cannon) == 1 && sternNoUpper,
                $"bow upper {bowView.decks[2].unlocked}, guns with foredeck cannon {upGuns}, stern: {d3.Message}");
            var dw = new ShipyardSlotDraft(lib, SlotModel.Migrate(ExpandedPresets.ExpandedLong(), lib, out _), null);
            bool wideRaise = dw.RaiseDeck("middle[0]");
            var wv = dw.View();
            Gate("slots-wide-section-raises-to-upper", wideRaise && wv.sections[1].decks[2].unlocked && wv.sections[1].wide
                && wv.sections[1].decks[0].cells.Count == 6 && wv.sections[1].decks[1].cells.Count == 6, dw.Message);

            // One-per-ship through the draft; the add drawer's options.
            var d4 = new ShipyardSlotDraft(lib, yard);
            bool pump1 = d4.BuildAndPlace("bow", 0, "S0", ModuleCatalog.BilgePump) || d4.MessageCode == SlotCodes.SlotOccupied;
            d4.Remove("bow", 0, "S0"); d4.BuildAndPlace("bow", 0, "S0", ModuleCatalog.BilgePump);
            bool pump2Refused = !d4.BuildAndPlace("stern", 1, "P1", ModuleCatalog.BilgePump);
            var opts = d4.Options("stern", 1, "P1");
            string A(string id) => opts.First(o => o.moduleId == id).action;
            Gate("slots-options-drawer", pump1 && pump2Refused && A(ModuleCatalog.Cannon) == "not-here" && A(ModuleCatalog.Bunk) == "build-place"
                && A(ModuleCatalog.BilgePump) == "not-here" && A(ModuleCatalog.RepairBench) == "build-place"
                && d4.Options("bow", 0, "P0").First(o => o.moduleId == ModuleCatalog.BilgePump).action == "move-here",
                string.Join(", ", opts.Select(o => $"{o.moduleId.Replace("module.", "")}:{o.action}")));
            var d5 = new ShipyardSlotDraft(lib, lng, null, null, 1);
            Gate("slots-draft-locked-at-dock-1", !d5.BuildModule(ModuleCatalog.RepairBench) && d5.MessageCode == SlotCodes.ModuleLocked
                && !d5.AddSection(0) && d5.MessageCode == SlotCodes.DockLevel, d5.Message);

            // ---- apply through the backend + save/load round-trip -----------------------
            var d6 = new ShipyardSlotDraft(lib, yard);
            d6.Remove("bow", 1, "P0"); d6.Remove("bow", 1, "S0");
            var bunkCell = d6.Snapshot().fits.First(f => f.moduleId == ModuleCatalog.Bunk);
            d6.Remove(bunkCell.section, bunkCell.deck, bunkCell.cell);
            d6.BuildAndPlace(bunkCell.section, bunkCell.deck, bunkCell.cell, ModuleCatalog.Crate);
            yard.crew = 6;
            bool applied = d6.Apply();
            Gate("slots-apply-moves-modules-to-store", applied && yard.store.Count(ModuleCatalog.Cannon) == 2 && yard.store.Count(ModuleCatalog.Bunk) == 1
                && yard.store.Count(ModuleCatalog.Crate) == 0 && SlotModel.Totals(yard.live, lib).cargo == 20,
                $"{d6.Message}; store: {string.Join(", ", yard.store.entries.Select(e => e.moduleId + " " + e.count))}");
            var cfgJson = ModularSave.Encode(yard.live);
            var back = ModularSave.Decode(cfgJson, lib, out bool has, out string warn);
            var dockBack = DryDock.FromJson(yard.store.ToJson());
            Gate("slots-save-load-round-trip", has && warn == null && back.ValueEquals(yard.live) && back.UsesSlots && dockBack.ValueEquals(yard.store)
                && ShipConfiguration.FromJson(yard.live.ToJson()).ValueEquals(yard.live), $"{cfgJson.Length} chars; {Counts(back)}");
            var oldDock = new DryDock(); oldDock.Add(ShipConfiguration.EquipmentCannon, 2); oldDock.Add(ModuleCatalog.Crate, 1);
            SlotModel.MigrateStore(oldDock, lib);
            Gate("slots-old-store-cannons-renamed", oldDock.Count(ModuleCatalog.Cannon) == 2 && oldDock.Count(ShipConfiguration.EquipmentCannon) == 0 && oldDock.Count(ModuleCatalog.Crate) == 1,
                string.Join(", ", oldDock.entries.Select(e => e.moduleId + " " + e.count)));
            yard.live = lng.Clone(); var stale2 = new ShipyardSlotDraft(lib, lng.Clone(), null, yard);
            stale2.Remove("bow", 1, "P0"); yard.live.fits.RemoveAt(0);
            Gate("slots-apply-stale-refused", !stale2.Apply() && stale2.Message.Contains("STALE"), stale2.Message);
        }
    }
}
