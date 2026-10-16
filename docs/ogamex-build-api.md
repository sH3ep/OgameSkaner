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

## Shipyard and build schemas (2026-10-16)
- Shipyard page is **`/hangar`** (`?planet=` works). Solar satellite block `div.hangar-detail#detail-SOLAR_SATELLITE`:
  count in the page script `ships['SOLAR_SATELLITE'] = 300`, cost in `.required-resource-item` and
  `a.upgrade-max-btn[data-required-metal|crystal|deuterium]` (0 / 2,000 / 500), energy in the text
  "Every solar satellite on this planet produces <span>55.97</span> energy" (depends on the planet), production
  duration "00:06". Without a shipyard: "Required technologies" (Shipyard 0/1), no cost.
- Order ships: `POST /hangar/createship {"ShipType":"SOLAR_SATELLITE","Quantity":n,"__PlanetId":…}` → `{IsSuccess, Message}`
  (from `/assets/js/app/pages/hangar.js`; also `cancelhangarproduction {ProductionId}`, `dmcomplete`).
- Construction durations: "Construction duration : 06:57" in every building block, for the next level after the queue.
  Format from the game's `ConvertToTimeString`: `[Nd ][hh:]mm:ss` (hours only when > 0) → "06:57" is 6 min 57 s.
  Durations are not proportional to cost on OgameX (metal 25→27: cost ×2.13, time ×2.22).
- Production per second in `var resources` (`metalProduction` etc.); ×3600 matches the tooltip "Production per hour".
- User: when a queued item's turn comes and its resources are missing, the game **removes** it.

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

## Formulas for the optimizer (measured 2026-10-16, genesis, test account)
Used by `Domain/Optimizer/GameModel` and pinned by `GameModelTests`. Measured by queueing levels one after another and
reading each next-level cost/duration (then cancelling), by demolishing (instant on OgameX) and rebuilding the
Colony's Metal Mine and Deuterium Synthesizer, and by raising the Colony's Shipyard to 7.
- **Mine costs**: one curve for all mines, base × 1.48^(L−1) up to level 20, then ×1.46 per level for 21–30 and
  ×1.445 from 31 (checked up to 36). Bases (4/3 of standard OGame): metal mine 80/20, crystal mine 64/32,
  deuterium synthesizer 340/100. **Solar plant** 100/40 ×1.5, ×1.49 from 21. Storages 20,000 ×1.5^(L−1)
  (metal only / +½ crystal / +equal crystal). Robotics 400/120/200, Shipyard 400/200/100, Nanite 1M/500k/100k, all ×2.
  The game's rounding differs from ours by a few units at high levels (< 0.001%).
- **Construction time** = (metal + crystal) · h · k^level ÷ ((1 + R) · 1.05^R) ÷ 1.8^N, shown floored to whole
  seconds. R = Robotics, N = Nanite. h/k per building: mines and solar plant 0.0037086 / 1.01981, storages
  0.0036916 / 1.05, Robotics 0.0051155 / 1.05, Nanite 0.003937 / 1.05, Shipyard 0.004305 / 1.05. Fitted to 120
  durations (all within 0.1%). The Robotics term reproduces the user's Metal Mine times for Robotics 0–4
  (79/37/24/17/13 s). Nanite ×1.8 per level (Home: Metal Mine 25 336 s → 186 s with Nanite 1).
- **Production per second** with the account's bonuses: metal 25 + 6.5645833·30·L·1.1^L·f, crystal
  12.5 + 6.5645833·20·L·1.1^L·f, deuterium 6.5645833·10·L·1.1^L·0.721488·(1.44 − 0.004·Tmax)·f (no base income),
  f = min(1, energy produced / used). Measured with each mine demolished to 0 (base income) and at Colony's levels.
- **Energy**: consumption is the standard formula without rounding (10/10/20·L·1.1^L); solar plant
  20·L·1.1^L·1.464; a satellite gives (Tmax + 140)/6·1.464 (Colony: 54.67, so Tmax ≈ 84).
- **Storage capacity** (`/building/buildinginfo?BuildingType=METAL_STORAGE` shows the table, bonus included):
  11M, 17.6M, 24.06M, 31.21M, 39.50M, 49.64M, 62.79M, 80.82M, 106.68M, 145.08M, 203.44M, 293.51M for levels 0–11.
- **Solar satellites**: 0/2,000/500, built in the shipyard alongside the buildings; one takes ≈ 22.6 s · 0.887^Shipyard
  (23, 20, 17, 15, 13, 12, 11, 9 s for Shipyard 0–7; Home: Shipyard 11 with Nanite 1 = 6 s, so Nanite doesn't help).
  The hangar page unlocks them at Shipyard 1; the model requires Robotics 3 and Shipyard 3 as the user specified.
- `/building/buildinginfo?BuildingType=…` tables: production/energy per level (production without the speed factor),
  storage capacity per level.
- `POST /building/demolishbuilding {BuildingType, __PlanetId}` demolishes one level, instantly on this server.
- Requests in a tight loop trigger a Cloudflare "Access denied" page for about a minute: keep ≥ 1.3 s between requests.

## Account bonuses (read before every optimizer run, 2026-10-16)
`IOgamexClient.GetAccountBonusesAsync` reads three pages (`AccountBonusParser`); `GameModel` takes the result.
- **`GET /resource`** (resource settings, current planet): table `#resource-settings-content` with the rows Basic
  production, one row per building (with `select.factor-select[data-factor-type]`), then **Researches, Officers,
  Academy, Premium, Ranking bonus, Class bonus**, Production per hour/day/week, Storage capacity. Each bonus row is an
  absolute number per resource; divided by the mine's own row it gives the percentage (Colony and Home: research 4%,
  officers 30%, academy 55%, ranking 500% for metal, crystal and deuterium; energy: research 16%, officers 6%,
  academy 8%). Rows are recognised by position, not by their (translated) labels.
  - Formula (verified on Colony and Home within 0.01%):
    `per hour = (basic + mine · energy factor · (1 + research + officers + academy + premium + ranking)) · (1 + class)`.
    The increases add up; the class bonus (Miner +50%, "affects all bonuses") multiplies everything, basic income included.
  - Mine base = economy speed (2000) · 30/20/10 · L · 1.1^L · (1 + planet slot bonus); deuterium also
    · (1.202 − 0.004 · average temperature). Basic income = 30/15/0 · economy speed.
  - Energy: solar plant 24 · L · 1.1^L (1.2 × standard), satellite 0.26158 · (min temperature + 140); both
    · (1 + energy increases). Class bonus doesn't apply to energy.
- **`GET /officer`**: `div.officer-detail[data-officer-type]` with `.officer-level` "Level: 5/20" and `.bottom-section`
  "Each level provides: Increases construction speed by 10%." Used: `CONSTRUCTION_EXPERT` (construction speed),
  `TECHNOCRAT` (ship production speed), `STORAGE_EXPERT` (storage capacity). Geologist/Engineer show up in the
  resource page's Officers row.
- **`GET /academy`**: selected class `.class-info-item.selectedClass h3`; talents `div.academy-element-container`
  with the details in `data-tooltip-content`: title + `<span>(Level: 6)</span>` (no span = level 0), "+5% Construction
  speed" and `UpgradeTalent('MINER_CONSTRUCTION_SPEED')` (the id). Used: `*_CONSTRUCTION_SPEED` (+5%/level),
  `*_HANGAR_SPEED` (+5%), `*_STORAGE_CAPACITY` (+2%).
- **Speeds** divide the time like the Nanite Factory ("+80% production speed" = time ÷ 1.8, matching the
  measurements): construction time ÷ (1 + construction speed), satellites ÷ (1 + ship production speed). The
  calibration account had construction +80% (Construction expert 5 + Construction mastery 6) and ships +10%
  (Technocrat 1); those are divided out of the measured durations. Assumption: the construction speed bonuses add up
  (not verified – the levels can't be changed without spending dark matter).
- Storage capacity · (1 + storage increase) (the measured table had +10% from Storage mastery 5).
- **Premium** (`/premium`) is only a purchase form; nothing was active, so active construction/hangar/storage premiums
  aren't readable yet. Production and energy premiums are covered by the resource page's Premium row.
- **Planet slots** (wiki `/Wiki/colonization`): temperature range and production bonus per slot – 1: crystal +34%,
  2: +32%, 3: +30%; 4–6: metal and crystal +15%; 7–9: metal +30%; 10–12: metal +15%, deuterium +10%; 13–15:
  deuterium +20%. Overview (`/home`) shows the planet's own temperature ("Approximately 69°C to 74°C").
- Research tables (`/research/researchinfo?ResearchType=MINERAL_TECHNOLOGY`): Mineral/Crystallization/Fuel
  Technology give 4, 9, 14, 19, 25, 31, 38, 45, 53, 61% total for levels 1–10; Energy Technology 2% per level.
