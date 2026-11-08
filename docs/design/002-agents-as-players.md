# 002 — AI agents as first-class players

**Date:** 2026-11-07. **Decided by:** Amit (product). **Status:** adopted.

## Question

Should AI agents be able to play Haulout, via an MCP server shipped with the game?

## Decision

Yes, as an architecture rule now and a dev tool soon; as a marketing promise only once the human game is fun.

1. `Haulout.Sim` exposes one facade: commands in (player actions), queries out (observations). The console, the Godot view and the MCP server are adapters over it and touch nothing else.
2. No adapter may expose state the player could not perceive in-game. The MCP returns the same partial, tool-mediated observations the console prints. No dump of the component graph or hidden fault state.
3. `Haulout.Mcp` is built right after the console loop works: one tool per player command, one resource for the logbook.
4. First use is an automated agent playtest: run a model against a fault and record steps-to-solve. This is the difficulty gauge for every fault we add. Trivial means the loop is shallow; unsolvable means the fault is not diagnosable (pillar 1).

## Why

- Nearly free: the sim was already commands in, state out.
- A two-person team cannot playtest a diagnostic loop enough. Agents can, at scale.
- A novel hook ("the boat sim you can hand to your agent") that costs nothing to keep open.
- Seeds the later async layer: agents as first brokers, yards and buyers so the economy is not empty.

## Guardrails

- Agents are not the audience. The game is a human feeling competent care. If agent play starts steering design, stop.
- Agents as NPCs (an LLM-driven previous owner, surveyor, yard foreman) is a separate decision with cost, latency and quality risk. Not decided here.

## Open questions

- Which model and prompt define the "reference agent" for difficulty measurements?
- Does the agent playtest run in CI, or on demand?

## Implementation notes

**Date:** 2026-11-07.

### The server (`src/Haulout.Mcp`)

- stdio MCP server on the official C# SDK (`ModelContextProtocol` 2.4.0, Microsoft.Extensions.Hosting). Stdout is the protocol; all logging goes to stderr.
- One game per process: `Game.NewFirstFault()`, the same start as the console. Restart the server for a new game.
- Tools, in the player's words: `look`, `open`, `inspect`, `measure`, `clean`, `replace`, `charge`, `buy`, `check_filter`, `check_flow`, `start_engine`. Read-only: `status`, `logbook`, `aboard`, `help`, `briefing`. Each tool description states the fixed time and money cost.
- Every tool returns plain prose, rendered by `src/Haulout.Prose` — the renderer the console uses, lifted out of the console's ResultPrinter, so both adapters print the same text. Ids are never rendered.
- Run it: `dotnet build src/Haulout.Mcp`, then `dotnet src/Haulout.Mcp/bin/Debug/net8.0/Haulout.Mcp.dll`. `.mcp.json` at the repo root registers it as `haulout` for Claude Code sessions in this folder. It points at the built DLL rather than `dotnet run --no-build`: about 220 ms to `initialize` warm against about 430 ms, and no MSBuild evaluation on startup. Both need a prior build.
- `HAULOUT_TRACE=<file>` makes the server append one JSON line per tool call: `seq`, `tool`, `args`, `accepted`, `result` (first 80 characters), then the game status after the call: `minutesSpent`, `moneySpentNok`, `engineRunning`, `finished`. The trace is for the harness only and is never shown to the agent.

### The playtest (`tools/playtest/`)

- `tools/playtest/playtest.sh [sonnet|opus|haiku]` (default sonnet) does four things:
  - builds `src/Haulout.Mcp`, but not the whole solution;
  - writes a per-run MCP config with the trace path;
  - runs headless Claude Code (`claude -p`) with the prompt from `prompt.md`, using only the haulout tools (`--strict-mcp-config`, `--allowedTools "mcp__haulout__*"`, built-in tools disallowed);
  - saves the output in `tools/playtest/runs/<timestamp>-<model>.{json,trace.jsonl}` and prints the score. `PLAYTEST_DRY_RUN=1` prints the command without running it.
- `prompt.md` tells the agent only what a human is told: the briefing, the goal (get the engine running for as little time and money as possible), `DONE <n>` when it runs, and `GAVE UP <n>` after 40 calls.
- `tools/playtest/score.py <trace>` (or `score.sh`) prints one line, for example `solved=yes steps=6 actions=6 queries=0 minutes=521 money=0 decoys=1 rejected=0 model=sonnet`:
  - `solved`: the engine ran.
  - `steps`: tool calls up to and including the first call after which the engine ran (all calls if it never ran).
  - `actions` and `queries`: steps split into commands that cost time and the free questions.
  - `minutes` and `money`: in-game cost at that point.
  - `decoys`: accepted clean/replace/charge actions on anything other than the fault's real fix.
  - `rejected`: commands the game refused, such as unknown names.
- The fix target is the `FIX_COMPONENT_IDS` constant in `score.py`, and each fault needs its own.
- Since the first trip (007) the trace keeps the first 300 characters of each result (was 80), so arrival, tow and sinking cards show, and each line also carries `day`, `port` (null at sea), `atSea` and `sunk` from the status. The score line keeps the fields above and adds the trip, read from those fields over the whole trace, for example `... rejected=0 departed=2 arrived_out=yes arrived_home=yes towed=0 sank=no hours_at_sea=6.0 goal=yes model=sonnet`:
  - `departed`: accepted `depart` calls.
  - `arrived_out` / `arrived_home`: she came off the sea into Hestholmen / Skjærvik at least once (by any means, a tow included).
  - `towed`: accepted `call_tow` calls.
  - `sank`: she sank at any point.
  - `hours_at_sea`: work time that passed while she was at sea (the tow's lost idle hours are not counted).
  - `goal`: yes only when she reached Hestholmen and later got back to Skjærvik, neither arrival by tow. Old traces without the status fields score no trip.

### Reference agent

Sonnet, with `prompt.md` as written, is the reference agent for difficulty. A fault is a good fault when Sonnet solves it in a moderate number of steps, with some wasted effort. If Sonnet solves it straight away, the loop is too shallow. If it fails, the fault is not diagnosable. Opus and Haiku give the upper and lower bounds. The playtest runs on demand, not in CI, because each run costs model usage and needs a logged-in `claude`.

A run normally takes under a minute. `playtest.sh` kills the agent after `PLAYTEST_TIMEOUT` seconds (default 300) and says so; anyone waiting on a run should give up on the same scale, not hours.

## Playtest log

| Date | Fault | Model | Solved | Steps | Game time | Money | Notes |
|---|---|---|---|---|---|---|---|
| 2026-11-07 | 001 corroded negative terminal | sonnet | yes | 13 | 1 h 30 min | 0 | Went straight to the battery box on the "click" symptom, inspected both terminals, cleaned the negative on the visible bloom alone. Never bought the remote start, never used the meter. Also cleaned the healthy positive terminal. |
| 2026-11-07 | 001 corroded negative terminal | haiku | yes | 23 | 1 h 13 min | 0 | Wandered first (saloon, wrong turn to the quarter berth), then the same path: battery box, inspect terminals, clean the negative on the bloom. No meter, no remote start, no decoys touched. |
| 2026-11-07 | 001, boot hidden | haiku | yes | 18 | 1 h 19 min | 0 | Went to the battery box, inspected, then used the new Uncover verb on the negative terminal and cleaned. Still no meter, no remote start. The Uncover verb appearing in the actions list is itself a hint. |
| 2026-11-08 | 001, boot hidden | sonnet | yes | 15 | 0 h 56 min | 0 | Straight to the battery box, uncovered both terminals, cleaned the negative. No meter, no remote start. Confirms finding 2: the Uncover verb is the hint now. |
| 2026-11-08 | 001, systems round (006) | sonnet | yes | 18 | 1 h 21 min | 0 | Battery box, uncovered both terminals, cleaned the negative. No meter. Same path as before; the new systems did not change the puzzle. |
| 2026-11-08 | 001, systems round (006) | haiku | no | 36 | 10 h 38 min | 400 | First run to use the meter properly: bought the remote start, measured the starter, positive terminal and both cables in turn, and ran out of its 36-action budget before reaching the negative terminal. Also charged the battery (decoy). Hit the limit, did not fail outright. |
| 2026-11-08 | 007 first trip (goal: Hestholmen and back) | sonnet | engine 19 steps; full season in 108 calls | 108 | — | — | Fixed the terminal (uncover, no meter), read the alternator-lamp alert and replaced the belt, bought impeller and filter, launched, departed, hove to in a force 5 and changed the fuel filter at sea, reached Hestholmen, bought spares there, sailed home, hove to again for a second filter. Declared DONE on the way home; the 80-char trace cannot confirm arrival (tooling fix requested). |
| 2026-11-08 | 007 first trip (goal: Hestholmen and back) | haiku | engine yes, 36 steps, with the meter | 115 (hit the CLI turn cap) | — | 400 | First run to find the terminal by measurement (6.24 V drop). Replaced belt and impeller, launched, then took an emergency lift to replace the seized seacock. Ran out of the CLI's 60-turn budget before departing. Cap raised to 250. |
| 2026-11-08 | 008 season (goal: Hestholmen, haul out, lay up, sell) | sonnet | engine 17 steps; sank day 108 | ~90 | — | 9 000 (emergency lift) | Fixed the terminal (no meter), charged, replaced belt, impeller and exhaust hose, launched, found the seized seacock afloat and paid the emergency lift, booked the haulout for day 255, motored to Hestholmen and back with no tow. Saw the bilge pump run on the way home and did not look; then waited 60 days without going aboard. The pump burnt out, she flooded and sank at her mooring; the insurer paid 108 000. Finding 3 below. |
| 2026-11-08 | 001 after the verb fix (finding 2) | haiku | no | 149 | 29 h 50 min | 18 550 | First run with no verb hint. Bought the meter, measured the cables, switch, solenoid, ground stud and starter (4.78 V while cranking against 12.6 at the battery) but never the two terminals, and never summed the drops. Then replaced eleven healthy parts (starter, injectors, exhaust elbow, thermostat, battery, glow plugs, raw-water pump…) and gave up suspecting the block. Finding 4 below. |
| 2026-11-08 | 008 season after the verb fix and the circuit-gap line | haiku | engine 26 steps; full season in 180 calls | 180 | — | 20 918 total | First full season by any model. Uncovered both terminals, inspected both, found the crust and cleaned it: the brute-force path, never the meter, so the gap line was never seen. Then belt, impeller, seacock, filter; launched day 79; Hestholmen and back with a filter change at sea; replaced the drain clamps and the mooring lines on the chafe warning; hauled out day 255; oil and filter; sold day 256 for 143 000. |
| 2026-11-08 | 008 season after the subtler inspect | haiku | engine 28 steps, 2 h 37 min, with the meter; full season in 187 calls | 187 | — | 400 | First clean diagnosis by measurement: inspected both terminals (nothing conclusive), bought the remote start, measured the battery, the positive terminal (0.03 V) and the negative (6.24 V), cleaned it, measured again (0.06 V). No decoys. Belt, impeller, seacock; Hestholmen and back with a filter change at the harbour; hauled out day 255; wintered her and sold in spring, day 449, for 156 500 against 143 000 for the autumn sale in the previous run. |
| 2026-11-08 | 010 wants and means (goal: engine, regatta, Midsummer, haul out, keep) | haiku | engine 29 steps with the meter; full season in 240 calls | 240 | — | 400 on the fix; balance 30 405 at the Keep | Clean diagnosis again (remote start, battery, both terminals: 6.24 V across the negative; cleaned; 0.06 V). Then belt, impeller, seacock; booked the lift for day 255 on day 82; played the whole season as `next_weekend` × 25, coming back early three times when the harbourmaster called (mooring chafe, drain clamps). Made the regatta (day 152) and Midsummer (one vacation day for the Friday); never invited the friend and fitted no upgrade; gave up the club cruise at `plan_trip`'s "no route from Skjærvik to Kvitnes". Hauled out day 255, full layup (winterise, battery ashore, tank, oil, cover), kept her. Balance never below 23 900: the paydays outran everything. Finding 5 below. |
| 2026-11-08 | 010 second pass (pay 3 500, 12 vacation days, fuel by hp, fog from dew point, boat as sold with old heads, stove and frozen shackle) | haiku | engine 32 steps with the meter; full season in 372 calls | 372 | — | 400 on the fix; balance 5 954 at the Keep | Meter diagnosis again. Replaced the seacock through a Tuesday night (vacation day charged, worked 01:00–04:00: the clock has no night). Departed day 82 with the as-sold water in the fuel: the engine coughed and died an hour out; found the bowl on the second try, drained water on days 82, 89 and 96, new element, then Hestholmen and back. Marked the regatta and Midsummer with `plan_event`; fixed the frozen shackle after `inspect` on deck; impeller on the weak-flow warning before Midsummer; 3 vacation days. Never invited the friend, no upgrade, skipped the cruise. Balance low 12 890 after the seacock, 26 074 before the lift, 5 954 after the lift and layup. Finding 6 below. |

Finding 1: the visible green bloom on inspect made the multimeter unnecessary. Hidden under the boot since 86a9faa.
Finding 2 (after hiding): a model still skips the meter if an Uncover verb is offered on the suspect part. A verb list is a hint. Options: offer Uncover on every booted/covered part so it carries no signal, or make the boot something you notice only on inspect ("the boot could be pulled back").

*Done:* which verbs a component offers depends only on what kind of part it is, never on whether it has a fault: `sloop28.json` has a `kinds` table (each kind lists its verbs) and every component names a `kind`; `ComponentActions` and the command handlers both read the kind, the loader refuses a kind that contradicts the data (a removable part without `uncover`, a replace price without `replace`, a fault fixed by a verb the kind lacks), and `VerbKindTests` checks that the options do not move with the faults.

Decision (Amit, 2026-11-08): uncover-then-inspect must not be a sure path to the terminal fault; what the eye sees under the boot is suggestive on both terminals, never conclusive, and only the meter or the clean (which pulls the clamp and finds the crust) resolves it. Built as data: the uncovered inspect text of every uncoverable part is in the same "dull, a trace of bloom, snug" register, and a fault's non-discovering `inspect` reveal with `needsUncovered` stands in for (replaces, never adds to) the part's `uncoveredText`, so the corroded terminal reads "white bloom along the edge" where the healthy one reads "a trace of white bloom at the edge"; the loader refuses such a reveal on a part with no `removablePart`. The ground stud's `ground-stud-soaked` fault and the impeller's cover-off inspect are left as they were.

### Finding 3: a long wait at a mooring is blind

The sinking in the 008 run took two months of nobody looking. The sim is right that she sinks (rule 3: consequences delayed, never hidden), but in a real marina someone rings you when a boat sits low. Proposed: a plain `WaitDays` is interrupted the way `WaitUntilSpring` already is when the water passes the sole at a mooring ("The harbourmaster rang: she's sitting low"), with the days actually waited reported. The player still has to go and find the leak. For Amit.

*Done (approved by Amit): see "(done) Harbourmaster" in `008-season-and-haulout.md`.*

### Finding 4: without the hint, the meter needs arithmetic

With the verb hint gone, Haiku measured every part of the chain but the terminals and did not notice that the drops it had (0.16 + 0.05 + 0.11 + 0.05 V) do not account for 12.6 V at the battery against 4.8 V at the starter. A sailor with a meter would. Proposed, for Amit: when the starter voltage has been measured, the meter's result names the gap without naming a part ("7.8 V lost somewhere between the battery posts and the starter; you have accounted for 0.37 V of it"). Diagnosable, not a hint at the terminal.

*Done (approved by Amit):* once the starter has been measured while cranking, every cranking measurement that day (a drop or the starter) carries a `CircuitGap` (gap volts from the model's battery-minus-starter chain, accounted volts summed from the drops the player was shown today) and Prose renders one line from it ("7.8 V lost somewhere in the starting circuit while cranking; your readings so far account for 0.37 V of it"), naming no part. A gap up to 2.0 V is ordinary for a sound chain and reads as such; otherwise unaccounted volts within 0.5 V read as "nothing lost that your readings don't account for"; readings are forgotten when the day changes or when a repair makes them differ from the current chain.

*Note on the 180-call run (superseded by Amit's decision above and the 187-call run):* with uncover offered on every terminal, uncover-then-inspect on both is now the cheapest path to the fault (about 15 minutes), and the meter was not needed. Whether that is acceptable is open question 4 under finding 2, for Amit.

### Finding 5: with 010's shipped numbers the money never bites and the hard wants are skipped

The first run under 010 confirms the lead's own review. Haiku was paid 36 000 NOK across the season and spent about 8 000 on parts, so the balance climbed from the first weekend and the overdraft was never a thought. It took the two free wants (the regatta and Midsummer fall on weekends and ask nothing of the boat) and skipped the two that cost something: the friend's week needs 16 500 NOK of upgrades and five vacation days, and the club cruise needs a route the trip planner does not know (Kvitnes is reached only via Hestholmen; `plan trip kvitnes` from home says "no route"). Two vacation days of 25 were used. The season is still won by waiting: 25 `next_weekend` calls carried it from the launch to the lift.

What the run says about the knobs: pay at 6 000 a month against about 22 000 of bills a year leaves a surplus that nothing claims; the friend's week is the only want that asks for money, and an agent that does not care about a fictional friend has no reason to pay. Candidates, for Amit: pay nearer 3 000–4 000, or a sinking fund the player must keep (insurance that rises with claims, an engine that needs its service hours), or wants that pin to weekdays. Also to fix regardless: the planner must route via Hestholmen or say so, and the friend's week needs something to happen during it.

### Finding 6: the second pass makes the money a budget but not yet a choice; the clock has no night

With pay at 3 500 the balance now ends the season near zero (5 954 after the lift) instead of 30 000, so the bills are felt. But nothing was bought beyond repairs: the heater at 14 000 is the only costly want left after the heads and stove became repairs, and an agent that does not care about the friend has no reason to buy it. The friend's week and the club cruise were skipped for the second run in a row; the two free wants were made. Vacation days: 3 of 12. So the budget exists and the choice does not, yet. Candidates: a want that needs money and that the player wants for themselves (the plotter for a fog-bound regatta morning; a heater for an autumn passage the calendar makes attractive); a year-two insurance renewal priced by the season's claims; parts that wear by the hour so the engine wants its service money.

Also seen: the clock runs through the night. Haiku replaced the seacock from 01:00 to 04:00 on a Tuesday, and the first run ordered parts at 03:20. The evening rule charges the day correctly, but nobody works at three in the morning and the chandlery is shut. The day needs an end: work past about 22:00 should stop ("it's late; you turn in") and resume at 08:00, with the yard and chandlery keeping hours. This is a fix for the next round, not a knob.

The probes' "engine silence" (lead's review) was investigated and is not a bug: the as-sold water in the fuel kills the engine in a seaway with its cough warning and filter-bowl symptom first, and an unserviced impeller overheats her after weeks with `check flow` weakening for several passages before. Both chains are diagnosable; the probes ignored them. The console now honours `--seed`, which it silently ignored before.
