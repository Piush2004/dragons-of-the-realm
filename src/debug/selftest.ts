/**
 * In-browser self-test suite. Run it by opening the game with `?selftest=1` (results appear in the console and
 * in `window.__selftestResults`), or call `window.__horizon.selfTest(['world', 'flight', ...])`.
 *
 * Tests drive the real game objects through `window.__horizon` using fixed 60 Hz simulation steps, so results
 * do not depend on frame rate. They change game state, so reload the page after a run.
 */
import * as THREE from 'three';
import { PLAYABLE_RADIUS, WATER_LEVEL } from '../world/WorldConfig';
import { PADS, PLAYER_START } from '../world/WorldMap';

export interface TestResult {
  group: string;
  name: string;
  pass: boolean;
  detail: string;
}

// The debug handle is intentionally loosely typed: it exposes most of the game's internals.
// eslint-disable-next-line @typescript-eslint/no-explicit-any
type H = any;
type Input = Record<string, unknown>;

const results: TestResult[] = [];

async function check(group: string, name: string, fn: () => Promise<string | void> | string | void): Promise<void> {
  try {
    const detail = await fn();
    results.push({ group, name, pass: true, detail: detail ?? '' });
  } catch (error) {
    results.push({ group, name, pass: false, detail: error instanceof Error ? error.message : String(error) });
  }
}

function expect(condition: boolean, message: string): void {
  if (!condition) throw new Error(message);
}

const fmt = (n: number, digits = 1) => Number(n.toFixed(digits));

function reset(h: H): void {
  for (const enemy of [...h.enemies]) h.removeEnemy(enemy);
  h.player.revive();
  const offset = h.player.controller.tuning.groundOffset;
  h.player.controller.resetTo(h.placeOnGround(PLAYER_START.x, PLAYER_START.z, offset), PLAYER_START.yaw);
  h.simulate(0.5);
}

function takeOff(h: H, altitude = 80): void {
  for (let i = 0; i < 20 && h.player.controller.getAltitude() < altitude; i++) h.simulate(0.5, { throttle: 1, handbrake: true });
}

/** An enemy with its AI switched off, so it only does what the test tells it to. */
function dummy(h: H, id: string, position: THREE.Vector3): H {
  const enemy = h.spawnEnemy(id, { x: position.x, y: position.y, z: position.z });
  h.enemies.splice(h.enemies.indexOf(enemy), 1);
  return enemy.combatant;
}

function ahead(h: H, distance: number, lateral = 0, height = 0): THREE.Vector3 {
  const heading = h.player.controller.heading;
  const p = h.player.controller.body.position;
  const fx = Math.sin(heading);
  const fz = Math.cos(heading);
  return new THREE.Vector3(p.x + fx * distance + fz * lateral, p.y + height, p.z + fz * distance - fx * lateral);
}

/** Pins `target` in place relative to the player while the simulation runs. */
function hold(h: H, target: H, distance: number, seconds: number, input: Input, lateral = 0): void {
  for (let i = 0; i < seconds * 60; i++) {
    const place = ahead(h, distance, lateral);
    target.controller.body.position.set(place.x, place.y, place.z);
    target.controller.body.velocity.set(0, 0, 0);
    h.simulate(1 / 60, input);
  }
}

function recordDamage(h: H): { amounts: number[]; restore: () => void } {
  const previous = h.combat.onDamage;
  const amounts: number[] = [];
  h.combat.onDamage = (victim: H, amount: number, attacker: H) => {
    if (victim !== h.player) amounts.push(amount);
    previous(victim, amount, attacker);
  };
  return { amounts, restore: () => (h.combat.onDamage = previous) };
}

// ------------------------------------------------------------------------------------------------ groups

async function worldTests(h: H): Promise<void> {
  const g = 'world';
  const terrain = h.world.terrain;
  await check(g, 'terrain heights are finite and plausible', () => {
    let min = Infinity;
    let max = -Infinity;
    for (let i = 0; i < 3000; i++) {
      const x = ((i * 7919) % 3000) - 1500;
      const z = ((i * 104729) % 3000) - 1500;
      const y = terrain.getHeightAt(x, z);
      expect(Number.isFinite(y), `non-finite height at ${x},${z}`);
      min = Math.min(min, y);
      max = Math.max(max, y);
    }
    expect(min > -90 && max < 450, `height range ${fmt(min)}..${fmt(max)} outside -90..450`);
    return `range ${fmt(min)} to ${fmt(max)} m`;
  });
  await check(g, 'the bay east of the coast is below water', () => {
    const y = terrain.getHeightAt(1450, 0);
    expect(y < WATER_LEVEL - 5, `sea floor at ${fmt(y)} m is not below water ${WATER_LEVEL}`);
    return `${fmt(y)} m`;
  });
  await check(g, 'castle platforms are level', () => {
    const spread: string[] = [];
    for (const pad of Object.values(PADS)) {
      let min = Infinity;
      let max = -Infinity;
      for (let k = 0; k < 24; k++) {
        const a = (k / 24) * Math.PI * 2;
        const y = terrain.getHeightAt(pad.x + Math.cos(a) * pad.radius * 0.8, pad.z + Math.sin(a) * pad.radius * 0.8);
        min = Math.min(min, y);
        max = Math.max(max, y);
      }
      expect(max - min < 2, `${pad.id} pad varies by ${fmt(max - min)} m`);
      spread.push(`${pad.id} ${fmt(max - min)}`);
    }
    return spread.join(', ');
  });
  await check(g, 'towns and forests are populated', () => {
    expect(h.world.settlements.houseCount >= 150, `only ${h.world.settlements.houseCount} houses`);
    expect(h.world.treeCount >= 10000, `only ${h.world.treeCount} trees`);
    return `${h.world.settlements.houseCount} houses, ${h.world.treeCount} trees`;
  });
  await check(g, 'roads stay above water', () => {
    let lowest = Infinity;
    let samples = 0;
    for (const track of h.world.tracks) {
      for (const p of track.getCenterlineSamples(20)) {
        lowest = Math.min(lowest, p.y);
        samples++;
      }
    }
    expect(lowest > WATER_LEVEL + 0.5, `a road dips to ${fmt(lowest)} m`);
    return `${samples} samples, lowest ${fmt(lowest)} m`;
  });
  await check(g, 'the player starts on dry land', () => {
    const y = terrain.getHeightAt(PLAYER_START.x, PLAYER_START.z);
    expect(y > WATER_LEVEL + 2, `start height ${fmt(y)}`);
    return `${fmt(y)} m`;
  });
}

async function flightTests(h: H): Promise<void> {
  const g = 'flight';
  const c = () => h.player.controller;
  reset(h);
  await check(g, 'starts grounded', () => {
    expect(c().isGrounded(), 'not grounded');
  });
  await check(g, 'flapping takes off and climbs', () => {
    h.simulate(4, { throttle: 1, handbrake: true });
    const altitude = c().getAltitude();
    expect(altitude > 30, `only ${fmt(altitude)} m after 4 s`);
    return `${fmt(altitude)} m after 4 s`;
  });
  await check(g, 'level flight reaches top speed', () => {
    takeOff(h, 70);
    h.simulate(6, { throttle: 1 });
    const speed = c().currentSpeed;
    const top = c().tuning.airTopSpeed;
    expect(speed > top * 0.9 && speed < top * 1.05, `speed ${fmt(speed)} vs top ${top}`);
    return `${fmt(speed)} of ${top} m/s`;
  });
  await check(g, 'releasing throttle glides down in speed, not height', () => {
    const before = c().getAltitude();
    h.simulate(5, {});
    const after = c().getAltitude();
    const speed = c().currentSpeed;
    expect(speed < c().tuning.airTopSpeed * 0.8, `speed still ${fmt(speed)}`);
    expect(before - after < 60, `lost ${fmt(before - after)} m`);
    return `speed ${fmt(speed)} m/s, altitude change ${fmt(after - before)} m`;
  });
  await check(g, 'diving loses height quickly', () => {
    const before = c().getAltitude();
    h.simulate(3, { throttle: 1, descend: true });
    const lost = before - c().getAltitude();
    expect(lost > 15, `lost only ${fmt(lost)} m`);
    return `${fmt(lost)} m in 3 s`;
  });
  await check(g, 'turning follows steering at the tuned rate', () => {
    const before = c().heading;
    h.simulate(1, { throttle: 1, steer: -1, handbrake: c().getAltitude() < 40 });
    const delta = c().heading - before;
    const rate = c().tuning.airTurnRate;
    expect(delta > 0 && Math.abs(delta - rate) < rate * 0.2, `turned ${fmt(delta, 2)} rad, expected ${fmt(rate, 2)}`);
    return `${fmt(delta, 2)} rad in 1 s`;
  });
  await check(g, 'descending lands the dragon on the ground', () => {
    for (let i = 0; i < 40 && !c().isGrounded(); i++) h.simulate(0.5, { descend: true });
    expect(c().isGrounded(), `still at ${fmt(c().getAltitude())} m`);
    const ground = h.world.getStandingHeight(c().body.position.x, c().body.position.z) + c().tuning.groundOffset;
    expect(Math.abs(c().body.position.y - ground) < 0.6, `body ${fmt(c().body.position.y)} vs ground ${fmt(ground)}`);
    return 'grounded on the terrain';
  });
  await check(g, 'walking stays on the ground at walking pace', () => {
    let worst = 0;
    for (let i = 0; i < 6; i++) {
      h.simulate(1, { throttle: 1, steer: i % 2 ? 0.3 : -0.3 });
      const ground = h.world.getStandingHeight(c().body.position.x, c().body.position.z) + c().tuning.groundOffset;
      worst = Math.max(worst, Math.abs(c().body.position.y - ground));
    }
    const speed = c().currentSpeed;
    expect(speed <= c().tuning.groundTopSpeed + 1 && speed > 3, `walking speed ${fmt(speed)}`);
    expect(worst < 1.5, `drifted ${fmt(worst)} m off the terrain`);
    return `${fmt(speed)} m/s, max offset ${fmt(worst, 2)} m`;
  });
  await check(g, 'the world boundary holds', () => {
    takeOff(h, 80);
    c().setHeading(Math.PI / 2);
    h.simulate(100, { throttle: 1, handbrake: c().getAltitude() < 60 });
    const r = Math.hypot(c().body.position.x, c().body.position.z);
    expect(r <= PLAYABLE_RADIUS + 2, `reached radius ${fmt(r)}`);
    return `stopped at radius ${fmt(r)} of ${PLAYABLE_RADIUS}`;
  });
}

async function combatTests(h: H): Promise<void> {
  const g = 'combat';
  const profile = () => h.player.profile;

  reset(h);
  takeOff(h, 80);
  await check(g, 'a bite in range lands for the dragon\'s bite damage', () => {
    const target = dummy(h, 'brazenhide', ahead(h, 17));
    const recorder = recordDamage(h);
    h.player.biteCooldown = 0;
    hold(h, target, 17, 2.0, { throttle: 0.3, bite: true });
    recorder.restore();
    expect(recorder.amounts.length >= 1, 'no bite landed');
    const first = recorder.amounts[0];
    expect(first >= profile().biteDamage * 0.88 && first <= profile().biteDamage * 1.12, `damage ${fmt(first)} vs ${profile().biteDamage}`);
    h.removeEnemy({ combatant: target });
    return `${recorder.amounts.length} bites, first ${fmt(first)}`;
  });
  await check(g, 'a bite out of range misses', () => {
    const target = dummy(h, 'brazenhide', ahead(h, 36));
    const recorder = recordDamage(h);
    h.player.biteCooldown = 0;
    hold(h, target, 36, 1.8, { throttle: 0.3, bite: true });
    recorder.restore();
    expect(recorder.amounts.length === 0, `hit for ${recorder.amounts.map((a) => fmt(a)).join(',')}`);
    h.removeEnemy({ combatant: target });
  });
  await check(g, 'cooldowns stop attacks being spammed', () => {
    h.simulate(3, { throttle: 0.3 });
    h.player.biteCooldown = 0;
    expect(h.combat.tryAttack(h.player, 'bite') === true, 'first bite refused');
    expect(h.combat.tryAttack(h.player, 'bite') === false, 'second bite accepted straight away');
    expect(h.combat.tryAttack(h.player, 'breath') === false, 'attacks overlapped');
    h.simulate(3, { throttle: 0.3 });
    expect(h.combat.tryAttack(h.player, 'bite') === true, 'bite not available after cooldown');
    h.simulate(2, { throttle: 0.3 });
  });
  await check(g, 'fire breath burns a target in the cone', () => {
    h.simulate(2, { throttle: 0.3 });
    const target = dummy(h, 'elderscale', ahead(h, 48));
    const before = target.health;
    h.player.fireCooldown = 0;
    hold(h, target, 48, 3.4, { throttle: 0.3, action: true });
    const damage = before - target.health;
    expect(damage > 100, `only ${fmt(damage)} damage`);
    expect(target.burn > 0 || target.health < before, 'not burning');
    h.removeEnemy({ combatant: target });
    return `${fmt(damage)} damage`;
  });
  await check(g, 'fire does not reach targets beyond its range', () => {
    h.simulate(8, { throttle: 0.3 });
    const target = dummy(h, 'elderscale', ahead(h, 140));
    const before = target.health;
    h.player.fireCooldown = 0;
    hold(h, target, 140, 3.4, { throttle: 0.3, action: true });
    expect(target.health === before, `took ${fmt(before - target.health)}`);
    h.removeEnemy({ combatant: target });
  });
  await check(g, 'fire does not hit a target behind the dragon', () => {
    h.simulate(8, { throttle: 0.3 });
    const target = dummy(h, 'elderscale', ahead(h, -40, 20));
    const before = target.health;
    h.player.fireCooldown = 0;
    for (let i = 0; i < 200; i++) {
      const place = ahead(h, -40, 20);
      target.controller.body.position.set(place.x, place.y, place.z);
      target.controller.body.velocity.set(0, 0, 0);
      h.simulate(1 / 60, { throttle: 0.3, action: true });
    }
    expect(target.health === before, `took ${fmt(before - target.health)}`);
    h.removeEnemy({ combatant: target });
  });
  await check(g, 'a wing buffet damages and shoves a target away', () => {
    h.simulate(6, { throttle: 0.3 });
    const target = dummy(h, 'brazenhide', ahead(h, 30));
    const before = target.health;
    const startDistance = target.position.distanceTo(h.player.position);
    h.player.buffetCooldown = 0;
    hold(h, target, 30, 1.2, { throttle: 0.3, buffet: true });
    const damage = before - target.health;
    expect(damage > 10, `damage ${fmt(damage)}`);
    // Let it fly free for a moment to show the knockback.
    target.controller.body.velocity.set(0, 0, 0);
    h.simulate(0.8, { throttle: 0.3 });
    const moved = target.position.distanceTo(h.player.position) - startDistance;
    expect(moved > 2, `pushed only ${fmt(moved)} m`);
    h.removeEnemy({ combatant: target });
    return `${fmt(damage)} damage, pushed ${fmt(moved)} m`;
  });
  await check(g, 'killing an enemy fires one event and brings it down', () => {
    h.simulate(4, { throttle: 0.3 });
    const target = dummy(h, 'brazenhide', ahead(h, 17));
    const events: string[] = [];
    const previous = h.combat.onEvent;
    h.combat.onEvent = (m: string, k: string, s: unknown) => {
      events.push(m);
      previous(m, k, s);
    };
    target.health = 40;
    h.player.biteCooldown = 0;
    hold(h, target, 17, 2.2, { throttle: 0.3, bite: true });
    h.combat.onEvent = previous;
    expect(!target.alive && target.controller.dead, 'still alive');
    expect(events.filter((e) => e.includes('slain')).length === 1, `events: ${events.join('|')}`);
    expect(!h.combat.tryAttack(target, 'bite'), 'a dead dragon can attack');
    const start = target.controller.body.position.y;
    h.simulate(8, { throttle: 0.3 });
    const fell = start - target.controller.body.position.y;
    expect(fell > 5 || target.controller.isGrounded(), `fell only ${fmt(fell)} m`);
    h.removeEnemy({ combatant: target });
    return `fell ${fmt(fell)} m`;
  });
  await check(g, 'burning finishes off a weak target', () => {
    h.simulate(8, { throttle: 0.3 });
    const target = dummy(h, 'elderscale', ahead(h, 40));
    target.health = 120;
    h.player.fireCooldown = 0;
    hold(h, target, 40, 3.6, { throttle: 0.3, action: true });
    expect(!target.alive, `health ${fmt(target.health)}`);
    h.removeEnemy({ combatant: target });
  });
  await check(g, 'the player can die, sees the death screen, and rises again', () => {
    reset(h);
    takeOff(h, 70);
    const enemy = dummy(h, 'brazenhide', ahead(h, -30));
    enemy.controller.setHeading(h.player.controller.heading);
    h.player.health = 5;
    h.combat.tryAttack(enemy, 'bite');
    for (let i = 0; i < 120; i++) {
      // Keep the player right in front of the enemy's jaws.
      const heading = enemy.controller.heading;
      const e = enemy.controller.body.position;
      h.player.controller.body.position.set(e.x + Math.sin(heading) * 17, e.y, e.z + Math.cos(heading) * 17);
      h.player.controller.body.velocity.set(0, 0, 0);
      enemy.controller.body.velocity.set(0, 0, 0);
      h.simulate(1 / 60, {});
    }
    expect(!h.player.alive, `player health ${fmt(h.player.health)}`);
    const overlay = [...document.querySelectorAll('div')].find((d) => d.textContent === 'YOU HAVE FALLEN')?.parentElement as HTMLElement | undefined;
    expect(!!overlay && overlay.style.display !== 'none', 'death screen not shown');
    h.removeEnemy({ combatant: enemy });
    h.respawnPlayer();
    h.simulate(0.3);
    expect(h.player.alive && h.player.health === h.player.maxHealth, 'not revived to full health');
    expect(overlay!.style.display === 'none', 'death screen still shown');
    return 'died, overlay shown, revived';
  });
}

async function switchingTests(h: H): Promise<void> {
  const g = 'switching';
  reset(h);
  await check(g, 'every dragon can be flown, with its own stats', () => {
    const seen: string[] = [];
    for (const id of ['brazenhide', 'elderscale', 'cinderwing']) {
      h.switchDragon(id);
      h.simulate(0.3);
      expect(h.player.profile.id === id, `profile ${h.player.profile.id}`);
      expect(h.player.health === h.player.maxHealth && h.player.alive, 'not at full health');
      expect(h.player.controller.mesh === h.player.model.group, 'controller and model out of step');
      expect(h.player.controller.mesh.parent === h.scene, 'dragon not in the scene');
      seen.push(`${h.player.name} ${h.player.maxHealth} hp`);
    }
    return seen.join(', ');
  });
  await check(g, 'the player is never matched against its own dragon', () => {
    h.switchDragon('brazenhide');
    expect(h.enemies.every((e: H) => e.combatant.profile.id !== 'brazenhide'), 'an enemy shares the player\'s dragon');
    expect(h.enemies.length === h.enemyCount, `${h.enemies.length} enemies, expected ${h.enemyCount}`);
    h.switchDragon('cinderwing');
  });
  await check(g, 'repeated switching does not leak GPU resources', () => {
    h.simulate(0.5);
    const base = { textures: h.renderer.info.memory.textures, geometries: h.renderer.info.memory.geometries };
    const ids = ['brazenhide', 'elderscale', 'cinderwing'];
    for (let i = 0; i < 30; i++) {
      h.switchDragon(ids[i % 3]);
      h.simulate(0.05);
    }
    const grown = { textures: h.renderer.info.memory.textures - base.textures, geometries: h.renderer.info.memory.geometries - base.geometries };
    expect(grown.textures <= 24 && grown.geometries <= 24, `after 30 switches: +${grown.textures} textures, +${grown.geometries} geometries`);
    return `+${grown.textures} textures, +${grown.geometries} geometries`;
  });
}

async function aiTests(h: H): Promise<void> {
  const g = 'ai';
  reset(h);
  h.player.controller.body.position.set(385, 60, -70);

  await check(g, 'idle dragons keep circling their lair while the player is far away', () => {
    const enemy = h.spawnEnemy('brazenhide');
    const lair = { x: 1190, z: -645 };
    let worst = 0;
    for (let i = 0; i < 60; i++) {
      h.simulate(1, {});
      const p = enemy.combatant.position;
      worst = Math.max(worst, Math.hypot(p.x - lair.x, p.z - lair.z));
    }
    expect(!enemy.ai.aggro, 'woke up with the player 1000 m away');
    expect(worst < 480, `wandered ${fmt(worst)} m from its lair`);
    h.removeEnemy(enemy);
    return `max ${fmt(worst)} m from the lair, never aggro`;
  });
  await check(g, 'a dragon wakes when the player comes close', () => {
    const enemy = h.spawnEnemy('brazenhide');
    h.simulate(2, {});
    h.player.controller.body.position.set(1000, 140, -450);
    h.simulate(3, { throttle: 0.2 });
    expect(enemy.ai.aggro, 'still asleep at 300 m');
    h.removeEnemy(enemy);
  });
  await check(g, 'an awake dragon closes in and attacks a fighting player', () => {
    reset(h);
    const enemy = h.spawnEnemy('brazenhide');
    enemy.ai.aggro = true;
    const attacks = { bite: 0, breath: 0, buffet: 0 } as Record<string, number>;
    const original = enemy.combatant.model.triggerAttack.bind(enemy.combatant.model);
    enemy.combatant.model.triggerAttack = (kind: string) => {
      const ok = original(kind);
      if (ok) attacks[kind]++;
      return ok;
    };
    h.player.controller.body.position.set(1000, 150, -450);
    let closest = Infinity;
    for (let i = 0; i < 90; i++) {
      const altitude = h.player.controller.getAltitude();
      h.simulate(1, { throttle: 0, steer: 0.4, handbrake: altitude < 80, descend: altitude > 150 });
      closest = Math.min(closest, enemy.combatant.position.distanceTo(h.player.position));
    }
    const total = attacks.bite + attacks.breath + attacks.buffet;
    expect(total >= 1, `no attacks in 90 s (closest ${fmt(closest)} m)`);
    h.removeEnemy(enemy);
    return `closest ${fmt(closest)} m; bites ${attacks.bite}, fire ${attacks.breath}, buffets ${attacks.buffet}`;
  });
  await check(g, 'an enemy lands to fight a player on the ground', () => {
    reset(h);
    const enemy = h.spawnEnemy('brazenhide');
    enemy.ai.aggro = true;
    h.player.controller.body.position.set(1020, h.world.getStandingHeight(1020, -400) + h.player.controller.tuning.groundOffset, -400);
    let landed = false;
    for (let i = 0; i < 60 && !landed; i++) {
      h.simulate(1, {});
      landed = enemy.combatant.controller.getAltitude() < 6;
    }
    expect(landed, `still at ${fmt(enemy.combatant.controller.getAltitude())} m after 60 s`);
    h.removeEnemy(enemy);
  });
}

async function stabilityTests(h: H): Promise<void> {
  const g = 'stability';
  reset(h);
  await check(g, 'three minutes of random play with enemies stays finite and sane', () => {
    const e1 = h.spawnEnemy('brazenhide');
    const e2 = h.spawnEnemy('elderscale');
    e1.ai.aggro = e2.ai.aggro = true;
    let seed = 1234567;
    const rand = () => (seed = (seed * 1664525 + 1013904223) % 4294967296) / 4294967296;
    let input: Input = {};
    for (let step = 0; step < 180 * 60; step++) {
      if (step % 90 === 0) {
        input = { throttle: rand() < 0.7 ? 1 : 0, steer: rand() * 2 - 1, handbrake: rand() < 0.5, descend: rand() < 0.2, bite: rand() < 0.2, action: rand() < 0.15, buffet: rand() < 0.1 };
      }
      h.simulate(1 / 60, input);
      if (step % 600 === 0 && !h.player.alive) h.respawnPlayer();
      if (step % 300 === 0) {
        for (const c of h.combat.combatants) {
          const p = c.controller.body.position;
          expect(Number.isFinite(p.x + p.y + p.z), `${c.name} position became ${p.x},${p.y},${p.z}`);
          expect(p.y > -60 && p.y < 900, `${c.name} left the world vertically (${fmt(p.y)})`);
          expect(Number.isFinite(c.health), `${c.name} health became ${c.health}`);
        }
      }
    }
    expect(h.combat.particles.liveCount < 10000, 'particles exceeded capacity');
    for (const e of [...h.enemies]) h.removeEnemy(e);
    return `${h.combat.combatants.length} combatants alive at the end`;
  });
  await check(g, 'five enemies at once run without errors', () => {
    reset(h);
    for (let i = 0; i < 5; i++) {
      const e = h.spawnEnemy();
      e.ai.aggro = true;
    }
    h.simulate(20, { throttle: 0.4, steer: 0.2 });
    for (const c of h.combat.combatants) {
      const p = c.controller.body.position;
      expect(Number.isFinite(p.x + p.y + p.z), `${c.name} not finite`);
    }
    for (const e of [...h.enemies]) h.removeEnemy(e);
  });
  await check(g, 'simulation cost per step stays small', () => {
    reset(h);
    for (let i = 0; i < 2; i++) h.spawnEnemy().ai.aggro = true;
    h.simulate(2);
    const start = performance.now();
    h.simulate(10, { throttle: 0.5 });
    const perStep = (performance.now() - start) / 600;
    expect(perStep < 8, `${fmt(perStep, 2)} ms per step`);
    for (const e of [...h.enemies]) h.removeEnemy(e);
    return `${fmt(perStep, 2)} ms per 1/60 s step (update + physics, one render per call)`;
  });
}

/** Last, because it waits on real-time timers: a slain enemy is removed and replaced. */
async function respawnTests(h: H): Promise<void> {
  const g = 'respawn';
  reset(h);
  await check(g, 'a slain enemy is cleared away and replaced at its lair', async () => {
    const enemy = h.spawnEnemy('brazenhide');
    enemy.ai.aggro = false;
    enemy.combatant.health = 0.5;
    h.player.controller.body.position.set(enemy.combatant.position.x - 12, enemy.combatant.position.y, enemy.combatant.position.z - 12);
    // Kill it by burning: simplest deterministic route.
    enemy.combatant.burn = 5;
    h.simulate(3, {});
    expect(!enemy.combatant.alive, 'did not die');
    h.simulate(11, {});
    expect(!h.enemies.includes(enemy), 'corpse not removed after 10 s');
    const before = h.enemies.length;
    await new Promise((resolve) => setTimeout(resolve, 4600));
    expect(h.enemies.length === before + 1, `replacement missing (${before} -> ${h.enemies.length})`);
    return 'removed after 10 s, replacement after 4 s';
  });
}

const GROUPS: Record<string, (h: H) => Promise<void>> = {
  world: worldTests,
  flight: flightTests,
  combat: combatTests,
  switching: switchingTests,
  ai: aiTests,
  stability: stabilityTests,
  respawn: respawnTests,
};

export async function runSelfTest(h: H, groups: string[] = Object.keys(GROUPS)): Promise<TestResult[]> {
  results.length = 0;
  h.debug.paused = true;
  const started = performance.now();
  for (const name of groups) {
    const run = GROUPS[name];
    if (!run) throw new Error(`Unknown test group "${name}"`);
    await run(h);
  }
  const passed = results.filter((r) => r.pass).length;
  const summary = `${passed}/${results.length} passed in ${fmt((performance.now() - started) / 1000)} s`;
  // eslint-disable-next-line no-console
  console.log(`SELFTEST ${summary}`);
  // eslint-disable-next-line no-console
  console.table(results.map((r) => ({ group: r.group, test: r.name, result: r.pass ? 'PASS' : 'FAIL', detail: r.detail })));
  (window as unknown as Record<string, unknown>).__selftestResults = results.map((r) => ({ ...r }));
  return results.map((r) => ({ ...r }));
}
