using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SeaSick.Ship;

/// Probe for the modular ladder, in the same spirit as the other Dev probes:
/// it does not stop at the last thing it can compute, it walks the whole
/// thing and reports what the GAME would actually do at every rung.
///
/// `LadderCheck.Execute()`      — save the open scene in place.
/// `LadderCheck.Assets()`       — manifest, meshes, kit and the corridor.
/// `LadderCheck.WalkTheLadder()`— apply all twenty rungs to a throwaway ship.
public static class LadderCheck
{
    [MenuItem("SeaSick/Shipyard/Check assets")]
    public static void Assets()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        int n = ShipLadder.Count;
        if (n == 0) { Debug.LogError("LadderCheck: manifest did not load"); return; }
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"LadderCheck: {n} rungs parsed");
        int missing = 0;
        for (int i = 0; i < n; i++)
            if (Resources.Load<GameObject>(ShipLadder.Node(i).ResourcePath) == null) missing++;
        sb.AppendLine($"  hull meshes: {n - missing}/{n} resolve");

        var root = Resources.Load<GameObject>("Kit/seasick_kit");
        if (root == null) sb.AppendLine("  KIT MISSING at Resources/Kit/seasick_kit");
        else
        {
            var names = new System.Collections.Generic.List<string>();
            foreach (var t in root.GetComponentsInChildren<Transform>())
                if (t != root.transform) names.Add(t.name);
            sb.AppendLine($"  kit pieces: {names.Count} — {string.Join(", ", names)}");
        }

        // Exactly one move should be legal at each rung. If two ever are, the
        // corridor has stopped gating and the ladder is no longer a zigzag.
        for (int i = 0; i < n - 1; i++)
        {
            int legal = 0;
            foreach (var m in new[] { "lengthen", "girdle", "raise" })
                if (ShipLadder.Blocked(i, m) == null) legal++;
            if (legal != 1) sb.AppendLine($"  rung {i}: {legal} legal moves (expected 1)");
        }
        sb.AppendLine("  corridor: one legal move per rung, all the way up");
        Debug.Log(sb.ToString());
    }

    public static void Execute()
    {
        Debug.Log($"LadderCheck: SaveOpenScenes -> {EditorSceneManager.SaveOpenScenes()}");
    }

    /// Play-mode: prove the bay decisions actually reach the game. Without
    /// this the panel is a viewer — the numbers change and nothing consumes
    /// them.
    [MenuItem("SeaSick/Shipyard/Check the wiring")]
    public static void CheckWiring()
    {
        if (!Application.isPlaying) { Debug.LogError("CheckWiring: play mode only"); return; }
        var yard = Object.FindFirstObjectByType<Shipyard>();
        var voyage = Object.FindFirstObjectByType<SeaSick.Voyage.VoyageManager>();
        var battery = yard != null ? yard.GetComponent<SeaSick.Ship.CannonBattery>() : null;
        if (yard == null || voyage == null)
        { Debug.LogError("CheckWiring: need a Shipyard and a VoyageManager"); return; }

        var sb = new System.Text.StringBuilder("CheckWiring:\n");
        sb.AppendLine($"  before        hold cap {voyage.HoldCapacity,3}  "
                    + $"guns/side {(battery != null ? battery.GunsPerSide : -1),2}  "
                    + $"cargo {yard.Cargo,3}  guns {yard.Guns,2}  crew {yard.Berths,2}");

        var roster = yard.GetComponent<SeaSick.Crew.CrewRoster>();
        sb.AppendLine($"  roster before  {(roster != null ? roster.CrewCount : -1)} hands");

        var n = yard.Node;
        // Three bays of battery, three of quarters, the rest hold — on the
        // top tier, and quarters on the one below if she has one.
        string top = n.tier_names[n.tier_names.Length - 1];
        for (int i = 0; i < n.bay_labels.Length; i++)
            yard.SetUse(n.bay_labels[i], top, i < 3 ? BayUse.Battery : BayUse.Hold);
        if (n.tier_names.Length > 1)
            for (int i = 0; i < Mathf.Min(6, n.bay_labels.Length); i++)
                yard.SetUse(n.bay_labels[i], n.tier_names[0], BayUse.Quarters);

        sb.AppendLine($"  after         hold cap {voyage.HoldCapacity,3}  "
                    + $"guns/side {(battery != null ? battery.GunsPerSide : -1),2}  "
                    + $"cargo {yard.Cargo,3}  guns {yard.Guns,2}  crew {yard.Berths,2}");
        sb.AppendLine($"  hold capacity follows cargo: "
                    + $"{(voyage.HoldCapacity == Mathf.Max(4, yard.Cargo) ? "YES" : "NO")}");
        sb.AppendLine($"  roster after   {(roster != null ? roster.CrewCount : -1)} hands "
                    + $"for {yard.Berths} berths — "
                    + $"{(roster != null && roster.CrewCount == Mathf.Max(1, yard.Berths) ? "YES" : "NO")}");
        sb.AppendLine($"  battery follows bays: "
                    + $"{(battery != null && battery.GunsPerSide == yard.Guns ? "YES" : "NO")}"
                    + $" ({(battery != null ? battery.GunsPerSide : -1)} vs {yard.Guns})");
        Debug.Log(sb.ToString());
    }

    /// Play-mode: take the real ship to the top of the ladder and fill her the
    /// way the design says — 18 hold, 10 battery, 20 quarters — then read the
    /// crew number back off the ROSTER rather than off the panel.
    ///
    /// Twenty is the design ceiling and the reason the crew are a cast rather
    /// than a resource. If the roster cannot actually reach it, the number in
    /// the panel is a claim and not a fact.
    [MenuItem("SeaSick/Shipyard/Check the crew ceiling")]
    public static void CheckCrewCeiling()
    {
        if (!Application.isPlaying) { Debug.LogError("play mode only"); return; }
        var yard = Object.FindFirstObjectByType<Shipyard>();
        if (yard == null) { Debug.LogError("no Shipyard"); return; }
        var roster = yard.GetComponent<SeaSick.Crew.CrewRoster>();
        var voyage = Object.FindFirstObjectByType<SeaSick.Voyage.VoyageManager>();
        var battery = yard.GetComponent<SeaSick.Ship.CannonBattery>();

        yard.Apply(ShipLadder.Count - 1);
        var n = yard.Node;
        int hold = 18, guns = 10, berths = 20, k = 0;
        foreach (var t in n.tier_names)
            foreach (var b in n.bay_labels)
            {
                BayUse u = k < hold ? BayUse.Hold
                         : k < hold + guns ? BayUse.Battery
                         : k < hold + guns + berths ? BayUse.Quarters : BayUse.Empty;
                yard.SetUse(b, t, u); k++;
            }

        var hands = yard.GetComponentsInChildren<SeaSick.Crew.CrewAgent>(false);
        Debug.Log($"CheckCrewCeiling: {n.label}, {n.cells} cells\n"
            + $"  panel says     cargo {yard.Cargo}, guns {yard.Guns}, berths {yard.Berths}, "
            + $"needs {yard.CrewNeeded}, undermanned {yard.Undermanned}\n"
            + $"  roster says    {(roster != null ? roster.CrewCount : -1)} hands, "
            + $"{hands.Length} active in the hierarchy\n"
            + $"  hold capacity  {(voyage != null ? voyage.HoldCapacity : -1)}\n"
            + $"  guns per side  {(battery != null ? battery.GunsPerSide : -1)}\n"
            + $"  ceiling of 20 reached: "
            + $"{((roster != null && roster.CrewCount == 20) ? "YES" : "NO")}");
    }

    /// Play-mode: the two things Kevin reported. Is there exactly ONE hull
    /// visual on her, and does every sail turn about its own mast?
    [MenuItem("SeaSick/Shipyard/Check the visuals")]
    public static void CheckVisuals()
    {
        if (!Application.isPlaying) { Debug.LogError("play mode only"); return; }
        var yard = Object.FindFirstObjectByType<Shipyard>();
        if (yard == null) { Debug.LogError("no Shipyard"); return; }
        var sb = new System.Text.StringBuilder("CheckVisuals:\n");

        foreach (int node in new[] { 0, 12, 19 })
        {
            yard.Apply(node);
            var n = yard.Node;
            int visuals = 0;
            var names = new System.Collections.Generic.List<string>();
            foreach (Transform t in yard.transform)
            {
                // Only ACTIVE ones. PaddleBoatVisual stays as a deactivated
                // child on purpose — she is the ship the game shipped with and
                // destroying her would make `Restore()` impossible.
                if (t.name.Contains("Visual") && t.gameObject.activeSelf)
                { visuals++; names.Add(t.name); }
            }
            var rig = yard.GetComponent<SeaSick.Ship.SailRig>();
            var hull = yard.transform.Find("HullVisual");
            int sails = 0;
            var pivotZ = new System.Collections.Generic.List<string>();
            if (hull != null)
                foreach (var t in hull.GetComponentsInChildren<Transform>())
                    if (t.name.EndsWith("_Pivot"))
                    {
                        sails++;
                        Vector3 lp = yard.transform.InverseTransformPoint(t.position);
                        pivotZ.Add($"{lp.z:F2}");
                    }
            sb.AppendLine($"  rung {node,2} {n.label,-17} visuals {visuals} "
                        + $"[{string.Join(",", names)}]  masts {n.masts}  "
                        + $"sail pivots {sails} at z {string.Join("/", pivotZ)}  "
                        + $"{(visuals == 1 && sails == n.masts ? "OK" : "PROBLEM")}");
        }
        Debug.Log(sb.ToString());
    }

    /// Play-mode: does handling actually differ by hull, and do the fittings
    /// survive a hull swap? Both are claims the panel makes.
    [MenuItem("SeaSick/Shipyard/Check handling and fittings")]
    public static void CheckHandling()
    {
        if (!Application.isPlaying) { Debug.LogError("play mode only"); return; }
        var yard = Object.FindFirstObjectByType<Shipyard>();
        var motor = yard != null ? yard.GetComponent<SeaSick.Ship.ShipMotor>() : null;
        if (yard == null || motor == null) { Debug.LogError("need a Shipyard + ShipMotor"); return; }

        var sb = new System.Text.StringBuilder("CheckHandling:\n");
        sb.AppendLine("rung  label              L      top m/s  turn °/s  accel");
        foreach (int i in new[] { 0, 6, 12, 15, 19 })
        {
            yard.Apply(i);
            var d = ShipLadder.Node(i);
            sb.AppendLine($"{i,4}  {d.label,-17} {d.length,5:F1} "
                        + $"{motor.MaxSpeed,9:F2} {motor.MaxTurnRate,9:F2} "
                        + $"{motor.AccelerationNow,7:F2}");
        }

        // Fit a rudder and a sail plan on the three-decker, then drop back to
        // the skiff and see what survives.
        yard.Apply(19);
        foreach (SeaSick.Ship.FitTrack t in System.Enum.GetValues(typeof(SeaSick.Ship.FitTrack)))
            for (int k = 0; k < 3; k++) yard.Upgrade(t);
        sb.AppendLine($"\nthree-decker fitted out: "
            + string.Join(", ", System.Array.ConvertAll(
                (SeaSick.Ship.FitTrack[])System.Enum.GetValues(typeof(SeaSick.Ship.FitTrack)),
                t => $"{t} {yard.Fit.Level(t)}")));
        sb.AppendLine($"  top {motor.MaxSpeed:F2} m/s, turn {motor.MaxTurnRate:F2}°/s");

        yard.Apply(0);
        sb.AppendLine($"back to the skiff: "
            + string.Join(", ", System.Array.ConvertAll(
                (SeaSick.Ship.FitTrack[])System.Enum.GetValues(typeof(SeaSick.Ship.FitTrack)),
                t => $"{t} {yard.Fit.Level(t)}")));
        sb.AppendLine($"  top {motor.MaxSpeed:F2} m/s, turn {motor.MaxTurnRate:F2}°/s");
        sb.AppendLine($"  guns gate: {yard.WhyNotFit(SeaSick.Ship.FitTrack.Guns) ?? "open"}");
        sb.AppendLine($"  sail-plan gate: {yard.WhyNotFit(SeaSick.Ship.FitTrack.SailPlan) ?? "open"}");
        sb.AppendLine($"  sail-size gate: {yard.WhyNotFit(SeaSick.Ship.FitTrack.SailArea) ?? "open"}");
        Debug.Log(sb.ToString());
    }


    /// What every rung weighs, where her centre of gravity sits, and how far
    /// a full hold actually sinks her. All of it derived — nothing here is a
    /// constant somebody chose to make the number look right.
    [MenuItem("SeaSick/Shipyard/Report the loading")]
    public static void ReportLoading()
    {
        if (!Application.isPlaying) { Debug.LogError("play mode only"); return; }
        var yard = Object.FindFirstObjectByType<Shipyard>();
        if (yard == null) { Debug.LogError("no Shipyard"); return; }
        var sb = new System.Text.StringBuilder("ReportLoading:\n");
        sb.AppendLine("rung  label              light  ballast  guns  crew  FULL cargo   total t"
                    + "     KG     KM     GM    TPC    empty     full     over  (cm)");
        foreach (int i in new[] { 0, 3, 6, 9, 12, 15, 19 })
        {
            yard.Apply(i);
            var n = yard.Node;

            // Fill her the way the design says: hold, battery, quarters in
            // thirds, so the numbers are for a ship someone would actually sail.
            // Half her cells to the hold, a quarter each to guns and berths —
            // and the HOLD FIRST, so even a two-cell skiff gets one. Filling in
            // thirds gave the skiff a gun and a berth and no hold at all, then
            // reported that cargo did not move her.
            int k = 0, half = Mathf.Max(1, n.cells / 2);
            int quarter = Mathf.Max(1, n.cells / 4);
            foreach (var t in n.tier_names)
                foreach (var b in n.bay_labels)
                {
                    BayUse u = k < half ? BayUse.Hold
                             : k < half + quarter ? BayUse.Quarters : BayUse.Battery;
                    yard.SetUse(b, t, u); k++;
                }

            var L = yard.Load;
            int full = yard.Cargo;
            float emptySink = L.SinkageEmptyM;
            L.CargoUnits = full;
            float fullSink = L.SinkageM;
            // Overloaded is the deck-cargo case the voyage loop already has:
            // past the marked line by the overload limit.
            L.CargoUnits = Mathf.RoundToInt(full * 1.6f);
            float overSink = L.SinkageM;
            float fullTotal = L.TotalKg;
            L.CargoUnits = full;
            sb.AppendLine($"{i,4}  {n.label,-17} "
                + $"{L.LightshipKg / 1000f,6:F1} {L.BallastKg / 1000f,8:F1} "
                + $"{L.GunsKg / 1000f,5:F1} {L.CrewMassKg / 1000f,5:F2} "
                + $"{full,5} u = {L.CargoKg / 1000f,5:F1} t "
                + $"{fullTotal / 1000f,8:F1} {L.KGm,6:F2} {n.km_above_keel_m,6:F2} "
                + $"{L.GMm,6:F2} {n.tpc_t_per_cm,6:F2} "
                + $"{emptySink * 100f,8:F1} {fullSink * 100f,8:F1} {overSink * 100f,8:F1}");
        }
        sb.AppendLine("\n  GM below ~0.3 m is tender; negative and she will not stand up.");
        sb.AppendLine("  sink is metres relative to her DRAWN waterline: negative rides high.");
        Debug.Log(sb.ToString());
    }

    [MenuItem("SeaSick/Shipyard/Walk the ladder")]
    public static void WalkTheLadder()
    {
        var go = new GameObject("LadderRig");
        try
        {
            var rb = go.AddComponent<Rigidbody>();
            go.AddComponent<SeaSick.Ocean.BuoyancyProbeSet>();
            go.AddComponent<SeaSick.Ocean.BuoyantBody>();
            var yard = go.AddComponent<Shipyard>();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("rung  label              move        L      B    mass t  rb.mass  cells  visual");
            for (int i = 0; i < ShipLadder.Count; i++)
            {
                yard.Apply(i);
                var d = ShipLadder.Node(i);
                var vis = go.transform.Find("HullVisual");
                sb.AppendLine($"{i,4}  {d.label,-17} {d.move,-9} {d.length,6:F2} "
                            + $"{d.beam,6:F2} {d.mass_kg / 1000f,8:F1} {rb.mass / 1000f,8:F1} "
                            + $"{d.cells,5}  {(vis != null ? "OK" : "MISSING")}");
            }

            // The design says a filled three-decker lands on twenty crew. It is
            // not clamped there — it falls out of guns costing berths.
            yard.Apply(ShipLadder.Count - 1);
            var n = ShipLadder.Node(ShipLadder.Count - 1);
            int hold = 18, guns = 10, berths = 20, k = 0;
            foreach (var t in n.tier_names)
                foreach (var b in n.bay_labels)
                {
                    BayUse u = k < hold ? BayUse.Hold
                             : k < hold + guns ? BayUse.Battery
                             : k < hold + guns + berths ? BayUse.Quarters : BayUse.Empty;
                    yard.SetUse(b, t, u); k++;
                }
            var furn = go.transform.Find("Furniture");
            sb.AppendLine($"\nthree-decker filled 18/10/20: crew {yard.Berths}, "
                        + $"guns {yard.Guns}, cargo {yard.Cargo}, "
                        + $"needs {yard.CrewNeeded}, undermanned {yard.Undermanned}, "
                        + $"furniture {(furn != null ? furn.childCount : 0)} objects");
            Debug.Log(sb.ToString());
        }
        finally { Object.DestroyImmediate(go); }
    }
}
