using System;
using System.Collections.Generic;
using SeaSick.Ship.Modular;

namespace SeaSick.UI.ModularYard
{
    // Adapter boundary: implementation belongs to the gameplay integration.
    // TryApply must compare expected with live state and commit atomically.
    public interface IShipyardRefit
    {
        ShipConfiguration ReadCurrent();
        string Validate(ShipConfiguration draft);
        bool TryApply(ShipConfiguration expected, ShipConfiguration draft, out string reason);
        // ---- equipment + dry dock (2026-09-25, docs/SHIPYARD-API.md §15) --
        // Pure: never touch the ship, the dock or the save.
        IReadOnlyList<EquipmentSlotView> EquipmentSlots(ShipConfiguration draft);
        ShipyardEdit FitEquipment(ShipConfiguration draft, string slotId, string moduleId);
        ShipyardEdit RemoveEquipment(ShipConfiguration draft, string slotId);
    }

    public sealed class ShipyardDraft
    {
        readonly ModuleLibrary library;
        readonly IShipyardRefit backend;
        readonly Func<ShipConfiguration, int, string> removalBlocker;
        readonly Func<string, string, bool> allowed;
        readonly ShipConfiguration baseline;
        ShipConfiguration draft;
        readonly Stack<ShipConfiguration> undo = new Stack<ShipConfiguration>();
        public AssemblyResult Assembly { get; private set; }
        public string Message { get; private set; } = "";
        public string Highlight { get; private set; }
        public bool Committed { get; private set; }
        public event Action Changed;
        public int Count => draft.middleIds.Count;
        public int Maximum => Math.Min(3, library.MaxMiddles);
        public string Rotor => draft.rotorId;
        public bool Dirty => !draft.ValueEquals(baseline);
        public bool CanUndo => undo.Count > 0 && !Committed;
        public bool HasBackend => backend != null;
        public ShipConfiguration Snapshot() => draft.Clone();
        public float OriginalLength { get; }

        public ShipyardDraft(ModuleLibrary library, ShipConfiguration current, IShipyardRefit backend = null,
            Func<ShipConfiguration, int, string> removalBlocker = null,
            Func<string, string, bool> allowed = null)
        {
            this.library = library ?? throw new ArgumentNullException(nameof(library));
            this.backend = backend;
            this.removalBlocker = removalBlocker;
            this.allowed = allowed;
            baseline = (current ?? throw new ArgumentNullException(nameof(current))).Clone();
            draft = baseline.Clone();
            Assembly = ShipAssembler.Assemble(draft, library);
            if (!Assembly.ok) throw new ArgumentException(Reason(Assembly));
            OriginalLength = Assembly.overallLengthM;
        }

        static string Reason(AssemblyResult result) => result.rejections.Count == 0
            ? "This configuration is unavailable." : result.rejections[0].message;

        bool Refuse(string reason) { Message = reason; Changed?.Invoke(); return false; }

        bool Set(ShipConfiguration next, string highlight)
        {
            if (Committed) return Refuse("This refit is already confirmed.");
            var result = ShipAssembler.Assemble(next, library);
            if (!result.ok) return Refuse(Reason(result));
            if (next.ValueEquals(draft)) return false;
            undo.Push(draft.Clone()); draft = next; Assembly = result;
            Highlight = highlight; Message = ""; Changed?.Invoke(); return true;
        }

        /// True while the draft is built from the W1x (expanded-beam) family;
        /// false for the standard W1-r2 family (docs/SHIPYARD-API.md §15).
        public bool IsWideBeam => draft.sternId == ExpandedPresets.ExpandedStern;

        /// The middle module id for the draft's OWN current width family --
        /// never the standard one outright (that was the 2026-09-25 bug: a
        /// wide-beam ship's AddMiddle used to add a W1-r2 middle, which
        /// ShipAssembler refuses to join to a W1x stern/bow).
        string MiddleIdForWidth() => IsWideBeam ? ExpandedPresets.ExpandedMiddle : ShipConfiguration.V3Middle;

        // First prototype appends/removes the bay immediately behind the bow.
        // Existing bay indices, and therefore equipment references, never shift.
        public bool AddMiddle()
        {
            string middleId = MiddleIdForWidth();
            if (!CanSelect(ModuleKind.Middle, middleId)) return Refuse("This section is unavailable.");
            if (Count >= Maximum) return Refuse("Maximum length for this hull.");
            var next = Snapshot(); next.middleIds.Add(middleId);
            return Set(next, ShipAssembler.MiddleKey(Count));
        }

        public bool RemoveMiddle()
        {
            if (Count == 0) return Refuse("The bow and stern must remain.");
            if (backend != null && removalBlocker == null)
                return Refuse("Section availability is not connected yet.");
            string reason = RemovalReason();
            if (!string.IsNullOrEmpty(reason)) return Refuse(reason);
            var next = Snapshot();
            int idx = Count - 1;
            next.middleIds.RemoveAt(idx);
            // A gun standing on the bay that just left has nowhere to stand
            // any more (its slot id is gone); take it off to the dry dock
            // instead of refusing the shrink (2026-09-25, Kevin: "guns
            // without a slot go to the dry dock", the same equipment API a
            // player would use by hand -- see docs/SHIPYARD-API.md §15).
            string prefix = ShipAssembler.MiddleKey(idx) + "/";
            int orphaned = next.equipment.RemoveAll(e => e != null && e.slotId != null && e.slotId.StartsWith(prefix));
            bool applied = Set(next, "bow");
            if (applied && orphaned > 0)
            {
                Message = orphaned == 1 ? "1 gun will go to the dry dock." : $"{orphaned} guns will go to the dry dock.";
                Changed?.Invoke();
            }
            return applied;
        }

        /// Swaps EVERY hull section between the W1-r2 and W1x families at
        /// once -- the two widths never mix (docs/SHIPYARD-API.md §9,
        /// enforced by ShipAssembler's join-profile check). Equipment is
        /// left untouched: the two families share the same slot ids (only
        /// their Y moved), so a fitted gun stays fitted.
        public bool SetWideBeam(bool wide)
        {
            if (IsWideBeam == wide) return false;
            string sternId = wide ? ExpandedPresets.ExpandedStern : ShipConfiguration.V3Stern;
            string bowId = wide ? ExpandedPresets.ExpandedBow : ShipConfiguration.V3Bow;
            if (!CanSelect(ModuleKind.Stern, sternId) || !CanSelect(ModuleKind.Bow, bowId))
                return Refuse("This beam is unavailable.");
            var next = Snapshot();
            next.sternId = sternId;
            next.bowId = bowId;
            string middleId = wide ? ExpandedPresets.ExpandedMiddle : ShipConfiguration.V3Middle;
            for (int i = 0; i < next.middleIds.Count; i++) next.middleIds[i] = middleId;
            return Set(next, ShipAssembler.StdKeyStern);
        }

        // ---- guns + dry dock (2026-09-25) ------------------------------

        /// Every deck-gun slot of the draft (empty or fitted), for the
        /// screen's gun rows.
        public IReadOnlyList<EquipmentSlotView> EquipmentSlots() =>
            backend != null ? backend.EquipmentSlots(Snapshot()) : Array.Empty<EquipmentSlotView>();

        /// Fits a deck cannon on an empty slot. Whether one is actually in
        /// the dry dock is checked at Confirm time (ApplyRefit); bind a
        /// picker's enabled state to the report's `dryDock` rows so a tap
        /// that cannot succeed is never offered.
        public bool FitGun(string slotId)
        {
            if (Committed) return Refuse("This refit is already confirmed.");
            if (backend == null) return Refuse("Live refitting is not connected.");
            var edit = backend.FitEquipment(Snapshot(), slotId, ShipConfiguration.EquipmentCannon);
            if (!edit.ok) return Refuse(edit.message);
            return Set(edit.draft, slotId);
        }

        /// Sends the gun fitted at `slotId` to the dry dock.
        public bool RemoveGun(string slotId)
        {
            if (Committed) return Refuse("This refit is already confirmed.");
            if (backend == null) return Refuse("Live refitting is not connected.");
            var edit = backend.RemoveEquipment(Snapshot(), slotId);
            if (!edit.ok) return Refuse(edit.message);
            return Set(edit.draft, slotId);
        }

        public bool ChooseWheel(string id)
        {
            if (!CanSelect(ModuleKind.Rotor, id)) return Refuse("This wheel is unavailable.");
            if (id != ShipConfiguration.TimberRotor && id != ShipConfiguration.ReinforcedRotor)
                return Refuse("That wheel is not available in this shipyard yet.");
            var next = Snapshot(); next.rotorId = id;
            return Set(next, ShipAssembler.StdKeyRotor);
        }

        public void Undo()
        {
            if (!CanUndo) return;
            draft = undo.Pop(); Assembly = ShipAssembler.Assemble(draft, library);
            Highlight = null; Message = ""; Changed?.Invoke();
        }

        public bool CanSelect(string kind, string id) => allowed == null || allowed(kind, id);
        public string RemovalReason() => Count == 0 ? "The bow and stern must remain." :
            backend != null && removalBlocker == null ? "Section availability is not connected yet." :
            removalBlocker?.Invoke(Snapshot(), Count - 1);

        public string CannotConfirm()
        {
            if (Committed) return "This refit is already confirmed.";
            if (!Dirty) return "No changes yet.";
            if (backend == null) return "Preview only. Live refitting is not connected.";
            return backend.Validate(Snapshot());
        }

        public bool Confirm()
        {
            string reason = CannotConfirm();
            if (!string.IsNullOrEmpty(reason)) return Refuse(reason);
            if (!backend.TryApply(baseline.Clone(), Snapshot(), out reason))
                return Refuse(string.IsNullOrEmpty(reason) ? "Refit could not be applied. Your ship is unchanged." : reason);
            Committed = true; undo.Clear(); Message = "Refit confirmed.";
            Changed?.Invoke(); return true;
        }
    }
}
