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
