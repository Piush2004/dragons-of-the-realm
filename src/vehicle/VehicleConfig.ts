export interface VehicleConfig {
  id: string;
  displayName: string;
  /** Visual + collision chassis dimensions in meters (x=width, y=height, z=length). */
  chassisSize: { x: number; y: number; z: number };
  chassisMass: number;
  /** Peak engine force applied at the drive wheels. */
  maxEngineForce: number;
  maxBrakeForce: number;
  maxSteerAngleRad: number;
  /** How quickly steering reaches its target angle, higher = snappier. */
  steerSpeed: number;
  suspensionStiffness: number;
  suspensionRestLength: number;
  dampingCompression: number;
  dampingRelaxation: number;
  wheelRadius: number;
  wheelFriction: number;
  /** Top speed in m/s, used to taper engine force so the car has a believable ceiling. */
  topSpeed: number;
  drive: 'FWD' | 'RWD' | 'AWD';
  color: number;
  body: VehicleBodyOptions;
}

/** Visual-only options consumed by the procedural car model builder. */
export interface VehicleBodyOptions {
  style: 'coupe' | 'sedan' | 'hatch';
  finish: 'gloss' | 'satin';
  wheelColor: number;
  /** Contrast roof colour; defaults to the body colour. */
  roofColor?: number;
  rearWing: boolean;
  exhausts: 2 | 4;
}

export const VEHICLE_CONFIGS: Record<string, VehicleConfig> = {
  verdant_rs: {
    id: 'verdant_rs',
    displayName: 'Verdant RS',
    chassisSize: { x: 1.92, y: 0.55, z: 4.95 },
    chassisMass: 1750,
    maxEngineForce: 5200,
    maxBrakeForce: 170,
    maxSteerAngleRad: 0.5,
    steerSpeed: 4.0,
    suspensionStiffness: 34,
    suspensionRestLength: 0.32,
    dampingCompression: 4.6,
    dampingRelaxation: 3.2,
    wheelRadius: 0.36,
    wheelFriction: 2.7,
    topSpeed: 86,
    drive: 'AWD',
    color: 0x26382b,
    body: { style: 'sedan', finish: 'satin', wheelColor: 0xb8733a, roofColor: 0x0a0b0c, rearWing: true, exhausts: 4 },
  },
  striker_gt: {
    id: 'striker_gt',
    displayName: 'Striker GT',
    chassisSize: { x: 1.9, y: 0.55, z: 4.4 },
    chassisMass: 1250,
    maxEngineForce: 3200,
    maxBrakeForce: 120,
    maxSteerAngleRad: 0.55,
    steerSpeed: 4.0,
    suspensionStiffness: 32,
    suspensionRestLength: 0.32,
    dampingCompression: 4.4,
    dampingRelaxation: 3.0,
    wheelRadius: 0.37,
    wheelFriction: 2.6,
    topSpeed: 75,
    drive: 'RWD',
    color: 0xb3121c,
    body: { style: 'coupe', finish: 'gloss', wheelColor: 0xc9ccd2, rearWing: false, exhausts: 2 },
  },
  rally_pup: {
    id: 'rally_pup',
    displayName: 'Rally Pup',
    chassisSize: { x: 1.8, y: 0.6, z: 4.1 },
    chassisMass: 1150,
    maxEngineForce: 2600,
    maxBrakeForce: 100,
    maxSteerAngleRad: 0.65,
    steerSpeed: 5.0,
    suspensionStiffness: 26,
    suspensionRestLength: 0.42,
    dampingCompression: 3.6,
    dampingRelaxation: 2.6,
    wheelRadius: 0.4,
    wheelFriction: 2.3,
    topSpeed: 58,
    drive: 'AWD',
    color: 0xe8a000,
    body: { style: 'hatch', finish: 'gloss', wheelColor: 0xf2f2f2, rearWing: false, exhausts: 2 },
  },
  apex_prototype: {
    id: 'apex_prototype',
    displayName: 'Apex Prototype',
    chassisSize: { x: 1.95, y: 0.45, z: 4.6 },
    chassisMass: 1050,
    maxEngineForce: 3800,
    maxBrakeForce: 140,
    maxSteerAngleRad: 0.48,
    steerSpeed: 4.5,
    suspensionStiffness: 40,
    suspensionRestLength: 0.26,
    dampingCompression: 5.2,
    dampingRelaxation: 3.6,
    wheelRadius: 0.35,
    wheelFriction: 2.9,
    topSpeed: 88,
    drive: 'RWD',
    color: 0x2a6fd6,
    body: { style: 'coupe', finish: 'gloss', wheelColor: 0x1c1c1f, rearWing: true, exhausts: 4 },
  },
};

/** Wheel mounting points relative to the chassis centre; shared by physics and the body's wheel-arch cutouts. */
export function getWheelLayout(config: VehicleConfig): { halfTrack: number; frontZ: number; rearZ: number } {
  return {
    halfTrack: config.chassisSize.x / 2 - 0.15,
    frontZ: config.chassisSize.z / 2 - 0.82,
    rearZ: -config.chassisSize.z / 2 + 0.78,
  };
}
