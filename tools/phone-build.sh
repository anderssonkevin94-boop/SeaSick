#!/bin/bash
# One-shot phone build: quit the editor, headless iOS build, xcodebuild, install over
# USB/Wi-Fi (phone unlocked), supervised launch (alive checks + crash logs), reopen the editor.
# Refuses to launch if the install failed (a stale app would otherwise open). 2026-09-27.
set -u
cd /Users/kevinandersson/Desktop/SeaSick
export PATH="$HOME/.local/bin:$PATH" UNITY_NO_BANNER=1 UNITY_NON_INTERACTIVE=1 UNITY_NO_PAGER=1
U=00008140-001069E421DB801C
APP=Builds/DerivedData/Build/Products/ReleaseForRunning-iphoneos/SeaSick.app
echo "== quit editor $(date +%T)"
~/.unity/bin/unity cmd eval --json --code 'UnityEditor.EditorApplication.Exit(0); return "bye";' >/dev/null 2>&1
for i in $(seq 1 30); do pgrep -if "Contents/MacOS/Unity -projectpath /Users/kevinandersson/Desktop/SeaSick" >/dev/null || break; sleep 3; done
if pgrep -f "Contents/MacOS/Unity -projectPath /Users/kevinandersson/Desktop/SeaSick" >/dev/null || pgrep -f "Contents/MacOS/Unity -projectpath /Users/kevinandersson/Desktop/SeaSick" >/dev/null; then echo "EDITOR STILL OPEN - abort"; exit 1; fi
echo "== unity build $(date +%T)"
tools/build-ios.sh --dev > Builds/build-ios.out 2>&1; echo "build-ios exit $?"
grep -E "Build Finished|result=" Builds/ios-build.log | tail -2
grep -q "Build Finished, Result: Success" Builds/ios-build.log || { echo "UNITY BUILD FAILED"; exit 1; }
echo "== xcodebuild $(date +%T)"
xcodebuild -project Builds/iOS/Unity-iPhone.xcodeproj -scheme Unity-iPhone -configuration ReleaseForRunning -destination id=$U -allowProvisioningUpdates -derivedDataPath Builds/DerivedData -quiet build > Builds/xcodebuild.log 2>&1
X=$?; echo "xcodebuild exit $X $(date +%T)"; [ $X -ne 0 ] && { grep -E "error:" Builds/xcodebuild.log | head -5; exit 1; }
find "$APP/Data/Managed/Metadata/global-metadata.dat" -mmin -20 | grep -q . || { echo "APP NOT FRESH"; exit 1; }
xcrun devicectl device install app --device $U "$APP" > Builds/install.log 2>&1 || { echo "INSTALL FAILED"; tail -5 Builds/install.log; exit 1; }
echo "installed $(date +%T)"
xcrun devicectl device process launch --terminate-existing --device $U com.kevinandersson.seasick 2>&1 | tail -1
for t in 10 10 10 15; do sleep $t; echo "alive $(date +%T): $(xcrun devicectl device info processes --device $U 2>/dev/null | grep -ci seasick)"; done
mkdir -p Builds/crash; xcrun devicectl device copy from --device $U --domain-type systemCrashLogs --source . --destination Builds/crash >/dev/null 2>&1
echo "recent crashes:"; find Builds/crash -iname '*seasick*' -mmin -15
open -a /Applications/Unity/Hub/Editor/6000.4.3f1/Unity.app --args -projectPath /Users/kevinandersson/Desktop/SeaSick
df -h / | tail -1; echo "== done $(date +%T)"
