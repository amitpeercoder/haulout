# Haulout — Concept

*The boat was fine last year.*

## Vision

A sandbox of sailboat ownership with all the pain intact. You buy a tired boat, keep it alive through the seasons, sail it somewhere, and eventually sell it to the next sucker. No winning, no racing, no combat. The fantasy is competent care: understanding a complex object well enough to trust it offshore.

Farming Simulator for boats, with Dredge's atmosphere and a diagnostic loop that makes real sailors wince in recognition.

## Pillars

1. **Diagnosable depth.** Systems are simulated down to cables, hoses and crimps, but only where cause → symptom → investigation → fix is learnable. Depth you can't reason about is cut.
2. **Seasonal rhythm.** Winter layup, spring launch deadline, summer cruising, autumn haul-out. Time is scarce, the yard is booked, and every deferred task compounds.
3. **Previous-owner archaeology.** Every used boat is a mystery of someone else's decisions. Discovery, not instruction, is how you learn the boat.

## Core loop

Check readiness → pick a task (DIY or pay) → do it, log it → sail somewhere → something breaks → diagnose → repeat. The scoreboard is the readiness of the boat against the trip you want to make.

Consequences are delayed (1–6 months) so causality is learnable but never instant. Money, hours, skill and deferral are the four levers.

## Audience

Primary: the aspirational sailor — "I'd love a boat someday" — Steam sim audience (Farming Sim, Euro Truck, House Flipper). Secondary: actual boat owners, who validate authenticity and drive word of mouth. Design for the first, never embarrass the second.

## Atmosphere

Nordic late summer. Grey-blue water, drizzle on the sprayhood, the marina at 06:00. Calm, slightly melancholic, competent. Sound does half the work: halyards, gulls, VHF, an engine note that changes when something is wrong.

## Presentation

Three views, no free-roam 3D:

- **Cutaway** — stylized side-section of the hull; lockers open, systems appear as you discover them. The signature image.
- **Chart** — top-down paper chart for planning and passages; weather, track, events as icons and text.
- **Logbook** — all UI as a physical book: survey, receipts, maintenance log, letters from the yard.

Marinas as low-poly 2D backdrops; diagnostic moments as lightly animated close-ups.

## Explicitly out of scope (v1)

- Free-roam 3D sailing or twitch sailing physics
- Racing
- MMO; multiplayer is a later async layer (player market, shared marinas)
- Real-world charts and real brands (fictional archipelago, fictional brands)
- Microtransactions

## Vertical slice

Buy a tired 28-footer. Get it through one season. Sell it.

One hull, one marina, ~40 components, one fault at start (engine won't start), a survey report with twelve items, one trip goal, two or three emergent failures at sea. If this is fun for ten hours, the rest is scope.

## Build order

1. Sim core as a pure, engine-independent library with tests. Text interface. Play it in a terminal.
2. Engine decision (default: Godot 4, C#). Godot is the view; the sim core stays separate.
3. Cutaway prototype: one hull, one marina, logbook UI, the first fault. That is the demo clip.

## Success

- Vertical slice: a sailor plays it and says "that's exactly what happened to me."
- Demo clip: the cutaway at night, cabin light on, one corroded terminal in red, rain on deck.
- Launch: Steam, PC first. Early Access when one full season is solid.

## Team

Two people. Amit: product, art direction, Blender, sailing domain. Claude: code, systems, sim. One contract artist later for hero boats and key illustrations.
