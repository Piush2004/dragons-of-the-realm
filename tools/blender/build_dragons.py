"""Builds the three rigged dragons (Caraxes, Vermithor, Vhagar) and exports one GLB each.

Run:  blender --background --python tools/blender/build_dragons.py -- public/assets/dragons [name ...]
"""
import json
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Vector

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from assetlib import make_material  # noqa: E402
from dragonlib import (  # noqa: E402
    P, Part, belly_textures, chain_weights, cone, curved_spike, ellipsoid, eye_disc, eye_texture, lerp,
    make_armature, membrane_texture, merge_weights, rig_metadata, skin_textures, smoothstep, sweep, tube,
)

ARGS = sys.argv[sys.argv.index('--') + 1:]
OUT_DIR = os.path.abspath(ARGS[0])
ONLY = set(ARGS[1:])
TEXTURE_DIR = os.path.join(os.path.dirname(OUT_DIR), '..', 'build', 'textures')

SKIN, BELLY, MOUTH, HORN, TOOTH, EYE, MEMBRANE, BACK = range(8)


# ------------------------------------------------------------------------------------------------ specs

def fingers(lengths, fans, droop):
    return {'lengths': lengths, 'fans': fans, 'droop': droop}


CARAXES = dict(
    name='caraxes', seed=11, tex_cells=34,
    torso_len=5.6, hip=(0.78, 0.92), waist=(0.72, 0.88), chest=(0.98, 1.22), chest_lift=0.3,
    neck=dict(len=7.4, n=10, base=(0.7, 0.82), mid=(0.36, 0.4), end=(0.27, 0.3), a0=38, a1=-30),
    tail=dict(len=10.5, m=14, base=(0.62, 0.72), tip=0.03),
    head=dict(length=1.9, skull_w=0.36, skull_h=0.36, snout=0.3, nose=0.06, jaw=0.62, pitch=-12, teeth=0.085,
              brow=0.1, wide_cheek=0.09,
              horns=[
                  # (t, y, z, dir, length, radius, bend, count_mirror)
                  dict(t=0.2, y=0.45, z=0.7, d=(-0.7, 0.55, 0.45), length=1.3, r=0.095, bend=-0.5),
                  dict(t=0.3, y=0.7, z=0.45, d=(-0.5, 0.85, 0.25), length=0.72, r=0.055, bend=-0.6),
                  dict(t=0.1, y=0.9, z=0.0, d=(-0.8, 0.5, -0.1), length=0.5, r=0.045, bend=-0.4),
              ],
              crest=7, crest_len=0.16, beard=11, beard_len=0.2),
    wing=dict(arm=3.4, fore=4.2, thick=0.22, fingers=fingers([8.4, 7.9, 6.9, 5.4], [-6, 26, 54, 78], 0.3), scallop=0.2, sag=0.9),
    leg=dict(thigh=1.8, shin=2.0, foot=1.3, thick=0.62),
    dorsal=dict(step=0.62, size=0.34, tail_size=0.2),
    palette=dict(dark=(0.12, 0.018, 0.016), light=(0.55, 0.085, 0.06), groove=(0.035, 0.007, 0.007),
                 belly_dark=(0.3, 0.09, 0.05), belly_light=(0.72, 0.42, 0.24),
                 back_dark=(0.03, 0.006, 0.006), back_light=(0.2, 0.03, 0.025),
                 membrane_dark=(0.13, 0.016, 0.013), membrane_light=(0.5, 0.075, 0.045),
                 horn=(0.025, 0.017, 0.015), eye=(0.95, 0.72, 0.12), metallic=0.12, roughness=0.62, wear=0.0),
)

VERMITHOR = dict(
    name='vermithor', seed=23, tex_cells=28,
    torso_len=7.2, hip=(1.5, 1.6), waist=(1.55, 1.7), chest=(1.85, 2.1), chest_lift=0.3, back_band=1.0,
    neck=dict(len=5.0, n=9, base=(1.4, 1.55), mid=(1.0, 1.08), end=(0.8, 0.86), a0=34, a1=-24),
    tail=dict(len=9.2, m=12, base=(1.3, 1.4), tip=0.06),
    head=dict(length=3.1, skull_w=0.84, skull_h=0.78, snout=0.7, nose=0.1, jaw=0.7, pitch=-8, teeth=0.15,
              brow=0.16, wide_cheek=0.16,
              horns=[
                  dict(t=0.18, y=0.55, z=0.75, d=(-0.55, 0.55, 0.62), length=1.9, r=0.17, bend=-0.9),
                  dict(t=0.3, y=0.85, z=0.5, d=(-0.35, 0.95, 0.3), length=1.1, r=0.12, bend=-0.8),
                  dict(t=0.1, y=0.92, z=0.1, d=(-0.7, 0.7, -0.1), length=0.85, r=0.1, bend=-0.5),
              ],
              crest=9, crest_len=0.3, beard=7, beard_len=0.3),
    wing=dict(arm=3.8, fore=4.6, thick=0.3, fingers=fingers([9.0, 8.4, 7.4, 5.8], [-8, 24, 52, 78], 0.12), scallop=0.16, sag=0.55),
    leg=dict(thigh=2.5, shin=2.7, foot=1.8, thick=1.0),
    dorsal=dict(step=0.8, size=0.5, tail_size=0.34),
    palette=dict(dark=(0.16, 0.07, 0.025), light=(0.78, 0.4, 0.13), groove=(0.045, 0.02, 0.012),
                 belly_dark=(0.4, 0.25, 0.1), belly_light=(0.85, 0.62, 0.34),
                 back_dark=(0.02, 0.018, 0.017), back_light=(0.2, 0.17, 0.15),
                 membrane_dark=(0.28, 0.1, 0.05), membrane_light=(0.8, 0.45, 0.2),
                 horn=(0.035, 0.026, 0.02), eye=(1.0, 0.55, 0.08), metallic=0.38, roughness=0.5, wear=0.15),
)

VHAGAR = dict(
    name='vhagar', seed=37, tex_cells=24,
    torso_len=9.2, hip=(2.0, 2.1), waist=(2.1, 2.25), chest=(2.4, 2.65), chest_lift=0.25, back_band=0.9,
    neck=dict(len=5.6, n=9, base=(1.9, 2.05), mid=(1.4, 1.5), end=(1.1, 1.18), a0=26, a1=-22),
    tail=dict(len=12.0, m=12, base=(1.75, 1.85), tip=0.08),
    head=dict(length=3.9, skull_w=1.15, skull_h=1.0, snout=0.95, nose=0.03, jaw=0.75, pitch=-10, teeth=0.2,
              brow=0.1, wide_cheek=0.1,
              horns=[
                  dict(t=0.2, y=0.5, z=0.8, d=(-0.75, 0.4, 0.5), length=1.0, r=0.26, bend=-0.4),
                  dict(t=0.1, y=0.9, z=0.15, d=(-0.8, 0.55, -0.1), length=0.8, r=0.2, bend=-0.3),
              ],
              crest=8, crest_len=0.2, beard=6, beard_len=0.2),
    wing=dict(arm=4.5, fore=5.5, thick=0.38, fingers=fingers([11.2, 10.4, 9.0, 7.0], [-6, 26, 54, 80], 0.1), scallop=0.14, sag=0.6),
    leg=dict(thigh=3.0, shin=3.2, foot=2.2, thick=1.3),
    dorsal=dict(step=1.1, size=0.3, tail_size=0.22),
    palette=dict(dark=(0.07, 0.085, 0.06), light=(0.45, 0.5, 0.38), groove=(0.025, 0.03, 0.02),
                 belly_dark=(0.38, 0.36, 0.26), belly_light=(0.78, 0.74, 0.6),
                 back_dark=(0.03, 0.04, 0.03), back_light=(0.22, 0.26, 0.2),
                 membrane_dark=(0.07, 0.085, 0.07), membrane_light=(0.34, 0.4, 0.34),
                 horn=(0.18, 0.17, 0.14), eye=(0.82, 0.78, 0.25), metallic=0.05, roughness=0.78, wear=0.65),
)

SPECS = {s['name']: s for s in (CARAXES, VERMITHOR, VHAGAR)}


# ------------------------------------------------------------------------------------------------ builder

def interp(t, knots, values):
    return float(np.interp(t, knots, values))


def make_materials(spec, tex_dir):
    pal = spec['palette']
    name = spec['name']
    skin_c, skin_n = skin_textures(name + '_skin', spec['seed'], pal, tex_dir, cells=spec['tex_cells'], wear=pal['wear'])
    belly_c, belly_n = belly_textures(name, spec['seed'] + 1, pal, tex_dir)
    mem_c, mem_n = membrane_texture(name, spec['seed'] + 2, pal, tex_dir)
    eye_c = eye_texture(name, pal, tex_dir)
    back_pal = dict(pal, dark=pal['back_dark'], light=pal['back_light'], groove=tuple(c * 0.5 for c in pal['back_dark']))
    back_c, back_n = skin_textures(name + '_back', spec['seed'] + 5, back_pal, tex_dir, cells=spec['tex_cells'], wear=pal['wear'])

    def mat(label, color=None, normal=None, rough=0.7, metal=0.0, base=None, backface=True):
        m = make_material(f'{name}_{label}', color, normal, roughness=rough, metallic=metal, base_color=base)
        m.use_backface_culling = backface
        return m

    return [
        mat('skin', skin_c, skin_n, pal['roughness'], pal['metallic']),
        mat('belly', belly_c, belly_n, 0.65),
        mat('mouth', base=(0.28, 0.04, 0.04), rough=0.35),
        mat('horn', base=pal['horn'], rough=0.45),
        mat('tooth', base=(0.85, 0.8, 0.68), rough=0.4),
        mat('eye', eye_c, rough=0.12),
        mat('membrane', mem_c, mem_n, 0.6, backface=False),
        mat('back', back_c, back_n, pal['roughness'], pal['metallic']),
    ]


def build_dragon(spec, materials):
    name = spec['name']
    L = spec['torso_len']
    neck_spec, tail_spec, head_spec, wing_spec, leg_spec = spec['neck'], spec['tail'], spec['head'], spec['wing'], spec['leg']
    n_neck, m_tail = neck_spec['n'], tail_spec['m']
    neck_seg = neck_spec['len'] / n_neck
    tail_seg = tail_spec['len'] / m_tail

    neck_bones = ['body'] + [f'neck_{i}' for i in range(n_neck)] + ['head']
    tail_bones = ['body'] + [f'tail_{i}' for i in range(m_tail)]

    # ---- spine paths in the (fwd, up) plane
    hip2 = np.array([-L / 2, 0.0])
    chest2 = np.array([L / 2, spec['chest_lift']])
    rings_per_neck_seg, rings_per_tail_seg = 4, 3

    neck_pts = [chest2.copy()]
    n_steps = n_neck * rings_per_neck_seg
    for i in range(n_steps):
        t = (i + 0.5) / n_steps
        angle = math.radians(lerp(neck_spec['a0'] * (1 - t) ** 1.2, neck_spec['a1'] * t ** 1.4, t) + 0)
        angle = math.radians(neck_spec['a0'] * (1 - t) ** 1.2 + neck_spec['a1'] * t ** 1.4)
        neck_pts.append(neck_pts[-1] + np.array([math.cos(angle), math.sin(angle)]) * (neck_spec['len'] / n_steps))
    neck_end_angle = math.atan2(neck_pts[-1][1] - neck_pts[-2][1], neck_pts[-1][0] - neck_pts[-2][0])

    tail_pts = [hip2.copy()]
    t_steps = m_tail * rings_per_tail_seg
    for i in range(t_steps):
        t = (i + 0.5) / t_steps
        angle = math.radians(-6 + 22 * t ** 2 - 14 * math.sin(math.pi * t))
        tail_pts.append(tail_pts[-1] + np.array([-math.cos(angle), math.sin(angle)]) * (tail_spec['len'] / t_steps))

    n_torso = max(8, int(L / 0.45))
    torso_pts = []
    for i in range(n_torso + 1):
        t = i / n_torso
        up = chest2[1] * t + 0.12 * spec['chest'][1] * math.sin(math.pi * t)
        torso_pts.append(np.array([lerp(hip2[0], chest2[0], t), up]))

    def torso_sizes(t):
        ks = [0, 0.3, 0.72, 1.0]
        w = interp(t, ks, [spec['hip'][0], spec['waist'][0], spec['chest'][0], spec['chest'][0] * 0.93])
        h = interp(t, ks, [spec['hip'][1], spec['waist'][1], spec['chest'][1], spec['chest'][1] * 0.93])
        return w, h

    def neck_sizes(t):
        ks = [0, 0.35, 1.0]
        w = interp(t, ks, [neck_spec['base'][0] * 0.93, neck_spec['mid'][0], neck_spec['end'][0]])
        h = interp(t, ks, [neck_spec['base'][1] * 0.93, neck_spec['mid'][1], neck_spec['end'][1]])
        return w, h

    def tail_sizes(t):
        w = lerp(tail_spec['base'][0], tail_spec['tip'], t ** 0.78) * (1 + 0.1 * math.sin(math.pi * min(1, t * 3)) * (1 - t))
        h = lerp(tail_spec['base'][1], tail_spec['tip'], t ** 0.78) * (1 + 0.1 * math.sin(math.pi * min(1, t * 3)) * (1 - t))
        return max(w, tail_spec['tip']), max(h, tail_spec['tip'])

    centers, sizes, weights, kinds = [], [], [], []
    # tail: tip -> root
    for i in range(len(tail_pts) - 1, 0, -1):
        t = i / (len(tail_pts) - 1)
        s = i / rings_per_tail_seg
        centers.append(P(tail_pts[i][0], 0, tail_pts[i][1]))
        sizes.append(tail_sizes(t))
        weights.append(chain_weights(s, tail_bones, 0.45))
        kinds.append('tail')
    # torso: hip -> chest (including both end points)
    for i, pt in enumerate(torso_pts):
        t = i / n_torso
        d_hip = t * L
        d_chest = (1 - t) * L
        wn = chain_weights(-d_chest / neck_seg, neck_bones, 0.45)
        wt = chain_weights(-d_hip / tail_seg, tail_bones, 0.45)
        n0, t0 = wn.get('neck_0', 0.0), wt.get('tail_0', 0.0)
        w = {'body': max(0.0, 1 - n0 - t0)}
        if n0 > 1e-4:
            w['neck_0'] = n0
        if t0 > 1e-4:
            w['tail_0'] = t0
        centers.append(P(pt[0], 0, pt[1]))
        sizes.append(torso_sizes(t))
        weights.append(w)
        kinds.append('torso')
    # neck: chest -> head joint
    for i in range(1, len(neck_pts)):
        t = i / (len(neck_pts) - 1)
        s = i / rings_per_neck_seg
        centers.append(P(neck_pts[i][0], 0, neck_pts[i][1]))
        sizes.append(neck_sizes(t))
        weights.append(chain_weights(s, neck_bones, 0.45))
        kinds.append('neck')

    body = Part(name + '_body')
    frames = sweep(body, centers, sizes, 18, weights, SKIN, tile=1.15, p=0.92, belly_flat=0.78,
                   belly_material=BELLY, belly_half_angle=0.62, cap_end=False, cap_start=True,
                   top_material=BACK, top_half_angle=spec.get('back_band', 0.8))

    # Dorsal spikes: spaced along the back from the neck to the tail tip.
    dorsal = spec['dorsal']
    travelled = 0.0
    for i in range(len(centers) - 1, 0, -1):
        travelled += (centers[i] - centers[i - 1]).length
        if travelled < dorsal['step']:
            continue
        travelled = 0.0
        kind = kinds[i]
        if kind == 'neck':
            size = dorsal['size'] * 0.65
        elif kind == 'torso':
            size = dorsal['size']
        else:
            size = dorsal['tail_size'] * (sizes[i][1] / max(sizes[0][1], 0.01) * 0.0 + min(1.0, 0.35 + sizes[i][1] / tail_spec['base'][1]))
        side, up = frames[i]
        tangent = (centers[min(i + 1, len(centers) - 1)] - centers[i - 1]).normalized()
        base = centers[i] + up * sizes[i][1] * 0.92
        tip = base + up * size - tangent * size * 0.55
        cone(body, base, tip, max(0.03, sizes[i][0] * 0.28), 5, weights[i], HORN)

    # Tail-side spikes in a row, as on Caraxes.
    if name == 'caraxes':
        for i in range(0, len(tail_pts) - 6, 2):
            t = (i + 2) / (len(tail_pts) - 1)
            w, h = tail_sizes(t)
            k = len(tail_pts) - 1 - (i + 2)  # centers list index (tail is reversed)
            if k < 1 or k >= len(centers):
                continue
            side, up = frames[k]
            for sgn in (-1, 1):
                base = centers[k] + side * w * 0.92 * sgn + up * h * 0.15
                cone(body, base, base + side * sgn * h * 0.9 + up * h * 0.3, max(0.025, w * 0.14), 4, weights[k], HORN)

    # ------------------------------------------------------------------ head
    joint_n = neck_pts[-1]
    phi = neck_end_angle + math.radians(head_spec['pitch'])
    cphi, sphi = math.cos(phi), math.sin(phi)
    Lh = head_spec['length']

    def H(x, y, z):
        return P(joint_n[0] + x * cphi - z * sphi, y, joint_n[1] + x * sphi + z * cphi)

    def Hd(x, y, z):
        return P(x * cphi - z * sphi, y, x * sphi + z * cphi)

    sw, sh, sn = head_spec['skull_w'], head_spec['skull_h'], head_spec['snout']
    nose = head_spec['nose']
    kt = [0, 0.12, 0.3, 0.52, 0.76, 0.93, 1.0]
    k_w = [0.78, 1.0, 0.92, lerp(0.6, 0.86, sn), lerp(0.5, 0.8, sn), lerp(0.46, 0.76, sn), lerp(0.2, 0.42, sn)]
    k_top = [0.6, 1.0, 0.84, 0.58, 0.5 + nose * 2, 0.54 + nose * 2.4, 0.32]
    k_bot = [0.5, 0.5, 0.36, 0.26, 0.22, 0.2, 0.12]

    def hw(t): return sw * interp(t, kt, k_w)
    def htop(t): return sh * interp(t, kt, k_top)
    def hbot(t): return sh * interp(t, kt, k_bot)
    def hx(t): return -0.16 * Lh + t * 1.16 * Lh

    head = Part(name + '_head')
    head_w = {'head': 1.0}
    n_head = 24
    ts = np.linspace(0, 1, n_head)
    centers_h = [H(hx(t), 0, 0) for t in ts]
    sizes_h = [(hw(t), (htop(t) + hbot(t)) / 2) for t in ts]
    # Each ring is centred between its top and bottom, so shift the centre line to match.
    centers_h = [H(hx(t), 0, (htop(t) - hbot(t)) / 2) for t in ts]
    sweep(head, centers_h, sizes_h, 18, [head_w] * n_head, SKIN, tile=0.8, p=0.9, belly_flat=1.0,
          belly_material=MOUTH, belly_half_angle=0.75, cap_end=True, twist_up=Hd(0, 0, 1))

    # brow ridges, cheek bulges
    for sgn in (-1, 1):
        pts, radii = [], []
        for t in np.linspace(0.18, 0.62, 8):
            pts.append(H(hx(t), sgn * hw(t) * 0.7, htop(t) * 0.62))
            radii.append(head_spec['brow'] * (1.0 - abs(t - 0.38) * 1.2) + 0.02)
        tube(head, pts, radii, 6, lambda i: head_w, SKIN, tile=0.5)
        pts, radii = [], []
        for t in np.linspace(0.08, 0.42, 6):
            pts.append(H(hx(t), sgn * hw(t) * 0.92, -hbot(t) * 0.2))
            radii.append(head_spec['wide_cheek'] * (1.0 - abs(t - 0.22) * 2.2) + 0.015)
        tube(head, pts, radii, 6, lambda i: head_w, SKIN, tile=0.5)

    # nostrils + eyes
    for sgn in (-1, 1):
        n_pos = H(hx(0.93), sgn * hw(0.93) * 0.55, htop(0.93) * 0.55)
        ellipsoid(head, n_pos, (0.03 * Lh * 0.6, 0.028 * Lh * 0.6, 0.02 * Lh * 0.6), head_w, MOUTH, rings=5, segments=8)
        eye_pos = H(hx(0.4), sgn * hw(0.4) * 0.86, htop(0.4) * 0.42)
        eye_disc(head, eye_pos, Hd(0.25, sgn * 1.0, 0.32), 0.052 * Lh * 0.55 + 0.02, head_w, EYE)

    # horns and crest
    for horn in head_spec['horns']:
        for sgn in (-1, 1):
            base = H(hx(horn['t']), sgn * hw(horn['t']) * horn['y'], htop(horn['t']) * horn['z'])
            direction = Hd(horn['d'][0], sgn * horn['d'][1], horn['d'][2])
            curved_spike(head, base, direction, horn['length'], horn['r'], Hd(0, 1, 0) * sgn, horn['bend'] * -1, head_w, HORN, sides=7, steps=9)
    for i in range(head_spec['crest']):
        t = 0.08 + i * (0.5 / head_spec['crest'])
        base = H(hx(t), 0, htop(t) * 0.97)
        size = head_spec['crest_len'] * (1.0 - i * 0.07)
        cone(head, base, base + Hd(-0.45, 0, 1.0).normalized() * size, 0.04 + size * 0.15, 5, head_w, HORN)

    # upper teeth
    tooth = head_spec['teeth']
    for sgn in (-1, 1):
        for i, t in enumerate(np.linspace(0.3, 0.97, 13)):
            size = tooth * (1.7 if 0.78 < t < 0.88 else 1.0) * (0.55 + 0.45 * math.sin(math.pi * min(1, (t - 0.25) / 0.75)))
            base = H(hx(t), sgn * hw(t) * 0.8, -hbot(t) * 0.92)
            cone(head, base, base + Hd(0.15, 0, -1).normalized() * size, size * 0.22, 5, head_w, TOOTH)

    # ------------------------------------------------------------------ jaw
    jaw = Part(name + '_jaw')
    jaw_w = {'jaw': 1.0}
    jt = np.linspace(0.14, 0.97, 16)
    jaw_depth = head_spec['jaw'] * sh

    def jw(t): return hw(t) * 0.88 * (1 - 0.35 * smoothstep(0.7, 1.0, t))
    def jh(t): return jaw_depth * interp(t, [0.14, 0.4, 0.8, 1.0], [0.55, 0.5, 0.36, 0.26]) / 2

    centers_j = [H(hx(t), 0, -hbot(t) * 0.9 - jh(t) * 0.55) for t in jt]
    sizes_j = [(jw(t), jh(t)) for t in jt]
    sweep(jaw, centers_j, sizes_j, 14, [jaw_w] * len(jt), SKIN, tile=0.8, p=0.9, belly_material=SKIN, cap_end=True, twist_up=Hd(0, 0, 1))
    for sgn in (-1, 1):
        for i, t in enumerate(np.linspace(0.34, 0.95, 11)):
            size = tooth * 0.85 * (0.6 + 0.4 * math.sin(math.pi * min(1, (t - 0.3) / 0.7)))
            base = H(hx(t), sgn * jw(t) * 0.72, -hbot(t) * 0.9 - jh(t) * 0.55 + jh(t) * 0.7)
            cone(jaw, base, base + Hd(-0.1, 0, 1).normalized() * size, size * 0.22, 5, jaw_w, TOOTH)
        # jaw "beard": spikes fanning along the lower edge and cheek
        for i in range(head_spec['beard']):
            t = 0.18 + i * (0.62 / max(1, head_spec['beard'] - 1))
            length = head_spec['beard_len'] * (1.0 - i * 0.06) * (1.0 + 0.4 * math.sin(math.pi * (i / max(1, head_spec['beard'] - 1))))
            base = H(hx(t), sgn * jw(t) * 0.82, -hbot(t) * 0.9 - jh(t) * 0.55 - jh(t) * 0.55)
            cone(jaw, base, base + Hd(-0.55, sgn * 0.55, -0.7).normalized() * length, 0.02 + length * 0.12, 5, jaw_w, HORN)

    # ------------------------------------------------------------------ wings
    shoulder_base = P(L * 0.3, 0, chest2[1] + spec['chest'][1] * 0.6)
    La, Lf, WT = wing_spec['arm'], wing_spec['fore'], wing_spec['thick']
    wing_geom = {}
    for sgn in (1, -1):
        S = shoulder_base + P(0, sgn * spec['chest'][0] * 0.55, 0)
        E = S + P(0.18 * La, sgn * 0.78 * La, 0.6 * La)
        W = E + P(-0.05 * Lf, sgn * 0.97 * Lf, 0.2 * Lf)
        tips = []
        for length, fan in zip(wing_spec['fingers']['lengths'], wing_spec['fingers']['fans']):
            a = math.radians(fan)
            tips.append(W + P(-math.sin(a) * length, sgn * math.cos(a) * length, -wing_spec['fingers']['droop'] * length))
        hip_attach = P(-L * 0.3, sgn * spec['hip'][0] * 0.88, 0.05)
        chest_attach = P(L * 0.22, sgn * spec['chest'][0] * 0.2, chest2[1] + spec['chest'][1] * 0.55)
        wing_geom[sgn] = dict(S=S, E=E, W=W, tips=tips, hip=hip_attach, chest=chest_attach)

    wing_bones_part = Part(name + '_wingbones')
    membrane_parts = {}
    for sgn, side_name in ((1, 'L'), (-1, 'R')):
        g = wing_geom[sgn]
        wb = [f'wing_{side_name}{i}' for i in range(6)]
        # arm tube shoulder -> elbow -> wrist
        pts = []
        radii = []
        w_arm = []
        for i in range(0, 13):
            u = i / 12
            seg = u * 2
            p = g['S'].lerp(g['E'], seg) if seg <= 1 else g['E'].lerp(g['W'], seg - 1)
            pts.append(p)
            radii.append(WT * (1.25 - 0.45 * u))
            w_arm.append(chain_weights(seg, ['body', wb[0], wb[1]], 0.3))
        tube(wing_bones_part, pts, radii, 8, lambda i: w_arm[i], SKIN, tile=1.0)
        ellipsoid(wing_bones_part, g['S'] + P(-0.2, sgn * -0.1, -0.35), (spec['chest'][0] * 0.55, spec['chest'][1] * 0.8, spec['chest'][1] * 0.7), {'body': 1.0}, SKIN, rings=8, segments=14)
        # knuckle + thumb claw
        ellipsoid(wing_bones_part, g['W'], (WT * 1.5,) * 3, {wb[1]: 1.0}, SKIN, rings=6, segments=10)
        thumb_dir = P(0.35, sgn * 0.4, 0.8)
        curved_spike(wing_bones_part, g['W'] + P(0.15, 0, 0.12), thumb_dir, Lf * 0.3, WT * 0.75, P(0, 1, 0) * sgn, 1.1, {wb[1]: 1.0}, HORN, sides=6)
        # fingers
        for fi, tip in enumerate(g['tips']):
            bone = wb[2 + fi]
            pts = [g['W'].lerp(tip, i / 10) for i in range(11)]
            radii = [WT * (0.8 - 0.62 * (i / 10)) + 0.012 for i in range(11)]
            wts = []
            for i in range(11):
                a = smoothstep(0.0, 0.15, i / 10)
                wts.append({bone: a, wb[1]: 1 - a} if a < 0.999 else {bone: 1.0})
            tube(wing_bones_part, pts, radii, 6, lambda i, wts=wts: wts[i], SKIN, tile=0.8)
            if fi == 0:
                hook = (tip - g['W']).normalized()
                curved_spike(wing_bones_part, tip, hook, 0.55, 0.045, P(0, 1, 0) * sgn, -1.3 * sgn, {bone: 1.0}, HORN, sides=5)

        # membrane
        mem = Part(f'{name}_membrane_{side_name}')
        na, ns = 8, 12
        scallop = wing_spec['scallop']
        for pi in range(3):
            fi_vec = g['tips'][pi] - g['W']
            fj_vec = g['tips'][pi + 1] - g['W']
            grid = []
            for ia in range(na + 1):
                a = ia / na
                edge = 1 - scallop * math.sin(math.pi * a) ** 1.4
                row = []
                for is_ in range(ns + 1):
                    s = is_ / ns
                    p = g['W'] + fi_vec.lerp(fj_vec, a) * (s * edge)
                    p += P(0, 0, -wing_spec['sag'] * math.sin(math.pi * a) * s * 0.35)
                    w = {wb[2 + pi]: 1 - a, wb[3 + pi]: a}
                    row.append((mem.vert(p, w), a, s))
                grid.append(row)
            for ia in range(na):
                for is_ in range(ns):
                    v00, v01, v11, v10 = grid[ia][is_], grid[ia][is_ + 1], grid[ia + 1][is_ + 1], grid[ia + 1][is_]
                    mem.face((v00[0], v01[0], v11[0], v10[0]), [(v00[1], v00[2]), (v01[1], v01[2]), (v11[1], v11[2]), (v10[1], v10[2])], MEMBRANE)
        # big panel between the arm, the body and the last finger
        arm_pts = [g['S'], g['E'], g['W']]
        trail_pts = [g['chest'], g['hip'], g['tips'][3]]

        def along(points, s):
            lengths = [(points[i + 1] - points[i]).length for i in range(len(points) - 1)]
            target = s * sum(lengths)
            for i, seg in enumerate(lengths):
                if target <= seg or i == len(lengths) - 1:
                    return points[i].lerp(points[i + 1], min(1.0, target / seg))
                target -= seg
            return points[-1]

        arm_len = [(arm_pts[1] - arm_pts[0]).length, (arm_pts[2] - arm_pts[1]).length]
        na2, ns2 = 8, 16
        grid = []
        for ia in range(na2 + 1):
            a = ia / na2
            row = []
            for is_ in range(ns2 + 1):
                s = is_ / ns2
                pa = along(arm_pts, s)
                pb = along(trail_pts, s)
                p = pa.lerp(pb, a) + P(0, 0, -wing_spec['sag'] * math.sin(math.pi * a) * 0.5 * smoothstep(0.0, 0.3, s))
                arm_s = s * 2
                w_a = chain_weights(arm_s, ['body', wb[0], wb[1]], 0.3)
                f4 = smoothstep(0.5, 0.82, s)
                w_b = {'body': 1 - f4, wb[5]: f4} if f4 > 0.001 else {'body': 1.0}
                w = merge_weights({k: v * (1 - a) for k, v in w_a.items()}, {k: v * a for k, v in w_b.items()})
                row.append((mem.vert(p, {k: v for k, v in w.items() if v > 1e-4}), a, s))
            grid.append(row)
        for ia in range(na2):
            for is_ in range(ns2):
                v00, v01, v11, v10 = grid[ia][is_], grid[ia][is_ + 1], grid[ia + 1][is_ + 1], grid[ia + 1][is_]
                mem.face((v00[0], v01[0], v11[0], v10[0]), [(v00[1], v00[2]), (v01[1], v01[2]), (v11[1], v11[2]), (v10[1], v10[2])], MEMBRANE)
        membrane_parts[side_name] = mem

    # ------------------------------------------------------------------ legs
    legs = Part(name + '_legs')
    thigh, shin, foot, thick = leg_spec['thigh'], leg_spec['shin'], leg_spec['foot'], leg_spec['thick']
    leg_geom = {}
    for sgn, side_name in ((1, 'L'), (-1, 'R')):
        hip_j = P(-L * 0.24, sgn * spec['hip'][0] * 0.78, -spec['hip'][1] * 0.1)
        knee = hip_j + P(0.72 * thigh, sgn * 0.12 * thigh, -0.68 * thigh)
        ankle = knee + P(-0.62 * shin, sgn * 0.02 * shin, -0.78 * shin)
        toe = ankle + P(0.94 * foot, 0, -0.3 * foot)
        lb = [f'leg_{side_name}0', f'leg_{side_name}1', f'leg_{side_name}2']
        pts, sizes_l, wts = [], [], []
        for seg_index, (a, b) in enumerate(((hip_j, knee), (knee, ankle), (ankle, toe))):
            for i in range(0, 7 if seg_index < 2 else 6):
                u = i / 6
                pts.append(a.lerp(b, u))
                s = seg_index + u
                wts.append(chain_weights(s, ['body'] + lb, 0.3))
                if seg_index == 0:
                    r = lerp(0.62, 0.42, u) * thick * 1.5 * (1 + 0.3 * math.sin(math.pi * u))
                    sizes_l.append((r * 1.05, r))
                elif seg_index == 1:
                    r = lerp(0.42, 0.26, u) * thick * 1.5
                    sizes_l.append((r * 0.85, r))
                else:
                    r = lerp(0.26, 0.2, u) * thick * 1.5
                    sizes_l.append((r, r * 0.75))
        sweep(legs, pts, sizes_l, 12, wts, SKIN, tile=0.9, p=1.0, cap_end=True)
        # toes: three forward, one back dew claw
        for toe_index, lat in enumerate((-0.4, 0.0, 0.4)):
            start = toe + P(-0.05 * foot, lat * thick * 1.5 * 0.6, 0.05)
            end = toe + P(0.55 * foot, lat * thick * 1.6 * 1.3, -0.12 * foot)
            tpts = [start.lerp(end, i / 4) for i in range(5)]
            tradii = [thick * 0.28 * (1 - 0.35 * (i / 4)) for i in range(5)]
            tube(legs, tpts, tradii, 6, lambda i: {lb[2]: 1.0}, SKIN, tile=0.5)
            curved_spike(legs, end, (end - start).normalized() + Vector((0, 0, -0.25)), foot * 0.42, thick * 0.2, P(0, 1, 0), 0.9, {lb[2]: 1.0}, HORN, sides=5)
        curved_spike(legs, ankle + P(-0.08, 0, 0.05), P(-0.7, 0, -0.5), foot * 0.4, thick * 0.18, P(0, 1, 0), -0.8, {lb[2]: 1.0}, HORN, sides=5)
        ellipsoid(legs, hip_j + P(0.1, 0, 0.1), (thick * 0.95, spec['hip'][1] * 1.0, spec['hip'][1] * 0.95), {'body': 0.5, lb[0]: 0.5}, SKIN, rings=8, segments=14)
        leg_geom[sgn] = dict(hip=hip_j, knee=knee, ankle=ankle, toe=toe, bones=lb)

    # ------------------------------------------------------------------ armature
    bone_defs = [('root', None, P(0, 0, -0.6), P(0, 0, 0.2)),
                 ('body', 'root', P(-L / 2, 0, 0), P(L / 2, 0, chest2[1]))]
    neck_joints = [P(neck_pts[i * rings_per_neck_seg][0], 0, neck_pts[i * rings_per_neck_seg][1]) for i in range(n_neck + 1)]
    for i in range(n_neck):
        bone_defs.append((f'neck_{i}', 'body' if i == 0 else f'neck_{i - 1}', neck_joints[i], neck_joints[i + 1]))
    head_tip = H(Lh, 0, 0)
    bone_defs.append(('head', f'neck_{n_neck - 1}', neck_joints[n_neck], head_tip))
    jaw_hinge = H(hx(0.14), 0, -hbot(0.14) * 0.9)
    bone_defs.append(('jaw', 'head', jaw_hinge, H(Lh * 0.98, 0, -hbot(0.9) * 1.3)))
    tail_joints = [P(tail_pts[i * rings_per_tail_seg][0], 0, tail_pts[i * rings_per_tail_seg][1]) for i in range(m_tail + 1)]
    for i in range(m_tail):
        bone_defs.append((f'tail_{i}', 'body' if i == 0 else f'tail_{i - 1}', tail_joints[i], tail_joints[i + 1]))
    for sgn, side_name in ((1, 'L'), (-1, 'R')):
        g = wing_geom[sgn]
        wb = [f'wing_{side_name}{i}' for i in range(6)]
        bone_defs.append((wb[0], 'body', g['S'], g['E']))
        bone_defs.append((wb[1], wb[0], g['E'], g['W']))
        for fi, tip in enumerate(g['tips']):
            bone_defs.append((wb[2 + fi], wb[1], g['W'], tip))
        lg = leg_geom[sgn]
        bone_defs.append((lg['bones'][0], 'body', lg['hip'], lg['knee']))
        bone_defs.append((lg['bones'][1], lg['bones'][0], lg['knee'], lg['ankle']))
        bone_defs.append((lg['bones'][2], lg['bones'][1], lg['ankle'], lg['toe']))

    armature = make_armature(name + '_rig', bone_defs)

    # Everything standing on its feet: origin at the body centre, feet lowest point gives the ground offset.
    lowest = min(min(v[2] for v in legs.verts), 0.0)
    ground_offset = -lowest

    objects = [body.build(materials, armature), head.build(materials, armature), jaw.build(materials, armature),
               wing_bones_part.build(materials, armature), legs.build(materials, armature)]
    for mem in membrane_parts.values():
        objects.append(mem.build(materials, armature))

    meta = dict(
        name=name,
        groundOffset=round(ground_offset, 3),
        length=round(neck_spec['len'] + tail_spec['len'] + L + Lh, 2),
        wingspan=round(2 * abs(wing_geom[1]['tips'][0].x), 2),
        bodyRadius=round(max(spec['chest']) * 1.15, 2),
        headLength=round(Lh, 2),
        neckBones=n_neck, tailBones=m_tail,
        bones=rig_metadata(bone_defs),
        triangles=sum(len(o.data.polygons) for o in objects),
    )
    return meta


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    summary = {}
    for name, spec in SPECS.items():
        if ONLY and name not in ONLY:
            continue
        bpy.ops.wm.read_factory_settings(use_empty=True)
        materials = make_materials(spec, TEXTURE_DIR)
        meta = build_dragon(spec, materials)
        path = os.path.join(OUT_DIR, name + '.glb')
        bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', export_yup=True, export_skins=True,
                                  export_animations=False, export_normals=True, export_image_format='JPEG',
                                  export_jpeg_quality=88)
        meta['bytes'] = os.path.getsize(path)
        with open(os.path.join(OUT_DIR, name + '.json'), 'w', encoding='utf-8') as handle:
            json.dump(meta, handle, indent=1)
        summary[name] = {k: v for k, v in meta.items() if k != 'bones'}
    print('DRAGON_REPORT', json.dumps(summary))


main()
