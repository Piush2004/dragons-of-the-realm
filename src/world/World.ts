import * as THREE from 'three';
import type { PhysicsWorld } from '../physics/PhysicsWorld';
import { Track } from './Track';
import { SkyManager } from './SkyManager';
import { Terrain } from './Terrain';
import { Vegetation } from './Vegetation';
import { Grass } from './Grass';
import { Water } from './Water';
import { Settlements } from './Settlements';
import { AssetLibrary } from './AssetLibrary';
import { ObstacleField } from '../physics/ObstacleField';
import { createGrassTexture, createRoadTextures } from './ProceduralTextures';
import { WATER_LEVEL } from './WorldConfig';
import { LANDMARKS, ROADS, macroHeight } from './WorldMap';

const ASSET_FILES = ['/assets/environment.glb', '/assets/structures.glb', '/assets/landmarks.glb'];

/** Roads ride on the broad shape of the land but never dip under water. */
function roadHeight(x: number, z: number): number {
  return Math.max(macroHeight(x, z), WATER_LEVEL + 1.5);
}

/** The playable realm: terrain, roads, sea, castles, towns, forests, sky and lighting. */
export class World {
  readonly scene: THREE.Scene;
  readonly tracks: Track[];
  readonly terrain: Terrain;
  readonly sky: SkyManager;
  readonly obstacles: ObstacleField;
  readonly assets: AssetLibrary;
  readonly settlements: Settlements;
  private readonly vegetation: Vegetation;
  private readonly grass: Grass;
  private readonly water: Water;

  static async create(scene: THREE.Scene, physics: PhysicsWorld, renderer: THREE.WebGLRenderer): Promise<World> {
    const anisotropy = renderer.capabilities.getMaxAnisotropy();
    const assets = await AssetLibrary.load(ASSET_FILES, anisotropy);
    return new World(scene, physics, renderer, assets);
  }

  private constructor(scene: THREE.Scene, physics: PhysicsWorld, renderer: THREE.WebGLRenderer, assets: AssetLibrary) {
    this.scene = scene;
    this.assets = assets;
    const anisotropy = renderer.capabilities.getMaxAnisotropy();

    this.sky = new SkyManager(scene, renderer);

    const roadTextures = createRoadTextures(anisotropy);
    this.tracks = ROADS.map((road) => new Track(road.points, roadHeight, roadTextures, road.width, 8, false));
    for (const track of this.tracks) scene.add(track.mesh);

    const centerline = this.tracks.flatMap((track) => track.getCenterlineSamples());
    this.terrain = new Terrain(centerline, physics, createGrassTexture(anisotropy));
    scene.add(this.terrain.mesh);

    this.water = new Water();
    scene.add(this.water.mesh);

    // Castles stand on levelled platforms, with their foundations buried so they never float on a slope.
    for (const landmark of LANDMARKS) {
      const object = assets.instantiate(landmark.model);
      object.position.set(landmark.x, this.terrain.getHeightAt(landmark.x, landmark.z) - 0.4, landmark.z);
      object.rotation.y = landmark.yaw;
      object.scale.setScalar(landmark.scale);
      scene.add(object);
    }

    this.settlements = new Settlements(this.terrain, assets);
    scene.add(this.settlements.group);

    this.vegetation = new Vegetation(this.terrain, assets);
    scene.add(this.vegetation.group);
    this.obstacles = new ObstacleField(physics.world, this.vegetation.trunks);

    this.grass = new Grass(this.terrain, assets);
    scene.add(this.grass.mesh);
  }

  /** Height a creature stands at: the ground, or just under the surface where the water is deep. */
  getStandingHeight(x: number, z: number): number {
    return Math.max(this.terrain.getHeightAt(x, z), WATER_LEVEL - 1.4);
  }

  /** Per-frame streaming of detail around the viewer. */
  update(viewer: THREE.Vector3, deltaSeconds: number): void {
    this.vegetation.update(viewer);
    this.grass.update(viewer, deltaSeconds);
    this.water.update(deltaSeconds);
    this.sky.followTarget(viewer);
  }

  get treeCount(): number {
    return this.vegetation.treeCount;
  }
}
