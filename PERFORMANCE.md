# Performance

## Hardware
AMD Ryzen 7 250 laptop with an NVIDIA GeForce RTX 5060 Laptop GPU and an AMD Radeon 780M iGPU, Windows 11. The measurements below ran in Chrome on the **Radeon 780M** (ANGLE / Direct3D 11); the RTX 5060 was
not used, so these are conservative.

## Measurements (2026-10-09, dev server, 1920x889, two enemy dragons, real-time loop)
| Metric | Value |
| --- | --- |
| Frame time, average over 240 frames | 17.2 ms (about 58 fps) |
| Frame time, 95th percentile | 23.7 ms |
| Time to a playable world after navigation | about 3.3 s |
| JS heap | about 290 MB |
| Terrain | 3.2 km square, 801 x 801 vertices (heightfield also used by physics) |
| Trees | 15,100 instanced (near trees swap to a detailed model within 240 m) |
| Houses | 238 instanced |
| Dragon models | about 24 k triangles each (after the detail pass), one skinned mesh set per dragon |
| Physics step | about 0.1 ms with 60 to 80 active bodies (measured earlier in the project, before dragons were added) |
| Particles | up to 5,000 flame plus 5,000 smoke/dust (about 650 live during a blast) |

Not measured: frame rate on the RTX 5060, at 4K, or with 5 enemies; GPU time per pass; memory beyond JS heap.

## What keeps it fast
- Instanced forests and houses, near/far tree detail swap, grass that follows the player.
- Tree colliders streamed in a 3 x 3 cell window instead of thousands of static bodies.
- One shadow map that follows the player.
- Flame, smoke and dust drawn as GPU billboards from one instanced buffer each.

## Known costs
- World generation (terrain, forests, houses) runs on the main thread at start.
- 3 GLB dragon files total about 7.3 MB, landmarks and structures 6.6 MB, environment 3.4 MB. The frame-rate figures above were measured before the detail pass and have not been re-measured.
