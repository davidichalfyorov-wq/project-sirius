#!/usr/bin/env bash
# Vulkan validation gate (roadmap 9.2): runs a capture script with the
# Khronos validation layer enabled and FAILS if any validation message
# lands in the log. If a bundled VVL package exists under bin/builddeps/vvl it
# is preferred; otherwise the system Vulkan SDK/driver layer is used.
#
# Usage: vk_validate_run.sh <outdir> [capture-script args...]
#   default capture: scripts/smaa_shot.sh <outdir> off vulkan
set -u
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="${SIRIUS_ROOT:-$(cd "$SCRIPT_DIR/.." && pwd)}"
OUT="${1:?outdir}"
shift || true

if [ -d "$ROOT/bin/builddeps/vvl/usr/share/vulkan/explicit_layer.d" ]; then
    export VK_LAYER_PATH="$ROOT/bin/builddeps/vvl/usr/share/vulkan/explicit_layer.d"
    # The layer manifest references the .so by bare name; point the loader at
    # the bundled library directory.
    export LD_LIBRARY_PATH="$ROOT/bin/builddeps/vvl/usr/lib/x86_64-linux-gnu:${LD_LIBRARY_PATH:-}"
fi
export SIRIUS_VK_VALIDATION=1

if [ $# -gt 0 ]; then
    "$@"
else
    "$ROOT/scripts/smaa_shot.sh" "$OUT" off vulkan 0
fi
status=$?

LOG="$OUT/run.log"
if [ ! -f "$LOG" ]; then
    echo "VALIDATION GATE: no log at $LOG"
    exit 2
fi

if ! grep -q "Validation layer enabled" "$LOG"; then
    echo "VALIDATION GATE: layer did not load (check VK_LAYER_PATH)"
    exit 3
fi

errors=$(grep -cE "VUID-|Validation Error" "$LOG" || true)
echo "VALIDATION GATE: $errors validation message(s), run exit=$status"
if [ "$errors" -gt 0 ]; then
    grep -E "VUID-|Validation Error" "$LOG" | head -10
    exit 1
fi
exit 0
