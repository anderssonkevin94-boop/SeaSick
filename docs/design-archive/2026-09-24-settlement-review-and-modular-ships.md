# Settlement Review And Modular Ship Discussion

Date: 2026-09-24
Status: discussion archive, not an implementation specification.
Kevin approved the general direction of the settlement review and requested
that it be saved for later. No implementation was requested in this discussion.

## Intended Gameplay Loop

Develop an island, upgrade production, balance food and progress, build defences.
Resource shortages or other opportunities motivate departure. Choose crew and
provisions, sail, find loot, gather from other islands, fight, then return to
deposit resources and see how the settlement progressed during the absence.

## Settlement Catalogue And Recommended Roles

| Building | Current role in reviewed code | Recommended direction |
| --- | --- | --- |
| Campfire | Establishes camp, initial storage, progression gates | Settlement heart: population, provisions, priorities, return summary. Evolve into a communal hearth instead of adding a redundant town hall. |
| Storage hut | Capacity and goods-handling hub | Reserve goods for residents, construction, and expeditions; show logistics bottlenecks. |
| Shelter | Housing capacity; housing and food enable recruitment | Sustainable population; later comfort and recovery as well as capacity. |
| Farm | Renewable Food through regrowing planted beds | Reliable food balanced against space and labour; retain reasons to hunt. |
| Sawmill | Boards and fine boards | Construction industry; choose ordinary expansion versus higher-quality materials. |
| Quarry | Turns gathered stone into Brick, wearing tools | Mechanically a stonecutting yard. Consider display names such as stonecutting yard and cut stone, unless extraction becomes a separate mechanic. |
| Blacksmith | Stone spears, iron, tools, saw blades, iron spears | Equipment and maintenance; keep early production together until specialist industries are justified. |
| Fletcher | Arrows for hunting and defence | Ranged equipment and ammunition with visible preparedness benefits. |
| Kitchen | Food into Meals | Feed residents efficiently and prepare voyage provisions. |
| Watchtower | Staffed threat reduction and cannon fire against ships | Early warning and defensive coverage; later distinguish lookouts and coastal batteries. |
| Pier | Docking berth; related systems handle loading | Arrival/departure hub: unload, provision, select crew, prepare ship. |
| Palisade | Obstacle that can be breached | Delay and channel attackers rather than grant invulnerability. |
| Double gate | Friendly passage through walls; excludes raiders | Deliberate entrance, defensive weak point, readable damage and repairs. |

The separate legacy Storehouse plan should preferably become the storage hut's
upgrade identity instead of a competing family of storage buildings.

## Observations To Recheck Before Implementation

These reflect the working files on this date, not a guarantee about later code:

- Kitchen creates Meals, but settlement upkeep consumes raw Food. No Meals
  consumption path was found. Cooking can deplete the stock that feeds residents.
- Shelter description promises four residents; its houses field is two.
- The discussed five-stage progression is not yet implemented: Techs has two
  campfire levels and selected level-two building upgrades.
- Building costs are temporarily capped for playtesting, not final balance.
- Generated PRODUCTION-CHAINS.md trails the live sawmill recipe. Read Recipe.cs
  as the authority until the generated document is refreshed.

Relevant sources: BuildPlan.cs, OutpostLedger.cs, Economy/Recipe.cs,
Economy/Techs.cs under Assets/_Project/Scripts/World; Combat/WatchtowerGun.cs;
Ship/HullIntegrity.cs and Ship/Shipyard.cs.

## Recommended Additions

1. Shipwright's yard: major repairs, refitting, ship improvements beside the pier.
   Keep emergency repairs available elsewhere. Give the player a reason to return.
2. Fishing hut: coastal alternative to farming, requiring labour and shoreline.
   Initially feed the existing Food resource; bounded output rather than unlimited food.
3. Training yard: only once individual defender roles make it useful. Preparation
   competes with production for workers; simple equipment and practice area at level one.
4. Trading post: later, limited exchanges and exploration-driving requests.
   Unlimited purchasing must not replace sailing and gathering.

Put provision packing in the kitchen, expedition planning at the pier, recruitment
at campfire/shelters, and hauling priorities in storage. These need not each become
a separate building. Defer tavern, infirmary, tannery, and separate smelter until
their supporting mechanics justify them.

Recommended sequence: connect Meals to feeding; make the pier an expedition hub;
add the shipwright; add fishing.

## Kevin's Modular Ship Vision

New discussion, not yet agreed mechanics or implementation:

- A dedicated view for building ships and swapping parts.
- Add decks, increasing height and top-heaviness; compensate through hull design,
  such as a deeper body.
- Add lengthwise sections and vary width: long narrow ships or short broad ships.
- Choose propulsion independently of hull size, including a large paddle wheel
  on a small boat for speed, or a large heavily armed ship that moves slowly.
- Deck sections have defined cannon placement spaces.
- Earlier art requirement still relevant: interchangeable stern paddle-wheel
  assembly integrated cleanly into the hull, not a barrel attached to the back.

Open questions include module boundaries, editable hull dimensions, how stability
and displacement are represented, and the balance between free placement and
authored sockets. Kevin explicitly asked to discuss the vision, not implement it.
