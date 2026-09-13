using System.Text;
using UnityEngine;
using SeaSick.Ship;
using SeaSick.World;

/// **Press the button and watch.** Sail her to a real island, land, make camp,
/// and then look at what is actually on the ground.
///
/// The shape of this is copied from `LandProbe`'s lesson deliberately: a
/// geometry survey once said all fifteen islands were landable and every beach
/// test passed, and you still could not gather a log, because everything AFTER
/// the prompt was broken. So nothing here asks whether making camp *would*
/// work. It makes one, then counts the trees that came down IN THE MESH, reads
/// the fire off the scene, and reads the pile off the ledger.
///
/// Measuring the artefact, not the rule that produced it: `FelledInMesh`
/// counts vertex runs that have collapsed, not the `felled` flags that the
/// felling set.
public class CampProbe : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("CampProbe: not in play mode"); return; }
        var runner = new GameObject("CampProbeRunner").AddComponent<CampProbe>();
        runner.StartCoroutine(runner.Run());
    }

    System.Collections.IEnumerator Run()
    {
        var sb = new StringBuilder();
        int fails = 0;

        var anchor = Object.FindFirstObjectByType<AnchorController>();
        var motor = Object.FindFirstObjectByType<ShipMotor>();
        var cam = Object.FindFirstObjectByType<SeaSick.CameraRig.ChaseCamera>();
        if (anchor == null || motor == null)
        {
            Report("FAIL: no ship in the scene\n");
            Destroy(gameObject);
            yield break;
        }

        // --- pick somewhere with a beach and some wood on it ------------------
        //
        // Ranked by shore gap, never by centre distance: in a world of lobed
        // islands a ship 20 m off a small island's beach is routinely nearer
        // the CENTRE of a big one, and that exact mistake once cost 12 of 34
        // approach bearings their landing prompt.

        Island target = null; Vector3 standOff = default; float bestGap = float.MaxValue;
        Vector3 from = motor.transform.position;
        foreach (var isle in Island.All)
        {
            if (isle == null || isle.IsHome) continue;
            for (int b = 0; b < 24; b++)
            {
                float ang = b / 24f * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang));
                Vector3 ship = isle.transform.position + dir * (isle.RadiusAt(ang) + 18f);
                if (Island.TerrainHeight == null || Island.TerrainHeight(ship.x, ship.z) > -0.5f) continue;

                // Ask the SAME three questions `AnchorController` asks, in the
                // same order. Checking only the beach says "yes" on a shore
                // where no prompt ever appears, because the first question
                // already picked a different island — a ship 18 m off a small
                // island is routinely nearer the CENTRE of a big one, and this
                // probe was told "no island in range" at a spot its own scan
                // had called landable.
                var think = Island.Nearest(ship);
                if (think != isle) continue;
                if (Island.FlatDistance(ship, isle.transform.position)
                    > isle.RadiusToward(ship) + 30f) continue;
                if (!isle.HasBeachToward(ship)) continue;

                float gap = Island.FlatDistance(ship, from);
                if (gap < bestGap) { bestGap = gap; target = isle; standOff = ship; }
            }
        }

        if (target == null)
        {
            Report("FAIL: no island in the world offers a beach to land on\n");
            Destroy(gameObject);
            yield break;
        }

        sb.AppendLine($"target {target.name}  r {target.Radius:F0} m, "
            + $"{bestGap:F0} m from where she started");

        // She starts the game tied up at her own berth, so she is Anchored and
        // `TryLand` refuses before it looks at anything. Let go first — through
        // the real call, because `CastOff` is the one place that knows how to
        // stop being moored and a second copy of that routine is exactly the
        // fault `GetUnderway` was written to end.
        if (anchor.CurrentState != AnchorController.State.Underway)
        {
            anchor.CastOff();
            float cast = Time.realtimeSinceStartup;
            while (anchor.CurrentState != AnchorController.State.Underway
                   && Time.realtimeSinceStartup - cast < 10f)
                yield return null;
            sb.AppendLine($"cast off: now {anchor.CurrentState}");
        }

        Warp(motor, standOff, Quaternion.LookRotation(target.transform.position - standOff));
        // Two frames: the warp moves the rigidbody, and `Island.Nearest` and
        // the beach test both answer about where she IS.
        yield return null;
        yield return new WaitForFixedUpdate();
        yield return null;

        // --- land -------------------------------------------------------------

        bool landed = anchor.TryLand(out string why);
        sb.AppendLine($"land: {(landed ? "yes" : "NO")} — {why}");
        Gate(sb, ref fails, "she-can-land-there", landed, why);
        if (!landed) { Finish(sb, fails); yield break; }

        // The survey runs over frames. Wait for it rather than assuming, and
        // time how long the player would have waited.
        float t0 = Time.realtimeSinceStartup;
        while (Outpost.Surveying(target) && Time.realtimeSinceStartup - t0 < 30f)
            yield return null;
        float waited = Time.realtimeSinceStartup - t0;

        var outpost = Outpost.Of(target);
        sb.AppendLine($"survey: {(outpost != null ? "ground found" : "refused")} "
            + $"after {waited:F2} s of waiting");
        Gate(sb, ref fails, "the-ground-was-surveyed", Outpost.Surveyed(target),
            "still not looked at");
        if (outpost == null)
        {
            sb.AppendLine("  (this island will not take a camp — a real answer, "
                + "but this probe needs one that will)");
            Finish(sb, fails); yield break;
        }

        // --- the camera should have left the water ---------------------------

        yield return null;
        bool overview = cam != null && cam.Overview.HasValue;
        sb.AppendLine($"camera: overview {(overview ? "engaged" : "NOT engaged")}"
            + (overview ? $", framing {cam.Overview.Value.radius:F0} m "
                + $"centred {cam.Overview.Value.centre.x:F0},{cam.Overview.Value.centre.z:F0}" : ""));
        Gate(sb, ref fails, "birds-eye-at-a-plain-island", overview,
            "the overview only engages at the home dock");

        // --- make camp --------------------------------------------------------

        var wood = target.GetComponentInChildren<SeaSick.Terrain.SceneryWood>();
        int standingBefore = wood != null ? wood.TreeCount - wood.FelledInMesh() : -1;

        sb.AppendLine();
        sb.AppendLine($"outpost: sited {outpost.Sited}, clearing {outpost.ClearingRadius:F1} m "
            + $"at {outpost.ClearingCentre.x:F0},{outpost.ClearingCentre.z:F0} "
            + $"standing {(Island.TerrainHeight != null ? Island.TerrainHeight(outpost.ClearingCentre.x, outpost.ClearingCentre.z) : 0f):F2} m");
        sb.AppendLine($"  fires already under this island: "
            + $"{target.GetComponentsInChildren<Campfire>().Length}");

        int felled = outpost.MakeCamp(out string whyNot);
        if (felled < 0) sb.AppendLine($"  MakeCamp refused: {whyNot}");
        yield return null;

        int standingAfter = wood != null ? wood.TreeCount - wood.FelledInMesh() : -1;
        int downInMesh = standingBefore - standingAfter;

        sb.AppendLine();
        sb.AppendLine($"MAKE CAMP on {target.name}:");
        sb.AppendLine($"  reported {felled} logs out of the clearing");
        sb.AppendLine($"  trees standing in the MESH: {standingBefore} -> {standingAfter} "
            + $"({downInMesh} came down)");
        sb.AppendLine($"  keeps {outpost.StoreCapacity}, ledger ceiling {outpost.Ledger?.ceiling}, "
            + $"pile {outpost.Ledger?.timber}");

        Gate(sb, ref fails, "the-camp-exists", outpost.HasCamp, "no campfire raised");
        Gate(sb, ref fails, "the-fire-is-on-the-ground",
            target.GetComponentInChildren<Campfire>() != null, "no Campfire in the scene");
        Gate(sb, ref fails, "making-camp-fells-its-own-site",
            wood == null || downInMesh == felled,
            $"reported {felled}, mesh says {downInMesh}");
        Gate(sb, ref fails, "a-camp-gives-the-place-a-ceiling",
            outpost.StoreCapacity == OutpostLedger.CampfireCeiling,
            $"keeps {outpost.StoreCapacity}, expected {OutpostLedger.CampfireCeiling}");
        Gate(sb, ref fails, "one-definition-of-the-ceiling",
            outpost.Ledger != null && outpost.Ledger.ceiling == outpost.StoreCapacity,
            $"ledger {outpost.Ledger?.ceiling} vs outpost {outpost.StoreCapacity}");

        // Making camp twice must not raise a second fire.
        int again = outpost.MakeCamp();
        Gate(sb, ref fails, "camp-is-made-once", again < 0 && outpost.CountOf("Campfire") == 1,
            $"second attempt returned {again}, {outpost.CountOf("Campfire")} fires");

        // --- and it has to actually produce ----------------------------------
        //
        // Put two hands on the wood and wind the clock. The ledger is the only
        // thing that produces; nobody has to be standing here.

        var l = outpost.Ledger;
        l.timber = 0; l.timberPart = 0f;
        l.hands.Clear();
        l.hands.Add(new OutpostHand { name = "probe-1", order = OutpostOrder.Cut });
        l.hands.Add(new OutpostHand { name = "probe-2", order = OutpostOrder.Cut });
        l.lastTicked = TimeOfDay.Seconds;

        double twoDays = TimeOfDay.Seconds + 2.0 * TimeOfDay.DayLength;
        l.ceiling = outpost.StoreCapacity;
        l.Tick(twoDays);

        sb.AppendLine();
        sb.AppendLine($"TWO HANDS, TWO GAME DAYS with nobody watching: "
            + $"{l.timber} logs of a possible {l.ceiling}, {l.standing:F0} still standing");
        Gate(sb, ref fails, "an-absent-camp-produces", l.timber > 0,
            $"{l.timber} logs after two days");

        Finish(sb, fails);
    }

    void Finish(StringBuilder sb, int fails)
    {
        sb.AppendLine();
        sb.AppendLine(fails == 0 ? "PASS — she lands, the ground is surveyed, the camp is made"
                                 : $"{fails} GATE(S) FAILED");
        Report(sb.ToString());
        Destroy(gameObject);
    }

    static void Warp(ShipMotor motor, Vector3 to, Quaternion facing)
    {
        to.y = motor.transform.position.y;
        var rb = motor.GetComponent<Rigidbody>();
        motor.transform.SetPositionAndRotation(to, facing);
        if (rb != null)
        {
            rb.position = to;
            rb.rotation = facing;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        motor.AnchorPoint = to;
    }

    static void Gate(StringBuilder sb, ref int fails, string name, bool ok, string detail)
    {
        if (!ok) fails++;
        sb.AppendLine($"  [{(ok ? "ok  " : "FAIL")}] {name}   {(ok ? "" : detail)}");
    }

    static void Report(string text)
    {
        Debug.Log("CampProbe\n" + text);
        var path = System.IO.Path.Combine(Application.dataPath, "../Logs/CampProbe.txt");
        try { System.IO.File.WriteAllText(path, text); } catch { }
    }
}
