import * as THREE from 'three';
import type { PhysicsWorld } from '../physics/PhysicsWorld';
import { Track } from './Track';
import { SkyManager } from './SkyManager';
import { Terrain, rollingHeight } from './Terrain';
import { Vegetation } from './Vegetation';
import { ObstacleField } from '../physics/ObstacleField';
import { createGrassTexture, createRoadTextures } from './ProceduralTextures';

const FOREST_RADIUS = 620;

/** Layout of the starter loop road (x, z) in meters. */
const VALLEY_LOOP: Array<[number, number]> = [
  [0, 0],
  [70, 45],
  [150, 30],
  [215, -40],
  [200, -130],
  [120, -190],
  [20, -215],
  [-80, -170],
  [-165, -110],
  [-210, -20],
  [-170, 70],
  [-80, 95],
];

/** The playable region: terrain, roads, vegetation, sky and lighting. */
export class World {
  readonly scene: THREE.Scene;
  readonly track: Track;
  readonly terrain: Terrain;
  readonly sky: SkyManager;
  readonly obstacles: ObstacleField;

  constructor(scene: THREE.Scene, physics: PhysicsWorld, renderer: THREE.WebGLRenderer) {
    this.scene = scene;
    const anisotropy = renderer.capabilities.getMaxAnisotropy();

    this.sky = new SkyManager(scene, renderer);

    this.track = new Track(VALLEY_LOOP, rollingHeight, createRoadTextures(anisotropy), 12, 32);
    scene.add(this.track.mesh);

    this.terrain = new Terrain(this.track.getCenterlineSamples(), physics, createGrassTexture(anisotropy));
    scene.add(this.terrain.mesh);

    const vegetation = new Vegetation(this.terrain, FOREST_RADIUS);
    scene.add(vegetation.group);
    this.obstacles = new ObstacleField(physics.world, vegetation.trunks);
  }
}
