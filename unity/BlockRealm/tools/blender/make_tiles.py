"""Draws every 16x16 block tile, run headless in Blender:

    blender --background --python make_tiles.py -- tiles.json <out dir>

The tile names come from the game's block registry (tiles.json is written by the core test tool), so the art can
never drift out of sync with the blocks. Anything without a drawing function is painted magenta so it is obvious.
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pixelart import Canvas, ascii_art, mix, save_png, seeded, shade  # noqa: E402

GRASS = (100, 160, 54)
GRASS_D = (78, 130, 42)
GRASS_L = (128, 190, 70)
DIRT = (134, 96, 67)
STONE = (125, 125, 127)
PLANK = (170, 135, 80)
WATER = (46, 96, 214)


def noise(name, base, spread=0.12, extra=0):
    c = Canvas()
    c.speckle(base, spread, seeded(name, extra))
    return c


def t_stone(n):
    c = noise(n, STONE, 0.10)
    r = seeded(n, 1)
    c.scatter(shade(STONE, 0.78), 14, r)
    c.scatter(shade(STONE, 1.15), 10, r)
    return c


def t_dirt(n):
    c = noise(n, DIRT, 0.13)
    r = seeded(n, 2)
    c.scatter(shade(DIRT, 0.72), 14, r)
    c.scatter((150, 112, 80), 10, r)
    c.scatter((110, 110, 105), 3, r)
    return c


def t_grass_top(n):
    c = noise(n, GRASS, 0.10)
    r = seeded(n, 3)
    c.scatter(GRASS_D, 22, r)
    c.scatter(GRASS_L, 20, r)
    return c


def t_grass_side(n):
    c = t_dirt('dirt')
    r = seeded(n, 4)
    top = t_grass_top('grass_top')
    for x in range(16):
        depth = 3 + (1 if r.random() < 0.45 else 0) + (1 if r.random() < 0.15 else 0)
        for y in range(depth):
            c.set(x, y, top.get(x, y))
    return c


def t_grass_snow_side(n):
    c = t_dirt('dirt')
    r = seeded(n, 5)
    for x in range(16):
        depth = 3 + (1 if r.random() < 0.5 else 0)
        for y in range(depth):
            v = 1.0 + r.uniform(-0.04, 0.04)
            c.set(x, y, shade((247, 250, 252), v))
    return c


def t_snow(n):
    c = noise(n, (246, 249, 252), 0.025)
    c.scatter((222, 232, 245), 10, seeded(n, 6))
    return c


def t_cobble(n):
    r = seeded(n, 7)
    c = Canvas(16, 16, shade(STONE, 0.55))
    # Irregular stones laid on a loose grid.
    rows = [(0, 4), (5, 9), (10, 15)]
    for (y0, y1) in rows:
        x = r.randint(0, 3)
        while x < 16 + 4:
            w = r.randint(4, 7)
            base = shade(STONE, r.uniform(0.82, 1.12))
            for yy in range(y0, y1):
                for xx in range(x, min(x + w - 1, 16)):
                    f = 1.0 + r.uniform(-0.08, 0.08)
                    c.set(xx % 16, yy, shade(base, f))
            x += w
    return c


def t_planks(n):
    r = seeded(n, 8)
    c = Canvas()
    for i in range(4):
        base = shade(PLANK, r.uniform(0.92, 1.08))
        for y in range(i * 4, i * 4 + 4):
            for x in range(16):
                c.set(x, y, shade(base, 1.0 + r.uniform(-0.05, 0.05)))
        c.hline(0, 15, i * 4 + 3, shade(PLANK, 0.62))
        # a nail-hole or grain mark per plank
        gx = r.randint(1, 13)
        c.hline(gx, gx + 2, i * 4 + 1, shade(PLANK, 0.8))
    return c


def t_bedrock(n):
    c = noise(n, (70, 70, 72), 0.25)
    r = seeded(n, 9)
    c.scatter((30, 30, 32), 40, r)
    c.scatter((110, 110, 114), 18, r)
    return c


def t_water(n):
    c = noise(n, WATER, 0.06)
    r = seeded(n, 10)
    c.scatter((86, 140, 240), 14, r)
    c.scatter((30, 70, 190), 12, r)
    return c


def t_lava(n):
    c = noise(n, (222, 96, 18), 0.18)
    r = seeded(n, 11)
    c.scatter((255, 200, 60), 26, r)
    c.scatter((160, 40, 8), 22, r)
    return c


def t_sand(n):
    c = noise(n, (218, 206, 160), 0.06)
    r = seeded(n, 12)
    c.scatter((196, 182, 140), 18, r)
    c.scatter((236, 226, 186), 12, r)
    return c


def t_gravel(n):
    r = seeded(n, 13)
    c = noise(n, (130, 126, 124), 0.12)
    for _ in range(18):
        x, y = r.randint(0, 14), r.randint(0, 14)
        tone = r.choice([(104, 100, 98), (150, 146, 142), (118, 112, 108), (168, 164, 160)])
        c.rect(x, y, x + 1, y + r.randint(0, 1), tone)
    return c


def ore(name, spot, spot2):
    def f(n):
        c = t_stone('stone')
        r = seeded(n, 14)
        for _ in range(5):
            cx, cy = r.randint(2, 13), r.randint(2, 13)
            for dx, dy in [(0, 0), (1, 0), (0, 1), (1, 1), (-1, 0)]:
                if r.random() < 0.85:
                    c.set(cx + dx, cy + dy, spot if r.random() < 0.7 else spot2)
        return c
    return f


def log_side(base, dark, light):
    def f(n):
        r = seeded(n, 15)
        c = Canvas()
        for x in range(16):
            col = shade(base, 1.0 + 0.07 * ((x * 5) % 7 - 3) / 3.0)
            for y in range(16):
                c.set(x, y, shade(col, 1.0 + r.uniform(-0.05, 0.05)))
        for x in (1, 5, 9, 13):
            c.vline(x, 0, 15, dark)
        for _ in range(16):
            c.set(r.randint(0, 15), r.randint(0, 15), light)
        return c
    return f


def log_top(bark, ring, core):
    def f(n):
        c = Canvas(16, 16, bark)
        c.rect(2, 2, 13, 13, ring)
        c.rect(3, 3, 12, 12, shade(ring, 0.88))
        c.rect(5, 5, 10, 10, ring)
        c.rect(6, 6, 9, 9, core)
        r = seeded(n, 16)
        c.scatter(shade(ring, 1.08), 8, r, (3, 3, 12, 12))
        return c
    return f


def leaves(base):
    def f(n):
        r = seeded(n, 17)
        c = Canvas()
        for y in range(16):
            for x in range(16):
                if r.random() < 0.17:
                    c.set(x, y, (0, 0, 0, 0))
                else:
                    tone = r.choice([shade(base, 0.82), shade(base, 1.0), shade(base, 1.18)])
                    c.set(x, y, tone)
        return c
    return f


def t_glass(n):
    c = Canvas()
    c.border((200, 225, 235, 255))
    r = seeded(n, 18)
    for i in range(3, 8):
        c.set(i + 1, i, (255, 255, 255, 255))
    c.set(11, 4, (240, 250, 255, 255))
    c.set(12, 5, (240, 250, 255, 255))
    c.scatter((210, 235, 245, 70), 12, r, (1, 1, 14, 14))
    return c


def t_sandstone_top(n):
    c = noise(n, (216, 204, 158), 0.04)
    return c


def t_sandstone(n):
    c = noise(n, (212, 198, 150), 0.05)
    c.hline(0, 15, 3, (186, 170, 124))
    c.hline(0, 15, 12, (186, 170, 124))
    c.hline(0, 15, 4, (226, 214, 170))
    return c


def t_ice(n):
    c = noise(n, (150, 190, 250), 0.06)
    r = seeded(n, 19)
    for i in range(5):
        c.set(2 + i * 3, 2 + (i * 5) % 12, (235, 245, 255, 255))
    c.scatter((120, 165, 235), 14, r)
    return c


def t_cactus_top(n):
    c = Canvas(16, 16, (40, 120, 50))
    c.rect(2, 2, 13, 13, (60, 150, 64))
    c.rect(5, 5, 10, 10, (46, 128, 52))
    return c


def t_cactus_side(n):
    r = seeded(n, 20)
    c = Canvas(16, 16, (50, 130, 56))
    for x in (0, 15):
        c.vline(x, 0, 15, (30, 90, 40))
    for x in (4, 8, 12):
        c.vline(x, 0, 15, (66, 154, 70))
    for _ in range(14):
        c.set(r.randint(1, 14), r.randint(0, 15), (20, 60, 24))
    return c


def t_clay(n):
    c = noise(n, (160, 166, 180), 0.04)
    c.scatter((142, 150, 165), 10, seeded(n, 21))
    return c


def t_bricks(n):
    c = Canvas(16, 16, (186, 182, 176))
    r = seeded(n, 22)
    for row in range(4):
        y0 = row * 4
        off = 0 if row % 2 == 0 else 4
        for k in range(-1, 3):
            x0 = off + k * 8
            col = shade((160, 70, 52), r.uniform(0.85, 1.12))
            for y in range(y0, y0 + 3):
                for x in range(x0, x0 + 7):
                    c.set(x, y, shade(col, 1.0 + r.uniform(-0.05, 0.05)))
    return c


def t_crafting_top(n):
    c = t_planks('planks')
    c.border(shade(PLANK, 0.55))
    for i in (5, 10):
        c.hline(1, 14, i, shade(PLANK, 0.6))
        c.vline(i, 1, 14, shade(PLANK, 0.6))
    return c


def t_crafting_side(n):
    c = t_planks('planks')
    c.rect(1, 1, 14, 5, shade(PLANK, 0.8))
    c.hline(1, 14, 6, shade(PLANK, 0.5))
    # hanging tools: saw, hammer
    c.rect(3, 8, 6, 9, (170, 170, 175))
    c.rect(4, 10, 4, 13, (110, 80, 50))
    c.rect(9, 8, 12, 9, (90, 90, 96))
    c.rect(10, 10, 10, 14, (110, 80, 50))
    return c


def t_furnace_top(n):
    c = t_stone('stone')
    c.border(shade(STONE, 0.6))
    return c


def t_furnace_side(n):
    c = t_cobble('cobble')
    c.rect(4, 6, 11, 12, (40, 40, 42))
    c.rect(5, 7, 10, 11, (22, 22, 24))
    c.hline(4, 11, 5, shade(STONE, 0.5))
    return c


def t_furnace_side_lit(n):
    c = t_furnace_side(n)
    c.rect(5, 7, 10, 11, (232, 130, 30))
    c.rect(6, 8, 9, 11, (255, 210, 80))
    c.rect(7, 9, 8, 10, (255, 245, 170))
    return c


def t_chest_top(n):
    c = Canvas(16, 16, (150, 100, 40))
    c.rect(1, 1, 14, 14, (176, 126, 56))
    c.border((90, 60, 24))
    return c


def t_chest_side(n):
    c = Canvas(16, 16, (176, 126, 56))
    c.border((90, 60, 24))
    c.hline(1, 14, 5, (110, 74, 30))
    c.hline(1, 14, 6, (110, 74, 30))
    c.rect(7, 5, 8, 8, (210, 210, 215))
    c.rect(7, 7, 8, 7, (120, 120, 125))
    r = seeded(n, 23)
    c.scatter((196, 146, 72), 10, r, (1, 7, 14, 14))
    return c


def t_torch(n):
    pal = {'W': (121, 85, 44), 'w': (98, 68, 34), 'Y': (255, 224, 90), 'O': (250, 150, 40), 'R': (210, 80, 20)}
    rows = ['.......','.......', '.......', '.......', '.......', '.......', '.......', '.......', '.......', '.......', '.......', '.......', '.......', '.......', '.......', '.......']
    c = Canvas(16, 16)
    c.rect(7, 6, 8, 15, pal['W'])
    c.vline(7, 6, 15, pal['w'])
    c.rect(7, 3, 8, 5, pal['O'])
    c.set(7, 4, pal['Y']); c.set(8, 4, pal['Y']); c.set(7, 3, pal['R']); c.set(8, 3, pal['R'])
    c.set(7, 5, pal['Y'])
    return c


def t_tall_grass(n):
    r = seeded(n, 24)
    c = Canvas()
    for x in range(1, 15, 2):
        h = r.randint(5, 12)
        tone = r.choice([GRASS, GRASS_D, GRASS_L])
        for y in range(15, 15 - h, -1):
            c.set(x + (1 if (y % 5 == 0 and r.random() < 0.5) else 0), y, shade(tone, 0.85 + 0.3 * (15 - y) / h))
    return c


def flower(petal, center):
    def f(n):
        c = Canvas()
        c.vline(7, 8, 15, (60, 130, 50))
        c.set(6, 12, (70, 150, 56)); c.set(8, 11, (70, 150, 56))
        c.rect(6, 5, 8, 7, petal)
        c.set(7, 6, center)
        c.set(7, 4, petal); c.set(7, 8, petal)
        return c
    return f


def t_dead_bush(n):
    r = seeded(n, 25)
    c = Canvas()
    col = (120, 88, 48)
    c.vline(7, 8, 15, col)
    for i in range(5):
        c.set(7 - i, 8 - i // 1, col)
        c.set(8 + i, 7 - i, col)
        c.set(6 - i // 2, 12 - i, col)
    c.scatter((96, 70, 38), 6, r, (3, 3, 12, 14))
    return c


def t_obsidian(n):
    c = noise(n, (22, 14, 38), 0.35)
    r = seeded(n, 26)
    c.scatter((60, 40, 96), 14, r)
    c.scatter((8, 4, 16), 14, r)
    return c


def t_wool(n):
    c = noise(n, (236, 236, 236), 0.05)
    c.scatter((214, 214, 218), 16, seeded(n, 27))
    return c


def t_scorched(n):
    c = noise(n, (58, 54, 56), 0.16)
    r = seeded(n, 28)
    for _ in range(3):
        x, y = r.randint(1, 13), r.randint(1, 13)
        for i in range(r.randint(3, 6)):
            c.set(x + i % 3, y + i, (230, 110, 24) if r.random() < 0.6 else (255, 190, 60))
    c.scatter((30, 28, 30), 16, r)
    return c


def t_fire(n):
    r = seeded(n, 29)
    c = Canvas()
    for x in range(1, 15):
        h = int(5 + 9 * max(0.0, 1.0 - abs(x - 7.5) / 8.0) + r.randint(0, 3))
        for y in range(15, max(0, 15 - h), -1):
            t = (15 - y) / max(1, h)
            col = mix((255, 240, 150), (240, 90, 18), t)
            if r.random() < 0.9:
                c.set(x, y, col)
    return c


def t_farmland(n):
    c = noise(n, (96, 62, 38), 0.08)
    for y in range(1, 16, 4):
        c.hline(0, 15, y, (70, 44, 26))
        c.hline(0, 15, y + 1, (112, 74, 46))
    return c


def wheat(stage):
    def f(n):
        r = seeded(n, 40)
        c = Canvas()
        height = 2 + stage * 1.7
        ripe = stage >= 6
        for x in (2, 5, 8, 11, 14):
            h = int(height + r.randint(-1, 1))
            h = max(2, min(15, h))
            top_col = (222, 190, 70) if ripe else (96, 160, 60)
            stem_col = (176, 168, 70) if ripe else (70, 130, 48)
            for y in range(15, 15 - h, -1):
                c.set(x, y, stem_col)
            c.set(x, 15 - h, top_col)
            if stage >= 4:
                c.set(x - 1, 15 - h + 1, top_col)
                c.set(x + 1, 15 - h + 1, top_col)
                if stage >= 6:
                    c.set(x, max(0, 15 - h - 1), (240, 210, 90))
        return c
    return f


def t_bed_top(n):
    c = noise(n, (176, 40, 44), 0.05)
    c.rect(1, 1, 14, 5, (238, 238, 238))
    c.hline(1, 14, 6, (190, 190, 190))
    c.border((120, 24, 28))
    return c


def t_bed_side(n):
    c = t_planks('planks')
    c.rect(0, 0, 15, 6, (176, 40, 44))
    c.hline(0, 15, 7, (120, 24, 28))
    return c


def build():
    return {
        'stone': t_stone, 'grass_top': t_grass_top, 'grass_side': t_grass_side, 'dirt': t_dirt,
        'cobble': t_cobble, 'planks': t_planks, 'bedrock': t_bedrock, 'water': t_water, 'lava': t_lava,
        'sand': t_sand, 'gravel': t_gravel,
        'coal_ore': ore('coal', (28, 28, 30), (60, 60, 64)),
        'iron_ore': ore('iron', (214, 176, 148), (186, 140, 112)),
        'gold_ore': ore('gold', (252, 238, 75), (222, 190, 40)),
        'diamond_ore': ore('diamond', (92, 219, 213), (170, 245, 240)),
        'log_oak': log_side((104, 82, 48), (70, 52, 28), (126, 100, 60)),
        'log_oak_top': log_top((104, 82, 48), (168, 136, 84), (128, 100, 58)),
        'leaves_oak': leaves((62, 128, 40)),
        'log_spruce': log_side((74, 52, 30), (50, 34, 18), (92, 66, 40)),
        'log_spruce_top': log_top((74, 52, 30), (120, 86, 50), (96, 68, 38)),
        'leaves_spruce': leaves((40, 92, 54)),
        'log_birch': None,  # filled below (needs black marks)
        'log_birch_top': log_top((214, 210, 196), (196, 178, 128), (170, 150, 100)),
        'leaves_birch': leaves((106, 158, 62)),
        'glass': t_glass, 'sandstone_top': t_sandstone_top, 'sandstone': t_sandstone, 'snow': t_snow, 'ice': t_ice,
        'cactus_top': t_cactus_top, 'cactus_side': t_cactus_side, 'clay': t_clay, 'bricks': t_bricks,
        'crafting_top': t_crafting_top, 'crafting_side': t_crafting_side, 'furnace_top': t_furnace_top,
        'furnace_side': t_furnace_side, 'furnace_side_lit': t_furnace_side_lit, 'chest_top': t_chest_top,
        'chest_side': t_chest_side, 'torch': t_torch, 'tall_grass': t_tall_grass,
        'flower_red': flower((214, 40, 40), (250, 220, 60)), 'flower_yellow': flower((250, 220, 50), (200, 140, 20)),
        'dead_bush': t_dead_bush, 'obsidian': t_obsidian, 'wool': t_wool, 'scorched_stone': t_scorched,
        'fire': t_fire, 'grass_snow_side': t_grass_snow_side,
        'farmland_top': t_farmland, 'bed_top': t_bed_top, 'bed_side': t_bed_side,
        'wheat_0': wheat(0), 'wheat_1': wheat(1), 'wheat_2': wheat(2), 'wheat_3': wheat(3),
        'wheat_4': wheat(4), 'wheat_5': wheat(5), 'wheat_6': wheat(6), 'wheat_7': wheat(7),
    }


def t_birch_side(n):
    c = log_side((218, 214, 200), (190, 186, 172), (232, 230, 220))(n)
    r = seeded(n, 30)
    for _ in range(7):
        x, y = r.randint(0, 13), r.randint(0, 14)
        c.rect(x, y, x + r.randint(1, 3), y, (50, 48, 46))
    return c


def main():
    argv = sys.argv[sys.argv.index('--') + 1:]
    spec = json.load(open(argv[0]))
    out = argv[1]
    gens = build()
    gens['log_birch'] = t_birch_side
    missing = []
    for name in spec['tiles']:
        fn = gens.get(name)
        if fn is None:
            c = Canvas(16, 16)
            for y in range(16):
                for x in range(16):
                    c.set(x, y, (255, 0, 255, 255) if (x // 4 + y // 4) % 2 == 0 else (0, 0, 0, 255))
            missing.append(name)
        else:
            c = fn(name)
        save_png(c, os.path.join(out, name + '.png'))
    print('tiles written: %d, missing drawings: %s' % (len(spec['tiles']), missing))


main()
