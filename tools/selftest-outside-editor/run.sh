#!/bin/zsh
# Run plain-C# self-tests WITHOUT the Unity editor, at the working tree or at
# any commit.
#
#   tools/selftest-outside-editor/run.sh [-c <commit>] [-k] <Namespace.Type.Method> [...]
#
#   tools/selftest-outside-editor/run.sh SeaSick.World.StationStockSelfTest.Run
#   tools/selftest-outside-editor/run.sh -c 0edd86c9 SeaSick.World.StationStockSelfTest.Run
#
# What it does: compiles Assembly-CSharp (every .cs under Assets that is not
# in an Editor folder) with Unity's own Roslyn, the editor build's defines and
# reference DLLs (same recipe as tools/compilecheck.sh), from this checkout or
# from `git archive <commit>`; then runs each named static, argument-less
# method on Unity's bundled .NET via Host.cs. Exit code = failed methods
# (2 = build failed). -k keeps the temp dir and prints it.
#
# Limits: only MANAGED Unity code runs. UnityEngine.JsonUtility is swapped for
# the managed stand-in in tools/modular-selftest/JsonShim.cs (compiled into
# the assembly, CS0436 silenced); Debug.Log goes to stdout; Resources.Load and
# other native calls throw (code that catches it falls back to defaults, e.g.
# the *Tuning assets). A self-test that needs a scene, a ScriptableObject
# instance or a native module will report THREW here -- run it in the editor.
# Static state starts fresh every run (like a domain reload).
#
# Bisecting: `-c` compiles that commit's sources against TODAY's Library
# reference DLLs; far-back commits can fail to build if a package API moved.
set -u
P="$(cd "$(dirname "$0")/../.." && pwd)"
H="$P/tools/selftest-outside-editor"
U=/Applications/Unity/Hub/Editor/6000.4.3f1/Unity.app/Contents
DOTNET="$U/Resources/Scripting/NetCoreRuntime/dotnet"
CSC="$U/Resources/Scripting/DotNetSdkRoslyn/csc.dll"
REF="$(ls -d "$U/Resources/Scripting/NetStandard/ref/"*/ 2>/dev/null | head -1)"
M="$U/Resources/Scripting/Managed/UnityEngine"

COMMIT=""; KEEP=0
while [ $# -gt 0 ]; do
  case "$1" in
    -c) COMMIT="$2"; shift 2;;
    -k) KEEP=1; shift;;
    -h|--help) sed -n 2,26p "$0"; exit 0;;
    *) break;;
  esac
done
[ $# -ge 1 ] || { sed -n 2,9p "$0"; exit 99; }

O="$(mktemp -d)"
if [ $KEEP -eq 1 ]; then echo "temp dir: $O"; else trap 'rm -rf "$O"' EXIT; fi

# Library: this checkout's, or (in a worktree) the main checkout's.
LIB="$P/Library"
if [ ! -d "$LIB/ScriptAssemblies" ]; then
  common="$(cd "$P" && cd "$(git rev-parse --git-common-dir 2>/dev/null)" 2>/dev/null && pwd)"
  [ -n "$common" ] && [ -d "$(dirname "$common")/Library/ScriptAssemblies" ] && LIB="$(dirname "$common")/Library"
fi
[ -d "$LIB/ScriptAssemblies" ] || { echo "missing Library/ScriptAssemblies (open the project in Unity once)"; exit 2; }

# Sources: the working tree, or the commit's Assets.
SRC="$P"
if [ -n "$COMMIT" ]; then
  SRC="$O/src"; mkdir -p "$SRC"
  git -C "$P" archive "$COMMIT" -- ':(glob)Assets/**/*.cs' | tar -x -C "$SRC" || { echo "git archive $COMMIT failed"; exit 2; }
  echo "sources: $COMMIT ($(git -C "$P" log -1 --format='%h %ad %s' --date=short "$COMMIT" | cut -c1-90))"
else
  echo "sources: working tree $P"
fi
( cd "$SRC" && find Assets -name "*.cs" -not -path "*/Editor/*" -not -path "*/worktrees/*" ) | sed "s|.*|\"$SRC/&\"|" > "$O/sources.rsp"
echo "\"$P/tools/modular-selftest/JsonShim.cs\"" >> "$O/sources.rsp"

rsp="$(ls -t "$LIB"/Bee/artifacts/*E.dag/Assembly-CSharp.rsp 2>/dev/null | head -1)"
if [ -n "$rsp" ]; then grep '^-define:' "$rsp" > "$O/defines.rsp"
else printf -- '-define:%s\n' UNITY_EDITOR DEBUG TRACE UNITY_IOS ENABLE_INPUT_SYSTEM UNITY_6000_4_OR_NEWER > "$O/defines.rsp"; fi
{
  for d in "$REF"*.dll; do echo "/r:\"$d\""; done
  for d in "$M/"*.dll; do echo "/r:\"$d\""; done
  for d in "$LIB/ScriptAssemblies/"*.dll; do
    case "$(basename "$d")" in Assembly-CSharp*.dll) continue;; esac
    echo "/r:\"$d\""
  done
} > "$O/refs.rsp"

"$DOTNET" "$CSC" -nologo -target:library -nostdlib+ -noconfig -langversion:9.0 -unsafe+ -nowarn:0436 \
  -out:"$O/Assembly-CSharp.dll" "@$O/defines.rsp" "@$O/refs.rsp" "@$O/sources.rsp" > "$O/build.log" 2>&1
if grep -q "error CS" "$O/build.log"; then
  grep "error CS" "$O/build.log" | sed "s|$SRC/||; s|$P/||" | sort -u | head -20; echo "BUILD FAILED"; exit 2
fi

"$DOTNET" "$CSC" -nologo -t:exe -nostdlib -noconfig -langversion:9.0 \
  -r:"$REF"netstandard.dll -r:"$M/UnityEngine.CoreModule.dll" -out:"$O/t.exe" "$H/Host.cs" > "$O/host.log" 2>&1 \
  || { cat "$O/host.log"; echo "HOST BUILD FAILED"; exit 2; }

# Runtime: Unity's managed modules + package assemblies next to the exe.
cp "$M/"*.dll "$O/"
for d in "$LIB/ScriptAssemblies/"*.dll; do
  case "$(basename "$d")" in Assembly-CSharp*.dll) continue;; esac
  cp "$d" "$O/"
done
NETV="$(ls "$U/Resources/Scripting/NetCoreRuntime/shared/Microsoft.NETCore.App/" | sort -V | tail -1)"
echo "{ \"runtimeOptions\": { \"tfm\": \"net8.0\", \"framework\": { \"name\": \"Microsoft.NETCore.App\", \"version\": \"$NETV\" } } }" > "$O/t.runtimeconfig.json"

( cd "$O" && "$DOTNET" t.exe "$O/Assembly-CSharp.dll" "$@" )
