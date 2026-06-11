#!/usr/bin/env bash
# Render parity gate: capture a fresh set with the given backend and compare
# against the stored GL baseline (tests/goldens/gl). Usage:
#   golden_gate.sh [gl|vulkan] [--update]   (--update: refresh the baseline)
set -e
set -o pipefail
ROOT="/run/media/ddavidich/Disk/Project Sirius"
RENDERER="${1:-gl}"
OUT=$(mktemp -d /tmp/golden_gate.XXXX)
"$ROOT/scripts/golden_capture.sh" "$OUT" "$RENDERER" >/dev/null
if [ "$2" = "--update" ]; then
    cp "$OUT"/{menu,launch,space,space_noui}.png "$ROOT/tests/goldens/gl/"
    echo "baseline updated from $OUT"
    exit 0
fi
fail=0
# Menu runs at 0.980: the frozen THN frame is identical, but the scene is
# dense with text/hull edges where GL and Vulkan rasterize sub-pixel
# coverage differently, and the brighter filmic default amplifies those
# absolute deltas. Structural regressions still trip this; the space gates
# below stay at 0.9985.
python3 "$ROOT/scripts/golden_compare.py" "$ROOT/tests/goldens/gl/menu.png" "$OUT/menu.png" --min-ssim 0.980 | sed 's/^/menu: /' || fail=1
# Launch THNs animate slightly differently across GL/Vulkan and are poor SSIM
# targets. Check the actual regression instead: broad green/magenta foreign
# render-target bands caused by broken post/viewport state.
python3 "$ROOT/scripts/golden_launch_check.py" "$OUT/launch.png" | sed 's/^/launch: /' || fail=1
for f in space space_noui; do
    python3 "$ROOT/scripts/golden_compare.py" "$ROOT/tests/goldens/gl/$f.png" "$OUT/$f.png" \
        --min-ssim 0.9985 --mask "$ROOT/tests/goldens/gl/space.mask.png" | sed "s/^/$f: /" || fail=1
done
exit $fail
