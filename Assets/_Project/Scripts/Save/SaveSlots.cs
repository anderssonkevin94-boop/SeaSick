using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.Save
{
    /// One row of `SaveSlots.List()` -- everything a slot picker needs to
    /// draw a tile without reading a save file itself.
    public class SaveSlotInfo
    {
        /// "m1".."m5" (fixed manual) or "a1".."a3" (rotating autosave).
        public string id;
        public bool isAuto;
        /// False for every field below when this is true: an empty tile.
        public bool exists;
        public string displayName;
        /// Cheap, derived text -- "home berth", or the ship's own position
        /// when she was under way or anchored off (no island name is saved
        /// anywhere; see `SaveSlots.DescribeLocation`).
        public string location;
        public System.DateTime savedAtUtc;
        /// `SaveData.timeSeconds` -- the world clock the save carries, not
        /// a wall-clock playtime (nothing in `SaveData` counts that).
        public float playSeconds;
        public string reason;
    }

    /// **Eight save slots on top of the one-file engine `SaveGame` already
    /// is.** Kevin, 2026-09-26: 5 fixed manual slots (`m1`..`m5`, named and
    /// overwritten only by an explicit save) plus 3 rotating autosave slots
    /// (`a1`..`a3`, filled empty-first then oldest-first, and never a
    /// manual slot) that catch docking, refits, buildings, voyage start,
    /// app pause/quit, and now a 5-minute timer (`SaveAutosaveTimer`).
    ///
    /// **Division of labour with `SaveGame`.** `SaveGame` is still the
    /// whole engine -- `Capture` reads the live scene, `Restore` puts a
    /// `SaveData` back into it, `Read`/`SaveTo` are the raw JSON-at-a-path
    /// primitives. This class only decides WHICH path: it is the slot
    /// table, the rotation/migration rules (delegated to the
    /// `UnityEngine`-free `SaveSlotLogic`), and the one place that knows a
    /// slot file is `seasick-save-<id>.json` next to the pre-slots
    /// `seasick-save.json`.
    ///
    /// **Migration.** The very first call into this class after slots
    /// shipped finds manual slot 1 empty and the old single file present,
    /// and copies it into `m1` byte-for-byte -- the original stays on disk
    /// untouched, forever, as Kevin's backup. `SaveSlotLogic.ShouldMigrate`
    /// is the whole rule and it is idempotent by construction: once `m1`
    /// has anything in it (the migration, or a player's own save), the
    /// check is false and stays false.
    ///
    /// **`ActiveSlotId`** is "the slot the running game last loaded or
    /// explicitly saved" -- set by `SaveManual` and by a load that actually
    /// starts (`RestorePending`), left alone by every autosave (autosaves
    /// never touch a manual slot, and must not silently retarget writes at
    /// whichever auto slot they just filled either). `SaveGame.Path`
    /// resolves through `ResolveActivePath`, which falls back to `m1` when
    /// nothing has been loaded or saved yet this session -- that is what
    /// the SAVE button and `ShipyardService`'s live refit-persist write to
    /// on a brand new voyage.
    public static class SaveSlots
    {
        public const int ManualCount = 5;
        public const int AutoCount = 3;

        public static string ActiveSlotId { get; private set; } = "";
        public static string PendingLoadSlot { get; private set; } = "";
        public static bool PendingNewGame { get; private set; }
        public static bool IsSaving { get; private set; }

        public static event System.Action<SaveSlotInfo> Saved;

        static float lastSaveRealtime = -1f;

        /// Seconds since the last write through this class (manual or
        /// auto), or +infinity if nothing has been written this session --
        /// what a "saving..." / "last saved Ns ago" indicator reads.
        public static float SecondsSinceLastSave =>
            lastSaveRealtime < 0f ? float.PositiveInfinity : Time.realtimeSinceStartup - lastSaveRealtime;

        /// Statics outlive play mode here, same as `SaveGame`/`GameBoot` --
        /// reset alongside them.
        public static void ResetForPlay()
        {
            ActiveSlotId = "";
            PendingLoadSlot = "";
            PendingNewGame = false;
            IsSaving = false;
            lastSaveRealtime = -1f;
        }

#if UNITY_EDITOR
        const string EditorOverrideKey = "SeaSick.SaveDirOverride";

        /// **The editor-persistent half of the test redirect.** A plain C#
        /// static set in edit mode is not good enough: `GameBoot` runs
        /// (`DefaultExecutionOrder(-300)`) and can call into `SaveSlots`
        /// (and trigger `EnsureMigrated`) on the very first frames of Play,
        /// before any `unity cmd eval` sent AFTER `editor_play` returns has
        /// a chance to run, and before any probe's own coroutine reaches
        /// its `WaitForEndOfFrame`. That is exactly the race that put a
        /// migrated `seasick-save-m1.json` into Kevin's REAL save folder
        /// on 2026-09-26. `UnityEditor.SessionState` survives that --
        /// unlike a runtime static, it is readable from frame 0 of Play as
        /// long as it was set in edit mode beforehand, whether or not a
        /// domain reload happens on entering Play. Set it via
        /// `RunProbe.SetSaveDirOverride(path)` (edit mode, before Play);
        /// clear it with `RunProbe.ClearSaveDirOverride()`. Compiled out of
        /// every player build -- `UnityEditor` does not exist there.
        public static string EditorTestDirectory
        {
            get
            {
                string v = UnityEditor.SessionState.GetString(EditorOverrideKey, "");
                return string.IsNullOrEmpty(v) ? null : v;
            }
            set
            {
                if (string.IsNullOrEmpty(value)) UnityEditor.SessionState.EraseString(EditorOverrideKey);
                else UnityEditor.SessionState.SetString(EditorOverrideKey, value);
            }
        }
#endif

        static string directoryOverrideRuntime;

        /// **Test-only redirect for every slot file** (`SaveGame.LegacyPath`
        /// follows it too, in the editor -- see there). Null in real play
        /// and in any player build. A runtime set (what `SaveSlotsProbe`
        /// and the shipyard probes do once they are already running) wins
        /// over the editor-persistent `EditorTestDirectory` set before Play
        /// -- both point off the player's own save, a probe's own scratch
        /// dir is just more specific. `ShipyardService.PersistPathOverride`
        /// is the equivalent for a refit's own file.
        public static string DirectoryOverride
        {
            get
            {
                if (directoryOverrideRuntime != null) return directoryOverrideRuntime;
#if UNITY_EDITOR
                return EditorTestDirectory;
#else
                return null;
#endif
            }
            set { directoryOverrideRuntime = value; }
        }

        public static string PathFor(string slotId) =>
            System.IO.Path.Combine(DirectoryOverride ?? Application.persistentDataPath, "seasick-save-" + slotId + ".json");

        /// `SaveGame.Path`'s implementation -- kept here so the "what does
        /// an unset active slot default to" rule lives in one place.
        internal static string ResolveActivePath() =>
            PathFor(string.IsNullOrEmpty(ActiveSlotId) ? "m1" : ActiveSlotId);

        // --- migration ----------------------------------------------------

        /// Copies the pre-slots file into manual slot 1 the first time
        /// anything asks. Cheap (two `File.Exists` calls) when there is
        /// nothing to do, which is every call after the first.
        static void EnsureMigrated()
        {
            try
            {
                string m1 = PathFor("m1");
                string legacy = SaveGame.LegacyPath;
                if (!SaveSlotLogic.ShouldMigrate(System.IO.File.Exists(m1), System.IO.File.Exists(legacy)))
                    return;

                System.IO.File.Copy(legacy, m1, overwrite: false);

                // A friendly name so it does not read as an anonymous slot;
                // best-effort -- the migration already succeeded even if
                // this half fails or the file is unreadable for some reason.
                try
                {
                    var d = SaveGame.Read(m1);
                    if (d != null && string.IsNullOrEmpty(d.slotDisplayName))
                    {
                        d.slotDisplayName = "Slot 1";
                        System.IO.File.WriteAllText(m1, JsonUtility.ToJson(d, true));
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("SaveSlots: migrated " + m1 + " but could not name it: " + e.Message);
                }

                Debug.Log("SaveSlots: migrated the pre-slots save -> " + m1
                    + "   (original kept at " + legacy + ")");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("SaveSlots: migration failed: " + e.Message);
            }
        }

        // --- reading --------------------------------------------------------

        static string DefaultName(string id, bool isAuto) =>
            isAuto ? "Autosave " + id.Substring(1) : "Slot " + id.Substring(1);

        /// Cheap, derived from the save itself -- no island name is ever
        /// saved (see `SaveData`/`OutpostSave`), so this reads the ship's
        /// own state instead of the world she is sitting in.
        static string DescribeLocation(SaveData d)
        {
            if (d?.ship == null) return "";
            if (d.ship.anchor == 2) return "home berth";
            if (d.ship.anchor == 1)
                return "anchored near (" + Mathf.RoundToInt(d.ship.x) + ", " + Mathf.RoundToInt(d.ship.z) + ")";
            return "under way near (" + Mathf.RoundToInt(d.ship.x) + ", " + Mathf.RoundToInt(d.ship.z) + ")";
        }

        static SaveSlotInfo ReadInfo(string id, bool isAuto)
        {
            var info = new SaveSlotInfo { id = id, isAuto = isAuto };
            string path = PathFor(id);
            SaveData d = System.IO.File.Exists(path) ? SaveGame.Read(path) : null;
            if (d == null)
            {
                info.exists = false;
                info.displayName = DefaultName(id, isAuto);
                return info;
            }
            info.exists = true;
            info.displayName = string.IsNullOrEmpty(d.slotDisplayName) ? DefaultName(id, isAuto) : d.slotDisplayName;
            info.location = DescribeLocation(d);
            info.savedAtUtc = System.IO.File.GetLastWriteTimeUtc(path);
            info.playSeconds = (float)d.timeSeconds;
            info.reason = d.reason ?? "";
            return info;
        }

        /// Manual m1..m5 then rotating a1..a3, empty slots included with
        /// `exists = false`.
        public static IReadOnlyList<SaveSlotInfo> List()
        {
            EnsureMigrated();
            var result = new List<SaveSlotInfo>(ManualCount + AutoCount);
            foreach (var id in SaveSlotLogic.ManualIds) result.Add(ReadInfo(id, false));
            foreach (var id in SaveSlotLogic.AutoIds) result.Add(ReadInfo(id, true));
            return result;
        }

        /// The existing slot with the newest save, or null with none.
        public static SaveSlotInfo MostRecent()
        {
            EnsureMigrated();
            string bestId = SaveSlotLogic.PickMostRecent(
                SaveSlotLogic.ListOrder(),
                id => System.IO.File.Exists(PathFor(id)),
                id => System.IO.File.GetLastWriteTimeUtc(PathFor(id)));
            return bestId == null ? null : ReadInfo(bestId, SaveSlotLogic.IsAuto(bestId));
        }

        // --- writing --------------------------------------------------------

        /// The explicit "save to this slot" a menu offers -- always a
        /// manual slot, overwrite allowed (the UI is the one that confirms
        /// it with the player). Sets `ActiveSlotId`, same as picking
        /// CONTINUE into it would.
        public static bool SaveManual(string slotId, string displayName, out string error)
        {
            error = null;
            if (!SaveSlotLogic.IsManual(slotId)) { error = "not a manual slot: " + slotId; return false; }
            if (!Application.isPlaying) { error = "not in play mode"; return false; }

            IsSaving = true;
            try
            {
                string name = string.IsNullOrEmpty(displayName) ? DefaultName(slotId, false) : displayName;
                bool ok = SaveGame.SaveTo(PathFor(slotId), "manual save", d => d.slotDisplayName = name);
                if (!ok) { error = "the save routine refused or failed; see the console"; return false; }
                ActiveSlotId = slotId;
                lastSaveRealtime = Time.realtimeSinceStartup;
                Saved?.Invoke(ReadInfo(slotId, false));
                return true;
            }
            catch (System.Exception e)
            {
                error = e.Message;
                return false;
            }
            finally { IsSaving = false; }
        }

        /// **`SaveGame.Autosave`'s actual write, and every other AUTOMATIC
        /// writer's persist target** (`ShipyardService.ApplyRefit` among
        /// them, 2026-09-26 -- Kevin's rule: an automatic write never
        /// overwrites a manual save). Rotates the auto pool
        /// (`SaveSlotLogic.PickAutoSlotToWrite`) and never touches
        /// `ActiveSlotId` -- an autosave is a safety net, not a change of
        /// "which slot am I playing". Returns whether the write actually
        /// landed, same as `SaveGame.SaveTo`, so a caller with its own
        /// atomic rollback (the refit) can undo on failure.
        internal static bool WriteAutosave(string reason)
        {
            string target = SaveSlotLogic.PickAutoSlotToWrite(
                id => System.IO.File.Exists(PathFor(id)),
                id => System.IO.File.GetLastWriteTimeUtc(PathFor(id)));

            IsSaving = true;
            try
            {
                bool ok = SaveGame.SaveTo(PathFor(target), reason, d => d.slotDisplayName = DefaultName(target, true));
                if (ok)
                {
                    lastSaveRealtime = Time.realtimeSinceStartup;
                    Saved?.Invoke(ReadInfo(target, true));
                }
                return ok;
            }
            finally { IsSaving = false; }
        }

        public static bool Delete(string slotId, out string error)
        {
            error = null;
            if (!SaveSlotLogic.IsKnownSlot(slotId)) { error = "unknown slot: " + slotId; return false; }
            try
            {
                string path = PathFor(slotId);
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
                if (ActiveSlotId == slotId) ActiveSlotId = "";
                return true;
            }
            catch (System.Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        /// Works whether or not a game is running -- a menu's slot list is
        /// often browsed from outside play mode -- by rewriting the file's
        /// `slotDisplayName` directly rather than going through `Capture`.
        public static bool Rename(string slotId, string displayName, out string error)
        {
            error = null;
            if (!SaveSlotLogic.IsKnownSlot(slotId)) { error = "unknown slot: " + slotId; return false; }
            string path = PathFor(slotId);
            var d = System.IO.File.Exists(path) ? SaveGame.Read(path) : null;
            if (d == null) { error = "slot " + slotId + " is empty"; return false; }
            try
            {
                d.slotDisplayName = displayName ?? "";
                System.IO.File.WriteAllText(path, JsonUtility.ToJson(d, true));
                return true;
            }
            catch (System.Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        // --- loading ----------------------------------------------------------

        /// Marks a slot to restore the next time the scene starts (or, in
        /// this session, the next `RestorePending` call). The menus agent
        /// reloads the scene after calling this; `GameBoot` consumes it via
        /// `RestorePending`.
        public static void RequestLoad(string slotId)
        {
            PendingLoadSlot = slotId;
            PendingNewGame = false;
        }

        /// Next start is a fresh world: no slot is read, none is
        /// overwritten, autosaves run as normal once the player is in.
        public static void RequestNewGame()
        {
            PendingNewGame = true;
            PendingLoadSlot = "";
        }

        /// **What CONTINUE does today, aimed at a chosen slot.** Consumes
        /// `PendingLoadSlot` if one was requested, else falls back to
        /// `MostRecent()` across all 8 -- that is plain CONTINUE, with no
        /// slot named. Kicks off `SaveGame.Restore` on an internal runner
        /// and returns immediately: true means a restore is under way (poll
        /// `SaveGame.Restoring` / `LastRestoreOk` / `LastRestoreNote`, same
        /// as `GameBoot` already does for the loading screen), false means
        /// there was nothing to continue (go New) and `error` says why.
        public static bool RestorePending(out string error)
        {
            error = null;
            EnsureMigrated();

            string slotId = PendingLoadSlot;
            PendingLoadSlot = ""; // consumed whether this succeeds or not

            if (!string.IsNullOrEmpty(slotId))
            {
                if (!System.IO.File.Exists(PathFor(slotId)))
                {
                    error = "slot " + slotId + " has no save";
                    return false;
                }
            }
            else
            {
                var recent = MostRecent();
                if (recent == null) { error = "no save yet"; return false; }
                slotId = recent.id;
            }

            var data = SaveGame.Read(PathFor(slotId));
            if (data == null) { error = "could not read slot " + slotId; return false; }

            ActiveSlotId = slotId;
            var runner = new GameObject("SaveSlotsRestoreRunner").AddComponent<SaveSlotRestoreRunner>();
            Object.DontDestroyOnLoad(runner.gameObject);
            runner.StartCoroutine(RunRestore(runner, data));
            return true;
        }

        static System.Collections.IEnumerator RunRestore(SaveSlotRestoreRunner host, SaveData data)
        {
            yield return SaveGame.Restore(data, host);
            Object.Destroy(host.gameObject);
        }
    }
}
