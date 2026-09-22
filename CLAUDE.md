# SeaSick — Unity 3D Game

## Project facts
- **Engine:** Unity 6000.4.3f1 (Unity 6), Universal Render Pipeline (URP), 3D
- **Target platform:** **Kevin's iPhone 16 Pro, portrait, one thumb, is the primary playtest target** (2026-09-22: "from here on out everything should be built for the iphone since that is where I will want to playtest it the most"). Landscape desktop (1920x1080, keyboard + mouse) stays supported and must keep working, but a design decision is made for the phone first and checked on the desktop second. Check both shapes: 1080x2340 *and* 1920x1080 (`RunProbe.ViewPhone()` / `ViewDesk()`, then `HudOverlapProbe`). The HUD has no "mode" — `HudLayout` reads the window's own shape and re-lays out. Touch first: no hover, no tiny buttons, thumb-reachable controls at the bottom; keyboard is the desktop override, never the only path. Phone build: `tools/build-ios.sh --dev` then Run from Xcode (see `docs/DEV-TOOLS.md`, "the game on an iPhone"). Keep the mobile URP asset (`Mobile_RPAsset`) in mind for performance.
- **Core design rule:** sailing feel is pillar #1; seasickness rate is driven by sailing smoothness (see GDD §6)
- **Editor binary:** `/Applications/Unity/Hub/Editor/6000.4.3f1/Unity.app/Contents/MacOS/Unity`
- **Input:** Unity Input System package (`com.unity.inputsystem`), not legacy Input Manager
- **Design doc:** `docs/GDD.md` — read it before implementing gameplay features; keep it updated when design decisions change

## AI workflow (important)
This project is built with a split AI workflow to conserve credits:
- **Claude Code:** architecture, C# systems, git, debugging, code review, project infrastructure, and (since 2026-09-21) scene wiring, visual checks and probe runs through the Unity CLI
- **Unity CLI (`~/.unity/bin/unity`, on PATH via `.zshrc`)** is the editor bridge. It talks to the OPEN editor through the `com.unity.pipeline` package (pinned in `Packages/manifest.json`). Set `UNITY_NO_BANNER=1 UNITY_NON_INTERACTIVE=1 UNITY_NO_PAGER=1` for scripted use. Key commands: `unity status`, `unity list` (discover commands; never guess names), `unity cmd eval --json --code '<C#>'` (runs against the project's compiled assemblies, so `RunProbe.X()` launchers work as-is), `unity cmd console --json level=error tail=N`, `unity cmd console_status --json` (compile-failure flag), `unity cmd recompile` + `recompile_status`, `editor_play` / `editor_stop`, `capture_game_view` / `capture_scene_view` (PNG), `run_tests`, `batch` (transactional scene edits). Do not launch a second batch-mode editor against the open project.
- **Coplay** is being retired (Kevin is cancelling the subscription). Its MCP tools may still appear while the plugin package is installed; do not depend on them. Its only capability the CLI lacks is generative asset creation, which this project never used (models come from Blender).
- **`docs/DEV-TOOLS.md`** — index of every probe and setup script, the edit/verify loop, and the measurement traps this project has actually hit. Read it before writing a new probe or trusting a measurement.

## Folder structure
All project-authored content lives under `Assets/_Project/`:
- `Scripts/` — C# code (namespace `SeaSick`), organized by feature subfolder
- `Scenes/` — game scenes (template scenes live in `Assets/Scenes/` until replaced)
- `Prefabs/`, `Materials/`, `Art/`, `Audio/`, `Animation/`, `Settings/`
Third-party/imported assets stay out of `_Project`.

## Conventions
- Namespace all game code under `SeaSick` (feature sub-namespaces like `SeaSick.Player`)
- One MonoBehaviour per file, filename matches class name
- Prefer `[SerializeField] private` fields over public fields
- Use the Input System's generated C# wrapper or `InputActionAsset` references, never `UnityEngine.Input`
- ScriptableObjects for shared config/tuning data, saved in `_Project/Settings`

## Git rules
- `main` is the working branch; commit after each verified milestone, small commits
- Never commit `Library/`, `Temp/`, `Logs/`, `UserSettings/` (gitignored)
- Binary assets (models, textures, audio) go through Git LFS (see `.gitattributes`)
- `git-lfs` and `gh` are installed at `~/.local/bin` (add to PATH if missing: `export PATH="$HOME/.local/bin:$PATH"`)
- Always verify the project compiles (`unity cmd console_status --json` compile flag / `tools/compilecheck.sh`, or the batch-mode run below when the editor is closed) before committing

## Headless validation
Run a compile/import check without opening the editor GUI:
```
"/Applications/Unity/Hub/Editor/6000.4.3f1/Unity.app/Contents/MacOS/Unity" -batchmode -quit -projectPath /Users/kevinandersson/Desktop/SeaSick -logFile /tmp/seasick-build.log
```
Exit code 0 = clean. Compile errors appear in the log near "Scripts have compiler errors".

## Known quirks
- Template shipped with Input System 1.12.0 which fails to compile on 6000.4.3 (`BuildTarget.ReservedCFE` error); pinned to 1.14.2 in `Packages/manifest.json`. Don't downgrade it.
