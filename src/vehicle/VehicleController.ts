import * as CANNON from 'cannon-es';
import * as THREE from 'three';
import type { PhysicsWorld } from '../physics/PhysicsWorld';
import type { DriveInput } from '../core/InputManager';
import { getWheelLayout, type VehicleConfig } from './VehicleConfig';
import { buildCarBody, buildWheel } from './CarModelBuilder';

const REVERSE_TOP_SPEED = 14;
const MIN_IMPACT_SPEED = 2.5;
const WHEEL_ORDER = ['frontLeft', 'frontRight', 'rearLeft', 'rearRight'] as const;
type WheelName = (typeof WHEEL_ORDER)[number];

/** Arcade-leaning but physically grounded car: raycast suspension, real engine/brake forces, drift via reduced rear slip grip. */
export class VehicleController {
  readonly config: VehicleConfig;
  readonly chassisBody: CANNON.Body;
  readonly vehicle: CANNON.RaycastVehicle;
  readonly chassisMesh: THREE.Group;
  readonly wheelMeshes: Record<WheelName, THREE.Group>;

  private currentSteer = 0;
  private rpm = 800; // idle RPM, purely presentational
  private gear = 1;
  private reverseEngaged = false;
  private pendingImpact = 0;
  private readonly visualSuspension: number[];

  constructor(physics: PhysicsWorld, config: VehicleConfig, scene: THREE.Scene, startPosition: CANNON.Vec3) {
    this.config = config;
    const { x: w, y: h, z: l } = config.chassisSize;

    const chassisShape = new CANNON.Box(new CANNON.Vec3(w / 2, h / 2, l / 2));
    this.chassisBody = new CANNON.Body({ mass: config.chassisMass });
    this.chassisBody.addShape(chassisShape);
    this.chassisBody.position.copy(startPosition);
    this.chassisBody.angularDamping = 0.6;
    this.chassisBody.linearDamping = 0.02;

    this.vehicle = new CANNON.RaycastVehicle({
      chassisBody: this.chassisBody,
      indexRightAxis: 0,
      indexUpAxis: 1,
      indexForwardAxis: 2,
    });

    const { halfTrack: axleHalfWidth, frontZ, rearZ } = getWheelLayout(config);

    const wheelOptions = {
      radius: config.wheelRadius,
      directionLocal: new CANNON.Vec3(0, -1, 0),
      suspensionStiffness: config.suspensionStiffness,
      suspensionRestLength: config.suspensionRestLength,
      frictionSlip: config.wheelFriction,
      dampingRelaxation: config.dampingRelaxation,
      dampingCompression: config.dampingCompression,
      maxSuspensionForce: 100000,
      rollInfluence: 0.02,
      axleLocal: new CANNON.Vec3(1, 0, 0),
      chassisConnectionPointLocal: new CANNON.Vec3(1, 0, 0),
      maxSuspensionTravel: 0.3,
      customSlidingRotationalSpeed: -30,
      useCustomSlidingRotationalSpeed: true,
    };

    const connectionPoints: Record<WheelName, CANNON.Vec3> = {
      frontLeft: new CANNON.Vec3(-axleHalfWidth, 0, frontZ),
      frontRight: new CANNON.Vec3(axleHalfWidth, 0, frontZ),
      rearLeft: new CANNON.Vec3(-axleHalfWidth, 0, rearZ),
      rearRight: new CANNON.Vec3(axleHalfWidth, 0, rearZ),
    };

    for (const name of WHEEL_ORDER) {
      this.vehicle.addWheel({ ...wheelOptions, chassisConnectionPointLocal: connectionPoints[name] });
    }
    this.vehicle.addToWorld(physics.world);
    this.visualSuspension = WHEEL_ORDER.map(() => config.suspensionRestLength);

    this.chassisBody.addEventListener('collide', (event: { contact: CANNON.ContactEquation }) => {
      const impactSpeed = Math.abs(event.contact.getImpactVelocityAlongNormal());
      if (impactSpeed > MIN_IMPACT_SPEED) this.pendingImpact = Math.max(this.pendingImpact, impactSpeed);
    });

    // Visuals
    this.chassisMesh = buildCarBody(config);
    scene.add(this.chassisMesh);

    this.wheelMeshes = {} as Record<WheelName, THREE.Group>;
    for (const name of WHEEL_ORDER) {
      const mesh = buildWheel(config);
      // Wheels are children of the chassis so they inherit its interpolated transform exactly.
      this.chassisMesh.add(mesh);
      this.wheelMeshes[name] = mesh;
    }
  }

  applyInput(input: DriveInput, deltaSeconds: number): void {
    const { config, vehicle } = this;

    const targetSteer = -input.steer * config.maxSteerAngleRad;
    const steerLerp = 1 - Math.exp(-config.steerSpeed * deltaSeconds);
    this.currentSteer += (targetSteer - this.currentSteer) * steerLerp;
    vehicle.setSteeringValue(this.currentSteer, 0);
    vehicle.setSteeringValue(this.currentSteer, 1);

    const speed = this.getSpeedMetersPerSecond();
    const forwardSpeed = this.getForwardSpeed();
    const speedRatio = THREE.MathUtils.clamp(speed / config.topSpeed, 0, 1);

    // Brake pedal doubles as reverse once the car has (nearly) stopped, and vice versa for throttle.
    const reversing = input.brake > 0 && input.throttle === 0 && forwardSpeed < 1;
    const brakingFromReverse = input.throttle > 0 && forwardSpeed < -1;
    this.reverseEngaged = reversing;

    let engineForce = 0;
    let brakeForce = 0;
    if (reversing) {
      const reverseRatio = THREE.MathUtils.clamp(-forwardSpeed / REVERSE_TOP_SPEED, 0, 1);
      engineForce = -config.maxEngineForce * 0.6 * input.brake * (1 - reverseRatio);
    } else if (brakingFromReverse) {
      brakeForce = config.maxBrakeForce * input.throttle;
    } else {
      engineForce = config.maxEngineForce * input.throttle * (1 - speedRatio * 0.85);
      brakeForce = config.maxBrakeForce * input.brake;
    }

    // Off-throttle: engine braking while rolling, and a hold brake so the car stays put on slopes.
    if (input.throttle === 0 && input.brake === 0) {
      brakeForce = Math.abs(forwardSpeed) < 3 ? config.maxBrakeForce * 0.5 : config.maxBrakeForce * 0.06;
      // Parked: bleed off the solver's residual micro-motion so the body does not shimmer at rest.
      if (speed < 0.6 && vehicle.numWheelsOnGround >= 3) {
        this.chassisBody.velocity.scale(0.85, this.chassisBody.velocity);
        this.chassisBody.angularVelocity.scale(0.85, this.chassisBody.angularVelocity);
      }
    }

    const driveWheels: number[] =
      config.drive === 'FWD' ? [0, 1] : config.drive === 'RWD' ? [2, 3] : [0, 1, 2, 3];
    // maxEngineForce is defined per axle, so total thrust is the same for FWD/RWD/AWD layouts.
    const perWheelForce = (engineForce * 2) / driveWheels.length;
    for (let i = 0; i < 4; i++) {
      vehicle.applyEngineForce(driveWheels.includes(i) ? -perWheelForce : 0, i);
    }

    const handbrakeForce = input.handbrake ? config.maxBrakeForce * 2 : 0;
    for (let i = 0; i < 4; i++) {
      vehicle.setBrake(i < 2 ? brakeForce : brakeForce + handbrakeForce, i);
    }

    // Purely cosmetic RPM/gear model driven by current speed vs top speed.
    const gearCount = 5;
    const gearSpan = 1 / gearCount;
    this.gear = Math.min(gearCount, Math.floor(speedRatio / gearSpan) + 1);
    const gearLocalRatio = (speedRatio % gearSpan) / gearSpan;
    this.rpm = 900 + gearLocalRatio * 6200;
  }

  /**
   * Copies physics state to the meshes. Uses cannon's interpolated transform so motion stays
   * smooth when the display refresh rate differs from the fixed 60 Hz physics step.
   */
  syncVisuals(deltaSeconds: number): void {
    this.chassisMesh.position.copy(this.chassisBody.interpolatedPosition as unknown as THREE.Vector3);
    this.chassisMesh.quaternion.copy(this.chassisBody.interpolatedQuaternion as unknown as THREE.Quaternion);

    const suspensionLerp = 1 - Math.exp(-30 * deltaSeconds);
    for (let i = 0; i < WHEEL_ORDER.length; i++) {
      const wheel = this.vehicle.wheelInfos[i];
      const mesh = this.wheelMeshes[WHEEL_ORDER[i]];
      const connection = wheel.chassisConnectionPointLocal;
      this.visualSuspension[i] += (wheel.suspensionLength - this.visualSuspension[i]) * suspensionLerp;
      mesh.position.set(connection.x, connection.y - this.visualSuspension[i], connection.z);
      mesh.rotation.set(wheel.rotation, wheel.steering, 0, 'YXZ');
    }
  }

  /** Largest recent impact speed (m/s); decays over time. Drives camera shake and, later, audio. */
  consumeImpact(): number {
    const impact = this.pendingImpact;
    this.pendingImpact = 0;
    return impact;
  }

  getSpeedMetersPerSecond(): number {
    return this.chassisBody.velocity.length();
  }

  /** Signed speed along the car's nose direction (negative when rolling backwards). */
  getForwardSpeed(): number {
    const forward = this.chassisBody.quaternion.vmult(new CANNON.Vec3(0, 0, 1));
    return this.chassisBody.velocity.dot(forward);
  }

  getSpeedKmh(): number {
    return this.getSpeedMetersPerSecond() * 3.6;
  }

  getRpm(): number {
    return this.rpm;
  }

  /** 0 = reverse, otherwise the forward gear number. */
  getGear(): number {
    return this.reverseEngaged ? 0 : this.gear;
  }

  getWorldPosition(): CANNON.Vec3 {
    return this.chassisBody.position;
  }

  resetTo(position: CANNON.Vec3, yawRadians = 0): void {
    this.chassisBody.position.copy(position);
    this.chassisBody.velocity.setZero();
    this.chassisBody.angularVelocity.setZero();
    this.chassisBody.quaternion.setFromAxisAngle(new CANNON.Vec3(0, 1, 0), yawRadians);
    // Keep the interpolated pose in step so the car does not visibly sweep across the map on reset.
    this.chassisBody.interpolatedPosition.copy(this.chassisBody.position);
    this.chassisBody.interpolatedQuaternion.copy(this.chassisBody.quaternion);
    this.chassisBody.previousPosition.copy(this.chassisBody.position);
    this.chassisBody.previousQuaternion.copy(this.chassisBody.quaternion);
  }
}
