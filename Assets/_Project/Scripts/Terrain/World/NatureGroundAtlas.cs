using System.Collections.Generic;
using SeaSick.World;
using UnityEngine;

namespace SeaSick.Terrain
{
    /// **Every island's painted ground at once** (moss under the canopy, bare
    /// earth at the roots, meadow/lush/dry patches), for the terrain shader.
    ///
    /// The nature ground used to be ONE global texture with one set of bounds,
    /// which is why it could only ever dress one island (Island_2). Now each
    /// island gets its own slice of a `Texture2DArray`, and a coarse world-wide
    /// index map says which slice (if any) covers a spot, so the shader pays one
    /// point sample and one array sample per pixel however many islands exist.
    ///
    /// Sizes: 256² RGBA32 a slice, the same resolution Island_2 was painted at,
    /// is 256 KB, so ~70 discovered islands is ~18 MB of GPU memory and no CPU
    /// copy (slices are filled by `Graphics.CopyTexture` from a throwaway
    /// texture). The index map is one byte per 6 m cell over the discovered
    /// world, re-uploaded at most once a frame (`Flush`), never once per island.
    public static class NatureGroundAtlas
    {
        public const int Size = 256, MaxSlots = 128;
        const float IndexCell = 6f;

        static Texture2DArray slices;
        static Texture2D index;
        static byte[] indexBytes;
        static int indexN;
        static float indexMin;
        static readonly Vector4[] bounds = new Vector4[MaxSlots];
        static readonly Dictionary<Island, int> slotOf = new Dictionary<Island, int>();
        static bool dirty;

        public static int Count => slotOf.Count;

        /// Forget the last world's ground. Called at the start of a world
        /// build (statics outlive play mode here).
        public static void Reset()
        {
            slotOf.Clear();
            if (slices != null) Object.Destroy(slices);
            if (index != null) Object.Destroy(index);
            slices = null; index = null; indexBytes = null; dirty = false;
            Shader.SetGlobalFloat("_IslandNatureEnabled", 0f);
        }

        /// Store one island's ground. `pixels` is Size² linear colours, alpha =
        /// how much of the painted ground shows (0 at the waterline). `span` is
        /// the metres the square covers, centred on `centre`.
        public static void Put(Island isle, Color[] pixels, Vector2 centre, float span,
                               System.Func<float, float> radiusAt, float worldHalfExtent)
        {
            if (isle == null || pixels == null || pixels.Length != Size * Size) return;
            Ensure(worldHalfExtent);
            if (!slotOf.TryGetValue(isle, out int slot))
            {
                if (slotOf.Count >= MaxSlots) { Debug.LogWarning("[NatureGroundAtlas] out of slots; " + isle.name + " keeps the plain ground"); return; }
                slot = slotOf.Count;
                slotOf.Add(isle, slot);
            }

            var temp = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true);
            temp.SetPixels(pixels);
            temp.Apply(false, false);
            Graphics.CopyTexture(temp, 0, 0, slices, slot, 0);
            Object.Destroy(temp);
            bounds[slot] = new Vector4(centre.x, centre.y, span, 1f / span);

            // Mark the island's own ground in the index: inside its outline and
            // a few metres of beach past it, so the painted alpha (which already
            // fades to nothing at the waterline) is never cut by a cell edge.
            float half = span * 0.5f;
            int x0 = Cell(centre.x - half), x1 = Cell(centre.x + half);
            int z0 = Cell(centre.y - half), z1 = Cell(centre.y + half);
            byte id = (byte)(slot + 1);
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                float px = indexMin + (x + 0.5f) * IndexCell - centre.x;
                float pz = indexMin + (z + 0.5f) * IndexCell - centre.y;
                float inside = radiusAt(Mathf.Atan2(px, pz)) - Mathf.Sqrt(px * px + pz * pz);
                if (inside > -IndexCell) indexBytes[z * indexN + x] = id;
            }
            dirty = true;
        }

        static int Cell(float w) => Mathf.Clamp(Mathf.FloorToInt((w - indexMin) / IndexCell), 0, indexN - 1);

        static void Ensure(float worldHalfExtent)
        {
            if (slices != null) return;
            slices = new Texture2DArray(Size, Size, MaxSlots, TextureFormat.RGBA32, false, true)
            {
                name = "Island nature ground (all islands)",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            slices.Apply(false, true);
            indexMin = -worldHalfExtent;
            indexN = Mathf.CeilToInt(worldHalfExtent * 2f / IndexCell);
            indexBytes = new byte[indexN * indexN];
            index = new Texture2D(indexN, indexN, TextureFormat.R8, false, true)
            {
                name = "Island nature ground index",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
            };
        }

        /// Upload what changed and point the shader at it. Cheap when nothing
        /// did; called every frame by `IslandNatureProfile`.
        public static void Flush()
        {
            if (!dirty || index == null) return;
            dirty = false;
            index.LoadRawTextureData(indexBytes);
            index.Apply(false, false);
            Shader.SetGlobalTexture("_IslandNatureGroundArr", slices);
            Shader.SetGlobalTexture("_IslandNatureIndex", index);
            Shader.SetGlobalVector("_IslandNatureIndexBounds",
                new Vector4(indexMin, indexMin, 1f / (indexN * IndexCell), 0f));
            Shader.SetGlobalVectorArray("_IslandNatureBoundsArr", bounds);
            Shader.SetGlobalFloat("_IslandNatureEnabled", 1f);
        }
    }
}
