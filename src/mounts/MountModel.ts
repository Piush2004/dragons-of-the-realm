import type * as THREE from 'three';

/** Everything a creature model needs to animate itself for one frame. */
export interface MountAnimationState {
  speed: number;
  grounded: boolean;
  /** -1 (diving) .. +1 (climbing hard). */
  climb: number;
  /** -1 (turning left) .. +1 (turning right). */
  steer: number;
  /** Current visual pitch / roll of the whole body in radians, so heads can counter them. */
  bodyPitch: number;
  bodyRoll: number;
  /** Action key held (roar). */
  action: boolean;
}

export interface MountModel {
  readonly group: THREE.Group;
  update(deltaSeconds: number, state: MountAnimationState): void;
  /** Frees GPU resources owned by this instance (not the shared meshes and textures). */
  dispose?(): void;
}

/** Handling numbers for a creature that can run on the ground and fly. Speeds in m/s. */
export interface FlyingMountTuning {
  mass: number;
  /** Collision sphere radius; keep it below groundOffset so it clears sloped ground. */
  bodyRadius: number;
  /** Distance from the model origin down to the feet. */
  groundOffset: number;
  groundTopSpeed: number;
  airTopSpeed: number;
  glideMinSpeed: number;
  acceleration: number;
  braking: number;
  groundTurnRate: number;
  airTurnRate: number;
  flapAcceleration: number;
  maxClimbSpeed: number;
  maxDiveSpeed: number;
  /** Bank angle at full turn input while flying, radians. */
  maxBank: number;
  groundedLabel: string;
}
