# Performance

## Hardware
AMD Ryzen 7 250, NVIDIA GeForce RTX 5060 Laptop GPU (plus Radeon 780M iGPU), Windows 11.

## Measurements (2026-10-09, dev server, golden-hour preset, 801x914 canvas)
| Metric | Value |
| --- | --- |
| Physics step, about 60 to 80 active bodies | 0.11 ms |
| Sim step + full render, back to back | about 1.4 ms |
| Render only (30 frames + gl.finish) | about 1.2 ms |
| Physics step with all 4,900 trees as bodies (rejected design) | 113 ms |

Caveats: measured in an embedded browser pane at a small window size, not full-screen 1080p or
1440p, and not confirmed to be running on the discrete GPU. Real frame rate at full resolution
has not been measured. There is no in-game FPS counter yet.

## Optimizations in place
- Instanced trees (4 draw calls for the whole forest).
- Streamed obstacle colliders (ObstacleField).
- One 4096 shadow map that follows the car (170 m box) instead of covering the map.
- Textures generated once at load.

## To do
- Full-screen FPS measurement and an on-screen frame-time readout.
- Graphics presets (shadow size, pixel ratio, bloom, tree count).
- Terrain chunking and LOD once the map grows.
