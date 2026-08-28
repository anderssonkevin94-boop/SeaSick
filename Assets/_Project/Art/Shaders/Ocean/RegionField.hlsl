// Regional envelope — GPU twin of RegionFieldParams.Evaluate in
// RegionField.cs. The two implementations MUST stay formula-identical; the
// DivergenceProbe exercises this one against the C# one end-to-end, so a
// drift between them fails the <5 cm gate. Edit both or neither.
#ifndef SEASICK_REGION_FIELD
#define SEASICK_REGION_FIELD

float4 _Ocean_Region;       // home.xy, calmRadius, wildRadius
float4 _Ocean_RegionScale;  // nearScale, farScale, shoreFalloff, islandCount
float4 _Ocean_Islands[24];  // xy = centre, z = radius
float4 _Ocean_ShoreRect;    // origin.xy, 1/size, texels per edge (0 = none)
float4 _Ocean_Shoal;        // depthZero, depthFull, chopFloor, unused
float4 _Ocean_DepthLimit;   // breakFraction, waveHs, unused, unused
Texture2D _Ocean_ShoreTex;  // terrain height, RFloat, clamp, bilinear
SamplerState sampler_Ocean_ShoreTex;
float4 _Ocean_Weather;      // 1/tileMetres, driftOffset.xy, texels per edge (0 = none)
float4 _Ocean_PatchLo;      // what the field's low end multiplies each cascade by
float4 _Ocean_PatchHi;      // and its high end. .x MUST stay 1 -- see the C# twin.
Texture2D _Ocean_WeatherTex;// roughness tile, RFloat, REPEAT, bilinear
SamplerState sampler_Ocean_WeatherTex;

// Drifting roughness patches. Twin of RegionFieldParams.WeatherAt, which is
// an explicit wrapped bilinear -- so this uses hardware Repeat filtering,
// which is the same arithmetic: texel centres at (i + 0.5)/N, wrap on the
// lattice index. Returns 1 (no patch) when there is no field.
float WeatherAt(float2 p)
{
    float result = 1.0;
    if (_Ocean_Weather.w >= 1.0)
    {
        float2 uv = p * _Ocean_Weather.x + _Ocean_Weather.yz;
        result = _Ocean_WeatherTex.SampleLevel(sampler_Ocean_WeatherTex, uv, 0).r;
    }
    return result;
}

// 1 in deep water, 0 at the shoreline and over land. Twin of
// RegionFieldParams.ShoreFactor: bilinear with clamped edges, 1 outside.
// Single-return rather than early-out: the early-out form makes Metal's
// compute codegen warn "potentially uninitialized variable" on every import.
// Identical arithmetic and identical branches -- the C# twin is unchanged.
float ShoreFactor(float2 p)
{
    float result = 1.0;
    if (_Ocean_ShoreRect.w >= 1.0)
    {
        float2 uv = (p - _Ocean_ShoreRect.xy) * _Ocean_ShoreRect.z;
        if (!any(uv < 0.0) && !any(uv > 1.0))
        {
            float h = _Ocean_ShoreTex.SampleLevel(sampler_Ocean_ShoreTex, uv, 0).r;
            result = smoothstep(_Ocean_Shoal.x, _Ocean_Shoal.y, -h);
        }
    }
    return result;
}

// Shoal factor, "is there water here at all", and the depth itself, from one
// sample. The chop floor needs the second (the sea must keep some texture in
// sheltered water but be perfectly gone over land) and the depth limit needs
// the third. Outside the grid the sea is untouched and nominally bottomless.
float3 ShoreWetDepth(float2 p)
{
    float3 result = float3(1.0, 1.0, 1e9);
    if (_Ocean_ShoreRect.w >= 1.0)
    {
        float2 uv = (p - _Ocean_ShoreRect.xy) * _Ocean_ShoreRect.z;
        if (!any(uv < 0.0) && !any(uv > 1.0))
        {
            float h = _Ocean_ShoreTex.SampleLevel(sampler_Ocean_ShoreTex, uv, 0).r;
            result = float3(smoothstep(_Ocean_Shoal.x, _Ocean_Shoal.y, -h),
                            smoothstep(0.0, 0.5, -h), -h);
        }
    }
    return result;
}

// How strongly each cascade's waves feel the sea bed.
//
// SHOALING IS A WAVELENGTH EFFECT, and treating it as a single scalar was
// wrong. A wave feels the bottom when kd is small, i.e. when its wavelength is
// comparable to the depth: in 10 m of water a 300 m swell is fully
// shallow-water (tanh kd -> kd), a 30 m wave feels it partly, and a 3 m ripple
// does not feel it at all. The old envelope scaled every cascade by the same
// factor, so inshore water became a 300 m swell shrunk to a couple of metres --
// amp/L about 0.005, which is the exact "flat sheet" failure this project
// already shipped once. It was not too SMALL near the islands, it was too FLAT.
//
// Cascade 0 is unchanged at 1.0, so the physics that has been measured and
// gated does not move. Cascades 1 and 2 keep most of their amplitude in
// shallow water, which is what leaves chop behind when the swell dies -- what
// real shorelines do.
//
// Bands: c0 >= 64 m, c1 16-64 m, c2 0.5-16 m.
static const float3 SS_BottomCoupling = float3(1.0, 0.40, 0.12);

// How strongly each cascade feels an island's RADIAL falloff. Twin of
// RegionFieldParams.IslandCoupling.
//
// This term is not the depth terms and does not do their job. It is a disc --
// a centre, a radius and 60 m of smoothstep -- that knows nothing about the
// seabed, the wavelength, or which side of the island you are on. On the
// generated world an island's MaxRadius is its outer bound and not its beach:
// measured on Island_1, the land stops 330 m from the centre and the disc goes
// on to 1130, so it was flattening an 800 m ANNULUS OF 180 m DEEP WATER to the
// chop floor. That is the pond, and it is 800 m out from any shore.
//
// Swell does not stop at a circle. It wraps round an island and rolls onto the
// beach; what dies in an island's lee is the WIND SEA, because a lee is a wind
// shadow. So the disc now reads as shelter from short waves: nothing on the
// swell, about half on the mid band, all of it on the chop. Cascade 0 losing
// it costs no safety -- the terms that keep the swell off the seabed are the
// depth ones, and they are untouched.
static const float3 SS_IslandCoupling = float3(0.0, 0.55, 1.0);

// Per-cascade envelope. Distance from home is the only wavelength-independent
// term; the depth terms and the island falloff both differ between cascades,
// and in opposite directions -- shoaling is a LONG-wave effect and shelter is
// a SHORT-wave one.
//
// Three-wide arithmetic and no `[c]` anywhere, matching the C# twin line for
// line. There it is load-bearing rather than tidy: indexing a float3 in the
// Burst job spills it to the stack and stops the function vectorising, and
// this runs eight times per query inside the sampler's Newton loop -- measured
// on the shipped storm, the per-cascade loop form cost SampleBatch 0.38 ->
// 1.72 ms per 1000 queries against a 0.4 ms budget.
float3 RegionEnvelopeCascades(float2 p)
{
    float d = distance(p, _Ocean_Region.xy);
    float t = smoothstep(_Ocean_Region.z, _Ocean_Region.w, d);
    float baseEnv = lerp(_Ocean_RegionScale.x, _Ocean_RegionScale.y, t);
    int count = (int)_Ocean_RegionScale.w;
    // Kept out of baseEnv: each cascade feels it differently. See
    // SS_IslandCoupling.
    float isle = 1.0;
    for (int i = 0; i < count; i++)
    {
        float shore = distance(p, _Ocean_Islands[i].xy) - _Ocean_Islands[i].z;
        isle *= smoothstep(0.0, _Ocean_RegionScale.z, shore);
    }
    float3 swd = ShoreWetDepth(p);
    // Never dead flat. Sheltered water still has chop; only land is glass.
    float floorTerm = _Ocean_Shoal.z * swd.y;

    // Drifting roughness patches. The FIELD LOOKUP does not depend on the
    // cascade; the RESPONSE to it does, and that is what makes a patch a kind
    // of water rather than a volume knob. A patch may go EITHER WAY about 1 at
    // the price of one rule: _Ocean_PatchHi.x is 1, so cascade 0 is never
    // lifted, because it is the swell that carries the height and an envelope
    // above 1 on it asks for waves the seabed cannot hold.
    float3 patch = float3(1.0, 1.0, 1.0);
    if (_Ocean_Weather.w >= 1.0)
        patch = lerp(_Ocean_PatchLo.xyz, _Ocean_PatchHi.xyz, WeatherAt(p));

    float3 bw = SS_BottomCoupling;
    float3 env = baseEnv * lerp(1.0, isle, SS_IslandCoupling)
                         * lerp(1.0, swd.x, bw)
                         * patch;

    // Depth limit: no wave taller than a fraction of the water under it.
    // Capped against the height THIS cascade carries, not the whole sea --
    // 5 m of chop in 10 m of water is not breaking just because a 70 m swell
    // would be. The cap goes last so it beats the chop floor: in half a metre
    // of water there is no chop to have.
    float3 cap = 3.402823e38;
    if (_Ocean_DepthLimit.y > 0.01)
        cap = max(0.0, _Ocean_DepthLimit.x * swd.z / (_Ocean_DepthLimit.y * bw));
    return min(max(env, floorTerm), cap);
}

// Cascade 0's envelope: the long swell, and what every gameplay reader means
// by "how big is the sea here". SS_BottomCoupling.x is exactly 1.0, so the
// depth terms reach it in full -- lerp(1, swd.x, 1) == swd.x and the cap
// divides by Hs * 1 -- and SS_IslandCoupling.x is exactly 0.0, so the radial
// disc does not reach it at all.
float RegionEnvelope(float2 p)
{
    return RegionEnvelopeCascades(p).x;
}

#endif
