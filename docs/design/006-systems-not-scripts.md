# 006 — Systems, not scripts

**Date:** 2026-11-08. **Decided by:** Amit (product). **Status:** adopted.

## Question

After 005 the boat has a fault lifecycle, but the lifecycle is a timeline: hand-authored successors on fixed timers, consequences as prose, degradation asleep, no water, no weather, no alerts, no way to sink. Amit: a fixed "20 days" loses interest and realism; faults should trigger faults; escalation should be a range shaped by negligence, weather and other factors we add later. What replaces the scripts?

## Decisions

### 1. A time driver
Every clock advance (an action's minutes, `WaitDays`, yard days, the survey day) runs the same tick: age components, advance fault progression, run each system, spawn faults that cross thresholds, emit alerts. There is one place where time passes.

### 2. Rates, not timers
Every active fault (and every component's wear) has a **progression rate per day**. Escalation fires when accumulated progression crosses a threshold with a **seeded spread**, so the same boat never seizes on the same day twice. The rate is the product of a base rate and named factors:

| Factor | Driven by |
|---|---|
| time | calendar days |
| weather exposure | the day's rain, temperature, wind (006.5) and whether the hatch/covers are open |
| use | engine hours, sailing hours (later) |
| neglect | known faults left open, batteries left flat, covers left off, days since the player was aboard |

Four factors until all four are felt in play. Adding a factor is a product decision.

**Diagnosable randomness (CLAUDE.md rule 3):** readiness shows the likely range ("likely 2–4 weeks"), and every escalation writes a logbook line naming its causes ("a week of rain, the hatch open, the bilge awash"). No hidden dice.

### 3. The effect graph spawns faults
Faults change component properties (ohms, flow factor, charge, leak rate…). Systems compute state from properties on every tick. **Thresholds spawn faults**, including on other components. Nobody authors chains:

- seized seacock → no cooling flow → engine overheats when run → exhaust hose cooked
- slipping alternator belt → battery not charging → low state of charge → starter drags → terminal strained
- weeping seacock + rain through the cockpit drains → bilge rising → pump cycling → pump burnt out → bilge awash → (006.4)

Hand-placed faults remain as the previous owner's legacy; everything after them is emergent. Spawned faults are real faults with the full lifecycle (discoverable, deferrable, fixable).

### 4. A water model, and she can sink
Ingress sources with rates (seacock weep, stern gland, a failed hose, rain via drains, a wave later) → **bilge level** → a bilge pump with a float switch, a fuse and a battery → pumped out. Pump needs power; power needs charge. Alerts: the pump cycling, the bilge smell, the counter on the pump. Level above the sole = soaked interior (spawns faults in anything low: starter, batteries). Level above a sinking threshold with no pump = she sinks, at the dock or at sea. **Loss is survivable** (CONCEPT.md): insurance pays part, the boat is written off or salvaged, the player buys the next one. A closing card, not a game over.

### 5. A weather series, not a simulator
A seeded daily record for the season: wind (force, direction), rain (mm), temperature, Nordic seasonal curves. It feeds the rates (rain into the bilge, frost in the raw-water system, UV on deck), **gates work** (no antifouling in rain, no going aloft in a gale, yards don't launch in a storm), colours the prose and the logbook header, and later drives passages. A forecast is not needed until trips exist. Today's weather is a query; the past is in the logbook.

### 6. Alerts: the boat talks
A push channel beside the pull channels (look, inspect, readiness). Alerts are perceptions at a place or on coming aboard: "the bilge pump has run eleven times since you were last here", "a smell of diesel in the saloon", "the panel lamp for the alternator stays on", "11.9 V on the battery this morning". Each alert names what the player could check next and leaks nothing they could not perceive. The MCP exposes them as the result of `look`/`status` and a `notices` tool; the web shows them as logbook lines in a different hand plus a quiet badge.

## Guardrails

- Every number that shapes a rate lives in data and is listed as a placeholder for Amit.
- Readiness and alerts never reveal latent faults; they reveal effects the player could perceive.
- Chill: cascades are the content, but each link is visible in the log, and nothing kills the account.

## Order of work (this round)

1. Time driver + rates with spread, replacing `afterDays`; neglect and weather factors; readiness ranges; cause lines in the log.
2. Weather series and its gates.
3. Water model with pump, soaked interior, sinking and the survivable closing.
4. Effect-graph spawning with the three chains above as tests.
5. Alerts across console, MCP and web.

## Open questions for Amit

- Typical weep rate of a bad seacock and stern gland (litres/hour)? Default placeholders in data.
- Insurance: what fraction of a write-off pays out, and does a known-and-deferred fault void it? Default: 60 %, and yes if the surveyor marked it *Before launch*.
- How often does a careful owner visit a boat on the hard in spring? Sets the neglect baseline. Default: twice a week.

## Implementation notes

**Date:** 2026-11-08. Written by the lead *before* the work, as the one shape every developer builds to. Sections marked (done) are filled in as the steps land.

### The tick loop

There is one clock writer: `Time/SimTime.cs` (`WorkMinutes`, `IdleMinutes`, `ClockMinutes`, `internal void Advance(int minutes, bool work)`). Only `Game.Tick` calls `Advance`. `Accept(...)` logs the action at its start stamp and then calls `Tick(minutes, work)`; every command that takes time (actions, `WaitDays`, the survey day, yard lead days, the lift days, launch) goes through `Accept`, so through `Tick`.

```
Tick(minutes, aboard):            // aboard = work time; idle time = the player is away
  while minutes left and not sunk:
    step = min(left, 60, minutes to next midnight)
    _time.Advance(step, aboard)
    Systems.Step(step):            // Systems/*.cs, in this order
      Electrical   battery SoC: crank draw (per start, not here), engine charge/drain while running, pump draw, self-discharge
      Cooling      while running: raw-water flow; flow < 0.25 adds to overheat minutes
      Water        ingress (seawater afloat only, rain always), float-switch pump, manual pump is a command, level, awash hours
    Spawn()                        // dormant faults whose spawn threshold a system value has crossed wake, Latent
    Alerts.Present(step)           // perceptions while aboard: lamp, alarm, smell, water over the sole
    if sunk: stop (the rest of the time does not pass)
    if crossed midnight: Daily(dayJustEnded)
  if !aboard and the step covered >= 1 day: Alerts.Arrival()   // "on coming back aboard"

Daily(day):
  wear: every component Age(1, engineHoursThatDay)
  progression: for every present fault with a progression spec
      rate = base * spread * Π(declared factors for that day)   // exposure, use, neglect; each >= 1.0
      progress += rate; record the day's causes in the fault's history
      progress >= threshold (1.0) and an escalation: Escalate (known: narrative + log line with causes; latent: silent)
  reset per-day counters (engine minutes, rain bookkeeping)
```

Escalation on launch stays (`escalation.onLaunch`). A fault starts progressing on the day it becomes active (game start, wake-up, spawn, poor job); its **spread** is drawn once at that moment from the *world* random (see Seeds) as `lo + r * (hi - lo)` with `rates.spread = [0.6, 1.6]`.

### Seeds

`Game(Boat boat, int startDay = 75, IRandom? random = null, int worldSeed = DefaultSeed, WeatherSeries? weather = null)`. `random` stays the player-luck stream (poor jobs), unchanged, so seed 4's first roll is still 0.249. A second stream, `new SeededRandom(worldSeed * 7919 + 17)`, draws spreads. The weather is `weather ?? WeatherSeries.Generate(worldSeed, climate)`. `NewFirstFault(seed)` passes `seed` to both. Tests that need dry, calm days pass `WeatherSeries.Fair()` (force 3 from the SW, 6 °C, no rain, every day).

### Factors (all weights in `rates`, all placeholders)

- **exposure** = `(1 + rainWeight * min(rainMm, rainCapMm) / rainCapMm + (nightC < 0 ? frostWeight : 0)) * (1 + perOpenCover * n)`, n = how many of the fault's `exposedBy` covers were off at the end of the day. 1.0 until step 2 lands (no weather yet), but the cover term works from step 1.
- **use** = `1 + perEngineHour * engineHoursThatDay`.
- **neglect** = `1 + min(1, perKnownOpenFault * knownOpen + (soc < 0.5 ? batteryLow : 0) + perCoverLeftOff * coversOffWhileAway + perDayAwayBeyond3 * max(0, daysSinceAboard - 3))`. `neglect.covers` lists which covers count as "left off" (the companionway hatch). "Aboard" means work minutes were spent that day.

Each day the fault's history counts what applied: rain days, frost nights, days per exposedBy cover off, days away beyond 3, battery-low days, known-open days, engine hours. The escalation log line names a cause when it applied on at least a third of the period's days (min 2): "The raw-water seacock has seized. It came on after twelve days of rain, with the floorboards up and the hatch open." With no factor above 1: "It came on with time alone." Number words one to twenty, numerals above. Cover phrases come from the cover's `offPhrase`.

### Readiness range

For a **discovered or deferred** fault with a progression and an escalation: `remaining = threshold - progress`, `m` = mean of the fault's factor product over its last 7 days (today's if no history), `LikelyMinDays = floor(remaining / (base * m * spreadHi))`, `LikelyMaxDays = ceil(remaining / (base * m * spreadLo))`. Never for latent or symptomatic faults. `ReadinessItem.EscalatesInDays` is replaced by `LikelyMinDays` / `LikelyMaxDays` (prose: "Likely to get worse in 9–24 days.").

### Data shapes (`sloop28.json`)

```jsonc
"rates": {
  "threshold": 1.0, "spread": [0.6, 1.6],
  "exposure": { "rainWeight": 0.5, "rainCapMm": 10, "frostWeight": 0.3, "perOpenCover": 0.25 },
  "use": { "perEngineHour": 0.25 },
  "neglect": { "perKnownOpenFault": 0.05, "batteryLow": 0.2, "perCoverLeftOff": 0.15, "perDayAwayBeyond3": 0.05, "covers": ["companionway-hatch"] }
},
// fault
"progression": { "baseRatePerDay": 0.0455, "factors": ["exposure", "neglect"], "exposedBy": ["saloon-floorboards", "companionway-hatch"] },
"escalation": { "to": "seacock-seized", "onLaunch": true, "text": "..." },          // afterDays is gone
"effects": [ { "kind": "property", "property": "leakLitresPerHour", "op": "add", "value": 0.05 } ],   // alongside or instead of "effect"
"spawn": { "when": "water.awashHours", "atLeast": 2 },                               // on a dormant fault
// component
"needsDry": true,                 // antifouling: DIY replace refused in rain
// place
"exposed": true,                  // on deck: refused in force 7+
// cover
"startsOff": true, "offPhrase": "the hatch open", "stateWords": ["open", "shut"]   // the companionway hatch
```

- **Property effects.** `kind: property` with `property`, `op` (`set` | `add` | `multiply` | `min`), `value`, optional `component` (default: the fault's own). A component has live state (`Component.State`, initialised from `properties`); `Systems/Properties.Effective(boat, componentId, name, fallback)` = live value with every present fault's property effects on it applied in data order. The old effect kinds stay and keep working; the battery's legacy `stateOfCharge` fault acts as a cap (`min`) over the live SoC.
- **System values** for spawn rules, by name: `water.bilgeLitres`, `water.pumpCycles` (since the pump was fitted), `water.pumpRunMinutes`, `water.awashHours`, `cooling.overheatMinutes`, `electrical.stateOfCharge`, `electrical.dragStarts`. Spawn: `value >= atLeast` wakes the dormant fault (Latent, spread drawn) once.
- **Weather** curves in `Data/weather.json` (embedded): twelve months of `tempC [lo, hi]`, `nightDropC`, `rainChance`, `rainAfterRainChance`, `rainMeanMm`, `windMeanForce`, `galeChance`, `prevailing` (directions). Months are `GameClock` months (30 days, day 75 = mid-March).

### New in the boat

- Covers: `companionway-hatch` (cockpit; starts off = open; going below with it shut slides it open for free), `bilge-pump-fuse` (companionway, by the battery switch; off = pulled, the pump has no power).
- Components: `bilge` (bilge under the saloon; visible from the saloon with the floorboards up; Inspect reports the level in words), `bilge-pump` (under the saloon sole, float switch, 20 L/min, fed from the start battery through the fuse).
- Faults: `alternator-belt-slipping` (legacy, latent; chargeFactor × 0), `drain-hose-failed` (successor of the clamps by progression), `bilge-pump-burnt-out` (spawn on pump cycles / continuous running), `exhaust-hose-cooked` (spawn on overheat minutes), `terminal-strained` (positive terminal; spawn on drag-starts), `starter-soaked`, `battery-soaked`, `ground-stud-soaked` (spawn on awash hours).

### Water (step 3)

Hourly (per step) ingress: seawater `leakLitresPerHour` (effective, every component) only while afloat; rain `rainLitresPerMm` (effective, every component) × the day's rain spread over 24 h, plus `water.rainThroughOpenHatchLitresPerMm` while the hatch is open. The float switch starts the pump at `onLitres` and stops at `offLitres`; one start = one cycle; if ingress outruns the pump it runs the whole step. Powered = fuse in and effective SoC > 0.15; each cycle and each run-minute takes charge. Above `soleLitres` (120) the interior is soaked (awash hours count). Afloat above `sinkLitres` (1500) she sinks: the command's `Observation` is a `LossView`, `StatusView.Sunk` is true, every later command is refused, queries still answer. Insurance: `insurance.payoutFraction` of `insurance.valueNok`, void if a *Before launch* survey item's fault (or what it escalated into) was ever deferred and never fixed. On the hard she cannot sink; she fills.

`PumpBilge` (cockpit, 15 min, 10 L/min). Level words: under 1 L "dry, a damp sheen"; under 5 "a finger of water"; under 30 "a few inches, sloshing"; under 120 "deep, up to the floorboards"; else "over the sole".

### Facade additions

- Commands: `StopEngine` ("stop", 1 min), `PumpBilge` ("pump-bilge").
- Queries: `Weather` → `WeatherView(int Day, string Text, int WindForce, string WindFrom, double RainMm, double TempC)`; `Notices` → `NoticesView(IReadOnlyList<Alert> Alerts)` (unread, then marks them read).
- `Alert(int Day, string TimeOfDay, string Text)`. `CommandResult.Alerts`: every accepted command carries all unread alerts and marks them read; refusals carry none. `StatusView.Notices` (unread, not marked), `StatusView.Sunk`, `StatusView.Weather` (today's text). `LogEntry.Alert` (alerts are also log lines). `LogView.Weather` (the header).
- `LossView(int Day, IReadOnlyList<string> Lines, int ValueNok, int PayoutNok, bool Voided, string? VoidReason)` observation.
- Gates (perceivable refusals, free, unlogged): DIY on a `needsDry` component in rain; going to an `exposed` place in force 7+; the yard's launch in force 7+.

### Alerts (step 5)

Arrival (end of an away period): pump cycles since last aboard ("The bilge pump counter has gone up by 14 since you were last aboard. Worth lifting the floorboards to see where the water comes from."), a bilge smell when the bilge is deep and the floorboards are in, the panel voltmeter when SoC < 0.5, water over the sole. Present: the alternator lamp while running without charge, the temperature alarm and hot-rubber smell while overheating, water over the sole when it crosses. Each kind at most once a day. An alert names what to check next and never a fault the player has not discovered.

### (done) Step 1 — time driver and rates with spread

Built: `Time/SimTime.cs` is the only clock; `Game.Tick.cs` holds `Tick(Turn t, int minutes, bool aboard)` (hourly steps, split at midnight) and `Daily(Turn t, int day)` (wear for every component, progression for every present fault with a `progression`, escalation at the threshold, known faults write the cause line as a `LogEntry`, latent ones stay silent). `SystemsStep`, `Spawn` and `Alerts` are empty stubs for steps 3 to 5. `Fault` carries `Progression`, `Progress`, `Spread` and a `History` (cause counts and the last seven daily products); `Escalation` lost `AfterDays`; `rates` loads into `Boat.Rates` (missing: threshold 1, spread 0.6 to 1.6, weights 0). `Game(boat, startDay, random, worldSeed)` draws spreads from `new SeededRandom(worldSeed * 7919 + 17)` at construction, on wake and for poor jobs. No `WeatherSeries` parameter yet: `ExposureWeatherTerm(day)`, `RainDay(day)`, `FrostNight(day)` and `BatteryLow()` are stubs returning 0 or false in `Game.Tick.cs`, for the weather and electrical steps to fill. New: the `companionway-hatch` cover (starts off; going below with it shut slides it open free), cover `offPhrase`/`stateWords`, `ReadinessItem.LikelyMinDays/LikelyMaxDays`, the `drain-hose-failed` successor (clamps progress to it at 0.02/day), seacock progression 0.0455/day. Wait, the survey, the yard fix and the emergency lift now stop the engine first.

Decided where the notes were silent: `Tick` takes the `Turn` (escalation needs the narrative); the cause line's first sentence is the first clause of `escalation.text` (up to the first colon or full stop); "known problems left open" counts the other known open faults, not the fault itself (otherwise every known fault would cite itself); `batteryLow` is false until step 3 gives the battery a live charge; a fault with no progression draws no spread (so poor jobs, which have none, do not move the world stream); the log's "Found" line for an escalation is written right after its cause line, at the stamp of midnight; the first midnight is 16 hours after the 08:00 start and counts as a full day of progress.

Tests changed: `PreLaunchTests` escalation tests no longer assume 20 days: they wait day by day until the seizure is found (`WaitUntilSeized`) or assert the readiness range; `EscalatesInDays_counts_down...` became `Readiness_gives_a_likely_range...`; the latent and overheating tests wait 45 days. `BoatLoaderTests` counts (covers 6, faults 13); `AdapterQueryTests.Nothing_is_found_before_going_aboard` (6 covers, only the hatch starts off). New: `TickTests`.

### (done) Step 2 — the weather wired in

Built: `Game(..., WeatherSeries? weather = null)` (default `WeatherSeries.Generate(worldSeed)`); today is `series[Day]`. The three stubs are filled (`ExposureWeatherTerm` = `rainWeight * min(rain, cap) / cap + (night < 0 ? frostWeight : 0)`), so rain days and frost nights reach the cause line. `Weather` query / `WeatherView`, `StatusView.Weather`, `LogView.Weather`, a `weather` catalogue entry; the console verb `weather` (also `sky`), an MCP `weather` tool (read-only, in the help footer) and the web logbook header carry it. Gates (free, unlogged refusals): `needsDry` on `antifouling` (DIY clean or replace in rain; the readiness DIY route and `ComponentActions` show it unavailable with the reason; the yard route is not gated), `exposed` on `on-deck` (force 7+), and the launch (force 7+). The hull has no paint route, so it carries no `needsDry`. Numbers live in `rates.weatherGates { windForce: 7, rainMm: 0.5 }`, listed as a placeholder.

Decided where the notes were silent: `WaitDays` summarises the days left behind, today up to the day before she is next seen (the arrival day is not counted), as a second narrative line after "You leave her for N days."; the yard-fix lead days, the survey day and the lift days say nothing about the weather. `LogView` also carries `Day` (the header needs it): the print is `Day N · <weather>` above the entries, also when the logbook is empty. `WeatherWords` gained public `RainWord`, `WindPhrase` and `Temperature` for the refusal texts. `WeatherSeries.From(Func<int, WeatherDay>)` builds hand-made series for tests. The facade's `Weather` query type shares its name with the `Haulout.Sim.Weather` namespace; code outside `Haulout.Sim.Play` that needs the query aliases it (`using WeatherQuery = Haulout.Sim.Play.Weather;`). The rain gate uses `weatherGates.rainMm` (0.5, equal to `WeatherWords.RainThresholdMm`) while the progression's `RainDay` uses `WeatherWords.IsRaining`; keep the two numbers equal.

Tests changed: `PreLaunchTests.NewGame`, `TickTests.NewGame` and the two `new Game(...)` in `GameScenarioTests` now pass `WeatherSeries.Fair()`: the seacock range/escalation tests (`Readiness_gives_a_likely_range...`, `A_discovered_but_undeferred_seacock_escalates...`) and the cause-line test depend on constant factors, and real rain and frost move the day and add "rain" to the line. Tests on `Game.NewFirstFault()` (AdapterQueryTests, GameScenarioTests, the Mcp tests) were left alone: seed 4 never passes force 6 in days 75 to 85, where those tests play, so no gate blocks them. `McpServerTests` tool list: 29 tools (added `weather`). New: `WeatherGameTests` (13 tests) and one prose test in `ProseTests`.

### (done) Step 3 — the effect-graph mechanism and the water model

Built (mechanism): `FaultEffect` gained the property form (`kind: property`, `property`, `op` set/add/multiply/min, `value`, optional `component`); a fault carries `effect` and/or `effects`, `Fault.Effects` is the combined list (legacy first when it is not `none`), `Fault.Effect` stays (the legacy one, else the first listed). `Component.State` is the live copy of `properties` (Replace puts it back to the spec). `Systems/Properties.Effective(boat, id, name, fallback)` = the state value (else the fallback) with every *present* fault's property effects on that component and property applied in the order of the boat's data. `Systems/SystemValues` is the name to number map the systems write each step; `Spawn()` wakes a dormant, never-spawned fault whose `spawn` rule (one `{when, atLeast}` or a list; any crossing wakes it) has been reached: Latent, silent, spread drawn, `Fault.Spawned` set so it never re-spawns (even after the player fixes it). The start battery's live charge is `State["stateOfCharge"]`; `Systems/Electrical.StateOfCharge` is that with property effects applied and the legacy `battery-low-charge` fault as a `min` cap; the engine inputs, `BatteryLow()` (< 0.5) and the pump all read it. Self-discharge per step from `electrical.selfDischargePerDay` (0.002) plus the battery's `selfDischargePerDay` property effects. `Charge` sets the live charge to 1.0 and clears the legacy fault. The engine's series ohms now come from `Properties.Effective(..., "seriesOhms", spec)` so a soaked starter or ground stud shows in cranking.

Built (water): `Systems/Water.cs` (`WaterSystem`) runs each step after `Electrical`: ingress litres/min = seawater (afloat only) from the effective `leakLitresPerHour` of every component, plus the day's rain share (mm/24 per hour) times the effective `rainLitresPerMm` of every component plus `water.rainThroughOpenHatchLitresPerMm` while the hatch is open. The float-switch pump is solved exactly inside the step (on at `onLitres`, off at `offLitres`, several cycles per step are counted, if ingress outruns the pump it runs the whole step). Powered = `bilge-pump-fuse` in and effective charge above `electrical.pumpMinStateOfCharge` (0.15); each cycle and run-minute draws the live charge. Cycles and run-minutes live in the pump's `State` (`cycles`, `runMinutes`: Replace resets them) and are published as `water.pumpCycles` / `water.pumpRunMinutes` next to `water.bilgeLitres`, `water.awashHours`, `electrical.stateOfCharge`. `CyclesSinceAboard` counts cycles since the last aboard step (reset by `Tick` on an aboard step, after the alerts hook) for step 5's arrival alert. Awash hours accrue above `soleLitres`. Afloat at `sinkLitres`: `Sink()` sets `Sunk`, stops the engine, builds the `LossView`; `Tick` returns at once, `Accept` returns the card as the observation (it wins over the command's own) with its lines added to the narrative, `MinutesTaken` is what actually passed, and every later command is refused ("She's on the bottom of the harbour. The logbook is all that's left."); queries answer, `StatusView.Sunk`. On the hard the level is clamped at `sinkLitres`. Insurance: `insurance.payoutFraction * valueNok` (108000), void when a Before-launch item of the report (`_surveyKept`) has a fault, or one it escalated into, that was ever deferred (`Fault.EverDeferred`) and is still present; the money balance is not credited (the game is over). `PumpBilge`: cockpit only, 15 min, removes up to 150 L, three narratives (dry, emptied, still coming). Level words are data (`water.levelWords`); the saloon's Look/GoTo say "Water stands over the saloon sole." above the sole even with the floorboards in.

Data: covers 7 (new `bilge-pump-fuse`: "pull fuse" works in the console, `pull` now maps to Remove when it is not about a boot), components 45 (`bilge`, `bilge-pump`), faults 17 (`bilge-pump-burnt-out` spawning on `water.pumpCycles >= 150` or `water.pumpRunMinutes >= 300`, `starter-soaked`, `battery-soaked`, `ground-stud-soaked` on `water.awashHours >= 2`). Effects: `seacock-weeping` and `seacock-seized` leak 0.05 L/h, `drain-clamps-rusted` 1.5 L/mm, `drain-hose-failed` 4 L/mm and 60 L/h. Blocks `water`, `electrical`, `insurance`, all in the placeholders list. Adapters: `PumpBilge` command ("pump-bilge"), catalogue "pump bilge", console `pump` / `pump bilge`, MCP `pump_bilge` (30 tools now) and the help footer, web `GameSession.PumpBilge` + a "Pump the bilge" button in the context panel's top bar (cockpit only) + a closing card for the `LossView`.

Decided where the notes were silent: `seacock-seized` also leaks 0.05 L/h (otherwise the weep would end the moment she is launched, since the weeping fault escalates on launch); the bilge starts at `water.startLitres` 0.5 L; `Sink` takes the day of the step's start; the sinking card does not credit the payout to the balance; a spawn rule fires once per fault for the whole game (a replaced pump that burns out again does not re-spawn); `Look`'s "Behind it" is empty for the fuse (nothing sits behind it); `InternalsVisibleTo("Haulout.Sim.Tests")` gives the tests `Game.BilgeLitres` (get/set), `Values` and `PumpCyclesSinceAboard`, nothing public. Note for step 4/5: with the placeholder numbers a hose that has come off (60 L/h) puts about 6 cycles an hour on the pump, so it burns out after about 25 hours and she sinks about a day after that unless someone notices: the arrival alerts matter, and the thresholds are the knob.

Tests changed: `BoatLoaderTests` (45 components, 7 covers, 17 faults; the count test was renamed `Sloop_has_45_components_7_places_and_7_covers`), `AdapterQueryTests.Nothing_is_found_before_going_aboard` (7 covers), `McpServerTests` tool list (30 tools, `pump_bilge`). New: `WaterTests` (30 tests).

### (done) Step 4 — the electrical and cooling sides, and the three chains

Built: `Systems/Electrical.cs` gained `ChargeFactor` (the alternator's effective `chargeFactor`, default 1), `Crank` and `Run`. `electrical.crankDraw` (0.01) comes off the live charge in `DoStart` for every attempt that cranks (any outcome but Nothing, so a Click draws too), not per step. While the engine runs each step adds `chargePerHour` (0.10) times the factor and subtracts `runDrawPerHour` (0.03), clamped 0..1 on the live value; the legacy 60 % fault still caps what everything reads. Published: `electrical.charging` (the effective factor while running, 0 when not) and `electrical.dragStarts` (starts whose outcome was SlowCrank or whose crank was laboured; counted in `DoStart` before the tick, so the fourth drag spawns in the same command). New `Systems/Cooling.cs` (`CoolingSystem`): while running, the builder's raw-water flow below `EngineModel.OverheatBelowFlow` adds the step's minutes to `cooling.overheatMinutes` (published). `ElectricalRules` carries `CrankDraw`, `ChargePerHour`, `RunDrawPerHour`; the three go in the `electrical` block and the placeholders list.

Data: `alternator-belt-slipping` (present, latent; `chargeFactor multiply 0` on `alternator`; inspect discovers; replace; DIY 90 NOK / 20 min; yard 450 NOK / 1 day); `terminal-strained` (dormant, spawn `electrical.dragStarts >= 4`, `seriesOhms add 0.02`; inspect and measure discover; clean or replace; DIY 60 NOK / 30 min; yard 350 NOK / 1 day); `exhaust-hose-cooked` (dormant, spawn `cooling.overheatMinutes >= 10`, no effect; inspect discovers; replace; DIY 1500 NOK / 180 min; yard 2800 NOK / 2 days). Faults 20. All prices and thresholds are listed as placeholders.

`StopEngine` command ("stop-engine", 1 minute, anywhere aboard, refused for free with "The engine isn't running." when it is not): narrative "You pull the stop and the engine dies."; the engine is off before the minute passes, so the stop's own minute accrues nothing. The private helper that the repair commands call ("You shut the engine down first.") is now `ShutDownEngine`, to leave the name to the command. Adapters: catalogue "stop", console `stop` (also `kill`), MCP `stop_engine` (31 tools) and the help footer, web `GameSession.StopEngine` and a "Stop engine" button beside "Try to start" while the engine runs.

Decided where the notes were silent: the cooked hose has no leak (no cheap honest one; the consequence text is the warning); the start's own two minutes count as running (so an overheating start is 2 overheat minutes, and a start plus two flow checks spawns the hose); a start that Clicks still draws the crank charge; the dead-battery rule (OCV below 10.5 V) can never trigger from charge alone (OCV floor is 11.8 V), so there is no "no draw" start to test.

Tests changed: `BoatLoaderTests` fault count 17 to 20, `McpServerTests` tool list 31 (`stop_engine`). No scenario test broke: the 60 % battery drags only after the terminal is cleaned, and those scenarios start once or twice. New: `ChainTests` (12): the three chains end to end through the facade, stop-engine tests, no-leak checks (Status, Readiness, Aboard, Logbook and every CommandResult serialised).

What surprised: (1) the starter turns laboured below roughly 70 % charge with a healthy loop, so the belt chain needs about ten engine hours from a full battery to reach the first drag start; the 60 % starting battery drags at once after the terminal is cleaned. (2) After the terminal strains, even a full battery gives only SlowCrank (about 7.2 V at the starter): the engine will not start until the terminal is fixed. (3) At 100 mm of rain a day the drains alone burn the pump on day 10 and soak the starter on day 11; at 10 or 30 mm the clamps progress reaches 1 near day 30 and the hose comes off, which burns the pump the same day, so the "clamps only" chain only plays out in heavy rain (the chain test uses 100 mm). (4) A weeping seacock on the hard, with the engine run for ten hours, only moved 0.09 of its progress.

### (done) Step 5 — alerts, the boat talks

Built: `Play/Game.Alerts.cs`. `Alert(Day, TimeOfDay, Text)`, `NoticesView(Alerts)`, `CommandResult.Alerts` (last positional parameter; accepted commands carry every unread alert and mark them read, refusals carry `[]`), `StatusView.Notices` (unread, not marked; null when none), `LogEntry.Alert`, the `Notices` query (JSON `notices`, result `notices-view`, catalogue "notices"). Raising writes an alert `LogEntry` at once; each kind (`alternator`, `overheat`, `sole`, `battery`, `pump-counter`, `bilge-smell`) is raised at most once per game day. Present, per aboard step in `Tick`: engine running with `electrical.charging` ~0 (alternator lamp), running with raw-water flow below `OverheatBelowFlow` (temperature alarm), the level crossing the sole (tracked as `WasOverSole`), and on the first aboard step of each day the voltmeter when the effective SoC is under 0.5. Arrival, at the end of `Tick` when the call was away time of a day or more: the pump counter (then `CyclesSinceAboard` is reset, so the count is per away period and the next wait gives a new one), the bilge smell (level 30 L or more, under the sole, floorboards in), water over the sole, the voltmeter (real `ElectricalChain.OpenCircuitVolts`, F1 invariant). The aboard step still resets `CyclesSinceAboard` after `PresentAlerts`. Texts are perceptions, name what to check next, and contain no fault name (the test checks every fault name and id).

Adapters: `Prose.Result` prints `  ! text` lines after the narrative and stamp (before the narrative on a look); `Prose.Query` prints StatusView notices at the top, a NoticesView, or "Nothing new from the boat."; the logbook print marks alert entries `Day 80 07:00  ! ...`. Console verb `notices` (also `alerts`). MCP `notices` tool (32 tools) and the help footer. Web: alert entries get class `alert` (italic hand, fault red, a "the boat:" prefix) and the Readiness tab shows a badge of alerts since that tab was last shown (`GameSession.UnseenAlerts`, web-local).

Decided where the notes were silent: "away period of at least a day" is a Tick call with `!aboard` and at least 1440 minutes (so a one-day yard fix or lift also reads the counter); the morning battery alert is a once-a-day check on the first aboard step of each game day, not a continuous watch (a battery that dips under half mid-day speaks the next morning, or on the next arrival); the day-one check passes because the shipped 60 % is above the line; because every accepted command hands its alerts over, `StatusView.Notices` and the `Notices` query are normally empty in-process (they are the safety net for an adapter that dropped a result).

Tests changed: `GameScenarioTests.Full_play_through_via_the_facade` counts log entries without the boat's own alerts (starting the engine on the slipping belt now logs the lamp); `WaterTests.With_the_pump_the_level_stays...` no longer reads `PumpCyclesSinceAboard` (each wait now ends with the arrival alert, which resets it) and asserts the pump-counter alert is in the log; `McpServerTests` 32 tools; `ProseTests` builds `CommandResult` with the new `Alerts` argument. New: `AlertTests` (16) and two prose tests.

### (done) Review fixes

- **Readiness floor is now the worst case of leaving her as she is.** `LikelyDays` keeps the ceiling (trailing mean product over `spreadLo`) but computes the floor day by day with full rain and frost weights, the covers as they are now, neglect as if the player left today (current neglect covers counted as left off, known open faults and battery as now, the days-away term growing each day), only the fault's declared factors, and `spreadHi`; `TickTests` checks it over 20 seeds in constant heavy rain and frost, and the ceiling when tended in fair weather.
- **The cause line names the real absence.** `FaultHistory.LongestAway` records the longest run of days away, and the phrase reads "no one aboard for seven days" with no "while she sat".
- **Covers with `stateWords` log their own wording.** Optional `removeLog` / `refitLog` on a cover ("Pulled the bilge pump fuse." / "Put the bilge pump fuse back in.", "Slid the companionway hatch open." / "...shut."), falling back to "Took off" / "Put back".

### (done) Away-escalation fix

An escalation or spawn that happens in an idle tick (the player is away) discovers nothing and writes no narrative: the successor wakes Latent, its cause line is kept (`_pendingCause`) and told in `Discover`, just before the "Found:" line, when the player finds it by the normal reveal path. A fault may carry `arrivalSign: { text }` (`drain-hose-failed` does; `seacock-seized` has none); on coming back aboard, `ArrivalAlerts` raises each such successor's sign as an alert and marks it Symptomatic, with no "Found:" line. The present-player case is unchanged: an aboard tick crossing midnight, and the launch-time `onLaunch` escalation, still tell the escalation text, write the cause line and Discover a known successor on the spot, because the player is there to see it. What the player knows does not change until they look: a known fault that escalated unseen stays in Readiness (its range line is dropped), the known list and the component marker until the successor is found, and `defer`, `undefer` and `yard-fix` still name it (the yard puts right whatever is there; if the successor is fixed unfound the old item moves to Fixed). The sinking card still says what the insurer's man found.

### (done) Amit's decisions applied

Insurance is now decided at launch (`RecordLaunchCover`, overwritten at each launch, so a boat lifted, put right and relaunched is covered): any survey item marked Before launch whose fault, or what it escalated into, is still present voids the cover, whether the player knew of the fault or not (a Cause item, or an Investigate item never followed up); the closing card says "...and it was put off" if the player ever deferred it, else "...and it was never put right", naming a deferral first when both occur. A fault may declare `progression.rainOnlyWhenOff` (a list of cover ids): rain then counts in the exposure factor, the cause line and the readiness worst case only when one of those covers is off (at the day's end, or now), frost always counts, and the open-cover multiplier is unchanged; `seacock-weeping` names `saloon-floorboards`. The three weep and ingress rates and the frost/rain claim on seacock seizing are marked UNVERIFIED in the placeholders list and in a `note` on the faults (faults now accept an ignored `note`). Checked and unchanged: neglect rises 0.05 per day away beyond three, no seawater ingress on the hard, the failed drain hose leaks 60 L/h afloat, and the pump burns out only at 150 cycles or 300 run minutes.

## Decisions (Amit, 2026-11-08, accepting the proposed defaults)

1. Weep rates stay as placeholders, marked unverified: seacock 0.05 L/h, failed drain hose 60 L/h, rain 1.5 L/mm through rusted clamps.
2. Insurance pays 60 % of 180 000 NOK, void if a *Before launch* survey item was deferred **or left open** at launch.
3. Neglect baseline: a careful owner visits twice a week; the factor rises beyond three days away.
4. A weeping seacock does not leak on the hard; seawater ingress counts only afloat.
5. Cockpit drain through-hulls are just below the waterline: a failed drain hose can sink her at the mooring.
6. Seacock seizing is driven by frost; rain counts only with the floorboards up. Weakest physical claim in the round, marked unverified.
7. Pump calibration accepted: the burnt-out-pump chain needs a hose off or very heavy rain, and should be rare.
