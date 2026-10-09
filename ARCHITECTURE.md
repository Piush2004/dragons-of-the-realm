# Architecture

Stack: TypeScript, Vite, Three.js (rendering), cannon-es (physics). No backend, no external assets.

```
src/
  main.ts                    bootstrap, game loop, debug/test handle (window.__horizon)
  core/
    InputManager.ts          keyboard state -> DriveInput
    Noise.ts                 seeded value noise / fbm / PRNG
    PostProcessing.ts        EffectComposer: render -> bloom -> ACES tonemap output
  physics/
    PhysicsWorld.ts          cannon world, fixed 60 Hz step, contact materials
    ObstacleField.ts         streams static prop colliders in a 3x3 cell window around cars
  vehicle/
    VehicleConfig.ts         tunable per-car data (physics + body options) and wheel layout
    VehicleController.ts     RaycastVehicle wrapper: drivetrain, brakes, reverse, visual sync, impacts
    CarModelBuilder.ts       procedural car body and wheels from a VehicleConfig
  world/
    World.ts                 composes sky, track, terrain, vegetation, obstacles
    Terrain.ts               heightfield (render mesh + cannon Heightfield), flattened under roads
    Track.ts                 spline road ribbon, centreline samples, checkpoints
    SkyManager.ts            sky dome, sun, fog, environment map, time-of-day presets
    Vegetation.ts            instanced trees + trunk collider list
    ProceduralTextures.ts    canvas-generated asphalt / grass textures
  camera/CameraRig.ts        camera modes, look-back, speed FOV, impact shake
  ui/HUD.ts                  speed / gear / RPM, help line, toast
```

## Frame flow
`tick` calls `update(dt, input)`: obstacles.update, vehicle.applyInput, physics.step,
vehicle.syncVisuals (interpolated), camera, sky shadow follow, HUD. Then `postFx.render()`.

`simulate(seconds, input)` runs the same `update` at a fixed 1/60 s without requestAnimationFrame.
Automated checks use it so results do not depend on tab visibility or frame rate.

## Notes for contributors
- Do not use `CANNON.Plane`: wheel raycasts against it miss in most places. Use Heightfield or Box.
- Do not add thousands of static bodies directly (4,900 bodies cost about 113 ms per step). Use ObstacleField.
- `maxEngineForce` is per axle. `maxBrakeForce` is a per-step impulse, so it depends on the 60 Hz step.
- Car-local axes: +Z forward, +Y up.
