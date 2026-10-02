// The ship's ambient, for the island (2026-10-02, Kevin: "i want the other
// assets to pop as much as they do. to have that rich beautiful color that
// we somehow created for the boat").
//
// SS_ShipAmbient is the ambient half of Ship/CoasterPaint.shader, verbatim:
// SH along the normal + a warm-ground/cool-sky hemisphere bounce that is full
// by day, eased in storms and gone at night, + a cool moon fill by night.
// Keep the numbers in step with CoasterPaint (and Crew/CrewPaint).
//
// _SS_RichLight (C#: SeaSick.Terrain.RichLight.amount, FeelLab knob) blends
// each island shader from its old ambient (0 = exactly the old look, which is
// also what an UNSET global gives) to this one (1). A float lerp, never a
// keyword: no extra variants on iOS.
//
// Included by World/EnvironmentToon, World/EnvironmentToonTextured,
// Terrain/TerrainVertexColor and World/WornRoad. ALU only -- no texture
// lookups, no passes.
#ifndef SEASICK_RICH_LIGHT_INCLUDED
#define SEASICK_RICH_LIGHT_INCLUDED

// SkyDirector globals (unset = 0 = broad clear daylight).
float _SS_Night;
float _SS_Storminess;
// RichLight.cs (unset = 0 = the old island look).
float _SS_RichLight;

float SS_Rich() { return saturate(_SS_RichLight); }

float3 SS_ShipAmbient(float3 n)
{
    float night = saturate(_SS_Night);
    float day = (1 - night) * lerp(1, .55, saturate(_SS_Storminess));
    float hemi = saturate(n.y * .5 + .5);
    float3 bounce = lerp(float3(.21,.18,.145), float3(.42,.46,.52), hemi) * day;
    float3 moon = float3(.025,.045,.08) * night * (.35 + .65 * hemi);
    return max(SampleSH(n), float3(.018,.024,.034)) + bounce + moon;
}

#endif
