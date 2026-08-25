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
Texture2D _Ocean_ShoreTex;  // terrain height, RFloat, clamp, bilinear
SamplerState sampler_Ocean_ShoreTex;

// 1 in deep water, 0 at the shoreline and over land. Twin of
// RegionFieldParams.ShoreFactor: bilinear with clamped edges, 1 outside.
float ShoreFactor(float2 p)
{
    if (_Ocean_ShoreRect.w < 1.0) return 1.0;
    float2 uv = (p - _Ocean_ShoreRect.xy) * _Ocean_ShoreRect.z;
    if (any(uv < 0.0) || any(uv > 1.0)) return 1.0;
    float h = _Ocean_ShoreTex.SampleLevel(sampler_Ocean_ShoreTex, uv, 0).r;
    return smoothstep(_Ocean_Shoal.x, _Ocean_Shoal.y, -h);
}

// Shoal factor AND "is there water here at all", from one sample. The chop
// floor needs the second: the sea must keep some texture in sheltered water,
// but must still be perfectly gone over land.
float2 ShoreAndWet(float2 p)
{
    if (_Ocean_ShoreRect.w < 1.0) return float2(1.0, 1.0);
    float2 uv = (p - _Ocean_ShoreRect.xy) * _Ocean_ShoreRect.z;
    if (any(uv < 0.0) || any(uv > 1.0)) return float2(1.0, 1.0);
    float h = _Ocean_ShoreTex.SampleLevel(sampler_Ocean_ShoreTex, uv, 0).r;
    return float2(smoothstep(_Ocean_Shoal.x, _Ocean_Shoal.y, -h),
                  smoothstep(0.0, 0.5, -h));
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
    float2 sw = ShoreAndWet(p);
    env *= sw.x;
    // Never dead flat. Sheltered water still has chop; only land is glass.
    return max(env, _Ocean_Shoal.z * sw.y);
}

#endif
