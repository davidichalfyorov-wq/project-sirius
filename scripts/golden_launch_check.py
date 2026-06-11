#!/usr/bin/env python3
import argparse
import sys
from pathlib import Path

from PIL import Image


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Detect broad foreign-target colour bands in launch captures."
    )
    parser.add_argument("image", type=Path)
    parser.add_argument("--max-green-row", type=float, default=0.35)
    parser.add_argument("--max-green-total", type=float, default=0.08)
    parser.add_argument("--max-magenta-row", type=float, default=0.20)
    parser.add_argument("--max-magenta-total", type=float, default=0.04)
    args = parser.parse_args()

    image = Image.open(args.image).convert("RGB")
    width, height = image.size
    pixels = image.load()
    max_green_row = 0.0
    max_magenta_row = 0.0
    green_total = 0
    magenta_total = 0

    for y in range(height):
        green_row = 0
        magenta_row = 0
        for x in range(width):
            r, g, b = pixels[x, y]
            if g > 96 and g > r * 1.25 and g > b * 1.25:
                green_row += 1
            if r > 96 and b > 96 and r > g * 1.25 and b > g * 1.25:
                magenta_row += 1
        max_green_row = max(max_green_row, green_row / width)
        max_magenta_row = max(max_magenta_row, magenta_row / width)
        green_total += green_row
        magenta_total += magenta_row

    green_fraction = green_total / (width * height)
    magenta_fraction = magenta_total / (width * height)
    ok = (
        max_green_row <= args.max_green_row
        and green_fraction <= args.max_green_total
        and max_magenta_row <= args.max_magenta_row
        and magenta_fraction <= args.max_magenta_total
    )
    status = "PASS" if ok else "FAIL"
    print(
        f"{status} green_row={max_green_row:.3f} green_total={green_fraction:.3f} "
        f"magenta_row={max_magenta_row:.3f} magenta_total={magenta_fraction:.3f}"
    )
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
