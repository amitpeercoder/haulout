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

### Reference agent

Sonnet, with `prompt.md` as written, is the reference agent for difficulty. A fault is a good fault when Sonnet solves it in a moderate number of steps, with some wasted effort. If Sonnet solves it straight away, the loop is too shallow. If it fails, the fault is not diagnosable. Opus and Haiku give the upper and lower bounds. The playtest runs on demand, not in CI, because each run costs model usage and needs a logged-in `claude`.

## Playtest log

| Date | Fault | Model | Solved | Steps | Game time | Money | Notes |
|---|---|---|---|---|---|---|---|
| 2026-11-07 | 001 corroded negative terminal | sonnet | yes | 13 | 1 h 30 min | 0 | Went straight to the battery box on the "click" symptom, inspected both terminals, cleaned the negative on the visible bloom alone. Never bought the remote start, never used the meter. Also cleaned the healthy positive terminal. |

Finding: the visible green bloom on inspect makes the multimeter unnecessary for a player who knows the symptom. The meter lesson is only taught if the hint is removed or made conditional (e.g. only after lifting the boot).
