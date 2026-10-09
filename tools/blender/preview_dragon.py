"""Renders several views of a rigged GLB (all meshes together) for visual review.

Run:  blender --background --python tools/blender/preview_dragon.py -- model.glb out_prefix view [view ...]
Views: side, three_quarter, front, top, head, head_front, wing, legs
"""
import math
import sys

import bpy
from mathutils import Vector

args = sys.argv[sys.argv.index('--') + 1:]
source, prefix, views = args[0], args[1], args[2:] or ['side']

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=source)
scene = bpy.context.scene
meshes = [o for o in scene.objects if o.type == 'MESH']
for obj in scene.objects:
    if obj.type == 'ARMATURE':
        obj.hide_viewport = False

lo = Vector((1e9, 1e9, 1e9))
hi = Vector((-1e9, -1e9, -1e9))
for obj in meshes:
    for corner in obj.bound_box:
        world = obj.matrix_world @ Vector(corner)
        lo = Vector((min(lo.x, world.x), min(lo.y, world.y), min(lo.z, world.z)))
        hi = Vector((max(hi.x, world.x), max(hi.y, world.y), max(hi.z, world.z)))
center = (lo + hi) / 2
size = hi - lo

# Locate the head by name of the head object, for close-ups.
head_objects = [o for o in meshes if o.name.endswith('_head')]
head_center = Vector((0, 0, 0))
head_size = 3.0
if head_objects:
    hlo = Vector((1e9, 1e9, 1e9))
    hhi = Vector((-1e9, -1e9, -1e9))
    for obj in head_objects:
        for corner in obj.bound_box:
            world = obj.matrix_world @ Vector(corner)
            hlo = Vector((min(hlo.x, world.x), min(hlo.y, world.y), min(hlo.z, world.z)))
            hhi = Vector((max(hhi.x, world.x), max(hhi.y, world.y), max(hhi.z, world.z)))
    head_center = (hlo + hhi) / 2
    head_size = max((hhi - hlo).x, (hhi - hlo).y, (hhi - hlo).z)

world = bpy.data.worlds.new('preview')
world.use_nodes = True
world.node_tree.nodes['Background'].inputs['Color'].default_value = (0.6, 0.72, 0.9, 1)
world.node_tree.nodes['Background'].inputs['Strength'].default_value = 1.0
scene.world = world
sun_data = bpy.data.lights.new('sun', 'SUN')
sun_data.energy = 4.5
sun = bpy.data.objects.new('sun', sun_data)
sun.rotation_euler = (math.radians(50), 0, math.radians(-30))
scene.collection.objects.link(sun)
ground_data = bpy.data.meshes.new('ground')
ground = bpy.data.objects.new('ground', ground_data)
ground_data.from_pydata([(-200, -200, lo.z), (200, -200, lo.z), (200, 200, lo.z), (-200, 200, lo.z)], [], [(0, 1, 2, 3)])
scene.collection.objects.link(ground)
gmat = bpy.data.materials.new('ground')
gmat.use_nodes = True
gmat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.35, 0.4, 0.22, 1)
ground_data.materials.append(gmat)

camera_data = bpy.data.cameras.new('camera')
camera = bpy.data.objects.new('camera', camera_data)
scene.collection.objects.link(camera)
scene.camera = camera
for engine in ('BLENDER_EEVEE', 'BLENDER_EEVEE_NEXT', 'BLENDER_WORKBENCH'):
    try:
        scene.render.engine = engine
        break
    except TypeError:
        continue
scene.render.resolution_x = 1280
scene.render.resolution_y = 720


def look(location, target, lens=40):
    camera_data.lens = lens
    camera.location = location
    camera.rotation_euler = (target - location).to_track_quat('-Z', 'Y').to_euler()


span = max(size.x, size.y, size.z)
for view in views:
    if view == 'side':
        look(center + Vector((span * 1.15, 0, span * 0.08)), center)
    elif view == 'three_quarter':
        look(center + Vector((span * 0.75, -span * 0.75, span * 0.28)), center)
    elif view == 'front':
        look(center + Vector((0, -span * 1.2, span * 0.12)), center)
    elif view == 'top':
        look(center + Vector((0, -0.01, span * 1.3)), center)
    elif view == 'head':
        look(head_center + Vector((head_size * 2.1, -head_size * 0.5, head_size * 0.15)), head_center, 50)
    elif view == 'head_front':
        look(head_center + Vector((head_size * 0.9, -head_size * 2.3, head_size * 0.35)), head_center, 50)
    elif view == 'wing':
        look(center + Vector((span * 0.4, -span * 0.9, span * 0.3)), center + Vector((span * 0.2, 0, 0)))
    elif view == 'legs':
        look(center + Vector((span * 0.5, span * 0.5, -span * 0.02)), center + Vector((0, 0, -size.z * 0.3)), 45)
    scene.render.filepath = f'{prefix}_{view}.png'
    bpy.ops.render.render(write_still=True)
print('PREVIEW_DONE', len(meshes), 'meshes', [round(v, 2) for v in size])
