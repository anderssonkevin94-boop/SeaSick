# Approved island integration

## Canopies, grounding and composed detail — 16 September

Broadleaf and scrub assets now use smooth, scalloped leaf masses with a continuous dark-underside/light-crown colour gradient. The four broadleaf silhouettes, three scrub shapes and individual resource identities remain. The Blender kit was regenerated through MCP, including its shared LOD library; the non-canopy builders retain their previous shapes.

Cliff shoulder heights now change in longer groups and their outward displacement is halved, reducing incidental diagonal wedges while retaining sharp larger facets. Ground cover is arranged as bush/fern/tuft pockets with occasional partly buried stones and sticks, instead of independent rolls around every tree. All three scrub variants participate. The dressing export contains 437 separate instances, including 171 trees before settlement clearance (127 in the tested game).

URP shadow distance was the principal cast-shadow limitation: both pipelines previously used 50 m. Desktop now uses 400 m, four cascades and a 4096 main-light atlas. Mobile uses 200 m, two cascades and a 2048 atlas with lower bias. The terrain/scenery shader now compiles soft-shadow variants. These settings increase shadow rendering cost; mobile hardware performance has not been measured. Water and sky assets, shading and animation were not edited.

The close-up `approved-island-unity-detail.png` and overview were visually reviewed: rounded canopy contours, cast canopy shadows, cliff shadows and grounded detail are visible. The revised mountain passed the projected-triangle check (zero nonpositive faces). Unity compilation, all 4,206 terrain collision/height samples, grounding of 127 trees, dock depth and individual harvesting checks passed. Play mode is reset after validation to restore the harvested test tree.

## Reference B refinement — 16 September

Connected west and northeast rock shoulders now extend to the water, interrupting the continuous sandy border. Selected lower and upper ledges pinch into the surrounding rock mass. The southern beach, flat foreland and central approach remain. Woodland targets are denser on the shoulders and rear slopes, with 171 individually authored trees before settlement clearance (127 in the tested runtime layout).

The authored terrain material now opts into three broad lighting bands with warm highlights, cool shadow tones and subtle variation within each band. Exported vertex alpha distinguishes rock from grass/sand; other island materials keep their existing lighting. This pass does not change ocean shading or add a separate underwater-rock kit.

The mountain retains 408 triangles and passed a check for nonpositive projected triangles (zero). Runtime checks passed for all 4,206 ground triangles: maximum collision/height error 0.000039 m, tree grounding error 0.000035 m, individual harvesting passed, dock berth seabed -12 m. The dock finder chose a new valid site on the same sheltered southern inlet. The current review images are `island-dressed-*.png` and `approved-island-unity-*.png`; older bare foreland and clay renders predate this refinement.

The home island now uses the approved dressed Blender study. Other islands retain their procedural terrain. `export_home_island.py`, executed through Blender MCP, exports exact terrain triangles, split normals, vertex colours and separate resource placements. An underwater continuation connects the authored beach to the seabed.

`HomePlateauSurface` renders the exported mesh and uses it for collision. Burst height queries use identical vertices with an 8 m spatial index. The streamed terrain stays underwater beneath this surface. `HomeIslandDressing` instantiates individual existing resource prefabs in the authored arrangement, respects settlement clearance, and registers trees with the existing harvesting and LOD systems. Runtime-owned grass/fern mesh copies match the study meadow palette and are released on destruction.

Unity compilation passed. In play mode, 4,206 triangle-centre rays agreed with the height lookup to 0.000046 m; 104 trees remained after settlement clearance, with maximum grounding error 0.000029 m. An individual tree received a harvest node and was removed without affecting its neighbour. The automatically placed dock has a berth over seabed at -12 m. Full report: `approved-island-unity-validation.txt`.

Overview, desktop 1920×1080 and portrait 1080×2340 camera renders were reviewed. These captures do not validate overlay HUD layout. The existing Unity toon lighting remains in use, so the lighting differs from the Blender preview. Comprehensive crew navigation across every upper shelf and device performance are not established by these checks. Play mode is restarted after the harvest test for handoff.
