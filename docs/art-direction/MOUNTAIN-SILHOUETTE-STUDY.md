# Mountain Silhouette Study

Stage one is a Blender-only proportion study for the connected mountain form. It keeps the existing plateau source and gameplay data untouched, and produces no Unity runtime export.

The intended read is an off-centre crown set toward the right and rear, reached by a long, gradual west shoulder. The east side should descend quickly into two low promontories. The crown should feel broad, blunt, and irregular, with a sloping saddle rather than a level terrace. The current refinement rebuilds the crown at higher topology with chamfered corners and an upper bevel, retaining the authored silhouette while giving the summit a less messy natural break-up. Explicit face-domain terrain zones preserve the intended grass/rock pattern as topology changes.

Review the isolated `mountain-silhouette-study.blend` source together with four renders:

- sea view clay render
- gameplay view clay render
- reverse view clay render
- sea view ink outline

The clay views establish the connected mass and facet rhythm. The outline isolates the skyline so lighting cannot compensate for weak proportions.

Stage-one review criteria:

1. From the sea view, the crown is visibly offset right and rear rather than centered.
2. The west shoulder reads as a long, steady rise into the crown.
3. The east descent is clearly shorter and steeper, ending in two readable low promontories.
4. The silhouette remains a single connected landform with a broad, irregular crown.
5. The reverse and gameplay views preserve the same directional story without revealing a flat terrace or accidental symmetry.
6. The clay facet pattern supports the form while the outline remains legible on its own.

The refined source currently contains 133 vertices and 252 triangles. Blender geometry QA found no negative or zero projected-area triangles (minimum projected area 0.1975). This document records visual intent and review criteria only. Passing this study is not gameplay validation and makes no claims about runtime rendering, collision, height queries, streaming, or player traversal; Unity runtime remains untested.
