#!/usr/bin/env bash
set -u
ROOT="/run/media/ddavidich/Disk1/Project Sirius"
OUT="${1:?outdir}"
mkdir -p "$OUT"; rm -f "$OUT"/*.png "$OUT/run.log"
PROFILE=/tmp/rt_test_home
rm -rf "$PROFILE"; mkdir -p "$PROFILE/.config"
cp "$HOME/.config/librelancer.ini" "$PROFILE/.config/"
printf 'post_aa = off\n' >> "$PROFILE/.config/librelancer.ini"
[ -n "${EXTRA_INI:-}" ] && printf '%s\n' "$EXTRA_INI" >> "$PROFILE/.config/librelancer.ini"
env SIRIUS_AUTOPLAY=1 SIRIUS_GOLDEN_DIR="$OUT" SIRIUS_RENDERER=vulkan \
    SIRIUS_PASS_TIMINGS="${PASS_TIMINGS:-1}" SIRIUS_WINDOW_SIZE=2560x1440 \
    ${EXTRA_ENV:-} \
    HOME="$PROFILE" XDG_CONFIG_HOME="$PROFILE/.config" \
    XDG_DATA_HOME="$PROFILE/.local/share" XDG_STATE_HOME="$PROFILE/.local/state" \
    dotnet "$ROOT/build/v3/lancer.dll" > "$OUT/run.log" 2>&1 &
PID=$!
for i in $(seq 1 90); do sleep 2; [ -f "$OUT/space_noui.png" ] && break; kill -0 $PID 2>/dev/null || { echo DIED; tail -5 "$OUT/run.log"; exit 2; }; done
sleep 4
kill $PID 2>/dev/null; sleep 2; kill -9 $PID 2>/dev/null
echo "done"
