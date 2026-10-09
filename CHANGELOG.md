# Changelog

## 0.6.0 - 2026-10-09
Graphics pass.
- Post-processing: the scene renders in a half-float HDR buffer; SMAA is now the default antialiasing
  (`?aa=msaa` keeps 4x MSAA as an option). A final grade pass adds a gentle contrast curve, saturation,
  warm highlights and cool shadows, a vignette and dither against banding.
- Sky: a drifting field of 46 soft cumulus clouds that follows the player, lit and shaded to match each
  time-of-day preset. New `dusk` preset; press T to cycle golden hour, midday, sunset and dusk.
- Terrain: two scales of the ground texture blend with distance to hide tiling, large-scale tone variation,
  a procedural detail normal map, and finer per-vertex colour variation.
- Fixes found while testing: dragon switching leaked GPU textures and geometries (skeletons were never
  disposed; now +0 over repeated switches); castle platforms were uneven because the road smoothing fought
  the platform (a Red Keep sample varied 12.5 m, now level to within 3.3 m at 0.8 of the radius); a walking
  dragon could overshoot its speed cap.
- Added an in-browser self-test suite (36 checks); see README.

## 0.5.0 - 2026-10-09
- The three dragons are now original designs with original names: Cinderwing (The Ember Serpent),
  Brazenhide (The Bronze Warden) and Elderscale (The Elder Tyrant). Their silhouettes, colours and movement
  still take general cues from the supplied references, but they are not copies of any film or TV creature.
  URL option is now `?dragon=cinderwing|brazenhide|elderscale`.
- Detail pass on the dragon meshes: skin geometry is subdivided and displaced with wrinkles and scale-plate
  ridges (about 24 k triangles each, up from about 7.5 k). A visible but modest step, not hand-sculpt quality.
- Reference video link (a Blender dragon-modelling tutorial) reviewed as far as possible; see TEST_REPORT.md.

## 0.4.0 - 2026-10-09
- Game is now a dragon-combat game. The car, pegasus and old procedural dragon were removed.
- Three Blender-built, rigged dragons modelled on the supplied references: Cinderwing, Brazenhide, Elderscale
  (single skinned body, scale textures, sculpted heads with horns and teeth, skeletal wings with membrane,
  hind legs; 7 to 8 k triangles each).
- Rig animation: segmented neck and tail that follow the head, wing flap and fold, walk, head stabilised
  against banking, flinch on hit, death pose.
- Combat: bite (neck coil and lunge), fire breath, wing buffet, health, knockback, burning, death, kill feed.
- Fire breath modelled on the supplied clip: throat glow, then a billowing HDR fireball with smoke, embers,
  impact flashes and a light that lifts the scene.
- Enemy AI: lair patrol, aggro, lead pursuit with speed control, attack selection, landing for ground fights.
- Open world reshaped after the setting of House of the Dragon (3.2 km): Blackwater Bay and open sea,
  King's Landing with Red Keep, Great Sept and Dragonpit, Dragonstone volcano island, Driftmark and High Tide,
  Harrenhal by the Gods Eye lake, King's Road and coast road, about 240 houses in four settlements.
- Combat HUD: health, cooldown pips, enemy name plates with distance and health, damage vignette, death screen.
- Camera scales to creature size; 1/2/3 switch dragon.
- Fixes: Vite crashed when the large reference video was locked (watch ignore list); AI woke before the first
  frame because creatures sat at the origin; AI could orbit a circling target forever (speed control and lead pursuit).

## 0.3.0 - 2026-10-09
- Player is now a red dragon (original wyvern-style design): 11-segment neck, 18-segment tail,
  skeletal bat wings with finger bones and a membrane rebuilt every frame, hind legs, horns, jaw.
- Neck animation: the head leads turns and each segment follows with a delay; looks up in climbs
  and down in dives; pumps with the wing beat; idle sway and glances; the head is stabilised
  against the world so it stays near level while the body banks.
- Tail follow-through, wing fold on the ground, outer-wing lag on each stroke, legs trail in flight.
- Roar (hold F): jaw opens and the head lifts. No sound yet.
- Pegasus flight code generalised into `FlyingMountController` with per-creature tuning;
  pegasus kept at `?mount=pegasus`, car at `?mount=car`.

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
