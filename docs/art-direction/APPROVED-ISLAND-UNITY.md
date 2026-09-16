# Approved island integration

## Reference B refinement — 16 September

Connected west and northeast rock shoulders now extend to the water, interrupting the continuous sandy border. Selected lower and upper ledges pinch into the surrounding rock mass. The southern beach, flat foreland and central approach remain. Woodland targets are denser on the shoulders and rear slopes, with 171 individually authored trees before settlement clearance (127 in the tested runtime layout).

The authored terrain material now opts into three broad lighting bands with warm highlights, cool shadow tones and subtle variation within each band. Exported vertex alpha distinguishes rock from grass/sand; other island materials keep their existing lighting. This pass does not change ocean shading or add a separate underwater-rock kit.

The mountain retains 408 triangles and passed a check for nonpositive projected triangles (zero). Runtime checks passed for all 4,206 ground triangles: maximum collision/height error 0.000039 m, tree grounding error 0.000035 m, individual harvesting passed, dock berth seabed -12 m. The dock finder chose a new valid site on the same sheltered southern inlet. The current review images are `island-dressed-*.png` and `approved-island-unity-*.png`; older bare foreland and clay renders predate this refinement.

The home island now uses the approved dressed Blender study. Other islands retain their procedural terrain. `export_home_island.py`, executed through Blender MCP, exports exact terrain triangles, split normals, vertex colours and separate resource placements. An underwater continuation connects the authored beach to the seabed.

`HomePlateauSurface` renders the exported mesh and uses it for collision. Burst height queries use identical vertices with an 8 m spatial index. The streamed terrain stays underwater beneath this surface. `HomeIslandDressing` instantiates individual existing resource prefabs in the authored arrangement, respects settlement clearance, and registers trees with the existing harvesting and LOD systems. Runtime-owned grass/fern mesh copies match the study meadow palette and are released on destruction.

Unity compilation passed. In play mode, 4,206 triangle-centre rays agreed with the height lookup to 0.000046 m; 104 trees remained after settlement clearance, with maximum grounding error 0.000029 m. An individual tree received a harvest node and was removed without affecting its neighbour. The automatically placed dock has a berth over seabed at -12 m. Full report: `approved-island-unity-validation.txt`.

Overview, desktop 1920×1080 and portrait 1080×2340 camera renders were reviewed. These captures do not validate overlay HUD layout. The existing Unity toon lighting remains in use, so the lighting differs from the Blender preview. Comprehensive crew navigation across every upper shelf and device performance are not established by these checks. Play mode is restarted after the harvest test for handoff.
