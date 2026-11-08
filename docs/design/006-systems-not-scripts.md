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
