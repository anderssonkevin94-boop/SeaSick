using System.Collections.Generic;
using SeaSick.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    // Read-only, pooled annotations. Never cover the sheet, chart, or another label.
    internal sealed class BuildingStatusLabels
    {
        const int Limit = 12;
        readonly Label[] labels = new Label[Limit];
        readonly List<(Vector3 at, string status)> warnings = new List<(Vector3, string)>();
        readonly List<Rect> occupied = new List<Rect>();
        readonly VisualElement chart;
        Outpost previous;
        float nextRefresh;

        public BuildingStatusLabels(VisualElement root)
        {
            chart = root.Q(className: "chart");
            for (int i = 0; i < Limit; i++)
            {
                var label = new Label { pickingMode = PickingMode.Ignore };
                label.AddToClassList("land-building-status");
                label.style.display = DisplayStyle.None;
                root.Add(label); labels[i] = label;
            }
        }

        public void Hide()
        {
            foreach (var label in labels) label.style.display = DisplayStyle.None;
        }

        internal static string Status(OutpostLedger ledger, StationStock station)
        {
            if (station == null) return null;
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

        public void Tick(Outpost camp, float scale)
        {
            if (camp == null || Camera.main == null) { Hide(); return; }
            if (previous != camp || Time.unscaledTime >= nextRefresh)
            {
                previous = camp; nextRefresh = Time.unscaledTime + .5f;
                warnings.Clear();
                for (int i = 0; i < camp.Built.Count; i++)
                {
                    var building = camp.Built[i];
                    if (building == null) continue;
                    string status = Status(camp.Ledger, camp.Ledger.StationForRaised(i));
                    if (status != null) warnings.Add((building.transform.position, status));
                }
                // **A blueprint nothing can supply** (Kevin, 2026-09-27): a
                // small warning over the site; the reason is on its sheet.
                var sites = camp.Ledger != null ? camp.Ledger.sites : null;
                if (sites != null)
                    foreach (var site in sites)
                    {
                        string issue = camp.Ledger.SiteIssueShort(site);
                        if (issue == null) continue;
                        var at = site.At;
                        at.y = camp.GroundAt(at);
                        warnings.Add((at, issue));
                    }
            }
            occupied.Clear();
            int count = 0;
            var camera = Camera.main;
            var safe = Screen.safeArea;
            var guiSafe = new Rect(safe.x, Screen.height-safe.yMax, safe.width, safe.height);
            Rect chartRect = chart != null ? chart.worldBound : Rect.zero;
            chartRect = new Rect(chartRect.position / scale, chartRect.size / scale);
            foreach (var warning in warnings)
            {
                if (count >= Limit) break;
                var point = camera.WorldToScreenPoint(warning.at + Vector3.up * 4f);
                if (point.z <= 0f) continue;
                var rect = new Rect(point.x-55f/scale, Screen.height-point.y-26f/scale, 110f/scale, 24f/scale);
                if (!guiSafe.Contains(rect.min) || !guiSafe.Contains(rect.max)
                    || rect.Overlaps(MidnightLandHud.ResourcesRect) || rect.Overlaps(MidnightLandHud.NavigationRect)
                    || rect.Overlaps(chartRect)
                    || (SheetHost.FrameOpen && rect.Overlaps(SheetHost.FrameRect))) continue;
                bool overlap = false;
                foreach (var taken in occupied) if (rect.Overlaps(taken)) { overlap = true; break; }
                if (overlap) continue;
                var label = labels[count++]; label.text = warning.status;
                label.style.left = rect.x * scale; label.style.top = rect.y * scale;
                label.style.display = DisplayStyle.Flex;
                occupied.Add(rect);
            }
            for (int i = count; i < Limit; i++) labels[i].style.display = DisplayStyle.None;
        }
    }
}
