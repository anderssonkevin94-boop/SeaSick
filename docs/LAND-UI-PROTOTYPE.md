# Midnight & Pearl: Island UI Trial

Reference: the user's D2 blue mockup, 2026-09-24. This is a reversible first
implementation, not approval of every screen or final icon artwork.

## Scope

- Active only when the existing camp sheet HUD owns the island interface.
- Top bar: actual timber/plank store counts, crew ashore, and day.
- Bottom navigation opens existing build, hands, stores, and ship pages.
- Smaller chart at upper right. Legacy place/crew/status overlays yield.
- Production stations use stable Production / Upgrade tabs. Production shows
  the selected station's input bay, bench progress, output rack, worker,
  stall reason, product choice, and finite/repeat/stop order controls together.
  Classic retains its original pages. No economic or assignment rules changed.
- The mockup's arbitrary three-worker counter is not used: the game currently
  assigns named workers through its existing WorkerSlot control.

## Compare and Tune

Settings > Island UI toggles Classic/Midnight for this play session. The choice
resets to Midnight next session. Sailing retains its previous UI.

`Assets/_Project/Resources/UI/MidnightLand.uss` owns the visual rules; the small
runtime palette is in `MidnightLandHud` and `SheetTheme`. Icons are native UI
meshes, not emoji or font glyphs. No scene/prefab migrations are required.
The host clones PanelSettings at runtime: portrait land scaling never modifies
the shared settings asset, and Classic restores the original resolution.

## Verification

Use `tools/compilecheck.sh` for both assemblies. In a camp in Play mode,
SeaSick > Land UI > Open Sawmill Preview opens an existing sawmill and suppresses
saving; it does not spawn buildings or populate invented inventories.
Capture And Check Layout writes a PNG and bounds report to
`/tmp/seasick-midnight-land.{png,txt}`.

Before accepting the design, inspect portrait and landscape captures; test
worker assignment, repeat/finite orders, stop, upgrades, dismissal, switching
buildings, each navigation destination, Classic rollback, and casting off.
Check empty bays, full racks, no available worker, and unaffordable upgrades.
Actual device touch/layout verification and a refreshed Unity iOS export are
still required. Compilation alone does not certify visual or interaction QA.

## Implemented and Checked, 2026-09-24

- Compiled runtime and editor assemblies with zero errors.
- Exercised Build, Crew, Stores and Ship navigation in Unity Play mode.
- Assigned a sawyer and placed a finite production order through the UI.
- Confirmed the Ship panel's Cast off returns to the existing sailing HUD.
- Inspected 1080x2340 portrait and 1920x1080 landscape. Moved the landscape
  chart clear of the panel and the performance overlay below the resource bar.
- Enlarged build buttons, separated fortifications from building pages, and
  arranged stores in two columns, paging additional resources four at a time.
- Sawmill capture and button-bounds report: `validation/midnight-land/`.
  This is a temporary test camp, not the user's saved island. The bounds check
  is limited to visible buttons and panel/navigation separation.
- Test camp created with Create Temporary Camp Fixture, autosaving suppressed,
  and discarded by stopping Play. No scene or save-file migration is needed.

Still to verify: physical-device safe areas and touch, Classic toggle in-game,
all late-game resource pages, successful upgrades, repeat/stop lifecycle and
full/empty production transitions. The legacy screens retain their existing
workflow; this pass establishes the visual shell, not a complete UX redesign.
No Unity iOS export or Xcode build was performed.

## Production UX Revision, 2026-09-24

- Removed layout-generated Work 1/2, Make, and Work 2/2 tabs from Midnight
  recipe stations. Production and Upgrade now describe player tasks.
- Worker assignment remains in Production; when nobody is idle, Manage crew
  opens the crew page instead of leaving a disabled assignment button.
- Product and quantity changes are drafts until Start/Update order is pressed.
  Stop uses the existing economy behavior, including finishing a loaded batch.
- Selecting a building issues one eased camera composition request, placing
  its ground anchor in the uncovered world area. Inspection tilt is at least
  60 degrees; manual dragging cancels pending framing. The minimap shrinks
  during building inspection. Camera behavior remains owned by IslandCam.
- Verified in a save-suppressed temporary fixture at 1080x2340 and 1920x1080:
  Production/Upgrade tab labels, visible text/control bounds inside panel
  content, and building anchor outside the panel/resource bar all pass.
  Twenty-four camera projection cases also pass.
- Captures/reports: `validation/midnight-land/production-ux-phone.*` and
  `production-ux-landscape.*`. Plot foliage was cleared only in the disposable
  fixture because direct fixture construction skips normal plot clearing.
- New pointer interaction checks were blocked by desktop automation returning
  noWindowsAvailable. Prior-shell interaction results above do not certify the
  new dropdowns and order footer. Manually check recipe selection, locked
  recipes, finite/repeat/update/stop orders, worker assignment, Upgrade, and
  camera drag cancellation before a phone export. Device testing remains open.

## Food And Building Attention, 2026-09-24

- Header adds a food bowl and days of stored food at current crew/rations.
  Below one day is coral; no crew shows `--`, disabled rations show `Off`.
  This is supply coverage, not a forecast of future gathering. Food reserved
  in station bays is excluded because upkeep only consumes the camp store.
- Flow labels and header hints now use the registry's Timber / Boards naming,
  matching recipes, inventories, and costs; resource IDs remain unchanged.
- Production buildings show passive Needs worker, Needs supplies, or Output
  full labels. Supplies warnings use the ledger's real stall reason, so empty
  bays with available/incoming supply are not falsely labelled as blocked.
- At most 12 pooled labels are visible, ignoring pointer input and avoiding
  one another, the safe-area edges, minimap, header, navigation, and sheet.
  Status scans run twice per second; positions track the camera each frame.
- Food/status checks passed in Unity, including idle/working/full stations,
  ration settings, empty crew, reserved food, and large-number formatting.
  Phone and desktop bounds checks passed; captures are `food-status-phone`
  and `food-status-landscape` under `validation/midnight-land`.
- Selection framing no longer expires while waiting for scene/camera startup;
  it still cancels on manual dragging, deselection, or invalid selection.
  The timeout removal compiled but needs a further cold-start playtest.
- Physical-device testing and an iOS export have not been performed.
