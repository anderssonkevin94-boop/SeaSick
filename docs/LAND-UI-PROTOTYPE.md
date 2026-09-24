# Midnight & Pearl: Island UI Trial

Reference: the user's D2 blue mockup, 2026-09-24. This is a reversible first
implementation, not approval of every screen or final icon artwork.

## Scope

- Active only when the existing camp sheet HUD owns the island interface.
- Top bar: actual timber/plank store counts, crew ashore, and day.
- Bottom navigation opens existing build, hands, stores, and ship pages.
- Smaller chart at upper right. Legacy place/crew/status overlays yield.
- Sawmill overview shows the selected station's input bay, bench state and
  progress, output rack, real worker assignment, and stall reason.
- Existing production amounts, repeat/stop orders, recipes, and upgrade costs
  remain on their existing pages. No economic or assignment rules changed.
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
