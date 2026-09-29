using System.Collections.Generic;
using SeaSick.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    // Pooled BUILDING annotations and station assignment buttons ("No worker · Assign",
    // "Needs supplies", "Output full"). No per-villager status words (Kevin, 2026-09-30).
    internal sealed class BuildingStatusLabels
    {
        const int Limit = 12;
        readonly Button[] labels = new Button[Limit];
        readonly StationStock[] targets = new StationStock[Limit];
        readonly StationStock[] pressed = new StationStock[Limit];
        readonly VisualElement root;
        readonly List<(Vector3 at, string status, StationStock station)> warnings = new List<(Vector3, string, StationStock)>();
        readonly List<Rect> occupied = new List<Rect>();
        readonly VisualElement chart;
        Outpost previous;
        float nextRefresh;

        public BuildingStatusLabels(VisualElement root)
        {
            this.root = root;
            chart = root.Q(className: "chart");
            for (int i = 0; i < Limit; i++)
            {
                int index = i;
                var label = new Button(() => Assign(index)) { pickingMode = PickingMode.Ignore };
                label.RegisterCallback<PointerDownEvent>(_ => pressed[index] = targets[index], TrickleDown.TrickleDown);
                label.RegisterCallback<KeyDownEvent>(_ => pressed[index] = targets[index]);
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

        void Assign(int index)
        {
            var station = targets[index];
            if (station != pressed[index]) return;
            pressed[index] = null;
            var ledger = previous != null ? previous.Ledger : null;
            if (ledger == null || !ledger.StationUnmanned(station)) return;
            var hand = ledger.FreeHandFor(station.planId);
            if (hand != null) previous.Assign(hand, station);
            nextRefresh = 0f;
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
                    if (status != null) warnings.Add((building.transform.position, status, camp.Ledger.StationForRaised(i)));
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
                        warnings.Add((at, issue, null));
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
                bool unmanned = warning.status == "No worker";
                float width = unmanned ? 158f : 110f, height = unmanned ? 44f : 24f;
                var rect = new Rect(point.x-width*.5f/scale, Screen.height-point.y-height/scale, width/scale, height/scale);
                if (!guiSafe.Contains(rect.min) || !guiSafe.Contains(rect.max)
                    || rect.Overlaps(MidnightLandHud.ResourcesRect) || rect.Overlaps(MidnightLandHud.NavigationRect)
                    || rect.Overlaps(ThumbBar.Rect) || rect.Overlaps(ThumbBar.CardRect) || rect.Overlaps(NextCard.Rect)
                    || rect.Overlaps(chartRect)
                    || (SheetHost.FrameOpen && rect.Overlaps(SheetHost.FrameRect))) continue;
                bool overlap = false;
                foreach (var taken in occupied) if (rect.Overlaps(taken)) { overlap = true; break; }
                if (overlap) continue;
                targets[count] = unmanned ? warning.station : null;
                var label = labels[count++];
                label.text = unmanned ? "No worker · Assign" : warning.status;
                label.pickingMode = unmanned ? PickingMode.Position : PickingMode.Ignore;
                label.SetEnabled(!unmanned || camp.Ledger.FreeHandFor(warning.station.planId) != null);
                label.style.width = width; label.style.height = height;
                label.tooltip = unmanned ? "Assign a free hand to this station" : warning.status;
                label.style.left = rect.x * scale; label.style.top = rect.y * scale;
                label.style.display = DisplayStyle.Flex;
                occupied.Add(rect);
            }
            for (int i = count; i < Limit; i++) labels[i].style.display = DisplayStyle.None;
        }
    }
}
