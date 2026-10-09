import * as THREE from 'three';
import * as CANNON from 'cannon-es';
import { PhysicsWorld } from './physics/PhysicsWorld';
import { World } from './world/World';
import { VehicleController } from './vehicle/VehicleController';
import { VEHICLE_CONFIGS } from './vehicle/VehicleConfig';
import { InputManager, type DriveInput } from './core/InputManager';
import { CameraRig, CAR_CAMERA_MODES, PEGASUS_CAMERA_MODES } from './camera/CameraRig';
import { PegasusController } from './pegasus/PegasusController';
import type { PlayerActor } from './core/PlayerActor';
import { HUD } from './ui/HUD';
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
const world = new World(scene, physics, renderer);

const startTransform = world.track.getStartTransform();
const startPos = new CANNON.Vec3(startTransform.position.x, startTransform.position.y + 0.9, startTransform.position.z);
// The pegasus is the default mount; add ?mount=car to the URL to drive the car instead.
const useCar = new URLSearchParams(window.location.search).get('mount') === 'car';
const vehicle: PlayerActor = useCar
  ? new VehicleController(physics, VEHICLE_CONFIGS.verdant_rs, scene, startPos)
  : new PegasusController(physics, scene, (x, z) => world.terrain.getHeightAt(x, z), startPos);
vehicle.resetTo(startPos, startTransform.yaw);

const input = new InputManager();
const cameraRig = new CameraRig(window.innerWidth / window.innerHeight, useCar ? CAR_CAMERA_MODES : PEGASUS_CAMERA_MODES);
const hud = new HUD(container);
hud.setHelp(vehicle.helpText);
const postFx = new PostProcessing(renderer, scene, cameraRig.camera);

// ResizeObserver (not window 'resize') so embedded/emulated viewports that change size without a resize event still work.
new ResizeObserver(() => {
  const width = container.clientWidth;
  const height = container.clientHeight;
  if (width === 0 || height === 0) return;
  renderer.setSize(width, height);
  cameraRig.setAspect(width / height);
  postFx.setSize(width, height);
}).observe(container);

window.addEventListener('keydown', (e) => {
  if (e.code === 'KeyR') {
    vehicle.resetTo(startPos, startTransform.yaw);
    cameraRig.snap();
  }
  if (e.code === 'KeyC' && !e.repeat) {
    hud.showMessage(`Camera: ${cameraRig.cycleMode().label}`);
  }
});

const clock = new THREE.Clock();
const debug = { freeCamera: false, paused: false };

function update(delta: number, driveInput: DriveInput): void {
  world.obstacles.update([vehicle.body.position]);
  vehicle.applyInput(driveInput, delta);
  physics.step(delta);
  vehicle.syncVisuals(delta);

  const impact = vehicle.consumeImpact();
  if (impact > 0) cameraRig.addShake(impact);
  if (!debug.freeCamera) {
    cameraRig.update(vehicle.mesh, vehicle.getSpeedMetersPerSecond(), delta, input.isPressed('KeyB'));
  }
  world.sky.followTarget(vehicle.mesh.position);
  const status = vehicle.getHudStatus();
  hud.update(vehicle.getSpeedKmh(), status.label, status.bar);
}

function tick(): void {
  const delta = Math.min(clock.getDelta(), 1 / 30);
  if (!debug.paused) update(delta, input.getDriveInput());
  postFx.render();
  requestAnimationFrame(tick);
}

/** Deterministic fixed-step simulation for automated tests (independent of requestAnimationFrame). */
function simulate(seconds: number, driveInput: Partial<DriveInput> = {}): void {
  const full: DriveInput = { throttle: 0, brake: 0, steer: 0, handbrake: false, descend: false, ...driveInput };
  const steps = Math.round(seconds * 60);
  for (let i = 0; i < steps; i++) update(1 / 60, full);
  postFx.render();
}

(window as unknown as Record<string, unknown>).__horizon = {
  vehicle,
  world,
  physics,
  renderer,
  simulate,
  debug,
  camera: cameraRig.camera,
  cameraRig,
  render: () => postFx.render(),
};

requestAnimationFrame(tick);
