import * as THREE from 'three';
import * as CANNON from 'cannon-es';
import type { PhysicsWorld } from '../physics/PhysicsWorld';
import { createNoise2D, fbm, smoothstep } from '../core/Noise';

const hillNoise = createNoise2D(11);
const detailNoise = createNoise2D(47);
const mountainNoise = createNoise2D(93);
const colorNoise = createNoise2D(5);

/** Gentle large-scale rolling relief. Roads are laid directly on this so they climb and dip naturally. */
export function rollingHeight(x: number, z: number): number {
  return (fbm(hillNoise, x / 320, z / 320, 2) - 0.5) * 22;
}

/** Full natural terrain height away from any road: rolling hills + rougher detail + a mountain rim at the map edge. */
function wildHeight(x: number, z: number, roadDistance: number): number {
  const rough = (fbm(detailNoise, x / 70, z / 70, 4) - 0.5) * 16 * smoothstep(12, 90, roadDistance);
  const r = Math.hypot(x, z);
  const rim = smoothstep(330, 680, r);
  const mountains = rim * rim * (40 + fbm(mountainNoise, x / 190, z / 190, 5) * 190);
  return rollingHeight(x, z) + rough + mountains;
}

export interface RoadSample {
  x: number;
  y: number;
  z: number;
}

/**
 * Heightfield terrain shared by rendering and physics. Near roads the ground is
 * flattened to the road's own elevation so the road ribbon sits flush on it.
 */
export class Terrain {
  static readonly SIZE = 1400;
  static readonly SEGMENTS = 350;
  static readonly ROAD_FLAT_RADIUS = 8;

  readonly mesh: THREE.Mesh;
  private readonly heights: Float32Array;
  private readonly roadDistances: Float32Array;
  private readonly cell = Terrain.SIZE / Terrain.SEGMENTS;
  private readonly stride = Terrain.SEGMENTS + 1;

  constructor(roadSamples: RoadSample[], physics: PhysicsWorld, grassMap: THREE.Texture) {
    const n = this.stride;
    this.heights = new Float32Array(n * n);
    this.roadDistances = new Float32Array(n * n);
    this.computeHeights(roadSamples);
    this.mesh = this.buildMesh(grassMap);
    this.buildPhysics(physics);
  }

  private computeHeights(roadSamples: RoadSample[]): void {
    const n = this.stride;
    const half = Terrain.SIZE / 2;
    for (let j = 0; j < n; j++) {
      const z = -half + j * this.cell;
      for (let i = 0; i < n; i++) {
        const x = -half + i * this.cell;
        let bestSq = Infinity;
        let roadY = 0;
        for (let k = 0; k < roadSamples.length; k++) {
          const s = roadSamples[k];
          const dx = s.x - x;
          const dz = s.z - z;
          const sq = dx * dx + dz * dz;
          if (sq < bestSq) {
            bestSq = sq;
            roadY = s.y;
          }
        }
        const distance = Math.sqrt(bestSq);
        const blend = smoothstep(Terrain.ROAD_FLAT_RADIUS, 55, distance);
        const index = j * n + i;
        this.roadDistances[index] = distance;
        this.heights[index] = roadY + (wildHeight(x, z, distance) - roadY) * blend;
      }
    }
  }

  private buildMesh(grassMap: THREE.Texture): THREE.Mesh {
    const n = this.stride;
    const half = Terrain.SIZE / 2;
    const positions = new Float32Array(n * n * 3);
    const uvs = new Float32Array(n * n * 2);
    const colors = new Float32Array(n * n * 3);
    const indices: number[] = [];

    for (let j = 0; j < n; j++) {
      for (let i = 0; i < n; i++) {
        const index = j * n + i;
        const x = -half + i * this.cell;
        const z = -half + j * this.cell;
        positions[index * 3] = x;
        positions[index * 3 + 1] = this.heights[index];
        positions[index * 3 + 2] = z;
        uvs[index * 2] = x / 7;
        uvs[index * 2 + 1] = z / 7;
      }
    }
    for (let j = 0; j < n - 1; j++) {
      for (let i = 0; i < n - 1; i++) {
        const a = j * n + i;
        const b = a + 1;
        const c = a + n;
        const d = c + 1;
        indices.push(a, c, b, b, c, d);
      }
    }

    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3));
    geometry.setAttribute('uv', new THREE.BufferAttribute(uvs, 2));
    geometry.setIndex(indices);
    geometry.computeVertexNormals();

    // Vertex tint by slope / altitude / road proximity; multiplied with the tiling grass detail map.
    const normals = geometry.getAttribute('normal');
    const grass = new THREE.Color(0x6f9a3a);
    const dryGrass = new THREE.Color(0xa89a55);
    const dirt = new THREE.Color(0x8a7355);
    const rock = new THREE.Color(0x9a948c);
    const snow = new THREE.Color(0xf4f6fa);
    const color = new THREE.Color();
    for (let index = 0; index < n * n; index++) {
      const x = positions[index * 3];
      const y = positions[index * 3 + 1];
      const z = positions[index * 3 + 2];
      const slope = 1 - normals.getY(index);
      const patch = fbm(colorNoise, x / 60, z / 60, 3);

      color.copy(grass).lerp(dryGrass, smoothstep(0.35, 0.75, patch));
      color.lerp(rock, smoothstep(0.12, 0.3, slope + (patch - 0.5) * 0.08));
      color.lerp(rock, smoothstep(70, 120, y) * 0.8);
      color.lerp(snow, smoothstep(150, 185, y + patch * 25) * smoothstep(0.5, 0.2, slope));
      const shoulder = 1 - smoothstep(Terrain.ROAD_FLAT_RADIUS - 1.5, Terrain.ROAD_FLAT_RADIUS + 4, this.roadDistances[index]);
      color.lerp(dirt, shoulder * 0.85);

      colors[index * 3] = color.r;
      colors[index * 3 + 1] = color.g;
      colors[index * 3 + 2] = color.b;
    }
    geometry.setAttribute('color', new THREE.BufferAttribute(colors, 3));

    const material = new THREE.MeshStandardMaterial({
      map: grassMap,
      vertexColors: true,
      roughness: 0.95,
      metalness: 0,
    });
    const mesh = new THREE.Mesh(geometry, material);
    mesh.receiveShadow = true;
    return mesh;
  }

  private buildPhysics(physics: PhysicsWorld): void {
    const n = this.stride;
    const data: number[][] = [];
    for (let i = 0; i < n; i++) {
      const column: number[] = [];
      for (let yi = 0; yi < n; yi++) {
        // Heightfield local +Y maps to world -Z after the -90deg X rotation below.
        const j = n - 1 - yi;
        column.push(this.heights[j * n + i]);
      }
      data.push(column);
    }
    const shape = new CANNON.Heightfield(data, { elementSize: this.cell });
    const body = new CANNON.Body({ mass: 0, material: physics.groundMaterial });
    body.addShape(shape);
    body.position.set(-Terrain.SIZE / 2, 0, Terrain.SIZE / 2);
    body.quaternion.setFromEuler(-Math.PI / 2, 0, 0);
    physics.world.addBody(body);
  }

  private sample(grid: Float32Array, x: number, z: number): number {
    const half = Terrain.SIZE / 2;
    const max = Terrain.SEGMENTS - 1e-4;
    const gx = Math.min(max, Math.max(0, (x + half) / this.cell));
    const gz = Math.min(max, Math.max(0, (z + half) / this.cell));
    const i = Math.floor(gx);
    const j = Math.floor(gz);
    const fx = gx - i;
    const fz = gz - j;
    const n = this.stride;
    const a = grid[j * n + i];
    const b = grid[j * n + i + 1];
    const c = grid[(j + 1) * n + i];
    const d = grid[(j + 1) * n + i + 1];
    return a + (b - a) * fx + (c - a) * fz + (a - b - c + d) * fx * fz;
  }

  getHeightAt(x: number, z: number): number {
    return this.sample(this.heights, x, z);
  }

  getRoadDistanceAt(x: number, z: number): number {
    return this.sample(this.roadDistances, x, z);
  }
}
