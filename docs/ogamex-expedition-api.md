# OgameX expedition API

Seen on genesis on 2026-10-18 through the browser (`lang=en`, fleet page and its `/assets/js/app/pages/fleet.js`). The
Expeditions page (`ExpeditionRunner`) uses these endpoints; the general fleet endpoints are in `ogamex-raid-api.md`.

## Slots
`GET /fleet` shows `.info_item_active_expeditions` → "Expeditions: 0/12" (used/max) next to `.info_item_active_fleets`
→ "Fleets: 1/11". An expedition takes one of each. `FleetPageParser` reads both (`FleetSnapshot.ExpeditionSlots`).

## Target
Position 16 of a system, planet type 1. The galaxy page's expedition button links to the origin's own system
(`a.btn-expedition` → `/fleet?x=8&y=260&z=16&mission=1`), and so does the runner.

## Briefing: `POST /fleet/fleetpage3data`
To `[8:260:16]` with one Spy Probe the answer was:

```json
{"AvailableMissions":[1],"FleetOneWayTimeOnSeconds":7,"DeuteriumConsumption":2,"Distance":1065, …,
 "TargetX":8,"TargetY":260,"TargetZ":16,"TargetPlanetType":1,"TargetPlanetName":null,
 "ExpeditionKeepValues":[60,120,180,240,300,360,420,480,540,600,660,720],"DefendHoldValues":null, …}
```

- `AvailableMissions` is `[1]` (expedition) only.
- `ExpeditionKeepValues` are the durations offered in minutes; the game shows them in `select#expeditionHoldTime`
  (values below 60 as minutes, so other classes may get e.g. 40). `FleetBriefing.ExpeditionDurations` holds them and
  `ExpeditionPlanner.Hold` takes the one nearest to the setting.

## Sending: `POST /fleet/submitfleet`
The usual body with `MissionType 1` plus **`HoldTimeOnMinutes`** (the chosen duration; the game's script sets it only
when the duration select is shown). `FleetMission.HoldMinutes` fills it; it's left out for other missions.

The return is at sent + 2 × one-way flight + the duration (events found on the way can change it).

## Event list
An own expedition is a `row-mission-type-EXPEDITION` row in `/home/fleetmovementlist` with the origin in
`.fleet-source-coords` and the target `[g:s:16]`. One row per fleet was seen (with `row-fleet-return` on the way back).
Whether the row stays while the fleet holds at position 16 wasn't seen, so the runner also counts what it sent until
the computed return.

## Moons
The account this was written on has no moon, so the sidebar markup of moons is **inferred**:
`BuildingPageParser.ParseMoons` takes the `?planet=` links of `#planet-selection` that `ParsePlanets` skips as moons
(a `moon` class on the link or its parents), with the coordinates of the planet entry they're in. Check it with
`Ogamex:DumpResponsesDirectory` once a moon exists. A moon is selected with `?planet=<moon id>` like a planet.

## Debris fields (Recyclers)
Seen on genesis on 2026-10-22 (galaxy pages of galaxy 4, the fleet page's script).

- **Galaxy page:** a row's `div.galaxy-col.col-debris` holds a `div.tooltip_sticky` icon (`galaxy-icon-kit.gif`
  sprite) whose `data-tooltip-content` reads "Debris field" and one `clearFix` line per resource: a div with the image
  `/assets/images/resource/metal.png` (or `crystal.png`) and a `span` with the amount (`4.956.800`). A metal line with
  `0` is still shown. `DebrisParser` goes by the images (labels follow the language). The header row's `col-debris`
  (`header-col`, text "DF") has no resources. Position 16's column also shows `div.fleetActionIcon.fleetActionFriendly`
  while own fleets are there. A debris field at position 16 itself wasn't seen yet; the fixture assumes the same icon
  next to the fleet icon.
- **Target type:** `window.planetType_DEBRIS_FIELD_Value = 3` on the fleet page (`PlanetType.DebrisField`), mission 3
  (Recycle). The briefing for Recycle to a debris field wasn't seen with Recyclers yet (the planet read had none:
  "Ships not found."), so the runner checks that `AvailableMissions` contains 3 before it sends.
- The galaxy page's own shortcut `POST /galaxy/sendrecyclers {X, Y, Z}` (`galaxy.js`) sends Recyclers from the current
  planet with a count the server picks; the runner doesn't use it, since it sends from a chosen origin with a counted
  number.
