# Trampled Dirt Roads L1 - first art review

Flat worn-earth ribbons with irregular shoulders and broad compacted patches.
No paving, curbs or raised road slabs. Separate hand-hewn torch holders with
rope bindings and charred fuel inserts. Staged only, not imported into Unity.

## Kit

- Road_Straight_4m_A/B/C: compatible variations, 96 triangles each.
- Road_Straight_2m: 48 triangles.
- Road_Curve_45 / Road_Curve_90: radius 3.6m, 96 triangles each.
- Road_T_Junction: 120 triangles; exact connector positions in the manifest.
- Road_End: tapered fading endpoint, 48 triangles.
- Torch_Post_A/B: 404 triangles each, no flames or lights.
- roads-lvl1.blend: selectable kit layout; hidden Review_Only_Not_Exported
  collection holds the demonstration road. FBXs contain only their named asset.
- module-contract.json: exact sockets, marker names, orientation and width.
- validation.json and PNGs: mesh checks, overview, close and context previews.

One unit = one metre. Source road start pivot at ground level, +X along road,
+Z up. FBX Y up / -Z forward. Road width including edge feather is 2.16m;
the main worn path is about 1.76m wide. Curve End heading is in the manifest.
Torch roots are ground pivots; Flame_Anchor is the external VFX reference.

## Important integration requirements

These intentionally OPEN meshes are surface templates, not collision slabs.
Project vertices onto the terrain and retain only a small surface offset
(authored 0.012m). Subdivide only when necessary to follow terrain curvature.
Snap endpoints exactly; don't stack overlapping ribbons at intersections.
The T-junction is a single connected surface. Curves may be mirrored through a
proper mesh conversion/recomputed normals, not by leaving inverted culling.

Col.rgb carries the earth palette; Col.a is a feather/coverage mask, NOT rock
classification. Do NOT use the existing terrain shader unmodified: it uses alpha
for other semantics. Implement a terrain paint mask, a suitable decal, or an
opaque terrain-colour blend using this coverage. Alpha-blended geometry is an
alternative requiring sorting/overdraw tests. The Blender previews use green
edge colours to demonstrate blending; those colours alone cannot match every
terrain biome. No Unity road shader or terrain projection code is supplied.

Start/end connector cross-sections match across the straight and curve modules.
Use Road_End at real path terminations. The kit does not yet include crossroads,
steep-slope stairs, bridges, elevation transitions or runtime spline generation.

Place torches sparingly (initial suggestion every 8-12m, alternating sides,
closer to useful junctions). Do not attach one automatically to every module.
Flames, smoke, light and flicker are EXTERNAL effects; no solid flame geometry.
Keep the number of active lights bounded on iPhone and use smooth range fade.
Roads do not imply movement bonuses, costs or pathfinding rules yet.

Rebuild source: tools/blender/roads_astra_lvl1.py. Road meshes pass zero-area
and no-more-than-two-faces-per-edge checks, with open boundary edges expected.
Torch meshes pass closed/manifold checks. Every mesh is flat shaded.
