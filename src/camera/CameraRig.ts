import * as THREE from 'three';

export interface CameraMode {
  id: string;
  label: string;
  /** Camera position in car-local space (x = left, y = up, z = forward). */
  offset: THREE.Vector3;
  /** Point the camera aims at, in car-local space. */
  lookAt: THREE.Vector3;
  fov: number;
  /** Extra FOV at top speed, for a sense of speed. */
  speedFov: number;
  /** Follow stiffness; null locks the camera rigidly to the car (onboard views). */
  followRate: number | null;
  /** How much the camera pulls back at speed (multiplier on the offset). */
  speedPullback: number;
}

export const CAR_CAMERA_MODES: CameraMode[] = [
  {
    id: 'chase',
    label: 'Chase',
    offset: new THREE.Vector3(0, 2.6, -6.8),
    lookAt: new THREE.Vector3(0, 1.0, 4),
    fov: 60,
    speedFov: 12,
    followRate: 7,
    speedPullback: 0.25,
  },
  {
    id: 'chase_far',
    label: 'Far Chase',
    offset: new THREE.Vector3(0, 4.4, -11.5),
    lookAt: new THREE.Vector3(0, 0.8, 5),
    fov: 55,
    speedFov: 10,
    followRate: 5,
    speedPullback: 0.3,
  },
  {
    id: 'hood',
    label: 'Hood',
    offset: new THREE.Vector3(0, 0.62, 0.55),
    lookAt: new THREE.Vector3(0, 0.45, 12),
    fov: 68,
    speedFov: 8,
    followRate: null,
    speedPullback: 0,
  },
  {
    id: 'bumper',
    label: 'Bumper',
    offset: new THREE.Vector3(0, 0.0, 2.6),
    lookAt: new THREE.Vector3(0, -0.05, 14),
    fov: 72,
    speedFov: 10,
    followRate: null,
    speedPullback: 0,
  },
  {
    id: 'cockpit',
    label: 'Cockpit',
    offset: new THREE.Vector3(0.36, 0.44, 0.05),
    lookAt: new THREE.Vector3(0.3, 0.2, 12),
    fov: 70,
    speedFov: 6,
    followRate: null,
    speedPullback: 0,
  },
];

export const PEGASUS_CAMERA_MODES: CameraMode[] = [
  {
    id: 'chase',
    label: 'Chase',
    offset: new THREE.Vector3(0, 2.6, -8),
    lookAt: new THREE.Vector3(0, 0.9, 4),
    fov: 60,
    speedFov: 12,
    followRate: 6,
    speedPullback: 0.25,
  },
  {
    id: 'chase_far',
    label: 'Far Chase',
    offset: new THREE.Vector3(0, 5, -15),
    lookAt: new THREE.Vector3(0, 0.6, 5),
    fov: 55,
    speedFov: 10,
    followRate: 4.5,
    speedPullback: 0.3,
  },
  {
    id: 'side',
    label: 'Wingside',
    offset: new THREE.Vector3(6.5, 1.4, 2.5),
    lookAt: new THREE.Vector3(0, 0.6, 0.4),
    fov: 50,
    speedFov: 6,
    followRate: 8,
    speedPullback: 0,
  },
  {
    id: 'rider',
    label: 'Rider',
    offset: new THREE.Vector3(0, 1.75, -0.1),
    lookAt: new THREE.Vector3(0, 1.3, 12),
    fov: 72,
    speedFov: 8,
    followRate: null,
    speedPullback: 0,
  },
];

/** Player camera with switchable views, speed-sensitive FOV, look-back and impact shake. */
export class CameraRig {
  readonly camera: THREE.PerspectiveCamera;

  private readonly modes: CameraMode[];
  private modeIndex = 0;
  private readonly currentPosition = new THREE.Vector3();
  private readonly currentLook = new THREE.Vector3();
  private needsSnap = true;
  private shake = 0;
  private shakeTime = 0;

  constructor(aspect: number, modes: CameraMode[]) {
    this.modes = modes;
    this.camera = new THREE.PerspectiveCamera(60, aspect, 0.15, 6000);
  }

  get mode(): CameraMode {
    return this.modes[this.modeIndex];
  }

  /** Switches to the next view and returns it. */
  cycleMode(): CameraMode {
    this.modeIndex = (this.modeIndex + 1) % this.modes.length;
    this.needsSnap = true;
    return this.mode;
  }

  setMode(id: string): void {
    const index = this.modes.findIndex((mode) => mode.id === id);
    if (index >= 0) {
      this.modeIndex = index;
      this.needsSnap = true;
    }
  }

  /** Call when the target teleports (reset, fast travel) so the camera does not sweep across the map. */
  snap(): void {
    this.needsSnap = true;
  }

  addShake(impactSpeed: number): void {
    this.shake = Math.min(1, this.shake + impactSpeed / 25);
  }

  update(target: THREE.Object3D, speedMetersPerSecond: number, deltaSeconds: number, lookBack: boolean): void {
    const mode = this.mode;
    const speedRatio = THREE.MathUtils.clamp(speedMetersPerSecond / 60, 0, 1);

    const offset = mode.offset.clone().multiplyScalar(1 + speedRatio * mode.speedPullback);
    const lookAt = mode.lookAt.clone();
    if (lookBack) {
      // Mirror front-to-back: chase cams swing to the nose, onboard cams look out the rear.
      if (mode.followRate !== null) offset.z = -offset.z;
      lookAt.z = -lookAt.z;
    }
    const desiredPosition = offset.applyQuaternion(target.quaternion).add(target.position);
    const desiredLook = lookAt.applyQuaternion(target.quaternion).add(target.position);

    if (mode.followRate === null || this.needsSnap) {
      this.currentPosition.copy(desiredPosition);
      this.currentLook.copy(desiredLook);
      this.needsSnap = false;
    } else {
      this.currentPosition.lerp(desiredPosition, 1 - Math.exp(-mode.followRate * deltaSeconds));
      this.currentLook.lerp(desiredLook, 1 - Math.exp(-mode.followRate * 1.6 * deltaSeconds));
    }

    this.camera.position.copy(this.currentPosition);
    if (mode.followRate === null) {
      // Onboard views roll and pitch with the car.
      this.camera.up.set(0, 1, 0).applyQuaternion(target.quaternion);
    } else {
      this.camera.up.set(0, 1, 0);
    }
    this.camera.lookAt(this.currentLook);

    if (this.shake > 0.001) {
      this.shakeTime += deltaSeconds * 55;
      const amplitude = this.shake * this.shake * 0.22;
      this.camera.position.x += Math.sin(this.shakeTime * 1.3) * amplitude;
      this.camera.position.y += Math.sin(this.shakeTime * 1.9 + 1.7) * amplitude;
      this.camera.rotateZ(Math.sin(this.shakeTime * 1.1) * amplitude * 0.08);
      this.shake *= Math.exp(-5 * deltaSeconds);
    }

    this.camera.fov = mode.fov + speedRatio * mode.speedFov;
    this.camera.updateProjectionMatrix();
  }

  setAspect(aspect: number): void {
    this.camera.aspect = aspect;
    this.camera.updateProjectionMatrix();
  }
}
