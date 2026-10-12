"""Re-exports the three Blender-built dragons (skinned meshes with armatures) as FBX for Unity:

    blender --background --python export_dragons_fbx.py -- <dragons dir with .glb> <Resources/BlockRealm dir>

The glTF files are the output of build_dragons.py. Skin textures are saved next to the FBX so Unity can assign them
explicitly, and the armature keeps its bone names (root, body, neck_*, tail_*, wing bones, legs) for the animator.
"""
import os
import sys

import bpy


def export(glb, out_dir, name):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=glb)

    tex_dir = os.path.join(out_dir, 'Models', 'Textures')
    os.makedirs(tex_dir, exist_ok=True)
    saved = []
    for img in bpy.data.images:
        if img.size[0] == 0 or img.users == 0:
            continue
        safe = ''.join(ch if ch.isalnum() else '_' for ch in img.name)
        path = os.path.join(tex_dir, '%s_%s.png' % (name, safe))
        img.filepath_raw = path
        img.file_format = 'PNG'
        img.save()
        saved.append(os.path.basename(path))

    bpy.ops.object.select_all(action='SELECT')
    fbx = os.path.join(out_dir, 'Models', 'dragon_%s.fbx' % name)
    bpy.ops.export_scene.fbx(
        filepath=fbx, use_selection=True, object_types={'ARMATURE', 'MESH', 'EMPTY'},
        add_leaf_bones=False, bake_anim=False, axis_forward='-Z', axis_up='Y',
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE', bake_space_transform=False,
        mesh_smooth_type='FACE', path_mode='AUTO')
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    arms = [o for o in bpy.context.scene.objects if o.type == 'ARMATURE']
    tris = sum(len(m.data.polygons) for m in meshes)
    print('dragon %s: %d meshes, %d armatures, %d bones, %d polygons, textures %s' % (
        name, len(meshes), len(arms), sum(len(a.data.bones) for a in arms), tris, saved))


def main():
    argv = sys.argv[sys.argv.index('--') + 1:]
    src, out = argv[0], argv[1]
    for name in ('cinderwing', 'brazenhide', 'elderscale'):
        export(os.path.join(src, name + '.glb'), out, name)


main()
