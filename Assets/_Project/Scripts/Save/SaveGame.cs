using System.Collections.Generic;
using SeaSick.Crew;
using SeaSick.Ship;
using SeaSick.Terrain;
using SeaSick.Voyage;
using SeaSick.World;
using SeaSick.World.Life;
using UnityEngine;

namespace SeaSick.Save
{
    /// **The playtest save: one JSON file, written whole, read whole.**
    ///
    /// Kevin, 2026-09-21: *"When playtesting I need to test the progression
    /// of the game. I need to be able to save in game and, when launching
    /// the game again, press New or Load."* This is that and no more -- no
    /// slots, no migration, no cloud. Its whole design is that the ledger
    /// was already savable (D2/D4) and the rest of the state is a short
    /// list: the clock, the ship, the hold, the stores, and where things
    /// stand.
    ///
    /// **Restore order is load-bearing** and lives in `Restore`:
    ///   1. the clock, before any ledger is asked to catch up;
    ///   2. the ship's rung, then her fittings, then her bays;
    ///   3. the hold and the stores;
    ///   4. the ship's pose;
    ///   5. every outpost -- survey, adopt the ledger, re-raise, re-home;
    ///   6. the anchor, last, so a camp she is lying off is awake to see her.
    ///
    /// Writes are gated three ways: never before the player has chosen
    /// New or Continue (`GameBoot.Decided`, so a `Start`-time cast-off
    /// cannot overwrite a save with a fresh world), never while a probe is
    /// driving the session (`Suppressed`), and never while the world is
    /// not built (`Capture` refuses). One console line per write and per
    /// read, with the path.
    public static class SaveGame
    {
        public const string FileName = "seasick-save.json";

        /// **The pre-slots single file** (2026-09-21..2026-09-25). A fixed
        /// path, never written or deleted by anything after slots shipped:
        /// `SaveSlots` reads it exactly once, to migrate it into manual
        /// slot 1, and leaves it on disk afterwards as Kevin's backup.
        /// **Follows `SaveSlots.EditorTestDirectory` in the editor**, same
        /// as every slot path (`SaveSlots.PathFor`) -- so with the redirect
        /// set, `EnsureMigrated` reads and writes entirely inside the
        /// scratch directory and never touches Kevin's real legacy file,
        /// even to look for it. A player build always resolves here.
        public static string LegacyPath
        {
            get
            {
#if UNITY_EDITOR
                string ed = SaveSlots.EditorTestDirectory;
                if (ed != null) return System.IO.Path.Combine(ed, FileName);
#endif
                return System.IO.Path.Combine(Application.persistentDataPath, FileName);
            }
        }

        /// **The slot system's write/read target** (2026-09-26, `SaveSlots`):
        /// whichever slot is "the game being played right now"
        /// (`SaveSlots.ActiveSlotId`), or manual slot 1 before anything has
        /// been loaded or explicitly saved this session. Every caller below
        /// and every other script that reads `SaveGame.Path` (the SAVE
        /// button, the refit-persist call in `ShipyardService`, the probes)
        /// is unchanged -- only what the path resolves to moved, from one
        /// fixed file to a slot.
        public static string Path => SaveSlots.ResolveActivePath();

        public static bool Exists => System.IO.File.Exists(Path);

        /// Autosaves are off for the rest of the session. Set by
        /// `GameBoot.Skip`, which is what every probe launcher calls: a probe
        /// that anchors forty times must not write the player's file forty
        /// times. The SAVE button and `SaveTo` still work.
        public static bool Suppressed { get; set; }

        /// True from the first frame of a load to the last. The HUD says so,
        /// and nothing autosaves in between.
        public static bool Restoring { get; private set; }
        public static bool LastRestoreOk { get; private set; }
        public static string LastRestoreNote { get; private set; } = "";

        static bool saving;

        /// Statics outlive play mode here (domain reload is off).
        public static void ResetForPlay()
        {
            Suppressed = false;
            Restoring = false;
            LastRestoreOk = false;
            LastRestoreNote = "";
            saving = false;
            LastAdopted = null;
        }

        // --- writing ----------------------------------------------------------

        /// The game's own saves: on an anchor, a departure, a building, a
        /// quit, or the 5-minute timer (`SaveAutosaveTimer`). Silent no-op
        /// until the player has picked New or Continue. **Never writes a
        /// manual slot** (Kevin, 2026-09-26) -- it rotates a `SaveSlots`
        /// autosave slot instead, even when a manual slot is the one
        /// currently active, so an autosave can never clobber a save the
        /// player named and chose to keep.
        public static void Autosave(string reason)
        {
            if (!GameBoot.Decided || Suppressed || Restoring) return;
            SaveSlots.WriteAutosave(reason);
        }

        /// The button. Not gated on `Suppressed`: a person pressing SAVE
        /// means it. Writes the active slot (`Path`) -- manual if one is
        /// loaded, else the slot-1 default.
        public static bool Save(string reason) => SaveTo(Path, reason);

        public static bool SaveTo(string path, string reason) => SaveTo(path, reason, null);

        /// As `SaveTo(path, reason)`, plus a hook to touch the captured
        /// `SaveData` before it is serialised -- `SaveSlots` uses it to
        /// stamp `slotDisplayName` without duplicating the capture/write
        /// plumbing here.
        public static bool SaveTo(string path, string reason, System.Action<SaveData> customize)
        {
            if (!Application.isPlaying) return false;
            if (saving) return false;      // a raise inside a capture must not recurse
            saving = true;
            try
            {
                var data = Capture(out string why);
                if (data == null)
                {
                    Debug.LogWarning("SaveGame: not saved (" + reason + "): " + why);
                    return false;
                }
                data.reason = reason;
                customize?.Invoke(data);
                string json = JsonUtility.ToJson(data, true);
                string dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(path, json);
                Debug.Log("SaveGame: saved (" + reason + ") -> " + path
                    + "   " + Summary(data));
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("SaveGame: could not write " + path + ": " + e.Message);
                return false;
            }
            finally { saving = false; }
        }

        public static void Delete()
        {
            try
            {
                if (Exists) { System.IO.File.Delete(Path); Debug.Log("SaveGame: deleted " + Path); }
            }
            catch (System.Exception e) { Debug.LogWarning("SaveGame: could not delete: " + e.Message); }
        }

        /// One line about a save, for the CONTINUE button and the console.
        public static string Summary(SaveData d)
        {
            if (d == null) return "";
            int day = (int)System.Math.Floor(d.CalendarDays);
            int camps = 0;
            foreach (var o in d.outposts) if (o != null && !o.isHome) camps++;
            return "saved " + d.savedAt + "  ·  day " + day + "  ·  rung " + d.ship.rung
                 + "  ·  " + camps + (camps == 1 ? " camp" : " camps");
        }

        /// The world's seed, or 0 when the world is not there to ask.
        public static int WorldSeed()
        {
            var pop = Object.FindFirstObjectByType<TerrainWorldPopulator>();
            return pop != null && pop.world != null ? pop.world.seed : 0;
        }

        /// Everything, from the live scene. Null with a reason when there is
        /// no world to read yet.
        public static SaveData Capture(out string why)
        {
            why = "";
            var pop = Object.FindFirstObjectByType<TerrainWorldPopulator>();
            if (pop == null || !pop.Done) { why = "the world is not built yet"; return null; }
            var voyage = Object.FindFirstObjectByType<VoyageManager>();
            var motor = Object.FindFirstObjectByType<ShipMotor>();
            if (voyage == null || motor == null) { why = "no ship in the scene"; return null; }

            var yard = motor.GetComponent<Shipyard>();
            var anchor = motor.GetComponent<AnchorController>();

            var d = new SaveData();
            d.version = SaveData.CurrentVersion;
            d.worldSeed = pop.world != null ? pop.world.seed : 0;
            d.savedAt = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            d.timeSeconds = TimeOfDay.Seconds;
            d.calendarDays = TimeOfDay.CalendarDays;
            d.savedAtUtcTicks = System.DateTime.UtcNow.Ticks;

            // Death/rescue phase 1: the global life registry rides the save
            // the same way `TimeOfDay`/`OceanTime` would if they needed to.
            Lives.SyncTo(d.lives, d.graveyard);
            // Phase 5a: castaways and the scripted-first-time flag.
            Lives.SyncCastawaysTo(d.castaways);
            // Phase 7: which islands already had their stranger roll, and
            // which castaway names are strangers rather than washed-ashore
            // crew.
            Lives.SyncStrangersTo(d.strangerIslandsRolled, d.strangerNames);
            d.firstOverboardDone = SeaSick.Ship.Overboard.FirstOverboard.Done;

            // --- the ship -------------------------------------------------
            var s = d.ship;
            s.rung = yard != null ? yard.NodeIndex : -1;
            if (yard != null)
            {
                foreach (FitTrack t in System.Enum.GetValues(typeof(FitTrack)))
                    s.fit.Add(yard.Fit.Level(t));
                var n = yard.Node;
                if (n != null && n.bay_labels != null && n.tier_names != null)
                    foreach (var bay in n.bay_labels)
                        foreach (var tier in n.tier_names)
                        {
                            var use = yard.Use(bay, tier);
                            if (use == BayUse.Empty) continue;
                            s.cells.Add(new CellSave { bay = bay, tier = tier, use = (int)use });
                        }
            }
            Vector3 p = motor.transform.position;
            s.x = p.x; s.y = p.y; s.z = p.z;
            s.yaw = motor.transform.eulerAngles.y;
            var shipyard = motor.GetComponent<SeaSick.Ship.Modular.ShipyardService>();
            s.modular = shipyard != null ? shipyard.SaveField() : "";
            s.dryDock = shipyard != null ? shipyard.DryDockField() : "";
            s.anchor = 0;
            if (anchor != null)
            {
                if (anchor.AtHomeDock) s.anchor = 2;
                else if (anchor.CurrentState != AnchorController.State.Underway
                         && anchor.CurrentIsland != null) s.anchor = 1;
            }
            var homeDock = Dock.Home;
            s.hasHomeBerth = homeDock != null;
            if (homeDock != null)
            {
                var hb = homeDock.Berth;
                s.homeBerthX = hb.x; s.homeBerthZ = hb.z;
            }

            // Villagers born at a camp. A born row that is still a born row
            // re-grows its own body on load (`Outpost.EnsureBornBodies`), so
            // only the ones the ledger will NOT re-create are written here:
            // carried aboard, or landed again as an ordinary hand.
            var bornRows = new HashSet<string>();
            foreach (var o in Outpost.All)
            {
                if (o == null || o.Ledger == null || o.Ledger.hands == null) continue;
                foreach (var h in o.Ledger.hands)
                    if (h != null && !string.IsNullOrEmpty(h.name) && h.born)
                        bornRows.Add(h.name);
            }
            foreach (var v in BornVillager.All())
            {
                if (v == null || string.IsNullOrEmpty(v.bornName)) continue;
                if (bornRows.Contains(v.bornName)) continue;
                // A raider is a `BornVillager` because that is how a body gets
                // made at runtime, not because he is one of ours. Without this
                // a raid in progress at the save writes "raider" into the
                // ship's crew list and the load boards him -- Kevin's save has
                // exactly that in it.
                if (v.GetComponent<SeaSick.Combat.RaidWalker>() != null) continue;
                if (!s.crewNames.Contains(v.bornName)) s.crewNames.Add(v.bornName);
            }

            // --- the hold and the stores ----------------------------------
            // Phase 6: a crate riding the sea is not written down as its own
            // save entity -- pull every one still afloat back into the hold
            // it came from FIRST, so a save/quit while cargo is in the water
            // returns those units rather than losing them. Swimmers need no
            // such step: `BornVillager.All()` below already walks inactive
            // bodies, and an authored hand's body is part of the scene
            // itself, so either one is already "aboard" again the moment the
            // save reloads.
            SeaSick.Ship.Overboard.FloatingCargo.RecallAllForSave();
            // Death/rescue phase 10: a store spear still out in a defending
            // hand's arms mid-raid is a real unit, same as floating cargo --
            // put it back in the count before the ledger is written down.
            SeaSick.Combat.RaidAlarm.ReturnAllHeldSpearsForSave();
            foreach (var kv in voyage.HeldStores)
                if (kv.Value > 0) d.hold.Add(new StoreEntry { resource = kv.Key, count = kv.Value });
            // `banked` is RETIRED (2026-10-04): only an old save's bank not
            // yet repaired into a home camp (none to take it) is carried
            // here, so it is never lost. Empty in every game since.
            foreach (var kv in voyage.BankedStores)
                if (kv.Value > 0) d.banked.Add(new StoreEntry { resource = kv.Key, count = kv.Value });

            // --- the outposts ---------------------------------------------
            //
            // Every ledger that has anything on it. A surveyed island nobody
            // touched is left out: the survey is lazy and repeatable, and a
            // save is not the place to cache it.
            foreach (var o in Outpost.All)
            {
                if (o == null || o.Ledger == null) continue;
                var l = o.Ledger;
                bool worth = o.IsHome || o.HasCampCentre
                    || l.hands.Count > 0 || l.built.Count > 0
                    || (l.raised != null && l.raised.Count > 0) || l.SiteCount > 0
                    // A camp-less island a gather party worked (2026-09-27):
                    // its named takes and reduced stock ARE state now.
                    || l.HasGroundTaken;
                if (!worth) continue;
                Vector3 c = o.CampCentre;
                var isle = o.Island;
                d.outposts.Add(new OutpostSave
                {
                    ledger = l,
                    campX = c.x, campY = c.y, campZ = c.z,
                    hasCampCentre = o.HasCampCentre,
                    isHome = o.IsHome,
                    hasIsle = isle != null,
                    isleX = isle != null ? isle.transform.position.x : 0f,
                    isleZ = isle != null ? isle.transform.position.z : 0f,
                });
            }

            // --- the chart -------------------------------------------------
            //
            // What she has seen and where she has been. Both are cheap and
            // both are pure knowledge: a save without them loads into a world
            // the player has already explored with a chart that says they
            // have not.
            d.seen = Discovery.Capture();
            d.takenFinds = IslandFind.Capture();
            UI.Sheets.ChartData.CaptureTrack(d.trackX, d.trackZ, d.trackAt);
            return d;
        }

        // --- reading ----------------------------------------------------------

        /// The file, parsed, or null (with a console line) when it is not
        /// there, not JSON, or not a version this build reads.
        public static SaveData Read(string path)
        {
            try
            {
                if (!System.IO.File.Exists(path)) return null;
                var d = JsonUtility.FromJson<SaveData>(System.IO.File.ReadAllText(path));
                if (d == null) { Debug.LogWarning("SaveGame: " + path + " is not a save"); return null; }
                if (d.version != SaveData.CurrentVersion)
                {
                    Debug.LogWarning("SaveGame: " + path + " is version " + d.version
                        + ", this build reads " + SaveData.CurrentVersion);
                    return null;
                }
                if (d.ship == null) d.ship = new ShipSave();
                if (d.hold == null) d.hold = new List<StoreEntry>();
                if (d.banked == null) d.banked = new List<StoreEntry>();
                if (d.outposts == null) d.outposts = new List<OutpostSave>();
                // Added after version 1 shipped and deliberately NOT a
                // version bump: `JsonUtility` leaves a field its JSON does
                // not mention at the value the constructor gave it, so an
                // older save reads back as an empty chart rather than as a
                // refusal. Bumping would have thrown away Kevin's saves to
                // add a drawing.
                if (d.seen == null) d.seen = new List<SeenSave>();
                if (d.takenFinds == null) d.takenFinds = new List<FindTakenSave>();
                if (d.ship.modular == null) d.ship.modular = "";
                if (d.ship.dryDock == null) d.ship.dryDock = "";
                if (d.slotDisplayName == null) d.slotDisplayName = "";
                if (d.trackX == null) d.trackX = new List<float>();
                if (d.trackZ == null) d.trackZ = new List<float>();
                if (d.trackAt == null) d.trackAt = new List<double>();
                // Death/rescue phase 1: an old save has nobody's story yet.
                if (d.lives == null) d.lives = new List<LifeRecord>();
                if (d.graveyard == null) d.graveyard = new List<GraveRecord>();
                Lives.SyncFrom(d.lives, d.graveyard);
                // Phase 5a: an old save has no castaways and the scripted
                // first time still has to happen.
                if (d.castaways == null) d.castaways = new List<SeaSick.World.Life.CastawayRecord>();
                Lives.SyncCastawaysFrom(d.castaways);
                // Phase 7: an old save has rolled no island yet.
                if (d.strangerIslandsRolled == null) d.strangerIslandsRolled = new List<string>();
                if (d.strangerNames == null) d.strangerNames = new List<string>();
                Lives.SyncStrangersFrom(d.strangerIslandsRolled, d.strangerNames);
                SeaSick.Ship.Overboard.FirstOverboard.Done = d.firstOverboardDone;
                return d;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("SaveGame: could not read " + path + ": " + e.Message);
                return null;
            }
        }

        /// **Put a save back into the live scene.** A coroutine, because a
        /// camp's island has to be surveyed and the survey is spread over
        /// frames. `host` runs it; `LastRestoreOk` says how it went.
        ///
        /// Refuses a save from another world: the island under a camp would
        /// be a different island, and the key would land in the sea.
        public static System.Collections.IEnumerator Restore(SaveData data, MonoBehaviour host)
        {
            Restoring = true;
            LastRestoreOk = false;
            LastRestoreNote = "";
            if (data == null) { Fail("nothing to restore"); yield break; }

            // 0. The world, and the frame in which everything's Start ran.
            TerrainWorldPopulator pop = null;
            float t0 = Time.realtimeSinceStartup;
            while (true)
            {
                pop = Object.FindFirstObjectByType<TerrainWorldPopulator>();
                if (pop != null && pop.Done) break;
                // No clock while it is building: the build is spread over
                // frames now, and a phone locked mid-load sits suspended for
                // as long as it likes. Timing out there would fall through
                // to a NEW voyage whose first autosave eats the player's file.
                if (pop != null && pop.Failed) { Fail("the world failed to build"); yield break; }
                if (pop == null && Time.realtimeSinceStartup - t0 > 90f) { Fail("the world never finished building"); yield break; }
                yield return null;
            }
            yield return null;

            var voyage = Object.FindFirstObjectByType<VoyageManager>();
            var motor = Object.FindFirstObjectByType<ShipMotor>();
            if (voyage == null || motor == null) { Fail("no ship in the scene"); yield break; }
            var yard = motor.GetComponent<Shipyard>();
            var anchor = motor.GetComponent<AnchorController>();

            // The spawn-time berthing writes the ship's pose on its own
            // first frame; wait it out rather than race it.
            t0 = Time.realtimeSinceStartup;
            while (anchor != null && !anchor.StartedDocked
                   && Time.realtimeSinceStartup - t0 < 10f) yield return null;

            int seed = pop.world != null ? pop.world.seed : 0;
            if (data.worldSeed != seed)
            {
                Fail("the save is from world seed " + data.worldSeed + ", this world is "
                    + seed + " -- playing a new voyage instead");
                yield break;
            }

            // 1. The clock. FIRST: every ledger below catches up to it.
            // The calendar with it (2026-09-29): the day and hour the save
            // was at survive a day-length change; the clock is not rescaled.
            TimeOfDay.SetClock(data.timeSeconds, data.CalendarDays);

            // 1a. The chart, straight after the clock and before the camps.
            // `Discovery.Apply` is monotonic, so the outposts raised in step
            // 5 marking their own islands as landed cannot undo it -- and the
            // track is aged against the clock that was just scrubbed, so it
            // has to come after the scrub and not before it.
            Discovery.Apply(data.seen);
            IslandFind.Apply(data.takenFinds);   // the world is built by now; remove finds already taken
            // (The fog of war's `IslandFog.Apply(data.fog)` stood here until
            // 2026-10-03; an old save's `fog` rows are now skipped by JsonUtility.)
            UI.Sheets.ChartData.RestoreTrack(data.trackX, data.trackZ, data.trackAt);

            // 2. The ship. `Apply` is the free path every probe uses; the
            // fittings are clamped to the rung the way the yard would.
            if (yard != null && !Shipyard.SuppressApplyOnStart && data.ship.rung >= 0)
            {
                yard.Apply(data.ship.rung);
                var tracks = (FitTrack[])System.Enum.GetValues(typeof(FitTrack));
                for (int i = 0; i < tracks.Length && i < data.ship.fit.Count; i++)
                    yard.Fit.SetLevel(tracks[i], data.ship.fit[i]);
                if (yard.Node != null) yard.Fit.ClampTo(yard.Node);
                foreach (var c in data.ship.cells)
                    if (c != null) yard.SetUseQuiet(c.bay, c.tier, (BayUse)c.use);
                yard.Refurnish();
            }

            // 2a. Her modular configuration, if the save has one: the hull,
            // deck, hold size and stations are rebuilt HERE, before the hold
            // and the hands come back, so they land on the right deck. An
            // old save (no field) is the standard steamer; an unbuildable
            // field falls back to it with a warning, never a refusal.
            var shipyard = motor.GetComponent<SeaSick.Ship.Modular.ShipyardService>();
            if (shipyard != null)
            {
                shipyard.ApplyFromSave(data.ship.modular);
                shipyard.ApplyDryDockFromSave(data.ship.dryDock);
            }

            // 3. The hold, after the yard told the voyage how big the hold
            // is -- and an old save's retired home bank, held until 5c2.
            voyage.RestoreStores(Pairs(data.hold), Pairs(data.banked));

            // 4. Where she is. Let go of the pier first, then put her there.
            if (anchor != null && anchor.CurrentState != AnchorController.State.Underway)
            {
                anchor.CastOff();
                yield return null;
            }
            Vector3 at = new Vector3(data.ship.x, data.ship.y, data.ship.z);
            Warp(motor, at, data.ship.yaw);

            // 4a. The hands she is carrying who were born at a camp. BEFORE
            // the outposts, because `RestoreOutpost` looks aboard by name for
            // a body to walk ashore -- a villager landed again as an ordinary
            // hand has a row in some ledger and nobody to wear it until he is
            // standing on the deck to be found.
            if (data.ship.crewNames != null)
                foreach (var who in data.ship.crewNames)
                {
                    if (string.IsNullOrEmpty(who)) continue;
                    // Written by a build from before raiders were kept out of
                    // this list (see the writer). Not crew; never was.
                    if (who == "raider") continue;
                    // Phase 5a: a dead or castaway name does not regenerate.
                    // He either has a tombstone or is standing on an island
                    // waiting to be fetched -- either way, not aboard.
                    if (Lives.IsTaken(who)) continue;
                    if (BornVillager.Board(who, motor.transform) == null)
                        Debug.LogWarning("SaveGame: could not re-make " + who
                            + ", who was aboard");
                }

            // 5. The outposts.
            int restored = 0;
            foreach (var os in data.outposts)
            {
                if (os == null || os.ledger == null) continue;
                yield return RestoreOutpost(os, pop, motor, host);
                if (os.ledger == LastAdopted) restored++;
            }
            // 5a. **Nobody answers to somebody else's name.** Two naming
            // paths meet on a load -- the yard clones her berths full in step
            // 2, the save boards her camp-born hands in 4a, and a save
            // written by a build that named every clone after the man it
            // copied (fixed 2026-09-22) arrives with three of them. The
            // authored cast and anybody a ledger row is holding keep their
            // names; the rest are re-drawn from the pool.
            CrewNames.Deduplicate();

            var roster = motor.GetComponent<CrewRoster>();
            if (roster != null) roster.Refresh();

            // 5b. Her home berth (2026-09-25, switchable): every outpost's
            // piers are rebuilt now (step 5, above), so the dock she chose
            // -- harbour or a player's pier -- can be found and set BEFORE
            // the anchor step reads `Dock.Home`. `hasHomeBerth` false (an
            // old save, or one that never moved it) changes nothing: `Home`
            // is already the harbour. Not found within a couple of metres
            // (her pier was demolished) also changes nothing -- she falls
            // back to the harbour, which is exactly the rule Kevin asked
            // for.
            if (data.ship.hasHomeBerth)
            {
                var wanted = new Vector3(data.ship.homeBerthX, 0f, data.ship.homeBerthZ);
                // Matched against the pier, not just the berth point: a save
                // from before the T-berth stored the old alongside berth,
                // beside the pier -- the harbour's sat ~11 m off its pier
                // line (measured 2026-09-26), a player pier's ~4 m.
                Dock found = null;
                float best = 15f;
                foreach (var d in Dock.All)
                {
                    if (d == null) continue;
                    float dd = d.DistanceFromPier(wanted);
                    if (dd < best) { best = dd; found = d; }
                }
                if (found != null)
                    Dock.SetHome(found);
                else
                    Debug.LogWarning("SaveGame: her home berth pier is gone; staying at "
                        + Dock.HomeLabel);
            }
            // 5c. A dry dock an older build's load dropped (it asked for the
            // home berth before 5b set it) stands again beside the berth
            // now that it is the right one. See Outpost.DryDockRestore.cs.
            Outpost.ResiteOrphanDryDocks();

            // 5c2. **The one-time home-bank repair** (2026-10-04): an old
            // save's `banked` -- the hold home docking used to bank into a
            // number the home camp never read (Kevin's "vanished" ore) --
            // goes into the home camp's store now. HERE because the home
            // camp's ledger was adopted in 5 and `Outpost.Home` follows
            // `Dock.Home`, set in 5b; before time away (5d) so the camp has
            // its goods while it plays and 5d's own save writes the repaired
            // books. Idempotent; with no home camp the bank waits.
            voyage.RepairLegacyBank();

            // 5d. **Time away** (2026-09-27): the real time since this save
            // was written, capped at 12 h, played through every camp's own
            // ledger -- before the anchor step, so no camp is watched yet
            // and every hand is an invisible walker. Raids stay frozen.
            // Saves itself when done. See AwayProgress.
            yield return AwayProgress.FromSave(data);

            // 6. The anchor, last, so the camp she lies off is awake to see
            // her arrive. She may have drifted a frame; put her back first.
            if (anchor != null)
            {
                if (data.ship.anchor == 2)
                {
                    if (!anchor.BerthAtHome(out string why))
                        Debug.LogWarning("SaveGame: could not berth her at home: " + why);
                }
                else if (data.ship.anchor == 1 && CampPierAt(at) is Dock pierHere)
                {
                    // **She was lying at a camp's pier** (2026-10-01). The
                    // save only says "stopped at an island", and anchoring
                    // her there as off a beach lost the pier: no lock, no
                    // catwalk, the beach plank run through her hull at the
                    // pier (Kevin's screenshot). Tie her up again instead,
                    // bow the way she was saved.
                    var bow = Quaternion.Euler(0f, data.ship.yaw, 0f) * Vector3.forward;
                    if (!anchor.BerthAt(pierHere, bow, out string why))
                        Debug.LogWarning("SaveGame: could not tie her up at the pier: " + why);
                }
                else if (data.ship.anchor == 1)
                {
                    Warp(motor, at, data.ship.yaw);
                    var isle = Island.Nearest(at);
                    if (isle == null || !anchor.MoorAt(isle))
                        Debug.LogWarning("SaveGame: could not anchor her off "
                            + (isle != null ? isle.name : "no island"));
                }
            }

            LastRestoreOk = true;
            LastRestoreNote = "loaded " + Summary(data);
            Restoring = false;
            AwayProgress.FlushSave();   // time away played: save it now
            Debug.Log("SaveGame: loaded <- " + Path + "   " + Summary(data)
                + "   (" + restored + " of " + data.outposts.Count + " outposts)");
        }

        /// The non-home pier she lies at if a save put her within a berth's
        /// reach of one (her centre within 12 m of the pier or its berth).
        static Dock CampPierAt(Vector3 at)
        {
            var d = Dock.Nearest(at);
            if (d == null || d.IsHome) return null;
            return d.DistanceFromPier(at) <= 12f ? d : null;
        }

        static void Fail(string why)
        {
            LastRestoreOk = false;
            LastRestoreNote = why;
            Restoring = false;
            Debug.LogWarning("SaveGame: not loaded -- " + why);
        }

        static OutpostLedger LastAdopted;

        static System.Collections.IEnumerator RestoreOutpost(OutpostSave os,
            TerrainWorldPopulator pop, ShipMotor motor, MonoBehaviour host)
        {
            Outpost o = null;
            // **No home shortcut** (2026-09-29). Home used to be a camp the
            // world build stood up, so `Outpost.Home` existed before any camp
            // was restored. Home is now a player's camp named by its pier,
            // `Outpost.Home` follows `Dock.Home`, and that is only restored in
            // 5b, after this -- so every camp, home's included, comes back by
            // its own island. `os.isHome` is kept in the file and not read.
            {
                // The island she camped on, by its own centre; the land mask
                // under the camp key only when a save predates that field.
                var isle = os.hasIsle ? IslandCentred(os.isleX, os.isleZ) : null;
                if (isle == null) isle = IslandAt(pop, os.ledger.keyX, os.ledger.keyZ);
                if (isle == null)
                {
                    Debug.LogWarning("SaveGame: no island under camp key ("
                        + os.ledger.keyX + "," + os.ledger.keyZ + "); camp dropped");
                    yield break;
                }
                o = Outpost.Of(isle);
                if (o == null)
                {
                    Outpost.BeginSurvey(isle, host);
                    float t0 = Time.realtimeSinceStartup;
                    while (Outpost.Surveying(isle) && Time.realtimeSinceStartup - t0 < 60f)
                        yield return null;
                    o = Outpost.Of(isle);
                    if (o == null)
                    {
                        Debug.LogWarning("SaveGame: " + isle.name
                            + " will not take a camp any more; camp at ("
                            + os.ledger.keyX + "," + os.ledger.keyZ + ") dropped");
                        yield break;
                    }
                }
            }
            if (o == null)
            {
                Debug.LogWarning("SaveGame: home has no outpost to adopt into");
                yield break;
            }

            var centre = new Vector3(os.campX, os.campY, os.campZ);
            if (!o.Adopt(os.ledger, centre, os.hasCampCentre))
            {
                Debug.LogWarning("SaveGame: " + o.name + " refused the ledger");
                yield break;
            }
            LastAdopted = os.ledger;

            // The bodies. Rows restore for free; a body is found aboard by
            // name and walked over without the side effects of `Station`.
            var aboard = motor.GetComponentsInChildren<CrewAgent>(true);
            var used = new HashSet<CrewAgent>();
            foreach (var row in o.Ledger.hands)
            {
                if (row == null || string.IsNullOrEmpty(row.name)) continue;
                bool found = false;
                foreach (var a in aboard)
                {
                    if (a == null || used.Contains(a) || a.DisplayName != row.name) continue;
                    if (o.Rehome(a)) { used.Add(a); found = true; }
                    break;
                }
                if (!found)
                    Debug.LogWarning("SaveGame: no body aboard for " + row.name
                        + " at " + o.name + "; the row still produces");
            }
        }

        /// The island whose land mask holds this point, or the nearest one
        /// when the point fell in a cell the scan called water (the mask is
        /// 24 m cells; a camp key is a metre).
        /// The island whose centre is (near enough) here. Centres are hundreds
        /// of metres apart, so 25 m is "this one" with room for float noise
        /// and a hand-edited save; anything further is a different world.
        public static Island IslandCentred(float x, float z)
        {
            Island best = null; float bestD = 25f;
            foreach (var isle in Island.All)
            {
                if (isle == null) continue;
                var p = isle.transform.position;
                float d = Mathf.Sqrt((p.x - x) * (p.x - x) + (p.z - z) * (p.z - z));
                if (d < bestD) { bestD = d; best = isle; }
            }
            return best;
        }

        public static Island IslandAt(TerrainWorldPopulator pop, float x, float z)
        {
            if (pop != null && pop.LandMask != null && pop.MaskCell > 0f)
            {
                int n = pop.MaskSize;
                int ci = Mathf.FloorToInt((x - pop.MaskOrigin.x) / pop.MaskCell);
                int cj = Mathf.FloorToInt((z - pop.MaskOrigin.y) / pop.MaskCell);
                for (int ring = 0; ring <= 1; ring++)
                    for (int dj = -ring; dj <= ring; dj++)
                        for (int di = -ring; di <= ring; di++)
                        {
                            if (ring == 1 && Mathf.Abs(di) != 1 && Mathf.Abs(dj) != 1) continue;
                            int i = ci + di, j = cj + dj;
                            if (i < 0 || j < 0 || i >= n || j >= n) continue;
                            var isle = pop.IslandForMask(pop.LandMask[j * n + i]);
                            if (isle != null) return isle;
                        }
            }
            return Island.Nearest(new Vector3(x, 0f, z));
        }

        static IEnumerable<KeyValuePair<string, int>> Pairs(List<StoreEntry> list)
        {
            if (list == null) yield break;
            foreach (var e in list)
                if (e != null) yield return new KeyValuePair<string, int>(e.resource, e.count);
        }

        /// Put her somewhere, the way `BerthAtHome` and the probes do: the
        /// transform AND the rigidbody, still, on the water's own height.
        public static void Warp(ShipMotor motor, Vector3 to, float yaw)
        {
            if (motor == null) return;
            if (Ocean.OceanSampler.Ready)
                to.y = Ocean.OceanSampler.SampleImmediate(to).height;
            var facing = Quaternion.Euler(0f, yaw, 0f);
            motor.transform.SetPositionAndRotation(to, facing);
            var rb = motor.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.position = to;
                rb.rotation = facing;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            motor.AnchorPoint = to;
        }
    }
}
