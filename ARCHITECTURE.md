# Architecture

Stack: TypeScript, Vite, Three.js (rendering), cannon-es (physics). Models are glTF files built by Blender
scripts. No backend and no external services.

```
tools/blender/             headless Blender scripts that generate every 3D asset
  assetlib.py              shared mesh builders, procedural textures, architecture kit
  dragonlib.py             dragon sweeps, scale/membrane textures, armature helpers
  build_dragons.py         Caraxes, Vermithor, Vhagar: skinned meshes + bone rig + rig JSON
  build_landmarks.py       Red Keep, Dragonpit, Dragonstone, Harrenhal, High Tide, Great Sept
  build_structures.py      houses, barn, tower, fence
  build_environment.py     trees, rocks, grass
public/assets/             generated GLB files (+ dragons/*.json rig data)

src/
  main.ts                  bootstrap, game loop, spawning, input to combat, debug handle (window.__horizon)
  core/                    InputManager, Noise, PostProcessing (bloom + ACES), PlayerActor
  physics/                 PhysicsWorld (cannon), ObstacleField (streamed tree colliders)
  world/
    WorldMap.ts            the realm's shape: coast, islands, hills, castle pads, settlements, roads, lairs
    Terrain.ts             heightfield (render + physics) from WorldMap, flattened under roads
    World.ts               assembles sky, terrain, sea, roads, landmarks, towns, forests, grass
    Settlements.ts         houses on jittered street grids
    Vegetation.ts          instanced forests with near/far detail swapping
    Grass.ts, Water.ts, SkyManager.ts, Track.ts, AssetLibrary.ts, ProceduralTextures.ts
  dragons/
    DragonRig.ts           drives skinned bones by model-space deltas from rest
    DragonModel.ts         animation: flight, walk, wing fold, neck, tail, head stabilisation, attacks
    DragonCatalog.ts       profiles (stats), loader, tuning per dragon
  mounts/                  FlyingMountController (ground + flight physics), MountModel interfaces
  combat/
    CombatManager.ts       attacks, damage, flame cones, burning, knockback, death, separation
    Combatant.ts           one dragon in a fight
    DragonAI.ts            lair patrol, pursuit, attack choice
    Particles.ts           fireball, smoke, dust and ember effects (GPU billboards)
  camera/CameraRig.ts      switchable views, speed FOV, shake, look-back, creature scale
  ui/                      HUD (speed, help) and CombatHud (health, cooldowns, name plates, kill feed)
```

## Frame flow
`update(dt, input)`: stream tree colliders, player attacks and aim, controller input for the player and
each AI, `physics.step`, controllers sync visuals and run the dragon animation, `combat.update`
(flames, damage, dust, deaths), camera, world streaming, HUD. Then `postFx.render()`.

`simulate(seconds, input)` runs the same update at fixed 1/60 s without `requestAnimationFrame`;
all automated checks use it.

## The rig convention
Animation never touches bone axes directly. `DragonRig.rotate(bone, q)` applies a rotation in model space
relative to the rest pose (`local = conj(Wparent) * q * Wparent * Lrest`), so the same code works whatever
orientation the exporter gave each bone. Necks and tails are chains of 9 to 14 bones; each segment follows
the one in front with a delay, and the head is re-aimed in world space so it stays near level in a bank.

## Notes for contributors
- Do not use `CANNON.Plane`; wheel/ground raycasts miss it. Use Heightfield or Box.
- Do not add thousands of static bodies; use ObstacleField.
- A positive controller throttle accelerates to top speed; to slow down use brake or release it.
- Bone names are the contract between Blender and the game (`neck_0..`, `tail_0..`, `wing_L0..5`, `leg_L0..2`, `head`, `jaw`).
