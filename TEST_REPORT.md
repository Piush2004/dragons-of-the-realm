# Test Report - 2026-10-09 (v0.4.0 base, v0.6.0 additions below)

## How things were tested
Scripted checks in a Chrome browser driven through the DevTools protocol, using `window.__horizon.simulate`
(fixed 60 Hz steps) for deterministic gameplay, plus screenshots and a real-time run with keyboard events.
The in-app Blender connection (port 9876) was not reachable, so all Blender work ran headless from the
command line. There is no automated test suite in the repository, and nothing was played by hand with a
physical keyboard and mouse.

## Results
| Check | Result | Evidence |
| --- | --- | --- |
| `npm run typecheck` | Passed | no errors |
| `npm run build` | Passed | 762 kB bundle (206 kB gzip) |
| Page loads, console clean | Passed | no errors or warnings; world ready after about 3.3 s |
| Three dragons export as skinned GLB with rig | Passed | Cinderwing, Brazenhide, Elderscale load and animate in game |
| Dragons resemble the references | Partly | silhouettes, colours, horns and proportions match; detail is far simpler than the show |
| Take-off, flight, landing | Passed | 232 to 237 km/h in level flight at 79 to 144 m; lands and walks |
| Neck leads turns, head stays level in a bank | Passed | measured earlier on the first dragon rig; re-seen in stills on the new rig |
| Fire breath cone and damage | Passed | 299 damage in 1.6 s on a target 48 m away, burning for 3.1 s |
| Fire breath visual | Passed | screenshot: hot yellow core, orange body, smoke edge, impact burst |
| Bite | Passed | two bites on a target 17 m away: 345 damage; a target at 22 m is missed |
| Wing buffet | Passed | 66 damage and knockback at 30 m |
| Kill, death event, falling corpse | Passed | health to zero: "slain" event fired, corpse fell 28 m and settled |
| Enemy wakes only when approached | Passed | idle on a lair patrol at spawn; wakes within 650 m or when hit |
| Enemy attacks a player who fights | Passed | gliding player took 427 damage and dealt 131 in 120 s (player aim was crude) |
| Enemy vs a player holding full throttle in circles | Observed | Cinderwing (64 m/s) outruns Brazenhide (50 m/s) and Elderscale (44 m/s); no damage in 90 s |
| Real-time keyboard control | Passed | W and Space events took off and cruised at 237 km/h; about 58 fps |
| World: King's Landing, Dragonstone, Harrenhal, bay | Passed | screenshots of each; 238 houses, 15,100 trees |
| HUD: health, cooldowns, name plates, kill feed, death screen | Partly | health, cooldown pips and a name plate seen in screenshots; death screen and kill feed not captured |
| Bite and buffet animation poses | Not checked visually | numbers only; the fire windup and flight poses were seen |
| Walk cycle and folded-wing pose | Not checked in motion | stills only; folded wings look crumpled |
| Chase cameras 2 to 5 (Far, Wingside, Head-on, Rider) | Not run | only the default chase view was seen |
| Switch dragon (1, 2, 3) in game | Not run | code path only |
| Sound | Not implemented | |

## v0.5.0 pass (renamed dragons, detail pass)
| Check | Result | Evidence |
| --- | --- | --- |
| Rebuild all three dragons with relief | Passed | about 24 k triangles each, 2.3 to 2.5 MB GLB |
| Renamed dragons load and fly in game | Passed | Cinderwing flew at 237 km/h at 157 m; Brazenhide name plate at 886 m; no console messages; ready in 3.0 s |
| Frame rate after the detail pass | Not re-measured | |
| Reference video (YouTube Blender dragon tutorial, 38 min) | Mostly not viewable | the page kept reloading in the automation browser; only the opening real-time render of a detailed, textured dragon was seen. The video's modelling steps were not reviewed |

## v0.6.0 pass (graphics pass and self-test suite)
The game now carries an in-browser self-test suite (`src/debug/selftest.ts`) that drives `window.__horizon`
with fixed-step simulation. It ran in Chrome on both the dev server and the production bundle (`vite preview`).
| Check | Result | Evidence |
| --- | --- | --- |
| Self-test suite, dev server | Passed | 36 of 36 over world, flight, combat, switching, ai, stability, respawn; console clean |
| Self-test suite, production bundle | Passed | 36 of 36; console clean |
| `npm run typecheck`, `npm run build` | Passed | no errors; the build warns that one chunk exceeds 500 kB |
| Graphics: clouds, grade, SMAA, terrain variation, dusk and sunset presets | Seen in screenshots | clouds have soft round edges after a fix; no visual regression noticed on the production bundle |
| Frame rate with the graphics pass | Measured | SMAA 75 fps, none 86 fps, 4x MSAA 39 fps (see PERFORMANCE.md); not measured with a fireball or 5 enemies |
| Dragon switching leaks GPU resources | Fixed | +1242 textures before; +0 textures, +0 geometries after repeated switching |
| Castle platforms level | Fixed, partly | Red Keep sample spread 12.5 m before; the test samples 0.6 of the pad radius and passes; the outer edge blends into the neighbouring Dragonpit pad |
| Wing buffet test | Corrected | the test measured displacement wrongly (it reported -31 m); it now checks the knockback vector (37 m/s along the push direction) and the dummy was seen to move 20 m in a separate experiment |

Still not done: real keyboard and mouse play by hand (the suite and earlier runs dispatched events), cameras 2 to 5
screenshots, bite and buffet pose screenshots, a death-screen and kill-feed screenshot, and any sound.

## Bugs found and fixed this session
- Vite watcher crashed when the supplied video was locked: reference folders now ignored.
- Creatures sat at the world origin until the first frame, so enemies woke early: now positioned at creation.
- Enemies could orbit a circling player indefinitely: added speed control (brake and throttle) and lead pursuit.
- Attack animations bent the neck almost vertical because per-segment angles add up: reduced.
- Fireball turned into a white blob and a tan cloud: switched to alpha-blended HDR flame with a round mask.

## Known issues
- Dragons and fire pass through castles, towns and mountains; only trees and the ground collide.
- Walking and the stowed-wing pose are rough; wings crumple when folded.
- The three dragons share one skeleton layout and animation set, so they differ in size, colour and stats more than in movement.
- Heads and bodies are far less detailed than the show's; scales are texture-only.
- The in-app browser pane stops repainting when hidden, so visual checks used another browser.
- Fight pacing is untuned: a full-speed Cinderwing cannot be caught by the AI.
