"""Rig fixes on the derived sheet: fill the chunk the arms left carved out of the rig bodies, and make the far arm's pieces
as bright as the near arm's (and as the idle sprite's arm)."""
from PIL import Image
from collections import Counter
import numpy as np

SRC = "ArcherOutfit_derived.png"
sh = Image.open(SRC).convert("RGBA")
orig = Image.open("original_sheet.png").convert("RGBA")           # the idle cell and the rig rows, as drawn
idle = orig.crop((0, 0, 48, 56))
ia = np.array(idle)

def cell(row, col): return (col * 48, row * 56, col * 48 + 48, row * 56 + 56)

for col in range(4):                                               # the four rig bodies
    box = cell(13, col)
    body = sh.crop(box); ba = np.array(body)
    hole = (ia[:, :, 3] > 0) & (ba[:, :, 3] == 0)
    hole[:28] = False; hole[46:] = False; hole[:, :6] = False; hole[:, 38:] = False     # only the torso area where the arms were
    for y, x in zip(*np.where(hole)):                               # the cloth the idle sprite has there (the resting sleeve, painted on)
        ba[y, x] = ia[y, x]
    print("rig body", col, "filled", int(hole.sum()), "px")
    sh.paste(Image.fromarray(ba), box[:2])

near_up = sh.crop(cell(14, 0)); near_fore = sh.crop(cell(14, 1))
sh.paste(near_up, cell(14, 2)[:2]); sh.paste(near_fore, cell(14, 3)[:2])                # far arm pieces now match the near arm's brightness
sh.save("ArcherOutfit_derived_fixed.png")
