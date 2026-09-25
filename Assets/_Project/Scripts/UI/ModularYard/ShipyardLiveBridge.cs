using System.Collections.Generic;
using SeaSick.Ship.Modular;
using UnityEngine;

namespace SeaSick.UI.ModularYard
{
    // Keep backend types independent of the presentation assembly.
    public sealed class ShipyardLiveBridge : IShipyardRefit
    {
        readonly ShipyardRefitAdapter adapter = new ShipyardRefitAdapter();
        string reportKey;
        float reportAt = float.NegativeInfinity;
        ShipyardReport report;
        ShipyardService reportOwner;

        public ShipConfiguration ReadCurrent() => adapter.ReadCurrent();
        public string Validate(ShipConfiguration draft) => adapter.Validate(draft);
        public bool TryApply(ShipConfiguration expected, ShipConfiguration draft, out string reason)
        {
            reportKey = null;
            return adapter.TryApply(expected, draft, out reason);
        }

        public ShipyardReport Report(ShipConfiguration draft)
        {
            var owner = ShipyardService.Player;
            string key = draft.ToJson();
            if (owner != reportOwner || key != reportKey || Time.unscaledTime - reportAt >= .25f)
            {
                report = adapter.Report(draft); reportOwner = owner;
                reportKey = key; reportAt = Time.unscaledTime;
            }
            return report;
        }

        public IReadOnlyList<EquipmentSlotView> EquipmentSlots(ShipConfiguration draft) => adapter.EquipmentSlots(draft);
        public ShipyardEdit FitEquipment(ShipConfiguration draft, string slotId, string moduleId) => adapter.FitEquipment(draft, slotId, moduleId);
        public ShipyardEdit RemoveEquipment(ShipConfiguration draft, string slotId) => adapter.RemoveEquipment(draft, slotId);

        // ---- Step 2 backend seam (docs/SHIPYARD-SECTIONS-UI.md) -----------
        // ISOLATED ON PURPOSE: `ShipConfiguration.layouts`/`SectionLayout`,
        // `ShipyardService.SectionSpace`/`WithBerths` and a middle-key
        // shifter for `layouts` are being built in parallel, in a different
        // worktree, under these exact names -- they do not exist in THIS
        // worktree yet, so every call into them is kept to this one block
        // (plus the Interior page in ShipyardSectionSheet.cs) so reconciling
        // the two branches only ever touches these few lines.

        /// This section's space budget/berths/hold (pure). Backend:
        /// `ShipyardService.SectionSpace(ShipConfiguration, string)`.
        public SectionSpaceView SectionSpace(ShipConfiguration draft, string sectionKey) =>
            ShipyardService.SectionSpace(draft, sectionKey);

        /// A new draft with `sectionKey`'s berths set to `berths` (clamped),
        /// hold recomputed from what is left of the section's budget.
        /// Backend: `ShipyardService.WithBerths(ShipConfiguration, string, int)`.
        public ShipConfiguration WithBerths(ShipConfiguration draft, string sectionKey, int berths) =>
            ShipyardService.WithBerths(draft, sectionKey, berths);

        /// Renumbers `layouts`' own `middle[i]` keys the same way
        /// `ShipyardDraft.RenumberMiddleKeys` renumbers `equipment` --
        /// wired in as `ShipyardDraft`'s `renumberLayouts` delegate so
        /// `InsertMiddle`/`RemoveSection` never reference the backend type
        /// directly. Backend: `ShipConfiguration.ShiftMiddleKeys` (named
        /// "maybe" in the spec; reconcile the exact name/signature here).
        public void RenumberLayouts(ShipConfiguration cfg, int fromIndex, int delta) =>
            ShipConfiguration.ShiftMiddleKeys(cfg, fromIndex, delta);

        public string RemovalBlocker(ShipConfiguration draft, int index)
        {
            var section = Report(draft)?.Section(ShipAssembler.MiddleKey(index));
            if (section == null) return "Section availability could not be checked.";
            return section.canRemove ? null : string.IsNullOrEmpty(section.reason) ? "This section is occupied." : section.reason;
        }

        public bool Allowed(string kind, string id)
        {
            var ids = ShipyardService.Player?.AllowedModuleIds(kind);
            if (ids != null) foreach (string candidate in ids) if (candidate == id) return true;
            return false;
        }

        public GameObject BuildPreview(ShipConfiguration draft, Transform parent) =>
            ShipyardService.Player != null ? ShipyardService.Player.BuildPreview(draft, parent) : null;

        public static void Open()
        {
            if (ShipyardService.Player == null || ShipyardModal.IsOpen) return;
            var bridge = new ShipyardLiveBridge();
            // End island gestures before the modal takes exclusive input.
            CampSiting.End(); WallSiting.End(); Hand.Instance?.Cancel();
            ShipyardModal.Open(bridge, ShipyardSession.SetWorldInputBlocked, bridge.RemovalBlocker);
            Sheets.Sheets.Close();
        }
    }
}
