"""
Procedural, seamlessly tiling PBR terrain materials, written with Blender's bundled numpy.

Run:  blender --background --python make_terrain_textures.py -- <out_dir> [size]
Writes for each material: <name>_albedo.png, <name>_normal.png (tangent space, OpenGL),
<name>_mask.png (Unity HDRP mask map: R metallic, G ambient occlusion, B detail/height, A smoothness).
"""
import sys
import numpy as np
import bpy

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = argv[0] if argv else '.'
SIZE = int(argv[1]) if len(argv) > 1 else 2048


def spectral_noise(size, beta, seed):
    """Tileable noise with a 1/f^beta spectrum, normalised to 0..1."""
    rng = np.random.default_rng(seed)
    white = rng.standard_normal((size, size))
    f = np.fft.fftfreq(size)
    fx, fy = np.meshgrid(f, f)
    r = np.sqrt(fx * fx + fy * fy)
    r[0, 0] = 1.0
    spec = np.fft.fft2(white) / (r ** beta)
    spec[0, 0] = 0
    n = np.real(np.fft.ifft2(spec))
    n -= n.min()
    n /= max(n.max(), 1e-9)
    return n


def fbm(size, seed, betas=(1.2, 1.8, 2.4), weights=(0.5, 0.35, 0.15)):
    out = np.zeros((size, size))
    for i, (b, w) in enumerate(zip(betas, weights)):
        out += w * spectral_noise(size, b, seed + i * 101)
    out -= out.min()
    out /= max(out.max(), 1e-9)
    return out


def ridged(n):
    return 1.0 - np.abs(2.0 * n - 1.0)


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def lerp3(c0, c1, t):
    t = t[..., None]
    return np.array(c0) * (1 - t) + np.array(c1) * t


def normal_from_height(h, strength):
    gx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5 * strength * h.shape[0] / 256.0
    gy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 0.5 * strength * h.shape[0] / 256.0
    nz = np.ones_like(h)
    ln = np.sqrt(gx * gx + gy * gy + nz * nz)
    n = np.stack([-gx / ln, gy / ln, nz / ln], axis=-1)       # OpenGL green-up
    return n * 0.5 + 0.5


def ambient_occlusion(h, radius_px, strength):
    blur = h.copy()
    for _ in range(3):
        blur = (blur + np.roll(blur, radius_px, 0) + np.roll(blur, -radius_px, 0) +
                np.roll(blur, radius_px, 1) + np.roll(blur, -radius_px, 1)) / 5.0
    return np.clip(1.0 - np.clip(blur - h, 0, None) * strength, 0.25, 1.0)


def save_png(arr, path):
    h, w = arr.shape[:2]
    if arr.ndim == 2:
        arr = np.stack([arr, arr, arr], axis=-1)
    if arr.shape[2] == 3:
        arr = np.concatenate([arr, np.ones((h, w, 1))], axis=-1)
    img = bpy.data.images.new(path, w, h, alpha=True, float_buffer=False)
    img.colorspace_settings.name = 'sRGB' if path.endswith('_albedo.png') else 'Non-Color'
    img.pixels.foreach_set(np.clip(arr, 0, 1).astype(np.float32).ravel())
    img.filepath_raw = path
    img.file_format = 'PNG'
    img.save()
    bpy.data.images.remove(img)


def write_material(name, albedo, height, smooth, normal_strength, ao_radius=3, ao_strength=3.0):
    save_png(albedo, f'{OUT}/{name}_albedo.png')
    save_png(normal_from_height(height, normal_strength), f'{OUT}/{name}_normal.png')
    ao = ambient_occlusion(height, ao_radius, ao_strength)
    mask = np.stack([np.zeros_like(height), ao, height, smooth], axis=-1)
    save_png(mask, f'{OUT}/{name}_mask.png')
    print('wrote', name)


def grass(size):
    base = fbm(size, 11)
    clumps = fbm(size, 21, betas=(0.8, 1.4), weights=(0.6, 0.4))
    blades = spectral_noise(size, 0.4, 31)
    height = 0.55 * blades + 0.3 * base + 0.15 * clumps
    dry = smoothstep(0.4, 0.8, clumps)
    c = lerp3((0.20, 0.27, 0.10), (0.42, 0.42, 0.17), dry)
    c = c * (0.65 + 0.55 * blades[..., None]) * (0.85 + 0.3 * base[..., None])
    soil = smoothstep(0.78, 0.95, base)[..., None]
    c = c * (1 - soil * 0.5) + np.array((0.2, 0.15, 0.09)) * soil * 0.5
    write_material('grass', c, height, 0.05 + 0.05 * blades, 1.6)


def rock(size):
    big = fbm(size, 41, betas=(1.4, 2.0, 2.6), weights=(0.5, 0.3, 0.2))
    strata = np.sin((np.arange(size)[:, None] / size * 14 + big * 3.0) * np.pi * 2) * 0.5 + 0.5
    cracks = ridged(fbm(size, 51, betas=(1.6, 2.2), weights=(0.6, 0.4)))
    cracks = smoothstep(0.82, 0.97, cracks)
    height = 0.55 * big + 0.25 * strata + 0.2 * spectral_noise(size, 0.9, 61)
    height = height * (1 - 0.5 * cracks)
    t = smoothstep(0.15, 0.85, height)
    c = lerp3((0.22, 0.21, 0.20), (0.62, 0.59, 0.53), t)
    lichen = smoothstep(0.7, 0.9, fbm(size, 71, betas=(1.0, 1.6), weights=(0.6, 0.4)))[..., None]
    c = c * (1 - lichen * 0.25) + np.array((0.28, 0.32, 0.18)) * lichen * 0.25
    c *= (1 - 0.55 * cracks[..., None])
    write_material('rock', c, height, 0.08 + 0.12 * t, 3.2, ao_radius=4, ao_strength=4.5)


def sand(size):
    ripples = np.sin((np.arange(size)[None, :] / size * 40 + fbm(size, 81) * 5.0) * np.pi * 2) * 0.5 + 0.5
    grain = spectral_noise(size, 0.2, 91)
    height = 0.6 * ripples * fbm(size, 83, betas=(1.2, 2.0), weights=(0.5, 0.5)) + 0.4 * grain
    c = lerp3((0.62, 0.55, 0.40), (0.82, 0.74, 0.55), grain)
    c *= 0.9 + 0.2 * ripples[..., None]
    write_material('sand', c, height, 0.04 + 0.05 * grain, 0.9, ao_radius=2, ao_strength=1.5)


def ash(size):
    n = fbm(size, 101, betas=(1.2, 1.8, 2.4))
    cinder = spectral_noise(size, 0.15, 111)
    glow = smoothstep(0.9, 1.0, cinder) * smoothstep(0.55, 0.9, n)
    height = 0.6 * n + 0.4 * cinder
    c = lerp3((0.04, 0.04, 0.045), (0.22, 0.21, 0.20), n)
    c = c + glow[..., None] * np.array((0.08, 0.02, 0.0))
    write_material('ash', c, height, 0.05 + 0.08 * cinder, 2.2)


def snow(size):
    n = fbm(size, 121, betas=(1.6, 2.2), weights=(0.6, 0.4))
    sparkle = spectral_noise(size, 0.1, 131)
    c = lerp3((0.78, 0.82, 0.88), (0.97, 0.98, 1.0), n)
    c *= 0.95 + 0.07 * sparkle[..., None]
    write_material('snow', c, 0.5 * n + 0.5 * sparkle, 0.25 + 0.2 * sparkle, 0.7, ao_strength=1.0)


def dirt(size):
    n = fbm(size, 141)
    pebbles = smoothstep(0.86, 0.96, spectral_noise(size, 0.5, 151))
    height = 0.6 * n + 0.4 * pebbles
    c = lerp3((0.24, 0.17, 0.11), (0.48, 0.36, 0.24), n)
    c = c * (1 - pebbles[..., None] * 0.2) + np.array((0.4, 0.38, 0.35)) * pebbles[..., None] * 0.2
    write_material('dirt', c, height, 0.04 + 0.04 * n, 1.6)


for fn in (grass, rock, sand, ash, snow, dirt):
    fn(SIZE)
print('terrain textures done')
