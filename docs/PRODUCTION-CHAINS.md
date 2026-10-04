# Production chains

Generated from `Scripts/World/Economy/` by `RecipeGraph.ToMarkdown()`. Do not edit by hand.

## Resources

| id | label | tier | source | fire | what |
|---|---|---|---|---|---|
| Timber | timber | Raw | Gathered | I | logs, off any wooded island |
| Stone | stone | Raw | Gathered | I | rough stone, off the grey boulders |
| Food | food | Raw | Gathered | I | wild berries and roots, foraged; eaten raw at a quarter |
| Potato | potato | Raw | Grown | I | the staple; fast to grow, fine raw in a pinch |
| Carrot | carrot | Raw | Grown | I | second veg; stew and roast |
| Onion | onion | Raw | Grown | II | the stew gate; a farm II crop |
| Wheat | wheat | Raw | Grown | II | only useful through the mill; a farm II crop |
| Apple | apple | Raw | Grown | II | an orchard crop; a farm III plot |
| Fish | fish | Raw | Gathered | I | off the fishing hut; grill it or eat it raw |
| Meat | meat | Raw | Hunted | I | off every animal a hunter brings home |
| Game | game | Raw | Hunted | I | the herd on the island, counted in animals |
| Hide | hide | Raw | Drop | I | one off every animal a hunter brings home |
| Ore | ore | Raw | Gathered | II | dark rock with the metal in it, on the far islands |
| Spice | spice | Raw | Gathered | II | picked far out; what the last rungs of the ship cost |
| KrakenInk | kraken ink | Raw | Salvaged | I | a trophy from a driven-off kraken; its use is still to be decided |
| Boards | boards | Treated | Made | I | timber sawn square |
| Flour | flour | Treated | Made | II | wheat ground at the mill; bread and biscuit |
| Iron | iron | Treated | Made | II | ore smelted to a bar |
| Brick | brick | Treated | Made | II | stone cut square with iron tools; what a building's second level is built of |
| FineBoards | fine boards | Treated | Made | II | boards cut true on an iron saw |
| BakedPotato | baked potato | Item | Made | I | a potato in the coals |
| GrilledFish | grilled fish | Item | Made | I | a fish over the fire |
| GrilledMeat | grilled meat | Item | Made | I | meat off the spit |
| RoastCarrots | roast carrots | Item | Made | I | two carrots, a proper plate |
| Bread | bread | Item | Made | II | flour baked; a little cheer |
| VegStew | vegetable stew | Item | Made | II | potato, carrot, onion; lifts the mood |
| FishPie | fish pie | Item | Made | II | fish, potato and flour; hands work faster on it |
| HuntersStew | hunter's stew | Item | Made | II | meat and three veg; the best plate in camp |
| Meals | ship's biscuit | Item | Made | II | hard bread for the hold; keeps a crew at sea |
| Arrows | arrows | Item | Made | I | what a bow shoots, one a shot; lookouts loose them at raiders |
| Spear | spear | Item | Made | I | a board and a stone tip; a hunter cannot hunt without one |
| Tools | tools | Item | Made | II | iron and a handle; the quarry wears them cutting brick, the ship's later rungs want them |
| SawBlade | saw blade | Item | Made | II | an iron edge for the sawmill; cuts fine boards |
| IronSpear | iron spear | Item | Made | II | a board and an iron tip; lasts three stone spears |
| Bow | bow | Item | Made | II | a fine board strung with hide; shoots arrows to hunt, defend and fight from the ship |

## Fire levels

- **I camp** — costs nothing. a fire, and somewhere to keep ten of anything
- **II hamlet** — costs 20 boards, 12 stone, 4 hide; opens Quarry, Mill. opens the quarry, the forge's iron work, and every building's second level

## Recipes

| station | makes | takes | per hand per day | fire | station level | tool (wear) |
|---|---|---|---|---|---|---|
| Sawmill | 3 boards | 1 timber | 12 | I | 1 |  |
| Sawmill | 1 fine boards | 2 boards | 2 | II | 1 | saw blade (0.05) |
| Kitchen | 1 baked potato | 1 potato | 12 | I | 1 |  |
| Kitchen | 1 grilled fish | 1 fish | 9 | I | 1 |  |
| Kitchen | 1 grilled meat | 1 meat | 9 | I | 1 |  |
| Kitchen | 1 roast carrots | 2 carrot | 9 | I | 1 |  |
| Kitchen | 3 bread | 2 flour | 12 | II | 2 |  |
| Kitchen | 3 vegetable stew | 2 potato, 1 carrot, 1 onion | 9 | II | 2 |  |
| Kitchen | 4 ship's biscuit | 2 flour | 12 | II | 2 |  |
| Kitchen | 4 fish pie | 2 fish, 2 potato, 1 flour | 8 | II | 3 |  |
| Kitchen | 4 hunter's stew | 1 meat, 2 potato, 1 carrot, 1 onion | 8 | II | 3 |  |
| Mill | 1 flour | 2 wheat | 6 | II | 1 |  |
| FishingHut | 1 fish | nothing | 4 | I | 1 |  |
| Fletcher | 3 arrows | 1 timber | 3 | I | 1 |  |
| Fletcher | 1 bow | 1 fine boards, 1 hide | 1 | II | 1 |  |
| Blacksmith | 1 spear | 1 boards, 1 stone | 1.5 | I | 1 |  |
| Blacksmith | 1 iron | 2 ore | 1.5 | II | 1 |  |
| Blacksmith | 1 saw blade | 2 iron | 0.5 | II | 1 |  |
| Blacksmith | 1 tools | 1 iron, 1 boards | 1 | II | 1 |  |
| Blacksmith | 1 iron spear | 1 boards, 1 iron | 1 | II | 2 |  |
| Quarry | 1 brick | 1 stone | 2 | II | 1 | tools (0.1) |

## Building upgrades

| building | to level | fire | costs | rate × | store + | beds + |
|---|---|---|---|---|---|---|
| Sawmill | 2 | II | 6 brick, 4 fine boards | 1.5 | 0 | 0 |
| Blacksmith | 2 | II | 6 brick, 4 fine boards | 1.5 | 0 | 0 |
| Kitchen | 2 | II | 4 brick, 4 fine boards | 1.5 | 0 | 0 |
| Kitchen | 3 | II | 8 brick, 6 fine boards | 1.5 | 0 | 0 |
| Farm | 2 | II | 4 brick, 6 boards | 1 | 0 | 0 |
| Farm | 3 | II | 8 brick, 4 fine boards | 1 | 0 | 0 |
| Mill | 2 | II | 6 brick, 3 fine boards | 1.5 | 0 | 0 |
| Fletcher | 2 | II | 4 brick, 3 fine boards | 1.5 | 0 | 0 |
| FishingHut | 2 | II | 4 brick, 3 fine boards | 1.5 | 0 | 0 |
| Quarry | 2 | II | 8 brick, 2 fine boards | 1.5 | 0 | 0 |
| Hut | 2 | II | 4 brick, 4 fine boards | 1 | 0 | 1 |
| Storage | 2 | II | 6 brick, 2 fine boards | 1 | 0 | 0 |
| Storage | 3 | II | 14 brick, 8 fine boards | 1 | 0 | 0 |
| Watchtower | 2 | II | 6 brick, 4 fine boards | 1 | 0 | 0 |

## Hunting

A hunter needs one of: iron spear, spear (best first). Wear per animal: stone spear 0.25, iron spear 0.08. Each animal also drops 1 hide.
