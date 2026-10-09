import * as THREE from 'three';
import * as CANNON from 'cannon-es';
import { PhysicsWorld } from './physics/PhysicsWorld';
import { World } from './world/World';
import { InputManager, type DriveInput } from './core/InputManager';
import { CameraRig, DRAGON_CAMERA_MODES } from './camera/CameraRig';
import { LAIRS, PLAYER_START } from './world/WorldMap';
import { FlyingMountController } from './mounts/FlyingMountController';
import { DRAGON_IDS, DragonLibrary, type DragonId, type DragonInstance } from './dragons/DragonCatalog';
import { Combatant } from './combat/Combatant';
import { CombatManager } from './combat/CombatManager';
import { DragonAI } from './combat/DragonAI';
import { HUD } from './ui/HUD';
import { CombatHud } from './ui/CombatHud';
import { PostProcessing } from './core/PostProcessing';

const container = document.getElementById('app');
if (!container) throw new Error('Missing #app container');

const renderer = new THREE.WebGLRenderer({ antialias: true, powerPreference: 'high-performance' });
renderer.shadowMap.enabled = true;
renderer.shadowMap.type = THREE.PCFSoftShadowMap;
renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
renderer.setSize(window.innerWidth, window.innerHeight);
container.appendChild(renderer.domElement);

const scene = new THREE.Scene();
const physics = new PhysicsWorld();
const loading = document.createElement('div');
loading.textContent = 'Loading world…';
loading.style.cssText =
  'position:absolute;inset:0;display:flex;align-items:center;justify-content:center;color:#fff;font:600 22px Segoe UI,sans-serif;background:#0b1118;z-index:5;';
container.appendChild(loading);
// Let the loading text paint before the heavy synchronous world build.
await new Promise((resolve) => setTimeout(resolve, 30));

const anisotropy = renderer.capabilities.getMaxAnisotropy();
const [world, dragons] = await Promise.all([World.create(scene, physics, renderer), DragonLibrary.load(anisotropy)]);
loading.remove();

const heightAt = (x: number, z: number) => world.getStandingHeight(x, z);
const startTransform = { position: { x: PLAYER_START.x, z: PLAYER_START.z }, yaw: PLAYER_START.yaw };

const params = new URLSearchParams(window.location.search);
const requested = params.get('dragon') as DragonId | null;
const playerDragon: DragonId = requested && DRAGON_IDS.includes(requested) ? requested : 'cinderwing';
const enemyCount = Math.max(0, Math.min(5, Number(params.get('enemies') ?? 2)));

const combat = new CombatManager();
scene.add(combat.particles.group);

function placeOnGround(x: number, z: number, groundOffset: number, extra = 0): CANNON.Vec3 {
  return new CANNON.Vec3(x, heightAt(x, z) + groundOffset + extra, z);
}

// ---- player
const playerInstance = dragons.create(playerDragon);
const playerPos = placeOnGround(startTransform.position.x, startTransform.position.z, playerInstance.tuning.groundOffset);
const playerController = new FlyingMountController(physics, scene, playerInstance.model, playerInstance.tuning, heightAt, playerPos);
playerController.setHeading(startTransform.yaw);
const player = new Combatant('player', playerController, playerInstance);
combat.add(player);

// ---- enemies
interface Enemy {
  combatant: Combatant;
  ai: DragonAI;
}
const enemies: Enemy[] = [];
let kills = 0;
let enemyCursor = 0;

function spawnEnemy(forcedId?: DragonId, at?: { x: number; y: number; z: number }): Enemy {
  const choices = DRAGON_IDS.filter((id) => id !== player.profile.id);
  const id = forcedId ?? choices[enemyCursor++ % choices.length];
  const instance = dragons.create(id);
  const lair = LAIRS[id];
  const angle = Math.random() * Math.PI * 2;
  const x = lair.x + Math.cos(angle) * lair.patrol * 0.6;
  const z = lair.z + Math.sin(angle) * lair.patrol * 0.6;
  const position = at ? new CANNON.Vec3(at.x, at.y, at.z) : placeOnGround(x, z, instance.tuning.groundOffset, lair.altitude);
  const controller = new FlyingMountController(physics, scene, instance.model, instance.tuning, heightAt, position);
  controller.setHeading(angle + Math.PI / 2);
  const combatant = new Combatant('enemy', controller, instance);
  combat.add(combatant);
  const enemy: Enemy = { combatant, ai: new DragonAI(combatant, combat, lair) };
  enemies.push(enemy);
  return enemy;
}
for (let i = 0; i < enemyCount; i++) spawnEnemy();

// ---- camera, HUD, post-processing
const input = new InputManager();
const cameraRig = new CameraRig(window.innerWidth / window.innerHeight, DRAGON_CAMERA_MODES);
cameraRig.setScale(playerInstance.scale);
const hud = new HUD(container);
hud.setHelp(playerController.helpText);
const combatHud = new CombatHud(container);
const aa = (params.get('aa') ?? 'smaa') as 'smaa' | 'msaa' | 'none';
const postFx = new PostProcessing(renderer, scene, cameraRig.camera, { antialias: aa, samples: Number(params.get('msaa') ?? 4) });

combat.onEvent = (message, kind) => {
  combatHud.showMessage(message, kind === 'kill' ? 'kill' : kind === 'death' ? 'death' : 'info');
  if (kind === 'kill') kills++;
};
combat.onDamage = (victim, amount, attacker) => {
  if (victim === player) {
    cameraRig.addShake(Math.min(14, amount / 14));
    combatHud.hurt(amount);
  } else if (attacker === player) {
    cameraRig.addShake(Math.min(5, amount / 40));
  }
};

new ResizeObserver(() => {
  const width = container.clientWidth;
  const height = container.clientHeight;
  if (width === 0 || height === 0) return;
  renderer.setSize(width, height);
  cameraRig.setAspect(width / height);
  postFx.setSize(width, height);
}).observe(container);

function respawnPlayer(instance?: DragonInstance): void {
  if (instance) combat.setDragon(player, instance);
  else player.revive();
  const groundOffset = player.controller.tuning.groundOffset;
  playerController.resetTo(placeOnGround(startTransform.position.x, startTransform.position.z, groundOffset), startTransform.yaw);
  player.model.setDead(false);
  cameraRig.snap();
}

function switchDragon(id: DragonId): void {
  if (id === player.profile.id) return;
  const instance = dragons.create(id);
  respawnPlayer(instance);
  cameraRig.setScale(instance.scale);
  combatHud.showMessage(`${instance.profile.name} — ${instance.profile.title}`);
  // Keep the fight fair: replace any enemy that is now the same dragon as the player.
  for (const enemy of [...enemies]) {
    if (enemy.combatant.profile.id === id) removeEnemy(enemy);
  }
  while (enemies.length < enemyCount) spawnEnemy();
}

function removeEnemy(enemy: Enemy): void {
  combat.remove(enemy.combatant);
  const index = enemies.indexOf(enemy);
  if (index >= 0) enemies.splice(index, 1);
}

window.addEventListener('keydown', (e) => {
  if (e.repeat) return;
  if (e.code === 'KeyR') {
    if (!player.alive) respawnPlayer();
    else {
      playerController.resetTo(placeOnGround(startTransform.position.x, startTransform.position.z, player.controller.tuning.groundOffset), startTransform.yaw);
      cameraRig.snap();
    }
  }
  if (e.code === 'KeyC') hud.showMessage(`Camera: ${cameraRig.cycleMode().label}`);
  if (e.code === 'KeyT') hud.showMessage(`Time of day: ${world.sky.cyclePreset().id}`);
  const digit = Number(e.code.replace('Digit', ''));
  if (digit >= 1 && digit <= DRAGON_IDS.length) switchDragon(DRAGON_IDS[digit - 1]);
});

const clock = new THREE.Clock();
const debug = { freeCamera: false, paused: false };
const lookTarget = { yaw: 0, pitch: 0 };

function update(delta: number, driveInput: DriveInput): void {
  const allPositions = combat.combatants.map((c) => c.controller.body.position);
  world.obstacles.update(allPositions);

  // Player attacks, aimed at whatever enemy is best lined up.
  if (player.alive) {
    if (driveInput.bite) combat.tryAttack(player, 'bite');
    if (driveInput.action) combat.tryAttack(player, 'breath');
    if (driveInput.buffet) combat.tryAttack(player, 'buffet');
    const target = combat.bestTarget(player, 220, 0.25);
    if (target && player.model.isAttacking()) {
      combat.aimAt(player, target, lookTarget);
      player.model.setLook(lookTarget.yaw, lookTarget.pitch);
    } else {
      player.model.setLook(0, 0);
    }
  }
  player.controller.applyInput(player.alive ? driveInput : { throttle: 0, brake: 0, steer: 0, handbrake: false, descend: false, action: false }, delta);

  // Enemies think, then act.
  for (const enemy of enemies) {
    const control = enemy.ai.update(delta, player.alive ? player : null);
    enemy.combatant.controller.applyInput(control, delta);
  }

  physics.step(delta);
  for (const c of combat.combatants) c.controller.syncVisuals(delta);
  combat.update(delta);

  // Clear away the slain and bring in replacements.
  for (const enemy of [...enemies]) {
    if (!enemy.combatant.alive && enemy.combatant.deadTime > 10) {
      removeEnemy(enemy);
      window.setTimeout(() => {
        if (enemies.length < enemyCount) spawnEnemy();
      }, 4000);
    }
  }

  const impact = player.controller.consumeImpact();
  if (impact > 0) cameraRig.addShake(impact);
  if (!debug.freeCamera) {
    cameraRig.update(player.controller.mesh, player.controller.getSpeedMetersPerSecond(), delta, input.isPressed('KeyB'));
  }
  world.update(cameraRig.camera.position, delta);
  const status = player.controller.getHudStatus();
  hud.update(player.controller.getSpeedKmh(), status.label, status.bar);
  combatHud.update(delta, cameraRig.camera, player, combat.combatants, kills);
}

function tick(): void {
  const delta = Math.min(clock.getDelta(), 1 / 30);
  if (!debug.paused) update(delta, input.getDriveInput());
  postFx.render();
  requestAnimationFrame(tick);
}

/** Deterministic fixed-step simulation for automated tests (independent of requestAnimationFrame). */
function simulate(seconds: number, driveInput: Partial<DriveInput> = {}): void {
  const full: DriveInput = { throttle: 0, brake: 0, steer: 0, handbrake: false, descend: false, action: false, ...driveInput };
  const steps = Math.round(seconds * 60);
  for (let i = 0; i < steps; i++) update(1 / 60, full);
  postFx.render();
}

(window as unknown as Record<string, unknown>).__horizon = {
  world,
  physics,
  renderer,
  simulate,
  debug,
  combat,
  player,
  enemies,
  dragons,
  camera: cameraRig.camera,
  cameraRig,
  scene,
  switchDragon,
  spawnEnemy,
  removeEnemy,
  respawnPlayer: () => respawnPlayer(),
  placeOnGround,
  getKills: () => kills,
  selfTest: (groups?: string[]) => import('./debug/selftest').then(({ runSelfTest }) => runSelfTest((window as unknown as { __horizon: never }).__horizon, groups)),
  enemyCount,
  render: () => postFx.render(),
};

if (params.has('selftest')) {
  // Lazy-loaded so the suite is not part of normal play.
  void import('./debug/selftest').then(({ runSelfTest }) => runSelfTest((window as unknown as { __horizon: never }).__horizon));
}

requestAnimationFrame(tick);
(window as unknown as Record<string, unknown>).__horizonReadyMs = Math.round(performance.now());
