"""Builds trees, rocks and grass and exports them as one GLB.

Run:  blender --background --python tools/blender/build_environment.py -- public/assets/environment.glb
"""
import math
import os
import random
import sys

import bpy
import bmesh
import numpy as np
from mathutils import Matrix, Vector, noise

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from assetlib import (  # noqa: E402
    MeshBuilder, colorize, draw_ellipse, export_glb, fbm, height_to_normal, make_material, save_image,
)

OUTPUT = sys.argv[sys.argv.index('--') + 1]
TEXTURE_DIR = os.path.join(os.path.dirname(os.path.abspath(OUTPUT)), '..', '..', 'build', 'textures')

bpy.ops.wm.read_factory_settings(use_empty=True)


# ---------------------------------------------------------------------------- textures

def bark_textures(name, dark, light, seed):
    rng = np.random.default_rng(seed)
    # Long vertical fissures: noise stretched along the trunk.
    ridges = fbm(512, 24, 3, 4, rng)
    ridges = np.abs(ridges - 0.5) * 2.0
    detail = fbm(512, 32, 32, 3, rng)
    height = np.clip(ridges * 0.8 + detail * 0.2, 0, 1)
    color = colorize(height * 0.85 + rng.random((512, 512)) * 0.15, dark, light)
    return save_image(name + '_color', color, TEXTURE_DIR), save_image(name + '_normal', height_to_normal(height, 5.0), TEXTURE_DIR, True)


def leaf_cluster_texture(name, seed, base, count=230, leaf=(34, 15)):
    rng = np.random.default_rng(seed)
    size = 512
    image = np.zeros((size, size, 4))
    image[..., 0], image[..., 1], image[..., 2] = base
    for i in range(count):
        # Dense in the middle, ragged toward the edge so cards do not read as squares.
        radius = abs(rng.normal(0, 0.27)) * size
        theta = rng.random() * math.tau
        if radius > size * 0.47:
            continue
        cx = size / 2 + math.cos(theta) * radius
        cy = size / 2 + math.sin(theta) * radius
        # Later leaves sit on top and are brighter: fake depth and sun on the outer layer.
        depth = i / count
        tint = 0.55 + depth * 0.6 + rng.normal(0, 0.08)
        color = (base[0] * tint * (1 + rng.normal(0, 0.1)), base[1] * tint, base[2] * tint * 0.9)
        draw_ellipse(image, cx, cy, leaf[0] * (0.7 + rng.random() * 0.5), leaf[1] * (0.7 + rng.random() * 0.5),
                     rng.random() * math.tau, color, shade=0.3, rib=0.12)
    return save_image(name, image, TEXTURE_DIR)


def needle_texture(name, seed):
    rng = np.random.default_rng(seed)
    size = 512
    base = (0.07, 0.17, 0.08)
    image = np.zeros((size, size, 4))
    image[..., 0], image[..., 1], image[..., 2] = base
    # A spray: one main twig up the middle with side twigs, each bristling with needles.
    twigs = [((size / 2, size * 0.98), -math.pi / 2, size * 0.94)]
    for i in range(11):
        t = 0.12 + i * 0.075
        for side in (-1, 1):
            origin = (size / 2, size * (0.98 - t * 0.94))
            twigs.append((origin, -math.pi / 2 + side * (0.75 + rng.random() * 0.3), size * (0.42 * (1 - t * 0.7))))
    for (ox, oy), angle, length in twigs:
        steps = int(length / 6)
        for s in range(steps):
            f = s / max(1, steps - 1)
            px = ox + math.cos(angle) * length * f
            py = oy + math.sin(angle) * length * f
            draw_ellipse(image, px, py, 4, 2.2, angle, (0.2, 0.13, 0.08))
            for side in (-1, 1):
                tint = 0.7 + rng.random() * 0.9
                needle_angle = angle + side * (0.9 + rng.random() * 0.5)
                needle_length = 15 + rng.random() * 9
                draw_ellipse(image, px + math.cos(needle_angle) * needle_length * 0.6, py + math.sin(needle_angle) * needle_length * 0.6,
                             needle_length, 1.9, needle_angle, (base[0] * tint, base[1] * tint * 1.25, base[2] * tint))
    return save_image(name, image, TEXTURE_DIR)


def grass_texture(name, seed):
    rng = np.random.default_rng(seed)
    size = 256
    base = (0.34, 0.44, 0.15)
    image = np.zeros((size, size, 4))
    image[..., 0], image[..., 1], image[..., 2] = base
    for _ in range(30):
        root_x = size * (0.08 + rng.random() * 0.84)
        height = size * (0.35 + rng.random() * 0.6)
        lean = rng.normal(0, 0.22)
        tint = 0.75 + rng.random() * 0.7
        color = (base[0] * tint * 1.1, base[1] * tint, base[2] * tint)
        # A blade is a stack of ever-thinner dabs following a slight curve.
        for s in range(14):
            f = s / 13
            px = root_x + math.sin(lean) * height * f + lean * 26 * f * f
            py = size - 1 - height * f
            draw_ellipse(image, px, py, 2.4 * (1 - f * 0.85) + 0.6, height / 22, lean + math.pi / 2, [c * (0.75 + f * 0.45) for c in color])
    return save_image(name, image, TEXTURE_DIR)


def rock_textures(name, seed):
    rng = np.random.default_rng(seed)
    broad = fbm(512, 5, 5, 5, rng)
    cracks = 1.0 - np.clip(np.abs(fbm(512, 7, 7, 4, rng) - 0.5) * 9.0, 0, 1)
    height = np.clip(broad - cracks * 0.35, 0, 1)
    color = colorize(np.clip(broad * 0.8 + rng.random((512, 512)) * 0.2 - cracks * 0.3, 0, 1), (0.2, 0.19, 0.17), (0.62, 0.59, 0.54))
    return save_image(name + '_color', color, TEXTURE_DIR), save_image(name + '_normal', height_to_normal(height, 7.0), TEXTURE_DIR, True)


# ---------------------------------------------------------------------------- trees

def perpendicular(direction, rng):
    axis = Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1)))
    return (axis - direction * axis.dot(direction)).normalized()


def build_broadleaf(name, seed, materials, lod):
    """Oak-like tree: recursive branching wood plus leaf-cluster cards shaded with spherical normals."""
    rng = random.Random(seed)
    builder = MeshBuilder()
    anchors = []
    trunk_height = rng.uniform(3.2, 4.4)
    max_depth = 1 if lod else 3

    def grow(start, direction, length, radius, depth):
        segments = 2 if lod else (5 if depth == 0 else 3)
        points = [Vector(start)]
        heading = Vector(direction).normalized()
        for _ in range(segments):
            # Wander a little and bend back toward the light.
            heading = (heading + perpendicular(heading, rng) * (0.1 + depth * 0.07) + Vector((0, 0, 0.1 * depth))).normalized()
            points.append(points[-1] + heading * (length / segments))
        end_radius = radius * (0.72 if depth == 0 else 0.5)
        radii = [radius + (end_radius - radius) * (i / segments) for i in range(segments + 1)]
        if depth == 0:
            radii[0] *= 1.45  # root flare
        sides = 5 if lod else max(4, 9 - depth * 2)
        builder.tube(points, radii, sides, 0, uv_repeat=2 if depth == 0 else 1)
        if depth >= max_depth:
            anchors.extend(points[1:])
            return
        if depth >= 1:
            anchors.append(points[-1])
        for i in range(rng.randint(3, 4) if depth == 0 else rng.randint(2, 3)):
            t = 1.0 if i == 0 else rng.uniform(0.45, 0.95)
            index = min(segments, max(1, round(t * segments)))
            spread = math.radians(rng.uniform(28, 62))
            axis = perpendicular(heading, rng)
            child = (Matrix.Rotation(spread, 3, axis) @ heading).normalized()
            grow(points[index], child, length * rng.uniform(0.62, 0.8), radii[index] * 0.72, depth + 1)

    grow((0, 0, -0.3), (0, 0, 1), trunk_height, rng.uniform(0.3, 0.42), 0)

    center = sum(anchors, Vector()) / len(anchors)
    cards_per_anchor = 1 if lod else 4
    card_size = (3.4, 4.6) if lod else (1.7, 2.6)
    for anchor in (anchors[::2] if lod else anchors):
        for _ in range(cards_per_anchor):
            offset = Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-0.6, 0.9))) * (0.4 if lod else 0.9)
            position = anchor + offset
            right = Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-0.4, 0.4))).normalized()
            up = right.cross(Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1)))).normalized()
            size = rng.uniform(*card_size)
            builder.card(position, right, up, size, size, 1,
                         normal=lambda corner: ((corner - center).normalized() + Vector((0, 0, 0.55))).normalized())
    return builder.build(name, materials)


def build_pine(name, seed, materials, lod):
    """Conifer: straight tapering trunk with whorls of drooping boughs carrying needle sprays."""
    rng = random.Random(seed)
    builder = MeshBuilder()
    height = rng.uniform(13, 17)
    trunk_points = [(rng.uniform(-0.05, 0.05) * i, rng.uniform(-0.05, 0.05) * i, -0.3 + (height + 0.3) * i / 6) for i in range(7)]
    base_radius = rng.uniform(0.26, 0.34)
    builder.tube(trunk_points, [base_radius * 1.3] + [base_radius * (1 - i / 6.3) for i in range(1, 7)], 5 if lod else 8, 0, uv_repeat=2)

    first_whorl = height * 0.2
    whorls = 9 if lod else 20
    for w in range(whorls):
        f = w / (whorls - 1)
        z = first_whorl + (height - first_whorl - 0.4) * f
        reach = (height * 0.27) * (1 - f) ** 0.8 + 0.35
        count = 3 if lod else rng.randint(5, 6)
        phase = rng.uniform(0, math.tau)
        for b in range(count):
            angle = phase + b / count * math.tau + rng.uniform(-0.2, 0.2)
            outward = Vector((math.cos(angle), math.sin(angle), 0))
            droop = -0.32 - (1 - f) * 0.2 + rng.uniform(-0.08, 0.08)
            direction = (outward + Vector((0, 0, droop))).normalized()
            start = Vector((0, 0, z))
            length = reach * rng.uniform(0.85, 1.1)
            tip = start + direction * length
            if not lod:
                # Boughs sag then lift slightly at the tip.
                middle = start + direction * length * 0.5 + Vector((0, 0, -0.05 * length))
                builder.tube([start, middle, tip + Vector((0, 0, 0.08 * length))], [0.07, 0.045, 0.02], 3, 0)
            side = direction.cross(Vector((0, 0, 1))).normalized()
            lift = side.cross(direction).normalized()
            spray_normal = (lift + outward * 0.35).normalized()
            width = length * (1.15 if lod else 0.85)
            builder.card(start + direction * 0.1, side, direction, width, length * 1.08, 1, normal=tuple(spray_normal), anchor_bottom=True)
            if not lod:
                # A second, tilted spray gives the bough thickness from the side.
                tilted = (side + lift * 0.6).normalized()
                builder.card(start + direction * 0.25 + Vector((0, 0, -0.12)), tilted, direction, width * 0.8, length * 0.9, 1,
                             normal=tuple(spray_normal), anchor_bottom=True)
    # Leader shoot.
    for angle in (0, math.pi / 2):
        right = Vector((math.cos(angle), math.sin(angle), 0))
        builder.card((0, 0, height - 1.7), right, (0, 0, 1), 1.3, 2.3, 1, normal=tuple((right.cross(Vector((0, 0, 1))) + Vector((0, 0, 0.6))).normalized()), anchor_bottom=True)
    return builder.build(name, materials)


# ---------------------------------------------------------------------------- rocks and grass

def build_rock(name, seed, material):
    rng = random.Random(seed)
    mesh = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=3, radius=1.0)
    offset = Vector((rng.uniform(0, 50), rng.uniform(0, 50), rng.uniform(0, 50)))
    stretch = Vector((rng.uniform(0.8, 1.35), rng.uniform(0.8, 1.35), rng.uniform(0.55, 0.85)))
    for vert in bm.verts:
        direction = vert.co.normalized()
        bulge = noise.fractal(direction * 1.1 + offset, 1.0, 2.0, 4) * 0.42
        # Quantizing part of the displacement produces the flat facets of fractured stone.
        facet = round(noise.noise(direction * 2.3 + offset) * 3.0) / 3.0 * 0.16
        vert.co = direction * (1.0 + bulge + facet)
        vert.co.x *= stretch.x
        vert.co.y *= stretch.y
        vert.co.z *= stretch.z
        vert.co.z = max(vert.co.z, -0.28)
    bm.normal_update()
    uv_layer = bm.loops.layers.uv.new('UVMap')
    for face in bm.faces:
        face.smooth = True
        # Box projection: map by the axis the face mostly points along.
        normal = face.normal
        axis = max(range(3), key=lambda i: abs(normal[i]))
        for loop in face.loops:
            co = loop.vert.co
            u, v = [(co.y, co.z), (co.x, co.z), (co.x, co.y)][axis]
            loop[uv_layer].uv = (u * 0.45, v * 0.45)
    bm.to_mesh(mesh)
    bm.free()
    mesh.materials.append(material)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def build_grass(name, seed, material):
    rng = random.Random(seed)
    builder = MeshBuilder()
    for i in range(3):
        angle = i / 3 * math.pi + rng.uniform(-0.2, 0.2)
        right = Vector((math.cos(angle), math.sin(angle), 0))
        # Upward normals make the cards take the same light as the ground they stand on.
        builder.card((rng.uniform(-0.08, 0.08), rng.uniform(-0.08, 0.08), -0.03), right, (0, 0, 1), 0.8, 0.42, 0, normal=(0, 0, 1), anchor_bottom=True)
    return builder.build(name, [material])


# ---------------------------------------------------------------------------- assemble

oak_bark = make_material('bark_oak', *bark_textures('bark_oak', (0.1, 0.075, 0.055), (0.42, 0.34, 0.26), 1), roughness=0.95)
pine_bark = make_material('bark_pine', *bark_textures('bark_pine', (0.13, 0.07, 0.045), (0.5, 0.33, 0.22), 2), roughness=0.95)
oak_leaves = make_material('leaves_oak', leaf_cluster_texture('leaves_oak', 3, (0.16, 0.3, 0.07)), roughness=0.75, alpha_clip=True)
birch_leaves = make_material('leaves_light', leaf_cluster_texture('leaves_light', 4, (0.3, 0.38, 0.08), leaf=(26, 14)), roughness=0.75, alpha_clip=True)
needles = make_material('needles', needle_texture('needles', 5), roughness=0.8, alpha_clip=True)
grass = make_material('grass', grass_texture('grass', 6), roughness=0.9, alpha_clip=True)
rock = make_material('rock', *rock_textures('rock', 7), roughness=0.92)

report = {}
for variant, (seed, leaves) in enumerate([(11, oak_leaves), (23, oak_leaves), (37, birch_leaves)]):
    for lod in (0, 1):
        obj = build_broadleaf(f'broadleaf_{variant}_lod{lod}', seed, [oak_bark, leaves], lod)
        report[obj.name] = len(obj.data.polygons)
for variant, seed in enumerate([51, 67]):
    for lod in (0, 1):
        obj = build_pine(f'pine_{variant}_lod{lod}', seed, [pine_bark, needles], lod)
        report[obj.name] = len(obj.data.polygons)
for variant in range(3):
    obj = build_rock(f'rock_{variant}', 100 + variant, rock)
    report[obj.name] = len(obj.data.polygons)
report['grass_0'] = len(build_grass('grass_0', 9, grass).data.polygons)

size = export_glb(os.path.abspath(OUTPUT))
print('ASSET_REPORT', report, 'bytes', size)
