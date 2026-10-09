import * as THREE from 'three';
import { createNoise2D, fbm, mulberry32 } from '../core/Noise';
import type { AssetLibrary, ModelPart } from './AssetLibrary';
import type { Terrain } from './Terrain';
import { PLAYABLE_RADIUS, WATER_LEVEL } from './WorldConfig';
import { isReserved } from './WorldMap';
import { insideSettlement } from './Settlements';

const TREE_ATTEMPTS = 70000;
const ROCK_ATTEMPTS = 2600;
const ROAD_CLEARANCE = 13;
const MAX_TREE_ALTITUDE = 175;
/** Trees closer than this to the viewer use the detailed model. */
const NEAR_RADIUS = 240;
/** The detailed set is only rebuilt after the viewer has moved this far. */
const REFRESH_DISTANCE = 30;
const NEAR_CAPACITY = 2600;
const CELL = 80;

const TREE_KINDS = ['broadleaf_0', 'broadleaf_1', 'broadleaf_2', 'pine_0', 'pine_1'] as const;

export interface TrunkCollider {
  x: number;
  y: number;
  z: number;
  radius: number;
}

interface TreeInstance {
  kind: number;
  matrix: THREE.Matrix4;
  x: number;
  z: number;
  /** Index into the far InstancedMesh of its kind. */
  farIndex: number;
}

const HIDDEN = new THREE.Matrix4().makeScale(0, 0, 0);

function instanced(parts: ModelPart[], capacity: number, castShadow: boolean): THREE.InstancedMesh[] {
  return parts.map((part) => {
    const mesh = new THREE.InstancedMesh(part.geometry, part.material, capacity);
    mesh.castShadow = castShadow;
    mesh.receiveShadow = true;
    // Instances span the whole map, so one bounding sphere would cull them wrongly.
    mesh.frustumCulled = false;
    return mesh;
  });
}

/**
 * Forests and boulders built from the Blender models. Every tree is always drawn with a cheap
 * distant model; the ones around the viewer are swapped for the detailed model as they move.
 */
export class Vegetation {
  readonly group = new THREE.Group();
  /** Cylinder obstacles (tree trunks and boulders) for the physics world. */
  readonly trunks: TrunkCollider[] = [];

  private readonly trees: TreeInstance[] = [];
  private readonly cells = new Map<string, number[]>();
  private readonly far: THREE.InstancedMesh[][] = [];
  private readonly near: THREE.InstancedMesh[][] = [];
  private readonly lastRefresh = new THREE.Vector3(Infinity, 0, Infinity);
  private promoted: number[] = [];

  constructor(terrain: Terrain, assets: AssetLibrary) {
    this.scatterTrees(terrain);
    this.buildTreeMeshes(assets);
    this.scatterRocks(terrain, assets);
  }

  private scatterTrees(terrain: Terrain): void {
    const rng = mulberry32(1337);
    const density = createNoise2D(61);
    const species = createNoise2D(17);
    const position = new THREE.Vector3();
    const quaternion = new THREE.Quaternion();
    const scale = new THREE.Vector3();
    const yAxis = new THREE.Vector3(0, 1, 0);
    const counts = TREE_KINDS.map(() => 0);

    for (let i = 0; i < TREE_ATTEMPTS; i++) {
      const angle = rng() * Math.PI * 2;
      const radius = Math.sqrt(rng()) * (PLAYABLE_RADIUS + 250);
      const x = Math.cos(angle) * radius;
      const z = Math.sin(angle) * radius;
      const pick = rng();
      const variant = rng();
      const size = 0.8 + rng() * 0.55;
      const yaw = rng() * Math.PI * 2;

      if (terrain.getRoadDistanceAt(x, z) < ROAD_CLEARANCE || isReserved(x, z, 28) || insideSettlement(x, z, 12)) continue;
      // Forests with clearings and meadows between them.
      const forest = fbm(density, x / 260, z / 260, 3);
      if (forest < 0.47 + pick * 0.08) continue;

      const y = terrain.getHeightAt(x, z);
      if (y > MAX_TREE_ALTITUDE || y < WATER_LEVEL + 0.8) continue;
      const slope = Math.abs(terrain.getHeightAt(x + 2, z) - y) + Math.abs(terrain.getHeightAt(x, z + 2) - y);
      if (slope > 1.9) continue;

      // Conifers take the high and the cold ground; stands of one species cluster together.
      const pineChance = THREE.MathUtils.clamp(0.15 + y / 90 + (species(x / 180, z / 180) - 0.5) * 1.1, 0.04, 0.97);
      const kind = pick < pineChance ? 3 + (variant < 0.5 ? 0 : 1) : Math.min(2, Math.floor(variant * 3));

      position.set(x, y, z);
      quaternion.setFromAxisAngle(yAxis, yaw);
      scale.set(size, size * (0.9 + variant * 0.25), size);
      this.trees.push({
        kind,
        matrix: new THREE.Matrix4().compose(position, quaternion, scale),
        x,
        z,
        farIndex: counts[kind]++,
      });
      this.trunks.push({ x, y, z, radius: 0.38 * size });

      const key = `${Math.floor(x / CELL)},${Math.floor(z / CELL)}`;
      const bucket = this.cells.get(key);
      if (bucket) bucket.push(this.trees.length - 1);
      else this.cells.set(key, [this.trees.length - 1]);
    }
  }

  private buildTreeMeshes(assets: AssetLibrary): void {
    const counts = TREE_KINDS.map((_, kind) => this.trees.filter((tree) => tree.kind === kind).length);
    TREE_KINDS.forEach((name, kind) => {
      const farMeshes = instanced(assets.get(`${name}_lod1`), Math.max(1, counts[kind]), false);
      const nearMeshes = instanced(assets.get(`${name}_lod0`), NEAR_CAPACITY, true);
      for (const mesh of nearMeshes) mesh.count = 0;
      this.far.push(farMeshes);
      this.near.push(nearMeshes);
      this.group.add(...farMeshes, ...nearMeshes);
    });
    for (const tree of this.trees) {
      for (const mesh of this.far[tree.kind]) mesh.setMatrixAt(tree.farIndex, tree.matrix);
    }
  }

  private scatterRocks(terrain: Terrain, assets: AssetLibrary): void {
    const rng = mulberry32(4242);
    const placements: THREE.Matrix4[][] = [[], [], []];
    const position = new THREE.Vector3();
    const quaternion = new THREE.Quaternion();
    const euler = new THREE.Euler();
    const scale = new THREE.Vector3();
    for (let i = 0; i < ROCK_ATTEMPTS; i++) {
      const angle = rng() * Math.PI * 2;
      const radius = Math.sqrt(rng()) * (PLAYABLE_RADIUS + 200);
      const x = Math.cos(angle) * radius;
      const z = Math.sin(angle) * radius;
      // Mostly small stones, with the occasional house-sized boulder.
      const size = 0.5 + Math.pow(rng(), 3.2) * 6.5;
      const variant = Math.floor(rng() * 3);
      euler.set((rng() - 0.5) * 0.5, rng() * Math.PI * 2, (rng() - 0.5) * 0.5);
      if (terrain.getRoadDistanceAt(x, z) < ROAD_CLEARANCE + size || isReserved(x, z, 10) || insideSettlement(x, z, 6)) continue;
      const y = terrain.getHeightAt(x, z);
      if (y < WATER_LEVEL - 1) continue;
      position.set(x, y - size * 0.12, z);
      quaternion.setFromEuler(euler);
      scale.setScalar(size);
      placements[variant].push(new THREE.Matrix4().compose(position, quaternion, scale));
      if (size > 0.9) this.trunks.push({ x, y, z, radius: size * 0.85 });
    }
    placements.forEach((matrices, variant) => {
      if (matrices.length === 0) return;
      for (const mesh of instanced(assets.get(`rock_${variant}`), matrices.length, true)) {
        matrices.forEach((matrix, index) => mesh.setMatrixAt(index, matrix));
        this.group.add(mesh);
      }
    });
  }

  /** Swaps trees near the viewer to the detailed model. Cheap to call every frame. */
  update(viewer: THREE.Vector3): void {
    const dx = viewer.x - this.lastRefresh.x;
    const dz = viewer.z - this.lastRefresh.z;
    if (dx * dx + dz * dz < REFRESH_DISTANCE * REFRESH_DISTANCE) return;
    this.lastRefresh.copy(viewer);

    // Give the previously detailed trees their distant model back.
    for (const index of this.promoted) {
      const tree = this.trees[index];
      for (const mesh of this.far[tree.kind]) mesh.setMatrixAt(tree.farIndex, tree.matrix);
    }
    this.promoted = [];

    const counts = TREE_KINDS.map(() => 0);
    const reach = Math.ceil(NEAR_RADIUS / CELL);
    const cx = Math.floor(viewer.x / CELL);
    const cz = Math.floor(viewer.z / CELL);
    const radiusSq = NEAR_RADIUS * NEAR_RADIUS;
    for (let i = cx - reach; i <= cx + reach; i++) {
      for (let j = cz - reach; j <= cz + reach; j++) {
        for (const index of this.cells.get(`${i},${j}`) ?? []) {
          const tree = this.trees[index];
          const tx = tree.x - viewer.x;
          const tz = tree.z - viewer.z;
          if (tx * tx + tz * tz > radiusSq || counts[tree.kind] >= NEAR_CAPACITY) continue;
          for (const mesh of this.near[tree.kind]) mesh.setMatrixAt(counts[tree.kind], tree.matrix);
          for (const mesh of this.far[tree.kind]) mesh.setMatrixAt(tree.farIndex, HIDDEN);
          counts[tree.kind]++;
          this.promoted.push(index);
        }
      }
    }

    TREE_KINDS.forEach((_, kind) => {
      for (const mesh of this.near[kind]) {
        mesh.count = counts[kind];
        mesh.instanceMatrix.needsUpdate = true;
      }
      for (const mesh of this.far[kind]) mesh.instanceMatrix.needsUpdate = true;
    });
  }

  get treeCount(): number {
    return this.trees.length;
  }
}
