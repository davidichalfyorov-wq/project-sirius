#!/usr/bin/env bash
# Build + render the standard volumetric poses and write screenshots + a montage.
# Portable (self-locating ROOT) so it runs from any clone: the project owner can
# run it by hand, a self-hosted GitHub runner can call it, or another agent's
# patch can be verified visually on a machine that actually has a GPU.
#
# Usage:  scripts/ci_render.sh [OUTDIR]
#   OUTDIR defaults to <root>/Output/ci_render
# Env:
#   SIRIUS_ROOT      override the project root (default: this script's parent dir)
#   SIRIUS_RENDERER  vulkan (default) | gl
#   CI_LIVE          1 (default) = temporal on (true gameplay look); 0 = golden
#   CI_POSES         space-separated "name:x,y,z,yaw" list (default: near + far Badlands)
set -u
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="${SIRIUS_ROOT:-$(cd "$SCRIPT_DIR/.." && pwd)}"
OUTDIR="${1:-$ROOT/Output/ci_render}"
RENDERER="${SIRIUS_RENDERER:-vulkan}"
LIVE="${CI_LIVE:-1}"
POSES="${CI_POSES:-near:-7460,500,55000,0 far:-7460,500,115000,180}"

echo "== ci_render =="
echo "ROOT=$ROOT"
echo "OUTDIR=$OUTDIR  RENDERER=$RENDERER  LIVE=$LIVE"
mkdir -p "$OUTDIR"
cd "$ROOT" || { echo "bad ROOT"; exit 2; }

# 0) Ensure gitignored build deps exist (a fresh CI checkout lacks them; an
#    owner's working clone already has them, so these are no-ops there).
if [ ! -x "$ROOT/bin/builddeps/bin/dxc" ]; then
    echo "== fetch dxc (missing) =="
    dxcurl=$(grep -E '^DXC_LINUXX64=' "$ROOT/build.config" | cut -d= -f2-)
    mkdir -p "$ROOT/bin/builddeps"
    curl -fsSL "$dxcurl" -o /tmp/dxc.tar.gz && tar -xzf /tmp/dxc.tar.gz -C "$ROOT/bin/builddeps"
    [ -x "$ROOT/bin/builddeps/bin/dxc" ] || { d=$(find "$ROOT/bin/builddeps" -name dxc -type f | head -1); \
        mkdir -p "$ROOT/bin/builddeps/bin"; ln -sf "$(realpath "$d")" "$ROOT/bin/builddeps/bin/dxc"; }
    chmod +x "$ROOT/bin/builddeps/bin/dxc" 2>/dev/null || true
fi
if [ ! -f "$ROOT/src/CommonVersion.props" ]; then
    printf '%s\n' '<Project><PropertyGroup><InformationalVersion>0.0.0-ci</InformationalVersion></PropertyGroup></Project>' \
        > "$ROOT/src/CommonVersion.props"
fi
# SPIRV-Cross shared lib: reflection the shader compiler P/Invokes. The full
# BuildLL native step also pulls in broken editor-only natives (crunch), so
# build just this one (same flags as BuildLL). No-op on an owner clone.
if [ ! -f "$ROOT/bin/builddeps/libspirv-cross-c-shared.so" ] && command -v cmake >/dev/null 2>&1; then
    echo "== build SPIRV-Cross native (missing) =="
    cmake -S "$ROOT/extern/SPIRV-Cross" -B "$ROOT/obj/spirvcross" -G "Unix Makefiles" \
        -DCMAKE_BUILD_TYPE=MinSizeRel -DSPIRV_CROSS_SHARED=ON -DSPIRV_CROSS_STATIC=OFF \
        -DSPIRV_CROSS_CLI=OFF -DSPIRV_CROSS_ENABLE_TESTS=OFF >/dev/null 2>&1 \
        && make -C "$ROOT/obj/spirvcross" -j"$(nproc)" >/dev/null 2>&1 \
        && mkdir -p "$ROOT/bin/builddeps" && cp "$ROOT/obj/spirvcross"/*.so "$ROOT/bin/builddeps/"
fi

# 1) Build (Release) + sync into build/v3, exactly like ui2_build but root-relative.
echo "== build =="
# Shader.targets invokes the Debug build of LLShaderCompiler; build it first
# (no-op on an owner clone where it already exists).
if [ ! -x "$ROOT/src/LLShaderCompiler/bin/Debug/net10.0/LLShaderCompiler" ]; then
    dotnet build "$ROOT/src/LLShaderCompiler/LLShaderCompiler.csproj" -v minimal 2>&1 | tail -3
fi
if ! dotnet build "$ROOT/src/lancer/lancer.csproj" -c Release -v minimal 2>&1 | tail -6; then
    echo "BUILD FAILED"; exit 1
fi
SRCOUT="$ROOT/src/lancer/bin/Release/net10.0"
mkdir -p "$ROOT/build/v3"
# Copy the WHOLE build output (incl. lancer.runtimeconfig.json + *.deps.json +
# native libs) - a hand-picked DLL list left a fresh build/v3 unrunnable
# ("libhostpolicy.so / lancer.runtimeconfig.json not found").
cp -r "$SRCOUT/." "$ROOT/build/v3/"
echo "build/v3 refreshed"

# 2) Capture each pose into its own dir (golden autoplay rig: menu->launch->settle->shot).
liveenv=""; [ "$LIVE" = "1" ] && liveenv="SIRIUS_VOLFOG_LIVE=1"
shots=()
for entry in $POSES; do
    name="${entry%%:*}"; pose="${entry##*:}"
    out="$OUTDIR/$name"; mkdir -p "$out"; rm -f "$out"/*.png "$out/run.log"
    prof="$(mktemp -d)"; mkdir -p "$prof/.config"
    cp "$HOME/.config/librelancer.ini" "$prof/.config/" 2>/dev/null || {
        echo "ERROR: no ~/.config/librelancer.ini (need freelancer_path set)"; exit 3; }
    printf 'post_aa = off\nvolumetric_nebulae = true\nrt_shadows = false\nrtao = false\nrt_reflections = false\nshadows = true\n' \
        >> "$prof/.config/librelancer.ini"
    echo "== pose $name ($pose) =="
    env SIRIUS_AUTOPLAY=1 SIRIUS_GOLDEN_DIR="$out" SIRIUS_RENDERER="$RENDERER" \
        SIRIUS_PASS_TIMINGS=1 SIRIUS_WINDOW_SIZE=2560x1440 \
        SIRIUS_TELEPORT="$pose" SIRIUS_VOLFOG=1 $liveenv \
        HOME="$prof" XDG_CONFIG_HOME="$prof/.config" \
        XDG_DATA_HOME="$prof/.local/share" XDG_STATE_HOME="$prof/.local/state" \
        dotnet "$ROOT/build/v3/lancer.dll" > "$out/run.log" 2>&1 &
    pid=$!
    for i in $(seq 1 90); do sleep 2; [ -f "$out/space_noui.png" ] && break; \
        kill -0 $pid 2>/dev/null || { echo "DIED"; tail -8 "$out/run.log"; break; }; done
    sleep 3; kill $pid 2>/dev/null; sleep 1; kill -9 $pid 2>/dev/null
    rm -rf "$prof"
    [ -f "$out/space_noui.png" ] && shots+=("$out/space_noui.png") && \
        grep -oE 'vol\.[a-z]+=[0-9.]+' "$out/run.log" | tr '\n' ' ' | sed "s/^/  $name timings: /;s/$/\n/"
done

# 3) Montage (best-effort; needs python3+Pillow, present on the dev machine).
if command -v python3 >/dev/null 2>&1 && [ "${#shots[@]}" -gt 0 ]; then
python3 - "$OUTDIR" "${shots[@]}" <<'PY' 2>/dev/null || true
import sys
from PIL import Image, ImageDraw
outdir, paths = sys.argv[1], sys.argv[2:]
ims = []
for p in paths:
    im = Image.open(p).convert("RGB").resize((640, 360))
    d = ImageDraw.Draw(im); name = p.split("/")[-2]
    d.rectangle([0,0,len(name)*8+8,20], fill=(0,0,0)); d.text((4,4), name, fill=(255,255,0))
    ims.append(im)
W = sum(i.width for i in ims) + 8*(len(ims)-1)
c = Image.new("RGB", (W, 360), (25,25,25))
x = 0
for i in ims: c.paste(i, (x,0)); x += i.width + 8
c.save(f"{outdir}/montage.png"); print(f"montage -> {outdir}/montage.png")
PY
fi
echo "== done: $OUTDIR (shots: ${#shots[@]}) =="
