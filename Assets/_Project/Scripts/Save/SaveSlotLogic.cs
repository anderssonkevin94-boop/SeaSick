using System;
using System.Collections.Generic;

namespace SeaSick.Save
{
    /// **The slot system's decision logic, kept `UnityEngine`-free on
    /// purpose.** Everything here takes plain data in and hands plain data
    /// back -- no `Application.persistentDataPath`, no `Debug.Log`, no
    /// `JsonUtility` -- so `tools/save-slots-selftest.sh` can compile and
    /// run it with plain `csc` against nothing but the .NET standard
    /// library, the same way `tools/modular-selftest.sh` runs the modular
    /// core, except this needs no `UnityEngine.CoreModule` reference and no
    /// JSON shim at all: there is nothing engine-shaped left to shim.
    /// `SaveSlots` (the Unity-facing half, in `SaveSlots.cs`) calls into
    /// this for every decision that does not actually need the engine, and
    /// does the file/JSON/scene work itself.
    public static class SaveSlotLogic
    {
        public static readonly string[] ManualIds = { "m1", "m2", "m3", "m4", "m5" };
        public static readonly string[] AutoIds = { "a1", "a2", "a3" };

        public static bool IsManual(string id)
        {
            foreach (var m in ManualIds) if (m == id) return true;
            return false;
        }

        public static bool IsAuto(string id)
        {
            foreach (var a in AutoIds) if (a == id) return true;
            return false;
        }

        public static bool IsKnownSlot(string id) => IsManual(id) || IsAuto(id);

        /// Manual m1..m5 then rotating a1..a3 -- the order `List()` returns
        /// and the order `PickMostRecent`'s tie-break favours.
        public static IEnumerable<string> ListOrder()
        {
            foreach (var m in ManualIds) yield return m;
            foreach (var a in AutoIds) yield return a;
        }

        /// **Which auto slot the next autosave overwrites.** An empty one
        /// wins outright, walking a1->a2->a3, so rotation fills the pool
        /// before it recycles anything; once all three exist, the one with
        /// the OLDEST write time is next. No persisted rotation pointer is
        /// needed -- restarting the game does not reset "whose turn it is",
        /// the write times already say.
        public static string PickAutoSlotToWrite(Func<string, bool> exists, Func<string, DateTime> lastWriteUtc)
        {
            foreach (var id in AutoIds)
                if (!exists(id)) return id;

            string best = AutoIds[0];
            DateTime bestTime = lastWriteUtc(best);
            foreach (var id in AutoIds)
            {
                var t = lastWriteUtc(id);
                if (t < bestTime) { bestTime = t; best = id; }
            }
            return best;
        }

        /// **The migration gate.** True exactly once, ever: manual slot 1
        /// is empty and the pre-slots single file exists. Idempotent by
        /// construction -- once m1 has anything in it (the migration itself
        /// put it there, or a player saved over it), this is false forever,
        /// with no separate flag to keep in sync.
        public static bool ShouldMigrate(bool manualSlot1Exists, bool legacyFileExists)
            => !manualSlot1Exists && legacyFileExists;

        /// **"Continue" across all 8.** The existing slot with the newest
        /// `savedAtUtc`; a tie is broken by slot order (manual before auto,
        /// lower numbers first) so the result never depends on filesystem
        /// timestamp precision alone.
        public static string PickMostRecent(IEnumerable<string> idsInOrder,
            Func<string, bool> exists, Func<string, DateTime> savedAtUtc)
        {
            string best = null;
            DateTime bestTime = default;
            foreach (var id in idsInOrder)
            {
                if (!exists(id)) continue;
                var t = savedAtUtc(id);
                if (best == null || t > bestTime) { best = id; bestTime = t; }
            }
            return best;
        }
    }
}
