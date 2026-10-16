# OgameX build API – what we need from the game

Reference for the build endpoints implemented in `OgamexClient` (`GetOwnPlanetsAsync`, `GetPlanetBuildStateAsync`,
`EnqueueBuildingAsync`) and parsed by `Ogamex/Parsing/BuildingPageParser`. Fixtures (real pages with ids replaced) are in
`tests/OgameSkaner.Core.Tests/Fixtures/building_*.html`. Implemented and tested live on 2026-10-16 (two planets).

| # | Information / action | Used by | Status |
|---|----------------------|---------|--------|
| 1 | List of own planets | `GetOwnPlanetsAsync` | ✔ (moon markup not seen yet) |
| 2 | Selecting which planet a request is about | all per-planet calls | ✔ `?planet=` (tested with two planets) |
| 3 | Building levels (resource buildings + facilities) | `GetPlanetBuildStateAsync` | ✔ |
| 4 | Current resources on the planet | `GetPlanetBuildStateAsync` | ✔ |
| 5 | Energy | `GetPlanetBuildStateAsync` | ✔ |
| 6 | Build queue contents | `GetPlanetBuildStateAsync` | ✔ one queue shared by resources and facilities |
| 7 | Server ids of the buildings | `BuildingCatalog.ServerId`, `EnqueueBuildingAsync` | ✔ |
| 8 | Adding a building to the queue | `EnqueueBuildingAsync` | ✔ (tested live 2026-10-16) |
| 9 | Things that can block an upgrade | planner / `EnqueueResult` | ✔ |

## Source: `GET /building/resource` (HTML, ~1650 lines)
Captured 2026-10-16, universe `genesis`, one planet. Raw copy kept outside the repo until we turn it into a fixture.
Useful parts, in page order:
- `<script>` near the top: `window.currentPlanetId = '<guid>'` and a JS object with **exact numbers**:
  ```js
  var resources = { initialMetal:28375000, initialCrystal:16000000, initialDeuterium:6073469.08…,
      metal: …, crystal: …, deuterium: …,
      energy: 13594.76…,             // total production
      energyConsumption: 4942.17…,   // total consumption
      darkmatter: 353830,
      metalProduction: 39325.43…,    // per second
      crystalProduction: …, deuteriumProduction: …,
      metalStorageCapacity: 28375000, crystalStorageCapacity: …, deuteriumStorageCapacity: … };
  ```
- Resource bar (`#metal-amount` etc.): the same values formatted with `.` thousands separators; the JS object is easier.
- One `div.building-detail[data-building-type="METAL_MINE"]` per building with
  `h3.building-level` "Level: 24", "Energy consumption/production" for the next level,
  "Required resources for level 25" (`span.resource-amount` per resource, the icon tells which resource),
  and `a.upgrade-btn[data-min-level="25"]` + `input.upgrade-input max-value="39"`.
- Compact list: `a.building-btn[data-building-type] > span.building-level` "24" and `a.upgrade-btn-small[data-tolevel="25"]`.
- "Buildings under construction" box: text "There is no building under construction." when the queue is empty.
- Bottom `<script>`: `window.remainingFieldsQueueIncluded = '262'` (free fields, queue included), `window.currentPlanet_Id`.
- Sidebar `#planet-selection` with the planet list (see 1).

## Source: `GET /building/facility` (HTML, ~1580 lines)
Same layout as `/building/resource`: same top script (`var resources`), same `building-detail` blocks,
same compact list, **same "Buildings under construction" box with the same items**, same planet sidebar.
Differences: title "Facilities - Home planet"; `window.currentPlanet_Id` is in backticks (`` `…` ``) instead of quotes;
page script `/assets/js/app/pages/buildingFacility.js` uses the same `/building/increasebuilding` / `createbuilding` calls and bodies.
Locked buildings (requirements not met, e.g. Nanite Factory needing Computer Technology 10, shown "8/10"):
no "Required resources" section, "Required technologies" instead, `a.upgrade-btn.disabled`, and `a.building-btn.disabled` in the compact list.

## Live tests (2026-10-16, test account, via the browser)
Requests sent exactly like the game's `PostRequest` (JSON, `__PlanetId` = current planet). All answers were HTTP 200.

| Test | Result |
|------|--------|
| `increasebuilding` into an empty queue | `{"IsSuccess":true,"Message":null,"ValidationErrors":null}`; item starts at once and its cost (911,648 / 227,912) is deducted immediately. |
| `increasebuilding` repeatedly (waiting items) | Each adds the next level; resources are not touched until the item starts. |
| `increasebuilding` when the next level costs more than the planet has (metal mine 36: 55.1M vs 45.1M) | **`IsSuccess: true` but nothing is queued.** The page shows the cost in red (`style="color:#d43635"`), `a.upgrade-btn.disabled` and `a.building-btn.disabled`. |
| `increasebuilding` on a locked building (nanite factory) | `IsSuccess:false`, `"First, the required technologies must be researched."` |
| Queue limit | **15 items = the running one + 14 waiting.** The 16th: `IsSuccess:false`, `"Construction queue is full."` |
| `createbuilding` `ToLevel` n levels ahead | One queue item per level. If the queue runs out of room it fills what fits and still reports success. |
| `createbuilding` across the resource limit (crystal mine → 38) | Queued 24…32 and stopped silently: it checks the **sum** of the levels in that one request against current resources (33 alone was affordable, the sum was not). |
| `cancelbuilding` `{BuildingType, Level}` | Success; cancelling a level in the middle also removes **all higher queued levels of that building**. |
| `GET /building/facility?planet=<current id>` | Normal page for that planet. |
| `GET /building/resource?planet=<unknown id>` | HTTP 200, the **current planet's** page – an invalid id is silently ignored. After switching, check `currentPlanetId`. |

Consequences for the client:
- Never trust `IsSuccess: true` alone: verify the queue grew (or check affordability first from the page: red cost / disabled button).
- `EnqueueResult`: "Construction queue is full." → `QueueFull`; disabled + red cost / silent no-op → `NotEnoughResources`; requirements message / anything else → `Rejected`.
- `Ogamex:BuildQueueCapacity` should be **15** (counting the running item), or 14 counting only waiting items.

## Also read from the pages (2026-10-16)
- Storage capacity: `metalStorageCapacity`, `crystalStorageCapacity`, `deuteriumStorageCapacity` in `var resources`.
  Resources stop growing at capacity (Home was at 101% metal / 100% crystal). The planner queues a storage at ≥ 70% fill.
- Free fields: `window.remainingFieldsQueueIncluded = '246'` – resources page only (not on the facilities page).
- Vacation mode: `window.isVacation = false;` near the top of every page.
- Moons: not seen yet. They have different buildings and will get their own automation; for now the parser skips sidebar
  links with a class containing "moon" and the client refuses a page without `METAL_MINE`.
- Research lab / shipyard upgrades do not block research or ship production on OgameX (user), so no special handling.

## 1. List of own planets
Need for each planet: **server id**, **name**, **coordinates**. Moons must be recognisable so we can skip them.
- The result is stored per universe in `own-planets.json` (`OwnPlanetService`); the Builder page has a refresh button and every build run refreshes it first.
- Findings: sidebar of every game page:
  ```html
  <div id="planet-selection"><span class="planet-info">[1/4] Planets</span>
    <div id="other-planets"><div class="planet-item selected">
      <a class="planet-select selected" href="?planet=<planet guid>">
        <span class="planet-name">Home planet</span><span class="planet-coords">[8:260:3]</span>
  ```
  Planet id = GUID from `href="?planet=…"`. "[1/4]" = planets owned / max. Moon markup not seen yet (no moon).

## 2. Selecting the planet
How does the server know which planet we mean?
- A query parameter on the page URL (e.g. `?cp=<id>`), a separate "switch planet" request, or the planet id in the POST body?
- Does switching change the *current planet in the browser too*? (It matters: the user keeps playing while the tool runs.)
- Findings (user): planet selection is **global server-side state**: opening any page with `?planet=<guid>`
  switches the current planet, and everything afterwards (also in the browser) happens on that planet.
  POST bodies also carry `__PlanetId`. `GET /building/resource?planet=<guid>` returns that planet's page in one request
  (tested with the only planet); an unknown id is ignored and the current planet is returned, so verify `currentPlanetId`.
  Consequence: while automation runs, the browser's current planet changes; we could switch back to the original planet at the end.

## 3. Building levels
Current level of every building in `BuildingType`: mines, solar plant, fusion reactor, storages (resources page) and robotics factory, shipyard, research lab, nanite factory (facilities page).
- One page or two? Do the pages show a level that already counts what's in the queue, or the real current level?
- Findings: resource buildings from `/building/resource`, facilities from `/building/facility` (`h3.building-level` / `span.building-level`).
  Levels are the **real current level** (queued items not included). With Metal Mine 24 and 25, 26 queued:
  `h3.building-level` "Level: 24", but "Required resources for level 27", `data-min-level="27"`,
  `upgrade-btn-small data-tolevel="27"` – the next-level cost on the page already accounts for the queue.
  `max-value` stays current + 15 (39).

## 4. Current resources
Metal, crystal, deuterium on the selected planet (resource bar at the top of every page?). Exact numbers, not rounded "1.2M".
- Findings: `var resources = {...}` in the page script (see Source). Storage capacities are there too.

## 5. Energy
The planner needs energy from sources **other than the solar plant** (fusion reactor, solar satellites):
`OtherEnergyProduction = total production − solar plant production`.
- Is total production / consumption shown (resource bar tooltip, resource settings page)?
- Findings: `resources.energy` (production) and `resources.energyConsumption`. `/building/buildinginfo?BuildingType=METAL_MINE`
  has a per-level table (production, energy consumption) whose energy values match our standard formulas
  (24 → 2,364; 25 → 2,709).
  The "Energy consumption/production" shown in each building block is **not** bonus-aware and is shifted by one level:
  with metal mine 24 (next 25) it shows 390 = formula(26) − formula(25); with the queue up to 26 (next 27) it shows
  498 = formula(28) − formula(27); solar next 8 shows 81 = formula(9) − formula(8).
  Only the totals (`energy`, `energyConsumption`) include research, officers and class bonuses.
  → The planner starts from the game's balance and adds base-formula differences for planned levels; bonuses mostly
  raise production, so this errs towards building the solar plant slightly early. Each state read re-anchors it.

## 6. Build queue
For every queue entry: **building**, **target level**, **started or waiting**. Plus:
- Is the 14-item limit shared by resource buildings and facilities? Does research/shipyard use the same queue?
- Does the page tell us the capacity, or do we keep `Ogamex:BuildQueueCapacity`?
- Findings ("Buildings under construction" box, captured with 9 items):
  - The running item: `div.container#firstConstruction[data-building-type="METAL_MINE"][data-tolevel="25"]`
    with progress `#building_percentage` (width %), remaining time `#firstConstructionRemainingTime` "05:18",
    and a DM complete button.
  - Every waiting item: `a.construction-queue-item[data-building-type="CRYSTAL_MINE"][data-tolevel="24"]`, in queue order.
  - Empty queue: `a.empty-text` "There is no building under construction."
  - So: queue = `#firstConstruction` (Started = true) + all `.construction-queue-item` (Started = false).
  - Capacity is not shown → keep `Ogamex:BuildQueueCapacity` (14).
  - Facilities share this queue: `/building/facility` shows exactly the same 9 items.
  - Resources between the two captures can't tell us when items are paid (production and new storage levels changed them);
    the user confirmed: checked against actual resources when added, paid when the item starts.

## 7. Building server ids
The id/name the server uses for each building in requests and page markup (e.g. `METAL_MINE`, `1`, …), to fill `BuildingDefinition.ServerId`.
- Findings: `METAL_MINE`, `CRYSTAL_MINE`, `DEUTERIUM_REFINERY`, `SOLAR_POWER_PLANT`, `FUSION_REACTOR`,
  `METAL_STORAGE`, `CRYSTAL_STORAGE`, `DEUTERIUM_TANK`.
  Facilities: `ROBOT_FACTORY`, `NANITE_FACTORY`, `SHIPYARD`, `RESEARCH_LAB`, `UNIVERSITY`, `TERRAFORMER`, `MISSILE_SILO`.
  Also seen: `MOON_BASE` (moon only).

## 8. Adding to the queue
- URL, method, body (JSON camelCase like the other endpoints, or form data?), headers, anti-forgery token if any.
- Responses for: success, **not enough resources**, **queue full**, anything else → map to `EnqueueResult`.
- Does it add "next level" of the building, or do we send the level?
- Findings (from `/assets/js/app/pages/buildingResource.js` and `PostRequest` in `/assets/js/default.js`):
  plain jQuery POST, `Content-Type: application/json`, no anti-forgery token, response JSON `{ IsSuccess, Message }`.
  The body uses **PascalCase** names (unlike our camelCase default, so it needs explicit names).
  - `POST /building/increasebuilding` `{ "BuildingType": "METAL_MINE", "__PlanetId": "<guid>" }`: next level (small "+" button).
  - `POST /building/createbuilding` `{ "BuildingType": "METAL_MINE", "ToLevel": 27, "__PlanetId": "<guid>" }`: several levels at once
    (the input allows up to current + 15).
  - `POST /building/cancelbuilding` `{ "BuildingType", "Level", "__PlanetId" }`; also `demolishbuilding`, `dmcomplete`, `byTradeIncreasebuilding` (not needed).
  - Refusals come back as **HTTP 200** with `IsSuccess: false`, e.g. (tested with the locked Nanite Factory, nothing queued):
    `{"IsSuccess":false,"Message":"First, the required technologies must be researched.","ValidationErrors":null}`.
    Messages follow the `lang` cookie, so map by known texts with a fallback to `EnqueueResult.Rejected`.
  - Still todo: the `Message` texts for not enough resources / queue full. Not testable without side effects right now
    (everything unlocked was affordable; queue-full needs 14 real items). Careful: `createbuilding` with a high `ToLevel`
    may only check the first level, so it is not a safe way to provoke the error.

## 9. Things that can block an upgrade
Not needed for the first version, but the server may refuse for these reasons and we should recognise them:
- No free planet fields.
- Requirements not met (e.g. nanite factory needs robotics factory 10 and computer technology 10).
- Research lab can't be upgraded while research is running; shipyard/nanite while the shipyard is building.
- Locked buildings are visible on the page: `a.upgrade-btn.disabled` and no "Required resources" section → the planner should skip them.
- Also: are the costs on the page the same as `BuildingCatalog` formulas (check a few)? If OgameX differs, we could read costs from the page instead.
- Findings: **costs differ from the standard formulas**: metal mine 25 = 911,647 / 227,912 (formula 1,010,046 / 252,511),
  crystal mine 24 = 499,532 / 249,768, deuterium synthesizer 11 = 17,143 / 5,042, solar 8 = 1,709 / 683,
  metal storage 4 = 67,500 (formula 8,000). Fusion reactor 1 = 900/360/180 matches.
  → use the page's next-level cost instead of the catalog formula.
  Checked 2026-10-16 (more levels): facilities follow the standard formulas (robotics 11 = 409,600/122,880/204,800,
  shipyard 12 = 819,200/409,600/204,800, research lab 9 = 51,200/102,400/51,200, missile silo 1 = 20,000/20,000/1,000).
  Storages: base 20,000 × 1.5^(level−1) (metal storage metal only, crystal storage half crystal, deuterium tank equal).
  Solar plant: base 100/40 × 1.5^(level−1). Mines: no clean formula found yet (ratio to standard varies per level).
  `remainingFieldsQueueIncluded` gives the free fields.
