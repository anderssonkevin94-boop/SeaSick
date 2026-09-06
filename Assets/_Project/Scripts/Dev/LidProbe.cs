using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using SeaSick.Ship;

/// Do the gun-port lids show the battery she ACTUALLY has?
///
/// Every port her decks allow is cut in the mesh and every lid is baked shut;
/// `PortLids` is the only thing that says which of them are open. Two ways
/// that can be wrong and neither shows up in a count of triangles:
///
///  1. The wrong NUMBER of lids opens — two guns agreeing on one lid, or a
///     gun opening a lid on the deck below it. One gun is a gun each side,
///     so the claim is exactly `OpenCount == 2 x Guns`.
///  2. The right number opens in the wrong PLACES. Checked here by matching
///     each open lid back to a gun independently of the code that opened it,
///     and reporting the worst distance — the loft rakes her stations, so a
///     port sits forward of its gun and the question is how far.
///
/// Then the case Kevin named: a LENGTHENING inserts bays amidships and moves
/// the whole run of ports, so a gun that had a port can end up behind solid
/// planking. `PruneCells` is supposed to demote it. This walks the ship
/// through those upgrades with guns aboard and re-checks both claims after.
public class LidProbe : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("LidProbe: not in play mode"); return; }
        var old = FindAnyObjectByType<LidProbe>();
        if (old != null) Destroy(old.gameObject);
        new GameObject("LidProbe").AddComponent<LidProbe>();
    }

    const string Out = "/tmp/seasick-lidprobe.txt";

    static readonly BindingFlags Priv =
        BindingFlags.Instance | BindingFlags.NonPublic;

    Shipyard yard;
    PortLids lids;
    StringBuilder sb;
    int problems;

    /// The gun list the yard itself would hand the lids. Read off the yard
    /// rather than recomputed here: a probe with its own copy of the rule can
    /// agree with itself while the game is wrong.
    List<Vector3> Guns()
    {
        var m = typeof(Shipyard).GetMethod("GunPositions", Priv);
        return (List<Vector3>)m.Invoke(yard, new object[] { yard.Node });
    }

    /// Each lid's seat in ship space — x is which side, y the sill, z the
    /// station — paired with whether that lid is open.
    List<(Vector3 seat, bool open)> Lids()
    {
        var fLids = typeof(PortLids).GetField("lids", Priv);
        var fSeats = typeof(PortLids).GetField("seats", Priv);
        var seats = (List<Vector3>)fSeats.GetValue(lids);
        var raw = (IList)fLids.GetValue(lids);
        var outp = new List<(Vector3, bool)>();
        FieldInfo fWant = null;
        for (int i = 0; i < raw.Count && i < seats.Count; i++)
        {
            if (fWant == null)
                fWant = raw[i].GetType().GetField("want",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            float want = (float)fWant.GetValue(raw[i]);
            outp.Add((seats[i], want > 0.5f));
        }
        return outp;
    }

    /// One line of verdict for the ship as she stands.
    void Check(string what)
    {
        var guns = Guns();
        var all = Lids();
        int open = 0, openStbd = 0, openPort = 0;
        foreach (var (seat, o) in all)
        {
            if (!o) continue;
            open++;
            if (seat.x > 0) openStbd++; else openPort++;
        }

        // The two questions are checked SEPARATELY, because one number cannot
        // answer both and trying to make it do so was this probe's own bug
        // twice over.
        //
        //  * EXCLUSIVITY — did two guns agree on one lid? — is answered by the
        //    count alone. A collision leaves a lid shut, so `open` falls below
        //    `2 x guns`. That is exactly the 70-of-72 failure the shipped code
        //    was written to fix, and it needs no matching of its own.
        //  * PLACEMENT — is each open lid at the deck and station of a gun? —
        //    is a nearest-gun distance, and it is deliberately NOT exclusive.
        //    A greedy exclusive walk in lid order is not an optimal
        //    assignment, so on a ship with every port filled it strands a
        //    couple of lids and reports a fault that is only its own ordering.
        float worst = 0f, total = 0f;
        int orphans = 0, matched = 0;
        foreach (var (seat, o) in all)
        {
            if (!o) continue;
            float best = float.MaxValue;
            for (int g = 0; g < guns.Count; g++)
            {
                float d = new Vector2(seat.z - guns[g].z, seat.y - guns[g].y).magnitude;
                if (d < best) best = d;
            }
            if (best >= 4f) { orphans++; continue; }
            worst = Mathf.Max(worst, best);
            total += best; matched++;
        }
        float mean = matched > 0 ? total / matched : 0f;

        bool countOk = open == guns.Count * 2;
        bool sidesOk = openStbd == guns.Count && openPort == guns.Count;
        bool placeOk = orphans == 0;
        if (!countOk || !sidesOk || !placeOk) problems++;

        var n = yard.Node;
        sb.AppendLine(
            $"  {what,-34} rung {yard.NodeIndex,2}  guns {guns.Count,2}  "
          + $"lids {all.Count,3} (mesh says {n.ports_per_side * 2,3})  "
          + $"open {open,3} = {openPort}p/{openStbd}s  "
          + $"match mean {mean,4:F2} worst {worst,5:F2} m  "
          + $"{(countOk && sidesOk && placeOk ? "ok" : "PROBLEM")}"
          + (countOk ? "" : $" count wants {guns.Count * 2}")
          + (sidesOk ? "" : " lopsided")
          + (placeOk ? "" : $" {orphans} lid(s) with no gun"));
    }

    /// Fill a rung's battery to `want` guns using the REAL button call.
    int FitGuns(int want)
    {
        int n = 0;
        for (int i = 0; i < want; i++)
            if (yard.AddCell(BayUse.Battery)) n++; else break;
        return n;
    }

    void ClearGuns()
    {
        for (int i = 0; i < 200 && yard.Guns > 0; i++)
            if (!yard.RemoveCell(BayUse.Battery)) break;
    }

    IEnumerator Start()
    {
        sb = new StringBuilder("=== LidProbe ===\n");
        yard = FindFirstObjectByType<Shipyard>();
        if (yard == null)
        {
            System.IO.File.WriteAllText(Out, "LidProbe: no Shipyard in the open scene\n");
            Debug.LogError("LidProbe: no Shipyard"); Destroy(gameObject); yield break;
        }
        int startedOn = yard.NodeIndex;

        // --- 1. shut is the default, on every rung that has ports -----------
        sb.AppendLine("== a ship at rest: every lid shut ==");
        foreach (int i in new[] { 9, 12, 15, 19 })
        {
            yard.Apply(i);
            yield return null;
            lids = yard.GetComponent<PortLids>();
            if (lids == null)
            {
                sb.AppendLine($"  rung {i}: NO PortLids COMPONENT — PROBLEM");
                problems++; continue;
            }
            ClearGuns();
            yield return null;
            Check("no guns aboard");
        }

        // --- 2. guns fitted, count and placement ----------------------------
        sb.AppendLine("\n== guns run out ==");
        foreach (var (rung, want) in new[] { (9, 3), (12, 6), (15, 9), (19, 10) })
        {
            yard.Apply(rung);
            yield return null;
            lids = yard.GetComponent<PortLids>();
            ClearGuns();
            int got = FitGuns(want);
            yield return null;
            Check($"fitted {got} of {want} asked");
        }

        // --- 3. a full battery: does every port open? -----------------------
        sb.AppendLine("\n== cleared for action, three-decker ==");
        yard.Apply(19);
        yield return null;
        lids = yard.GetComponent<PortLids>();
        ClearGuns();
        int all = FitGuns(200);
        yield return null;
        Check($"filled the battery: {all} guns");
        sb.AppendLine($"    (she can man {yard.MaxGunsManned} of them; "
                    + $"{yard.EmptyCells} bays left empty)");

        // --- 4. THE CASE KEVIN NAMED: an upgrade moves the port run ---------
        sb.AppendLine("\n== after an upgrade moves the ports ==");
        // 12->13 and 15->16 are LENGTHENINGS: bays go in amidships and every
        // port shifts. 11->12 and 14->15 are RAISES, which add a whole tier.
        foreach (var (from, to, want) in new[] { (12, 13, 6), (13, 14, 6), (15, 16, 8), (18, 19, 8) })
        {
            yard.Apply(from);
            yield return null;
            lids = yard.GetComponent<PortLids>();
            ClearGuns();
            int got = FitGuns(want);
            yield return null;
            Check($"on rung {from} with {got} guns");

            yard.Apply(to);
            yield return null;
            lids = yard.GetComponent<PortLids>();
            Check($"  -> upgraded to {to}");
        }

        // --- 5. does a gun ever LOSE its port to an upgrade? ----------------
        // The yard puts a gun on the lowest gun deck amidships, which is where
        // ports are densest, so the ordinary case never strains `PruneCells`.
        // Filling the battery to the last port is what puts guns out at the
        // extremities, where a shifted run can leave one behind planking.
        sb.AppendLine("\n== battery filled to the last port, then upgraded ==");
        foreach (var (from, to) in new[] { (9, 10), (10, 11), (11, 12), (12, 13),
                                           (13, 14), (14, 15), (15, 16), (18, 19) })
        {
            yard.Apply(from);
            yield return null;
            lids = yard.GetComponent<PortLids>();
            ClearGuns();
            int full = FitGuns(500);
            yield return null;
            Check($"rung {from} full: {full} guns");

            yard.Apply(to);
            yield return null;
            lids = yard.GetComponent<PortLids>();
            int after = yard.Guns;
            Check($"  -> {to}: {after} guns"
                + (after < full ? $" ({full - after} PRUNED)" : " (none pruned)"));
        }

        // --- 6. what a RENAMED tier costs the player ------------------------
        // `PruneCells` keys a cell on (bay, tier NAME), so a tier that is
        // renamed between two rungs is indistinguishable from a tier that was
        // deleted, and everything stowed on it is dropped. Two steps on the
        // ladder rename one: 2->3 (`Open` becomes `Hold`) and 14->15 (`Deck`
        // becomes `MiddleGunDeck` + `UpperGunDeck`). Adding a tier WITHOUT
        // renaming one — 8->9 and 18->19 — costs nothing, which is the
        // behaviour the design asks for.
        sb.AppendLine("\n== what an upgrade costs a fully-fitted ship ==");
        foreach (var (from, to) in new[] { (2, 3), (8, 9), (13, 14), (14, 15), (18, 19) })
        {
            yard.Apply(from);
            yield return null;
            ClearGuns();
            // Fill her the way a player would: guns first, then berths, then
            // every remaining bay as hold.
            FitGuns(500);
            for (int i = 0; i < 200 && yard.AddCell(BayUse.Quarters); i++) { }
            for (int i = 0; i < 200 && yard.AddCell(BayUse.Hold); i++) { }
            yield return null;
            int g0 = yard.Guns, b0 = yard.Berths, c0 = yard.Cargo;
            var t0 = string.Join("/", ShipLadder.Node(from).tier_names);

            yard.Apply(to);
            yield return null;
            int g1 = yard.Guns, b1 = yard.Berths, c1 = yard.Cargo;
            var t1 = string.Join("/", ShipLadder.Node(to).tier_names);
            bool lost = g1 < g0 || b1 < b0 || c1 < c0;
            if (lost) problems++;
            sb.AppendLine($"  {from,2} -> {to,2}  guns {g0,2}->{g1,-2} berths {b0,2}->{b1,-2} "
                        + $"cargo {c0,2}->{c1,-2}  {(lost ? "LOSES HER FITTING" : "keeps everything")}");
            sb.AppendLine($"          tiers {t0}  ->  {t1}");
        }

        sb.AppendLine($"\n{(problems == 0 ? "CLEAN — every check passed" : problems + " PROBLEM(S)")}");
        System.IO.File.WriteAllText(Out, sb.ToString());
        Debug.Log(sb.ToString());
        yard.Apply(startedOn);
        Destroy(gameObject);
    }
}
