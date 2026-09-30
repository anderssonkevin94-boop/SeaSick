using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// **Islands you have not settled start fogged (Kevin, 2026-09-30).**
    /// Approved mockups "7 · Landing party: Explore (fog)" and "7b · Gather":
    /// an island without a camp is under a soft cloud cover except the strip
    /// of shore where the ship anchored; explorers of the landing party open
    /// it around them as they walk, and what is open stays open (saved per
    /// island). What lies in the open ground is what the party has FOUND:
    /// harvest nodes, herds and island finds.
    ///
    /// **The grid.** One coarse bit per cell over the island's own land:
    /// cells of `CellTarget` (5 m), coarser only when an island would need
    /// more than `MaxCellsPerSide` a side (a 250 m landmark: 6 m). The
    /// bounds are the island's component in the populator's flood-fill
    /// mask (24 m cells), so a crescent gets its own box and never its
    /// neighbour's land. A cell is LAND when the ground there stands above
    /// 0.5 m (the mask's own threshold) and the mask says this island owns
    /// it; the land share is what `Revealed01` counts.
    ///
    /// **Settled islands are clear.** An island with a campfire or any
    /// building (`Settled`) reports everything revealed, whatever its bits
    /// say, and `IslandFogView` draws nothing there. `For` never returns
    /// null for a real island: a settled one simply reads `Revealed01` = 1.
    ///
    /// **Cheap.** Built lazily, once per island per world, on the first
    /// `For` (a few thousand height samples at most). `RevealAround` walks
    /// the circle's box only; `IsRevealed` is an index and a bit.
    public sealed class IslandFog
    {
        /// Target cell edge, metres.
        public const float CellTarget = 5f;
        /// Cells a side at most; bigger islands get bigger cells.
        public const int MaxCellsPerSide = 96;
        /// The shore strip opened on anchoring (`RevealShoreNear`).
        public const float ShoreRevealRadius = 40f;
        /// How far inland the anchoring strip reaches past the beach.
        const float ShoreStripInland = 18f;
        /// Ground above this is land (the flood fill's own threshold).
        const float LandHeight = 0.5f;
        /// Cells the cloud reaches past the coast, so the beach is covered.
        public const int FogDilateCells = 2;

        static readonly Dictionary<Island, IslandFog> all = new Dictionary<Island, IslandFog>();
        static readonly List<Island> scratchDead = new List<Island>();
        static SeaSick.Terrain.TerrainWorldPopulator populator;

        public Island Island { get; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public float Cell { get; private set; }
        /// World XZ of cell (0, 0)'s corner.
        public Vector2 Origin { get; private set; }
        /// Bumps on every change; caches (the view, the chart) compare it.
        public int Version { get; private set; }

        byte[] bits;          // revealed, one bit per cell
        byte[] flags;         // bit0 land, bit1 fog-covered (land dilated)
        float[] ground;       // ground height per cell centre, metres
        int landCount, revealedLand;
        bool built;

        const byte FlagLand = 1, FlagFog = 2;

        public event System.Action Changed;

        IslandFog(Island island) { Island = island; }

        // --- lookup ---------------------------------------------------------

        /// The fog of `island`, built on first ask. Null only for a null
        /// island. A settled island's fog reads fully revealed.
        public static IslandFog For(Island island)
        {
            if (island == null) return null;
            if (all.TryGetValue(island, out var f)) return f;
            PurgeDead();
            f = new IslandFog(island);
            f.Build();
            all[island] = f;
            return f;
        }

        /// The fog if it has been built already, else null. Never builds:
        /// for painters (the chart) that must not spend a frame on it.
        public static IslandFog Existing(Island island)
        {
            if (island == null) return null;
            return all.TryGetValue(island, out var f) ? f : null;
        }

        /// A camp, or any standing building, lifts the fog for good.
        public static bool Settled(Island island)
        {
            if (island == null) return true;
            if (island.IsHome) return true;
            var o = Outpost.Of(island);
            return o != null && (o.HasCamp || (o.Built != null && o.Built.Count > 0));
        }

        /// Is this island's fog worth drawing: not settled, and not open.
        public bool Fogged => !Settled(Island) && landCount > 0 && revealedLand < landCount;

        /// Statics outlive play mode (domain reload is off). Called from the
        /// populator's world build beside `IslandFind.ResetForPlay`.
        public static void ResetForPlay()
        {
            all.Clear();
            populator = null;
        }

        static void PurgeDead()
        {
            scratchDead.Clear();
            foreach (var kv in all) if (kv.Key == null) scratchDead.Add(kv.Key);
            foreach (var k in scratchDead) all.Remove(k);
        }

        // --- the grid -------------------------------------------------------

        void Build()
        {
            if (built) return;
            built = true;
            Vector3 c = Island.transform.position;

            // Bounds: the island's component in the flood-fill mask, one
            // mask cell of margin; a radius box when there is no mask.
            if (populator == null) populator = Object.FindFirstObjectByType<SeaSick.Terrain.TerrainWorldPopulator>();
            int myMask = 0;
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            var pop = populator;
            int[] mask = pop != null ? pop.LandMask : null;
            int mn = pop != null ? pop.MaskSize : 0;
            float mc = pop != null ? pop.MaskCell : 0f;
            Vector2 mo = pop != null ? pop.MaskOrigin : Vector2.zero;
            if (mask != null && mn > 0 && mc > 0f)
            {
                for (int j = 0; j < mn; j++)
                    for (int i = 0; i < mn; i++)
                    {
                        int v = mask[j * mn + i];
                        if (v <= 0) continue;
                        if (myMask == 0)
                        {
                            if (pop.IslandForMask(v) != Island) continue;
                            myMask = v;
                        }
                        else if (v != myMask) continue;
                        float x0 = mo.x + i * mc, z0 = mo.y + j * mc;
                        if (x0 < minX) minX = x0;
                        if (z0 < minZ) minZ = z0;
                        if (x0 + mc > maxX) maxX = x0 + mc;
                        if (z0 + mc > maxZ) maxZ = z0 + mc;
                    }
            }
            if (myMask == 0)
            {
                float r = Island.MaxRadius * 1.1f + 12f;
                minX = c.x - r; maxX = c.x + r; minZ = c.z - r; maxZ = c.z + r;
                mask = null;
            }
            else
            {
                minX -= mc; minZ -= mc; maxX += mc; maxZ += mc;
            }

            float span = Mathf.Max(maxX - minX, maxZ - minZ);
            Cell = Mathf.Max(CellTarget, span / MaxCellsPerSide);
            Width = Mathf.Max(1, Mathf.CeilToInt((maxX - minX) / Cell));
            Height = Mathf.Max(1, Mathf.CeilToInt((maxZ - minZ) / Cell));
            Origin = new Vector2(minX, minZ);
            int n = Width * Height;
            bits = new byte[(n + 7) >> 3];
            flags = new byte[n];
            ground = new float[n];

            var h = Island.TerrainHeight;
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    int k = y * Width + x;
                    float wx = Origin.x + (x + 0.5f) * Cell, wz = Origin.y + (y + 0.5f) * Cell;
                    float g;
                    bool mine;
                    if (h != null)
                    {
                        g = h(wx, wz);
                        mine = g > LandHeight && Owns(mask, mn, mc, mo, myMask, wx, wz, c);
                    }
                    else
                    {
                        // No height function (a scene without the populator):
                        // the radial outline stands in for the shore.
                        Vector3 p = new Vector3(wx, 0f, wz);
                        mine = Island.FlatDistance(p, c) < Island.RadiusToward(p);
                        g = mine ? 2f : 0f;
                    }
                    ground[k] = g;
                    if (mine) { flags[k] = FlagLand; landCount++; }
                }

            // The cloud reaches a couple of cells past the coast so the beach
            // and the palms on it are under it too.
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    int k = y * Width + x;
                    if ((flags[k] & FlagLand) != 0) { flags[k] |= FlagFog; continue; }
                    bool near = false;
                    for (int dy = -FogDilateCells; dy <= FogDilateCells && !near; dy++)
                    {
                        int yy = y + dy;
                        if (yy < 0 || yy >= Height) continue;
                        for (int dx = -FogDilateCells; dx <= FogDilateCells; dx++)
                        {
                            int xx = x + dx;
                            if (xx < 0 || xx >= Width) continue;
                            if ((flags[yy * Width + xx] & FlagLand) != 0) { near = true; break; }
                        }
                    }
                    if (near) flags[k] |= FlagFog;
                }
        }

        /// Is the land at (wx, wz) this island's? The mask cell says so, or
        /// (on the coast, where a 24 m cell reads water) a neighbour does
        /// and no other island's cell is nearer.
        bool Owns(int[] mask, int mn, float mc, Vector2 mo, int myMask, float wx, float wz, Vector3 c)
        {
            if (mask == null)
            {
                var p = new Vector3(wx, 0f, wz);
                return Island.FlatDistance(p, c) < Island.RadiusToward(p) * 1.25f + 10f;
            }
            int i = Mathf.FloorToInt((wx - mo.x) / mc), j = Mathf.FloorToInt((wz - mo.y) / mc);
            if (i < 0 || j < 0 || i >= mn || j >= mn) return false;
            int v = mask[j * mn + i];
            if (v == myMask) return true;
            if (v > 0) return false;
            bool mine = false;
            for (int dj = -1; dj <= 1; dj++)
                for (int di = -1; di <= 1; di++)
                {
                    int ii = i + di, jj = j + dj;
                    if (ii < 0 || jj < 0 || ii >= mn || jj >= mn) continue;
                    int w = mask[jj * mn + ii];
                    if (w == myMask) mine = true;
                    else if (w > 0) return false;
                }
            return mine;
        }

        bool CellOf(Vector3 world, out int x, out int y)
        {
            x = Mathf.FloorToInt((world.x - Origin.x) / Cell);
            y = Mathf.FloorToInt((world.z - Origin.y) / Cell);
            return x >= 0 && y >= 0 && x < Width && y < Height;
        }

        bool Bit(int k) => (bits[k >> 3] & (1 << (k & 7))) != 0;

        /// Set cell k; true when it was not set before.
        bool SetBit(int k)
        {
            int b = 1 << (k & 7);
            if ((bits[k >> 3] & b) != 0) return false;
            bits[k >> 3] = (byte)(bits[k >> 3] | b);
            if ((flags[k] & FlagLand) != 0) revealedLand++;
            int x = k % Width, y = k / Width;
            if (x < dirtyX0) dirtyX0 = x;
            if (y < dirtyY0) dirtyY0 = y;
            if (x > dirtyX1) dirtyX1 = x;
            if (y > dirtyY1) dirtyY1 = y;
            return true;
        }

        // Cells opened since the view last looked, as a box.
        int dirtyX0 = int.MaxValue, dirtyY0 = int.MaxValue, dirtyX1 = -1, dirtyY1 = -1;

        /// The box of cells opened since the last call (for the view's fade),
        /// and forget it. False when nothing opened.
        public bool TakeDirty(out int x0, out int y0, out int x1, out int y1)
        {
            x0 = dirtyX0; y0 = dirtyY0; x1 = dirtyX1; y1 = dirtyY1;
            dirtyX0 = dirtyY0 = int.MaxValue; dirtyX1 = dirtyY1 = -1;
            return x1 >= x0 && y1 >= y0;
        }

        // --- the API --------------------------------------------------------

        /// Open every cell whose centre lies within `radius` of `world`.
        /// Idempotent; raises `Changed` only when a cell actually opened.
        public void RevealAround(Vector3 world, float radius)
        {
            if (radius <= 0f || bits == null) return;
            int x0 = Mathf.FloorToInt((world.x - radius - Origin.x) / Cell);
            int x1 = Mathf.FloorToInt((world.x + radius - Origin.x) / Cell);
            int y0 = Mathf.FloorToInt((world.z - radius - Origin.y) / Cell);
            int y1 = Mathf.FloorToInt((world.z + radius - Origin.y) / Cell);
            if (x1 < 0 || y1 < 0 || x0 >= Width || y0 >= Height) return;
            x0 = Mathf.Max(0, x0); y0 = Mathf.Max(0, y0);
            x1 = Mathf.Min(Width - 1, x1); y1 = Mathf.Min(Height - 1, y1);
            float r2 = radius * radius;
            bool any = false;
            for (int y = y0; y <= y1; y++)
            {
                float dz = Origin.y + (y + 0.5f) * Cell - world.z;
                for (int x = x0; x <= x1; x++)
                {
                    float dx = Origin.x + (x + 0.5f) * Cell - world.x;
                    if (dx * dx + dz * dz > r2) continue;
                    if (SetBit(y * Width + x)) any = true;
                }
            }
            if (any) Touch();
        }

        /// Open ground at `world`? Anything off this island's grid is not
        /// its land and reads open; a settled island is open everywhere.
        public bool IsRevealed(Vector3 world)
        {
            if (bits == null || Settled(Island)) return true;
            if (!CellOf(world, out int x, out int y)) return true;
            return Bit(y * Width + x);
        }

        /// Share of the island's land revealed, 0..1. Settled islands = 1.
        public float Revealed01
        {
            get
            {
                if (Settled(Island) || landCount == 0) return 1f;
                return Mathf.Clamp01(revealedLand / (float)landCount);
            }
        }

        /// **The shore strip on anchoring.** Opens the beach within
        /// `ShoreRevealRadius` (40 m) of the landing and a short way inland
        /// (`ShoreStripInland`) -- the party sees where it steps ashore and
        /// not the island. `anchorWorld` may be the ship (on the water): the
        /// strip is measured from the nearest land cell to it.
        public void RevealShoreNear(Vector3 anchorWorld)
        {
            if (bits == null) return;
            // The landing: the land cell nearest the anchor point.
            int best = -1; float bestD = float.MaxValue;
            for (int k = 0; k < flags.Length; k++)
            {
                if ((flags[k] & FlagLand) == 0) continue;
                float dx = Origin.x + (k % Width + 0.5f) * Cell - anchorWorld.x;
                float dz = Origin.y + (k / Width + 0.5f) * Cell - anchorWorld.z;
                float d = dx * dx + dz * dz;
                if (d < bestD) { bestD = d; best = k; }
            }
            if (best < 0) return;
            float lx = Origin.x + (best % Width + 0.5f) * Cell;
            float lz = Origin.y + (best / Width + 0.5f) * Cell;
            float sandTop = Outpost.Rules.buildFloor > 0f ? Outpost.Rules.buildFloor + 1f : 5.5f;

            float r = ShoreRevealRadius, r2 = r * r, inland2 = ShoreStripInland * ShoreStripInland;
            bool any = false;
            int x0 = Mathf.Max(0, Mathf.FloorToInt((lx - r - Origin.x) / Cell));
            int x1 = Mathf.Min(Width - 1, Mathf.FloorToInt((lx + r - Origin.x) / Cell));
            int y0 = Mathf.Max(0, Mathf.FloorToInt((lz - r - Origin.y) / Cell));
            int y1 = Mathf.Min(Height - 1, Mathf.FloorToInt((lz + r - Origin.y) / Cell));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int k = y * Width + x;
                    float dx = Origin.x + (x + 0.5f) * Cell - lx, dz = Origin.y + (y + 0.5f) * Cell - lz;
                    float d2 = dx * dx + dz * dz;
                    if (d2 > r2) continue;
                    // Beach (low ground, or water beside it) the whole width;
                    // higher ground only close to where they step ashore.
                    bool beach = ground[k] < sandTop;
                    if (!beach && d2 > inland2) continue;
                    if (SetBit(k)) any = true;
                }
            if (any) Touch();
        }

        void Touch()
        {
            Version++;
            Changed?.Invoke();
        }

        // --- what has been found ----------------------------------------------

        /// Adds, per resource id, the units standing in revealed ground and
        /// not yet gathered: kit nodes (ore, spice, deposits) by their units,
        /// scenery trees one log each, loose scenery rocks by their size.
        /// ACCUMULATES into `into` (the caller clears it).
        public void FoundResources(Dictionary<string, int> into)
        {
            if (into == null || Island == null) return;
            var o = Outpost.Of(Island);
            var books = o != null ? o.Ledger : null;

            foreach (var n in ResourceNode.All)
            {
                if (n == null || n.Home != Island || n.Harvested || !n.isActiveAndEnabled) continue;
                if (n.TreeIndex >= 0) continue;               // counted with the scenery trees
                if (!Res.IsGatherable(n.Resource)) continue;
                if (books != null && books.Taken(n)) continue;
                if (!IsRevealed(n.transform.position)) continue;
                int units = n.Deposit != null ? Mathf.Max(1, n.Deposit.Units)
                          : n.Resource == Res.Timber ? 1 : n.UnitsPerProp;
                Add(into, n.Resource, units);
            }

            var wood = Island.GetComponentInChildren<SeaSick.Terrain.SceneryWood>();
            if (wood != null)
            {
                int trees = 0;
                for (int i = 0; i < wood.TreeCount; i++)
                {
                    var t = wood.TreeAt(i);
                    if (t.felled || t.ledgerFelled) continue;
                    if (books != null && books.TreeTaken(i)) continue;
                    if (IsRevealed(t.baseAt)) trees++;
                }
                if (trees > 0) Add(into, Res.Timber, trees);
            }

            var rocks = SeaSick.Terrain.SceneryRocks.On(Island);
            if (rocks != null && !rocks.Materialized)
            {
                var h = Island.TerrainHeight;
                int stone = 0;
                for (int i = 0; i < rocks.Count; i++)
                {
                    if (rocks.IsHidden(i)) continue;
                    if (books != null && books.RockTaken(i)) continue;
                    var r = rocks.RockAt(i);
                    if (h != null && h(r.at.x, r.at.z) < LandHeight) continue;   // surf stays scenery
                    if (IsRevealed(r.at)) stone += SeaSick.Terrain.SceneryRocks.UnitsOf(r);
                }
                if (stone > 0) Add(into, Res.Stone, stone);
            }
        }

        static void Add(Dictionary<string, int> into, string res, int units)
        {
            into.TryGetValue(res, out int had);
            into[res] = had + units;
        }

        /// Adds every live animal of this island's herds standing in revealed
        /// ground. There is no herd type in the game: a herd is its animals
        /// (`Animal`, goats and boar, owned by the island's `FaunaLod`).
        public void FoundHerds(List<Animal> into)
        {
            if (into == null) return;
            var lod = Fauna();
            if (lod == null) return;
            var animals = lod.Animals;
            for (int i = 0; i < animals.Count; i++)
            {
                var a = animals[i];
                if (a == null || a.Dead || !a.isActiveAndEnabled) continue;
                if (IsRevealed(a.transform.position)) into.Add(a);
            }
        }

        FaunaLod fauna;
        float nextFaunaLook;
        /// The island's herd root. Looked up at most every 10 s while there
        /// is none (a fog built mid world-build can predate its fauna).
        FaunaLod Fauna()
        {
            if (fauna != null) return fauna;
            if (Time.unscaledTime < nextFaunaLook) return null;
            nextFaunaLook = Time.unscaledTime + 10f;
            foreach (var f in Object.FindObjectsByType<FaunaLod>(FindObjectsSortMode.None))
                if (f != null && f.Island == Island) { fauna = f; break; }
            return fauna;
        }

        /// Adds this island's uncollected finds (caches, cairns) standing in
        /// revealed ground.
        public void FoundIslandFinds(List<IslandFind> into)
        {
            if (into == null) return;
            var finds = IslandFind.All;
            for (int i = 0; i < finds.Count; i++)
            {
                var f = finds[i];
                if (f == null || f.Owner != Island) continue;
                if (IsRevealed(f.Position)) into.Add(f);
            }
        }

        // --- for the view and the chart ---------------------------------------

        /// Cell k should be under cloud (land or the beach margin, not open).
        public bool FogAt(int k) => bits != null && (flags[k] & FlagFog) != 0 && !Bit(k);
        public bool LandAt(int k) => flags != null && (flags[k] & FlagLand) != 0;
        /// Cell k is ever under cloud (land or beach margin), revealed or not.
        public bool CoverAt(int k) => flags != null && (flags[k] & FlagFog) != 0;
        public float GroundAt(int k) => ground != null ? ground[k] : 0f;
        public int LandCells => landCount;

        /// Metres a chart fog block spans (a few pixels at the chart's
        /// sailing range).
        public const float ChartBlock = 12f;
        readonly List<Vector4> chartRuns = new List<Vector4>();
        int chartRunsVersion = -1;

        /// **The fog as the chart draws it**: runs of still-clouded land
        /// along x, one row per `ChartBlock` metres, as (x0, x1, z, half
        /// row height) in world metres. A block is clouded when most of its
        /// land is. Cached until the fog changes, so painting it every frame
        /// allocates nothing.
        public IReadOnlyList<Vector4> ChartRuns()
        {
            if (chartRunsVersion == Version || bits == null) return chartRuns;
            chartRunsVersion = Version;
            chartRuns.Clear();
            int b = Mathf.Max(1, Mathf.RoundToInt(ChartBlock / Cell));
            float bm = b * Cell;
            for (int by = 0; by * b < Height; by++)
            {
                int runStart = -1;
                int bxCount = (Width + b - 1) / b;
                for (int bx = 0; bx <= bxCount; bx++)
                {
                    bool fogged = false;
                    if (bx < bxCount)
                    {
                        int land = 0, shut = 0;
                        for (int y = by * b; y < Mathf.Min(Height, by * b + b); y++)
                            for (int x = bx * b; x < Mathf.Min(Width, bx * b + b); x++)
                            {
                                int k = y * Width + x;
                                if ((flags[k] & FlagLand) == 0) continue;
                                land++;
                                if (!Bit(k)) shut++;
                            }
                        fogged = land > 0 && shut * 2 >= land;
                    }
                    if (fogged && runStart < 0) runStart = bx;
                    else if (!fogged && runStart >= 0)
                    {
                        float z = Origin.y + (by + 0.5f) * bm;
                        chartRuns.Add(new Vector4(Origin.x + (runStart + 0.5f) * bm,
                                                  Origin.x + (bx - 0.5f) * bm, z, bm * 0.5f));
                        runStart = -1;
                    }
                }
            }
            return chartRuns;
        }

        // --- the save ---------------------------------------------------------

        /// Every unsettled island with anything revealed, as save rows.
        public static List<IslandFogSave> Capture()
        {
            var rows = new List<IslandFogSave>();
            foreach (var kv in all)
            {
                var f = kv.Value;
                if (kv.Key == null || f == null || f.bits == null) continue;
                if (Settled(kv.Key)) continue;
                bool any = false;
                for (int i = 0; i < f.bits.Length; i++) if (f.bits[i] != 0) { any = true; break; }
                if (!any) continue;
                var p = kv.Key.transform.position;
                rows.Add(new IslandFogSave
                {
                    island = kv.Key.name, x = p.x, z = p.z,
                    ox = f.Origin.x, oz = f.Origin.y, cell = f.Cell, w = f.Width, h = f.Height,
                    bits = System.Convert.ToBase64String(f.bits),
                });
            }
            return rows;
        }

        /// Put a save's rows back (after the world is built). Additive: bits
        /// only ever open. A row whose grid no longer matches this build's is
        /// replayed cell by cell at the saved centres.
        public static void Apply(List<IslandFogSave> rows)
        {
            if (rows == null) return;
            foreach (var r in rows)
            {
                if (r == null || string.IsNullOrEmpty(r.bits)) continue;
                var isle = Save.SaveGame.IslandCentred(r.x, r.z);
                if (isle == null && !string.IsNullOrEmpty(r.island))
                    foreach (var i in Island.All) if (i != null && i.name == r.island) { isle = i; break; }
                if (isle == null) continue;
                byte[] saved;
                try { saved = System.Convert.FromBase64String(r.bits); }
                catch (System.FormatException) { continue; }
                var f = For(isle);
                if (f == null || f.bits == null) continue;

                bool same = r.w == f.Width && r.h == f.Height && Mathf.Abs(r.cell - f.Cell) < 0.01f
                         && Mathf.Abs(r.ox - f.Origin.x) < 0.05f && Mathf.Abs(r.oz - f.Origin.y) < 0.05f
                         && saved.Length == f.bits.Length;
                bool any = false;
                int n = Mathf.Min(r.w * r.h, saved.Length * 8);
                for (int k = 0; k < n; k++)
                {
                    if ((saved[k >> 3] & (1 << (k & 7))) == 0) continue;
                    if (same) { if (f.SetBit(k)) any = true; continue; }
                    var at = new Vector3(r.ox + (k % r.w + 0.5f) * r.cell, 0f, r.oz + (k / r.w + 0.5f) * r.cell);
                    int before = f.Version;
                    f.RevealAround(at, r.cell * 0.75f);
                    if (f.Version != before) any = true;
                }
                if (any && same) f.Touch();
            }
        }
    }

    /// One island's revealed cells, as `JsonUtility` can write them: the
    /// grid's geometry and the bit array as base64. Keyed on the island's
    /// centre like `SeenSave`, the name as the fallback.
    [System.Serializable]
    public class IslandFogSave
    {
        public string island;
        public float x, z;
        public float ox, oz, cell;
        public int w, h;
        public string bits;
    }
}
