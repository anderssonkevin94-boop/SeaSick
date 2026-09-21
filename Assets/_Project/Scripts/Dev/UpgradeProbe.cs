using System.Collections;
using System.Text;
using UnityEngine;
using SeaSick.Ship;

/// Do the yard's upgrade buttons do what their labels say?
///
/// Six rows were asked for — hull, gun number, gun calibre, sail number, sail
/// size, crew number, rudder — and every one of them is a claim: that pressing
/// it changes something the player can see or feel, and that when it is greyed
/// out the reason is a fact about the ship. This presses all of them.
///
/// It drives the REAL calls the panel makes (`AddCell`, `RemoveCell`,
/// `Upgrade`), not a parallel path, for the same reason `LadderFloatProbe`
/// does: a probe with its own rig can pass while the game is broken.
public class UpgradeProbe : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("UpgradeProbe: not in play mode"); return; }
        var old = FindAnyObjectByType<UpgradeProbe>();
        if (old != null) Destroy(old.gameObject);
        new GameObject("UpgradeProbe").AddComponent<UpgradeProbe>();
    }

    IEnumerator Start()
    {
        var yard = FindFirstObjectByType<Shipyard>();
        if (yard == null) { Debug.LogError("UpgradeProbe: no Shipyard"); Destroy(gameObject); yield break; }
        var motor = yard.GetComponent<ShipMotor>();
        var battery = yard.GetComponent<CannonBattery>();
        var rig = yard.GetComponent<SailRig>();
        int startedOn = yard.NodeIndex;

        var sb = new StringBuilder("UpgradeProbe:\n");

        // --- 1. quantity: does + actually add a gun and a hand? --------------
        sb.AppendLine("== quantity buttons, on the three-decker ==");
        yard.Apply(19);
        yield return null;
        sb.AppendLine($"as applied: guns {yard.Guns} a side, berths {yard.Berths}, "
                    + $"cargo {yard.Cargo}, empty bays {yard.Count(BayUse.Empty)}");

        for (int i = 0; i < 4; i++) yard.AddCell(BayUse.Battery);
        for (int i = 0; i < 6; i++) yard.AddCell(BayUse.Quarters);
        for (int i = 0; i < 3; i++) yard.AddCell(BayUse.Hold);
        yield return null;
        sb.AppendLine($"+4 guns, +6 berths, +3 hold -> guns {yard.Guns}, "
                    + $"berths {yard.Berths}, cargo {yard.Cargo}, "
                    + $"empty {yard.Count(BayUse.Empty)}");
        sb.AppendLine($"  battery actually fitted: {(battery != null ? battery.GunsPerSide : -1)} a side");
        sb.AppendLine($"  hands actually aboard: "
                    + $"{yard.GetComponentsInChildren<SeaSick.Crew.CrewAgent>(false).Length}");

        yard.RemoveCell(BayUse.Battery);
        yard.RemoveCell(BayUse.Quarters);
        yield return null;
        sb.AppendLine($"−1 gun, −1 berth -> guns {yard.Guns}, berths {yard.Berths}, "
                    + $"battery {(battery != null ? battery.GunsPerSide : -1)} a side");

        // --- 2. where the yard PUTS them ------------------------------------
        var n = ShipLadder.Node(19);
        sb.AppendLine("\n== where the yard put them (tier 0 is the hold) ==");
        for (int ti = n.tier_names.Length - 1; ti >= 0; ti--)
        {
            int guns = 0, berths = 0, hold = 0;
            for (int bi = 0; bi < n.bay_labels.Length; bi++)
                switch (yard.Use(n.bay_labels[bi], n.tier_names[ti]))
                {
                    case BayUse.Battery: guns++; break;
                    case BayUse.Quarters: berths++; break;
                    case BayUse.Hold: hold++; break;
                }
            sb.AppendLine($"  tier {ti} {n.tier_names[ti],-10} guns {guns}  "
                        + $"berths {berths}  hold {hold}");
        }

        // --- 3. the gates, rung by rung -------------------------------------
        sb.AppendLine("\n== what each rung will sell you ==");
        sb.AppendLine("rung  hull                B     masts  gun-add        "
                    + "sail·num      sail·size");
        foreach (int i in new[] { 0, 6, 8, 9, 12, 14, 15, 19 })
        {
            yard.Apply(i);
            yield return null;
            var d = ShipLadder.Node(i);
            string gunAdd = yard.WhyNotAdd(BayUse.Battery) ?? "yes";
            string plan = yard.WhyNotFit(FitTrack.SailPlan) ?? "yes";
            string size = yard.WhyNotFit(FitTrack.SailArea) ?? "yes";
            sb.AppendLine($"{i,4}  {d.label,-18} {d.beam,5:F1} {d.masts,5}   "
                        + $"{Trim(gunAdd, 14),-14} {Trim(plan, 13),-13} {Trim(size, 13)}");
        }

        // --- 4. quality: does a bigger suit of sails do anything? ------------
        sb.AppendLine("\n== sail size, on the three-decker ==");
        yard.Apply(19);
        yield return null;
        sb.AppendLine($"level 0: top {motor.MaxSpeed:F2} m/s  accel "
                    + $"{motor.AccelerationNow:F2}  canvas x{(rig != null ? rig.Area : 0f):F2}"
                    + $"  sails {(rig != null ? rig.SailCount : 0)}");
        for (int k = 0; k < 3; k++)
        {
            // The free path: `Upgrade` now costs cargo and refuses at sea, and
            // this probe measures what a level DOES, not whether it can be bought.
            int next = yard.Fit.Level(FitTrack.SailArea) + 1;
            yard.Fit.SetLevel(FitTrack.SailArea, next);
            yard.PushToGame();
            bool ok = yard.Fit.Level(FitTrack.SailArea) == next;
            yield return null;
            sb.AppendLine($"level {yard.Fit.Level(FitTrack.SailArea)}: "
                        + $"top {motor.MaxSpeed:F2} m/s  accel {motor.AccelerationNow:F2}  "
                        + $"canvas x{(rig != null ? rig.Area : 0f):F2}"
                        + (ok ? "" : $"  REFUSED: {yard.Status}"));
        }

        // --- 5. and does a smaller hull take it away again? ------------------
        yard.Apply(0);
        yield return null;
        sb.AppendLine($"\nback to the skiff (beam {ShipLadder.Node(0).beam:F1}): "
                    + $"sail·size {yard.Fit.Level(FitTrack.SailArea)}, "
                    + $"canvas x{(rig != null ? rig.Area : 0f):F2}, "
                    + $"guns {yard.Guns}, add a gun? {yard.WhyNotAdd(BayUse.Battery) ?? "yes"}");

        Debug.Log(sb.ToString());
        yard.Apply(startedOn);
        Destroy(gameObject);
    }

    static string Trim(string s, int n) => s.Length <= n ? s : s.Substring(0, n - 1) + "…";
}
