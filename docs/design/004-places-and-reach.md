# 004 — Places, covers and reach

**Date:** 2026-11-07. **Decided by:** Amit (product). **Status:** adopted.

## Question

The first cutaway drew compartments as boxes with lists of parts. "Engine, side" and "Engine, front" sat next to each other as if they were rooms; they are two access panels onto one engine under the companionway. The drawing conflated where a thing is with how you get at it. Amit: confusing and misleading. What replaces it?

## Decision

The boat is modelled and drawn the way a sailor moves through it.

1. **Components have a true position.** Each component lives at one physical spot (engine under the steps, start battery under the quarter berth, tank under the cockpit locker). The overview cutaway draws them there.
2. **Places** are where the player stands, sits or kneels: cockpit, companionway, quarter berth, saloon, forepeak, on deck, on the ground beside the hull. The player is always at one place and moves between adjacent places (a command; costs a minute or two).
3. **Covers** are things you remove to see or reach what is behind them: companionway steps, the quarter-berth panel, the battery-box lid, a locker lid, floorboards. Removing one is a command with a time cost; some need tools later. A removed cover stays off until put back.
4. **Reach** is what the player can perceive and touch from a place, given which covers are off. Each component declares, per place, whether it is *visible*, *reachable* (can be touched: clean, replace, probe) or neither, and which cover gates that. "You can see the solenoid from the front but you can't get a probe on it from here" is a real line.
5. **Two levels in the UI, never more.** The overview cutaway is the map: parts in place, systems appearing as discovered, the hero image. Clicking a place zooms to a close-up: the parts you can see from there, drawn in situ, with the verbs. Zoom out returns to the map. No third level, no corridors.

## Why

- It matches a real boat, which is the whole promise (CONCEPT.md: "design for the first, never embarrass the second").
- Access is a diagnosable mechanic with real game moments, not just a view.
- It keeps the two-level limit, so the game does not become a click-maze and stays "chill".

## Costs accepted

- Per-boat art roughly doubles: one overview plus six to eight close-ups per hull, as ink drawings. Every future hull is a drawing project.
- The facade changes: observations are from a place. Console, MCP and web adapters all update.

## Order of work

1. Sim: places, covers, reach in the data and the facade; console says where you are and what you can reach; tests and the agent playtest prompt updated.
2. Web: the overview redrawn with parts in place; close-ups per place; zoom in/out.

## Open questions

- Does moving between places cost time in the slice, or is it free until the economy exists? Default: one minute, visible in the log.
- Do covers ever need tools (screwdriver for the panel)? Default: not in the slice.
