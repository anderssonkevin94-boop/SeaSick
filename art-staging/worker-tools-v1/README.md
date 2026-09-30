# Worker tools V1 — approved 2026-09-30, awaiting integration

Seven models for six tool roles: Axe, Hammer, Saw, Hoe, StirPaddle, SpearStone and SpearIron. No runtime files changed. Approved by Kevin and queued in ASTRAS_READY_ASSETS.md.

Source: worker-tools.blend; generator: tools/blender/worker_tools_v1.py. Import individual named FBXs; source blend contains a review layout, not game placement. Each export is a single vertex-colored mesh with one material (GameColor), no textures. Flat normals, warm wood, dark iron with broad light edges. Dimensions and geometry checks in manifest.json.

## Existing animation contract inspected

VillagerActing.BuildTool/PoseTool: tool origin is fist centre, +Y up the handle, +Z working face, X swing axis; props parented to body at unit scale, NOT to scaled deckhand bones. Grip and working points preserved in the authored tool frame:

- Hammer: Y .30 / Z .10 m.
- Axe: Y .56 / Z .13 m; second hand .11m above grip.
- Saw: Y .33 / Z .055 m, teeth facing work; open wooden handle behind blade.
- Hoe: Y 1.02 / Z .14 m; second hand .42m above grip.
- Stir paddle: head at Y .66m, existing procedural stirring orientation retained.
- HunterProps: shaft extends .65m below and 1.15m above grip, stone and iron spear variants retained; head adds length above shaft.

Source coordinate conversion is game-frame (x,y,z) -> Blender (x,-z,y), followed by FBX -Z-forward/Y-up export. Verify the imported wrapper's axes and face direction in Unity before replacing primitives; do not compensate by changing animation reach constants. Dimensions are metres; do not apply old primitive scales again. No animation clips needed: existing work/hunt posing moves the rigid tool. No colliders included.

Head widths and bevels are slightly emphasized for readability, while preserving working-face reach. Before integration approval, inspect the review sheet. Before production: test all strokes, saw contact, hoe ground strike, stirring pot clearance, two-hand grips, spear carrying/jabs, mirrored worker sides and actual phone zoom. Studio previews are not in-game animation verification. Contact sheet explicitly uses different camera scale for each tool so small tools can be inspected.
