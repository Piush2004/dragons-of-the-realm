"""Renders a contact sheet of every object in a GLB so models can be checked without opening Blender.

Run:  blender --background --python tools/blender/preview.py -- input.glb output.png [name_filter]
"""
import math
import sys

import bpy
from mathutils import Vector

args = sys.argv[sys.argv.index('--') + 1:]
source, output = args[0], args[1]
name_filter = args[2] if len(args) > 2 else ''

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=source)
scene = bpy.context.scene

objects = [o for o in scene.objects if o.type == 'MESH' and o.parent is None and name_filter in o.name]
for obj in [o for o in scene.objects if o.type == 'MESH' and o not in objects and o.parent is None]:
    obj.hide_render = True

# Lay the models out in a row with a gap proportional to their size.
cursor = 0.0
tallest = 0.0
heights = []
for obj in sorted(objects, key=lambda o: o.name):
    corners = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    width = max(c.x for c in corners) - min(c.x for c in corners)
    tallest = max(tallest, max(c.z for c in corners))
    heights.append(max(c.z for c in corners))
    obj.location.x += cursor + width / 2 - (max(c.x for c in corners) + min(c.x for c in corners)) / 2
    cursor += width * 1.15 + 0.5

world = bpy.data.worlds.new('preview')
world.use_nodes = True
world.node_tree.nodes['Background'].inputs['Color'].default_value = (0.55, 0.7, 0.9, 1)
world.node_tree.nodes['Background'].inputs['Strength'].default_value = 0.9
scene.world = world

sun_data = bpy.data.lights.new('sun', 'SUN')
sun_data.energy = 4.0
sun = bpy.data.objects.new('sun', sun_data)
sun.rotation_euler = (math.radians(52), 0, math.radians(35))
scene.collection.objects.link(sun)

camera_data = bpy.data.cameras.new('camera')
camera_data.lens = 50
camera = bpy.data.objects.new('camera', camera_data)
scene.collection.objects.link(camera)
scene.camera = camera
typical = sorted(heights)[len(heights) // 2]
distance = max(cursor * 0.8, typical * 2.6) + 4
camera.location = (cursor / 2 + distance * 0.25, -distance, typical * 0.5 + distance * 0.12)
direction = Vector((cursor / 2, 0, typical * 0.45)) - camera.location
camera.rotation_euler = direction.to_track_quat('-Z', 'Y').to_euler()

for engine in ('BLENDER_EEVEE', 'BLENDER_EEVEE_NEXT', 'BLENDER_WORKBENCH'):
    try:
        scene.render.engine = engine
        break
    except TypeError:
        continue
scene.render.resolution_x = 1400
scene.render.resolution_y = 600
scene.render.filepath = output
bpy.ops.render.render(write_still=True)
print('PREVIEW_DONE', scene.render.engine, len(objects))
