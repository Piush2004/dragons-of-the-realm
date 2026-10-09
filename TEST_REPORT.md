# Test Report - 2026-10-09 (v0.1.0)

Method: scripted checks run in the in-app browser against the dev server through
`window.__horizon.simulate` (fixed 60 Hz steps), plus screenshots. There is no automated test
suite in the repo yet, and nothing here was play-tested by hand with a keyboard.

| Check | Result | Evidence |
| --- | --- | --- |
| Type check (`npm run typecheck`) | Passed | no errors |
| Production build (`npm run build`) | Passed | 665 kB bundle (177 kB gzip) |
| Page loads without console errors | Passed | clean load |
| Car settles on all four wheels | Passed | 4 wheels in contact, upright |
| Acceleration | Passed | flat-ground test on Striker GT: 78 km/h at 5 s, 173 km/h at 15 s |
| Braking | Passed | 173 km/h to rest within 6 s |
| Reverse | Passed | -7 m/s after 3 s of brake from rest, HUD gear R |
| Steering and road following | Passed | scripted autopilot completed the 1.2 km lap in 41.7 s, stayed upright |
| Parked car holds on a slope | Passed | 0.2 to 0.4 km/h residual after the fix (was 10 km/h) |
| Tree collision | Passed | 60 km/h head-on: 17 m/s impact reported, car stopped at the trunk, upright |
| Ride smoothness on road at 100 km/h | Passed | vertical acceleration RMS 0.19 m/s^2 |
| Camera modes render | Passed | Chase, Far Chase, Hood, Cockpit screenshots. Bumper not screenshotted |
| Look-back (B), camera key (C), reset (R) through real key presses | Not run | only exercised through code |
| Handbrake and drift behaviour | Not run | |
| Vibration fix on a high-refresh display | Not run | the fix is render interpolation; it needs a real-time look by a person |
| Final headlight and cockpit-camera tweak | Not run | type-checked and built only |
| Gamepad | Not run | not implemented |
| Races, AI, garage, saves, menus, audio | Blocked | not implemented |

## Known issues
- The test autopilot drifted up to 6.8 m from the centreline in one corner (road half-width is 6 m). This is crude test steering and is not confirmed as a handling fault.
- Cockpit view has no interior model; you see roof lining and bonnet only.
- Trees are low-poly, and only their trunks collide.
- Nothing stops the car beyond the mountain rim at the map edge.
- npm audit reports 2 advisories in dev-only dependencies (the vite/esbuild dev server).
