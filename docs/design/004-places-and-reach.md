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

## Implementation notes

**Date:** 2026-11-07. Order of work step 1 (sim, console, MCP) is done. The web view still uses the old cutaway, with a compile fix only.

### Place graph

The player starts in the cockpit. Moving to an adjacent place takes 1 min and is logged. Look takes 2 min.

- cockpit: companionway, on deck
- companionway (in the hatch / on the steps): cockpit, saloon, quarter berth
- quarter berth: companionway
- saloon: companionway, forepeak
- forepeak: saloon
- on deck: cockpit, beside the hull (down the ladder)
- beside the hull: on deck. Only while she is on the hard; this is always true in the slice.

### Covers

| Cover | Removed from | Remove / refit |
|---|---|---|
| Companionway steps | companionway | 3 / 3 min |
| Quarter-berth panel | quarter berth | 5 / 5 min |
| Battery-box lid | quarter berth | 2 / 2 min |
| Cockpit-locker lid | cockpit | 2 / 2 min |
| Saloon floorboards | saloon | 4 / 4 min |

No cover needs tools. There is no engine-front sound panel.

### Positions and reach

Each component has one true position:

- engine bay, front (under the steps)
- engine bay, quarter-berth side
- under the quarter berth
- under the cockpit locker
- bilge under the saloon
- cockpit
- deck
- hull, outside

Each component also has a reach list in `sloop28.json`. A reach entry names a place, an optional cover that must be off, and whether the component is visible from there and whether it is reachable. Inspect needs the component to be visible from the current place. Measure, clean, replace, charge and check filter need it to be reachable.

Two commands are tied to a place rather than to a component:

- Start works only from the cockpit, where the key is.
- Check flow works only from outside (the cockpit, on deck or beside the hull), where you can see the exhaust outlet at the transom.

The layout:

- **Quarter berth, panel off.** The engine's fuel side: starter motor, solenoid, ground stud, lift pump, secondary filter, injection pump, return line, glow plugs, oil filter and exhaust elbow.
- **Companionway, steps off.** The front of the engine: belt, alternator, raw-water pump, impeller, oil filler, coolant cap, thermostat, heat exchanger and the injectors on top. From here the solenoid is visible but cannot be reached, and the starter cannot be seen at all.
- **Fuel filters.** The primary filter / water separator is on the cockpit-locker bulkhead beside the tank. It sits upstream of the lift pump and close to the tank, where its bowl can be seen and drained without opening up the engine. The lift pump and the secondary filter are on the engine block, on the fuel side.

### Refusals

Refusals are free and not logged. There are three levels:

1. A component the player has never seen: "You haven't found anything called 'x' yet." This is unchanged, and a part that does not exist gets the same answer.
2. A component the player has seen, but cannot see from here: "You can't see the x from here."
3. A component that is visible but not reachable: "You can see the x from here but can't get at it", followed by either "; the {cover} is in the way." or "; you'd have to get at it from the {place}."

`Open` and the compartment notion are gone. The console still accepts "open" as a word for remove.

### Open questions for Amit (real layout)

1. Which side is the fuel side on a typical 28-foot sloop's small diesel? Is it the one facing the quarter berth? I assumed that the lift pump, secondary filter, injection pump and oil filter are all there.
2. Is the battery switch on the face of the berth, reachable without lifting anything? Or is it on the companionway bulkhead?
3. Is the engine's raw-water seacock really under the saloon floorboards? Or is it in the engine bay or under the steps, which is where it was before?
4. Is the heat exchanger at the front, reached with the steps off? I moved it there from the side.
5. Can you see the solenoid from the companionway with the steps off? I said yes, but you cannot reach it from there.
6. Is the starter visible from the front at all? I said no.
7. Should moving cost more than 1 min when you climb (on deck to beside the hull, by ladder)?

## Implementation notes — web UI (order of work step 2)

**Date:** 2026-11-07. The web adapter now draws the two levels. No facade changes were needed: `Aboard()` (places with Here/Visited/Adjacent, covers with place and state, found components with position label and VisibleHere/ReachableHere) and `ComponentActions()` carry everything.

### File layout (`src/Haulout.Web`)

- `Services/GameSession.cs`: the only door to the facade. `Level` (Map or CloseUp), `SelectedComponent` (by name). `ClickPlace` issues GoTo unless already there and zooms in only if the sim accepts; a refusal stays as the last result for the panel. `ClickMapMarker` selects, and zooms in when the part is visible from where the player is. `ClickCover` issues Remove or Refit from the cover's current state. `ZoomOut`. The temporary shape-to-place click map is gone.
- `Art/Shapes.cs`: the drawing contract shared by both levels: `DecorPath`, `MarkerSpot`, `PlaceShape`, `CoverShape` (InPlace / Removed drawings plus a hit path), `CloseUpScene`. All names are the facade's display names, matched case-insensitively. No ids.
- `Art/CutawayGeometry.cs`: the map (viewBox 1000 x 600): yard, hull, fin keel, spade rudder, rig stub, interior ink, the seven place regions, the five covers drawn in state, the position map.
- `Art/CloseUps/<Place>.cs`: one static `Scene` per place; `Art/CloseUps/CloseUps.cs` maps a place name to its scene.
- `Components/Cutaway.razor`: renders the map. `Components/CloseUp.razor`: renders the current place's scene, covers clickable, markers for known components visible from here (visible-but-not-reachable ones dimmed). `Components/Marker.razor`: the one marker (hollow found, dull red known fault, amber ring selected), used by both. `Components/ContextPanel.razor`: place header with its covers (toggle buttons), Try to start, Buy remote start button, last result, selected part with verbs and the facade's reasons.
- `Pages/Play.razor`: both layers stacked in one grid cell; opacity crossfade over `--zoom` (320 ms). The hidden layer is inert.
- `wwwroot/css/drawing.css`: ink classes shared by map and close-ups (`hull-line`, `detail`, `ink`, `paper-fill`, `hatch-fill`, ...), place, cover and marker styles. Every SVG defines `<pattern id="hatch">`.

### The position map

A component's map position is `CutawayGeometry.SpotFor(name, positionLabel, index)`: the anchor for its position label (`PositionAnchors`, one per label in `sloop28.json` positions) plus a per-component offset and short label from a table keyed by component name, so engine parts sit round the engine outline, battery parts in the box, fuel gear round the tank. A name missing from the table falls back to the anchor plus an index offset, so a new component shows up somewhere sensible before it is placed by hand. Dense clusters (engine, battery box) show labels on hover, focus, selection or a known fault only.

### Adding a close-up

1. Add `Art/CloseUps/<Place>.cs` with `public static readonly CloseUpScene Scene`, viewBox 1000 x 600, leaving the top-left ~260 x 40 (Zoom out) and y > 575 free.
2. Backdrop for what is always there; one `CoverShape` per cover removed from this place, with what it hides in its `Removed` paths; `Spots` keyed by component name for everything visible from here (check the reach lists in `sloop28.json`); a few `Fallback` points.
3. Add the place name to `CloseUps.For`.
4. Use only classes from `drawing.css`. If the place gets a new cover, also draw it in state in `CutawayGeometry.Covers`.

### Notes

- The place graph is the sim's: from the quarter berth the cockpit is two moves (via the companionway); clicking it directly shows the refusal.
- Space on a focused place or cover may also scroll; Blazor cannot prevent default conditionally.
