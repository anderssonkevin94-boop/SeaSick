# SeaSick graphic adventure art pass

**Ship fleet revision (2026-09-17):** [Revised fleet gallery](fleet-v3/index.html) and [ship construction / progression rules](SHIP-FLEET-RULES.md). These are standalone Blender studies; the earlier fleet gallery is superseded by this review revision.

**Latest session (2026-09-17):** [Standalone crew and settlement handoff](SESSION-HANDOFF.md). Current restriction: leave Unity untouched; new assets await explicit import authorization.

**Island rebuild:** see [the standalone asset library and gameplay checks](STORYBOOK-ISLANDS.md). The newer storybook kit replaces the first-pass scenery described below.

The Option B treatment is applied to **Sea.unity**. `ArtDirectionLab.unity` remains available for look development. The actual twenty-node fleet retains its original meshes, rigging, sockets, displacement data and physics.

## Implemented

- Cobalt ocean, turquoise shallows, broader lighting and reflections, less fine ripple/foam noise. Simulation displacement and buoyancy are unchanged.
- Warm sand, coordinated grass/rock colours, simpler terrain shading and cool shadow fill.
- A reproducible Blender scenery kit: 26 templates, 1,694 triangles across the source kit, including both tree LODs. Broader crowns and shore rocks use the existing welded-cell rendering and resource placement.
- Shared 128×128 semantic fleet palette: golden timber, cream canvas and cool dark structural bands. All twenty ladder variants successfully swap to the new palette.
- Warmer dock timber and roof/wall/beam colours for runtime buildings; original footprints and construction rules retained.
- Input System migration for IslandCam, fixing the legacy-input exception encountered during review.

## Review captures

- [Daylight island and brig](option-b-island-final.png)
- [Home harbour](option-b-home.png)
- [Portrait camera render](option-b-portrait.png)
- [Dusk](option-b-dusk.png), [night](option-b-night.png), [storm lighting](option-b-storm-lighting.png)
- [Original harbour](baseline-harbour.png)
- [Original brig palette in Blender](brig-baseline-blender.png), [new palette in Blender](brig-option-b-blender.png)
- [Selected concept](harbour-directions-v1.png)

Unity captures are camera renders, without IMGUI. The island comparison uses camera (112,27,-144), look-at (45,8,-300), FOV 52, brig at (55,0,-195), time .36. Dusk/night use .72/.85; storm lighting forces SkyDirector to 1. Sea phases and sail trim are not frozen, so these are visual references rather than pixel-exact comparisons. The storm shot tests lighting, not a full western storm sailing trial.

## Verification and limits

Runtime and editor C# compile cleanly. Loaded SeaSick shaders report no import messages. All twenty fleet swaps use the new atlas with zero legacy fleet palette assignments; see [fleet report](fleet-validation.txt). The existing editor performance probe passes: median simulation dispatch CPU 0.015 ms, p90 0.036 ms, cascade/simulation VRAM 21.8 MB. See [report](performance-art.txt). These figures do not measure GPU frame time or prove the planned 5% baseline regression budget.

Portrait and landscape camera renders were reviewed. The GameViewSize helper reports selecting the requested sizes, but the runtime Screen dimensions remain inconsistent and HudOverlapProbe observes zero rectangles. **HUD validation is inconclusive**, not a pass. Full player/device performance, motion/LOD transitions, harvesting and a complete voyage remain unverified. The home island's sparse planting, existing navigation beams and crew designs remain recognizable parts of the game; this pass does not recreate the concept's illustrative village layout.

## Reproduce

- `SeaSick → Art → Create Option B Lab`: configure the review scene.
- `SeaSick → Art → Apply Option B To Sea`: apply the treatment to the production scene without replacing gameplay objects.
- Fleet palette source: `tools/blender/graphic_art_preview.py`.
- Scenery source: `tools/blender/graphic_scenery.py`; uses the existing builders with optional graphic parameters and preserves the user's original Blender scene.
- Original materials/settings remain intact; the scene references copies under `Materials/GraphicArt`.

Unity CLI must run with local editor network access. During recovery, sandboxed discovery checks removed the connection descriptor; do not use those checks. The temporary bootstrap is removed after validation. Existing package and editor-setting changes are excluded from the art commit.

The [overhaul plan](OVERHAUL-PLAN.md) records the larger art direction and outstanding acceptance checks. Concept prompt: [prompt.txt](prompt.txt).
