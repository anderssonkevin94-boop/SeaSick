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
    /// counting rules themselves are the fog's (kit nodes by their units,
    /// scenery trees one log each, loose rocks by their size, surf rocks left
    /// as scenery), plus wild berry bushes, one Food each (2026-10-03), all
    /// in ONE list (`Sources`) the party reads too.
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

        // --- the sources, one definition (2026-10-03) ----------------------

        /// What kind of thing a `Source` is: a live `ResourceNode` (a kit
        /// deposit, an ore or spice prop, a camp's own rock node), or an
        /// entry in one of the island's baked scenery indices (a tree, a
        /// loose rock, a wild berry bush), which has no node until a party
        /// stands one on it.
        public enum Kind : byte { Node, Tree, Rock, Bed }

        /// **One thing a landing party can take, and what it is worth.**
        /// Kevin, 2026-10-03, a massive island read "79 timber, 169 stone"
        /// and no food, and "All N" could never deliver N: the tile counted
        /// every tree and rock while the party only ever stood the nearest 48
        /// and 60 as nodes, and nothing counted the berry bushes at all. Now
        /// this list is THE definition: the sheet's tile and "All N"
        /// (`Ship.GatherParty.Survey`, which adds the walk), and the nodes the
        /// party stands (`GatherParty.StandMore`), all read it, so a tile's
        /// figure is exactly what the party can carry off.
        public struct Source
        {
            public Kind kind;
            /// Index in the scenery index (`Tree`/`Rock`/`Bed`), else -1.
            public int index;
            /// The node (`Node` only).
            public ResourceNode node;
            public string resource;
            public Vector3 at;
            /// How far from `at` a hand stands to work it (what the node
            /// stood on it will say, `ResourceNode.StandOff`).
            public float standOff;
            public int units;

            /// One key per source across kinds, for caches.
            public int Key => ((int)kind << 24) | (index & 0xFFFFFF);
        }

        /// Food a landing party carries off one wild berry bush. The camp's
        /// books weigh a bush at `SceneryCrops.YieldOf` (0.5 of a wheat bed)
        /// in a fractional stock; a hand's armful is whole units, so a party
        /// picks one Food a bush (2026-10-03, PROVISIONAL, unplayed) and the
        /// ledger loses the bush's 0.5 (`OutpostLedger.BookBedTake`).
        public const int BerryUnitsPerBush = 1;
        /// One tree is one log (GDD 2026-08-18).
        public const int TreeUnits = 1;
        /// Where a hand stands off a plain node (`ResourceNode.StandOff`).
        public const float PlainStandOff = 1.1f;

        /// Units in a live node: a deposit by its own units (a scenery rock's
        /// by its size), a tree one log, an ore/spice prop one prop's worth.
        public static int UnitsOf(ResourceNode n)
        {
            if (n == null) return 0;
            if (n.Deposit != null) return Mathf.Max(1, n.Deposit.Units);
            if (n.BedIndex >= 0) return BerryUnitsPerBush;
            return n.Resource == Res.Timber ? TreeUnits : n.UnitsPerProp;
        }

        /// Where a hand stands off a loose scenery rock: what
        /// `StoneDeposit.DressScenery` gives the node stood on it.
        public static float RockStandOff(in SeaSick.Terrain.SceneryRocks.Rock r)
            => Mathf.Max(1.1f, r.radius * 0.8f + 0.7f);

        /// Adds every standing, ungathered source on `island` to `into`
        /// (the caller clears it): kit nodes, scenery trees, loose rocks on
        /// land (surf rocks stay scenery) unless a camp stood nodes on them,
        /// and wild berry bushes. Leaves out whatever a camp or a party has
        /// taken -- felled, hidden, harvested, or booked by name in the
        /// island's ledger (`OutpostLedger.GroundTaken`).
        public static void Sources(Island island, List<Source> into)
        {
            if (into == null || island == null) return;
            var o = Outpost.Of(island);
            var books = o != null ? o.Ledger : null;

            foreach (var n in ResourceNode.All)
            {
                if (n == null || n.Home != island || n.Harvested || !n.isActiveAndEnabled) continue;
                if (n.TreeIndex >= 0 || n.BedIndex >= 0) continue;   // counted with the scenery
                if (!Res.IsGatherable(n.Resource)) continue;
                if (books != null && books.Taken(n)) continue;
                into.Add(new Source
                {
                    kind = Kind.Node, index = -1, node = n, resource = n.Resource,
                    at = n.transform.position, standOff = n.StandOff, units = UnitsOf(n),
                });
            }

            var wood = island.GetComponentInChildren<SeaSick.Terrain.SceneryWood>();
            if (wood != null)
            {
                for (int i = 0; i < wood.TreeCount; i++)
                {
                    var t = wood.TreeAt(i);
                    if (t.felled || t.ledgerFelled) continue;
                    if (books != null && books.TreeTaken(i)) continue;
                    into.Add(new Source
                    {
                        kind = Kind.Tree, index = i, resource = Res.Timber,
                        at = t.baseAt, standOff = PlainStandOff, units = TreeUnits,
                    });
                }
            }

            var rocks = SeaSick.Terrain.SceneryRocks.On(island);
            if (rocks != null && !rocks.Materialized)
            {
                var h = Island.TerrainHeight;   // static: the world's height function
                for (int i = 0; i < rocks.Count; i++)
                {
                    if (rocks.IsHidden(i)) continue;
                    if (books != null && books.RockTaken(i)) continue;
                    var r = rocks.RockAt(i);
                    if (h != null && h(r.at.x, r.at.z) < LandHeight) continue;   // surf stays scenery
                    into.Add(new Source
                    {
                        kind = Kind.Rock, index = i, resource = Res.Stone,
                        at = r.at, standOff = RockStandOff(r), units = SeaSick.Terrain.SceneryRocks.UnitsOf(r),
                    });
                }
            }

            // Wild berry bushes (GDD: "hands told to gather Food forage wild
            // berries"). Wheat stays the camp's field. A bush already picked
            // -- by the camp's count (`SyncHarvest`) or by a party, booked by
            // name and held -- is not standing, so the one ledger is never
            // counted twice.
            var crops = SeaSick.Terrain.SceneryCrops.On(island);
            if (crops != null)
            {
                for (int i = 0; i < crops.BedCount; i++)
                {
                    var b = crops.BedAt(i);
                    if (b.kind != SeaSick.Terrain.SceneryCrops.BedKind.Berry || b.harvested || b.held) continue;
                    if (books != null && books.BedTaken(i)) continue;
                    into.Add(new Source
                    {
                        kind = Kind.Bed, index = i, resource = Res.Food,
                        at = b.at, standOff = PlainStandOff, units = BerryUnitsPerBush,
                    });
                }
            }
        }

        static readonly List<Source> scratch = new List<Source>(256);

        /// Adds, per resource id, the units standing on `island` and not yet
        /// gathered (`Sources`, summed). ACCUMULATES into `into` (the caller
        /// clears it).
        public static void Resources(Island island, Dictionary<string, int> into)
        {
            if (into == null || island == null) return;
            scratch.Clear();
            Sources(island, scratch);
            for (int i = 0; i < scratch.Count; i++) Add(into, scratch[i].resource, scratch[i].units);
            scratch.Clear();
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
