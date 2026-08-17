using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.World
{
    /// Builds an organic island as a radial mesh: a noisy outline rather than a
    /// circle, a height profile that rises from beach to peak, and triangles
    /// split into sand / dirt / rock submeshes by height so the layers read as
    /// crisp low-poly bands.
    ///
    /// Some sectors are cliffs — no beach at all, the land drops straight into
    /// the water — which is what makes an approach worth thinking about.
    public static class IslandMeshBuilder
    {
        public const int Sectors = 30;
        const int Rings = 13;
        const float SkirtDepth = 14f;   // below water, so there's never a gap

        public struct Profile
        {
            public float radius;
            public float peakHeight;
            public IslandKind kind;
            public float[] outline;      // outline radius per sector
            public float[] beachFrac;    // 0 = sheer cliff, larger = wide beach
            public bool[] hasBeach;      // can the ship land on this bearing?
        }

        public enum IslandKind { SandOnly, SandAndDirt, Mountainous }

        public static Profile BuildProfile(float radius, IslandKind kind, int seed)
        {
            var rnd = new System.Random(seed);
            float ox = (float)rnd.NextDouble() * 100f;
            float oy = (float)rnd.NextDouble() * 100f;

            var outline = new float[Sectors];
            var beachFrac = new float[Sectors];
            var hasBeach = new bool[Sectors];

            // One or two stretches of cliff, at least one good landing beach.
            int cliffCount = kind == IslandKind.Mountainous ? 2 : (rnd.NextDouble() < 0.6 ? 1 : 0);
            var cliffStart = new int[cliffCount];
            var cliffLen = new int[cliffCount];
            for (int c = 0; c < cliffCount; c++)
            {
                cliffStart[c] = rnd.Next(0, Sectors);
                cliffLen[c] = rnd.Next(Sectors / 8, Sectors / 4);
            }

            for (int s = 0; s < Sectors; s++)
            {
                float ang = s / (float)Sectors * Mathf.PI * 2f;
                // Sample noise around a circle so the outline wraps seamlessly.
                float n = Mathf.PerlinNoise(ox + Mathf.Cos(ang) * 1.6f, oy + Mathf.Sin(ang) * 1.6f);
                float n2 = Mathf.PerlinNoise(ox * 0.5f + Mathf.Cos(ang) * 3.4f,
                                             oy * 0.5f + Mathf.Sin(ang) * 3.4f);
                outline[s] = radius * Mathf.Lerp(0.70f, 1.18f, n * 0.75f + n2 * 0.25f);

                bool cliff = false;
                for (int c = 0; c < cliffCount; c++)
                    for (int k = 0; k < cliffLen[c]; k++)
                        if ((cliffStart[c] + k) % Sectors == s) cliff = true;

                hasBeach[s] = !cliff;
                beachFrac[s] = cliff ? 0.03f : Mathf.Lerp(0.20f, 0.40f, n2);
            }

            // Guarantee at least a quarter of the island is landable.
            int beaches = 0;
            foreach (var b in hasBeach) if (b) beaches++;
            if (beaches < Sectors / 4)
                for (int s = 0; s < Sectors / 4; s++) { hasBeach[s] = true; beachFrac[s] = 0.28f; }

            return new Profile
            {
                radius = radius,
                peakHeight = kind switch
                {
                    IslandKind.SandOnly => radius * 0.07f,
                    IslandKind.SandAndDirt => radius * 0.22f,
                    _ => radius * 0.52f,
                },
                kind = kind,
                outline = outline,
                beachFrac = beachFrac,
                hasBeach = hasBeach,
            };
        }

        /// Height above water at sector s, at normalised distance t (0 centre,
        /// 1 outline). Beach slopes gently out of the sea; past it the land
        /// climbs, steeply where the beach is only a sliver (a cliff).
        public static float HeightAt(in Profile p, int s, float t)
        {
            float u = 1f - Mathf.Clamp01(t);          // 0 at edge, 1 at centre
            float beach = p.beachFrac[s % Sectors];

            if (u < beach)
                return Mathf.Lerp(-2.2f, 0.9f, u / Mathf.Max(0.001f, beach));

            float inland = (u - beach) / Mathf.Max(0.001f, 1f - beach);
            float shaped = p.kind == IslandKind.Mountainous
                ? Mathf.Pow(inland, 1.5f)
                : Mathf.SmoothStep(0f, 1f, inland);
            return Mathf.Lerp(0.9f, p.peakHeight, shaped);
        }

        public static Mesh Build(in Profile p, out float maxHeight)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var sand = new List<int>();
            var dirt = new List<int>();
            var rock = new List<int>();

            maxHeight = 0f;
            int n = Sectors;

            // Vertex grid: ring 0 is the centre, ring Rings is the shoreline,
            // plus one skirt ring dropped below the water.
            for (int r = 0; r <= Rings + 1; r++)
            {
                bool skirt = r == Rings + 1;
                float t = Mathf.Clamp01(r / (float)Rings);
                for (int s = 0; s < n; s++)
                {
                    float ang = s / (float)n * Mathf.PI * 2f;
                    float outR = p.outline[s];
                    float rad = t * outR;
                    float y = skirt ? -SkirtDepth : HeightAt(p, s, t);
                    if (skirt) rad = outR * 1.02f;
                    maxHeight = Mathf.Max(maxHeight, y);
                    verts.Add(new Vector3(Mathf.Sin(ang) * rad, y, Mathf.Cos(ang) * rad));
                    normals.Add(Vector3.up);
                }
            }

            float sandTop = 1.4f;
            float dirtTop = Mathf.Max(sandTop + 0.5f, p.peakHeight * 0.55f);

            for (int r = 0; r <= Rings; r++)
                for (int s = 0; s < n; s++)
                {
                    int s2 = (s + 1) % n;
                    int a = r * n + s;
                    int b = r * n + s2;
                    int c = (r + 1) * n + s;
                    int d = (r + 1) * n + s2;

                    float h = (verts[a].y + verts[b].y + verts[c].y + verts[d].y) * 0.25f;
                    var target = h < sandTop ? sand : (h < dirtTop ? dirt : rock);

                    // Wind outward so the surface faces up.
                    target.Add(a); target.Add(c); target.Add(b);
                    target.Add(b); target.Add(c); target.Add(d);
                }

            var mesh = new Mesh { name = "IslandMesh" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.subMeshCount = 3;
            mesh.SetTriangles(sand, 0);
            mesh.SetTriangles(dirt, 1);
            mesh.SetTriangles(rock, 2);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
