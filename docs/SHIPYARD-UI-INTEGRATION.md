# Shipyard UI integration - 2026-09-24

Installed only in `/Users/kevinandersson/Desktop/SeaSick-modular`, uncommitted.
Main SeaSick gameplay, scenes and saves are untouched. Claude's concurrent
changes to capacity, hydrostatics and backend tests were not modified.

## Entry and contracts

Ship manifest action row: All ashore / Shipyard / Cast off. Shipyard appears
only when ShipyardService.Player exists. The full-screen editor supports 0-3
middle bays and the two allowed M1 wheel drawings. Removal is disabled when the
backend section report refuses it, with the reason in the scrollable details.

ShipyardLiveBridge wraps ShipyardRefitAdapter without modifying that backend
class. It reads allowed module IDs, uses service.BuildPreview for detached
visuals, and shows Report figures (including availability/provisional flags).
No local cargo, berth, displacement, draft or gun-capacity arithmetic. Preview
and cancel never call TryApply; confirm supplies the captured baseline and the
draft. Backend remains responsible for eligibility, fresh validation, rollback
and persistence. No costs or performance bonuses are invented.

The report is cached for at most 250 ms per draft/service. A failed apply remains
open with its reason. STALE_DRAFT requires closing and reopening to read a new
baseline; there is no automatic silent rebase.

## Integration files

- New UI/ModularYard runtime scripts; Resources/UI/ModularShipyard.uss.
- UI/Sheets/ShipSheet.cs: entry point, existing ashore/cast-off retained.
- UIBlocker, WorldPicker, Hand, CampSiting, WallSiting: input guards.
- Ship/AnchorController.cs: only spacebar-command and OnGUI guards. Its ongoing
  mooring/state update is not paused.
- StatusHUD, MiniMap, NavigationAid, HomeTab, SheetBits (in SheetBootstrap),
  Sheets, ChartWatch: rebind to PlayerShipReplaced with lifecycle cleanup.
- Existing placement/hand gestures are cancelled when the modal opens.
- Sheets.Open refuses new sheets during the modal; closing restores input.

Backend currently rebuilds in place; UI rebinds also support a future replaced
object. Non-UI cached references are still the backend's responsibility.

## Verification and limits

Runtime/editor compile and 27 headless draft checks pass. Backend tests are
being updated concurrently by Claude; the latest observed run passed 97/97.
Connected Unity render checks use an empty-scene backend fixture, real module
data and service-generated previews, never the player's ship/save. They do not
exercise a successful live rebuild or save rollback.

Connected visual review passed at 320x568, 390x844, 430x932 and 844x390,
including touch-target bounds, label fit, backend length display, nonblank
preview rendering and correct confirmation availability. Simulated Button
add/undo/wheel/cancel checks passed with no leaked preview camera or texture.
Results: `art-staging/shipyard-ui-v1/review-connected/` in the main workspace.

Still required before main-game merge: gameplay play-mode input-leak test,
successful/refused refits with real cargo/crew, save/load/rollback probe,
physical iPhone touch/safe-area and memory/performance checks. The preview
background and wire selection outline remain functional prototype graphics.

Source staging lives in `art-staging/shipyard-ui-v1` in the main workspace;
integration/ and integration-ship/ are edited copies of the worktree files.
Do not blindly recopy them after other agents change those same files; merge
the small diffs instead. Do not copy Tests/ into Unity Assets.
