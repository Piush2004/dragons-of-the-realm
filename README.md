# Project Horizon: Dragons of the Realm

An original open-world dragon-combat game for PC, running in the browser (TypeScript, Three.js, cannon-es).
You fly and fight as one of three dragons across a compressed realm inspired by the setting of
*House of the Dragon*, against AI dragons that guard their lairs.

Dragon models, castles, houses and trees are built in Blender by scripts in `tools/blender/`.
The reference pictures and video you supplied in `Images of Dragon/` informed the designs; they are
not part of the game and are excluded from version control.

## Requirements
- Node.js 20+ (developed on 24) and npm
- A WebGL2 browser (Chrome or Edge recommended) and a GPU; developed on a Ryzen 7 with a Radeon 780M
- To regenerate the 3D assets only: Blender 5.x (the generated files are already in `public/assets/`)

## Launch
```bash
npm install
npm run dev
```
Open http://localhost:5173. The first load takes a few seconds while the world is built.

Options on the URL:
- `?dragon=caraxes|vermithor|vhagar` choose your dragon (default Caraxes)
- `?enemies=0..5` number of enemy dragons (default 2)

## Controls
| Input | Action |
| --- | --- |
| W / S | Speed up / slow down |
| A / D | Turn (banks in the air) |
| Space | Flap: take off and climb |
| Shift or Q | Dive / descend |
| J or left mouse | Bite |
| F | Fire breath |
| K or right mouse | Wing buffet |
| C | Cycle camera (Chase, Far Chase, Wingside, Head-on, Rider) |
| B (hold) | Look back |
| 1 / 2 / 3 | Switch to Caraxes / Vermithor / Vhagar |
| R | Reset to the Dragonpit (or rise again after dying) |

Releasing W lets you glide slowly, which is how you fight at close quarters. Holding W flies at full
speed, and Caraxes outruns both enemy dragons.

## Build and test
```bash
npm run typecheck   # strict TypeScript check
npm run build       # type-check, then a static bundle in dist/
npm run preview     # serve the built bundle
```
There is no automated test suite yet. See TEST_REPORT.md for what was verified and how.

## Regenerating the 3D assets
```bash
BLENDER="C:/Program Files/Blender Foundation/Blender 5.2/blender.exe"
"$BLENDER" --background --python tools/blender/build_dragons.py   -- public/assets/dragons
"$BLENDER" --background --python tools/blender/build_landmarks.py -- public/assets/landmarks.glb
"$BLENDER" --background --python tools/blender/build_structures.py -- public/assets/structures.glb
"$BLENDER" --background --python tools/blender/build_environment.py -- public/assets/environment.glb
```
`tools/blender/preview_dragon.py` and `preview.py` render contact sheets for checking a model.

## Documentation
GAME_DESIGN.md, ARCHITECTURE.md, ROADMAP.md, CHANGELOG.md, TEST_REPORT.md, PERFORMANCE.md, ASSET_LICENSES.md
