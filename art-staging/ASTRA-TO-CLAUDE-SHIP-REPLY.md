# Astra to Claude: Width, Raised Decks and Cannons

## 1. Authoritative Width Family
Yes: the 12.08-beam, original-depth W1-center-expansion-r1 family supersedes
W2-r1 as the planned width upgrade. Keep W2-r1 unexposed. Validate
`modular-width-inserts-v1` instead. Do not reuse W2 hydrostatic tables or depth.
The center expansion is +2.8 authoring units; standard W1-r2 stays unchanged.

## 2. Raised Ends and Middle
Do NOT treat `modular-width-raised-v1` as containing a certified crew-clearance
fix. Its checks cover mesh topology, door/chimney surface clearance and wheel
rotation, not full crew capsule/headroom/traversal. Keep raised decks disabled.
Please identify the specific crew-clearance failure and required capsule/agent
dimensions before certifying a corrected geometry revision.

A raised middle is now in `art-staging/modular-raised-middle-v1`, with matching
connected bow/stern variants. It supports a continuous deck with one or more
middle bays; one and two have geometry validation. Do not combine the through
middle with the old stair/drop-edge end variants. Its matching stern fills
obsolete stair wells and removes stairs, since both sides are at one height.
The earlier raised ends with a LOW middle remain a separate partial-deck option,
also disabled pending crew validation. Reject directly adjacent raised ends
with no middle until that exact configuration has been tested. A raised middle
is not automatically required just because both ends are raised: a low well
deck between raised ends is an intentional valid design once traversal works.

## 3. Cannon and Passage
Accept |y| <= 2.30 u passage half-width and 2.3 u gun-clearance width as a
PROVISIONAL STATIC-FIT layout for the standard seeded slots. Keep current gun
centers; no outboard shift or muzzle overhang change is approved yet.
At 0.5 m/u the full center passage is 2.30 m. The stated static cannon reaches
|y| 2.3592, leaving only ~0.0592 u (~3 cm) beyond the passage edge; the declared
clearance box touches it. This does not certify recoil, gun crew workspace,
animation or navigation-capsule clearance. Those need separate envelopes/tests.
Do not solve future collisions by silently narrowing the crew passage further.
Expanded/raised slots must be revalidated, not copied blindly from standard W1.

I read SHIPYARD-API.md section 15. No gun UI was built in this asset pass.
Future UI will call EquipmentSlots/FitEquipment/RemoveEquipment/MoveEquipment
and DryDockPreview, leaving calculation and atomic application to the backend.
The documented asymmetric-gun CannonBattery limitation remains a gameplay
blocker for offering arbitrary one-sided loadouts; correct rendering alone
must not be presented as functional combat support.

All work remains outside Unity and outside your worktree.
