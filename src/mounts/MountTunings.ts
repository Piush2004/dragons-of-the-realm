import type { FlyingMountTuning } from './MountModel';
import { PEGASUS_GROUND_OFFSET } from '../pegasus/PegasusModel';
import { DRAGON_GROUND_OFFSET } from '../dragon/DragonModel';

export const PEGASUS_TUNING: FlyingMountTuning = {
  mass: 420,
  bodyRadius: 0.65,
  groundOffset: PEGASUS_GROUND_OFFSET,
  groundTopSpeed: 17,
  airTopSpeed: 44,
  glideMinSpeed: 11,
  acceleration: 9,
  braking: 16,
  groundTurnRate: 1.9,
  airTurnRate: 1.15,
  flapAcceleration: 26,
  maxClimbSpeed: 13,
  maxDiveSpeed: 24,
  maxBank: 0.6,
  groundedLabel: 'GALLOP',
};

/** Heavier and far faster in the air than the pegasus, but slow and lumbering on foot. */
export const DRAGON_TUNING: FlyingMountTuning = {
  mass: 2600,
  bodyRadius: 1.3,
  groundOffset: DRAGON_GROUND_OFFSET,
  groundTopSpeed: 9,
  airTopSpeed: 58,
  glideMinSpeed: 15,
  acceleration: 10,
  braking: 14,
  groundTurnRate: 1.3,
  airTurnRate: 0.95,
  flapAcceleration: 22,
  maxClimbSpeed: 15,
  maxDiveSpeed: 34,
  maxBank: 0.75,
  groundedLabel: 'WALK',
};
