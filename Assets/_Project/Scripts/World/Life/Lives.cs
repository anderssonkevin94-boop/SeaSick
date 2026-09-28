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
        /// **Phase 4 (pout + floor).** Logged once per pout at the campfire
        /// (`OutpostLedger.StartPout`); repeats collapse the same way any
        /// other event does, so "sulked twice" reads as one row with count 2.
        public const string Pouted = "Pouted";
        public const string HuntingKill = "HuntingKill";
        public const string BuildingFinished = "BuildingFinished";

        // --- future phases, named now so the shape never changes -----------
        public const string Overboard = "Overboard";
        public const string Rescued = "Rescued";
        public const string RescuedOther = "RescuedOther";
        public const string Downed = "Downed";
        public const string DraggedOther = "DraggedOther";
        public const string WashedAshore = "WashedAshore";
        /// **Phase 7.** A stranger found on an island and taken aboard --
        /// logged at pickup, not at creation, so it only ever describes
        /// somebody who actually made it home (`Lives.MarkStranger`'s doc).
        public const string FoundCastaway = "FoundCastaway";
        /// **Phase 7.** Landed at a camp other than `LifeRecord.homeCamp`
        /// (`Outpost.Station`) -- `other` carries the camp they came from.
        public const string Ferried = "Ferried";
        /// **Phase 9 (village defence).** Logged on a defender per raider
        /// he kills -- repeats collapse the same way any other event does,
        /// so "killed 3 raiders" reads as one row with count 3.
        public const string KilledRaider = "KilledRaider";
        /// **Phase 9.** Logged once per raid on every hand who defended at
        /// all during it (`RaidParty.Recall`), whether he landed a kill or
        /// not.
        public const string DefendedCamp = "DefendedCamp";
        /// **Phase 12.** Logged on a defender the moment his STORE spear
        /// wears out mid-fight (`Combat.RaidAlarm.WearOnKill`) -- a hunter's
        /// own spear wears through the hunting path instead and never logs
        /// this.
        public const string SpearBroke = "SpearBroke";

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
        /// **Phase 5a.** Washed-ashore swimmers, waiting to be fetched.
        /// Removing a name from here is phase 7's job (ferrying); nothing
        /// in phase 5a ever un-castaways anyone.
        static readonly List<CastawayRecord> castaways = new List<CastawayRecord>();

        /// **Phase 7.** Islands `CastawayField` has already rolled a
        /// stranger-or-not decision for -- so a declined/picked-up stranger
        /// never re-rolls, and an old save gains strangers naturally (an
        /// island not in this set yet is still unrolled). Keyed by the
        /// `Island` GameObject's own name, same as `CastawayRecord.island`.
        static readonly HashSet<string> strangerRolledIslands = new HashSet<string>();

        /// **Phase 7.** Names created as a fresh stranger (as opposed to a
        /// name that washed ashore off the ship's own crew) -- so the pickup
        /// flow knows which "found" life event to log, and so
        /// `StrangersAlive` can cap them. A name leaves this set the moment
        /// it is picked up (same call that removes it from `castaways`).
        static readonly HashSet<string> strangerNames = new HashSet<string>();

        /// Raised the instant `OutpostLedger.Die` writes a `GraveRecord` --
        /// phase 3's tombstone-siting flow subscribes to this. Not saved;
        /// a session that was not open when somebody died does not need to
        /// be told twice (the graveyard list already has them).
        public static event System.Action<GraveRecord> Died;

        public static IReadOnlyDictionary<string, LifeRecord> Records => records;
        public static IReadOnlyList<GraveRecord> Graveyard => graveyard;
        public static IReadOnlyList<CastawayRecord> Castaways => castaways;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            // Domain reload may be off in the editor: forget the last run
            // rather than carry a dead session's cast into a fresh load.
            records.Clear();
            graveyard.Clear();
            castaways.Clear();
            strangerRolledIslands.Clear();
            strangerNames.Clear();
            Died = null;
        }

        /// **Phase 5a.** Somebody the sea gave back, but not to the ship.
        /// Called once by `Swimmer`'s timeout path; never removes anyone
        /// (phase 7's job).
        public static void MarkCastaway(CastawayRecord c)
        {
            if (c == null || string.IsNullOrEmpty(c.name)) return;
            castaways.Add(c);
        }

        /// Is this name already ashore as a castaway?
        public static bool IsCastaway(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            foreach (var c in castaways) if (c.name == name) return true;
            return false;
        }

        /// **Phase 7.** Somebody `CastawayField` invented out of the name
        /// pool rather than somebody who went overboard -- called once, the
        /// same moment `MarkCastaway` files the record.
        public static void MarkStranger(string name)
        {
            if (!string.IsNullOrEmpty(name)) strangerNames.Add(name);
        }

        /// Was this castaway a stranger `CastawayField` invented, rather
        /// than crew who washed ashore off the ship? Decides which "found"
        /// life event the pickup flow logs.
        public static bool IsStranger(string name) =>
            !string.IsNullOrEmpty(name) && strangerNames.Contains(name);

        /// Has `CastawayField` already rolled its once-per-island
        /// stranger-or-not decision for this island (its GameObject name)?
        public static bool IsIslandRolled(string islandKey) =>
            !string.IsNullOrEmpty(islandKey) && strangerRolledIslands.Contains(islandKey);

        public static void MarkIslandRolled(string islandKey)
        {
            if (!string.IsNullOrEmpty(islandKey)) strangerRolledIslands.Add(islandKey);
        }

        /// Strangers alive right now (created, not yet picked up) -- what
        /// `RecruitTuning.MaxStrangers` caps.
        public static int StrangersAlive()
        {
            int n = 0;
            foreach (var c in castaways) if (strangerNames.Contains(c.name)) n++;
            return n;
        }

        /// **Phase 7: a castaway is fetched.** The only thing that ever
        /// removes a name from `Castaways` -- `Swimmer`/strangers only ever
        /// add. Also drops the stranger flag, so the name is free to be
        /// marked again if it is somehow re-used (it never should be: a
        /// picked-up name boards the crew, which makes it `CrewNames.InUse`
        /// on its own).
        public static bool RemoveCastaway(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            strangerNames.Remove(name);
            for (int i = 0; i < castaways.Count; i++)
                if (castaways[i] != null && castaways[i].name == name)
                {
                    castaways.RemoveAt(i);
                    return true;
                }
            return false;
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
            return IsCastaway(name);
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

        /// **Phase 5a.** Castaways ride a separate list (`SaveData.castaways`)
        /// -- called alongside `SyncTo` rather than folded into it, so an
        /// old save (no castaways yet) still round-trips the lives/graves
        /// call exactly as before.
        public static void SyncCastawaysTo(List<CastawayRecord> outCastaways)
        {
            outCastaways.Clear();
            outCastaways.AddRange(castaways);
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

        /// Companion to `SyncCastawaysTo` -- see there for why it is not
        /// folded into `SyncFrom`. Missing in an old save reads as nobody
        /// washed ashore yet.
        public static void SyncCastawaysFrom(List<CastawayRecord> savedCastaways)
        {
            castaways.Clear();
            if (savedCastaways != null)
                foreach (var c in savedCastaways)
                    if (c != null && !string.IsNullOrEmpty(c.name))
                        castaways.Add(c);
        }

        /// **Phase 7.** The rolled-islands set and the stranger-name flags
        /// ride two more separate lists (`SaveData.strangerIslandsRolled`,
        /// `SaveData.strangerNames`) -- same reasoning as
        /// `SyncCastawaysTo`/`From`: an old save has neither, which reads as
        /// "no island has been rolled yet", exactly right for a save made
        /// before this phase existed.
        public static void SyncStrangersTo(List<string> outRolledIslands, List<string> outStrangerNames)
        {
            outRolledIslands.Clear();
            outRolledIslands.AddRange(strangerRolledIslands);
            outStrangerNames.Clear();
            outStrangerNames.AddRange(strangerNames);
        }

        public static void SyncStrangersFrom(List<string> savedRolledIslands, List<string> savedStrangerNames)
        {
            strangerRolledIslands.Clear();
            if (savedRolledIslands != null)
                foreach (var key in savedRolledIslands)
                    if (!string.IsNullOrEmpty(key)) strangerRolledIslands.Add(key);
            strangerNames.Clear();
            if (savedStrangerNames != null)
                foreach (var n in savedStrangerNames)
                    if (!string.IsNullOrEmpty(n)) strangerNames.Add(n);
        }
    }
}
