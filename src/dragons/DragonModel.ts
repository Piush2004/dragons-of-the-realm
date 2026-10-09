import * as THREE from 'three';
import type { MountAnimationState, MountModel } from '../mounts/MountModel';
import { DragonRig, basis, X_AXIS, Y_AXIS } from './DragonRig';

export type AttackKind = 'bite' | 'breath' | 'buffet';

interface AttackTiming {
  windup: number;
  strike: number;
  hold: number;
  recover: number;
}

/** Durations (seconds) modelled on the clip: a visible tell, a fast strike, then a slow recovery. */
export const ATTACK_TIMING: Record<AttackKind, AttackTiming> = {
  bite: { windup: 0.42, strike: 0.16, hold: 0.1, recover: 0.5 },
  breath: { windup: 0.8, strike: 1.6, hold: 0, recover: 0.7 },
  buffet: { windup: 0.34, strike: 0.22, hold: 0, recover: 0.55 },
};

export interface DragonEvents {
  /** Fired once at the moment a bite or buffet connects. */
  onStrike?: (kind: AttackKind) => void;
  onBreathStart?: () => void;
  onBreathEnd?: () => void;
}

type Phase = 'windup' | 'strike' | 'hold' | 'recover';

const smooth = (t: number) => {
  const x = Math.min(1, Math.max(0, t));
  return x * x * (3 - 2 * x);
};
const lerp = (a: number, b: number, t: number) => a + (b - a) * t;

/** Skinned, rigged dragon with flight, walk, wing-fold and combat animation. */
export class DragonModel implements MountModel {
  readonly group: THREE.Group;
  events: DragonEvents = {};

  private readonly rig: DragonRig;
  private readonly neckCount: number;
  private readonly tailCount: number;
  private readonly neckYaw: number[];
  private readonly neckPitch: number[];
  private readonly tailYaw: number[];
  private readonly tailPitch: number[];

  private time = 0;
  private wingPhase = 0;
  private stridePhase = 0;
  private spread = 0;
  private flapAmplitude = 0;
  private glance = 0;
  private flinch = 0;
  private flinchSide = 1;
  private deadBlend = 0;
  private dead = false;

  private attack: { kind: AttackKind; t: number; struck: boolean } | null = null;
  private jawOpen = 0;
  /** Where the head should look relative to the body's heading (radians), set by the combat/AI layer. */
  private lookYaw = 0;
  private lookPitch = 0;

  private readonly tmpA = new THREE.Vector3();
  private readonly tmpB = new THREE.Vector3();
  private readonly tmpQ = new THREE.Quaternion();
  private readonly tmpQ2 = new THREE.Quaternion();

  /** 0..1 how much the throat is glowing (for the fire light and particles). */
  breathGlow = 0;
  /** True while flame is actually being emitted. */
  breathing = false;

  constructor(rig: DragonRig) {
    this.rig = rig;
    this.group = rig.root;
    this.neckCount = rig.meta.neckBones;
    this.tailCount = rig.meta.tailBones;
    this.neckYaw = new Array(this.neckCount).fill(0);
    this.neckPitch = new Array(this.neckCount).fill(0);
    this.tailYaw = new Array(this.tailCount).fill(0);
    this.tailPitch = new Array(this.tailCount).fill(0);
  }

  get meta() {
    return this.rig.meta;
  }

  // ------------------------------------------------------------------------------------ combat API

  isAttacking(): boolean {
    return this.attack !== null;
  }

  /** Starts an attack unless one is already running or the dragon is dead. */
  triggerAttack(kind: AttackKind): boolean {
    if (this.attack || this.dead) return false;
    this.attack = { kind, t: 0, struck: false };
    return true;
  }

  hit(strength: number, fromSide = 1): void {
    this.flinch = Math.min(1.4, this.flinch + strength);
    this.flinchSide = fromSide >= 0 ? 1 : -1;
  }

  setDead(dead: boolean): void {
    this.dead = dead;
    if (dead) this.attack = null;
  }

  setLook(yaw: number, pitch: number): void {
    this.lookYaw = THREE.MathUtils.clamp(yaw, -1.0, 1.0);
    this.lookPitch = THREE.MathUtils.clamp(pitch, -0.7, 0.7);
  }

  /** World position of the mouth tip (valid after update). */
  mouthWorld(target: THREE.Vector3): THREE.Vector3 {
    return this.rig.pointAlong('head', this.headLength() * 0.92, target);
  }

  /** World direction the head is pointing. */
  mouthDirection(target: THREE.Vector3): THREE.Vector3 {
    return this.rig.worldDirection('head', target).normalize();
  }

  headWorld(target: THREE.Vector3): THREE.Vector3 {
    return this.rig.pointAlong('head', this.headLength() * 0.45, target);
  }

  private headLength(): number {
    return this.rig.meta.headLength ?? this.rig.meta.length * 0.075;
  }

  private phase(): { phase: Phase; p: number } | null {
    if (!this.attack) return null;
    const timing = ATTACK_TIMING[this.attack.kind];
    let t = this.attack.t;
    if (t < timing.windup) return { phase: 'windup', p: t / timing.windup };
    t -= timing.windup;
    if (t < timing.strike) return { phase: 'strike', p: t / timing.strike };
    t -= timing.strike;
    if (t < timing.hold) return { phase: 'hold', p: t / Math.max(0.001, timing.hold) };
    t -= timing.hold;
    return { phase: 'recover', p: Math.min(1, t / timing.recover) };
  }

  // ------------------------------------------------------------------------------------ animation

  update(deltaSeconds: number, state: MountAnimationState): void {
    const dt = Math.min(deltaSeconds, 0.05);
    this.time += dt;
    const blend = (rate: number) => 1 - Math.exp(-rate * dt);
    const airborne = !state.grounded;

    this.deadBlend += ((this.dead ? 1 : 0) - this.deadBlend) * blend(this.dead ? 2.2 : 6);
    this.flinch *= Math.exp(-5 * dt);

    // Advance the current attack and fire its one-shot events.
    let phaseInfo = this.phase();
    if (this.attack) {
      this.attack.t += dt;
      const timing = ATTACK_TIMING[this.attack.kind];
      const total = timing.windup + timing.strike + timing.hold + timing.recover;
      const wasBreathing = this.breathing;
      phaseInfo = this.phase();
      if (this.attack.kind === 'breath') {
        this.breathing = phaseInfo?.phase === 'strike';
        if (this.breathing && !wasBreathing) this.events.onBreathStart?.();
        if (!this.breathing && wasBreathing) this.events.onBreathEnd?.();
      } else if (!this.attack.struck && phaseInfo && (phaseInfo.phase === 'strike' && phaseInfo.p > 0.45 || phaseInfo.phase === 'hold' || phaseInfo.phase === 'recover')) {
        this.attack.struck = true;
        this.events.onStrike?.(this.attack.kind);
      }
      if (this.attack.t >= total) {
        if (this.breathing) this.events.onBreathEnd?.();
        this.breathing = false;
        this.attack = null;
        phaseInfo = null;
      }
    }
    const kind = this.attack?.kind ?? null;

    this.spread += ((airborne && !this.dead ? 1 : this.dead ? 0.6 : 0) - this.spread) * blend(airborne ? 5 : 3);
    const effort = THREE.MathUtils.clamp(0.45 + state.climb * 0.55 - state.speed / 160, 0.1, 1);
    const flapTarget = airborne && !this.dead ? 0.2 + effort * 0.5 : 0;
    this.flapAmplitude += (flapTarget - this.flapAmplitude) * blend(4);
    this.wingPhase += dt * Math.PI * 2 * (0.5 + effort * 0.65);
    this.stridePhase += dt * Math.PI * 2 * (state.grounded ? THREE.MathUtils.clamp(state.speed / 7, 0, 1.6) : 0);

    this.rig.resetPose();
    this.animateBody(dt, state, phaseInfo, kind);
    this.animateWings(phaseInfo, kind);
    this.animateNeck(state, phaseInfo, kind, blend);
    this.animateTail(state, blend);
    this.animateLegs(state, blend);
    this.rig.commit();
    this.stabiliseHead(state, phaseInfo, kind);
    this.rig.commit();

    // Glow builds through the windup, holds through the blast, fades during recovery.
    let glow = 0;
    if (kind === 'breath' && phaseInfo) {
      glow = phaseInfo.phase === 'windup' ? smooth(phaseInfo.p) : phaseInfo.phase === 'strike' ? 1 : 1 - smooth(phaseInfo.p);
    }
    this.breathGlow += (glow - this.breathGlow) * blend(14);
  }

  private animateBody(dt: number, state: MountAnimationState, phaseInfo: { phase: Phase; p: number } | null, kind: AttackKind | null): void {
    void dt;
    const beat = Math.sin(this.wingPhase);
    const bob = -Math.cos(this.wingPhase) * this.flapAmplitude * 0.5 * this.spread;
    const stride = state.grounded ? Math.abs(Math.sin(this.stridePhase)) * 0.18 * Math.min(1, state.speed / 5) : 0;
    this.rig.bone('root').position.y += bob + stride - this.deadBlend * 0.4;

    // The torso pitches nose-up on the downstroke, rocks in the walk, rears for fire, and lunges for a bite.
    let pitch = THREE.MathUtils.lerp(0.04, beat * this.flapAmplitude * 0.07, this.spread);
    if (kind === 'breath' && phaseInfo) {
      pitch -= phaseInfo.phase === 'windup' ? smooth(phaseInfo.p) * 0.14 : phaseInfo.phase === 'strike' ? 0.14 : 0.14 * (1 - smooth(phaseInfo.p));
    }
    if (kind === 'bite' && phaseInfo) {
      pitch += phaseInfo.phase === 'strike' ? smooth(phaseInfo.p) * 0.12 : phaseInfo.phase === 'windup' ? -smooth(phaseInfo.p) * 0.07 : 0;
    }
    const sway = state.grounded ? Math.sin(this.stridePhase) * 0.04 * Math.min(1, state.speed / 5) : 0;
    this.rig.bend('body', pitch + this.flinch * 0.05, 0, sway + this.flinch * 0.04 * this.flinchSide);
  }

  private animateWings(phaseInfo: { phase: Phase; p: number } | null, kind: AttackKind | null): void {
    const spread = this.spread;
    const amp = this.flapAmplitude;
    const flap = Math.sin(this.wingPhase) * amp;
    const outerFlap = Math.sin(this.wingPhase - 0.9) * amp;
    const tipFlap = Math.sin(this.wingPhase - 1.7) * amp;
    // Fingers draw together on the upstroke so the wing sheds air.
    const close = Math.max(0, Math.cos(this.wingPhase - 0.2)) * amp * 0.55;

    // Wing buffet: raise high, then slam forward and down.
    let buffetFlap = 0;
    let buffetSweep = 0;
    if (kind === 'buffet' && phaseInfo) {
      if (phaseInfo.phase === 'windup') {
        buffetFlap = smooth(phaseInfo.p) * 0.9;
        buffetSweep = smooth(phaseInfo.p) * 0.3;
      } else if (phaseInfo.phase === 'strike') {
        buffetFlap = lerp(0.9, -0.55, smooth(phaseInfo.p));
        buffetSweep = lerp(0.3, -0.7, smooth(phaseInfo.p));
      } else {
        buffetFlap = lerp(-0.55, 0, smooth(phaseInfo.p));
        buffetSweep = lerp(-0.7, 0, smooth(phaseInfo.p));
      }
    }
    const useBuffet = kind === 'buffet' ? 1 : 0;
    const spreadForBuffet = Math.max(spread, useBuffet * 0.9);

    for (const [side, m] of [['L', 1], ['R', -1]] as const) {
      const w = (i: number) => `wing_${side}${i}`;
      const folded = 1 - spreadForBuffet;
      const droop = this.deadBlend * 0.8;

      // Upper arm: sweep (about Y) and flap (about Z). Left wing is +X, so the right wing mirrors both angles.
      const armFlap = lerp(0.42, flap * 0.85 + 0.08, spreadForBuffet) + (useBuffet ? buffetFlap * 0.6 : 0) - droop;
      const armSweep = lerp(1.15, 0.05 + buffetSweep * 0.5, spreadForBuffet);
      this.rig.rotateEuler(w(0), 0, m * armSweep, m * armFlap);

      // Forearm folds back on itself when the wing is stowed.
      const foreFlap = lerp(0.2, outerFlap * 0.9, spreadForBuffet) + (useBuffet ? buffetFlap * 0.4 : 0) - droop * 0.5;
      const foreSweep = lerp(2.55, 0.0 + buffetSweep * 0.3, spreadForBuffet);
      this.rig.rotateEuler(w(1), 0, m * foreSweep, m * foreFlap);

      // Fingers fan out in flight and gather along the flank when stowed.
      for (let i = 0; i < 4; i++) {
        const gather = lerp(-1.9 + i * 0.04, close * (0.2 + i * 0.2), spreadForBuffet);
        const fingerFlap = lerp(0.05, tipFlap * 0.9, spreadForBuffet) + (useBuffet ? buffetFlap * 0.3 : 0) - droop * 0.4;
        this.rig.rotateEuler(w(2 + i), 0, m * (gather + folded * 0.0), m * fingerFlap);
      }
    }
  }

  private animateNeck(
    state: MountAnimationState,
    phaseInfo: { phase: Phase; p: number } | null,
    kind: AttackKind | null,
    blend: (rate: number) => number,
  ): void {
    const n = this.neckCount;
    const last = n - 1;
    const airborne = !state.grounded;

    this.glance += (Math.sin(this.time * 0.37) * Math.sin(this.time * 0.23 + 1.3) - this.glance) * blend(1.5);
    const idle = state.grounded ? 1 - Math.min(1, state.speed / 6) : 0.25;

    // Look target: steering plus any attack aim, spread across the neck so the head leads.
    const lookYaw = -state.steer * (airborne ? 0.9 : 0.6) + this.glance * 0.6 * idle + this.lookYaw;
    const lookPitch = -state.climb * 0.5 + this.lookPitch;
    this.neckYaw[last] += (lookYaw / n - this.neckYaw[last]) * blend(7);
    this.neckPitch[last] += (lookPitch / n - this.neckPitch[last]) * blend(6);
    for (let i = last - 1; i >= 0; i--) {
      this.neckYaw[i] += (this.neckYaw[i + 1] - this.neckYaw[i]) * blend(9);
      this.neckPitch[i] += (this.neckPitch[i + 1] - this.neckPitch[i]) * blend(9);
    }

    const beat = Math.sin(this.wingPhase);
    this.jawOpen = 0;
    for (let i = 0; i < n; i++) {
      const t = i / last;
      // Standing: the neck rears up and the head is carried level. Flying: the rest S-curve.
      const rear = (1 - this.spread) * (t < 0.65 ? -0.17 : 0.03);
      const pump = Math.sin(this.wingPhase - 0.6 - t * 2.2) * this.flapAmplitude * 0.05 * this.spread;
      const sway = Math.sin(this.time * 1.1 - t * 3.0) * 0.022 + Math.sin(this.time * 0.9 - t * 1.5) * 0.01;

      let attackPitch = 0;
      let attackYaw = 0;
      if (kind && phaseInfo) {
        // Per-segment pitches add up along the chain, so these are small: roughly 20 deg up then 40 deg back down.
        const coil = t < 0.45 ? -0.08 : 0.13; // S-curve pulled back toward the shoulders
        const reach = t < 0.45 ? 0.025 : -0.065; // unwound and stretched forward
        const raise = -0.06;
        if (kind === 'bite') {
          if (phaseInfo.phase === 'windup') attackPitch = coil * smooth(phaseInfo.p);
          else if (phaseInfo.phase === 'strike') attackPitch = lerp(coil, reach, smooth(phaseInfo.p * 1.15));
          else if (phaseInfo.phase === 'hold') attackPitch = reach;
          else attackPitch = reach * (1 - smooth(phaseInfo.p));
        } else if (kind === 'breath') {
          const lift = raise * (t < 0.7 ? 1 : 0.5);
          if (phaseInfo.phase === 'windup') attackPitch = lift * smooth(phaseInfo.p);
          else if (phaseInfo.phase === 'strike') {
            attackPitch = lift * 0.7;
            // The whole neck trembles with the force of the blast.
            attackYaw = Math.sin(this.time * 55 + i) * 0.012 * (0.4 + t);
            attackPitch += Math.sin(this.time * 47 + i * 1.7) * 0.01;
          } else attackPitch = lift * 0.7 * (1 - smooth(phaseInfo.p));
        } else if (kind === 'buffet') {
          attackPitch = -0.02 * (phaseInfo.phase === 'recover' ? 1 - phaseInfo.p : 1);
        }
      }

      const flinchPitch = this.flinch * Math.sin(this.time * 24) * 0.06 * (0.4 + t);
      const flinchYaw = this.flinch * this.flinchSide * 0.07 * (0.3 + t);
      const deadPitch = this.deadBlend * (0.05 + t * 0.1);
      this.rig.bend(
        `neck_${i}`,
        rear + this.neckPitch[i] + pump + attackPitch + flinchPitch + deadPitch,
        this.neckYaw[i] * (0.55 + t * 0.9) + sway + attackYaw + flinchYaw,
        (-state.bodyRoll * 0.6) / n,
      );
    }

    // Jaw
    let jaw = 0.05 + Math.max(0, Math.sin(this.time * 0.8)) * 0.04 + this.deadBlend * 0.25;
    if (kind && phaseInfo) {
      if (kind === 'bite') {
        if (phaseInfo.phase === 'windup') jaw = lerp(jaw, 0.5, smooth(phaseInfo.p));
        else if (phaseInfo.phase === 'strike') jaw = lerp(0.5, 1.0, smooth(phaseInfo.p));
        else if (phaseInfo.phase === 'hold') jaw = lerp(1.0, 0.0, smooth(phaseInfo.p * 2));
        else jaw = lerp(0.0, 0.05, smooth(phaseInfo.p));
      } else if (kind === 'breath') {
        if (phaseInfo.phase === 'windup') jaw = lerp(jaw, 0.6, smooth(phaseInfo.p));
        else if (phaseInfo.phase === 'strike') jaw = 0.9 + Math.sin(this.time * 38) * 0.04;
        else jaw = lerp(0.9, 0.05, smooth(phaseInfo.p));
      }
    }
    this.jawOpen += (jaw - this.jawOpen) * 1;
    this.rig.bend('jaw', this.jawOpen * 0.7, 0);
    void beat;
  }

  private animateTail(state: MountAnimationState, blend: (rate: number) => number): void {
    const m = this.tailCount;
    const yawTarget = (state.steer * (state.grounded ? 0.6 : 1.0)) / m;
    const pitchTarget = (state.climb * 0.45 - state.bodyPitch * 0.4) / m;
    this.tailYaw[0] += (yawTarget - this.tailYaw[0]) * blend(5);
    this.tailPitch[0] += (pitchTarget - this.tailPitch[0]) * blend(5);
    for (let i = 1; i < m; i++) {
      this.tailYaw[i] += (this.tailYaw[i - 1] - this.tailYaw[i]) * blend(8);
      this.tailPitch[i] += (this.tailPitch[i - 1] - this.tailPitch[i]) * blend(8);
    }
    const speedFactor = THREE.MathUtils.clamp(state.speed / 35, 0, 1);
    const whip = this.attack?.kind === 'bite' ? 0.04 : 0;
    for (let i = 0; i < m; i++) {
      const t = i / (m - 1);
      const wave = Math.sin(this.time * 1.6 - t * 4.5) * (0.04 + t * 0.06) * (1 - speedFactor * 0.5);
      const flutter = Math.sin(this.wingPhase - 1.2 - t * 3) * this.flapAmplitude * 0.035 * this.spread;
      const carry = (1 - this.spread) * (t < 0.3 ? -0.03 : 0.02); // lifted off the ground when standing
      this.rig.bend(`tail_${i}`, carry + this.tailPitch[i] + flutter + this.deadBlend * 0.02, this.tailYaw[i] * (1 + t) + wave + whip * Math.sin(this.time * 9 - t * 3), 0);
    }
  }

  private animateLegs(state: MountAnimationState, blend: (rate: number) => number): void {
    void blend;
    const grounded = state.grounded;
    const walk = grounded ? THREE.MathUtils.clamp(state.speed / 4, 0, 1) : 0;
    for (const [side, offset] of [['L', 0], ['R', 0.5]] as const) {
      let hip: number;
      let knee: number;
      let foot: number;
      if (grounded) {
        const phase = this.stridePhase + offset * Math.PI * 2;
        const swing = Math.sin(phase) * 0.5 * walk;
        const lift = Math.max(0, Math.cos(phase)) * 0.55 * walk;
        hip = swing - lift * 0.2;
        knee = -lift * 0.9;
        foot = -swing * 0.5 + lift * 0.5;
      } else {
        // Trail back along the tail in flight.
        hip = 1.75 + state.climb * 0.15;
        knee = 0.85;
        foot = 0.55;
      }
      const hipPose = THREE.MathUtils.lerp(hip, 1.2, this.deadBlend * 0.3);
      this.rig.bend(`leg_${side}0`, hipPose, 0);
      this.rig.bend(`leg_${side}1`, knee, 0);
      this.rig.bend(`leg_${side}2`, foot, 0);
    }
  }

  /** Re-aims the head after the neck is posed so it stays near level while the body banks, pitches and flaps. */
  private stabiliseHead(state: MountAnimationState, phaseInfo: { phase: Phase; p: number } | null, kind: AttackKind | null): void {
    const rig = this.rig;
    const lastNeck = `neck_${this.neckCount - 1}`;
    const rootQ = rig.root.getWorldQuaternion(this.tmpQ);
    const rootInv = this.tmpQ2.copy(rootQ).invert();

    // Neck-end orientation after bending, as a delta from rest.
    const neckDelta = rig.currentDelta(lastNeck, new THREE.Quaternion());
    const fRest = rig.restDirection('head');
    const uRest = Y_AXIS.clone().sub(fRest.clone().multiplyScalar(Y_AXIS.dot(fRest))).normalize();
    const follow = neckDelta;
    const forwardW = this.tmpA.copy(fRest).applyQuaternion(follow).applyQuaternion(rootQ);
    const upW = this.tmpB.copy(uRest).applyQuaternion(follow).applyQuaternion(rootQ);

    // Level: hold the head's forward axis near horizontal and its up axis near the world's up.
    let pitchBias = this.lookPitch * 0.8;
    if (kind === 'breath' && phaseInfo) {
      pitchBias -= phaseInfo.phase === 'windup' ? smooth(phaseInfo.p) * 0.2 : phaseInfo.phase === 'strike' ? 0.1 : 0.1 * (1 - smooth(phaseInfo.p));
    }
    const targetForward = forwardW.clone();
    targetForward.y = targetForward.y * 0.45 + Math.sin(-pitchBias) * 0.5;
    targetForward.normalize();
    const holdLevel = (1 - this.deadBlend) * (kind === 'bite' ? 0.35 : 0.85);
    const targetUp = upW.clone().lerp(new THREE.Vector3(0, 1, 0), holdLevel).normalize();
    // Mix: partly follow the neck, partly level.
    const finalForward = forwardW.clone().lerp(targetForward, holdLevel).normalize();

    const toModel = (v: THREE.Vector3) => v.clone().applyQuaternion(rootInv);
    const targetQ = basis(toModel(finalForward), toModel(targetUp));
    const restQ = basis(fRest, uRest);
    const delta = targetQ.multiply(restQ.invert());
    // Expressive nod with each wing beat and a tilt into turns.
    const nod = new THREE.Quaternion().setFromAxisAngle(X_AXIS, 0.05 - Math.sin(this.wingPhase) * this.flapAmplitude * 0.03 * this.spread);
    const tilt = new THREE.Quaternion().setFromAxisAngle(fRest, -state.steer * 0.12);
    delta.multiply(nod).multiply(tilt);

    const head = rig.bone('head');
    const parentNow = rig.modelQuaternion(head.parent!, new THREE.Quaternion());
    head.quaternion.copy(parentNow).invert().multiply(delta).multiply(rig.restModelQuaternion('head'));
  }
}
