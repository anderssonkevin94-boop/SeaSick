# Sawmill Level 1

Open-sided timber saw shed with orange wooden shingles, braced framing,
stone pads, log rack, board stack and a hand-cranked circular saw bench.
The palette and faceted forms follow the supplied island-settlement reference.

Built within the existing BuildPlans.Sawmill footprint (7.56 x 5.85) and
ridge limit (3.84). Blender uses metres, Z up, front toward -Y.
Root sits at ground level. No Unity scene, prefab or build plan is changed.

## Files and Structure

- `sawmill-level-1.fbx`: six vertex-colored flat-shaded mesh modules.
- Blender source retains individually editable construction pieces.
- Roof is a separate export module for future visibility controls.
- Saw_Rotation and Crank_Rotation have local X rotation axes.
- Entry, Sawyer, Log_feeder and Output markers are provided for integration;
  their placement still needs to be tested against runtime navigation.
- No animation clips, colliders, LODs or automatic Unity material assignment.

The hand-powered machine is a stylized level-one design, not an engineering
simulation. Parts are authored for future animation; gameplay logic is not wired.
Roofing and timber joints use intentional physical overlaps.

Validation checks each source mesh for nonmanifold edges and zero-area faces,
the asset bounds against the existing footprint, and independent FBX reimport
for flat shading, color attributes, module count, markers and moving hierarchy.
No exhaustive animated-intersection or iOS performance test has been run.
Review images use Blender Workbench, not the Unity lighting pipeline.
