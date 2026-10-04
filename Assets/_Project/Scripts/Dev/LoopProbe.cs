using System.Collections;
using System.Text;
using UnityEngine;
using SeaSick.Ship;
using SeaSick.Voyage;
using SeaSick.World;

/// Drives one whole voyage in about ten seconds and reports what the loop
/// did: cast off, load, come home, let the home camp's hands carry the haul
/// ashore into its store, build.
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
        var village = Outpost.Home;
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

        // --- load her ----------------------------------------------------
        // Island stores are unlimited and home docking no longer banks or
        // spoils anything (2026-10-04): the hold stays aboard and the home
        // camp's hands carry it ashore. A modest haul, so the carry finishes
        // inside the probe's wait.
        voyage.TakeDeckCargo = true;
        voyage.AddLoot(12, "Timber");
        int loaded = voyage.TotalHeld;
        int homeWas = voyage.HomeStoreOf("Timber");
        sb.AppendLine($"loaded -> hold {loaded} of {voyage.MaxHold}, "
            + $"home store timber {homeWas}");

        // --- and bring her home --------------------------------------------
        Warp(motor, dock.Berth, dock.Heading);
        for (int i = 0; i < 4; i++) yield return null;
        bool took = anchor.TryComeAlongside();
        sb.AppendLine($"come alongside: {took}");
        float t = 0f;
        while (!voyage.AtHome && t < 4f) { t += Time.deltaTime; yield return null; }
        sb.AppendLine($"panel up after {t:F2}s: {voyage.AtHome} (want true)");
        Line(voyage, village, "home, hold still aboard");

        // The home camp's hands carry the hold ashore armful by armful
        // (`OutpostLedger.OrderTransfer`, placed on arrival), so the hold is
        // behind the store until they finish. Waited for in real time, capped.
        float carry = 0f;
        while (voyage.TotalHeld > 0 && carry < 120f) { carry += Time.deltaTime; yield return null; }
        sb.AppendLine($"hold {voyage.TotalHeld} left after {carry:F0}s   "
            + $"home store timber {homeWas} -> {voyage.HomeStoreOf("Timber")} "
            + $"(want +{loaded}; 0 left aboard means every unit landed)");
        Line(voyage, village, "home, carried ashore");

        // --- build --------------------------------------------------------
        var plan = BuildPlans.Storage;
        int capBefore = voyage.StoreCapacity, hadBefore = voyage.HomeStoreOf(plan.resource);
        bool built = voyage.TryBuild(plan);
        yield return null;
        sb.AppendLine($"build {plan.label}: {built}   "
            + $"timber {hadBefore} -> {voyage.HomeStoreOf(plan.resource)} (cost {plan.cost})   "
            + $"capacity {capBefore} -> {voyage.StoreCapacity}");
        sb.AppendLine($"buildings standing: {(village != null ? village.Built.Count : -1)}");

        Report(sb.ToString());
        Destroy(gameObject);
    }

    void Line(VoyageManager v, Outpost village, string label)
    {
        sb.AppendLine($"{label}: panel {v.AtHome}   hold {v.TotalHeld}   "
            + $"home store {v.HomeStoreTotal}   "
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
