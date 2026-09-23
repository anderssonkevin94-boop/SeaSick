# Stern paddle steamer V4

Review asset, not integrated into the production Unity scene. V3 is preserved.

## Changes
- Wider hull, rounder lower sections and fuller stern cheeks.
- Longer raised-deck transition into the integrated wheel opening.
- Stronger teal rails, fewer posts, thicker wheel rims and deeper paddles.
- Straight deck board joints with staggered ends, larger helm and chimney base.
- No people or cannons.

## Files and assembly
Source: `tools/blender/source/stern-paddle-astra-v4.blend`.
Generator: `tools/blender/stern_paddle_astra_v4.py`.
FBX components: `models/`. Fixed pieces share their hull origin.
Paddle_Rotor and Paddle_Frame use axle-local origins. Place both at the
Unity socket position (0, 0.35, -10.83). The rotor remains independently
replaceable; replacement wheels must fit the existing clearance envelope.
The Blender scene includes the assembled socket and rotation preview.

## Verification
6,234 triangles including 1,924 optional plank-seam triangles.
Omitting Plank_Seams yields 4,310 triangles.
All meshes pass open-edge, overconnected-edge and degenerate-face checks.
Independent verification samples rotor/hull intersection at 72 angles.
FBX reimport checks triangle counts and preservation of Col vertex colors.
See validation.json and export-verification.json for actual results.

These tests do not prove absence of every fitting intersection or establish
continuous collision clearance for arbitrary replacement wheels.
Review images use Blender Workbench vertex colors, not the Unity BoatToon
shader. Unity appearance and actual iOS performance remain untested.
