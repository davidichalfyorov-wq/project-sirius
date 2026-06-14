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
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="${SIRIUS_ROOT:-$(cd "$SCRIPT_DIR/.." && pwd)}"
OUT="${1:-$ROOT/Output/phase5_volumetric_smoke}"
POSE="${SIRIUS_PHASE5_POSE:--7460,500,55000,0}"
QUALITY="${SIRIUS_PHASE5_QUALITY:-2}"
DEBUG_VIEW="${SIRIUS_PHASE5_DEBUG_VIEW:-off}"
DEV_HUD="${SIRIUS_PHASE5_DEV_HUD:-0}"
PASS_TIMINGS_FLAG="${SIRIUS_PHASE5_PASS_TIMINGS:-0}"

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

echo "-- capture"
PASS_TIMINGS="$PASS_TIMINGS_FLAG" EXTRA_ENV="$PHASE5_ENV" EXTRA_INI="$PHASE5_INI" \
    "$ROOT/scripts/rt_space_test.sh" "$OUT/capture"

if [ ! -f "$OUT/capture/space_noui.png" ]; then
    echo "FAIL: no screenshot at $OUT/capture/space_noui.png"
    exit 2
fi

echo "-- validation"
PASS_TIMINGS="$PASS_TIMINGS_FLAG" EXTRA_ENV="$PHASE5_ENV" EXTRA_INI="$PHASE5_INI" \
    "$ROOT/scripts/vk_validate_run.sh" "$OUT/validation" \
    "$ROOT/scripts/rt_space_test.sh" "$OUT/validation" | tee "$OUT/validation.txt"

cat > "$OUT/summary.txt" <<EOF
Phase 5 volumetric smoke PASS
pose=$POSE
quality=$QUALITY
debug_view=$DEBUG_VIEW
dev_hud=$DEV_HUD
pass_timings=$PASS_TIMINGS_FLAG
screenshot=$OUT/capture/space_noui.png
validation_log=$OUT/validation/run.log
EOF

echo "PASS: screenshot -> $OUT/capture/space_noui.png"
echo "PASS: validation -> $OUT/validation.txt"
