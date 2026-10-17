# OgameX raid API (inactive farmer)

Captured live on genesis on 2026-10-16 through the browser (`lang=en`). The Raids page (`RaidRunner`) uses these endpoints.

## Galaxy rows: player status, id and rank
Source: `GET /galaxy/galaxydata?x=&y=` (HTML, one `div.galaxy-item` per position). The player column is spread over several lines:

```html
<div class="galaxy-col col-player">
<a onclick="ShowPlayerBadgeDialog('<player guid>');">
<span class="text-area isInactive7 tooltip_sticky" data-tooltip-position="right" data-tooltip-content="<div ...><span class='flag_sm flag-pl' ...></span> <span style='margin-top:6px;float:left;'>MoE</span></div>...<span ...>Ranking : </span><a class='galaxy-shortcut-action' href='/statistics?rel=<guid>'>909</a>...">MoE</span>
<span style="font-size:10px;">(<span class="isInactive7 tooltip" ...>i</span>)</span>
```

- The tooltip on the name span (on a single line) holds the full name. The visible text is cut off ("Disruptor Hecto...").
- The **rank** is the text of the `/statistics?rel=` link in the tooltip. It uses thousands dots ("2.471"). A lower rank means more points, so the raider takes candidates in rank order.
- **Status classes** on the name span:

  | Class | Letter | Row class |
  |---|---|---|
  | `isInactive7` | (i) | `filterInactive` |
  | `isInactive28` | (I) | |
  | `isVacation` | (v) | `filterVacation` |
  | `isNoob` | (n) | `filterWeak` |
  | `isStrong` | (s) | `filterStrong` |
  | `isProtection` | (p, "Empire protection") | |
  | `isBanned` | (b) | |

  `isInactive28` and `isBanned` weren't seen in 40 scanned systems; their names are inferred from the briefing flags `PlayerIsInactive28` and `PlayerIsBanned`. Everything else was seen. Active players have no class (`text-area  tooltip_sticky`).
- **Player id**: the GUID in `ShowPlayerBadgeDialog('…')` on the line before the name.

## Fleet page
`GET /fleet` (optionally `?planet=<guid>`, which switches the current planet like every page):
- `.info_item_active_fleets` → "Fleets: 1/11" (used/max), `.info_item_active_expeditions` → "Expeditions: 0/1".
- Ships: `#fleet1_content_container .ship-item` (only ships present on the planet). Each has an `a.ship-btn[data-ship-type][data-ship-quantity][data-ship-points]`. `data-ship-points` is the points per ship (cost / 1000).
  - The `data-tooltip-content` of `.ship-item` has "Cargo capacity: 200.000" (per ship, technology included) and "Total cargo capacity".
  - On genesis a Colony Ship carries 200.000, so capacities are far above the base values: always read them from here.
- Unit ids (shipyard):
  - Civil and cargo: `SOLAR_SATELLITE CRAWLER SPY_PROBE LIGHT_CARGO HEAVY_CARGO RECYCLER COLONY_SHIP ASTEROID_MINER`
  - Combat: `LIGHT_FIGHTER HEAVY_FIGHTER CRUISER BATTLESHIP BATTLE_CRUISER PLANET_BOMBER DESTROYER REAPER GALLEON DEATH_STAR FALCON AVATAR`
- Defense ids: `MISSILE_LAUNCHER LIGHT_LASER_TURRET HEAVY_LASER_TURRET ION_CANNON GAUSS_CANNON PLASMA_CANNON DORA_GUN PHOTON_CANNON SMALL_SHIELD_DOME LARGE_SHIELD_DOME ATMOSPHERIC_SHIELD FORTRESS DOOM_CANNON ORBITAL_DEFENSE_PLATFORM INTERCEPTOR INTERPLANETARY_MISSILE`.

## Missions
These are the `data-mission-type` values on the fleet page:

| Id | Mission |
|---|---|
| 1 | Expedition |
| 2 | Colonize |
| 3 | Recycle |
| 4 | Transport |
| 5 | Deploy |
| 6 | Spy |
| 7 | Hold (defend an ally) |
| 8 | Attack |
| 9 | ACS Attack |
| 10 | Destroy |

The old code called mission 5 "Hold"; it is Deploy.

## Briefing (read-only): `POST /fleet/fleetpage3data`
Body `{TargetX, TargetY, TargetZ, TargetPlanetType, SpeedPercentage, Ships:[{ShipType, Quantity}]}`. The current planet is the origin. Answer (nothing is sent):

```json
{"AvailableMissions":[4,8,6],"FleetOneWayTimeOnSeconds":15,"DeuteriumConsumption":2,"Distance":3650,"CargoCapacity":0,
 "PlayerId":"…","PlayerName":"MoE","PlayerRanking":909,"PlayerPoints":127914033447,"PlayerFleetPoints":0,
 "PlayerIsVacation":false,"PlayerIsBanned":false,"PlayerIsNoob":false,"PlayerIsStrong":false,"PlayerIsInactive7":true,
 "PlayerIsInactive28":false,"PlayerIsUnderProtection":false,"TargetPlanetName":"Colony","TargetPlanetId":"…",
 "ShowWarning":false,"WarningMessage":null,"IsSuccess":true,"Message":null}
```

- It gives the flight time, attackability (`8` in `AvailableMissions`) and the player's current status and fleet points.
- `Distance` follows the standard OGame formula. From 8:260 to 8:250: 2700 + 95·10 = 3650.
- `POST /fleet/fleetpage2data {Ships}` returns `CargoCapacity`, `MaxSpeed` and `FuelConsumption` for ships on the planet. For ships the planet doesn't have, it answers "The value(s) entered are invalid."

## Sending: `POST /fleet/submitfleet`
- Body: `{Ships, TargetX, TargetY, TargetZ, TargetPlanetType, SpeedPercentage, Metal, Crystal, Deuterium, MissionType}`, plus optional `HoldTimeOnMinutes`, `UnionFleetCode` and so on.
  - camelCase works.
  - **There is no origin planet**: the fleet leaves from the server-side current planet. `OgamexClient` therefore opens `fleet?planet=<origin>` and posts under one lock that every planet switch of the client takes.
- Answer `{"IsSuccess":true,"Message":null,"ValidationErrors":null}`; refusals have `IsSuccess:false` and a `Message`.
- Spying with N probes is `MissionType 6` with `SPY_PROBE × N`; tested with 20 probes. `POST /galaxy/sendspy {x,y,z,planetType}` sends the count set in the game options ("Sent 1 spy probe to [8:250:10]").
- `GET /fleet/fleetmovements` lists the own fleets: `a.btnReturn[data-fleet-id][data-fleet-start-timestamp]` with the tooltip's `data-one-way-flight` (seconds), and arrival and return times as text. The raider doesn't read it: it computes return times from the briefing.

## Espionage reports
- List: `GET /messages/messagedata?MessageCategoryType=FLEET_ESPIONAGE&page=1` (HTML fragment, newest first, "No messages found." when empty). Each report looks like this:

  ```html
  <div data-msg-id="<guid>" data-msg-template="FLEET_ESPIONAGE_REPORT" class="message-item msg-new ">
    ... Espionage report from <a href="/galaxy?x=8&amp;y=250">Colony [8:250:10]</a>.
    <span>16.10.2026 21:47:03</span>            (shown in the timeZoneOffset cookie's zone)
    Player: ... <span onclick="ShowPlayerBadgeDialog('<guid>');" class="msg-player-status isInactive7">MoE</span>
    <img src="/assets/images/resource/metal.png" ...> : 131.233.495.045     (crystal.png, deuterium.png alike)
    <span>Fleet: 0</span>   <span style="color:#F00;">Chance of counter-espionage: 100%</span>
    actions: <a href="/fleet?x=8&amp;y=250&amp;z=10&amp;planet=1&amp;mission=8" class="btn-msg-attack">   (planet=1 planet, 2 moon)
             <a onclick="SendPlunder('<guid>')">  <a class="btn-report-more-info" data-modal-url="/messages/spyreportdetail?reportid=<guid>">
  ```

- Detail: `GET /messages/spyreportdetail?reportid=<msg id>` (the same GUID as `data-msg-id`).
  - It contains `#spy-report-modal` with seven `div.expand-item` sections in a fixed order: Resources, Ships, Defenses, Buildings, Researches, Officers, Academy. Each has a `div.header` and a `div.content`.
  - A section the probes saw shows its **points** in the header, not a unit count. `Defenses (<span>2.000</span>)` with 1.000 Missile Launchers means 2 points each, cost / 1000. `RaidSettings.MaxDefensePoints` and `MaxFleetPoints` compare against these numbers, which also cover OgameX's own units.
  - A unit row is `<div><img src="/assets/images/V2/defense/MISSILE_LAUNCHER.gif"><span>Missile Launcher</span><span>1.000</span></div>`; the image is named after the unit id. Seen on a report with full information (5:288:3, 2026-10-16).
  - A hidden section has no number, and its content is only the sentence "We were unable to retrieve any reliable information of this type from the scan." Both 1 and 20 probes left Defenses hidden on 8:250:10.
- Other actions: `POST /messages/deletemessage`, `POST /messages/sendplundermessages {Id}` (the game's own "send plunder fleet"), `POST /messages/sendspymessages {Id}`.

## Ranking: `GET /statistics/statisticsdata?Category=PLAYER&SubCategory=DEFENSE&Page=<n>&Rel=`
Seen on genesis on 2026-10-17. The statistics page (`/statistics`) loads this HTML fragment when a category is clicked; the
categories are Points, Building, Research, Fleet and Defense (`SubCategory=DEFENSE`; the others' names weren't checked).
Without `Page` it opens on the page with your own player.

- 100 players per page, 34 pages on genesis. The pagination is `div.pagination` with `a[data-page-target="<n>"]` and a
  `select.x-pagination-select` (options `1-100`, `101-200`, …).
- Each `tbody tr` has 7 cells: rank, change (`-5` with a chevron), flag, status (`span.player-status.isInactive28` with
  `(I)`; the same classes as the galaxy), the player, the message button and the points:

```html
<td>
    <a href="/alliance/allianceinfo?allianceId=<guid>" class="alliance-tag tooltip" ...>[Euro]</a>   (only in an alliance)
    <a onclick="ShowPlayerBadgeDialog('<player guid>');">ALMIRANTE STORN</a>
</td>
...
<td style="text-align:right;padding:1px;">
    <div style="font-weight:700;font-size:12px;">5.054.976</div>
    <div class="x-points-change" ...><i class="fas fa-arrow-up"></i> 0</div>
</td>
```

- The player id is the same GUID as in the galaxy (`ShowPlayerBadgeDialog`), so the ranking joins the scanned planets.
- Points use thousands dots. Defense points are 1 per 1,000 resources of defenses, so **0 means no defenses**: the end of
  the defense ranking lists the players who are safe to attack (the last page on genesis: all 0, mostly `isInactive28`).
- `StatisticsPageParser` reads it; `RaidRunner.ReadDefenseRankingAsync` reads every page and stores the points.

## Loot
The share an attack takes isn't documented on the server. `RaidSettings.LootPercent` defaults to 50% (classic OGame).
