# Island dressing study

Blender composition pass built from the approved foreland and mountain study. Reuses the individually authored Storybook flora kit, with seven tree variants, variable scale and rotation, and uneven woodland groups. Open grass approaches and the harbour foreland remain visible. Trees require grass, a sufficiently upward-facing surface, and nearby ground samples within 1.4 m of the planting height.

The scene contains 122 trees, 295 understory objects, 30 shoreline stones and three driftwood objects. Each is a separate instance with `asset_id` and `resource_type` properties; mesh data is shared. Grass and fern vertex colours are blended toward the study meadow palette. These tags support later integration but do not implement harvesting.

Rebuild with `tools/blender/island_dressing.py` through Blender MCP. Output is `tools/blender/source/island-dressed-study.blend`, scene `Island_Dressed_Study`. The bare terrain study remains available separately. Review renders: `island-dressed-overview.png`, `island-dressed-harbour.png`, and `island-dressed-plan.png`. Counts are recorded in `island-dressing-validation.json`.

Overview and harbour renders were visually inspected. Placement checks sample terrain beneath tree origins and nearby points; they are not full mesh-intersection or navigation checks. Unity assets, collision, harvesting and runtime placement have not been changed or validated in this pass. The water remains the study's simple preview material.
