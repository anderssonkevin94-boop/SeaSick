# Watchtower L1 - V2, Bare Platform

This version SUPERSEDES watchtower-astra-lvl1-v1 for first-stage use. User explicitly requested no roof, no railing and no diagonal cross-bracing. Only four upright timber posts, a plain plank platform, small stone footings and a ladder remain. The more elaborate V1 is retained as an earlier study, not the current L1.

## Files And Scale

- `watchtower-lvl1.fbx`: current integration source; one static state.
- Source: `../../tools/blender/source/watchtower-astra-lvl1-v2.blend`.
- PNGs: front, both front three-quarter views, gameplay scale.
- Validation and FBX reimport results are in the two JSON reports.

Meters; Blender Z-up, front/ladder -Y; FBX -Z forward / Y up. Ground-level centered root. Fits the existing 2.6 x 2.6 m plot. Walking surface remains 4.61 m high, but total height is now approximately 4.65 m rather than V1's 7.2 m. Any old 7.5 m height metadata is an allowance, not the new visual height; review it for selection/occlusion logic. Do not stretch the model to fill that allowance.

## Meshes And Rendering

Four meshes: Footings, Posts, Platform, Ladder. No canopy, roof supports, rails, balusters, cross-braces, bell, flag, decorative straps or alert variants. Short horizontal joists directly under the deck support the planks; there are no intermediate cross-members on the legs.

Flat normals, one `Col` vertex-color material, no textures. Preserve the flat shading and use the game's vertex-color shader. Source and reimport checks cover mesh contents, manifold edges, zero-area faces, colors and normals.

## Integration

Staged art only, not imported. Ladder_Bottom, Ladder_Top and Lookout_Anchor are reference empties, not implemented climbing or navigation. This deliberately simplified game asset is not a real-world structurally engineered or safe observation platform.

Claude still needs a prefab, conservative collision, explicit ladder animation/navigation transition, platform occupancy and edge behavior. There are intentionally no physical rails to contain the character: implement appropriate movement limits without visually adding rails. Verify crew dimensions and climbing poses in-engine. Do not assume the current Hut-kind Watchtower BuildPlan implements lookout or defense mechanics. Disable the old visual when replacing it. No gameplay code, animations, colliders or alert logic supplied.
