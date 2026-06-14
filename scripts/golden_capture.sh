#!/usr/bin/env bash
# Golden capture for render-backend parity (Phase 0).
# Boots the game headlessly (SIRIUS_AUTOPLAY), captures deterministic
# menu.png + launch.png + space.png into the given directory, then shuts down.
#
# Usage: golden_capture.sh <output-dir> [gl|vulkan]
set -e
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="${SIRIUS_ROOT:-$(cd "$SCRIPT_DIR/.." && pwd)}"
OUT="$1"
RENDERER="${2:-gl}"
[ -n "$OUT" ] || { echo "usage: golden_capture.sh <output-dir> [gl|vulkan]"; exit 1; }
mkdir -p "$OUT"
rm -f "$OUT/menu.png" "$OUT/launch.png" "$OUT/space.png" "$OUT/space_noui.png"

PROFILE=/tmp/sirius_golden_home
rm -rf "$PROFILE/.local/share/Librelancer"
mkdir -p "$PROFILE/.config"
cp /tmp/sirius_home/.config/librelancer.ini "$PROFILE/.config/" 2>/dev/null || \
  cp "$HOME/.config/librelancer.ini" "$PROFILE/.config/"
# Pin the gate's post chain: tonemap/bloom/rays are deterministic, but
# post-AA amplifies sub-pixel rasterization differences between APIs on
# every edge (last value wins for duplicate INI keys).
printf 'post_aa = off\n' >> "$PROFILE/.config/librelancer.ini"

SIRIUS_AUTOPLAY=1 SIRIUS_GOLDEN_DIR="$OUT" SIRIUS_RENDERER="$RENDERER" \
HOME="$PROFILE" XDG_CONFIG_HOME="$PROFILE/.config" \
XDG_DATA_HOME="$PROFILE/.local/share" XDG_STATE_HOME="$PROFILE/.local/state" \
nohup dotnet "$ROOT/build/v3/lancer.dll" > "$OUT/run.log" 2>&1 &
PID=$!

for i in $(seq 1 60); do
    sleep 3
    [ -f "$OUT/menu.png" ] && [ -f "$OUT/launch.png" ] && [ -f "$OUT/space.png" ] && [ -f "$OUT/space_noui.png" ] && break
    kill -0 $PID 2>/dev/null || { echo "game died, see $OUT/run.log"; exit 2; }
done
sleep 2
kill $PID 2>/dev/null || true
# A game stuck in a loading phase can survive SIGTERM and then hold the
# build output dlls open, hanging every later dotnet build on the copy
# step - force-kill after a grace period.
for i in 1 2 3; do
    kill -0 $PID 2>/dev/null || break
    sleep 2
done
kill -9 $PID 2>/dev/null || true
[ -f "$OUT/menu.png" ] && [ -f "$OUT/launch.png" ] && [ -f "$OUT/space.png" ] && [ -f "$OUT/space_noui.png" ] && echo "captured: $OUT/{menu,launch,space,space_noui}.png" || { echo "capture incomplete"; exit 3; }
