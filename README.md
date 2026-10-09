# Project Horizon

An original open-world driving game for PC, running in the browser (TypeScript + Three.js + cannon-es).

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

## Controls
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
Early playable prototype: free-roam driving only. See ROADMAP.md for what is and is not built,
and TEST_REPORT.md for what has been verified.

## Other docs
GAME_DESIGN.md, ARCHITECTURE.md, ROADMAP.md, CHANGELOG.md, TEST_REPORT.md, PERFORMANCE.md, ASSET_LICENSES.md
