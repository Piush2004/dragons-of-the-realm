"""Tiny pixel-art toolkit used by the Blender texture scripts.

Canvases are stored top-down as RGBA tuples (0..255). Saving goes through Blender's own image datablocks so the
whole art pipeline runs inside Blender, with a pure-Python PNG writer as a fallback when bpy is not importable.
"""
import math
import os
import random
import struct
import zlib

try:
    import bpy  # noqa: F401
    HAVE_BPY = True
except Exception:  # pragma: no cover - only outside Blender
    HAVE_BPY = False


def clamp(v, lo=0, hi=255):
    return lo if v < lo else hi if v > hi else v


def shade(c, f):
    """Multiply a colour's brightness."""
    return (clamp(int(c[0] * f)), clamp(int(c[1] * f)), clamp(int(c[2] * f)), c[3] if len(c) > 3 else 255)


def mix(a, b, t):
    a = a if len(a) > 3 else (a[0], a[1], a[2], 255)
    b = b if len(b) > 3 else (b[0], b[1], b[2], 255)
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(4))


def rgba(c):
    return c if len(c) > 3 else (c[0], c[1], c[2], 255)


class Canvas:
    def __init__(self, w=16, h=16, fill=(0, 0, 0, 0)):
        self.w, self.h = w, h
        self.px = [rgba(fill)] * (w * h)

    def get(self, x, y):
        if 0 <= x < self.w and 0 <= y < self.h:
            return self.px[y * self.w + x]
        return (0, 0, 0, 0)

    def set(self, x, y, c):
        if 0 <= x < self.w and 0 <= y < self.h:
            self.px[y * self.w + x] = rgba(c)

    def fill(self, c):
        self.px = [rgba(c)] * (self.w * self.h)

    def rect(self, x0, y0, x1, y1, c):
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                self.set(x, y, c)

    def hline(self, x0, x1, y, c):
        for x in range(x0, x1 + 1):
            self.set(x, y, c)

    def vline(self, x, y0, y1, c):
        for y in range(y0, y1 + 1):
            self.set(x, y, c)

    def speckle(self, base, spread, rng, alpha=255):
        for y in range(self.h):
            for x in range(self.w):
                f = 1.0 + rng.uniform(-spread, spread)
                c = shade(base, f)
                self.set(x, y, (c[0], c[1], c[2], alpha))

    def scatter(self, color, count, rng, region=None):
        x0, y0, x1, y1 = region or (0, 0, self.w - 1, self.h - 1)
        for _ in range(count):
            self.set(rng.randint(x0, x1), rng.randint(y0, y1), color)

    def disc(self, cx, cy, r, c):
        for y in range(self.h):
            for x in range(self.w):
                if (x - cx) ** 2 + (y - cy) ** 2 <= r * r:
                    self.set(x, y, c)

    def border(self, c, width=1):
        for i in range(width):
            self.hline(i, self.w - 1 - i, i, c)
            self.hline(i, self.w - 1 - i, self.h - 1 - i, c)
            self.vline(i, i, self.h - 1 - i, c)
            self.vline(self.w - 1 - i, i, self.h - 1 - i, c)

    def blit(self, other, ox, oy):
        for y in range(other.h):
            for x in range(other.w):
                p = other.get(x, y)
                if p[3] > 0:
                    self.set(ox + x, oy + y, p)

    def flipped_rows(self):
        rows = [self.px[y * self.w:(y + 1) * self.w] for y in range(self.h)]
        return [p for row in reversed(rows) for p in row]


def ascii_art(rows, palette):
    """Build a canvas from rows of characters. '.' and ' ' are transparent."""
    h = len(rows)
    w = max(len(r) for r in rows)
    c = Canvas(w, h)
    for y, row in enumerate(rows):
        for x, ch in enumerate(row):
            if ch in ('.', ' '):
                continue
            c.set(x, y, palette[ch])
    return c


def _png_bytes(w, h, pixels_top_down):
    raw = bytearray()
    for y in range(h):
        raw.append(0)
        for x in range(w):
            r, g, b, a = pixels_top_down[y * w + x]
            raw += bytes((r, g, b, a))

    def chunk(tag, data):
        c = struct.pack('>I', len(data)) + tag + data
        return c + struct.pack('>I', zlib.crc32(tag + data) & 0xFFFFFFFF)

    return (b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 6, 0, 0, 0)) +
            chunk(b'IDAT', zlib.compress(bytes(raw), 9)) + chunk(b'IEND', b''))


def save_png(canvas, path):
    """Save through a Blender image datablock (Non-Color, so bytes are exactly what we drew)."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    if HAVE_BPY:
        import bpy
        name = os.path.basename(path)
        img = bpy.data.images.new(name, canvas.w, canvas.h, alpha=True)
        img.colorspace_settings.name = 'Non-Color'
        img.alpha_mode = 'STRAIGHT'
        flat = []
        for (r, g, b, a) in canvas.flipped_rows():
            flat.extend((r / 255.0, g / 255.0, b / 255.0, a / 255.0))
        img.pixels = flat
        img.filepath_raw = path
        img.file_format = 'PNG'
        img.save()
        bpy.data.images.remove(img)
    else:
        with open(path, 'wb') as f:
            f.write(_png_bytes(canvas.w, canvas.h, canvas.px))


def seeded(name, salt=0):
    h = 1469598103934665603
    for ch in name:
        h = ((h ^ ord(ch)) * 1099511628211) & 0xFFFFFFFFFFFFFFFF
    return random.Random(h ^ salt)
