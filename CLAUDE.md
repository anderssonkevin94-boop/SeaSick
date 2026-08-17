# SeaSick — Unity 3D Game

## Project facts
- **Engine:** Unity 6000.4.3f1 (Unity 6), Universal Render Pipeline (URP), 3D
- **Editor binary:** `/Applications/Unity/Hub/Editor/6000.4.3f1/Unity.app/Contents/MacOS/Unity`
- **Input:** Unity Input System package (`com.unity.inputsystem`), not legacy Input Manager
- **Design doc:** `docs/GDD.md` — read it before implementing gameplay features; keep it updated when design decisions change

## AI workflow (important)
This project is built with a split AI workflow to conserve credits:
- **Claude Code:** architecture, C# systems, git, debugging, code review, project infrastructure
- **Coplay (GPT models) inside Unity:** scene wiring, asset placement, visual/UI iteration, asset generation
- Coplay MCP tools are available to Claude Code when the Unity Editor is open with the SeaSick project loaded (`check_compile_errors`, `get_unity_logs`, `play_game`, scene/prefab tools, etc.)

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
- Always verify the project compiles (Coplay `check_compile_errors` or batch-mode run) before committing

## Headless validation
Run a compile/import check without opening the editor GUI:
```
"/Applications/Unity/Hub/Editor/6000.4.3f1/Unity.app/Contents/MacOS/Unity" -batchmode -quit -projectPath /Users/kevinandersson/Desktop/SeaSick -logFile /tmp/seasick-build.log
```
Exit code 0 = clean. Compile errors appear in the log near "Scripts have compiler errors".

## Known quirks
- Template shipped with Input System 1.12.0 which fails to compile on 6000.4.3 (`BuildTarget.ReservedCFE` error); pinned to 1.14.2 in `Packages/manifest.json`. Don't downgrade it.
