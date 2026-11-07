# Haulout — instructions for Claude sessions

Read `CONCEPT.md` first. It is the document we argue with; this file is how we work.

## What this is

A sailboat-ownership sim. Buy a tired boat, keep it alive through the seasons, sail it, sell it. No winning, racing or combat. See CONCEPT.md for pillars, loop, atmosphere and scope.

## Non-negotiable rules

1. **The sim core is engine-independent.** `Haulout.Sim` has no dependency on Godot or any UI framework. It is a pure .NET library, driven by commands, observed through state. If a feature needs the engine to exist, it is in the wrong layer.
2. **Depth must be diagnosable.** Every simulated detail has a path: cause → symptom → investigation → fix. If the player cannot reason about it with in-game tools, cut it.
3. **Consequences are delayed, never hidden.** Deferred work compounds visibly. Randomness is allowed; unexplained randomness is not.
4. **Text first.** Every mechanic is playable in `Haulout.Console` before it gets a view. If it is not fun as text, no art will save it.
5. **Fictional world.** Fictional archipelago, fictional brands. Real standards (wire sizing, service intervals) are fine; real names are not.

## Build order

1. Sim core + tests + console (now).
2. Engine decision after the sim is fun in a terminal. Default: Godot 4 with C#.
3. Cutaway prototype: one hull, one marina, logbook UI, the first fault.

## Repository layout

```
CONCEPT.md            vision, pillars, scope
CLAUDE.md             this file
docs/design/          design notes: component graph, degradation, economy
src/Haulout.Sim/      the sim core (library)
src/Haulout.Console/  text interface
tests/Haulout.Sim.Tests/
```

## Code conventions

- C# 12, .NET 8, nullable enabled, warnings as errors.
- Domain types are records or small classes with explicit state; no static mutable state.
- Simulation time is a `GameClock` value, never `DateTime.Now`. All randomness goes through an injected `IRandom` so runs are replayable.
- Units are explicit in names: `hours`, `volts`, `ohms`, `litres`, `nok`. The in-game currency is a single abstract unit; never store two currencies.
- Tests: xUnit. Every component rule and degradation curve has a test. Scenarios ("the windlass is sluggish") are tests too.
- Keep the component model data-driven where it is cheap (JSON/YAML under `src/Haulout.Sim/Data`), code where the rule is logic.

## Working style

- Small commits with a one-line summary of the design decision, not just the change.
- When a design question comes up, write the decision into `docs/design/` and link it from the commit.
- Domain questions (how a boat actually behaves) go to Amit. Do not invent sailing facts; flag uncertainty.

## Team

Amit: product, art direction, Blender, sailing domain. Claude: code, systems, sim.
