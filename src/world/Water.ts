import * as THREE from 'three';
import { createNoise2D, fbm } from '../core/Noise';
import { WATER_LEVEL, WORLD_SIZE } from './WorldConfig';

function createRippleNormalMap(): THREE.CanvasTexture {
  const size = 256;
  const noise = createNoise2D(808);
  const period = 8;
  const wrap = (v: number) => ((v % period) + period) % period;
  const height = new Float32Array(size * size);
  for (let y = 0; y < size; y++) {
    for (let x = 0; x < size; x++) {
      const px = (x / size) * period;
      const py = (y / size) * period;
      // Blend four shifted lookups so the pattern tiles without a seam.
      const fx = x / size;
      const fy = y / size;
      const sample = (ox: number, oy: number) => fbm((a, b) => noise(wrap(a), wrap(b)), px + ox, py + oy, 3);
      height[y * size + x] =
        sample(0, 0) * (1 - fx) * (1 - fy) + sample(-period, 0) * fx * (1 - fy) + sample(0, -period) * (1 - fx) * fy + sample(-period, -period) * fx * fy;
    }
  }
  const canvas = document.createElement('canvas');
  canvas.width = size;
  canvas.height = size;
  const context = canvas.getContext('2d');
  if (!context) throw new Error('2D canvas context unavailable');
  const image = context.createImageData(size, size);
  const at = (x: number, y: number) => height[((y + size) % size) * size + ((x + size) % size)];
  for (let y = 0; y < size; y++) {
    for (let x = 0; x < size; x++) {
      const dx = (at(x + 1, y) - at(x - 1, y)) * 9;
      const dy = (at(x, y + 1) - at(x, y - 1)) * 9;
      const inv = 1 / Math.sqrt(dx * dx + dy * dy + 1);
      const o = (y * size + x) * 4;
      image.data[o] = (-dx * inv * 0.5 + 0.5) * 255;
      image.data[o + 1] = (dy * inv * 0.5 + 0.5) * 255;
      image.data[o + 2] = (inv * 0.5 + 0.5) * 255;
      image.data[o + 3] = 255;
    }
  }
  context.putImageData(image, 0, 0);
  const texture = new THREE.CanvasTexture(canvas);
  texture.wrapS = THREE.RepeatWrapping;
  texture.wrapT = THREE.RepeatWrapping;
  return texture;
}

/**
 * One water sheet at sea level across the map: the lake and every hollow below it fill naturally.
 * It reflects the sky environment and ripples by scrolling a normal map (no second scene render).
 */
export class Water {
  readonly mesh: THREE.Mesh;
  private readonly ripples: THREE.CanvasTexture;

  constructor() {
    this.ripples = createRippleNormalMap();
    // Far larger than the terrain so the bay reads as open sea out to the horizon.
    const extent = WORLD_SIZE * 5;
    this.ripples.repeat.set(extent / 26, extent / 26);
    const material = new THREE.MeshPhysicalMaterial({
      color: 0x1d4a55,
      roughness: 0.07,
      metalness: 0.0,
      normalMap: this.ripples,
      normalScale: new THREE.Vector2(0.45, 0.45),
      transparent: true,
      opacity: 0.86,
      envMapIntensity: 1.6,
    });
    const geometry = new THREE.PlaneGeometry(extent, extent);
    geometry.rotateX(-Math.PI / 2);
    this.mesh = new THREE.Mesh(geometry, material);
    this.mesh.position.y = WATER_LEVEL;
    this.mesh.receiveShadow = true;
  }

  update(deltaSeconds: number): void {
    this.ripples.offset.x += deltaSeconds * 0.012;
    this.ripples.offset.y += deltaSeconds * 0.019;
  }
}
