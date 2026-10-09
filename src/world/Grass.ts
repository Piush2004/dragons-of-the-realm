import * as THREE from 'three';
import type { AssetLibrary } from './AssetLibrary';
import type { Terrain } from './Terrain';
import { WATER_LEVEL } from './WorldConfig';

const RADIUS = 62;
const SPACING = 1.25;
const REFRESH_DISTANCE = 7;
/** Above this height over the ground the viewer cannot see grass blades, so none are placed. */
const MAX_VIEW_HEIGHT = 70;

function hash(ix: number, iz: number, salt: number): number {
  let h = Math.imul(ix, 374761393) ^ Math.imul(iz, 668265263) ^ Math.imul(salt, 2246822519);
  h = Math.imul(h ^ (h >>> 13), 1274126177);
  return ((h ^ (h >>> 16)) >>> 0) / 4294967296;
}

/**
 * A carpet of grass clumps that follows the viewer. Clump positions come from a hash of their
 * grid cell, so the same clump reappears in the same place whenever the viewer returns.
 */
export class Grass {
  readonly mesh: THREE.InstancedMesh;
  private readonly terrain: Terrain;
  private readonly last = new THREE.Vector3(Infinity, 0, Infinity);
  private readonly windTime = { value: 0 };

  constructor(terrain: Terrain, assets: AssetLibrary) {
    this.terrain = terrain;
    const part = assets.get('grass_0')[0];
    const material = part.material as THREE.MeshStandardMaterial;
    // Sway the tops of the blades (uv.y = 1) in the wind; roots stay planted.
    material.onBeforeCompile = (shader) => {
      shader.uniforms.windTime = this.windTime;
      shader.vertexShader = shader.vertexShader
        .replace('#include <common>', '#include <common>\nuniform float windTime;')
        .replace(
          '#include <begin_vertex>',
          `#include <begin_vertex>
          vec4 grassWorld = instanceMatrix * vec4(position, 1.0);
          float gust = sin(windTime * 1.7 + grassWorld.x * 0.35 + grassWorld.z * 0.27) + sin(windTime * 2.9 + grassWorld.x * 0.11) * 0.5;
          transformed.x += gust * 0.07 * uv.y;
          transformed.z += gust * 0.04 * uv.y;`,
        );
    };

    const side = Math.ceil((RADIUS * 2) / SPACING);
    this.mesh = new THREE.InstancedMesh(part.geometry, material, side * side);
    this.mesh.count = 0;
    this.mesh.frustumCulled = false;
    this.mesh.receiveShadow = true;
  }

  update(viewer: THREE.Vector3, deltaSeconds: number): void {
    this.windTime.value += deltaSeconds;
    const dx = viewer.x - this.last.x;
    const dz = viewer.z - this.last.z;
    if (dx * dx + dz * dz < REFRESH_DISTANCE * REFRESH_DISTANCE) return;
    this.last.copy(viewer);

    if (viewer.y - this.terrain.getHeightAt(viewer.x, viewer.z) > MAX_VIEW_HEIGHT) {
      this.mesh.count = 0;
      return;
    }

    const matrix = new THREE.Matrix4();
    const position = new THREE.Vector3();
    const quaternion = new THREE.Quaternion();
    const scale = new THREE.Vector3();
    const yAxis = new THREE.Vector3(0, 1, 0);
    const reach = Math.ceil(RADIUS / SPACING);
    const cx = Math.round(viewer.x / SPACING);
    const cz = Math.round(viewer.z / SPACING);
    let count = 0;

    for (let i = cx - reach; i <= cx + reach; i++) {
      for (let j = cz - reach; j <= cz + reach; j++) {
        const x = (i + hash(i, j, 1) - 0.5) * SPACING;
        const z = (j + hash(i, j, 2) - 0.5) * SPACING;
        const ox = x - viewer.x;
        const oz = z - viewer.z;
        const distance = Math.sqrt(ox * ox + oz * oz);
        // Thin the carpet toward its edge so it fades out instead of ending in a hard ring.
        if (distance > RADIUS || hash(i, j, 3) < Math.pow(distance / RADIUS, 2.2)) continue;
        if (this.terrain.getRoadDistanceAt(x, z) < 7.4) continue;
        const y = this.terrain.getHeightAt(x, z);
        if (y < WATER_LEVEL + 0.5 || y > 120) continue;
        const size = 0.7 + hash(i, j, 4) * 0.9;
        position.set(x, y, z);
        quaternion.setFromAxisAngle(yAxis, hash(i, j, 5) * Math.PI);
        scale.set(size, size * (0.7 + hash(i, j, 6) * 0.8), size);
        this.mesh.setMatrixAt(count++, matrix.compose(position, quaternion, scale));
      }
    }
    this.mesh.count = count;
    this.mesh.instanceMatrix.needsUpdate = true;
  }
}
