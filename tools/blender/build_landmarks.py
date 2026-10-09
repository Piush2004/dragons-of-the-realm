"""Builds the world's landmark castles and exports them as one GLB.

Run:  blender --background --python tools/blender/build_landmarks.py -- public/assets/landmarks.glb
Every model stands on z=0 centred on the origin, entrance toward -Y (which exports as +Z).
Dimensions are metres. Designs are original, inspired by the Houses of the Dragon setting.
"""
import math
import os
import random
import sys

import bpy
import numpy as np
from mathutils import Vector

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from assetlib import Structure, colorize, export_glb, fbm, height_to_normal, make_material, save_image  # noqa: E402

OUTPUT = sys.argv[sys.argv.index('--') + 1]
TEXTURE_DIR = os.path.join(os.path.dirname(os.path.abspath(OUTPUT)), '..', 'build', 'textures')

bpy.ops.wm.read_factory_settings(use_empty=True)


def smoothstep(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


# ------------------------------------------------------------------------------------------------ textures

def stone_textures(name, seed, dark, light, rows=9, scorch=0.0):
    """Coursed masonry: rows of uneven blocks with recessed joints, weathering and optional scorching."""
    rng = np.random.default_rng(seed)
    size = 512
    height = np.zeros((size, size))
    tone = np.zeros((size, size))
    row_height = size // rows
    for row in range(rows):
        x = int(rng.integers(0, 50))
        y0 = row * row_height
        while x < size + 80:
            width = int(rng.integers(40, 120))
            shade = rng.uniform(0.35, 1.0)
            xs = np.arange(x, x + width) % size
            ys = np.arange(y0, y0 + row_height)
            fx = np.minimum(np.arange(width), width - 1 - np.arange(width)) / 6.0
            fy = np.minimum(np.arange(row_height), row_height - 1 - np.arange(row_height)) / 6.0
            height[np.ix_(ys, xs)] = np.clip(np.minimum(fx[None, :], fy[:, None]), 0, 1)
            tone[np.ix_(ys, xs)] = shade
            x += width
    grain = fbm(size, 14, 14, 4, rng)
    streaks = fbm(size, 20, 2, 3, rng)
    value = tone * (0.5 + height * 0.5) * (0.7 + grain * 0.6) * (1 - streaks * 0.25)
    value = np.clip(value - scorch * fbm(size, 5, 5, 4, rng) * 0.6, 0, 1)
    color = colorize(value, dark, light)
    return (save_image(name + '_color', color, TEXTURE_DIR),
            save_image(name + '_normal', height_to_normal(height + grain * 0.25, 5.5), TEXTURE_DIR, True))


def tile_textures(name, seed, dark, light):
    rng = np.random.default_rng(seed)
    size = 512
    rows, columns = 12, 12
    ys, xs = np.mgrid[0:size, 0:size]
    row = ys * rows // size
    fy = (ys * rows / size) % 1.0
    fx = ((xs * columns / size) + (row % 2) * 0.5) % 1.0
    column = ((xs * columns / size) + (row % 2) * 0.5).astype(int) % columns
    height = np.clip(fy ** 1.5 * (0.45 + 0.55 * np.sqrt(np.clip((1 - np.abs(fx - 0.5) * 2) * 2.2, 0, 1))), 0, 1)
    per_tile = rng.random((rows, columns))[row, column]
    value = np.clip(0.25 + per_tile * 0.45 + height * 0.25 + fbm(size, 5, 5, 4, rng) * 0.3 - 0.2, 0, 1)
    return (save_image(name + '_color', colorize(value, dark, light), TEXTURE_DIR),
            save_image(name + '_normal', height_to_normal(height, 8.0), TEXTURE_DIR, True))


# ------------------------------------------------------------------------------------------------ kit

RED_STONE, RED_ROOF, CREAM, BLACK, CHARCOAL, PALE, BLUE_ROOF, WINDOW, WOOD = range(9)


def windows(s, center, radius, z, count, material=WINDOW, width=1.1, height=2.6, phase=0.0):
    cx, cy = center
    for k in range(count):
        a = phase + math.tau * k / count
        n = Vector((math.cos(a), math.sin(a), 0))
        side = Vector((-n.y, n.x, 0))
        p = Vector((cx, cy, z)) + n * (radius + 0.06)
        s.quad(p - side * width / 2, p + side * width / 2, p + side * width / 2 + Vector((0, 0, height)), p - side * width / 2 + Vector((0, 0, height)), material, 2.0)


def merlons(s, points, z, material, size=1.8, gap=1.8, height=1.7, thickness=2.0, tile=3.0):
    """Crenellations along a polyline of (x, y) points at height z."""
    for (x0, y0), (x1, y1) in zip(points, points[1:]):
        direction = Vector((x1 - x0, y1 - y0, 0))
        length = direction.length
        if length < 1e-6:
            continue
        direction.normalize()
        normal = Vector((-direction.y, direction.x, 0))
        steps = int(length // (size + gap))
        for i in range(steps):
            d = i * (size + gap) + gap / 2
            c = Vector((x0, y0, z)) + direction * d
            lo = c - normal * thickness / 2
            hi = c + direction * size + normal * thickness / 2
            s.box((min(lo.x, hi.x), min(lo.y, hi.y), z), (max(lo.x, hi.x), max(lo.y, hi.y), z + height), material, tile)


def wall_ring(s, points, z0, z1, thickness, material, closed=True, tile=6.0, crenel=True, gate_at=None):
    """Thick curtain wall through `points`, with crenellations."""
    pts = list(points) + ([points[0]] if closed else [])
    for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
        d = Vector((x1 - x0, y1 - y0, 0))
        n = Vector((-d.y, d.x, 0)).normalized() * thickness / 2
        a, b = Vector((x0, y0, 0)), Vector((x1, y1, 0))
        # Outer face, inner face and top as quads (outward = to the right of the path).
        s.quad(a - n + Vector((0, 0, z0)), b - n + Vector((0, 0, z0)), b - n + Vector((0, 0, z1)), a - n + Vector((0, 0, z1)), material, tile)
        s.quad(b + n + Vector((0, 0, z0)), a + n + Vector((0, 0, z0)), a + n + Vector((0, 0, z1)), b + n + Vector((0, 0, z1)), material, tile)
        s.quad(a + n + Vector((0, 0, z1)), b + n + Vector((0, 0, z1)), b - n + Vector((0, 0, z1)), a - n + Vector((0, 0, z1)), material, tile)
        if crenel:
            merlons(s, [(x0, y0), (x1, y1)], z1, material, thickness=thickness * 0.9)


def round_tower(s, center, radius, z0, z1, material, roof_material, roof_height, sides=20, window_rows=(), flare=1.12, roof='cone'):
    """Tower with a flared base, a corbelled top and a roof."""
    s.lathe([(radius * flare, z0), (radius * flare, z0 + 1.5), (radius, z0 + 4.0), (radius, z1 - 3.0), (radius * 1.12, z1 - 2.0), (radius * 1.12, z1)], sides, material, center, 6.0)
    if roof == 'cone':
        s.cone(center, radius * 1.22, z1, z1 + roof_height, sides, roof_material, 5.0, bulge=0.05)
    for z in window_rows:
        windows(s, center, radius, z, max(3, int(radius * 0.7)), phase=0.3)


def gable_roof(s, lo, hi, z, height, roof_material, wall_material, overhang=1.2, axis='x', tile=4.0):
    x0, y0 = lo
    x1, y1 = hi
    mx, my = (x0 + x1) / 2, (y0 + y1) / 2
    if axis == 'x':
        s.quad((x0 - overhang, y0 - overhang, z), (x1 + overhang, y0 - overhang, z), (x1 + overhang, my, z + height), (x0 - overhang, my, z + height), roof_material, tile)
        s.quad((x1 + overhang, y1 + overhang, z), (x0 - overhang, y1 + overhang, z), (x0 - overhang, my, z + height), (x1 + overhang, my, z + height), roof_material, tile)
        s.tri((x0, y1, z), (x0, y0, z), (x0, my, z + height), wall_material, 6.0)
        s.tri((x1, y0, z), (x1, y1, z), (x1, my, z + height), wall_material, 6.0)
    else:
        s.quad((x0 - overhang, y0 - overhang, z), (mx, y0 - overhang, z + height), (mx, y1 + overhang, z + height), (x0 - overhang, y1 + overhang, z), roof_material, tile)
        s.quad((x1 + overhang, y1 + overhang, z), (mx, y1 + overhang, z + height), (mx, y0 - overhang, z + height), (x1 + overhang, y0 - overhang, z), roof_material, tile)
        s.tri((x1, y0, z), (x0, y0, z), (mx, y0, z + height), wall_material, 6.0)
        s.tri((x0, y1, z), (x1, y1, z), (mx, y1, z + height), wall_material, 6.0)


def gate(s, center_x, y, width, height, material=WINDOW, frame=WOOD, facing=-1):
    """Dark gateway recess with a timber frame set on a wall face at y (facing -1 = toward -Y)."""
    off = 0.15 * facing
    for grow, mat in ((0.8, frame), (0.0, material)):
        w = width / 2 + grow
        s.quad((center_x - w, y + off, 0), (center_x + w, y + off, 0), (center_x + w, y + off, height + grow), (center_x - w, y + off, height + grow), mat, 3.0)


# ------------------------------------------------------------------------------------------------ landmarks

def build_red_keep(name):
    s = Structure()
    # Foundation platform and curtain wall.
    s.box((-82, -62, -8), (82, 62, 2.0), RED_STONE, 8.0)
    wall = [(-75, -55), (75, -55), (75, 55), (-75, 55)]
    wall_ring(s, wall, 2, 22, 5.5, RED_STONE, closed=True)
    for x, y in wall:
        round_tower(s, (x, y), 9.5, 2, 44, RED_STONE, RED_ROOF, 24, window_rows=(18, 30, 38))
    for x in (-38, 38):
        round_tower(s, (x, -55), 6.5, 2, 34, RED_STONE, RED_ROOF, 16, window_rows=(20, 27))
    for y in (-18, 18):
        round_tower(s, (75, y), 6.5, 2, 32, RED_STONE, RED_ROOF, 15, window_rows=(18, 25))
        round_tower(s, (-75, y), 6.5, 2, 32, RED_STONE, RED_ROOF, 15, window_rows=(18, 25))
    gate(s, 0, -58.0, 11, 14)
    round_tower(s, (-13, -55), 7.5, 2, 30, RED_STONE, RED_ROOF, 17)
    round_tower(s, (13, -55), 7.5, 2, 30, RED_STONE, RED_ROOF, 17)
    s.box((-13, -58, 20), (13, -52, 30), RED_STONE, 6.0)

    # The inner holdfast: a square keep ringed by turrets.
    s.box((-30, -22, 2), (30, 22, 40), RED_STONE, 6.0)
    merlons(s, [(-30, -22), (30, -22), (30, 22), (-30, 22), (-30, -22)], 40, RED_STONE, size=2.2, gap=2.0, height=2.0, thickness=2.4)
    for x in (-30, 30):
        for y in (-22, 22):
            s.box((x - 5.5, y - 5.5, 2), (x + 5.5, y + 5.5, 58), RED_STONE, 6.0)
            s.box((x - 6.5, y - 6.5, 58), (x + 6.5, y + 6.5, 60), RED_STONE, 6.0)
            gable_roof(s, (x - 5.5, y - 5.5), (x + 5.5, y + 5.5), 60, 12, RED_ROOF, RED_STONE, 1.0, 'x')
    for z in (15, 26, 34):
        for x in (-20, -6, 8, 22):
            s.quad((x, -22.08, z), (x + 1.6, -22.08, z), (x + 1.6, -22.08, z + 3.2), (x, -22.08, z + 3.2), WINDOW, 2.0)
    # Towers: the great round tower, the Tower of the Hand and three more.
    round_tower(s, (0, 8), 13, 2, 98, RED_STONE, RED_ROOF, 34, window_rows=(24, 40, 56, 72, 88), flare=1.2)
    round_tower(s, (-44, 4), 9, 2, 78, RED_STONE, RED_ROOF, 24, window_rows=(24, 40, 56, 68))
    round_tower(s, (44, -4), 8.5, 2, 70, RED_STONE, RED_ROOF, 22, window_rows=(24, 40, 56))
    round_tower(s, (-18, 40), 7.5, 2, 62, RED_STONE, RED_ROOF, 20, window_rows=(20, 36, 52))
    round_tower(s, (24, 38), 7, 2, 56, RED_STONE, RED_ROOF, 18, window_rows=(20, 34, 46))
    # Great hall and storehouses between the walls.
    s.box((-60, -46, 2), (-22, -30, 16), RED_STONE, 6.0)
    gable_roof(s, (-60, -46), (-22, -30), 16, 9, RED_ROOF, RED_STONE, 1.5, 'x')
    s.box((26, -46, 2), (64, -30, 14), RED_STONE, 6.0)
    gable_roof(s, (26, -46), (64, -30), 14, 8, RED_ROOF, RED_STONE, 1.5, 'x')
    s.box((-64, 28, 2), (-30, 48, 13), RED_STONE, 6.0)
    gable_roof(s, (-64, 28), (-30, 48), 13, 8, RED_ROOF, RED_STONE, 1.5, 'x')
    s.box((34, 26, 2), (66, 46, 13), RED_STONE, 6.0)
    gable_roof(s, (34, 26), (66, 46), 13, 8, RED_ROOF, RED_STONE, 1.5, 'x')
    return s.build(name, MATERIALS)


def build_dragonpit(name):
    s = Structure()
    outer, inner = 50.0, 44.0
    s.lathe([(outer + 3, -8), (outer + 3, 3), (outer, 4), (outer, 30), (outer + 1.5, 30), (outer + 1.5, 33), (inner, 33), (inner, 4)], 48, CREAM, tile=8.0)
    # Dome: a shell with a great opening at the crown.
    dome = []
    for i in range(0, 15):
        t = i / 14
        angle = t * math.radians(66)
        dome.append((inner * math.cos(angle) + 1.5, 33 + inner * 0.78 * math.sin(angle)))
    s.lathe(dome, 48, CREAM, tile=7.0)
    dome_in = [(r - 1.8, z - 1.2) for r, z in dome]
    s.lathe(list(reversed(dome_in)), 48, CREAM, tile=7.0)
    # Ribs and buttress towers.
    for k in range(16):
        a = math.tau * k / 16
        pts = [(math.cos(a) * (r + 1.0), math.sin(a) * (r + 1.0), z + 0.4) for r, z in dome]
        for (x0, y0, z0), (x1, y1, z1) in zip(pts, pts[1:]):
            n = Vector((-math.sin(a), math.cos(a), 0)) * 0.9
            for dz in (0,):
                s.quad(Vector((x0, y0, z0)) - n, Vector((x1, y1, z1)) - n, Vector((x1, y1, z1)) + n, Vector((x0, y0, z0)) + n, CREAM, 3.0)
    for k in range(8):
        a = math.tau * (k + 0.5) / 8
        round_tower(s, (math.cos(a) * (outer + 3.5), math.sin(a) * (outer + 3.5)), 5.5, -2, 44, CREAM, BLUE_ROOF, 14, sides=14, window_rows=(18, 30))
    # Arched entrances at the cardinal points.
    for k in range(4):
        a = math.tau * k / 4 + math.pi / 2
        n = Vector((math.cos(a), math.sin(a), 0))
        side = Vector((-n.y, n.x, 0))
        p = n * (outer + 0.1)
        for grow, mat in ((1.0, WOOD), (0.0, WINDOW)):
            w = 6.0 + grow
            s.quad(p - side * w, p + side * w, p + side * w + Vector((0, 0, 15 + grow)), p - side * w + Vector((0, 0, 15 + grow)), mat, 3.0)
    return s.build(name, MATERIALS)


def build_dragonstone(name):
    s = Structure()
    rng = random.Random(7)
    # Basalt keep: stacked, tapering faceted drums, like cooled lava.
    for (radius, z0, z1, taper) in ((34, -10, 28, 0.9), (26, 28, 58, 0.85), (17, 58, 88, 0.75), (9.5, 88, 118, 0.5)):
        s.prism((0, 0), radius, z0, z1, 8, BLACK, 7.0, rotation=math.radians(22.5), taper=taper)
    # Ring of angular towers of varying height.
    for k in range(10):
        a = math.tau * k / 10 + 0.15
        r = 64 + rng.uniform(-4, 4)
        height = rng.uniform(46, 84)
        radius = rng.uniform(7, 11)
        cx, cy = math.cos(a) * r, math.sin(a) * r
        s.prism((cx, cy), radius, -10, height, 6, BLACK, 6.0, rotation=a, taper=0.82)
        s.prism((cx, cy), radius * 0.82, height, height + 10, 6, BLACK, 6.0, rotation=a, taper=0.35)
        windows(s, (cx, cy), radius * 0.92, 20, 6)
    # Outer wall, an irregular octagon, with a massive gate.
    pts = [(math.cos(math.tau * k / 8 + 0.4) * (92 + 7 * math.sin(k * 2.3)), math.sin(math.tau * k / 8 + 0.4) * (84 + 6 * math.cos(k * 1.7))) for k in range(8)]
    wall_ring(s, pts, -4, 20, 6.0, BLACK, closed=True)
    for x, y in pts:
        s.prism((x, y), 8, -8, 40, 6, BLACK, 6.0, rotation=0.3, taper=0.8)
    gate(s, 0, -88.0, 14, 18)
    # Long stairway of terraces climbing the mountainside behind.
    for i in range(7):
        s.box((-26 - i * 2, 28 + i * 10, -10), (26 + i * 2, 38 + i * 10, 4 + i * 6), BLACK, 6.0)
    return s.build(name, MATERIALS)


def build_harrenhal(name):
    s = Structure()
    rng = random.Random(21)
    # Huge curtain wall and gatehouse, scorched and broken.
    outer = [(-130, -105), (130, -105), (150, 0), (130, 105), (-130, 105), (-150, 0)]
    s.box((-155, -112, -10), (155, 112, 2), CHARCOAL, 10.0)
    wall_ring(s, outer, 2, 26, 8.0, CHARCOAL, closed=True)
    gate(s, 0, -109.0, 18, 22)

    def melt_radius(theta, z, seed):
        h = 1.0
        return 1.0 + 0.1 * math.sin(theta * 4 + seed) * smoothstep(0.4, 1.0, z / 120) + 0.18 * math.sin(theta * 2 + seed * 2) * smoothstep(0.6, 1.0, z / 120)

    # Five colossal towers whose upper halves have slumped like candles.
    layout = [(-92, -60, 21, 118), (92, -64, 20, 108), (-100, 56, 19, 98), (96, 60, 22, 124), (0, 10, 26, 140)]
    for i, (x, y, radius, height) in enumerate(layout):
        profile = []
        steps = 18
        for j in range(steps + 1):
            t = j / steps
            r = radius * (1.18 - 0.18 * t) if t < 0.7 else radius * (1.0 + 0.12 * (t - 0.7) / 0.3)
            profile.append((r, 2 + t * height))
        profile.append((radius * 0.3, 2 + height * 1.03))
        s.lathe(profile, 28, CHARCOAL, (x, y), 8.0,
                noise_fn=lambda th, z, i=i: melt_radius(th, z, i * 1.7),
                z_fn=lambda th, z, i=i, h=height: -0.14 * h * (0.5 + 0.5 * math.sin(th * 3 + i)) * smoothstep(0.72, 1.0, (z - 2) / h))
        windows(s, (x, y), radius, height * 0.3, 8)
        windows(s, (x, y), radius, height * 0.55, 8, phase=0.2)
    # Great halls with collapsed roofs.
    for (x0, y0, x1, y1, h) in ((-58, -88, 58, -60, 24), (-120, -25, -80, 25, 30), (80, -22, 124, 28, 28), (-50, 60, 46, 92, 26)):
        s.box((x0, y0, 2), (x1, y1, 2 + h), CHARCOAL, 8.0)
        merlons(s, [(x0, y0), (x1, y0), (x1, y1), (x0, y1), (x0, y0)], 2 + h, CHARCOAL, size=3.0, gap=3.0, height=3.0, thickness=3.0)
        for k in range(5):
            wx = x0 + (x1 - x0) * (k + 0.5) / 5
            s.quad((wx - 1.2, y0 - 0.08, 10), (wx + 1.2, y0 - 0.08, 10), (wx + 1.2, y0 - 0.08, 18), (wx - 1.2, y0 - 0.08, 18), WINDOW, 2.0)
    return s.build(name, MATERIALS)


def build_high_tide(name):
    s = Structure()
    s.box((-34, -28, -12), (34, 28, 1.5), PALE, 8.0)
    wall_ring(s, [(-30, -24), (30, -24), (30, 24), (-30, 24)], 1.5, 12, 3.5, PALE, closed=True)
    for x, y in ((-30, -24), (30, -24), (30, 24), (-30, 24)):
        round_tower(s, (x, y), 5.5, 1.5, 32, PALE, BLUE_ROOF, 14, window_rows=(14, 22, 28))
    s.box((-12, -10, 1.5), (12, 10, 30), PALE, 6.0)
    merlons(s, [(-12, -10), (12, -10), (12, 10), (-12, 10), (-12, -10)], 30, PALE, size=1.6, gap=1.6, height=1.6, thickness=2.0)
    round_tower(s, (0, 0), 7.5, 1.5, 48, PALE, BLUE_ROOF, 18, window_rows=(20, 30, 40))
    gate(s, 0, -26.0, 7, 10)
    return s.build(name, MATERIALS)


def build_great_sept(name):
    s = Structure()
    s.box((-34, -22, -4), (34, 22, 3), CREAM, 8.0)
    s.box((-30, -18, 3), (30, 18, 24), CREAM, 6.0)
    gable_roof(s, (-30, -18), (30, 18), 24, 12, BLUE_ROOF, CREAM, 1.5, 'x')
    # Seven spired towers, as the building's name suggests.
    for k in range(7):
        a = math.tau * k / 7 + math.pi / 7
        round_tower(s, (math.cos(a) * 32, math.sin(a) * 22), 5.2, 0, 44 + (k % 3) * 8, CREAM, BLUE_ROOF, 18, sides=14, window_rows=(14, 26, 36))
    # Central dome.
    dome = [(14 * math.cos(t * math.radians(80)), 24 + 14 * 1.1 * math.sin(t * math.radians(80))) for t in [i / 10 for i in range(11)]]
    s.lathe(dome, 28, BLUE_ROOF, tile=5.0)
    s.cone((0, 0), 2.5, 24 + 14 * 1.1, 24 + 14 * 1.1 + 12, 10, BLUE_ROOF, 4.0)
    gate(s, 0, -19.0, 8, 12)
    return s.build(name, MATERIALS)


# ------------------------------------------------------------------------------------------------ assemble

def textured(name, textures, rough=0.9, metal=0.0):
    return make_material(name, textures[0], textures[1], roughness=rough, metallic=metal)


MATERIALS = [
    textured('red_stone', stone_textures('red_stone', 1, (0.07, 0.03, 0.025), (0.5, 0.22, 0.15), rows=9)),
    textured('red_roof', tile_textures('red_roof', 2, (0.07, 0.02, 0.02), (0.42, 0.1, 0.07)), 0.75),
    textured('cream_stone', stone_textures('cream_stone', 3, (0.28, 0.25, 0.2), (0.9, 0.84, 0.7), rows=8)),
    textured('black_stone', stone_textures('black_stone', 4, (0.012, 0.012, 0.015), (0.18, 0.18, 0.2), rows=6), 0.8),
    textured('charcoal_stone', stone_textures('charcoal_stone', 5, (0.018, 0.016, 0.016), (0.32, 0.29, 0.26), rows=7, scorch=0.9), 0.95),
    textured('pale_stone', stone_textures('pale_stone', 6, (0.3, 0.3, 0.29), (0.82, 0.8, 0.74), rows=9)),
    textured('blue_roof', tile_textures('blue_roof', 7, (0.03, 0.05, 0.08), (0.18, 0.32, 0.45)), 0.7),
    make_material('window_dark', roughness=0.2, metallic=0.4, base_color=(0.012, 0.014, 0.018)),
    make_material('timber', roughness=0.85, base_color=(0.14, 0.085, 0.045)),
]

report = {}
for builder, name in ((build_red_keep, 'red_keep'), (build_dragonpit, 'dragonpit'), (build_dragonstone, 'dragonstone'),
                      (build_harrenhal, 'harrenhal'), (build_high_tide, 'high_tide'), (build_great_sept, 'great_sept')):
    obj = builder(name)
    dims = obj.dimensions
    report[name] = {'triangles': sum(len(p.vertices) - 2 for p in obj.data.polygons), 'size': [round(dims.x), round(dims.y), round(dims.z)]}

size = export_glb(os.path.abspath(OUTPUT))
print('LANDMARK_REPORT', report, 'bytes', size)
