# BlockRealm

A multiplayer, open-world, survival block game built in **Unity 6** with every texture, icon and model made
by **Blender** scripts. Dig, build, craft, smelt, fight at night, find a dragon's nest on a mountain peak,
tame the dragon and ride it. One world, up to eight friends over a network.

> **Status.** The game logic, networking and world generation are complete and covered by 103 automated tests
> that run without Unity. The Unity presentation layer (rendering, interface, audio) is written and type-checks
> against the real Unity 6.3 assemblies, but **has not been run yet**, because Unity needs a licence activated
> by signing in to Unity Hub, which only the account owner can do. See *What has been verified* below.

## Quick start

1. Install **Unity Hub** and the **Unity 6000.3.26f1** Editor (6.3 LTS) and sign in to activate a free
   Personal licence.
2. Open the folder `unity/BlockRealm` as a project. On first open the project sets itself up (player settings,
   gamepad axes, the start scene). Wait for the import to finish, then press Play.
3. To make a standalone build: menu **BlockRealm → Build Windows player**.

The whole game is created from code at start-up (`GameBootstrap`), so no scene editing is needed.

## Controls

| Action | Keyboard and mouse | Gamepad (Xbox layout) |
| --- | --- | --- |
| Move / look | W A S D, mouse | left stick, right stick |
| Jump | Space (double-tap toggles flight in creative) | A |
| Sneak (no falling off edges) | Left Shift | B |
| Sprint | Left Ctrl, or double-tap W | left stick click |
| Break block / attack | hold left mouse | right trigger |
| Place block / use / interact | right mouse | left trigger |
| Hotbar | 1-9, mouse wheel | bumpers |
| Inventory | E | Y |
| Drop item | Q (Ctrl+Q for the stack) | D-pad down |
| Chat / command | T or Enter / `/` | - |
| Camera (first, behind, front) | F5 | right stick click |
| Debug overlay | F3 | - |
| Pause | Esc | Start |

In inventories a gamepad uses a cursor moved by the left stick: A clicks, X right-clicks, Y shift-clicks.

## Playing

* **Survival.** Health, hunger, drowning, fall damage, lava and fire. Eat cooked meat to stay fed, sleep is
  not needed. Death drops your items where you fell; respawn at the world spawn.
* **Mining.** Break times depend on the block and tool. Stone needs a pickaxe, iron ore a stone pickaxe, gold
  and diamond an iron one, obsidian a diamond one. Tools wear out.
* **Crafting.** 2x2 in the inventory, 3x3 at a crafting table. Furnaces smelt ore, sand, clay, logs and raw meat
  using coal, charcoal or wood as fuel. Chests store items.
* **Creatures.** Pigs, cows, sheep and chickens by day; zombies, skeletons (arrows) and creepers (explosions) in the
  dark and in caves.
* **Dragons.** Three kinds nest on high peaks (Cinderwing, Brazenhide, Elderscale, from weakest to mightiest). They
  breathe fire that burns blocks and bite hard. Wear one down, feed it raw meat to **tame** it, craft a **dragon
  saddle** (leather, a dragon scale and iron), right-click to **ride**: W/S speed, A/D turn, Space climb, Shift dive,
  and Shift twice to dismount. Each nest has a hoard chest. A tamed dragon follows you and fights hostile creatures.
* **Creative mode.** Unlimited items from a palette in the inventory, flying, no damage.

### Commands (operators only)

`/gamemode survival|creative`, `/time set day|noon|night|midnight|<ticks>`, `/tp <x y z | player>`, `/give <item> [n]`,
`/summon <pig|cow|sheep|chicken|zombie|skeleton|creeper|cinderwing|brazenhide|elderscale>`, `/heal`, `/kill`,
`/spawn`, `/seed`, `/help`. In single player and in a world you host, you are the operator.

## Multiplayer

* **Host a world:** Multiplayer → *Host a world on this network*, then pick or create a world. The pause menu shows
  the address friends use (`ip:25565`).
* **Join:** Multiplayer → type `host:port` → *Join server*.
* **Dedicated server:** run the built game headless:

  ```
  BlockRealm.exe -batchmode -nographics -server -world "My World" -seed 2024 -port 25565 -viewdistance 8 \
                 -maxplayers 8 -public play.example.com:25565
  ```

  Remote administration is enabled only with `-public`. A strong admin token is generated and shown once on the
  console (or supply one in the `BLOCKREALM_ADMIN_TOKEN` environment variable or with `-admintokenfile`); the server
  stores only a hash. Server owners type that token on the Multiplayer screen to become operators.
* **Without Unity at all:** the same server and a scripted bot client can be run from the core library for testing:

  ```
  bash unity/BlockRealm/tools/core/core.sh test --serve 25565 MyWorld 2024
  bash unity/BlockRealm/tools/core/core.sh test --bot 127.0.0.1:25565 Alice 30
  ```

### How the network works

The server is authoritative for blocks, inventories, health, mobs and dragons. Clients simulate their own movement
(so the game feels immediate) and the server checks it for plausibility. Terrain is sent as compressed columns and
edits as single-block updates. Single player uses the same server code over an in-process connection.

**Security model, and its limits.** Logins use a challenge-response: the client proves it knows its identity key and
(optionally) the admin token without sending either, and a recording cannot be replayed. Names are reserved to the
first identity that used them. Credentials are scoped to the server's id and to the address the player dialled, so a
different server cannot reuse them. Packets are bounds-checked, input is rate-limited, movement and mining are
validated. **Traffic is not encrypted**, so on the open internet use a VPN or SSH tunnel, and only join servers you
trust: without transport encryption a hostile server could still tamper with the game you see.

## Project layout

```
Assets/Scripts/Core      pure C# game: blocks, world, lighting, generation, meshing, physics, protocol, server, client
Assets/Scripts/Game      Unity layer: renderer, camera, creatures, dragons, effects, audio, interface, session
Assets/Resources/BlockRealm   shaders, plus the art that Blender generated (tiles, icons, UI, models)
Assets/Editor            project setup, import rules, build script
tools/blender            Blender scripts that make every texture and model
tools/core               build and test tooling (runs without a Unity licence)
tests-core               103 automated tests, console server and bot
docs/specs               design documents
```

### Art pipeline (all Blender, headless)

```
blender --background --python tools/blender/make_tiles.py       -- tools/blender/tiles.json <Resources/BlockRealm>
blender --background --python tools/blender/make_items_ui.py    -- tools/blender/tiles.json <Resources/BlockRealm>
blender --background --python tools/blender/make_block_icons.py -- tools/blender/tiles.json <Resources/BlockRealm>
blender --background --python tools/blender/make_models.py      -- <Resources/BlockRealm>
blender --background --python tools/blender/export_dragons_fbx.py -- <dragon .glb folder> <Resources/BlockRealm>
```

`tiles.json` is produced by `core.sh test --tiles tools/blender/tiles.json`, so art can never drift out of sync with
the block registry. The three dragons are the Blender-built rigged models from the earlier browser version of this
project, re-exported to FBX.

## Building and testing without Unity

```
bash unity/BlockRealm/tools/core/core.sh test        # build the core and run all 103 tests
bash unity/BlockRealm/tools/core/unitycheck.sh       # type-check the Unity layer against the Editor's own assemblies
```

Both use the compiler and .NET runtime bundled inside the Unity Editor install; neither needs a licence.

## What has been verified

| Area | How | Result |
| --- | --- | --- |
| Terrain, caves, ores, trees, lairs, lighting, meshing, collision | automated tests | 103 pass |
| Mining, building, crafting, smelting, inventory rules | automated tests over the real protocol | pass |
| Mobs, explosions, fire, day and night, dragons, taming, riding | automated tests | pass |
| Two players, TCP, persistence, hostile and abusive input | automated tests, plus separate server and bot processes | pass |
| Unity layer compiles against Unity 6.3 | `unitycheck.sh` | pass |
| Textures, icons and models | generated and inspected as images | looks right |
| **Unity rendering, shaders, interface, audio, controls** | **not yet run (no licence)** | **unverified** |

Things most likely to need a first-run adjustment: shader compilation details, model facing direction
(`ModelFactory.ModelYawOffset`), and gamepad axis numbers on non-Xbox pads.

## Known limitations

* Water and lava do not flow; farming, armour, beds, doors and villages are not implemented.
* Creatures walk straight at targets rather than path-finding around obstacles.
* Dragon animation is a simplified procedural rig, not the full animator of the earlier version.
* No transport encryption (see above); no voice or text-chat moderation tools beyond operator commands.
