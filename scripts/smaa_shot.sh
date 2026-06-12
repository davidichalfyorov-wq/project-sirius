#!/usr/bin/env bash
# SMAA effect capture: boots to the menu with post_aa forced to a given
# mode (off/fxaa/smaa) in a clean profile, grabs the deterministic
# menu.png. Usage: smaa_shot.sh <outdir> <mode> [vulkan|gl] [debug01]
set -u
ROOT="/run/media/ddavidich/Disk1/Project Sirius"
OUT="${1:?outdir}"
MODE="${2:?post_aa mode}"
RENDERER="${3:-vulkan}"
DEBUG="${4:-0}"
SCENE="${5:-}"
mkdir -p "$OUT"
rm -f "$OUT"/menu.png "$OUT/run.log"
PROFILE=/tmp/smaa_test_home
rm -rf "$PROFILE"
mkdir -p "$PROFILE/.config"
sed "s/^post_aa = .*/post_aa = $MODE/" "$HOME/.config/librelancer.ini" > "$PROFILE/.config/librelancer.ini"

env SIRIUS_AUTOPLAY=1 SIRIUS_GOLDEN_DIR="$OUT" SIRIUS_RENDERER="$RENDERER" \
    SIRIUS_DEBUG_SMAA="$DEBUG" SIRIUS_WINDOW_SIZE=2560x1440 SIRIUS_OPEN_SCENE="$SCENE" \
    HOME="$PROFILE" XDG_CONFIG_HOME="$PROFILE/.config" \
    XDG_DATA_HOME="$PROFILE/.local/share" XDG_STATE_HOME="$PROFILE/.local/state" \
    dotnet "$ROOT/build/v3/lancer.dll" > "$OUT/run.log" 2>&1 &
PID=$!

for i in $(seq 1 40); do
    sleep 2
    [ -f "$OUT/menu.png" ] && break
    kill -0 $PID 2>/dev/null || { echo "GAME DIED"; tail -5 "$OUT/run.log"; exit 2; }
done
prev=-1
for i in $(seq 1 15); do
    size=$(stat -c%s "$OUT/menu.png" 2>/dev/null || echo 0)
    [ "$size" -gt 0 ] && [ "$size" = "$prev" ] && break
    prev=$size
    sleep 1
done
kill $PID 2>/dev/null; sleep 2; kill -9 $PID 2>/dev/null
[ -f "$OUT/menu.png" ] && echo "captured: $OUT/menu.png ($MODE)" || { echo "capture incomplete"; exit 3; }
