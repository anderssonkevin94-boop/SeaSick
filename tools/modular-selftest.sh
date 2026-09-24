#!/bin/zsh
# Headless self-test of the modular-ship core -- no Unity editor, no scene.
#
# Compiles Assets/_Project/Scripts/Ship/Modular/*.cs (NOT View/, Runtime/ or
# Editor/) plus Steamer/HullFormData.cs (the hull form the shipyard reshapes)
# with Unity's own Roslyn against NetStandard 2.1 + UnityEngine.CoreModule,
# adds tools/modular-selftest/{JsonShim,Main}.cs (a managed JsonUtility
# stand-in, because the real one is a native module), and runs
# ModularShipSelfTest.RunWith() on the JSON under Resources/ShipModules with
# Unity's bundled dotnet. Exit code 0 = every gate passed.
#
# Only managed UnityEngine math may be used by the core (Vector3 arithmetic,
# Mathf); a native call such as Quaternion.Euler or Debug.Log would crash
# here, which is exactly what this runner is meant to catch.
set -u
P="$(cd "$(dirname "$0")/.." && pwd)"
U=/Applications/Unity/Hub/Editor/6000.4.3f1/Unity.app/Contents
DOTNET=$U/Resources/Scripting/NetCoreRuntime/dotnet
CSC=$U/Resources/Scripting/DotNetSdkRoslyn/csc.dll
CORE=$U/Resources/Scripting/Managed/UnityEngine/UnityEngine.CoreModule.dll
O="$(mktemp -d)"
trap 'rm -rf "$O"' EXIT

"$DOTNET" "$CSC" -nologo -t:exe -nostdlib -noconfig -langversion:9.0 -nowarn:0162,0168,0219,0414,0649,0169,0436 \
  -r:$U/Resources/Scripting/NetStandard/ref/2.1.0/netstandard.dll -r:$CORE \
  -out:$O/t.exe "$P"/Assets/_Project/Scripts/Ship/Modular/*.cs "$P"/Assets/_Project/Scripts/Steamer/HullFormData.cs \
  "$P"/tools/modular-selftest/JsonShim.cs "$P"/tools/modular-selftest/Main.cs > $O/build.log 2>&1
if grep -q "error CS" $O/build.log; then grep "error CS" $O/build.log | sed "s|$P/||"; echo "BUILD FAILED"; exit 2; fi
cp $CORE $O/
echo '{ "runtimeOptions": { "tfm": "net8.0", "framework": { "name": "Microsoft.NETCore.App", "version": "8.0.16" } } }' > $O/t.runtimeconfig.json
"$DOTNET" $O/t.exe "$P"
