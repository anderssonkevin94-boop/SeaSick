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
        public TerrainMapStage stage = TerrainMapStage.RawFbm;
        [Tooltip("World XZ at the centre of the preview window.")]
        public Vector2 centre = Vector2.zero;
        [Tooltip("Half-width of the window in metres.")]
        public float extent = 2000f;
        public int textureSize = 512;
        [Tooltip("Tint texels above 0 (raw noise) / above sea level (heights) so the sign reads at a glance.")]
        public bool tintAboveZero = true;
        public bool autoRefresh = true;

        Texture2D tex;
        Material mat;
        public Texture2D Texture => tex;
        /// Min/max of the last generated map, for probes.
        public float LastMin { get; private set; }
        public float LastMax { get; private set; }

        [BurstCompile]
        struct SampleJob : IJobParallelFor
        {
            public int size;
            public float2 centre;
            public float extent;
            public int seed, octaves;
            public float baseFrequency, lacunarity, gain;
            public int stage;
            [WriteOnly] public NativeArray<float> values;

            public void Execute(int index)
            {
                int x = index % size, y = index / size;
                float2 uv = new float2((x + 0.5f) / size, (y + 0.5f) / size);
                float2 world = centre + (uv * 2f - 1f) * extent;
                // Step 1: only raw fBm exists. Later stages are added in step 2.
                values[index] = TerrainNoise.Fbm(world, seed, octaves, baseFrequency, lacunarity, gain);
            }
        }

        void OnEnable() { EnsureRenderer(); if (tex == null) Regenerate(); }
        void OnValidate() { if (autoRefresh && isActiveAndEnabled) Regenerate(); }

        public void Regenerate()
        {
            if (settings == null) return;
            int size = Mathf.Clamp(textureSize, 16, 2048);
            var values = new NativeArray<float>(size * size, Allocator.TempJob);
            new SampleJob
            {
                size = size, centre = centre, extent = extent,
                seed = settings.seed, octaves = settings.octaves,
                baseFrequency = settings.baseFrequency, lacunarity = settings.lacunarity, gain = settings.gain,
                stage = (int)stage, values = values,
            }.Schedule(values.Length, 256).Complete();

            if (tex == null || tex.width != size)
            {
                if (tex != null) DestroyImmediate(tex);
                tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "TerrainMap", filterMode = FilterMode.Point };
            }
            var pixels = new Color32[values.Length];
            float mn = float.MaxValue, mx = float.MinValue;
            for (int i = 0; i < values.Length; i++)
            {
                float v = values[i];
                mn = math.min(mn, v); mx = math.max(mx, v);
                float g = math.saturate(v * 0.5f + 0.5f);
                // Tint grows with the value above zero so there's no hard step at the contour.
                float k = tintAboveZero ? math.saturate(v * 2f) * 0.22f : 0f;
                pixels[i] = new Color32((byte)(g * (1f - k) * 255f), (byte)(g * 255f), (byte)(g * (1f - k) * 255f), 255);
            }
            LastMin = mn; LastMax = mx;
            values.Dispose();
            tex.SetPixels32(pixels);
            tex.Apply(false);

            EnsureRenderer();
            mat.mainTexture = tex;
            transform.localScale = new Vector3(extent * 2f, extent * 2f, 1f);
            transform.position = new Vector3(centre.x, settings.seaLevel + 0.05f, centre.y);
            transform.rotation = Quaternion.Euler(90f, 0f, 0f);
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
