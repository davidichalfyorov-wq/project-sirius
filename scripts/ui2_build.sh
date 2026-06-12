#!/usr/bin/env bash
# UI 2.0 build helper: recompile the uixml bundle, rebuild the game, refresh build/v3.
# Usage: ui2_build.sh [--ui-only]
#   --ui-only: skip the C# rebuild if only uixml/* changed (bundle is embedded, so
#              LibreLancer.dll must still be rebuilt — this flag is therefore only
#              a guard against accidentally thinking xml edits skip the rebuild).
set -e
set -o pipefail
ROOT="/run/media/ddavidich/Disk1/Project Sirius"
IEDIT="$ROOT/src/Editor/InterfaceEdit/bin/Release/net10.0/InterfaceEdit.dll"

echo "== 1/3 compile uixml -> interface.json"
dotnet "$IEDIT" --compile "$ROOT/uixml" "$ROOT/src/LibreLancer/Interface/Default/interface.json"

echo "== 2/3 dotnet build lancer (Release)"
BUILDLOG=$(mktemp /tmp/ui2build.XXXX.log)
if ! dotnet build "$ROOT/src/lancer/lancer.csproj" -c Release 2>&1 | tee "$BUILDLOG" | tail -4; then
    echo "BUILD FAILED, full log: $BUILDLOG"; exit 1
fi
# SH0004 shader errors don't say "error CS" — catch any error marker (CHANGELOG.md:127).
if grep -E "error (CS|SH|MSB)" "$BUILDLOG" >/dev/null; then
    echo "BUILD ERRORS DETECTED, full log: $BUILDLOG"; exit 1
fi

echo "== 3/3 sync app assemblies into build/v3"
SRCOUT="$ROOT/src/lancer/bin/Release/net10.0"
for f in lancer.dll lancer.pdb LibreLancer.dll LibreLancer.pdb LibreLancer.Base.dll LibreLancer.Base.pdb \
         LibreLancer.Data.dll LibreLancer.Data.pdb LibreLancer.Media.dll LibreLancer.Media.pdb \
         LibreLancer.Physics.dll LibreLancer.Physics.pdb LibreLancer.Thorn.dll LibreLancer.Thorn.pdb \
         LibreLancer.Entities.dll LibreLancer.Database.dll LibreLancer.ImUI.dll; do
    if [ -f "$SRCOUT/$f" ]; then cp "$SRCOUT/$f" "$ROOT/build/v3/$f"; fi
done
echo "OK: build/v3 refreshed"
