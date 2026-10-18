# OgameX asteroid mining

Captured on genesis (October 2026) and from the in-game wiki (`/Wiki/asteroidmining`).

## Game rules (wiki)

- Asteroids are at **position 17** of random systems, only in the galaxy where the player has the most planets.
- **Spawning:** each player gets 3–6 per hour, and each lives 1–2 hours.
- **Ownership:** an asteroid belongs to its player alone, and one mining removes it.
- **Who can mine:** only Asteroid Miners (`ASTEROID_MINER`) can be sent.
- **Possible results:**

  | Result | Chance |
  | --- | --- |
  | Nothing | 20% |
  | Dark matter (grows with the number of miners) | 15% |
  | Container | 5% |
  | Resources | 60% |

- **Resource amount:** proportional to the account's total hourly production. The miners bring at most their cargo capacity.
- **Daily decline:** each successful mining lowers the gains by 2% for the rest of the day, down to 40%. The count resets at 00:00 UTC.
- **Academy:** the talent `DISCOVERER_ASTEROID_MINING_LUCKINESS` raises the luck.

## Finding asteroids

### The hint
`GET /galaxy/Partial_AsteroidLocation` is the galaxy page's "Find asteroids" button. It returns an HTML fragment:

```html
<div class="galaxy-asteroid-modal">
    <div class="asteroid-modal-desc"> Our research indicates that there may be asteroids in the following locations.</div>
    <table id="playerAsteroidTable"><tbody>
        <tr>
            <td class="x-col"> <a href="/galaxy?x=7&y=1">[7:1:17]</a> </td>
            <td class="x-col" style="min-width:20px;"><i class="fas fa-question"></i></td>
            <td class="x-col"> <a href="/galaxy?x=7&y=65">[7:65:17]</a> </td>
        </tr>
        …
```

How to read it:
- Each row gives a system range: 48 to 64 systems wide, clamped at 1 and 499.
- Rows overlap and can repeat (two identical `[7:451:17] ? [7:499:17]` rows).
- We read each row as one asteroid. `AsteroidPlanner.Uncovered` matches every row to its own known asteroid; the rows left over are searched.

### The galaxy page
On the galaxy page (`/galaxy/galaxydata?x=&y=`), position 17 shows the asteroid, with its seconds left in `data-asteroid-disappear`:

```html
<span class="planet-index">17</span> … Asteroid belt …
<a href="/fleet?x=7&y=16&z=17&mission=12" class="btn-asteroid">
    <img src="/assets/images/mission/ASTEROID_MINING.png?v=4" … /> Asteroid
    <span style="color:#ffd700;" data-asteroid-disappear="402">(06:42)</span>
</a>
```

Without an asteroid it shows `<div class="col-asteroid"><span class="btn-asteroid-find x-find-asteroid"> Find asteroids </span></div>`.

## Sending miners

- **Mission:** id **12** (`data-mission-type="12"`, "Asteroid Mining").
- **Target:** the asteroid's coordinates with z = 17 and target type 1 (planet).
- **Briefing:** `/fleet/fleetpage3data` with `ASTEROID_MINER` answers `"AvailableMissions":[12]`, the flight time and `"CargoCapacity":11750`. The capacity is per miner with technology; the fleet page tooltip shows the same value.
- **Distance:** from another galaxy it is 20,000, the same as any other target.
- **Sending:** use `/fleet/submitfleet` as for any mission (see `ogamex-raid-api.md`).

## Mining reports

### Where they are
`GET /messages/messagedata?MessageCategoryType=FLEET_OTHER&page=n` (the "Other" tab, shared with colonization messages) returns the reports newest first.

### Markup
Each report is a `div.message-item` with `data-msg-template="FLEET_ASTEROID_MINING_REPORT"`. Its parts:
- `data-msg-id` is a GUID.
- The `head-left` holds the title "Asteroid mining report from [7:472:17]".
- The `head-right` holds the time `17.10.2026 21:35:50`, in the game's time zone.

A report with resources looks like this:

```html
<div class="message-content">
    <div style="width:100%;float:left;margin-top:10px;">
        <div> Some raw materials has been found on this asteroid. </div>
        <div …> Total cargo capacity of astro miners : 587.500 </div>
        <div …> <img src="/assets/images/resource/metal.png" …> : <span style="color:#9C0;">587.500</span> / 186.276.153.246 </div>
        <div …> <img src="/assets/images/resource/crystal.png" …> : <span style="color:#9C0;">0</span> / 72.440.777.586 </div>
    </div>
</div>
```

- Each resource line reads "gained / what the asteroid held". The cargo is filled with metal first.
- The asteroids seen held only metal and crystal, 260–290 billion together. That is far more than any miner fleet carries, so in practice the miners' cargo capacity limits the gains.

Other results are text only, for example `<div> A container has been found on this asteroid. </div>`. The parser reads them like this:
- A number in the text, or a dark matter image, counts as dark matter. No dark matter report has been seen yet.
- The word "container" counts as a container. This depends on the English labels.
- Anything else counts as nothing.
