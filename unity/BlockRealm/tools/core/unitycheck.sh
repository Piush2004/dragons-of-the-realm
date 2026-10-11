#!/usr/bin/env bash
# Compiles the Unity-facing C# (runtime + editor scripts) against the engine assemblies shipped with the Editor.
# It proves the code type-checks against the real Unity API without needing a licence or opening the Editor.
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
ED="${UNITY_EDITOR_DIR:-/c/Program Files/Unity/Hub/Editor/6000.3.26f1/Editor}/Data"
DOTNET="$ED/NetCoreRuntime/dotnet.exe"
OUT="$ROOT/tools/core/out"
mkdir -p "$OUT"
w() { cygpath -w "$1"; }

REFS=()
REFS+=("-r:$(w "$ED/NetStandard/ref/2.1.0/netstandard.dll")")
for f in "$ED"/Managed/UnityEngine/UnityEngine*.dll "$ED"/Managed/UnityEngine/UnityEditor*.dll ; do
  [ -f "$f" ] && REFS+=("-r:$(w "$f")")
done

SOURCES=()
while IFS= read -r f; do SOURCES+=("$(w "$f")"); done < <(find "$ROOT/Assets/Scripts" "$ROOT/Assets/Editor" -name '*.cs' 2>/dev/null | sort)

"$DOTNET" "$(w "$ED/DotNetSdkRoslyn/csc.dll")" -nologo -target:library -langversion:9 -nullable:disable -nostdlib+ \
  -define:UNITY_EDITOR -define:UNITY_STANDALONE -define:UNITY_STANDALONE_WIN -define:UNITY_6000_0_OR_NEWER \
  -warn:3 -nowarn:CS0649,CS0169,CS0414 -out:"$(w "$OUT/UnityCheck.dll")" "${REFS[@]}" "${SOURCES[@]}"
