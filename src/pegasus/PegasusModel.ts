import * as THREE from 'three';

/** Distance from the model origin (physics sphere centre) down to the hooves. */
export const PEGASUS_GROUND_OFFSET = 0.8;

const HIP_Y = 0.3;
const UPPER_LEG = 0.55;
const LOWER_LEG = 0.55;

export interface PegasusAnimationState {
  speed: number;
  grounded: boolean;
  /** -1 (diving) .. +1 (climbing hard). */
  climb: number;
}

interface Leg {
  hip: THREE.Group;
  knee: THREE.Group;
  /** Gallop phase offset as a fraction of the stride. */
  phase: number;
  front: boolean;
}

interface Wing {
  shoulder: THREE.Group;
  outer: THREE.Group;
}

/** Flat feathered wing panel in the XZ plane: span along +X, scalloped trailing edge toward -Z. */
function featherPanel(length: number, chord: number, feathers: number, taper: number): THREE.BufferGeometry {
  const chordAt = (x: number) => chord * (1 - taper * (x / length));
  const shape = new THREE.Shape();
  shape.moveTo(0, 0.12);
  shape.lineTo(length * 0.92, 0.06);
  shape.quadraticCurveTo(length * 1.04, 0, length, -chordAt(length) * 0.75);
  for (let i = feathers - 1; i >= 0; i--) {
    const x = (i / feathers) * length;
    const mid = x + (length / feathers) * 0.55;
    // Each feather bulges past the trailing edge, then tucks back in at its base.
    shape.quadraticCurveTo(mid, -chordAt(mid) * 1.12, x, -chordAt(x) * 0.8);
  }
  shape.lineTo(0, 0.12);
  const geometry = new THREE.ShapeGeometry(shape, 6);
  geometry.rotateX(Math.PI / 2);
  return geometry;
}

/** Procedural winged horse with gallop, wing-fold and wing-beat animation. +Z is forward. */
export class PegasusModel {
  readonly group = new THREE.Group();

  private readonly bodyPivot = new THREE.Group();
  private readonly legs: Leg[] = [];
  private readonly wings: Wing[] = [];
  private readonly tail = new THREE.Group();
  private readonly neck = new THREE.Group();

  private stridePhase = 0;
  private wingPhase = 0;
  /** 0 = wings folded against the body, 1 = fully spread. */
  private spread = 0;
  private flapAmplitude = 0;

  constructor() {
    const coat = new THREE.MeshStandardMaterial({ color: 0xf1ede4, roughness: 0.62 });
    const hair = new THREE.MeshStandardMaterial({ color: 0xe6cf8f, roughness: 0.75 });
    const hoof = new THREE.MeshStandardMaterial({ color: 0x2a2623, roughness: 0.5 });
    const dark = new THREE.MeshStandardMaterial({ color: 0x15110f, roughness: 0.3 });
    const feather = new THREE.MeshStandardMaterial({ color: 0xfbfaf6, roughness: 0.7, side: THREE.DoubleSide });
    const covert = new THREE.MeshStandardMaterial({ color: 0xdcd8cf, roughness: 0.75, side: THREE.DoubleSide });

    this.group.add(this.bodyPivot);
    const add = (geometry: THREE.BufferGeometry, material: THREE.Material, parent: THREE.Object3D = this.bodyPivot) => {
      const mesh = new THREE.Mesh(geometry, material);
      mesh.castShadow = true;
      mesh.receiveShadow = true;
      parent.add(mesh);
      return mesh;
    };

    // --- Torso ---
    const barrel = add(new THREE.CapsuleGeometry(0.36, 1.1, 8, 16), coat);
    barrel.rotation.x = Math.PI / 2;
    barrel.position.set(0, 0.58, 0);
    add(new THREE.SphereGeometry(0.43, 18, 14), coat).position.set(0, 0.62, 0.5);
    add(new THREE.SphereGeometry(0.44, 18, 14), coat).position.set(0, 0.64, -0.55);

    // --- Neck + head ---
    this.neck.position.set(0, 0.78, 0.7);
    this.bodyPivot.add(this.neck);
    const neckMesh = add(new THREE.CylinderGeometry(0.15, 0.27, 0.95, 12), coat, this.neck);
    neckMesh.position.set(0, 0.38, 0.2);
    neckMesh.rotation.x = 0.55;
    const head = new THREE.Group();
    head.position.set(0, 0.8, 0.46);
    head.rotation.x = 0.75;
    this.neck.add(head);
    const skull = add(new THREE.SphereGeometry(0.17, 12, 10), coat, head);
    skull.position.set(0, 0.05, 0);
    const muzzle = add(new THREE.CapsuleGeometry(0.1, 0.3, 6, 10), coat, head);
    muzzle.rotation.x = Math.PI / 2;
    muzzle.position.set(0, -0.02, 0.3);
    for (const side of [-1, 1]) {
      const ear = add(new THREE.ConeGeometry(0.045, 0.16, 6), coat, head);
      ear.position.set(side * 0.09, 0.2, -0.04);
      ear.rotation.x = -0.5;
      add(new THREE.SphereGeometry(0.028, 8, 8), dark, head).position.set(side * 0.13, 0.07, 0.1);
    }
    const mane = add(new THREE.BoxGeometry(0.06, 0.95, 0.2), hair, this.neck);
    mane.position.set(0, 0.42, 0.04);
    mane.rotation.x = 0.55;
    const forelock = add(new THREE.BoxGeometry(0.08, 0.2, 0.1), hair, head);
    forelock.position.set(0, 0.18, 0.06);

    // --- Tail ---
    this.tail.position.set(0, 0.82, -0.95);
    this.bodyPivot.add(this.tail);
    const tailMesh = add(new THREE.ConeGeometry(0.16, 1.0, 10), hair, this.tail);
    tailMesh.rotation.x = Math.PI;
    tailMesh.position.set(0, -0.5, 0);
    this.tail.rotation.x = 0.5;

    // --- Legs ---
    const legSpecs: Array<{ x: number; z: number; phase: number; front: boolean }> = [
      { x: 0.2, z: 0.52, phase: 0.0, front: true },
      { x: -0.2, z: 0.52, phase: 0.12, front: true },
      { x: 0.2, z: -0.58, phase: 0.5, front: false },
      { x: -0.2, z: -0.58, phase: 0.62, front: false },
    ];
    for (const spec of legSpecs) {
      const hip = new THREE.Group();
      hip.position.set(spec.x, HIP_Y, spec.z);
      this.bodyPivot.add(hip);
      add(new THREE.CylinderGeometry(spec.front ? 0.1 : 0.13, 0.07, UPPER_LEG, 10), coat, hip).position.y = -UPPER_LEG / 2;
      const knee = new THREE.Group();
      knee.position.y = -UPPER_LEG;
      hip.add(knee);
      add(new THREE.SphereGeometry(0.075, 10, 8), coat, knee);
      add(new THREE.CylinderGeometry(0.06, 0.05, LOWER_LEG - 0.1, 10), coat, knee).position.y = -(LOWER_LEG - 0.1) / 2;
      add(new THREE.CylinderGeometry(0.065, 0.08, 0.1, 10), hoof, knee).position.y = -LOWER_LEG + 0.05;
      this.legs.push({ hip, knee, phase: spec.phase, front: spec.front });
    }

    // --- Wings ---
    const innerLength = 1.25;
    const innerPanel = featherPanel(innerLength, 0.95, 5, 0.1);
    const outerPanel = featherPanel(1.75, 0.9, 8, 0.6);
    const innerCovert = featherPanel(innerLength, 0.5, 6, 0.15);
    const outerCovert = featherPanel(1.3, 0.42, 7, 0.55);
    const armGeometry = new THREE.CylinderGeometry(0.05, 0.07, innerLength, 8);
    armGeometry.rotateZ(Math.PI / 2);
    armGeometry.translate(innerLength / 2, 0, 0.08);

    for (const side of [1, -1]) {
      // Mirroring the whole wing root keeps one set of animation values for both sides.
      const root = new THREE.Group();
      root.position.set(side * 0.26, 0.92, 0.22);
      root.scale.x = side;
      this.bodyPivot.add(root);

      const shoulder = new THREE.Group();
      root.add(shoulder);
      add(innerPanel, feather, shoulder);
      add(innerCovert, covert, shoulder).position.y = 0.025;
      add(armGeometry, coat, shoulder);

      const outer = new THREE.Group();
      outer.position.x = innerLength;
      shoulder.add(outer);
      add(outerPanel, feather, outer);
      add(outerCovert, covert, outer).position.y = 0.025;

      this.wings.push({ shoulder, outer });
    }
  }

  update(deltaSeconds: number, state: PegasusAnimationState): void {
    const airborne = !state.grounded;
    const blend = (rate: number) => 1 - Math.exp(-rate * deltaSeconds);

    // --- Wings ---
    this.spread += ((airborne ? 1 : 0) - this.spread) * blend(airborne ? 7 : 4);
    // Hard climbs need big fast beats; fast level flight settles into a shallow glide rhythm.
    const effort = THREE.MathUtils.clamp(0.45 + state.climb * 0.55 - state.speed / 110, 0.12, 1);
    const targetAmplitude = airborne ? 0.28 + effort * 0.6 : 0;
    this.flapAmplitude += (targetAmplitude - this.flapAmplitude) * blend(5);
    this.wingPhase += deltaSeconds * Math.PI * 2 * (1.1 + effort * 1.5);

    const beat = Math.sin(this.wingPhase);
    const outerBeat = Math.sin(this.wingPhase - 0.9);
    const spread = this.spread;
    for (const wing of this.wings) {
      // Folded: raised and swept back along the flank. Spread: level, beating about the shoulder.
      wing.shoulder.rotation.set(
        0,
        THREE.MathUtils.lerp(1.25, 0.08, spread),
        THREE.MathUtils.lerp(0.8, 0.12 + beat * this.flapAmplitude, spread),
        'YZX',
      );
      wing.outer.rotation.set(
        0,
        THREE.MathUtils.lerp(2.3, 0.1 - Math.max(0, -outerBeat) * 0.25, spread),
        THREE.MathUtils.lerp(-0.5, outerBeat * this.flapAmplitude * 0.9 - 0.08, spread),
        'YZX',
      );
    }

    // --- Legs ---
    const strideRate = state.grounded ? THREE.MathUtils.clamp(state.speed / 5.5, 0, 3.2) : 0;
    this.stridePhase += deltaSeconds * strideRate * Math.PI * 2;
    const moving = THREE.MathUtils.clamp(state.speed / 4, 0, 1);
    const reach = 0.25 + THREE.MathUtils.clamp(state.speed / 18, 0, 1) * 0.55;
    for (const leg of this.legs) {
      let hipAngle: number;
      let kneeAngle: number;
      if (airborne) {
        // Tucked up under the body in flight.
        hipAngle = leg.front ? -0.9 : 0.55;
        kneeAngle = leg.front ? 1.7 : -1.3;
      } else {
        const phase = this.stridePhase + leg.phase * Math.PI * 2;
        hipAngle = Math.sin(phase) * reach * moving;
        // Knees only fold while the leg swings forward.
        const fold = Math.max(0, Math.cos(phase)) * (0.5 + reach) * moving;
        kneeAngle = leg.front ? fold : -fold * 0.8;
      }
      leg.hip.rotation.x += (hipAngle - leg.hip.rotation.x) * blend(14);
      leg.knee.rotation.x += (kneeAngle - leg.knee.rotation.x) * blend(14);
    }

    // --- Body motion ---
    const gallopBob = state.grounded ? Math.abs(Math.sin(this.stridePhase)) * 0.07 * moving : 0;
    const wingBob = airborne ? -beat * this.flapAmplitude * 0.13 : 0;
    this.bodyPivot.position.y = gallopBob + wingBob;
    this.bodyPivot.rotation.x = state.grounded ? Math.sin(this.stridePhase * 2) * 0.035 * moving : 0;
    this.neck.rotation.x = (airborne ? 0.35 : 0.05) + Math.sin(this.stridePhase) * 0.06 * moving;
    // Streams out behind as speed builds.
    this.tail.rotation.x = 0.45 + THREE.MathUtils.clamp(state.speed / 30, 0, 1) * 0.8;
    this.tail.rotation.z = Math.sin(this.stridePhase * 0.5 + this.wingPhase * 0.3) * 0.12;
  }
}
