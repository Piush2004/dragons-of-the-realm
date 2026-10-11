"""Blocky creature and character models, built and exported headless in Blender:

    blender --background --python make_models.py -- <Resources/BlockRealm dir>

Each model is a hierarchy of box parts whose object origins are the joints (neck, shoulder, hip), so the game can
swing limbs by rotating the part transforms. Creatures face -Y in Blender, which imports facing +Z in Unity.
Skin textures are painted procedurally and use the classic unfolded-box layout.
"""
import math
import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pixelart import Canvas, save_png, seeded, shade  # noqa: E402

TEX_W, TEX_H = 64, 64


# ---------------------------------------------------------------- scene helpers

def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def make_box_object(name, size, uv, tex, pivot, center, scale, parent=None, inflate=0.0):
    """Create a box part. size/uv/pivot/center are in texture pixels; scale converts pixels to metres."""
    w, h, d = size
    u, v = uv
    tw, th = tex
    hw, hh, hd = w / 2.0 + inflate, h / 2.0 + inflate, d / 2.0 + inflate
    # vertex positions relative to the pivot: x right, y back (+Y), z up
    cx, cy, cz = center
    xs = (cx - hw, cx + hw)
    ys = (cy - hd, cy + hd)      # y: front is the smaller (-Y) side
    zs = (cz - hh, cz + hh)
    verts = []
    for z in zs:
        for y in ys:
            for x in xs:
                verts.append((x * scale, y * scale, z * scale))
    # index = zi*4 + yi*2 + xi
    def vi(xi, yi, zi):
        return zi * 4 + yi * 2 + xi

    def uvp(px, py):
        return (px / tw, 1.0 - py / th)

    faces = []   # (vertex indices ccw seen from outside, uv corners in the same order)
    # front (-Y): seen from -Y, right = +X
    faces.append(([vi(0, 0, 0), vi(1, 0, 0), vi(1, 0, 1), vi(0, 0, 1)],
                  [uvp(u + d, v + d + h), uvp(u + d + w, v + d + h), uvp(u + d + w, v + d), uvp(u + d, v + d)]))
    # back (+Y): seen from +Y, right = -X
    faces.append(([vi(1, 1, 0), vi(0, 1, 0), vi(0, 1, 1), vi(1, 1, 1)],
                  [uvp(u + 2 * d + w, v + d + h), uvp(u + 2 * d + 2 * w, v + d + h), uvp(u + 2 * d + 2 * w, v + d), uvp(u + 2 * d + w, v + d)]))
    # model right (-X)
    faces.append(([vi(0, 1, 0), vi(0, 0, 0), vi(0, 0, 1), vi(0, 1, 1)],
                  [uvp(u, v + d + h), uvp(u + d, v + d + h), uvp(u + d, v + d), uvp(u, v + d)]))
    # model left (+X)
    faces.append(([vi(1, 0, 0), vi(1, 1, 0), vi(1, 1, 1), vi(1, 0, 1)],
                  [uvp(u + d + w, v + d + h), uvp(u + 2 * d + w, v + d + h), uvp(u + 2 * d + w, v + d), uvp(u + d + w, v + d)]))
    # top (+Z): back edge at the top of the region
    faces.append(([vi(0, 0, 1), vi(1, 0, 1), vi(1, 1, 1), vi(0, 1, 1)],
                  [uvp(u + d, v + d), uvp(u + d + w, v + d), uvp(u + d + w, v), uvp(u + d, v)]))
    # bottom (-Z)
    faces.append(([vi(0, 1, 0), vi(1, 1, 0), vi(1, 0, 0), vi(0, 0, 0)],
                  [uvp(u + d + w, v), uvp(u + d + 2 * w, v), uvp(u + d + 2 * w, v + d), uvp(u + d + w, v + d)]))

    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], [f[0] for f in faces])
    uv_layer = mesh.uv_layers.new(name='UVMap')
    for poly, (idx, uvs) in zip(mesh.polygons, faces):
        for li, uvc in zip(poly.loop_indices, uvs):
            uv_layer.data[li].uv = uvc
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    obj.location = (pivot[0] * scale, pivot[1] * scale, pivot[2] * scale)
    if parent is not None:
        obj.parent = parent
        # keep the world position: parent has identity transform, so location is already in parent space
    return obj


def make_root(name):
    root = bpy.data.objects.new(name, None)
    bpy.context.scene.collection.objects.link(root)
    return root


def export_fbx(path, objects):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objects:
        o.select_set(True)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={'EMPTY', 'MESH'}, global_scale=1.0,
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE', axis_forward='-Z', axis_up='Y',
        bake_space_transform=False, mesh_smooth_type='FACE', add_leaf_bones=False, path_mode='AUTO')


# ---------------------------------------------------------------- painting

def fill_faces(c, u, v, w, h, d, base, spread=0.06, rng=None, top=None, bottom=None, front=None, back=None,
               left=None, right=None):
    """Paint a box's unfolded faces with a base colour plus noise; individual faces may override."""
    rng = rng or seeded('fill', u * 100 + v)

    def region(x0, y0, rw, rh, col):
        for y in range(y0, y0 + rh):
            for x in range(x0, x0 + rw):
                f = 1.0 + rng.uniform(-spread, spread)
                cc = shade(col, f)
                c.set(x, y, (cc[0], cc[1], cc[2], 255))
    region(u + d, v, w, d, top or base)
    region(u + d + w, v, w, d, bottom or shade(base, 0.8))
    region(u, v + d, d, h, right or base)
    region(u + d, v + d, w, h, front or base)
    region(u + d + w, v + d, d, h, left or base)
    region(u + 2 * d + w, v + d, w, h, back or shade(base, 0.9))


def px(c, x, y, col):
    c.set(x, y, (col[0], col[1], col[2], 255))


# ---------------------------------------------------------------- humanoids

def humanoid(name, tex_painter, arm_w=4, scale=1.8 / 32.0, arm_pose=0.0):
    reset_scene()
    root = make_root(name)
    tex = (TEX_W, TEX_H)
    parts = []
    # y is depth (front = -Y), z is up in pixels, feet at z = 0.
    head = make_box_object('Head', (8, 8, 8), (0, 0), tex, (0, 0, 24), (0, 0, 4), scale, root)
    body = make_box_object('Body', (8, 12, 4), (16, 16), tex, (0, 0, 12), (0, 0, 6), scale, root)
    armR = make_box_object('ArmR', (arm_w, 12, arm_w), (40, 16), tex, (-(4 + arm_w / 2.0), 0, 24), (0, 0, -6 + 2), scale, root)
    armL = make_box_object('ArmL', (arm_w, 12, arm_w), (32, 48), tex, ((4 + arm_w / 2.0), 0, 24), (0, 0, -6 + 2), scale, root)
    legR = make_box_object('LegR', (4, 12, 4), (0, 16), tex, (-2, 0, 12), (0, 0, -6), scale, root)
    legL = make_box_object('LegL', (4, 12, 4), (16, 48), tex, (2, 0, 12), (0, 0, -6), scale, root)
    parts = [root, head, body, armR, armL, legR, legL]
    c = Canvas(TEX_W, TEX_H)
    tex_painter(c, arm_w)
    return root, parts, c


def paint_player(c, arm_w):
    skin, hair, tunic, trousers, boots = (232, 184, 148), (84, 54, 30), (46, 128, 138), (66, 74, 112), (50, 38, 30)
    rng = seeded('player')
    fill_faces(c, 0, 0, 8, 8, 8, skin, 0.03, rng, top=hair, back=hair, left=shade(hair, 1.05), right=shade(hair, 0.95))
    # hair fringe on the front and the face
    for x in range(8, 16):
        px(c, x, 8, hair); px(c, x, 9, hair)
    for x in (8, 15):
        px(c, x, 10, hair)
    px(c, 10, 12, (255, 255, 255)); px(c, 11, 12, (60, 90, 190)); px(c, 12, 12, (60, 90, 190)); px(c, 13, 12, (255, 255, 255))
    for x in range(10, 14):
        px(c, x, 14, (170, 100, 90))
    fill_faces(c, 16, 16, 8, 12, 4, tunic, 0.04, rng)
    for x in range(20, 28):
        px(c, x, 26, (86, 60, 34))        # belt
    fill_faces(c, 40, 16, arm_w, 12, arm_w, tunic, 0.04, rng, bottom=skin)
    fill_faces(c, 32, 48, arm_w, 12, arm_w, tunic, 0.04, rng, bottom=skin)
    for ox, oy in ((40, 16), (32, 48)):
        for yy in range(oy + arm_w + 8, oy + arm_w + 12):
            for xx in range(ox, ox + 2 * arm_w + arm_w * 2):
                if c.get(xx, yy)[3]:
                    px(c, xx, yy, skin)
    fill_faces(c, 0, 16, 4, 12, 4, trousers, 0.04, rng)
    fill_faces(c, 16, 48, 4, 12, 4, trousers, 0.04, rng)
    for ox, oy in ((0, 16), (16, 48)):
        for yy in range(oy + 4 + 9, oy + 4 + 12):
            for xx in range(ox, ox + 16):
                if c.get(xx, yy)[3]:
                    px(c, xx, yy, boots)


def paint_zombie(c, arm_w):
    skin, shirt, pants = (96, 142, 74), (52, 112, 150), (62, 58, 122)
    rng = seeded('zombie')
    fill_faces(c, 0, 0, 8, 8, 8, skin, 0.05, rng)
    for (x, y) in ((10, 12), (11, 12), (12, 12), (13, 12)):
        px(c, x, y, (10, 10, 10))
    px(c, 10, 11, (10, 10, 10)); px(c, 13, 11, (10, 10, 10))
    for x in range(10, 14):
        px(c, x, 14, (40, 70, 40))
    fill_faces(c, 16, 16, 8, 12, 4, shirt, 0.05, rng)
    fill_faces(c, 40, 16, arm_w, 12, arm_w, skin, 0.05, rng)
    fill_faces(c, 32, 48, arm_w, 12, arm_w, skin, 0.05, rng)
    fill_faces(c, 0, 16, 4, 12, 4, pants, 0.05, rng)
    fill_faces(c, 16, 48, 4, 12, 4, pants, 0.05, rng)
    c.scatter((70, 110, 56, 255), 30, rng, (0, 0, 40, 30))


def paint_skeleton(c, arm_w):
    bone, joint = (222, 222, 212), (150, 150, 142)
    rng = seeded('skeleton')
    fill_faces(c, 0, 0, 8, 8, 8, bone, 0.04, rng)
    for (x, y) in ((10, 11), (11, 11), (12, 11), (13, 11), (10, 12), (13, 12)):
        px(c, x, y, (20, 20, 22))
    for x in range(10, 14):
        px(c, x, 14, (60, 60, 60))
    fill_faces(c, 16, 16, 8, 12, 4, (28, 28, 30), 0.0, rng)
    for r in range(0, 12, 3):
        for x in range(20, 28):
            px(c, x, 20 + r, bone)
    for x in (23, 24):
        for y in range(20, 32):
            px(c, x, y, bone)
    fill_faces(c, 40, 16, arm_w, 12, arm_w, bone, 0.04, rng)
    fill_faces(c, 32, 48, arm_w, 12, arm_w, bone, 0.04, rng)
    fill_faces(c, 0, 16, 4, 12, 4, bone, 0.04, rng)
    fill_faces(c, 16, 48, 4, 12, 4, bone, 0.04, rng)
    c.scatter(joint + (255,), 30, rng, (0, 16, 63, 63))


def build_humanoid(root_dir, name, painter, arm_w=4, scale=1.8 / 32.0):
    root, parts, canvas = humanoid(name, painter, arm_w, scale)
    save_png(canvas, os.path.join(root_dir, 'Models', 'Textures', name + '.png'))
    export_fbx(os.path.join(root_dir, 'Models', name + '.fbx'), parts)


# ---------------------------------------------------------------- quadrupeds and others

def quadruped(root_dir, name, spec, painter, scale):
    reset_scene()
    root = make_root(name)
    tex = (spec.get('tex_w', 64), spec.get('tex_h', 32))
    parts = [root]
    for p in spec['parts']:
        parent = root
        parts.append(make_box_object(p['name'], p['size'], p['uv'], tex, p['pivot'], p['center'], scale, parent, p.get('inflate', 0.0)))
    c = Canvas(tex[0], tex[1])
    painter(c, spec)
    save_png(c, os.path.join(root_dir, 'Models', 'Textures', name + '.png'))
    export_fbx(os.path.join(root_dir, 'Models', name + '.fbx'), parts)


PIG = {
    'parts': [
        # horizontal bodies lie along Y: size is (width, height, depth) with depth along the animal's length
        {'name': 'Body', 'size': (10, 8, 16), 'uv': (28, 8), 'pivot': (0, 0, 10), 'center': (0, 0, 0)},
        {'name': 'Head', 'size': (8, 8, 8), 'uv': (0, 0), 'pivot': (0, -8, 12), 'center': (0, -4, 0)},
        {'name': 'Snout', 'size': (4, 3, 1), 'uv': (16, 16), 'pivot': (0, -8, 12), 'center': (0, -8.5, -1)},
        {'name': 'LegFR', 'size': (4, 6, 4), 'uv': (0, 16), 'pivot': (-3, -5, 6), 'center': (0, 0, -3)},
        {'name': 'LegFL', 'size': (4, 6, 4), 'uv': (0, 16), 'pivot': (3, -5, 6), 'center': (0, 0, -3)},
        {'name': 'LegBR', 'size': (4, 6, 4), 'uv': (0, 16), 'pivot': (-3, 5, 6), 'center': (0, 0, -3)},
        {'name': 'LegBL', 'size': (4, 6, 4), 'uv': (0, 16), 'pivot': (3, 5, 6), 'center': (0, 0, -3)},
    ],
}


def paint_pig(c, spec):
    pink, snout = (238, 152, 152), (214, 120, 122)
    rng = seeded('pig')
    fill_faces(c, 28, 8, 10, 8, 16, pink, 0.04, rng)
    fill_faces(c, 0, 0, 8, 8, 8, pink, 0.04, rng)
    for (x, y) in ((10, 11), (13, 11)):
        px(c, x, y, (255, 255, 255)); px(c, x + 0, y + 0, (40, 30, 80))
    fill_faces(c, 16, 16, 4, 3, 1, snout, 0.03, rng)
    px(c, 17, 19, (120, 50, 60)); px(c, 18, 19, (120, 50, 60))
    fill_faces(c, 0, 16, 4, 6, 4, pink, 0.04, rng)


COW = {
    'parts': [
        {'name': 'Body', 'size': (12, 10, 18), 'uv': (18, 4), 'pivot': (0, 0, 17), 'center': (0, 0, 0)},
        {'name': 'Head', 'size': (8, 8, 6), 'uv': (0, 0), 'pivot': (0, -9, 20), 'center': (0, -3, 0)},
        {'name': 'HornR', 'size': (1, 3, 1), 'uv': (22, 0), 'pivot': (-5, -10, 26), 'center': (0, 0, 1)},
        {'name': 'HornL', 'size': (1, 3, 1), 'uv': (22, 0), 'pivot': (5, -10, 26), 'center': (0, 0, 1)},
        {'name': 'LegFR', 'size': (4, 12, 4), 'uv': (0, 16), 'pivot': (-4, -6, 12), 'center': (0, 0, -6)},
        {'name': 'LegFL', 'size': (4, 12, 4), 'uv': (0, 16), 'pivot': (4, -6, 12), 'center': (0, 0, -6)},
        {'name': 'LegBR', 'size': (4, 12, 4), 'uv': (0, 16), 'pivot': (-4, 7, 12), 'center': (0, 0, -6)},
        {'name': 'LegBL', 'size': (4, 12, 4), 'uv': (0, 16), 'pivot': (4, 7, 12), 'center': (0, 0, -6)},
    ],
}


def paint_cow(c, spec):
    brown, white, dark = (74, 48, 30), (232, 228, 220), (46, 30, 20)
    rng = seeded('cow')
    fill_faces(c, 18, 4, 12, 10, 18, brown, 0.05, rng)
    for _ in range(7):
        x, y = rng.randint(18, 56), rng.randint(4, 30)
        for dx in range(rng.randint(2, 5)):
            for dy in range(rng.randint(2, 4)):
                if c.get(x + dx, y + dy)[3]:
                    px(c, x + dx, y + dy, white)
    fill_faces(c, 0, 0, 8, 8, 6, brown, 0.05, rng)
    for x in range(6, 14):
        px(c, x, 9, shade(brown, 1.1))
    px(c, 7, 8, (255, 255, 255)); px(c, 12, 8, (255, 255, 255)); px(c, 8, 8, (20, 20, 20)); px(c, 11, 8, (20, 20, 20))
    for x in range(8, 12):
        for y in range(11, 14):
            px(c, x, y, (210, 170, 150))
    fill_faces(c, 22, 0, 1, 3, 1, white, 0.0, rng)
    fill_faces(c, 0, 16, 4, 12, 4, dark, 0.05, rng)


SHEEP = {
    'parts': [
        {'name': 'Body', 'size': (8, 6, 16), 'uv': (28, 8), 'pivot': (0, 0, 15), 'center': (0, 0, 0)},
        {'name': 'Wool', 'size': (8, 6, 16), 'uv': (28, 8), 'pivot': (0, 0, 15), 'center': (0, 0, 0), 'inflate': 2.0},
        {'name': 'Head', 'size': (6, 6, 8), 'uv': (0, 0), 'pivot': (0, -8, 18), 'center': (0, -3, 0)},
        {'name': 'LegFR', 'size': (4, 12, 4), 'uv': (0, 16), 'pivot': (-3, -5, 12), 'center': (0, 0, -6)},
        {'name': 'LegFL', 'size': (4, 12, 4), 'uv': (0, 16), 'pivot': (3, -5, 12), 'center': (0, 0, -6)},
        {'name': 'LegBR', 'size': (4, 12, 4), 'uv': (0, 16), 'pivot': (-3, 6, 12), 'center': (0, 0, -6)},
        {'name': 'LegBL', 'size': (4, 12, 4), 'uv': (0, 16), 'pivot': (3, 6, 12), 'center': (0, 0, -6)},
    ],
}


def paint_sheep(c, spec):
    wool, skin = (236, 234, 230), (196, 176, 158)
    rng = seeded('sheep')
    fill_faces(c, 28, 8, 8, 6, 16, wool, 0.07, rng)
    fill_faces(c, 0, 0, 6, 6, 8, skin, 0.04, rng)
    px(c, 8, 9, (255, 255, 255)); px(c, 12, 9, (255, 255, 255)); px(c, 9, 9, (30, 30, 30)); px(c, 11, 9, (30, 30, 30))
    fill_faces(c, 0, 16, 4, 12, 4, skin, 0.04, rng)


CHICKEN = {
    'parts': [
        {'name': 'Body', 'size': (6, 6, 8), 'uv': (0, 9), 'pivot': (0, 0, 8), 'center': (0, 0, 0)},
        {'name': 'Head', 'size': (4, 6, 3), 'uv': (0, 0), 'pivot': (0, -4, 11), 'center': (0, -1, 2)},
        {'name': 'Beak', 'size': (4, 2, 2), 'uv': (14, 0), 'pivot': (0, -4, 11), 'center': (0, -3.5, 3)},
        {'name': 'Wattle', 'size': (2, 2, 2), 'uv': (14, 4), 'pivot': (0, -4, 11), 'center': (0, -2.5, 1)},
        {'name': 'WingR', 'size': (1, 4, 6), 'uv': (24, 13), 'pivot': (-3.5, 0, 10), 'center': (0, 0, -2)},
        {'name': 'WingL', 'size': (1, 4, 6), 'uv': (24, 13), 'pivot': (3.5, 0, 10), 'center': (0, 0, -2)},
        {'name': 'LegR', 'size': (3, 5, 3), 'uv': (26, 0), 'pivot': (-1.5, 1, 5), 'center': (0, 0, -2.5)},
        {'name': 'LegL', 'size': (3, 5, 3), 'uv': (26, 0), 'pivot': (1.5, 1, 5), 'center': (0, 0, -2.5)},
    ],
}


def paint_chicken(c, spec):
    white, yellow, red = (244, 244, 240), (240, 190, 40), (206, 40, 40)
    rng = seeded('chicken')
    fill_faces(c, 0, 9, 6, 6, 8, white, 0.04, rng)
    fill_faces(c, 0, 0, 4, 6, 3, white, 0.03, rng)
    px(c, 4, 5, (20, 20, 20)); px(c, 6, 5, (20, 20, 20))
    fill_faces(c, 14, 0, 4, 2, 2, yellow, 0.03, rng)
    fill_faces(c, 14, 4, 2, 2, 2, red, 0.03, rng)
    fill_faces(c, 24, 13, 1, 4, 6, shade(white, 0.92), 0.04, rng)
    fill_faces(c, 26, 0, 3, 5, 3, yellow, 0.03, rng)


CREEPER = {
    'parts': [
        {'name': 'Head', 'size': (8, 8, 8), 'uv': (0, 0), 'pivot': (0, 0, 20), 'center': (0, 0, 4)},
        {'name': 'Body', 'size': (8, 12, 4), 'uv': (16, 16), 'pivot': (0, 0, 12), 'center': (0, 0, 0)},
        {'name': 'LegFR', 'size': (4, 6, 4), 'uv': (0, 16), 'pivot': (-2, -2, 6), 'center': (0, 0, -3)},
        {'name': 'LegFL', 'size': (4, 6, 4), 'uv': (0, 16), 'pivot': (2, -2, 6), 'center': (0, 0, -3)},
        {'name': 'LegBR', 'size': (4, 6, 4), 'uv': (0, 16), 'pivot': (-2, 2, 6), 'center': (0, 0, -3)},
        {'name': 'LegBL', 'size': (4, 6, 4), 'uv': (0, 16), 'pivot': (2, 2, 6), 'center': (0, 0, -3)},
    ],
}


def paint_creeper(c, spec):
    rng = seeded('creeper')
    green = (84, 168, 72)
    for (u, v, w, h, d) in ((0, 0, 8, 8, 8), (16, 16, 8, 12, 4), (0, 16, 4, 6, 4)):
        fill_faces(c, u, v, w, h, d, green, 0.12, rng)
    dark = (14, 40, 14)
    # the face on the head's front region (x 8..15, y 8..15)
    for (x, y) in ((9, 10), (10, 10), (9, 11), (10, 11), (13, 10), (14, 10), (13, 11), (14, 11), (11, 12), (12, 12),
                   (11, 13), (12, 13), (10, 14), (13, 14), (10, 13), (13, 13)):
        px(c, x, y, dark)


MOBS = {
    'pig': (PIG, paint_pig, 1 / 16.0),
    'cow': (COW, paint_cow, 1 / 16.0),
    'sheep': (SHEEP, paint_sheep, 1 / 16.0),
    'chicken': (CHICKEN, paint_chicken, 1 / 16.0),
    'creeper': (CREEPER, paint_creeper, 1.7 / 26.0),
}


def main():
    argv = sys.argv[sys.argv.index('--') + 1:]
    root_dir = argv[0]
    build_humanoid(root_dir, 'player', paint_player)
    build_humanoid(root_dir, 'zombie', paint_zombie)
    build_humanoid(root_dir, 'skeleton', paint_skeleton, arm_w=2, scale=1.9 / 32.0)
    for name, (spec, painter, scale) in MOBS.items():
        quadruped(root_dir, name, spec, painter, scale)
    print('models written to', os.path.join(root_dir, 'Models'))


main()
