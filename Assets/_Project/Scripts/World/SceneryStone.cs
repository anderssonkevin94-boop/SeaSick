using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **The island's loose rocks become the camp's stone (2026-09-27).**
    /// Kevin, phone: *"there are so many rocks on the island but I guess they
    /// don't qualify as a stone resource, please change so they do."*
    ///
    /// Stands a Stone `ResourceNode` (no renderer, no collider -- a transform,
    /// a claim and a `StoneDeposit` in scenery mode) on each loose rock the
    /// bake indexed (`Terrain.SceneryRocks`) that the camp can actually get
    /// to, so every system that already knows stone rocks sees them with no
    /// new path: the seam sizing (`StoneDeposits` -> `SizeStoneToDeposits`),
    /// the picture (`GatherSync`, nearest the camp first, the gathered set a
    /// prefix of that order and therefore a pure function of the SAVED
    /// ledger -- which is the whole persistence story, the same one kit
    /// deposits and felled trees use), the bodies' trips (`CampWorker
    /// .NearestNode`, reachable only), a plot's clearing (`Outpost.OwnRocks`
    /// -> `HeldBySite`, 2 stone a rock) and the path grid's rock obstacles
    /// (`CampPath.MarkRocks`, D4).
    ///
    /// **Which rocks.** Inside the camp's path grid, above the beach
    /// (`Outpost.MinGroundHeight`), and `CampPath.Reachable` from the fire
    /// -- a rock across a ravine or on a sea stack is scenery. At most
    /// `MaxNodes`, nearest the camp first: a cap on GameObjects on the phone,
    /// far above what a camp's hands will ever work.
    ///
    /// **When.** Once per bake of the island (`SceneryRocks.Materialized`),
    /// after the scenery is built and the camp's grid exists. The nodes are
    /// children of the scenery, so an island streamed out takes them with
    /// it and a reload stands the same ones again (same bake, same camp).
    /// The grid is invalidated after, so its next build marks them.
    public static class SceneryStone
    {
        /// Most nodes one camp stands on scenery rock.
        public const int MaxNodes = 240;

        /// Seconds a camp waits for its island's scenery before the seam is
        /// sized without it (an island with nothing baked on it at all).
        const float WaitForBake = 30f;

        static readonly Dictionary<Outpost, float> firstAsk = new Dictionary<Outpost, float>();
        static readonly Dictionary<Outpost, Terrain.SceneryRocks> cache = new Dictionary<Outpost, Terrain.SceneryRocks>();
        static readonly Dictionary<Outpost, float> nextLook = new Dictionary<Outpost, float>();
        static readonly List<(float d2, int i)> picks = new List<(float, int)>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay() { firstAsk.Clear(); cache.Clear(); nextLook.Clear(); }

        /// Last run's numbers, for `SceneryStoneCheck`.
        public static int LastIndexed, LastMade, LastOffGrid, LastLow, LastUnreachable, LastOverCap;

        /// The island's indexed rocks, cached (re-looked-for every 2 s while
        /// missing, so a streaming island is not walked every CatchUp).
        public static Terrain.SceneryRocks RocksOf(Outpost o)
        {
            if (o == null) return null;
            if (cache.TryGetValue(o, out var r) && r != null) return r;
            float now = Time.realtimeSinceStartup;
            if (nextLook.TryGetValue(o, out float t) && now < t) return null;
            nextLook[o] = now + 2f;
            r = Terrain.SceneryRocks.On(o);
            if (r != null) cache[o] = r;
            return r;
        }

        /// Stand this camp's nodes on its island's loose rocks, once per bake.
        /// True when the island's rocks are settled -- stood, or there are
        /// none to stand -- so the seam may be sized; false while the bake or
        /// the grid is not there yet.
        public static bool Ensure(Outpost o)
        {
            if (o == null || o.Island == null) return false;
            float now = Time.realtimeSinceStartup;
            if (!firstAsk.TryGetValue(o, out float since)) firstAsk[o] = since = now;

            var rocks = RocksOf(o);
            if (rocks == null)
            {
                // The home island's authored dressing and the cone fallback
                // index no rocks: their wood is the sign the bake is done.
                if (o.Island.IsHome || o.GetComponentInChildren<Terrain.SceneryWood>() != null) return true;
                return now - since > WaitForBake;
            }
            if (rocks.Materialized) return true;
            if (!o.Sited || !o.HasGround) return false;

            var map = CampPath.For(o);
            if (map == null) return false;
            map.Reachable(o.CampCentre);            // builds the grid if it is not up
            if (!map.Built) return false;

            Stand(o, rocks, map);
            rocks.Materialized = true;
            if (LastMade > 0) map.Invalidate();     // the next build marks them (D4)
            return true;
        }

        static void Stand(Outpost o, Terrain.SceneryRocks rocks, CampPath map)
        {
            var isle = o.Island;
            Vector3 camp = o.CampCentre;
            float cell = map.CellSize;
            Vector2 lo = map.Origin + new Vector2(cell * 2f, cell * 2f);
            Vector2 hi = map.Origin + new Vector2((map.Side - 3) * cell, (map.Side - 3) * cell);
            float beach = o.MinGroundHeight;

            LastIndexed = rocks.Count;
            LastMade = LastOffGrid = LastLow = LastUnreachable = LastOverCap = 0;
            picks.Clear();
            for (int i = 0; i < rocks.Count; i++)
            {
                var r = rocks.RockAt(i);
                Vector3 p = r.at;
                if (p.x < lo.x || p.x > hi.x || p.z < lo.y || p.z > hi.y) { LastOffGrid++; continue; }
                if (o.GroundAt(p) < beach) { LastLow++; continue; }
                if (!map.Reachable(p)) { LastUnreachable++; continue; }
                Vector3 d = p - camp; d.y = 0f;
                picks.Add((d.sqrMagnitude, i));
            }
            picks.Sort((a, b) => a.d2 != b.d2 ? a.d2.CompareTo(b.d2) : a.i.CompareTo(b.i));
            if (picks.Count > MaxNodes) { LastOverCap = picks.Count - MaxNodes; picks.RemoveRange(MaxNodes, LastOverCap); }

            foreach (var (_, i) in picks)
            {
                var go = new GameObject("SceneryRock_" + i);
                go.transform.SetParent(rocks.transform, false);
                // Position first: `Awake` takes `SpawnPos` from it.
                go.transform.position = rocks.RockAt(i).at;
                var node = go.AddComponent<ResourceNode>();
                node.Configure(Res.Stone, isle, 4);
                StoneDeposit.DressScenery(node, rocks, i);
                LastMade++;
            }
            picks.Clear();
            Debug.Log($"SceneryStone: {o.name} -- {LastMade} of {LastIndexed} loose rocks are stone "
                + $"({LastOffGrid} off the grid, {LastLow} on the beach, {LastUnreachable} unreachable, {LastOverCap} over the cap), "
                + $"{rocks.LandformCount} rock stamps left as landform");
        }

        /// Forget one camp's caches (a dev reset); null forgets all.
        public static void Forget(Outpost o)
        {
            if (o == null) { firstAsk.Clear(); cache.Clear(); nextLook.Clear(); return; }
            firstAsk.Remove(o); cache.Remove(o); nextLook.Remove(o);
        }
    }
}
