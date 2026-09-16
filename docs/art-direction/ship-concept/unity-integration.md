# Adventure brig integration

The approved V4 Blender design replaces Shipyard node 12 only. Source: `tools/blender/source/adventure-brig-design-v4.blend`. Export through Blender MCP using `tools/blender/export_adventure_brig.py`; output is `Assets/_Project/Resources/Ships/AdventureBrig.json`.

The export consolidates artwork into 19 mesh groups (62,574 triangles), scales the hull to the existing 26 m by 7.8 m brig, and preserves vertex material colors. Shipyard retains existing physics, load, upgrades and buoyancy. Four square sail assemblies use the existing SailRig; the jib remains static. The proposed new sail simulation is deferred.

Crew heights are sampled from the approved deck. Functional cannons use hull-specific sockets, and 14 separate lids connect to PortLids. Gunport framing is an inset visual treatment rather than a cut-through interior.

Validation: Unity compilation passed; in-play check confirmed the approved visual, 14 buoyancy probes, four sail assemblies and 14 lids. A temporary battery fitting produced one functional gun per side and was restored afterward. Reviewed `brig-unity.png` after correcting original deck/hull face winding. Existing default fitting has no batteries or crew berths. Play mode is left running for user evaluation. Full sailing balance and performance profiling remain user-test follow-ups.
