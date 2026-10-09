"""Builds village houses, a barn and a stone watchtower and exports them as one GLB.

Run:  blender --background --python tools/blender/build_structures.py -- public/assets/structures.glb
Models stand on z=0, centred on the origin, front door facing -Y (which exports as +Z).
"""
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Vector

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from assetlib import MeshBuilder, colorize, export_glb, fbm, height_to_normal, make_material, save_image  # noqa: E402

OUTPUT = sys.argv[sys.argv.index('--') + 1]
TEXTURE_DIR = os.path.join(os.path.dirname(os.path.abspath(OUTPUT)), '..', '..', 'build', 'textures')

bpy.ops.wm.read_factory_settings(use_empty=True)

STONE, PLASTER, ROOF, WOOD, GLASS = range(5)


# ---------------------------------------------------------------------------- textures

def stone_wall_textures(seed):
    """Coursed rubble: rows of uneven blocks with recessed mortar."""
    rng = np.random.default_rng(seed)
    size = 512
    rows = 8
    height = np.zeros((size, size))
    tone = np.zeros((size, size))
    row_height = size // rows
    for row in range(rows):
        x = int(rng.integers(0, 40))
        y0 = row * row_height
        while x < size + 60:
            width = int(rng.integers(46, 110))
            shade = rng.uniform(0.35, 0.95)
            xs = np.arange(x, x + width) % size
            ys = np.arange(y0, y0 + row_height)
            # Rounded block profile: high in the middle, falling into the joints.
            fx = np.minimum(np.arange(width), width - 1 - np.arange(width)) / 7.0
            fy = np.minimum(np.arange(row_height), row_height - 1 - np.arange(row_height)) / 7.0
            profile = np.clip(np.minimum(fx[None, :], fy[:, None]), 0, 1)
            height[np.ix_(ys, xs)] = profile
            tone[np.ix_(ys, xs)] = shade
            x += width
    grain = fbm(size, 16, 16, 4, rng)
    value = np.clip(tone * (0.5 + height * 0.5) * (0.7 + grain * 0.6), 0, 1)
    color = colorize(value, (0.13, 0.12, 0.11), (0.7, 0.66, 0.58))
    return save_image('stone_color', color, TEXTURE_DIR), save_image('stone_normal', height_to_normal(height + grain * 0.25, 6.0), TEXTURE_DIR, True)


def plaster_textures(seed):
    rng = np.random.default_rng(seed)
    blotch = fbm(512, 4, 4, 5, rng)
    streak = fbm(512, 24, 2, 3, rng)
    value = np.clip(0.55 + blotch * 0.4 - streak * 0.18, 0, 1)
    color = colorize(value, (0.55, 0.5, 0.42), (0.95, 0.92, 0.84))
    return save_image('plaster_color', color, TEXTURE_DIR), save_image('plaster_normal', height_to_normal(fbm(512, 40, 40, 3, rng), 2.0), TEXTURE_DIR, True)


def roof_textures(seed):
    """Overlapping rows of rounded tiles."""
    rng = np.random.default_rng(seed)
    size = 512
    rows, columns = 10, 12
    ys, xs = np.mgrid[0:size, 0:size]
    row = ys * rows // size
    fy = (ys * rows / size) % 1.0
    shift = (row % 2) * 0.5
    fx = ((xs * columns / size) + shift) % 1.0
    column = ((xs * columns / size) + shift).astype(int) % columns
    # Each tile is thickest at its lower lip and tucks under the row above.
    lip = fy ** 1.6
    rounded = 1.0 - np.abs(fx - 0.5) * 2.0
    height = np.clip(lip * (0.45 + 0.55 * np.sqrt(np.clip(rounded * 2.2, 0, 1))), 0, 1)
    per_tile = rng.random((rows, columns))[row, column]
    weather = fbm(size, 5, 5, 4, rng)
    value = np.clip(0.25 + per_tile * 0.45 + height * 0.25 + weather * 0.25 - 0.15, 0, 1)
    color = colorize(value, (0.2, 0.08, 0.05), (0.72, 0.36, 0.22))
    return save_image('roof_color', color, TEXTURE_DIR), save_image('roof_normal', height_to_normal(height, 9.0), TEXTURE_DIR, True)


def wood_textures(seed):
    rng = np.random.default_rng(seed)
    size = 512
    grain = fbm(size, 3, 40, 4, rng)
    planks = 6
    xs = np.arange(size)
    seam = np.clip(np.minimum(xs % (size // planks), (size // planks) - 1 - xs % (size // planks)) / 3.0, 0, 1)[None, :].repeat(size, 0)
    per_plank = rng.random(planks)[(xs * planks // size) % planks][None, :].repeat(size, 0)
    value = np.clip((0.3 + grain * 0.5 + per_plank * 0.25) * (0.45 + seam * 0.55), 0, 1)
    color = colorize(value, (0.09, 0.055, 0.03), (0.5, 0.35, 0.2))
    return save_image('wood_color', color, TEXTURE_DIR), save_image('wood_normal', height_to_normal(grain * 0.5 + seam, 5.0), TEXTURE_DIR, True)


# ---------------------------------------------------------------------------- geometry helpers

class Structure(MeshBuilder):
    def quad(self, a, b, c, d, material, tile=2.5):
        """Counter-clockwise quad seen from outside. UVs follow real-world size so textures keep their scale."""
        a, b, c, d = Vector(a), Vector(b), Vector(c), Vector(d)
        normal = (b - a).cross(d - a).normalized()
        start = len(self.vertices)
        for corner in (a, b, c, d):
            self.vertices.append(tuple(corner))
            self.normals.append(tuple(normal))
        self.faces.append((start, start + 1, start + 2, start + 3))
        u_axis = (b - a).normalized()
        v_axis = normal.cross(u_axis)
        self.uvs.extend([((corner - a).dot(u_axis) / tile, (corner - a).dot(v_axis) / tile) for corner in (a, b, c, d)])
        self.material_indices.append(material)

    def triangle(self, a, b, c, material, tile=2.5):
        a, b, c = Vector(a), Vector(b), Vector(c)
        normal = (b - a).cross(c - a).normalized()
        start = len(self.vertices)
        for corner in (a, b, c):
            self.vertices.append(tuple(corner))
            self.normals.append(tuple(normal))
        self.faces.append((start, start + 1, start + 2))
        u_axis = (b - a).normalized()
        v_axis = normal.cross(u_axis)
        self.uvs.extend([((corner - a).dot(u_axis) / tile, (corner - a).dot(v_axis) / tile) for corner in (a, b, c)])
        self.material_indices.append(material)

    def box(self, lo, hi, material, tile=2.5, top=True):
        x0, y0, z0 = lo
        x1, y1, z1 = hi
        self.quad((x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1), material, tile)
        self.quad((x1, y1, z0), (x0, y1, z0), (x0, y1, z1), (x1, y1, z1), material, tile)
        self.quad((x1, y0, z0), (x1, y1, z0), (x1, y1, z1), (x1, y0, z1), material, tile)
        self.quad((x0, y1, z0), (x0, y0, z0), (x0, y0, z1), (x0, y1, z1), material, tile)
        if top:
            self.quad((x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1), material, tile)

    def opening(self, wall, along, bottom, width, height, depth_offset, half_w, half_d, glass=True):
        """Door or window set into a wall: wooden surround with a glass or plank panel proud of the surface."""
        for inset, material, grow in ((0.03, WOOD, 0.12), (0.06, GLASS if glass else WOOD, 0.0)):
            w = width / 2 + grow
            z0, z1 = bottom - grow, bottom + height + grow
            if wall == 'front':
                y = -half_d - inset - depth_offset
                self.quad((along - w, y, z0), (along + w, y, z0), (along + w, y, z1), (along - w, y, z1), material, 1.6)
            elif wall == 'back':
                y = half_d + inset + depth_offset
                self.quad((along + w, y, z0), (along - w, y, z0), (along - w, y, z1), (along + w, y, z1), material, 1.6)
            elif wall == 'right':
                x = half_w + inset + depth_offset
                self.quad((x, along - w, z0), (x, along + w, z0), (x, along + w, z1), (x, along - w, z1), material, 1.6)
            else:
                x = -half_w - inset - depth_offset
                self.quad((x, along + w, z0), (x, along - w, z0), (x, along - w, z1), (x, along + w, z1), material, 1.6)


def build_house(name, materials, width, depth, wall_height, roof_height, storeys=1, wall_material=PLASTER, chimney=True, big_door=False):
    s = Structure()
    hw, hd = width / 2, depth / 2
    plinth = 0.7
    # Stone plinth (runs below ground so it never floats on a slope), then the main walls.
    s.box((-hw - 0.08, -hd - 0.08, -1.5), (hw + 0.08, hd + 0.08, plinth), STONE, top=True)
    s.box((-hw, -hd, plinth), (hw, hd, wall_height), wall_material, top=False)
    # Gable ends (left and right walls) and the roof, which overhangs all round.
    ridge = wall_height + roof_height
    s.triangle((hw, -hd, wall_height), (hw, hd, wall_height), (hw, 0, ridge), wall_material)
    s.triangle((-hw, hd, wall_height), (-hw, -hd, wall_height), (-hw, 0, ridge), wall_material)
    over = 0.55
    slope = roof_height / hd
    eave_z = wall_height - over * slope
    for thickness in (0.0, -0.14):
        r = ridge + thickness
        e = eave_z + thickness
        front = [(-hw - over, -hd - over, e), (hw + over, -hd - over, e), (hw + over, 0, r), (-hw - over, 0, r)]
        back = [(hw + over, hd + over, e), (-hw - over, hd + over, e), (-hw - over, 0, r), (hw + over, 0, r)]
        if thickness == 0.0:
            s.quad(*front, ROOF, 2.2)
            s.quad(*back, ROOF, 2.2)
        else:
            # Underside of the eaves in timber.
            s.quad(*reversed(front), WOOD, 2.0)
            s.quad(*reversed(back), WOOD, 2.0)
    if chimney:
        s.box((hw * 0.35, hd * 0.2, wall_height), (hw * 0.35 + 0.8, hd * 0.2 + 0.8, ridge + 0.9), STONE, tile=1.6)

    # Exposed timber frame.
    for x in (-hw, hw):
        for y in (-hd, hd):
            s.box((x - 0.11, y - 0.11, plinth), (x + 0.11, y + 0.11, wall_height), WOOD, tile=1.5, top=False)
    for z in ([wall_height - 0.2] + ([plinth + (wall_height - plinth) / 2] if storeys == 2 else [])):
        s.box((-hw - 0.04, -hd - 0.04, z - 0.1), (hw + 0.04, hd + 0.04, z + 0.1), WOOD, tile=1.5)

    door_w, door_h = (2.6, 2.9) if big_door else (1.1, 2.05)
    s.opening('front', 0.0 if big_door else -hw * 0.3, plinth - 0.35, door_w, door_h, 0.0, hw, hd, glass=False)
    # Two stone steps up to the door, so the sill is reachable from the ground.
    door_x = 0.0 if big_door else -hw * 0.3
    for i, (reach, top) in enumerate(((0.95, 0.36), (0.5, 0.7))):
        s.box((door_x - door_w / 2 - 0.15, -hd - 0.08 - reach, -1.5), (door_x + door_w / 2 + 0.15, -hd, top), STONE, tile=1.4)
    floor_levels = [plinth + 0.75] + ([plinth + (wall_height - plinth) / 2 + 0.55] if storeys == 2 else [])
    if not big_door:
        for z in floor_levels:
            s.opening('front', hw * 0.45, z, 1.0, 1.05, 0.0, hw, hd)
            s.opening('back', -hw * 0.4, z, 1.0, 1.05, 0.0, hw, hd)
            s.opening('back', hw * 0.4, z, 1.0, 1.05, 0.0, hw, hd)
            s.opening('right', 0.0, z, 0.9, 1.05, 0.0, hw, hd)
            s.opening('left', 0.0, z, 0.9, 1.05, 0.0, hw, hd)
    return s.build(name, materials)


def build_tower(name, materials):
    s = Structure()
    height = 17.0
    # Slightly battered (tapering) drum, a corbelled gallery, then a conical tiled roof.
    s.tube([(0, 0, -2), (0, 0, height * 0.5), (0, 0, height)], [3.5, 3.15, 3.0], 20, STONE, uv_repeat=8, v_scale=0.4)
    s.tube([(0, 0, height), (0, 0, height + 0.5), (0, 0, height + 2.6)], [3.0, 3.6, 3.6], 20, STONE, uv_repeat=8, v_scale=0.4)
    s.tube([(0, 0, height + 2.4), (0, 0, height + 5.2), (0, 0, height + 8.0)], [4.1, 2.0, 0.05], 20, ROOF, uv_repeat=9, v_scale=0.45)
    for i in range(6):
        angle = i / 6 * math.tau
        normal = Vector((math.cos(angle), math.sin(angle), 0))
        side = Vector((-normal.y, normal.x, 0))
        for z, radius in ((height + 1.0, 3.66), (height * 0.55, 3.2)):
            center = normal * radius + Vector((0, 0, z))
            for grow, material in ((0.14, WOOD), (0.0, GLASS)):
                w, h = 0.38 + grow, 1.2 + grow * 2
                p = center + normal * (0.0 if material == WOOD else 0.03)
                s.quad(p - side * w, p + side * w, p + side * w + Vector((0, 0, h)), p - side * w + Vector((0, 0, h)), material, 1.5)
    door = Vector((0, -3.5, 0))
    s.quad(door + Vector((-0.8, -0.05, -0.3)), door + Vector((0.8, -0.05, -0.3)), door + Vector((0.8, 0.12, 2.3)), door + Vector((-0.8, 0.12, 2.3)), WOOD, 1.6)
    return s.build(name, materials)


def build_fence(name, materials):
    s = Structure()
    for x in (-1.5, 1.5):
        s.box((x - 0.07, -0.07, -0.6), (x + 0.07, 0.07, 1.15), WOOD, tile=1.2)
    for z in (0.45, 0.9):
        s.box((-1.6, -0.035, z - 0.06), (1.6, 0.035, z + 0.06), WOOD, tile=1.2)
    return s.build(name, materials)


# ---------------------------------------------------------------------------- assemble

materials = [
    make_material('stone', *stone_wall_textures(1), roughness=0.92),
    make_material('plaster', *plaster_textures(2), roughness=0.9),
    make_material('roof', *roof_textures(3), roughness=0.8),
    make_material('wood', *wood_textures(4), roughness=0.85),
    make_material('glass', roughness=0.08, metallic=0.4, base_color=(0.03, 0.05, 0.07)),
]

report = {}
for obj in (
    build_house('house_cottage', materials, 7.5, 5.6, 3.4, 2.6),
    build_house('house_tall', materials, 6.4, 6.0, 5.9, 3.0, storeys=2),
    build_house('house_stone', materials, 8.5, 5.2, 3.3, 2.3, wall_material=STONE),
    build_house('barn', materials, 9.5, 7.5, 4.6, 3.6, wall_material=WOOD, chimney=False, big_door=True),
    build_tower('tower', materials),
    build_fence('fence', materials),
):
    report[obj.name] = len(obj.data.polygons)

size = export_glb(os.path.abspath(OUTPUT))
print('ASSET_REPORT', report, 'bytes', size)
