# Placeholder art in the game (audit 2026-09-30)

Everything still drawn with Unity primitives (cubes, spheres, cylinders, capsules) or crude code boxes. No `.prefab` in `_Project` uses a built-in mesh: every placeholder is spawned from C#. **Seen** = normal play; **Rare** = fallback / edge case.

## Seen in normal play

### Resources and piles (`World/CampPiles.cs`)
| What | Looks like | Where |
|---|---|---|
| Timber / boards piles, deck loads, floating cargo | brown cylinders | `CampPiles.cs:445` BuildLog (shared by `CargoVisual`) |
| Stone / ore piles | grey flat cubes | `CampPiles.cs:474` |
| Brick pile | orange flat cubes | `CampPiles.cs:503` |
| Arrows pile | thin cylinder bundle | `CampPiles.cs:523` |
| Tools / other goods with no shape | plain cube heap | `CampPiles.cs:460` |
| Build-plot log stack, corner stakes | cylinders, thin cubes | `World/BuildSite.cs:282`, `:215` |

### Food
| What | Looks like | Where |
|---|---|---|
| Every food pile (fish, meat, crops, meals) | squashed spheres + band, cube crate at 6+ | `CampPiles.cs:546/554/570` |
| Food carried by villagers | cube "sacks" | `World/VillagerActing.cs:1000` |
| Food item **icons**: Potato, Carrot, Onion, Wheat, Apple, Fish, Meat, Flour, BakedPotato, GrilledFish, GrilledMeat, RoastCarrots, Bread, VegStew, FishPie, HuntersStew | blank tile / generic pictogram | `UI/Sheets/ItemIconSet.cs` (16 of the game's items have no icon) |

### Carried items and tools
| What | Looks like | Where |
|---|---|---|
| Carried logs / planks / stone / brick / sacks | cylinders and cubes | `World/VillagerActing.cs:936-1000` |
| Hammer, axe, saw, hoe, stir paddle | stick-and-block cubes | `World/VillagerActing.cs:1010-1082` |
| Hunter's spear | cylinder shaft, cube head | `World/HunterProps.cs:261-279` |

### Animals and sea life
| What | Looks like | Where |
|---|---|---|
| Sea monster (3 per world) | teal spheres, cubes, cylinders | `Combat/SeaMonster.cs:103-156` |
| Dolphins | grey capsules | `Ship/SeaLife/DolphinPod.cs:42` |
| Gulls over fish shoals; shoal patch | flat white spheres; dark disc | `Ship/SeaLife/FishShoal.cs:60/74` |

### Ship and sea
| What | Looks like | Where |
|---|---|---|
| Ship cannons + watchtower cannon | cube carriage, cylinder wheels/barrel | `Ship/Cannon.cs:149-178` |
| Cannonball | black sphere | `Ship/CannonBall.cs:56` |
| Floating salvage crates, plank flotsam | cubes | `Voyage/SalvageSpawner.cs:84`, `Ship/SeaLife/FlotsamCrate.cs:85` |
| Message in a bottle | tiny green cylinder | `Ship/SeaLife/MessageBottle.cs:70` |
| Gangway to shore | brown cube | `Ship/Gangway.cs:34` |
| Reefs (24 per world) | dark grey cubes + foam quad | `Terrain/World/TerrainWorldPopulator.cs:720/731` |

### Buildings and props
| What | Looks like | Where |
|---|---|---|
| Campfire flame | orange sphere | `World/BuildingFactory.cs:755` |
| Gravestones (4 kinds) | cube slab, cube cross, sphere cairn, cube board | `World/Life/GraveVisual.cs:72-107` |
| Shelter-island cache / cairn finds | stacked cubes, cube pole and flag | `World/IslandFind.cs:266-304` |
| Cliff ladder and landings | code boxes | `World/LadderLayout.cs:327` |
| Wall repair blueprint ghost | plain cube | `World/BuildingFactory.cs:468/497` |
| Home dock | code-built planks and posts (look not checked) | `Terrain/World/DockBuilder.cs:112` |

## Rare (fallbacks and edge cases)
- Man overboard swimmer head (sphere), rescue jolly boat (cube hull, capsule rower): `Ship/Overboard/Swimmer.cs:115`, `JollyBoat.cs:58/66`
- Goat / boar / gull stand-in cube if the FBX fails to load: `World/Fauna/FaunaField.cs:361`
- Tree / boulder / ore / spice fallbacks if the kit is missing: `Terrain/World/IslandPropFactory.cs:68-137`
- Raider hull and guns if fleet art fails: `Combat/EnemyShip.cs:490-534`
- Modular ship "PLACEHOLDER" cube for a module with no mesh: `Ship/Modular/View/ModularShipView.cs:88-124`
- Coaster outfitting boxes (floors, panels, rungs, rails): `Ship/Modular/View/CoasterOutfitting.cs:146`
- Palisade, pier, dry dock, fletcher, quarry cube fallbacks (real kits exist): `World/BuildingFactory.cs:377-1109`
