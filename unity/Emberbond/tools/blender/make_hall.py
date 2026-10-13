"""
Throne hall for Emberbond: a long stone hall with pillars, a dais and a throne made of hundreds of swords.

Run:  blender --background --python make_hall.py -- <out_dir>
Writes throne_hall.fbx. Materials: stone, timber, banner, blade, gold, dark. UVs are in metres (reuse the city's
tiling stone and timber textures in Unity). Z is up in Blender; the throne stands at +Y facing -Y.
"""
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = argv[0] if argv else '.'
os.makedirs(OUT, exist_ok=True)
MATS = ['stone', 'timber', 'banner', 'blade', 'gold', 'dark']


class B:
    def __init__(self):
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

    def box(self, x0, y0, z0, x1, y1, z1, mat, inward=False):
        quads = [
            [(x0, y0, z0), (x0, y1, z0), (x1, y1, z0), (x1, y0, z0)],        # bottom (normal -z)
            [(x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)],        # top
            [(x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1)],        # -y
            [(x1, y1, z0), (x0, y1, z0), (x0, y1, z1), (x1, y1, z1)],        # +y
            [(x0, y1, z0), (x0, y0, z0), (x0, y0, z1), (x0, y1, z1)],        # -x
            [(x1, y0, z0), (x1, y1, z0), (x1, y1, z1), (x1, y0, z1)],        # +x
        ]
        for q in quads:
            self.face(list(reversed(q)) if inward else q, mat)

    def rbox(self, center, size, euler, mat):
        """A box rotated about its centre."""
        sx, sy, sz = size[0] / 2, size[1] / 2, size[2] / 2
        rot = Euler(euler, 'XYZ').to_matrix()
        c = Vector(center)
        pts = {}
        for ix in (-1, 1):
            for iy in (-1, 1):
                for iz in (-1, 1):
                    pts[(ix, iy, iz)] = c + rot @ Vector((ix * sx, iy * sy, iz * sz))
        def q(a, b, cc, d):
            self.face([pts[a], pts[b], pts[cc], pts[d]], mat)
        q((-1, -1, -1), (-1, 1, -1), (1, 1, -1), (1, -1, -1))
        q((-1, -1, 1), (1, -1, 1), (1, 1, 1), (-1, 1, 1))
        q((-1, -1, -1), (1, -1, -1), (1, -1, 1), (-1, -1, 1))
        q((1, 1, -1), (-1, 1, -1), (-1, 1, 1), (1, 1, 1))
        q((-1, 1, -1), (-1, -1, -1), (-1, -1, 1), (-1, 1, 1))
        q((1, -1, -1), (1, 1, -1), (1, 1, 1), (1, -1, 1))

    def cylinder(self, cx, cy, z0, z1, r, mat, sides=16):
        pts = [(cx + math.cos(2 * math.pi * i / sides) * r, cy + math.sin(2 * math.pi * i / sides) * r) for i in range(sides)]
        for i in range(sides):
            a, b = pts[i], pts[(i + 1) % sides]
            self.face([(a[0], a[1], z0), (b[0], b[1], z0), (b[0], b[1], z1), (a[0], a[1], z1)], mat)

    def finish(self, name):
        me = bpy.data.meshes.new(name)
        self.bm.to_mesh(me)
        self.bm.free()
        ob = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(ob)
        for n in MATS:
            m = bpy.data.materials.new(n)
            m.use_nodes = True
            ob.data.materials.append(m)
        return ob


def main():
    r = random.Random(11)
    b = B()
    W, L, H = 13.0, 31.0, 15.0
    # Shell: floor, walls and ceiling, seen from inside.
    b.box(-W, -L, -1.0, W, L, 0.0, 'stone')
    b.box(-W - 1, -L - 1, 0.0, -W, L + 1, H, 'stone', inward=False)
    b.box(W, -L - 1, 0.0, W + 1, L + 1, H, 'stone')
    b.box(-W - 1, L, 0.0, W + 1, L + 1, H, 'stone')
    b.box(-W - 1, -L - 1, 0.0, W + 1, -L, H, 'stone')
    b.box(-W - 1, -L - 1, H, W + 1, L + 1, H + 1.0, 'timber')
    # Inner faces for the shell (so it reads from inside): thin inset slabs facing in.
    b.box(-W, -L, 0.0, W, L, H, 'stone', inward=True)
    # Pillars with capitals and braziers.
    for y in range(-24, 25, 8):
        for x in (-8.0, 8.0):
            b.cylinder(x, y, 0.0, H - 1.0, 1.0, 'stone', sides=18)
            b.box(x - 1.5, y - 1.5, H - 1.5, x + 1.5, y + 1.5, H, 'stone')
            b.box(x - 1.3, y - 1.3, 0.0, x + 1.3, y + 1.3, 0.6, 'stone')
            b.box(x - 0.35, y - 0.35, 3.0, x + 0.35, y + 0.35, 3.6, 'gold')
    # Dais steps toward the throne.
    for i in range(4):
        b.box(-9.0, 15.0 + i * 3.6, 0.0, 9.0, 18.6 + i * 3.6, 0.35 * (i + 1), 'stone')
    # Central carpet strip.
    b.box(-2.2, -30.0, 0.01, 2.2, 15.0, 0.05, 'banner')
    # The throne of swords: a seat, arms and a fan of blades.
    ty = 27.5
    top = 0.35 * 4
    b.box(-2.2, ty - 2.2, top, 2.2, ty + 2.2, top + 1.6, 'blade')
    for sx in (-1, 1):
        for k in range(14):
            ang = r.uniform(-0.4, 0.4)
            b.rbox((sx * 2.4 + r.uniform(-0.3, 0.3), ty - 1.8 + r.uniform(-0.5, 0.5), top + 1.8 + k * 0.1), (0.14, 0.05, r.uniform(2.0, 3.2)),
                   (r.uniform(-0.4, 0.4), 0.0, sx * r.uniform(0.2, 0.9)), 'blade')
    for k in range(240):
        t = r.random()
        a = (t - 0.5) * 2.4                              # fan angle
        length = r.uniform(5.0, 11.5) * (1.0 - 0.35 * abs(t - 0.5) * 2)
        base = Vector((math.sin(a) * 2.0, ty + 1.8 + r.uniform(-0.5, 0.5), top + 1.4))
        tilt = Euler((r.uniform(-0.15, 0.15), 0.0, -a * 0.9 + r.uniform(-0.1, 0.1)), 'XYZ')
        c = base + tilt.to_matrix() @ Vector((0, 0, length / 2))
        b.rbox((c.x, c.y + r.uniform(-0.8, 0.8), c.z), (r.uniform(0.18, 0.4), 0.07, length), (tilt.x, 0.0, tilt.z), 'blade')
    # Banners on the long walls and behind the throne.
    for y in range(-20, 21, 10):
        for x in (-W + 0.1, W - 0.1):
            b.box(x - 0.05, y - 1.6, 5.0, x + 0.05, y + 1.6, 13.0, 'banner')
            b.box(x - 0.07, y - 0.3, 5.0, x + 0.07, y + 0.3, 13.0, 'gold')
    b.box(-6.0, L - 0.2, 4.0, 6.0, L - 0.1, 13.5, 'banner')
    # Tall windows (dark insets) letting the eye find depth.
    for y in range(-24, 25, 12):
        for x in (-W + 0.02, W - 0.02):
            b.box(x - 0.1, y - 1.2, 6.5, x + 0.1, y + 1.2, 12.0, 'dark')
    ob = b.finish('throne_hall')
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, 'throne_hall.fbx'), use_selection=True, axis_forward='-Z', axis_up='Y',
                             apply_unit_scale=True, bake_space_transform=True, path_mode='STRIP', mesh_smooth_type='FACE')
    print('hall done')


main()
