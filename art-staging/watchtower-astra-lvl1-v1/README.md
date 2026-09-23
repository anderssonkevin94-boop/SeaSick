# Watchtower L1 - Astra V1

Staged art only. Not imported into Unity. First watchtower version.

## Deliverables

- `watchtower-state-kit.fbx`: preferred integration master, with both mutually exclusive flag variants. Initialize only one as visible.
- `watchtower-idle.fbx`: static rolled-flag version; appropriate when no alert display is implemented.
- `watchtower-alert.fbx`: static unfurled-flag version.
- Source: `../../tools/blender/source/watchtower-astra-lvl1-v1.blend`.
- PNGs include both front three-quarter views, front, platform detail, alert and gameplay-scale previews.
- `validation.json` and `export-verification.json`: source and FBX reimport checks.

## Scale And Rendering

One Blender unit is one meter. Ground-centered root, Blender Z-up, front/ladder on -Y. FBX -Z forward / Y up. Full asset fits the current 2.6 x 2.6 m plot and 7.5 m height allowance; roof peak 7.2 m. Exact bounds in validation.json. Platform walking surface is 4.61 m high.

One vertex-color material, `Col`, flat normals, no textures. Preserve hard normals and colors, use the game's vertex-color shader. Camera is excluded from exports.

## Modules

Footings, Tower_Frame, Joinery, Platform, Guardrails, Ladder, Roof_Frame and Canopy form the tower. Bell_Bracket, Signal_Bell, Bell_Rope and Flag_Pole are separate accessory modules. Signal_Bell includes the clapper; no independent clapper rig is supplied. Its geometry is hollow at the skirt, not a capped cylinder.

`Flag_Stowed` and `Flag_Alert` are alternative appearances of one signal flag. Never show both together. No cloth simulation, flag transition, bell animation, sound or alert system is supplied. These are optional visual states, not implemented gameplay.

## Access And Markers

The front guardrail has a 0.785 m clear opening. Ladder stile inner spacing is approximately 0.575 m. The external, near-vertical ladder arrives at the deck edge, not through a floor hatch. Its side rails extend above the platform for handholds. No railing crosses the entry opening.

- Ladder_Bottom: reference for climb start, not a ready NavMesh endpoint.
- Ladder_Top: reference for climb arrival near the platform edge.
- Lookout_Anchor: standing-position reference on the platform floor.
- Bell_Anchor: suspension reference for future ringing setup. The bell mesh origin is not automatically a swing pivot; reparent with world transforms preserved or rebase its origin first.

These markers do not validate crew width, hand reach, transitions, head clearance during climbing or ground approach. Actual character animation and collision tests remain required.

## Gameplay Integration Still Required

The current BuildPlan identifies Watchtower as BuildKind.Hut and describes its behavior as a guess. Do not assume the asset creates a functioning defensive building, lookout job, climb system, targeting or alert behavior.

Claude should create the prefab, replace any old procedural visual, configure conservative colliders and a platform navigation surface, implement an explicit climb link/animation, and validate the actual crew against the narrow ladder and deck. Do not let a generic walking NavMesh treat the ladder as a ramp. Map flag changes to real gameplay only when that system exists; otherwise keep Flag_Stowed visible. No storage or production slots are needed here.

Source validation covers plot bounds, roof/frame clearance, flat normals, manifold edges and zero-area faces. Reimport checks cover topology/colors, markers and flag variants. This is not structural engineering, in-engine performance or climb validation. Joined constructed components are not a single boolean-unioned solid. Cross braces occupy adjacent depths at their crossings.
