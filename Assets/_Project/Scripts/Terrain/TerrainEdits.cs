using Unity.Mathematics;

namespace SeaSick.Terrain
{
    /// **Hand edits to the generated terrain, applied LAST in
    /// `TerrainHeight.Evaluate`** (2026-10-04). Up to four capsules, each a
    /// segment with a radius, a soft falloff and a target height; inside one
    /// the ground is pulled DOWN to the target (never up), so the only thing
    /// an edit can do is turn shallow sand into water. Water already deeper
    /// than the target is untouched by construction (exactly zero delta).
    ///
    /// Blittable and fixed-size (no arrays) so it rides inside
    /// `TerrainParams` into every Burst job, and every `TerrainParams.From`
    /// caller -- streamer chunks + colliders, shore grid, world populator,
    /// horizon ring, probes -- sees the same edited ground.
    ///
    /// **Never touches dry land.** A height gate fades the edit out between
    /// `GateLow` and `GateHigh` (0.3 -> 0.5 m above the sea), so ground the
    /// populator's island discovery counts as land (> 0.5 m) is bit-identical,
    /// and the capsules themselves are placed to fade to zero before any
    /// building, pier, pile bed, wall or road.
    ///
    /// The only edit so far: **Kevin's home-island sand spit** (seed 1337
    /// world, island centre (-732, 297)), the long, mostly submerged spit
    /// that closed the inlet at his pier into a 34 m canal his ship could not
    /// turn in. Kevin, 2026-10-04: "remove the spit on my island only".
    /// See docs/GDD.md (terrain edits) and `TerrainEditSelfTest`.
    public struct TerrainEdits
    {
        public const int Max = 4;
        /// Above this the edit starts to fade, and it is gone by `GateHigh`.
        public const float GateLow = 0.3f, GateHigh = 0.5f;

        public int count;
        /// Union of every capsule's reach (min x, min z, max x, max z): one
        /// compare and the hot path is out for the rest of the world.
        public float4 bounds;
        /// Segment (ax, az, bx, bz).
        public float4 seg0, seg1, seg2, seg3;
        /// (core radius, falloff width, target height above sea, unused).
        public float4 shape0, shape1, shape2, shape3;

        /// **One sandy point** (2026-10-04, Kevin: the arm must not end
        /// squared off at the dry dock). Where a capsule cuts across a beach
        /// it leaves a straight cliff; this instead lowers the ground toward a
        /// rounded point. In a frame at `tipAt` facing `tipDir` (along, lat),
        /// the target height falls with the elliptical distance
        /// d = knee * length(along / A, lat / B): gently above water
        /// (`top - s1 * d` down to the waterline at d = knee), then at `s2`
        /// like the natural flank, floored at the target. It only acts ahead
        /// of `tipAt` (fading in over `alongFade`), out to `alongMax` and
        /// within `latMax` either side, so nothing behind the point (Kevin's
        /// dry dock) or beside it (the island's own beaches) is touched.
        public int tipCount;
        public float2 tipAt, tipDir;
        /// (top, s1, knee, s2) and (A, B, alongFade, target).
        public float4 tipProfile, tipShape;
        /// (alongMax, alongMaxFade, latMax, latFade).
        public float4 tipReach;

        /// The world the spit edit belongs to: terrain seed and world offset
        /// of `Materials/GraphicArt/TerrainSettings.asset`. Any other world
        /// (another seed, a moved offset) gets no edits at all.
        public const int SpitSeed = 1337;
        /// (Consts, not static readonly float2s: this type is reached from
        /// Burst jobs and must not need a static constructor.)
        public const float SpitWorldOffsetX = -58.950436f, SpitWorldOffsetZ = 4818.805f;
        /// Kevin's home island centre (save `isleX`/`isleZ`).
        public const float SpitIslandX = -732f, SpitIslandZ = 297f;
        public static float2 SpitIsland => new float2(SpitIslandX, SpitIslandZ);
        /// Open-water depth the spit is lowered to. Deep enough that a hull
        /// (`HullIntegrity.groundingDraft` 0.3 m) clears it with 3.2 m to spare
        /// in any swell a lagoon sees, and shallower than every world-build
        /// threshold below the waterline (reef / sea monster skip at -4 m,
        /// anchorage at -12 m), so the seeded world build makes exactly the
        /// same decisions it did before the edit.
        public const float SpitTarget = -3.5f;

        /// The edits for the world `seed` + `worldOffset` describe.
        public static TerrainEdits For(int seed, float2 worldOffset)
        {
            var e = new TerrainEdits();
            if (seed != SpitSeed || math.any(math.abs(worldOffset - new float2(SpitWorldOffsetX, SpitWorldOffsetZ)) > 0.01f))
                return e;
            // Along the spit's crest, measured 2026-10-04 on a 2 m grid: root
            // stub left at x < -600 (it carries Kevin's dry dock's land end and
            // joins the island's dry south-east corner), then east along
            // z ~ 216-220 and up the hook to its awash fan at (-400, 252).
            // The beach keeps going to x ~ -606 and ends there as a rounded
            // sandy point (shaped and checked on a 1 m grid against the dry
            // dock's footprint + 2 m: zero delta); the first capsule starts
            // where the point's slope has already reached the target.
            e.Tip(new float2(-606f, 223f), new float2(0.986f, -0.164f),
                  new float4(0.3f, 0.03f, 10f, 0.35f), new float4(14f, 10f, 4f, SpitTarget),
                  new float4(50f, 8f, 20f, 6f));
            e.Add(new float2(-555f, 219f), new float2(-480f, 216f), 14f, 6f, SpitTarget);
            e.Add(new float2(-480f, 216f), new float2(-445f, 232f), 18f, 8f, SpitTarget);
            e.Add(new float2(-445f, 232f), new float2(-398f, 255f), 30f, 10f, SpitTarget);
            return e;
        }

        void Tip(float2 at, float2 dir, float4 profile, float4 shape, float4 reach)
        {
            tipCount = 1;
            tipAt = at; tipDir = math.normalize(dir);
            tipProfile = profile; tipShape = shape; tipReach = reach;
            // A generous box round the strip it can touch.
            float r = math.max(reach.x + reach.y, reach.z + reach.w);
            float4 bb = new float4(at - r, at + r);
            bounds = count == 0 ? bb : new float4(math.min(bounds.xy, bb.xy), math.max(bounds.zw, bb.zw));
            hasAny = 1;
        }

        /// 1 once anything (a capsule or the point) is set.
        public int hasAny;

        void Add(float2 a, float2 b, float radius, float falloff, float target)
        {
            if (count >= Max) return;
            var seg = new float4(a, b);
            var shape = new float4(radius, falloff, target, 0f);
            switch (count)
            {
                case 0: seg0 = seg; shape0 = shape; break;
                case 1: seg1 = seg; shape1 = shape; break;
                case 2: seg2 = seg; shape2 = shape; break;
                default: seg3 = seg; shape3 = shape; break;
            }
            float reach = radius + falloff;
            float4 bb = new float4(math.min(a, b) - reach, math.max(a, b) + reach);
            bounds = hasAny == 0 ? bb : new float4(math.min(bounds.xy, bb.xy), math.max(bounds.zw, bb.zw));
            count++;
            hasAny = 1;
        }

        /// Height `h` (absolute, sea level `seaLevel`) at `p` after the edits.
        public static float Apply(in TerrainEdits e, float2 p, float h, float seaLevel)
        {
            if (e.hasAny == 0 || p.x < e.bounds.x || p.y < e.bounds.y || p.x > e.bounds.z || p.y > e.bounds.w)
                return h;
            if (h - seaLevel >= GateHigh) return h;
            if (e.tipCount > 0) h = Point(e, p, h, seaLevel);
            if (e.count == 0) return h;
            float above = h - seaLevel;
            if (above >= GateHigh) return h;
            float gate = 1f - math.smoothstep(GateLow, GateHigh, above);
            h = One(e.seg0, e.shape0, p, h, seaLevel, gate);
            if (e.count > 1) h = One(e.seg1, e.shape1, p, h, seaLevel, gate);
            if (e.count > 2) h = One(e.seg2, e.shape2, p, h, seaLevel, gate);
            if (e.count > 3) h = One(e.seg3, e.shape3, p, h, seaLevel, gate);
            return h;
        }

        /// The sandy point: see `tipAt`.
        static float Point(in TerrainEdits e, float2 p, float h, float seaLevel)
        {
            float above = h - seaLevel;
            float2 r = p - e.tipAt;
            float along = math.dot(r, e.tipDir);
            if (along <= 0f) return h;
            float lat = e.tipDir.x * r.y - e.tipDir.y * r.x;
            float4 pr = e.tipProfile, sh = e.tipShape, re = e.tipReach;
            float w = math.smoothstep(0f, sh.z, along)
                    * (1f - math.smoothstep(re.x, re.x + re.y, along))
                    * (1f - math.smoothstep(re.z, re.z + re.w, math.abs(lat)));
            if (w <= 0f) return h;
            float d = pr.z * math.length(new float2(along / sh.x, lat / sh.y));
            float t = d < pr.z ? pr.x - pr.y * d : pr.x - pr.y * pr.z - pr.w * (d - pr.z);
            t = seaLevel + math.max(t, sh.w);
            if (h <= t) return h;
            float gate = 1f - math.smoothstep(GateLow, GateHigh, above);
            return h + (t - h) * w * gate;
        }

        /// Pull `h` toward `min(h, target)` by the capsule's weight. Where two
        /// capsules overlap the second only pulls what the first left above
        /// the target, so a joint is never deeper than the target.
        static float One(float4 seg, float4 shape, float2 p, float h, float seaLevel, float gate)
        {
            float target = seaLevel + shape.z;
            if (h <= target) return h;
            float2 a = seg.xy, ab = seg.zw - seg.xy;
            float t = math.saturate(math.dot(p - a, ab) / math.max(1e-6f, math.dot(ab, ab)));
            float d = math.length(p - a - t * ab);
            float w = 1f - math.smoothstep(shape.x, shape.x + shape.y, d);
            return h + (target - h) * w * gate;
        }
    }
}
