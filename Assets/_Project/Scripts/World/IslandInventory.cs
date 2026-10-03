using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **What an island offers a landing party: everything on it (Kevin,
    /// 2026-10-03: "remove the fog of war").** Was `IslandFog.FoundResources`
    /// / `FoundHerds` / `FoundIslandFinds` (2026-09-30), which counted only
    /// what stood on ground a party had walked. With the fog gone every
    /// island is fully visible, so "found" is simply "there": every standing,
    /// ungathered source, every live animal, every uncollected find. The
    /// counting rules themselves are the fog's, unchanged (kit nodes by their
    /// units, scenery trees one log each, loose rocks by their size, surf
    /// rocks left as scenery).
    ///
    /// Read by `UI.Sheets.LandingPartySheet` (its tiles and chips) and
    /// `Ship.GatherParty` (the report). Stateless except the per-island
    /// `FaunaLod` lookup cache, emptied by `ResetForPlay` (statics outlive
    /// play mode here; called from the populator's world build).
    public static class IslandInventory
    {
        /// Ground above this is land (the populator's flood-fill threshold).
        const float LandHeight = 0.5f;

        static readonly Dictionary<Island, FaunaLod> faunaOf = new Dictionary<Island, FaunaLod>();
        static readonly Dictionary<Island, float> nextFaunaLook = new Dictionary<Island, float>();

        public static void ResetForPlay()
        {
            faunaOf.Clear();
            nextFaunaLook.Clear();
        }

        /// Adds, per resource id, the units standing on `island` and not yet
        /// gathered: kit nodes (ore, spice, deposits) by their units, scenery
        /// trees one log each, loose scenery rocks by their size.
        /// ACCUMULATES into `into` (the caller clears it).
        public static void Resources(Island island, Dictionary<string, int> into)
        {
            if (into == null || island == null) return;
            var o = Outpost.Of(island);
            var books = o != null ? o.Ledger : null;

            foreach (var n in ResourceNode.All)
            {
                if (n == null || n.Home != island || n.Harvested || !n.isActiveAndEnabled) continue;
                if (n.TreeIndex >= 0) continue;               // counted with the scenery trees
                if (!Res.IsGatherable(n.Resource)) continue;
                if (books != null && books.Taken(n)) continue;
                int units = n.Deposit != null ? Mathf.Max(1, n.Deposit.Units)
                          : n.Resource == Res.Timber ? 1 : n.UnitsPerProp;
                Add(into, n.Resource, units);
            }

            var wood = island.GetComponentInChildren<SeaSick.Terrain.SceneryWood>();
            if (wood != null)
            {
                int trees = 0;
                for (int i = 0; i < wood.TreeCount; i++)
                {
                    var t = wood.TreeAt(i);
                    if (t.felled || t.ledgerFelled) continue;
                    if (books != null && books.TreeTaken(i)) continue;
                    trees++;
                }
                if (trees > 0) Add(into, Res.Timber, trees);
            }

            var rocks = SeaSick.Terrain.SceneryRocks.On(island);
            if (rocks != null && !rocks.Materialized)
            {
                var h = Island.TerrainHeight;   // static: the world's height function
                int stone = 0;
                for (int i = 0; i < rocks.Count; i++)
                {
                    if (rocks.IsHidden(i)) continue;
                    if (books != null && books.RockTaken(i)) continue;
                    var r = rocks.RockAt(i);
                    if (h != null && h(r.at.x, r.at.z) < LandHeight) continue;   // surf stays scenery
                    stone += SeaSick.Terrain.SceneryRocks.UnitsOf(r);
                }
                if (stone > 0) Add(into, Res.Stone, stone);
            }
        }

        static void Add(Dictionary<string, int> into, string res, int units)
        {
            into.TryGetValue(res, out int had);
            into[res] = had + units;
        }

        /// Adds every live animal of `island`'s herds. There is no herd type
        /// in the game: a herd is its animals (`Animal`, goats and boar,
        /// owned by the island's `FaunaLod`).
        public static void Herds(Island island, List<Animal> into)
        {
            if (into == null || island == null) return;
            var lod = Fauna(island);
            if (lod == null) return;
            var animals = lod.Animals;
            for (int i = 0; i < animals.Count; i++)
            {
                var a = animals[i];
                if (a == null || a.Dead || !a.isActiveAndEnabled) continue;
                into.Add(a);
            }
        }

        /// The island's herd root. Looked up at most every 10 s per island
        /// while there is none (asked mid world-build it can predate its
        /// fauna); the sheet asks every refresh, so never a scene scan a frame.
        static FaunaLod Fauna(Island island)
        {
            if (faunaOf.TryGetValue(island, out var f) && f != null) return f;
            if (nextFaunaLook.TryGetValue(island, out float at) && Time.unscaledTime < at) return null;
            nextFaunaLook[island] = Time.unscaledTime + 10f;
            foreach (var x in Object.FindObjectsByType<FaunaLod>(FindObjectsSortMode.None))
                if (x != null && x.Island == island) { faunaOf[island] = x; return x; }
            return null;
        }

        /// Adds `island`'s uncollected finds (caches, cairns).
        public static void Finds(Island island, List<IslandFind> into)
        {
            if (into == null || island == null) return;
            var finds = IslandFind.All;
            for (int i = 0; i < finds.Count; i++)
            {
                var f = finds[i];
                if (f != null && f.Owner == island) into.Add(f);
            }
        }
    }
}
