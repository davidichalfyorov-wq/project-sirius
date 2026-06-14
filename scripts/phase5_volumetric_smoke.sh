#!/usr/bin/env bash
# Quick Phase 5 volumetric smoke for the current full opt-in stack.
#
# English: renders Li01/Badlands with the feature-gated froxel path enabled and
# runs the same Vulkan validation gate used by the golden scripts.
# Russian: быстрый smoke для Li01/Badlands без SSIM-baseline; проверяет, что
# полный opt-in volumetric path стартует, делает скриншот и не даёт VVL ошибок.
#
# Usage:
#   scripts/phase5_volumetric_smoke.sh [OUTDIR]
#
# Useful overrides:
#   SIRIUS_PHASE5_POSE="-7460,500,55000,0"
#   SIRIUS_PHASE5_QUALITY=2
#   SIRIUS_PHASE5_DEBUG_VIEW=off|vol_transmittance|vol_near_density|vol_wake_vectors
#   SIRIUS_PHASE5_DEV_HUD=0|1
#   SIRIUS_PHASE5_PASS_TIMINGS=0|1
#   SIRIUS_PHASE5_ASSERT_PASSES=0|1
#   SIRIUS_PHASE5_BUILD=0|1
#   SIRIUS_PHASE5_BUILD_CONFIG=Debug|Release
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="${SIRIUS_ROOT:-$(cd "$SCRIPT_DIR/.." && pwd)}"
OUT="${1:-$ROOT/Output/phase5_volumetric_smoke}"
POSE="${SIRIUS_PHASE5_POSE:--7460,500,55000,0}"
QUALITY="${SIRIUS_PHASE5_QUALITY:-2}"
DEBUG_VIEW="${SIRIUS_PHASE5_DEBUG_VIEW:-off}"
DEV_HUD="${SIRIUS_PHASE5_DEV_HUD:-0}"
PASS_TIMINGS_FLAG="${SIRIUS_PHASE5_PASS_TIMINGS:-0}"
ASSERT_PASSES="${SIRIUS_PHASE5_ASSERT_PASSES:-1}"
VALIDATION_PASS_TIMINGS="${SIRIUS_PHASE5_VALIDATION_PASS_TIMINGS:-1}"
BUILD_FIRST="${SIRIUS_PHASE5_BUILD:-1}"
BUILD_CONFIG="${SIRIUS_PHASE5_BUILD_CONFIG:-Release}"

mkdir -p "$OUT"

PHASE5_INI="volumetric_nebula = true
volumetric_nebulae = true
volumetric_quality = $QUALITY
volumetric_composite = true
volumetric_temporal = true
volumetric_reprojection = true
volumetric_blue_noise = true
volumetric_adaptive_quality = true
volumetric_near_cascade = true
volumetric_near_composite = true
volumetric_near_detail = true
volumetric_ship_displacement = true
volumetric_wake_history = true
volumetric_wake_curl = true
volumetric_god_rays = true
volumetric_material_fog = true
volumetric_lightning_channels = true
volumetric_lightning_deterministic = true
volumetric_lightning_golden_disable = true
volumetric_lightning_replay_time = 0.01
volumetric_lightning_replay_seed = 0
atmosphere_luts = true
atmosphere_aerial = true
atmosphere_cloud_shell = true
rt_shadows = false
rtao = false
rt_reflections = false
shadows = true"

PHASE5_ENV="SIRIUS_TELEPORT=$POSE SIRIUS_DEV_HUD=$DEV_HUD SIRIUS_DEBUG_VIEW=$DEBUG_VIEW SIRIUS_VOLFOG=1"

echo "== Phase 5 volumetric smoke =="
echo "ROOT=$ROOT"
echo "OUT=$OUT"
echo "POSE=$POSE QUALITY=$QUALITY DEBUG_VIEW=$DEBUG_VIEW DEV_HUD=$DEV_HUD PASS_TIMINGS=$PASS_TIMINGS_FLAG"
echo "ASSERT_PASSES=$ASSERT_PASSES VALIDATION_PASS_TIMINGS=$VALIDATION_PASS_TIMINGS"
echo "BUILD_FIRST=$BUILD_FIRST BUILD_CONFIG=$BUILD_CONFIG"

if [ "$BUILD_FIRST" = "1" ]; then
    echo "-- build/sync build/v3"
    dotnet build "$ROOT/src/lancer/lancer.csproj" -c "$BUILD_CONFIG" -v minimal
    SRCOUT="$ROOT/src/lancer/bin/$BUILD_CONFIG/net10.0"
    if [ ! -f "$SRCOUT/lancer.dll" ]; then
        echo "FAIL: build output missing at $SRCOUT/lancer.dll"
        exit 2
    fi
    mkdir -p "$ROOT/build/v3"
    cp -r "$SRCOUT/." "$ROOT/build/v3/"

    NATIVE_REF="${SIRIUS_NATIVE_REF:-/run/media/ddavidich/Disk1/Project Sirius}"
    if [ -d "$NATIVE_REF/build/v3" ] &&
       [ "$(readlink -f "$NATIVE_REF/build/v3")" != "$(readlink -f "$ROOT/build/v3")" ]; then
        for so in "$NATIVE_REF/build/v3"/*.so "$NATIVE_REF/build/v3"/*.so.*; do
            [ -e "$so" ] || continue
            b="$(basename "$so")"
            [ -e "$ROOT/build/v3/$b" ] || cp "$so" "$ROOT/build/v3/$b"
        done
    fi
fi

echo "-- capture"
PASS_TIMINGS="$PASS_TIMINGS_FLAG" EXTRA_ENV="$PHASE5_ENV" EXTRA_INI="$PHASE5_INI" \
    "$ROOT/scripts/rt_space_test.sh" "$OUT/capture"

if [ ! -f "$OUT/capture/space_noui.png" ]; then
    echo "FAIL: no screenshot at $OUT/capture/space_noui.png"
    exit 2
fi

echo "-- validation"
PASS_TIMINGS="$VALIDATION_PASS_TIMINGS" EXTRA_ENV="$PHASE5_ENV" EXTRA_INI="$PHASE5_INI" \
    "$ROOT/scripts/vk_validate_run.sh" "$OUT/validation" \
    "$ROOT/scripts/rt_space_test.sh" "$OUT/validation" | tee "$OUT/validation.txt"

if [ "$ASSERT_PASSES" = "1" ]; then
    LOG="$OUT/validation/run.log"
    required_passes=(
        "vol_nebula_clear"
        "vol_nebula_displacement"
        "vol_nebula_displacement_history"
        "vol_nebula_wake_curl"
        "vol_nebula_density"
        "vol_nebula_light"
        "vol_nebula_lightning_channels"
        "vol_nebula_integrate"
        "vol_nebula_depth_copy"
        "vol_nebula_reproject"
        "vol_nebula_composite"
        "post.godrays"
    )
    for pass in "${required_passes[@]}"; do
        if ! grep -Fq "$pass" "$LOG"; then
            echo "FAIL: expected volumetric pass marker '$pass' in $LOG"
            exit 3
        fi
    done
    echo "PASS: required volumetric pass markers found"
fi

cat > "$OUT/summary.txt" <<EOF
Phase 5 volumetric smoke PASS
pose=$POSE
quality=$QUALITY
debug_view=$DEBUG_VIEW
dev_hud=$DEV_HUD
capture_pass_timings=$PASS_TIMINGS_FLAG
validation_pass_timings=$VALIDATION_PASS_TIMINGS
assert_passes=$ASSERT_PASSES
build_first=$BUILD_FIRST
build_config=$BUILD_CONFIG
screenshot=$OUT/capture/space_noui.png
validation_log=$OUT/validation/run.log
EOF

echo "PASS: screenshot -> $OUT/capture/space_noui.png"
echo "PASS: validation -> $OUT/validation.txt"
