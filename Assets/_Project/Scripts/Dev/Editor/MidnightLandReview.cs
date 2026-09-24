using System;
using System.IO;
using System.Text;
using SeaSick.UI.Sheets;
using SeaSick.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class MidnightLandReview
{
    [MenuItem("SeaSick/Land UI/Create Temporary Camp Fixture")]
    public static void Fixture()
    {
        ProtectSave();
        SeaSick.Save.GameBoot.Skip();
        SeaSick.UI.CampSiting.End();
        var anchor = UnityEngine.Object.FindFirstObjectByType<SeaSick.Ship.AnchorController>();
        if (anchor.CurrentIsland == null && !anchor.TryLand(out var why))
            throw new InvalidOperationException("Land near an island first.");
        var camp = Outpost.Of(anchor.CurrentIsland);
        if (camp == null) throw new InvalidOperationException("Survey must finish first; retry shortly.");
        if (!camp.HasCamp)
        {
            var fire = camp.Raise(BuildPlans.Campfire);
            if (fire == null) throw new InvalidOperationException("No suitable clearing for the temporary fire.");
            camp.Ledger.built.Add(BuildPlans.Campfire.id);
        }
        Building mill = null;
        foreach (var b in camp.Built) if (b.Id == BuildPlans.Sawmill.id) mill = b;
        for (int ring=0; ring<12 && mill==null; ring++)
        for (int i=0; i<16 && mill==null; i++)
        {
            float angle = i*Mathf.PI*2/16 + ring*.7f;
            var p = camp.CampCentre + new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*(10+ring*4);
            p.y = camp.GroundAt(p);
            if (camp.CanPlace(BuildPlans.Sawmill,p,out _)) mill = camp.Raise(BuildPlans.Sawmill,p);
        }
        if (mill == null) throw new InvalidOperationException("No suitable temporary sawmill site.");
        if (!camp.Ledger.built.Contains(BuildPlans.Sawmill.id)) camp.Ledger.built.Add(BuildPlans.Sawmill.id);
        camp.Ledger.Store(Res.Timber,true).whole = 8;
        camp.Ledger.Store(Res.Boards,true).whole = 4;
        var roster = UnityEngine.Object.FindFirstObjectByType<SeaSick.Crew.CrewRoster>();
        if (roster != null)
            foreach (var hand in roster.All)
                if (hand != null && camp.Ledger.hands.Count<3) camp.Station(hand);
        camp.ShowHands(true);
        foreach (var hand in camp.Ledger.hands) camp.OrderIdle(hand);
        var cam = UnityEngine.Object.FindFirstObjectByType<SeaSick.CameraRig.IslandCam>();
        if (cam != null) cam.LookAtGround(mill.transform.position,65f);
        TimeOfDay.SetTime01(.4f);
        MidnightLandHud.Enabled = true;
        Sheets.Open(new StationSheet(camp,mill));
        Debug.Log("Temporary land UI fixture created; saving suppressed. Stop Play to discard.");
    }

    [MenuItem("SeaSick/Land UI/Protect Playtest Save")]
    public static void ProtectSave()
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play mode first.");
        SeaSick.Save.SaveGame.Suppressed = true;
        Debug.Log("Land UI review: autosaving suppressed for this play session. Continue may safely load the existing save.");
    }

    [MenuItem("SeaSick/Land UI/Portrait View")]
    public static void Portrait() => PortraitGameView.Execute();

    [MenuItem("SeaSick/Land UI/Landscape View")]
    public static void Landscape() => DesktopGameView.Execute();

    [MenuItem("SeaSick/Land UI/Open Sawmill Preview")]
    public static void Preview()
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play mode at your camp first.");
        var camp = MidnightLandHud.Camp;
        if (camp == null) throw new InvalidOperationException("Anchor at a camp first. This preview does not alter your world.");
        foreach (var building in camp.Built)
        {
            if (building == null || building.Id != BuildPlans.Sawmill.id) continue;
            SeaSick.Save.GameBoot.Skip();
            MidnightLandHud.Enabled = true;
            Sheets.Open(new StationSheet(camp, building));
            return;
        }
        throw new InvalidOperationException("No built sawmill at this camp.");
    }

    [MenuItem("SeaSick/Land UI/Capture And Check Layout")]
    public static void Capture()
    {
        var host = SheetHost.Instance;
        if (!EditorApplication.isPlaying || host == null || !MidnightLandHud.Active)
            throw new InvalidOperationException("Open the Midnight island UI in Play mode first.");
        var root = host.GetComponent<UIDocument>().rootVisualElement;
        var report = new StringBuilder();
        report.AppendLine($"Panel {root.worldBound}; scale {SheetHost.PanelScale}. Bounds are panel coordinates.");
        int failures = 0;
        var card = root.Q(className: "sheet-card");
        var nav = root.Q(className: "land-nav");
        if (Sheets.IsOpen && card.worldBound.Overlaps(nav.worldBound)) { report.AppendLine("FAIL card overlaps navigation"); failures++; }
        var body = root.Q(className: "sheet-body");
        root.Query<Button>().ForEach(button =>
        {
            if (button.resolvedStyle.display == DisplayStyle.None || button.worldBound.width < 1) return;
            // Only the active page: hidden pages and panels do not accept taps.
            for (var p = button.parent; p != null; p = p.parent)
                if (p.resolvedStyle.display == DisplayStyle.None) return;
            var r = button.worldBound;
            report.AppendLine($"button {button.text ?? button.tooltip} {r}");
            if (body.Contains(button) && (r.yMax > body.worldBound.yMax + 1 || r.xMax > body.worldBound.xMax + 1))
            { report.AppendLine("FAIL page button clipped"); failures++; }
        });
        report.AppendLine(failures == 0 ? "PASS visible button bounds and card/navigation separation" : $"FAIL {failures} layout checks");
        File.WriteAllText("/tmp/seasick-midnight-land.txt", report.ToString());
        ScreenCapture.CaptureScreenshot("/tmp/seasick-midnight-land.png");
        Debug.Log(report.ToString());
    }
}
