> Unity implementation authorized and completed 2026-09-17. See [integration and validation notes](fleet-v3/UNITY-INTEGRATION.md). Earlier standalone-only statements describe the art phase.

# Player fleet art direction

Latest standalone revision: [fleet v3 gallery](fleet-v3/index.html). Editable sources are `tools/blender/source/fleet-v3/01.blend` through `20.blend`; generator: `tools/blender/fleet_design_v3.py`. Keep Unity and gameplay assets separate from these studies.

## User requirements from the fleet review

- Each upgrade must add craftsmanship, architecture or distinctive fittings. Do not just enlarge the preceding model.
- Every ship from stage 4 onward must have cannons. Total cannon count must never decrease on an upgrade. Hull length, beam, height and sail count must also never regress.
- The early V14 brig remains a craftsmanship reference, but stage 13 must use the current fleet proportions. Do not preserve its old standalone geometry as stage 13. Ships 1–3 were accepted in the first fleet review.
- Captain's quarters must always be part of the hull. Continue the hull sides and transom to the quarterdeck; do not put an inset rectangular hut on the deck.
- Complete the rear railing. Include posts and join the guard to the side rails. Leave a purposeful opening for stairs, not an unguarded rear edge.
- Ship 6 has an open helm, without the canopy/sheet over the captain.
- Ships 16–20 keep their total cannon counts. Move the upper battery onto the weather deck and put the enclosed battery halfway between the two old enclosed row heights. Do not add guns near the waterline.
- Give the prow a pronounced rising curve and rake. Follow that curve with the hull planking, stem, cap rails and attached fittings.
- Retain the strong honey timber / teal / vermilion / brass / ivory palette, small non-overlapping jib, readable cartoon cannons and deliberately attached rigging.
- Apply ship 13's attention to detail to the whole fleet. Check both bow and stern views, connected hull/cabin seams, railing corners, stair supports and rope routes around the helm.

## Revision 2 implementation

The raised stern shares vertices with each side shell and extends the transom. Quarterdeck planks and windows follow the hull contour. The launches receive stern railings and fitted rear fascia. The cabin ships have complete quarterdeck guards, supported stairs, panelled doors and paired side backstays clear of the wheel. Enclosed gunports are cut through the hull rather than represented only by dark exterior plates. The bow deformation moves fitted geometry together so trim remains attached.

The revised 16–20 enclosed battery centres are 2.360, 2.435, 2.510, 2.585 and 2.635 m in the study coordinates. The weather-deck batteries contain 8, 8, 10, 10 and 12 guns respectively. Total armaments remain 16, 18, 20, 22 and 24. These are static art layouts, not runtime sea-state or stability validation.

Revision 3 retains ships 1–3 and 16–20 from revision 2. Stage 13 is rebuilt with 14 cannons on the current fleet hull construction, between the 12-cannon stage 12 and 16-cannon stage 14. Nominal dimensions in the gallery describe the original hull design before the extended prow; camera framing is independent per ship. Source revisions are retained for comparison. The user accepted revision 2 overall and requested the revision 3 armament/progression fixes. Do not label revision 3 approved before the user reviews it.

## Revision 3 cannon ladder

Stages 1–3 have no guns. Stages 4–20 have **2, 2, 4, 4, 6, 6, 8, 10, 12, 14, 16, 16, 16, 18, 20, 22, 24** cannons. Equal-count stages upgrade hull width, height, rigging or craftsmanship. These counts describe the standalone art fleet, not an authorized change to Unity gameplay data.

The saved scenes are checked for actual gun counts and nondecreasing hull length, beam and upper-hull height. The gallery includes a common-scale render of ships 12–14; individual main screenshots remain independently framed for detail.
