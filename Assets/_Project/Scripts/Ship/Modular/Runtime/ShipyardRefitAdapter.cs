using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.Ship.Modular
{
    /// **Astra's seam** (A8): exactly the three methods her staged shipyard
    /// UI calls through `SeaSick.UI.ModularYard.IShipyardRefit`. This class
    /// does NOT reference that interface; she adds `: IShipyardRefit` when
    /// she integrates. Everything delegates to the player's
    /// `ShipyardService`, the one authoritative calculation -- the UI only
    /// displays what comes back.
    public class ShipyardRefitAdapter
    {
        readonly ShipyardService fixedService;
        string cacheKey;
        string cacheAnswer;

        /// `service` null = the player's (`ShipyardService.Player`), looked
        /// up on every call so a scene reload cannot leave it stale.
        public ShipyardRefitAdapter(ShipyardService service = null) { fixedService = service; }

        ShipyardService S => fixedService != null ? fixedService : ShipyardService.Player;

        /// A snapshot of the live ship's configuration (a copy).
        public ShipConfiguration ReadCurrent()
        {
            var s = S;
            return s != null ? s.Current : ShipConfiguration.Long();
        }

        /// Null/empty = the draft can be applied now. Otherwise one readable
        /// line per blocking reason (and why she cannot be refitted right
        /// now). Read-only; cached by the draft JSON and the live ship's
        /// state, so calling it every 250 ms costs a string compare.
        public string Validate(ShipConfiguration draft)
        {
            var s = S;
            if (s == null) return "There is no ship to refit.";
            if (draft == null) return "There is no design to check.";
            var snap = s.Snapshot();
            bool now = s.CanRefitNow(out string why);
            string key = draft.ToJson() + "|" + snap.config.ToJson() + "|" + snap.totalHeld + "|" + snap.kindsOnDeck + "|" + snap.crewAboard + "|" + why;
            if (key == cacheKey) return cacheAnswer;
            var v = s.Validate(draft);
            var lines = new List<string>();
            foreach (var i in v.issues) lines.Add(i.message);
            if (!now) lines.Add(why);
            cacheKey = key;
            cacheAnswer = lines.Count == 0 ? null : string.Join("\n", lines);
            return cacheAnswer;
        }

        /// Apply `draft` if the live ship still is `expected`; persists
        /// through the game's save. On false nothing changed and `reason`
        /// says why.
        public bool TryApply(ShipConfiguration expected, ShipConfiguration draft, out string reason)
        {
            var s = S;
            if (s == null) { reason = "There is no ship to refit."; return false; }
            var r = s.ApplyRefit(expected, draft);
            cacheKey = null;
            reason = r.ok ? "" : r.ToString();
            return r.ok;
        }

        /// The richer current-vs-proposed report for the display.
        public ShipyardReport Report(ShipConfiguration draft)
        {
            var s = S;
            return s != null ? s.Report(draft) : null;
        }

        // ---- equipment + dry dock (2026-09-25, docs/SHIPYARD-API.md §15) --

        public IReadOnlyList<EquipmentSlotView> EquipmentSlots(ShipConfiguration draft)
        {
            var s = S;
            return s != null ? s.EquipmentSlots(draft) : new List<EquipmentSlotView>();
        }

        public ShipyardEdit FitEquipment(ShipConfiguration draft, string slotId, string moduleId)
        {
            var s = S;
            if (s != null) return s.FitEquipment(draft, slotId, moduleId);
            return new ShipyardEdit { ok = false, code = "NO_SHIP", message = "There is no ship to refit.", draft = draft };
        }

        public ShipyardEdit RemoveEquipment(ShipConfiguration draft, string slotId)
        {
            var s = S;
            if (s != null) return s.RemoveEquipment(draft, slotId);
            return new ShipyardEdit { ok = false, code = "NO_SHIP", message = "There is no ship to refit.", draft = draft };
        }
    }
}
