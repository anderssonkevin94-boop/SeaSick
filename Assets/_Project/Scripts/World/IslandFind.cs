using System.Collections.Generic;
using SeaSick.Ship;
using SeaSick.Ship.Overboard;
using SeaSick.UI.Sheets;
using SeaSick.Voyage;
using UnityEngine;

namespace SeaSick.World
{
    /// **The one thing a shelter-only island is worth sailing to.** Those
    /// islands (mean shore radius under `WorldSettings.shelterOnlyBelowRadius`)
    /// carry no resources and no raiders, so on the chart they were pure
    /// scenery. Each now gets exactly one find on a beach, of two kinds:
    ///
    ///  - **Cache**: a washed-up crate pile holding a few units of the
    ///    resource its ring of the world unlocks.
    ///  - **Cairn**: a lookout cairn that puts the two nearest unseen
    ///    islands on the chart (`Discovery.NoteGlimpse`).
    ///
    /// **Sailing past collects it** — inside `collectRadius` of the ship, no
    /// tap and no landing, because this is played with one thumb on a phone.
    /// Goods go into the SHIP'S HOLD exactly as `FlotsamCrate.Recover` does
    /// (`VoyageManager.ReturnCargo` + `ShipHold.AddVisual`), never straight
    /// into home stock: the house rule is that goods count at home only when
    /// somebody physically delivers them.
    ///
    /// **Stable across loads.** Kind is a hash of the island centre rounded
    /// to metres, not an index or `Random` (island names are raster-scan
    /// order, and the populator's seeded `Random` sequence must not be
    /// disturbed). Taken finds are saved as their (x, z) and matched to a
    /// find within `TakenTolerance` -- see `SaveData.takenFinds`.
    ///
    /// The art is stand-in primitives, chunky and tall enough (about 3.5 m
    /// with a bright pennant) to be spotted from 40-80 m at sea.
    public class IslandFind : MonoBehaviour
    {
        public enum FindKind { Cache, Cairn }

        static readonly List<IslandFind> all = new List<IslandFind>();
        /// Every live find, for dev probes. Cleared by `ResetForPlay` at the
        /// start of a world build (statics outlive play mode here).
        public static IReadOnlyList<IslandFind> All => all;

        /// Finds already collected, as (x, z). Additive: `Apply` merges a
        /// save's rows in, `MarkTaken` appends, `ResetForPlay` empties.
        static readonly List<Vector2> takenAt = new List<Vector2>();

        /// A find within this many metres of a saved row is that find. The
        /// position is deterministic from the world seed, so this is only
        /// float noise and hand-edited-save slack.
        public const float TakenTolerance = 25f;

        const float PollSeconds = 0.3f;
        static readonly string[] Compass =
            { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };

        static ShipMotor playerShip;

        public FindKind Kind { get; private set; }
        public string Resource { get; private set; }
        public int Units { get; private set; }
        public Island Owner { get; private set; }
        public Vector3 Position => transform.position;

        float collectRadius = 35f;
        float nextPoll;
        bool collected;

        // --- world build -----------------------------------------------------

        /// Forget the last world's finds. Called at the top of the populator's
        /// `Build`, beside `IslandScenery.Report.Clear()`.
        public static void ResetForPlay()
        {
            all.Clear();
            takenAt.Clear();
            playerShip = null;
        }

        /// A stable 32-bit hash of an island centre, rounded to whole metres.
        public static uint HashCentre(Vector3 centre)
        {
            unchecked
            {
                int x = Mathf.RoundToInt(centre.x), z = Mathf.RoundToInt(centre.z);
                uint h = (uint)(x * 73856093) ^ (uint)(z * 19349663);
                h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
                return h;
            }
        }

        /// Place the island's one find at `pos` (a beach point, already on the
        /// ground). `resource` is what the ring of the world says a cache
        /// holds; `ring` is 0..1 out from home. Returns null when this find
        /// was already taken in the save being restored.
        public static IslandFind Spawn(Transform parent, Island owner, Vector3 pos, float ring,
                                       string resource, float cacheShare, float radius)
        {
            if (IsTaken(pos)) return null;
            uint h = HashCentre(owner.transform.position);
            bool cache = (h % 100u) < Mathf.RoundToInt(Mathf.Clamp01(cacheShare) * 100f);

            var go = new GameObject(cache ? "Find_Cache" : "Find_Cairn");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, h % 360u, 0f);
            var f = go.AddComponent<IslandFind>();
            f.Kind = cache ? FindKind.Cache : FindKind.Cairn;
            f.Resource = resource;
            f.Units = 3 + Mathf.RoundToInt(Mathf.Clamp01(ring) * 5f);
            f.Owner = owner;
            f.collectRadius = radius;
            f.nextPoll = Time.time + (h % 100u) * 0.003f;   // stagger the polls
            if (cache) BuildCache(go.transform); else BuildCairn(go.transform);
            all.Add(f);
            return f;
        }

        void OnDestroy() => all.Remove(this);

        // --- collecting ------------------------------------------------------

        void Update()
        {
            // A float compare per frame; the real work runs a few times a second.
            if (collected || Time.time < nextPoll) return;
            nextPoll = Time.time + PollSeconds;

            if (playerShip == null)
            {
                playerShip = FindFirstObjectByType<ShipMotor>();
                if (playerShip == null) return;
            }
            Vector3 d = playerShip.transform.position - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude <= collectRadius * collectRadius) Collect(playerShip.transform);
        }

        void Collect(Transform ship)
        {
            collected = true;
            if (Kind == FindKind.Cairn && !RevealLand())
                // Nothing left unseen to point at: a small cache at its foot instead.
                GiveCargo(ship, Resource, 2, "Nothing new to see from the cairn, but you find 2 "
                          + Resource.ToLowerInvariant() + " at its foot");
            else if (Kind == FindKind.Cache)
                GiveCargo(ship, Resource, Units, "Recovered " + Units + " " + Resource.ToLowerInvariant()
                          + " from a washed-up cache");
            MarkTaken(transform.position);
            Destroy(gameObject);
        }

        /// The same path as `FlotsamCrate.Recover`: into the hold, with the
        /// deck-load visuals, and a banner.
        static void GiveCargo(Transform ship, string resource, int units, string message)
        {
            var voyage = ship.GetComponentInParent<VoyageManager>();
            if (voyage == null) voyage = FindFirstObjectByType<VoyageManager>();
            if (voyage != null)
            {
                voyage.ReturnCargo(units, resource);
                var hold = ship.GetComponent<ShipHold>();
                if (hold != null)
                    for (int i = 0; i < units; i++) hold.AddVisual(resource);
            }
            Banner.Show(message);
        }

        /// Glimpse the two nearest islands the chart has not got yet. False
        /// when there are none, so the caller can fall back to a cache reward.
        bool RevealLand()
        {
            Island a = null, b = null;
            float da = float.MaxValue, db = float.MaxValue;
            foreach (var isle in Island.All)
            {
                if (isle == null || isle == Owner || Discovery.Of(isle) != Seen.Never) continue;
                float d = Island.FlatDistance(isle.transform.position, transform.position);
                if (d < da) { b = a; db = da; a = isle; da = d; }
                else if (d < db) { b = isle; db = d; }
            }
            if (a == null) return false;

            string dirA = CompassToward(a.transform.position);
            string text = "From the cairn you spot land to the " + dirA;
            Discovery.NoteGlimpse(a);
            if (b != null)
            {
                string dirB = CompassToward(b.transform.position);
                if (dirB != dirA) text += " and the " + dirB;
                Discovery.NoteGlimpse(b);
            }
            Banner.Show(text, 5f);
            return true;
        }

        /// Eight-point compass word for the bearing from this find to `to`.
        /// North is +z, east +x, as everywhere else in the project.
        string CompassToward(Vector3 to)
        {
            Vector3 d = to - transform.position;
            float deg = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            int i = Mathf.RoundToInt((((deg % 360f) + 360f) % 360f) / 45f) % 8;
            return Compass[i];
        }

        // --- the save --------------------------------------------------------

        public static bool IsTaken(Vector3 pos)
        {
            float tol2 = TakenTolerance * TakenTolerance;
            foreach (var t in takenAt)
            {
                float dx = t.x - pos.x, dz = t.y - pos.z;
                if (dx * dx + dz * dz <= tol2) return true;
            }
            return false;
        }

        static void MarkTaken(Vector3 pos)
        {
            if (!IsTaken(pos)) takenAt.Add(new Vector2(pos.x, pos.z));
        }

        public static List<FindTakenSave> Capture()
        {
            var rows = new List<FindTakenSave>(takenAt.Count);
            foreach (var t in takenAt) rows.Add(new FindTakenSave { x = t.x, z = t.y });
            return rows;
        }

        /// Put a save's rows back. The world is built BEFORE a save is applied,
        /// so a taken find already exists by now: remove it as well as
        /// remembering it. Old saves have no list; null is fine.
        public static void Apply(List<FindTakenSave> rows)
        {
            if (rows == null) return;
            foreach (var r in rows)
            {
                if (r == null) continue;
                var p = new Vector3(r.x, 0f, r.z);
                MarkTaken(p);
                for (int i = all.Count - 1; i >= 0; i--)
                {
                    var f = all[i];
                    if (f == null) continue;
                    Vector3 d = f.transform.position - p; d.y = 0f;
                    if (d.sqrMagnitude <= TakenTolerance * TakenTolerance)
                    {
                        f.collected = true;
                        Destroy(f.gameObject);
                    }
                }
            }
        }

        // --- stand-in art ----------------------------------------------------

        static GameObject Part(Transform parent, PrimitiveType shape, string mat, Color colour,
                               Vector3 localPos, Vector3 scale, float yaw = 0f)
        {
            var go = GameObject.CreatePrimitive(shape);
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.GetComponent<MeshRenderer>().sharedMaterial = SeaSick.Terrain.IslandPropFactory.Mat(mat, colour);
            return go;
        }

        /// A bright pennant on a pole, so the find can be picked out from the sea.
        static void Flag(Transform parent, float height, string key, Color colour)
        {
            Part(parent, PrimitiveType.Cube, "find_pole", new Color(0.36f, 0.26f, 0.17f),
                 new Vector3(0f, height * 0.5f, 0f), new Vector3(0.12f, height, 0.12f));
            Part(parent, PrimitiveType.Cube, key, colour,
                 new Vector3(0.55f, height - 0.35f, 0f), new Vector3(1.1f, 0.6f, 0.06f));
        }

        static void BuildCache(Transform root)
        {
            var wood = new Color(0.62f, 0.44f, 0.24f);
            var dark = new Color(0.42f, 0.3f, 0.2f);
            Part(root, PrimitiveType.Cube, "find_crate", wood, new Vector3(0f, 0.55f, 0f), new Vector3(1.6f, 1.1f, 1.6f), 8f);
            Part(root, PrimitiveType.Cube, "find_crate", wood, new Vector3(1.5f, 0.45f, 0.4f), new Vector3(1.3f, 0.9f, 1.3f), -20f);
            Part(root, PrimitiveType.Cube, "find_crate", wood, new Vector3(0.3f, 1.55f, 0.1f), new Vector3(1.2f, 0.9f, 1.2f), 30f);
            Part(root, PrimitiveType.Cube, "find_plank", dark, new Vector3(-1.4f, 0.1f, -0.9f), new Vector3(2.6f, 0.15f, 0.4f), 55f);
            Flag(root, 3.6f, "find_flag_cache", new Color(0.98f, 0.62f, 0.12f));
        }

        static void BuildCairn(Transform root)
        {
            var stone = new Color(0.62f, 0.63f, 0.66f);
            var stoneDark = new Color(0.5f, 0.51f, 0.54f);
            Part(root, PrimitiveType.Cube, "find_stone_dark", stoneDark, new Vector3(0f, 0.45f, 0f), new Vector3(2.4f, 0.9f, 2.2f), 12f);
            Part(root, PrimitiveType.Cube, "find_stone", stone, new Vector3(0.05f, 1.3f, 0f), new Vector3(1.7f, 0.85f, 1.6f), -18f);
            Part(root, PrimitiveType.Cube, "find_stone_dark", stoneDark, new Vector3(0f, 2.05f, 0.05f), new Vector3(1.1f, 0.7f, 1.0f), 25f);
            Part(root, PrimitiveType.Cube, "find_stone", stone, new Vector3(0f, 2.65f, 0f), new Vector3(0.6f, 0.5f, 0.6f), -8f);
            Flag(root, 4.2f, "find_flag_cairn", new Color(0.3f, 0.85f, 0.95f));
        }
    }

    /// One taken find, as `JsonUtility` can write it (public fields, no
    /// Vector2). Keyed by the find's own position, not its island.
    [System.Serializable]
    public class FindTakenSave
    {
        public float x, z;
    }
}
