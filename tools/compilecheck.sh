#!/bin/bash
# Compile-check Assembly-CSharp WITHOUT the Unity editor.
#
# Why this exists: the normal loop is edit -> execute_script RefreshOnly ->
# check_compile_errors, and it dies whenever the Coplay bridge drops -- which
# it does, silently, usually right after a domain reload. Unity then does not
# even pick the edits up (auto-refresh only runs when the editor regains
# focus), so Library/ScriptAssemblies goes stale and there is no way to tell a
# clean edit from a broken one. This runs Unity's own Roslyn against Unity's
# own reference assemblies, so a green result here means the same thing a
# green check_compile_errors does.
#
# Covers C# only. Shaders are invisible to it, exactly as they are to
# check_compile_errors -- for those, grep Editor.log for "Shader error in".
#
# Usage:  tools/compilecheck.sh            # the whole runtime assembly
set -u

P="$(cd "$(dirname "$0")/.." && pwd)"
U="/Applications/Unity/Hub/Editor/6000.4.3f1/Unity.app/Contents"
DOTNET="$U/Resources/Scripting/NetCoreRuntime/dotnet"
CSC="$U/Resources/Scripting/DotNetSdkRoslyn/csc.dll"
REF="$(ls -d "$U/Resources/Scripting/NetStandard/ref/"*/ 2>/dev/null | head -1)"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

for f in "$DOTNET" "$CSC"; do
  [ -e "$f" ] || { echo "missing: $f (Unity version moved?)"; exit 2; }
done

# Assembly-CSharp is every .cs under Assets that is NOT in an Editor folder.
# Paths are quoted: this project has folders with spaces and a '#' in them.
find "$P/Assets" -name "*.cs" -not -path "*/Editor/*" -not -path "*/worktrees/*" \
  | sed 's/.*/"&"/' > "$TMP/sources.rsp"
{
  for d in "$REF"*.dll;                                   do echo "/r:\"$d\""; done
  for d in "$U/Resources/Scripting/Managed/UnityEngine/"*.dll; do echo "/r:\"$d\""; done
  for d in "$P/Library/ScriptAssemblies/"*.dll; do
    case "$(basename "$d")" in Assembly-CSharp*.dll) continue;; esac
    echo "/r:\"$d\""
  done
} > "$TMP/refs.rsp"

"$DOTNET" "$CSC" -nologo -target:library -nostdlib+ -noconfig -langversion:9.0 -unsafe+ \
  -out:"$TMP/check.dll" "@$TMP/refs.rsp" "@$TMP/sources.rsp" > "$TMP/out.log" 2>&1

n=$(grep -c "error CS" "$TMP/out.log")
echo "sources $(wc -l < "$TMP/sources.rsp" | tr -d ' ')   refs $(wc -l < "$TMP/refs.rsp" | tr -d ' ')   errors $n"
if [ "$n" -gt 0 ]; then
  grep "error CS" "$TMP/out.log" | sed "s|$P/||" | sort -u | head -40
  exit 1
fi
echo "clean."
