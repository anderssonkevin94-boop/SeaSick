# Shipyard UI V1 - handoff for Claude

The UI is now installed in the isolated SeaSick-modular worktree. The main
SeaSick Assets folder, scenes and player save have not been changed.
See INTEGRATION.md for the backend wiring and remaining verification.

## Implemented

- UI Toolkit Midnight/Pearl screen using the project's fonts.
- Actual modular mesh preview, camera orbit, pinch/mouse-wheel zoom, top and
  three-quarter views. RenderTexture capped at 1024 pixels on its longest edge;
  camera only renders when the preview changes, not every frame. Preview uses
  2x MSAA; phone memory/performance still needs profiling.
- Add/remove the last middle bay, immediately behind the bow (0-3), and swap
  timber/reinforced M1 rotors. Existing bay indices and equipment references stay
  stable. Live removal is gated by the backend's per-section report, not UI
  occupancy calculations. Offline assembly validation refuses dangling fittings.
- Wire bounds highlight on the inserted section or changed wheel.
- Undo, cancellation by discarding the session, length comparison, rejection
  messages and confirm. No made-up cost, cargo, speed or handling numbers.
- Separate draft copy: editing never calls the live apply operation. Expected
  baseline accompanies confirmation so the gameplay adapter can reject stale
  edits. Confirm is disabled without an adapter or with missing preview meshes.
- Editor preview window and explicit runtime modal entry point, neither auto-boots.

## Install in the modular worktree, not the main tree

Copy Runtime/ to Assets/_Project/Scripts/UI/ModularYard/ and Editor/ to its
Editor/ subfolder. Copy Resources/UI/ModularShipyard.uss to
Assets/_Project/Resources/UI/ModularShipyard.uss. Let Unity create meta files.
Requires the existing modular foundation, SheetPanel asset, fonts and scenery
material helper. Do not copy Tests/ into Assets.

Editor menu: SeaSick > Modular > Shipyard UI Preview. Works outside Play mode;
confirmation deliberately disabled. Uses a one-bay reference ship, not a save.

## Gameplay adapter: keep your backend names, wrap them here

Implement SeaSick.UI.ModularYard.IShipyardRefit:

```csharp
ShipConfiguration ReadCurrent();
string Validate(ShipConfiguration draft); // null/empty means valid
bool TryApply(ShipConfiguration expected, ShipConfiguration draft, out string reason);
```

ReadCurrent must return a snapshot of the selected live ship. Validate is
read-only and may be called every 250 ms; include availability, equipment and
crew safety gates. TryApply must freshly validate, compare expected with the
current live configuration, and apply gameplay plus persistence atomically.
False must leave the old ship and save intact; reason is shown to the player.
UI-level assembly validation does not substitute for gameplay validation.

Open through ShipyardModal.Open(adapter, setWorldInputBlocked, removalBlocker).
The removalBlocker delegate receives a defensive draft snapshot and zero-based
middle-section index, and returns the report's blocking reason (null if removable).
Map this directly to Report(draft)'s per-section result; do not duplicate cargo,
berth or equipment occupancy logic. Live removal is refused without this binding.
The mandatory
callback receives true on opening and false on closing. Its owner must block
world picking, camera/helm input, combat and conflicting HUD interactions, then
restore their prior state. UIDocument event consumption alone is insufficient
for this game's direct Input System readers. Do not implement this as a global
time-scale change. Coordinate game pause/port eligibility with your backend.

The modal uses safe-area padding and a side panel in landscape. It owns a cloned
PanelSettings, off-world preview objects and camera on layer 31, and a render
texture; it cleans them up on closing/disable. Layer 31 must not have unrelated
objects near the off-world preview location (0,0,-1000).

## Verification

Run `bash art-staging/shipyard-ui-v1/verify.sh /path/to/SeaSick-modular` from
the main repository. It reads the worktree but writes build products only to a
temporary directory. Runtime and editor C# compile against Unity references;
27 headless draft checks cover isolation, undo, length limits, backend allowlists, wheel restrictions,
occupied-bay protection, backend rejection, stale confirmation and repeat apply.

Unity batch visual review completed in a disposable APFS-cloned project, not
either active project. Screenshots and report are in review/. All four logical
viewports passed: 320x568, 390x844, 430x932 and 844x390, with simulated safe-area
insets. Checks cover nonblank rendered pixels, >=44-point button bounds, wheel
selector clipping, single-line label fit and offline confirmation staying disabled.
Unity Clickable simulation exercised actual add/undo/wheel/cancel bindings and
verified that cancel destroys the preview camera and render texture. This is not
physical touch testing. Fixed unsupported USS first-child selector, clipped camera
tools, default scrollbar appearance and wheel selector clipping during review.

NOT YET VERIFIED: physical touch/pinch, runtime safe-area changes, live apply,
phone build, shader stripping, performance and input leaking into live gameplay.
Do not describe this package as phone-ready yet. The plain preview background
and selection bounds are functional review graphics, not finished shipyard art.

Batch renderer: Editor/ShipyardUiReview.cs. For safety it only runs in batch mode
in a disposable project whose directory name starts with seasick-yard-ui-review.
Set SHIPYARD_REVIEW_OUTPUT to the desired report directory, then execute
SeaSick.UI.ModularYard.EditorTools.ShipyardUiReview.Run. It uses the local Unity
version's internal panel repaint and Clickable simulator APIs only for QA.

## Deliberately deferred

Direct mesh tapping, arbitrary middle insertion, catalog thumbnails, raised
sections, width/depth changes, oversized wheels, equipment editing, shipyard
environment art and costs. The first pass is a functional UI prototype, not a
pixel-perfect implementation of the generated concept image.

Backend handoff update: ShipyardLiveBridge now binds Report(draft), per-section
removal checks, AllowedModuleIds, BuildPreview and the atomic adapter. Offline
preview length still uses the foundation assembler; the connected screen reads
all displayed figures from the backend. Apply success closes the modal. UI ship
caches subscribe to PlayerShipReplaced, and world-input readers have modal guards.
