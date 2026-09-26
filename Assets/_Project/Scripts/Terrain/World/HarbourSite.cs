using UnityEngine;

namespace SeaSick.Terrain
{
    /// Where a dock goes, decided by the ground rather than by a bearing.
    ///
    /// The island already has a radial outline (`Island.RadiusAt`) and it is
    /// the wrong instrument for this: a radius measured from the centre stops
    /// at the first water it crosses, which is a fair description of a disc
    /// and a lie about a bay -- and a bay is precisely what a harbour wants.
    /// So this works off the height field directly, the way the buildable
    /// survey does.
    ///
    /// **One finder, called by both the populator and the probe.** A dock
    /// sited one way and measured another is the duplicated-gate failure this
    /// project keeps paying for; the editor tool exists to REPORT what the
    /// runtime will build, not to work it out again.
    ///
    /// A site has to satisfy four things at once, and any one of them alone
    /// picks somewhere useless:
    ///   - deep enough water within a short pier, or you are building a
    ///     causeway;
    ///   - a clear approach, or the boat grounds on a bar before she reaches
    ///     the head of it;
    ///   - land behind it you can stand on and build on, or the dock lands
    ///     you against a cliff;
    ///   - and shelter, which is what makes it a harbour instead of a jetty
    ///     on an exposed beach.
    public static class HarbourSite
    {
        public struct Site
        {
            public bool found;
            public Vector3 root;        // where the pier meets the land
            public Vector3 head;        // seaward end of the decking
            public Vector3 berth;       // where the ship lies alongside
            public Vector2 seaward;     // unit direction, land -> water
            public float pierLength;    // metres of decking
            public float berthDepth;    // water under her at the berth
            public float backSlope;     // how the land rises behind the root
            public float flatBehind;    // hectares of buildable ground within Hinterland
            public float shelter;       // 0 open beach .. 1 enclosed
            public float score;
        }

        /// What the boat needs under her. Her drawn keel is 1.02 m below the
        /// designed waterline and she sinks up to about 0.9 m more with a
        /// full hold, so 3 m is her loaded draft with a fair margin -- and
        /// the margin is the point, because the sea still moves here.
        public const float BerthDepth = 3.0f;
        public const float BerthWidth = 8.4f;

        /// She must not touch anything on the way in either. A berth in a
        /// pocket of deep water behind a bar is not a berth.
        public const float ApproachDepth = 2.5f;
        public const float ApproachRun = 160f;

        /// Longer than this and it is a causeway, not a pier.
        public const float MaxPier = 90f;

        /// How far back from the root the land has to be worth arriving at.
        public const float Hinterland = 90f;

        /// How far out to look for a berth, given an island's mean radius.
        ///
        /// It has to come from the ISLAND. A fixed radius was used first and
        /// it reached 700 m from a 151 m island, found a better shore on the
        /// NEIGHBOUR, and built the home dock over there -- and because the
        /// camera frames whichever island the dock belongs to, the overview
        /// then framed the wrong island too. One constant, two wrong answers,
        /// and both of them looked like plausible pictures.
        public static float SearchRadiusFor(float meanRadius)
            => Mathf.Clamp(meanRadius * 1.6f + 80f, 200f, 900f);

        /// Finds the best site around a centre. `height` is the world height
        /// function; sea level is zero.
        public static Site Find(Vector3 centre, float searchRadius,
            System.Func<float, float, float> height, System.Collections.Generic.List<Site> all = null)
        {
            var best = new Site { score = float.MinValue };
            float cell = 6f;
            int n = Mathf.Clamp(Mathf.CeilToInt(searchRadius * 2f / cell), 16, 400);
            float x0 = centre.x - searchRadius, z0 = centre.z - searchRadius;

            var h = new float[n * n];
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                    h[j * n + i] = height(x0 + i * cell, z0 + j * cell);

            for (int j = 1; j < n - 1; j++)
                for (int i = 1; i < n - 1; i++)
                {
                    int k = j * n + i;
                    if (h[k] <= 0.2f || h[k] > 6f) continue;      // a shore, not a hill

                    // The raster is a SQUARE and the search is a RADIUS, so
                    // without this the corners reach 1.41 x further than the
                    // radius allows -- which is exactly how a 322 m search
                    // from a 151 m island reached a neighbour 420 m away and
                    // built the home dock on it.
                    float ddx = x0 + i * cell - centre.x, ddz = z0 + j * cell - centre.z;
                    if (ddx * ddx + ddz * ddz > searchRadius * searchRadius) continue;
                    bool touchesWater = h[k - 1] <= 0f || h[k + 1] <= 0f
                                     || h[k - n] <= 0f || h[k + n] <= 0f;
                    if (!touchesWater) continue;

                    // Downhill is seaward. Taken from the raster so it is the
                    // local coast normal rather than a bearing from the centre,
                    // which on a lobed island points across the land.
                    float gx = (h[k + 1] - h[k - 1]) / (2f * cell);
                    float gz = (h[k + n] - h[k - n]) / (2f * cell);
                    var sea = new Vector2(-gx, -gz);
                    if (sea.sqrMagnitude < 1e-6f) continue;
                    sea.Normalize();

                    var s = Evaluate(new Vector3(x0 + i * cell, 0f, z0 + j * cell), sea, height);
                    if (!s.found) continue;
                    if (all != null) all.Add(s);
                    if (s.score > best.score) best = s;
                }

            return best;
        }

        /// Everything about one candidate. Split out so the probe can ask
        /// about a specific spot as well as about the best one.
        public static Site Evaluate(Vector3 root, Vector2 sea,
            System.Func<float, float, float> height)
        {
            var s = new Site { root = root, seaward = sea, score = float.MinValue };

            // How long the pier has to be, decided by the only criterion
            // that matters: SHE MUST FLOAT ALONG HER WHOLE LENGTH.
            //
            // Reaching deep water at the head is not the same thing and the
            // difference is the whole design. She is 24.2 m long, and on a
            // shore that goes from +0.3 m to -10.6 m in forty metres a pier
            // long enough to have deep water under its own head can still
            // leave one of her ends aground. This is exactly why a real pier
            // on a steep-to coast runs out past the shoal instead of
            // stopping at the first deep sounding.
            //
            // **T-BERTH (2026-09-26).** She no longer lies BESIDE this
            // point, her length running out to sea -- she lies ACROSS it,
            // beyond the head, her length running ALONG the shore and her
            // beam the dimension pointing out to sea. So the footprint
            // sampled below swaps which axis carries which of her numbers:
            // `t` (her length) now walks `alongAxis`, `b` (her beam) now
            // walks `sea`, and the candidate berth sits `d + BerthOffset`
            // out along `sea` -- past the head by half a beam and a fender,
            // not beside it.
            var alongAxis = new Vector2(-sea.y, sea.x);
            float pier = -1f;
            for (float d = 8f; d <= MaxPier; d += 2f)
            {
                float bx = root.x + sea.x * (d + BerthOffset);
                float bz = root.z + sea.y * (d + BerthOffset);
                bool floats = true;
                for (float t = -0.5f; t <= 0.5f; t += 0.1f)
                {
                    // A centreline sounding misses the shoreward corners.
                    for (float b = -.5f; b <= .5f; b += .25f)
                    {
                        float px = bx + alongAxis.x * t * SeaSick.World.WorldScale.ShipLength + sea.x * b * BerthWidth;
                        float pz = bz + alongAxis.y * t * SeaSick.World.WorldScale.ShipLength + sea.y * b * BerthWidth;
                        if (height(px, pz) > -BerthDepth) { floats = false; break; }
                    }
                    if (!floats) break;
                }
                if (floats) { pier = d; break; }
            }
            if (pier < 0f) return s;

            // The approach: nothing shallower than ApproachDepth from the head
            // out to sea, or she grounds before she gets there.
            for (float d = pier; d <= pier + ApproachRun; d += 4f)
                if (height(root.x + sea.x * d, root.z + sea.y * d) > -ApproachDepth) return s;

            // The land behind. A dock against a cliff lands the crew nowhere.
            float back = 0f;
            int backN = 0;
            for (float d = 10f; d <= Hinterland; d += 6f)
            {
                float a = height(root.x - sea.x * d, root.z - sea.y * d);
                float b = height(root.x - sea.x * (d + 6f), root.z - sea.y * (d + 6f));
                back += Mathf.Abs(b - a) / 6f;
                backN++;
            }
            s.backSlope = backN > 0 ? back / backN : 9f;
            if (s.backSlope > 0.36f) return s;                  // 20 deg, a hillside

            // Buildable ground within reach of the root -- this is where the
            // harbour settlement goes, and a dock with nowhere to put anything
            // behind it is just a place to stand.
            int flat = 0, tot = 0;
            for (float dz = -Hinterland; dz <= Hinterland; dz += 8f)
                for (float dx = -Hinterland; dx <= Hinterland; dx += 8f)
                {
                    if (dx * dx + dz * dz > Hinterland * Hinterland) continue;
                    float px = root.x + dx, pz = root.z + dz;
                    if (height(px, pz) <= 0.5f) continue;
                    tot++;
                    float sx = (height(px + 4f, pz) - height(px - 4f, pz)) / 8f;
                    float sy = (height(px, pz + 4f) - height(px, pz - 4f)) / 8f;
                    if (Mathf.Sqrt(sx * sx + sy * sy) < 0.176f) flat++;
                }
            s.flatBehind = flat * 64f / 10000f;

            // Shelter: how much of the seaward horizon is land. An open beach
            // sees nothing but water; a bay has arms around it. Sampled as a
            // half-disc so a spit two hundred metres away still counts.
            int arms = 0, arcN = 0;
            for (int a = -80; a <= 80; a += 8)
            {
                float rad = Mathf.Atan2(sea.x, sea.y) + a * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
                arcN++;
                for (float d = 60f; d <= 260f; d += 20f)
                    if (height(root.x + dir.x * d, root.z + dir.y * d) > 0.5f) { arms++; break; }
            }
            s.shelter = arcN > 0 ? arms / (float)arcN : 0f;

            s.found = true;
            s.pierLength = pier;
            s.head = new Vector3(root.x + sea.x * pier, 0f, root.z + sea.y * pier);
            s.root = new Vector3(root.x, height(root.x, root.z), root.z);

            // She lies ACROSS the head, beyond it by half her beam plus a
            // fender, not beside it -- so the depth that matters is the
            // depth where her hull actually sits, which can differ from the
            // head's own sounding by a metre on a shore this steep. Take the
            // shallowest reading along her whole 24 m length rather than one
            // at her midpoint, because she grounds by her ends -- her ends
            // now run along the shore, not out to sea.
            s.berth = new Vector3(s.head.x + sea.x * BerthOffset, 0f, s.head.z + sea.y * BerthOffset);
            float shallowest = 999f;
            for (float t = -0.5f; t <= 0.5f; t += 0.1f)
            {
                for (float b = -.5f; b <= .5f; b += .25f)
                {
                    float bx = s.berth.x + alongAxis.x * t * SeaSick.World.WorldScale.ShipLength + sea.x * b * BerthWidth;
                    float bz = s.berth.z + alongAxis.y * t * SeaSick.World.WorldScale.ShipLength + sea.y * b * BerthWidth;
                    shallowest = Mathf.Min(shallowest, -height(bx, bz));
                }
            }
            s.berthDepth = shallowest;   // the shallowest water under her, not the deepest

            // Short pier, flat land, room behind, some shelter. Weighted so no
            // single term can carry a site that fails the others: a beautiful
            // sheltered cove with a cliff behind it is not a harbour.
            //
            // Pier length is scored as a BAND, not as "shorter is better".
            // The first version rewarded the shortest pier and therefore
            // chose the steepest shore in the world -- deep water close in is
            // exactly what a steep-to coast gives you, and a steep-to coast
            // is a cliff with the harbour village on top of it. Anything from
            // a few metres to about sixty is a real pier; past that it is a
            // causeway and the cost starts to bite.
            float pierScore = pier <= 55f ? 1f : Mathf.Clamp01(1f - (pier - 55f) / (MaxPier - 55f));

            // A 90 m disc is 2.54 ha and a shore site has water over half of
            // it, so about 1.2 ha is as much flat hinterland as any coastal
            // spot can have. Normalising by 2 ha made every site look poor.
            //
            // Shelter carries the most of the four, and it was the least at
            // 1.4 until the authored home island made the reason obvious. The
            // other three terms DISCRIMINATE only where the ground varies:
            // on a massif, backSlope and flatBehind are what separate a beach
            // from a cliff foot, and shelter is a tiebreak. On flat ground
            // every shore has a gentle back slope and a hectare of flat
            // behind it, all four sites score the same on three terms out of
            // four, and the one thing that actually distinguishes a harbour
            // from a jetty on an open beach was the one term too small to
            // decide anything -- it put the home dock on the exposed north
            // beach of an island with a cove cut into its south side.
            s.score = pierScore * 1.2f
                    + Mathf.Clamp01(1f - s.backSlope / 0.36f) * 1.8f
                    + Mathf.Clamp01(s.flatBehind / 1.0f) * 2.4f
                    + s.shelter * 2.6f;
            return s;
        }

        /// How far beyond the head her centreline sits, seaward, at the
        /// default beam (there is no ship built yet at world-gen time to
        /// ask a real one). **T-berth (2026-09-26)**: she used to lie
        /// alongside the head, and this was the head's half-width plus her
        /// half-beam plus a fender -- a lateral offset. Now she lies across
        /// it, beyond the head rather than beside it, so the head's own
        /// width drops out and this is just:
        ///     4.22 (half her assumed 8.44 m beam)
        ///   + 0.9  (a fender, and the slop in a spring line)
        public const float BerthOffset = 5.12f;
    }
}
