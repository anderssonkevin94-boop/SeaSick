# Food ingredients V1 — review only

Seven ingredients: Potato, Carrot, Onion, Wheat, Apple, Fish, Meat. Each has a standalone metre-scale FBX (`<ID>_Unit.fbx`), a crate-top display (`<ID>_Display.fbx`) and a transparent 256x256 rendered icon (`icons/<ID>.png`). No gameplay assets changed and not added to the approved queue.

Source food-ingredients.blend; generator tools/blender/food_ingredients_v1.py. Use individual FBXs, not the source's review layout. Preserve GameColor, flat normals and the SS_Food_GameColor opaque vertex-color material; no textures needed for 3D ingredients. Food crate displays reuse approved V2 Cargo_Box_Small geometry without redesign; use its existing material mapping. Icons are rendered from these exact ingredient meshes.

## Runtime contracts inspected

CampPiles.ShapeFor routes all FoodBook.IsFoodish items to generic Sacks; DrawPile draws a count-limited sequence and adds one crate at six or more units. BuildUnit/CargoVisual also uses the same shape rules. Integrating this kit means replacing that visual path intentionally, keeping stock quantity/threshold logic and resource identity. Do not draw the entire fixed display for each inventory unit. Units can be repeated in a dynamic pile; each Display has separately named food children that may be toggled. The display is an on-lid arrangement on the already approved closed crate, not a newly approved open crate or a claim that food is inside it. Production layout/counts remain runtime-owned.

VillagerActing currently carries food in sacks. Keep that transport behavior unless separately requested; use approved sacks. These food items do not add recipes or change yields. Fish here is a small food prop, distinct from the previously approved large swimming fish.

ItemIconSet resolves exact Res IDs from Resources/UI/ItemIcons.asset using serialized Texture2D references. On approval/import, place PNGs at Assets/_Project/Art/UI/Icons/Items/<ID>.png and update the matching IDs/references in that registry; copying files alone will not make icons appear. Preserve unrelated entries. 256px RGBA, no background, render-tested at 48px on dark and light backgrounds. Match existing icon import settings, enable alpha, and check compression at actual UI scale. Do not create a separate Resources folder under Art.

## Size / display notes

Source Z-up, metres, origin at base of the food unit; FBX Y-up/-Z-forward. Models are deliberately broad and simplified: warm tan potato, orange tapered carrot, purple onion, gold wheat bundle, red apple, teal/silver fish and red steak with cream fat/bone. Grain/clusters represent readable food units, not literal counted kernels. Stock displays use food at 65% of unit size to fit the original .56 x .47m crate lid. Do not multiply unit models by the old sphere dimensions as a second scale.

Review sheet is independently framed for identification, not equal physical scale. Stock displays and icons are Blender previews, not Unity playtests. Remaining checks: stock quantity transitions, crate/food clearances, actual phone camera visibility, icon transparency and UI states, material/mesh batching and use of existing sacks for hauling. Await approval before queueing.
