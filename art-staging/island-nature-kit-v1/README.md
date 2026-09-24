# Untouched Island Nature Kit V1

Nine low-poly nature assets matching the approved flat-shaded forest and
middle-weight groundcover studies. Staged only: nothing imported into Unity.
Designed for the island BEFORE habitation. Do not create settlement clearings,
building buffers, pier approaches or worn human routes in the baseline scatter.

## Contents

| FBX | Triangles | Intended use |
| --- | ---: | --- |
| Cliff_LowLedge | 52 | Broad partly embedded rock at a cliff foot or exposed ridge |
| Cliff_BrokenSlab | 52 | Elongated fragment to bridge rock faces and ground |
| Cliff_TalusCluster | 108 | Three separate closed stones combined into one reusable mesh |
| Shore_WashedStones | 144 | Four small stones, decorative shoreline scatter |
| Coast_DuneGrass | 80 | Five solid folded blades; sparse dry/coastal grass |
| Coast_WindScrub | 68 | One continuous low wind-shaped shrub crown |
| Shore_ForkedDriftwood | 72 | Connected tapered fork, with a welded branch junction |
| Forest_Bracken | 200 | Five broad lobed fronds; use as an occasional understory accent |
| Forest_FallenTrunk | 52 | Tapered bent trunk with irregular pale broken ends |

The kit total is 828 triangles, counting one of each asset, excluding preview
ground and the existing hornbeam. This is not a scene budget or an iOS frame-rate
claim. Consult mesh-audit.json for authoritative measured counts and dimensions.

## Files

- island-nature-kit-v1.blend: opens on a composed nature vignette. Asset-library
  and lineup collections are hidden so no duplicate meshes obscure the preview.
  Enable the asset-library collection to inspect the ground-centred export sources.
- fbx/: nine individual triangulated mesh exports, one mesh/material slot each.
- nature-close.png, nature-gameplay.png: actual Blender renders, not generated concepts.
- kit-lineup.png: three rows, left to right in the order of the table above.
- mesh-audit.json: source counts, bounds, flat shading, manifold and area checks.
- export-verification.json: FBX re-import checks.
- Generator: ../../tools/blender/island_nature_kit_v1.py.

## Import Contract

Meters; source Z up, FBX -Z forward/Y up. Every source is at (0,0,0) with identity
rotation and unit scale. Origins are horizontal ground anchors; rock feet are
intentionally slightly below Z=0 for embedding. This is not a promise that each
asset's minimum vertex height is zero. Align the origin to sampled terrain, then
adjust embedding locally. Do not stretch stones vertically to fit arbitrary slopes.

All faces are flat-shaded. Import authored normals; do not smooth them. Colour
attribute `Col` is linear vertex colour; FBX exports it explicitly as LINEAR.
Use one shared opaque game material that consumes vertex COLOR. The Blender
material is a preview, not a Unity shader. Do not double-convert linear colours
or duplicate materials per instance. No textures, alpha cards or alpha blending.

The source meshes are closed and triangulated. Foliage has real thin thickness
and can be viewed from below without a double-sided shader. Multi-piece stone
clusters have separate closed components in one mesh; they are not a single solid.
No colliders, LODs, wind animation, resource logic or Unity prefabs are supplied.

## Natural Placement

Place by geology, slope, shore exposure and vegetation cover, independently of
the player's buildings. Embed ledges, group slabs/talus near rock faces, keep
washed stones/driftwood near plausible high-water lines, put dune grass above
wet sand, and use scrub on exposed margins. Bracken and fallen trunks belong in
sheltered woodland. Avoid arranging every piece into the repeated preview cluster.

Pair with the approved V4 forest family (V2 trees plus palms; exclude rejected
V3 additions) and the middle-weight groundcover. Use moss, dry grass and earth
patches as natural ground variation, not pre-existing roads. Building clearance
and trampling can be later gameplay-driven changes, separate from this baseline.

## Resource Readability And Mobile

These new pieces are decorative by default. Keep the approved gatherable boulders
and trees as distinct resource nodes. Do not grant yield to cliff meshes or small
pebbles just because they use the stone palette. If a fallen trunk becomes a
gatherable resource, wire that explicitly, including depleted state and save ID.

Reuse mesh assets and shared material; instance/batch where compatible. Cull
small grass, stones and bracken before larger silhouettes. Avoid grass colliders
and distant micro-prop shadows. Profile the real island on iPhone before choosing
scatter density; these files alone have not been tested on-device.

## Verification

Source meshes checked for closed manifold edges, zero-area faces, triangle counts
and flat shading. FBXs re-imported for triangle-count and Col attribute parity,
closed topology, flat shading, one material, metre-scale bounds and ground origins.
Preview terrain, lighting, camera and hornbeam are not exported in the nine FBXs.
Visual checks cover the close vignette, gameplay-distance view and isolated lineup.
