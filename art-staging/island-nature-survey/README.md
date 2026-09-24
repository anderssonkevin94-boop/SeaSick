# Island 2: Nature Dressing Survey

> Direction update: the user approved the nature direction but requested that
> design ignore all buildings and represent the island before habitation.
> Settlement-based zoning and the landing-corridor pilot below are historical
> survey suggestions, superseded by this requirement. Use natural geology,
> exposure and vegetation patterns instead. New assets are staged in
> `../island-nature-kit-v1/`; no baseline roads or building clearings.

## Inspected State

Inspected the iPhone snapshot saved 2026-09-24 16:09, copied to
`/tmp/seasick-island-survey-phone.json`. The desktop save contains only the
empty home island and was not replaced. The phone snapshot was restored only
into a temporary Unity Play session with SaveGame.Suppressed verified true.
The session was stopped afterward. Desktop save byte comparison passed.

Island_2 is centred at world X=660, Z=81, with measured maximum radius 358 m.
Its built list contains Campfire, Pier, Storage, Farm, Blacksmith, Sawmill and
Fletcher. The rendered snapshot also shows palisades, gates and construction
ghosts. Buildings, walls, resources and home-island designation were not edited.

- island-overview.png: actual Unity overhead render, north (+Z) at top.
- settlement-current.png: actual Unity oblique render from the northern sea.
- terrain-survey.txt: partial terrain samples around the settlement.
- Survey.cs: external in-memory inspection script, not a game asset or runtime feature.

## Read of the Island

The island has a broad interior, a long southwestern extension, an eastern
headland, and a small northern bay east of the developed landing. The northern
settlement sits above cliffs, with a pier below and palisades enclosing an open
work area. Grey exposed terrain winds through the interior. These grey bands
are not verified navigation paths: do not treat all of them as roads.

Current vegetation reads as scattered isolated specimens and oversized rounded
crowns. Small props do not visually join the trees, rocks and ground. Cliff
faces and sharp grass-to-rock transitions dominate the landing view. Nature
props alone will not fix the terrain's visibly stepped edges at distance.

## Dressing Zones

1. Settlement and landing: retain open ground inside the walls and an unobscured
   pier-to-gate-to-storage route. Confirm actual crew routes before painting
   worn earth. Put low grass and shrubs beside, not across, routes. Keep future
   building space and all construction ghosts clear. Reserve a few trees at
   the perimeter; avoid crowns hiding workstations from the gameplay camera.
2. Interior woodland: use approved hornbeam and broadleaf as the dominant mix,
   with slender/uneven variants within groves and young trees at their edges.
   Add occasional oak anchors and small birch groups. Leave connected clearings
   rather than spreading every species equally over the island.
3. Higher ridges and exposed ends: sparse approved pine and coastal forms,
   low scrub, larger partly embedded rock masses, and dry grass. Let bare ground
   and sky separate silhouettes. Do not fill every slope with trees.
4. Sheltered sandy bay east of the landing: small irregular groups of approved
   palms above the wet shoreline, dune grass and a few washed stones. Preserve
   open sand and landing room. Palms should not appear uniformly around all coasts.
5. Cliff feet and rock outcrops: use the approved 52-triangle boulder family,
   plus a few elongated fragments in coherent size groups, to ease the transition
   into earth or sand. Large shapes should follow the cliff's orientation.

## Ground Treatment

Adapt the approved forest-ground-palette-v2 vocabulary: mossy greens beneath
groves, irregular exposed earth near roots and rocks, lighter dry clearings,
and narrow worn earth where real crew traffic exists. Blend by terrain slope,
height and local vegetation density, not independent circular stains under
every object. Reuse the terrain shading/colour system where feasible rather
than covering the island with individual decal objects.

## Missing Assets, In Priority Order

- Cliff transition kit: a broad low ledge, an elongated broken slab and a small
  talus cluster. Shared rock palette and deliberate low-poly silhouettes.
- Coastal groundcover: one sparse dune-grass fan and one squat wind-shaped shrub.
- Shore accents: a washed-stone group and a forked driftwood piece.
- Woodland accents: a low fern/bracken clump and one fallen trunk. Existing tree
  stumps already cover harvested-state basics; do not make duplicate stump families.

Use the approved V4 combined forest family (14 trees and matching stumps),
which retains V2 and adds palms. Exclude the rejected V3 additions. Use the
middle-weight groundcover study and its newer boulders, not the rejected old
stone-resource pack. No new tree family is needed for the first pass.

## Resource and Mobile Constraints

Keep decoration distinct from gatherable stone and timber. Do not relocate,
replace or create resource nodes until their identity, remaining yield,
depletion and regrowth are mapped to the save system. Decorative pebbles and
cliff slabs must not misleadingly read as plentiful harvestable stone.

Trees in the approved pack are under 250 triangles each, but triangle count
alone is not a performance guarantee. Reuse meshes and shared materials; use
instancing where supported. Reduce small groundcover and shadows with distance,
and provide cheaper distant crowns if profiling warrants them. Do not add
colliders to decorative grass. Ensure reachable tree trunks and resource nodes.

## Proposed First Test

Dress only the landing-to-settlement corridor and one adjoining forest edge.
Leave terrain heights, buildings, walls, ship docking and save data unchanged.
Review both portrait gameplay and close views, then profile on the iPhone
before expanding the treatment to the rest of this island and its neighbours.
The user has not yet approved implementation of this plan.
