using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// **Dev: every jump of the player ship, frame by frame, across scene loads.**
/// `ShipPoseTrace.Start()` (play mode, via `unity cmd eval`) hangs a
/// DontDestroyOnLoad watcher that re-finds the ShipMotor every frame (so a
/// Continue/Load scene reload is followed), and logs a line whenever she
/// moves more than 3 m in one frame, the ship object changes, the restore
/// starts/ends, or the voyage phase flips. `ShipPoseTrace.Dump()` returns
/// the log; `ShipPoseTrace.Visual()` reports her renderers, layers and
/// whether the main camera can see them.
public static class ShipPoseTrace
{
    public static readonly List<string> Log = new List<string>();

    public static string Start()
    {
        Log.Clear();
        var go = GameObject.Find("ShipPoseTrace");
        if (go == null)
        {
            go = new GameObject("ShipPoseTrace");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<ShipPoseTraceHost>();
        }
        return "tracing";
    }

    public static string Dump() => string.Join("\n", Log);

    public static string Visual()
    {
        var motor = Object.FindFirstObjectByType<SeaSick.Ship.ShipMotor>();
        if (motor == null) return "no ship";
        var sb = new StringBuilder();
        var cam = Camera.main;
        sb.AppendLine($"ship {motor.name} active={motor.gameObject.activeInHierarchy} pos={motor.transform.position} scale={motor.transform.lossyScale}");
        if (cam != null) sb.AppendLine($"cam {cam.name} pos={cam.transform.position} fwd={cam.transform.forward} mask={cam.cullingMask} dist={Vector3.Distance(cam.transform.position, motor.transform.position):F1}");
        int on = 0, off = 0;
        var byParent = new Dictionary<string, int>();
        foreach (var r in motor.GetComponentsInChildren<Renderer>(true))
        {
            bool vis = r.enabled && r.gameObject.activeInHierarchy;
            if (vis) on++; else off++;
            var top = r.transform;
            while (top.parent != null && top.parent != motor.transform) top = top.parent;
            string key = top.name + (vis ? "+" : "-") + " L" + LayerMask.LayerToName(r.gameObject.layer)
                + (cam != null && (cam.cullingMask & (1 << r.gameObject.layer)) == 0 ? "(culled)" : "");
            byParent.TryGetValue(key, out int n); byParent[key] = n + 1;
        }
        sb.AppendLine($"renderers on={on} off={off}");
        foreach (var kv in byParent) sb.AppendLine($"  {kv.Key} x{kv.Value}");
        for (int i = 0; i < motor.transform.childCount; i++)
        {
            var c = motor.transform.GetChild(i);
            sb.AppendLine($"  child {c.name} active={c.gameObject.activeSelf} lpos={c.localPosition} lscale={c.localScale}");
        }
        return sb.ToString();
    }
}

public class ShipPoseTraceHost : MonoBehaviour
{
    SeaSick.Ship.ShipMotor last;
    Vector3 lastPos;
    bool lastRestoring, lastHome;
    string lastDock = "";

    void Note(string s) => ShipPoseTrace.Log.Add($"f{Time.frameCount} t{Time.realtimeSinceStartup:F2} {s}");

    void LateUpdate() => Tick("late");
    void FixedUpdate() => Tick("fixed");

    void Tick(string where)
    {
        var motor = FindFirstObjectByType<SeaSick.Ship.ShipMotor>();
        if (motor != last)
        {
            Note($"[{where}] ship object -> {(motor != null ? motor.GetInstanceID().ToString() : "null")} at {(motor != null ? motor.transform.position.ToString() : "-")}");
            last = motor;
            if (motor != null) lastPos = motor.transform.position;
        }
        if (motor == null) return;
        var p = motor.transform.position;
        if ((p - lastPos).sqrMagnitude > 9f)
        {
            var anchor = motor.GetComponent<SeaSick.Ship.AnchorController>();
            Note($"[{where}] JUMP {lastPos} -> {p} ({Vector3.Distance(lastPos, p):F0} m) anchor={anchor?.CurrentState} restoring={SeaSick.Save.SaveGame.Restoring}");
        }
        lastPos = p;
        bool r = SeaSick.Save.SaveGame.Restoring;
        if (r != lastRestoring) { Note($"restoring={r}"); lastRestoring = r; }
        var v = FindFirstObjectByType<SeaSick.Voyage.VoyageManager>();
        bool h = v != null && v.AtHome;
        if (h != lastHome) { Note($"voyage AtHome={h}"); lastHome = h; }
        var d = SeaSick.World.Dock.Home;
        string ds = d != null ? d.name + "@" + d.Berth : "null";
        if (ds != lastDock) { Note($"Dock.Home={ds}"); lastDock = ds; }
    }
}
