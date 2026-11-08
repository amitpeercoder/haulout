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
