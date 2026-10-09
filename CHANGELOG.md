# Changelog

## 0.2.0 - 2026-10-09
- Player is now a pegasus: gallop on the terrain, flap to take off and climb, glide, descend and land.
- Procedural pegasus model with gallop cycle, wing fold on the ground and wing-beat animation in flight
  (beat rate and depth follow climb effort and speed), banking, pitch, tail and neck motion.
- Pegasus camera set: Chase, Far Chase, Wingside, Rider.
- `PlayerActor` interface so the loop, camera and HUD work with any mount; the car remains at `?mount=car`.
- HUD shows GALLOP or altitude for the pegasus.
- Dev server now binds to localhost only (was exposed to the local network).
- Space and arrow keys no longer scroll the page.

## 0.1.0 - 2026-10-09
- Project scaffold: Vite + TypeScript + Three.js + cannon-es.
- Raycast-vehicle car with throttle, brake, reverse, handbrake, engine braking and parking hold.
- Heightfield terrain with rolling hills and a mountain rim, flattened under the road.
- 1.2 km spline road with procedural asphalt, edge lines and dashed centre line.
- Instanced forest (about 4,900 trees) with streamed trunk colliders.
- Physically based sky, sun shadows, sky environment lighting, fog, bloom, ACES tonemapping.
- Procedural car bodies (coupe / sedan / hatch) with wheel arches, lights, wing and exhausts; four car configs.
- Camera rig: Chase, Far Chase, Hood, Bumper, Cockpit, look-back, impact shake.
- HUD: speed, gear (including R), RPM bar, controls line, toast.
- Fixes: wheel raycasts missing the CANNON.Plane ground; car shaking from un-interpolated rendering;
  car creeping downhill when parked.
