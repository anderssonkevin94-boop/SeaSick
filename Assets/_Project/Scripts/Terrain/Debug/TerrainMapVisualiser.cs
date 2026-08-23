using System.IO;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace SeaSick.Terrain
{
    /// Renders one stage of the height pipeline over a window of the world
    /// into a texture, shown on a quad at real world scale so it can be read
    /// in the Scene view, and exportable as PNG for the MCP loop.
    /// Sampling is by absolute world coordinate — what you see here is
    /// exactly what a chunk at that position will get.
    public enum TerrainMapStage { RawFbm, IslandMask, Terraced, FinalHeight }

    [ExecuteAlways]
    public class TerrainMapVisualiser : MonoBehaviour
    {
        public TerrainSettings settings;
        public TerrainMapStage stage = TerrainMapStage.FinalHeight;
        [Tooltip("World XZ at the centre of the preview window.")]
        public Vector2 centre = Vector2.zero;
        [Tooltip("Half-width of the window in metres.")]
        public float extent = 2000f;
        public int textureSize = 512;
        [Tooltip("RawFbm: tint texels above 0 so the sign reads at a glance.")]
        public bool tintAboveZero = true;
        [Tooltip("Height stages: metres at which the land ramp reaches its brightest colour.")]
        public float rampMaxHeight = 60f;
        public bool autoRefresh = true;

        Texture2D tex;
        Material mat;
        public Texture2D Texture => tex;
        /// Stats of the last generated map, for probes and the inspector.
        public float LastMin { get; private set; }
        public float LastMax { get; private set; }
        /// Fraction of texels with final height above sea level (any stage).
        public float LastLandFraction { get; private set; }

        [BurstCompile]
        struct SampleJob : IJobParallelFor
        {
            public int size;
            public float2 centre;
            public float extent;
            public int stage;
            public TerrainParams prm;
            [ReadOnly] public NativeArray<float> lut;
            [WriteOnly] public NativeArray<float> values;
            [WriteOnly] public NativeArray<float> heights;

            public void Execute(int index)
            {
                int x = index % size, y = index / size;
                float2 uv = new float2((x + 0.5f) / size, (y + 0.5f) / size);
                float2 world = centre + (uv * 2f - 1f) * extent;
                TerrainSample s = TerrainHeight.Evaluate(world, prm, lut);
                heights[index] = s.height;
                switch (stage)
                {
                    case 0: values[index] = s.noise01 * 2f - 1f; break;
                    case 1: values[index] = s.mask; break;
                    case 2: values[index] = s.terraced; break;
                    default: values[index] = s.height; break;
                }
            }
        }

        void OnEnable() { EnsureRenderer(); if (tex == null) Regenerate(); }
        void OnValidate() { if (autoRefresh && isActiveAndEnabled) Regenerate(); }

        public void Regenerate()
        {
            if (settings == null) return;
            int size = Mathf.Clamp(textureSize, 16, 2048);
            var values = new NativeArray<float>(size * size, Allocator.TempJob);
            var heights = new NativeArray<float>(size * size, Allocator.TempJob);
            var lut = TerrainCurveLut.Bake(settings.terraceCurve, Allocator.TempJob);
            new SampleJob
            {
                size = size, centre = centre, extent = extent, stage = (int)stage,
                prm = TerrainParams.From(settings), lut = lut, values = values, heights = heights,
            }.Schedule(values.Length, 256).Complete();

            if (tex == null || tex.width != size)
            {
                if (tex != null) DestroyImmediate(tex);
                tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "TerrainMap", filterMode = FilterMode.Point };
            }
            var pixels = new Color32[values.Length];
            float mn = float.MaxValue, mx = float.MinValue;
            int land = 0;
            for (int i = 0; i < values.Length; i++)
            {
                float v = values[i];
                mn = math.min(mn, v); mx = math.max(mx, v);
                if (heights[i] > settings.seaLevel) land++;
                pixels[i] = Colour(v);
            }
            LastMin = mn; LastMax = mx; LastLandFraction = land / (float)values.Length;
            values.Dispose(); heights.Dispose(); lut.Dispose();
            tex.SetPixels32(pixels);
            tex.Apply(false);

            EnsureRenderer();
            mat.mainTexture = tex;
            transform.localScale = new Vector3(extent * 2f, extent * 2f, 1f);
            transform.position = new Vector3(centre.x, settings.seaLevel + 0.05f, centre.y);
            transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }

        Color32 Colour(float v)
        {
            switch (stage)
            {
                case TerrainMapStage.RawFbm:
                {
                    float g = math.saturate(v * 0.5f + 0.5f);
                    float k = tintAboveZero ? math.saturate(v * 2f) * 0.22f : 0f;
                    return new Color32((byte)(g * (1f - k) * 255f), (byte)(g * 255f), (byte)(g * (1f - k) * 255f), 255);
                }
                case TerrainMapStage.IslandMask:
                {
                    byte g = (byte)(math.saturate(v) * 255f);
                    return new Color32(g, g, g, 255);
                }
                default:
                    return HeightColour(v - settings.seaLevel);
            }
        }

        /// Hypsometric ramp: deep blue → light blue at 0 → sand → green → brown → white.
        Color32 HeightColour(float h)
        {
            Color c;
            if (h <= 0f)
            {
                float t = math.saturate(-h / math.max(1f, -settings.seabedDepth));
                c = Color.Lerp(new Color(0.55f, 0.8f, 0.95f), new Color(0.03f, 0.12f, 0.4f), t);
            }
            else
            {
                float t = math.saturate(h / math.max(1f, rampMaxHeight));
                Color sand = new Color(0.92f, 0.86f, 0.6f), grass = new Color(0.25f, 0.6f, 0.2f),
                      rock = new Color(0.45f, 0.35f, 0.25f), snow = Color.white;
                c = t < 0.1f ? Color.Lerp(sand, grass, t / 0.1f)
                  : t < 0.6f ? Color.Lerp(grass, rock, (t - 0.1f) / 0.5f)
                             : Color.Lerp(rock, snow, (t - 0.6f) / 0.4f);
            }
            return c;
        }

        public void ExportPng(string path)
        {
            if (tex == null) Regenerate();
            File.WriteAllBytes(path, ImageConversion.EncodeToPNG(tex));
        }

        void EnsureRenderer()
        {
            var mf = GetComponent<MeshFilter>();
            if (mf == null) mf = gameObject.AddComponent<MeshFilter>();
            var mr = GetComponent<MeshRenderer>();
            if (mr == null) mr = gameObject.AddComponent<MeshRenderer>();
            if (mf.sharedMesh == null)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                mf.sharedMesh = q.GetComponent<MeshFilter>().sharedMesh;
                DestroyImmediate(q);
            }
            if (mat == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
                mat = new Material(sh) { name = "TerrainMapPreview", hideFlags = HideFlags.DontSave };
            }
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
