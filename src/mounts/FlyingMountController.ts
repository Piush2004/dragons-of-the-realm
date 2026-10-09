import * as CANNON from 'cannon-es';
import * as THREE from 'three';
import type { PhysicsWorld } from '../physics/PhysicsWorld';
import type { DriveInput } from '../core/InputManager';
import type { HudStatus, PlayerActor } from '../core/PlayerActor';
import type { FlyingMountTuning, MountModel } from './MountModel';
import { PLAYABLE_RADIUS } from '../world/WorldConfig';

const GRAVITY = 9.82;
const MAX_ALTITUDE = 700;
const MIN_IMPACT_SPEED = 2.5;

/**
 * Rideable flying creature: runs on the terrain, flaps to take off and climb, glides at speed.
 * A cannon sphere provides collisions; heading, speed and lift are driven directly for a
 * responsive arcade feel. The creature itself (model + handling) can be swapped at runtime.
 */
export class FlyingMountController implements PlayerActor {
  mesh: THREE.Object3D;
  readonly body: CANNON.Body;
  readonly helpText =
    'W/S speed · A/D turn · Space climb · Shift dive · J bite · F fire · K wing buffet · C camera · B look back · R reset';

  model: MountModel;
  tuning: FlyingMountTuning;
  /** Set by the combat layer: the dragon is dead and ignores input. */
  dead = false;

  private readonly scene: THREE.Scene;
  private readonly physics: PhysicsWorld;
  private readonly heightAt: (x: number, z: number) => number;
  private actionInput = false;
  private yaw = 0;
  private speed = 0;
  private grounded = true;
  private climbInput = 0;
  private steerInput = 0;
  private visualPitch = 0;
  private visualRoll = 0;
  private pendingImpact = 0;
  /** Extra velocity from hits and explosions, decaying toward zero. */
  private readonly knock = new THREE.Vector3();

  constructor(
    physics: PhysicsWorld,
    scene: THREE.Scene,
    model: MountModel,
    tuning: FlyingMountTuning,
    heightAt: (x: number, z: number) => number,
    startPosition: CANNON.Vec3,
  ) {
    this.scene = scene;
    this.physics = physics;
    this.heightAt = heightAt;
    this.model = model;
    this.tuning = tuning;
    this.mesh = this.model.group;
    scene.add(this.mesh);

    this.body = new CANNON.Body({ mass: tuning.mass, shape: new CANNON.Sphere(tuning.bodyRadius), fixedRotation: true });
    this.body.updateMassProperties();
    this.body.position.copy(startPosition);
    this.body.interpolatedPosition.copy(startPosition);
    this.body.previousPosition.copy(startPosition);
    // Visible and positioned from the first frame, so AI and effects never see a creature at the origin.
    this.mesh.position.set(startPosition.x, startPosition.y, startPosition.z);
    physics.world.addBody(this.body);

    this.body.addEventListener('collide', (event: { contact: CANNON.ContactEquation }) => {
      const impactSpeed = Math.abs(event.contact.getImpactVelocityAlongNormal());
      if (impactSpeed > MIN_IMPACT_SPEED) this.pendingImpact = Math.max(this.pendingImpact, impactSpeed);
    });
  }

  /** Replaces the creature (model, handling and collision size) while keeping position and heading. */
  swapCreature(model: MountModel, tuning: FlyingMountTuning): void {
    this.scene.remove(this.mesh);
    this.model = model;
    this.tuning = tuning;
    this.mesh = model.group;
    this.scene.add(this.mesh);
    for (const shape of [...this.body.shapes]) this.body.removeShape(shape);
    this.body.addShape(new CANNON.Sphere(tuning.bodyRadius));
    this.body.mass = tuning.mass;
    this.body.updateMassProperties();
    this.body.updateBoundingRadius();
    this.dead = false;
  }

  /** Removes the creature from the world. */
  dispose(): void {
    this.scene.remove(this.mesh);
    this.physics.world.removeBody(this.body);
  }

  get heading(): number {
    return this.yaw;
  }

  setHeading(yaw: number): void {
    this.yaw = yaw;
  }

  get currentSpeed(): number {
    return this.speed;
  }

  get bodyRadius(): number {
    return this.tuning.bodyRadius;
  }

  /** Adds a decaying push (m/s), e.g. from a bite, a buffet or a collision. */
  addKnockback(x: number, y: number, z: number): void {
    this.knock.x += x;
    this.knock.y += y;
    this.knock.z += z;
  }

  applyInput(input: DriveInput, deltaSeconds: number): void {
    const { body, tuning } = this;
    if (this.dead) {
      input = { throttle: 0, brake: 0, steer: 0, handbrake: false, descend: true, action: false };
    }
    this.actionInput = input.action;
    const groundY = this.heightAt(body.position.x, body.position.z) + tuning.groundOffset;
    const ascend = input.handbrake;
    const descend = input.descend;

    // Land (or stay planted) when near the ground and not flapping upward.
    const nearGround = body.position.y - groundY < 0.25;
    this.grounded = nearGround && !ascend && body.velocity.y <= 1.5;

    // Heading
    this.steerInput = input.steer;
    const turnRate = this.grounded ? tuning.groundTurnRate : tuning.airTurnRate;
    this.yaw += -input.steer * turnRate * deltaSeconds;
    const forwardX = Math.sin(this.yaw);
    const forwardZ = Math.cos(this.yaw);

    // Speed: a collision shows up as lost forward velocity, so never carry more than was kept.
    const measured = (body.velocity.x - this.knock.x) * forwardX + (body.velocity.z - this.knock.z) * forwardZ;
    this.speed = Math.min(this.speed, Math.max(0, measured) + 2);
    const topSpeed = this.grounded ? tuning.groundTopSpeed : tuning.airTopSpeed;
    if (input.throttle > 0) {
      this.speed += tuning.acceleration * input.throttle * deltaSeconds;
    } else if (input.brake > 0) {
      this.speed -= tuning.braking * input.brake * deltaSeconds;
    } else {
      // Coast: slow to a halt on the ground, settle to a glide in the air.
      const rest = this.grounded || this.dead ? 0 : tuning.glideMinSpeed;
      this.speed += (rest - this.speed) * (1 - Math.exp(-(this.grounded || this.dead ? 1.2 : 0.35) * deltaSeconds));
    }
    if (this.speed > topSpeed) this.speed += (topSpeed - this.speed) * (1 - Math.exp(-6 * deltaSeconds));
    this.speed = Math.max(0, this.speed);

    this.knock.multiplyScalar(Math.exp(-2.6 * deltaSeconds));
    body.velocity.x = forwardX * this.speed + this.knock.x;
    body.velocity.z = forwardZ * this.speed + this.knock.z;

    // Vertical
    if (this.grounded) {
      body.position.y = groundY;
      body.velocity.y = 0;
      this.climbInput = 0;
      this.knock.y = 0;
    } else {
      // Wings carry more of the weight the faster the creature flies.
      const lift = this.dead ? 0 : THREE.MathUtils.clamp(this.speed / (tuning.glideMinSpeed * 1.8), 0.35, 0.94);
      let vy = body.velocity.y + GRAVITY * lift * deltaSeconds + this.knock.y * deltaSeconds * 3;
      if (this.dead) vy -= GRAVITY * deltaSeconds;
      if (ascend) vy += tuning.flapAcceleration * deltaSeconds;
      if (descend && !this.dead) vy -= tuning.flapAcceleration * deltaSeconds;
      if (!ascend && !descend) vy *= Math.exp(-0.9 * deltaSeconds);
      if (body.position.y > MAX_ALTITUDE && vy > 0) vy *= Math.exp(-6 * deltaSeconds);
      body.velocity.y = THREE.MathUtils.clamp(vy, -tuning.maxDiveSpeed * (this.dead ? 1.5 : 1), tuning.maxClimbSpeed);
      this.climbInput = ascend ? 1 : descend ? -1 : THREE.MathUtils.clamp(body.velocity.y / 8, -1, 0.3);
    }

    // Never sink through the ground, even when knocked down.
    if (body.position.y < groundY) body.position.y = groundY;

    // Keep the player inside the mountain rim.
    const radius = Math.hypot(body.position.x, body.position.z);
    if (radius > PLAYABLE_RADIUS) {
      const scale = PLAYABLE_RADIUS / radius;
      body.position.x *= scale;
      body.position.z *= scale;
    }
  }

  syncVisuals(deltaSeconds: number): void {
    const { body } = this;
    this.mesh.position.copy(body.interpolatedPosition as unknown as THREE.Vector3);

    let targetPitch: number;
    let targetRoll: number;
    if (this.grounded) {
      // Follow the slope under the feet.
      const span = this.tuning.bodyRadius * 1.5;
      const ahead = this.heightAt(body.position.x + Math.sin(this.yaw) * span, body.position.z + Math.cos(this.yaw) * span);
      const behind = this.heightAt(body.position.x - Math.sin(this.yaw) * span, body.position.z - Math.cos(this.yaw) * span);
      targetPitch = -Math.atan2(ahead - behind, span * 2) * 0.8;
      targetRoll = this.steerInput * 0.08 * Math.min(1, this.speed / 8);
    } else {
      targetPitch = -Math.atan2(body.velocity.y, Math.max(this.speed, 8)) * 0.85;
      targetRoll = this.steerInput * this.tuning.maxBank;
    }
    if (this.dead) {
      targetPitch *= 0.5;
      targetRoll = 0.35 * (this.knock.x >= 0 ? 1 : -1);
    }
    const blend = 1 - Math.exp(-5 * deltaSeconds);
    this.visualPitch += (targetPitch - this.visualPitch) * blend;
    this.visualRoll += (targetRoll - this.visualRoll) * blend;
    this.mesh.rotation.set(this.visualPitch, this.yaw, this.visualRoll, 'YXZ');

    this.model.update(deltaSeconds, {
      speed: this.speed,
      grounded: this.grounded,
      climb: this.climbInput,
      steer: this.steerInput,
      bodyPitch: this.visualPitch,
      bodyRoll: this.visualRoll,
      action: this.actionInput,
    });
  }

  consumeImpact(): number {
    const impact = this.pendingImpact;
    this.pendingImpact = 0;
    return impact;
  }

  resetTo(position: CANNON.Vec3, yawRadians = 0): void {
    this.body.position.copy(position);
    this.body.velocity.setZero();
    this.body.interpolatedPosition.copy(position);
    this.body.previousPosition.copy(position);
    this.yaw = yawRadians;
    this.speed = 0;
    this.grounded = true;
    this.knock.set(0, 0, 0);
  }

  isGrounded(): boolean {
    return this.grounded;
  }

  getAltitude(): number {
    const { position } = this.body;
    return Math.max(0, position.y - this.tuning.groundOffset - this.heightAt(position.x, position.z));
  }

  getSpeedMetersPerSecond(): number {
    return this.body.velocity.length();
  }

  getSpeedKmh(): number {
    return this.getSpeedMetersPerSecond() * 3.6;
  }

  getHudStatus(): HudStatus {
    return {
      label: this.grounded ? this.tuning.groundedLabel : `ALT ${Math.round(this.getAltitude())} m`,
      bar: THREE.MathUtils.clamp(this.speed / this.tuning.airTopSpeed, 0, 1),
    };
  }
}
