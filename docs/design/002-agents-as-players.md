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
