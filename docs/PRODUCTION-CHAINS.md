# Production chains

Generated from `Scripts/World/Economy/` by `RecipeGraph.ToMarkdown()`. Do not edit by hand.

## Resources

| id | label | tier | source | fire | what |
|---|---|---|---|---|---|
| Timber | timber | Raw | Gathered | I | logs, off any wooded island |
| Stone | stone | Raw | Gathered | I | rough stone, off the grey boulders |
| Food | food | Raw | Gathered | I | berries, wheat, meat: what the camp eats |
| Game | game | Raw | Hunted | I | the herd on the island, counted in animals |
| Hide | hide | Raw | Drop | I | one off every animal a hunter brings home |
| Ore | ore | Raw | Gathered | II | dark rock with the metal in it, on the far islands |
| Spice | spice | Raw | Gathered | II | picked far out; what the last rungs of the ship cost |
| Boards | boards | Treated | Made | I | timber sawn square |
| Iron | iron | Treated | Made | II | ore smelted to a bar |
| Brick | brick | Treated | Made | II | stone cut square with iron tools; what a building's second level is built of |
| FineBoards | fine boards | Treated | Made | II | boards cut true on an iron saw |
| Meals | meals | Item | Made | I | food cooked; feeds better than it was |
| Arrows | arrows | Item | Made | I | spent by hunters and lookouts |
| Spear | spear | Item | Made | I | a board and a stone tip; a hunter cannot hunt without one |
| Tools | tools | Item | Made | II | iron and a handle; the quarry wears them cutting brick, the ship's later rungs want them |
| SawBlade | saw blade | Item | Made | II | an iron edge for the sawmill; cuts fine boards |
| IronSpear | iron spear | Item | Made | II | a board and an iron tip; lasts three stone spears |

## Fire levels

- **I camp** — costs nothing. a fire, and somewhere to keep ten of anything
- **II hamlet** — costs 10 boards, 6 stone, 4 hide; opens Quarry. opens the quarry, the forge's iron work, and every building's second level

## Recipes

| station | makes | takes | per hand per day | fire | station level | tool (wear) |
|---|---|---|---|---|---|---|
| Sawmill | 1 boards | 1 timber | 3 | I | 1 |  |
| Sawmill | 1 fine boards | 2 boards | 2 | II | 1 | saw blade (0.05) |
| Kitchen | 1 meals | 1 food | 3 | I | 1 |  |
| Fletcher | 3 arrows | 1 timber | 3 | I | 1 |  |
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
| Fletcher | 2 | II | 4 brick, 3 fine boards | 1.5 | 0 | 0 |
| Quarry | 2 | II | 8 brick, 2 fine boards | 1.5 | 0 | 0 |
| Hut | 2 | II | 4 brick, 4 fine boards | 1 | 0 | 1 |
| Storehouse | 2 | II | 8 brick, 4 fine boards | 1 | 20 | 0 |
| Storage | 2 | II | 6 brick, 2 fine boards | 1 | 10 | 0 |

## Hunting

A hunter needs one of: iron spear, spear (best first). Wear per animal: stone spear 0.25, iron spear 0.08. Each animal also drops 1 hide.
