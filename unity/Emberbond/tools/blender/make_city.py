"""
Hearthmoor city kit for Emberbond, generated headlessly in Blender.

Run:  blender --background --python make_city.py -- <out_dir>
Writes FBX modules (house_a/b/c, tower_round, wall_segment, gate, keep, pier, ship, market_stall, ruin_wall) and the
tiling PBR textures they use (plaster, roof_tile, stone_brick, timber). Material names are "plaster", "roof_tile",
"stone", "timber" and "dark" so the Unity step can bind HDRP materials. UVs are in metres (1 UV = 1 m).
"""
import math
import os
import random
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = argv[0] if argv else '.'
os.makedirs(OUT, exist_ok=True)

MATS = ['plaster', 'roof_tile', 'stone', 'timber', 'dark']


# ------------------------------------------------------------------ textures
def save(arr, name, srgb):
    h, w = arr.shape[:2]
    if arr.shape[2] == 3:
        arr = np.concatenate([arr, np.ones((h, w, 1))], axis=-1)
    img = bpy.data.images.new(name, w, h, alpha=True)
    img.colorspace_settings.name = 'sRGB' if srgb else 'Non-Color'
    img.pixels.foreach_set(np.clip(arr, 0, 1).astype(np.float32).ravel())
    img.filepath_raw = os.path.join(OUT, name)
    img.file_format = 'PNG'
    img.save()
    bpy.data.images.remove(img)


def spectral(size, beta, seed):
    rng = np.random.default_rng(seed)
    w = rng.standard_normal((size, size))
    f = np.fft.fftfreq(size)
    fx, fy = np.meshgrid(f, f)
    r = np.sqrt(fx * fx + fy * fy)
    r[0, 0] = 1
    n = np.real(np.fft.ifft2(np.fft.fft2(w) / r ** beta))
    n -= n.min()
    return n / max(n.max(), 1e-9)


def normal_map(h, strength):
    gx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5 * strength * h.shape[0] / 256.0
    gy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 0.5 * strength * h.shape[0] / 256.0
    ln = np.sqrt(gx * gx + gy * gy + 1)
    return np.stack([-gx / ln * 0.5 + 0.5, gy / ln * 0.5 + 0.5, 1 / ln * 0.5 + 0.5], axis=-1)


def make_textures():
    s = 1024
    n1 = spectral(s, 1.4, 11)
    n2 = spectral(s, 0.7, 12)
    yy, xx = np.mgrid[0:s, 0:s]

    # plaster: warm off-white with stains
    h = 0.5 * n1 + 0.5 * n2
    col = np.stack([0.80 + 0.08 * n1, 0.74 + 0.08 * n1, 0.62 + 0.07 * n1], axis=-1) * (0.88 + 0.2 * n2[..., None])
    save(col, 'plaster_albedo.png', True)
    save(normal_map(h, 0.8), 'plaster_normal.png', False)

    # roof tiles: rows of overlapping scallops, terracotta
    rows, cols = 16, 12
    ty = (yy / s * rows)
    tx = (xx / s * cols + (np.floor(ty) % 2) * 0.5)
    fy = ty - np.floor(ty)
    fx = tx - np.floor(tx)
    scallop = np.clip(1 - np.abs(fx - 0.5) * 2, 0, 1)
    tile_h = fy * 0.7 + scallop * 0.2
    shade = spectral(s, 0.5, 13)
    base = np.stack([0.50 + 0.18 * shade, 0.20 + 0.08 * shade, 0.12 + 0.05 * shade], axis=-1)
    gap = (fy < 0.06)[..., None]
    col = np.where(gap, base * 0.4, base * (0.75 + 0.5 * tile_h[..., None]))
    save(col, 'roof_tile_albedo.png', True)
    save(normal_map(tile_h + 0.1 * shade, 2.4), 'roof_tile_normal.png', False)

    # stone brick: coursed ashlar blocks with mortar
    bh, bw = 10, 6
    by = yy / s * bh
    bx = xx / s * bw + (np.floor(by) % 2) * 0.5
    ly = by - np.floor(by)
    lx = bx - np.floor(bx)
    mortar = ((ly < 0.07) | (lx < 0.04))[..., None]
    block_id = (np.floor(by) * 17 + np.floor(bx) * 31) % 7 / 7.0
    tone = 0.42 + 0.16 * block_id + 0.1 * n1
    col = np.stack([tone, tone * 0.97, tone * 0.9], axis=-1)
    col = np.where(mortar, np.array([0.28, 0.27, 0.25]), col)
    hh = np.where(mortar[..., 0], 0.0, 0.6 + 0.4 * n2)
    save(col, 'stone_brick_albedo.png', True)
    save(normal_map(hh, 3.0), 'stone_brick_normal.png', False)

    # timber planks
    pl = 8
    px = xx / s * pl
    pfx = px - np.floor(px)
    grain = np.sin((yy / s * 40 + n1 * 6 + np.floor(px) * 3) * np.pi) * 0.5 + 0.5
    seam = (pfx < 0.04)[..., None]
    t = 0.24 + 0.12 * grain + 0.06 * n2
    col = np.stack([t * 1.1, t * 0.78, t * 0.5], axis=-1)
    col = np.where(seam, col * 0.4, col)
    save(col, 'timber_albedo.png', True)
    save(normal_map(0.6 * grain + 0.4 * n2, 1.4), 'timber_normal.png', False)
    print('city textures done')


# ------------------------------------------------------------------ geometry helpers
class Builder:
    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.verify()

    def face(self, pts, mat):
        vs = [self.bm.verts.new(p) for p in pts]
        try:
            f = self.bm.faces.new(vs)
        except ValueError:
            return
        f.material_index = MATS.index(mat)
        n = f.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        for l in f.loops:
            c = l.vert.co
            l[self.uv].uv = [(c.y, c.z), (c.x, c.z), (c.x, c.y)][ax]

    def box(self, x0, y0, z0, x1, y1, z1, mat, top=None):
        top = top or mat
        # Counter-clockwise seen from outside, so the normals point out and back-face culling keeps the walls.
        self.face(list(reversed([(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0)])), mat)          # bottom
        self.face(list(reversed([(x0, y0, z1), (x0, y1, z1), (x1, y1, z1), (x1, y0, z1)])), top)          # top
        self.face(list(reversed([(x0, y0, z0), (x0, y0, z1), (x1, y0, z1), (x1, y0, z0)])), mat)          # front (-y)
        self.face(list(reversed([(x1, y1, z0), (x1, y1, z1), (x0, y1, z1), (x0, y1, z0)])), mat)          # back (+y)
        self.face(list(reversed([(x0, y1, z0), (x0, y1, z1), (x0, y0, z1), (x0, y0, z0)])), mat)          # left
        self.face(list(reversed([(x1, y0, z0), (x1, y0, z1), (x1, y1, z1), (x1, y1, z0)])), mat)          # right

    def gable(self, x0, y0, x1, y1, z, rise, over, wall_mat, roof_mat):
        """Gable roof along the x axis with the ridge at the middle of y."""
        ym = (y0 + y1) / 2
        zt = z + rise
        # roof planes with overhang
        self.face([(x0 - over, y0 - over, z - 0.1), (x1 + over, y0 - over, z - 0.1), (x1 + over, ym, zt), (x0 - over, ym, zt)], roof_mat)
        self.face([(x0 - over, y1 + over, z - 0.1), (x0 - over, ym, zt), (x1 + over, ym, zt), (x1 + over, y1 + over, z - 0.1)], roof_mat)
        # gable ends
        self.face([(x0, y0, z), (x0, ym, zt), (x0, y1, z)], wall_mat)
        self.face([(x1, y1, z), (x1, ym, zt), (x1, y0, z)], wall_mat)
        # underside
        self.face([(x0, y0, z), (x0, y1, z), (x1, y1, z), (x1, y0, z)], 'timber')

    def cylinder(self, cx, cy, z0, z1, r, mat, sides=16, cap=None):
        pts0 = [(cx + math.cos(2 * math.pi * i / sides) * r, cy + math.sin(2 * math.pi * i / sides) * r) for i in range(sides)]
        for i in range(sides):
            a, b = pts0[i], pts0[(i + 1) % sides]
            self.face([(a[0], a[1], z0), (b[0], b[1], z0), (b[0], b[1], z1), (a[0], a[1], z1)], mat)
        if cap:
            self.face([(p[0], p[1], z1) for p in pts0], cap)

    def cone(self, cx, cy, z0, z1, r, mat, sides=16):
        pts = [(cx + math.cos(2 * math.pi * i / sides) * r, cy + math.sin(2 * math.pi * i / sides) * r) for i in range(sides)]
        for i in range(sides):
            a, b = pts[i], pts[(i + 1) % sides]
            self.face([(a[0], a[1], z0), (b[0], b[1], z0), (cx, cy, z1)], mat)

    def crenellations(self, x0, y0, x1, y1, z, h=0.9, w=0.9, mat='stone'):
        x = x0
        i = 0
        while x + w <= x1:
            if i % 2 == 0:
                self.box(x, y0, z, x + w, y0 + 0.6, z + h, mat)
                self.box(x, y1 - 0.6, z, x + w, y1, z + h, mat)
            x += w
            i += 1

    def finish(self):
        me = bpy.data.meshes.new(self.name)
        self.bm.to_mesh(me)
        self.bm.free()
        ob = bpy.data.objects.new(self.name, me)
        bpy.context.scene.collection.objects.link(ob)
        for m in materials():
            ob.data.materials.append(m)
        for p in me.polygons:
            p.use_smooth = False
        return ob


_mats = {}


def materials():
    if not _mats:
        for n in MATS:
            m = bpy.data.materials.new(n)
            m.use_nodes = True
            _mats[n] = m
    return [_mats[n] for n in MATS]


def export(ob):
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, ob.name + '.fbx'), use_selection=True, axis_forward='-Z',
                             axis_up='Y', apply_unit_scale=True, bake_space_transform=True, path_mode='STRIP',
                             embed_textures=False, mesh_smooth_type='FACE')
    bpy.data.objects.remove(ob, do_unlink=True)


# ------------------------------------------------------------------ modules (Z is up in Blender)
def house(name, seed):
    r = random.Random(seed)
    b = Builder(name)
    w, d = r.choice([6.0, 7.0, 8.0]), r.choice([5.0, 6.0, 7.0])
    floors = r.choice([2, 2, 3])
    fh = 2.8
    b.box(-w / 2 - 0.2, -d / 2 - 0.2, -1.0, w / 2 + 0.2, d / 2 + 0.2, 0.0, 'stone')               # plinth below ground line
    b.box(-w / 2, -d / 2, 0.0, w / 2, d / 2, fh, 'stone')                                              # stone ground floor
    for f in range(1, floors):
        j = 0.35 if f == 1 else 0.0                                                                    # jetty
        b.box(-w / 2 - j, -d / 2 - j, fh * f, w / 2 + j, d / 2 + j, fh * (f + 1), 'plaster')
        for k in range(5):                                                                             # timber frame
            x = -w / 2 - j + (w + 2 * j) * k / 4
            b.box(x - 0.08, -d / 2 - j - 0.05, fh * f, x + 0.08, -d / 2 - j + 0.05, fh * (f + 1), 'timber')
            b.box(x - 0.08, d / 2 + j - 0.05, fh * f, x + 0.08, d / 2 + j + 0.05, fh * (f + 1), 'timber')
        b.box(-w / 2 - j, -d / 2 - j - 0.05, fh * (f + 1) - 0.1, w / 2 + j, -d / 2 - j + 0.05, fh * (f + 1), 'timber')
    top = fh * floors
    b.gable(-w / 2, -d / 2, w / 2, d / 2, top, rise=d * 0.45, over=0.45, wall_mat='plaster', roof_mat='roof_tile')
    # chimney
    cx = r.choice([-1, 1]) * w * 0.28
    b.box(cx - 0.35, -0.35, top, cx + 0.35, 0.35, top + d * 0.45 + 1.0, 'stone')
    # door and windows (dark insets on the front, -y)
    b.box(-0.55, -d / 2 - 0.08, 0.0, 0.55, -d / 2 + 0.02, 2.1, 'dark')
    b.box(-0.7, -d / 2 - 0.12, 2.1, 0.7, -d / 2 + 0.0, 2.25, 'timber')
    for f in range(floors):
        for x in (-w * 0.3, w * 0.3):
            z = fh * f + 1.1
            jj = 0.35 if f == 1 else 0.0
            b.box(x - 0.45, -d / 2 - jj - 0.1, z, x + 0.45, -d / 2 - jj + 0.02, z + 1.0, 'dark')
    return b.finish()


def tower_round(name='tower_round'):
    b = Builder(name)
    b.cylinder(0, 0, -2.0, 14.0, 3.4, 'stone', sides=20)
    b.cylinder(0, 0, 14.0, 14.6, 3.9, 'stone', sides=20, cap='stone')
    for i in range(10):
        a = 2 * math.pi * i / 10
        x, y = math.cos(a) * 3.7, math.sin(a) * 3.7
        b.box(x - 0.45, y - 0.45, 14.6, x + 0.45, y + 0.45, 15.5, 'stone')
    b.cone(0, 0, 14.6, 21.0, 3.0, 'roof_tile', sides=20)
    for z in (5.0, 9.0):
        b.box(-0.3, -3.5, z, 0.3, -3.3, z + 1.4, 'dark')
    return b.finish()


def wall_segment(name='wall_segment'):
    b = Builder(name)
    b.box(-5.0, -1.4, -2.0, 5.0, 1.4, 8.0, 'stone')
    b.crenellations(-5.0, -1.4, 5.0, 1.4, 8.0)
    return b.finish()


def gate(name='gate'):
    b = Builder(name)
    b.box(-8.0, -2.4, -2.0, -3.0, 2.4, 12.0, 'stone')
    b.box(3.0, -2.4, -2.0, 8.0, 2.4, 12.0, 'stone')
    b.box(-3.0, -2.4, 6.0, 3.0, 2.4, 12.0, 'stone')                    # lintel with the passage beneath
    b.crenellations(-8.0, -2.4, 8.0, 2.4, 12.0)
    b.box(-3.0, -0.2, 0.0, -2.6, 0.2, 6.0, 'timber')
    b.box(2.6, -0.2, 0.0, 3.0, 0.2, 6.0, 'timber')
    return b.finish()


def keep(name='keep'):
    b = Builder(name)
    b.box(-14.0, -11.0, -3.0, 14.0, 11.0, 24.0, 'stone')
    b.crenellations(-14.0, -11.0, 14.0, 11.0, 24.0)
    b.box(-8.0, -6.0, 24.0, 8.0, 6.0, 34.0, 'stone')
    b.gable(-8.0, -6.0, 8.0, 6.0, 34.0, rise=6.0, over=0.8, wall_mat='stone', roof_mat='roof_tile')
    for sx in (-1, 1):
        for sy in (-1, 1):
            b.cylinder(sx * 14.0, sy * 11.0, -3.0, 30.0, 4.2, 'stone', sides=20)
            b.cone(sx * 14.0, sy * 11.0, 30.0, 40.0, 4.8, 'roof_tile', sides=20)
    for z in (6.0, 13.0, 20.0):
        for x in (-8.0, 0.0, 8.0):
            b.box(x - 0.6, -11.1, z, x + 0.6, -10.9, z + 2.2, 'dark')
    b.box(-1.8, -11.1, 0.0, 1.8, -10.8, 5.0, 'dark')
    return b.finish()


def pier(name='pier'):
    b = Builder(name)
    b.box(-1.8, 0.0, -0.4, 1.8, 28.0, 0.0, 'timber')
    for i in range(0, 28, 4):
        for sx in (-1.6, 1.6):
            b.box(sx - 0.15, i - 0.15, -4.0, sx + 0.15, i + 0.15, 0.0, 'timber')
    return b.finish()


def ship(name='ship'):
    b = Builder(name)
    L, W = 18.0, 5.0
    steps = 8
    for i in range(steps):
        y0 = -L / 2 + L * i / steps
        y1 = -L / 2 + L * (i + 1) / steps
        t0 = abs(y0) / (L / 2)
        t1 = abs(y1) / (L / 2)
        w0, w1 = W * (1 - 0.7 * t0 ** 2) / 2, W * (1 - 0.7 * t1 ** 2) / 2
        b.face([(-w0, y0, 1.6), (-w1, y1, 1.6), (-w1 * 0.6, y1, 0.0), (-w0 * 0.6, y0, 0.0)], 'timber')
        b.face([(w0, y0, 1.6), (w0 * 0.6, y0, 0.0), (w1 * 0.6, y1, 0.0), (w1, y1, 1.6)], 'timber')
        b.face([(-w0, y0, 1.6), (w0, y0, 1.6), (w1, y1, 1.6), (-w1, y1, 1.6)], 'timber')
    b.box(-0.2, -1.0, 1.6, 0.2, -0.6, 14.0, 'timber')                 # mast
    b.box(-4.0, -1.0, 10.0, 4.0, -0.9, 13.5, 'plaster')               # sail
    return b.finish()


def stall(name='market_stall'):
    b = Builder(name)
    for sx in (-1.4, 1.4):
        for sy in (-1.0, 1.0):
            b.box(sx - 0.07, sy - 0.07, 0.0, sx + 0.07, sy + 0.07, 2.4, 'timber')
    b.box(-1.7, -1.3, 2.4, 1.7, 1.3, 2.55, 'plaster')
    b.box(-1.4, -0.9, 0.0, 1.4, -0.3, 0.9, 'timber')
    return b.finish()


def ruin_wall(name='ruin_wall'):
    b = Builder(name)
    r = random.Random(5)
    x = -6.0
    while x < 6.0:
        h = r.uniform(1.5, 7.5)
        b.box(x, -0.7, -1.0, x + 1.5, 0.7, h, 'stone')
        x += 1.5
    return b.finish()


def main():
    make_textures()
    for i, s in enumerate((1, 2, 3, 4)):
        export(house('house_%s' % 'abcd'[i], s))
    export(tower_round())
    export(wall_segment())
    export(gate())
    export(keep())
    export(pier())
    export(ship())
    export(stall())
    export(ruin_wall())
    print('city done')


main()
