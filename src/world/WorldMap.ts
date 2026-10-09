import { createNoise2D, fbm, smoothstep } from '../core/Noise';
import { PLAYABLE_RADIUS, WATER_LEVEL } from './WorldConfig';

/**
 * The shape of the realm, compressed from the setting of House of the Dragon: Blackwater Bay to the east,
 * King's Landing on its hills along the western shore, the volcano island of Dragonstone to the north-east,
 * Driftmark to the south-east, and Harrenhal beside the Gods Eye lake in the west. Coordinates are metres;
 * +X is east, +Z is south.
 */

const hillNoise = createNoise2D(11);
const mountainNoise = createNoise2D(93);
const shoreNoise = createNoise2D(305);

export interface Pad {
  id: string;
  x: number;
  z: number;
  /** Fully flat out to this radius, easing back into the land by 1.8x this. */
  radius: number;
}

export interface Landmark {
  model: string;
  x: number;
  z: number;
  /** Rotation about the vertical axis so the entrance faces the right way. */
  yaw: number;
  scale: number;
  pad: Pad;
}

export const KINGS_LANDING = { x: 390, z: -95 };
export const DRAGONMONT = { x: 1190, z: -645, radius: 360, peak: 310 };
export const DRIFTMARK = { x: 1010, z: 620, radius: 400, peak: 125 };
export const GODS_EYE = { x: -470, z: 520, radius: 400, depth: 22 };

/** West shore of the bay: land lies to the west (smaller x) of this line. */
export function coastX(z: number): number {
  return 520 + 70 * Math.sin(z / 260 + 0.6) + 40 * Math.sin(z / 110) + (shoreNoise(z / 90, 4.2) - 0.5) * 50;
}

export function seaAmount(x: number, z: number): number {
  const shore = coastX(z);
  return smoothstep(shore - 40, shore + 60, x);
}

function bump(x: number, z: number, cx: number, cz: number, radius: number, height: number): number {
  const d = Math.hypot(x - cx, z - cz) / radius;
  return d >= 1 ? 0 : height * Math.pow(1 - d * d, 2);
}

function cone(x: number, z: number, cx: number, cz: number, radius: number, peak: number): number {
  const d = Math.hypot(x - cx, z - cz);
  const t = Math.max(0, 1 - d / radius);
  const ridges = 1 + 0.4 * (fbm(mountainNoise, (x - cx) / 90 + 40, (z - cz) / 90 + 40, 3) - 0.5);
  return peak * Math.pow(t, 1.35) * ridges;
}

/** Gentle large-scale relief. */
export function rollingHeight(x: number, z: number): number {
  return (fbm(hillNoise, x / 320, z / 320, 2) - 0.5) * 22;
}

/** Terrain height before any castle platforms are levelled in. */
function baseHeight(x: number, z: number): number {
  const sea = seaAmount(x, z);
  const r = Math.hypot(x, z);

  // Land: rolling country, foothills inland, and the mountain rim round the west, north and south edges.
  const rim = smoothstep(PLAYABLE_RADIUS * 0.62, PLAYABLE_RADIUS * 1.05, r);
  const mountains = rim * rim * (60 + fbm(mountainNoise, x / 260, z / 260, 5) * 330);
  const hills = Math.pow(fbm(mountainNoise, x / 520 + 9.1, z / 520 - 4.7, 3), 3) * 150 * smoothstep(330, 720, Math.hypot(x + 150, z + 60));
  const gods = Math.hypot(x - GODS_EYE.x, z - GODS_EYE.z);
  const basin = smoothstep(GODS_EYE.radius, GODS_EYE.radius * 0.3, gods);
  const land = ((rollingHeight(x, z) + 8 + hills) * (1 - basin * 0.85) - basin * GODS_EYE.depth + mountains) * (1 - sea * 0.8);

  // Sea floor deepens offshore.
  const shore = coastX(z);
  const seabed = WATER_LEVEL - 5 - 40 * smoothstep(shore, shore + 520, x);

  let height = land + (seabed - land) * sea;

  // The three hills of King's Landing, and the two islands.
  height += bump(x, z, 455, -165, 105, 56) + bump(x, z, 330, -95, 88, 36) + bump(x, z, 385, 15, 95, 44);
  height += cone(x, z, DRAGONMONT.x, DRAGONMONT.z, DRAGONMONT.radius, DRAGONMONT.peak);
  height += cone(x, z, DRIFTMARK.x, DRIFTMARK.z, DRIFTMARK.radius, DRIFTMARK.peak);
  return height;
}

export const PADS: Record<string, Pad> = {
  redKeep: { id: 'redKeep', x: 455, z: -165, radius: 92 },
  dragonpit: { id: 'dragonpit', x: 385, z: 15, radius: 68 },
  greatSept: { id: 'greatSept', x: 330, z: -95, radius: 46 },
  dragonstone: { id: 'dragonstone', x: 1010, z: -585, radius: 108 },
  highTide: { id: 'highTide', x: 1000, z: 600, radius: 52 },
  harrenhal: { id: 'harrenhal', x: -470, z: 40, radius: 175 },
};

const padHeights = new Map<string, number>();
function padHeight(pad: Pad): number {
  let h = padHeights.get(pad.id);
  if (h === undefined) {
    h = Math.max(baseHeight(pad.x, pad.z), WATER_LEVEL + 8);
    padHeights.set(pad.id, h);
  }
  return h;
}

/** Large-scale terrain height with castle platforms levelled in; roughness is added by the terrain itself. */
export function macroHeight(x: number, z: number): number {
  let height = baseHeight(x, z);
  for (const pad of Object.values(PADS)) {
    const d = Math.hypot(x - pad.x, z - pad.z);
    if (d >= pad.radius * 1.8) continue;
    const t = smoothstep(pad.radius * 1.8, pad.radius, d);
    height += (padHeight(pad) - height) * t;
  }
  return height;
}

/** 0 away from castle platforms, rising to 1 on them; used to keep roughness off the flat ground. */
export function padInfluence(x: number, z: number): number {
  let influence = 0;
  for (const pad of Object.values(PADS)) {
    const d = Math.hypot(x - pad.x, z - pad.z);
    influence = Math.max(influence, smoothstep(pad.radius * 1.9, pad.radius * 1.1, d));
  }
  return influence;
}

/** True where trees, houses and props should not be placed: castle platforms. */
export function isReserved(x: number, z: number, margin = 0): boolean {
  for (const pad of Object.values(PADS)) {
    if (Math.hypot(x - pad.x, z - pad.z) < pad.radius * 1.15 + margin) return true;
  }
  return false;
}

export const LANDMARKS: Landmark[] = [
  { model: 'red_keep', x: PADS.redKeep.x, z: PADS.redKeep.z, yaw: Math.PI * 0.62, scale: 1, pad: PADS.redKeep },
  { model: 'dragonpit', x: PADS.dragonpit.x, z: PADS.dragonpit.z, yaw: Math.PI * 0.5, scale: 1, pad: PADS.dragonpit },
  { model: 'great_sept', x: PADS.greatSept.x, z: PADS.greatSept.z, yaw: Math.PI * 0.4, scale: 1, pad: PADS.greatSept },
  { model: 'dragonstone', x: PADS.dragonstone.x, z: PADS.dragonstone.z, yaw: -Math.PI * 0.5, scale: 1, pad: PADS.dragonstone },
  { model: 'high_tide', x: PADS.highTide.x, z: PADS.highTide.z, yaw: -Math.PI * 0.5, scale: 1, pad: PADS.highTide },
  { model: 'harrenhal', x: PADS.harrenhal.x, z: PADS.harrenhal.z, yaw: Math.PI * 0.5, scale: 1, pad: PADS.harrenhal },
];

export interface Settlement {
  id: string;
  x: number;
  z: number;
  radiusX: number;
  radiusZ: number;
  houses: number;
  /** Distance between house plots; tighter in cities. */
  spacing: number;
}

export const SETTLEMENTS: Settlement[] = [
  { id: 'kings_landing', x: 335, z: -70, radiusX: 215, radiusZ: 215, houses: 320, spacing: 14 },
  { id: 'dragonstone_harbour', x: 935, z: -640, radiusX: 55, radiusZ: 62, houses: 26, spacing: 15 },
  { id: 'harrenhal_town', x: -400, z: 235, radiusX: 95, radiusZ: 65, houses: 34, spacing: 15 },
  { id: 'driftmark_harbour', x: 850, z: 640, radiusX: 75, radiusZ: 75, houses: 26, spacing: 15 },
];

/** Control points for the roads, as (x, z). */
export const ROADS: Array<{ id: string; points: Array<[number, number]>; width: number }> = [
  {
    id: 'coast_road',
    width: 12,
    points: [[255, -690], [380, -520], [455, -350], [430, -230], [395, -60], [430, 100], [400, 260], [320, 400], [200, 520], [60, 610]],
  },
  {
    id: 'kings_road',
    width: 12,
    points: [[330, -60], [220, -10], [90, 60], [-60, 110], [-210, 70], [-330, 20], [-390, 40]],
  },
];

export interface Lair {
  x: number;
  z: number;
  altitude: number;
  /** Radius of the circuit it flies while idle. */
  patrol: number;
}

/** Where each dragon makes its home. */
export const LAIRS: Record<string, Lair> = {
  vermithor: { x: 1190, z: -645, altitude: 150, patrol: 240 },
  vhagar: { x: -470, z: 60, altitude: 90, patrol: 260 },
  caraxes: { x: 990, z: 600, altitude: 90, patrol: 200 },
};

/** Where the player begins: beside the Dragonpit. */
export const PLAYER_START = { x: 385, z: -70, yaw: Math.PI * 0.5 };
