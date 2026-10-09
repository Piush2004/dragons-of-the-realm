import * as THREE from 'three';
import { createNoise2D, fbm } from '../core/Noise';

/** Soft turbulent puff used by every particle; its shape and swirl give flames and smoke their billowing look. */
function createPuffTexture(): THREE.CanvasTexture {
  const size = 128;
  const noise = createNoise2D(4242);
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
      const r = Math.sqrt(nx * nx + ny * ny);
      const turbulence = fbm(noise, x / 22 + 3, y / 22 + 7, 4);
      // Radial falloff eaten into by noise, so the edge is ragged like a cloud rather than a disc.
      const body = Math.max(0, 1 - r * (0.82 + (turbulence - 0.5) * 0.9));
      const value = Math.min(1, body * 1.5);
      const o = (y * size + x) * 4;
      image.data[o] = image.data[o + 1] = image.data[o + 2] = 255 * (0.55 + turbulence * 0.45);
      image.data[o + 3] = 255 * value * value;
    }
  }
  context.putImageData(image, 0, 0);
  const texture = new THREE.CanvasTexture(canvas);
  texture.colorSpace = THREE.NoColorSpace;
  return texture;
}

const VERTEX = /* glsl */ `
  attribute vec3 iPos;
  attribute vec4 iParams; // size, age (0..1), seed, kind
  varying vec2 vUv;
  varying float vAge;
  varying float vSeed;
  varying float vKind;
  void main() {
    vUv = uv;
    vAge = iParams.y;
    vSeed = iParams.z;
    vKind = iParams.w;
    vec4 view = modelViewMatrix * vec4(iPos, 1.0);
    float angle = iParams.z * 6.2831 + iParams.y * (iParams.z - 0.5) * 3.0;
    vec2 corner = position.xy;
    vec2 rotated = vec2(corner.x * cos(angle) - corner.y * sin(angle), corner.x * sin(angle) + corner.y * cos(angle));
    view.xy += rotated * iParams.x;
    gl_Position = projectionMatrix * view;
  }
`;

const FRAGMENT = /* glsl */ `
  uniform sampler2D puff;
  uniform float additive;
  varying vec2 vUv;
  varying float vAge;
  varying float vSeed;
  varying float vKind;

  void main() {
    // Two samples drifting in opposite directions give the flame its rolling, boiling motion.
    vec2 swirl = vec2(vSeed * 3.1, vSeed * 7.7);
    vec4 a = texture2D(puff, vUv + swirl * 0.1 + vec2(vAge * 0.1, -vAge * 0.2));
    vec4 b = texture2D(puff, vec2(1.0 - vUv.x, vUv.y) * 1.1 - swirl * 0.07 + vec2(-vAge * 0.07, vAge * 0.15));
    float shape = clamp(a.a * 0.75 + b.a * 0.55 - 0.18, 0.0, 1.0);
    // Guarantee a soft round edge so no quad outline ever shows.
    shape *= smoothstep(0.5, 0.2, length(vUv - 0.5));
    float detail = (a.r + b.r) * 0.5;

    vec3 color;
    float alpha;
    if (vKind < 0.5) {
      // Flame: white-yellow core, orange body, red edge, then gone.
      float heat = clamp(1.0 - vAge * 1.15 + (detail - 0.5) * 0.35, 0.0, 1.0);
      // HDR values: the scene is tone-mapped afterwards, so flames need to be brighter than 1.0 to stay vivid.
      vec3 hot = vec3(1.0, 0.62, 0.12) * 3.6;
      vec3 mid = vec3(1.0, 0.27, 0.025) * 2.4;
      vec3 cool = vec3(0.6, 0.06, 0.008) * 1.0;
      color = heat > 0.5 ? mix(mid, hot, (heat - 0.5) / 0.5) : mix(cool, mid, heat / 0.5);
      // Self-lit: stays bright against the scene but is opaque enough that overlapping puffs build a solid, billowing mass.
      alpha = clamp(shape * 1.35, 0.0, 1.0) * smoothstep(1.0, 0.5, vAge) * smoothstep(0.0, 0.04, vAge) * 0.8;
    } else if (vKind < 1.5) {
      // Smoke: warm grey that darkens and thins as it spreads.
      float tone = 0.07 + detail * 0.09;
      color = vec3(tone * 1.05, tone, tone * 0.92);
      alpha = shape * smoothstep(1.0, 0.1, vAge) * smoothstep(0.0, 0.12, vAge) * 0.42;
    } else if (vKind < 2.5) {
      // Dust: pale earth colour.
      float tone = 0.5 + detail * 0.2;
      color = vec3(tone * 0.92, tone * 0.8, tone * 0.62);
      alpha = shape * smoothstep(1.0, 0.0, vAge) * smoothstep(0.0, 0.08, vAge) * 0.42;
    } else {
      // Ember: tiny bright spark.
      color = vec3(1.0, 0.62, 0.18) * 2.0;
      alpha = smoothstep(0.5, 0.0, length(vUv - 0.5)) * smoothstep(1.0, 0.7, vAge);
    }
    gl_FragColor = vec4(color, alpha);
    if (additive < 0.5) gl_FragColor.rgb = pow(gl_FragColor.rgb, vec3(0.9));
    if (gl_FragColor.a < 0.004) discard;
  }
`;

const KIND_FIRE = 0;
const KIND_SMOKE = 1;
const KIND_DUST = 2;
const KIND_EMBER = 3;

class Layer {
  readonly mesh: THREE.Mesh;
  private readonly capacity: number;
  private readonly positions: Float32Array;
  private readonly params: Float32Array;
  private readonly velocity: Float32Array;
  private readonly life: Float32Array;
  private readonly age: Float32Array;
  private readonly size0: Float32Array;
  private readonly size1: Float32Array;
  private readonly drag: Float32Array;
  private readonly lift: Float32Array;
  private readonly seed: Float32Array;
  private readonly kind: Float32Array;
  private readonly geometry: THREE.InstancedBufferGeometry;
  private readonly posAttr: THREE.InstancedBufferAttribute;
  private readonly paramAttr: THREE.InstancedBufferAttribute;
  count = 0;

  constructor(capacity: number, texture: THREE.Texture, additive: boolean) {
    this.capacity = capacity;
    this.positions = new Float32Array(capacity * 3);
    this.params = new Float32Array(capacity * 4);
    this.velocity = new Float32Array(capacity * 3);
    this.life = new Float32Array(capacity);
    this.age = new Float32Array(capacity);
    this.size0 = new Float32Array(capacity);
    this.size1 = new Float32Array(capacity);
    this.drag = new Float32Array(capacity);
    this.lift = new Float32Array(capacity);
    this.seed = new Float32Array(capacity);
    this.kind = new Float32Array(capacity);

    const quad = new THREE.PlaneGeometry(1, 1);
    this.geometry = new THREE.InstancedBufferGeometry();
    this.geometry.index = quad.index;
    this.geometry.setAttribute('position', quad.getAttribute('position'));
    this.geometry.setAttribute('uv', quad.getAttribute('uv'));
    this.posAttr = new THREE.InstancedBufferAttribute(this.positions, 3).setUsage(THREE.DynamicDrawUsage);
    this.paramAttr = new THREE.InstancedBufferAttribute(this.params, 4).setUsage(THREE.DynamicDrawUsage);
    this.geometry.setAttribute('iPos', this.posAttr);
    this.geometry.setAttribute('iParams', this.paramAttr);
    this.geometry.instanceCount = 0;

    const material = new THREE.ShaderMaterial({
      uniforms: { puff: { value: texture }, additive: { value: additive ? 1 : 0 } },
      vertexShader: VERTEX,
      fragmentShader: FRAGMENT,
      transparent: true,
      depthWrite: false,
      blending: additive ? THREE.AdditiveBlending : THREE.NormalBlending,
    });
    this.mesh = new THREE.Mesh(this.geometry, material);
    this.mesh.frustumCulled = false;
    this.mesh.renderOrder = additive ? 11 : 10;
  }

  spawn(
    kind: number, x: number, y: number, z: number, vx: number, vy: number, vz: number,
    life: number, size0: number, size1: number, drag: number, lift: number,
  ): void {
    if (this.count >= this.capacity) return;
    const i = this.count++;
    this.positions[i * 3] = x;
    this.positions[i * 3 + 1] = y;
    this.positions[i * 3 + 2] = z;
    this.velocity[i * 3] = vx;
    this.velocity[i * 3 + 1] = vy;
    this.velocity[i * 3 + 2] = vz;
    this.life[i] = life;
    this.age[i] = 0;
    this.size0[i] = size0;
    this.size1[i] = size1;
    this.drag[i] = drag;
    this.lift[i] = lift;
    this.seed[i] = Math.random();
    this.kind[i] = kind;
  }

  update(dt: number): void {
    let i = 0;
    while (i < this.count) {
      this.age[i] += dt;
      if (this.age[i] >= this.life[i]) {
        this.remove(i);
        continue;
      }
      const damping = Math.exp(-this.drag[i] * dt);
      this.velocity[i * 3] *= damping;
      this.velocity[i * 3 + 1] = this.velocity[i * 3 + 1] * damping + this.lift[i] * dt;
      this.velocity[i * 3 + 2] *= damping;
      this.positions[i * 3] += this.velocity[i * 3] * dt;
      this.positions[i * 3 + 1] += this.velocity[i * 3 + 1] * dt;
      this.positions[i * 3 + 2] += this.velocity[i * 3 + 2] * dt;
      const t = this.age[i] / this.life[i];
      // Ease the growth out: fast expansion at first, as a fireball does, then settling.
      const grow = 1 - (1 - t) * (1 - t);
      this.params[i * 4] = this.size0[i] + (this.size1[i] - this.size0[i]) * grow;
      this.params[i * 4 + 1] = t;
      this.params[i * 4 + 2] = this.seed[i];
      this.params[i * 4 + 3] = this.kind[i];
      i++;
    }
    this.geometry.instanceCount = this.count;
    this.posAttr.needsUpdate = true;
    this.paramAttr.needsUpdate = true;
  }

  private remove(i: number): void {
    const last = --this.count;
    if (i === last) return;
    for (let k = 0; k < 3; k++) {
      this.positions[i * 3 + k] = this.positions[last * 3 + k];
      this.velocity[i * 3 + k] = this.velocity[last * 3 + k];
    }
    this.life[i] = this.life[last];
    this.age[i] = this.age[last];
    this.size0[i] = this.size0[last];
    this.size1[i] = this.size1[last];
    this.drag[i] = this.drag[last];
    this.lift[i] = this.lift[last];
    this.seed[i] = this.seed[last];
    this.kind[i] = this.kind[last];
  }
}

const rand = (a: number, b: number) => a + Math.random() * (b - a);

/** Flame, smoke, dust and ember effects for dragon combat. */
export class Particles {
  readonly group = new THREE.Group();
  private readonly fire: Layer;
  private readonly smoke: Layer;
  /** A glow that lights the scene while flame is in the air. */
  readonly light = new THREE.PointLight(0xff8a2a, 0, 160, 1.6);
  private lightLevel = 0;
  private lightTarget = new THREE.Vector3();

  constructor() {
    const texture = createPuffTexture();
    this.smoke = new Layer(5000, texture, false);
    this.fire = new Layer(5000, texture, false);
    this.group.add(this.smoke.mesh, this.fire.mesh, this.light);
  }

  /**
   * Continuous fireball: a dense, fast cloud that boils outward in a widening cone, with smoke and
   * embers peeling off its edges. Call every frame while breathing; `scale` is the dragon's size.
   */
  emitBreath(origin: THREE.Vector3, dir: THREE.Vector3, dt: number, scale: number, strength = 1): void {
    const right = new THREE.Vector3().crossVectors(dir, new THREE.Vector3(0, 1, 0));
    if (right.lengthSq() < 1e-4) right.set(1, 0, 0);
    right.normalize();
    const up = new THREE.Vector3().crossVectors(right, dir).normalize();

    const fireCount = Math.round(520 * dt * strength);
    for (let i = 0; i < fireCount; i++) {
      const spread = 0.2;
      const a = Math.random() * Math.PI * 2;
      const r = Math.sqrt(Math.random()) * spread;
      const speed = rand(30, 52) * scale;
      const d = dir.clone().addScaledVector(right, Math.cos(a) * r).addScaledVector(up, Math.sin(a) * r).normalize();
      this.fire.spawn(
        KIND_FIRE,
        origin.x + rand(-0.4, 0.4) * scale, origin.y + rand(-0.4, 0.4) * scale, origin.z + rand(-0.4, 0.4) * scale,
        d.x * speed, d.y * speed + 1.5, d.z * speed,
        rand(0.8, 1.4), rand(1.4, 2.4) * scale, rand(7, 12) * scale, 1.5, 5 * scale,
      );
    }
    const smokeCount = Math.round(110 * dt * strength);
    for (let i = 0; i < smokeCount; i++) {
      const a = Math.random() * Math.PI * 2;
      const r = Math.sqrt(Math.random()) * 0.3;
      const speed = rand(10, 24) * scale;
      const d = dir.clone().addScaledVector(right, Math.cos(a) * r).addScaledVector(up, Math.sin(a) * r).normalize();
      this.smoke.spawn(
        KIND_SMOKE,
        origin.x + d.x * 6 * scale, origin.y + d.y * 6 * scale, origin.z + d.z * 6 * scale,
        d.x * speed, d.y * speed + 2, d.z * speed,
        rand(2.2, 3.4), rand(3, 5) * scale, rand(12, 20) * scale, 0.9, 3 * scale,
      );
    }
    const emberCount = Math.round(60 * dt * strength);
    for (let i = 0; i < emberCount; i++) {
      const d = dir.clone().add(new THREE.Vector3(rand(-0.4, 0.4), rand(-0.2, 0.5), rand(-0.4, 0.4))).normalize();
      const speed = rand(25, 60) * scale;
      this.fire.spawn(KIND_EMBER, origin.x, origin.y, origin.z, d.x * speed, d.y * speed, d.z * speed, rand(0.8, 1.6), rand(0.25, 0.5) * scale, 0.1, 0.8, -6);
    }
    this.lightTarget.copy(origin).addScaledVector(dir, 22 * scale);
    this.lightLevel = Math.max(this.lightLevel, strength);
  }

  /** The throat flaring before the blast: a small hot glow at the mouth. */
  emitCharge(origin: THREE.Vector3, dt: number, scale: number, glow: number): void {
    const count = Math.round(70 * dt * glow);
    for (let i = 0; i < count; i++) {
      this.fire.spawn(
        KIND_FIRE,
        origin.x + rand(-0.5, 0.5) * scale, origin.y + rand(-0.3, 0.5) * scale, origin.z + rand(-0.5, 0.5) * scale,
        rand(-2, 2), rand(0.5, 3), rand(-2, 2), rand(0.25, 0.5), rand(0.5, 1) * scale * glow, rand(0.1, 0.4) * scale, 2, 1,
      );
    }
    this.lightTarget.copy(origin);
    this.lightLevel = Math.max(this.lightLevel, glow * 0.35);
  }

  /** Radial flash where flame or a bite lands. */
  emitBurst(position: THREE.Vector3, scale: number, flames = 16): void {
    for (let i = 0; i < flames; i++) {
      const d = new THREE.Vector3(rand(-1, 1), rand(-0.2, 1), rand(-1, 1)).normalize();
      const speed = rand(6, 22) * scale;
      this.fire.spawn(KIND_FIRE, position.x, position.y, position.z, d.x * speed, d.y * speed, d.z * speed, rand(0.5, 0.9), rand(1.2, 2.4) * scale, rand(4, 8) * scale, 2.2, 4);
    }
    for (let i = 0; i < flames / 2; i++) {
      const d = new THREE.Vector3(rand(-1, 1), rand(0, 1), rand(-1, 1)).normalize();
      const speed = rand(3, 10) * scale;
      this.smoke.spawn(KIND_SMOKE, position.x, position.y, position.z, d.x * speed, d.y * speed, d.z * speed, rand(1.4, 2.4), rand(2, 3.5) * scale, rand(7, 12) * scale, 1.2, 2);
    }
    this.lightTarget.copy(position);
    this.lightLevel = Math.max(this.lightLevel, 0.6);
  }

  emitDust(position: THREE.Vector3, count: number, scale: number, velocity?: THREE.Vector3): void {
    for (let i = 0; i < count; i++) {
      const spread = 3.5 * scale;
      this.smoke.spawn(
        KIND_DUST,
        position.x + rand(-1, 1) * scale, position.y + rand(0, 0.6), position.z + rand(-1, 1) * scale,
        (velocity?.x ?? 0) * 0.25 + rand(-spread, spread), rand(0.5, 4) * scale, (velocity?.z ?? 0) * 0.25 + rand(-spread, spread),
        rand(1.2, 2.2), rand(1.2, 2.2) * scale, rand(4, 8) * scale, 1.4, 0.6,
      );
    }
  }

  update(dt: number): void {
    this.fire.update(dt);
    this.smoke.update(dt);
    this.light.position.copy(this.lightTarget);
    this.light.intensity = this.lightLevel * 3.2e4 * (0.85 + Math.random() * 0.3);
    this.lightLevel = Math.max(0, this.lightLevel - dt * 2.4);
  }

  get liveCount(): number {
    return this.fire.count + this.smoke.count;
  }
}
