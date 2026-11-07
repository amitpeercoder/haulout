# 003 — First real UX is a web adapter

**Date:** 2026-11-07. **Decided by:** Amit (product), on Claude's recommendation. **Status:** adopted.

## Question

The console proved the diagnostic loop. What is the first real UX, and does it change the engine plan (CONCEPT.md build order step 2, default Godot 4)?

## Decision

Build `src/Haulout.Web`, a Blazor WebAssembly app, as the second adapter over the `Haulout.Sim.Play` facade. Scope: the first-fault slice, solvable by clicking. Three panels: cutaway, context/actions, logbook.

The engine decision stays open. The three views in CONCEPT.md (cutaway, chart, logbook) are all 2D, so a web UI may carry the game, with Steam via a desktop wrapper (Tauri/Electron). Decide after the web slice exists.

## Why

- Same C#, no server: the sim runs in the browser. Nothing in the sim changes; this also proves the adapter rule (CLAUDE.md rule 5).
- Fast iteration and no asset pipeline. The cutaway is vector art; the logbook is CSS.
- Claude can see and correct what it builds in the browser pane. Godot would be built blind on the art side.
- Godot now would make the asset pipeline the bottleneck on day three of a two-person project.

## Art direction (owned by Claude)

- **Palette:** Nordic late summer. Grey-blue water, slate, wet-wood brown, pale paper. One accent: a warm cabin-light amber. Faults in a dull red, never neon.
- **Cutaway:** a side-section of the hull as a technical drawing: thin ink lines, light hatching, compartments as regions that lift open. Components appear only once found. Night variant later (cabin light on, rain).
- **Logbook:** paper texture, a handwriting face for the owner's entries and a typewriter/print face for receipts and yard letters. Time and money as a logbook header line, not a HUD.
- **Motion:** slow and sparse. Nothing bounces.
- **Sound:** later. Describe it in text for now.

## Non-goals for this round

Chart view, seasons, economy, sound, mobile layout, the MCP adapter.

## Implementation notes

**Run it.** `dotnet run --project src/Haulout.Web --urls http://localhost:5180`, then open http://localhost:5180. Blazor WebAssembly, no server project: the sim runs in the browser. Everything is offline (no web fonts, no CDN, no CSS framework). Laid out for desktop, ~1280 px wide and up.

**Structure.**
- `Services/GameSession.cs`: holds the `Game`; the only place the facade is touched. Execute/Ask, a `Changed` event, the selection, the last result, and the last inspect/meter text per component.
- `Pages/Play.razor`: the briefing with "Go aboard" (issues Look), then the three regions.
- `Components/Cutaway.razor`: SVG cutaway. All geometry is in `Art/CutawayGeometry.cs` (paths, captions, marker slots), so the art pass edits only that file.
- `Components/ContextPanel.razor`: Try to start, Buy remote start button, the last result as prose, the closing card, and the selected component's verbs.
- `Components/Logbook.razor`: the paper logbook with the time/money header line.
- `wwwroot/css/haulout.css`: design tokens (palette, line weights, faces, fade) as custom properties.

**Facade additions** (`Haulout.Sim.Play`, tested in `tests/Haulout.Sim.Tests/Play/AdapterQueryTests.cs`):
- `Aboard()` → `AboardView`: each compartment with `Opened` (opened, or in plain view after Look) and the components found in it (`FoundComponent(Id, Name, HasKnownFault)`; HasKnownFault means a discovered fault is still present). Unfound components are not in it, so they are not in the DOM.
- `ComponentActions(component)` → `ComponentActionsView`: the verbs that make sense on a found component (`ActionOption(Verb, Label, Available, UnavailableReason, Minutes, CostNok)`), e.g. Measure on a cranking-circuit part is unavailable without the remote start button, and Check flow is unavailable while the engine is not running. An unfound name gets `Found = false`, the same answer as a non-existent one.
Both are queries: no time passes. The MCP adapter can reuse them.

**Clicking.** Clicking a closed compartment issues Open (2 min); clicking it again only selects it. Clicking a marker selects the component; the panel's buttons issue the commands.
