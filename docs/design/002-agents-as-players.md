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

Finding 1: the visible green bloom on inspect made the multimeter unnecessary. Hidden under the boot since 86a9faa.
Finding 2 (after hiding): a model still skips the meter if an Uncover verb is offered on the suspect part. A verb list is a hint. Options: offer Uncover on every booted/covered part so it carries no signal, or make the boot something you notice only on inspect ("the boot could be pulled back").

*Done:* which verbs a component offers depends only on what kind of part it is, never on whether it has a fault: `sloop28.json` has a `kinds` table (each kind lists its verbs) and every component names a `kind`; `ComponentActions` and the command handlers both read the kind, the loader refuses a kind that contradicts the data (a removable part without `uncover`, a replace price without `replace`, a fault fixed by a verb the kind lacks), and `VerbKindTests` checks that the options do not move with the faults.

### Finding 3: a long wait at a mooring is blind

The sinking in the 008 run took two months of nobody looking. The sim is right that she sinks (rule 3: consequences delayed, never hidden), but in a real marina someone rings you when a boat sits low. Proposed: a plain `WaitDays` is interrupted the way `WaitUntilSpring` already is when the water passes the sole at a mooring ("The harbourmaster rang: she's sitting low"), with the days actually waited reported. The player still has to go and find the leak. For Amit.

*Done (approved by Amit): see "(done) Harbourmaster" in `008-season-and-haulout.md`.*

### Finding 4: without the hint, the meter needs arithmetic

With the verb hint gone, Haiku measured every part of the chain but the terminals and did not notice that the drops it had (0.16 + 0.05 + 0.11 + 0.05 V) do not account for 12.6 V at the battery against 4.8 V at the starter. A sailor with a meter would. Proposed, for Amit: when the starter voltage has been measured, the meter's result names the gap without naming a part ("7.8 V lost somewhere between the battery posts and the starter; you have accounted for 0.37 V of it"). Diagnosable, not a hint at the terminal.

*Done (approved by Amit):* once the starter has been measured while cranking, every cranking measurement that day (a drop or the starter) carries a `CircuitGap` (gap volts from the model's battery-minus-starter chain, accounted volts summed from the drops the player was shown today) and Prose renders one line from it ("7.8 V lost somewhere in the starting circuit while cranking; your readings so far account for 0.37 V of it"), naming no part. A gap up to 2.0 V is ordinary for a sound chain and reads as such; otherwise unaccounted volts within 0.5 V read as "nothing lost that your readings don't account for"; readings are forgotten when the day changes or when a repair makes them differ from the current chain.

*Note on the 180-call run:* with uncover offered on every terminal, uncover-then-inspect on both is now the cheapest path to the fault (about 15 minutes), and the meter was not needed. Whether that is acceptable is open question 4 under finding 2, for Amit.
