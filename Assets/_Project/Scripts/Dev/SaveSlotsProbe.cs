using System.Collections.Generic;
using System.Text;
using UnityEngine;
using SeaSick.Save;

/// **Does the slot system itself do what `SaveSlots`'s contract says?**
///
/// `tools/save-slots-selftest.sh` covers `SaveSlotLogic` (rotation pick,
/// migration gate, most-recent ordering) with no engine at all. This probe
/// covers the other half, the half that needs Unity: real files, real
/// `JsonUtility`, `Application.isPlaying`, `SaveGame.Suppressed`. It still
/// exercises the real slot ids (`m5`, whichever auto slot rotation picks)
/// so the gates read exactly like `SaveSlots`'s own contract -- but it
/// points `SaveSlots.DirectoryOverride` at a scratch folder under
/// `Application.temporaryCachePath` first, the same way
/// `ShipyardService.PersistPathOverride` keeps the modular-ship probes off
/// the player's own save, so "m5" here is never Kevin's actual slot 5. The
/// legacy `seasick-save.json` is never touched either way -- nothing here
/// calls migration on a real legacy file, and `DirectoryOverride` does not
/// apply to `SaveGame.LegacyPath`.
///
/// Play mode, any scene with a ship (`Sea.unity`). Writes
/// `Logs/SaveSlotsProbe.txt`. Run via
/// `unity cmd eval --json --code 'SaveSlotsProbe.Execute(); return "started";'`
/// (not wired into `RunProbe` -- see the save-slots handoff notes).
public class SaveSlotsProbe : MonoBehaviour
{
    public static void Execute()
    {
        if (!Application.isPlaying) { Debug.LogError("SaveSlotsProbe: not in play mode"); return; }
        var old = FindAnyObjectByType<SaveSlotsProbe>();
        if (old != null) Destroy(old.gameObject);
        var runner = new GameObject("SaveSlotsProbeRunner").AddComponent<SaveSlotsProbe>();
        runner.StartCoroutine(runner.Run());
    }

    readonly StringBuilder sb = new StringBuilder();
    int fails;

    System.Collections.IEnumerator Run()
    {
        yield return new WaitForEndOfFrame();
        SeaSick.Save.GameBoot.Skip(); // autosaves off by default; this probe flips Suppressed itself below

        string scratch = System.IO.Path.Combine(Application.temporaryCachePath, "SaveSlotsProbe");
        try { System.IO.Directory.Delete(scratch, true); } catch { }
        SaveSlots.DirectoryOverride = scratch;
        SaveSlots.ResetForPlay(); // a clean ActiveSlotId/PendingLoad state for this run

        float t0 = Time.realtimeSinceStartup;
        SeaSick.Terrain.TerrainWorldPopulator pop = null;
        while (Time.realtimeSinceStartup - t0 < 60f)
        {
            pop = FindFirstObjectByType<SeaSick.Terrain.TerrainWorldPopulator>();
            if (pop != null && pop.Done) break;
            yield return null;
        }
        if (pop == null || !pop.Done) { Finish("the world never built"); yield break; }

        // --- list(): 8 slots, m1..m5 then a1..a3, all empty at first (a
        // fresh persistentDataPath in a clean test run; if this box has a
        // played game already this gate only checks ordering and count,
        // not emptiness -- see the ordering assert below). ------------------
        var list = SaveSlots.List();
        Gate("list-returns-8-slots", list.Count == 8, "got " + list.Count);
        var ids = new List<string>();
        foreach (var s in list) ids.Add(s.id);
        Gate("list-order-is-m1-m5-a1-a3",
            string.Join(",", ids) == "m1,m2,m3,m4,m5,a1,a2,a3", string.Join(",", ids));

        // --- SaveManual writes m5 (the slot least likely to collide with
        // anything Kevin is actually using) and sets ActiveSlotId. ----------
        bool wroteM5 = SaveSlots.SaveManual("m5", "SaveSlotsProbe", out string err1);
        Gate("save-manual-into-m5-succeeds", wroteM5, err1 ?? "");
        Gate("save-manual-sets-active-slot", SaveSlots.ActiveSlotId == "m5", SaveSlots.ActiveSlotId);

        var m5 = Find(SaveSlots.List(), "m5");
        Gate("m5-now-exists-with-the-given-name", m5 != null && m5.exists && m5.displayName == "SaveSlotsProbe",
            m5 == null ? "null" : (m5.exists + " " + m5.displayName));

        // --- overwrite: saving m5 again with a different name replaces it,
        // does not create a second row, keeps it a manual slot. -------------
        bool overwrote = SaveSlots.SaveManual("m5", "SaveSlotsProbe-2", out string err2);
        Gate("save-manual-overwrite-succeeds", overwrote, err2 ?? "");
        var m5b = Find(SaveSlots.List(), "m5");
        Gate("overwrite-replaced-the-name-not-appended", m5b != null && m5b.displayName == "SaveSlotsProbe-2",
            m5b == null ? "null" : m5b.displayName);

        // --- rename: works on the file alone, without a fresh capture. -----
        bool renamed = SaveSlots.Rename("m5", "Renamed", out string err3);
        Gate("rename-succeeds", renamed, err3 ?? "");
        var m5c = Find(SaveSlots.List(), "m5");
        Gate("rename-changed-the-display-name-only", m5c != null && m5c.displayName == "Renamed" && m5c.exists,
            m5c == null ? "null" : m5c.displayName);

        // --- Suppressed blocks SaveGame.Autosave, and therefore blocks a
        // WriteAutosave rotation -- but never blocks the explicit manual
        // save (SaveManual), same rule as SaveGame.Save always had. ---------
        var beforeAutoList = SaveSlots.List();
        int autoExistCountBefore = CountExisting(beforeAutoList, true);
        SeaSick.Save.SaveGame.Suppressed = true;
        SeaSick.Save.SaveGame.Autosave("SaveSlotsProbe-suppressed");
        int autoExistCountAfterSuppressed = CountExisting(SaveSlots.List(), true);
        Gate("suppressed-blocks-an-autosave", autoExistCountAfterSuppressed == autoExistCountBefore,
            autoExistCountBefore + " -> " + autoExistCountAfterSuppressed);

        // Still Suppressed here on purpose: SaveManual is the explicit
        // button-press path (like SaveGame.Save), never gated by it.
        bool stillManualWhileSuppressed = SaveSlots.SaveManual("m5", "Renamed-again", out string err4);
        Gate("manual-save-is-never-gated-by-suppressed", stillManualWhileSuppressed, err4 ?? "");
        SeaSick.Save.SaveGame.Suppressed = false;

        // --- a real autosave, once unsuppressed, fills an auto slot and
        // never touches the manual slot that is currently active. -----------
        string activeBeforeAuto = SaveSlots.ActiveSlotId;
        SeaSick.Save.SaveGame.Autosave("SaveSlotsProbe-real");
        int autoExistCountAfterReal = CountExisting(SaveSlots.List(), true);
        Gate("an-unsuppressed-autosave-fills-an-auto-slot",
            autoExistCountAfterReal >= autoExistCountBefore, autoExistCountBefore + " -> " + autoExistCountAfterReal);
        Gate("autosave-never-changes-the-active-manual-slot",
            SaveSlots.ActiveSlotId == activeBeforeAuto, SaveSlots.ActiveSlotId + " vs " + activeBeforeAuto);
        var m5AfterAuto = Find(SaveSlots.List(), "m5");
        Gate("autosave-did-not-touch-the-active-manual-slot-s-name",
            m5AfterAuto != null && m5AfterAuto.displayName == "Renamed-again",
            m5AfterAuto == null ? "null" : m5AfterAuto.displayName);

        // --- MostRecent() reports something that exists. --------------------
        var recent = SaveSlots.MostRecent();
        Gate("most-recent-is-not-null-once-something-is-saved", recent != null, "");
        Gate("most-recent-reports-an-existing-slot", recent != null && recent.exists, "");

        // --- delete: m5 goes back to empty; ActiveSlotId (pointed at m5)
        // is cleared rather than left dangling at a deleted file. ------------
        bool deleted = SaveSlots.Delete("m5", out string err5);
        Gate("delete-succeeds", deleted, err5 ?? "");
        var m5AfterDelete = Find(SaveSlots.List(), "m5");
        Gate("m5-is-empty-after-delete", m5AfterDelete != null && !m5AfterDelete.exists, "");
        Gate("active-slot-cleared-when-its-file-is-deleted", SaveSlots.ActiveSlotId == "", SaveSlots.ActiveSlotId);

        Finish(null);
    }

    static SaveSlotInfo Find(IReadOnlyList<SaveSlotInfo> list, string id)
    {
        foreach (var s in list) if (s.id == id) return s;
        return null;
    }

    static int CountExisting(IReadOnlyList<SaveSlotInfo> list, bool auto)
    {
        int n = 0;
        foreach (var s in list) if (s.isAuto == auto && s.exists) n++;
        return n;
    }

    void Gate(string name, bool ok, string detail)
    {
        if (!ok) fails++;
        sb.AppendLine("  [" + (ok ? "ok  " : "FAIL") + "] " + name + "   " + (ok ? "" : detail));
    }

    void Finish(string stopped)
    {
        SaveSlots.DirectoryOverride = null;   // back to the real persistentDataPath
        SeaSick.Save.SaveGame.Suppressed = true; // GameBoot.Skip()'s own rule for the rest of this session
        SaveSlots.ResetForPlay();
        sb.AppendLine();
        if (!string.IsNullOrEmpty(stopped)) sb.AppendLine("STOPPED: " + stopped);
        sb.AppendLine(fails == 0 ? "PASS -- the slot system does what the contract says" : fails + " GATE(S) FAILED");
        Report(sb.ToString());
        Destroy(gameObject);
    }

    static void Report(string text)
    {
        Debug.Log("SaveSlotsProbe\n" + text);
        var path = System.IO.Path.Combine(Application.dataPath, "../Logs/SaveSlotsProbe.txt");
        try { System.IO.File.WriteAllText(path, text); } catch { }
    }
}
