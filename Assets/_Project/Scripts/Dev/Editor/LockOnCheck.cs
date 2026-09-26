using System.Text;
using SeaSick.CameraRig;
using SeaSick.Combat;
using SeaSick.Ship;
using SeaSick.UI;
using UnityEngine;

/// **Lock-on check (2026-09-27): the phone's lock button, auto-fire, and the
/// lock camera.** Kevin, iPhone: "I can't press the button at sea that lets
/// me lock on to the enemy. also, cannons should auto fire when engaged."
///
/// PLAY mode, at sea. Two `unity cmd eval` calls, some seconds apart:
///
///   `LockOnCheck.Start(bring)` -- finds the player's `CombatLock` and the
///   nearest raider. With `bring`, a raider further than 60 m is moved to
///   45 m off her starboard beam (DEV ONLY: a teleport, not a spawn). Then
///   presses the lock through `CombatLock.ToggleLock()` -- the method the
///   phone button and the space bar both call -- and snapshots the shot
///   counters.
///
///   `LockOnCheck.Report(capturePath, release)` -- lock state, target and
///   range, the button's rect and whether any other HUD claim overlaps it,
///   the camera's `LockTarget` / `LockLevel` / portrait blend, and the shots
///   the battery fired ON ITS OWN since Start (`CannonBattery.AutoShots`),
///   with the session gunnery tally. With `release`, presses the button
///   again and confirms auto-fire stood down. Optional screenshot, written
///   at the end of the frame.
///
/// Run it in both shapes (`RunProbe.ViewPhone()` / `ViewDesk()`).
///
/// e.g. `unity cmd eval --json --code 'return LockOnCheck.Start(true);'`
///      (wait ~10 s)
///      `unity cmd eval --json --code 'return LockOnCheck.Report("/tmp/lockon.png", false);'`
public static class LockOnCheck
{
    static CombatLock lockOn;
    static CannonBattery battery;
    static float startedAt;
    static int autoShots0, shots0, hits0;

    public static string Start(bool bring = true)
    {
        var sb = new StringBuilder("LockOnCheck.Start\n");
        if (!Application.isPlaying) return sb.Append("FAIL: needs play mode").ToString();

        lockOn = Object.FindFirstObjectByType<CombatLock>();
        if (lockOn == null) return sb.Append("FAIL: no CombatLock in the scene").ToString();
        battery = lockOn.GetComponent<CannonBattery>();
        var ship = lockOn.transform;

        EnemyShip enemy = null;
        float best = float.MaxValue;
        foreach (var e in EnemyShip.All)
        {
            if (e == null || !e.Alive) continue;
            float d = Flat(e.transform.position - ship.position);
            if (d < best) { best = d; enemy = e; }
        }
        if (enemy == null) return sb.Append("FAIL: no live EnemyShip in the world").ToString();
        sb.AppendLine($"nearest raider: {enemy.name} at {best:F0} m ({enemy.Current})");

        if (bring && best > 60f)
        {
            Vector3 right = ship.right; right.y = 0f; right.Normalize();
            Vector3 at = ship.position + right * 45f;
            enemy.transform.position = new Vector3(at.x, enemy.transform.position.y, at.z);
            sb.AppendLine($"moved it to 45 m off the starboard beam {at:F0} (DEV teleport)");
        }

        if (lockOn.Locked != null)
            sb.AppendLine($"already locked on {Name(lockOn.Locked)}; releasing first");
        if (lockOn.Locked != null) lockOn.ToggleLock();

        bool held = lockOn.ToggleLock();
        sb.AppendLine(held
            ? $"ToggleLock -> LOCKED on {Name(lockOn.Locked)}"
            : "ToggleLock -> no lock (no candidate inside lock range this frame; Update refreshes it -- try again next frame)");

        startedAt = Time.time;
        autoShots0 = battery != null ? battery.AutoShots : 0;
        shots0 = GunneryStats.Shots;
        hits0 = GunneryStats.Hits;
        sb.AppendLine($"battery: {(battery == null ? "NONE" : $"{battery.PortCount} port / {battery.StarboardCount} stbd, reach {battery.GunRange:F0} m")}");
        return sb.ToString();
    }

    public static string Report(string capturePath = null, bool release = false)
    {
        var sb = new StringBuilder("LockOnCheck.Report\n");
        if (!Application.isPlaying) return sb.Append("FAIL: needs play mode").ToString();
        if (lockOn == null) return sb.Append("FAIL: run LockOnCheck.Start first").ToString();

        float secs = Time.time - startedAt;
        var t = lockOn.Locked;
        sb.AppendLine(t != null
            ? $"lock: HELD on {Name(t)} at {Flat(t.HitCentre - lockOn.transform.position):F0} m, hull {t.HitPoints - t.DamageTaken}/{t.HitPoints}"
            : "lock: NONE (broke, target sunk, or never taken)");
        sb.AppendLine($"candidate now: {(lockOn.CurrentCandidate == null ? "none" : Name(lockOn.CurrentCandidate))}");

        // The button: where it is and whether anything else claims its pixels.
        var r = lockOn.ButtonRect;
        sb.AppendLine($"screen {Screen.width}x{Screen.height}, Wide={HudLayout.Wide}, unit {HudLayout.Unit}px, dpi {Screen.dpi:F0}");
        if (r.width <= 0f) sb.AppendLine("button: NOT DRAWN last frame (lost the prompt slot, or nothing to lock)");
        else
        {
            sb.AppendLine($"button: {r} (h {r.height:F0} px)");
            var claimed = UIBlocker.Claimed;
            var owners = UIBlocker.Owners;
            int overlaps = 0;
            for (int i = 0; i < claimed.Count; i++)
            {
                if (claimed[i] == r || !claimed[i].Overlaps(r)) continue;
                overlaps++;
                sb.AppendLine($"  OVERLAP with {(i < owners.Count ? owners[i] : "?")} {claimed[i]}");
            }
            if (overlaps == 0) sb.AppendLine("  no other UIBlocker claim overlaps it");
        }

        var chase = Object.FindFirstObjectByType<ChaseCamera>();
        if (chase == null) sb.AppendLine("camera: no ChaseCamera");
        else
            sb.AppendLine($"camera: LockTarget={(chase.LockTarget == null ? "null" : chase.LockTarget.name)}, LockLevel {chase.LockLevel:F2}, portrait {chase.Portrait01:F2}");

        if (battery != null)
        {
            sb.AppendLine($"auto-fire: target {(battery.AutoFireTarget == null ? "null" : Name(battery.AutoFireTarget))}, " +
                          $"{battery.AutoShots - autoShots0} shots on their own in {secs:F1} s");
            sb.AppendLine($"gunnery tally since Start: {GunneryStats.Shots - shots0} shots, {GunneryStats.Hits - hits0} hits; ready now port {battery.PortReady} stbd {battery.StarboardReady}");
        }

        if (release)
        {
            lockOn.ToggleLock();
            sb.AppendLine($"released: Locked={(lockOn.Locked == null ? "null" : "STILL HELD")}, " +
                          $"AutoFireTarget={(battery == null || battery.AutoFireTarget == null ? "null" : "STILL SET")}, " +
                          $"camera LockTarget {(chase != null && chase.LockTarget == null ? "cleared" : "cleared next frame")}");
        }

        if (!string.IsNullOrEmpty(capturePath))
        {
            ScreenCapture.CaptureScreenshot(capturePath);
            sb.AppendLine($"capture requested: {capturePath} (written at end of frame)");
        }
        return sb.ToString();
    }

    static float Flat(Vector3 d) { d.y = 0f; return d.magnitude; }

    static string Name(IHittable t) => t is MonoBehaviour mb && mb != null ? mb.name : t?.GetType().Name ?? "null";
}
