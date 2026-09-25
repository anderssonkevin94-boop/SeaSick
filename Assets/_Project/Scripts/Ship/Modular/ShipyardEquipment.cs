using System;
using System.Collections.Generic;
using SeaSick.Steamer;
using UnityEngine;

namespace SeaSick.Ship.Modular
{
    // ---------------------------------------------------------------------
    // Equipment editing (2026-09-25): the pure half of ShipyardService's
    // FitEquipment / RemoveEquipment / MoveEquipment / EquipmentSlots /
    // DryDockPreview. Everything here returns a NEW ShipConfiguration and
    // touches nothing -- no ship, no dock, no save (docs/SHIPYARD-API.md).
    // ---------------------------------------------------------------------

    /// One deck-gun slot of a draft, for Astra's picker.
    [Serializable]
    public class EquipmentSlotView
    {
        /// Qualified, e.g. "middle[0]/DeckSlot_1_1".
        public string slotId;
        /// "stern", "middle[0]", .., "bow".
        public string sectionKey;
        /// "port" or "starboard" (game +X = starboard).
        public string side;
        /// "Middle bay 1, starboard gun".
        public string label;
        public string[] accepts;
        /// "" = empty.
        public string occupantModuleId;
        public bool usable;
        /// "" when usable.
        public string blockedReason;
        /// Ship frame.
        public Vector3 positionM;
    }

    /// One module id's dry-dock stock, for Astra's picker.
    [Serializable]
    public class DryDockRow
    {
        public string moduleId;
        public string name;
        public int inDockNow;
        public int inDockAfterApply;
    }

    /// The result of a pure equipment edit.
    public class ShipyardEdit
    {
        public bool ok;
        public string code = "";
        public string message = "";
        /// The edited copy on success; an UNCHANGED copy of the input draft
        /// on failure (never the caller's own reference either way).
        public ShipConfiguration draft;
    }

    public static class ShipyardEquipment
    {
        const float Eps = 1e-3f;

        /// Add `moduleId` at `slotId`. Fails (draft unchanged) exactly the
        /// way ShipAssembler would refuse the resulting configuration --
        /// EQUIPMENT_SLOT_UNKNOWN, EQUIPMENT_CLASS_NOT_ALLOWED,
        /// EQUIPMENT_SLOT_TAKEN ("SLOT_OCCUPIED" in the brief -- the same
        /// code, ShipAssembler already owns it), EQUIPMENT_EXCEEDS_CLEARANCE,
        /// EQUIPMENT_BLOCKS_PASSAGE, EQUIPMENT_OVERLAP -- or the policy's
        /// NOT_IN_PROTOTYPE for a module id the yard does not offer.
        public static ShipyardEdit Fit(ShipConfiguration draft, string slotId, string moduleId, ModuleLibrary lib)
        {
            var before = draft != null ? draft.Clone() : new ShipConfiguration();
            var after = before.Clone();
            after.equipment.Add(new EquipmentChoice { slotId = slotId, moduleId = moduleId });
            return Validated(before, after, slotId, lib);
        }

        /// Remove whatever is fitted at `slotId`. Fails NOTHING_THERE if the
        /// draft has nothing there.
        public static ShipyardEdit Remove(ShipConfiguration draft, string slotId, ModuleLibrary lib)
        {
            var before = draft != null ? draft.Clone() : new ShipConfiguration();
            var after = before.Clone();
            int removed = after.equipment.RemoveAll(e => e != null && e.slotId == slotId);
            if (removed == 0)
                return new ShipyardEdit { ok = false, code = "NOTHING_THERE", draft = before,
                    message = $"There is nothing fitted at {slotId}." };
            return Validated(before, after, slotId, lib);
        }

        /// Move whatever is fitted at `fromSlotId` to `toSlotId` (same module,
        /// so the dry dock sees no net change -- ApplyRefit's diff is by
        /// module id, docs/SHIPYARD-API.md §5). Fails NOTHING_THERE if
        /// the draft has nothing at `fromSlotId`.
        public static ShipyardEdit Move(ShipConfiguration draft, string fromSlotId, string toSlotId, ModuleLibrary lib)
        {
            var before = draft != null ? draft.Clone() : new ShipConfiguration();
            var after = before.Clone();
            var item = after.equipment.Find(e => e != null && e.slotId == fromSlotId);
            if (item == null)
                return new ShipyardEdit { ok = false, code = "NOTHING_THERE", draft = before,
                    message = $"There is nothing fitted at {fromSlotId} to move." };
            item.slotId = toSlotId;
            return Validated(before, after, toSlotId, lib);
        }

        static ShipyardEdit Validated(ShipConfiguration before, ShipConfiguration after, string atSlot, ModuleLibrary lib)
        {
            var policy = ShipyardPolicy.Check(after, lib);
            var asm = ShipAssembler.Assemble(after, lib);
            var problems = new List<Rejection>(policy);
            problems.AddRange(asm.rejections);
            if (problems.Count > 0)
            {
                Rejection pick = problems.Find(p => p.partId == atSlot) ?? problems[0];
                return new ShipyardEdit { ok = false, code = pick.code, message = pick.message, draft = before };
            }
            return new ShipyardEdit { ok = true, draft = after };
        }

        /// Every deck-gun slot of the draft (fixed `deck.slot` sockets that
        /// accept `equipment.deck-gun`; the free-placement deck AREA is not
        /// enumerated as discrete slots). Empty if the draft does not
        /// assemble.
        public static List<EquipmentSlotView> Slots(ShipConfiguration draft, ModuleLibrary lib, HullFormData reference)
        {
            var list = new List<EquipmentSlotView>();
            if (draft == null || lib == null || reference == null) return list;
            var refPlan = ShipyardPlanner.PlanFor(ShipConfiguration.Long(), lib, reference, null, out _);
            var plan = ShipyardPlanner.PlanFor(draft, lib, reference, refPlan, out var asm);
            if (asm == null || !asm.ok) return list;
            float viewZ = plan != null ? plan.viewOffset.z : 0f;
            foreach (var s in asm.slots)
            {
                if (s.role != SocketRole.DeckSlot || s.classes == null || Array.IndexOf(s.classes, "equipment.deck-gun") < 0) continue;
                string blocked = "";
                foreach (var r in asm.reservations)
                    if (r.kind == "passage" && BoxOverlap(s.clearanceMinM, s.clearanceMaxM, r.minM, r.maxM))
                    { blocked = $"stands in the crew passage ({r.id})"; break; }
                int slash = s.qualifiedId.LastIndexOf('/');
                string sectionKey = slash > 0 ? s.qualifiedId.Substring(0, slash) : s.qualifiedId;
                string side = s.positionM.x > 0f ? "starboard" : "port";
                list.Add(new EquipmentSlotView
                {
                    slotId = s.qualifiedId,
                    sectionKey = sectionKey,
                    side = side,
                    label = $"{SectionLabel(sectionKey)}, {side} gun",
                    accepts = s.classes,
                    occupantModuleId = s.occupiedBy ?? "",
                    usable = blocked.Length == 0,
                    blockedReason = blocked,
                    positionM = new Vector3(s.positionM.x, s.positionM.y, viewZ + s.positionM.z),
                });
            }
            AddForeAft(list);
            return list;
        }

        /// A section+side with more than one slot ("Middle bay 1, starboard
        /// gun" twice over) is ambiguous -- tag each with its position along
        /// the ship (game +Z is the bow, `ModularScale`), largest Z first.
        /// A side with exactly one slot needs no qualifier.
        static void AddForeAft(List<EquipmentSlotView> list)
        {
            var groups = new Dictionary<(string section, string side), List<EquipmentSlotView>>();
            foreach (var v in list)
            {
                var key = (v.sectionKey, v.side);
                if (!groups.TryGetValue(key, out var g)) groups[key] = g = new List<EquipmentSlotView>();
                g.Add(v);
            }
            foreach (var g in groups.Values)
            {
                if (g.Count < 2) continue;
                g.Sort((a, b) => b.positionM.z.CompareTo(a.positionM.z));
                for (int i = 0; i < g.Count; i++)
                {
                    string where = i == 0 ? "forward" : i == g.Count - 1 ? "aft" : "amidships";
                    g[i].label = $"{SectionLabel(g[i].sectionKey)}, {g[i].side}, {where}";
                }
            }
        }

        static string SectionLabel(string sectionKey)
        {
            if (sectionKey == ShipAssembler.StdKeyStern) return "Stern";
            if (sectionKey == ShipAssembler.StdKeyBow) return "Bow";
            if (sectionKey.StartsWith("middle[") && sectionKey.EndsWith("]")
                && int.TryParse(sectionKey.Substring(7, sectionKey.Length - 8), out int i))
                return $"Middle bay {i + 1}";
            return sectionKey;
        }

        static bool BoxOverlap(Vector3 aMin, Vector3 aMax, Vector3 bMin, Vector3 bMax) =>
            aMin.x < bMax.x - Eps && bMin.x < aMax.x - Eps &&
            aMin.y < bMax.y - Eps && bMin.y < aMax.y - Eps &&
            aMin.z < bMax.z - Eps && bMin.z < aMax.z - Eps;

        /// Per module id the dock holds now or the draft would move through
        /// it: how many are in the dock now, and how many would be after
        /// applying `expected` -> `draft` (the same diff ApplyRefit uses).
        public static List<DryDockRow> DockPreview(ShipConfiguration expected, ShipConfiguration draft, DryDock dock, ModuleLibrary lib)
        {
            var rows = new List<DryDockRow>();
            dock = dock ?? DryDock.Empty();
            var diff = DryDock.Diff(expected, draft);
            var ids = new HashSet<string>();
            foreach (var e in dock.entries) if (e != null) ids.Add(e.moduleId);
            foreach (var d in diff) ids.Add(d.moduleId);
            foreach (var id in ids)
            {
                int now = dock.Count(id);
                int after = now;
                foreach (var d in diff) if (d.moduleId == id) after -= d.delta;
                string name = id;
                if (lib != null && lib.TryGet(id, out var def)) name = ModuleLibrary.Name(def);
                rows.Add(new DryDockRow { moduleId = id, name = name, inDockNow = now, inDockAfterApply = after });
            }
            rows.Sort((a, b) => string.Compare(a.moduleId, b.moduleId, StringComparison.Ordinal));
            return rows;
        }
    }
}
