import * as THREE from 'three';
import { mulberry32 } from '../core/Noise';
import type { AssetLibrary } from './AssetLibrary';
import type { Terrain } from './Terrain';
import { SETTLEMENTS, isReserved, type Settlement } from './WorldMap';
import { WATER_LEVEL } from './WorldConfig';

const MODELS = ['house_cottage', 'house_tall', 'house_stone', 'barn'] as const;
const ROAD_CLEARANCE = 15;
const MAX_SLOPE = 0.7;

/** True inside any settlement's footprint (used to keep forests out of towns). */
export function insideSettlement(x: number, z: number, margin = 0): boolean {
  return SETTLEMENTS.some((s) => ((x - s.x) / (s.radiusX + margin)) ** 2 + ((z - s.z) / (s.radiusZ + margin)) ** 2 < 1);
}

/** Towns and villages built from the Blender house models, laid out on jittered street grids. */
export class Settlements {
  readonly group = new THREE.Group();
  readonly houseCount: number;
  /** Why candidate sites were rejected, per settlement (for diagnostics). */
  readonly stats: Record<string, Record<string, number>> = {};

  constructor(terrain: Terrain, assets: AssetLibrary) {
    const placements: THREE.Matrix4[][] = MODELS.map(() => []);
    const position = new THREE.Vector3();
    const quaternion = new THREE.Quaternion();
    const scale = new THREE.Vector3();
    const yAxis = new THREE.Vector3(0, 1, 0);
    let total = 0;

    for (const settlement of SETTLEMENTS) {
      const rng = mulberry32(settlement.id.length * 7919 + Math.round(settlement.x));
      const streetAngle = rng() * Math.PI;
      const cos = Math.cos(streetAngle);
      const sin = Math.sin(streetAngle);
      const candidates: Array<[number, number]> = [];
      const spacing = settlement.spacing;
      const reachX = Math.ceil(settlement.radiusX / spacing);
      const reachZ = Math.ceil(settlement.radiusZ / spacing);
      for (let i = -reachX; i <= reachX; i++) {
        for (let j = -reachZ; j <= reachZ; j++) {
          // Leave every fifth row and column open as a street.
          if (i % 5 === 0 || j % 5 === 0) continue;
          const lx = i * spacing + (rng() - 0.5) * spacing * 0.35;
          const lz = j * spacing + (rng() - 0.5) * spacing * 0.35;
          if ((lx / settlement.radiusX) ** 2 + (lz / settlement.radiusZ) ** 2 > 1) continue;
          candidates.push([settlement.x + lx * cos - lz * sin, settlement.z + lx * sin + lz * cos]);
        }
      }
      // Fill the densest areas first, then trim to the settlement's size.
      candidates.sort((a, b) => Math.hypot(a[0] - settlement.x, a[1] - settlement.z) - Math.hypot(b[0] - settlement.x, b[1] - settlement.z));

      let placed = 0;
      const stat = { candidates: candidates.length, water: 0, reserved: 0, road: 0, slope: 0, placed: 0 };
      this.stats[settlement.id] = stat;
      for (const [x, z] of candidates) {
        if (placed >= settlement.houses) break;
        const y = terrain.getHeightAt(x, z);
        if (y < WATER_LEVEL + 3) { stat.water++; continue; }
        if (isReserved(x, z, 4)) { stat.reserved++; continue; }
        if (terrain.getRoadDistanceAt(x, z) < ROAD_CLEARANCE) { stat.road++; continue; }
        const slope = Math.abs(terrain.getHeightAt(x + 6, z) - y) + Math.abs(terrain.getHeightAt(x, z + 6) - y);
        if (slope / 6 > MAX_SLOPE) { stat.slope++; continue; }
        stat.placed++;

        const model = pickModel(settlement, rng());
        const yaw = streetAngle + Math.round(rng() * 3) * (Math.PI / 2) + (rng() - 0.5) * 0.18;
        const size = 0.92 + rng() * 0.32;
        position.set(x, y - 0.1, z);
        quaternion.setFromAxisAngle(yAxis, yaw);
        scale.setScalar(size);
        placements[model].push(new THREE.Matrix4().compose(position, quaternion, scale));
        placed++;
        total++;
      }
    }

    placements.forEach((matrices, index) => {
      if (matrices.length === 0) return;
      for (const part of assets.get(MODELS[index])) {
        const mesh = new THREE.InstancedMesh(part.geometry, part.material, matrices.length);
        matrices.forEach((matrix, k) => mesh.setMatrixAt(k, matrix));
        mesh.instanceMatrix.needsUpdate = true;
        mesh.castShadow = true;
        mesh.receiveShadow = true;
        mesh.frustumCulled = false;
        this.group.add(mesh);
      }
    });
    this.houseCount = total;
  }
}

function pickModel(settlement: Settlement, r: number): number {
  const town = settlement.id === 'kings_landing';
  if (!town && r > 0.9) return 3;
  if (r < 0.34) return 0;
  if (r < (town ? 0.7 : 0.6)) return town ? 1 : 0;
  return 2;
}
