#!/bin/zsh
# Headless self-test of the save-slot system's PURE decision logic -- no
# Unity, no engine references at all, unlike tools/modular-selftest.sh.
#
# SaveSlotLogic.cs (Assets/_Project/Scripts/Save/SaveSlotLogic.cs) is
# deliberately UnityEngine-free: no Application.persistentDataPath, no
# Debug.Log, no JsonUtility. Everything it does takes plain data in
# (strings, bools, DateTimes, delegates) and hands plain data back, so this
# script compiles it with plain csc against nothing but netstandard and
# runs the gates in tools/save-slots-selftest/Main.cs against synthetic
# data. Exit code 0 = every gate passed.
#
# This does NOT exercise SaveSlots.cs itself (the Unity-facing half: actual
# file I/O, JsonUtility, Application.isPlaying, the restore coroutine) --
# that needs the engine. See Assets/_Project/Scripts/Dev/SaveSlotsProbe.cs
# for the play-mode gate on that half (manual save/overwrite/delete/rename,
# migration against real files, Suppressed blocking a write).
set -u
P="$(cd "$(dirname "$0")/.." && pwd)"
U=/Applications/Unity/Hub/Editor/6000.4.3f1/Unity.app/Contents
DOTNET=$U/Resources/Scripting/NetCoreRuntime/dotnet
CSC=$U/Resources/Scripting/DotNetSdkRoslyn/csc.dll
O="$(mktemp -d)"
trap 'rm -rf "$O"' EXIT

for f in "$DOTNET" "$CSC"; do
  [ -e "$f" ] || { echo "missing: $f (Unity version moved?)"; exit 2; }
done

"$DOTNET" "$CSC" -nologo -t:exe -nostdlib -noconfig -langversion:9.0 -nowarn:0162,0168,0219,0414,0649,0169,0436 \
  -r:$U/Resources/Scripting/NetStandard/ref/2.1.0/netstandard.dll \
  -out:$O/t.exe "$P"/Assets/_Project/Scripts/Save/SaveSlotLogic.cs "$P"/tools/save-slots-selftest/Main.cs > $O/build.log 2>&1
if grep -q "error CS" $O/build.log; then
  grep "error CS" $O/build.log | sed "s|$P/||"
  echo "BUILD FAILED"
  exit 2
fi

echo '{ "runtimeOptions": { "tfm": "net8.0", "framework": { "name": "Microsoft.NETCore.App", "version": "8.0.16" } } }' > $O/t.runtimeconfig.json
"$DOTNET" $O/t.exe
exit $?
