"""Derive the archer's other animation frames from its idle frame, layered like a 2D character: rigid legs, a torso and
clothing piece that overhangs them, a head that rides with the torso, and two arms drawn as clean sleeve-and-hand shapes
that rotate from the shoulder. Nothing is stretched or sheared; legs and arms keep their length in every frame."""
from collections import Counter
from math import radians, sin, cos
from PIL import Image

D = "/private/tmp/claude-501/-Users-genesis-My-project/0cedc54b-ed41-4b36-81da-2d4959f6f78e/"
OUT = D + "scratchpad/stack/"
SRC = D + "images/18.png"
CW, CH = 24, 28                  # logical cell: 2x2 blocks of the 48 x 56 template cell

sheet = Image.open(SRC).convert("RGBA")
idle_px = sheet.crop((0, 0, 48, 56))
IDLE = [[idle_px.getpixel((2 * x, 2 * y)) if idle_px.getpixel((2 * x, 2 * y))[3] > 127 else None for x in range(CW)] for y in range(CH)]

LEG_TOP, LEG_BOTTOM = 22, 26     # logical rows of the legs and boots below the robe
UPPER_BOTTOM = 21
SPLIT = 12                       # back leg = columns left of this, front leg = from here on
HEM_TOP = 19                     # rows of the robe's lower hem that can sway
NEAR_ARM = (14, 18, 15, 20)      # x0, x1 (exclusive), y0, y1 of the arm at the front of the chest (the right oval)
FAR_ARM = (6, 10, 14, 20)        # the arm behind the body (the left oval)
NEAR_SHOULDER, FAR_SHOULDER = (15.5, 15.0), (8.0, 14.5)
ARM_LEN = 5


def blank(): return [[None] * CW for _ in range(CH)]
def put(c, x, y, col):
    if col is not None and 0 <= x < CW and 0 <= y < CH: c[y][x] = col
def in_box(b, x, y): return b[0] <= x < b[1] and b[2] <= y <= b[3]


def ring_colour(box):
    near = Counter()
    for y in range(box[2], box[3] + 1):
        for x in (box[0] - 1, box[0] - 2, box[1], box[1] + 1):
            if 0 <= x < CW and IDLE[y][x] is not None: near[IDLE[y][x]] += 1
    return near.most_common(1)[0][0]


def dominant(box, y0, y1):
    n = Counter(IDLE[y][x] for y in range(y0, y1 + 1) for x in range(box[0], box[1]) if IDLE[y][x] is not None)
    return n.most_common(1)[0][0]


ARM_EDGE = (22, 14, 38, 255)
NEAR_FILL, FAR_FILL = ring_colour(NEAR_ARM), ring_colour(FAR_ARM)
SLEEVE = {"near": dominant(NEAR_ARM, 15, 18), "far": dominant(FAR_ARM, 15, 18)}
HAND = {"near": dominant(NEAR_ARM, 19, 20), "far": dominant(FAR_ARM, 19, 20)}


def draw_arm(c, which, angle, shift=(0, 0)):
    """An arm two blocks thick from the shoulder, `angle` degrees from straight down (positive = forward/right,
    180 = straight up); the last block is the hand."""
    sx, sy = NEAR_SHOULDER if which == "near" else FAR_SHOULDER
    a = radians(angle); dx, dy = sin(a), cos(a)
    px, py = (-dy, dx) if abs(dx) < abs(dy) else (0.0, 1.0)          # sideways across the arm
    cells = []
    for i in range(ARM_LEN + 1):
        x, y = sx + dx * i + shift[0], sy + dy * i + shift[1]
        colour = HAND[which] if i == ARM_LEN else SLEEVE[which]
        for k in (-0.5, 0.5): cells.append((round(x + px * k), round(y + py * k), colour))
    mine = {(x, y) for x, y, _ in cells}
    for x, y, _ in cells:                                              # a dark edge round the arm so it reads against the hood and cloth
        for ox, oy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            if (x + ox, y + oy) not in mine: put(c, x + ox, y + oy, ARM_EDGE)
    for x, y, colour in cells: put(c, x, y, colour)
    return c


def frame(front=(0, 0), back=(0, 0), up=0, lean=0, arms=None, hem=(0, 0)):
    """front/back = (dx, lift) of each rigid leg (lift in rows, positive = raised); up = rows the torso and head rise
    (negative = sink onto the legs); lean = columns the head leans forward; arms = (near, far) angles in degrees, or None to
    keep the painted arms; hem = (dx, dy) of the robe's lowest rows."""
    c = blank()
    for (x0, x1), (dx, lift) in (((0, SPLIT), back), ((SPLIT, CW), front)):          # rigid: the whole leg moves, never stretches
        for y in range(LEG_TOP, LEG_BOTTOM + 1):
            for x in range(x0, x1): put(c, x + dx, y - lift, IDLE[y][x])
    for y in range(0, UPPER_BOTTOM + 1):
        shift = round(lean * (UPPER_BOTTOM - y) / (UPPER_BOTTOM - 3))
        hx, hy = hem if y >= HEM_TOP else (0, 0)
        for x in range(CW):
            col = IDLE[y][x]
            if arms is not None and col is not None:
                if in_box(NEAR_ARM, x, y): col = NEAR_FILL
                elif in_box(FAR_ARM, x, y): col = FAR_FILL
            put(c, x + shift + hx, y - up + hy, col)
    if arms is not None:
        sh = (round(lean * 0.5), -up)
        draw_arm(c, "far", arms[1], sh)
        draw_arm(c, "near", arms[0], sh)
    return c


A = [3, 2, 0, -2, -3, -2, 0, 2]                      # one leg's forward/back position through the 8 run frames
LIFT = [0, 0, 0, 0, 0, 1, 1, 1]                      # the leg swinging forward is lifted
SINK = [-1, -1, 0, -1, -1, -1, 0, -1]                # the torso sinks onto the legs at contact and is highest passing over


def run(lean=0, reach=0, swing=34):
    out = []
    for i in range(8):
        j = (i + 4) % 8
        out.append(frame(front=(A[i] + (reach if A[i] > 0 else 0), LIFT[i]), back=(A[j] - (reach if A[j] < 0 else 0), LIFT[j]),
                         up=SINK[i], lean=lean, arms=(-A[i] * swing / 3, A[i] * swing / 3),
                         hem=(-1 if i in (1, 2, 5, 6) else 0, 0)))
    return out


UP = 165                                             # arms thrown up beside the head (degrees from straight down)

ROWS = {
    0: [frame()],                                                                        # Idle: one still frame (no idle animation)
    1: run(),
    2: run(lean=1, reach=1, swing=44),                                                   # Sprint: leaning, longer strides, bigger arms
    3: [frame(front=(2, 0), back=(-2, 0), up=-1, arms=(20, -20))],                       # Land: knees bent, arms out
    4: [frame(front=(2, 1), back=(-2, 0), up=0, arms=(UP, -UP + 10), hem=(0, -1))],       # JumpUp: arms up, front knee raised
    5: [frame(front=(2, 0), back=(-2, 0), up=0, arms=(UP, -UP + 10), hem=(0, -1))],       # Fall: arms up and the legs apart
    6: [frame(front=(2, 1), back=(-2, 1), up=0, arms=(UP, -UP + 10)), frame(front=(2, 2), back=(-2, 2), up=0, arms=(UP, -UP + 10))],
    7: [frame(front=(0, 0), back=(0, 0), up=0, lean=1, arms=(UP + 5, -UP - 5))],         # FastFall: straight legs, arms straight up
    11: [frame(arms=(150, -20)), frame(arms=(70, -20)), frame(arms=(25, -20))],                  # Swing: only the arms move, the head and torso stay put
    12: [frame(front=(2, 0), back=(-2, 0), lean=-2, arms=(-50, -70))],                    # Hit: arms flung back
}


def cell_image(c):
    im = Image.new("RGBA", (48, 56), (0, 0, 0, 0)); p = im.load()
    for y in range(CH):
        for x in range(CW):
            if c[y][x] is None: continue
            for dy in (0, 1):
                for dx in (0, 1): p[2 * x + dx, 2 * y + dy] = c[y][x]
    return im


if __name__ == "__main__":
    out = sheet.copy()
    for row, frames in ROWS.items():
        for col in range(8): out.paste((0, 0, 0, 0), (col * 48, row * 56, col * 48 + 48, row * 56 + 56))
        for col, c in enumerate(frames): out.alpha_composite(cell_image(c), (col * 48, row * 56))
    out.save(OUT + "ArcherOutfit_derived.png")
    print("idle frame identical to the original:", list(cell_image(ROWS[0][0]).getdata()) == list(idle_px.getdata()))
