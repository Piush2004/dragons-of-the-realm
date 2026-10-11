#!/usr/bin/env bash
# Builds and runs the pure C# core (no UnityEngine) with the compiler and .NET runtime bundled in the Unity Editor.
# Usage: tools/core/core.sh build|test [extra test args]
# Needs no Unity licence: only the Editor files are used.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
ED="${UNITY_EDITOR_DIR:-/c/Program Files/Unity/Hub/Editor/6000.3.26f1/Editor}/Data"
DOTNET="$ED/NetCoreRuntime/dotnet.exe"
FW="$(ls -d "$ED"/NetCoreRuntime/shared/Microsoft.NETCore.App/*/ | head -n1)"
OUT="$ROOT/tools/core/out"
mkdir -p "$OUT"
w() { cygpath -w "$1"; }

REFS=()
for f in "$FW"System.*.dll "$FW"Microsoft.*.dll "$FW"mscorlib.dll "$FW"netstandard.dll; do
  case "$(basename "$f")" in *Native*|Microsoft.DiaSymReader*) continue;; esac
  REFS+=("-r:$(w "$f")")
done

build() {
  local sources=()
  while IFS= read -r f; do sources+=("$(w "$f")"); done < <(find "$ROOT/Assets/Scripts/Core" "$ROOT/tests-core" -name '*.cs' | sort)
  "$DOTNET" "$(w "$ED/DotNetSdkRoslyn/csc.dll")" -nologo -target:exe -langversion:9 -nullable:disable -warn:4 \
    -optimize+ -debug- -define:BLOCKREALM_CORE_TESTS -out:"$(w "$OUT/CoreTests.dll")" "${REFS[@]}" "${sources[@]}"
  echo '{"runtimeOptions":{"tfm":"net6.0","framework":{"name":"Microsoft.NETCore.App","version":"6.0.0"},"configProperties":{"System.GC.Server":false}}}' > "$OUT/CoreTests.runtimeconfig.json"
}

case "${1:-test}" in
  build) build ;;
  test) build; shift || true; "$DOTNET" "$(w "$OUT/CoreTests.dll")" "$@" ;;
  *) echo "usage: $0 build|test"; exit 2 ;;
esac
