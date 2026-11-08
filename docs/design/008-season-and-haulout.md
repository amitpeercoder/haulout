# 008 — The season, the haulout and the sale

**Date:** 2026-11-08. **Decided by:** Amit (product). **Status:** adopted with proposed defaults; the open questions below are for Amit to overrule.

## Question

The slice promises "buy a tired 28-footer, get it through one season, sell it" (CONCEPT.md). After 007 we have the spring and one trip. What is the rest of the season, and what does it mean to come out the other end?

## Decisions

### 1. The season is the same sim, longer
No new fault system, no season script. A season is the existing tick running from launch to haulout with three things the sim does not do yet: **the hull fouls** afloat, **the autumn weather turns** (the series already has Nordic curves; gales and frost become real risks on a mooring), and **the yard has a calendar**. Everything a player does or defers afloat compounds into the haulout and the winter through the rates and cascades of 006.

### 2. More places to go
Two more ports in data, so a season has trips in it, not one trip: a longer passage and a sheltered anchorage. Same route model as 007 (distance, course, shelters). Each port has its own arrival lines. Placeholders: **Storholmen**, 26 nm from Skjærvik on 300°, a small harbour with a shop like Hestholmen's; **Kvitnes**, 9 nm from Hestholmen on 120°, an anchorage with no shop at all. Trips remain text; the chart view is still a later UI round.

### 3. Fouling
Antifouling is a component with a condition. Afloat, growth accrues on the hull at a rate driven by water temperature (from the series) and antifouling condition, with the usual seeded spread. Growth costs speed under sail and under engine (a knot or so when heavy), raises the engine load and so the impeller shed, and is visible: `Look` over the side at a mooring reports the waterline ("a green beard at the waterline"), and the haulout shows the whole underwater body. Cleaned on the hard (`Clean hull`, hours, a yard option) or scrubbed afloat in part (`Scrub`, at anchor or at a mooring, slow, never complete). Antifouling itself is painted on the hard before launch (already in the survey's list of deferrable items; this round gives it its consequence).

### 4. The yard has a calendar
The yard lifts boats on booked days. `BookHaulout(day)` reserves a slot; the yard's autumn is busy, so the slots on offer depend on when you ask: book in July and the week you want is open; ask in late September and the next free slot is weeks away. Slots are drawn seeded from a window (placeholder: haulout season day 255–300; the first free slot moves later by a seeded 0–7 days for every week after day 240 the player waits to book). Changing a booking costs a fee. Missing your slot (at sea, or she will not start to motor to the crane) forfeits it. The lift costs the planned price (placeholder 7 000 NOK lift plus 9 000 NOK winter storage on the hard, both billed at the lift); the emergency lift of 005 stays three times the planned one and still takes a slot.

### 5. Autumn on a mooring is a risk
After day 270 the series' gales (force 7+) are common. Afloat on a mooring in a gale the boat takes exposure as today (rain, wind through an open hatch), and the **mooring** becomes a component: a gale day adds chafe to the lines at a rate by force; a chafed mooring in a second gale may part, which is a tow and a cosmetic repair bill, not a loss (chill, as 007). Looking at the mooring lines at the bow shows chafe; `Replace` the lines is cheap. This is the one new at-mooring cascade; it exists so that "I'll haul her next month" has a price the player can see coming.

### 6. Winter layup is tasks, not a cut-scene
On the hard after the autumn lift, the player can do the layup work, each a command with hours and a cost: **winterise the engine** (drain the raw-water side and run antifreeze through), **change the oil**, **fill the tank**, **take the batteries ashore to charge** (or leave them aboard with a charger), **fit the winter cover**. Each skipped task feeds an existing or new rate: raw water left in the engine on frost days wakes a cracked heat-exchanger or raw-water pump body (a spawn on `cooling.frostHoursWithWater`); a half-empty tank through temperature swings grows water in the fuel (the existing fault, spawned from `fuel.condensationDays`); a battery left to self-discharge in the cold reaches the existing `battery-low-charge` and, below a floor on frost days, a new `battery-dead`; no cover means rain exposure on deck through the winter at today's rates plus snow load on the sprayhood (cosmetic). None of these fires on a fixed day; all follow the rates with their spread and are diagnosable in spring by the same tools as today. Winter waiting: `WaitDays` as today, up to 60 at a time; a `WaitUntilSpring` convenience that waits to the next day 75 in steps, stopping if an alert is raised.

### 7. The valuation is the logbook
At any time on the hard the player may `ListForSale(askNok)`. The boat's worth is not a number the sim hides; it is computed from what a buyer can see and what their surveyor will find, and shown as a **valuation letter** from the broker: base value (placeholder 180 000 NOK, the insured value), less the yard cost of every fault the buyer's survey finds (the existing survey mechanic run by an independent surveyor on the day of the viewing: the same severity and detectability rules as the player's own surveyor, so a latent fault the player never found can still be found), less a deduction per undocumented year of engine hours, plus a premium for a **kept logbook** (counted maintenance entries this season, up to a cap) and for being sold **in spring** rather than autumn (placeholder 8 % seasonal swing, so wintering her costs storage but may pay). Buyers arrive over days seeded, with offers spread around the valuation and sensitive to the ask; the player accepts or waits. Each viewing's survey is shown to the player, so a sale attempt is also a free (and sometimes embarrassing) inspection.

### 8. The end of the season
`AcceptOffer` ends the game with a **closing card**: price, total spent, days, hours worked, trips made, tows, the two or three lines the logbook earned. Every later command is refused as after a sinking; queries answer. `Keep` is the alternative: the game continues into the next spring with everything carried: the clock, the faults (whatever the winter did), the deadline day 96 of the next year. Nothing is reset. This is how a second season starts, and the sim already supports it if the weather series and the clock run past day 360.

## Guardrails

- No score, no grade. The closing card states facts; the player judges.
- Everything the buyer's surveyor finds, the player could have found with the same tools. No hidden "true condition".
- Every number here is a placeholder in data and listed in the data's placeholders list. Domain facts (haulout season, winterising tasks, fouling rates, valuation practice) are flagged UNVERIFIED until Amit confirms.
- The web UI gets the season's actions through the existing panel and a season line in the status. No new drawing this round.

## Order of work

1. Sim: calendar and slots, haulout and the hull view, two ports, fouling, mooring chafe. Console words, MCP tools, tests.
2. Sim: winter layup tasks and their spawns, WaitUntilSpring, year roll-over (clock, weather, deadline).
3. Sim: valuation, listing, buyers, the sale and the closing card; Keep.
4. Adapters: console and MCP for all of it; playtest prompt gains the season goal (haul her out and sell her); web gets a Season tab (book, winter tasks, list, offers) with no drawing.

## Open questions for Amit

- Haulout season in the fictional archipelago: day 255–300 (mid-September to late October) with gales from day 270. Right order of magnitude for a Nordic yard?
- Planned lift 7 000 NOK plus 9 000 NOK winter storage, emergency lift 3×. Default stands unless you say otherwise.
- Winterising task list: raw-water drain and antifreeze, oil change, full tank, batteries ashore or on a charger, winter cover. Anything missing that a 28-footer's owner would call essential (stern gland, water tanks, heads)?
- Valuation: base 180 000, buyer's survey deducts yard price of findings, logbook premium, 8 % spring over autumn. Does a broker's letter feel right as the form?
- Fouling: a season's growth costing up to about a knot; scrubbing afloat partial. Fair?

## Implementation notes

Written by the lead before the work (see 007 for the form). Every number lives in data and is a placeholder for Amit.

**Date:** 2026-11-08. The one shape every developer builds to. Sections marked (done) are filled in as the steps land. Every number below lives in `sloop28.json` (or `weather.json`) and is added to the data's `placeholders` list with "(design note 008)". Domain claims are marked UNVERIFIED there too.

### Where the state lives

- New partials: `Play/Game.Season.cs` (the yard calendar, the lift, fouling, the mooring, the `Season` query), `Play/Game.Layup.cs` (winter tasks, the winter accumulators, `WaitUntilSpring`), `Play/Game.Sale.cs` (valuation, listing, buyers, the closing card, `Keep`, the `Sale` query). Rules records in `Data/BoatRules.cs`: `YardRules`, `FoulingRules`, `MooringRules`, `LayupRules`, `SaleRules` (init-property records with the numbers below as defaults, like `SeaRules`), passed to `Boat` as optional constructor parameters and deserialised straight from top-level blocks `yard`, `fouling`, `mooring`, `layup`, `sale`.
- `Game._launchDeadlineDay` (starts at `economy.launchDeadlineDay`) replaces every read of `Economy.LaunchDeadlineDay` in Game (briefing, status, readiness, `LaunchIn`, the launch card). `Keep` moves it.
- Seeds: nothing new draws from `_world` or `_random` (so no existing seeded outcome moves). Every new roll is `new SeededRandom(SeasonSeed(salt, a, b))` with `SeasonSeed = unchecked(worldSeed * 73856093 ^ salt * 19349663 ^ a * 83492791 ^ b * 2654435761)` and a distinct salt per purpose (1 yard drift, 2 fouling, 3 chafe, 4 spawn spread, 5 viewing, 6 buyer survey, 7 offer). Same inputs, same answer, whatever else the player did.
- Clock: `Day` is absolute and runs on past 360. `GameClock(Day)` gives year (0-based internally, "year 2" in words), day of year, month, season. `WeatherSeries.LastDay` becomes 1080 (three years); generation is sequential, so days 0 to 720 are unchanged for every seed. The launch deadline after a Keep is the next day whose day of year is 96.

### Data shapes (`sloop28.json`, all placeholders)

```jsonc
"trips": {
  "ports": [ /* skjaervik gains "fuel": true; hestholmen unchanged */
    { "id": "storholmen", "name": "Storholmen", "aliases": ["storholmen"], "yard": false, "shopName": "the harbour kiosk", "fuel": true,
      "stock": ["primary-filter-element", "secondary-filter-element", "impeller"], "priceFactor": 1.3, "arrivalLines": ["..."] },
    { "id": "kvitnes", "name": "Kvitnes", "aliases": ["kvitnes", "the anchorage"], "yard": false, "shop": false, "arrivalLines": ["..."] } ],
  "routes": [ /* existing first, so Depart() with no destination still means Hestholmen from Skjærvik and Skjærvik from Hestholmen */
    { "from": "skjaervik", "to": "storholmen", "distanceNm": 26, "courseDeg": 300, "shelter": [ { "name": "Måkeskjær", "atNm": 12 } ] },
    { "from": "kvitnes", "to": "hestholmen", "distanceNm": 9, "courseDeg": 300, "shelter": [] } ]   // Kvitnes lies 9 nm from Hestholmen on 120°
},
"yard": {
  "windowFromDayOfYear": 255, "windowToDayOfYear": 300, "driftFromDayOfYear": 240, "driftMaxDaysPerWeek": 7, "minLeadDays": 1,
  "liftNok": 7000, "storageNok": 9000, "changeFeeNok": 500, "washNok": 1200, "washMinutes": 120, "hauloutMinutes": 120,
  "autumnFromDayOfYear": 240        // a lift on or after this day of year is the autumn lift: storage is billed and she is laid up
},
"fouling": {
  "baseGrowthPerDay": 0.012, "waterLagDays": 20, "growthFromC": 6, "fullGrowthC": 15, "paintProtection": 0.9,
  "spread": [0.6, 1.6], "maxKnotsLost": 1.2, "loadPerGrowth": 0.5,
  "cleanMinutes": 240, "scrubMinutes": 120, "scrubFraction": 0.4, "paintNeedsCleanBelow": 0.1, "showFrom": 0.05,
  "waterlineWords": [ { "under": 0.05, "text": "" }, { "under": 0.25, "text": "a faint green film at the waterline" },
                      { "under": 0.5, "text": "a green beard at the waterline" }, { "under": null, "text": "a thick beard of weed, and barnacles where the paint has gone" } ],
  "hullWords":      [ /* the whole underwater body, at the lift and on Inspect hull */ ]
},
"mooring": {
  "startChafe": 0.2, "fromForce": 7, "chafeByForce": [0.25, 0.45, 0.7],   // index 0 = force 7
  "spread": [0.6, 1.6], "partAt": 1.0, "warnFrom": 0.5, "partedRepairNok": 4000,
  "chafeWords": [ { "under": 0.25, "text": "..." }, { "under": 0.5, "text": "..." }, { "under": 1.0, "text": "..." }, { "under": null, "text": "..." } ]
},
"layup": {
  "winteriseMinutes": 90, "antifreezeNok": 250, "fillMinutes": 30, "dieselNokPerLitre": 22, "fullAt": 0.9,
  "batteryMinutes": 30, "batteryRoomNok": 400, "chargerMinutes": 20, "chargerNok": 600, "coverMinutes": 120, "coverNok": 2500,
  "drawPerDay": 0.006, "frostHoursPerDegree": 3, "maxFrostHoursPerNight": 14, "condensationBelowNightC": 5,
  "freezeBelowCharge": 0.25, "snowBelowC": 1, "springDayOfYear": 75
},
"sale": {
  "baseNok": 180000, "undocumentedYearsAtPurchase": 4, "perUndocumentedYearNok": 3000, "perEntryNok": 600, "entriesCap": 12,
  "springBonus": 0.04, "autumnDiscount": 0.04, "floorFraction": 0.3, "foulingFindFrom": 0.2, "findChance": 0.85,
  "viewingChancePerDay": 0.2, "askSensitivity": 3, "seasonViewingFactor": { "spring": 1.3, "summer": 1.0, "autumn": 0.6, "winter": 0.6 },
  "offerSpread": [0.88, 1.02], "offerHoldsDays": 7, "listMinutes": 30, "broker": "Holm & Datter, yacht brokers"
}
// components (new): "mooring-lines" (deck, position "at-the-bow", reach on-deck visible+reachable, properties { "chafe": 0 }, replace via the
//   chandlery item "mooring-lines" 600 NOK / 30 min), "sprayhood" (deck, cockpit visible+reachable, step 2).
// hull gains properties { "fouling": 0 }. raw-water-pump gains { "waterHeld": 1 } (step 2). fuel-tank gains { "fuelLitres": 18 } (step 2; "litres": 40 is the capacity).
// spawn rules gain optional "spread": true (the threshold times a seeded spread), "repeats": true (a fixed spawned fault re-arms) and "cause" (a template).
```

- `Port` gains `bool Shop = true` (false: no shop at all; Buy refused "There's no shop at Kvitnes.", Replace with no spare refused "There's no shop at Kvitnes, and no spare X aboard.") and `bool Fuel = false`.
- Word bands (`waterlineWords`, `hullWords`, `chafeWords`) are `{ under, text }` lists read into a new `Band(double? Under, string Text)` record (null: anything above); the first band whose `under` exceeds the value wins.

### Facade additions

Commands (JSON name; console words; MCP tool):

| Command | JSON | Console | MCP |
|---|---|---|---|
| `BookHaulout(int Day)` | book-haulout | book haulout <day> / book <day> | book_haulout |
| `HaulOut()` | haul-out | haul out | haul_out |
| `Scrub()` | scrub | scrub | scrub |
| `WashHull()` | wash-hull | yard wash | yard_wash |
| `WinteriseEngine()` | winterise-engine | winterise | winterise_engine |
| `FillTank()` | fill-tank | fill tank | fill_tank |
| `BatteryAshore()` / `BatteryAboard()` | battery-ashore / battery-aboard | battery ashore / battery aboard | battery_ashore / battery_aboard |
| `WinterCharger()` | winter-charger | charger | winter_charger |
| `FitWinterCover()` | fit-winter-cover | fit cover | fit_winter_cover |
| `WaitUntilSpring()` | wait-until-spring | wait until spring / wait spring | wait_until_spring |
| `ListForSale(int AskNok)` | list-for-sale | list for sale <nok> / sell <nok> | list_for_sale |
| `AcceptOffer(int? Offer = null)` | accept-offer | accept [offer] [<n>] | accept_offer |
| `Keep()` | keep | keep | keep |

Clean hull is the existing `Clean("hull")`; change the oil is the existing `Replace("engine oil")`.

Queries: `Season()` (JSON `season`) → `SeasonView`; `Sale()` (`sale`) → `SaleView`.

Views (no ids anywhere; every name is the player's word for it):

- `SeasonView(int Day, int Year, int DayOfYear, string SeasonWord, string Text, bool Afloat, bool LaidUp, int LaunchDeadlineDay, int? BookedDay, int? FirstFreeDay, int SlotsFromDay, int SlotsToDay, int LiftNok, int StorageNok, int ChangeFeeNok, int EmergencyLiftNok, bool HauledOut, IReadOnlyList<LayupTask> Tasks, bool Listed, int? AskNok, int OpenOffers, bool Kept)`. `Text` is the season line (also `StatusView.Season`). `FirstFreeDay` null: the yard is full for the autumn (or the lifting season is over). `Tasks` empty until she is laid up.
- `LayupTask(string Name, bool Done, int Minutes, int CostNok, string Command, string Risk)`: Name "Winterise the engine", Command the console words ("winterise"), Risk what skipping it does in plain words.
- `SaleView(bool Listed, int? AskNok, ValuationLetter? Letter, IReadOnlyList<ViewingReport> Viewings, bool Sold)`.
- `ValuationLetter(int Day, string Broker, IReadOnlyList<ValuationLine> Lines, int ValueNok, int? AskNok, IReadOnlyList<string> Text)`; `ValuationLine(string Text, int Nok)` (signed: deductions negative).
- `ViewingReport(int Number, int Day, IReadOnlyList<string> Findings, int ValueNok, int OfferNok, int HoldsUntilDay, bool Open)`: Findings are the buyer's surveyor's sentences with their price ("Raw-water pump body cracked: 2 100 NOK at the yard."). Number counts viewings from 1.
- Observations: `HauloutView(int Day, int LiftNok, int StorageNok, string HullText, IReadOnlyList<string> Lines)` (HaulOut, and the autumn emergency lift); `ListingView(ValuationLetter Letter)` (ListForSale); `ClosingView(int Day, int PriceNok, int SpentNok, int BalanceNok, int Days, int HoursWorked, int Trips, int Tows, IReadOnlyList<string> Lines)` (AcceptOffer).
- `StatusView` gains `string? Season = null, bool Sold = false` at the end. `TripView` gains `IReadOnlyList<string>? Destinations = null` at the end (in port: "Hestholmen, 14 nm" per route out).
- `CommandResult` unchanged. After a sale every command is refused, free, "She's sold. The logbook is all that's left of her for you." (as after a sinking); queries answer.

### Command table (refusals are free and unlogged)

| Command | Where | Minutes | Effect / refusal (in order) |
|---|---|---|---|
| BookHaulout(day) | not at sea, anywhere aboard (a phone call) | 0 | books the day; a change costs `changeFeeNok`. Refused: at sea; past the window ("The yard has finished lifting for this year."); yard full ("The yard is full for the autumn: the emergency lift is all that's left."); day before `FirstFreeDay` ("The yard's first free slot is day N."), after the window, in the past; already booked that day; money for the change fee |
| HaulOut | at Skjærvik, afloat, cockpit, engine running, today is the booked day | hauloutMinutes | stops the engine, she is lifted (`_onTheHard`, laid up if autumn), lift + storage billed (may go below zero, like the tow), booking cleared, the hull view, observation `HauloutView`. Refused: at sea; YardGate; no booking ("You haven't booked a slot: book haulout <day>."); not the day ("Your slot is day N."); on the hard; not in the cockpit; engine not running ("Start the engine: you motor her round to the crane."); gale ("The yard won't lift in this: …") |
| Clean hull | on the hard, beside the hull (reach) | cleanMinutes | fouling := 0, the scraped-off line. Afloat it is refused by the reach as today ("…below the waterline…" via AfloatRefusal) |
| WashHull | on the hard at Skjærvik | washMinutes (away) | washNok, fouling := 0. Refused: afloat ("She's in the water: the yard washes her on the hard."), YardGate, money |
| Scrub | afloat, in port or at anchor (not under way), cockpit or on deck | scrubMinutes | fouling × (1 − scrubFraction). Refused: on the hard ("Clean the hull properly from the ground."), under way at sea ("Anchor first."), elsewhere aboard |
| WinteriseEngine | on the hard, companionway (engine reach as for the raw-water pump) | winteriseMinutes | antifreezeNok, raw-water-pump `waterHeld` := 0, engine stopped first. Refused afloat ("…while she's afloat the raw-water side stays full."), already winterised, money |
| FillTank | at a port with `fuel`, afloat or on the hard | fillMinutes | (capacity − fuelLitres) × diesel price, fuelLitres := capacity. Refused: at sea, no fuel here ("There's no fuel at X."), full, money |
| BatteryAshore | on the hard at Skjærvik, quarter-berth with the battery box lid off (reach) | batteryMinutes | batteryRoomNok; battery ashore (no draw, no self-discharge, kept full: live charge := 1 and Charge-fixed faults cleared when it comes back). Refused: afloat, already ashore, money |
| BatteryAboard | on the hard at Skjærvik, quarter-berth (reach) | batteryMinutes | battery fitted again. Refused: not ashore |
| WinterCharger | on the hard at Skjærvik | chargerMinutes | chargerNok; while on the hard the charger holds the battery full (Fill each step; Charge-fixed faults cleared at once). Disconnected at launch. Refused: afloat, battery ashore, already connected, money |
| FitWinterCover | on the hard, on deck | coverMinutes | coverNok the first time only (kept for later winters), cover on. Refused afloat, already on, money. Taken off and stowed at launch |
| WaitUntilSpring | not at sea | days | one day at a time (each an away `Tick` of 1440) to the next day of year `springDayOfYear`; stops after a day on which an alert was raised, or at the morning of a booked haulout day. Refused at sea |
| ListForSale(ask) | on the hard at Skjærvik | listMinutes | listed (or the ask changed), observation `ListingView` with the broker's letter. Refused: afloat ("The broker sells her from the hard."), YardGate, ask ≤ 0 |
| AcceptOffer(n) | anywhere not at sea | 0 | the open offer n (null: the best open one) is accepted: the sale closes, `ClosingView`, the game is over. Refused: not listed, no open offer, unknown or lapsed number |
| Keep | not at sea | 0 | withdraws a listing, moves the deadline to the next day of year 96, starts a new season for the logbook premium. Refused: kept already since the last launch |

Changed: `WaitDays` stops at the morning of a booked haulout day (same time of day, on that day; "You come back on day N for your haulout slot."). `Launch` takes the winter cover off, disconnects the charger, withdraws a listing (each with a narrative line), is refused while the battery is ashore ("The battery is in the yard's battery room: fit it first (battery aboard)."), clears laid-up, and draws the season's fouling spread. `EmergencyLift` on or after `autumnFromDayOfYear` is an autumn lift: storage billed, laid up, any booking cancelled, the hull view, `HauloutView`. `StartEngine` is refused while the battery is ashore. Any command naming the start battery while it is ashore is refused "The battery is in the yard's battery room.". `Replace("antifouling")` is refused while fouling ≥ `paintNeedsCleanBelow` ("Scrape the growth off first: clean the hull."). `Inspect hull` adds the hull words; `Inspect mooring lines` adds the chafe words.

### The calendar

- Window this year: absolute days `year·360 + windowFrom` to `year·360 + windowTo`. `FirstFree(today)`: before `driftFromDayOfYear`, the window's first day (but ≥ today + minLeadDays); after it, `max(windowFrom, today + minLeadDays) + drift`, where drift is the sum over each full week w = 1..⌊(dayOfYear − driftFrom)/7⌋ of a seeded integer 0..driftMaxDaysPerWeek (`SeasonSeed(1, year, w)`). Above the window's last day: full (null). Book in July and day 255 is open; ask on day 268 and the first slot is about two weeks off; ask in mid-October and the yard is full.
- Daily, at the end of the booked day if she was not hauled: today a gale (force ≥ `weatherGates.windForce`) → the booking moves to the next day, alert `slot-moved` "The yard couldn't lift in the gale. Your slot moves to day N."; else forfeited, alert `slot-missed` "You missed your haulout slot on day N; the yard has given it to the next boat. Book again: the first free slot is day M." (or "…the yard is full for the autumn.").

### Fouling

- `hull.State["fouling"]` 0 to 1. Daily, if she was afloat at the end of the day (in port, at anchor or at sea): `growth += baseGrowthPerDay · spread · tempFactor · paintFactor`, clamped at 1. `waterC` = mean `TempC` over the last `waterLagDays` days of the series (ending today); `tempFactor = clamp((waterC − growthFromC) / (fullGrowthC − growthFromC), 0, 1)`; `paintFactor = 1 − paintProtection · antifouling.Condition`. `spread` is drawn at each launch from `SeasonSeed(2, launchDay, 0)` in [lo, hi]. On the hard nothing grows (nothing falls off either).
- Speed: `knotsLost = maxKnotsLost · growth`, off motorKnots and the sail knots (≥ 0.5), in `SeaBefore` and `LegMinutes`. Load: the impeller shed per load hour × (1 + loadPerGrowth · growth).
- Seen: `Look` in the cockpit or on deck afloat, not under way, adds "Over the side: {waterline words}." when growth ≥ showFrom, and sets `_foulingSeen` to the growth. The lift's hull view and Inspect hull (on the hard) set it too. `PlanTrip` estimates with the speeds less the loss of `_foulingSeen` (what the player has seen), never the true growth.
- Cause on the lift: the hull view names it in the same voice as a cause line: "Under the waterline: {hull words}. It grew over N days afloat on {thin paint / fresh paint}, in water that reached N °C."

### The mooring

- `mooring-lines.State["chafe"]`. Daily, afloat in port (not at sea, not on the hard), wind force ≥ fromForce: `chafe += chafeByForce[min(force − 7, 2)] · spread`, spread from `SeasonSeed(3, day, 0)`. Then chafe ≥ partAt: she breaks her mooring: tow.nok + partedRepairNok + the chandlery price of mooring lines billed (below zero allowed), lines renewed (Replace on the component), a log line with the bill, alert `mooring-parted` "The harbourmaster rang: she broke her mooring in the night's gale and fetched up against the next pontoon. The towboat brought her back and she's on new lines; the bill is in the logbook." No loss.
- Arrival alert (after an away period, afloat): chafe ≥ warnFrom and it has grown since the player was last aboard: "After the blow the bow lines creak and saw at the fairleads. Worth a look on deck." Inspect mooring lines: description + the chafe words. Replace: the chandlery price (stocked at Skjærvik only); new lines start at chafe 0 (the property), the old ones at `mooring.startChafe` 0.2 (set at game start).

### The tick

```
Tick step: unchanged, plus in SystemsStep:
  battery ashore → no Electrical.Step / Run / pump power; winter charger on the hard → Electrical.Fill after the step
  winter cover on and on the hard → the water model gets rain 0
Daily(day): existing wear, progression, escalation; then SeasonDaily(day):
  fouling growth (afloat at the end of the day); chafe and parting (afloat in port, gale)
  layup (step 2): draw drawPerDay if laid up, battery aboard, no charger
                  frostHours: on the hard and raw-water-pump waterHeld > 0 and night < 0 → += min(maxFrostHoursPerNight, −night · frostHoursPerDegree)
                  condensationDays: on the hard, fuel < fullAt · capacity, night < condensationBelowNightC → += 1 − fuel/capacity
                  frozenNights: battery aboard, no charger, night < 0, effective charge < freezeBelowCharge → += 1
                  snowDays: on the hard, no cover, rain ≥ weatherGates.rainMm, TempC < snowBelowC → += 1
                  publish cooling.frostHoursWithWater, fuel.condensationDays, electrical.frozenNights, deck.snowDays
  the slot (moved or forfeited); buyers (step 3: offers lapse, then at most one viewing)
RainTerm(day) is 0 while the winter cover is on (it keeps the deck dry), so covered faults age without the rain.
```

Spawn changes (step 2): a rule with `spread: true` fires at `atLeast · s`, s from `SeasonSeed(4, hash(fault id), 0)` in `rates.spread`; a fault's `SpawnBaseline` (per `when`) is subtracted from the value (0 for data faults). A rule with `repeats: true`: when a spawned fault is Fixed, `Spawn` adds a dormant clone (`{id}#2`, `#3`…, same data, its own spread, baselines at the current values) to the component, so next winter can do it again. A rule's `cause` template ("The raw-water pump body has cracked. It came on after {n} hours of frost with raw water left in the engine.") is filled with `Number(round(value − baseline))` at the spawn and kept in `_pendingCause`, so the cause line is told when the player finds it (006 away-escalation path).

### New faults (step 2; all dormant, all with reveals for the existing verbs and yard/DIY routes, all `repeats`)

| Fault | Component | Spawn | Effect | Found by | Fix |
|---|---|---|---|---|---|
| raw-water-pump-cracked | raw-water-pump | cooling.frostHoursWithWater ≥ 24 · s | rawWaterFlowFactor 0.6, leak 0.5 L/h afloat | look (companionway, steps off): salt crust under the pump (hint); inspect: the crack (discovers); check flow: weak | replace (1 400) or yard |
| heat-exchanger-cracked | heat-exchanger | cooling.frostHoursWithWater ≥ 48 · s | rawWaterFlowFactor 0.7 | inspect: coolant weeping from a split (discovers) | replace (3 200) or yard |
| water-in-fuel-condensation | fuel-tank | fuel.condensationDays ≥ 40 · s | as water-in-fuel (diyDrainDays 3, atSea) | check filter bowl | drain on three days + new primary element, or yard |
| battery-dead | start-battery | electrical.frozenNights ≥ 2 · s | stateOfCharge cap 0, internalOhms × 25 | measure: collapses under the starter (discovers); inspect: bulged case (discovers) | replace (1 900) or yard |
| sprayhood-torn | sprayhood | deck.snowDays ≥ 4 · s | none (cosmetic) | look in the cockpit: sagging (hint); inspect: split seams (discovers) | replace (3 500) or yard |

### The sale (step 3)

- Valuation for a list of findings: `baseNok` − Σ finding prices − perUndocumentedYearNok · undocumentedYears + perEntryNok · min(entriesCap, maintenance entries this season), then × (1 + springBonus) in months 2–5 (March to June), × (1 − autumnDiscount) in months 8–1 (September to February), floored at floorFraction · base. A finding's price is its yard price, else the DIY parts, else costLater; heavy fouling (≥ foulingFindFrom) is a finding at washNok.
- `undocumentedYears` = undocumentedYearsAtPurchase + seasons of the player's ownership without an engine service entry (Replace of engine oil, oil filter, impeller, either filter element or the belt, or WinteriseEngine), the current season counted as undocumented until it has one. "Maintenance entries this season" are accepted Clean, Replace, yard-fix, the layup commands, Clean hull, WashHull and Scrub since the start of the season (game start, or the last Keep).
- The broker's letter (ListForSale, and `SaleView.Letter`): findings = the problems the player knows are open (never anything else), the lines in words, and the text "A buyer's surveyor will go over her for themselves."
- Daily while listed, on the hard: offers older than offerHoldsDays lapse; a viewing happens with chance `viewingChancePerDay · clamp(1 + askSensitivity · (letter − ask)/letter, 0.1, 2) · seasonViewingFactor` (roll `SeasonSeed(5, day, 0)`). At a viewing the buyer's surveyor goes over her: every present, not harmless fault with a discovering reveal for look, inspect, measure, check filter bowl or check flow is found with chance findChance (roll `SeasonSeed(6, day, fault index)`), plus the fouling finding. Found faults are Discovered for the player (with their cause line, as the player's own survey). Value V from those findings; offer = round(V · (lo + r·(hi − lo)) / 100) · 100 (roll `SeasonSeed(7, day, 0)`), capped at the ask. Log entry with the document "viewing N", alert `viewing` "The broker rang: a buyer looked her over today and offers N NOK, open until day D. Their surveyor's notes are in the logbook ('sale')."
- AcceptOffer: income += price (Balance = start − spent + income), the closing card: "Sold on day N for P NOK, to the buyer who viewed her on day M." / "You spent S NOK on her over D days, and H hours of your own work." / "Trips made: T. Tows: W." / up to three lines the logbook earned: the first arrival at a port other than home, the first cause line, the first tow line, in that order where present.

### Alerts (existing channel, once per kind per day)

`slot-moved`, `slot-missed`, `mooring-parted`, `chafe` (arrival), `viewing`. The winter faults speak through the existing alerts (battery voltmeter, weak flow) and their own arrival signs only where a sign is perceivable.

### Placeholders for Amit (also in the data's list)

Storholmen 26 nm on 300° with Måkeskjær at 12 nm, Kvitnes 9 nm on 120° from Hestholmen; the yard window 255–300 with drift 0–7 days per week after 240; lift 7 000, storage 9 000, change 500, wash 1 200; fouling growth 0.012/day at full temperature (6 to 15 °C), paint protection 0.9, a knot lost of 1.2 at full growth, +50 % impeller load; scrub 40 %; mooring chafe 0.25/0.45/0.7 per gale day of force 7/8/9, parts at 1.0, starts at 0.2, repair 4 000; every layup minute and price; layup draw 0.006/day; 3 frost hours per degree below zero (max 14); condensation below 5 °C nights; freeze below 25 % charge; snow below 1 °C; the spawn thresholds (24, 48 frost hours; 40 condensation days; 2 frozen nights; 4 snow days); the sale numbers.

### (done) Step 1 — calendar, lift, ports, fouling, mooring

**Built.**
- Data (`sloop28.json`): ports Storholmen (26 nm on 300°, shelter Måkeskjær at 12 nm, "the harbour kiosk", `fuel`, Hestholmen's stock at 1.3) and Kvitnes (`shop: false`; route Kvitnes to Hestholmen 9 nm on 300°, so Hestholmen to Kvitnes is 120°), arrival lines for both, `fuel: true` on Skjærvik; blocks `yard`, `fouling`, `mooring` with the note's numbers and the waterline, hull and chafe word bands; component `mooring-lines` (Hull subsystem, position "At the bow", on-deck visible and reachable, `chafe` 0) with the chandlery item `mooring-lines` (600 NOK, 30 min; the yard chandlery only); `hull` has `fouling: 0`; position `at-the-bow`; seven new `placeholders` lines, the domain claims marked UNVERIFIED. Records `Band` (with `Band.Pick`), `YardRules`, `FoulingRules`, `MooringRules` in `Data/BoatRules.cs`, `Port.Shop` and `Port.Fuel` (`Sells` is false without a shop); `Boat` takes the three rules as optional constructor parameters; the loader deserialises them straight from the data.
- `Game._launchDeadlineDay` replaces every read of `Economy.LaunchDeadlineDay` inside Game. `WeatherSeries.LastDay` is 1080; days 0 to 720 are pinned by a golden hash of 20 seeds taken before the change (`SeasonTests`).
- New partial `Play/Game.Season.cs`: `BookHaulout`, `HaulOut`, `Scrub`, `WashHull` (JSON `book-haulout`, `haul-out`, `scrub`, `wash-hull`), the query `Season` (`season`) giving `SeasonView`, observation `HauloutView`, `LayupTask` (defined, unused until step 2). `SeasonSeed(worldSeed, salt, a, b)` as specified (salts 1 yard drift, 2 fouling, 3 chafe); nothing draws from `_world` or `_random`. The calendar (`FirstFreeDay`), the daily slot check (gale: moved a day, alert `slot-moved`; else forfeited, alert `slot-missed`), `WaitDays` stopping on the morning of a booked day, the laid-up flag, fouling growth, the hull view, the chafe and the parting (alert `mooring-parted`, bill below zero allowed), the chafe arrival alert, the season line. `StatusView.Season` and `TripView.Destinations` added at the end of their records.
- Elsewhere: `Game.Sea.cs` (speed loss and impeller load in `SeaBefore`, speed in `LegMinutes`, `TripSnapshot` lists the routes out), `Game.Trip.cs` (`PlanTrip` uses `_foulingSeen`), `Game.Inventory.cs` (no-shop refusals, `ReplaceGate` also gates the antifouling), `Game.Lifecycle.cs` (Launch clears laid-up and draws the fouling spread; the autumn emergency lift), `Game.Alerts.cs` (`MooringArrivalAlert`), `Game.cs` (look over the side, Inspect hull and mooring lines, Clean hull, the catalogue). Adapters: console words `season`, `book [haulout|haul out] [day] <n>`, `haul out`, `scrub`, `yard wash` (`clean hull` goes through `clean`); MCP tools `season`, `book_haulout`, `haul_out`, `scrub`, `yard_wash` and the help footer; prose for `SeasonView`, the lift's bill line, `Routes out:` and the status season line.

**Decided where the notes were silent.**
- `SeasonView.Year` is 1-based (the first year is year 1); `DayOfYear` is 0-based like `GameClock`; `HauledOut` is "launched, and on the hard again"; `LaidUp` is the autumn-lift flag.
- Booking refusal order: at sea, past the window ("finished lifting"), yard full, day already gone by, day after the window ("The yard stops lifting on day N."), before the first free slot, already booked, change fee money. A booking is allowed on the hard too, before launch (a phone call).
- A wait that would pass the booked day stops at that day's morning (same time of day); a wait that starts on the slot day itself is not stopped, so waiting through the slot day forfeits it (or moves it in a gale), as the daily check says.
- The lift's storage is billed when the lift is on or after `autumnFromDayOfYear` (a December lift counts; a January one does not). The emergency lift keeps its money gate on the emergency price alone and bills storage below zero on top; the booking lapses with a narrative line.
- `HaulOut` refusal wording: no cockpit "You motor her round to the crane from the helm: go to the cockpit."; gale "The yard won't lift in this: {wind} (force N). If it blows all day, your slot moves a day."; on the hard "She's already ashore."; the refusal order is the note's (so on the hard with the wrong day says "Your slot is day N.").
- Fouling: growth counts every day afloat (at sea too); `waterC` is the mean air temperature of the series over the lag (there is no water temperature in the weather); the cause line counts days afloat and the warmest `waterC` since the hull was last clean; "thin paint" is antifouling condition under `paintThinBelow` 0.5 (new data key), else "fresh paint". `Clean("hull")`, `WashHull`: fouling := 0 and the cause counters reset; `Scrub` multiplies by `1 − scrubFraction` and keeps the counters.
- Seen: `_foulingSeen` is the growth when the player looked and it was at least `showFrom`, else 0 (they saw nothing); it is set by Look over the side, Inspect hull, the lift card, Scrub; it is set to 0 by Clean hull and the yard wash. PlanTrip says "(the growth you saw on her hull costs her about N knots)" when it is above 0. A Look in the cockpit or on deck afloat at anchor counts as "not under way".
- Scrub: also refused from below ("You scrub over the side: go up to the cockpit or on deck."), and under way says "Anchor first."; a scrub with nothing to take off still takes the 2 hours. Clean hull and Scrub shut the engine down first. The yard wash with nothing on the hull still costs the 1 200.
- Replace of `mooring-lines` does not roll a poor job (the poor-job templates are about paint). `Replace("antifouling")` is gated on the true growth, not the seen one.
- Chafe: the arrival alert is raised from `ArrivalAlerts` (an away stretch of a day or more) when the chafe is at least `warnFrom` and above what the player last knew; the knowledge moves on every aboard tick and on every midnight passed aboard. The parting bill (tow + repair + the chandlery price of new lines) is one logbook entry with the cost, plus the alert. The last chafe word band starts at 0.75 (the note's 1.0 would never show, the lines part at 1.0).
- Kvitnes: Buy refused "There's no shop at Kvitnes."; Replace with no spare "There's no shop at Kvitnes, and no spare X aboard."; the inventory view has `Shop` null there. An arrival still says "You pick up a mooring and stop the engine." (also at Kvitnes, where she anchors).

**Tests.** Sim tests 317 to 361 (44 new, in `Play/SeasonTests.cs`), Mcp tests 23 to 26 (one tool test, two prose tests). Existing tests changed, each by the intended change and nothing weakened: `WeatherTests.Same_seed_gives_identical_721_days` (renamed `Same_seed_gives_identical_days`; the count is `LastDay + 1`) and `Lookups_clamp_to_the_range` (clamps to `LastDay`, not 720) because the series is 1080 days; `BoatLoaderTests` 45 to 46 components (renamed `Sloop_has_46_components…`) for the mooring lines; `InventoryTests.The_inventory_lists_what_is_aboard_and_what_is_for_sale` 9 to 10 items at the yard chandlery (mooring lines); `PlanTripTests.The_wind_serves_by_force_and_angle` picks Hestholmen's route home by destination (`Single(r => r.To == "skjaervik")`, Hestholmen now has two routes out); `McpServerTests` `ExpectedTools` +5 and the tool count 43 to 48. Also the leak checks for the season views and the lift are in `SeasonTests`. Gale measurement, 20 seeds (1 to 20), days of force 7 or more on absolute days 270 to 330: min 0, mean 3.5, max 7 (per seed 4,3,5,1,0,2,3,7,5,3,7,1,4,5,1,5,6,6,0,2); in the yard window 255 to 300: min 0, mean 1.3, max 4.

**Not done / for the next developer.** Tasks, `Listed`, `AskNok`, `OpenOffers`, `Kept` in `SeasonView` are placeholders (steps 2 and 3); `StatusView.Sold` is not added yet. `Launch` does not take a cover off or disconnect a charger yet, and relaunching after a lift in the same year reports the original deadline. The launch deadline is still never enforced. A tow of a moored boat while the player is ashore and the seasonal winter water temperature (the weather has only air temperature) are not modelled. The MCP tool descriptions quote the numbers (7 000, 9 000, 500, 1 200, 2 hours) as text; the emergency lift is checked against the data by a test, these are not. Web: only keeps compiling; no Season tab. Unattended, she sinks about five weeks after launch (measured: launched day 100, drain hose off from day 133, on the bottom on day 135; the cockpit drain hose, 006), so a player who sails once and "keeps her afloat until September" needs the pump and the fix: a real season is only possible with that repaired, which is for the playtest to show.

### (done) Step 2 — winter layup, spawns, year roll-over

**Built.**
- Spawn mechanism (`Faults/Fault.cs`, `Data/BoatLoader.cs`, `Play/Game.Water.cs`): `SpawnRule(When, AtLeast, Spread, Repeats, Cause)`. A rule with `spread: true` fires at `atLeast` times a seeded factor in `rates.spread`, rolled from `SeasonSeed(worldSeed, 4, StableHash(fault id), 0)` (`StableHash` is FNV-1a over the id, so it is the same on every machine). `Fault.SpawnBaseline` (per `when`) is subtracted from the value. `repeats: true`: when a spawned fault is Fixed, `Spawn` adds a dormant clone `{id}#2`, `#3`… (`Fault.CloneDormant`: same name, reveals, routes, effects, spawn rules) armed from the values of that moment and with its own spread; the original is marked re-armed so it clones once. `cause` is filled with `Number(round(value − baseline))` (`{n}`, and `{s}` for a plural s) at the spawn and kept in `_pendingCause`, told when the player finds the fault and logged before "Found:". Existing data faults carry none of the three flags and behave as before (a test pins that only the five new faults use them).
- Data (`sloop28.json`): block `layup` with the note's numbers; `raw-water-pump` properties `waterHeld: 1`; `fuel-tank` `fuelLitres: 18` (capacity stays `litres: 40`); component `sprayhood` (hull subsystem, position Cockpit, visible and reachable from the cockpit, replace 3 500 NOK and 240 min); the five dormant faults of the note's table with reveals, consequences, diy and yard routes, `atSea` where it applies, spawn rules with `spread`, `repeats` and `cause`, and `note` UNVERIFIED; three new `placeholders` lines (layup numbers; fault thresholds, effects and prices; the UNVERIFIED domain claims). `LayupRules` in `Data/BoatRules.cs`, passed to `Boat` as an optional parameter. No chandlery item covers any of the new parts, so the replace prices are the components' own (pump 1 400, heat exchanger 3 200, battery 1 900, sprayhood 3 500); the fuel fault's diy is the existing primary element price (180). Yard prices: pump 2 100, heat exchanger 4 600, battery 2 600 (as battery-soaked), sprayhood 4 800 (7 days), tank 2 800 (as water-in-fuel).
- New partial `Play/Game.Layup.cs`: `WinteriseEngine`, `FillTank`, `BatteryAshore`, `BatteryAboard`, `WinterCharger`, `FitWinterCover`, `WaitUntilSpring` (JSON `winterise-engine`, `fill-tank`, `battery-ashore`, `battery-aboard`, `winter-charger`, `fit-winter-cover`, `wait-until-spring`), refusals free and unlogged. `ElectricalStep` (no self-discharge, run or pump power with the battery ashore; the charger holds a battery aboard full on the hard each step and clears Charge-fixed faults; running the engine sets `waterHeld` back to 1), `LayupDaily` (the draw, and the four accumulators, published as `cooling.frostHoursWithWater`, `fuel.condensationDays`, `electrical.frozenNights`, `deck.snowDays`), `UnderWinterCover` (rain 0 to the water model, `RainTerm` and the rain flag of the cause lines are 0), `SeasonView.Tasks`, the launch lines (cover off, charger unplugged), the launch refusal with the battery ashore, `StartEngine` refused with the battery ashore, and "The battery is in the yard's battery room." for every command or query that names it (`Reach` takes a set of components that are not aboard, so a look at the quarter berth no longer lists it). `EngineInputsBuilder` now reads `internalOhms` through `Properties.Effective` (needed for battery-dead's ×25; no other fault touches it).
- Adapters: console words `winterise`, `fill tank`, `battery ashore`, `battery aboard`, `charger`, `fit cover`, `wait until spring` / `wait spring`; MCP tools `winterise_engine`, `fill_tank`, `battery_ashore`, `battery_aboard`, `winter_charger`, `fit_winter_cover`, `wait_until_spring` and the help footer; prose for the task list in `Prose.Season`; catalogue entries.

**Decided where the notes were silent.**
- The accumulators and the layup draw count only while she is **laid up** (the autumn lift), not on every day on the hard; otherwise the spring boat of the first-fault game, on the hard in March with raw water in the pump, would crack on its first frost nights and every seeded test would move.
- A look hint only fires on first sight of a part, so a spawned fault never showed its Look hint (the pump was seen long before it cracked). `Look` now repeats the Look reveal of a Latent fault woken by a spawn rule (`RevealSpawnedByLook`; escalation successors and every other fault are untouched).
- `WaitUntilSpring` stops after a day on which an alert of a kind **not already raised the day before** was raised. An ongoing condition (a flat battery's voltmeter, raised every morning) stops it once, not on every day after, or the second call would stop after one day for ever. It also stops on the morning of a booked slot, and a sinking returns the loss card. From day 75 itself the target is day 75 of the next year. Called in spring it waits just the same (it is not refused when not laid up). The long-wait weather line is "In that time: N days of rain or snow, N days of gale and N nights of frost" (the one-week summary is for waits of a week or less).
- Refusal order: the yard gate (also "You're at sea"), afloat, already done, then where (reach), then money; for `BatteryAboard` the "aboard already" check comes first. `FillTank` is allowed afloat or on the hard at any port with `fuel`, from any place aboard; full is within half a litre of the capacity; the cost is rounded up. `WinterCharger` is charged every time it is connected (the note says `chargerNok`); `FitWinterCover` only the first time. The charger and the cover are allowed on the hard in spring too (they come off at launch).
- The battery alert gates (morning check, at sea, arrival) and `BatteryLow()` are off while the battery is ashore. The battery's live charge is filled to 1 and its Charge-fixed faults cleared when it comes back aboard (as the note says), not when it goes ashore.
- `Change the oil` has no fault behind it: the note's table has no oil fault. It is on the list, done when the engine oil was replaced after the autumn lift, and skipping it costs nothing yet.
- Cause texts are mine (UNVERIFIED like the rest); the fuel fault's "about N cold nights' worth of damp air" because the count is weighted by how empty the tank is.
- Dormant clones keep the original's `Spawn` rules, so a copy repeats in its turn. Replacing the pump resets `waterHeld` to 1 (a new part, a fresh component state), so a pump replaced in the middle of the winter is exposed again until the player winterises.
- Not done on purpose: the launch deadline is not moved and `LaunchView` still compares with the original deadline day.

**Tests.** Sim 361 to 407 (46 new in `Play/LayupTests.cs`), Mcp 26 to 28 (the layup tools and the prose of the task list; the tool-count and ExpectedTools lists carry the seven new tools, 48 to 55). Existing tests changed, each by the intended change: `BoatLoaderTests` 46 to 47 components (renamed `Sloop_has_47_components…`, the sprayhood) and `Every_fault_can_be_discovered` 20 to 25 faults; `McpServerTests` `ExpectedTools` +7 and the count 55; the tool-results prose test calls the seven new tools too. Nothing weakened.

**Measurement, 20 seeds (1 to 20, generated weather).** Hauled out on the booked day 285 and left untouched (no antifreeze, 18 of 40 litres, battery aboard, no cover, her first-fault battery cap left as it was) until day 435: all five woke in all 20 seeds. Day of the wake (min / median / max): raw-water-pump-cracked 293 / 312 / 325; heat-exchanger-cracked 299 / 318 / 327; water-in-fuel-condensation 334 / 363 / 402; battery-dead 360 / 361 / 363; sprayhood-torn 333 / 347 / 367. With all five tasks done (winterised, tank filled, oil changed, battery ashore, cover on): none woke in any seed.

**Not done / does not hold up in play.**
- battery-dead is not luck: the discharge (0.008 a day) puts the charge under a quarter on nearly the same day for every seed, so the spread (2 to 3 frozen nights) only moves it by three days (360 to 363). If it should be luck, the draw per day or the freeze charge should carry a spread.
- The pump and the heat exchanger wake after only two to six frost nights (24 and 48 hours at up to 14 a night), 8 to 40 days after the lift: an unwinterised engine is near certain to crack in a Nordic winter at these numbers. The thresholds are placeholders for Amit.
- The launch deadline after a spring in year two is the old one: the launch card says "339 days after the deadline", readiness shows a negative count. Step 3's `Keep` is meant to move it.
- A boat in the water through the winter, or one lifted before day 240, has no winter: nothing counts for her. No oil fault; no stern gland, water tanks or heads (the open question to Amit).
- `WaitUntilSpring` called in the first spring of the first-fault game is stopped by the old cockpit drain-hose sign on day 33; it is an alert like any other.

### (done) Step 3 — valuation, the sale, Keep

**Built.**
- Data (`sloop28.json`): block `sale` with the note's numbers plus `serviceComponents` (engine oil, oil filter, impeller, both filters, the alternator belt: replacing one is an engine service entry, and so is `WinteriseEngine`); three new `placeholders` lines (the valuation, the buyers, the UNVERIFIED valuation practice). `SaleRules` in `Data/BoatRules.cs`, passed to `Boat` as an optional parameter, deserialised straight from the data.
- New partial `Play/Game.Sale.cs`: `ListForSale`, `AcceptOffer`, `Keep` (JSON `list-for-sale`, `accept-offer`, `keep`), the query `Sale` (`sale`) giving `SaleView`; views `ValuationLine`, `ValuationLetter`, `ViewingReport`, `ListingView`, `ClosingView` and `StatusView.Sold`. Valuation exactly as the note: base, the findings at their price (yard, else parts, else costLater; heavy growth at the wash), undocumented years (4 at purchase plus every season of her ownership without a service entry, the current one until it has one), the logbook premium (accepted Clean, Replace, yard-fix, the layup commands, Clean hull, WashHull and Scrub since the season began, capped), the seasonal factor by month, the floor. The lines of the letter always add up to its value (the seasonal swing and the floor are lines of their own). The letter uses only the problems the player knows are open (`KnownOpenFaults`) and heavy growth only if they have seen it (`_foulingSeen`).
- Daily, listed and on the hard: a viewing with the chance of the note (roll `SeasonSeed(5, day, 0)`); the buyer's surveyor goes over every present, not harmless fault that has a discovering reveal for look, inspect, measure, check filter bowl or check flow (roll `SeasonSeed(6, day, fault index)` against `findChance`) and adds heavy growth without a roll; found faults go through `Discover` (cause line told, `Found:` lines written, `NewlyDiscovered` on whatever command was passing the time); the offer is the value of those findings times the spread (roll `SeasonSeed(7, day, 0)`), rounded to 100 and capped at the ask; logbook entry with the document "viewing N"; alert `viewing`. Nothing draws from `_world` or `_random` (a test compares every fault's spread with and without a listing, and runs the same seed under two different player luck streams).
- Income is `_incomeNok`; `Balance = start − spent + income`, `MoneySpentNok` is unchanged. After `AcceptOffer` every command is refused with "She's sold. The logbook is all that's left of her for you." (free, unlogged, like the sinking) and every query answers. The closing card has the note's lines; the counters are `_trips` (passages that ended in arrival, not turned back, not towed) and `_tows`; the earned lines are the first arrival at a port other than home, the first cause line the logbook told, and the first tow, each as "Day N: …".
- `Keep`: withdraws a listing, moves `_launchDeadlineDay` to the next day after today whose day of year is the economy's deadline day of year (96), starts a new season for the premium and the service count, "You keep her. The yard has her down for launch on day N."; refused once already since the last launch. `Launch` withdraws a listing (a narrative line and a logbook line) and clears the kept mark. `SeasonView.Listed/AskNok/OpenOffers/Kept` are filled and the season line says "She is listed for sale at N NOK, with an offer open." / "She's sold." The launch card, the launch summary, `LaunchIn` and readiness already read `_launchDeadlineDay`, so after a Keep they count against the moved deadline; tests pin that (launch on day 435 after a Keep from day 262: "21 days before the deadline", readiness 456 − day).
- Step 2's review fix: the layup draw is multiplied by a seeded factor in `rates.spread`, rolled once per layup from the day of the autumn lift (`SeasonSeed(8, liftDay, 0)`, recomputed rather than stored). 
- Adapters: console words `list for sale <nok>` / `list <nok>` / `sell [for|at] <nok>`, `accept [offer] [<n>]`, `keep`, `sale`; MCP tools `list_for_sale(ask_nok)`, `accept_offer(offer?)`, `keep`, `sale` and the help footer; prose `Prose.Letter` (the broker, the lines with NOK, the value, the ask against the value, the broker's words), `Prose.Sale` (letter, listing, each viewing with its findings and whether the offer is open or lapsed), the closing card (the narrative lines, then the balance and "The game is over; the logbook still reads."), the logbook shows "(sale)" after a viewing entry; catalogue entries and JSON types for all of it.

**Decided where the notes were silent.**
- Refusal order: `ListForSale` is gated by the yard (also "You're at sea."), then afloat, then the ask; `AcceptOffer` and `Keep` say "You're at sea." first. Listing again with a new ask is allowed (and takes the half hour again); offers already made stay open at the price they were made at. Listing is the player's own time (work) and shuts the engine down.
- The letter is available through `sale` whenever she is not sunk or sold, not only when listed, so the ask can be chosen against it; after the sale `Letter` is null.
- An offer is open through its `HoldsUntilDay` (day viewed plus 7) and lapsed the day after; the lapse is computed from the day, so the daily "lapse old offers" step has nothing to do. A launch or a Keep closes every offer.
- A viewing's `Day` is the day it happened (the day that ended at the midnight which ran it); its logbook lines are stamped that day at 12:00, the alert comes at midnight. The fixed hour is a convenience.
- The buyer's surveyor finds faults the player already knows too (they are present); a discovering reveal that needs a boot pulled back still counts, the surveyor pulls it. A harmless fault is never a finding.
- Maintenance entries are counted in `Accept`, per accepted command of the listed kinds, whatever it achieved (see below). Replacing from the spares counts. Yard fixes count, but they are not engine service entries.
- Trips are passages that ended in arrival; a turn back and a tow are not trips. Hours are rounded; "1 hour", "1 day". Prices are plain numbers, as everywhere else ("180000 NOK").
- `Keep` is allowed anywhere not at sea, afloat or ashore, laid up or not; the only limit is once per launch.

**Tests.** Sim 407 to 446 (39 new in `Play/SaleTests.cs`), Mcp 28 to 33 (two tool tests, three prose tests; `ExpectedTools` +4, 59 tools; the prose-without-ids test calls the sale tools too). Existing tests changed, each by the intended change and nothing weakened: in `LayupTests`, `A_battery_aboard_without_a_charger_runs_down…` (the expected loss uses the winter's draw factor), `Waiting_until_spring_stops_at_the_first_alert…` and `A_battery_left_aboard_runs_down_and_then_freezes_on_its_own_schedule` (their day ranges are computed from the factor instead of a constant 0.008 a day), and the measurement test gained an assertion that the flat battery wakes weeks apart.

**Measurement, battery-dead, 20 seeds (1 to 20, generated weather).** Hauled out on the booked day 285 and left untouched until day 435, as in step 2: before the fix 360 / 361 / 363 (min / median / max); now **341 / 360 / 385**, all 20 woke. The other four rows are unchanged (pump 293 / 312 / 325, heat exchanger 299 / 318 / 327, water in the fuel 334 / 363 / 402, sprayhood 333 / 347 / 367). With all five tasks done none woke.

**Lead-review fix (the farmed premium).** A maintenance entry now counts only when it changed something. `Replace` always counts (it always renews a part) and so do `yard-fix` and the layup commands; `Clean` counts only when it cleared a fault; `Clean hull`, the yard wash and `Scrub` count only when there was growth to take off (at or above `fouling.showFrom`). Those four call `CountMaintenance()` themselves in `Game.Clean`, `DoCleanHull`, `DoWashHull` and `DoScrub`; `MaintenanceActions` keeps the always-counted verbs. Tests in `SaleTests`: twelve cleans of a sound terminal add nothing beyond the one that fixed it; three replaces of sound drains add 1 800; scrubbing a clean hull afloat and cleaning one on the hard add nothing, while cleaning a fouled hull adds 600 once (a second clean adds nothing); the cap test now uses repeated replaces of the alternator belt.

**Does not hold up in play.**
- A fair ask sells her in about a week: in spring a viewing comes on roughly every second day (0.2 × up to 2 × 1.3), offers hold seven days, and the best open offer is almost always right to take. There is no haggling and no reason to wait except the season; the market has no tension yet.
- A listing is a free survey: the first two or three viewings discover about ten of the first-fault boat's faults (85 % each) and the cause lines, for nothing, where the surveyor costs 5600 NOK and a day. Listing at a low ask, reading the notes, then `keep` or launching withdraws it and leaves the player knowing everything. That is what the note asks for ("a free and sometimes embarrassing inspection"), but it undercuts the survey; a fee per viewing or a cap on how often a listing may be changed would restore the price.
- The logbook premium can be farmed: every accepted `Clean` counted, so twelve cleans of the negative terminal (six hours of work, nothing changed) were +7 200 NOK. **Fixed in the lead review** (see below).
- The first-fault boat is valued at about 172 000 NOK by a letter that knows nothing of its ten faults and sells for about 140 000 to 157 000 NOK once the buyers have been; the gap is the point, but a player who never inspects is surprised by 15 to 30 thousand, never by a hidden fault.
- Every viewing since the listing is printed by `sale`, lapsed ones too; after a long winter the report is long. `WaitUntilSpring` stops at each new viewing alert, as it should, but a listed boat then costs a call per viewing.
- The viewing hour (12:00) and the offer spread are the same for everyone; a buyer has no name, no character and no reason to walk away.

### (done) Step 4 — adapters, playtest, web

**Built.**
- Web: `PanelTab.Season` and a **Season** tab in `ContextPanel.razor` beside Here / Readiness / Trip: the season line; the yard (your slot, or the first free slot, or "full"; the window; lift, storage, change fee, emergency lift; a day input with **Book**; **Haul out** when it is the booked day and she is afloat); the hull (**Scrub** afloat; **Clean hull** and **Yard wash** on the hard); the winter work, each task with its minutes, price, risk, a Done mark and a button (winterise, change the oil as `Replace engine oil`, fill tank, battery ashore with **Battery aboard** and **Charger** beside it, fit cover); **Wait until spring**; the sale (the broker's letter lines, an ask input and **List for sale**, each viewing with its findings and offer, **Accept** per open offer); **Keep**. A closing card for `ClosingView` (kept in `GameSession.Closing` like the loss card). A season line (`StatusView.Season`) under the logbook header. `GameSession` has a method per command and `RunLayupTask(command)`. No new drawing, no ids.
- Help and briefing: the catalogue and the MCP `HelpFooter` already carried every new verb (steps 1 to 3); checked against the tool list. The briefing gains one closing line: "The season runs on past the launch, to the autumn haulout at the yard, the winter on the hard and a sale." Nothing pinned the briefing's length.
- Playtest: `prompt.md` keeps the Hestholmen goal as the first goal; then get through the season, haul her out in the autumn (book early), lay her up, sell her; DONE after the sale closes or a Keep; GAVE UP at 300 tool calls. `playtest.sh`: `--max-turns` 400 (room for thinking turns above the 300 calls), default timeout 1800 s. The MCP trace gains a `sold` field. `score.py`: the old `goal` (Hestholmen and back) is now `trip_goal`; new `hauled_out`, `listed`, `sold`, `kept`, and `goal` is hauled out and then sold or kept.
- Scripted season: `tools/playtest/season-run.txt` (console commands, ends with `quit`; deterministic, the same output every run).

**Season run (default game, seed 4).** Terminal cleaned, seacock, drain clamps, belt and impeller replaced, battery charged, booked day 262 on day 75, launched, to Hestholmen (water in the fuel at sea: filter replaced) and home, three days of draining the bowl, the summer in weeks, the mooring lines replaced on the chafe alert, hauled out on day 262, hull cleaned, the five winter tasks, listed at 165 000, an offer of 153 800 on day 264, accepted on day 265. See the report for what did not hold up.


### (done) Lead review

- Re-ran `tools/playtest/season-run.txt` through the console: identical output, engine fixed day 75, Hestholmen and back (water in the fuel bit both ways and was answered at sea), the chafe warning on day 99 answered with new lines, hauled out on the booked day 262 (lift and storage 16 000 NOK, the hull view of 187 days on thin paint), the five winter tasks, listed at 165 000, sold on day 265 for 153 800. The run does not winter her; the roll-over is covered by LayupTests and SaleTests (Keep to day 456, relaunch in year 2).
- Accepted: battery-dead now varies by seed (layup draw spread, salt 8); the logbook premium counts only work that changed something.
- Left for Amit / the next round: (1) mooring chafe applies to any gale afloat, so the lines can part in spring (week five of the run, about 10 600 NOK) though decision 5 frames it as autumn; (2) the pump-counter alert after rain through the open hatch sends the player to the floorboards, where nothing says the water came from the hatch: a diagnosability gap from 006 that the long summer waits now expose; (3) the engine-start line says "grey March light" in every month; (4) readiness keeps showing the launch deadline after launch; (5) a listing is a free, near-complete survey and a fair ask sells within days, so the market has little tension; (6) the emergency lift stays 9 000 NOK, not three times the new 7 000 lift, because 21 000 would make a spring lift prohibitive; (7) fuel is not consumed, so the tank stays full after one fill.

### (done) Second pass — review fixes

**1. The pump counter and the open hatch.**
- `WaterSystem` now sums the litres that came in since the player was last aboard by source: the open hatch (`rainThroughOpenHatchLitresPerMm`), rain through components, seawater through leaks and spray (`HatchLitresSinceAboard`, `RainLitresSinceAboard`, `SeaLitresSinceAboard`, `HatchDominant` = the hatch let in more than the other two together). `ResetSinceAboard()` clears them with `CyclesSinceAboard` (every aboard tick and every read of the counter).
- `Game.Alerts.cs` `PumpCounterAlert()`: hatch dominant, kind `pump-counter-hatch`, "…gone up by N since you were last aboard. You left the companionway hatch open; rain has been finding its way below."; otherwise the old text and kind `pump-counter` (floorboards).
- `WaitUntilSpring` stops only on an explicit set (`NeedsHands`): a `sign-*` arrival sign, `mooring-parted`, `chafe`, `sole`, `bilge-smell`, `battery`, `pump-counter`, `viewing`, `slot-moved`, `slot-missed` (and the booked-slot morning, as before). `pump-counter-hatch` is not in it.
- Because the winter wait ticks day by day, the counter would have been read and reset every morning (an alert a day, each saying "since you were last aboard" and counting one day). The wait now holds the counter (`_holdPumpCounter`): a counter the floorboards explain is read at once (it wants hands), the hatch one is read once, when the wait ends, with the whole winter's count. An alert that came every morning (a flat battery, water over the sole) is told once in the result (`DistinctBy` text); the logbook keeps every one.
- "You leave her for the winter." adds "The companionway hatch is open: slide it shut before you go if you want her dry below." when the hatch is open.

**2. The month.** The only fixed month in narration (Briefing aside) was the engine start text, "grey March light". `EngineModel.Attempt(inputs, monthName)` takes the month as a parameter (null: "grey light"); `Game.DoStart` passes `GameClock.MonthName` (new, January to December, 30-day months). Tested for March, September and December.

**3. Readiness after launch.** `ReadinessView` gains the trailing `SeasonLead` (a sentence; a perceivable fact in the same voice as the season line). Once launched it replaces the deadline in `Prose.Readiness` and in the web Readiness tab header: "Haulout booked for day N." / "No slot booked: the yard lifts from day 255 to day 300; the first free slot is day M." / "…the yard is full for the autumn; the emergency lift is all that's left." / "…the yard has finished lifting for this year." / "Winter: 3 of 5 tasks done. Left: …" / "She's sold." Null before the first launch and after a Keep (the deadline line is unchanged there). Prose tests for each state.

**4. Status "Known:".** What it showed before this round (`git show HEAD:…`): every problem the player ever found, fixed ones included, as history; six tests pin "stays in the history", and the readiness list keeps "Done:". So the list stays and the fixed ones are now marked: `KnownFault.Fixed`, printed "Known: X (Component) - fixed". After the sale the list is empty (the status already says she is sold).

**5. Mooring chafe (data only).** `startChafe` 0.2 to 0.4 (the previous owner's lines), `chafeByForce` [0.25, 0.45, 0.7] to [0.05, 0.09, 0.14], `warnFrom` 0.5 to 0.25, `partAt` 1.0 and the spread unchanged. One gale day no longer parts new lines at all (a force-9 gale was 1.1 at the top of the spread). Measured over 20 seeds (1 to 20, generated weather, launched day 96, nothing touched, day by day to 270):

| | parted by day 270 | first chafe alert (days) |
|---|---|---|
| new lines | 1 of 20: seed 18 on day 174 | 7 seeds: 99, 119, 126, 155, 162, 168, 252; seed 18's came on day 119, 55 days before it parted |
| previous owner's lines | 1 of 20: seed 18 on day 170; **0 of 20 by day 130** | 15 seeds, 98 to 257 |

(The gale days from day 96 to 270 were 0 to 4 in 19 seeds and 10 in seed 18.) The old lines are now hardly a spring risk; if Amit wants them to be, raise `startChafe`. The warning is days, not weeks, if a gale blows for a week (seed 18 at the first calibration: 5 days); at 0.25 it comes after two or three gale days. The test pins: new lines at most 2 of 20, none in the first 30 days, a warning at least 14 days before each, old lines at most 5 of 20 by day 130.

**6. Fouling.** `baseGrowthPerDay` 0.012 to 0.008; waterline and hull words 4 to 7 bands (under 0.05, 0.15, 0.3, 0.45, 0.6, 0.8, then the top); new `fouling.compareStep` 0.04: a look over the side with the same words as the last look but more than 0.04 of growth moved says "…, more than when you last looked." / "…, less than when you last looked." (`_foulingLooked` is the last look; a scrub updates what the trip estimate uses but not the last look, so the next look after a scrub compares with the one before it). Bands (0 is clean, 6 the top, 20 seeds, launched day 96 on new mooring lines):

| day | thin paint | fresh paint |
|---|---|---|
| 150 | 0 to 2, mostly 1 | all 0 |
| 200 | 2 to 4, none at the top | 1 to 2 |
| 250 | 3 to 6, top in 4 of 20 | 2 to 3 |
| 270 | 4 to 6, top in 10 of 20 | 2 to 3 |

Maximum true growth, fresh paint, day 270: under 0.45. The knot loss (`maxKnotsLost` 1.2) and the impeller load (+50 %) at growth 1.0 are unchanged: 1.2 knots is "about a knot", and the top band starts at 0.8 (0.96 knots). The season run (launched day 75, thin paint) now shows the band below the top at the lift on day 262, where the old numbers were at the top by day 232.

**7. The season line.** It already said Winter in winter and Spring in spring; it had no year. It is now "Winter, year 1. …" (the sim's `GameClock` seasons: day 340 winter, 400 winter of year 2, 440 spring of year 2).

**Tests.** Sim 450 to 470 (20 new in `Play/SecondPassTests.cs`: 13 methods, two of them theories with 9 cases between them (the month and the season line)), Mcp 33 to 35 (readiness prose in each state; the status marking). Existing tests changed, each by the intended change and nothing weakened: `SeasonTests` (season-line strings gain ", year 1"; the chafe tests use the new start 0.4 and the new rates 0.05 and 0.09, parting is tested from 0.95; the look-over-the-side and lift tests use growth values inside the new bands), `LayupTests` (two season-line prefixes gain ", year 2"), `SaleTests` (the sold season line; the clean-the-hull test raises `baseGrowthPerDay` for that boat so that ten days afloat still leave growth to take off).

**Run files.** `tools/playtest/season-run.txt` unchanged and still reaches the sale: the same sale (153 800 NOK on day 265), the pump alerts now name the hatch, the readiness line leads with the booked slot, the lift's hull view names a lower band ("weed in tails along the hull", not the top), the status "Known:" marks six faults fixed and is empty once sold, the September start text. `tools/playtest/neglected-winter.txt`: the same season to the lift, no winter tasks, the hatch left open, three `wait until spring` (a fourth would wait another year), then the spring inspection. It stops on day 271 (the voltmeter), day 381 (the voltmeter again, water over the sole) and spring on day 435; the cracked pump (35 hours of frost), the split heat exchanger (61), the water in the fuel (about 44 nights' worth), the frozen battery (three nights) and the sagging sprayhood are all there to find.

**Not done / for the next round.** The status line still prints "Launch deadline: day 96" after launch (the readiness list and the season line no longer do). The voltmeter alert text changes a tenth of a volt a day, so a long wait still reports a handful of voltmeter lines (the `DistinctBy` is on the text). Gale days cluster, so a warning "weeks" before parting depends on the weather as much as on `warnFrom`.

**Follow-ups (same pass).**
- *One source of truth for the season buttons.* `SeasonView.Actions` (optional trailing `IReadOnlyList<SeasonAction(Verb, Label, Available, UnavailableReason, Minutes, CostNok)>`, 15 verbs: wait-until-spring, keep, book-haulout, haul-out, scrub, wash-hull, clean-hull, the six layup commands, list-for-sale, accept-offer). Each `Do*` now calls a `string? XRefusal()` (`BookRefusal(day?)`, `HaulOutRefusal`, `ScrubRefusal`, `WashRefusal`, `CleanHullRefusal`, `WinteriseRefusal`, `FillRefusal`, `BatteryAshoreRefusal`, `BatteryAboardRefusal`, `ChargerRefusal`, `CoverRefusal`, `WaitUntilSpringRefusal`, `KeepRefusal`, `ListRefusal(ask?)`, `AcceptRefusal(n?)`), the same one `SeasonActions()` reads, in the same order, so the reason shown is the reason given. Once sold or sunk every action says what every command says. Two refusals are new rules: `wait until spring` is refused in the season when she is not laid up ("It's already the season.": day of year `layup.seasonFromDayOfYear` 60 up to the yard's autumn day 240), and `keep` before the first launch ("You haven't had a season with her yet."). The web Season tab shows each button only when its action is available (the winter-task buttons, Battery aboard and Charger, Book, Haul out, Scrub, Clean hull, Yard wash, Wait until spring, List for sale, Accept, Keep); the yard, hull and letter blocks are unchanged. The oil change has no action of its own and stays shown while undone.
- *Status line.* `StatusView.SeasonLead` (trailing, the same sentence as `ReadinessView.SeasonLead`); `Prose` prints it in place of "Launch deadline: day N." once she is launched.
- *Tests.* Sim 470 to 478 (a theory over five states: day 75 on the hard, mid-summer afloat, laid up in the cockpit, beside the hull, on deck; for each of the 15 verbs a fresh game executes the command and the outcome must match `Actions` exactly, the withheld ones giving the stated reason, free and unlogged; plus the spring/keep reasons, the sold state, the status lead). Mcp 35 to 36. Existing tests changed by the two new refusals: `LayupTests` (the booked-slot wait starts on day 241 and books the first free slot; the day-75 wait is now refused with "It's already the season."), `SaleTests` (the Keep tests launch first or use the laid-up boat; the launch-card-after-keep test now goes through the autumn lift and the wait for spring), `McpServerTests` (keep over the wire before a launch is refused; its "kept her already" assertion is covered by the Sim tests, as it cannot be reached without a launch over the wire).

### (done) Harbourmaster

**The rule.** A plain `WaitDays` while she lies afloat at a port (not at sea, not on the hard, not laid up) is no longer blind (finding 3 in `002-agents-as-players.md`, the 60-day wait that sank her at the mooring). It runs a day at a time (`Tick(1440, aboard: false)`, as `WaitUntilSpring` does, in `Game.Harbourmaster.cs`) and stops after the day on which something rang. The slot-morning stop (`StopAtSlot`), the weather summary and the result shape are as before; on the hard (laid up or not) the wait is unchanged, and at sea it is still refused.

**The trigger and the alert are data.** `water.harbourmasterLitres` (default: `soleLitres`, shipped as 120) and `water.harbourmasterText` ("The harbourmaster rang: she's sitting low on her mooring. Worth getting down there."), both listed in `placeholders` for Amit. The bilge passing the level at any moment of a day, even for an hour, counts for that day (the pump may have brought it back down by midnight): `SystemsStep` remembers it while a mooring wait is on. The alert is kind `harbourmaster`. It names no fault; it is the same fixed text whatever is wrong, and finding the leak is still the player's work.

**What stops it** (`MooringStops`): the harbourmaster, `mooring-parted` (already a phone call), a new fault's arrival sign (`sign-*`), the `chafe` warning, `sole`, `viewing`, `slot-moved`, `slot-missed`. A kind that goes on day after day stops it once, as for the winter wait (`FreshAlert`). **What does not:** `pump-counter-hatch`, the plain `pump-counter` and the `battery` voltmeter, and also `bilge-smell` (a decision where the brief was silent: it is a smell you only meet aboard, and the harbourmaster's level is the sole; a deep bilge still shows in the harbourmaster's call or the pump counter). The voltmeter and the hatch counter are things the player reads on coming aboard: nobody rings a marina owner about a falling battery, and the hatch counter only tells them what they did. `WaitUntilSpring` still stops on the voltmeter and the plain counter (a winter on the hard has nobody ringing and the player must come back to a flat battery); this wait does not, because the afloat boat is watched.

**The narrative** reports the days actually waited: "You meant to leave her for 60 days; the phone rang on day 12." Day 12 is the day of the wait on which it stopped, counted from 1. For a stop that is not a phone call (a sign, the chafe warning) it says "you came back on day N: something on her wants your attention." The weather summary covers the days waited, the logbook says "Waited 12 days." (where the old line was), and `MinutesTaken` is what passed. A wait that runs its full span says "You leave her for N days." as before.

**Same outcomes as one long Tick.** Pump counter: held while the wait runs (`_holdPumpCounter`) and read once at the end, so the hatch counter is one alert with the whole stretch's count. The bilge smell and the voltmeter are also read once at the end (`_holdMorningReadings`), not every morning, so the logbook does not fill with a voltmeter line a day. Neglect's days-away count is `day - lastAboardDay`, independent of how the time is cut. Checked against the commit before (a throwaway test run on both trees): a 40-day wait that runs its span, with rain or without, hatch open, battery at 0.45, gave the same final day, minutes, alerts (text and day), logbook and pump counter. Where they differ is where the new rule is meant to: a boat whose drain hose fails mid-wait now stops on the day its sign arrives, where the old code ran on to day 133 and sank her.

**Tests** (`HarbourmasterTests`, 11): a failed hose with a dead pump stops after the first day, with the alert and the line, and she does not sink (a second wait does); a slow leak with the level preset stops on day 5, counted from the start; the level is read from data (20 litres rings, 120 does not); a dry boat waits 60 days; lines that part in a gale stop it that day; the hatch counter, the voltmeter and the plain counter do not; the voltmeter and counter are read once; on the hard nothing changes and at sea it is refused; the call names no fault; a 30-day wait and 30 waits of a day leave every fault's progress, longest-away, the clock and the pump counter equal. No existing test changed. The console run `tools/playtest/harbourmaster.txt` is the playtest's boat (drain clamps left unreplaced): the chafe warning stops it on day 21, then day 6, the hose's sign on day 1 and the harbourmaster on day 1, before the pump's burn-out has sunk her; another wait would.

**Finding 3.** Resolved as proposed in 002, widened a little: the call is not only the sole but any bilge level data names, and not only the water but the lines and the signs. The player still has to go and find the leak.
