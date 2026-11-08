# 007 — The first trip

**Date:** 2026-11-08. **Decided by:** Amit (product). **Status:** adopted.

## Question

The pre-launch game is complete (001–006). CONCEPT.md's slice needs "one trip goal, two or three emergent failures at sea". What is a trip, and how do deferred faults bite without a second script?

## Decisions

### 1. A trip is the same boat under load
No new fault system. At sea the tick runs hourly with two extra factors on the existing rates and systems: **sea state** (from the weather series: wind force → waves) and **engine hours**. Water ingress gains wave slop and spray; the fuel tank is stirred; the engine runs for hours instead of minutes; the battery is drawn and charged by the alternator. The cascades of 006 do the rest.

### 2. One destination for the slice
The trip goal from the onboarding chat: **the regatta party at Hestholmen**, 14 nautical miles across the sound, a Saturday in early summer (day 150 by default; the player may go any time after launch). Fictional archipelago, no chart view yet: the trip is planned and sailed as text and logbook in this round. The chart view is a later UI round (CONCEPT.md "Chart").

### 3. Planning
`PlanTrip(destination, departDay)` shows: distance, expected hours under engine and under sail (placeholder: 5 kn motoring, sail when wind is force 3–5 and not on the nose), the weather forecast for the window (the series is deterministic; the forecast shows the real days with a little noise, so it can be wrong), **readiness against this trip**: open faults whose consequence is at-sea, and the spares aboard. The player decides.

### 4. Spares and tools aboard
An inventory: `Buy(item)` at the chandlery (impeller, alternator belt, fuel filter elements, engine oil, hose clamps, fuses, a length of hose). Placeholder prices. A fix at sea needs the spare, the skill, and reach (engine work at sea only when hove-to or at anchor; the engine must be stopped). Sensible default kit listed in 005: impeller, belts, filters, oil, clamps, fuses.

### 5. Under way
`Depart()`; then the sim advances in hourly legs with a short prose line per hour (weather, sea, the boat), alerts as they come (temperature lamp, engine note change, bilge pump cycling, "fires and dies"). At any hour the player can: `Continue`, `HeaveTo` (stop, work on the boat; drift costs time), `Anchor` (only within a mile of shelter), `TurnBack`, `Sail` / `Motor`, `CallTow`. Engine work uses the existing commands; the engine must be stopped. Arrival at Hestholmen ends the trip: a closing paragraph, the party, the logbook.

### 6. Consequences, traced
Every at-sea failure comes from a fault the player knew or could have known, through the systems: overheat (impeller/seacock/strainer), fires-and-dies (water in fuel, stirred up), no charging (belt), flooding (hose, stern gland). Each writes a cause line. With the right spare and skill: a fix at sea and on you go. Without: sail if the wind serves, turn back, or **call a tow** (placeholder 6 000 NOK plus a day; reputation later). No lives at risk in the slice; no capsize; sinking at sea possible only through the water model and only with the pump dead.

### 7. After the trip
She is back at a mooring. The season continues (waits, work, further trips later). The autumn haulout is a later round.

## Guardrails

- No twitch sailing, no physics beyond speed-by-wind and the existing systems (CONCEPT.md out of scope).
- The forecast may be wrong, but the weather that happens is the series; both are logged.
- Chill: a bad trip costs money and a day; it is a story, not a game over.

## Order of work

1. Sim: trip plan, spares inventory, hourly legs with sea factors, at-sea commands, tow, arrival; alerts under way; cause lines. Console words for all of it; MCP tools; playtest prompt gains a sentence about the trip goal.
2. Web: a minimal "Trip" tab in the context panel (plan, depart, hourly log, the at-sea commands). No chart drawing this round.

## Open questions for Amit

- Default departure day and distance: day 150, 14 nm. Sensible for a first trip?
- Tow cost and whether it should depend on distance from port. Default: flat 6 000 NOK.
- Should the forecast be shown as a surveyor-style letter from the harbourmaster, or as a line per day? Default: a line per day.

## Implementation notes

**Date:** 2026-11-08. Written by the lead *before* the work, as the one shape every developer builds to. Sections marked (done) are filled in as the steps land. Every number below lives in data and is a placeholder for Amit.

### Where she is

- `Game._portId`: the port she lies at when not at sea (starts at `trips.home`, Skjærvik). `Game._passage`: non-null while at sea. `AtSea => _passage is not null`. At sea she is afloat; `_onTheHard` is false.
- The yard (FixByYard, Charge on shore power, OrderSurvey, EmergencyLift, Launch) is only at a port with `yard: true` (Skjærvik). A chandlery is at every port (`shopName`); `Buy` is refused at sea.
- Precondition to depart: afloat, at a port, not sunk, the engine has run once (`_finished`), the engine is running now (you motor off the mooring), wind below `rates.weatherGates.windForce`.

### Data shapes (`sloop28.json`, all placeholders)

```jsonc
"chandlery": [
  { "id": "remote-start-button", "name": "remote start button", "aliases": ["remote start", "remote", "button"], "priceNok": 400, "minutes": 30, "tool": true, "logText": "Bought a remote start button." },
  { "id": "impeller", "name": "impeller", "aliases": ["spare impeller", "raw-water impeller", "raw water impeller"], "priceNok": 350, "minutes": 15, "forComponent": "impeller" },
  { "id": "alternator-belt", "name": "alternator belt", "aliases": ["belt", "fan belt", "spare belt"], "priceNok": 220, "minutes": 15, "forComponent": "alternator-belt" },
  { "id": "primary-filter-element", "name": "primary filter element", "aliases": ["primary filter", "fuel filter", "filter element", "primary element"], "priceNok": 180, "minutes": 15, "forComponent": "primary-filter" },
  { "id": "secondary-filter-element", "name": "secondary filter element", "aliases": ["secondary filter", "secondary element", "fine filter"], "priceNok": 160, "minutes": 15, "forComponent": "secondary-filter" },
  { "id": "engine-oil", "name": "engine oil (4 L)", "aliases": ["oil", "engine oil"], "priceNok": 300, "minutes": 15, "forComponent": "engine-oil" },
  { "id": "hose-clamps", "name": "hose clamps (10)", "aliases": ["hose clamps", "clamps"], "priceNok": 120, "minutes": 15, "forComponent": "cockpit-drains" },
  { "id": "fuses", "name": "fuses (5)", "aliases": ["fuses", "fuse"], "priceNok": 60, "minutes": 15 },
  { "id": "hose", "name": "hose (2 m)", "aliases": ["hose", "2 m hose", "length of hose"], "priceNok": 250, "minutes": 15 }
],
"trips": {
  "home": "skjaervik",
  "ports": [
    { "id": "skjaervik", "name": "Skjærvik", "aliases": ["home", "skjaervik", "the yard"], "yard": true, "shopName": "the yard chandlery",
      "arrivalLines": ["..."] },
    { "id": "hestholmen", "name": "Hestholmen", "aliases": ["hestholmen", "the regatta", "the party", "regatta"], "yard": false, "shopName": "the harbour shop",
      "arrivalLines": ["... the party, the evening ..."] }
  ],
  "routes": [ { "from": "skjaervik", "to": "hestholmen", "distanceNm": 14, "courseDeg": 40,
                "shelter": [ { "name": "Lyngskjær", "atNm": 7 } ] } ],
  "tow": { "nok": 6000, "hours": 6, "lostHours": 18 }
},
"sea": {
  "stateByForce": [0, 1, 1, 2, 3, 4, 5, 6, 7, 8],
  "stateWords": ["flat calm", "a ripple", "small wavelets", "a short chop", "moderate waves", "rough water, whitecaps everywhere", "a rough sea", "a very rough sea", "a high sea"],
  "motorKnots": 5, "sailMinForce": 3, "sailMaxForce": 5, "sailKnots": [0, 0, 0, 4.0, 5.0, 5.5], "noGoDeg": 45,
  "heaveToDriftKnots": 0.5, "anchorWithinNm": 1, "towKnots": 3,
  "slopPerSeaState": 0.5, "sprayFromSeaState": 2, "sprayLitresPerHourPerSeaState": 2,
  "stirFromSeaState": 3, "stirHoursToBite": 2,
  "impellerShedPerLoadHour": 0.3, "overheatStopMinutes": 90,
  "underWayDrawPerHour": 0.05,
  "departMinutes": 10, "heaveToMinutes": 5, "anchorMinutes": 15, "turnBackMinutes": 2
},
"forecast": { "maxLeadDays": 3, "forceErrorChance": [0, 0.3, 0.5, 0.7], "rainFlipChance": [0, 0.1, 0.2, 0.3] },
// on a fault: what it means at sea, and the spare that answers it (a chandlery id, optional)
"atSea": { "text": "Under load for hours the engine will run hot.", "spare": "impeller" },
// on a component: live state the sea writes (Replace puts it back)
"impeller": { "properties": { "vaneFactor": 1 } }, "primary-filter": { "properties": { "waterMinutes": 0 } }
```

- `Shop.cs` loses its static catalogue: the chandlery is data (`Boat.Chandlery`). `Shop.RemoteStartButton` stays as the name constant. The return route is the route reversed (course + 180, shelter at `distance − atNm`).
- `atSea` on: impeller-tired (impeller), water-in-fuel (primary filter element), primary-filter-clogged (primary filter element), alternator-belt-slipping (alternator belt), drain-clamps-rusted (hose clamps), drain-hose-failed (hose clamps), seacock-weeping, seacock-seized, battery-low-charge, bilge-pump-burnt-out, exhaust-hose-cooked (no spare).

### Inventory

`Game._inventory: Dictionary<string itemId, int count>`. `Buy(item)`: at a port only; tools once ("You already have one."), spares stack; costs `priceNok`, takes `minutes`, logs. `Replace(component)`: if a spare with `forComponent == component` is aboard, it is used (count − 1, cost 0, narrative "…from the spares locker"); else at a port the part is bought as today (`replaceCostNok`); at sea it is refused, free: "You don't have a spare impeller aboard." (or "There's no spare for the X aboard." when nothing in the chandlery fits). `StatusView.Tools` = names of tools owned (unchanged). The remote start button is the `tool` item.

### Facade additions

- Queries: `PlanTrip(string? Destination = null, int? DepartDay = null)` (JSON `plan-trip`) → `TripPlanView`; `Inventory()` (`inventory`) → `InventoryView`; `Trip()` (`trip`) → `TripView`. Destination null = the one route out of the current port.
- Commands (JSON names): `Depart(string? Destination = null)` "depart", `Continue()` "continue", `Motor()` "motor", `Sail()` "sail", `HeaveTo()` "heave-to", `Anchor()` "anchor", `TurnBack()` "turn-back", `CallTow()` "call-tow". `StartEngine`, `StopEngine` unchanged.
- Views (no ids anywhere):
  - `TripPlanView(string From, string To, double DistanceNm, int CourseDeg, int DepartDay, double MotorHours, double? SailHours, string Estimate, IReadOnlyList<ForecastDay> Forecast, IReadOnlyList<TripRisk> Risks, IReadOnlyList<string> SparesAboard, IReadOnlyList<string> SparesMissing, IReadOnlyList<string> Shelter, int BalanceNok, int TowNok, IReadOnlyList<string> Blockers)`.
  - `ForecastDay(int Day, bool Known, string Text, int WindForce, string WindFrom, bool Rain, bool WindServes)` (Known false beyond `maxLeadDays`: "No forecast that far out.").
  - `TripRisk(string Problem, string Component, string AtSea, string? Spare, bool SpareAboard)`.
  - `InventoryView(IReadOnlyList<InventoryLine> Aboard, string? Shop, IReadOnlyList<ShopLine> ForSale)`; `InventoryLine(string Name, int Count, bool Tool)`; `ShopLine(string Name, int PriceNok, int Minutes)` (ForSale empty and Shop null at sea).
  - `TripView(bool AtSea, string? Port, string? From, string? To, double PositionNm, double RemainingNm, string? Mode, int Hours, bool CanAnchor, string? NearestShelter, bool WindServes, string Text)`.
  - Observations: `LegView(int Hour, string Mode, double PositionNm, double RemainingNm, int WindForce, string WindFrom, int SeaState, string Text)` for Continue/Motor/Sail; `TripResultView(string Destination, bool Towed, bool TurnedBack, int Minutes, int MoneySpentNok, IReadOnlyList<string> Failures, IReadOnlyList<string> Fixes, IReadOnlyList<LogEntry> Log, IReadOnlyList<string> Lines)` at arrival or tow.
- `StatusView` gains `string? Port = null, bool AtSea = false` at the end.
- Mode words: "under engine", "under sail", "hove-to", "at anchor", "drifting", "under tow".

### The at-sea tick

The trip adds no clock and no second fault system: `Tick` runs as before, and while `_passage` is set each hourly step does two extra things around `SystemsStep`:

```
Tick step:
  _time.Advance(step, aboard)
  SeaBefore(step)                     // at sea only
    seaState = stateByForce[Today.WindForce]
    speed by mode: Motoring (engine running) motorKnots; Sailing sailKnots[force] if the wind serves else 0 (→ Drifting);
                   HoveTo / Drifting 0, DriftNm += heaveToDriftKnots·h; Anchored 0; UnderTow towKnots towards the origin
    PositionNm += speed·h
    under load (Motoring, engine running): impeller State.vaneFactor −= impellerShedPerLoadHour·(1 − impeller.Condition)·h (≥ 0)
    engine running and seaState ≥ stirFromSeaState and water-in-fuel present: primary-filter State.waterMinutes += step
    Electrical.Draw(underWayDrawPerHour·h)    // instruments, VHF, lights
  SystemsStep(step)                   // WaterSystem.Inputs gains SeaState (0 unless under way): seawater leaks × (1 + slopPerSeaState·seaState);
                                      // spray: hatch open and seaState ≥ sprayFromSeaState → sprayLitresPerHourPerSeaState·seaState L/h
  if sunk: return                     // the existing card; its first line says where she went down
  Spawn(aboard)
  SeaAfter(step)                      // at sea only
    engine running and EngineInputsBuilder fuel restriction ≥ FiresAndDiesRestriction → engine dies, alert "fuel", Motoring → Drifting
    overheating while running: _passage.OverheatMinutes += step; ≥ overheatStopMinutes → engine stops itself, alert, → Drifting
    PositionNm ≥ DistanceNm + DriftNm → Arrive()
  PresentAlerts(step)                 // at sea the battery is watched every step, not only the morning check
```

- `EngineInputsBuilder`: raw-water flow × the impeller's effective `vaneFactor` (default 1); fuel restriction ≥ 0.9 when the primary filter's `waterMinutes ≥ stirHoursToBite·60`. So the existing start model, overheat alarm, cooling system and exhaust-hose spawn all see it. Replacing the impeller or the filter element resets the state (Component.Replace).
- Expected with the shipped numbers: a tired impeller (condition 0.35, flow 0.4) gives 0.32 after hour 1 and 0.24 (alarm) in hour 2, about 10 nm out, past Lyngskjær. Water in the fuel in force 4+ fires-and-dies at the end of hour 2. A slipping belt loses 0.08 of charge an hour under engine (0.03 run + 0.05 under way), so the 60 % battery speaks in hour 2. A healthy boat gains 0.02 an hour under engine.
- Leg commands (Continue, Motor, Sail) take `min(60, ceil(remaining / speed · 60))` minutes (60 when the speed is 0), so arrival lands at the end of a leg. After the time has passed the sim writes one line, adds it to the narrative and the logbook (stamped at the leg's end) and returns a `LegView` (an arrival or sinking card wins as the observation; the line still comes first in the narrative):
  "Hour 3. Force 4 from the southwest, a short chop on the beam. The engine note is steady; the bilge pump has run once. 4.0 nm to Hestholmen."
  Wind on the boat from |windFrom − course|: < noGoDeg "on the nose", < 80 "forward of the beam", < 110 "on the beam", < 150 "on the quarter", else "from astern". Engine: overheating "the engine note has gone hard and the temperature alarm whines"; fuel restriction ≥ 0.6 "the engine note hunts and sags"; running "the engine note is steady"; off "the engine is silent". Pump: cycles during the leg.
- The wind serves when force is within [sailMinForce, sailMaxForce] and |windFrom − course| ≥ noGoDeg (compass words to degrees, north 0, clockwise by 45).

### Commands at sea (refusals are free and unlogged)

| Command | Where | Minutes | Effect / refusal |
|---|---|---|---|
| Depart | at a port, cockpit | departMinutes | starts the passage, mode Motoring. Refused: on the hard, at sea, sunk, engine never run, engine not running ("Start the engine first: you'll motor off the mooring."), gale |
| Continue | at sea, cockpit | leg | an hour in the current mode |
| Motor | at sea, cockpit, engine running | leg | mode Motoring (weighs anchor if anchored), then the leg |
| Sail | at sea, cockpit, wind serves | leg | mode Sailing (engine may run, no load), then the leg |
| HeaveTo | at sea, not anchored | heaveToMinutes | mode HoveTo |
| Anchor | at sea, within anchorWithinNm of the origin, the destination or a shelter | anchorMinutes | mode Anchored; else "Nowhere to anchor here: Lyngskjær is 3.2 nm ahead." |
| TurnBack | at sea, not already turned back | turnBackMinutes | destination := origin, route reversed, position := distance − position, drift reset |
| CallTow | at sea, anywhere | tow.hours aboard + tow.lostHours away | tow.nok (allowed into a negative balance: the bill comes later), mode UnderTow, ends at the origin port with a `TripResultView` (Towed) |

- Engine and component work at sea (Uncover, Measure, Clean, Replace) only when HoveTo or Anchored and the engine is stopped: "Heave to or anchor first: you can't work on the engine under way." / "Stop the engine first." (at sea the auto-stop of the ashore commands does not apply). Inspect, CheckFilterBowl, CheckRawWaterFlow, Remove/Refit covers, PumpBilge, Start/Stop work at sea. WaitDays, Buy, Charge, FixByYard, OrderSurvey, Launch, EmergencyLift are refused at sea ("You're at sea." with what to do instead).
- Arrive: stops the engine ("You pick up a mooring and stop the engine."), `_portId` := destination, `_passage` := null, builds `TripResultView` from the port's `arrivalLines` (Hestholmen: the party, the evening; home: back at the mooring), minutes since departure, money spent since departure, failures = the alerts raised during the passage, fixes = the log texts of Clean/Replace done at sea, the passage's log entries. Handed over by `Accept` like the loss card.

### PlanTrip

- From the current port along its route (destination by name/alias via NameResolver, null = the one route). `DepartDay` defaults to today, clamped to ≥ today.
- Forecast: days `DepartDay .. DepartDay+1` (out and the day after). Lead = day − today. Lead > maxLeadDays → Known false. Else from `_weather[day]` with noise from `new SeededRandom(Hash(worldSeed, day, today))` (never the world stream): with `forceErrorChance[lead]` force ±1 (coin for the sign, clamped 0..9), with `rainFlipChance[lead]` rain yes/no flipped. Lead 0 is the real day. Deterministic per seed, bounded by one force.
- Estimate from the forecast for DepartDay (never the real series): MotorHours = distance / motorKnots; SailHours = distance / sailKnots[force] when the forecast wind serves, else null; unknown forecast → SailHours null and the estimate says so.
- Risks: known open faults (Discovered/Deferred, plus SupersededOpen) with an `atSea`, in plain words, with their spare and whether it is aboard. Never latent or symptomatic. SparesAboard: inventory lines (not tools). SparesMissing: the spares the risks name that are not aboard.
- Blockers: plain-word reasons Depart would refuse right now (on the hard, engine never run, at sea already, sunk).

### Alerts under way (existing channel, once per kind per day)

- `fuel`: "The engine coughs, picks up, coughs again and dies. Worth a look in the fuel filter bowl."
- `overheat-stop`: "Steam from the exhaust, and the engine labours and stops by itself. She's too hot to run."
- `battery` checked every step at sea (the existing text).
- Existing `overheat` and `alternator` fire under way as they are.

### Placeholders for Amit (also in the data's placeholders list)

Chandlery prices and minutes; Skjærvik–Hestholmen 14 nm on 040°, Lyngskjær at 7 nm; motoring 5 kn; sailing force 3–5, 4/5/5.5 kn, no-go 45°; heave-to drift 0.5 kn; anchoring within 1 nm; tow 6 000 NOK, 6 h, a day lost, 3 kn; sea state by force; slop +50 % per sea state; spray 2 L/h per sea state with the hatch open from sea state 2; tank stirring from sea state 3, bites after 2 h; impeller shed 0.3 × (1 − condition) per load hour; the engine stops after 90 overheat minutes; under-way draw 0.05 of a charge per hour; forecast error chances.

### (done) Step 1 — data, inventory, PlanTrip

**Built.** `sloop28.json` gains `chandlery`, `trips`, `sea`, `forecast`, `atSea` on the eleven listed faults, `vaneFactor: 1` on the impeller and `waterMinutes: 0` on the primary filter, and six 007 placeholder lines. `BoatRules.cs` has the records `ChandleryItem`, `Port`, `Shelter`, `Route` (with `Reversed()`), `TowRules`, `TripRules` (`FindPort`, `RoutesFrom(portId)` = data routes plus reversed ones), `SeaRules`, `ForecastRules` (init-property records with the note's numbers as defaults, deserialised straight from the data). `Boat` takes them as optional constructor parameters (`Chandlery`, `Trips` (null: nowhere to go), `SeaRules`, `ForecastRules`). `Fault.AtSea` is an `AtSeaSpec(Text, Spare?)`. `Sea` (src/Haulout.Sim/Sea.cs) is the pure helper: `Degrees`, `SeaState`, `StateWord`, `WindOnBoat` (0 to 180, on the nose = 0), `WindPhrase`, `SailKnots`, `Serves`. `Game.Inventory.cs`: `_inventory` (item id to count), Buy, spares, Replace-with-spare, the `Inventory` query. `Game.Trip.cs`: `_portId`, `internal bool AtSea`, `internal sealed class Passage` (stub: Route, PositionNm, DriftNm, Mode, Hours, OverheatMinutes, DepartClockMinutes, MoneyAtDeparture, LogStart, Failures, Fixes), `YardGate()`, `ForecastFor`, `TripBlockers`, `PlanTripSnapshot`. `StatusView` gains `Port` and `AtSea`.

**Decided where the notes were silent.**
- Buy resolves with `NameResolver` after stripping "a " and "spare "; an ambiguous word ("filter") is refused with the candidates. Unknown keeps "The chandlery doesn't have 'x'."
- Buy narrative: "You walk over to {shopName} and buy a/an {name}." (no article when the name has a bracket, e.g. "hose clamps (10)"). Log text is the item's `logText` or "Bought …".
- Replace from a spare: narrative "You take the spare {item} from the spares aboard." and log "Replaced the X from the spares aboard." Cost 0, money check skipped. At sea the check sits in the `pre` gate of `ResolveComponent` (`ReplaceGate`), so it comes before the reach and the below-waterline rule; the next developer must still skip `AfloatRefusal` at sea when a spare is aboard (done: `ReplaceGate` does not call it at sea) and add the heave-to and engine-stopped rules.
- `YardGate()` returns "You're at sea." at sea and "The yard is at Skjærvik." elsewhere; it gates FixByYard (inside `YardRefusal`, so the readiness routes show it), Charge (also its option), OrderSurvey, Launch, EmergencyLift. No `Trips` data: no gating.
- Forecast: noise stream is `new SeededRandom(worldSeed*73856093 ^ day*19349663 ^ today*83492791)` with three draws (force roll, sign, rain roll) always taken, so results do not depend on which chances fire. Lead is `day - today` (clamped at 0); chance index is `min(lead, arrays - 1)`. A flipped dry day becomes 1.0 mm. Wind direction and temperature are the real ones. Text is `WeatherWords.Describe` of the noisy day.
- Unknown or ambiguous destination, or planning from the water: a view with zeros and a Blocker; "There's no route from Skjærvik to 'x'." Destinations resolve over the ports reachable by a route from here, so "home" from Skjærvik is no route.
- `SparesAboard` / `SparesMissing` / `TripRisk.Spare` are chandlery item names. Blockers: on the hard, engine never run, at sea ("You're already at sea."), sunk, and a gale today when departing today. The engine not running is not a blocker.
- `Shop` keeps only `RemoteStartButton` (name) and an internal id constant; `GameSession.RemoteStartButtonCostNok` reads the Inventory query.

**Tests.** No existing test changed (254 + 17 still green). New: `InventoryTests` (10) and `PlanTripTests` (11); Sim tests now 275. The Replace-at-sea refusal and Buy-at-sea refusal are untested (cannot get to sea yet).

**Not done / for the next developer.** `_passage` is never assigned; the MCP `buy` tool description in `Haulout.Mcp/HaulTools.cs` still says the remote start button is the only item; the console parser's help is untouched.

### (done) Step 2 — the passage and the at-sea tick

**Built** (all in `Haulout.Sim`; no adapter source touched).
- `Passage` (Game.Trip.cs) is filled in: oriented `Route`, origin and destination port ids, `PositionNm` (through the water), `DriftNm` (the goal is `Distance + Drift`), `SeaMode` (Motoring, Sailing, HoveTo, Anchored, Drifting, UnderTow) with `ModeWord`, `TurnedBack`, `OverheatMinutes`, departure clock, money and log index, `Failures`, `Fixes`, and `EngineNote` (how the engine sounded at the end of the last step, for the hour line). New partial `Game.Sea.cs` holds `SeaBefore`, `SeaAfter`, `Arrive`, the helm commands, the tow and the `Trip` query.
- Tick: `SeaBefore(step)` before `SystemsStep`, `SeaAfter(step)` after `Spawn`, then `PresentAlerts(step, atSea)`, all only while `_passage` is set (taken at the start of the step, so the arrival step still counts as at sea). `WaterSystem.Inputs` gained `SeaState` (slop on seawater leaks, spray through the open hatch). `EngineInputsBuilder`: flow times the impeller's effective `vaneFactor`; restriction max'ed with 0.9 once the primary filter's `waterMinutes` reach `stirHoursToBite * 60`. New alert kinds `fuel` and `overheat-stop` (texts per the notes). Every alert raised during a passage also goes into `Passage.Failures`.
- Commands `Depart(Destination?)`, `Continue`, `Motor`, `Sail`, `HeaveTo`, `Anchor`, `TurnBack`, `CallTow` (JSON `depart`, `continue`, `motor`, `sail`, `heave-to`, `anchor`, `turn-back`, `call-tow`); query `Trip()` (`trip`) giving `TripView` (`trip-view`); observations `LegView` (`leg-view`) and `TripResultView` (`trip-result-view`). Catalogue verbs: depart, continue, motor, sail, heave to, anchor, turn back, call tow, trip. `Accept` takes a null log text (the legs write only their hour line) and an `afterTick` hook, and hands over a pending trip card the way it hands over the loss card.
- Gates at sea: Uncover, Measure, Clean, Replace need HoveTo or Anchored and the engine stopped (`SeaWorkRefusal`, also in the component-action options; Replace checks it before the spare). WaitDays refused ("You're at sea: continue, heave to or anchor."); Buy, Charge, FixByYard, OrderSurvey, Launch, EmergencyLift were already refused through `YardGate` and the Buy check.
- The sinking card says where at sea: "She went down 9.6 nm out from Skjærvik on day …".

**Decided where the notes were silent.**
- Depart takes its 10 minutes at the mooring (a normal aboard tick, no sea state, no progress); the passage starts after them. So the first leg is hour 1, position after k legs under engine is 5k nm, and "minutes since departure" in the card and the hour number count from leaving the mooring. Hour = minutes since departure / 60 rounded up, so time spent working while hove-to moves the hour number on.
- `PositionNm` in `TripView`/`LegView` is the net distance made good from the route's start (drift taken off, never below 0, rounded to 0.1); `RemainingNm` is `Distance + Drift - Position`. Anchoring, shelter distances and the sinking card use the net distance. TurnBack: new position = distance - net progress, drift reset, destination the origin.
- Hour line engine word: capitalised first word of the engine phrase ("The engine is silent", "The engine note has gone hard and the temperature alarm whines"). Pump: "has stayed quiet" / "has run once" / "twice" / "N times". Calm (force 0): "No wind, flat calm." Wind on the boat uses the current (possibly reversed) route course.
- The engine stops itself when `overheatStopMinutes` of consecutive overheating have run (the counter is reset when it cools or stops); fires-and-dies takes the engine out the same way. Both turn Motoring into Drifting. Drifting and HoveTo drift at `heaveToDriftKnots`; Anchored does not. Sailing turns into Drifting at the start of a step if the wind stops serving; Motoring likewise if the engine is stopped. The player must say Motor/Sail again.
- Under-way draw is `Electrical.DrawFromEffective`: it first brings the live charge down to what the panel shows (the 60 % cap of the legacy low-charge fault), then draws. Without it a 60 % battery whose hidden live charge is 0.93 cannot reach 0.5 in a three-hour trip and the notes' "belt alert in hour 2" cannot happen. Only the sea draw does this; the rest of the electrical model is untouched. Once an alert kind has been raised on a day it is not raised again that day, so the alternator lamp lit at the mooring that morning is not repeated under way (it is in the logbook; the battery alert, first raised under way, is in the card's Failures).
- Arrival: engine stopped, `_portId` := destination, narrative "You pick up a mooring and stop the engine." + the port's arrivalLines (also the card's Lines), logbook "Arrived at X." stamped at the arrival minute. If the command that was ticking has minutes left, they pass at the mooring. A turn back that gets home is an ordinary arrival (TurnedBack true, Destination Skjærvik, Skjærvik's arrival lines).
- Tow: engine stopped, mode UnderTow, a log entry at the call (cost on it), `tow.hours` aboard hourly steps (the sea factors, draw and water still apply), arrival at the port she left (the original origin even after a turn back), then `tow.lostHours` of away tick. `MinutesTaken` is the lot (1440 with the shipped data); the card has two own lines (no party): the towboat and the bill, and the day gone. `_money` takes the 6 000 even below zero; Balance goes negative and every purchase is then refused with the existing "That's N NOK and you have -M NOK left."
- Depart refusals in order: already at sea, no trip data, on the hard, not in the cockpit, engine never run, engine not running, gale (force >= 7: "It's blowing too hard to leave: …"), unknown destination. Continue/Motor/Sail/HeaveTo/Anchor/TurnBack: "You're not at sea." then "The tiller is in the cockpit."; CallTow needs only the sea (the VHF). Sail refusals say too little wind / too rough / on the nose with the wind in words. Motor with the engine off: "The engine isn't running: start it first." Anchor when already at anchor, HeaveTo when at anchor or already hove-to, TurnBack twice are refused with plain sentences.
- Anchoring places: the port she left, the named shelters, the port she is bound for; nearest by net distance; the refusal and `TripView.NearestShelter` say "Lyngskjær, 2.0 nm ahead" (or "astern").

**Expected timings checked with the shipped data (force 3 southwest, constant weather): they hold, nothing in the data changed.** Impeller: vaneFactor 0.805 after hour 1, 0.610 after hour 2 (flow 0.244), alarm in hour 2, about 10 nm out; the engine stops itself in hour 3. Water in the fuel, force 4: waterMinutes 60 after hour 1, 120 at the end of hour 2, the engine dies then. Belt (impeller replaced): panel charge 0.52 after hour 1 (no alert), 0.44 after hour 2 with the voltmeter alert; the healthy boat gains 0.02 an hour. A typical (a) run, force 3 from the southwest, hours 1 and 2:
- "Hour 1. Force 3 from the southwest, small wavelets from astern. The engine note is steady; the bilge pump has stayed quiet. 9.0 nm to Hestholmen."
- "Hour 2. Force 3 from the southwest, small wavelets from astern. The engine note has gone hard and the temperature alarm whines; the bilge pump has stayed quiet. 4.0 nm to Hestholmen." (with the alarm alert), then heave to, stop, replace from the spares, start, `Motor` to a closing hour "Hour 5. … The engine note is steady; … 0.0 nm to Hestholmen." and the card.

**Tests.** `TripTests.cs` (36 new, Sim tests now 311): speed by wind, hour-line format, trip query, impeller shed only under load, water minutes by sea state/engine, slop and spray (water system and in the game), story (a) end to end, the engine stopping itself, story (b) (the bite, the missing spare, tow with its numbers, tow into a negative balance, turn back under sail home), story (c) (belt: lamp in the log, battery alert in hour 2, healthy gain), the refusals (Replace without the spare free and unlogged, Buy/Wait/yard at sea, work while motoring or with the engine running, Anchor near shelter only, cockpit rule, Depart refusals), the reversed route, sinking at sea, JSON leak checks over Status, Trip, plan, logbook and every command result of two trips, and the catalogue/JSON names. No existing test changed.

**Not done / for the adapter developers.** The Prose, Console, Mcp and Web sources are untouched: they need words for the eight commands, the `trip` query, `LegView`, `TripResultView` and `TripView` (the hour line is already a sentence), and the MCP `buy` description is still the old one.

### (done) Lead review

- Played the console end to end (survey, clean the terminal, replace the seacock, yard-fix the fuel, start, launch, buy an impeller, plan, depart, overheat in hour 2, heave to, stop, replace the impeller from the spares, start, motor, arrive at Hestholmen after 5 h). The slipping belt (latent) spoke on its own under way. Accepted step 2's `Electrical.DrawFromEffective` (the under-way draw first brings the hidden live charge down to the legacy 60 % cap). The battery alert at sea now reads "… V and falling." instead of "this morning". The playtest give-up limit is raised from 40 to 150 tool calls (a full trip cannot fit in 40).
- Open questions for Amit: (1) chandlery prices disagree with the components' replace prices (belt 220 vs 90, secondary element 160 vs 220, oil 300 vs 320); which wins? (2) Should a tow go where you ask, not only back to the port you left? (3) Fuses and the 2 m hose have no use yet: keep them as carried spares or cut? (4) Is a tired impeller overheating within two hours under load plausible, and should an overheating diesel stop itself (90 min placeholder) or seize? (5) Should a tow be allowed into a negative balance? (6) Hestholmen has a shop; should it?
- Cuts: the forecast is not logged at departure (only the real weather is); weather is one record per day, so the wind does not change within a passage; no fuel consumption; no chart.
