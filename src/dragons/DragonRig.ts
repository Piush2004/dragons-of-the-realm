import * as THREE from 'three';
import { clone as cloneSkinned } from 'three/examples/jsm/utils/SkeletonUtils.js';

export interface BoneMeta {
  parent: string | null;
  head: [number, number, number];
  /** Rest direction from the bone's head toward its tail, in model space (+Z forward, +Y up). */
  dir: [number, number, number];
}

export interface DragonMeta {
  name: string;
  groundOffset: number;
  length: number;
  wingspan: number;
  bodyRadius: number;
  headLength?: number;
  neckBones: number;
  tailBones: number;
  bones: Record<string, BoneMeta>;
}

const X_AXIS = new THREE.Vector3(1, 0, 0);
const Y_AXIS = new THREE.Vector3(0, 1, 0);
const Z_AXIS = new THREE.Vector3(0, 0, 1);

const scratchA = new THREE.Quaternion();
const scratchB = new THREE.Quaternion();
const scratchC = new THREE.Quaternion();
const scratchMatrix = new THREE.Matrix4();

/**
 * Drives a skinned dragon by model-space rotation deltas relative to its rest pose, so animation code
 * never needs to know how the exporter oriented each bone.
 */
export class DragonRig {
  readonly root = new THREE.Group();
  readonly meta: DragonMeta;
  readonly skinned: THREE.SkinnedMesh[] = [];

  private readonly bones = new Map<string, THREE.Bone>();
  private readonly restLocal = new Map<string, THREE.Quaternion>();
  private readonly restModel = new Map<string, THREE.Quaternion>();
  private readonly restParentModel = new Map<string, THREE.Quaternion>();
  private readonly restPosition = new Map<string, THREE.Vector3>();
  private readonly dirs = new Map<string, THREE.Vector3>();
  private readonly bendUps = new Map<string, THREE.Vector3>();
  private readonly scene: THREE.Object3D;

  constructor(source: THREE.Object3D, meta: DragonMeta) {
    this.meta = meta;
    this.scene = cloneSkinned(source);
    this.root.add(this.scene);

    this.scene.traverse((child) => {
      if (child instanceof THREE.Bone) this.bones.set(child.name, child);
      if (child instanceof THREE.SkinnedMesh) {
        child.frustumCulled = false;
        child.castShadow = true;
        child.receiveShadow = true;
        this.skinned.push(child);
      }
    });

    for (const [name, bone] of this.bones) {
      this.restLocal.set(name, bone.quaternion.clone());
      this.restPosition.set(name, bone.position.clone());
      this.restModel.set(name, this.modelQuaternion(bone));
      this.restParentModel.set(name, bone.parent ? this.modelQuaternion(bone.parent) : new THREE.Quaternion());
      const info = meta.bones[name];
      const dir = info ? new THREE.Vector3(...info.dir) : new THREE.Vector3(0, 0, 1);
      this.dirs.set(name, dir);
      const up = new THREE.Vector3().crossVectors(dir, X_AXIS);
      this.bendUps.set(name, up.lengthSq() < 1e-4 ? Y_AXIS.clone() : up.normalize());
    }
  }

  /** Releases the GPU bone textures of this dragon's skeletons. */
  dispose(): void {
    for (const mesh of this.skinned) mesh.skeleton.dispose();
  }

  has(name: string): boolean {
    return this.bones.has(name);
  }

  bone(name: string): THREE.Bone {
    const bone = this.bones.get(name);
    if (!bone) throw new Error(`Bone "${name}" missing from ${this.meta.name}`);
    return bone;
  }

  /** Orientation of a bone relative to the rig root (independent of where the dragon stands in the world). */
  modelQuaternion(bone: THREE.Object3D, target = new THREE.Quaternion()): THREE.Quaternion {
    target.copy(bone.quaternion);
    let parent = bone.parent;
    while (parent && parent !== this.root) {
      target.premultiply(parent.quaternion);
      parent = parent.parent;
    }
    return target;
  }

  resetPose(): void {
    for (const [name, bone] of this.bones) {
      bone.quaternion.copy(this.restLocal.get(name)!);
      bone.position.copy(this.restPosition.get(name)!);
    }
  }

  restDirection(name: string): THREE.Vector3 {
    return this.dirs.get(name)!;
  }

  restUp(name: string): THREE.Vector3 {
    return this.bendUps.get(name)!;
  }

  restModelQuaternion(name: string): THREE.Quaternion {
    return this.restModel.get(name)!;
  }

  /** Applies `delta` (a rotation about model-space axes through the bone's pivot) on top of the rest pose. */
  rotate(name: string, delta: THREE.Quaternion): void {
    const bone = this.bones.get(name);
    if (!bone) return;
    const parentRest = this.restParentModel.get(name)!;
    // local = conj(Wp) * delta * Wp * Lrest
    bone.quaternion
      .copy(parentRest)
      .invert()
      .multiply(delta)
      .multiply(parentRest)
      .multiply(this.restLocal.get(name)!);
  }

  /**
   * Bends a bone about its own rest frame: pitch about the model's lateral axis (positive = nose down),
   * yaw about the bone's up axis (positive = toward the model's left), roll about the bone's length.
   */
  bend(name: string, pitch: number, yaw: number, roll = 0): void {
    if (!this.bones.has(name)) return;
    const q = scratchA.setFromAxisAngle(this.bendUps.get(name)!, yaw);
    q.multiply(scratchB.setFromAxisAngle(X_AXIS, pitch));
    if (roll !== 0) q.multiply(scratchC.setFromAxisAngle(this.dirs.get(name)!, roll));
    this.rotate(name, q);
  }

  /** Rotation about the model's own axes, e.g. wing flap (about Z) and sweep (about Y). */
  rotateEuler(name: string, x: number, y: number, z: number): void {
    scratchA.setFromEuler(new THREE.Euler(x, y, z, 'ZYX'));
    this.rotate(name, scratchA);
  }

  /** Rest-relative rotation a bone currently has, expressed in model space. */
  currentDelta(name: string, target = new THREE.Quaternion()): THREE.Quaternion {
    const bone = this.bone(name);
    this.modelQuaternion(bone, target);
    return target.multiply(scratchB.copy(this.restModel.get(name)!).invert());
  }

  /** Points the bone along `forward` with its up axis as close to `up` as possible (model space). */
  aim(name: string, forward: THREE.Vector3, up: THREE.Vector3): void {
    const bone = this.bone(name);
    const restForward = this.dirs.get(name)!;
    const restUp = Y_AXIS.clone().sub(restForward.clone().multiplyScalar(Y_AXIS.dot(restForward))).normalize();
    const target = basis(forward, up);
    const rest = basis(restForward, restUp);
    const delta = target.multiply(rest.invert());
    const parent = bone.parent ? this.modelQuaternion(bone.parent) : new THREE.Quaternion();
    // Absolute model-space orientation = delta * restModel; local = conj(parentNow) * that.
    bone.quaternion.copy(parent).invert().multiply(delta).multiply(this.restModel.get(name)!);
  }

  /** Current model-space direction of a bone (rest direction carried by its current rotation). */
  direction(name: string, target = new THREE.Vector3()): THREE.Vector3 {
    const bone = this.bone(name);
    this.modelQuaternion(bone, scratchA);
    const delta = scratchA.multiply(scratchB.copy(this.restModel.get(name)!).invert());
    return target.copy(this.dirs.get(name)!).applyQuaternion(delta);
  }

  /** Updates world matrices after pose changes so positions can be read. */
  commit(): void {
    this.root.updateMatrixWorld(true);
  }

  /** World position of a point `distance` along the bone from its head, after `commit()`. */
  pointAlong(name: string, distance: number, target = new THREE.Vector3()): THREE.Vector3 {
    const bone = this.bone(name);
    bone.updateWorldMatrix(true, false);
    const dir = this.direction(name, scratchDir).applyQuaternion(this.root.getWorldQuaternion(scratchWorldQ));
    return bone.getWorldPosition(target).addScaledVector(dir, distance);
  }

  worldDirection(name: string, target = new THREE.Vector3()): THREE.Vector3 {
    this.direction(name, target);
    return target.applyQuaternion(this.root.getWorldQuaternion(scratchWorldQ));
  }
}

const scratchDir = new THREE.Vector3();
const scratchWorldQ = new THREE.Quaternion();

export function basis(forward: THREE.Vector3, up: THREE.Vector3): THREE.Quaternion {
  const z = forward.clone().normalize();
  const y = up.clone().sub(z.clone().multiplyScalar(up.dot(z)));
  if (y.lengthSq() < 1e-6) y.copy(Y_AXIS);
  y.normalize();
  const x = new THREE.Vector3().crossVectors(y, z).normalize();
  scratchMatrix.makeBasis(x, y, z);
  return new THREE.Quaternion().setFromRotationMatrix(scratchMatrix);
}

export { X_AXIS, Y_AXIS, Z_AXIS };
