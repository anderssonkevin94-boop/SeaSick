using System.Collections.Generic;
using UnityEngine;
using SeaSick.World;

namespace SeaSick.Terrain
{
    /// Harvest index for the wheat an island was dressed with -- the crop
    /// equivalent of `SceneryWood`, and shaped the same way for the same
    /// reason: the mats are welded into the scenery cells so a field costs
    /// one draw call, and this records where each mat sits and which
    /// vertices it owns in both LOD meshes, so one bed can be harvested
    /// without touching the rest.
    ///
    /// **A bed is one unit of Food.** Harvesting one does not delete it the
    /// way felling collapses a tree: the mat is squashed to stubble (its
    /// vertices pulled down to a quarter of their height above the ground)
    /// and `Regrow` stands it back up, so a field worked and left alone
    /// comes back the way the ledger's regrowth says it does.
    ///
    /// Which beds are harvested, and how many, is the OUTPOST's decision
    /// (`Outpost.SyncHarvest`), nearest the camp outward, driven off the
    /// ledger's Food stock -- the same discipline that keeps the wood a
    /// pure function of the books. This only knows how to squash and
    /// restore a mat.
    ///
    /// Beds may also be PLANTED at runtime (`Plant`): a farm puts its own
    /// rows down, each as a small standalone mesh using the same kit pieces.
    public class SceneryCrops : MonoBehaviour
    {
        /// What a bed grows. Wheat is the rich source (see `YieldOf`); berry
        /// is the scrub bush indexed alongside it -- present on islands with
        /// no wheat, so there is always something to gather.
        public enum BedKind { Wheat, Berry }

        public struct Bed
        {
            public Vector3 at;                       // ground point the mat stands on
            public int kit;                          // 0..2, which Crop_/Scrub_ piece
            public BedKind kind;                      // Wheat or Berry
            public int cell;                         // index into `cells` (welded) or -1
            public int vertStart, vertCount;         // LOD0 run in the cell's mesh
            public int lod1Start, lod1Count;         // LOD1 run (0 if none)
            public bool harvested;
            public double regrowAtDay;               // game day this bed stands again if left alone
            public GameObject instance;              // runtime-planted bed, or null
            /// Picked by a landing party and booked BY NAME in the island's
            /// ledger (`OutpostLedger.takenBeds`, 2026-10-03). Out of the
            /// field's units (`TotalUnits`, `StandingUnits`) so the camp's
            /// count-based Food never includes it, and `Regrow` refuses it:
            /// only `ReleaseHeld` (the ledger entry's regrow day passing,
            /// `GroundTaken.Apply`) stands it back up.
            public bool held;
        }

        /// How much of a mat is left standing after it is cut.
        public const float StubbleScale = 0.25f;

        /// A picked berry bush is not cut to stubble -- it is stripped: it
        /// keeps most of its height and just goes bare-twig.
        public const float BerryPickedScale = 0.85f;

        /// The colour a berry bush's vertices lerp toward when picked.
        static readonly Color32 TwigBrown = new Color32(92, 68, 46, 255);

        /// How much a picked bush's colour moves toward `TwigBrown`.
        const float TwigBlend = 0.65f;

        readonly List<Bed> beds = new List<Bed>();
        List<SceneryWood.Cell> cells;
        Island island;

        /// Original vertex runs (and, for berries, original colours) of
        /// harvested beds, so `Regrow` restores the mat exactly rather than
        /// re-inflating a guess.
        readonly Dictionary<int, (Vector3[] v0, Vector3[] v1, Color32[] c0, Color32[] c1)> kept
            = new Dictionary<int, (Vector3[], Vector3[], Color32[], Color32[])>();

        public Island Island => island;
        public int BedCount => beds.Count;
        public Bed BedAt(int i) => beds[i];

        public int Standing
        {
            get { int n = 0; for (int i = 0; i < beds.Count; i++) if (!beds[i].harvested) n++; return n; }
        }
        public int Harvested => beds.Count - Standing;

        /// Food units one bed is worth: wheat is the rich source, berry is
        /// the fallback that keeps a wheatless island fed but poorer.
        public float YieldOf(int i)
            => (i >= 0 && i < beds.Count && beds[i].kind == BedKind.Berry) ? 0.5f : 1.0f;

        /// Sum of `YieldOf` over every bed still standing -- the units
        /// figure `Outpost.ReconcileCrops` can use in place of a raw bed
        /// count once it wants wheat and berry to weigh differently.
        /// Every bed's yield, picked or not: the field's capacity, which is
        /// what the ledger's ceiling should be. `StandingUnits` is what is
        /// there to take right now.
        ///
        /// A bed a landing party picked (`Bed.held`) is in neither: the party
        /// booked its yield out of the ledger's Food (standing and ceiling
        /// together, `OutpostLedger.BookBedTake`), so leaving it out here is
        /// what keeps `Outpost.ReconcileCrops`'s ceiling equal to the books.
        public float TotalUnits
        {
            get
            {
                float u = 0f;
                for (int i = 0; i < beds.Count; i++) if (!beds[i].held) u += YieldOf(i);
                return u;
            }
        }

        public float StandingUnits
        {
            get
            {
                float u = 0f;
                for (int i = 0; i < beds.Count; i++)
                    if (!beds[i].harvested && !beds[i].held) u += YieldOf(i);
                return u;
            }
        }

        /// Picked by a landing party and still booked by name (see `Bed.held`).
        public bool IsHeld(int i) => i >= 0 && i < beds.Count && beds[i].held;

        /// **A landing party picked bed `i` (2026-10-03).** Squashed like any
        /// harvest (a bush goes bare-twig, `BerryPickedScale`) and held: no
        /// camp regrow stands it up until `ReleaseHeld`. Idempotent.
        public void HoldPicked(int i)
        {
            if (i < 0 || i >= beds.Count) return;
            Harvest(i);
            var b = beds[i];
            b.held = true;
            beds[i] = b;
        }

        /// The ledger's regrow day for a party-picked bed passed: it is the
        /// field's again, and stands back up. Idempotent.
        public void ReleaseHeld(int i)
        {
            if (i < 0 || i >= beds.Count || !beds[i].held) return;
            var b = beds[i];
            b.held = false;
            beds[i] = b;
            Regrow(i);
        }

        /// Wire the welded index. `cells` is the SAME list the wood was
        /// configured with, so both share one cached vertex array per cell
        /// and a felling never writes stale wheat back over a harvest.
        public void Configure(List<SceneryWood.Cell> cells, List<Bed> index, Island isle)
        {
            this.cells = cells;
            island = isle;
            beds.Clear();
            beds.AddRange(index);
        }

        /// The crops on this island, if it was dressed with any.
        public static SceneryCrops On(Island isle)
            => isle != null ? isle.GetComponentInChildren<SceneryCrops>() : null;

        // --- harvest / regrow ------------------------------------------------

        /// Lerp a colour toward `TwigBrown` by `TwigBlend`, alpha untouched.
        static Color32 Twiggy(Color32 src) => new Color32(
            (byte)Mathf.RoundToInt(Mathf.Lerp(src.r, TwigBrown.r, TwigBlend)),
            (byte)Mathf.RoundToInt(Mathf.Lerp(src.g, TwigBrown.g, TwigBlend)),
            (byte)Mathf.RoundToInt(Mathf.Lerp(src.b, TwigBrown.b, TwigBlend)),
            src.a);

        /// Squash bed `i`. Wheat goes to stubble; a berry bush is stripped
        /// instead -- it keeps most of its height and just goes bare-twig.
        /// Idempotent.
        public void Harvest(int i)
        {
            if (i < 0 || i >= beds.Count || beds[i].harvested) return;
            var b = beds[i];
            bool berry = b.kind == BedKind.Berry;
            float scale = berry ? BerryPickedScale : StubbleScale;
            if (b.instance != null)
            {
                var s = b.instance.transform.localScale;
                b.instance.transform.localScale = new Vector3(s.x, s.y * scale, s.z);
            }
            else if (cells != null && b.cell >= 0 && b.cell < cells.Count)
            {
                var c = cells[b.cell];
                Vector3[] k0 = null, k1 = null;
                Color32[] kc0 = null, kc1 = null;
                if (c.v0 == null && c.lod0 != null) c.v0 = c.lod0.vertices;
                if (c.v1 == null && c.lod1 != null) c.v1 = c.lod1.vertices;
                Color32[] col0 = berry && c.lod0 != null ? c.lod0.colors32 : null;
                Color32[] col1 = berry && c.lod1 != null ? c.lod1.colors32 : null;
                if (c.v0 != null && b.vertCount > 0)
                {
                    k0 = new Vector3[b.vertCount];
                    if (col0 != null) kc0 = new Color32[b.vertCount];
                    for (int v = 0; v < b.vertCount && b.vertStart + v < c.v0.Length; v++)
                    {
                        int vi = b.vertStart + v;
                        var p = c.v0[vi];
                        k0[v] = p;
                        p.y = b.at.y + (p.y - b.at.y) * scale;
                        c.v0[vi] = p;
                        if (col0 != null && vi < col0.Length)
                        {
                            kc0[v] = col0[vi];
                            col0[vi] = Twiggy(col0[vi]);
                        }
                    }
                    c.lod0.SetVertices(c.v0);
                    if (col0 != null) c.lod0.SetColors(col0);
                }
                if (c.v1 != null && b.lod1Count > 0)
                {
                    k1 = new Vector3[b.lod1Count];
                    if (col1 != null) kc1 = new Color32[b.lod1Count];
                    for (int v = 0; v < b.lod1Count && b.lod1Start + v < c.v1.Length; v++)
                    {
                        int vi = b.lod1Start + v;
                        var p = c.v1[vi];
                        k1[v] = p;
                        p.y = b.at.y + (p.y - b.at.y) * scale;
                        c.v1[vi] = p;
                        if (col1 != null && vi < col1.Length)
                        {
                            kc1[v] = col1[vi];
                            col1[vi] = Twiggy(col1[vi]);
                        }
                    }
                    c.lod1.SetVertices(c.v1);
                    if (col1 != null) c.lod1.SetColors(col1);
                }
                kept[i] = (k0, k1, kc0, kc1);
            }
            b.harvested = true;
            float regrow = Res.RegrowPerDay(Res.Food);
            b.regrowAtDay = TimeOfDay.Seconds / TimeOfDay.WorkDaySeconds
                          + (regrow > 0f ? 1f / regrow : double.MaxValue);
            beds[i] = b;
        }

        /// Stand bed `i` back up. Idempotent.
        public void Regrow(int i)
        {
            if (i < 0 || i >= beds.Count || !beds[i].harvested) return;
            // A party's picked bush is the ledger's by name until its day
            // comes (`ReleaseHeld`); `Outpost.SyncHarvest` regrowing the back
            // of its order must not stand it up.
            if (beds[i].held) return;
            var b = beds[i];
            if (b.instance != null)
            {
                float scale = b.kind == BedKind.Berry ? BerryPickedScale : StubbleScale;
                var s = b.instance.transform.localScale;
                b.instance.transform.localScale = new Vector3(s.x, s.y / scale, s.z);
            }
            else if (kept.TryGetValue(i, out var k) && cells != null && b.cell >= 0 && b.cell < cells.Count)
            {
                var c = cells[b.cell];
                if (k.v0 != null && c.v0 != null)
                {
                    for (int v = 0; v < k.v0.Length && b.vertStart + v < c.v0.Length; v++)
                        c.v0[b.vertStart + v] = k.v0[v];
                    c.lod0.SetVertices(c.v0);
                }
                if (k.v1 != null && c.v1 != null)
                {
                    for (int v = 0; v < k.v1.Length && b.lod1Start + v < c.v1.Length; v++)
                        c.v1[b.lod1Start + v] = k.v1[v];
                    c.lod1.SetVertices(c.v1);
                }
                if (k.c0 != null && c.lod0 != null)
                {
                    var col0 = c.lod0.colors32;
                    for (int v = 0; v < k.c0.Length && b.vertStart + v < col0.Length; v++)
                        col0[b.vertStart + v] = k.c0[v];
                    c.lod0.SetColors(col0);
                }
                if (k.c1 != null && c.lod1 != null)
                {
                    var col1 = c.lod1.colors32;
                    for (int v = 0; v < k.c1.Length && b.lod1Start + v < col1.Length; v++)
                        col1[b.lod1Start + v] = k.c1[v];
                    c.lod1.SetColors(col1);
                }
                kept.Remove(i);
            }
            b.harvested = false;
            beds[i] = b;
        }

        /// Regrow every bed whose own timer has run out. For crops with no
        /// camp keeping books on them; a camp drives regrowth off its ledger
        /// through `Outpost.SyncHarvest` instead.
        public int RegrowOverdue()
        {
            double today = TimeOfDay.Seconds / TimeOfDay.WorkDaySeconds;
            int n = 0;
            for (int i = 0; i < beds.Count; i++)
                if (beds[i].harvested && beds[i].instance != null && today >= beds[i].regrowAtDay)
                { Regrow(i); n++; }
            return n;
        }

        /// Nearest standing bed to `at` within `reach`, or -1.
        public int NearestStanding(Vector3 at, float reach)
        {
            int best = -1;
            float bestSq = reach * reach;
            for (int i = 0; i < beds.Count; i++)
            {
                if (beds[i].harvested) continue;
                Vector3 d = beds[i].at - at;
                d.y = 0f;
                float m = d.sqrMagnitude;
                if (m < bestSq) { bestSq = m; best = i; }
            }
            return best;
        }

        // --- planting at runtime ---------------------------------------------

        /// Put `beds` wheat mats down in rows at `at`, facing `yaw` (degrees),
        /// as standalone meshes from the same Crop_0..2 kit pieces the island
        /// dressing uses, each gatherable like any other bed. Creates the
        /// component on the island if it has none. Returns how many stood.
        public static int Plant(Island isle, Vector3 at, float yaw, int beds)
        {
            if (isle == null || beds <= 0) return 0;
            var crops = On(isle);
            if (crops == null)
            {
                var go = new GameObject("Crops");
                go.transform.SetParent(isle.transform, false);
                crops = go.AddComponent<SceneryCrops>();
                crops.Configure(new List<SceneryWood.Cell>(), new List<Bed>(), isle);
            }
            return crops.PlantHere(at, yaw, beds);
        }

        /// **A farm the player moved takes its field with it (Kevin,
        /// 2026-09-30: "I want to be able to turn and move buildings even
        /// after they're built").** Every runtime-planted bed (`PlantHere`,
        /// the only kind with an `instance`) that `mine` claims is carried
        /// rigidly from the old root pose to the new one -- the same spots
        /// `FarmFields` plants at when the moved farm is raised on the next
        /// load -- and re-seated on the ground there. Its harvest state rides
        /// along; the island's own dressed wheat (welded, no instance) never
        /// moves. Returns how many beds moved.
        public int MoveBeds(Vector3 fromRoot, Quaternion fromRot, Vector3 toRoot, Quaternion toRot,
            System.Func<Vector3, bool> mine)
        {
            var turn = toRot * Quaternion.Inverse(fromRot);
            int moved = 0;
            for (int i = 0; i < beds.Count; i++)
            {
                var b = beds[i];
                if (b.instance == null || (mine != null && !mine(b.at))) continue;
                Vector3 d = b.at - fromRoot;
                d.y = 0f;
                Vector3 p = toRoot + turn * d;
                p.y = Island.TerrainHeight != null ? Island.TerrainHeight(p.x, p.z) : b.at.y;
                b.instance.transform.position = new Vector3(p.x, p.y - 0.12f, p.z);
                b.instance.transform.rotation = turn * b.instance.transform.rotation;
                b.at = p;
                beds[i] = b;
                moved++;
            }
            return moved;
        }

        public int PlantHere(Vector3 at, float yaw, int count)
        {
            var kit = new[] { SceneryKit.Get("Crop_0"), SceneryKit.Get("Crop_1"), SceneryKit.Get("Crop_2") };
            if (kit[0] == null || kit[1] == null || kit[2] == null) return 0;
            var mat = IslandScenery.SceneryMaterial();
            var rng = new System.Random(Mathf.RoundToInt(at.x * 7f + at.z * 13f));
            float spacing = 1.6f;
            int cols = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(count)));
            Quaternion facing = Quaternion.Euler(0f, yaw, 0f);
            int made = 0;
            for (int k = 0; k < count; k++)
            {
                int cx = k % cols, cz = k / cols;
                var local = new Vector3((cx - (cols - 1) * 0.5f) * spacing, 0f,
                                        (cz - (Mathf.CeilToInt(count / (float)cols) - 1) * 0.5f) * spacing);
                Vector3 p = at + facing * local;
                if (Island.TerrainHeight != null) p.y = Island.TerrainHeight(p.x, p.z);
                int v = rng.Next(3);
                float target = WorldScale.CropHeight * Mathf.Lerp(0.86f, 1.26f, (float)rng.NextDouble());
                float s = target / Mathf.Max(0.2f, kit[v].height);

                var vs = new List<Vector3>(); var ns = new List<Vector3>();
                var cs = new List<Color32>(); var ts = new List<int>();
                SceneryKit.Stamp(kit[v], vs, ns, cs, ts, Vector3.zero,
                    (float)rng.NextDouble() * Mathf.PI * 2f, Vector3.one);
                var mesh = new Mesh { name = "CropBed" };
                mesh.SetVertices(vs); mesh.SetNormals(ns); mesh.SetColors(cs); mesh.SetTriangles(ts, 0);
                mesh.RecalculateBounds();

                var go = new GameObject("Bed_" + beds.Count);
                go.transform.SetParent(transform, true);
                go.transform.position = new Vector3(p.x, p.y - 0.12f, p.z);
                go.transform.localScale = new Vector3(s, s, s);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                beds.Add(new Bed { at = p, kit = v, cell = -1, instance = go });
                made++;
            }
            return made;
        }
    }
}
