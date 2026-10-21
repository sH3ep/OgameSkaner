# OgameX attack protection API

Seen on genesis on 2026-10-18 through the browser (`lang=en`). The Defense page (`AttackMonitor`) uses these endpoints.

## Event list: `GET /home/fleetmovementlist`
The header's "Missions" box (`#fleet-movement-detail-btn`) loads this HTML fragment (no `<html>`, ~4 KB per fleet) into
`#fleet-movement-content`. A whole page coming back means a login redirect. The fragment is one
`table.fleet-movement-table`, with one `tr` per fleet:

```html
<tr data-fleet-id="<guid>" class="row-mission-type-ATTACK row-fleet-return ">
  <td data-remaining-seconds="15" class="x-calc-fleet-time col-mission-time tooltip" data-tooltip-content="…18.10.2026 11:49:34…">00:15</td>
  <td><img src="/assets/images/V2/mission/ATTACK.png?v=2" class="tooltip" …></td>
  <td> Home planet <a href="/galaxy?x=8&y=260" class="fleet-source-coords">[8:260:3]</a></td>
  <td><span class="tooltip" data-tooltip-content="<ships and resources>">2.178.533</span></td>
  <td><a href="#" class="tooltip" …><img src="/assets/images/fleet-movement-icon-reverse.gif?v=2"></a></td>   (fleet-movement-icon.gif outbound)
  <td> 8-102 <a href="/galaxy?x=8&y=102&z=10">[8:102:10]</a></td>
  <td><a class="user_profile_icon tooltip_sticky" …></a></td>
  <td><a class="x_btn_fleet_return tooltip" data-fleet-id="<guid>" data-fleet-start-timestamp="1792315276"></a></td>   (own outbound fleets only)
</tr>
```

- **Mission**: `row-mission-type-<ID>`. The game's CSS knows `EXPEDITION DEPLOY COLLECT TRANSPORT COLONIZE RECYCLE ESPIONAGE
  HOLD ATTACK ACS_ATTACK DESTROY MISSILE_ATTACK ASTEROID_MINING`.
- **Direction**: `row-fleet-return` on the way back.
- **Time**: `data-remaining-seconds` counts from the response; the tooltip has the arrival in the display time zone.
- **Ships**: the count in the 4th cell; its tooltip lists ships and cargo. Names follow the language cookie.
- **Planet type**: the row has none, only the place's name before the coordinates. A moon is told from its planet by
  the name.

### Hostile and friendly fleets
No foreign fleet was in flight when the list was captured. The markup below comes from the game's stylesheet, so it's
**inferred**:

- `tr.row-hostile-mission` (dark red row) and `tr.row-friendly-mission` (green row, e.g. ACS defense).
- `.fleet-sender-player` holds the sender's name, `.fleet-source-coords` the coordinates it comes from (both red for
  hostile fleets), and `.col-acs-players-count` the players in an ACS attack.
- `#header .fleet-info-section.hostile` blinks the header box while a hostile fleet is listed, and
  `#planet-selection .planet-item.underAttack` blinks the attacked planet in the sidebar. Both are on every game page,
  so they're a fallback if the list's row markup turns out different.

`FleetMovementParser` reads the rows as the own ones are built, takes the relation from the row class and the sender
from `.fleet-sender-player`. When the first real attack shows up, check `tests/.../Fixtures/fleet_movements.html`
against it (set `Ogamex:DumpResponsesDirectory`).

## Escape
The escape uses the fleet endpoints from `ogamex-raid-api.md`:

- `GET /fleet?planet=<guid>` gives the planet's ships, their cargo capacity and its resources (`var resources`).
- The briefing `POST /fleet/fleetpage3data` with `TargetPlanetType 2` (moon) and mission Deploy (5) tells whether the
  moon exists (`AvailableMissions` contains 5), and gives the flight time and the fuel (`DeuteriumConsumption`).
- `POST /fleet/submitfleet` with `MissionType 5`, `SpeedPercentage` (10 = 10%) and `Metal`/`Crystal`/`Deuterium` as
  cargo. Solar satellites and crawlers can't fly and are left out.

## Turning a fleet back: `POST /fleet/returnfleet`
Found in the game's `default.js` (`ReturnFleet(d, e)`, called by the event list's `a.x_btn_fleet_return`); **not yet seen
on a real fleet**:

- Body (JSON, `application/json`, PascalCase): `{"FleetId": "<data-fleet-id>", "TS": <data-fleet-start-timestamp>}`. The
  button's script that passes `TS` isn't in `default.js`, so whether it goes as a number or a string is unknown.
  `OgamexClient.ReturnFleetAsync` sends a number and retries with a string on HTTP 400.
- Answer: `{"IsSuccess": true|false, "Message": …}` like `submitfleet`; the page reloads on success.
- Only own outbound fleets have the button (`FleetMovement.ReturnTimestamp`). A fleet turned back flies home in the
  time it has flown so far.

`AttackMonitor` uses it after an escape (`DefenseSettings.RecallAfterAttack`): once no attack on the planet is listed any
more, `RecallDelaySeconds` after the last one landed, it finds the escape in the event list (own Deploy from the planet
to the destination, landing within 2 min of the computed time) and turns it back. A fleet that landed first stays there.
