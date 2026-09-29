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
            if(CoasterFamily.Is(draft)&&ShipyardService.Player!=null)
            {
                var service=ShipyardService.Player;var builds=new List<string>();
                foreach(var row in service.DryDockPreview(draft))
                    if(row.moduleId==ShipConfiguration.EquipmentCannon)
                        for(int i=0;i<-row.inDockAfterApply;i++)builds.Add(ModuleCatalog.Cannon);
                var result=service.ApplyRefit(expected,draft,builds);reason=result.ok?"":result.ToString();return result.ok;
            }
            return adapter.TryApply(expected, draft, out reason);
        }

        public ShipyardReport Report(ShipConfiguration draft)
        {
            var owner = ShipyardService.Player;
            string key = draft.ToJson();
            if (owner != reportOwner || key != reportKey || Time.unscaledTime - reportAt >= .25f)
            {
                report = adapter.Report(draft);
                if(CoasterFamily.Is(draft)&&report!=null)
                    foreach(var row in report.dryDock)if(row.moduleId==ShipConfiguration.EquipmentCannon&&row.inDockAfterApply<0)
                    {int count=-row.inDockAfterApply;row.inDockAfterApply=0;report.warnings.Add(new ShipyardNote{code="CANNONS_BUILT",message=$"{count} new cannon(s) will be built and fitted (prototype: free)."});}
                reportOwner = owner;
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
            ShipyardService.Player?.SectionSpace(draft, sectionKey);

        /// A new draft with `sectionKey`'s berths set to `berths` (clamped),
        /// hold recomputed from what is left of the section's budget.
        /// Backend: `ShipyardService.WithBerths(ShipConfiguration, string, int)`.
        public ShipConfiguration WithBerths(ShipConfiguration draft, string sectionKey, int berths) =>
            ShipyardService.Player != null ? ShipyardService.Player.WithBerths(draft, sectionKey, berths) : draft;

        /// Renumbers `layouts`' own `middle[i]` keys the same way
        /// `ShipyardDraft.RenumberMiddleKeys` renumbers `equipment` --
        /// wired in as `ShipyardDraft`'s `renumberLayouts` delegate so
        /// `InsertMiddle`/`RemoveSection` never reference the backend type
        /// directly. Backend: `ShipConfiguration.ShiftMiddleKeys` (named
        /// "maybe" in the spec; reconcile the exact name/signature here).
        public void RenumberLayouts(ShipConfiguration cfg, int fromIndex, int delta)
        {
            // Draft already shifts ordinary equipment. The shared helper also
            // shifts fittings/layouts, so protect only those already-shifted keys.
            var keys=new Dictionary<EquipmentChoice,string>();
            foreach(var e in cfg.equipment)if(e?.slotId!=null&&!e.slotId.StartsWith("fitting:"))keys[e]=e.slotId;
            ShipConfiguration.ShiftMiddleKeys(cfg,fromIndex,delta);
            foreach(var pair in keys)pair.Key.slotId=pair.Value;
        }

        public string RemovalBlocker(ShipConfiguration draft, int index)
        {
            var section = Report(draft)?.Section(ShipAssembler.MiddleKey(index));
            if (section == null) return "Section availability could not be checked.";
            return section.canRemove ? null : string.IsNullOrEmpty(section.reason) ? "This section is occupied." : section.reason;
        }

        public bool Allowed(string kind, string id)
        {
            if (CoasterFamily.Is(id)) return ShipyardService.Player?.Library.TryGet(id,out _) == true;
            var ids = ShipyardService.Player?.AllowedModuleIds(kind);
            if (ids != null) foreach (string candidate in ids) if (candidate == id) return true;
            return false;
        }

        public GameObject BuildPreview(ShipConfiguration draft, Transform parent) =>
            ShipyardService.Player != null ? ShipyardService.Player.BuildPreview(draft, parent) : null;

        public static void Open()
        {
            if (ShipyardService.Player == null || ShipyardModal.IsOpen) return;
            // End island gestures before the modal takes exclusive input.
            CampSiting.End(); WallSiting.End(); Hand.Instance?.Cancel();
            if (!ShipyardModal.UseLegacyScreen && !CoasterFamily.Is(ShipyardService.Player.Current))
            {
                // The slot yard (2026-09-27): no 3D preview, so the live
                // ship stays where she is -- only world input is blocked.
                ShipyardModal.OpenSlots(ShipyardService.Player, ShipyardSession.SetWorldInputBlocked);
                Sheets.Sheets.Close();
                return;
            }
            var bridge = new ShipyardLiveBridge();
            ShipyardModal.Open(bridge, SetSessionBlocked, bridge.RemovalBlocker);
            Sheets.Sheets.Close();
        }

        /// **Live-ship hide/restore rides with the modal's own open/close**
        /// (2026-09-26, fix/yard-hide), not with every `WorldInputBlocked`
        /// flip: Pause and the Home screen drive that same static through
        /// `GameMenus.SetWorldBlocked` and must NOT hide her -- only the
        /// shipyard modal's own `Open`/`OnDisable` call this delegate.
        /// `ShipyardPreview` now stages the draft ship IN the world dry
        /// dock, so without this the live ship at her home-pier berth would
        /// render right alongside it (`ShipyardSession.SetLiveShipHidden`).
        static void SetSessionBlocked(bool blocked)
        {
            ShipyardSession.SetWorldInputBlocked(blocked);
            ShipyardSession.SetLiveShipHidden(blocked);
        }
    }
}
