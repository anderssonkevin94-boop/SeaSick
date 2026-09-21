using System.Collections.Generic;
using SeaSick.Crew;
using SeaSick.Ship;
using SeaSick.Terrain;
using SeaSick.Voyage;
using SeaSick.World;
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

        public static string Path =>
            System.IO.Path.Combine(Application.persistentDataPath, FileName);

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
        /// quit. Silent no-op until the player has picked New or Continue.
        public static void Autosave(string reason)
        {
            if (!GameBoot.Decided || Suppressed || Restoring) return;
            Save(reason);
        }

        /// The button. Not gated on `Suppressed`: a person pressing SAVE
        /// means it.
        public static bool Save(string reason) => SaveTo(Path, reason);

        public static bool SaveTo(string path, string reason)
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
            int day = TimeOfDay.DayLength > 0f ? (int)(d.timeSeconds / TimeOfDay.DayLength) : 0;
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
            s.anchor = 0;
            if (anchor != null)
            {
                if (anchor.AtHomeDock) s.anchor = 2;
                else if (anchor.CurrentState != AnchorController.State.Underway
                         && anchor.CurrentIsland != null) s.anchor = 1;
            }

            // --- the hold and the stores ----------------------------------
            foreach (var kv in voyage.HeldStores)
                if (kv.Value > 0) d.hold.Add(new StoreEntry { resource = kv.Key, count = kv.Value });
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
                    || (l.raised != null && l.raised.Count > 0) || l.pending != null;
                if (!worth) continue;
                Vector3 c = o.CampCentre;
                d.outposts.Add(new OutpostSave
                {
                    ledger = l,
                    campX = c.x, campY = c.y, campZ = c.z,
                    hasCampCentre = o.HasCampCentre,
                    isHome = o.IsHome,
                });
            }
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
                if (Time.realtimeSinceStartup - t0 > 90f) { Fail("the world never finished building"); yield break; }
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
            TimeOfDay.Scrub(data.timeSeconds);

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

            // 3. The hold and the stores, after the yard told the voyage
            // how big the hold is.
            voyage.RestoreStores(Pairs(data.hold), Pairs(data.banked));

            // 4. Where she is. Let go of the pier first, then put her there.
            if (anchor != null && anchor.CurrentState != AnchorController.State.Underway)
            {
                anchor.CastOff();
                yield return null;
            }
            Vector3 at = new Vector3(data.ship.x, data.ship.y, data.ship.z);
            Warp(motor, at, data.ship.yaw);

            // 5. The outposts.
            int restored = 0;
            foreach (var os in data.outposts)
            {
                if (os == null || os.ledger == null) continue;
                yield return RestoreOutpost(os, pop, motor, host);
                if (os.ledger == LastAdopted) restored++;
            }
            var roster = motor.GetComponent<CrewRoster>();
            if (roster != null) roster.Refresh();

            // 6. The anchor, last, so the camp she lies off is awake to see
            // her arrive. She may have drifted a frame; put her back first.
            if (anchor != null)
            {
                if (data.ship.anchor == 2)
                {
                    if (!anchor.BerthAtHome(out string why))
                        Debug.LogWarning("SaveGame: could not berth her at home: " + why);
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
            Debug.Log("SaveGame: loaded <- " + Path + "   " + Summary(data)
                + "   (" + restored + " of " + data.outposts.Count + " outposts)");
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
            if (os.isHome) o = Outpost.Home;
            else
            {
                var isle = IslandAt(pop, os.ledger.keyX, os.ledger.keyZ);
                if (isle == null)
                {
                    Debug.LogWarning("SaveGame: no island under camp key ("
                        + os.ledger.keyX + "," + os.ledger.keyZ + "); camp dropped");
                    yield break;
                }
                if (isle.IsHome) o = Outpost.Home;
                else
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
