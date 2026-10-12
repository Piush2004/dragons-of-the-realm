"""
Realistic-ish vegetation kit for Emberbond, generated headlessly in Blender.

Run:  blender --background --python make_vegetation.py -- <out_dir>
Writes FBX models (pine_1..3, oak_1..3, birch_1, bush_1..2, rock_1..4, grass_tuft, fern) and the PNG textures
they use (bark, leaf, needle, grass blade, rock is shaded by the terrain rock material in Unity).
Materials are named "bark", "leaf" or "rock" so the Unity import step can swap in HDRP materials.
"""
import math
import os
import random
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = argv[0] if argv else '.'
os.makedirs(OUT, exist_ok=True)


# ------------------------------------------------------------------ textures
def save_rgba(arr, name, srgb=True):
    h, w = arr.shape[:2]
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


def make_textures():
    s = 512
    # Bark: vertical ridges
    x = np.arange(s)[None, :] / s
    n = spectral(s, 1.6, 3)
    ridge = np.abs(np.sin((x * 22 + n * 2.2) * np.pi)) ** 0.6
    fine = spectral(s, 0.8, 4)
    h = 0.6 * ridge + 0.4 * fine
    c = np.stack([0.20 + 0.16 * h, 0.15 + 0.11 * h, 0.10 + 0.07 * h, np.ones_like(h)], axis=-1)
    save_rgba(c, 'bark_albedo.png')
    gx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 6.0
    gy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 6.0
    ln = np.sqrt(gx * gx + gy * gy + 1)
    nrm = np.stack([-gx / ln * 0.5 + 0.5, gy / ln * 0.5 + 0.5, 1 / ln * 0.5 + 0.5, np.ones_like(h)], axis=-1)
    save_rgba(nrm, 'bark_normal.png', srgb=False)

    # Leaf cluster: many small leaves with alpha
    rng = np.random.default_rng(7)
    atlas = np.zeros((s, s, 4))
    yy, xx = np.mgrid[0:s, 0:s]
    for _ in range(70):
        cx, cy = rng.uniform(20, s - 20, 2)
        ang = rng.uniform(0, math.pi)
        l, wd = rng.uniform(26, 44), rng.uniform(9, 15)
        dx, dy = xx - cx, yy - cy
        u = dx * math.cos(ang) + dy * math.sin(ang)
        v = -dx * math.sin(ang) + dy * math.cos(ang)
        m = (np.abs(u) / l) ** 2 + (np.abs(v) / (wd * (1 - 0.6 * np.abs(u) / l))) ** 2 < 1
        shade = rng.uniform(0.7, 1.15)
        col = np.array([0.13 * shade, 0.27 * shade, 0.07 * shade, 1.0])
        vein = np.abs(v) < 0.8
        atlas[m] = col
        atlas[m & vein] = col * np.array([1.2, 1.15, 1.0, 1.0])
    save_rgba(atlas, 'leaf_albedo.png')

    # Pine needle spray
    nd = np.zeros((s, s, 4))
    for _ in range(420):
        cx = rng.uniform(0.45 * s, 0.55 * s)
        cy = rng.uniform(0, s)
        ang = rng.uniform(-0.9, 0.9) + math.pi / 2
        l = rng.uniform(40, 120)
        t = np.linspace(0, 1, 40)
        px = (s * 0.5 + np.cos(ang) * l * t * (rng.choice([-1, 1]))).astype(int)
        py = (cy + np.sin(ang) * l * t * 0.3).astype(int)
        for a, b in zip(px, py):
            if 0 <= a < s and 0 <= b < s:
                nd[max(0, b - 1):b + 2, max(0, a - 1):a + 1] = (0.07 + 0.05 * rng.random(), 0.20 + 0.06 * rng.random(), 0.08, 1.0)
    save_rgba(nd, 'needle_albedo.png')

    # Grass blade card
    g = np.zeros((256, 256, 4))
    for _ in range(26):
        bx = rng.uniform(30, 226)
        top = rng.uniform(60, 245)
        lean = rng.uniform(-40, 40)
        for yv in range(0, int(top)):
            t = yv / top
            xp = int(bx + lean * t * t)
            wv = max(1, int(5 * (1 - t)))
            if 0 <= xp < 256:
                g[yv, max(0, xp - wv):xp + wv] = (0.16 + 0.12 * t, 0.30 + 0.16 * t, 0.07, 1.0)
    save_rgba(g[::-1], 'grass_albedo.png')
    print('textures done')


# ------------------------------------------------------------------ mesh helpers
def new_obj(name):
    me = bpy.data.meshes.new(name)
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob, me


def material(name, image=None, alpha=False):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get('Principled BSDF')
    if image:
        tex = m.node_tree.nodes.new('ShaderNodeTexImage')
        tex.image = bpy.data.images.load(os.path.join(OUT, image), check_existing=True)
        m.node_tree.links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
        if alpha:
            m.node_tree.links.new(tex.outputs['Alpha'], bsdf.inputs['Alpha'])
    return m


def add_tube(bm, p0, p1, r0, r1, sides=8, uv_layer=None):
    d = (p1 - p0)
    length = d.length
    if length < 1e-5:
        return
    z = d.normalized()
    ref = Vector((0, 0, 1)) if abs(z.z) < 0.99 else Vector((1, 0, 0))
    x = z.cross(ref).normalized()
    y = z.cross(x)
    rings = []
    for pos, r in ((p0, r0), (p1, r1)):
        ring = []
        for i in range(sides):
            a = 2 * math.pi * i / sides
            ring.append(bm.verts.new(pos + x * math.cos(a) * r + y * math.sin(a) * r))
        rings.append(ring)
    uv = bm.loops.layers.uv.verify() if uv_layer is None else uv_layer
    for i in range(sides):
        j = (i + 1) % sides
        f = bm.faces.new((rings[0][i], rings[0][j], rings[1][j], rings[1][i]))
        f.material_index = 0
        u0, u1 = i / sides, (i + 1) / sides
        vv = length / 2.0
        f.loops[0][uv].uv = (u0, 0)
        f.loops[1][uv].uv = (u1, 0)
        f.loops[2][uv].uv = (u1, vv)
        f.loops[3][uv].uv = (u0, vv)


def add_card(bm, center, normal_dir, width, height, tilt_dir, uv_off=(0, 0), uv_scale=0.5):
    """A quad centred at `center`, lying roughly in the plane facing `normal_dir`, drooping along `tilt_dir`."""
    n = normal_dir.normalized()
    t = tilt_dir.normalized()
    side = n.cross(t)
    if side.length < 1e-4:
        side = Vector((1, 0, 0))
    side.normalize()
    up = side.cross(n).normalized()
    c = center
    v = [bm.verts.new(c - side * width * 0.5 - up * height * 0.5),
         bm.verts.new(c + side * width * 0.5 - up * height * 0.5),
         bm.verts.new(c + side * width * 0.5 + up * height * 0.5),
         bm.verts.new(c - side * width * 0.5 + up * height * 0.5)]
    f = bm.faces.new(v)
    f.material_index = 1
    uv = bm.loops.layers.uv.verify()
    ox, oy = uv_off
    f.loops[0][uv].uv = (ox, oy)
    f.loops[1][uv].uv = (ox + uv_scale, oy)
    f.loops[2][uv].uv = (ox + uv_scale, oy + uv_scale)
    f.loops[3][uv].uv = (ox, oy + uv_scale)


def finish(ob, me, bm, mats):
    bm.to_mesh(me)
    bm.free()
    for m in mats:
        ob.data.materials.append(m)
    me.update()
    for p in me.polygons:
        p.use_smooth = True


def export(ob, name):
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, name + '.fbx'), use_selection=True,
                             axis_forward='-Z', axis_up='Y', apply_unit_scale=True, bake_space_transform=True,
                             path_mode='STRIP', embed_textures=False, mesh_smooth_type='FACE')
    bpy.data.objects.remove(ob, do_unlink=True)


# ------------------------------------------------------------------ models
def pine(seed, bark, needle):
    rnd = random.Random(seed)
    ob, me = new_obj('pine_%d' % seed)
    bm = bmesh.new()
    height = rnd.uniform(15, 22)
    top = Vector((0, 0, height))
    add_tube(bm, Vector((0, 0, 0)), top, 0.42, 0.03, sides=9)
    tiers = rnd.randint(11, 15)
    for t in range(tiers):
        h = height * (0.22 + 0.76 * t / tiers)
        frac = 1.0 - (h / height)
        radius = 0.8 + 4.2 * frac ** 0.8
        n = 9 + int(5 * frac)
        for i in range(n):
            a = 2 * math.pi * i / n + rnd.uniform(-0.2, 0.2) + t * 0.7
            dirv = Vector((math.cos(a), math.sin(a), 0))
            tip = dirv * radius
            c = Vector((0, 0, h)) + dirv * radius * 0.5 + Vector((0, 0, -0.5 * (1 - frac) - 0.3))
            nrm = Vector((0, 0, 1)) + dirv * 0.35
            add_card(bm, c, nrm, radius * 0.85, radius * 1.15, dirv, uv_off=(0, 0), uv_scale=1.0)
            if rnd.random() < 0.5:
                add_card(bm, c + Vector((0, 0, 0.25)), nrm, radius * 0.8, radius * 1.0, dirv, uv_scale=1.0)
    finish(ob, me, bm, [bark, needle])
    return ob


def oak(seed, bark, leaf):
    rnd = random.Random(seed + 100)
    ob, me = new_obj('oak_%d' % seed)
    bm = bmesh.new()
    trunk_h = rnd.uniform(3.2, 4.6)
    lean = Vector((rnd.uniform(-0.25, 0.25), rnd.uniform(-0.25, 0.25), 0))
    top = Vector((0, 0, trunk_h)) + lean
    add_tube(bm, Vector((0, 0, 0)), top, 0.5, 0.3, sides=10)
    branches = rnd.randint(5, 8)
    clumps = []
    for i in range(branches):
        a = 2 * math.pi * i / branches + rnd.uniform(-0.4, 0.4)
        up = rnd.uniform(0.7, 1.4)
        length = rnd.uniform(2.6, 4.5)
        start = top - Vector((0, 0, rnd.uniform(0.2, 1.1)))
        end = start + Vector((math.cos(a) * length, math.sin(a) * length, up * length * 0.7))
        add_tube(bm, start, end, 0.22, 0.06, sides=6)
        clumps.append(end)
        # twigs
        for k in range(2):
            a2 = a + rnd.uniform(-0.8, 0.8)
            e2 = end + Vector((math.cos(a2), math.sin(a2), rnd.uniform(0.2, 0.9))) * rnd.uniform(1.0, 1.8)
            add_tube(bm, end, e2, 0.07, 0.02, sides=5)
            clumps.append(e2)
    clumps.append(top + Vector((0, 0, 1.8)))
    for c in clumps:
        for _ in range(16):
            d = Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-0.3, 1))).normalized()
            p = c + d * rnd.uniform(0.2, 1.7)
            nrm = Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(0.2, 1.0)))
            add_card(bm, p, nrm, 1.5, 1.5, d, uv_off=(rnd.choice([0, 0.5]), rnd.choice([0, 0.5])), uv_scale=0.5)
    finish(ob, me, bm, [bark, leaf])
    return ob


def bush(seed, bark, leaf):
    rnd = random.Random(seed + 300)
    ob, me = new_obj('bush_%d' % seed)
    bm = bmesh.new()
    for _ in range(46):
        d = Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(0.0, 0.9))).normalized()
        p = d * rnd.uniform(0.2, 0.95) + Vector((0, 0, 0.5))
        add_card(bm, p, Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(0.2, 1))), 0.9, 0.9, d,
                 uv_off=(rnd.choice([0, 0.5]), rnd.choice([0, 0.5])), uv_scale=0.5)
    finish(ob, me, bm, [bark, leaf])
    return ob


def rock(seed, rock_mat):
    rnd = random.Random(seed + 500)
    ob, me = new_obj('rock_%d' % seed)
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=4, radius=1.0)
    sx, sy, sz = rnd.uniform(0.9, 1.7), rnd.uniform(0.8, 1.4), rnd.uniform(0.5, 0.9)
    freq = rnd.uniform(1.1, 2.0)
    for v in bm.verts:
        p = v.co.copy()
        n = (math.sin(p.x * freq * 3 + seed) * math.cos(p.y * freq * 3.3) + math.sin(p.z * freq * 4.1 + seed * 2))
        n2 = math.sin(p.x * 9 + p.y * 7 + seed) * 0.5
        r = 1.0 + 0.16 * n + 0.04 * n2
        v.co = Vector((p.x * r * sx, p.y * r * sy, max(p.z * r * sz, -0.25)))
    uv = bm.loops.layers.uv.verify()
    for f in bm.faces:
        nrm = f.normal
        axis = max(range(3), key=lambda i: abs(nrm[i]))
        for l in f.loops:
            c = l.vert.co
            l[uv].uv = [(c.y, c.z), (c.x, c.z), (c.x, c.y)][axis]
    finish(ob, me, bm, [rock_mat])
    return ob


def grass_tuft(grass):
    ob, me = new_obj('grass_tuft')
    bm = bmesh.new()
    uv = bm.loops.layers.uv.verify()
    for k in range(3):
        a = math.pi * k / 3
        dx, dy = math.cos(a) * 0.5, math.sin(a) * 0.5
        v = [bm.verts.new((-dx, -dy, 0)), bm.verts.new((dx, dy, 0)), bm.verts.new((dx, dy, 0.9)), bm.verts.new((-dx, -dy, 0.9))]
        f = bm.faces.new(v)
        f.material_index = 0
        for l, c in zip(f.loops, [(0, 0), (1, 0), (1, 1), (0, 1)]):
            l[uv].uv = c
    finish(ob, me, bm, [grass])
    return ob


def main():
    make_textures()
    bark = material('bark', 'bark_albedo.png')
    leaf = material('leaf', 'leaf_albedo.png', alpha=True)
    needle = material('needle', 'needle_albedo.png', alpha=True)
    grass = material('grass_blade', 'grass_albedo.png', alpha=True)
    rock_mat = material('rock')
    for s in (1, 2, 3):
        export(pine(s, bark, needle), 'pine_%d' % s)
        export(oak(s, bark, leaf), 'oak_%d' % s)
    for s in (1, 2):
        export(bush(s, bark, leaf), 'bush_%d' % s)
    for s in (1, 2, 3, 4):
        export(rock(s, rock_mat), 'rock_%d' % s)
    export(grass_tuft(grass), 'grass_tuft')
    print('vegetation done')


main()
