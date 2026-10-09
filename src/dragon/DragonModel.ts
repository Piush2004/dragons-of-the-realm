import * as THREE from 'three';
import type { MountAnimationState, MountModel } from '../mounts/MountModel';

/** Distance from the model origin (physics sphere centre) down to the feet. */
export const DRAGON_GROUND_OFFSET = 2.05;

const NECK_SEGMENTS = 11;
const NECK_SEGMENT_LENGTH = 0.44;
const TAIL_SEGMENTS = 18;
const TAIL_SEGMENT_LENGTH = 0.5;

/** Per-segment neck pitch (radians, + = nose down). Flying: low swan-like S. Ground: reared up, head level. */
const NECK_FLY_CURVE = [-0.2, -0.2, -0.14, 0.0, 0.2, 0.28, 0.28, 0.2, 0.04, -0.16, -0.26];
const NECK_GROUND_CURVE = [-0.34, -0.3, -0.24, -0.16, -0.06, 0.08, 0.2, 0.28, 0.28, 0.2, 0.1];

const V = (x: number, y: number, z: number) => new THREE.Vector3(x, y, z);

/** Wing skeleton directions for the left wing (+X). Right wing mirrors X. */
const WING = {
  shoulder: V(0.55, 0.5, 0.95),
  hip: V(0.5, 0.05, -1.9),
  upperArm: 1.9,
  forearm: 2.5,
  fingers: [3.7, 4.1, 3.6, 2.9],
  /** In-plane fan angle of each finger, from pointing outward (0) toward the tail (+). */
  fingerFan: [-0.12, 0.34, 0.82, 1.3],
  foldedUpperArm: V(0.45, 0.62, -0.62).normalize(),
  foldedForearm: V(0.28, -0.72, 0.62).normalize(),
  foldedFinger: V(0.1, 0.5, -0.86).normalize(),
};

interface WingRig {
  side: number;
  membrane: THREE.BufferGeometry;
  positions: Float32Array;
  bones: THREE.Mesh[];
  claw: THREE.Mesh;
}

interface LegRig {
  hip: THREE.Group;
  knee: THREE.Group;
  ankle: THREE.Group;
  phase: number;
}

const UP = V(0, 1, 0);
const WORLD_UP = V(0, 1, 0);

/** Stretches a unit-height cylinder between two points. */
function placeBone(bone: THREE.Mesh, from: THREE.Vector3, to: THREE.Vector3): void {
  const direction = to.clone().sub(from);
  const length = direction.length();
  bone.position.copy(from).addScaledVector(direction, 0.5);
  bone.quaternion.setFromUnitVectors(UP, direction.divideScalar(length || 1));
  bone.scale.set(1, length, 1);
}

/**
 * Original procedural red dragon (wyvern build): chained neck and tail with follow-through,
 * skeletal bat wings with a per-frame membrane, hind legs, jaw. +Z is forward.
 */
export class DragonModel implements MountModel {
  readonly group = new THREE.Group();

  private readonly bodyPivot = new THREE.Group();
  private readonly neck: THREE.Group[] = [];
  private readonly tail: THREE.Group[] = [];
  private readonly head = new THREE.Group();
  private readonly jaw = new THREE.Group();
  private readonly wings: WingRig[] = [];
  private readonly legs: LegRig[] = [];

  /** Per-segment look offsets; the head leads and each segment trails the one in front of it. */
  private readonly neckYaw = new Array<number>(NECK_SEGMENTS).fill(0);
  private readonly neckPitch = new Array<number>(NECK_SEGMENTS).fill(0);
  /** Tail sway; the base leads and the tip trails. */
  private readonly tailYaw = new Array<number>(TAIL_SEGMENTS).fill(0);
  private readonly tailPitch = new Array<number>(TAIL_SEGMENTS).fill(0);

  private readonly parentWorld = new THREE.Quaternion();
  private readonly offsetQuaternion = new THREE.Quaternion();
  private readonly headOffset = new THREE.Euler();
  private readonly basis = new THREE.Matrix4();
  private readonly forward = new THREE.Vector3();
  private readonly upward = new THREE.Vector3();
  private readonly rightward = new THREE.Vector3();

  private time = 0;
  private wingPhase = 0;
  private stridePhase = 0;
  private spread = 0;
  private flapAmplitude = 0;
  private jawOpen = 0;
  private glance = 0;

  constructor() {
    const scales = new THREE.MeshStandardMaterial({ color: 0x8a1d1a, roughness: 0.55, metalness: 0.1 });
    const darkScales = new THREE.MeshStandardMaterial({ color: 0x4f1211, roughness: 0.6, metalness: 0.1 });
    const belly = new THREE.MeshStandardMaterial({ color: 0xb5562c, roughness: 0.6 });
    const horn = new THREE.MeshStandardMaterial({ color: 0x2a1a17, roughness: 0.45 });
    const membraneMaterial = new THREE.MeshStandardMaterial({
      color: 0xa8302a,
      roughness: 0.6,
      side: THREE.DoubleSide,
      // Faint self-glow stands in for sunlight passing through the thin wing skin.
      emissive: 0x2a0704,
    });
    const tooth = new THREE.MeshStandardMaterial({ color: 0xe9e2cf, roughness: 0.4 });
    const eye = new THREE.MeshStandardMaterial({ color: 0xffc23a, emissive: 0xffa010, emissiveIntensity: 1.6 });

    this.group.add(this.bodyPivot);
    const add = (geometry: THREE.BufferGeometry, material: THREE.Material, parent: THREE.Object3D) => {
      const mesh = new THREE.Mesh(geometry, material);
      mesh.castShadow = true;
      mesh.receiveShadow = true;
      parent.add(mesh);
      return mesh;
    };
    const ellipsoid = (sx: number, sy: number, sz: number) => {
      const geometry = new THREE.SphereGeometry(1, 20, 16);
      geometry.scale(sx, sy, sz);
      return geometry;
    };
    /** Tapered tube running from the segment origin along +Z. */
    const tube = (radiusNear: number, radiusFar: number, length: number) => {
      const geometry = new THREE.CylinderGeometry(radiusFar, radiusNear, length, 12);
      geometry.rotateX(Math.PI / 2);
      geometry.translate(0, 0, length / 2);
      return geometry;
    };
    const spike = (height: number, radius: number) => {
      const geometry = new THREE.ConeGeometry(radius, height, 5);
      geometry.translate(0, height / 2, 0);
      return geometry;
    };

    // --- Torso ---
    add(ellipsoid(0.8, 0.85, 1.35), scales, this.bodyPivot).position.set(0, 0, 0.55);
    add(ellipsoid(0.66, 0.68, 1.45), scales, this.bodyPivot).position.set(0, -0.05, -0.85);
    add(ellipsoid(0.6, 0.6, 1.0), scales, this.bodyPivot).position.set(0, -0.02, -1.95);
    add(ellipsoid(0.6, 0.45, 1.9), belly, this.bodyPivot).position.set(0, -0.42, -0.3);
    for (let i = 0; i < 9; i++) {
      const z = 1.2 - i * 0.42;
      const dorsal = add(spike(0.42 - Math.abs(i - 3) * 0.03, 0.09), horn, this.bodyPivot);
      dorsal.position.set(0, 0.7 - Math.abs(i - 2) * 0.03, z);
      dorsal.rotation.x = -0.5;
    }

    // --- Neck chain ---
    let parent: THREE.Object3D = this.bodyPivot;
    for (let i = 0; i < NECK_SEGMENTS; i++) {
      const t = i / (NECK_SEGMENTS - 1);
      const segment = new THREE.Group();
      if (i === 0) segment.position.set(0, 0.22, 1.6);
      else segment.position.z = NECK_SEGMENT_LENGTH;
      parent.add(segment);
      const near = THREE.MathUtils.lerp(0.5, 0.2, t);
      const far = THREE.MathUtils.lerp(0.5, 0.2, Math.min(1, t + 1 / NECK_SEGMENTS));
      add(tube(near, far, NECK_SEGMENT_LENGTH + 0.04), scales, segment);
      add(new THREE.SphereGeometry(near, 12, 10), scales, segment);
      const crest = add(spike(0.26 - t * 0.08, 0.06), horn, segment);
      crest.position.set(0, near * 0.9, NECK_SEGMENT_LENGTH * 0.5);
      crest.rotation.x = -0.6;
      const throat = add(new THREE.SphereGeometry(near * 0.7, 8, 6), belly, segment);
      throat.position.set(0, -near * 0.45, NECK_SEGMENT_LENGTH * 0.5);
      this.neck.push(segment);
      parent = segment;
    }

    // --- Head ---
    this.head.position.z = NECK_SEGMENT_LENGTH;
    parent.add(this.head);
    add(ellipsoid(0.3, 0.26, 0.42), scales, this.head).position.set(0, 0.04, 0.22);
    add(ellipsoid(0.2, 0.15, 0.5), scales, this.head).position.set(0, 0.03, 0.78);
    add(ellipsoid(0.24, 0.1, 0.2), darkScales, this.head).position.set(0, 0.2, 0.3);
    this.jaw.position.set(0, -0.08, 0.2);
    this.head.add(this.jaw);
    add(ellipsoid(0.17, 0.07, 0.48), darkScales, this.jaw).position.set(0, -0.04, 0.5);
    for (let i = 0; i < 5; i++) {
      for (const side of [-1, 1]) {
        const upper = add(spike(0.09, 0.022), tooth, this.head);
        upper.position.set(side * (0.15 - i * 0.012), -0.06, 0.62 + i * 0.13);
        upper.rotation.x = Math.PI;
        const lower = add(spike(0.07, 0.02), tooth, this.jaw);
        lower.position.set(side * (0.13 - i * 0.012), 0.0, 0.42 + i * 0.12);
      }
    }
    for (const side of [-1, 1]) {
      add(new THREE.SphereGeometry(0.055, 10, 8), eye, this.head).position.set(side * 0.24, 0.13, 0.42);
      const brow = add(spike(0.2, 0.05), horn, this.head);
      brow.position.set(side * 0.2, 0.2, 0.36);
      brow.rotation.set(-1.0, 0, side * -0.5);
      // Swept-back main horns plus a smaller pair, as on the reference.
      const mainHorn = add(spike(0.85, 0.09), horn, this.head);
      mainHorn.position.set(side * 0.2, 0.2, 0.02);
      mainHorn.rotation.set(-1.25, 0, side * -0.3);
      const smallHorn = add(spike(0.5, 0.06), horn, this.head);
      smallHorn.position.set(side * 0.27, 0.05, 0.0);
      smallHorn.rotation.set(-1.45, 0, side * -0.55);
      const cheek = add(spike(0.3, 0.05), horn, this.head);
      cheek.position.set(side * 0.25, -0.08, 0.1);
      cheek.rotation.set(-1.7, 0, side * -0.9);
    }

    // --- Tail chain (root is turned around so each segment's +Z points backward) ---
    const tailRoot = new THREE.Group();
    tailRoot.position.set(0, 0, -2.7);
    tailRoot.rotation.y = Math.PI;
    this.bodyPivot.add(tailRoot);
    parent = tailRoot;
    for (let i = 0; i < TAIL_SEGMENTS; i++) {
      const t = i / (TAIL_SEGMENTS - 1);
      const segment = new THREE.Group();
      if (i > 0) segment.position.z = TAIL_SEGMENT_LENGTH;
      parent.add(segment);
      const near = THREE.MathUtils.lerp(0.5, 0.05, Math.pow(t, 0.8));
      const far = THREE.MathUtils.lerp(0.5, 0.05, Math.pow(Math.min(1, t + 1 / TAIL_SEGMENTS), 0.8));
      add(tube(near, far, TAIL_SEGMENT_LENGTH + 0.03), scales, segment);
      add(new THREE.SphereGeometry(near, 10, 8), scales, segment);
      if (i % 2 === 0 && i < TAIL_SEGMENTS - 3) {
        const ridge = add(spike(0.3 * (1 - t) + 0.08, 0.05), horn, segment);
        ridge.position.set(0, near * 0.9, TAIL_SEGMENT_LENGTH * 0.4);
        ridge.rotation.x = 0.6;
      }
      this.tail.push(segment);
      parent = segment;
    }
    // Orange tail fin, like the underside flash in the reference.
    const finShape = new THREE.Shape();
    finShape.moveTo(0, 0);
    finShape.quadraticCurveTo(0.55, -0.5, 0.1, -1.5);
    finShape.quadraticCurveTo(0, -1.0, -0.1, -1.5);
    finShape.quadraticCurveTo(-0.55, -0.5, 0, 0);
    const finGeometry = new THREE.ShapeGeometry(finShape, 8);
    finGeometry.rotateX(-Math.PI / 2);
    const fin = add(finGeometry, new THREE.MeshStandardMaterial({ color: 0xd8742a, roughness: 0.6, side: THREE.DoubleSide }), parent);
    fin.position.z = -0.2;
    fin.rotation.y = Math.PI;

    // --- Hind legs ---
    for (const side of [1, -1]) {
      const hip = new THREE.Group();
      hip.position.set(side * 0.55, -0.25, -1.75);
      this.bodyPivot.add(hip);
      add(ellipsoid(0.3, 0.55, 0.36), scales, hip).position.set(0, -0.4, 0);
      const knee = new THREE.Group();
      knee.position.y = -0.9;
      hip.add(knee);
      const shin = add(new THREE.CylinderGeometry(0.17, 0.1, 0.9, 10), scales, knee);
      shin.position.y = -0.45;
      const ankle = new THREE.Group();
      ankle.position.y = -0.9;
      knee.add(ankle);
      add(ellipsoid(0.16, 0.1, 0.3), darkScales, ankle).position.set(0, -0.04, 0.16);
      for (const toe of [-1, 0, 1]) {
        const claw = add(spike(0.3, 0.05), horn, ankle);
        claw.position.set(toe * 0.11, -0.04, 0.4);
        claw.rotation.set(1.75, 0, toe * -0.25);
      }
      this.legs.push({ hip, knee, ankle, phase: side > 0 ? 0 : 0.5 });
    }

    // --- Wings ---
    const boneGeometry = new THREE.CylinderGeometry(1, 1, 1, 8);
    const boneRadii = [0.17, 0.13, 0.055, 0.05, 0.045, 0.04];
    // 4 body/arm triangles + 2 per finger gap (3 gaps) + 2 for the trailing gap to the hip.
    const triangleCount = 3 + 3 * 2 + 2;
    for (const side of [1, -1]) {
      const positions = new Float32Array(triangleCount * 9);
      const membrane = new THREE.BufferGeometry();
      membrane.setAttribute('position', new THREE.BufferAttribute(positions, 3));
      const membraneMesh = new THREE.Mesh(membrane, membraneMaterial);
      membraneMesh.castShadow = true;
      membraneMesh.frustumCulled = false;
      this.bodyPivot.add(membraneMesh);

      const bones = boneRadii.map((radius) => {
        const bone = add(boneGeometry, darkScales, this.bodyPivot);
        bone.scale.set(radius, 1, radius);
        bone.userData.radius = radius;
        return bone;
      });
      const claw = add(spike(0.45, 0.07), horn, this.bodyPivot);
      this.wings.push({ side, membrane, positions, bones, claw });
    }
  }

  update(deltaSeconds: number, state: MountAnimationState): void {
    const dt = Math.min(deltaSeconds, 0.05);
    this.time += dt;
    const airborne = !state.grounded;
    const blend = (rate: number) => 1 - Math.exp(-rate * dt);

    this.spread += ((airborne ? 1 : 0) - this.spread) * blend(airborne ? 5 : 3);
    const spread = this.spread;

    // Big slow beats when climbing, shallow ones in a fast glide.
    const effort = THREE.MathUtils.clamp(0.45 + state.climb * 0.55 - state.speed / 140, 0.1, 1);
    this.flapAmplitude += ((airborne ? 0.22 + effort * 0.62 : 0) - this.flapAmplitude) * blend(4);
    this.wingPhase += dt * Math.PI * 2 * (0.55 + effort * 0.75);
    const beat = Math.sin(this.wingPhase);

    this.updateWings(spread);
    this.updateNeck(state, beat, blend);
    this.updateTail(state, blend);
    this.updateLegs(dt, state, blend);

    // The body rises on each downstroke and sinks on the upstroke.
    const strideBob = state.grounded ? Math.abs(Math.sin(this.stridePhase)) * 0.1 * Math.min(1, state.speed / 3) : 0;
    this.bodyPivot.position.y = -Math.cos(this.wingPhase) * this.flapAmplitude * 0.28 * spread + strideBob;
    this.bodyPivot.rotation.x = THREE.MathUtils.lerp(-0.12, beat * this.flapAmplitude * 0.05, spread);
  }

  private updateWings(spread: number): void {
    const flap = Math.sin(this.wingPhase) * this.flapAmplitude;
    // The outer wing lags the arm, which is what makes the stroke look like it pushes air.
    const outerFlap = Math.sin(this.wingPhase - 0.85) * this.flapAmplitude;
    const tipFlap = Math.sin(this.wingPhase - 1.5) * this.flapAmplitude;
    // Wings fold in a little on the upstroke.
    const tuck = Math.max(0, Math.cos(this.wingPhase - 0.4)) * this.flapAmplitude * 0.35;

    for (const wing of this.wings) {
      const mirror = (v: THREE.Vector3) => v.setX(v.x * wing.side);
      const direction = (spreadDir: THREE.Vector3, foldedDir: THREE.Vector3) =>
        mirror(foldedDir.clone().lerp(spreadDir, spread).normalize());

      const armAngle = 0.12 + flap * 0.75;
      const foreAngle = 0.05 + outerFlap * 1.0;
      const handAngle = -0.02 + tipFlap * 1.15;

      const shoulder = mirror(WING.shoulder.clone());
      const hip = mirror(WING.hip.clone());
      const elbow = shoulder
        .clone()
        .addScaledVector(direction(V(Math.cos(armAngle), Math.sin(armAngle), 0.32 - tuck * 0.4), WING.foldedUpperArm), WING.upperArm);
      const wrist = elbow
        .clone()
        .addScaledVector(direction(V(Math.cos(foreAngle), Math.sin(foreAngle), 0.3 - tuck), WING.foldedForearm), WING.forearm);

      const tips = WING.fingers.map((length, i) => {
        const fan = WING.fingerFan[i] + tuck * 0.5 * i;
        const open = V(Math.cos(fan) * Math.cos(handAngle), Math.cos(fan) * Math.sin(handAngle) - 0.04 * i, -Math.sin(fan));
        const folded = WING.foldedFinger.clone().add(V(0, -0.05 * i, -0.03 * i));
        return wrist.clone().addScaledVector(direction(open, folded), length * THREE.MathUtils.lerp(0.62, 1, spread));
      });

      placeBone(wing.bones[0], shoulder, elbow);
      placeBone(wing.bones[1], elbow, wrist);
      tips.forEach((tip, i) => placeBone(wing.bones[2 + i], wrist, tip));
      for (const bone of wing.bones) {
        bone.scale.x = bone.userData.radius as number;
        bone.scale.z = bone.userData.radius as number;
      }
      wing.claw.position.copy(wrist);
      wing.claw.quaternion.setFromUnitVectors(UP, mirror(V(0.3, 0.2, 1).normalize()));

      // Membrane: arm panels, then scalloped panels between fingers and back to the hip.
      const triangles: THREE.Vector3[] = [];
      const push = (a: THREE.Vector3, b: THREE.Vector3, c: THREE.Vector3) => triangles.push(a, b, c);
      push(shoulder, elbow, hip);
      push(elbow, wrist, hip);
      const edge = [...tips, hip];
      for (let i = 0; i < edge.length - 1; i++) {
        const scallop = edge[i].clone().lerp(edge[i + 1], 0.5).lerp(wrist, i === edge.length - 2 ? 0.3 : 0.16);
        push(wrist, edge[i], scallop);
        push(wrist, scallop, edge[i + 1]);
      }
      // Leading-edge skin between arm and first finger.
      push(shoulder, wrist, elbow);

      for (let i = 0; i < triangles.length; i++) {
        wing.positions[i * 3] = triangles[i].x;
        wing.positions[i * 3 + 1] = triangles[i].y;
        wing.positions[i * 3 + 2] = triangles[i].z;
      }
      wing.membrane.attributes.position.needsUpdate = true;
      wing.membrane.computeVertexNormals();
    }
  }

  private updateNeck(
    state: MountAnimationState,
    beat: number,
    blend: (rate: number) => number,
  ): void {
    const airborne = !state.grounded;
    const last = NECK_SEGMENTS - 1;

    // Occasional idle glances so the head is never frozen.
    this.glance += (Math.sin(this.time * 0.37) * Math.sin(this.time * 0.23 + 1.3) - this.glance) * blend(1.5);
    const idle = state.grounded ? 1 - Math.min(1, state.speed / 4) : 0.25;

    // Where the head wants to look, as a total angle spread over the whole neck.
    const lookYaw = -state.steer * (airborne ? 1.05 : 0.8) + this.glance * 0.7 * idle;
    // Look up into climbs and down into dives, and cancel part of the body's own pitch.
    const lookPitch = -state.climb * 0.55 - state.bodyPitch * 0.6 + (state.action ? -0.35 : 0);

    // The head leads; every segment behind it follows the one in front with a delay.
    this.neckYaw[last] += (lookYaw / NECK_SEGMENTS - this.neckYaw[last]) * blend(7);
    this.neckPitch[last] += (lookPitch / NECK_SEGMENTS - this.neckPitch[last]) * blend(6);
    for (let i = last - 1; i >= 0; i--) {
      this.neckYaw[i] += (this.neckYaw[i + 1] - this.neckYaw[i]) * blend(9);
      this.neckPitch[i] += (this.neckPitch[i + 1] - this.neckPitch[i]) * blend(9);
    }

    for (let i = 0; i < NECK_SEGMENTS; i++) {
      const t = i / last;
      const basePitch = THREE.MathUtils.lerp(NECK_GROUND_CURVE[i], NECK_FLY_CURVE[i], this.spread);
      // Wing-beat pump travelling up the neck, plus a slow snake-like sway.
      const pump = Math.sin(this.wingPhase - 0.6 - t * 2.2) * this.flapAmplitude * 0.055 * this.spread;
      const sway = Math.sin(this.time * 1.1 - t * 3.0) * 0.025;
      const breathe = Math.sin(this.time * 0.9 - t * 1.5) * 0.012;
      // More of the turn comes from the front half of the neck, like a real long-necked animal.
      const yawWeight = 0.55 + t * 0.9;
      this.neck[i].rotation.set(
        basePitch + this.neckPitch[i] + pump + breathe,
        this.neckYaw[i] * yawWeight + sway,
        // Twist against the bank so the head stays close to level through a turn.
        (-state.bodyRoll * 0.85) / NECK_SEGMENTS,
        'YXZ',
      );
    }

    // Head stabilisation: like a bird, the head holds itself near level in the world while the
    // neck and body bank and pitch underneath it.
    const lastSegment = this.neck[last];
    lastSegment.updateWorldMatrix(true, false);
    lastSegment.getWorldQuaternion(this.parentWorld);
    this.forward.set(0, 0, 1).applyQuaternion(this.parentWorld);
    this.forward.y *= 0.55;
    this.forward.normalize();
    this.upward.set(0, 1, 0).applyQuaternion(this.parentWorld).lerp(WORLD_UP, 0.88).normalize();
    this.rightward.crossVectors(this.upward, this.forward).normalize();
    this.upward.crossVectors(this.forward, this.rightward);
    this.basis.makeBasis(this.rightward, this.upward, this.forward);
    this.head.quaternion.setFromRotationMatrix(this.basis).premultiply(this.parentWorld.invert());
    // Small expressive offsets on top: nod with the wing beat, tilt into the turn.
    this.headOffset.set(0.06 - beat * this.flapAmplitude * 0.03 * this.spread, this.neckYaw[last] * 1.2, -state.steer * 0.1, 'YXZ');
    this.head.quaternion.multiply(this.offsetQuaternion.setFromEuler(this.headOffset));

    const jawTarget = state.action ? 0.75 : 0.05 + Math.max(0, Math.sin(this.time * 0.8)) * 0.04;
    this.jawOpen += (jawTarget - this.jawOpen) * blend(state.action ? 12 : 5);
    this.jaw.rotation.x = this.jawOpen;
  }

  private updateTail(state: MountAnimationState, blend: (rate: number) => number): void {
    // The tail swings out of a turn and trails the body's vertical motion; each segment lags the last.
    const yawTarget = (state.steer * (state.grounded ? 0.5 : 0.95)) / TAIL_SEGMENTS;
    const pitchTarget = (state.climb * 0.5 - state.bodyPitch * 0.4) / TAIL_SEGMENTS;
    this.tailYaw[0] += (yawTarget - this.tailYaw[0]) * blend(5);
    this.tailPitch[0] += (pitchTarget - this.tailPitch[0]) * blend(5);
    for (let i = 1; i < TAIL_SEGMENTS; i++) {
      this.tailYaw[i] += (this.tailYaw[i - 1] - this.tailYaw[i]) * blend(8);
      this.tailPitch[i] += (this.tailPitch[i - 1] - this.tailPitch[i]) * blend(8);
    }
    const speedFactor = THREE.MathUtils.clamp(state.speed / 30, 0, 1);
    for (let i = 0; i < TAIL_SEGMENTS; i++) {
      const t = i / (TAIL_SEGMENTS - 1);
      const wave = Math.sin(this.time * 1.6 - t * 4.5) * (0.05 + t * 0.07) * (1 - speedFactor * 0.5);
      const flutter = Math.sin(this.wingPhase - 1.2 - t * 3) * this.flapAmplitude * 0.04 * this.spread;
      // On the ground the tail droops, then curls up off the floor toward the tip.
      const groundCurve = (t < 0.35 ? 0.09 : -0.045) * (1 - this.spread);
      const flyCurve = (0.025 - t * 0.02) * this.spread;
      this.tail[i].rotation.set(
        groundCurve + flyCurve + this.tailPitch[i] + flutter,
        this.tailYaw[i] * (1 + t) + wave,
        0,
        'YXZ',
      );
    }
  }

  private updateLegs(dt: number, state: MountAnimationState, blend: (rate: number) => number): void {
    const walking = state.grounded ? THREE.MathUtils.clamp(state.speed / 3, 0, 1) : 0;
    this.stridePhase += dt * Math.PI * 2 * (state.grounded ? THREE.MathUtils.clamp(state.speed / 5, 0, 1.8) : 0);
    for (const leg of this.legs) {
      let hip: number;
      let knee: number;
      let ankle: number;
      if (state.grounded) {
        const phase = this.stridePhase + leg.phase * Math.PI * 2;
        const swing = Math.sin(phase) * 0.5 * walking;
        const lift = Math.max(0, Math.cos(phase)) * 0.6 * walking;
        hip = -0.55 + swing - lift * 0.4;
        knee = 1.05 + lift;
        ankle = -0.5 - swing * 0.5;
      } else {
        // Trailing back along the tail in flight, as in the reference.
        hip = 1.15 + state.climb * 0.12;
        knee = 0.55;
        ankle = 0.9;
      }
      leg.hip.rotation.x += (hip - leg.hip.rotation.x) * blend(8);
      leg.knee.rotation.x += (knee - leg.knee.rotation.x) * blend(8);
      leg.ankle.rotation.x += (ankle - leg.ankle.rotation.x) * blend(8);
    }
  }
}
