#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SeaSick.UI.Sheets
{
    internal static class ProductionUxReview
    {
        [MenuItem("SeaSick/Land UI/Check Food And Building Status")]
        static void CheckStatus()
        {
            var ledger = new SeaSick.World.OutpostLedger();
            ledger.built.Add(SeaSick.World.BuildPlans.Sawmill.id);
            ledger.EnsureStations();
            var station = ledger.StationOf(SeaSick.World.BuildPlans.Sawmill.id);
            void Require(bool condition, string name)
            { if (!condition) throw new InvalidOperationException("Land HUD: " + name); }
            Require(SheetBits.FoodDays(ledger) < 0f, "empty camp food estimate");
            Require(BuildingStatusLabels.Status(ledger, station) == null, "unassigned station (no worker sign, by design)");
            var hand = new SeaSick.World.OutpostHand { order = SeaSick.World.OutpostOrder.Work,
                target = SeaSick.World.BuildPlans.Sawmill.id };
            ledger.hands.Add(hand);
            ledger.stores.Add(new SeaSick.World.OutpostStore { resource = SeaSick.World.Res.Food, whole = 4 });
            ledger.rations = SeaSick.World.Rations.Full;
            Require(Mathf.Approximately(SheetBits.FoodDays(ledger),4f), "full ration duration");
            station.Bay(SeaSick.World.Res.Food,true).whole = 20;
            Require(Mathf.Approximately(SheetBits.FoodDays(ledger),4f), "reserved food excluded");
            ledger.rations = SeaSick.World.Rations.None;
            Require(SheetBits.FoodDays(ledger) < 0f, "rations off");
            Require(BuildingStatusLabels.Status(ledger,station) == null, "idle is not missing supplies");
            station.orderRecipe = "boards"; station.orderRepeat = true;
            Require(BuildingStatusLabels.Status(ledger,station) == "Needs supplies", "exhausted input");
            station.benchState = SeaSick.World.BenchState.Working;
            Require(BuildingStatusLabels.Status(ledger,station) == null, "working is not missing supplies");
            station.Rack(SeaSick.World.Res.Boards,true).whole = station.OutputCap;
            Require(BuildingStatusLabels.Status(ledger,station) == "Output full", "full output");
            Require(MidnightLandHud.CompactCount(12500) == "12.5k", "large count formatting");
            File.WriteAllText("/tmp/seasick-land-status.txt", "PASS: food duration, no crew, rations off, reserved food exclusion, unassigned, idle, missing inputs, working, full output, compact counts.\n");
            Debug.Log("Land HUD food and status checks passed.");
        }

        [MenuItem("SeaSick/Land UI/Clear Temporary Review Plot")]
        static void ClearPlot()
        {
            if (!EditorApplication.isPlaying || !SeaSick.Save.SaveGame.Suppressed || !(Sheets.Current is StationSheet))
                throw new InvalidOperationException("Only use in the protected, disposable review fixture.");
            var camp = MidnightLandHud.Camp;
            var wood = camp.GetComponentInChildren<SeaSick.Terrain.SceneryWood>();
            if (wood != null) camp.Ledger.treesFelled += wood.FellWithin(Sheets.Current.AnchorWorld,10f);
        }

        [MenuItem("SeaSick/Land UI/Check Production UX")]
        static void Check()
        {
            CheckProjection();
            if (!EditorApplication.isPlaying || !(Sheets.Current is StationSheet))
                throw new InvalidOperationException("Open a production building in a protected play session first.");
            SheetHost.Instance.StartCoroutine(Capture());
        }

        static void CheckProjection()
        {
            var go = new GameObject("UI framing check") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var camera = go.AddComponent<Camera>(); camera.enabled = false;
                camera.fieldOfView = 36f;
                foreach (float aspect in new[] { 1080f/2340f, 1920f/1080f })
                foreach (float tilt in new[] { 25f, 32f, 60f, 80f })
                foreach (float yaw in new[] { 0f, 80f, 190f })
                {
                    camera.aspect = aspect;
                    var rotation = Quaternion.Euler(tilt,yaw,0f);
                    var subject = new Vector3(117f,38f,-81f);
                    var target = aspect < 1f ? new Vector2(.5f,.73f) : new Vector2(.32f,.5f);
                    var pivot = BuildingSheetFocus.PivotFor(subject,rotation,36f,80f,target,aspect);
                    float span = 80f/(2f*Mathf.Tan(18f*Mathf.Deg2Rad));
                    camera.transform.SetPositionAndRotation(pivot - rotation*Vector3.forward*span,rotation);
                    Vector3 actual = camera.WorldToViewportPoint(subject);
                    if (actual.z <= 0f || Vector2.Distance(actual,target) > .001f)
                        throw new InvalidOperationException($"Framing failed at {aspect}, {tilt}, {yaw}: {actual}");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
            Debug.Log("Production UX: 24 framing projection cases passed.");
        }

        static IEnumerator Capture()
        {
            yield return new WaitForSecondsRealtime(2f);
            yield return new WaitForEndOfFrame();
            var sheet = Sheets.Current as StationSheet;
            if (sheet == null) yield break;
            var report = new StringBuilder();
            int failures = 0;
            // **Obsolete gate, updated 2026-09-27.** `StationSheet` used to be
            // a two-tab Production/Upgrade card; the station-page redesign
            // (concept A2, one tall page per building) dropped tabs entirely
            // -- `TabLabels` now always returns null by design. The old
            // assertion here failed on every run since that redesign landed,
            // not because the page was broken. The gate now asserts the
            // CURRENT contract (no tabs) instead of the old one, so a
            // regression that brought tabs back would still be caught.
            var labels = sheet.TabLabels;
            report.AppendLine("Tabs: " + (labels == null ? "none (one-page station card)" : string.Join(", ", labels)));
            if (labels != null) failures++;
            var root = SheetHost.Instance.GetComponent<UIDocument>().rootVisualElement;
            var body = root.Q(className:"sheet-body");
            var card = root.Q(className:"sheet-card");
            var resources = root.Q(className:"land-resources");
            resources.Query<Label>().ForEach(label =>
            {
                var bounds = label.worldBound;
                if (!resources.worldBound.Contains(bounds.min) || !resources.worldBound.Contains(bounds.max))
                { failures++; report.AppendLine("FAIL resource bounds: " + label.text); }
            });
            root.Query<VisualElement>().ForEach(element =>
            {
                if (!(element is Button) && !(element is DropdownField) && !(element is Label)) return;
                if (!card.Contains(element)) return;
                for (var p = element; p != null; p = p.parent)
                    if (p.resolvedStyle.display == DisplayStyle.None || p.resolvedStyle.visibility == Visibility.Hidden) return;
                var rect = element.worldBound;
                if (rect.width < 1f || rect.height < 1f) return;
                var container = body.Contains(element) ? body.LocalToWorld(body.contentRect) : card.worldBound;
                if (rect.xMin < container.xMin-1f || rect.xMax > container.xMax+1f || rect.yMin < container.yMin-1f || rect.yMax > container.yMax+1f)
                {
                    failures++;
                    report.AppendLine($"FAIL bounds: {element.GetType().Name} {(element as TextElement)?.text} {rect} in {container}");
                }
            });
            var camera = Camera.main;
            Vector3 point = camera.WorldToScreenPoint(sheet.AnchorWorld);
            var islandCamera = UnityEngine.Object.FindFirstObjectByType<SeaSick.CameraRig.IslandCam>();
            report.AppendLine($"Camera ready: {islandCamera.Ready}; focus: {islandCamera.FocusPoint}; ground: {islandCamera.Ground}; subject: {sheet.AnchorWorld}");
            var sight = new Ray(camera.transform.position, sheet.AnchorWorld-camera.transform.position);
            if (SeaSick.CameraRig.GroundPick.Along(sight,out var hit))
                report.AppendLine($"Terrain hit {hit}; distance before anchor: {Vector3.Distance(camera.transform.position,sheet.AnchorWorld)-Vector3.Distance(camera.transform.position,hit):0.00}m");
            Vector2 guiPoint = new Vector2(point.x, Screen.height-point.y);
            bool visible = point.z > 0f && Screen.safeArea.Contains((Vector2)point)
                && !SheetHost.FrameRect.Contains(guiPoint) && !MidnightLandHud.ResourcesRect.Contains(guiPoint);
            report.AppendLine($"Building ground anchor: {point}; outside UI: {visible}; sheet: {SheetHost.FrameRect}");
            if (!visible) failures++;
            report.AppendLine(failures == 0 ? "PASS" : $"FAIL {failures}");
            File.WriteAllText("/tmp/seasick-production-ux.txt",report.ToString());
            ScreenCapture.CaptureScreenshot("/tmp/seasick-production-ux.png");
            Debug.Log(report.ToString());
        }
    }
}
#endif
