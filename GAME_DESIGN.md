# Game Design

## Vision
A fan-made, open-world dragon-combat prototype. You are a dragon: fly across a realm, take off and land,
and fight rival dragons with bites, wing buffets and a billowing fire breath. The tone and look follow the
*House of the Dragon* setting. This is a personal prototype built from original procedural models, not an
official or licensed product.

## Reference and what was taken from it
Studied from the supplied pictures and video:
- **Caraxes**: slender and snake-like with the longest neck, dark crimson scales, swept-back brow and
  cheek horns, a "beard" of jaw spikes, red-lined wings with hooked finger claws, spined tail.
- **Vermithor**: huge and bulky, bronze body with a charcoal back, heavy curved horns, tan-red wings.
- **Vhagar**: the largest, grey-green and weathered, blunt heavy head with short horns, vast ribbed wings.
- **The fight clip** (ground fight, about 130 to 270 s): the fire is a telegraphed breath. The head
  rears, the throat glows, then a thick billowing fireball erupts for about 1.5 s with smoke at its edges
  and a fiery flash on impact. Necks coil into an S and whip forward to bite. Wings are thrown out to shove.
  Dust and debris fly on every impact.

## Dragons
| Dragon | Role | Health | Bite | Fire | Air speed |
| --- | --- | --- | --- | --- | --- |
| Caraxes, The Blood Wyrm | Fast, long-necked | 3000 | 170 | 210/s | 230 km/h |
| Vermithor, The Bronze Fury | Heavy | 4200 | 210 | 250/s | 180 km/h |
| Vhagar, Queen of All Dragons | Largest, slowest, hardest hitting | 5600 | 260 | 290/s | 160 km/h |

Attacks: **bite** (short reach, neck lunge), **fire** (a 90 m cone with burning damage over time),
**wing buffet** (knockback and a gust of dust). Each has a cooldown shown on the HUD.

## The realm
Compressed from the show's geography (3.2 km square; distances are far shorter than in the story):
- **Blackwater Bay** to the east: open sea.
- **King's Landing** on hills above the bay: the Red Keep, the Great Sept, the Dragonpit with its ribbed
  dome, and a city of about 180 houses. The player begins beside the Dragonpit.
- **Dragonstone** (north-east): a black basalt fortress on the flank of a volcanic island peak.
  Vermithor's lair.
- **Driftmark** (south-east): High Tide castle on an island. Caraxes's lair when you fly another dragon.
- **Harrenhal** (west): colossal towers slumped like melted candles beside the Gods Eye lake, with a
  lakeside village. Vhagar's lair.
- The King's Road and a coast road connect the cities; forests, rocks, grass, mountains to the west,
  north and south.

## Enemy behaviour
Dragons circle their lair until the player comes within about 650 m or hurts them. They then close in,
slow down to turn tightly, bite in range, breathe fire at mid range, buffet when crowded, and peel away
after a close pass. If the player is on the ground they land and fight on foot. A slain dragon falls, and
a replacement appears at its lair after a few seconds.

## Not designed or built yet
Progression, upgrades, saves, multiple missions, riders, a map, weather and a day/night cycle, sound.
