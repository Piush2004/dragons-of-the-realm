"""
Rigged humanoid for Emberbond: an armoured rider with a silver braid, cloak and sword.

Run:  blender --background --python make_characters.py -- <out_dir>
Writes rider.fbx: one skinned mesh (materials skin, cloth, leather, steel, hair, cloak, gold) on a 21-bone armature in
an A-pose, facing -Y in Blender (+Z in Unity after export). Unity animates the bones procedurally.
"""
import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = argv[0] if argv else '.'
os.makedirs(OUT, exist_ok=True)

MATS = ['skin', 'cloth', 'leather', 'steel', 'hair', 'cloak', 'gold']
COL = {
    'skin': (0.72, 0.56, 0.45), 'cloth': (0.10, 0.09, 0.09), 'leather': (0.25, 0.15, 0.09),
    'steel': (0.55, 0.57, 0.6), 'hair': (0.86, 0.85, 0.80), 'cloak': (0.05, 0.045, 0.045), 'gold': (0.7, 0.55, 0.2),
}

# bone name -> (head, tail, parent)
S = 1.0
BONES = {
    'hips': ((0, 0, 0.95), (0, 0, 1.12), None),
    'spine': ((0, 0, 1.12), (0, 0, 1.38), 'hips'),
    'chest': ((0, 0, 1.38), (0, 0, 1.55), 'spine'),
    'neck': ((0, 0, 1.55), (0, 0, 1.66), 'chest'),
    'head': ((0, 0, 1.66), (0, 0, 1.88), 'neck'),
}
for side, sx in (('L', 1), ('R', -1)):
    BONES['upper_arm_' + side] = ((sx * 0.2, 0, 1.48), (sx * 0.33, 0, 1.18), 'chest')
    BONES['lower_arm_' + side] = ((sx * 0.33, 0, 1.18), (sx * 0.38, 0, 0.93), 'upper_arm_' + side)
    BONES['hand_' + side] = ((sx * 0.38, 0, 0.93), (sx * 0.4, 0, 0.82), 'lower_arm_' + side)
    BONES['thigh_' + side] = ((sx * 0.1, 0, 0.95), (sx * 0.1, 0, 0.5), 'hips')
    BONES['shin_' + side] = ((sx * 0.1, 0, 0.5), (sx * 0.1, 0, 0.09), 'thigh_' + side)
    BONES['foot_' + side] = ((sx * 0.1, 0, 0.09), (sx * 0.1, -0.2, 0.04), 'shin_' + side)
BONES['braid'] = ((0, 0.1, 1.78), (0, 0.16, 1.2), 'head')
BONES['cloak_top'] = ((0, 0.14, 1.52), (0, 0.18, 1.0), 'chest')
BONES['cloak_low'] = ((0, 0.18, 1.0), (0, 0.22, 0.4), 'cloak_top')


class Mesh:
    def __init__(self):
        self.bm = bmesh.new()
        self.dl = self.bm.verts.layers.deform.verify()
        self.uv = self.bm.loops.layers.uv.verify()
        self.groups = {}

    def group(self, name):
        if name not in self.groups:
            self.groups[name] = len(self.groups)
        return self.groups[name]

    def _tag(self, verts, bone, mat_idx, faces):
        gi = self.group(bone)
        for v in verts:
            v[self.dl][gi] = 1.0
        for f in faces:
            f.material_index = mat_idx
            f.smooth = True

    def frustum(self, p0, p1, r0, r1, bone, mat, sides=12, cap=True, squash=1.0):
        p0, p1 = Vector(p0), Vector(p1)
        d = p1 - p0
        z = d.normalized()
        ref = Vector((0, 1, 0)) if abs(z.y) < 0.9 else Vector((1, 0, 0))
        x = z.cross(ref).normalized()
        y = z.cross(x)
        rings = []
        for pos, r in ((p0, r0), (p1, r1)):
            ring = []
            for i in range(sides):
                a = 2 * math.pi * i / sides
                ring.append(self.bm.verts.new(pos + x * math.cos(a) * r + y * math.sin(a) * r * squash))
            rings.append(ring)
        faces = []
        for i in range(sides):
            j = (i + 1) % sides
            faces.append(self.bm.faces.new((rings[0][i], rings[0][j], rings[1][j], rings[1][i])))
        if cap:
            faces.append(self.bm.faces.new(list(reversed(rings[0]))))
            faces.append(self.bm.faces.new(rings[1]))
        verts = rings[0] + rings[1]
        self._tag(verts, bone, MATS.index(mat), faces)
        return verts

    def sphere(self, c, r, bone, mat, scale=(1, 1, 1), seg=10):
        res = bmesh.ops.create_uvsphere(self.bm, u_segments=seg, v_segments=seg // 2 + 1, radius=r)
        verts = res['verts']
        for v in verts:
            v.co = Vector((v.co.x * scale[0], v.co.y * scale[1], v.co.z * scale[2])) + Vector(c)
        faces = list({f for v in verts for f in v.link_faces})
        self._tag(verts, bone, MATS.index(mat), faces)

    def box(self, lo, hi, bone, mat):
        x0, y0, z0 = lo
        x1, y1, z1 = hi
        v = [self.bm.verts.new(p) for p in
             [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0), (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]]
        idx = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (2, 3, 7, 6), (3, 0, 4, 7), (1, 2, 6, 5)]
        faces = []
        for a, b, c, d in idx:
            try:
                faces.append(self.bm.faces.new((v[a], v[b], v[c], v[d])))
            except ValueError:
                pass
        self._tag(v, bone, MATS.index(mat), faces)

    def cloth(self, bone_top, bone_low):
        """A hanging cloak: two separate grids (front and back faces) so it is visible from both sides."""
        cols, rows = 7, 9
        w = 0.34
        for side in (0, 1):
            grid = []
            for r in range(rows):
                t = r / (rows - 1)
                z = 1.52 - 1.15 * t
                y = 0.13 + 0.12 * t + side * 0.004
                wid = w * (0.8 + 0.5 * t)
                row = []
                for c in range(cols):
                    x = (c / (cols - 1) - 0.5) * 2 * wid
                    v = self.bm.verts.new((x, y + 0.02 * math.cos(c * 0.9), z))
                    bone = bone_top if t < 0.45 else bone_low
                    v[self.dl][self.group(bone)] = 1.0
                    row.append(v)
                grid.append(row)
            for r in range(rows - 1):
                for c in range(cols - 1):
                    q = (grid[r][c], grid[r][c + 1], grid[r + 1][c + 1], grid[r + 1][c])
                    f = self.bm.faces.new(q if side == 0 else tuple(reversed(q)))
                    f.material_index = MATS.index('cloak')
                    f.smooth = True


def build_mesh():
    m = Mesh()
    # Torso and armour
    m.frustum((0, 0, 0.95), (0, 0, 1.12), 0.17, 0.17, 'hips', 'cloth', squash=0.75)
    m.frustum((0, 0, 1.12), (0, 0, 1.38), 0.17, 0.2, 'spine', 'leather', squash=0.8)
    m.frustum((0, 0, 1.38), (0, 0, 1.55), 0.21, 0.19, 'chest', 'steel', squash=0.8)
    m.frustum((0, 0, 1.0), (0, 0, 1.06), 0.19, 0.19, 'hips', 'gold', squash=0.8)         # belt
    m.frustum((0, -0.02, 0.62), (0, -0.02, 0.97), 0.17, 0.14, 'hips', 'cloth', squash=0.35)  # tabard
    m.frustum((0, 0, 1.55), (0, 0, 1.67), 0.075, 0.065, 'neck', 'skin')
    # Head
    m.sphere((0, 0, 1.76), 0.105, 'head', 'skin', scale=(0.9, 1.0, 1.15))
    m.sphere((0, 0.035, 1.8), 0.112, 'head', 'hair', scale=(0.95, 1.0, 0.9))
    m.frustum((0, 0.1, 1.76), (0, 0.15, 1.22), 0.035, 0.03, 'braid', 'hair', sides=8)
    # Arms, shoulders
    for side, sx in (('L', 1), ('R', -1)):
        up, lo, ha = 'upper_arm_' + side, 'lower_arm_' + side, 'hand_' + side
        m.sphere((sx * 0.21, 0, 1.5), 0.085, 'chest', 'steel', scale=(1.1, 1.0, 0.8))                # pauldron
        m.frustum((sx * 0.2, 0, 1.48), (sx * 0.33, 0, 1.18), 0.065, 0.055, up, 'leather')
        m.sphere((sx * 0.33, 0, 1.18), 0.055, lo, 'steel')
        m.frustum((sx * 0.33, 0, 1.18), (sx * 0.38, 0, 0.93), 0.055, 0.045, lo, 'steel')
        m.sphere((sx * 0.39, 0, 0.86), 0.05, ha, 'skin')
        # legs
        th, sh, ft = 'thigh_' + side, 'shin_' + side, 'foot_' + side
        m.frustum((sx * 0.1, 0, 0.95), (sx * 0.1, 0, 0.5), 0.095, 0.075, th, 'cloth')
        m.sphere((sx * 0.1, 0, 0.5), 0.07, sh, 'leather')
        m.frustum((sx * 0.1, 0, 0.5), (sx * 0.1, 0, 0.09), 0.075, 0.06, sh, 'leather')
        m.box((sx * 0.1 - 0.065, -0.2, 0.0), (sx * 0.1 + 0.065, 0.06, 0.1), ft, 'leather')
    # Sword in the right hand: grip, guard, blade pointing forward (-Y) and down.
    m.frustum((-0.4, -0.02, 0.85), (-0.4, 0.03, 0.85), 0.02, 0.02, 'hand_R', 'leather', sides=8)
    m.box((-0.5, -0.03, 0.835), (-0.3, 0.03, 0.865), 'hand_R', 'gold')
    m.box((-0.415, -0.95, 0.838), (-0.385, -0.03, 0.862), 'hand_R', 'steel')
    m.cloth('cloak_top', 'cloak_low')
    return m


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    # Armature
    arm_data = bpy.data.armatures.new('rider_rig')
    arm_obj = bpy.data.objects.new('rider_rig', arm_data)
    bpy.context.scene.collection.objects.link(arm_obj)
    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.object.mode_set(mode='EDIT')
    eb = {}
    for name, (head, tail, parent) in BONES.items():
        b = arm_data.edit_bones.new(name)
        b.head = Vector(head)
        b.tail = Vector(tail)
        eb[name] = b
    for name, (head, tail, parent) in BONES.items():
        if parent:
            eb[name].parent = eb[parent]
            eb[name].use_connect = False
    bpy.ops.object.mode_set(mode='OBJECT')

    m = build_mesh()
    me = bpy.data.meshes.new('rider_body')
    m.bm.to_mesh(me)
    ob = bpy.data.objects.new('rider_body', me)
    bpy.context.scene.collection.objects.link(ob)
    for name in sorted(m.groups, key=lambda n: m.groups[n]):
        ob.vertex_groups.new(name=name)
    # BMesh deform layer indices follow creation order of groups, and so do the vertex groups.
    for name in MATS:
        mat = bpy.data.materials.new(name)
        mat.use_nodes = True
        mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (*COL[name], 1)
        ob.data.materials.append(mat)
    ob.parent = arm_obj
    mod = ob.modifiers.new('Armature', 'ARMATURE')
    mod.object = arm_obj

    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    arm_obj.select_set(True)
    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, 'rider.fbx'), use_selection=True, axis_forward='-Z', axis_up='Y',
                             apply_unit_scale=True, bake_space_transform=True, path_mode='STRIP',
                             add_leaf_bones=False, mesh_smooth_type='FACE', object_types={'ARMATURE', 'MESH'})
    print('rider done: verts', len(me.vertices), 'bones', len(BONES))


main()
