import * as THREE from 'three';
import { createNoise2D, fbm, mulberry32 } from '../core/Noise';

/** All surface textures are generated at load time, so the game ships with no external image assets. */

function makeCanvas(size: number): { canvas: HTMLCanvasElement; ctx: CanvasRenderingContext2D } {
  const canvas = document.createElement('canvas');
  canvas.width = size;
  canvas.height = size;
  const ctx = canvas.getContext('2d');
  if (!ctx) throw new Error('2D canvas context unavailable');
  return { canvas, ctx };
}

function finishTexture(canvas: HTMLCanvasElement, srgb: boolean, anisotropy: number): THREE.CanvasTexture {
  const texture = new THREE.CanvasTexture(canvas);
  texture.wrapS = THREE.RepeatWrapping;
  texture.wrapT = THREE.RepeatWrapping;
  texture.anisotropy = anisotropy;
  if (srgb) texture.colorSpace = THREE.SRGBColorSpace;
  return texture;
}

/** Converts a tileable height array into a tangent-space normal map. */
function heightToNormalCanvas(height: Float32Array, size: number, strength: number): HTMLCanvasElement {
  const { canvas, ctx } = makeCanvas(size);
  const image = ctx.createImageData(size, size);
  const at = (x: number, y: number) => height[((y + size) % size) * size + ((x + size) % size)];
  for (let y = 0; y < size; y++) {
    for (let x = 0; x < size; x++) {
      const dx = (at(x + 1, y) - at(x - 1, y)) * strength;
      const dy = (at(x, y + 1) - at(x, y - 1)) * strength;
      const inv = 1 / Math.sqrt(dx * dx + dy * dy + 1);
      const o = (y * size + x) * 4;
      image.data[o] = (-dx * inv * 0.5 + 0.5) * 255;
      image.data[o + 1] = (dy * inv * 0.5 + 0.5) * 255;
      image.data[o + 2] = (inv * 0.5 + 0.5) * 255;
      image.data[o + 3] = 255;
    }
  }
  ctx.putImageData(image, 0, 0);
  return canvas;
}

export interface RoadTextures {
  map: THREE.Texture;
  normalMap: THREE.Texture;
  roughnessMap: THREE.Texture;
}

/**
 * Asphalt with aggregate grain, tyre-worn lanes, edge lines and a dashed centre line.
 * U spans the road width; one V repeat covers one dash + gap.
 */
export function createRoadTextures(anisotropy: number): RoadTextures {
  const size = 1024;
  const rng = mulberry32(2024);
  const blotch = createNoise2D(8);

  const { canvas, ctx } = makeCanvas(size);
  const image = ctx.createImageData(size, size);
  const height = new Float32Array(size * size);
  const { canvas: roughCanvas, ctx: roughCtx } = makeCanvas(size);
  const roughImage = roughCtx.createImageData(size, size);

  for (let y = 0; y < size; y++) {
    for (let x = 0; x < size; x++) {
      const u = x / size;
      const grain = rng();
      // Tileable large-scale tonal variation (period = texture size).
      const px = (x / size) * 6;
      const py = (y / size) * 6;
      const tone = fbm((a, b) => blotch(((a % 6) + 6) % 6, ((b % 6) + 6) % 6), px, py, 2);
      // Two darker, smoother strips where tyres polish each lane.
      const laneWear =
        Math.exp(-Math.pow((u - 0.27) / 0.07, 2)) + Math.exp(-Math.pow((u - 0.73) / 0.07, 2));
      const base = 92 + grain * 16 + (tone - 0.5) * 20 - laneWear * 10;
      const stone = grain > 0.97 ? 26 : 0;
      const o = (y * size + x) * 4;
      image.data[o] = base + stone;
      image.data[o + 1] = base + stone;
      image.data[o + 2] = base + stone + 3;
      image.data[o + 3] = 255;
      height[y * size + x] = grain;
      const rough = 205 + grain * 40 - laneWear * 45;
      roughImage.data[o] = rough;
      roughImage.data[o + 1] = rough;
      roughImage.data[o + 2] = rough;
      roughImage.data[o + 3] = 255;
    }
  }
  ctx.putImageData(image, 0, 0);
  roughCtx.putImageData(roughImage, 0, 0);

  const paint = (context: CanvasRenderingContext2D, fill: string) => {
    context.fillStyle = fill;
    const edge = size * 0.035;
    const lineWidth = size * 0.014;
    context.fillRect(edge, 0, lineWidth, size);
    context.fillRect(size - edge - lineWidth, 0, lineWidth, size);
    // Dashed centre line: painted for the first third of each repeat.
    context.fillRect(size / 2 - lineWidth / 2, 0, lineWidth, size * 0.33);
  };
  paint(ctx, 'rgba(232, 228, 214, 0.9)');
  paint(roughCtx, 'rgb(150, 150, 150)');

  return {
    map: finishTexture(canvas, true, anisotropy),
    normalMap: finishTexture(heightToNormalCanvas(height, size, 1.6), false, anisotropy),
    roughnessMap: finishTexture(roughCanvas, false, anisotropy),
  };
}

/** Neutral-brightness grass detail; hue comes from terrain vertex colours. */
export function createGrassTexture(anisotropy: number): THREE.Texture {
  const size = 512;
  const rng = mulberry32(77);
  const { canvas, ctx } = makeCanvas(size);
  const image = ctx.createImageData(size, size);
  const patch = createNoise2D(31);
  for (let y = 0; y < size; y++) {
    for (let x = 0; x < size; x++) {
      const px = (x / size) * 8;
      const py = (y / size) * 8;
      const tone = patch(((px % 8) + 8) % 8, ((py % 8) + 8) % 8);
      const blade = rng();
      const v = 150 + blade * 80 + (tone - 0.5) * 50;
      const o = (y * size + x) * 4;
      image.data[o] = v * 0.94;
      image.data[o + 1] = v;
      image.data[o + 2] = v * 0.86;
      image.data[o + 3] = 255;
    }
  }
  ctx.putImageData(image, 0, 0);
  return finishTexture(canvas, true, anisotropy);
}
