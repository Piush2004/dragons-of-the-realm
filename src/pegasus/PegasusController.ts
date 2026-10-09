import * as CANNON from 'cannon-es';
import * as THREE from 'three';
import type { PhysicsWorld } from '../physics/PhysicsWorld';
import type { DriveInput } from '../core/InputManager';
import type { HudStatus, PlayerActor } from '../core/PlayerActor';
import { PegasusModel, PEGASUS_GROUND_OFFSET } from './PegasusModel';

// Smaller than the hoof offset so the sphere clears sloped ground while galloping (hooves are placed by height lookup).
const BODY_RADIUS = 0.65;
const GROUND_TOP_SPEED = 17;
const AIR_TOP_SPEED = 44;
const GLIDE_MIN_SPEED = 11;
const ACCELERATION = 9;
const BRAKING = 16;
const GROUND_TURN_RATE = 1.9;
const AIR_TURN_RATE = 1.15;
const FLAP_ACCELERATION = 26;
const MAX_CLIMB_SPEED = 13;
const MAX_DIVE_SPEED = 24;
const GRAVITY = 9.82;
const MAX_ALTITUDE = 420;
const WORLD_RADIUS = 680;
const MIN_IMPACT_SPEED = 2.5;

/**
 * Rideable pegasus: gallops on the terrain, flaps to take off and climb, glides at speed.
 * A cannon sphere provides collisions; heading, speed and lift are driven directly for a
 * responsive arcade feel.
 */
export class PegasusController implements PlayerActor {
  readonly mesh: THREE.Group;
  readonly body: CANNON.Body;
  readonly helpText =
    'W / S — speed   ·   A / D — turn   ·   Space — flap up   ·   Shift — descend   ·   C — camera   ·   B — look back   ·   R — reset';

  private readonly model = new PegasusModel();
  private readonly heightAt: (x: number, z: number) => number;
  private yaw = 0;
  private speed = 0;
  private grounded = true;
  private climbInput = 0;
  private steerInput = 0;
  private visualPitch = 0;
  private visualRoll = 0;
  private pendingImpact = 0;

  constructor(
    physics: PhysicsWorld,
    scene: THREE.Scene,
    heightAt: (x: number, z: number) => number,
    startPosition: CANNON.Vec3,
  ) {
    this.heightAt = heightAt;
    this.mesh = this.model.group;
    scene.add(this.mesh);

    this.body = new CANNON.Body({ mass: 420, shape: new CANNON.Sphere(BODY_RADIUS), fixedRotation: true });
    this.body.updateMassProperties();
    this.body.position.copy(startPosition);
    physics.world.addBody(this.body);

    this.body.addEventListener('collide', (event: { contact: CANNON.ContactEquation }) => {
      const impactSpeed = Math.abs(event.contact.getImpactVelocityAlongNormal());
      if (impactSpeed > MIN_IMPACT_SPEED) this.pendingImpact = Math.max(this.pendingImpact, impactSpeed);
    });
  }

  applyInput(input: DriveInput, deltaSeconds: number): void {
    const { body } = this;
    const groundY = this.heightAt(body.position.x, body.position.z) + PEGASUS_GROUND_OFFSET;
    const ascend = input.handbrake;
    const descend = input.descend;

    // Land (or stay planted) when near the ground and not flapping upward.
    const nearGround = body.position.y - groundY < 0.25;
    this.grounded = nearGround && !ascend && body.velocity.y <= 1.5;

    // Heading
    this.steerInput = input.steer;
    const turnRate = this.grounded ? GROUND_TURN_RATE : AIR_TURN_RATE;
    this.yaw += -input.steer * turnRate * deltaSeconds;
    const forwardX = Math.sin(this.yaw);
    const forwardZ = Math.cos(this.yaw);

    // Speed: a collision shows up as lost forward velocity, so never carry more than was kept.
    const measured = body.velocity.x * forwardX + body.velocity.z * forwardZ;
    this.speed = Math.min(this.speed, Math.max(0, measured) + 2);
    const topSpeed = this.grounded ? GROUND_TOP_SPEED : AIR_TOP_SPEED;
    if (input.throttle > 0) {
      this.speed += ACCELERATION * input.throttle * deltaSeconds;
    } else if (input.brake > 0) {
      this.speed -= BRAKING * input.brake * deltaSeconds;
    } else {
      // Coast: slow to a halt on the ground, settle to a glide in the air.
      const rest = this.grounded ? 0 : GLIDE_MIN_SPEED;
      this.speed += (rest - this.speed) * (1 - Math.exp(-(this.grounded ? 1.2 : 0.35) * deltaSeconds));
    }
    if (this.speed > topSpeed) this.speed += (topSpeed - this.speed) * (1 - Math.exp(-6 * deltaSeconds));
    this.speed = Math.max(0, this.speed);

    body.velocity.x = forwardX * this.speed;
    body.velocity.z = forwardZ * this.speed;

    // Vertical
    if (this.grounded) {
      body.position.y = groundY;
      body.velocity.y = 0;
      this.climbInput = 0;
    } else {
      // Wings carry more of the weight the faster the pegasus flies.
      const lift = THREE.MathUtils.clamp(this.speed / 20, 0.35, 0.94);
      let vy = body.velocity.y + GRAVITY * lift * deltaSeconds;
      if (ascend) vy += FLAP_ACCELERATION * deltaSeconds;
      if (descend) vy -= FLAP_ACCELERATION * deltaSeconds;
      if (!ascend && !descend) vy *= Math.exp(-0.9 * deltaSeconds);
      if (body.position.y > MAX_ALTITUDE && vy > 0) vy *= Math.exp(-6 * deltaSeconds);
      body.velocity.y = THREE.MathUtils.clamp(vy, -MAX_DIVE_SPEED, MAX_CLIMB_SPEED);
      this.climbInput = ascend ? 1 : descend ? -1 : THREE.MathUtils.clamp(body.velocity.y / 8, -1, 0.3);
    }

    // Keep the player inside the mountain rim.
    const radius = Math.hypot(body.position.x, body.position.z);
    if (radius > WORLD_RADIUS) {
      const scale = WORLD_RADIUS / radius;
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
      // Follow the slope under the hooves.
      const ahead = this.heightAt(body.position.x + Math.sin(this.yaw) * 1.2, body.position.z + Math.cos(this.yaw) * 1.2);
      const behind = this.heightAt(body.position.x - Math.sin(this.yaw) * 1.2, body.position.z - Math.cos(this.yaw) * 1.2);
      targetPitch = -Math.atan2(ahead - behind, 2.4);
      targetRoll = this.steerInput * 0.12 * Math.min(1, this.speed / 8);
    } else {
      targetPitch = -Math.atan2(body.velocity.y, Math.max(this.speed, 8)) * 0.85;
      targetRoll = this.steerInput * 0.6;
    }
    const blend = 1 - Math.exp(-5 * deltaSeconds);
    this.visualPitch += (targetPitch - this.visualPitch) * blend;
    this.visualRoll += (targetRoll - this.visualRoll) * blend;
    this.mesh.rotation.set(this.visualPitch, this.yaw, this.visualRoll, 'YXZ');

    this.model.update(deltaSeconds, { speed: this.speed, grounded: this.grounded, climb: this.climbInput });
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
  }

  isGrounded(): boolean {
    return this.grounded;
  }

  getAltitude(): number {
    const { position } = this.body;
    return Math.max(0, position.y - PEGASUS_GROUND_OFFSET - this.heightAt(position.x, position.z));
  }

  getSpeedMetersPerSecond(): number {
    return this.body.velocity.length();
  }

  getSpeedKmh(): number {
    return this.getSpeedMetersPerSecond() * 3.6;
  }

  getHudStatus(): HudStatus {
    return {
      label: this.grounded ? 'GALLOP' : `ALT ${Math.round(this.getAltitude())} m`,
      bar: THREE.MathUtils.clamp(this.speed / AIR_TOP_SPEED, 0, 1),
    };
  }
}
