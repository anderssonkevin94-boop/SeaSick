using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace SeaSick.Ocean
{
    /// Blittable copy of the regional envelope for Burst jobs. The same
    /// formula exists in RegionField.hlsl; the two are held identical by the
    /// DivergenceProbe, which runs the HLSL side end-to-end against this one.
    public struct RegionFieldParams
    {
        public float2 home;
        public float calmRadius, wildRadius, nearScale, farScale, shoreFalloff;
        public int islandCount;
        // Shore grid: world-anchored terrain heights, bilinear, 1 outside the rect.
        public float2 shoreOrigin;
        public float shoreInvSize;   // 1 / (metres covered by the grid)
        public int shoreN;           // texels per edge, 0 = no grid
        public float shoalDepthFull; // depth (m) at and below which waves are untouched
        public float shoalDepthZero; // depth (m) at and above which waves are gone (shoreline)
        public float chopFloor;      // envelope never falls below this in water
        public float breakFraction;  // a wave may not exceed this x the water depth
        public float waveHs;         // the open-sea Hs the spectrum is producing, m
        // Drifting roughness patches (WeatherField). Tile is sampled with WRAP.
        public float weatherInvTile; // 1 / metres the tile covers
        public float2 weatherOffset; // drift, in tile units
        public int weatherN;         // texels per edge, 0 = no field
        public float3 patchLo;       // what the field's low end multiplies each cascade by
        public float3 patchHi;       // and its high end. patchHi.x MUST stay 1.
        // Shoreward band -- the swell the depth cap removed, turned toward the
        // beach. See ShorewardHeight. Strength 0 switches it off entirely.
        public float shorewardStrength;
        public float shorewardFalloff;  // exponent on the bite; pushes the band inshore
        public float shorewardOmega;    // rad/s of the shore swell
        public float shorewardSlope;    // nominal beach slope for the WKB phase
        public float shorewardTime;     // ocean seconds

        public static RegionFieldParams Neutral => new RegionFieldParams
        {
            home = float2.zero,
            calmRadius = 0f,
            wildRadius = 1f,
            nearScale = 1f,
            farScale = 1f,
            shoreFalloff = 1f,
            islandCount = 0,
            shoreN = 0,
            shoalDepthFull = 8f,
            shoalDepthZero = 0.5f,
            chopFloor = 0f,
            breakFraction = 0f,
            waveHs = 0f,
            weatherInvTile = 0f,
            weatherOffset = float2.zero,
            weatherN = 0,
            patchLo = new float3(1f, 1f, 1f),
            patchHi = new float3(1f, 1f, 1f),
            shorewardStrength = 0f,
            shorewardFalloff = 2f,
            shorewardOmega = 0f,
            shorewardSlope = 0.03f,
            shorewardTime = 0f,
        };

        /// islands: xy = centre, z = radius. shore: terrain heights on the
        /// shore grid (length shoreN*shoreN; ignored when shoreN == 0).
        /// MUST match RegionField.hlsl.
        /// How strongly each cascade's waves feel the sea bed.
        /// MUST match SS_BottomCoupling in RegionField.hlsl.
        ///
        /// SHOALING IS A WAVELENGTH EFFECT, and treating it as a single scalar
        /// was wrong. A wave feels the bottom when kd is small -- when its
        /// wavelength is comparable to the depth. In 10 m of water a 300 m
        /// swell is fully shallow-water, a 30 m wave feels it partly, and a
        /// 3 m ripple does not feel it at all. Scaling every cascade by one
        /// factor made inshore water a 300 m swell shrunk to a couple of
        /// metres: amp/L about 0.005, which is precisely the "flat sheet"
        /// failure this project already shipped once. The water near the
        /// islands was not too SMALL, it was too FLAT.
        ///
        /// Cascade 0 stays exactly 1.0, so everything already measured and
        /// gated against the long swell is untouched.
        ///
        /// Bands: c0 >= 64 m, c1 16-64 m, c2 0.5-16 m.
        public static float3 BottomCoupling => new float3(1f, 0.40f, 0.12f);

        /// How strongly each cascade feels an island's RADIAL falloff.
        /// MUST match SS_IslandCoupling in RegionField.hlsl.
        ///
        /// This term is not the depth terms and does not do their job. It is a
        /// disc -- a centre, a radius and 60 m of smoothstep -- that knows
        /// nothing about the seabed, the wavelength, or which side of the
        /// island you are on. On the generated world an island's `MaxRadius`
        /// is its outer bound and not its beach: measured on Island_1, the
        /// land stops 330 m from the centre and the disc goes on to 1130,
        /// so it was flattening an 800 m ANNULUS OF 180 m DEEP WATER to the
        /// chop floor. That is the pond, and it is 800 m out from any shore.
        ///
        /// Swell does not stop at a circle. It wraps round an island and rolls
        /// onto the beach; what dies in an island's lee is the WIND SEA, the
        /// short stuff, because a lee is a wind shadow. So the disc now reads
        /// as shelter from short waves: nothing on the swell, about half on
        /// the mid band, all of it on the chop.
        ///
        /// Cascade 0 losing this term costs no safety, because the terms that
        /// actually keep the swell off the seabed are the depth ones and they
        /// are untouched: measured at storm on the same island, the swell in
        /// 11.6 m of water is on the depth cap (0.098) and in 3.6 m it is on
        /// the cap again (0.030), with the island falloff nowhere near
        /// binding. What changes is only the deep water offshore.
        public static float3 IslandCoupling => new float3(0f, 0.55f, 1f);

        /// Per-cascade envelope. Distance from home is the only
        /// wavelength-independent term; the depth terms and the island falloff
        /// both differ between cascades, and in opposite directions -- shoaling
        /// is a LONG-wave effect and shelter is a SHORT-wave one.
        /// MUST match RegionEnvelopeCascades in RegionField.hlsl.
        ///
        /// NO PER-CASCADE LOOP, AND NO `[c]` ANYWHERE. This is not style. The
        /// three cascades used to be a `for` loop indexing float3s, and taking
        /// the address of a float3 to index it spills it to the stack and
        /// stops Burst vectorising the function around it. This runs UP TO
        /// EIGHT TIMES PER QUERY inside the sampler's Newton loop -- the
        /// sampler only keeps refreshing the envelope while the Newton point
        /// is still moving, so a converged point costs 2-3 calls and only a
        /// folding crest costs the full eight -- and measured on the shipped
        /// storm the loop form cost `SampleBatch` 0.38 -> 1.72 ms per 1000
        /// queries against a 0.4 ms budget -- a 4.5x blowout from nothing but
        /// the indexing. Written as three-wide arithmetic it is one pass of
        /// SIMD and comes in under the budget. If you add a term here, add it
        /// as a float3.
        public float3 EvaluateCascades(float2 p, NativeArray<float4> islands,
                                       NativeArray<float> shore, NativeArray<float> weather)
        {
            float d = math.distance(p, home);
            float t = math.smoothstep(calmRadius, wildRadius, d);
            float baseEnv = math.lerp(nearScale, farScale, t);
            // Kept out of baseEnv: each cascade feels it differently. See
            // IslandCoupling.
            float isle = 1f;
            for (int i = 0; i < islandCount; i++)
            {
                float s = math.distance(p, islands[i].xy) - islands[i].z;
                isle *= math.smoothstep(0f, shoreFalloff, s);
            }

            float shoal = 1f, wet = 1f, depth = 1e9f;
            if (shoreN > 0)
            {
                float3 swd = ShoreWetDepth(p, shore);
                shoal = swd.x; wet = swd.y; depth = swd.z;
            }
            // Never dead flat. Sheltered water still has chop; only land is
            // glass. Twin of the max() in RegionField.hlsl.
            float floorTerm = chopFloor * wet;

            // Drifting roughness patches. The FIELD LOOKUP does not depend on
            // the cascade; the RESPONSE to it does, and that is what makes a
            // patch a kind of water rather than a volume knob.
            //
            // A patch may go EITHER WAY about 1, which the reduce-only version
            // could not, at the price of one rule: patchHi.x is 1, so cascade 0
            // is never lifted. An envelope above 1 on the swell asks for waves
            // the depth limit and the seabed cannot hold, and it is the swell
            // that carries the height. The short cascades hold 0.65 m of RMS
            // between them against the swell's 17, so lifting THEM is texture
            // and not steepness.
            float3 patch = new float3(1f, 1f, 1f);
            if (weatherN > 0)
                patch = math.lerp(patchLo, patchHi, WeatherAt(p, weather));

            float3 bw = BottomCoupling;
            float3 env = baseEnv * math.lerp(new float3(1f), new float3(isle), IslandCoupling)
                                 * math.lerp(new float3(1f), new float3(shoal), bw)
                                 * patch;

            // Depth limit: no wave taller than a fraction of the water under
            // it -- the shoaling/breaking rule, which makes seabed clipping
            // structurally impossible at ANY wave size rather than something to
            // re-tune every time the sea grows. Capped against the height THIS
            // cascade carries, not the whole sea: 5 m of chop in 10 m of water
            // is not breaking just because a 70 m swell would be.
            //
            // The cap is applied last so it beats the chop floor: in half a
            // metre of water there is no chop to have.
            float3 cap = new float3(float.MaxValue);
            if (shoreN > 0 && waveHs > 0.01f)
                cap = math.max(0f, breakFraction * depth / (waveHs * bw));
            return math.min(math.max(env, new float3(floorTerm)), cap);
        }

        /// Cascade 0's envelope: the long swell, and what every gameplay reader
        /// means by "how big is the sea here". BottomCoupling(0) is exactly 1,
        /// so the depth terms reach it in full; IslandCoupling(0) is exactly 0,
        /// so the radial disc does not reach it at all.
        public float Evaluate(float2 p, NativeArray<float4> islands,
                              NativeArray<float> shore, NativeArray<float> weather)
            => EvaluateCascades(p, islands, shore, weather).x;

        /// Wrapped bilinear over the weather tile. MUST match WeatherAt in
        /// RegionField.hlsl, which is hardware Repeat filtering: texel centres
        /// at (i + 0.5)/N, and a POSITIVE modulo, because world positions west
        /// of home make these coordinates negative and C#'s % does not.
        public float WeatherAt(float2 p, NativeArray<float> weather)
        {
            if (weatherN <= 0 || weather.Length < weatherN * weatherN) return 1f;
            int n = weatherN;
            float2 uv = p * weatherInvTile + weatherOffset;
            float2 f = uv * n - 0.5f;
            int2 i0 = (int2)math.floor(f);
            float2 w = f - i0;
            int x0 = WrapIndex(i0.x, n), y0 = WrapIndex(i0.y, n);
            int x1 = WrapIndex(i0.x + 1, n), y1 = WrapIndex(i0.y + 1, n);
            float a = weather[y0 * n + x0], b = weather[y0 * n + x1];
            float c = weather[y1 * n + x0], d = weather[y1 * n + x1];
            return math.lerp(math.lerp(a, b, w.x), math.lerp(c, d, w.x), w.y);
        }

        static int WrapIndex(int a, int n) { int r = a % n; return r < 0 ? r + n : r; }

        /// Shoal factor, "is there water here at all", and the depth itself,
        /// from one lookup. The chop floor needs the second (the sea keeps
        /// texture in sheltered water but must be perfectly gone over land)
        /// and the depth limit needs the third. Outside the grid the sea is
        /// untouched and nominally bottomless.
        /// The swell the depth cap took, turned toward the beach.
        /// MUST match ShorewardHeight in RegionField.hlsl.
        ///
        /// The envelope can only ever SCALE a wave at a given position, so a
        /// crest running past an island gets a notch punched in it where the
        /// shelf is and sails on regardless -- "it cuts a large wave off as it
        /// passes by". Real water does the opposite: a crest over the shelf
        /// slows (c = sqrt(g d)), the rest of it does not, and the wave BENDS
        /// until its crests lie along the depth contours and it rolls onto the
        /// beach. Islands focus wave energy; ours ignores them.
        ///
        /// A scalar per-cascade envelope cannot rotate anything, so this does
        /// not try. It adds a separate shore swell whose crests ARE the depth
        /// contours -- which is what a fully refracted wave train looks like --
        /// and pays for it out of exactly what the cap removed.
        ///
        /// The phase is the WKB solution for a plane beach, and it is worth
        /// writing down because it is not a fudge. In shallow water
        /// k = omega / sqrt(g d); on a beach of slope s, depth d is s*x, so
        ///
        ///     phase = integral k dx = (2 omega / (s sqrt(g))) * sqrt(d)
        ///
        /// Crests are therefore level sets of depth (parallel to the contours,
        /// which is the refracted end state) and they run SHOREWARD, because a
        /// crest holds phase + omega*t constant and t only increases. It also
        /// gets the wavelength right for free: at Hs-scale defaults the band
        /// comes out at 98 m in 12 m of water against the shallow-water
        /// prediction T*sqrt(g d) = 97.6 m.
        ///
        /// KNOWN LIMIT: on a perfectly flat shelf depth carries no shoreward
        /// direction, so the phase goes constant and the band heaves as one
        /// piston instead of rolling. It wants bathymetry with a slope to it.
        ///
        /// Height only, deliberately: with no horizontal displacement it never
        /// enters the sampler's Newton inversion, whose budget is already spent
        /// at 0.29 of 0.4 ms and ten iterations.
        public float ShorewardHeight(float2 p, NativeArray<float> shore)
        {
            if (shorewardStrength <= 0f || shoreN <= 0 || waveHs <= 0.01f
                || breakFraction <= 0f) return 0f;
            float3 swd = ShoreWetDepth(p, shore);
            float depth = swd.z;
            // The depth at which the cap stops binding at all. Past it the cap
            // took nothing, so there is nothing for this to spend.
            float capDepth = waveHs / breakFraction;
            float bite = math.saturate(1f - depth / capDepth);
            if (bite <= 0f) return 0f;
            float amp = breakFraction * depth * math.pow(bite, shorewardFalloff)
                        * swd.y * shorewardStrength;
            float k = 2f * shorewardOmega / (shorewardSlope * 3.132092f); // sqrt(9.81)
            return amp * math.sin(k * math.sqrt(math.max(depth, 0f))
                                  + shorewardOmega * shorewardTime);
        }

        public float3 ShoreWetDepth(float2 p, NativeArray<float> shore)
        {
            float2 uv = (p - shoreOrigin) * shoreInvSize;
            if (uv.x < 0f || uv.y < 0f || uv.x > 1f || uv.y > 1f)
                return new float3(1f, 1f, 1e9f);
            float2 f = uv * shoreN - 0.5f;
            int2 i0 = math.clamp((int2)math.floor(f), 0, shoreN - 1);
            int2 i1 = math.min(i0 + 1, shoreN - 1);
            float2 w = math.saturate(f - i0);
            float a = shore[i0.y * shoreN + i0.x], b = shore[i0.y * shoreN + i1.x];
            float c = shore[i1.y * shoreN + i0.x], e = shore[i1.y * shoreN + i1.x];
            float h = math.lerp(math.lerp(a, b, w.x), math.lerp(c, e, w.x), w.y);
            return new float3(math.smoothstep(shoalDepthZero, shoalDepthFull, -h),
                              math.smoothstep(0f, 0.5f, -h), -h);
        }

        /// 1 in deep water, 0 at the shoreline and over land. Bilinear over the
        /// grid with clamped edges; outside the rect the sea is untouched.
        public float ShoreFactor(float2 p, NativeArray<float> shore)
        {
            float2 uv = (p - shoreOrigin) * shoreInvSize;
            if (uv.x < 0f || uv.y < 0f || uv.x > 1f || uv.y > 1f) return 1f;
            float2 f = uv * shoreN - 0.5f;
            int2 i0 = math.clamp((int2)math.floor(f), 0, shoreN - 1);
            int2 i1 = math.min(i0 + 1, shoreN - 1);
            float2 w = math.saturate(f - i0);
            float a = shore[i0.y * shoreN + i0.x], b = shore[i0.y * shoreN + i1.x];
            float c = shore[i1.y * shoreN + i0.x], e = shore[i1.y * shoreN + i1.x];
            float h = math.lerp(math.lerp(a, b, w.x), math.lerp(c, e, w.x), w.y);
            return math.smoothstep(shoalDepthZero, shoalDepthFull, -h);
        }
    }

    /// The regional sea: a calm shelf around home, wilder water further out,
    /// and waves dying against island shores. Applied as a displacement
    /// multiplier after the (spatially homogeneous) FFT — in the shader on
    /// the GPU and post-sample in the Burst jobs, from this single parameter
    /// source. The westward storm weight additionally drives which weather
    /// state SeaStateController targets (M6).
    public class RegionField : MonoBehaviour
    {
        /// MUST match the array length in RegionField.hlsl.
        ///
        /// It was 24 against a world of 34 islands, and on 2026-09-14 the world
        /// became 65 -- so 41 of them had NO shelter term at all, chosen by
        /// nothing better than flood-fill discovery order. Two thirds of the
        /// archipelago was open sea right up to the beach.
        ///
        /// Raising it alone would have been a perf regression: the island loop
        /// runs inside EvaluateCascades, which the sampler calls up to eight
        /// times per query inside its Newton loop, against a budget that is
        /// already spent. 65 islands there is 2.7x the work of 24. So the cap
        /// went up only as headroom, and the SELECTION became proximity-based
        /// -- see SelectIslands, which normally binds far fewer than 24.
        public const int MaxIslands = 48;

        // Reused by PublishNeutralIfAbsent, which runs every frame there is no
        // RegionField in the scene; an all-zero array, so sharing it is safe.
        static readonly Vector4[] neutralIslands = new Vector4[MaxIslands];

        public static RegionField Instance { get; private set; }

        [SerializeField] float calmRadius = 260f;
        [SerializeField] float wildRadius = 1150f;
        [SerializeField] float nearScale = 0.35f;
        [SerializeField] float farScale = 1.5f;
        [SerializeField] float shoreFalloff = 60f;

        [Header("Storm region (drives weather target, not the envelope)")]
        [SerializeField] Vector2 stormBearing = new Vector2(-1f, 0f);
        [SerializeField] float stormNear = 450f;
        [SerializeField] float stormFar = 1250f;

        [Tooltip("Bind home to VoyageManager.HomePoint and islands from the generated world. Probes that set their own geography disable this implicitly.")]
        [SerializeField] bool autoBindWorld = true;
        [Tooltip("Islands whose nearest water is beyond this from the view are left out of the shelter set. 1500 m: the term only touches cascades 1-2, and a 60 m shelter band around an island further off than this is sub-pixel at deck level on a portrait screen. Measured at the world's density it puts ~20 islands in range, so the cap never binds and the hot loop is SHORTER than the old hard 24.")]
        [SerializeField] float islandSelectRadius = 1500f;
        [Tooltip("Re-pick the island set when the view has moved this far, metres.")]
        [SerializeField] float islandRebindDistance = 250f;
        [Tooltip("Whose surroundings the island set is chosen around. Falls back to the main camera.")]
        [SerializeField] Transform islandFocus;
        Vector2 lastSelectAt;
        bool haveSelected;
        int islandsConsidered, islandsDropped;
        /// How many islands the world offered, how many were in range, and how
        /// many the cap threw away. `ReadIslands` prints these -- a cap that is
        /// silently binding is exactly the bug this replaced.
        public int IslandsConsidered => islandsConsidered;
        public int IslandsDropped => islandsDropped;
        public int IslandCount => islandCount;
        /// Read back off the live component, never off the source. A NEW
        /// [SerializeField] keeps whatever initialiser it was born with, and a
        /// later edit to that initialiser does not reach a component the editor
        /// already has loaded -- this project has lost days to that twice.
        public float IslandSelectRadius => islandSelectRadius;
        bool manualBound;
        bool worldBound;

        [Header("Shore grid (fed by the terrain)")]
        [Tooltip("Depth at and below which waves are untouched.")]
        [SerializeField] float shoalDepthFull = 8f;
        [Tooltip("Depth at and above which waves are gone — the shoreline.")]
        [SerializeField] float shoalDepthZero = 0.5f;
        [Tooltip("The envelope never falls below this in water, so sheltered anchorages keep some chop instead of turning to glass. Land is still perfectly flat.")]
        [Range(0f, 0.4f)] [SerializeField] float chopFloor = 0.12f;
        [Tooltip("A wave may not be taller than this fraction of the water under it. The real breaking index is about 0.78; below that leaves margin for the hull and the camera.")]
        [Range(0.1f, 0.8f)] [SerializeField] float breakFraction = 0.55f;

        [Header("Shoreward band")]
        /// Live A/B for the whole feature. Wired to the settings drawer; this
        /// is a look call and the look only exists in a full sea near land,
        /// which is a place you have to sail to.
        /// OFF BY DEFAULT AS OF 2026-09-17, and it must stay off until the
        /// phase field below is done properly.
        ///
        /// Kevin switched it on while sailing and it threw the ship into an
        /// island and flung her into the air. That is the aliasing limit
        /// already recorded below, showing its physical face: the band's real
        /// wavelength is T*sqrt(g*d) * (slopeAssumed / slopeActual), so on the
        /// STEEP flank of an island -- exactly where this feature is supposed
        /// to work -- the wavelength collapses and the band becomes
        /// high-frequency noise about two metres tall. The hull then sits on a
        /// surface with an enormous local slope and the buoyancy probes do
        /// what they are told. `BuoyantBody` will happily deliver
        /// maxBurialAccel 14 m/s^2 against that.
        ///
        /// Flipping the switch is ALSO a discontinuity in its own right: the
        /// water under her steps by up to the band's full amplitude in one
        /// frame. Any future version wants a ramp on the toggle as well as a
        /// bounded slope.
        public static bool ShorewardEnabled;
        [Tooltip("Height of the shore swell as a fraction of what the depth cap allows. The cap itself bounds it, so this is a fraction of a fraction: at 1 the band stands as tall as the breaking limit permits and nothing taller can exist in that depth anyway.")]
        [Range(0f, 1f)] [SerializeField] float shorewardStrength = 0.35f;
        [Tooltip("Exponent on the bite. Higher pushes the band inshore, where the cap is taking the most.")]
        [Range(0.5f, 6f)] [SerializeField] float shorewardFalloff = 2f;
        [Tooltip("Period of the shore swell, seconds. With the WKB phase this sets the wavelength honestly: lambda = T*sqrt(g*depth), so 9 s is about 98 m in 12 m of water.")]
        [Range(3f, 20f)] [SerializeField] float shorewardPeriod = 9f;
        [Tooltip("Nominal beach slope the WKB phase assumes. Smaller means longer crests and more of them stacked up the shelf. Not read off the terrain: the shore grid is 16 m per texel and its gradient is too coarse to trust.")]
        [Range(0.005f, 0.2f)] [SerializeField] float shorewardSlope = 0.03f;

        // Pulled from the renderer and the weather field each frame rather
        // than pushed, so the data flows one way and nothing has to remember
        // to call a setter.
        float waveHs;
        float weatherInvTile; float2 weatherOffset; int weatherN;
        float3 patchLo = new float3(1f, 1f, 1f), patchHi = new float3(1f, 1f, 1f);
        NativeArray<float> weather;    // borrowed from WeatherField, never owned
        Texture2D weatherTex;

        Vector2 home;
        readonly Vector4[] islandsGpu = new Vector4[MaxIslands];
        NativeArray<float4> islands;
        int islandCount;

        // _Ocean_Islands is bound once at world-bind and otherwise never
        // changes, but Publish() runs every frame -- these track what was
        // last actually uploaded so an unchanged 24-Vector4 array isn't
        // re-sent every frame. See Publish().
        readonly Vector4[] lastPublishedIslands = new Vector4[MaxIslands];
        int lastPublishedIslandCount = -1;

        struct IsleHit { public Vector2 pos; public float radius; public float gap; }
        // Reused every re-select; this runs while sailing and must not allocate.
        readonly System.Collections.Generic.List<IsleHit> scratch =
            new System.Collections.Generic.List<IsleHit>(128);

        // Shader.PropertyToID cache for Publish()'s per-frame globals. Every
        // Shader.SetGlobal* call below used to hash its string name each
        // frame; PropertyToID does that hash once here instead.
        static readonly int RegionId = Shader.PropertyToID("_Ocean_Region");
        static readonly int RegionScaleId = Shader.PropertyToID("_Ocean_RegionScale");
        static readonly int IslandsId = Shader.PropertyToID("_Ocean_Islands");
        static readonly int ShoreRectId = Shader.PropertyToID("_Ocean_ShoreRect");
        static readonly int ShoalId = Shader.PropertyToID("_Ocean_Shoal");
        static readonly int DepthLimitId = Shader.PropertyToID("_Ocean_DepthLimit");
        static readonly int ShorewardId = Shader.PropertyToID("_Ocean_Shoreward");
        static readonly int ShorewardTimeId = Shader.PropertyToID("_Ocean_ShorewardTime");
        static readonly int WeatherId = Shader.PropertyToID("_Ocean_Weather");
        static readonly int PatchLoId = Shader.PropertyToID("_Ocean_PatchLo");
        static readonly int PatchHiId = Shader.PropertyToID("_Ocean_PatchHi");
        static readonly int WeatherTexId = Shader.PropertyToID("_Ocean_WeatherTex");
        static readonly int ShoreTexId = Shader.PropertyToID("_Ocean_ShoreTex");

        NativeArray<float> shore;      // heights, shoreN² (a 1-element dummy when absent)
        Texture2D shoreTex;
        float2 shoreOrigin; float shoreSize; int shoreN;

        public NativeArray<float4> Islands => islands;
        public NativeArray<float> Shore => shore;
        public int ShoreN => shoreN;
        public NativeArray<float> Weather => weather;

        /// The terrain hands over a world-anchored grid of heights (length n*n,
        /// row-major, x fastest) covering [origin, origin + size). Copied; the
        /// caller keeps ownership of its array.
        public void SetShore(NativeArray<float> heights, float2 origin, float size, int n)
        {
            if (!shore.IsCreated || shore.Length != n * n)
            {
                if (shore.IsCreated) shore.Dispose();
                shore = new NativeArray<float>(n * n, Allocator.Persistent);
            }
            shore.CopyFrom(heights);
            shoreOrigin = origin; shoreSize = size; shoreN = n;
            if (shoreTex == null || shoreTex.width != n)
            {
                if (shoreTex != null) Destroy(shoreTex);
                shoreTex = new Texture2D(n, n, TextureFormat.RFloat, false, true)
                {
                    name = "OceanShore", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
                };
            }
            shoreTex.SetPixelData(heights, 0);
            shoreTex.Apply(false);
        }

        public void ClearShore() { shoreN = 0; }

        public RegionFieldParams Params => new RegionFieldParams
        {
            home = new float2(home.x, home.y),
            calmRadius = calmRadius,
            wildRadius = wildRadius,
            nearScale = nearScale,
            farScale = farScale,
            shoreFalloff = shoreFalloff,
            islandCount = islandCount,
            shoreOrigin = shoreOrigin,
            shoreInvSize = shoreSize > 0f ? 1f / shoreSize : 0f,
            shoreN = shoreN,
            shoalDepthFull = shoalDepthFull,
            shoalDepthZero = shoalDepthZero,
            chopFloor = chopFloor,
            breakFraction = breakFraction,
            waveHs = waveHs,
            weatherInvTile = weatherInvTile,
            weatherOffset = weatherOffset,
            weatherN = weatherN,
            patchLo = patchLo,
            patchHi = patchHi,
            shorewardStrength = ShorewardEnabled ? shorewardStrength : 0f,
            shorewardFalloff = shorewardFalloff,
            shorewardOmega = 2f * Mathf.PI / Mathf.Max(1f, shorewardPeriod),
            shorewardSlope = Mathf.Max(0.002f, shorewardSlope),
            shorewardTime = (float)OceanTime.Now,
        };

        void OnEnable()
        {
            Instance = this;
            islands = new NativeArray<float4>(MaxIslands, Allocator.Persistent);
            shore = new NativeArray<float>(1, Allocator.Persistent);
            shoreN = 0;
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
            if (islands.IsCreated) islands.Dispose();
            if (shore.IsCreated) shore.Dispose();
            if (shoreTex != null) Destroy(shoreTex);
        }

        public void SetHome(Vector2 xz)
        {
            home = xz;
            manualBound = true;
        }

        /// Runtime configuration (setup scripts and probes).
        public void SetProfile(float calmR, float wildR, float nearS, float farS, float shoreF)
        {
            calmRadius = calmR; wildRadius = wildR;
            nearScale = nearS; farScale = farS; shoreFalloff = shoreF;
        }

        public void ClearIslands() => islandCount = 0;

        public void AddIsland(Vector2 centre, float radius)
        {
            if (islandCount >= MaxIslands) return;
            islands[islandCount] = new float4(centre.x, centre.y, radius, 0f);
            islandsGpu[islandCount] = new Vector4(centre.x, centre.y, radius, 0f);
            islandCount++;
        }

        public float Evaluate(Vector2 p) =>
            Params.Evaluate(new float2(p.x, p.y), islands, shore, weather);

        /// 0 at/inside stormNear along the storm bearing, 1 beyond stormFar.
        /// Let the storm itself move, instead of being a fixed ramp westward.
        ///
        /// A/B switch. OFF is the shipped expression character for character.
        ///
        /// MEASURED, and this is why it exists. `VoyageVarietyProbe` sailed the
        /// same 3 km westward route eight times and the Hs profiles came back
        /// at a Pearson correlation of **0.9966** -- consecutive crossings were
        /// not similar, they were the SAME CROSSING with a scale factor on it.
        /// The reason was right here: `storm` had no time argument, so the
        /// ceiling at any point was identical on every voyage forever. And
        /// because `lull` carries a `(1 - stormSteady * storm)` factor with
        /// stormSteady at 0.88, the weather cells keep only 12% of their
        /// authority once `storm` reaches 1 -- so out west, which is where the
        /// game is actually played, the sea was very nearly a pure function of
        /// distance from home and nothing could move it.
        ///
        /// Making the cells evolve was tried first and measured: correlation
        /// 0.9966 -> 0.9869, which is nothing, because it was tuning a term the
        /// code had already almost switched off.
        ///
        /// ON. Measured over eight crossings of the same 3 km westward route:
        ///
        ///                       corr   rms diff   voyage spread   mean Hs
        ///   shipped            0.9966     19%          34%         22.4 m
        ///   evolving cells     0.9869     21%          28%         20.5 m
        ///   WANDERING STORM    0.9141     51%          75%         22.0 m
        ///   both               0.8988     51%          67%         20.4 m
        ///
        /// Both together is WORSE than the storm alone on the number that
        /// matters -- how much one crossing differs from the next in overall
        /// roughness -- and it drags the mean down with it. WeatherField.
        /// EvolvingCells stays off; do not re-walk it.
        public static bool WanderingStorm = true;

        [Tooltip("Degrees the storm's bearing wanders either side of the authored one. A day is 180 s in this game, so a 30 degree swing over ~20 minutes of play is a front turning over a couple of days -- slow weather, not a spinning compass.")]
        [Range(0f, 80f)] [SerializeField] float stormTurnRange = 30f;
        [SerializeField] float stormTurnPeriod = 1300f;
        [Tooltip("How far the storm FRONT advances and retreats along its bearing, as a log ratio: 0.9 swings the front between about 0.4x and 2.5x its authored distance. The ramp keeps its width and slides bodily, so a westward run meets the weather at a different range depending on when it sails.")]
        [Range(0f, 1.6f)] [SerializeField] float stormFrontLogRange = 0.9f;
        [SerializeField] float stormFrontPeriod = 640f;

        /// Two incommensurate sinusoids, mean zero, so the AVERAGE difficulty
        /// at a given distance is unchanged -- this alters when you meet the
        /// storm, not how often. Closed form in t with no accumulation, so the
        /// field stays a pure function of (position, OceanTime) and
        /// OceanTime.Scrub keeps every probe repeatable.
        static float Osc(double t, float period, float ratio)
        {
            float a = Mathf.Sin((float)(t / Mathf.Max(1f, period)) * 2f * Mathf.PI);
            float b = Mathf.Sin((float)(t / Mathf.Max(1f, period * (1f + ratio))) * 2f * Mathf.PI);
            return (a + 0.6f * b) / 1.6f;
        }

        /// Mean of cos(A * x) over a symmetric swing, to second order --
        /// enough at these angles (30 deg gives 0.966 against an exact 0.968)
        /// and it costs no trig at run time.
        static float MeanCos(float degrees)
        {
            float a = degrees * Mathf.Deg2Rad;
            return Mathf.Max(0.5f, 1f - 0.25f * a * a);
        }

        public float StormWeight(Vector2 p) => StormWeight(p, OceanTime.Now);

        /// C# only -- the GPU never sees this; it feeds the spectrum, which is
        /// global. Grep the shaders before assuming otherwise.
        public float StormWeight(Vector2 p, double t)
        {
            Vector2 bearing = stormBearing.normalized;
            float near = stormNear, far = stormFar;
            if (WanderingStorm)
            {
                float rad = stormTurnRange * Osc(t, stormTurnPeriod, 0.37f) * Mathf.Deg2Rad;
                float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
                bearing = new Vector2(bearing.x * c - bearing.y * s,
                                      bearing.x * s + bearing.y * c);
                // Slide the ramp bodily and keep its WIDTH: a front that also
                // got steeper or shallower would be two changes at once and
                // the probe could not tell which one moved the numbers.
                //
                // GEOMETRIC, not additive, and that is a correction to a
                // measured mistake. An additive swing has to be clamped so an
                // advancing front cannot cross home, and the clamp bit about a
                // third of the time -- always pushing the front OUTWARD, which
                // measured as mean Hs 22.4 -> 20.0, an 11% quieter game nobody
                // asked for. A log-symmetric swing cannot go negative, needs no
                // clamp, and has the authored distance as its geometric mean.
                // Log is also the right space here: every other gradient in
                // this weather system runs in log metres for the same reason.
                float width = Mathf.Max(50f, stormFar - stormNear);
                near = stormNear * Mathf.Exp(stormFrontLogRange * Osc(t, stormFrontPeriod, 0.61f));
                far = near + width;
            }
            float along = Vector2.Dot(p - home, bearing);
            // A rotation can only ever SHORTEN the along-bearing distance
            // (cos <= 1), so a wandering bearing quietly pushes every point
            // further from the storm and eases the whole game. Divide by the
            // mean cosine of the swing to put that back -- the wander then
            // changes WHERE the storm is without changing how much storm there
            // is on average.
            if (WanderingStorm) along /= MeanCos(stormTurnRange);
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(near, far, along));
        }

        void LateUpdate()
        {
            // How tall the open sea currently is, so the depth limit knows what
            // it is capping. Declared by the sea-state asset and checked
            // against the measured Hs by WaveSizeProbe.
            var ocean = OceanRenderer.Instance;
            waveHs = ocean != null && ocean.Settings != null ? ocean.Settings.nominalHs : 0f;

            // Drifting roughness patches, if there is a weather field in the
            // scene. Borrowed, never owned: WeatherField bakes the tile once
            // and this only carries it to the shader and the jobs.
            var wf = WeatherField.Instance;
            if (wf != null && wf.Tile.IsCreated)
            {
                weather = wf.Tile;
                weatherTex = wf.TileTexture;
                weatherN = wf.TileTexels;
                weatherInvTile = wf.InvTileMetres;
                weatherOffset = wf.PatchOffset;
                patchLo = wf.PatchRangeLo;
                patchHi = wf.PatchRangeHi;
            }
            else
            {
                weatherN = 0;
                patchLo = new float3(1f, 1f, 1f);
                patchHi = new float3(1f, 1f, 1f);
            }

            // The world is generated at runtime, so home and islands can only
            // be picked up once they exist. Probes that set their own geography
            // (SetHome/AddIsland) win and this never fires.
            if (autoBindWorld && !manualBound)
            {
                if (!worldBound)
                {
                    var voyage = FindFirstObjectByType<Voyage.VoyageManager>();
                    if (voyage != null && voyage.HomePoint != null && World.Island.All.Count > 0)
                    {
                        home = new Vector2(voyage.HomePoint.position.x, voyage.HomePoint.position.z);
                        worldBound = true;
                        SelectIslands(FocusXZ());
                    }
                }
                else
                {
                    // Re-pick as the view travels. Safe to change the SET while
                    // sailing because of the margin argument in SelectIslands:
                    // every island that is dropped was contributing exactly
                    // 1.0, so the envelope does not move when it goes.
                    Vector2 f = FocusXZ();
                    if (!haveSelected || Vector2.Distance(f, lastSelectAt) > islandRebindDistance)
                        SelectIslands(f);
                }
            }
            Publish();
        }

        Vector2 FocusXZ()
        {
            Transform t = islandFocus != null ? islandFocus
                        : (Camera.main != null ? Camera.main.transform : null);
            if (t == null) return home;
            var p = t.position;
            return new Vector2(p.x, p.z);
        }

        /// The nearest islands to the view, not the first ones the flood fill
        /// happened to label.
        ///
        /// WHY DROPPING THE REST IS FREE, AND NOT AN APPROXIMATION: an island's
        /// term is `smoothstep(0, shoreFalloff, distance - radius)`, which is
        /// exactly 1.0 -- no effect whatever -- once the query is more than
        /// `shoreFalloff` (60 m) beyond the island's radius. So an island far
        /// from everything being drawn multiplies the envelope by one, and
        /// leaving it out is a no-op rather than a simplification. That is what
        /// makes it safe to change the set WHILE SAILING without stepping the
        /// envelope, which would otherwise be the exact fault this ocean is
        /// already being investigated for.
        ///
        /// `islandSelectRadius` is measured from the VIEW and must therefore
        /// cover everything drawn, not just the water under the hull. 3 km is
        /// past the displacement fade, and the shelter term only touches
        /// cascades 1 and 2 -- short waves the clipmap cannot resolve at that
        /// range anyway.
        ///
        /// Sorted nearest-first so that if the cap ever does bind, what it
        /// throws away is the farthest and least consequential. It also
        /// normally binds FEWER than the old hard 24: at the world's island
        /// density only a dozen or so are ever in range, so the hot loop gets
        /// shorter as well as more correct.
        public void SelectIslands(Vector2 focus) => SelectIslands(focus, islandSelectRadius);

        /// `radius` override exists so a probe can A/B the hot loop's LENGTH
        /// without touching the serialized field -- which it could not reach
        /// anyway, see IslandSelectRadius.
        public void SelectIslands(Vector2 focus, float radius)
        {
            var isles = World.Island.All;
            islandsConsidered = isles.Count;
            islandsDropped = 0;
            scratch.Clear();
            for (int i = 0; i < isles.Count; i++)
            {
                var isle = isles[i];
                if (isle == null) continue;
                var p = isle.transform.position;
                float r = isle.MaxRadius;
                float gap = Vector2.Distance(new Vector2(p.x, p.z), focus) - r;
                if (gap > radius) continue;
                scratch.Add(new IsleHit { pos = new Vector2(p.x, p.z), radius = r, gap = gap });
            }
            scratch.Sort((a, b) => a.gap.CompareTo(b.gap));

            islandCount = 0;
            for (int i = 0; i < scratch.Count; i++)
            {
                if (islandCount >= MaxIslands)
                {
                    // Everything still in this list passed the range test, so
                    // anything the cap throws away is an island we had already
                    // judged close enough to matter. Counting only the ones
                    // within `shoreFalloff` of the FOCUS was the lenient
                    // version of this check and it reported a comfortable zero
                    // while the cap was in fact binding on 17 islands.
                    islandsDropped++;
                    continue;
                }
                var h = scratch[i];
                islands[islandCount] = new float4(h.pos.x, h.pos.y, h.radius, 0f);
                islandsGpu[islandCount] = new Vector4(h.pos.x, h.pos.y, h.radius, 0f);
                islandCount++;
            }
            for (int i = islandCount; i < MaxIslands; i++)
                islandsGpu[i] = Vector4.zero;

            if (islandsDropped > 0)
                Debug.LogWarning($"RegionField: island cap {MaxIslands} is binding — "
                    + $"{islandsDropped} island(s) close enough to shelter water were dropped.");

            lastSelectAt = focus;
            haveSelected = true;
        }

        public void Publish()
        {
            Shader.SetGlobalVector(RegionId,
                new Vector4(home.x, home.y, calmRadius, wildRadius));
            Shader.SetGlobalVector(RegionScaleId,
                new Vector4(nearScale, farScale, shoreFalloff, islandCount));
            // _Ocean_Islands never changes once the world is bound (AddIsland
            // only runs during the one-shot auto-bind or a probe's own setup),
            // so re-uploading all 24 Vector4s every frame is waste. Compare
            // against what was last actually sent and skip when nothing moved.
            bool islandsChanged = islandCount != lastPublishedIslandCount;
            if (!islandsChanged)
            {
                for (int i = 0; i < MaxIslands; i++)
                {
                    if (islandsGpu[i] != lastPublishedIslands[i]) { islandsChanged = true; break; }
                }
            }
            if (islandsChanged)
            {
                Shader.SetGlobalVectorArray(IslandsId, islandsGpu);
                System.Array.Copy(islandsGpu, lastPublishedIslands, MaxIslands);
                lastPublishedIslandCount = islandCount;
            }
            Shader.SetGlobalVector(ShoreRectId,
                new Vector4(shoreOrigin.x, shoreOrigin.y, shoreSize > 0f ? 1f / shoreSize : 0f, shoreN));
            Shader.SetGlobalVector(ShoalId,
                new Vector4(shoalDepthZero, shoalDepthFull, chopFloor, 0f));
            Shader.SetGlobalVector(DepthLimitId,
                new Vector4(breakFraction, waveHs, 0f, 0f));
            // Same four numbers the Burst twin reads off Params, pushed the
            // same frame -- a shoreward band the boat feels but you cannot see
            // (or the reverse) is the parity failure this contract exists for.
            Shader.SetGlobalVector(ShorewardId, new Vector4(
                ShorewardEnabled ? shorewardStrength : 0f,
                shorewardFalloff,
                2f * Mathf.PI / Mathf.Max(1f, shorewardPeriod),
                Mathf.Max(0.002f, shorewardSlope)));
            Shader.SetGlobalFloat(ShorewardTimeId, (float)OceanTime.Now);
            Shader.SetGlobalVector(WeatherId,
                new Vector4(weatherInvTile, weatherOffset.x, weatherOffset.y, weatherN));
            Shader.SetGlobalVector(PatchLoId,
                new Vector4(patchLo.x, patchLo.y, patchLo.z, 0f));
            Shader.SetGlobalVector(PatchHiId,
                new Vector4(patchHi.x, patchHi.y, patchHi.z, 0f));
            Shader.SetGlobalTexture(WeatherTexId,
                weatherTex != null ? (Texture)weatherTex : Texture2D.whiteTexture);
            // ALWAYS bind, even with no shore grid. A fragment shader tolerates
            // an unbound texture it never samples (the _Ocean_ShoreRect.w guard
            // sees to that), but a COMPUTE dispatch does not: Metal validates
            // every declared resource up front and silently skips the whole
            // dispatch, writing nothing. That is what killed DivergenceProbe --
            // its verify kernel returned zeros for a fortnight and the failure
            // read as an ocean parity drift. Costs one global set per frame.
            Shader.SetGlobalTexture(ShoreTexId,
                shoreTex != null ? (Texture)shoreTex : Texture2D.blackTexture);
        }

        /// When no RegionField exists (early lab scenes), the shader still
        /// needs sane globals: a neutral envelope of 1 everywhere.
        public static void PublishNeutralIfAbsent()
        {
            if (Instance != null) return;
            Shader.SetGlobalVector("_Ocean_Region", new Vector4(0f, 0f, 0f, 1f));
            Shader.SetGlobalVector("_Ocean_RegionScale", new Vector4(1f, 1f, 1f, 0f));
            Shader.SetGlobalVector("_Ocean_ShoreRect", Vector4.zero);
            // The rest of the contract, for the same compute-dispatch reason:
            // every resource RegionField.hlsl declares must be bound or a
            // kernel including it does nothing at all.
            Shader.SetGlobalVector("_Ocean_Shoal", Vector4.zero);
            Shader.SetGlobalVector("_Ocean_DepthLimit", Vector4.zero);
            Shader.SetGlobalVector("_Ocean_Shoreward", Vector4.zero);
            Shader.SetGlobalVector("_Ocean_Weather", Vector4.zero);
            Shader.SetGlobalVector("_Ocean_PatchLo", new Vector4(1f, 1f, 1f, 0f));
            Shader.SetGlobalVector("_Ocean_PatchHi", new Vector4(1f, 1f, 1f, 0f));
            Shader.SetGlobalTexture("_Ocean_WeatherTex", Texture2D.whiteTexture);
            Shader.SetGlobalVectorArray("_Ocean_Islands", neutralIslands);
            Shader.SetGlobalTexture("_Ocean_ShoreTex", Texture2D.blackTexture);
        }
    }
}
