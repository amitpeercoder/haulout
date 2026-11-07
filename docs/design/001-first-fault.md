# 001 — First fault: "the engine won't start"

**Question.** What is the smallest sim that makes "click, no crank" a fun detective loop in a terminal?

**Decision.** A data-driven component graph (`Data/sloop28.json`, 40 components in 8 compartments), faults as first-class objects on components, a pure rule table for the start attempt, and a command-driven `Game` facade that the console only feeds commands into. The fault layout is fixed; no randomness in this slice.

## Component graph (40)

| Subsystem | Components |
|---|---|
| Electrical (10) | start battery, battery switch, positive terminal, main positive cable, negative terminal, negative cable, engine ground stud, starter solenoid, starter motor, ignition panel |
| Fuel (9) | tank, shutoff valve, supply line, primary filter / water separator, lift pump, secondary filter, injection pump, injectors, return line |
| Cooling (10) | raw-water seacock, strainer, raw-water pump, impeller, heat exchanger, thermostat, coolant, exhaust elbow, exhaust hose, anti-siphon loop |
| Engine (6) | engine block, oil, oil filter, alternator, alternator belt, glow plugs |
| Hull (5, placeholders) | hull, keel, rudder, standing rig, antifouling |

Compartments the player can open: battery box, cockpit, cockpit locker, engine front (steps off), engine side (quarter-berth panel), bilge under the steps, hull outside, deck. Each component carries: id, name, subsystem, location, condition 0..1, optional healthy series resistance, cleanable flag, replace cost and hours, and its faults.

## Fault model

A fault lives on a component: `id`, mechanic's `name`, `present`, `discovered`, one **effect** (extra series ohms, battery state of charge, fuel restriction 0..1, raw-water flow factor), a list of **reveals** (action → text, and whether that action *discovers* it or only *hints*), and the actions that **fix** it (clean, replace, charge). Replacing a component clears all its faults and sets condition to 1.

| Fault | On | Effect | Revealed by | Fixed by |
|---|---|---|---|---|
| **Corroded negative terminal** (the cause) | negative terminal | +0.060 Ω | inspect: hint (green bloom at the boot); measure while cranking: discovers (~6 V drop); clean: discovers as it is fixed | clean, replace |
| Battery at 60 % (decoy) | start battery | SoC 0.6 → 12.28 V | measure: discovers | charge (8 h), replace |
| Primary filter half clogged (decoy) | primary filter | restriction 0.5 | check filter bowl: discovers | replace element |
| Tired impeller (decoy) | impeller | flow ×0.4 | inspect: discovers; check flow (running): hint | replace |

Every symptom in the start result traces to a component; every fault has at least one discovering action (tested).

## Engine rule table

Electrical path is one series loop: battery internal → + terminal → + cable → switch → solenoid contacts → starter → ground stud → − cable → − terminal. `I = OCV / ΣR`; voltage at the starter `= I × R_starter`. Battery OCV `= 11.8 + 0.8 × SoC` (12.6 V full, 12.2 V at 50 %). Healthy loop ≈ 0.059 Ω → ~215 A, ~9.6 V at the starter. With the corroded terminal ≈ 0.119 Ω → ~104 A, ~4.7 V at the starter, ~6.2 V lost across the terminal.

Evaluated top to bottom, first match wins:

| # | Condition | Outcome |
|---|---|---|
| 1 | switch off, open circuit anywhere, ignition panel dead, or OCV < 10.5 V | nothing |
| 2 | starter voltage < 6.0 V | click |
| 3 | starter voltage < 8.0 V | slow crank |
| 4 | no fuel (tank < 2 L, shutoff closed, lift pump dead, air in fuel) or glow plugs dead | cranks, no fire |
| 5 | fuel restriction ≥ 0.85 | fires and dies |
| 6 | fuel restriction ≥ 0.6, or injectors < 0.3 | runs rough |
| 7 | otherwise | runs |

Cooling is a warning on a running engine, not a gate: raw-water flow (seacock open × impeller × strainer) below 0.25 sets an overheat warning. The slice ends on *runs* without the warning.

Decoys are tuned so each fixed one leaves the outcome at *click*, and fixing only the terminal (with all decoys still present) gives *runs*.

## Player interface (adapter-neutral)

Players will be humans in a terminal now and AI agents over MCP later, so the sim exposes one facade, `Game`, with two doors: `Execute(Command)` for actions (Look, Open, Inspect, Measure, Start, Clean, Replace, Charge, CheckFilterBowl, CheckRawWaterFlow) and `Ask(Query)` for observations (Status, Logbook, Compartments, Commands). Commands name things the way a player would ("negative terminal"); the sim resolves names. Every result is a plain, JSON-serialisable record that carries only what the player could perceive: narrative text, meter readings, what is visible, discovered faults. Never conditions, hidden faults or the graph. A component can only be named once the player has seen it (Look shows the cockpit, deck and hull; Open shows a compartment); an unseen part and a non-existent one get the same refusal. The console only parses words into commands and prints results.

## Actions (time / money)

look 2 min; open 2 min; inspect 5 min; measure 10 min; start 2 min; check filter bowl 10 min; check flow 5 min; clean 30 min; charge 8 h; replace per component (e.g. terminal 30 min / 60, battery 1 h / 1 900, starter 3 h / 3 800). All recorded in the logbook.

## Decisions (Amit, 2026-11-07)

1. **Ashore.** She is on the hard with the yard hose on the raw-water intake. Standard practice; the first fault happens before launch.
2. **Starter thresholds** (click < 6 V, slow crank < 8 V, starter 0.045 Ω) stay as placeholders, marked *unverified* until a mechanic confirms.
3. **No helper modelled.** A remote start button is a cheap tool the player can buy; solo measurement is possible but costs something.
4. **Glow plugs:** dead plugs in a Nordic March give "cranks, no fire" or "fires and dies" depending on temperature. 0.8 Ω placeholder, *unverified*.
5. **Low battery cranks slower** once the terminal is fixed, and the start text says so. Otherwise the decoy teaches nothing.
6. **Corrosion difficulty** stays hint-plus-meter. Difficulty comes from decoys and from later faults with no visible hint.
7. **Prices and hours** stay placeholders until there is an economy; calibrate them all against a season's budget.
