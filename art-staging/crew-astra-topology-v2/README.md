# Deckhand: connected topology rebuild

Rebuilt from explicit connected surfaces, not intersecting limb primitives,
boolean unions or bevels used to imitate a faceted style. V1 is preserved.

## Topology
- Shirt: one component. Each sleeve bridges a 2x2 torso armhole patch.
- Four intentional garment boundary loops: waist, neck and two cuffs.
- Trousers: one component, shared crotch edge, continuous pelvis and legs.
- Each forearm, wrist, palm and thumb forms one connected skin component.
- Elbow and knee loops carry explicit, normalized blend weights.
- Boots have an internal shaft and fitted opening around the trousers.
- Cap, hair and small facial details remain separate costume islands.

The complete figure has 1,668 triangles. Source garments retain quads.
Broad planes are defined by the mesh; smooth normals and selected sharp
edges control shading without adding artificial block-shaped attachments.

## Rig and export
Source: tools/blender/source/crew-astra-topology-v2.blend
Generator: tools/blender/crew_astra_topology_v2.py
Verifier: tools/blender/verify_crew_astra_topology_v2.py
Runtime FBX: deckhand-rigged.fbx

16-bone hand-weighted skeleton with linear blend skinning. The runtime FBX
contains two skinned meshes, CREW_Cloth and CREW_Skin, exported in rest pose.
The source retains named editable garment parts. The test poses are keyed
in Blender at frames 1 neutral, 21 working, 41 reach, 61 crouch and 81 stride.
They are deformation tests, not finished animations, and are not baked into
the FBX. There is no finger rig, IK control rig, or Unity prefab integration.

The existing CrewVertexColor shader can consume these vertex colors.
Set clothing _BaseColor to white and skin _BaseColor to sRGB #D99259.
Runtime skin colors are neutral white for tinting. Blender source skin is
warm-colored for review; the generator neutralizes it during export.

## Verification
validation.json records connectivity, intended garment boundary loops,
normalized weights and zero overconnected edges/degenerate faces.
All five poses have zero degenerate triangles. Triangle-intersection tests
find no shirt/forearm or trousers/boot intersections in those poses.
These are scoped checks, not proof against every possible pose or every
self-intersection. Extreme motion can still require weight adjustment.

Independent FBX reimport verifies two meshes, 16 bones, 1,668 triangles,
vertex colors and weights; rotating an imported forearm moves its mesh.
See export-verification.json. Renders use Blender Workbench; final Unity
lighting, animation retargeting and iOS performance remain untested.

Inspect topology-front.png to see the edge flow. Working, reaching, crouch
and stride views are included from front and rear, plus a small-scale render.
