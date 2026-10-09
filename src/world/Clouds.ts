import * as THREE from 'three';
import { createNoise2D, fbm, mulberry32 } from '../core/Noise';

const CLOUD_COUNT = 46;
const PUFFS_PER_CLOUD = 11;
const RING = 6500;
const WIND = new THREE.Vector2(7, 2.5);

/** Soft cumulus puff with baked top lighting: bright crown, shadowed belly, ragged edge. */
function createCloudTexture(): THREE.CanvasTexture {
  const size = 256;
  const noise = createNoise2D(777);
  const canvas = document.createElement('canvas');
  canvas.width = size;
  canvas.height = size;
  const context = canvas.getContext('2d');
  if (!context) throw new Error('2D canvas context unavailable');
  const image = context.createImageData(size, size);
  for (let y = 0; y < size; y++) {
    for (let x = 0; x < size; x++) {
      const nx = (x / size) * 2 - 1;
      const ny = (y / size) * 2 - 1;
      const r = Math.sqrt(nx * nx * 0.9 + ny * ny * 1.25);
      const turbulence = fbm(noise, x / 38 + 5, y / 38 + 11, 5);
      const body = Math.max(0, 1 - r * (0.78 + (turbulence - 0.5) * 0.85));
      const alpha = Math.min(1, body * 1.7);
      // Canvas y grows downward, so small y is the top of the cloud.
      const top = 1 - (y / size);
      const light = Math.min(1, 0.25 + top * 0.8 + (turbulence - 0.5) * 0.5);
      const o = (y * size + x) * 4;
      image.data[o] = image.data[o + 1] = image.data[o + 2] = 255 * light;
      image.data[o + 3] = 255 * alpha * alpha;
    }
  }
  context.putImageData(image, 0, 0);
  const texture = new THREE.CanvasTexture(canvas);
  texture.colorSpace = THREE.NoColorSpace;
  return texture;
}

const VERTEX = /* glsl */ `
  attribute vec3 iPos;
  attribute vec2 iShape; // size, seed
  varying vec2 vUv;
  varying float vDistance;
  varying float vSeed;
  void main() {
    vUv = uv;
    vSeed = iShape.y;
    vec4 view = modelViewMatrix * vec4(iPos, 1.0);
    vDistance = length(view.xyz);
    float angle = (iShape.y - 0.5) * 0.5;
    vec2 corner = position.xy * vec2(1.35, 0.8);
    view.xy += vec2(corner.x * cos(angle) - corner.y * sin(angle), corner.x * sin(angle) + corner.y * cos(angle)) * iShape.x;
    gl_Position = projectionMatrix * view;
  }
`;

const FRAGMENT = /* glsl */ `
  uniform sampler2D map;
  uniform vec3 lit;
  uniform vec3 shade;
  varying vec2 vUv;
  varying float vDistance;
  varying float vSeed;
  void main() {
    vec4 tex = texture2D(map, vec2(vUv.x, vUv.y));
    // Guarantee a soft round edge so no quad outline ever shows.
    tex.a *= smoothstep(0.5, 0.16, length((vUv - 0.5) * vec2(1.0, 1.15)));
    float fadeNear = smoothstep(250.0, 1100.0, vDistance);
    float fadeFar = 1.0 - smoothstep(5200.0, 7400.0, vDistance);
    vec3 color = mix(shade, lit, tex.r);
    gl_FragColor = vec4(color, tex.a * fadeNear * fadeFar * 0.9);
    if (gl_FragColor.a < 0.004) discard;
  }
`;

/** A drifting field of cumulus that follows the viewer, so the sky always has depth and scale. */
export class Clouds {
  readonly mesh: THREE.Mesh;
  private readonly centers: Float32Array;
  private readonly offsets: Float32Array;
  private readonly sizes: Float32Array;
  private readonly seeds: Float32Array;
  private readonly positions: Float32Array;
  private readonly shape: Float32Array;
  private readonly posAttr: THREE.InstancedBufferAttribute;
  private readonly material: THREE.ShaderMaterial;
  private drift = new THREE.Vector2();

  constructor() {
    const rng = mulberry32(2024);
    const total = CLOUD_COUNT * PUFFS_PER_CLOUD;
    this.centers = new Float32Array(CLOUD_COUNT * 3);
    this.offsets = new Float32Array(total * 3);
    this.sizes = new Float32Array(total);
    this.seeds = new Float32Array(total);
    this.positions = new Float32Array(total * 3);
    this.shape = new Float32Array(total * 2);

    for (let c = 0; c < CLOUD_COUNT; c++) {
      const angle = rng() * Math.PI * 2;
      const radius = Math.sqrt(rng()) * RING;
      this.centers[c * 3] = Math.cos(angle) * radius;
      this.centers[c * 3 + 1] = 520 + rng() * 420;
      this.centers[c * 3 + 2] = Math.sin(angle) * radius;
      const span = 260 + rng() * 380;
      for (let p = 0; p < PUFFS_PER_CLOUD; p++) {
        const i = c * PUFFS_PER_CLOUD + p;
        const t = rng() * Math.PI * 2;
        const r = Math.sqrt(rng());
        this.offsets[i * 3] = Math.cos(t) * r * span;
        this.offsets[i * 3 + 1] = (rng() - 0.35) * span * 0.18 + (1 - r) * span * 0.12;
        this.offsets[i * 3 + 2] = Math.sin(t) * r * span * 0.7;
        this.sizes[i] = 190 + rng() * 260 - r * 60;
        this.seeds[i] = rng();
        this.shape[i * 2] = this.sizes[i];
        this.shape[i * 2 + 1] = this.seeds[i];
      }
    }

    const quad = new THREE.PlaneGeometry(1, 1);
    const geometry = new THREE.InstancedBufferGeometry();
    geometry.index = quad.index;
    geometry.setAttribute('position', quad.getAttribute('position'));
    geometry.setAttribute('uv', quad.getAttribute('uv'));
    this.posAttr = new THREE.InstancedBufferAttribute(this.positions, 3).setUsage(THREE.DynamicDrawUsage);
    geometry.setAttribute('iPos', this.posAttr);
    geometry.setAttribute('iShape', new THREE.InstancedBufferAttribute(this.shape, 2));
    geometry.instanceCount = total;

    this.material = new THREE.ShaderMaterial({
      uniforms: {
        map: { value: createCloudTexture() },
        lit: { value: new THREE.Color(2.0, 1.9, 1.75) },
        shade: { value: new THREE.Color(0.7, 0.78, 0.95) },
      },
      vertexShader: VERTEX,
      fragmentShader: FRAGMENT,
      transparent: true,
      depthWrite: false,
      fog: false,
    });
    this.mesh = new THREE.Mesh(geometry, this.material);
    this.mesh.frustumCulled = false;
    this.mesh.renderOrder = 4;
  }

  /** Sun-lit and shaded colours, as HDR values (the scene is tone-mapped afterwards). */
  setLighting(lit: THREE.ColorRepresentation, shade: THREE.ColorRepresentation): void {
    (this.material.uniforms.lit.value as THREE.Color).set(lit);
    (this.material.uniforms.shade.value as THREE.Color).set(shade);
  }

  update(viewer: THREE.Vector3, deltaSeconds: number): void {
    this.drift.addScaledVector(WIND, deltaSeconds);
    const wrap = RING * 2;
    for (let c = 0; c < CLOUD_COUNT; c++) {
      // Keep each cloud within a square window centred on the viewer, so the field never runs out.
      let x = this.centers[c * 3] + this.drift.x - viewer.x;
      let z = this.centers[c * 3 + 2] + this.drift.y - viewer.z;
      x = ((((x + RING) % wrap) + wrap) % wrap) - RING;
      z = ((((z + RING) % wrap) + wrap) % wrap) - RING;
      const cx = viewer.x + x;
      const cz = viewer.z + z;
      const cy = this.centers[c * 3 + 1];
      for (let p = 0; p < PUFFS_PER_CLOUD; p++) {
        const i = c * PUFFS_PER_CLOUD + p;
        this.positions[i * 3] = cx + this.offsets[i * 3];
        this.positions[i * 3 + 1] = cy + this.offsets[i * 3 + 1];
        this.positions[i * 3 + 2] = cz + this.offsets[i * 3 + 2];
      }
    }
    this.posAttr.needsUpdate = true;
  }
}
