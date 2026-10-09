import type * as THREE from 'three';
import type * as CANNON from 'cannon-es';
import type { DriveInput } from './InputManager';

export interface HudStatus {
  /** Short secondary readout under the speed, e.g. "GEAR 3" or "ALT 42 m". */
  label: string;
  /** 0..1 fill of the HUD bar (RPM for cars, flight effort for the pegasus). */
  bar: number;
}

/** Anything the player can control. The game loop, camera and HUD only depend on this. */
export interface PlayerActor {
  readonly mesh: THREE.Object3D;
  readonly body: CANNON.Body;
  readonly helpText: string;
  applyInput(input: DriveInput, deltaSeconds: number): void;
  syncVisuals(deltaSeconds: number): void;
  consumeImpact(): number;
  resetTo(position: CANNON.Vec3, yawRadians?: number): void;
  getSpeedMetersPerSecond(): number;
  getSpeedKmh(): number;
  getHudStatus(): HudStatus;
}
