# Project Horizon

An original open-world riding and driving game for PC, running in the browser (TypeScript + Three.js + cannon-es).
You ride a red dragon that walks and flies. Other mounts: http://localhost:5173/?mount=pegasus and http://localhost:5173/?mount=car.

## Requirements
- Node.js 20+ (developed on 24.16) and npm
- A WebGL2-capable browser (Chrome / Edge recommended)

## Launch
```bash
npm install
npm run dev
```
Then open http://localhost:5173.

## Build
```bash
npm run build     # type-checks, then writes a static bundle to dist/
npm run preview   # serves the built bundle
```

## Controls: dragon (default) and pegasus
| Key | Action |
| --- | --- |
| W / Up | Speed up |
| S / Down | Slow down |
| A, D / Left, Right | Turn (banks in the air) |
| Space or E | Flap: take off and climb |
| Shift or Q | Descend |
| F (hold) | Roar (dragon) |
| C | Cycle camera (dragon: Chase, Far Chase, Wingside, Head-on, Rider) |
| B (hold) | Look back |
| R | Reset to the start line |

Let go of Space at speed to glide. Descend to the ground to land and walk.

## Controls: car (`?mount=car`)
| Key | Action |
| --- | --- |
| W / Up | Throttle |
| S / Down | Brake; reverse once stopped |
| A, D / Left, Right | Steer |
| Space | Handbrake |
| C | Cycle camera (Chase, Far Chase, Hood, Bumper, Cockpit) |
| B (hold) | Look back |
| R | Reset car to the start line |

## Status
Early playable prototype: free-roam riding, flying and driving only. See ROADMAP.md for what is and is not built,
and TEST_REPORT.md for what has been verified.

## Other docs
GAME_DESIGN.md, ARCHITECTURE.md, ROADMAP.md, CHANGELOG.md, TEST_REPORT.md, PERFORMANCE.md, ASSET_LICENSES.md
