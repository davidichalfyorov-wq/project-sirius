#!/usr/bin/env bash
# UI 2.0 click-coverage harness. Drives the game with SIRIUS_UI_AUTOTEST
# (see src/LibreLancer/SiriusUiAutotest.cs) and asserts the walk happened.
#
# Usage: ui2_autotest.sh <menu|full> <output-dir> [vulkan|gl]
set -u
ROOT="/run/media/ddavidich/Disk1/Project Sirius"
MODE="${1:?mode menu|full}"
OUT="${2:?output dir}"
RENDERER="${3:-vulkan}"
mkdir -p "$OUT"
rm -f "$OUT"/*.png "$OUT/run.log" 2>/dev/null

PROFILE=/tmp/sirius_golden_home
rm -rf "$PROFILE/.local/share/Librelancer"
mkdir -p "$PROFILE/.config"
cp "$HOME/.config/librelancer.ini" "$PROFILE/.config/"
printf 'post_aa = off\n' >> "$PROFILE/.config/librelancer.ini"

GOLDEN_ENV=()
if [ "$MODE" = "full" ]; then
    GOLDEN_ENV=(SIRIUS_GOLDEN_DIR="$OUT")
fi

env SIRIUS_AUTOPLAY=1 SIRIUS_RENDERER="$RENDERER" \
    SIRIUS_UI_AUTOTEST="$MODE" SIRIUS_UI_AUTOTEST_DIR="$OUT" \
    "${GOLDEN_ENV[@]}" \
    HOME="$PROFILE" XDG_CONFIG_HOME="$PROFILE/.config" \
    XDG_DATA_HOME="$PROFILE/.local/share" XDG_STATE_HOME="$PROFILE/.local/state" \
    dotnet "$ROOT/build/v3/lancer.dll" > "$OUT/run.log" 2>&1 &
PID=$!

if [ "$MODE" = "menu" ]; then
    # The walk ends by clicking EXIT: the game must terminate on its own.
    for i in $(seq 1 60); do
        kill -0 $PID 2>/dev/null || break
        sleep 2
    done
    if kill -0 $PID 2>/dev/null; then
        echo "FAIL: game still running after menu walk (EXIT click did not quit)"
        kill -9 $PID 2>/dev/null
        exit 1
    fi
else
    # Wait for the space walk to finish, then shut down.
    for i in $(seq 1 150); do
        grep -q "UiTest: space walk complete" "$OUT/run.log" 2>/dev/null && break
        kill -0 $PID 2>/dev/null || break
        sleep 2
    done
    sleep 3
    kill $PID 2>/dev/null; sleep 2; kill -9 $PID 2>/dev/null
fi

fail=0
expect() {
    if grep -q "$1" "$OUT/run.log"; then
        echo "PASS: $1"
    else
        echo "FAIL: missing log '$1'"
        fail=1
    fi
}

expect "UiTest: menu walker armed"
expect "menu step 0 released: click_options"
expect "menu step 1 released: options_goback"
expect "menu step 2 released: click_loadgame"
expect "menu step 3 released: loadgame_goback"
expect "menu step 4 released: click_multiplayer"
expect "menu step 5 released: serverlist_mainmenu"
if [ "$MODE" = "menu" ]; then
    expect "menu step 6 released: click_exit"
    echo "PASS: process exited via EXIT button"
else
    expect "menu step 6 released: click_newgame"
    expect "golden: launch.png"
    expect "golden: space.png"
    expect "golden: space_noui.png"
    expect "released: map_open"
    expect "released: map_hover_a"
    expect "released: map_hover_b"
    expect "released: map_wheel_in"
    expect "released: map_wheel_in2"
    expect "released: map_wheel_out"
    expect "released: map_universe"
    expect "released: uni_hover"
    expect "released: map_close"
    expect "released: info_open"
    expect "released: info_close"
    expect "released: rep_open"
    expect "released: rep_close"
    expect "UiTest: space walk complete"
fi
# scene transitions actually happened (lua OpenScene -> SetWidget)
transitions=$(grep -c "SetWidget:" "$OUT/run.log" || true)
echo "INFO: $transitions SetWidget scene transitions logged"
[ "$transitions" -ge 7 ] || { echo "FAIL: expected >=7 scene transitions"; fail=1; }

shots=$(ls "$OUT"/*_*.png 2>/dev/null | wc -l)
echo "INFO: $shots autotest screenshots in $OUT"

[ $fail -eq 0 ] && echo "AUTOTEST $MODE: ALL PASS" || echo "AUTOTEST $MODE: FAILURES"
exit $fail
