"""Renders isometric 3D icons for every solid block, headless in Blender (Cycles, CPU):

    blender --background --python make_block_icons.py -- tiles.json <Resources/BlockRealm dir>

The icons use the same 16x16 tile images the world uses, shaded flat like the in-game faces (top bright, left
medium, right dark), so the inventory shows the block exactly as it will look when placed.
"""
import json
import math
import os
import sys

import bpy
from mathutils import Vector

SIZE = 64


def make_material(name, image_path, brightness):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new('ShaderNodeOutputMaterial')
    emission = nt.nodes.new('ShaderNodeEmission')
    transparent = nt.nodes.new('ShaderNodeBsdfTransparent')
    mix = nt.nodes.new('ShaderNodeMixShader')
    tex = nt.nodes.new('ShaderNodeTexImage')
    tex.image = bpy.data.images.load(image_path, check_existing=True)
    tex.image.colorspace_settings.name = 'sRGB'
    tex.interpolation = 'Closest'
    shade = nt.nodes.new('ShaderNodeMix')
    shade.data_type = 'RGBA'
    shade.blend_type = 'MULTIPLY'
    shade.inputs[0].default_value = 1.0
    shade.inputs[7].default_value = (brightness, brightness, brightness, 1.0)
    nt.links.new(tex.outputs['Color'], shade.inputs[6])
    nt.links.new(shade.outputs[2], emission.inputs['Color'])
    nt.links.new(tex.outputs['Alpha'], mix.inputs[0])
    nt.links.new(transparent.outputs[0], mix.inputs[1])
    nt.links.new(emission.outputs[0], mix.inputs[2])
    nt.links.new(mix.outputs[0], out.inputs['Surface'])
    return mat


def build_cube(top, side, bottom):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0, 0))
    obj = bpy.context.active_object
    mesh = obj.data
    mats = [
        ('top', top, 1.0), ('left', side, 0.80), ('right', side, 0.62), ('bottom', bottom, 0.5),
    ]
    for name, path, b in mats:
        mesh.materials.append(make_material('m_' + name, path, b))
    for poly in mesh.polygons:
        n = poly.normal
        if n.z > 0.5:
            poly.material_index = 0
        elif n.z < -0.5:
            poly.material_index = 3
        elif n.y < -0.5:          # faces the camera on its left
            poly.material_index = 1
        elif n.x > 0.5:           # faces the camera on its right
            poly.material_index = 2
        else:
            poly.material_index = 2
    return obj


def setup_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'CPU'
    scene.cycles.samples = 1
    scene.cycles.use_denoising = False
    scene.cycles.filter_width = 0.01
    scene.render.film_transparent = True
    scene.render.resolution_x = SIZE
    scene.render.resolution_y = SIZE
    scene.render.image_settings.file_format = 'PNG'
    scene.render.image_settings.color_mode = 'RGBA'
    scene.view_settings.view_transform = 'Standard'
    cam_data = bpy.data.cameras.new('cam')
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = 1.78
    cam = bpy.data.objects.new('cam', cam_data)
    scene.collection.objects.link(cam)
    cam.location = (6.0, -6.0, 6.0)
    target = bpy.data.objects.new('target', None)
    scene.collection.objects.link(target)
    con = cam.constraints.new('TRACK_TO')
    con.target = target
    con.track_axis = 'TRACK_NEGATIVE_Z'
    con.up_axis = 'UP_Y'
    scene.camera = cam
    return scene


def main():
    argv = sys.argv[sys.argv.index('--') + 1:]
    spec = json.load(open(argv[0]))
    root = argv[1]
    tiles = os.path.join(root, 'Tiles')
    out = os.path.join(root, 'BlockIcons')
    os.makedirs(out, exist_ok=True)
    done = 0
    for b in spec['blocks']:
        if b['render'] not in ('Cube', 'Cutout'):
            continue
        scene = setup_scene()
        top = os.path.join(tiles, b['top'] + '.png')
        side = os.path.join(tiles, b['side'] + '.png')
        bottom = os.path.join(tiles, b['bottom'] + '.png')
        build_cube(top, side, bottom)
        scene.render.filepath = os.path.join(out, 'block_%d.png' % b['id'])
        bpy.ops.render.render(write_still=True)
        done += 1
    print('block icons rendered: %d' % done)


main()
