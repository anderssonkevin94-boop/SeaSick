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

float RegionEnvelope(float2 p)
{
    float d = distance(p, _Ocean_Region.xy);
    float t = smoothstep(_Ocean_Region.z, _Ocean_Region.w, d);
    float env = lerp(_Ocean_RegionScale.x, _Ocean_RegionScale.y, t);
    int count = (int)_Ocean_RegionScale.w;
    for (int i = 0; i < count; i++)
    {
        float shore = distance(p, _Ocean_Islands[i].xy) - _Ocean_Islands[i].z;
        env *= smoothstep(0.0, _Ocean_RegionScale.z, shore);
    }
    float3 swd = ShoreWetDepth(p);
    env *= swd.x;
    // Depth limit: no wave taller than a fraction of the water under it. The
    // shoaling/breaking rule, which makes seabed clipping impossible at any
    // wave size instead of something to re-tune whenever the sea grows.
    float cap = 3.402823e38;
    if (_Ocean_DepthLimit.y > 0.01)
        cap = max(0.0, _Ocean_DepthLimit.x * swd.z / _Ocean_DepthLimit.y);
    // Never dead flat. Sheltered water still has chop; only land is glass.
    // The cap goes last so it beats the chop floor: in half a metre of water
    // there is no chop to have.
    return min(max(env, _Ocean_Shoal.z * swd.y), cap);
}

#endif
