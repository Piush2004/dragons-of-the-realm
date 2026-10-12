# EMBERBOND, version 2: reference-driven design

This replaces the tone and chapter list of `story_and_design.md`. The player supplied two reference images
(`reference/ref_gameplay_sheet.png`, `reference/ref_overlook.png`). They define the **look and the feel**; this
document turns every frame in them into a system or a scene, and bases the story on the succession war of *Fire &
Blood* / *House of the Dragon* and the cold threat of *A Song of Ice and Fire* / *Game of Thrones*.

## 1. What the references say (frame by frame)

| Frame | What it shows | What we build |
|---|---|---|
| Dragon flight, over coast | Third-person behind the rider's shoulder, huge wing in frame, clouds below, an enemy dragon breathing a fireball, boss health bar on top, own bar at bottom | Flight with a chase camera, wing in frame, cloud layer, fireball projectiles, aerial duel with lock-on and a boss bar |
| Street fight | Rider with silver hair, sword clash with sparks, crowd, "Parry" prompt, stamina/health, a timing bar | Melee combat: light/heavy, parry window with a timing meter, stamina, crowds that react |
| "Quest Active: Confront the Beast" | Torch-lit descent, chains, huge pit, a dragon head in the dark, compass strip | Dungeon traversal with a torch as the light source (real dynamic light), chained dragon encounter, quest banner and compass |
| Battlefield charge | Armoured rider on a horse, "Charge" prompt and meter, dragon burning a siege tower overhead | Mounted combat, army simulation (hundreds of soldiers), dragon-support fire strike, charge meter |
| Throne room dialogue | Iron-spike throne, two characters, timed tone choices: [Diplomatic] [Aggressive] [Deceptive] with a countdown | Branching dialogue with tagged tones and a timer; tone feeds reputation; the throne room as a hero set |
| Dragon head close-up | Sigil banners, ember particles, detailed scales | Cinematic close-up camera on the bonded dragon, particles, banners |
| Overlook | Cloaked rider on a cliff, a vast walled port city with a castle on a hill, ruins, a dragon overhead, a compass bar, a sigil health badge, "Summon Mount" / "Open map" prompts | The hub city as a landmark vista, map, mount summon, sigil-badge health/stamina HUD |

## 2. Look target

- Cinematic realism: warm late-afternoon sun, hazy atmospheric perspective, volumetric clouds, god rays through
  smoke, desaturated stone and leather palette with saturated fire and red cloth accents, shallow depth of field in
  dialogue scenes, film grain, bloom on fire, motion blur in flight.
- Characters: armour with layered plates and cloth, hair and cloak simulation, a rider with a visible silver-white
  braid as the family mark.
- Dragons: large, scarred, textured scales and membrane with subsurface light, ember glow in the throat.
- HUD: thin dark bars with gold trims, a top compass strip with quest diamond, a round sigil badge at bottom left,
  contextual button prompts at the bottom right, subtle and period-styled typography.
- **Honest limit:** the reference frames are AI-generated concept art. Matching that fidelity "same to same" needs
  hand-authored high-poly characters, motion capture and a large art team. What we build is the same *composition,
  lighting model, camera language and UI*, generated procedurally with Blender and HDRP, and improved over time.

## 3. Story (original names, adapted from the source books)

The structure follows the Dance of the Dragons; the cold threat follows the Others. Every name below can be swapped
for the book name through `Design/name_map.csv` for private use; a shipped game must keep the original names.

### Cast and the source parallel
| In Emberbond | Plays the role of | Notes |
|---|---|---|
| **The Heir-Born** (player, chosen name) | Jacaerys / Lucerys | Young rider, silver-braided, child of the heir |
| **Queen-Heir Maelys Veyra** | Rhaenyra | Named heir by the old king; your parent |
| **Prince Kael the Rogue** | Daemon | Your uncle and mentor: reckless, loving, dangerous |
| **King Aldric Veyra** | Viserys I | Gentle, failing, names Maelys heir then remarries |
| **Dowager Alisande Hale** | Alicent | The rival mother, not a villain |
| **Lord Hand Otrek Hale** | Otto Hightower | Cunning councillor |
| **Corvane Veyra** | Aegon II | Half-brother, crowned in haste |
| **Prince Aemric Veyra** | Aemond | One-eyed rival rider on the largest, oldest wyrm |
| **Maester Orrin Vale** | Mentor | Dies in Act II |
| **Tamsin** | Found-family squire | Egg-and-Dunk thread |
| **The Pale Tide / the Cold Dead** | The Others / Night King | Act III threat |

Dragons: your **wyrm** (bonded from the cradle egg, you name it), Kael's lean red **Blood-Wyrm**, Aemric's huge
**Old Vael**, and the chained **Beast** in the Pit.

### Acts and chapters (maps to the references)
**Prologue: The Cradle.** Born in Hearthmoor, an egg in your cradle.

**ACT I, EMBER**
1. *The Hatching* (age 7): the egg hatches in firelight at Emberhold.
2. *First Flight* (age 11): the coast, cloud banks, the open sea. The first aerial run (frame: flight).
3. *Blades in the Market* (age 15): a rival house's men insult your mother's claim in the market of Hearthmoor;
   sword fight with a parry meter (frame: street fight). Kael steps in.
4. *The Old King's Table* (age 17): the king dies. Throne-room council with timed tone choices
   (frame: throne dialogue). First real consequence of your reputation.

**ACT II, ASH**
5. *The Stair and the Torch* (age 19): the regent chains your wyrm beneath the city. You descend with a torch to
   confront the Beast, the pit's wild dragon, and your own chained one (frame: "Confront the Beast"). The bond is
   tested, not assumed.
6. *The Charge* (age 21): a river crossing, a mounted charge, and your wyrm burning a siege tower (frame: charge).
7. *Over the Gods Lake* (age 23): aerial duel with Aemric on Old Vael (frame: flight/fire). Orrin is killed in the
   fire at the end.
8. *The Pit Riot* (age 25): a mob storms the dragon-pen; choose who to save.

**ACT III, FROST**
9. *The Pale Tide* (age 27): winter breaks the strait; the war of succession is a sideshow.
10. *The Last Egg* (age 29): choose Hearthfire, Wildfire or Unbound. The final overlook (frame: overlook) shows the
    city as your choices left it.

### Dialogue system (frame: throne room)
Each beat offers up to three tones, `[Diplomatic]`, `[Aggressive]`, `[Deceptive]`, with a visible countdown.
Tones change three hidden values (Honour, Fear, Cunning) that unlock lines, allies and endings. Silence is a choice.

## 4. Systems list (replaces section 5 of the earlier document)
1. Third-person character: walk, run, climb, mantle, sneak, ride horse, summon mount, interact.
2. Combat: sword, parry meter, stamina, dodge, light/heavy, finishers; crowd AI.
3. Dragon: bond, growth stages, flight (chase camera), fireball, aerial lock-on duel, perch, summon.
4. Mass battle: formation soldiers, siege towers, dragon fire strikes, charge meter.
5. Dialogue: branching with tone tags, timers, reputation values, cinematic camera.
6. Quests and compass: active quest banner, diamond markers, map.
7. World: the Cinder Isles, the hub port city (Hearthmoor), Emberhold keep, ruins, the Pit.
8. Building: Emberhold fortification and village (kept from the first design).
9. Presentation: HDRP, volumetric clouds, god rays, film grain, DoF, motion blur, HUD matching the references.
10. Save, settings, accessibility, controller support.

## 5. Build order
1. Island, terrain, sky, ocean, vegetation in HDRP (in progress).
2. Third-person player, HUD (compass, bars, prompts) matching the references.
3. Dragon flight with chase camera and fireball.
4. Hearthmoor hub: a procedurally built walled port city and castle on a hill (frame: overlook).
5. Dialogue/throne-room scene.
6. Melee and parry, the Pit descent, the charge.
7. Chapters wired into a quest/story state machine, saves, polish.
