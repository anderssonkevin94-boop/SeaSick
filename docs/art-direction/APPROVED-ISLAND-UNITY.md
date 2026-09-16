# Approved island integration

The home island now uses the approved dressed Blender study. Other islands retain their procedural terrain. `export_home_island.py`, executed through Blender MCP, exports exact terrain triangles, split normals, vertex colours and separate resource placements. An underwater continuation connects the authored beach to the seabed.

`HomePlateauSurface` renders the exported mesh and uses it for collision. Burst height queries use identical vertices with an 8 m spatial index. The streamed terrain stays underwater beneath this surface. `HomeIslandDressing` instantiates individual existing resource prefabs in the authored arrangement, respects settlement clearance, and registers trees with the existing harvesting and LOD systems. Runtime-owned grass/fern mesh copies match the study meadow palette and are released on destruction.

Unity compilation passed. In play mode, 4,206 triangle-centre rays agreed with the height lookup to 0.000046 m; 104 trees remained after settlement clearance, with maximum grounding error 0.000029 m. An individual tree received a harvest node and was removed without affecting its neighbour. The automatically placed dock has a berth over seabed at -12 m. Full report: `approved-island-unity-validation.txt`.

Overview, desktop 1920×1080 and portrait 1080×2340 camera renders were reviewed. These captures do not validate overlay HUD layout. The existing Unity toon lighting remains in use, so the lighting differs from the Blender preview. Comprehensive crew navigation across every upper shelf and device performance are not established by these checks. Play mode is restarted after the harvest test for handoff.
