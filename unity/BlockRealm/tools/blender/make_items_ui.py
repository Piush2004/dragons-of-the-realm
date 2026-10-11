"""Item icons, interface sprites, break-crack overlays and sky/particle sprites, drawn headless in Blender:

    blender --background --python make_items_ui.py -- tiles.json <Resources/BlockRealm dir>
"""
import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pixelart import Canvas, ascii_art, mix, save_png, seeded, shade  # noqa: E402

STICK = (121, 86, 44)
STICK_D = (88, 62, 30)
MATERIALS = {
    'wood': ((166, 132, 76), (122, 94, 52)),
    'stone': ((148, 148, 150), (104, 104, 108)),
    'iron': ((222, 222, 226), (160, 160, 168)),
    'diamond': ((96, 232, 224), (40, 160, 154)),
}

HANDLE = [(2, 13), (3, 12), (4, 11), (5, 10), (6, 9), (7, 8), (8, 7), (9, 6), (10, 5), (11, 4)]
PICK_HEAD = [(5, 4), (6, 3), (7, 2), (8, 2), (9, 2), (10, 3), (11, 4), (12, 5), (13, 6), (13, 7), (13, 8),
             (7, 3), (8, 3), (9, 3), (10, 4), (11, 5), (12, 6), (12, 7)]
AXE_HEAD = [(8, 2), (9, 2), (10, 2), (11, 2), (8, 3), (9, 3), (10, 3), (11, 3), (12, 3), (9, 4), (10, 4), (11, 4),
            (12, 4), (13, 4), (10, 5), (11, 5), (12, 5), (13, 5), (11, 6), (12, 6)]
SHOVEL_HEAD = [(11, 2), (12, 2), (10, 3), (11, 3), (12, 3), (13, 3), (10, 4), (11, 4), (12, 4), (13, 4), (11, 5),
               (12, 5), (9, 4), (10, 5)]
HOE_HEAD = [(7, 3), (8, 3), (9, 3), (10, 3), (11, 3), (12, 3), (12, 4), (7, 4)]
SWORD_BLADE = [(5, 10), (6, 9), (7, 8), (8, 7), (9, 6), (10, 5), (11, 4), (12, 3), (13, 2),
               (6, 10), (7, 9), (8, 8), (9, 7), (10, 6), (11, 5), (12, 4), (13, 3)]
SWORD_GUARD = [(3, 9), (4, 10), (5, 11), (6, 12), (4, 9), (5, 10), (6, 11), (3, 10), (4, 11), (5, 12)]
SWORD_GRIP = [(2, 13), (3, 12), (4, 11)]


def draw_tool(kind, material):
    light, dark = MATERIALS[material]
    c = Canvas()
    if kind == 'sword':
        for p in SWORD_GRIP:
            c.set(p[0], p[1], STICK)
        for p in SWORD_GUARD:
            c.set(p[0], p[1], dark)
        pts = set(SWORD_BLADE)
        for p in pts:
            edge = (p[0] + 1, p[1]) not in pts or (p[0], p[1] + 1) not in pts
            c.set(p[0], p[1], dark if edge else light)
        return c
    for p in HANDLE:
        c.set(p[0], p[1], STICK)
        c.set(p[0], p[1] + 1, STICK_D) if c.get(p[0], p[1] + 1)[3] == 0 else None
    head = {'pickaxe': PICK_HEAD, 'axe': AXE_HEAD, 'shovel': SHOVEL_HEAD, 'hoe': HOE_HEAD}[kind]
    pts = set(head)
    for p in pts:
        edge = (p[0] + 1, p[1]) not in pts or (p[0], p[1] + 1) not in pts
        c.set(p[0], p[1], dark if edge else light)
    return c


def line(c, p0, p1, col):
    x0, y0 = p0
    x1, y1 = p1
    n = max(abs(x1 - x0), abs(y1 - y0), 1)
    for i in range(n + 1):
        c.set(round(x0 + (x1 - x0) * i / n), round(y0 + (y1 - y0) * i / n), col)


def outlined(c, color=(24, 20, 20, 255)):
    """Adds a 1px dark outline around opaque pixels."""
    out = Canvas(c.w, c.h)
    for y in range(c.h):
        for x in range(c.w):
            if c.get(x, y)[3] == 0:
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    if c.get(x + dx, y + dy)[3] > 0:
                        out.set(x, y, color)
                        break
            else:
                out.set(x, y, c.get(x, y))
    return out


def blob(c, cx, cy, rx, ry, col, rng=None, jitter=0.0):
    for y in range(c.h):
        for x in range(c.w):
            d = ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2
            if d <= 1.0:
                f = 1.0
                if rng is not None:
                    f += rng.uniform(-jitter, jitter)
                c.set(x, y, shade(col, f))


def icon_stick(n):
    c = Canvas()
    line(c, (3, 12), (12, 3), STICK)
    line(c, (3, 13), (12, 4), STICK_D)
    return c


def icon_coal(n, col=(30, 30, 32), hi=(70, 70, 76)):
    r = seeded(n)
    c = Canvas()
    blob(c, 8, 8, 5, 4.5, col, r, 0.12)
    c.set(6, 6, hi); c.set(7, 6, hi); c.set(10, 8, hi); c.set(6, 9, shade(hi, 0.8))
    return outlined(c)


def icon_charcoal(n):
    return icon_coal(n, (56, 48, 44), (96, 84, 76))


def icon_ingot(col, dark):
    def f(n):
        c = Canvas()
        for y in range(6, 11):
            for x in range(3 + (10 - y) // 2, 12 + (10 - y) // 2 - 2):
                c.set(x, y, col)
        for x in range(3, 12):
            c.set(x + 0, 10, dark)
        c.hline(5, 12, 6, shade(col, 1.2))
        return outlined(c)
    return f


def icon_diamond(n):
    c = Canvas()
    pal = {'l': (170, 250, 245), 'm': (92, 224, 216), 'd': (40, 150, 150)}
    rows = ['................', '................', '.....lllll......', '....lmmmmml.....', '...lmmllmmml....',
            '..lmmlmmmlmml...', '..mmmmmmmmmmm...', '...dmmmmmmmd....', '....dmmmmmd.....',
            '.....dmmmd......', '......dmd.......', '.......d........', '................',
            '................', '................', '................']
    return outlined(ascii_art(rows, pal))


def icon_apple(n):
    c = Canvas()
    blob(c, 7.5, 9, 4.8, 4.6, (210, 38, 36))
    blob(c, 5.5, 7, 1.4, 1.4, (255, 120, 110))
    c.set(8, 4, (96, 64, 30)); c.set(8, 5, (96, 64, 30))
    c.set(9, 3, (70, 160, 50)); c.set(10, 3, (70, 160, 50)); c.set(10, 4, (50, 130, 40))
    return outlined(c)


def meat(body, fat, bone=True):
    def f(n):
        r = seeded(n)
        c = Canvas()
        blob(c, 8, 8, 5.2, 3.8, body, r, 0.08)
        blob(c, 6.5, 7, 2.4, 1.6, fat)
        if bone:
            line(c, (11, 11), (14, 14), (240, 236, 220))
            c.set(14, 13, (240, 236, 220)); c.set(13, 14, (240, 236, 220))
        return outlined(c)
    return f


def icon_leather(n):
    c = Canvas()
    c.rect(3, 4, 12, 11, (150, 96, 52))
    c.border((100, 62, 30)) if False else None
    for x in range(3, 13):
        c.set(x, 4, (110, 70, 36)); c.set(x, 11, (110, 70, 36))
    for y in range(4, 12):
        c.set(3, y, (110, 70, 36)); c.set(12, y, (110, 70, 36))
    c.scatter((176, 118, 66), 10, seeded(n), (4, 5, 11, 10))
    return outlined(c)


def icon_feather(n):
    c = Canvas()
    for i in range(10):
        c.set(3 + i, 12 - i, (245, 245, 245))
        c.set(4 + i, 12 - i, (214, 214, 220))
        if 2 < i < 9:
            c.set(3 + i, 11 - i, (232, 232, 236))
    c.set(2, 13, (160, 150, 140)); c.set(3, 13, (160, 150, 140))
    return outlined(c)


def icon_bone(n):
    c = Canvas()
    line(c, (4, 12), (11, 5), (236, 232, 214))
    line(c, (5, 12), (12, 5), (200, 196, 180))
    for p in [(3, 11), (4, 13), (3, 12), (11, 4), (12, 6), (13, 5)]:
        c.set(p[0], p[1], (236, 232, 214))
    return outlined(c)


def icon_arrow(n):
    c = Canvas()
    line(c, (3, 12), (12, 3), STICK)
    c.set(12, 3, (170, 170, 176)); c.set(13, 2, (170, 170, 176)); c.set(13, 3, (130, 130, 136)); c.set(12, 2, (130, 130, 136))
    for p in [(2, 13), (3, 13), (2, 12), (4, 13), (2, 11)]:
        c.set(p[0], p[1], (240, 240, 240))
    return outlined(c)


def icon_bow(n):
    c = Canvas()
    pts = [(5, 2), (6, 3), (7, 4), (7, 5), (8, 6), (8, 7), (8, 8), (8, 9), (7, 10), (7, 11), (6, 12), (5, 13)]
    for p in pts:
        c.set(p[0], p[1], STICK)
        c.set(p[0] + 1, p[1], STICK_D)
    line(c, (5, 2), (5, 13), (232, 232, 232))
    return outlined(c)


def icon_gunpowder(n):
    r = seeded(n)
    c = Canvas()
    for _ in range(40):
        x, y = int(r.gauss(8, 2.4)), int(r.gauss(9, 1.8))
        c.set(x, y, shade((120, 120, 124), r.uniform(0.7, 1.2)))
    return outlined(c)


def icon_dragon_scale(n):
    c = Canvas()
    pal = {'a': (30, 120, 120), 'b': (60, 190, 180), 'c': (140, 240, 226), 'd': (20, 70, 80)}
    rows = ['................', '.......aa.......', '......abba......', '.....abcbba.....', '....abcbbbba....',
            '....abbbbbba....', '...abbbbbbbba...', '...abbbbbbbda...', '....abbbbbbda...', '....addbbbdda...',
            '.....addddda....', '......aaaaa.....', '................', '................', '................', '................']
    return outlined(ascii_art(rows, pal))


def icon_saddle(n):
    c = Canvas()
    c.rect(3, 6, 12, 10, (120, 78, 42))
    c.rect(5, 4, 10, 6, (100, 64, 34))
    c.rect(3, 10, 12, 11, (80, 52, 28))
    c.set(3, 12, (200, 200, 205)); c.set(12, 12, (200, 200, 205)); c.set(3, 13, (200, 200, 205)); c.set(12, 13, (200, 200, 205))
    c.hline(4, 11, 8, (150, 100, 56))
    return outlined(c)


def icon_shears(n):
    c = Canvas()
    line(c, (4, 3), (11, 10), (210, 210, 216))
    line(c, (11, 3), (4, 10), (170, 170, 178))
    c.rect(3, 11, 5, 13, (200, 40, 40)); c.rect(10, 11, 12, 13, (200, 40, 40))
    c.set(7, 7, (60, 60, 66)); c.set(8, 7, (60, 60, 66))
    return outlined(c)


ARMOR_COLOURS = {
    'leather': ((168, 108, 58), (116, 72, 36)),
    'iron': ((226, 226, 230), (150, 150, 158)),
    'diamond': ((100, 236, 226), (40, 160, 154)),
}

ARMOR_SHAPES = {
    'helmet': ['................', '................', '....########....', '...##########...', '..############..',
               '..############..', '..###......###..', '..###......###..', '..##........##..', '................',
               '................', '................', '................', '................', '................', '................'],
    'chestplate': ['................', '..###......###..', '.####......####.', '.#####....#####.', '.##############.',
                   '.##############.', '..############..', '..############..', '..############..', '..############..',
                   '..############..', '..############..', '...##########...', '................', '................', '................'],
    'leggings': ['................', '................', '..############..', '..############..', '..############..',
                 '..###......###..', '..###......###..', '..###......###..', '..###......###..', '..###......###..',
                 '..###......###..', '..###......###..', '................', '................', '................', '................'],
    'boots': ['................', '................', '................', '................', '................',
              '................', '...###....###...', '...###....###...', '...###....###...', '..####....####..',
              '.#####....#####.', '.#####....#####.', '................', '................', '................', '................'],
}


def draw_armor(piece, material):
    light, dark = ARMOR_COLOURS[material]
    c = Canvas()
    r = seeded(piece + material)
    for y, row in enumerate(ARMOR_SHAPES[piece]):
        for x, ch in enumerate(row):
            if ch == '#':
                f = 1.0 + r.uniform(-0.06, 0.06)
                tone = light if (y + x) % 5 else dark
                c.set(x, y, (int(tone[0] * f), int(tone[1] * f), int(tone[2] * f), 255))
    return c


def icon_seeds(n):
    r = seeded(n)
    c = Canvas()
    for _ in range(9):
        x, y = r.randint(4, 11), r.randint(5, 11)
        c.rect(x, y, x + 1, y, (92, 150, 52))
        c.set(x, y + 1, (52, 96, 30))
    return outlined(c)


def icon_wheat(n):
    c = Canvas()
    for i, x in enumerate((5, 8, 11)):
        line(c, (x, 14), (x + (i - 1), 4), (186, 168, 70))
        for k in range(4):
            c.set(x + (i - 1) - 1, 4 + k, (230, 198, 80))
            c.set(x + (i - 1) + 1, 5 + k, (230, 198, 80))
        c.set(x + (i - 1), 2, (240, 214, 96))
        c.set(x + (i - 1), 3, (240, 214, 96))
    return outlined(c)


def icon_bread(n):
    c = Canvas()
    blob(c, 8, 9, 6.0, 3.6, (196, 140, 62))
    blob(c, 8, 8, 5.0, 2.4, (222, 170, 86))
    for x in (5, 8, 11):
        c.set(x, 7, (150, 100, 44))
        c.set(x + 1, 8, (150, 100, 44))
    return outlined(c)


def icon_bucket(filled):
    def f(n):
        rows = ['................', '................', '...##########...', '..#..........#..', '..#..........#..',
                '...#........#...', '...#........#...', '...#........#...', '....#......#....', '....#......#....',
                '.....########...', '................', '................', '................', '................', '................']
        c = ascii_art(rows, {'#': (186, 190, 198)})
        if filled:
            for y in range(3, 9):
                for x in range(3, 13):
                    if c.get(x, y)[3] == 0:
                        c.set(x, y, (60, 110, 230))
        return outlined(c)
    return f


def build_items():
    d = {
        'stick': icon_stick, 'coal': icon_coal, 'charcoal': icon_charcoal,
        'iron_ingot': icon_ingot((220, 220, 226), (150, 150, 158)),
        'gold_ingot': icon_ingot((252, 220, 64), (190, 150, 24)),
        'diamond': icon_diamond, 'apple': icon_apple, 'leather': icon_leather, 'feather': icon_feather,
        'bone': icon_bone, 'arrow': icon_arrow, 'bow': icon_bow, 'gunpowder': icon_gunpowder,
        'dragon_scale': icon_dragon_scale, 'saddle': icon_saddle, 'shears': icon_shears,
        'raw_pork': meat((236, 150, 150), (255, 200, 200)),
        'cooked_pork': meat((170, 98, 54), (206, 140, 84)),
        'raw_beef': meat((190, 56, 56), (230, 120, 110)),
        'steak': meat((130, 70, 40), (170, 106, 62)),
        'raw_chicken': meat((236, 190, 170), (250, 225, 210)),
        'cooked_chicken': meat((196, 140, 70), (226, 176, 104)),
        'raw_mutton': meat((200, 80, 80), (236, 140, 130)),
        'cooked_mutton': meat((146, 84, 50), (186, 120, 74)),
        'rotten_flesh': meat((110, 130, 70), (140, 160, 90), bone=False),
        'seeds': icon_seeds, 'wheat': icon_wheat, 'bread': icon_bread,
        'bucket': icon_bucket(False), 'water_bucket': icon_bucket(True),
    }
    for mat in ARMOR_COLOURS:
        for piece in ('helmet', 'chestplate', 'leggings', 'boots'):
            d['%s_%s' % (mat, piece)] = (lambda m, pc: (lambda n: outlined(draw_armor(pc, m))))(mat, piece)
    for material in MATERIALS:
        for kind in ('pickaxe', 'axe', 'shovel', 'sword', 'hoe'):
            d['%s_%s' % (material, kind)] = (lambda m, k: (lambda n: outlined(draw_tool(k, m))))(material, kind)
    return d


# ---------------------------------------------------------------- interface sprites

def ui_slot():
    c = Canvas(18, 18, (60, 60, 64))
    c.rect(1, 1, 16, 16, (139, 139, 139))
    c.hline(0, 17, 0, (55, 55, 55)); c.vline(0, 0, 17, (55, 55, 55))
    c.hline(1, 17, 17, (255, 255, 255)); c.vline(17, 1, 17, (255, 255, 255))
    c.rect(1, 1, 16, 1, (60, 60, 60)); c.rect(1, 1, 1, 16, (60, 60, 60))
    return c


def ui_slot_selected():
    c = Canvas(22, 22)
    c.border((255, 255, 255, 255), 2)
    c.border((30, 30, 30, 255), 1)
    c.rect(2, 2, 19, 19, (255, 255, 255, 0))
    return c


def ui_panel():
    c = Canvas(16, 16, (198, 198, 198))
    c.hline(0, 15, 0, (255, 255, 255)); c.vline(0, 0, 15, (255, 255, 255))
    c.hline(0, 15, 15, (85, 85, 85)); c.vline(15, 0, 15, (85, 85, 85))
    c.hline(1, 14, 14, (139, 139, 139)); c.vline(14, 1, 14, (139, 139, 139))
    c.set(0, 15, (198, 198, 198)); c.set(15, 0, (198, 198, 198))
    return c


def ui_button(hover):
    base = (118, 118, 124) if not hover else (130, 138, 190)
    c = Canvas(16, 16, base)
    c.hline(0, 15, 0, (190, 190, 196)); c.vline(0, 0, 15, (190, 190, 196))
    c.hline(0, 15, 15, (40, 40, 44)); c.vline(15, 0, 15, (40, 40, 44))
    c.hline(1, 14, 14, shade(base, 0.7)); c.vline(14, 1, 14, shade(base, 0.7))
    return c


HEART = ['..##..##.', '.#RR##RR#', '#RWRRRRR#', '#RRRRRRR#', '#RRRRRRR#', '.#RRRRR#.', '..#RRR#..', '...#R#...', '....#....']


def ui_heart(state):
    pal = {'#': (30, 4, 4), 'R': (220, 30, 30), 'W': (255, 140, 140)}
    c = ascii_art(HEART, pal)
    if state == 'empty':
        for y in range(9):
            for x in range(9):
                p = c.get(x, y)
                if p[3] and p != (30, 4, 4, 255):
                    c.set(x, y, (60, 20, 24))
    elif state == 'half':
        for y in range(9):
            for x in range(5, 9):
                p = c.get(x, y)
                if p[3] and p != (30, 4, 4, 255):
                    c.set(x, y, (60, 20, 24))
    return c


DRUM = ['..#####..', '.#BBBBB#.', '#BBBBBBB#', '#BBBBBBB#', '#BBBBBBB#', '.#BBBBB#.', '..#BBB#..', '...#W#...', '..#WWW#..']


def ui_food(state):
    pal = {'#': (50, 24, 8), 'B': (190, 110, 44), 'W': (240, 234, 214)}
    c = ascii_art(DRUM, pal)
    if state != 'full':
        lo = 5 if state == 'half' else 0
        for y in range(9):
            for x in range(lo if state == 'half' else 0, 9):
                p = c.get(x, y)
                if p[3] and p != (50, 24, 8, 255) and p != (240, 234, 214, 255):
                    c.set(x, y, (72, 52, 38))
    return c


def ui_bubble():
    c = Canvas(9, 9)
    for y in range(9):
        for x in range(9):
            d = math.hypot(x - 4, y - 4)
            if 2.8 <= d <= 4.1:
                c.set(x, y, (140, 200, 255))
            elif d < 2.8:
                c.set(x, y, (60, 120, 220, 120))
    c.set(3, 2, (255, 255, 255))
    return c


def ui_crosshair():
    c = Canvas(15, 15)
    for i in range(15):
        if 5 <= i <= 9:
            continue
        c.set(7, i, (255, 255, 255, 220)); c.set(i, 7, (255, 255, 255, 220))
    c.set(7, 7, (255, 255, 255, 220))
    return c


def crack_stage(stage):
    r = seeded('crack', 0)
    c = Canvas()
    count = 3 + stage * 6
    cx = cy = 7.5
    n = 0
    angle0 = 0.7
    k = 0
    while n < count:
        ang = angle0 + k * 0.9 + r.uniform(-0.3, 0.3)
        reach = 2 + (stage + 1) * 0.55 + r.uniform(0, 2.5)
        steps = int(reach)
        for s in range(steps):
            x = int(round(cx + math.cos(ang) * s))
            y = int(round(cy + math.sin(ang) * s))
            c.set(x, y, (0, 0, 0, 190))
            n += 1
        k += 1
        if k > 40:
            break
    return c


def soft_disc(size, inner, outer, color, max_alpha=255):
    c = Canvas(size, size)
    cx = cy = (size - 1) / 2.0
    for y in range(size):
        for x in range(size):
            d = math.hypot(x - cx, y - cy) / (size / 2.0)
            a = 1.0 if d <= inner else max(0.0, 1.0 - (d - inner) / max(1e-6, outer - inner))
            c.set(x, y, (color[0], color[1], color[2], int(a * max_alpha)))
    return c


def sun_sprite():
    c = Canvas(32, 32)
    for y in range(32):
        for x in range(32):
            if 8 <= x < 24 and 8 <= y < 24:
                edge = x in (8, 23) or y in (8, 23)
                c.set(x, y, (255, 240, 170) if edge else (255, 252, 214))
    return c


def moon_sprite():
    c = Canvas(32, 32)
    r = seeded('moon')
    for y in range(8, 24):
        for x in range(8, 24):
            c.set(x, y, shade((232, 234, 242), 1.0 + r.uniform(-0.04, 0.04)))
    for cx, cy in ((12, 12), (19, 16), (14, 20)):
        c.rect(cx, cy, cx + 2, cy + 1, (190, 194, 208))
    return c


def cloud_texture():
    r = seeded('cloud')
    size = 64
    cells = [[r.random() for _ in range(size // 4)] for _ in range(size // 4)]
    c = Canvas(size, size)
    for y in range(size):
        for x in range(size):
            v = cells[y // 4][x // 4]
            a = 255 if v > 0.58 else 0
            c.set(x, y, (255, 255, 255, a))
    return c


def flame_particle():
    size = 32
    c = Canvas(size, size)
    cx = cy = 15.5
    for y in range(size):
        for x in range(size):
            d = math.hypot((x - cx) / 1.0, (y - cy) / 1.15) / 16.0
            a = max(0.0, 1.0 - d) ** 1.6
            col = mix((255, 250, 200), (255, 120, 20), min(1.0, d * 1.4))
            c.set(x, y, (col[0], col[1], col[2], int(a * 255)))
    return c


def smoke_particle():
    r = seeded('smoke')
    size = 32
    c = Canvas(size, size)
    cx = cy = 15.5
    for y in range(size):
        for x in range(size):
            d = math.hypot(x - cx, y - cy) / 16.0
            a = max(0.0, 1.0 - d) ** 1.2 * (0.75 + 0.25 * r.random())
            c.set(x, y, (60, 60, 64, int(a * 200)))
    return c


def main():
    argv = sys.argv[sys.argv.index('--') + 1:]
    spec = json.load(open(argv[0]))
    root = argv[1]
    items = build_items()
    missing = []
    for name in spec['items']:
        fn = items.get(name)
        if fn is None:
            c = Canvas()
            c.rect(2, 2, 13, 13, (255, 0, 255, 255))
            missing.append(name)
        else:
            c = fn(name)
        save_png(c, os.path.join(root, 'Items', name + '.png'))

    ui = os.path.join(root, 'UI')
    save_png(ui_slot(), os.path.join(ui, 'slot.png'))
    save_png(ui_slot_selected(), os.path.join(ui, 'slot_selected.png'))
    save_png(ui_panel(), os.path.join(ui, 'panel.png'))
    save_png(ui_button(False), os.path.join(ui, 'button.png'))
    save_png(ui_button(True), os.path.join(ui, 'button_hover.png'))
    for s in ('full', 'half', 'empty'):
        save_png(ui_heart(s), os.path.join(ui, 'heart_%s.png' % s))
        save_png(ui_food(s), os.path.join(ui, 'food_%s.png' % s))
    save_png(ui_bubble(), os.path.join(ui, 'bubble.png'))
    save_png(ui_crosshair(), os.path.join(ui, 'crosshair.png'))
    for i in range(10):
        save_png(crack_stage(i), os.path.join(root, 'Cracks', 'crack_%d.png' % i))
    fx = os.path.join(root, 'Sky')
    save_png(sun_sprite(), os.path.join(fx, 'sun.png'))
    save_png(moon_sprite(), os.path.join(fx, 'moon.png'))
    save_png(cloud_texture(), os.path.join(fx, 'clouds.png'))
    save_png(flame_particle(), os.path.join(root, 'Particles', 'flame.png'))
    save_png(smoke_particle(), os.path.join(root, 'Particles', 'smoke.png'))
    print('items written: %d, missing drawings: %s' % (len(spec['items']), missing))


main()
