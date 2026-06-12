#!/usr/bin/env bash
# Fast UI-iteration capture: boots the game in golden mode just long enough
# to grab the deterministic menu.png, then kills it (no launch/space wait).
#
# Usage: ui2_menu_shot.sh <output-dir> [vulkan|gl] [open-scene]
#   open-scene: optional SIRIUS_OPEN_SCENE value (options, loadgame, ...)
set -e
ROOT="/run/media/ddavidich/Disk1/Project Sirius"
OUT="$1"
RENDERER="${2:-vulkan}"
SCENE="${3:-}"
[ -n "$OUT" ] || { echo "usage: ui2_menu_shot.sh <output-dir> [vulkan|gl] [open-scene]"; exit 1; }
mkdir -p "$OUT"
rm -f "$OUT/menu.png"

PROFILE=/tmp/sirius_golden_home
rm -rf "$PROFILE/.local/share/Librelancer"
mkdir -p "$PROFILE/.config"
cp "$HOME/.config/librelancer.ini" "$PROFILE/.config/"
printf 'post_aa = off\n' >> "$PROFILE/.config/librelancer.ini"

SIRIUS_AUTOPLAY=1 SIRIUS_GOLDEN_DIR="$OUT" SIRIUS_RENDERER="$RENDERER" \
SIRIUS_OPEN_SCENE="$SCENE" \
HOME="$PROFILE" XDG_CONFIG_HOME="$PROFILE/.config" \
XDG_DATA_HOME="$PROFILE/.local/share" XDG_STATE_HOME="$PROFILE/.local/state" \
nohup dotnet "$ROOT/build/v3/lancer.dll" > "$OUT/run.log" 2>&1 &
PID=$!

for i in $(seq 1 50); do
    sleep 2
    [ -f "$OUT/menu.png" ] && break
    kill -0 $PID 2>/dev/null || { echo "game died, see $OUT/run.log"; exit 2; }
done
# wait for the PNG encode to finish (size stable and non-zero)
prev=-1
for i in $(seq 1 20); do
    size=$(stat -c%s "$OUT/menu.png" 2>/dev/null || echo 0)
    [ "$size" -gt 0 ] && [ "$size" = "$prev" ] && break
    prev=$size
    sleep 1
done
kill $PID 2>/dev/null || true
for i in 1 2 3; do
    kill -0 $PID 2>/dev/null || break
    sleep 2
done
kill -9 $PID 2>/dev/null || true
[ -f "$OUT/menu.png" ] && echo "captured: $OUT/menu.png" || { echo "capture incomplete"; exit 3; }
