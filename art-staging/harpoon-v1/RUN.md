# Harpoon v1: run order (headless)
Rules:
- Run one Blender at a time.
- Before each run, check that `df -h /System/Volumes/Data` shows at least 1 GB free.
- Run nothing while Unity is in play mode (the Mac has 8 GB).

Each step writes only into `art-staging/harpoon-v1/`. The mill blend, kit.json and cannon.fbx are opened read-only and never saved.

zsh note: type the arguments literally after `--` (for example `-- only barb`). Don't pass them through an unquoted variable: zsh does not word-split it, so the script would get one argument instead of two.

From `/Users/kevinandersson/Desktop/SeaSick`:

1. **Build** → `HarpoonMount.fbx` (with the gun-lamp), `HarpoonBarb.fbx`, `harpoon-v1.blend`. Takes about 2 s and ~300 MB. It prints `BUILD_OK mount tris N barb tris N lamp tris N`, and the Lamp's local position under Swivel.
   ```
   /Applications/Blender.app/Contents/MacOS/Blender -b art-staging/lumber-mill-triangular-lvl1-v2/lumber-mill.blend --python art-staging/harpoon-v1/build.py
   ```
2. **Check** (re-imports the FBXs and sweeps the gun over the real BowLow) → `export-verification.json` + `lamp-verification.json`. Takes about 5 s and ~300 MB. It prints `CHECK … errors 0` and `CHECK_LAMP … errors 0`.
   ```
   /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python art-staging/harpoon-v1/check.py
   ```
3. **Review renders** (Cycles, 16 samples, denoised; real SternLow + BowLow + RotorLow from kit.json and the real cannon at the bow ports). Takes about 45 s and ~1.4 GB peak.
   ```
   /Applications/Blender.app/Contents/MacOS/Blender -b art-staging/harpoon-v1/harpoon-v1.blend --python art-staging/harpoon-v1/render.py
   ```
   To render one image, add `-- only <part-of-name>`, for example `-- only barb` or `-- only phone`.

   It writes:
   - `review-phone-high50.png` (540×1170, about 50° from behind the ship, like the sea camera)
   - `review-hero.png` (the mount on the bow, loaded barb)
   - `review-bow-ports.png` (gun yawed 40°, both bow cannons in view)
   - `review-rope-slack.png`, `review-rope-taut.png`, `review-rope-strained.png` (14 m line to a floating barb, 22° yaw)
   - `review-barb.png`

To abort: Ctrl-C, or `pkill -f "Blender -b"`.

`geo.py` is shared code. It holds the palette, the mesh builder, the read-only kit.json hull loader and the placement constants (`MOUNT_SRC_U`).

## Gun-lamp (Kevin 2026-10-04)
The hooded bullseye lamp on the gun's starboard side is part of `HarpoonMount.fbx` (step 1 builds it; there is no separate mode). The old `-- lantern` mode and `BowLantern.fbx` are gone.
- **Check** (step 2) also writes `lamp-verification.json` and prints `CHECK_LAMP … errors 0`. It covers the hierarchy, the light empty at the lens centre, the lens behind the muzzle, the side away from the crank, and no clipping into the body, the crank over a full turn, the base or a loaded barb. It also sweeps the rope over yaw −45…45 in 1° steps to sea-level targets at 10–35 m, both straight and through the in-game stem fairlead, on the low and the raised bow. The gate is a clearance of at least 0.1 m.
- **Renders**: `-- only lamp` writes `review-lamp-close.png` (3/4 front, 1000×1000), plus `review-lamp-phone-high50.png` and `review-lamp-phone-yaw30.png` (the sea camera at 1080×2340). Every other shot also shows the lamp.
  ```
  /Applications/Blender.app/Contents/MacOS/Blender -b art-staging/harpoon-v1/harpoon-v1.blend --python art-staging/harpoon-v1/render.py -- only lamp
  ```
