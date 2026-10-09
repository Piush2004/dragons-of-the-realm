import * as THREE from 'three';
import { GLTFLoader } from 'three/examples/jsm/loaders/GLTFLoader.js';
import type { FlyingMountTuning } from '../mounts/MountModel';
import { DragonModel } from './DragonModel';
import { DragonRig, type DragonMeta } from './DragonRig';

export type DragonId = 'caraxes' | 'vermithor' | 'vhagar';

export interface DragonProfile {
  id: DragonId;
  name: string;
  title: string;
  maxHealth: number;
  biteDamage: number;
  fireDps: number;
  /** Seconds between uses. */
  biteCooldown: number;
  fireCooldown: number;
  buffetCooldown: number;
  airTopSpeed: number;
  groundTopSpeed: number;
  turnRate: number;
  /** How aggressive and accurate it is when the AI flies it (0..1). */
  skill: number;
}

/** Stats follow the show: Caraxes is the fastest, Vermithor the second-largest, Vhagar the biggest and hardest-hitting. */
export const DRAGON_PROFILES: Record<DragonId, DragonProfile> = {
  caraxes: {
    id: 'caraxes', name: 'Caraxes', title: 'The Blood Wyrm',
    maxHealth: 3000, biteDamage: 170, fireDps: 210, biteCooldown: 1.7, fireCooldown: 6.5, buffetCooldown: 5,
    airTopSpeed: 64, groundTopSpeed: 13, turnRate: 1.15, skill: 0.8,
  },
  vermithor: {
    id: 'vermithor', name: 'Vermithor', title: 'The Bronze Fury',
    maxHealth: 4200, biteDamage: 210, fireDps: 250, biteCooldown: 2.1, fireCooldown: 7.5, buffetCooldown: 5.5,
    airTopSpeed: 50, groundTopSpeed: 11, turnRate: 0.9, skill: 0.7,
  },
  vhagar: {
    id: 'vhagar', name: 'Vhagar', title: 'Queen of All Dragons',
    maxHealth: 5600, biteDamage: 260, fireDps: 290, biteCooldown: 2.5, fireCooldown: 8.5, buffetCooldown: 6,
    airTopSpeed: 44, groundTopSpeed: 9, turnRate: 0.72, skill: 0.6,
  },
};

export const DRAGON_IDS = Object.keys(DRAGON_PROFILES) as DragonId[];

export interface DragonInstance {
  id: DragonId;
  profile: DragonProfile;
  model: DragonModel;
  tuning: FlyingMountTuning;
  meta: DragonMeta;
  /** Dragon size relative to Caraxes, used to scale cameras and effects. */
  scale: number;
}

export function tuningFor(profile: DragonProfile, meta: DragonMeta): FlyingMountTuning {
  const scale = meta.length / 25;
  return {
    mass: 2000 * scale ** 3,
    bodyRadius: meta.bodyRadius * 0.85,
    groundOffset: meta.groundOffset,
    groundTopSpeed: profile.groundTopSpeed,
    airTopSpeed: profile.airTopSpeed,
    glideMinSpeed: profile.airTopSpeed * 0.3,
    acceleration: 11,
    braking: 15,
    groundTurnRate: profile.turnRate * 1.1,
    airTurnRate: profile.turnRate,
    flapAcceleration: 20 * Math.sqrt(scale),
    maxClimbSpeed: 17,
    maxDiveSpeed: 36,
    maxBank: 0.7,
    groundedLabel: 'WALK',
  };
}

/** Loads the three Blender-built dragon models and creates rigged instances of them. */
export class DragonLibrary {
  private readonly sources = new Map<DragonId, { scene: THREE.Object3D; meta: DragonMeta }>();

  static async load(anisotropy: number): Promise<DragonLibrary> {
    const library = new DragonLibrary();
    const loader = new GLTFLoader();
    await Promise.all(
      DRAGON_IDS.map(async (id) => {
        const [gltf, metaResponse] = await Promise.all([loader.loadAsync(`/assets/dragons/${id}.glb`), fetch(`/assets/dragons/${id}.json`)]);
        if (!metaResponse.ok) throw new Error(`Missing rig data for ${id}`);
        const meta = (await metaResponse.json()) as DragonMeta;
        gltf.scene.traverse((child) => {
          if (!(child instanceof THREE.Mesh)) return;
          const materials = Array.isArray(child.material) ? child.material : [child.material];
          for (const material of materials as THREE.MeshStandardMaterial[]) {
            for (const map of [material.map, material.normalMap]) if (map) map.anisotropy = anisotropy;
          }
        });
        library.sources.set(id, { scene: gltf.scene, meta });
      }),
    );
    return library;
  }

  create(id: DragonId): DragonInstance {
    const source = this.sources.get(id);
    if (!source) throw new Error(`Dragon ${id} not loaded`);
    const profile = DRAGON_PROFILES[id];
    const rig = new DragonRig(source.scene, source.meta);
    return {
      id,
      profile,
      model: new DragonModel(rig),
      tuning: tuningFor(profile, source.meta),
      meta: source.meta,
      scale: source.meta.length / 25,
    };
  }
}
