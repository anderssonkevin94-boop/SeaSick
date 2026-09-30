using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.Ship.SeaLife
{
    /// **Astra's sea discovery kit v1, Kevin approved 2026-09-30**
    /// (`art-staging/sea-discovery-v1`): the message bottle, the salvage
    /// cluster, the board pieces and the three reef clusters. The FBXs live in
    /// `Resources/Kits/Sea/`; `SeaKitImport` (editor) pins their import, remaps
    /// the opaque materials onto the shared `GameColor` and gives the bottle
    /// its own transparent glass.
    ///
    /// Each model is loaded ONCE and cached in a static; a spawn is one
    /// `Instantiate` of the cached FBX root (materials come with it, no
    /// per-instance material). A model that fails to load caches as null and
    /// every caller falls back to the primitive it always used, so a missing
    /// file costs looks, never the sea.
    public static class SeaKit
    {
        public const string MessageBottle = "MessageBottle";
        public const string SalvageCluster = "SalvageCluster";
        public const string LashedBoardBundle = "LashedBoardBundle";
        public const string BrokenBoardShort = "BrokenBoardShort";
        public const string BrokenBoardLong = "BrokenBoardLong";

        /// The three reef clusters. Each is a unit horizontal radius with its
        /// origin at the mean waterline and a submerged skirt below it.
        static readonly string[] Reefs = { "ReefSplitPeak", "ReefLowLedge", "ReefLeaningTeeth" };

        static readonly Dictionary<string, GameObject> cache = new Dictionary<string, GameObject>();

        /// The cached FBX root, or null when the file is missing (cached as
        /// null too, so a missing file is looked up and warned about once).
        public static GameObject Model(string name)
        {
            if (cache.TryGetValue(name, out var go)) return go;
            go = Resources.Load<GameObject>("Kits/Sea/" + name);
            if (go == null)
                Debug.LogWarning("SeaKit: Resources/Kits/Sea/" + name + " did not load; falling back to the placeholder.");
            cache[name] = go;
            return go;
        }

        /// A fresh copy of the model parented under `parent` at `localPos`, or
        /// null when the model is not there. Colliders are stripped (the kit
        /// ships none; a hazard is `Reef.Radius`, a pickup is a distance test).
        public static GameObject Spawn(string name, Transform parent, Vector3 localPos)
        {
            var src = Model(name);
            if (src == null) return null;
            var go = Object.Instantiate(src, parent, false);
            go.name = name;
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
            return go;
        }

        /// Which of the three reef meshes reef number `index` of a world gets.
        /// A pure function of the world seed and the reef's index -- it draws
        /// nothing from `UnityEngine.Random`, so the seeded stream the world is
        /// built from (and every spawn after it) is untouched.
        public static string ReefName(int worldSeed, int index)
            => Reefs[(int)(Hash(worldSeed, index, 1u) % (uint)Reefs.Length)];

        /// A stable yaw, 0..360, for the same reef (same rule: no Random).
        public static float ReefYaw(int worldSeed, int index)
            => Hash(worldSeed, index, 2u) % 360u;

        static uint Hash(int seed, int index, uint salt)
        {
            unchecked
            {
                uint h = (uint)seed * 2654435761u ^ (uint)(index + 1) * 2246822519u ^ salt * 3266489917u;
                h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
                return h;
            }
        }
    }
}
