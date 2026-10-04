using System.Collections;
using System.Text;
using UnityEngine;
using SeaSick.Ship;
using SeaSick.Voyage;
using SeaSick.World;

/// **Does the Home tab actually get her home, or only move her?**
///
/// Putting the hull on the berth is the easy half and the half a check is
/// tempted to stop at. The lesson `LandProbe` had to learn is that a check
/// which stops at the last thing it can compute stops before the thing that
/// is broken — a geometry survey said every island was landable while you
/// could not gather a single log. So this presses the button and then watches:
/// she has to be ON the berth, LYING STILL, tied up as far as
/// `AtHomeDock` is concerned, and the VOYAGE has to close on its own, with
/// her cargo still aboard.
///
/// It takes her a kilometre out under way with a load in the hold first,
/// because a respawn from a standing start next to the pier proves nothing.
///
/// Play mode, Sea.unity. Writes /tmp/seasick-hometab.txt.
public class HomeTabProbe : MonoBehaviour
{
    static bool running;

    // No screenshot mode, deliberately. The tab is IMGUI, so it exists only
    // in a rendered game-view frame — and `ScreenCapture` does not resolve
    // from the runtime assembly in this project (its module is reachable only
    // from the editor one), while the RenderTexture route every other shot
    // tool uses does not draw IMGUI at all. `IslandLook` wrote that down
    // years-worth of sessions ago; this is it being read.
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("HomeTabProbe: not in play mode"); return; }
        if (running) { Debug.LogError("HomeTabProbe: already running"); return; }
        running = true;
        new GameObject("HomeTabProbe").AddComponent<HomeTabProbe>();
    }

    IEnumerator Start()
    {
        var sb = new StringBuilder();
        System.IO.File.WriteAllText("/tmp/seasick-hometab.txt", "HomeTabProbe: did not finish\n");

        var anchor = FindAnyObjectByType<AnchorController>();
        var motor = anchor != null ? anchor.GetComponent<ShipMotor>() : null;
        var voyage = FindAnyObjectByType<VoyageManager>();
        var tab = FindAnyObjectByType<SeaSick.UI.HomeTab>();
        if (anchor == null || motor == null)
        { Finish(sb, "ABORT: no ship"); yield break; }

        sb.AppendLine("HomeTabProbe — does the Home tab get her home, or only move her?");
        sb.AppendLine(tab == null
            ? "*** HomeTab is NOT in the scene — the button does not exist ***"
            : $"HomeTab on '{tab.gameObject.name}'");

        // The dock does not exist until the populator has run.
        float wait = 0f;
        while (Dock.Home == null && wait < 25f) { wait += Time.deltaTime; yield return null; }
        if (Dock.Home == null) { Finish(sb, "ABORT: no home dock after 25 s"); yield break; }
        var berth = Dock.Home.Berth;
        sb.AppendLine($"home berth at ({berth.x:F1}, {berth.z:F1})");
        yield return new WaitForSeconds(3f);

        // --- take her away, loaded and under way -----------------------------
        int loadWas = voyage != null ? voyage.TotalHeld : 0;
        if (voyage != null && voyage.TotalHeld == 0) { voyage.AddSalvage(6); loadWas = voyage.TotalHeld; }
        // What the home camp already holds, because a free tow means the
        // cargo is on its way ASHORE, not retained: arriving at the berth
        // completes the voyage, which places an "all ashore" transfer order
        // (2026-10-04: home docking unloads like any camp, the hold is no
        // longer banked) and the home hands carry it armful by armful. So
        // cargo may be aboard, in a hand's arms, or in the home store --
        // measure all three, never just where it was.
        int homeWas = voyage != null ? voyage.HomeStoreTotal : 0;
        anchor.CastOff();
        var rb = motor.GetComponent<Rigidbody>();
        Vector3 away = berth + new Vector3(0f, 0f, 1000f);
        away.y = berth.y;
        if (rb != null)
        {
            rb.position = away;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        // Under way, not drifting: the interesting case is a moving ship.
        motor.ThrottleOrder = 1f;
        // Give her real way rather than waiting for the engine to build it:
        // the point of the test is a MOVING ship, and six seconds of ramp off
        // a standing start left her at 0.5 m/s, which is not one.
        yield return new WaitForSeconds(2f);
        if (rb != null) rb.linearVelocity = motor.transform.forward * 8f;
        yield return new WaitForSeconds(1f);

        float outDist = Vector3.Distance(FlatXZ(motor.transform.position), FlatXZ(berth));
        sb.AppendLine($"before: {outDist:F0} m from the berth, making {motor.CurrentSpeed:F1} m/s, "
                    + $"hold {(voyage != null ? voyage.TotalHeld : -1)}, "
                    + $"AtHomeDock {anchor.AtHomeDock}, "
                    + $"voyage AtHome {(voyage != null ? voyage.AtHome.ToString() : "n/a")}");
        if (outDist < 200f)
            sb.AppendLine("*** she never got clear of the berth — the rest proves nothing ***");

        // --- press it --------------------------------------------------------
        bool ok = anchor.BerthAtHome(out string why);
        sb.AppendLine(ok ? "BerthAtHome: accepted" : $"BerthAtHome: REFUSED — {why}");
        if (!ok) { Finish(sb, "FAIL: refused from open water with nobody ashore"); yield break; }

        // One frame is enough for the transform; the rest needs the physics
        // and the voyage to notice, so give them a second and a half.
        yield return new WaitForSeconds(1.5f);

        float dist = Vector3.Distance(FlatXZ(motor.transform.position), FlatXZ(berth));
        float speed = motor.CurrentSpeed;
        float heading = Mathf.Abs(Mathf.DeltaAngle(
            motor.transform.eulerAngles.y, Dock.Home.Heading.eulerAngles.y));
        int loadNow = voyage != null ? voyage.TotalHeld : 0;
        int homeNow = voyage != null ? voyage.HomeStoreTotal : 0;
        // Armfuls already off the ship but still walking to the store.
        var homeCamp = Outpost.Home;
        int inArms = homeCamp != null && homeCamp.Ledger != null
            ? homeCamp.Ledger.CarryingTransfer(null, false) : 0;

        sb.AppendLine($"after:  {dist:F2} m from the berth, making {speed:F2} m/s, "
                    + $"{heading:F1}° off the berth heading, hold {loadNow}");
        sb.AppendLine($"        AtHomeDock {anchor.AtHomeDock}, state {anchor.CurrentState}, "
                    + $"voyage AtHome {(voyage != null ? voyage.AtHome.ToString() : "n/a")}");

        int fails = 0;
        void Gate(bool pass, string line) { if (!pass) fails++; sb.AppendLine((pass ? "  ok   " : "  FAIL ") + line); }

        Gate(dist < 12f, $"on the berth (gap {dist:F2} m)");
        Gate(speed < 1.5f, $"lying still (speed {speed:F2} m/s) — she must not arrive under power");
        Gate(heading < 25f, $"lying along the pier ({heading:F1}° off)");
        Gate(anchor.AtHomeDock, "tied up as far as AtHomeDock is concerned");
        Gate(voyage == null || voyage.AtHome, "the VOYAGE closed — not just the hull moved");
        // Still aboard, in a hand's arms, or landed in the home store — any
        // is fine, vanished is not.
        int accounted = loadNow + inArms + (homeNow - homeWas);
        Gate(voyage == null || accounted >= loadWas,
             $"her cargo came home with her: {loadWas} aboard → {loadNow} aboard "
             + $"+ {inArms} in arms + {homeNow - homeWas} landed = {accounted}. Kevin's call is a free "
             + "tow, so none of it may vanish.");

        Finish(sb, fails == 0 ? "PASS" : $"FAIL: {fails} gate(s)");
    }

    static Vector3 FlatXZ(Vector3 v) => new Vector3(v.x, 0f, v.z);

    static void Finish(StringBuilder sb, string tail)
    {
        sb.AppendLine(tail);
        System.IO.File.WriteAllText("/tmp/seasick-hometab.txt", sb.ToString());
        Debug.Log("HomeTabProbe:\n" + sb);
        running = false;
    }
}
