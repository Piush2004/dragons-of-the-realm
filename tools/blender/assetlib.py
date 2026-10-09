"""Shared helpers for the headless Blender asset scripts (Blender Z-up, meters)."""
import math
import os

import bpy
import numpy as np
from mathutils import Vector


# ----------------------------------------------------------------------------
# Procedural textures (numpy arrays are HxWxC float, row 0 = top of the image)
# ----------------------------------------------------------------------------

def tile_noise(size, cells_x, cells_y, rng):
    """Tileable smooth value noise in [0, 1]."""
    grid = rng.random((cells_y, cells_x))
    ys = np.arange(size) / size * cells_y
    xs = np.arange(size) / size * cells_x
    y0 = np.floor(ys).astype(int)
    x0 = np.floor(xs).astype(int)
    fy = ys - y0
    fx = xs - x0
    fy = fy * fy * (3 - 2 * fy)
    fx = fx * fx * (3 - 2 * fx)
    y1 = (y0 + 1) % cells_y
    x1 = (x0 + 1) % cells_x
    a = grid[np.ix_(y0, x0)]
    b = grid[np.ix_(y0, x1)]
    c = grid[np.ix_(y1, x0)]
    d = grid[np.ix_(y1, x1)]
    fxg = fx[None, :]
    fyg = fy[:, None]
    return a + (b - a) * fxg + (c - a) * fyg + (a - b - c + d) * fxg * fyg


def fbm(size, cells_x, cells_y, octaves, rng, gain=0.5):
    total = np.zeros((size, size))
    amplitude = 1.0
    norm = 0.0
    for _ in range(octaves):
        total += tile_noise(size, cells_x, cells_y, rng) * amplitude
        norm += amplitude
        amplitude *= gain
        cells_x *= 2
        cells_y *= 2
    return total / norm


def height_to_normal(height, strength):
    dx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * strength
    dy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * strength
    inv = 1.0 / np.sqrt(dx * dx + dy * dy + 1.0)
    out = np.ones(height.shape + (4,))
    out[..., 0] = -dx * inv * 0.5 + 0.5
    out[..., 1] = dy * inv * 0.5 + 0.5
    out[..., 2] = inv * 0.5 + 0.5
    return out


def colorize(value, dark, light):
    """Maps a 0..1 array onto a two-colour ramp, returns HxWx4 with alpha 1."""
    out = np.ones(value.shape + (4,))
    for channel in range(3):
        out[..., channel] = dark[channel] + (light[channel] - dark[channel]) * value
    return out


def draw_ellipse(image, cx, cy, radius_x, radius_y, angle, color, shade=0.0, rib=0.0):
    """Alpha-composites a rotated ellipse (pixels) onto an RGBA image. `shade` darkens toward one tip."""
    size = image.shape[0]
    reach = int(max(radius_x, radius_y)) + 2
    x_min, x_max = max(0, int(cx) - reach), min(size, int(cx) + reach + 1)
    y_min, y_max = max(0, int(cy) - reach), min(size, int(cy) + reach + 1)
    if x_min >= x_max or y_min >= y_max:
        return
    ys, xs = np.mgrid[y_min:y_max, x_min:x_max]
    dx = xs - cx
    dy = ys - cy
    cos_a, sin_a = math.cos(angle), math.sin(angle)
    u = (dx * cos_a + dy * sin_a) / radius_x
    v = (-dx * sin_a + dy * cos_a) / radius_y
    distance = u * u + v * v
    coverage = np.clip((1.0 - distance) * 6.0, 0.0, 1.0)
    tone = 1.0 - shade * (u * 0.5 + 0.5)
    if rib > 0:
        tone = tone + rib * np.exp(-(v * v) * 60.0)
    patch = image[y_min:y_max, x_min:x_max]
    for channel in range(3):
        patch[..., channel] = patch[..., channel] * (1 - coverage) + np.clip(color[channel] * tone, 0, 1) * coverage
    patch[..., 3] = np.maximum(patch[..., 3], coverage)


def save_image(name, array, directory, non_color=False):
    """Creates a Blender image from the array and saves it to disk so the glTF exporter can embed it."""
    height, width = array.shape[:2]
    image = bpy.data.images.new(name, width, height, alpha=True)
    image.pixels.foreach_set(np.flipud(np.clip(array, 0, 1)).astype(np.float32).ravel())
    os.makedirs(directory, exist_ok=True)
    image.filepath_raw = os.path.join(directory, name + '.png')
    image.file_format = 'PNG'
    image.save()
    if non_color:
        image.colorspace_settings.name = 'Non-Color'
    return image


def make_material(name, color_image=None, normal_image=None, roughness=0.9, metallic=0.0, alpha_clip=False, base_color=None):
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    tree = material.node_tree
    bsdf = next((node for node in tree.nodes if node.type == 'BSDF_PRINCIPLED'), None)
    if bsdf is None:
        bsdf = tree.nodes.new('ShaderNodeBsdfPrincipled')
        output = tree.nodes.new('ShaderNodeOutputMaterial')
        tree.links.new(bsdf.outputs['BSDF'], output.inputs['Surface'])
    bsdf.inputs['Roughness'].default_value = roughness
    bsdf.inputs['Metallic'].default_value = metallic
    if base_color is not None:
        bsdf.inputs['Base Color'].default_value = (*base_color, 1.0)
    if color_image is not None:
        texture = tree.nodes.new('ShaderNodeTexImage')
        texture.image = color_image
        tree.links.new(texture.outputs['Color'], bsdf.inputs['Base Color'])
        if alpha_clip:
            tree.links.new(texture.outputs['Alpha'], bsdf.inputs['Alpha'])
    if normal_image is not None:
        normal_texture = tree.nodes.new('ShaderNodeTexImage')
        normal_texture.image = normal_image
        normal_map = tree.nodes.new('ShaderNodeNormalMap')
        tree.links.new(normal_texture.outputs['Color'], normal_map.inputs['Color'])
        tree.links.new(normal_map.outputs['Normal'], bsdf.inputs['Normal'])
    if alpha_clip:
        material.use_backface_culling = False
        try:
            material.blend_method = 'CLIP'
        except Exception:
            pass
    return material


# ----------------------------------------------------------------------------
# Mesh building
# ----------------------------------------------------------------------------

class MeshBuilder:
    """Accumulates vertices, faces, UVs, per-vertex normals and material slots, then makes an object."""

    def __init__(self):
        self.vertices = []
        self.normals = []
        self.faces = []
        self.uvs = []
        self.material_indices = []

    def tube(self, points, radii, sides, material_index, uv_repeat=1.0, v_scale=0.5):
        """Sweeps a ring along a polyline. Returns nothing; rings are oriented by parallel transport."""
        points = [Vector(p) for p in points]
        first = (points[1] - points[0]).normalized()
        reference = Vector((1, 0, 0)) if abs(first.x) < 0.9 else Vector((0, 1, 0))
        side = first.cross(reference).normalized()
        ring_starts = []
        travelled = 0.0
        for i, point in enumerate(points):
            if i == 0:
                tangent = first
            elif i == len(points) - 1:
                tangent = (points[i] - points[i - 1]).normalized()
            else:
                tangent = (points[i + 1] - points[i - 1]).normalized()
            side = (side - tangent * side.dot(tangent)).normalized()
            up = tangent.cross(side)
            if i > 0:
                travelled += (points[i] - points[i - 1]).length
            ring_starts.append(len(self.vertices))
            # One extra vertex closes the UV seam.
            for s in range(sides + 1):
                angle = (s / sides) * math.tau
                radial = side * math.cos(angle) + up * math.sin(angle)
                self.vertices.append(tuple(point + radial * radii[i]))
                self.normals.append(tuple(radial))
            ring_starts[-1] = (ring_starts[-1], travelled)
        for i in range(len(points) - 1):
            start_a, v_a = ring_starts[i]
            start_b, v_b = ring_starts[i + 1]
            for s in range(sides):
                self.faces.append((start_a + s, start_a + s + 1, start_b + s + 1, start_b + s))
                u0 = s / sides * uv_repeat
                u1 = (s + 1) / sides * uv_repeat
                self.uvs.extend([(u0, v_a * v_scale), (u1, v_a * v_scale), (u1, v_b * v_scale), (u0, v_b * v_scale)])
                self.material_indices.append(material_index)

    def card(self, center, right, up, width, height, material_index, normal=None, uv=(0, 0, 1, 1), anchor_bottom=False):
        """Adds a single quad. `normal` overrides shading normals for all four corners (soft foliage lighting)."""
        center = Vector(center)
        right = Vector(right).normalized() * (width / 2)
        up = Vector(up).normalized() * height
        base = center if anchor_bottom else center - up / 2
        corners = [base - right, base + right, base + right + up, base - right + up]
        face_normal = Vector(right).cross(Vector(up)).normalized()
        start = len(self.vertices)
        for corner in corners:
            self.vertices.append(tuple(corner))
            if callable(normal):
                self.normals.append(tuple(normal(corner)))
            else:
                self.normals.append(tuple(normal if normal is not None else face_normal))
        self.faces.append((start, start + 1, start + 2, start + 3))
        u0, v0, u1, v1 = uv
        self.uvs.extend([(u0, v0), (u1, v0), (u1, v1), (u0, v1)])
        self.material_indices.append(material_index)

    def build(self, name, materials):
        mesh = bpy.data.meshes.new(name)
        mesh.from_pydata(self.vertices, [], self.faces)
        uv_layer = mesh.uv_layers.new(name='UVMap')
        uv_layer.data.foreach_set('uv', [value for uv in self.uvs for value in uv])
        mesh.polygons.foreach_set('material_index', self.material_indices)
        mesh.polygons.foreach_set('use_smooth', [True] * len(self.faces))
        for material in materials:
            mesh.materials.append(material)
        mesh.update()
        mesh.normals_split_custom_set_from_vertices(self.normals)
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        return obj


def export_glb(path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.gltf(
        filepath=path,
        export_format='GLB',
        export_yup=True,
        export_apply=False,
        export_normals=True,
        export_tangents=False,
        export_animations=False,
    )
    return os.path.getsize(path)


# ----------------------------------------------------------------------------
# Architecture kit (used by the landmark builders)
# ----------------------------------------------------------------------------

class Structure(MeshBuilder):
    """MeshBuilder plus flat-shaded quads, boxes and lathe solids with real-world-scaled UVs."""

    def quad(self, a, b, c, d, material, tile=3.0):
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

    def tri(self, a, b, c, material, tile=3.0):
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

    def box(self, lo, hi, material, tile=3.0, bottom=False):
        x0, y0, z0 = lo
        x1, y1, z1 = hi
        self.quad((x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1), material, tile)
        self.quad((x1, y1, z0), (x0, y1, z0), (x0, y1, z1), (x1, y1, z1), material, tile)
        self.quad((x1, y0, z0), (x1, y1, z0), (x1, y1, z1), (x1, y0, z1), material, tile)
        self.quad((x0, y1, z0), (x0, y0, z0), (x0, y0, z1), (x0, y1, z1), material, tile)
        self.quad((x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1), material, tile)
        if bottom:
            self.quad((x0, y1, z0), (x1, y1, z0), (x1, y0, z0), (x0, y0, z0), material, tile)

    def prism(self, center, radius, z0, z1, sides, material, tile=3.0, rotation=0.0, taper=1.0, cap=True):
        """Faceted column (a cylinder when sides is large). `taper` scales the top radius."""
        cx, cy = center
        for k in range(sides):
            a0 = rotation + math.tau * k / sides
            a1 = rotation + math.tau * (k + 1) / sides
            p0 = (cx + math.cos(a0) * radius, cy + math.sin(a0) * radius)
            p1 = (cx + math.cos(a1) * radius, cy + math.sin(a1) * radius)
            t0 = (cx + math.cos(a0) * radius * taper, cy + math.sin(a0) * radius * taper)
            t1 = (cx + math.cos(a1) * radius * taper, cy + math.sin(a1) * radius * taper)
            self.quad((p0[0], p0[1], z0), (p1[0], p1[1], z0), (t1[0], t1[1], z1), (t0[0], t0[1], z1), material, tile)
            if cap:
                self.tri((cx, cy, z1), (t0[0], t0[1], z1), (t1[0], t1[1], z1), material, tile)
                # Face order flipped so the cap faces up.
        return None

    def lathe(self, profile, sides, material, center=(0.0, 0.0), tile=3.0, noise_fn=None, z_fn=None):
        """Revolves (radius, height) points about the vertical axis. Smooth shading around the ring."""
        rings = []
        for r, z in profile:
            ring = []
            for k in range(sides):
                theta = math.tau * k / sides
                radius = r * (noise_fn(theta, z) if noise_fn else 1.0)
                height = z + (z_fn(theta, z) if z_fn else 0.0)
                ring.append((center[0] + math.cos(theta) * radius, center[1] + math.sin(theta) * radius, height))
            rings.append(ring)
        for i in range(len(profile) - 1):
            for k in range(sides):
                k1 = (k + 1) % sides
                a, b = rings[i][k], rings[i][k1]
                c, d = rings[i + 1][k1], rings[i + 1][k]
                start = len(self.vertices)
                for p in (a, b, c, d):
                    self.vertices.append(p)
                    radial = Vector((p[0] - center[0], p[1] - center[1], 0.0))
                    self.normals.append(tuple(radial.normalized() if radial.length > 1e-6 else Vector((0, 0, 1))))
                self.faces.append((start, start + 1, start + 2, start + 3))
                arc = (math.tau * profile[i][0] / sides) / tile
                self.uvs.extend([(k * arc, profile[i][1] / tile), ((k + 1) * arc, profile[i][1] / tile), ((k + 1) * arc, profile[i + 1][1] / tile), (k * arc, profile[i + 1][1] / tile)])
                self.material_indices.append(material)

    def cone(self, center, radius, z0, z1, sides, material, tile=3.0, bulge=0.0):
        """Pointed roof."""
        cx, cy = center
        profile = [(radius, z0), (radius * (0.62 + bulge), z0 + (z1 - z0) * 0.5), (radius * 0.28, z0 + (z1 - z0) * 0.82), (0.0, z1)]
        self.lathe(profile, sides, material, center, tile)
