using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World.Life
{
    /// The kind constants a `LifeEvent`/`LifeStory` can name. Strings, not an
    /// enum, because `JsonUtility` writes them straight through and a future
    /// kind never needs a save migration. Several are defined now for
    /// phases this session does not build, so the story generator and the
    /// save format do not change shape again when they land.
    public static class LifeEvents
    {
        public const string Born = "Born";
        public const string Recruited = "Recruited";
        public const string WentAshore = "WentAshore";
        public const string WentAboard = "WentAboard";
        public const string Hungry = "Hungry";
        public const string SurvivedRaid = "SurvivedRaid";
        public const string HuntingKill = "HuntingKill";
        public const string BuildingFinished = "BuildingFinished";

        // --- future phases, named now so the shape never changes -----------
        public const string Overboard = "Overboard";
        public const string Rescued = "Rescued";
        public const string RescuedOther = "RescuedOther";
        public const string Downed = "Downed";
        public const string DraggedOther = "DraggedOther";
        public const string WashedAshore = "WashedAshore";

        // --- death causes ----------------------------------------------------
        public const string KilledInRaid = "KilledInRaid";
        public const string LostAtSea = "LostAtSea";
        public const string HuntingAccident = "HuntingAccident";
        public const string Shipwreck = "Shipwreck";
    }

    /// **The global life registry.** One `LifeRecord` per name, one
    /// `GraveRecord` per death, kept in memory and synced to `SaveData` at
    /// save/load (`SyncTo`/`SyncFrom`) the same way every other static world
    /// registry in this project (`TimeOfDay`, `OceanTime`) rides the save
    /// file without being a MonoBehaviour itself.
    ///
    /// Global rather than per-camp, on purpose (Kevin's brief): a hand moves
    /// between the ship and any camp, and his story should follow him.
    public static class Lives
    {
        static readonly Dictionary<string, LifeRecord> records = new Dictionary<string, LifeRecord>();
        static readonly List<GraveRecord> graveyard = new List<GraveRecord>();

        /// Raised the instant `OutpostLedger.Die` writes a `GraveRecord` --
        /// phase 3's tombstone-siting flow subscribes to this. Not saved;
        /// a session that was not open when somebody died does not need to
        /// be told twice (the graveyard list already has them).
        public static event System.Action<GraveRecord> Died;

        public static IReadOnlyDictionary<string, LifeRecord> Records => records;
        public static IReadOnlyList<GraveRecord> Graveyard => graveyard;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            // Domain reload may be off in the editor: forget the last run
            // rather than carry a dead session's cast into a fresh load.
            records.Clear();
            graveyard.Clear();
            Died = null;
        }

        /// The record for `name`, created empty if this is the first time
        /// anything has been logged about them.
        public static LifeRecord Record(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (!records.TryGetValue(name, out var r))
            {
                r = new LifeRecord { name = name };
                records[name] = r;
            }
            return r;
        }

        /// Is this name already spoken for -- alive anywhere, or in the
        /// graveyard? `VillagerNames.NextFor` uses this so a new recruit
        /// never takes a dead man's name.
        public static bool IsTaken(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            foreach (var g in graveyard) if (g.name == name) return true;
            return false;
        }

        /// **Log one event on `name`.** Creates the record lazily. A repeat
        /// of the same kind+camp+other bumps `count` and refreshes `day`
        /// instead of appending -- "went over the rail twice" is one row,
        /// not two. Bounded by `LifeTuning.MaxEventsPerLife`: once full, the
        /// event with the smallest `count` (the least-established one) is
        /// dropped to make room, so a life stays a life, not an ever-growing
        /// diary.
        public static void Log(string name, string kind, string camp = "", string other = null)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(kind)) return;
            var r = Record(name);
            if (r == null) return;
            camp ??= "";
            other ??= "";
            int day = TimeOfDay.Day;
            if (r.bornDay < 0 && (kind == LifeEvents.Born || kind == LifeEvents.Recruited))
            {
                r.bornDay = day;
                r.homeCamp = camp;
            }
            if (string.IsNullOrEmpty(r.homeCamp) && !string.IsNullOrEmpty(camp)) r.homeCamp = camp;

            for (int i = 0; i < r.events.Count; i++)
            {
                var e = r.events[i];
                if (e.kind == kind && e.camp == camp && e.other == other)
                {
                    e.count++;
                    e.day = day;
                    return;
                }
            }

            int cap = Mathf.Max(1, LifeTuning.MaxEventsPerLife);
            if (r.events.Count >= cap)
            {
                int worst = 0;
                for (int i = 1; i < r.events.Count; i++)
                    if (r.events[i].count < r.events[worst].count) worst = i;
                r.events.RemoveAt(worst);
            }
            r.events.Add(new LifeEvent { kind = kind, camp = camp, other = other, day = day, count = 1 });
        }

        /// A death. Called once by `OutpostLedger.Die`, never directly by
        /// anything else -- see that method for what else it does (deposit
        /// the load, remove the roster row, remove the body).
        public static void Bury(GraveRecord grave)
        {
            if (grave == null || string.IsNullOrEmpty(grave.name)) return;
            graveyard.Add(grave);
            records.Remove(grave.name);
            Died?.Invoke(grave);
        }

        // --- save/load -------------------------------------------------------

        /// Copy the live registry into a save's lists. Call right before
        /// writing the file.
        public static void SyncTo(List<LifeRecord> outLives, List<GraveRecord> outGraves)
        {
            outLives.Clear();
            foreach (var kv in records) outLives.Add(kv.Value);
            outGraves.Clear();
            outGraves.AddRange(graveyard);
        }

        /// Rebuild the live registry from a save's lists. Call right after
        /// reading the file, before anything else logs an event. Missing in
        /// an old save (null lists) reads as nobody has a story yet.
        public static void SyncFrom(List<LifeRecord> savedLives, List<GraveRecord> savedGraves)
        {
            records.Clear();
            graveyard.Clear();
            if (savedLives != null)
                foreach (var r in savedLives)
                    if (r != null && !string.IsNullOrEmpty(r.name))
                    {
                        r.events ??= new List<LifeEvent>();
                        records[r.name] = r;
                    }
            if (savedGraves != null)
                foreach (var g in savedGraves)
                    if (g != null && !string.IsNullOrEmpty(g.name))
                    {
                        g.story ??= new string[3];
                        graveyard.Add(g);
                    }
        }
    }
}
