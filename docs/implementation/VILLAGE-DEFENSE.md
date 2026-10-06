# Village defense — 6 October 2026

## Structure
Persistent gear and quivers live on OutpostHand via VillagerEquipment. The ledger owns atomic stock transfers, rescue assignment, wounds and death. CampWorker owns locomotion and attack clocks. VillagerActing renders synchronized procedural poses on the existing rig. RaidWalker owns raider health and retaliation. VillageDefense checks sight and raises the shared RaidAlarm. Existing save serialization includes the new fields; old saves start with empty equipment.

## Playable stages
1. Shoreline and detection: weighted valid approaches, avoid the last sector when alternatives exist; a ground observer sees 24 m, an elevated tower observer 55 m, with terrain and structural cover.
2. Gear and combat: villager page → Equipment; six slots, bow uses both hands, 12-arrow reserve. Owned gear remains equipped after raids. Spears and bows can still be borrowed through the existing alarm system. Personal archers return to stores when empty. Shield blocks frontal strikes probabilistically; armor reduces damage. Added craftable shield and basic leather pieces through existing fletcher/smith production.
3. Casualties: three-minute downed window (existing LifeTuning), physical routed drag, bleed-out continues until arrival at a hut. A known home hut is preferred; otherwise one is assigned on rescue. No hut means no recovery. Safe automatic rescue or manual override on the villager page. Armor and gear drop on death. Short-lived visual corpse remains; existing grave/life-story system owns permanent death.

## Ownership and limits
- Removing a villager to board the ship returns personal gear to the camp store; ship equipment remains the existing separate system.
- Raid state remains transient on save/load, as before; equipment and downed/rescue state persist.
- Offline and paused bleed-out remains frozen, preserving the existing game rule.
- Armor slots and damage reduction are functional; clothing meshes are not yet swapped by equipment. The equipped shield is visible. Balance is a first pass. Combat animations are procedural overlays on the current rig, not newly authored animation clips.

## Verification
Run VillageDefenseCheck.RunBatch through Unity batch mode with the editor closed. All 45 gates passed: inventory conservation, two-handed rules, old saves, save/load, armor, bleed-out during drag, interrupted rescue, death, resource graph, terrain visibility, reachable shore landings and variation between successive raids.

Run RunVillageDefensePlay.Batch for the runtime and UI fixture. Passed: sight-triggered shared alarm, spear wind-up/contact, raider wind-up/contact and mirrored-rig weapon animation, real arrow spawn and ammunition, delayed projectile damage, movement dodging, physical rescue pickup, missing/far hut rejection and recovery on arrival. Equipment scrolling and touch target checks passed at 1080×2340 and 1920×1080; screenshots inspected in Logs/DefensePreview. This is a controlled Unity fixture, not a full settlement battle stress test.
Phone playtest: equip spear/shield and bow on separate villagers, stock arrows, post a tower lookout, trigger a raid, observe detection and ranged/melee damage, down a hand, manually rescue, repeat with no reachable hut, and inspect alternate landings next raid. Numbers and visuals require approval after playtesting.
