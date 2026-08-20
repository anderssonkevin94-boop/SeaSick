// Regional envelope — GPU twin of RegionFieldParams.Evaluate in
// RegionField.cs. The two implementations MUST stay formula-identical; the
// DivergenceProbe exercises this one against the C# one end-to-end, so a
// drift between them fails the <5 cm gate. Edit both or neither.
#ifndef SEASICK_REGION_FIELD
#define SEASICK_REGION_FIELD

float4 _Ocean_Region;       // home.xy, calmRadius, wildRadius
float4 _Ocean_RegionScale;  // nearScale, farScale, shoreFalloff, islandCount
float4 _Ocean_Islands[24];  // xy = centre, z = radius

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
    return env;
}

#endif
