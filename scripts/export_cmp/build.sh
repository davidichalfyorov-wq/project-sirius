#!/bin/bash
set -e
ROOT="/run/media/ddavidich/Disk1/Project Sirius"
ED="$ROOT/src/Editor/LancerEdit/bin/Release/net10.0"
CSC="/usr/lib/dotnet/sdk/10.0.109/Roslyn/bincore/csc.dll"
FW="/usr/lib/dotnet/shared/Microsoft.NETCore.App/10.0.9"
refs=()
for d in "$FW"/*.dll; do refs+=( "-r:$d" ); done
for d in "$ED"/*.dll; do
  bn=$(basename "$d")
  case "$bn" in
    System.*|Microsoft.*|netstandard.dll|mscorlib.dll|WindowsBase.dll|export_cmp.dll) ;;
    *) refs+=( "-r:$d" ) ;;
  esac
done
dotnet "$CSC" -nologo -nostdlib -target:exe -langversion:latest \
  -out:"$ED/export_cmp.dll" "${refs[@]}" "$ROOT/scripts/export_cmp/export_cmp.cs" 2>&1 | grep -iE "error" || true
cp -f "$ED/LancerEdit.runtimeconfig.json" "$ED/export_cmp.runtimeconfig.json"
test -f "$ED/export_cmp.dll" && echo "BUILD OK" || { echo "BUILD FAILED"; exit 1; }
