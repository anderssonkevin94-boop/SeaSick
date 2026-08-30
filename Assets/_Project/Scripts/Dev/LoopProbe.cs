using System.Collections;
using System.Text;
using UnityEngine;
using SeaSick.Ship;
using SeaSick.Voyage;
using SeaSick.World;

/// Drives one whole voyage in about ten seconds and reports what the loop
/// did: cast off, load, come home, land the haul against a capacity, build.
///
/// It moves the SHIP and then lets the real systems run — it does not call
/// CompleteVoyage or set a phase itself. That distinction is the whole value
/// of the probe: a check that drove the state machine by hand would prove
/// only that the state machine can be driven by hand.
public class LoopProbe : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("LoopProbe: not in play mode"); return; }
        var old = FindAnyObjectByType<LoopProbe>();
        if (old != null) Destroy(old.gameObject);
        new GameObject("LoopProbe").AddComponent<LoopProbe>();
    }

    StringBuilder sb = new StringBuilder();

    IEnumerator Start()
    {
        var voyage = FindFirstObjectByType<VoyageManager>();
        var motor = FindFirstObjectByType<ShipMotor>();
        var anchor = motor != null ? motor.GetComponent<AnchorController>() : null;
        var dock = Dock.Home;
        var village = Village.Home;
        if (voyage == null || motor == null || anchor == null || dock == null)
        { Report("missing voyage/ship/anchor/dock\n"); yield break; }

        Line(voyage, village, "at the start");
        if (voyage.AtHome) sb.AppendLine("  ! the panel is up before she has sailed");

        // --- cast off and stand out to sea ---------------------------------
        anchor.CastOff();
        yield return null;
        sb.AppendLine($"cast off -> anchor state {anchor.CurrentState}, "
            + $"at home dock {anchor.AtHomeDock}");

        Vector3 out2 = dock.Berth + dock.Seaward * 400f;
        Warp(motor, out2, Quaternion.LookRotation(dock.Seaward));
        for (int i = 0; i < 6; i++) yield return null;
        sb.AppendLine($"warped {dock.DistanceFrom(motor.transform.position):F0} m off the berth"
            + $" -> panel up: {voyage.AtHome} (want false)");

        // --- load her past what home can keep ------------------------------
        voyage.TakeDeckCargo = true;
        voyage.AddLoot(60, "Timber");
        // What she actually took, not what was offered -- AddLoot clamps to
        // the physical limit, and the overflow arithmetic below has to be
        // against the real load or the report lies about the game.
        int loaded = voyage.TotalHeld;
        sb.AppendLine($"loaded -> hold {loaded} of {voyage.MaxHold}, "
            + $"home keeps {voyage.StoreCapacity}");

        // --- and bring her home --------------------------------------------
        Warp(motor, dock.Berth, dock.Heading);
        for (int i = 0; i < 4; i++) yield return null;
        bool took = anchor.TryComeAlongside();
        sb.AppendLine($"come alongside: {took}");
        float t = 0f;
        while (!voyage.AtHome && t < 4f) { t += Time.deltaTime; yield return null; }
        sb.AppendLine($"panel up after {t:F2}s: {voyage.AtHome} (want true)");
        Line(voyage, village, "home");
        sb.AppendLine($"  spoiled on the sand: {loaded - voyage.BankedTotal} "
            + $"(want {loaded} landed minus the {voyage.StoreCapacity} home can keep)");

        // Let the unload coroutine set the pile down before counting it.
        yield return new WaitForSeconds(voyage.BankedTotal * 0.18f + 0.6f);
        var pile = Stockpile.Instance;
        int onBeach = pile != null ? pile.CountOf("Timber") : -1;
        sb.AppendLine($"logs actually on the beach: {onBeach} (want {voyage.Banked("Timber")})");

        // --- build --------------------------------------------------------
        var plan = BuildPlans.Storehouse;
        int capBefore = voyage.StoreCapacity, hadBefore = voyage.Banked(plan.resource);
        bool built = voyage.TryBuild(plan);
        yield return null;
        sb.AppendLine($"build {plan.label}: {built}   "
            + $"timber {hadBefore} -> {voyage.Banked(plan.resource)} (cost {plan.cost})   "
            + $"capacity {capBefore} -> {voyage.StoreCapacity}");
        sb.AppendLine($"logs on the beach after paying: "
            + $"{(pile != null ? pile.CountOf("Timber") : -1)} (want {voyage.Banked("Timber")})");
        sb.AppendLine($"buildings standing: {(village != null ? village.Built.Count : -1)}");

        Report(sb.ToString());
        Destroy(gameObject);
    }

    void Line(VoyageManager v, Village village, string label)
    {
        sb.AppendLine($"{label}: panel {v.AtHome}   hold {v.TotalHeld}   "
            + $"stores {v.BankedTotal}/{v.StoreCapacity}   "
            + $"buildings {(village != null ? village.Built.Count : -1)}");
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

    static void Report(string s)
    {
        Debug.Log("LOOP PROBE\n" + s);
        System.IO.File.WriteAllText("/tmp/loop-probe.txt", s);
    }
}
