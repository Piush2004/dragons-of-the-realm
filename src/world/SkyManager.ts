import * as THREE from 'three';
import { Sky } from 'three/examples/jsm/objects/Sky.js';

export interface TimeOfDayPreset {
  id: string;
  /** Sun elevation in degrees above the horizon. Low values (near 0) give long golden-hour light. */
  elevationDeg: number;
  azimuthDeg: number;
  turbidity: number;
  rayleigh: number;
  mieCoefficient: number;
  mieDirectionalG: number;
  sunLightColor: THREE.ColorRepresentation;
  sunLightIntensity: number;
  ambientColor: THREE.ColorRepresentation;
  groundAmbientColor: THREE.ColorRepresentation;
  fogColor: THREE.ColorRepresentation;
  fogNear: number;
  fogFar: number;
  exposure: number;
  /** HDR colours of the sunlit and shaded sides of clouds. */
  cloudLit: [number, number, number];
  cloudShade: [number, number, number];
}

export const TIME_OF_DAY_PRESETS: Record<string, TimeOfDayPreset> = {
  golden: {
    id: 'golden',
    elevationDeg: 16,
    azimuthDeg: 205,
    turbidity: 4.5,
    rayleigh: 1.9,
    mieCoefficient: 0.006,
    mieDirectionalG: 0.86,
    sunLightColor: 0xffdcae,
    sunLightIntensity: 4.2,
    ambientColor: 0xbcd4ff,
    groundAmbientColor: 0x6b5a3c,
    fogColor: 0xd9d4c8,
    fogNear: 400,
    fogFar: 5200,
    exposure: 0.55,
    cloudLit: [2.3, 1.95, 1.55],
    cloudShade: [0.62, 0.7, 0.9],
  },
  sunset: {
    id: 'sunset',
    elevationDeg: 4.5,
    azimuthDeg: 200,
    turbidity: 6,
    rayleigh: 3.2,
    mieCoefficient: 0.012,
    mieDirectionalG: 0.9,
    sunLightColor: 0xffb35c,
    sunLightIntensity: 3.4,
    ambientColor: 0xffb98a,
    groundAmbientColor: 0x3a2a1f,
    fogColor: 0xf3a46a,
    fogNear: 300,
    fogFar: 4200,
    exposure: 0.5,
    cloudLit: [3.0, 1.45, 0.75],
    cloudShade: [0.55, 0.34, 0.5],
  },
  dusk: {
    id: 'dusk',
    elevationDeg: 1.2,
    azimuthDeg: 215,
    turbidity: 9,
    rayleigh: 3.9,
    mieCoefficient: 0.02,
    mieDirectionalG: 0.93,
    sunLightColor: 0xff7a3d,
    sunLightIntensity: 2.4,
    ambientColor: 0x7c78b4,
    groundAmbientColor: 0x2a2030,
    fogColor: 0xb98a92,
    fogNear: 300,
    fogFar: 3600,
    exposure: 0.62,
    cloudLit: [2.4, 0.85, 0.55],
    cloudShade: [0.32, 0.26, 0.5],
  },
  midday: {
    id: 'midday',
    elevationDeg: 58,
    azimuthDeg: 160,
    turbidity: 3,
    rayleigh: 1.6,
    mieCoefficient: 0.005,
    mieDirectionalG: 0.8,
    sunLightColor: 0xffffff,
    sunLightIntensity: 4.6,
    ambientColor: 0xbfd9ff,
    groundAmbientColor: 0x3a2f20,
    fogColor: 0xcfe6ff,
    fogNear: 500,
    fogFar: 6000,
    exposure: 0.4,
    cloudLit: [2.2, 2.2, 2.2],
    cloudShade: [0.7, 0.8, 1.0],
  },
};

const SHADOW_HALF_EXTENT = 110;
const SUN_DISTANCE = 400;

/**
 * Physically-based sky dome + matching sun light + fog, driven by swappable time-of-day presets.
 * The sky is also baked into an environment map so every material is lit and reflects by the same sky.
 */
export class SkyManager {
  readonly sky: Sky;
  readonly sunLight: THREE.DirectionalLight;
  readonly hemiLight: THREE.HemisphereLight;
  private readonly scene: THREE.Scene;
  private readonly renderer: THREE.WebGLRenderer;
  private readonly sunDirection = new THREE.Vector3();
  private readonly shadowAnchor = new THREE.Vector3();
  private readonly pmrem: THREE.PMREMGenerator;
  private readonly envScene = new THREE.Scene();
  private readonly envSky = new Sky();
  private envTarget: THREE.WebGLRenderTarget | null = null;

  constructor(scene: THREE.Scene, renderer: THREE.WebGLRenderer) {
    this.scene = scene;
    this.renderer = renderer;
    this.pmrem = new THREE.PMREMGenerator(renderer);
    this.envSky.scale.setScalar(4500);
    this.envScene.add(this.envSky);

    this.sky = new Sky();
    this.sky.scale.setScalar(4500);
    scene.add(this.sky);

    this.sunLight = new THREE.DirectionalLight(0xffffff, 2);
    this.sunLight.castShadow = true;
    this.sunLight.shadow.mapSize.set(4096, 4096);
    this.sunLight.shadow.camera.left = -SHADOW_HALF_EXTENT;
    this.sunLight.shadow.camera.right = SHADOW_HALF_EXTENT;
    this.sunLight.shadow.camera.top = SHADOW_HALF_EXTENT;
    this.sunLight.shadow.camera.bottom = -SHADOW_HALF_EXTENT;
    this.sunLight.shadow.camera.near = 10;
    this.sunLight.shadow.camera.far = 1000;
    this.sunLight.shadow.bias = -0.0004;
    this.sunLight.shadow.normalBias = 0.06;
    scene.add(this.sunLight);
    scene.add(this.sunLight.target);

    this.hemiLight = new THREE.HemisphereLight(0xffffff, 0x000000, 0.15);
    scene.add(this.hemiLight);

    this.applyPreset(TIME_OF_DAY_PRESETS.golden);
  }

  /** Fires after a preset is applied, e.g. so clouds can re-tint themselves. */
  onPreset: (preset: TimeOfDayPreset) => void = () => {};
  current: TimeOfDayPreset = TIME_OF_DAY_PRESETS.golden;

  applyPreset(preset: TimeOfDayPreset): void {
    this.current = preset;
    const uniforms = this.sky.material.uniforms;
    uniforms.turbidity.value = preset.turbidity;
    uniforms.rayleigh.value = preset.rayleigh;
    uniforms.mieCoefficient.value = preset.mieCoefficient;
    uniforms.mieDirectionalG.value = preset.mieDirectionalG;

    const phi = THREE.MathUtils.degToRad(90 - preset.elevationDeg);
    const theta = THREE.MathUtils.degToRad(preset.azimuthDeg);
    this.sunDirection.setFromSphericalCoords(1, phi, theta);
    uniforms.sunPosition.value.copy(this.sunDirection);

    const envUniforms = this.envSky.material.uniforms;
    envUniforms.turbidity.value = preset.turbidity;
    envUniforms.rayleigh.value = preset.rayleigh;
    envUniforms.mieCoefficient.value = preset.mieCoefficient;
    envUniforms.mieDirectionalG.value = preset.mieDirectionalG;
    envUniforms.sunPosition.value.copy(this.sunDirection);
    const previousTarget = this.envTarget;
    this.envTarget = this.pmrem.fromScene(this.envScene, 0, 1, 10000);
    this.scene.environment = this.envTarget.texture;
    this.scene.environmentIntensity = 0.75;
    previousTarget?.dispose();

    this.renderer.toneMappingExposure = preset.exposure;

    this.sunLight.position.copy(this.sunDirection).multiplyScalar(SUN_DISTANCE);
    this.sunLight.target.position.set(0, 0, 0);
    this.sunLight.color.set(preset.sunLightColor);
    this.sunLight.intensity = preset.sunLightIntensity;

    this.hemiLight.color.set(preset.ambientColor);
    this.hemiLight.groundColor.set(preset.groundAmbientColor);

    this.scene.fog = new THREE.Fog(preset.fogColor, preset.fogNear, preset.fogFar);
    this.onPreset(preset);
  }

  /** Steps to the next time of day and returns it. */
  cyclePreset(): TimeOfDayPreset {
    const order = ['golden', 'midday', 'sunset', 'dusk'];
    const next = order[(order.indexOf(this.current.id) + 1) % order.length];
    this.applyPreset(TIME_OF_DAY_PRESETS[next]);
    return this.current;
  }

  /** Keeps the sun + shadow frustum centered on the player so shadows stay sharp while driving far from origin. */
  followTarget(position: THREE.Vector3): void {
    // Move the shadow frustum in whole-meter steps so shadow edges do not shimmer as the car moves.
    this.shadowAnchor.set(Math.round(position.x), Math.round(position.y), Math.round(position.z));
    this.sunLight.position.copy(this.sunDirection).multiplyScalar(SUN_DISTANCE).add(this.shadowAnchor);
    this.sunLight.target.position.copy(this.shadowAnchor);
  }
}
