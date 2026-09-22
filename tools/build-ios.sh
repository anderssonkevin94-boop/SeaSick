#!/bin/bash
# Build the iOS Xcode project headless.
#
#   tools/build-ios.sh          # release-ish player, Xcode project in Builds/iOS
#   tools/build-ios.sh --dev    # development player (profiler + console attach)
#
# Requires the Unity editor to be CLOSED (batchmode cannot share the project
# with an open editor). Then: open Builds/iOS/Unity-iPhone.xcodeproj, pick your
# Apple ID team under Signing & Capabilities, plug the phone in, Run.
set -u
P="$(cd "$(dirname "$0")/.." && pwd)"
UNITY="/Applications/Unity/Hub/Editor/6000.4.3f1/Unity.app/Contents/MacOS/Unity"
LOG="$P/Builds/ios-build.log"
mkdir -p "$P/Builds"

if pgrep -f "Unity.app/Contents/MacOS/Unity" >/dev/null; then
  echo "Unity editor is running -- close it first (batchmode cannot open the same project)."; exit 2
fi

DEV=""
[ "${1:-}" = "--dev" ] && DEV="-development"

echo "building iOS -> $P/Builds/iOS  (log: $LOG)"
"$UNITY" -batchmode -quit -nographics -projectPath "$P" -buildTarget iOS \
  -executeMethod SeaSick.Dev.Build.IOS $DEV -logFile "$LOG"
code=$?
[ -f "$LOG" ] && grep -E "^\[Build\]|error CS|Scripts have compiler errors" "$LOG" | sed 's/^/  /' | head -40
if [ $code -ne 0 ]; then echo "FAILED (exit $code) -- see $LOG"; exit $code; fi
# Unity exits 0 when it never got as far as running our method (e.g. the
# Rosetta 2 dialog on a fresh macOS), so trust the artefact, not the exit code.
if [ ! -d "$P/Builds/iOS/Unity-iPhone.xcodeproj" ]; then
  echo "FAILED: no Xcode project written. Editor output above / in $LOG."; exit 1
fi
echo "ok: open \"$P/Builds/iOS/Unity-iPhone.xcodeproj\""
