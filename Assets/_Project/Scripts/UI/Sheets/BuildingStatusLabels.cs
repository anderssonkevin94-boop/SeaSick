using System.Collections.Generic;
using SeaSick.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    /// **A station's one-word state** ("No worker", "Output full", "Needs
    /// supplies") for the sheets that list stations. It used to also float
    /// over the buildings as world labels and an "No worker · Assign"
    /// button; Kevin, 2026-09-30: "i dont want that text on screen" -- the
    /// world carries no text, the Camp sheet lists unmanned stations with
    /// their Assign button.
    internal static class BuildingStatusLabels
    {
        internal static string Status(OutpostLedger ledger, StationStock station)
        {
            if (station == null) return null;
            if (ledger.StationUnmanned(station)) return "No worker";
            if (station.RackFull) return "Output full";
            OutpostHand worker = null;
            foreach (var hand in ledger.hands)
                if (hand != null && hand.order == OutpostOrder.Work && ledger.StationOfHand(hand) == station)
                { worker = hand; break; }
            if (worker == null) return null;
            if (!station.HasOrder || station.benchState != BenchState.Empty || worker.Hauling) return null;
            // Use the economy's actual blocker, not an empty bay that is being supplied.
            string reason = ledger.StallReason(worker);
            return reason != null && (reason.StartsWith("waiting for ", System.StringComparison.Ordinal)
                || reason.StartsWith("needs a ", System.StringComparison.Ordinal)) ? "Needs supplies" : null;
        }
    }
}
