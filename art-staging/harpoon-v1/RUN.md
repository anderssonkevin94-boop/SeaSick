# Harpoon v1: run order (headless)
Rules:
- Run one Blender at a time.
- Before each run, check that `df -h /System/Volumes/Data` shows at least 1 GB free.
- Run nothing while Unity is in play mode (the Mac has 8 GB).

Each step writes only into `art-staging/harpoon-v1/`. The mill blend, kit.json and cannon.fbx are opened read-only and never saved.

zsh note: type the arguments literally after `--` (for example `-- only barb`). Don't pass them through an unquoted variable: zsh does not word-split it, so the script would get one argument instead of two.

From `/Users/kevinandersson/Desktop/SeaSick`:

1. **Build** → `HarpoonMount.fbx`, `HarpoonBarb.fbx`, `harpoon-v1.blend`. Takes about 2 s and ~300 MB. It prints `BUILD_OK mount tris N barb tris N`.
   ```
   /Applications/Blender.app/Contents/MacOS/Blender -b art-staging/lumber-mill-triangular-lvl1-v2/lumber-mill.blend --python art-staging/harpoon-v1/build.py
   ```
2. **Check** (re-imports the FBXs and sweeps the gun over the real BowLow) → `export-verification.json`. Takes about 5 s and ~300 MB. It prints `CHECK … errors 0`.
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

## Bow lantern on a beam (Kevin 2026-10-04)

Same rules (one Blender, at least 1 GB free, no play mode at the same time).

1. **Build** → `BowLantern.fbx` only (the mount/barb FBXs are left untouched; the blend gets all three). Prints `LANTERN kit islands kept 8` and `lantern tris N (budget 600)`.
   ```
   /Applications/Blender.app/Contents/MacOS/Blender -b art-staging/lumber-mill-triangular-lvl1-v2/lumber-mill.blend --python art-staging/harpoon-v1/build.py -- lantern
   ```
2. **Check** (the plain check command above). With `BowLantern.fbx` present it also writes `lantern-verification.json` and prints `CHECK_LANTERN … errors 0`: hierarchy and names, pivot, ≤ 600 tris, kit winding kept, line clearance to beam/chain/lantern over yaw −45…45 (1° steps within ±6°), targets at 10/35 m at sea level, +1.5 m and +3 m, straight and with the taut sag, rope radius, lantern swung in a 10° cone (20° and 34° reported), lantern ahead of the hull front, below the muzzle, beam root inside the stem, swing contacts.
3. **Renders**: `-- only lantern` writes `review-lantern-phone-high50.png`, `review-lantern-side-deadahead.png`, `review-lantern-close.png` (taut line 20 m dead ahead). Every other review shot also shows the new lantern once it is built.
   ```
   /Applications/Blender.app/Contents/MacOS/Blender -b art-staging/harpoon-v1/harpoon-v1.blend --python art-staging/harpoon-v1/render.py -- only lantern
   ```
