import * as THREE from 'three';
import { mergeGeometries } from 'three/examples/jsm/utils/BufferGeometryUtils.js';
import { createNoise2D, fbm, mulberry32 } from '../core/Noise';
import type { Terrain } from './Terrain';

const TREE_ATTEMPTS = 9000;
const ROAD_CLEARANCE = 13;
const MAX_TREE_ALTITUDE = 95;

function buildPineFoliage(): THREE.BufferGeometry {
  const tiers: THREE.BufferGeometry[] = [];
  const tierCount = 4;
  for (let i = 0; i < tierCount; i++) {
    const t = i / (tierCount - 1);
    const cone = new THREE.ConeGeometry(2.6 - t * 1.5, 3.6 - t * 0.8, 9, 1, true);
    cone.translate(0, 3.4 + i * 1.9, 0);
    tiers.push(cone);
  }
  return mergeGeometries(tiers);
}

function buildBroadleafFoliage(): THREE.BufferGeometry {
  const rng = mulberry32(404);
  const blobs: THREE.BufferGeometry[] = [];
  for (let i = 0; i < 5; i++) {
    const blob = new THREE.IcosahedronGeometry(1.6 + rng() * 0.9, 1);
    blob.translate((rng() - 0.5) * 2.6, 5 + rng() * 2.4, (rng() - 0.5) * 2.6);
    blobs.push(blob);
  }
  return mergeGeometries(blobs);
}

/** GPU-instanced forests scattered by a density noise, kept off roads, steep slopes and high peaks. */
export interface TrunkCollider {
  x: number;
  y: number;
  z: number;
  radius: number;
}

export class Vegetation {
  readonly group = new THREE.Group();
  /** One entry per tree trunk, for the physics world to turn into static obstacles. */
  readonly trunks: TrunkCollider[] = [];

  constructor(terrain: Terrain, worldRadius: number) {
    const rng = mulberry32(1337);
    const density = createNoise2D(61);
    const pines: THREE.Matrix4[] = [];
    const broadleaves: THREE.Matrix4[] = [];
    const pineColors: THREE.Color[] = [];
    const broadleafColors: THREE.Color[] = [];

    const position = new THREE.Vector3();
    const quaternion = new THREE.Quaternion();
    const scale = new THREE.Vector3();
    const yAxis = new THREE.Vector3(0, 1, 0);

    for (let i = 0; i < TREE_ATTEMPTS; i++) {
      const angle = rng() * Math.PI * 2;
      const radius = Math.sqrt(rng()) * worldRadius;
      const x = Math.cos(angle) * radius;
      const z = Math.sin(angle) * radius;
      const pick = rng();
      const size = 0.75 + rng() * 0.8;
      const yaw = rng() * Math.PI * 2;
      const tint = rng();

      if (terrain.getRoadDistanceAt(x, z) < ROAD_CLEARANCE) continue;
      const forest = fbm(density, x / 140, z / 140, 3);
      if (forest < 0.42) continue;

      const y = terrain.getHeightAt(x, z);
      if (y > MAX_TREE_ALTITUDE) continue;
      const slope = Math.abs(terrain.getHeightAt(x + 2, z) - y) + Math.abs(terrain.getHeightAt(x, z + 2) - y);
      if (slope > 1.6) continue;

      position.set(x, y - 0.3, z);
      quaternion.setFromAxisAngle(yAxis, yaw);
      scale.set(size, size * (0.9 + tint * 0.3), size);
      const matrix = new THREE.Matrix4().compose(position, quaternion, scale);
      this.trunks.push({ x, y, z, radius: 0.42 * size });

      // Conifers dominate higher ground, broadleaf trees the lowlands.
      const pineChance = 0.35 + Math.min(0.5, Math.max(0, y / 60));
      if (pick < pineChance) {
        pines.push(matrix);
        pineColors.push(new THREE.Color().setHSL(0.31 + tint * 0.05, 0.5, 0.075 + tint * 0.04));
      } else {
        broadleaves.push(matrix);
        broadleafColors.push(new THREE.Color().setHSL(0.24 + tint * 0.07, 0.55, 0.11 + tint * 0.06));
      }
    }

    const trunkMaterial = new THREE.MeshStandardMaterial({ color: 0x4d3a2a, roughness: 1 });
    const foliageMaterial = new THREE.MeshStandardMaterial({ color: 0xffffff, roughness: 0.85, side: THREE.DoubleSide });

    const pineTrunk = new THREE.CylinderGeometry(0.22, 0.38, 4.5, 6);
    pineTrunk.translate(0, 2.25, 0);
    const broadleafTrunk = new THREE.CylinderGeometry(0.3, 0.45, 5.4, 6);
    broadleafTrunk.translate(0, 2.7, 0);

    this.addInstances(pineTrunk, trunkMaterial, pines);
    this.addInstances(buildPineFoliage(), foliageMaterial, pines, pineColors);
    this.addInstances(broadleafTrunk, trunkMaterial, broadleaves);
    this.addInstances(buildBroadleafFoliage(), foliageMaterial, broadleaves, broadleafColors);
  }

  private addInstances(
    geometry: THREE.BufferGeometry,
    material: THREE.Material,
    matrices: THREE.Matrix4[],
    colors?: THREE.Color[],
  ): void {
    if (matrices.length === 0) return;
    const mesh = new THREE.InstancedMesh(geometry, material, matrices.length);
    for (let i = 0; i < matrices.length; i++) {
      mesh.setMatrixAt(i, matrices[i]);
      if (colors) mesh.setColorAt(i, colors[i]);
    }
    mesh.instanceMatrix.needsUpdate = true;
    if (mesh.instanceColor) mesh.instanceColor.needsUpdate = true;
    mesh.castShadow = true;
    mesh.receiveShadow = true;
    // Instances span the whole map, so the default single bounding sphere would cull incorrectly.
    mesh.frustumCulled = false;
    this.group.add(mesh);
  }
}
