# Game Design

## Vision
An original open-world driving and racing game: free-roam a scenic fictional region, enter races
against AI, earn credits, upgrade and repaint cars. Arcade-simulation handling. No real brands,
logos, maps or licensed cars.

## Art direction
Realistic, bright, high-contrast outdoor look in the style of modern open-world racers:
sky-driven image-based lighting, physically based materials, glossy or satin car paint, detailed
asphalt, rolling terrain with a mountain rim, dense forests, atmospheric haze. Default time of
day is golden hour. Everything is generated procedurally at load time (no external art assets),
so the result is stylised-realistic, not photoreal.

## World (current)
"Valley Loop": a 1.2 km closed road over rolling hills inside a 1.4 km square terrain ringed by
mountains, with mixed conifer and broadleaf forest. Planned regions: coast, city, mountain pass,
off-road trails, festival hub.

## Player mount
The player rides a red dragon (default): a wyvern-style design with a long serpentine neck,
bat wings, hind legs and a long tail. It lumbers on foot, takes off by flapping, flies at up to
about 210 km/h and glides when the rider stops flapping. Hold F to roar.

A white pegasus (`?mount=pegasus`, gallops at about 61 km/h, flies at about 158 km/h) and the
car fleet below (`?mount=car`) are also kept.

## Vehicles
| Id | Name | Style | Drive | Notes |
| --- | --- | --- | --- | --- |
| verdant_rs | Verdant RS | Sedan | AWD | Player default. Satin dark green, bronze wheels, black roof, rear wing |
| striker_gt | Striker GT | Coupe | RWD | Balanced |
| rally_pup | Rally Pup | Hatch | AWD | Soft long-travel suspension, lower top speed |
| apex_prototype | Apex Prototype | Coupe | RWD | Light, stiff, fastest |

Only Verdant RS is selectable in-game today (there is no garage UI yet).

## Planned loop (not built yet)
Free roam, drive to an event marker, race against AI, results and credits, garage upgrades, unlock more events.
