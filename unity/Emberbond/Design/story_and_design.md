# EMBERBOND: story and game design

A realistic, single-player story game about growing up beside a dragon. You are born, an egg is laid in your
cradle, it hatches, and for twenty years the two of you grow up, build, fight and decide together.

Inspiration (themes and structure, not names or characters): *A Song of Ice and Fire*, *Fire & Blood*, the Tales of
Dunk and Egg, and the HBO adaptations. The world, houses, places and dragons below are original so the game is
ours to ship. Research notes: `story_research.md` (kept in the session scratchpad).

---

## 1. Pillars

1. **A bond that grows.** The dragon is not a mount. It is a character that changes size, silhouette, abilities
   and temperament as you both age, and how you treat it changes who it becomes.
2. **Build together.** A homestead becomes a keep becomes a town. The dragon hauls beams, melts ore, fuses glass,
   scorches ground clear and guards the walls. What you build, and what you burn, stays.
3. **Power has a price.** Every act ends with the dragon able to do more and the world asking more of you.
4. **Intimate to epic.** It starts in a cradle and a barn and ends over a burning, frozen sea.
5. **Looks like a place.** Realistic light, weather, terrain and creatures at 4K on a strong GPU.

---

## 2. World

**The Cinder Isles** are a chain of volcanic islands at the edge of a cold sea, ruled from the walled town of
**Hearthmoor**. Long ago the isles were the nursery of the **Ashen Concord**, an empire whose lords, the
**wyrmwards**, bound dragons with horn-songs and ruled the known world. A night of eruptions ("the Quenching")
destroyed the empire in a single night; only the house that moved its hatcheries to the isles on a prophetic dream
survived.

Those survivors are **House Veyra**, the royal line, now thin, proud and quarrelling. Dragons are down to a handful
of ageing beasts and a vault of cold eggs. Eggs have not hatched in three generations. The Veyra cradle custom
(an egg beside every royal newborn) is a hollow ritual.

Beyond the northern strait the **Pale Tide** is rising: a creeping winter and the cold dead that follow it. The
court argues over succession and ignores it.

Dragons here are called **wyrms**. Three lineages exist, matching the modelled dragons:
- **Cinderwing**: swift, red-black, sleek, volcano-born.
- **Brazenhide**: bronze, heavy-bodied, territorial, fire-hardy.
- **Elderscale**: pale, huge, slow to grow and long-lived; legends say it never stops growing.

---

## 3. The player and the dragon

You are **the Heir-Born**, the player's chosen name and look, born in House Veyra's most overlooked branch, the
wardens of the failing hatchery at **Emberhold** on the island's edge. Your parent is the warden. An egg
is placed in your cradle. Everyone expects it to stay stone.

It does not. The egg you bond with depends on a choice made as a child (which of the three lineages you tended),
and you name your wyrm.

Companions:
- **Maester Orrin Vale** (mentor): keeps the hatchery ledgers and the old horn-songs. Warm, wry, secretly afraid.
  Dies at the midpoint.
- **Tamsin** (found-family squire): a sharp orphan who arrives in Chapter 3 and stays. The quiet thread that stays
  warm while the plot darkens.
- **Corvane Veyra** (sibling or cousin, rival): raised beside you, ambitious, loved by you, used by the court.
- **Regent Maelis** (cunning councillor): the ruler's advisor who always seems to be on your side.

---

## 4. Structure: three acts, nine chapters

Ages are the player character's. In-game time passes between chapters.

### Prologue: The Cradle (birth)
Short, interactive, no combat. You see the hatchery through a baby's eyes: warmth, hands, an egg. A choice: which
way you reach. This seeds the lineage and the first line of the bond.

### ACT I: EMBER (warm, growing up)
1. **The Hatching** (age 7). The egg warms during a winter storm. You carry it to the forge and the hearth. A quiet
   set piece: cracking, a chirp, a choice of name. Teach feeding, carrying, and keeping it warm.
2. **First Flight** (age 11). The hatchling is a fledgling the size of a hound; it can glide from your arm. A day of
   freedom: explore the island from the air. Learn flight, perching, and calling.
3. **Hearthstead** (age 14). Your homestead: build a smithy, a barn, a wall. The dragon hauls beams and melts ore.
   Tamsin arrives. Intro to building, resources, and the first raid (bandits and wolves).
4. **The Wild Wyrm** (age 16). A wild unbonded dragon in the volcano threatens the herds. Respect it, drive it off or
   befriend it, each with consequences. Your dragon is a juvenile and can carry you.

### ACT II: ASH (the court, the cost)
5. **Stonefall** (age 18). The royal progress comes to Emberhold. A feud sparks a siege. Your dragon burns a fortress
   wall for the first time and the result stays: glassy, twisted stone. The mentor dies in the fire. The mid-point
   rug-pull.
6. **The Succession** (age 20). The old ruler dies. Corvane and another claimant each demand your dragon's
   allegiance. Choose oaths, alliances and which villages to protect. Your homestead grows into a keep.
7. **The Pit** (age 23). Hearthmoor builds a dragon-pen "for safety". Confinement stunts dragons, and a mob riot
   storms it. Your choices decide which wyrms live. You may put your own in the pen or defy the council.

### ACT III: FROST (the cold dead)
8. **Winter's Edge** (age 26). The Pale Tide breaks the northern strait. The settlement you built becomes a
   fortress. Your dragon is adult and the size of a barn. Aerial battle over the strait against cold things and the
   weapons that kill dragons.
9. **The Last Egg** (age 28). The final choice about what to do with overwhelming power: bind the cold with the
   old horn-songs at the cost of your bond, burn the north and the survivors with it, or let the wyrms go free and
   trust the world to cope. Epilogue shows the Isles ten years later, shaped by every earlier choice.

Endings: **Hearthfire** (stability at the cost of freedom), **Wildfire** (power used, world scarred),
**Unbound** (dragons freed, you human again). Each has variants driven by Corvane's fate, whether Tamsin
survives, which villages stand, and the dragon's size and trust.

---

## 5. Core systems

### 5.1 The bond
- **Trust** (0-100): raised by feeding by hand, flying together, staying near, keeping promises in dialogue;
  lowered by neglect, confinement, cruelty, orders that ignore its fear.
- **Temper**: fed by hunger, pain, and the player's choices. It shapes fire discipline: a high-temper dragon can
  burn what you did not ask it to.
- **Growth** is driven by food, open sky and rest, not age. Confinement stunts it (the Pit chapter makes this
  the central lesson).

### 5.2 Dragon growth stages
| Stage | Size | Abilities |
|---|---|---|
| Hatchling | cat | follows, perches, small spark, carries nothing |
| Fledgling | hound | glides from your arm, hauls light loads, finds ore by smell |
| Juvenile | horse | rideable for short flights, hauls beams, melts ore and sand |
| Adult | barn | long flight, fire on walls and units, guards the homestead, hauls blocks |
| Elder | keep | storms, sweeping fire, reshapes ground; each use has a cost to trust and to the land |

### 5.3 Building together
- Authored modular kit (timber frame, stone, thatch, slate, gates, towers, bridges) placed on a snap grid with
  structural rules (weight, supports, fire) and with realistic materials. No free voxel digging.
- The dragon acts as crane, furnace and wall: it lifts heavy beams, melts ore to ingots, fuses sand to glass,
  clears and scorches ground, and defends.
- Villagers (found family) move in, work and have stories. The homestead's growth is tracked and visible across
  chapters.

### 5.4 Flight and combat
- Third-person flight with weight and momentum, wind and thermals, stamina tied to the dragon's stage.
- Fire has range, spread and ground effects (scorching, glass, wreckage). Allies and civilians can be hurt.
- Enemies: bandits and wolves early, soldiers and siege weapons (scorpion-style bolts at the eye and wing),
  rival dragons, and the cold dead.

### 5.5 Narrative delivery
- Real-time dialogue scenes with branching choices, in-engine cutscenes, journal entries and found books with
  conflicting accounts of history (the lore is deliberately unreliable).
- A persistent state file records trust, choices, the keep's layout and every burned or saved place.

---

## 6. Technology plan (Unity 6.3 LTS, HDRP)

- **Rendering:** HDRP, physically based materials, volumetric fog and clouds, physical sky, SSAO, SSR, contact
  shadows, ray-traced-style options on capable GPUs, high-end post-processing, DLSS/FSR where available, 4K target.
- **Terrain:** Unity Terrain generated from deterministic noise and hydrology (islands, volcanoes, rivers, coast),
  splat-mapped PBR layers (rock, ash, grass, snow, sand), GPU-instanced vegetation and rocks, water with proper
  shaders and shoreline foam.
- **Assets:** produced in Blender with Python: dragon models (reuse the three existing, upgrade materials and
  rigs), buildings kit, props, trees, rocks, tileable PBR textures (4K) baked procedurally.
- **Gameplay code:** C#, a small state machine for chapters, a data-driven quest/dialogue system, a dragon
  controller (growth, trust, flight, fire), a building system with structural simulation.
- **Verification:** unattended smoke run of a built player capturing screenshots, frame time and errors, as done
  for the voxel prototype; scripted playthrough of each chapter's key beats.

## 7. First vertical slice (what we build next)

1. A realistic island: sky, sea, terrain, trees and rocks in HDRP.
2. Emberhold hatchery: the cradle, the egg, hatching, and the hatchling following the player.
3. Third-person player controller and a juvenile dragon you can fly.
4. The first building interaction (hearth wall) with the dragon hauling a beam.
5. Smoke test at 4K with frame-time capture.
