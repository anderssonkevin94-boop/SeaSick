using System.Collections.Generic;
using UnityEngine;

namespace SeaSick.Terrain
{
    /// The ground a settlement will actually stand on: the largest CONTIGUOUS
    /// piece of buildable land on an island.
    ///
    /// This is the same measurement `TuneIslands.Flats` makes across the
    /// world, moved here so the runtime can ask it too -- the camera has to
    /// frame the settlement, and framing it from a different calculation than
    /// the one that sites it is how you end up looking at the wrong place
    /// with great confidence.
    ///
    /// Contiguity is the whole point. A percentage of buildable land says
    /// nothing about whether it is one field or a thousand patches, and a
    /// village needs the field.
    public static class SettlementSite
    {
        public struct Site
        {
            public bool found;
            public Vector3 centre;      // centroid of the patch
            public float extent;        // furthest patch cell from that centre
            public float core;          // radius holding most of it -- what to frame
            public float areaHa;
            public float inscribed;     // radius of the largest circle inside it
            public Vector3 inscribedAt;
            public float villageClearing;   // the same, but where `preferNear` asked for it
            public Vector3 villageAt;
        }

        /// Under ten degrees. Same threshold the world survey uses.
        public const float BuildableSlope = 0.176f;

        /// Below this an in-frame clearing is not worth having -- a
        /// storehouse is 8 m by 5 m, so anything under about two of them
        /// side by side is a yard, not a village.
        public const float MinVillageClearing = 12f;

        /// `minHeight` keeps the village off the beach. Sand is FLAT, so it
        /// passes a slope test with room to spare and drags the settlement's
        /// centre down onto the foreshore -- which is both the wrong place to
        /// build and the wrong place to point a camera. Pass sandHeight and
        /// the patch becomes ground you would actually put a longhouse on.
        /// `preferNear` / `preferRadius` ask a second question of the same
        /// patch: where is the best clearing WITHIN REACH OF A GIVEN POINT.
        ///
        /// The largest circle on the island and the place a village belongs
        /// are not the same spot. Home's biggest inscribed circle is 130 m
        /// from the head of its pier, which is outside the frame the docked
        /// camera holds -- so a village sited there is a village the player
        /// never sees, in a game whose whole homecoming is that one shot.
        /// Pass the camera's frame and the village is sited into it.
        public static Site Find(Vector3 islandCentre, float searchRadius,
            System.Func<float, float, float> height, float cell = 4f, float minHeight = 0.5f,
            Vector3 preferNear = default, float preferRadius = 0f)
        {
            var s = new Site();
            int n = Mathf.Clamp(Mathf.CeilToInt(searchRadius * 2f / cell), 8, 500);
            float x0 = islandCentre.x - searchRadius, z0 = islandCentre.z - searchRadius;

            var h = new float[n * n];
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                    h[j * n + i] = height(x0 + i * cell, z0 + j * cell);

            // This island only: flood the land from the centre, so a
            // neighbour inside the same square is not mistaken for more of
            // the same island.
            // Land for CONNECTIVITY is anything dry -- the island is one
            // piece across its beaches, and flooding only the high ground
            // would split it into unrelated summits.
            var land = new bool[n * n];
            for (int k = 0; k < land.Length; k++) land[k] = h[k] > 0.5f;
            int ci = Mathf.Clamp(Mathf.RoundToInt((islandCentre.x - x0) / cell), 0, n - 1);
            int cj = Mathf.Clamp(Mathf.RoundToInt((islandCentre.z - z0) / cell), 0, n - 1);
            int seed = NearestLand(land, n, ci, cj);
            if (seed < 0) return s;
            var mine = Flood(land, n, n, seed);

            var build = new bool[n * n];
            for (int j = 1; j < n - 1; j++)
                for (int i = 1; i < n - 1; i++)
                {
                    int k = j * n + i;
                    if (!mine[k] || h[k] < minHeight) continue;
                    float dx = (h[k + 1] - h[k - 1]) / (2f * cell);
                    float dz = (h[k + n] - h[k - n]) / (2f * cell);
                    if (Mathf.Sqrt(dx * dx + dz * dz) < BuildableSlope) build[k] = true;
                }

            var id = Label(build, n, n);
            var count = new Dictionary<int, int>();
            int best = 0, bestId = 0;
            foreach (var v in id)
            {
                if (v == 0) continue;
                count.TryGetValue(v, out int c);
                count[v] = c + 1;
                if (c + 1 > best) { best = c + 1; bestId = v; }
            }
            if (bestId == 0 || best < 8) return s;

            var inside = new bool[n * n];
            double sx = 0, sz = 0;
            for (int k = 0; k < id.Length; k++)
            {
                if (id[k] != bestId) continue;
                inside[k] = true;
                sx += x0 + (k % n) * cell;
                sz += z0 + (k / n) * cell;
            }
            s.centre = new Vector3((float)(sx / best), 0f, (float)(sz / best));

            // Two radii, and the difference between them matters.
            //
            // EXTENT is the furthest cell, and on real terrain it is
            // dominated by tendrils: this island's patch is 1.6 ha with an
            // extent of 207 m, which is not a field, it is a ribbon of flat
            // ground following a contour with branches off it. Framing a
            // camera on that backs it off far enough to cover the tendrils
            // and leaves the part anyone would build on tiny in the middle.
            //
            // CORE is the radius holding four cells in five, which is the
            // part that behaves like a place.
            var dists = new List<float>();
            for (int k = 0; k < inside.Length; k++)
            {
                if (!inside[k]) continue;
                float px = x0 + (k % n) * cell, pz = z0 + (k / n) * cell;
                dists.Add(Vector2.Distance(new Vector2(px, pz), new Vector2(s.centre.x, s.centre.z)));
            }
            dists.Sort();
            s.extent = dists[dists.Count - 1];
            s.core = dists[Mathf.Clamp(dists.Count * 4 / 5, 0, dists.Count - 1)];
            s.areaHa = best * cell * cell / 10000f;

            var d2 = Edt(inside, n, n);
            float bd = -1f; int bk = 0;
            for (int k = 0; k < d2.Length; k++)
                if (inside[k] && d2[k] > bd) { bd = d2[k]; bk = k; }
            s.inscribed = Mathf.Sqrt(Mathf.Max(0f, bd)) * cell;
            s.inscribedAt = new Vector3(x0 + (bk % n) * cell, 0f, z0 + (bk / n) * cell);

            // Same clearance field, asked about a smaller area. Falls back to
            // the island's best if nothing in reach is big enough to stand a
            // village in -- a village you cannot see beats no village at all.
            s.villageClearing = s.inscribed;
            s.villageAt = s.inscribedAt;
            if (preferRadius > 0f)
            {
                float pr2 = preferRadius * preferRadius;
                float pd = -1f; int pk = -1;
                for (int k = 0; k < d2.Length; k++)
                {
                    if (!inside[k]) continue;
                    float px = x0 + (k % n) * cell, pz = z0 + (k / n) * cell;
                    float dx = px - preferNear.x, dz = pz - preferNear.z;
                    if (dx * dx + dz * dz > pr2) continue;
                    if (d2[k] > pd) { pd = d2[k]; pk = k; }
                }
                float r = pk >= 0 ? Mathf.Sqrt(Mathf.Max(0f, pd)) * cell : 0f;
                if (r >= MinVillageClearing)
                {
                    s.villageClearing = r;
                    s.villageAt = new Vector3(x0 + (pk % n) * cell, 0f, z0 + (pk / n) * cell);
                }
            }
            s.centre.y = height(s.centre.x, s.centre.z);
            s.found = true;
            return s;
        }

        static int NearestLand(bool[] land, int n, int ci, int cj)
        {
            for (int r = 0; r < n; r++)
                for (int j = cj - r; j <= cj + r; j++)
                    for (int i = ci - r; i <= ci + r; i++)
                    {
                        if (Mathf.Max(Mathf.Abs(i - ci), Mathf.Abs(j - cj)) != r) continue;
                        if (i < 0 || j < 0 || i >= n || j >= n) continue;
                        if (land[j * n + i]) return j * n + i;
                    }
            return -1;
        }

        static bool[] Flood(bool[] m, int w, int h, int seed)
        {
            var got = new bool[w * h];
            var stack = new Stack<int>();
            got[seed] = true; stack.Push(seed);
            while (stack.Count > 0)
            {
                int k = stack.Pop();
                int i = k % w, j = k / w;
                if (i > 0 && m[k - 1] && !got[k - 1]) { got[k - 1] = true; stack.Push(k - 1); }
                if (i < w - 1 && m[k + 1] && !got[k + 1]) { got[k + 1] = true; stack.Push(k + 1); }
                if (j > 0 && m[k - w] && !got[k - w]) { got[k - w] = true; stack.Push(k - w); }
                if (j < h - 1 && m[k + w] && !got[k + w]) { got[k + w] = true; stack.Push(k + w); }
            }
            return got;
        }

        static int[] Label(bool[] m, int w, int h)
        {
            var id = new int[w * h];
            var stack = new Stack<int>();
            int next = 0;
            for (int start = 0; start < m.Length; start++)
            {
                if (!m[start] || id[start] != 0) continue;
                next++;
                stack.Push(start); id[start] = next;
                while (stack.Count > 0)
                {
                    int k = stack.Pop();
                    int i = k % w, j = k / w;
                    if (i > 0 && m[k - 1] && id[k - 1] == 0) { id[k - 1] = next; stack.Push(k - 1); }
                    if (i < w - 1 && m[k + 1] && id[k + 1] == 0) { id[k + 1] = next; stack.Push(k + 1); }
                    if (j > 0 && m[k - w] && id[k - w] == 0) { id[k - w] = next; stack.Push(k - w); }
                    if (j < h - 1 && m[k + w] && id[k + w] == 0) { id[k + w] = next; stack.Push(k + w); }
                }
            }
            return id;
        }

        /// Exact squared Euclidean distance to the nearest cell outside the
        /// mask (Felzenszwalb & Huttenlocher). Exact rather than a chamfer,
        /// because the answer is a building size and a few per cent is a wall.
        static float[] Edt(bool[] inside, int w, int h)
        {
            const float INF = 1e20f;
            var f = new float[w * h];
            for (int k = 0; k < f.Length; k++) f[k] = inside[k] ? INF : 0f;
            int n = Mathf.Max(w, h);
            var d = new float[n]; var v = new int[n]; var z = new float[n + 1]; var col = new float[n];
            for (int j = 0; j < h; j++)
            {
                for (int i = 0; i < w; i++) col[i] = f[j * w + i];
                Env1D(col, d, v, z, w);
                for (int i = 0; i < w; i++) f[j * w + i] = d[i];
            }
            for (int i = 0; i < w; i++)
            {
                for (int j = 0; j < h; j++) col[j] = f[j * w + i];
                Env1D(col, d, v, z, h);
                for (int j = 0; j < h; j++) f[j * w + i] = d[j];
            }
            return f;
        }

        static void Env1D(float[] f, float[] d, int[] v, float[] z, int n)
        {
            const float INF = 1e20f;
            int k = 0; v[0] = 0; z[0] = -INF; z[1] = INF;
            for (int q = 1; q < n; q++)
            {
                float s;
                while (true)
                {
                    s = ((f[q] + q * q) - (f[v[k]] + v[k] * (float)v[k])) / (2f * q - 2f * v[k]);
                    if (s <= z[k] && k > 0) k--; else break;
                }
                k++; v[k] = q; z[k] = s; z[k + 1] = INF;
            }
            k = 0;
            for (int q = 0; q < n; q++)
            {
                while (z[k + 1] < q) k++;
                float dq = q - v[k];
                d[q] = dq * dq + f[v[k]];
            }
        }
    }
}
