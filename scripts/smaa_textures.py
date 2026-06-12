#!/usr/bin/env python3
"""One-shot generator for the SMAA lookup textures embedded in LibreLancer.dll.

Parses the reference C headers from https://github.com/iryoku/smaa
(AreaTex.h 160x560 RG8, SearchTex.h 64x16 R8) and writes the raw byte
payloads used by Render/SmaaTextures.cs:
    src/LibreLancer/Shaders/SMAA/AreaTex.bin    (179200 bytes)
    src/LibreLancer/Shaders/SMAA/SearchTex.bin  (1024 bytes)

Usage: python3 scripts/smaa_textures.py <AreaTex.h> <SearchTex.h> <outdir>
"""
import re
import sys
import pathlib


def parse_c_bytes(path):
    text = pathlib.Path(path).read_text()
    body = text[text.index('{') + 1:text.rindex('}')]
    return bytes(int(tok, 0) for tok in re.findall(r'0x[0-9a-fA-F]+|\d+', body))


def main():
    area_h, search_h, outdir = sys.argv[1:4]
    out = pathlib.Path(outdir)
    out.mkdir(parents=True, exist_ok=True)

    area = parse_c_bytes(area_h)
    assert len(area) == 160 * 560 * 2, f"AreaTex: {len(area)} bytes"
    (out / 'AreaTex.bin').write_bytes(area)

    search = parse_c_bytes(search_h)
    assert len(search) == 64 * 16, f"SearchTex: {len(search)} bytes"
    (out / 'SearchTex.bin').write_bytes(search)

    print(f"AreaTex.bin {len(area)} bytes, SearchTex.bin {len(search)} bytes -> {out}")


if __name__ == '__main__':
    main()
