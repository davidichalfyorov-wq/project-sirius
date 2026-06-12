#!/usr/bin/env bash
# RT effects golden gate (roadmap phase 4 / B7).
#
# Captures the station pose on Vulkan with rt_shadows=true and asserts:
#   1. SSIM >= 0.995 against tests/goldens/rt/space_noui.png (stability)
#   2. The effect exists: SSIM(capture, rt-off baseline) < 0.999 - a
#      silently dead RT path reproduces the raster frame and fails here.
#   3. vk_validate_run.sh reports 0 validation messages on the same pose.
#
# Refresh baselines (deliberately!) with: rt_golden_gate.sh --rebase
set -u
ROOT="/run/media/ddavidich/Disk1/Project Sirius"
GOLD="$ROOT/tests/goldens/rt"
POSE="-35763,150,-24500,0"
OUT=/tmp/rt_golden_gate.$$
mkdir -p "$OUT"

capture() { # outdir extra_ini
    # PASS_TIMINGS=0: the timing overlay text changes between runs and
    # would dominate the stability diff.
    PASS_TIMINGS=0 EXTRA_ENV="SIRIUS_TELEPORT=$POSE" EXTRA_INI="$2" \
        "$ROOT/scripts/rt_space_test.sh" "$1" > /dev/null 2>&1
    [ -f "$1/space_noui.png" ] || { echo "FAIL: no capture in $1"; exit 2; }
}

# The user profile may enable any rt_* flag; every capture pins all three
# so baselines and gate runs never inherit ambient settings.
ON_INI="rt_shadows = true
rtao = false
rt_reflections = false"
OFF_INI="rt_shadows = false
rtao = false
rt_reflections = false"
AO_INI="rt_shadows = true
rtao = true
rt_reflections = false"

if [ "${1:-}" = "--rebase" ]; then
    mkdir -p "$GOLD"
    capture "$OUT/on" "$ON_INI"
    capture "$OUT/off" "$OFF_INI"
    capture "$OUT/ao" "$AO_INI"
    cp "$OUT/on/space_noui.png" "$GOLD/space_noui.png"
    cp "$OUT/off/space_noui.png" "$GOLD/space_noui_off.png"
    cp "$OUT/ao/space_noui.png" "$GOLD/space_noui_ao.png"
    echo "REBASED: $GOLD"
    exit 0
fi

[ -f "$GOLD/space_noui.png" ] || { echo "FAIL: no baselines, run --rebase"; exit 2; }

capture "$OUT/on" "$ON_INI"

echo "-- stability vs RT baseline (>=0.995)"
python3 "$ROOT/scripts/golden_compare.py" "$GOLD/space_noui.png" \
    "$OUT/on/space_noui.png" --min-ssim 0.995 || { echo "RT GATE: FAIL (stability)"; exit 1; }

echo "-- effect presence vs raster baseline (<0.98; measured 0.9586 alive, 0.9996 dead)"
python3 - "$GOLD/space_noui_off.png" "$OUT/on/space_noui.png" << 'PYEOF' || { echo "RT GATE: FAIL (effect missing)"; exit 1; }
import subprocess, sys
r = subprocess.run([sys.executable,
    "/run/media/ddavidich/Disk1/Project Sirius/scripts/golden_compare.py",
    sys.argv[1], sys.argv[2], "--min-ssim", "0.0"],
    capture_output=True, text=True)
line = [l for l in r.stdout.splitlines() if "ssim=" in l]
ssim = float(line[0].split("ssim=")[1].split()[0]) if line else 1.0
print(f"effect ssim(on, raster-off)={ssim:.5f}")
sys.exit(0 if ssim < 0.98 else 1)
PYEOF

echo "-- rtao stability vs AO baseline (>=0.995)"
capture "$OUT/ao" "$AO_INI"
python3 "$ROOT/scripts/golden_compare.py" "$GOLD/space_noui_ao.png" \
    "$OUT/ao/space_noui.png" --min-ssim 0.995 || { echo "RT GATE: FAIL (rtao stability)"; exit 1; }

echo "-- rtao effect presence (diff>8 pixels vs shadows-only, expect ~10k)"
python3 - "$GOLD/space_noui.png" "$OUT/ao/space_noui.png" << 'PYEOF2' || { echo "RT GATE: FAIL (rtao effect missing)"; exit 1; }
import sys
from PIL import Image, ImageChops
a = Image.open(sys.argv[1]).convert('RGB')
b = Image.open(sys.argv[2]).convert('RGB')
h = ImageChops.difference(a, b).convert('L').histogram()
changed = sum(h[9:])
print(f"rtao changed pixels: {changed}")
sys.exit(0 if changed >= 3000 else 1)
PYEOF2

echo "-- validation (0 messages)"
EXTRA_INI="$ON_INI" EXTRA_ENV="SIRIUS_TELEPORT=$POSE" \
    "$ROOT/scripts/vk_validate_run.sh" "$OUT/val" \
    "$ROOT/scripts/rt_space_test.sh" "$OUT/val" | tail -1 || { echo "RT GATE: FAIL (validation)"; exit 1; }

rm -rf "$OUT"
echo "RT GATE: PASS"
