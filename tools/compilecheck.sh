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
# Covers BOTH assemblies. Assembly-CSharp-Editor was missing until
# 2026-09-11, and it is where most of this project's tooling lives -- every
# probe launcher, every setup script, every Dev/Editor shot tool. A green run
# that silently skipped all of them is the same shape as the git-bundle check
# that certified a backup missing every piece of art: a check that stops at
# the last thing it can compute stops before the thing that is broken.
#
# Compiles with the SAME scripting defines Unity uses for the editor build of
# each assembly (UNITY_EDITOR, DEBUG, UNITY_IOS, ... ~140 of them), read from
# Unity's own compiler response files in Library/Bee. Until 2026-09-27 it
# passed none, so every `#if UNITY_EDITOR` member was invisible and a probe
# that called one failed here while compiling fine in Unity (three false
# CS0117s in ShipyardRefitProbe).
#
# Safe to run from a git worktree under .claude/worktrees/: sources are found
# relative to the project root, and a worktree with no Library of its own
# borrows the main checkout's reference DLLs and defines.
#
# Usage:  tools/compilecheck.sh            # both assemblies
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

# Library: this checkout's, or -- in a worktree, which has none -- the main
# checkout's (git's common dir sits inside it). Reference DLLs and defines only;
# sources always come from $P.
LIB="$P/Library"
if [ ! -d "$LIB/ScriptAssemblies" ]; then
  common="$(cd "$P" && cd "$(git rev-parse --git-common-dir 2>/dev/null)" 2>/dev/null && pwd)"
  [ -n "$common" ] && [ -d "$(dirname "$common")/Library/ScriptAssemblies" ] \
    && LIB="$(dirname "$common")/Library"
fi
[ -d "$LIB/ScriptAssemblies" ] || { echo "missing: $P/Library/ScriptAssemblies (open the project in Unity once)"; exit 2; }
[ "$LIB" = "$P/Library" ] || echo "no Library here -- borrowing ${LIB%/Library}'s"

# Defines: the -define: lines of Unity's own response file for the EDITOR
# build of the assembly (Library/Bee/artifacts/<hash>E.dag/<asm>.rsp; the
# PDevDbg dag next to it is the player build). Newest wins if Unity has left
# more than one. Without it, fall back to the handful this project's code
# actually branches on, and say so -- a check with the wrong defines is a
# check of different code.
defines() {  # $1 = assembly name, $2 = output .rsp
  local rsp
  rsp="$(ls -t "$LIB"/Bee/artifacts/*E.dag/"$1".rsp 2>/dev/null | head -1)"
  if [ -n "$rsp" ] && grep -q '^-define:UNITY_EDITOR$' "$rsp"; then
    grep '^-define:' "$rsp" > "$2"
    DEFSRC="${rsp#"$LIB"/}"
  else
    printf -- '-define:%s\n' UNITY_EDITOR UNITY_EDITOR_64 UNITY_EDITOR_OSX DEBUG TRACE \
      UNITY_ASSERTIONS UNITY_IOS UNITY_IPHONE PLATFORM_IOS ENABLE_INPUT_SYSTEM \
      UNITY_6000_4_OR_NEWER UNITY_2022_3_OR_NEWER UNITY_5_3_OR_NEWER > "$2"
    DEFSRC="FALLBACK (no $1.rsp in $LIB/Bee -- let Unity compile once)"
  fi
}

# Assembly-CSharp is every .cs under Assets that is NOT in an Editor folder.
# Paths are quoted: this project has folders with spaces and a '#' in them.
# The worktree filter is matched on the path RELATIVE to $P, so it skips a
# worktree nested inside this checkout but not this checkout itself when it
# is one (.claude/worktrees/<name>/ -- matching the absolute path there once
# dropped every source and reported a clean compile of nothing).
( cd "$P" && find Assets -name "*.cs" -not -path "*/Editor/*" -not -path "*/worktrees/*" ) \
  | sed "s|.*|\"$P/&\"|" > "$TMP/sources.rsp"
[ -s "$TMP/sources.rsp" ] || { echo "no sources found under $P/Assets -- refusing to call that clean"; exit 2; }
defines Assembly-CSharp "$TMP/defines.rsp"
echo "defines $(wc -l < "$TMP/defines.rsp" | tr -d ' ') from $DEFSRC"
{
  for d in "$REF"*.dll;                                   do echo "/r:\"$d\""; done
  for d in "$U/Resources/Scripting/Managed/UnityEngine/"*.dll; do echo "/r:\"$d\""; done
  for d in "$LIB/ScriptAssemblies/"*.dll; do
    case "$(basename "$d")" in Assembly-CSharp*.dll) continue;; esac
    echo "/r:\"$d\""
  done
} > "$TMP/refs.rsp"

"$DOTNET" "$CSC" -nologo -target:library -nostdlib+ -noconfig -langversion:9.0 -unsafe+ \
  -out:"$TMP/check.dll" "@$TMP/defines.rsp" "@$TMP/refs.rsp" "@$TMP/sources.rsp" > "$TMP/out.log" 2>&1

n=$(grep -c "error CS" "$TMP/out.log")
echo "Assembly-CSharp         sources $(wc -l < "$TMP/sources.rsp" | tr -d ' ')   refs $(wc -l < "$TMP/refs.rsp" | tr -d ' ')   errors $n"
if [ "$n" -gt 0 ]; then
  grep "error CS" "$TMP/out.log" | sed "s|$P/||" | sort -u | head -40
  exit 1
fi

# ---- Assembly-CSharp-Editor: everything under an Editor folder. -------------
# It references the runtime assembly just built (not the possibly-stale one in
# Library/ScriptAssemblies) plus Unity's editor DLLs, so an editor script that
# calls into game code is checked against the code as it is RIGHT NOW.
( cd "$P" && find Assets -name "*.cs" -path "*/Editor/*" -not -path "*/worktrees/*" ) \
  | sed "s|.*|\"$P/&\"|" > "$TMP/esources.rsp"

if [ -s "$TMP/esources.rsp" ]; then
  {
    cat "$TMP/refs.rsp"
    echo "/r:\"$TMP/check.dll\""
    for d in "$U/Resources/Scripting/Managed/UnityEditor/"*.dll; do
      [ -e "$d" ] && echo "/r:\"$d\""
    done
    for d in "$U/Managed/UnityEditor.dll" "$U/Managed/UnityEngine.dll"; do
      [ -e "$d" ] && echo "/r:\"$d\""
    done
  } > "$TMP/erefs.rsp"
  defines Assembly-CSharp-Editor "$TMP/edefines.rsp"
  echo "defines $(wc -l < "$TMP/edefines.rsp" | tr -d ' ') from $DEFSRC"

  "$DOTNET" "$CSC" -nologo -target:library -nostdlib+ -noconfig -langversion:9.0 -unsafe+ \
    -out:"$TMP/echeck.dll" "@$TMP/edefines.rsp" "@$TMP/erefs.rsp" "@$TMP/esources.rsp" > "$TMP/eout.log" 2>&1

  m=$(grep -c "error CS" "$TMP/eout.log")
  echo "Assembly-CSharp-Editor  sources $(wc -l < "$TMP/esources.rsp" | tr -d ' ')   refs $(wc -l < "$TMP/erefs.rsp" | tr -d ' ')   errors $m"
  if [ "$m" -gt 0 ]; then
    grep "error CS" "$TMP/eout.log" | sed "s|$P/||" | sort -u | head -40
    exit 1
  fi
fi

echo "clean."
