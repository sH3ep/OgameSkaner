# OgameX moon API

Read from the game (genesis) on 2026-10-19 on the account's moon at 8:260:3 (freshly created: nothing built). Used by
the Builder's **Moons** tab (`Services/Moons/MoonRunner`, `MoonPlanner`).

## Sidebar
```html
<div class="planet-item selected">
  <a class="planet-select" href="?planet=<planet guid>">
    <img class="planet-icon" /><span class="planet-name">Home planet</span><span class="planet-coords">[8:260:3]</span>
  </a>
  <a class="moon-select selected" href="?planet=<moon guid>"><img /></a>   <!-- no name -->
</div>
```
`BuildingPageParser.ParseMoons` takes `a.moon-select` (a class containing "moon") with its planet entry's coordinates;
the name falls back to "Moon". A moon is selected with `?planet=<moon guid>` like a planet.

## Moon pages
- `/building/resource?planet=<moon>` **redirects to `/building/Facility`**: moons have no mines and no storages.
- `/building/facility?planet=<moon>` has the usual `div.building-detail[data-building-type]` blocks for
  `MOON_BASE, ROBOT_FACTORY, NANITE_FACTORY, SHIPYARD, PHALANX_SENSOR, JUMP_GATE` (level, "Construction duration",
  "Required resources for level N"; a locked one shows "Required technologies x/y" and no cost), the same build queue
  box as planets and `window.remainingBuildingFields = \`1\`` (free fields, the queue counted: on a planet it equals the
  resources page's `remainingFieldsQueueIncluded`). `var resources` has the moon's metal/crystal/deuterium (0) and may
  leave out the storage capacities. `window.currentPlanetId` is the moon's id. The overview's header says "Overview - Moon".
- Upgrades use the planets' `POST /building/increasebuilding {BuildingType, __PlanetId: <moon guid>}`.
- Fixture: `tests/.../Fixtures/building_facility_moon.html` (built from this markup, ids replaced).

## Moon buildings
Every moon building costs twice its previous level (costs from the game's cost table, `/simulator/costtable`).

| Building | Level 1 cost | Duration at Robot 0 / Nanite 0 | Needs |
|---|---|---|---|
| Moon Base | 200,000 / 400,000 / 100,000 (each level ×2) | 31:06 | — ("each level makes 3 new fields") |
| Robot Factory | 400 / 120 / 200 | 00:01 | — |
| Nanite Factory | 1,000,000 / 500,000 / 100,000 | 1:11:06 | Robot Factory 10 |
| Shipyard | 400 / 200 / 100 | 00:01 | Robot Factory 2 |
| Phalanx Sensor | 200,000 / 400,000 / 200,000 | 35:33 | Moon Base 3 |
| Jump Gate | 2,000,000 / 4,000,000 / 2,000,000 (max level 48) | 5:55:33 | Moon Base 5 |

A fresh moon has **1 free field**. The planner assumes every level takes a field and a Moon Base level adds 3, and keeps
the last field for the Moon Base (check once a Moon Base level is built: if it doesn't take a field, the rule is only
cautious).

## Creating a moon
`/home/moonformation?planet=<planet>` (the overview's "Create a Moon around this planet" button), script
`/assets/js/app/pages/moonFormation.js`:
- Page script: `window.gravitonLevel`, `window.currentMoonDiameter` (0 = no moon yet), `window.minValueForDiameter`
  (1000), `window.maxValueForDiameter` (9999), input `#inputMoonDiameter`, button `#btnCreateMoon`.
- Cost (metal only, from the planet): `ceil(2·d·(d−900)·((d+10000)/10000)·√d·1.2^(d/200))` — 17,311,270 for 1,000 km.
- Graviton Research needed: `max(0, ceil((d−1799)/600))` (level 4 allows up to 4,199 km).
- Create: `POST /home/createmoon {"Diameter": d, "PlanetId": "<planet guid>"}` → `{IsSuccess, Message, Data}`, `Data` = the new
  moon's id (the game then opens `/home?planet=<Data>`).
- Enlarge (not used): `POST /home/expandmoon {"Diameter": d, "MoonId": "<moon guid>"}` for the difference in cost; the page
  then shows "Moon Expansion" and a 24 h cooldown.

## Transport to the moon
Mission Transport (4) with `TargetPlanetType` 2 (moon) at the planet's coordinates, from the planet (`SendFleetAsync`
with the planet as origin, the cargo in `FleetMission.Cargo`). The event list shows own TRANSPORT rows without the cargo.
