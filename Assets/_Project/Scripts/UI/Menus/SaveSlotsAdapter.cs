using System;
using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.UI.Menus
{
    /// **The one file that names `SeaSick.Save.SaveSlots` directly.**
    ///
    /// The slot backend (5 manual + 3 auto saves) is being built in parallel,
    /// in `Save/`, which this session does not touch. Its contract is fixed
    /// (see the task brief) but the class does not exist in this worktree yet
    /// -- so every menu screen calls THIS adapter instead of the real type,
    /// and reconciling the two branches means editing one file instead of
    /// four. Nothing here has a UI opinion; it is a thin, typed pass-through.
    internal static class SaveSlotsAdapter
    {
        public static int ManualCount => SeaSick.Save.SaveSlots.ManualCount;
        public static int AutoCount => SeaSick.Save.SaveSlots.AutoCount;

        public static List<SeaSick.Save.SaveSlotInfo> List() => new List<SeaSick.Save.SaveSlotInfo>(SeaSick.Save.SaveSlots.List());
        public static SeaSick.Save.SaveSlotInfo MostRecent() => SeaSick.Save.SaveSlots.MostRecent();

        public static bool SaveManual(string slotId, string displayName, out string error) =>
            SeaSick.Save.SaveSlots.SaveManual(slotId, displayName, out error);

        public static bool Delete(string slotId, out string error) =>
            SeaSick.Save.SaveSlots.Delete(slotId, out error);

        public static bool Rename(string slotId, string name, out string error) =>
            SeaSick.Save.SaveSlots.Rename(slotId, name, out error);

        public static void RequestLoad(string slotId) => SeaSick.Save.SaveSlots.RequestLoad(slotId);
        public static string PendingLoadSlot => SeaSick.Save.SaveSlots.PendingLoadSlot;

        public static void RequestNewGame() => SeaSick.Save.SaveSlots.RequestNewGame();
        public static bool PendingNewGame => SeaSick.Save.SaveSlots.PendingNewGame;

        public static string ActiveSlotId => SeaSick.Save.SaveSlots.ActiveSlotId;
        public static float SecondsSinceLastSave => SeaSick.Save.SaveSlots.SecondsSinceLastSave;
        public static bool IsSaving => SeaSick.Save.SaveSlots.IsSaving;

        public static event Action<SeaSick.Save.SaveSlotInfo> Saved
        {
            add { SeaSick.Save.SaveSlots.Saved += value; }
            remove { SeaSick.Save.SaveSlots.Saved -= value; }
        }

        /// Restores whatever `RequestLoad` asked for, once the fresh scene's
        /// world exists. **This call is the loading-screen hook**: a future
        /// progress bar wraps it (shows while it runs, or polls a progress
        /// value beside it) rather than anything in `GameBoot` itself -- see
        /// `GameBoot.HandleSlotRequest`, the only place this is called from.
        public static bool RestorePending(out string error) =>
            SeaSick.Save.SaveSlots.RestorePending(out error);

        // --- display formatting -------------------------------------------
        //
        // One place for "how a slot reads on a row", so the Load list and the
        // Pause "Save" list format identically.

        public static string FormatWhen(DateTime savedAtUtc)
        {
            if (savedAtUtc == default) return "";
            var local = savedAtUtc.ToLocalTime();
            return local.ToString("MMM d, HH:mm");
        }

        public static string FormatPlayTime(float seconds)
        {
            if (seconds <= 0f) return "0m played";
            int total = Mathf.RoundToInt(seconds);
            int h = total / 3600;
            int m = (total % 3600) / 60;
            return h > 0 ? $"{h}h {m:00}m played" : $"{m}m played";
        }

        /// "<island> — <date>", the default name offered for a fresh manual
        /// save. The island comes from the live `AnchorController` (read
        /// only -- that component belongs to someone else's system) and
        /// falls back to "At sea" when she is not lying anywhere in particular.
        public static string DefaultSaveName()
        {
            string where = "At sea";
            var anchor = UnityEngine.Object.FindFirstObjectByType<SeaSick.Ship.AnchorController>();
            if (anchor != null && anchor.CurrentIsland != null && !string.IsNullOrEmpty(anchor.CurrentIsland.name))
                where = anchor.CurrentIsland.IsHome ? "Home island" : anchor.CurrentIsland.DisplayName;
            return where + " — " + DateTime.Now.ToString("MMM d, HH:mm");
        }
    }
}
