# Fishing Hut L1 - first art review

Canvas-roof shore fishing station: timber frame, teal windbreak, repaired tarp,
net-drying rack, preparation bench, hand-line floats and visible catch crate.
Flat-shaded, vertex-colour geometry. No Unity import or gameplay changes.

## Files and scale

- fishing-hut-lvl1.blend: editable source, saved with the stocked review visible.
- fishing-hut-state-kit.fbx: preferred functional asset; includes all removable
  fish and the work visual. Initialize their visibility from real inventory.
- fishing-hut-empty.fbx: empty static illustration, not the functional master.
- PNGs: front, opposite, empty and 360x300 gameplay-size previews.
- validation.json: triangle counts and topology checks per group.

One Blender unit = one metre. Origin at ground level, front -Y, Z up. FBX uses
Y up / -Z forward. Suggested reserved plot 5.5 x 5.2m; not wired to BuildPlans.
Terrain must support all feet; don't place the whole hut partially over water.
Use Col vertex colours (linear FBX), imported flat normals, one vertex-colour
material. No texture dependencies. All-state kit: 4,668 triangles.

## Stock and work states

Permanent modules: Frame, Windbreak, Canopy, Work_Bench, Gear, Net_Rack,
Output_Crate, Sign. The net bundle and floats are equipment, not stored fish.
Output_Fish_01..04 are independent visual inventory slots. Work_Catch is the
fish on the preparation board. Keep the empty crate visible at zero stock.

On initialization hide all Output_Fish_ and Work_Catch, then show only the
appropriate stock/work state. Visual slots are NOT a proposed gameplay capacity
or recipe yield. Gathering at sea is the conceptual input, not a second crate
of fish; the eventual fishing system owns catches, timing, crew and resources.
When a finished catch cannot be transferred because output is full, retain its
work visual and block the next job. Never count the same fish on the bench and
in storage. No animation or production logic is included.

Worker_Stand, Catch_Anchor, Output_Anchor and Shore_Direction are reference
empties, not validated navigation/reach targets. Validate crew clearance, camera
visibility, collision and shore placement during integration. Stock and work
meshes retain authored root-space placement; cache their resting transforms.

Rebuild source: tools/blender/fishing_hut_astra_lvl1.py in the main SeaSick repo.
Source meshes passed closed/manifold, zero-area and flat-normal checks. Those
checks do not claim boolean-unioned carpentry: beams, straps and joinery remain
separate closed components joined into logical meshes.
