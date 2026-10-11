# P1 Design: Voxel world in Unity

Status: draft for review, 2026-10-11. Nothing in this document has been built yet.

## 1. Why this exists

The user asked for the game to become "like a Minecraft open world game", with survival from the start, with
the three dragons living inside the block world, and with **Unity** as the engine. The current game is a
TypeScript / Three.js dragon-combat game (see ARCHITECTURE.md). It stays untouched and runnable until the
dragons have been ported (P3).

### Decisions taken with the user

| Topic | Decision |
| --- | --- |
| Engine | Unity 6000.3.26f1 (Unity 6.3 LTS), Universal Render Pipeline, C# |
| Dragons | Live inside the block world (ride and fight them); the hand-built realm is replaced by voxel terrain |
| Mechanics depth | Survival from the start overall, delivered in stages |
| World | 384 blocks tall (y -64 to 319), 12 chunks of view distance by default |
| Look | Blocky 16x16 pixel textures with modern lighting: real-time shadows, bloom, tone-mapping, SMAA, nicer sky and water |
| Input | Mouse and keyboard, first person with F5 third-person toggle, plus gamepad support |
| P1 scope | Lean: endless terrain, walking, breaking and placing blocks. Caves, saving and day/night move to P2 |

### Programme order

| # | Sub-project | Delivers |
| --- | --- | --- |
| P1 | Voxel world engine (this document) | Endless block terrain, first-person movement, break and place |
| P2 | World and survival core | Caves, ores, saving, day/night, flood-fill light and torches, inventory, drops, tool mining, crafting, health and hunger, animals and night monsters |
| P3 | Dragons in the world | FBX export of the Blender dragons, C# port of rig animation / AI / combat, lair bosses, taming, saddle and riding, fire that burns blocks |
| P4 | Richness and polish | Villages, ruins, dragon-lair structures, sound, settings menu, docs |

Each sub-project gets its own spec, plan, build and test cycle and leaves the game playable.

## 2. Environment (verified 2026-10-11)

- Unity Hub 3.22.2 installed through winget (`Unity.UnityHub`, official Unity Technologies package).
- Unity Editor 6000.3.26f1 installed by the Hub with Windows standalone support only, 7.9 GB on disk.
- **The licence is not activated.** Activating Unity Personal needs a Unity ID sign-in in the Hub, which only
  the user can do. Nothing below can be compiled or run until that is done.
- Hardware: Ryzen 7 with an AMD Radeon 780M iGPU and an NVIDIA RTX 5060 laptop GPU. Windows normally runs
  Unity on the iGPU unless "High performance" is set for it in Windows Graphics Settings. Every performance
  figure must state which GPU `SystemInfo.graphicsDeviceName` reported.

## 3. Goals and non-goals of P1

Goals:
1. An endless, seed-deterministic block world with distinct biomes, hills and mountains, a sea, beaches, and
   trees, streamed around the player without hitches.
2. First-person walking, sprinting, sneaking, jumping, swimming and a creative-style fly toggle, on mouse and
   keyboard and on a gamepad.
3. Break and place blocks anywhere, with correct face targeting, and see the result immediately, including
   across chunk borders.
4. The agreed look: pixel textures with modern lighting.
5. Verifiable: headless automated tests, screenshots and measured frame times.

Non-goals (all later): caves, ore, saving and loading, day/night cycle, flowing water, inventory, item drops,
crafting, health, mobs, dragons, sound, multiplayer, menus beyond a minimal pause overlay.

## 4. Architecture

Project folder: `unity/BlockRealm/` inside this repository (Library, Temp, Logs, UserSettings and Builds are
git-ignored). Code lives in assembly definitions so tests can reference it: `BlockRealm.Voxel`,
`BlockRealm.Player`, `BlockRealm.Rendering`, plus `BlockRealm.Tests.EditMode` and `BlockRealm.Tests.PlayMode`.

### 4.1 Data

- **Block ids** are one `byte` each (256 types; P1 uses about 40).
- **Column**: 16 x 16 footprint, 384 tall, stored as 24 **sections** of 16 x 16 x 16 blocks. A section that
  is entirely air is not allocated, so open sky costs nothing. A column also keeps a 16 x 16 heightmap of the
  highest opaque block, used for sky exposure.
- Block data lives in `NativeArray<byte>` so Burst jobs can read it. Memory budget at view distance 12:
  25 x 25 columns, mostly empty above the terrain surface, well under 100 MB.
- **Block registry** (`BlockTypes`): per id, a name, hardness (unused until P2), solid / transparent /
  cross-quad flags, the texture-array layer for top, side and bottom, and a tint rule (grass and leaves are
  tinted per biome).

### 4.2 Generation (Burst, `WorldGenJob`)

Deterministic from a 32-bit seed using `Unity.Mathematics.noise`:
- continentalness and erosion noise give ocean, coast, plains, hills and mountains;
- temperature and humidity noise choose the biome: plains, forest, desert, snowy tundra, mountains, ocean;
- the surface block depends on biome and slope (grass, sand, snow on high peaks, bare stone on steep faces),
  with three layers of dirt or sand above stone and bedrock at the bottom;
- sea level at y = 63; water fills below it;
- trees (oak-like, pine-like) and tall grass / flowers are placed from a per-column hash so that trees cross
  chunk borders without seams (a tree is generated by every column that could contain part of it).

### 4.3 Meshing (Burst, `MeshJob`)

One job per dirty 16 x 16 x 16 section, reading the section plus a one-block border from its six neighbours:
- emit a quad only for a face whose neighbour is air, transparent or a different transparent type;
- per-vertex **ambient occlusion** from the three neighbouring blocks at each corner, with the standard
  flipped-diagonal rule to avoid anisotropy artefacts;
- per-vertex **sky exposure** from the column heightmap (darker under overhangs and inside dug tunnels);
  full flood-fill light is a P2 feature;
- separate submeshes for opaque, cutout (leaves, grass) and water, so water can be drawn with its own shader;
- results written with `Mesh.MeshDataArray` and applied with `Mesh.ApplyAndDisposeWritableMeshData`.

### 4.4 World streaming (`World`)

- Keeps columns within the view distance of the player, prioritised nearest first.
- Pipeline per column: generate, then mesh sections when all four neighbour columns have generated blocks.
- A per-frame time budget limits how many jobs complete and how many meshes are applied, so streaming never
  costs more than a set share of the frame.
- Unloads columns beyond view distance + 2. (Edits are not persisted in P1, so an unloaded edited column is
  regenerated fresh. This is documented behaviour, fixed by saving in P2.)

### 4.5 Rendering

- URP with real-time shadows from one directional light (two cascades, limited shadow distance; low, medium
  and high presets), bloom, ACES tone-mapping and SMAA. In P1 the sun is fixed at a golden-hour angle with a
  debug key to scrub it for testing; the real cycle is P2.
- Hand-written HLSL shader for the block materials: samples a `Texture2DArray`, multiplies by vertex colour
  (AO, sky exposure, biome tint), applies fog. Water uses a second shader (tinted, slightly transparent, simple
  animated normals and a shoreline fade).
- Textures: sixteen-by-sixteen pixel-art tiles **drawn by code** at startup into a `Texture2DArray` with
  point sampling and generated mip-maps, so there are no external art files and no atlas bleeding.
- Sky: URP procedural skybox with a sun disc, plus distance fog tuned to the view distance.

### 4.6 Player and interaction

- **Collision** is a custom swept axis-aligned box (0.6 x 1.8 x 0.6) against the block grid, resolved one axis
  at a time. Unity mesh colliders and `CharacterController` are not used because chunk mesh colliders would
  dominate the frame cost.
- **Movement**: walk, sprint, sneak (does not step off edges), jump, swim in water, and a creative-style fly
  toggle (double-tap jump). Movement uses a fixed step so behaviour is frame-rate independent.
- **Targeting**: voxel DDA raycast up to 6 blocks, returning the block and the face hit; an outline mesh
  highlights the target.
- **Edit**: hold primary to break (repeating at a short interval) and secondary to place, never inside the
  player's own box. A 9-slot hotbar holds placeable block types with unlimited supply in P1.
- **Edit propagation**: a changed block marks its section dirty and, when it lies on a section border, the
  neighbouring section; remeshing is prioritised over streaming work.
- **Input** uses the Unity Input System with one action asset:

| Action | Mouse and keyboard | Gamepad |
| --- | --- | --- |
| Move | WASD | Left stick |
| Look | Mouse | Right stick |
| Jump / fly up | Space (double-tap toggles fly) | A |
| Sneak / fly down | Left Ctrl | B |
| Sprint | Left Shift | Left stick click |
| Break | Left mouse (hold) | Right trigger |
| Place | Right mouse | Left trigger |
| Hotbar | 1-9, mouse wheel | Bumpers |
| Camera | F5 | Y |
| Pause | Esc | Start |

## 5. Testing and verification

Automated, run headless with `Unity.exe -batchmode -runTests` (EditMode and PlayMode):
- **Worldgen determinism**: the same seed and coordinates give byte-identical columns; different seeds differ.
- **Terrain sanity**: heights stay within bounds, sea level water exists, bedrock is complete, no floating
  surface holes at column borders.
- **Mesher**: known tiny layouts give the exact expected face and vertex counts; a block next to a neighbouring
  section border produces no seam face; AO values for known corner cases.
- **Raycast**: hits the expected block and face from known positions and directions, including across
  section borders.
- **Collision**: the player stops at walls, slides along them, lands on floors, cannot tunnel at terminal
  falling speed, and does not step off an edge while sneaking.
- **Edit and remesh**: breaking a border block updates both sections; placing never overlaps the player.
- **Input mapping**: gamepad and keyboard actions driven through the Input System test fixture produce the same
  movement and edit results.

Run-time evidence (reported honestly with what was and was not measured):
- Screenshots from a real graphics run (not `-nographics`) of each biome, the sea, trees, shadows and the
  highlight outline.
- Frame-time statistics (average and 95th percentile) from a Windows standalone player at the default view
  distance, with the GPU name stated.
- A short play session driven by scripted input to check streaming while moving.

## 6. Acceptance criteria for P1

1. Opening the player puts you on dry land in a recognisable block world with the agreed look.
2. All automated tests pass headless; their results are recorded in a test report.
3. You can walk, sprint, jump, swim and fly with keyboard and mouse and with a gamepad.
4. You can break and place blocks, including across chunk borders, with no visual seams.
5. Moving a long distance in any direction keeps generating terrain with no holes and no long hitches.
6. Measured frame time at the default settings is reported with the GPU used. The target is 60 fps on
   the machine's GPU; if it is missed, the report says by how much and what the cheapest remedies are.

## 7. Risks

| Risk | Mitigation |
| --- | --- |
| Unity licence not activated; Editor cannot run | Needs the user's sign-in; work that does not need the Editor (this spec, asset scripts) proceeds meanwhile |
| 384-tall world and 12 chunks of view are heavy for an iGPU | Sparse sections, Burst jobs, a per-frame budget, shadow presets, adjustable view distance |
| Shadow cost at long range on the iGPU | Limited shadow distance, two cascades, quality presets |
| Cross-border seams (trees, AO, faces) | Border-aware mesher and tree placement, tested explicitly |
| Writing a custom collision system | Small, well-isolated, covered by the collision tests above |

## 8. Out of scope here, but designed for

- P2 adds `WorldSave`, flood-fill light as a second vertex channel, `DayNightCycle`, caves in `WorldGenJob`,
  and the inventory / crafting / health systems on top of the existing block and edit APIs.
- P3 reuses the existing Blender scripts (`tools/blender/`) to export the dragons to FBX and ports
  `DragonRig` / `DragonModel` / `DragonAI` / `CombatManager` to C#; creature movement uses the same swept-box
  voxel collision as the player.
