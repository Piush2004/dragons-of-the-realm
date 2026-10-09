"""Geometry, texture and rig helpers for the dragon builder.

Authoring coordinates are (fwd, lat, up) in metres; P() converts to Blender (X right, -Y forward, Z up),
which the glTF exporter turns into the game's +Z-forward, +Y-up space.
"""
import math
import os

import bmesh
import bpy
import numpy as np
from mathutils import Vector

from assetlib import colorize, fbm, height_to_normal, save_image


def P(fwd, lat, up):
    return Vector((lat, -fwd, up))


def smoothstep(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def lerp(a, b, t):
    return a + (b - a) * t


def chain_weights(s, bones, tau):
    """Skin weights for a point at chain coordinate s. Bone i pivots at s = i-1; bones[0] covers s < 0."""
    ramps = [smoothstep(j - tau, j + tau, s) for j in range(len(bones) - 1)]
    weights = {}
    for i, name in enumerate(bones):
        high = 1.0 if i == 0 else ramps[i - 1]
        low = 0.0 if i == len(bones) - 1 else ramps[i]
        value = (1.0 - ramps[0]) if i == 0 else high - low
        if value > 1e-4:
            weights[name] = value
    return weights


def merge_weights(*groups):
    out = {}
    for group in groups:
        for name, value in group.items():
            out[name] = out.get(name, 0.0) + value
    return out


# ----------------------------------------------------------------------------- mesh part

class Part:
    """Accumulates one skinned object: vertices with bone weights, faces with UVs and materials."""

    def __init__(self, name):
        self.name = name
        self.verts = []
        self.weights = []
        self.faces = []
        self.face_uvs = []
        self.face_mats = []

    def vert(self, co, weights):
        self.verts.append(tuple(co))
        self.weights.append(weights)
        return len(self.verts) - 1

    def face(self, indices, uvs, material):
        self.faces.append(tuple(indices))
        self.face_uvs.append(uvs)
        self.face_mats.append(material)

    def build(self, materials, armature, relief=None):
        mesh = bpy.data.meshes.new(self.name)
        obj = bpy.data.objects.new(self.name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        for material in materials:
            mesh.materials.append(material)
        groups = {}
        for weights in self.weights:
            for name in weights:
                if name not in groups:
                    groups[name] = obj.vertex_groups.new(name=name)

        bm = bmesh.new()
        uv_layer = bm.loops.layers.uv.new('UVMap')
        deform = bm.verts.layers.deform.verify()
        verts = []
        for co, weights in zip(self.verts, self.weights):
            vert = bm.verts.new(co)
            for name, value in weights.items():
                vert[deform][groups[name].index] = value
            verts.append(vert)
        for indices, uvs, material in zip(self.faces, self.face_uvs, self.face_mats):
            try:
                face = bm.faces.new([verts[i] for i in indices])
            except ValueError:
                continue
            face.material_index = material
            face.smooth = True
            for loop, uv in zip(face.loops, uvs):
                loop[uv_layer].uv = uv
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        if relief:
            apply_relief(bm, relief)
        bm.to_mesh(mesh)
        bm.free()

        modifier = obj.modifiers.new('Armature', 'ARMATURE')
        modifier.object = armature
        obj.parent = armature
        return obj


# ----------------------------------------------------------------------------- relief

def apply_relief(bm, relief):
    """Subdivide skin geometry and push it along its normals with wrinkles and plate ridges.

    relief: dict(skin=(material indices), cuts=int, scale=float metres-ish size of the creature / 25,
    wrinkle=amplitude, plates=amplitude, plate_size=metres, seed=int)
    """
    skin = set(relief['skin'])
    size = relief.get('scale', 1.0)
    if relief.get('cuts', 0) > 0:
        # Only subdivide faces made of skin so horns, teeth and eyes keep their shape.
        edges = set()
        for face in bm.faces:
            if face.material_index in skin:
                edges.update(face.edges)
        bmesh.ops.subdivide_edges(bm, edges=list(edges), cuts=relief['cuts'], use_grid_fill=True, use_smooth_even=False)
    bm.verts.ensure_lookup_table()
    bm.normal_update()
    offset = Vector((relief.get('seed', 0) * 13.7, relief.get('seed', 0) * 7.3, 31.1))
    from mathutils import noise
    for vert in bm.verts:
        if not vert.link_faces or not all(f.material_index in skin for f in vert.link_faces):
            continue
        p = vert.co
        wrinkle = noise.fractal(p * (0.55 / size) + offset, 1.0, 2.0, 4) * relief.get('wrinkle', 0.05) * size
        # Ridged plates: the distance between the nearest two cell centres is small along cell borders.
        distances, _ = noise.voronoi(p * (1.0 / relief.get('plate_size', 0.35)) + offset)
        edge = distances[1] - distances[0]
        plate = (min(1.0, edge / 0.22) ** 0.8 - 1.0) * relief.get('plates', 0.03) * size
        vert.co = p + vert.normal * (wrinkle + plate)


# ----------------------------------------------------------------------------- primitives

def sweep(part, centers, sizes, sides, ring_weights, material, tile=1.0, p=1.0, belly_flat=1.0,
          belly_material=None, belly_half_angle=0.55, cap_end=True, cap_start=False, v_offset=0.0, twist_up=None,
          top_material=None, top_half_angle=0.75):
    """Lofts an elliptical ring (half width, half height) along a polyline. Returns ring frames for decoration."""
    count = len(centers)
    tangents = []
    for i in range(count):
        a = centers[max(0, i - 1)]
        b = centers[min(count - 1, i + 1)]
        tangents.append((b - a).normalized())
    reference = twist_up if twist_up is not None else Vector((0, 0, 1))
    side = tangents[0].cross(reference)
    if side.length < 1e-4:
        side = tangents[0].cross(Vector((0, 1, 0)))
    side.normalize()
    frames = []
    for i in range(count):
        t = tangents[i]
        side = (side - t * side.dot(t))
        side.normalize()
        up = side.cross(t).normalized()
        # Keep +up pointing away from -Z so the dorsal line stays on top.
        if up.z < 0 and abs(t.z) < 0.9:
            up = -up
            side = -side
        frames.append((side.copy(), up.copy()))

    ring_index = []
    arc = v_offset
    for i in range(count):
        if i > 0:
            arc += (centers[i] - centers[i - 1]).length
        w, h = sizes[i]
        side_vec, up_vec = frames[i]
        indices = []
        for k in range(sides):
            theta = math.tau * k / sides
            c, s = math.cos(theta), math.sin(theta)
            lateral = w * math.copysign(abs(s) ** p, s)
            vertical = h * math.copysign(abs(c) ** p, c)
            if vertical < 0:
                vertical *= belly_flat
            indices.append(part.vert(centers[i] + side_vec * lateral + up_vec * vertical, ring_weights[i]))
        circumference = math.tau * (w + h) / 2
        ring_index.append((indices, arc / tile, max(1.0, circumference / tile)))

    for i in range(count - 1):
        a, v_a, r_a = ring_index[i]
        b, v_b, r_b = ring_index[i + 1]
        for k in range(sides):
            k1 = (k + 1) % sides
            theta_mid = math.tau * (k + 0.5) / sides
            belly = belly_material is not None and abs(theta_mid - math.pi) < belly_half_angle
            uvs = [(k / sides * r_a, v_a), ((k + 1) / sides * r_a, v_a), ((k + 1) / sides * r_b, v_b), (k / sides * r_b, v_b)]
            on_top = top_material is not None and min(theta_mid, math.tau - theta_mid) < top_half_angle
            part.face((a[k], a[k1], b[k1], b[k]), uvs, belly_material if belly else (top_material if on_top else material))

    def fan(ring, at, flip):
        indices, v, r = ring
        apex = part.vert(centers[at], ring_weights[at])
        for k in range(sides):
            k1 = (k + 1) % sides
            tri = (indices[k1], indices[k], apex) if flip else (indices[k], indices[k1], apex)
            part.face(tri, [(0.0, v), (0.0, v), (0.0, v)], material)

    if cap_end:
        fan(ring_index[-1], count - 1, False)
    if cap_start:
        fan(ring_index[0], 0, True)
    return frames


def tube(part, points, radii, sides, weights_fn, material, tile=0.6):
    """Circular tube along a polyline; weights_fn(i) gives the bone weights of ring i."""
    sizes = [(r, r) for r in radii]
    return sweep(part, points, sizes, sides, [weights_fn(i) for i in range(len(points))], material, tile=tile)


def cone(part, base, tip, radius, sides, weights, material, tilt=None):
    """Closed cone from `base` to `tip`."""
    axis = (tip - base)
    axis_n = axis.normalized()
    reference = Vector((0, 0, 1)) if abs(axis_n.z) < 0.9 else Vector((1, 0, 0))
    u = axis_n.cross(reference).normalized()
    v = axis_n.cross(u).normalized()
    ring = []
    for k in range(sides):
        theta = math.tau * k / sides
        ring.append(part.vert(base + (u * math.cos(theta) + v * math.sin(theta)) * radius, weights))
    apex = part.vert(tip, weights)
    centre = part.vert(base, weights)
    for k in range(sides):
        k1 = (k + 1) % sides
        part.face((ring[k], ring[k1], apex), [(k / sides, 0), ((k + 1) / sides, 0), (0.5, 1)], material)
        part.face((ring[k1], ring[k], centre), [(0, 0), (0, 0), (0, 0)], material)


def curved_spike(part, base, direction, length, radius, bend_axis, bend, weights, material, sides=6, steps=7):
    """A tapered, curved horn/claw: starts at `base`, heads along `direction`, bending about `bend_axis`."""
    points = [Vector(base)]
    heading = Vector(direction).normalized()
    step = length / steps
    radii = []
    for i in range(steps + 1):
        t = i / steps
        radii.append(max(0.004, radius * (1 - t) ** 0.85))
        if i < steps:
            points.append(points[-1] + heading * step)
            rotation = Vector(bend_axis).normalized()
            angle = bend / steps
            heading = (heading * math.cos(angle) + rotation.cross(heading) * math.sin(angle) +
                       rotation * rotation.dot(heading) * (1 - math.cos(angle))).normalized()
    points = points[: len(radii)]
    sizes = [(r, r) for r in radii]
    sweep(part, points, sizes, sides, [weights] * len(points), material, tile=max(0.2, length * 0.8))


def ellipsoid(part, center, radii, weights, material, rings=10, segments=14, axes=None):
    """Ellipsoid in authoring axes; `axes` optionally gives (x_axis, y_axis, z_axis) unit vectors."""
    ax, ay, az = axes if axes else (Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1)))
    grid = []
    for i in range(rings + 1):
        phi = math.pi * i / rings
        row = []
        for k in range(segments):
            theta = math.tau * k / segments
            local = (radii[0] * math.sin(phi) * math.cos(theta), radii[1] * math.sin(phi) * math.sin(theta), radii[2] * math.cos(phi))
            co = center + ax * local[0] + ay * local[1] + az * local[2]
            row.append(part.vert(co, weights))
        grid.append(row)
    for i in range(rings):
        for k in range(segments):
            k1 = (k + 1) % segments
            part.face((grid[i][k], grid[i][k1], grid[i + 1][k1], grid[i + 1][k]),
                      [(k / segments, i / rings), ((k + 1) / segments, i / rings), ((k + 1) / segments, (i + 1) / rings), (k / segments, (i + 1) / rings)],
                      material)


def eye_disc(part, center, outward, radius, weights, material):
    """A bulging eyeball whose UVs are projected along `outward`, so an iris texture faces the viewer."""
    outward = outward.normalized()
    up = Vector((0, 0, 1))
    right = outward.cross(up).normalized()
    up = right.cross(outward).normalized()
    rings, segments = 8, 14
    grid = []
    for i in range(rings + 1):
        phi = (math.pi / 2) * i / rings
        row = []
        for k in range(segments):
            theta = math.tau * k / segments
            local = right * (math.sin(phi) * math.cos(theta)) + up * (math.sin(phi) * math.sin(theta)) + outward * math.cos(phi)
            row.append((part.vert(center + local * radius, weights), 0.5 + 0.5 * math.sin(phi) * math.cos(theta), 0.5 + 0.5 * math.sin(phi) * math.sin(theta)))
        grid.append(row)
    for i in range(rings):
        for k in range(segments):
            k1 = (k + 1) % segments
            a, b, c, d = grid[i][k], grid[i][k1], grid[i + 1][k1], grid[i + 1][k]
            part.face((a[0], b[0], c[0], d[0]), [(a[1], a[2]), (b[1], b[2]), (c[1], c[2]), (d[1], d[2])], material)


# ----------------------------------------------------------------------------- textures

def voronoi(size, cells, rng, stretch=(1.0, 1.0)):
    """Periodic Voronoi: returns (F2-F1, per-cell random id, offset from the cell centre in cell units)."""
    jitter = rng.random((cells, cells, 2))
    ids = rng.random((cells, cells))
    ys, xs = np.mgrid[0:size, 0:size]
    xs = (xs + 0.5) * cells / size
    ys = (ys + 0.5) * cells / size
    base_x = np.floor(xs).astype(int)
    base_y = np.floor(ys).astype(int)
    f1 = np.full((size, size), 9.0)
    f2 = np.full((size, size), 9.0)
    ident = np.zeros((size, size))
    off_x = np.zeros((size, size))
    off_y = np.zeros((size, size))
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            cx = base_x + dx
            cy = base_y + dy
            px = cx + jitter[cy % cells, cx % cells, 0]
            py = cy + jitter[cy % cells, cx % cells, 1]
            d = np.sqrt(((xs - px) * stretch[0]) ** 2 + ((ys - py) * stretch[1]) ** 2)
            closer = d < f1
            second = (~closer) & (d < f2)
            f2 = np.where(closer, f1, np.where(second, d, f2))
            ident = np.where(closer, ids[cy % cells, cx % cells], ident)
            off_x = np.where(closer, xs - px, off_x)
            off_y = np.where(closer, ys - py, off_y)
            f1 = np.where(closer, d, f1)
    return f2 - f1, ident, off_x, off_y


def smooth01(x, a, b):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def skin_textures(name, seed, palette, directory, size=1024, cells=30, wear=0.0):
    """Overlapping reptile scales: colour + normal map. U wraps around the body, V runs along it."""
    rng = np.random.default_rng(seed)
    edge, ident, ox, oy = voronoi(size, cells, rng, stretch=(1.0, 0.72))
    plate = smooth01(edge, 0.0, 0.34) ** 0.7
    # Each scale rises toward its rear edge, as if overlapping the next one.
    height = np.clip(plate * 0.8 + (oy * 0.5 + 0.5) * 0.25 * plate, 0, 1)
    height = np.clip(height - smooth01(fbm(size, 9, 9, 4, rng), 0.55, 0.9) * wear * 0.4, 0, 1)
    broad = fbm(size, 5, 5, 4, rng)
    fine = fbm(size, 90, 90, 2, rng)
    tone = np.clip(ident * 0.55 + broad * 0.45, 0, 1)
    color = colorize(tone, palette['dark'], palette['light'])
    groove = np.array(palette['groove'])
    for c in range(3):
        shaded = color[..., c] * (0.78 + 0.22 * plate) * (0.9 + fine * 0.2)
        # Sun-bleached, worn patches on the weathered dragon.
        shaded = shaded * (1 - wear * 0.35 * smooth01(broad, 0.5, 0.8)) + wear * 0.12 * smooth01(broad, 0.5, 0.8)
        color[..., c] = shaded * plate + groove[c] * (1 - plate)
    return (save_image(name + '_color', color, directory),
            save_image(name + '_normal', height_to_normal(height + fine * 0.05, 5.5), directory, True))


def belly_textures(name, seed, palette, directory, size=512):
    """Wide overlapping belly plates running across the body."""
    rng = np.random.default_rng(seed)
    ys = np.arange(size)[:, None].repeat(size, 1) / size
    xs = np.arange(size)[None, :].repeat(size, 0) / size
    plates = 10
    f = (ys * plates) % 1.0
    row = np.floor(ys * plates).astype(int)
    per_plate = rng.random(plates)[row % plates]
    edge = smooth01(f, 0.0, 0.18) * (1 - smooth01(f, 0.82, 1.0) * 0.55)
    height = edge * (0.7 + 0.3 * f)
    tone = np.clip(0.35 + per_plate * 0.35 + fbm(size, 6, 6, 3, rng) * 0.35, 0, 1)
    color = colorize(tone, palette['belly_dark'], palette['belly_light'])
    for c in range(3):
        color[..., c] = color[..., c] * (0.6 + 0.4 * edge)
    return (save_image(name + '_belly_color', color, directory),
            save_image(name + '_belly_normal', height_to_normal(height + xs * 0.0, 4.0), directory, True))


def membrane_texture(name, seed, palette, directory, size=512):
    """Wing skin. U runs between two finger bones (0..1), V runs from the wrist (0) to the trailing edge (1)."""
    rng = np.random.default_rng(seed)
    us = np.arange(size)[None, :].repeat(size, 0) / size
    vs = np.arange(size)[:, None].repeat(size, 1) / size
    mottle = fbm(size, 4, 4, 3, rng)
    fine = fbm(size, 60, 60, 2, rng)
    # Darker toward the finger bones on both sides, a little lighter and thinner mid-panel.
    near_bone = np.exp(-(np.minimum(us, 1 - us) * 8.0) ** 2)
    # A few branching veins running from the wrist toward the trailing edge.
    vein_field = fbm(size, 7, 3, 3, rng)
    veins = np.exp(-((np.sin((us * 4 + vein_field * 2.2) * math.pi)) * 7.0) ** 2) * 0.5
    tone = np.clip(0.5 + (mottle - 0.5) * 0.35 + (fine - 0.5) * 0.12 - near_bone * 0.32 - veins * 0.12 + (1 - vs) * 0.06, 0, 1)
    color = colorize(tone, palette['membrane_dark'], palette['membrane_light'])
    height = near_bone * 0.5 + veins * 0.3 + fine * 0.05
    return (save_image(name + '_membrane_color', color, directory),
            save_image(name + '_membrane_normal', height_to_normal(height, 2.0), directory, True))


def eye_texture(name, palette, directory, size=128):
    ys, xs = np.mgrid[0:size, 0:size]
    x = (xs / size - 0.5) * 2
    y = (ys / size - 0.5) * 2
    r = np.sqrt(x * x + y * y)
    iris = np.clip(1 - r * 1.05, 0, 1)
    base = np.array(palette['eye'])
    color = np.ones((size, size, 4))
    for c in range(3):
        color[..., c] = (base[c] * (0.55 + 0.9 * iris) * (0.85 + 0.3 * np.sin(np.arctan2(y, x) * 14) ** 2)).clip(0, 1)
    slit = np.abs(x) < (0.11 * (1 - np.abs(y) ** 2) + 0.01)
    color[slit] = (0.02, 0.01, 0.01, 1)
    color[r > 0.98] = (0.05, 0.03, 0.02, 1)
    return save_image(name + '_eye', color, directory)


# ----------------------------------------------------------------------------- rig

def make_armature(name, bones):
    """bones: list of (name, parent, head Vector, tail Vector). Returns the armature object."""
    data = bpy.data.armatures.new(name)
    obj = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode='EDIT')
    edit = {}
    for bone_name, parent, head, tail in bones:
        if (tail - head).length < 0.05:
            tail = head + Vector((0, 0, 0.05))
        bone = data.edit_bones.new(bone_name)
        bone.head = head
        bone.tail = tail
        bone.roll = 0.0
        edit[bone_name] = bone
    for bone_name, parent, _, _ in bones:
        if parent:
            edit[bone_name].parent = edit[parent]
    bpy.ops.object.mode_set(mode='OBJECT')
    return obj


def rig_metadata(bones):
    """Rest head position and direction per bone in the game's Y-up space (x, z, -y)."""
    def convert(v):
        return [round(v.x, 4), round(v.z, 4), round(-v.y, 4)]
    out = {}
    for name, parent, head, tail in bones:
        direction = (tail - head)
        direction = direction.normalized() if direction.length > 1e-6 else Vector((0, 0, 1))
        out[name] = {'parent': parent, 'head': convert(head), 'dir': convert(direction)}
    return out
