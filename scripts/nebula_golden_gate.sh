#!/usr/bin/env bash
# Volumetric nebula golden gate (phase 5 / P7).
#
# Captures the Badlands nebula pose on Vulkan and asserts:
#   1. SSIM >= 0.995 against tests/goldens/nebula/space_noui_on.png.
#   2. SSIM >= 0.995 between two fresh on captures.
#   3. The volumetric effect exists: SSIM(on, off baseline) < 0.97.
#   4. VATA >= 0.30, so the image still has cloud structure.
#   5. vk_validate_run.sh reports 0 validation messages on the same pose.
#
# Refresh baselines deliberately with: nebula_golden_gate.sh --rebase
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="${SIRIUS_ROOT:-$(cd "$SCRIPT_DIR/.." && pwd)}"
GOLD="$ROOT/tests/goldens/nebula"
POSE="-7460,500,55000,0"
OUT="${NEBULA_GOLDEN_OUT:-$(mktemp -d /tmp/nebula_golden_gate.XXXX)}"
CLEAN_OUT=1
if [ -n "${NEBULA_GOLDEN_OUT:-}" ]; then
    CLEAN_OUT=0
fi
mkdir -p "$OUT"

capture() {
    local outdir="$1"
    local extra_ini="$2"
    PASS_TIMINGS=0 EXTRA_ENV="SIRIUS_TELEPORT=$POSE" EXTRA_INI="$extra_ini" \
        "$ROOT/scripts/rt_space_test.sh" "$outdir" > /dev/null 2>&1
    [ -f "$outdir/space_noui.png" ] || {
        echo "FAIL: no capture in $outdir"
        exit 2
    }
}

ON_INI="volumetric_nebulae = true
volumetric_quality = 2
rt_shadows = false
rtao = false
rt_reflections = false
shadows = true"

OFF_INI="volumetric_nebulae = false
volumetric_quality = 2
rt_shadows = false
rtao = false
rt_reflections = false
shadows = true"

if [ "${1:-}" = "--rebase" ]; then
    mkdir -p "$GOLD"
    capture "$OUT/on_baseline" "$ON_INI"
    capture "$OUT/off_baseline" "$OFF_INI"
    cp "$OUT/on_baseline/space_noui.png" "$GOLD/space_noui_on.png"
    cp "$OUT/off_baseline/space_noui.png" "$GOLD/off_baseline.png"
    echo "REBASED: $GOLD"
    exit 0
fi

[ -f "$GOLD/space_noui_on.png" ] || {
    echo "FAIL: no on baseline, run --rebase"
    exit 2
}
[ -f "$GOLD/off_baseline.png" ] || {
    echo "FAIL: no off baseline, run --rebase"
    exit 2
}

capture "$OUT/on1" "$ON_INI"
capture "$OUT/on2" "$ON_INI"

echo "-- stability vs nebula baseline (>=0.995)"
python3 "$ROOT/scripts/golden_compare.py" "$GOLD/space_noui_on.png" \
    "$OUT/on1/space_noui.png" --min-ssim 0.995 || {
    echo "NEBULA GATE: FAIL (baseline stability)"
    exit 1
}

echo "-- run-to-run stability (>=0.995)"
python3 "$ROOT/scripts/golden_compare.py" "$OUT/on1/space_noui.png" \
    "$OUT/on2/space_noui.png" --min-ssim 0.995 || {
    echo "NEBULA GATE: FAIL (run-to-run stability)"
    exit 1
}

echo "-- effect presence vs off baseline (<0.97)"
if ! python3 - "$GOLD/off_baseline.png" "$OUT/on1/space_noui.png" "$ROOT/scripts/golden_compare.py" << 'PYEOF'
import subprocess
import sys

result = subprocess.run(
    [sys.executable, sys.argv[3], sys.argv[1], sys.argv[2], "--min-ssim", "0.0"],
    capture_output=True,
    text=True,
)
print(result.stdout.strip())
line = [l for l in result.stdout.splitlines() if "ssim=" in l]
ssim = float(line[0].split("ssim=")[1].split()[0]) if line else 1.0
print(f"effect ssim(on, off)={ssim:.5f}")
sys.exit(0 if ssim < 0.97 else 1)
PYEOF
then
    echo "NEBULA GATE: FAIL (effect missing)"
    exit 1
fi

echo "-- VATA structure (>=0.30)"
if ! python3 - "$OUT/on1/space_noui.png" "$ROOT/scripts/vata_metric.py" << 'PYEOF2'
import re
import subprocess
import sys

result = subprocess.run([sys.executable, sys.argv[2], sys.argv[1]],
    capture_output=True, text=True)
print(result.stdout.strip())
match = re.search(r"std = ([0-9.]+)", result.stdout)
value = float(match.group(1)) if match else 0.0
sys.exit(0 if value >= 0.30 else 1)
PYEOF2
then
    echo "NEBULA GATE: FAIL (flat nebula)"
    exit 1
fi

echo "-- validation (0 messages)"
EXTRA_INI="$ON_INI" EXTRA_ENV="SIRIUS_TELEPORT=$POSE" \
    "$ROOT/scripts/vk_validate_run.sh" "$OUT/val" \
    "$ROOT/scripts/rt_space_test.sh" "$OUT/val" | tee "$OUT/validation.txt" | tail -1

if [ "$CLEAN_OUT" -eq 1 ]; then
    rm -rf "$OUT"
fi
echo "NEBULA GATE: PASS"
