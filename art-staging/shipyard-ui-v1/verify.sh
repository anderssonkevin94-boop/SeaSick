#!/bin/bash
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
P="${1:-/Users/kevinandersson/Desktop/SeaSick-modular}"
REF_PROJECT="${2:-/Users/kevinandersson/Desktop/SeaSick}"
U=/Applications/Unity/Hub/Editor/6000.4.3f1/Unity.app/Contents
DOTNET="$U/Resources/Scripting/NetCoreRuntime/dotnet"
CSC="$U/Resources/Scripting/DotNetSdkRoslyn/csc.dll"
CORE="$U/Resources/Scripting/Managed/UnityEngine/UnityEngine.CoreModule.dll"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

find "$P/Assets" -name '*.cs' -not -path '*/Editor/*' -not -path '*/worktrees/*' -not -path '*/UI/ModularYard/*' | while IFS= read -r source; do
  ui="$HERE/integration/${source#"$P/Assets/_Project/Scripts/UI/"}"
  ship="$HERE/integration-ship/${source#"$P/Assets/_Project/Scripts/Ship/"}"
  if [ -f "$ui" ]; then source="$ui"; elif [ -f "$ship" ]; then source="$ship"; fi
  printf '"%s"\n' "$source"
done > "$TMP/sources.rsp"
find "$HERE/Runtime" -name '*.cs' | sed 's/.*/"&"/' >> "$TMP/sources.rsp"
{
  for d in "$U/Resources/Scripting/NetStandard/ref/2.1.0/"*.dll "$U/Resources/Scripting/Managed/UnityEngine/"*.dll; do printf '/r:"%s"\n' "$d"; done
  for d in "$REF_PROJECT/Library/ScriptAssemblies/"*.dll; do
    case "$(basename "$d")" in Assembly-CSharp*.dll) continue;; esac
    printf '/r:"%s"\n' "$d"
  done
} > "$TMP/refs.rsp"
if ! "$DOTNET" "$CSC" -nologo -target:library -nostdlib+ -noconfig -langversion:9.0 -unsafe+ -out:"$TMP/check.dll" "@$TMP/refs.rsp" "@$TMP/sources.rsp" > "$TMP/compile.log" 2>&1; then cat "$TMP/compile.log"; exit 1; fi
find "$HERE/Editor" -name '*.cs' | sed 's/.*/"&"/' > "$TMP/editor.rsp"
for d in "$U/Resources/Scripting/Managed/UnityEditor/"*.dll "$U/Managed/UnityEditor.dll"; do
  [ -f "$d" ] && printf '/r:"%s"\n' "$d"
done >> "$TMP/refs.rsp"
"$DOTNET" "$CSC" -nologo -target:library -nostdlib+ -noconfig -langversion:9.0 -out:"$TMP/editor.dll" -r:"$TMP/check.dll" "@$TMP/refs.rsp" "@$TMP/editor.rsp"
echo 'Runtime and editor UI compile: PASS'

"$DOTNET" "$CSC" -nologo -t:exe -nostdlib -noconfig -langversion:9.0 -nowarn:0162,0168,0219,0414,0649,0169,0436 \
  -r:"$U/Resources/Scripting/NetStandard/ref/2.1.0/netstandard.dll" -r:"$CORE" \
  -out:"$TMP/test.exe" \
  "$P/Assets/_Project/Scripts/Ship/Modular/ModularJson.cs" \
  "$P/Assets/_Project/Scripts/Ship/Modular/ModularScale.cs" \
  "$P/Assets/_Project/Scripts/Ship/Modular/ModuleLibrary.cs" \
  "$P/Assets/_Project/Scripts/Ship/Modular/ModuleSchema.cs" \
  "$P/Assets/_Project/Scripts/Ship/Modular/ShipAssembler.cs" \
  "$P/Assets/_Project/Scripts/Ship/Modular/ShipConfiguration.cs" \
  "$P/Assets/_Project/Scripts/Ship/Modular/ShipHydrostatics.cs" \
  "$P/tools/modular-selftest/JsonShim.cs" "$HERE/Runtime/ShipyardDraft.cs" "$HERE/Tests/DraftTests.cs"
cp "$CORE" "$TMP/"
printf '%s\n' '{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"8.0.16"}}}' > "$TMP/test.runtimeconfig.json"
"$DOTNET" "$TMP/test.exe" "$P"
