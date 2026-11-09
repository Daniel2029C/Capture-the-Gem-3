"""Builds Assets/Resources/Maps/stalactite.json: the untouched arena (Tools/source/stalactite_arena.json) with
the shared gem area stamped onto each end and a large spawn room built outside it. The gem area comes from
Tools/source/gem_area_source.json and is the same for every map.

Gem area rows line up with arena rows (row + ROW_OFFSET) so its floor meets the arena's floor line and its
last column sits just left of the arena's first column.
"""
import json, os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
arena = json.load(open(os.path.join(ROOT, "Tools/source/stalactite_arena.json")))
gem = json.load(open(os.path.join(ROOT, "Tools/source/gem_area_source.json")))   # gem[row][col]

GEM_H, GEM_W = len(gem), len(gem[0])
ROW_OFFSET = 11
ROOM_INTERIOR, ROOM_WALL = 24, 2
PREPEND = ROOM_WALL + ROOM_INTERIOR + GEM_W
W0, H = arena["width"], arena["height"]
W = W0 + 2 * PREPEND
GEM_X0 = ROOM_WALL + ROOM_INTERIOR
GEM_COL, GEM_ROW = 17, 26   # the gem's 6x9 frame inside the gem area stamp

PLATFORM, MARKER = 19, 541
SHAPES = {1: "SlopeDownLeft", 2: "SlopeDownRight", 3: "SlopeUpLeft", 4: "SlopeUpRight"}
FLIP = {"SlopeDownLeft": "SlopeDownRight", "SlopeDownRight": "SlopeDownLeft",
        "SlopeUpLeft": "SlopeUpRight", "SlopeUpRight": "SlopeUpLeft"}

liq0 = arena["liquids"] or ["." * W0] * H
tiles = [["B"] * PREPEND + list(r) + ["R"] * PREPEND for r in arena["tiles"]]
liq = [["."] * PREPEND + list(r) + ["."] * PREPEND for r in liq0]
shapes = [dict(s, x=s["x"] + PREPEND) for s in arena["shapes"]]

def put(x, y, glyph):
    tiles[y][x] = glyph
    mx = W - 1 - x
    tiles[y][mx] = {"B": "R", "b": "r"}.get(glyph, glyph)

def put_shape(x, y, shape):
    shapes.append({"x": x, "y": y, "shape": shape})
    shapes.append({"x": W - 1 - x, "y": y, "shape": FLIP.get(shape, shape)})

def fill(x0, y0, x1, y1, glyph):
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            put(x, y, glyph)

# Spawn room interior, open from the arena's ceiling filler down to its floor line.
top, bottom = ROW_OFFSET, ROW_OFFSET + GEM_H - 1
fill(ROOM_WALL, top, ROOM_WALL + ROOM_INTERIOR - 1, bottom, ".")

# Gem area stamp.
for r in range(GEM_H):
    for c in range(GEM_W):
        t = gem[r][c]
        x, y = GEM_X0 + c, ROW_OFFSET + r
        tt = t["TileType"]
        # The 6x9 frame around the gem was background wall in the source; only the pedestal below it is kept.
        in_frame = GEM_COL <= c < GEM_COL + 6 and GEM_ROW <= r < GEM_ROW + 9
        if tt is None or tt == MARKER or in_frame:
            put(x, y, ".")
            continue
        put(x, y, "p" if tt == PLATFORM else "B")
        if t["HalfBlock"]:
            put_shape(x, y, "Half")
        elif t["Slope"] in SHAPES and tt != PLATFORM:
            put_shape(x, y, SHAPES[t["Slope"]])

# Glass steps inside the room, up to the gem area's doorway.
def room_platform(row, x_from, x_to):   # x relative to the gem area's first column (negative = in the room)
    fill(GEM_X0 + x_from, ROW_OFFSET + row, GEM_X0 + x_to, ROW_OFFSET + row, "p")
room_platform(34, -8, -1)
room_platform(30, -14, -6)
room_platform(25, -9, -1)

if not any(e["glyph"] == "p" for e in arena["legend"]):
    arena["legend"].append({"glyph": "p", "tile": "Platform"})

def zone(kind, x, y, w, h):
    return {"kind": kind, "x": x, "y": y, "w": w, "h": h}

out = dict(arena)
out["width"] = W
out["tiles"] = ["".join(r) for r in tiles]
out["liquids"] = ["".join(r) for r in liq]
out["shapes"] = shapes
out["zones"] = [
    zone("SpawnBlue", ROOM_WALL, top, ROOM_INTERIOR, GEM_H),
    zone("SpawnRed", W - ROOM_WALL - ROOM_INTERIOR, top, ROOM_INTERIOR, GEM_H),
    zone("BlueGem", GEM_X0 + GEM_COL, ROW_OFFSET + GEM_ROW, 6, 9),
    zone("RedGem", W - (GEM_X0 + GEM_COL + 6), ROW_OFFSET + GEM_ROW, 6, 9),
    zone("NoBuild", GEM_X0, top, GEM_W, GEM_H),
    zone("NoBuild", W - GEM_X0 - GEM_W, top, GEM_W, GEM_H),
]
json.dump(out, open(os.path.join(ROOT, "Assets/Resources/Maps/stalactite.json"), "w"), indent=1)
print("width", W, "shapes", len(shapes))
