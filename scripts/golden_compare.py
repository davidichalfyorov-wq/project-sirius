#!/usr/bin/env python3
"""Golden screenshot comparator for render-backend parity (Phase 0).

Computes windowed SSIM on luma plus per-channel MAE, writes an amplified
diff map next to the candidate, and fails when SSIM drops below the
threshold (roadmap target: raster parity SSIM >= 0.999).

Usage: golden_compare.py reference.png candidate.png [--min-ssim 0.999]
"""
import argparse
import sys
from PIL import Image, ImageChops


def luma(img):
    return img.convert("L")


def windowed_ssim(a, b, window=8):
    # Classic SSIM over non-overlapping windows; plain-python but fast enough
    # for 1024x768 goldens. Constants per Wang et al. with L=255.
    c1, c2 = (0.01 * 255) ** 2, (0.03 * 255) ** 2
    pa, pb = a.load(), b.load()
    w, h = a.size
    total = 0.0
    count = 0
    for wy in range(0, h - window + 1, window):
        for wx in range(0, w - window + 1, window):
            sa = sb = saa = sbb = sab = 0.0
            for y in range(wy, wy + window):
                for x in range(wx, wx + window):
                    va, vb = pa[x, y], pb[x, y]
                    sa += va
                    sb += vb
                    saa += va * va
                    sbb += vb * vb
                    sab += va * vb
            n = window * window
            ma, mb = sa / n, sb / n
            va = saa / n - ma * ma
            vb = sbb / n - mb * mb
            cov = sab / n - ma * mb
            total += ((2 * ma * mb + c1) * (2 * cov + c2)) / \
                     ((ma * ma + mb * mb + c1) * (va + vb + c2))
            count += 1
    return total / max(count, 1)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("reference")
    ap.add_argument("candidate")
    ap.add_argument("--min-ssim", type=float, default=0.999)
    ap.add_argument("--mask", help="PNG: black areas are excluded (known noise)")
    args = ap.parse_args()

    ref = Image.open(args.reference).convert("RGB")
    cand = Image.open(args.candidate).convert("RGB")
    if ref.size != cand.size:
        print(f"FAIL size mismatch: {ref.size} vs {cand.size}")
        return 2

    if args.mask:
        # Stamp masked (black) areas with identical grey in both images:
        # those windows then always compare equal.
        mask = Image.open(args.mask).convert("L").resize(ref.size)
        grey = Image.new("RGB", ref.size, (64, 64, 64))
        inverted = mask.point(lambda p: 255 if p < 128 else 0)
        ref.paste(grey, (0, 0), inverted)
        cand.paste(grey, (0, 0), inverted)

    ssim = windowed_ssim(luma(ref), luma(cand))
    diff = ImageChops.difference(ref, cand)
    mae = sum(
        sum(px) for px in diff.resize((ref.width // 4, ref.height // 4)).getdata()
    ) / (3 * (ref.width // 4) * (ref.height // 4))
    diff_path = args.candidate.rsplit(".", 1)[0] + ".diff.png"
    diff.point(lambda p: min(255, p * 8)).save(diff_path)

    verdict = "PASS" if ssim >= args.min_ssim else "FAIL"
    print(f"{verdict} ssim={ssim:.5f} mae={mae:.3f} min={args.min_ssim} diff={diff_path}")
    return 0 if verdict == "PASS" else 1


if __name__ == "__main__":
    sys.exit(main())
