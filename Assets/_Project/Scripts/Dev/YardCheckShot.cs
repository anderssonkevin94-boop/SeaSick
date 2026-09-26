using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
using SeaSick.Ship.Modular;
using SeaSick.UI.ModularYard;
using SeaSick.World;
using SheetsApi = SeaSick.UI.Sheets.Sheets;

/// **One-off driver for the 2026-09-26 shipyard verification pass.**
///
/// Not a probe (no PASS/FAIL gates) -- a set of small static entry points
/// called one at a time through `unity cmd eval`, each advancing the UI by
/// one step so a screenshot can be taken between calls. Exists because the
/// checklist needs a HUMAN judgement of the rendered picture at each state,
/// which a probe's text gates cannot give. Reuses the same "find the click
/// delegate on `Clickable` and invoke it by reflection" trick
/// `ShipyardUiProbe.ClickViaDelegate` uses, and the same probe-injected dry
/// dock `ShipyardRefitProbe.EnsureDryDock` uses.
///
/// State lives in static fields so it survives between eval calls within one
/// play session; nothing here is serialized, so it does not survive a domain
/// reload or leaving play mode.
public static class YardCheckShot
{
    public static ShipyardService Yard;
    public static SeaSick.Ship.AnchorController Anchor;
    public static readonly List<string> Log = new List<string>();

    public static string Setup()
    {
        Log.Clear();
        Yard = SeaSick.Ship.Modular.ShipyardService.Player;
        if (Yard == null) return "no ShipyardService.Player";
        Anchor = Yard.GetComponent<SeaSick.Ship.AnchorController>();
        string why0;
        if (Anchor != null && !Anchor.AtHomeDock && !Anchor.BerthAtHome(out why0)) Log.Add("could not berth: " + why0);
        EnsureDryDock();
        return Report();
    }

    public static string Report()
    {
        bool canRefit = false; string why = "";
        if (Yard != null) canRefit = Yard.CanRefitNow(out why);
        return $"ModularActive {Yard?.ModularActive}, CanRefit {canRefit} {why}, "
            + $"AtHomeDock {Anchor?.AtHomeDock}, HomeSlip {(DryDockSlip.HomeSlip != null)}; " + string.Join(" | ", Log);
    }

    /// **Real siting, not the probes' bare-component injection.** A probe
    /// only needs `DryDockSlip.HomeSlip` to answer non-null; a screenshot
    /// needs the actual raised building (Astra's slip kit) at a valid spot
    /// beside the home berth, so this goes through `Outpost.SnapDryDock` +
    /// `Outpost.Raise` -- the same path the player's own build UI uses --
    /// and only falls back to the bare injection if siting fails for some
    /// reason (so the rest of the checklist can still be driven).
    public static void EnsureDryDock()
    {
        if (DryDockSlip.HomeSlip != null) return;
        var home = Dock.Home;
        var isle = home != null ? Island.Nearest(home.Berth) : null;
        var outpost = isle != null ? Outpost.Of(isle) : null;
        if (outpost == null) { Log.Add("EnsureDryDock: no outpost at home island"); return; }
        // Neither `home.Berth` (the mooring point out in the water -- its
        // shore walk lands 45.7 m away, past the 40 m `DryDockMaxFromHome`
        // cap) nor the dock's own transform (14 m inside the harbour's own
        // keep-out) works as the seed point. Ring-search a ring of points
        // around the dock for one that both snaps AND clears `CanPlace` --
        // this is exactly what a player drags the ghost through by hand;
        // there is no single "correct" seed to hardcode.
        Vector3 basePos = home.transform.position;
        bool placed = false;
        for (int ang = 0; ang < 360 && !placed; ang += 15)
        {
            for (float dist = 16f; dist <= 38f && !placed; dist += 4f)
            {
                float rad = ang * Mathf.Deg2Rad;
                Vector3 seed = basePos + new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * dist;
                if (!outpost.SnapDryDock(seed, out Vector3 centre, out float yaw, out string why1)) continue;
                if (!outpost.CanPlace(BuildPlans.DryDock, centre, yaw, out string why2)) continue;
                var b = outpost.Raise(BuildPlans.DryDock, centre, yaw);
                if (b != null) { Log.Add($"EnsureDryDock: raised at ang={ang} dist={dist} centre={centre}"); placed = true; }
            }
        }
        if (placed) return;
        Log.Add("EnsureDryDock: ring search found no valid spot");

        Log.Add("EnsureDryDock: falling back to a bare probe-style injection (no kit visual)");
        var go = new GameObject("DryDockSlip (shot-injected fallback)");
        go.transform.position = home.Berth;
        var bb = go.AddComponent<Building>();
        bb.Configure(BuildPlans.DryDock);
        var slip = go.AddComponent<DryDockSlip>();
        slip.Configure(BuildPlans.DryDock);
        slip.Register(bb, outpost);
    }

    public static void RemoveDryDock()
    {
        var s = DryDockSlip.HomeSlip;
        if (s != null) UnityEngine.Object.Destroy(s.gameObject);
    }

    /// Warps the ship far from her home berth and leaves her under way (not
    /// anchored), so the ship sheet's "away from home" blocker is genuine.
    public static string WarpAway()
    {
        if (Yard == null) return "no yard";
        Yard.transform.position += new Vector3(4000f, 0f, 4000f);
        var rb = Yard.GetComponent<Rigidbody>();
        if (rb != null) rb.linearVelocity = Vector3.zero;
        return Report();
    }

    // ---- shipyard modal -------------------------------------------------

    public static ShipyardModal Modal => UnityEngine.Object.FindAnyObjectByType<ShipyardModal>();
    public static VisualElement Root()
    {
        var m = Modal;
        var doc = m != null ? m.GetComponent<UIDocument>() : null;
        return doc != null ? doc.rootVisualElement : null;
    }

    public static string OpenYard()
    {
        SheetsApi.Close();
        try { ShipyardLiveBridge.Open(); } catch (Exception e) { return "EXCEPTION " + e; }
        return ShipyardModal.IsOpen ? "opened" : "did not open";
    }

    public static void CloseYard()
    {
        var root = Root();
        if (root == null) return;
        ClickByClass(root, "yard-icon-button", 0); // header close
    }

    static bool ClickViaDelegate(Button b)
    {
        if (b == null || b.clickable == null) return false;
        int invoked = 0;
        foreach (var f in typeof(Clickable).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            if (f.FieldType == typeof(Action) && f.GetValue(b.clickable) is Action a) { a(); invoked++; }
        return invoked > 0;
    }

    public static List<Button> ByClass(VisualElement root, string cls)
    {
        var l = new List<Button>();
        root.Query<Button>(className: cls).ForEach(b => l.Add(b));
        return l;
    }

    public static bool ClickByClass(VisualElement root, string cls, int index)
    {
        var l = ByClass(root, cls);
        return index < l.Count && ClickViaDelegate(l[index]);
    }

    public static bool ClickByText(VisualElement root, string startsWith)
    {
        Button found = null;
        root.Query<Button>().ForEach(b => { if (found == null && !string.IsNullOrEmpty(b.text) && b.text.StartsWith(startsWith)) found = b; });
        return ClickViaDelegate(found);
    }

    public static string TapTile(int index) { var r = Root(); return r != null && ClickByClass(r, "yard-tile", index) ? "tapped tile " + index : "FAILED tile " + index; }
    public static string TapReportTab() { var r = Root(); return ClickByText(r, "Report") ? "tapped Report" : "FAILED Report"; }
    public static string TapHullTab() { var r = Root(); return ClickByText(r, "Hull") ? "tapped Hull" : "FAILED Hull"; }
    public static string TapStructureTab() { var r = Root(); return ClickByText(r, "Structure") ? "tapped Structure" : "FAILED Structure"; }
    public static string TapGunsTab() { var r = Root(); return ClickByText(r, "Guns") ? "tapped Guns" : "FAILED Guns"; }
    public static string TapInteriorTab() { var r = Root(); return ClickByText(r, "Interior") ? "tapped Interior" : "FAILED Interior"; }
    public static string TapDone() { var r = Root(); return ClickByText(r, "Done") ? "tapped Done" : "FAILED Done"; }
    public static string TapConfirmRefit() { var r = Root(); return ClickByText(r, "Confirm refit") ? "tapped Confirm refit" : "FAILED Confirm refit"; }
    public static string TapOk() { var r = Root(); return ClickByText(r, "OK") ? "tapped OK" : "FAILED OK"; }

    public static string TapFirstCargoCell()
    {
        var r = Root(); if (r == null) return "no root";
        var cells = ByClass(r, "yard-cutaway-cell");
        for (int i = 0; i < cells.Count; i++)
            if (!cells[i].ClassListContains("yard-cutaway-cell--bunk"))
                return ClickViaDelegate(cells[i]) ? "tapped cargo cell " + i : "FAILED cargo cell " + i;
        return "no cargo cell found (" + cells.Count + " cells)";
    }

    public static string TapFirstGunMarker()
    {
        var r = Root(); if (r == null) return "no root";
        return ClickByClass(r, "yard-cutaway-gun", 0) ? "tapped gun marker" : "FAILED gun marker (0 found?)";
    }

    /// Text dump of the overview/report/sheet state for cross-checking a
    /// screenshot against the actual data (clamped +, blocker colour class
    /// etc. are visual only, but the reasons and counts are here).
    public static string DumpState()
    {
        var r = Root();
        if (r == null) return "no modal root";
        var sb = new System.Text.StringBuilder();
        r.Query<Label>().ForEach(l => { if (!string.IsNullOrEmpty(l.text)) sb.Append('[').Append(l.text.Replace("\n", "\\n")).Append("] "); });
        return sb.ToString();
    }

    /// Gun slot z positions vs the open section sheet's highlighted z range
    /// -- checks the cutaway places markers at their real fore/aft spot.
    public static string DumpGuns()
    {
        var yard = SeaSick.Ship.Modular.ShipyardService.Player;
        if (yard == null) return "no player ship";
        var sb = new System.Text.StringBuilder();
        var r = Root();
        var sheet = r?.Q<ShipyardSectionSheet>();
        if (sheet != null)
        {
            var pv = typeof(ShipyardSectionSheet).GetField("preview", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(sheet) as ShipyardPreview;
            foreach (var s in yard.EquipmentSlots(yard.Current))
            {
                float along = -1f; bool ok = pv != null && pv.TrySlotAlong(s.sectionKey, s.slotId, out along);
                sb.Append($"{s.sectionKey} {s.side} z={s.positionM.z:F2} along={(ok ? along.ToString("F2") : "n/a")} | ");
            }
        }
        return sb.ToString();
    }

    public static string ShipSheetState()
    {
        var yard = SeaSick.Ship.Modular.ShipyardService.Player;
        if (yard == null) return "no player ship";
        var blockers = yard.RefitBlockers();
        return blockers.Count == 0 ? "ENABLED" : "DISABLED: " + string.Join(" || ", blockers);
    }
}
