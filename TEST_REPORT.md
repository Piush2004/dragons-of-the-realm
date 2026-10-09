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

## Dragon (v0.3.0), same scripted method
| Check | Result | Evidence |
| --- | --- | --- |
| Type check and production build | Passed | no errors |
| Take-off, climb, cruise | Passed | 60 m altitude, 214 km/h in level flight |
| Descend and land, then walk | Passed | grounded, HUD shows WALK, 38 km/h on foot |
| Head leads the turn | Passed | 0.15 s into a turn: head segment 0.067 rad, base segment 0.002 rad; equal after about 1.7 s |
| Neck bends into turns | Passed | 1.12 rad total neck yaw at full turn input |
| Neck looks up in climbs, down in dives | Passed | summed neck pitch -0.43 rad climbing, +0.49 rad diving |
| Head stays level in a bank | Passed | body rolled 0.75 rad, head up-vector y 0.97 (was 0.77 before stabilisation) |
| Roar | Passed | jaw opens to 0.75 rad while F is held |
| Wing, neck and tail appearance | Passed | screenshots in level flight, banked turn (side and head-on) and standing |
| Walk cycle, folded-wing pose, tail motion, wing-beat neck pump | Not run | code runs, motion not watched in real time |
| Pegasus mode after the controller refactor | Passed | gallops at 66 km/h, takes off to 19 m in 2 s |
| Dragon cameras, real key presses | Not run | |

Dragon known issues: folded wings on the ground read as a small bundle on the back rather than
resting wrists on the ground; it is built from simple shapes, with no skinned mesh or textures;
walk top speed overshoots its 32 km/h setting slightly.

## Pegasus (v0.2.0), same scripted method
| Check | Result | Evidence |
| --- | --- | --- |
| Type check | Passed | no errors |
| Gallop | Passed | 74 km/h after 4 s of throttle; coasts to 2 km/h in 3 s; brakes to rest |
| Turning | Passed | 1.9 rad of heading change in 1 s of left input on the ground |
| Take-off and climb | Passed | 35 m altitude after 3 s of flapping |
| Cruise | Passed | 171 km/h in level flight |
| Glide | Passed | lost 5.6 m of altitude in 5 s with no input |
| Descend and land | Passed | back on the ground and reported grounded |
| Tree collision on the ground | Passed | stopped 0.93 m from the trunk centre (sphere 0.65 m + trunk 0.4 m) |
| Wing animation | Passed | screenshots show folded wings on the ground and up/down strokes in flight |
| Final tail and wing-fold pose tweak | Not run | loads and flies, but the new pose was not looked at |
| Car mode (`?mount=car`) after the refactor | Passed | loads, 4 wheels grounded, 52 km/h after 4 s of throttle |
| Pegasus cameras, real key presses, flight into trees or mountains | Not run | |

## Known issues
- The pegasus is built from simple shapes (capsules, cylinders, flat feather panels); it is not a sculpted model.
- On the ground the pegasus follows terrain height directly, so it can run up any slope.
- The test autopilot drifted up to 6.8 m from the centreline in one corner (road half-width is 6 m). This is crude test steering and is not confirmed as a handling fault.
- Cockpit view has no interior model; you see roof lining and bonnet only.
- Trees are low-poly, and only their trunks collide.
- Nothing stops the car beyond the mountain rim at the map edge.
- npm audit reports 2 advisories in dev-only dependencies (the vite/esbuild dev server).
