#!/usr/bin/env python3
# Численный детектор «ватности» ближнего волюметрик-слоя (регрессия «пелена»).
# Меряет std высокочастотного остатка (кадр минус gaussian blur 12px) в двух
# боковых панелях неба — мимо солнца, HUD и корабля. Калибровка на эталонах
# Output/phase5_etalons (поза Badlands -7460,500,55000,0 @ 2560x1440):
#   near GOOD (вата)      ~ 0.45
#   near REGRESSED (пелена) ~ 0.09
#   distant GOOD          ~ 3.35
# Гейт ближней позы: >= 0.30 — вата есть; < 0.15 — пелена (FAIL).
import sys
from PIL import Image, ImageFilter
import numpy as np

def vata(path):
    g = Image.open(path).convert('L')
    blur = g.filter(ImageFilter.GaussianBlur(12))
    hi = np.asarray(g, dtype=np.float32) - np.asarray(blur, dtype=np.float32)
    h, w = hi.shape
    left = hi[h//6:h//2, w//12:w//3]
    right = hi[h//6:h//2, 2*w//3:11*w//12]
    return float(np.concatenate([left.ravel(), right.ravel()]).std())

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("usage: vata_metric.py frame.png [frame2.png ...]")
        sys.exit(2)
    fail = False
    for p in sys.argv[1:]:
        v = vata(p)
        verdict = "VATA" if v >= 0.30 else ("FLAT/пелена" if v < 0.15 else "СЕРАЯ ЗОНА")
        print(f"{p}: hi-freq std = {v:.3f}  [{verdict}]")
        fail |= v < 0.15
    sys.exit(1 if fail else 0)
