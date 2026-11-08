# 009 — The chart

**Date:** 2026-11-08. **Decided by:** Amit (product). **Status:** adopted with proposed defaults; the open questions below are for Amit to overrule.

## Question

CONCEPT.md names three views: cutaway, chart, logbook. The cutaway and the logbook exist; trips (007) and the season (008) are text. What is the chart, and what does it need from the sim?

## Decisions

### 1. The chart is a query, not a system
The chart draws what the facade already knows: ports, routes, shelters, where she is, the passage under way and its hourly fixes, today's weather and the forecast. One new query, `Chart`, returns it; the web draws it; the console prints it as text; the MCP server serves it to agents. No sim logic moves into the view, and the view learns nothing the player could not read in the logbook.

### 2. Positions are single-sourced
Ports get chart coordinates in data (nautical miles east and north of the home port). Route distance and course are **derived** from the two ports' positions and the fields `distanceNm` and `courseDeg` leave the data, the way prices left `replace` in 007. The loader rejects a route whose ports lack a position. Shelters stay as a distance along the route. The current archipelago is placed so that nothing changes in play: Hestholmen 14 nm on 040°, Storholmen 26 nm on 300°, Kvitnes 9 nm from Hestholmen on 120°.

### 3. What is on the paper
Cream paper, grey-blue water, a fictional coastline drawn by hand around each port (web art, not sim data), a compass rose, a scale bar in nautical miles, a few soundings for flavour. Ports carry small marks: a crane for the yard, a shop, a fuel pump, an anchor for an anchorage. Routes are faint pencil lines with their shelters named. The boat is a small mark at her port or along the route at her position. Today's wind is an arrow with its force; rain greys the paper. The passage under way is a pencilled track with a fix per hour and an icon where something happened (an alert, hove-to, at anchor, a repair, a tow), each with its text on hover. Previous passages this season stay as faded tracks: the chart is a season's memory.

### 4. The chart is where you plan
A third toggle above the drawing: **Boat / Chart** (the cutaway's Map and Close-up stay as they are under Boat). Clicking a port on the chart plans the trip there and opens the Trip tab with the plan, the forecast and the readiness against it; the chart shows the planned route, its shelters, the estimate under engine and sail. Depart, Continue, Motor, Sail, Heave to, Anchor, Turn back and Call tow stay as they are in the Trip tab. Each hourly leg moves the mark and adds a fix. Nothing on the chart is dragged, drawn or sailed by hand: the chart is for choosing and watching, not steering (CONCEPT.md: no twitch sailing).

### 5. Text first still
The console gets `chart`: a line per port with bearing and distance from where she is and what is there; the shelters on the way; the track of the passage under way as fixes ("Hour 3: 11.2 nm out, hove-to, changed the primary filter"). The MCP tool returns the same view with coordinates so an agent can reason about distance and shelter.

## Guardrails

- Fictional geography only; no coastline traced from a real chart.
- The web never computes what the sim can answer; if the chart needs a number, it is a field on `ChartView`.
- The chart shows nothing the logbook does not already record. Latent faults have no icon.
- One hull, one archipelago: four ports is the whole world this round. More ports is a data change.

## Order of work

1. Sim: port positions in data, derived routes, loader validation, `Chart` query and `ChartView` (ports with marks, routes with shelters, the boat's place and position, tracks with fixes and events, today's wind, the forecast for the planned day), tests. Console `chart`. MCP tool.
2. Web: the Boat / Chart toggle, the chart drawing (coastline art, rose, scale, marks, routes, boat, tracks and fixes), port click → plan, Trip tab wired to the chart, hover texts. Verified in the browser at desktop and phone widths.

## Open questions for Amit

- Do you want to draw the archipelago yourself (you own art direction)? Default: Claude draws a first fictional coastline around the four ports; you replace it later.
- Faded tracks of earlier passages: keep or clutter? Default: keep, faded.
- Chart style: Nordic paper chart (white-and-cream, blue shallows) or the game's logbook palette? Default: the logbook palette so the two views feel like one book.

## Implementation notes

Written by the lead before the work (see 007 and 008 for the form). Every number lives in data and is a placeholder for Amit.

**Date:** 2026-11-08. The one shape every developer builds to. Sections marked (done) are filled in as the steps land. New numbers live in `sloop28.json` and go into its `placeholders` list with "(design note 009)".

### Positions and derived routes

- `Port` gains `Chart: ChartPoint?` (`record ChartPoint(double EastNm, double NorthNm)`), JSON `"chart": { "eastNm": .., "northNm": .. }`, and `bool Anchorage` (JSON `"anchorage"`, default false: she lies to her own anchor here, no mooring or harbour; Kvitnes true). Positions are nautical miles east and north of the home port, which is `(0, 0)`.
- The archipelago, one decimal, chosen so the derived numbers are unchanged: Skjærvik (0, 0); Hestholmen (9.0, 10.7); Storholmen (−22.5, 13.0); Kvitnes (16.8, 6.2). Derived: Skjærvik–Hestholmen 14.0 nm on 040, Skjærvik–Storholmen 26.0 on 300, Kvitnes–Hestholmen 9.0 on 300 (so Kvitnes lies 9 nm from Hestholmen on 120), Hestholmen–Skjærvik 14.0 on 220.
- `distanceNm` and `courseDeg` leave the JSON routes. `Route.DistanceNm` and `Route.CourseDeg` stay as init properties but are **set by the loader**: distance = hypot(Δeast, Δnorth) rounded to 0.1 nm; course = atan2(Δeast, Δnorth) in degrees, rounded to the integer, normalised 0..359. The rounding is part of the rule (one decimal is what the plan and the hour line show); no test changes its expectation.
- The loader rejects: a route whose `from` or `to` port has no `chart` ("Route skjaervik → x: port x has no chart position."), a route that still carries `distanceNm` or `courseDeg` (single source, like prices in 007), two ports at the same point, a shelter whose `atNm` is not inside (0, distance).
- `TripRules.PointAlong(Route r, double nm) → ChartPoint`: the From port's point plus `nm` along the straight line to the To port (clamped to [0, distance]). Shelters are drawn and listed at `PointAlong(route, atNm)`. This is the only geometry in the sim; the web draws the numbers it is given.

### Where the track lives

- `Passage` gains `List<TrackFix> Track` (internal mutable `TrackFix { int Hour; double EastNm, NorthNm; string Mode; List<string> Events }`) and `int Year` (the `GameClock` year at departure).
- At departure (in Depart's afterTick, once the passage exists) fix hour 0 at the origin, mode "under engine", event "Left {from} for {to}.".
- After every at-sea tick step (end of `SeaAfter`, and after each tow step: the tow ticks hourly in `Tick` with the passage still set), `UpdateFix(p)`: hour = `HourOf(p)` (the same hour numbering as the hour line); if the last fix has that hour, its position and mode are updated; else a new fix is appended. Position = `PointAlong(p.Route, p.ProgressNm)` (net of drift, as everywhere else). On arrival the last fix sits at the destination.
- Events go to the current hour's fix through one helper `MarkTrack(string text)` (no-op when not at sea), called by: `Raise` (every alert raised at sea, its text: the same line that goes to Failures and the logbook), HeaveTo ("Hove-to."), Anchor ("Anchored near X."), TurnBack ("Turned back for X."), a fix at sea (the Clean/Replace log text that goes to `Fixes`), CallTow (its log text) and arrival ("Arrived at X."). Every event is a line the logbook already has; nothing latent can get one. If the event comes before any step of the new hour has run (heave-to at the start of hour 3), it goes on the fix the next step will update: `MarkTrack` creates or finds the fix for `HourOf(p)` the same way.
- Finished passages: on arrival and at the end of the tow the passage (with outcome) moves to `Game._pastPassages: List<PastPassage>` (`From, To, Year, Day, Outcome, Track`). Outcome words: "arrived", "turned back", "towed". If she sinks at sea the passage goes there too with outcome "sank" (the loss card is unchanged). "This season" = the same clock year as now. Nothing new is drawn from any random stream; nothing in the tick changes.

### The query

`Chart(string? Planned = null)` (JSON `chart`) → `ChartView` (JSON `chart-view`). `Planned` is the destination the adapter has planned (the web passes the port clicked, the console `chart <destination>`); queries stay pure, the game does not remember a plan. Views (records in Results.cs, no ids, display names only):

```
ChartView(string Here, IReadOnlyList<ChartPort> Ports, IReadOnlyList<ChartRoute> Routes, ChartBoat Boat,
          ChartTrack? Track, IReadOnlyList<ChartTrack> EarlierTracks, ChartWind Wind, ChartPlan? Plan)
ChartPort(string Name, double EastNm, double NorthNm, bool Yard, bool Shop, bool Fuel, bool Anchorage, bool IsHere,
          bool Reachable, double DistanceNm, int BearingDeg)     // distance/bearing from the boat's position now; Reachable: a route from here (false at sea)
ChartRoute(string From, string To, double DistanceNm, int CourseDeg, IReadOnlyList<ChartShelter> Shelters)   // data routes once each, as written in the data
ChartShelter(string Name, double AtNm, double EastNm, double NorthNm)
ChartBoat(bool AtSea, string? Port, double EastNm, double NorthNm, string? From, string? To, double PositionNm,
          double RemainingNm, string? Mode, int HeadingDeg, bool Sunk)   // HeadingDeg: route course at sea, 0 in port
ChartTrack(string From, string To, int Day, string Outcome, IReadOnlyList<ChartFix> Fixes)   // Outcome "under way" for the current one
ChartFix(int Hour, double EastNm, double NorthNm, string Mode, IReadOnlyList<string> Events)
ChartWind(int Force, string From, bool Rain, string Text)      // today's real weather (WeatherWords.Describe), as Status shows it
ChartPlan(string From, string To, double DistanceNm, int CourseDeg, IReadOnlyList<ChartShelter> Shelters,
          double MotorHours, double? SailHours, string Estimate, IReadOnlyList<string> Blockers)   // from PlanTripSnapshot; null when Planned is null or no route
```

- Numbers rounded to 0.1 nm, bearings to whole degrees. `Here`: "She lies at Skjærvik." / "Under engine, 6.2 nm out of Skjærvik, 7.8 nm to Hestholmen." / "She lies on the bottom …" (reuse the trip text where it exists).
- No trip data: empty lists, the boat at (0, 0), Here "She lies at her mooring.".

### Adapters

- Prose `Chart(ChartView)` (decision 5): `Here`; "Wind: …"; a line per port: "Hestholmen, 14.0 nm on 040: harbour shop, fuel." with "(you are here)" and "(no route from here)"; "On the way to X: Lyngskjær at 7 nm." for each route out of here (or the planned one); "Planned: …" with the estimate; the track under way as "Hour 3: 11.2 nm out, hove-to. Changed the primary filter element." (events joined); "Earlier this season: day 152, Skjærvik to Hestholmen, arrived, 3 h." The MCP text adds the coordinates ("at 9.0 E 10.7 N") so an agent can reason about distance; the console prints the same text (one formatter, the coordinates included: honest and short).
- Console `chart` and `chart <destination>` (WordParser), and in the help. MCP tool `chart` (read-only, optional `destination`), added to the tool-name list and the end-to-end call list in `McpServerTests`, and to the help alias line if relevant.
- Tests (`ChartTests.cs`): loader derives the three routes' distance/course; loader rejects a missing position and a leftover `distanceNm`; `PointAlong`; the chart at home (ports, marks, reachable, distances/bearings from Skjærvik); a passage: fix 0 at the origin, one fix per hour, positions on the line, heave-to/repair/alert events on the right hour, arrival fix at the destination; earlier tracks after arrival, cleared in the next year; tow and turn-back tracks; a planned route; JSON round trip `chart`/`chart-view`; the chart in the no-ids sweeps (`SeasonTests` and the 007 sweep if there is one).

### The web (step 2)

- `GameSession`: `View` (`Boat` | `Chart`, default Boat), `ShowView`, `Chart` (the ChartView, refreshed with every other query after each command, with `PlannedTo`), `PlanTo(string port)` = set `PlannedTo`, run the existing plan path, `ShowTab(Trip)`. Departing clears nothing: the plan stays until the passage starts, then the chart shows the track instead. `PlannedTo` is cleared on arrival.
- `Pages/Play.razor`: a two-button toggle "Boat / Chart" above the stage. Boat shows the existing stage (Map and Close-up unchanged); Chart shows `<Chart />` in the same box.
- `Art/ChartGeometry.cs`: viewBox `0 0 1000 600`, 20 units per nm, `X(east) = (east + 28) * 20`, `Y(north) = (22 - north) * 20` (east −28..22, north −8..22). Pure data: coastline islands as closed smooth paths (Catmull-Rom to cubic Béziers from hand-placed points), the mainland edge, a compass rose, a scale bar (0–5 nm), a dozen soundings, hand-placed label offsets per port name. Every port sits on a shore, in a sound or a bay; every shelter in the lee of a skerry. Nothing traced from a real chart.
- `Components/Chart.razor` (+ `.razor.css`): paper, water, land, routes (faint pencil dash), shelters named, the planned route darker with its estimate, port marks (crane, shop, pump, anchor glyphs) as clickable groups for reachable ports (`data-port` attribute, keyboard focusable, `<title>`), the boat mark (a small hull pointing along `HeadingDeg`), the current track (solid pencil, a dot per fix, an icon on fixes with events, `<title>` with "Hour 3, hove-to: …"), earlier tracks faded, a wind arrow with "F4" in a corner, rain: a grey wash over the paper. Clicking a port: `Session.PlanTo(name)`.
- Phone: the SVG scales to the width; labels at least 13 units so they stay legible at 375 px; no horizontal scroll at 375 px.

### (done) Step 1 — positions, derived routes, the Chart query

**Built.** `sloop28.json`: ports carry `chart` (Skjærvik 0/0, Hestholmen 9.0/10.7, Storholmen −22.5/13.0, Kvitnes 16.8/6.2), Kvitnes `anchorage: true`, routes lost `distanceNm`/`courseDeg`, one 009 placeholder line. `BoatRules.cs`: `ChartPoint`, `Port.Chart`, `Port.Anchorage`, `TripRules.PointAlong`. `BoatLoader` reads routes through a DTO and derives distance (0.1 nm) and course (whole degrees): 14.0/040, 26.0/300, 9.0/300, reversed 14.0/220. New partial `Play/Game.Chart.cs` (track fixes, past passages, `ChartSnapshot`); hooks in Game.Sea (start, update, mark, archive), Game.Alerts (`Raise` marks the track), Game.Water (sinking at sea), Game.cs (dispatch, the Accept hook, a help line). `Chart(string? Planned)` → `ChartView` with the records exactly as in the notes. Prose `Chart`, console `chart [destination]`, MCP tool `chart` (optional `destination`).

**Decided where the notes were silent.**
- Loader errors are `InvalidOperationException`; a port without `chart` is allowed only if no route uses it (it is then left off the chart). Same-point ports and a shelter at or beyond either end are errors.
- `PointAlong` scales by the route's rounded distance, so a route's end is exactly its To port.
- Events marked from `Accept` (heave-to, anchor, turn back, a fix at sea) land on the hour she is in once the time has passed: a five-minute heave-to at the end of hour 2 shows on hour 3. `Raise`, the tow call and arrival mark the moment they happen.
- A sinking at sea archives the passage as "sank" with the logbook line on its last fix; `Track` is then null so it is not drawn twice. A tow archives as "towed" when the tow ends (before the lost hours). `ChartTrack.To` is the port first bound for, even after a turn back; `ChartBoat.From/To/HeadingDeg` follow the route as she sails it now.
- `ChartTrack.Day` is the absolute day (the logbook's numbering). "This season" is the `GameClock` year (360 days).
- Prose prints coordinates ("9.0 E 10.7 N") for console and MCP alike; "(no route from here)" only in port; at sea every port has distance and bearing from the boat.

**Tests.** No existing expectation changed. `ChartTests.cs` (15) plus the Chart query in the SeasonTests and SaleTests no-id sweeps: Sim 489 → 505. MCP: `chart` in the tool list and two calls in the end-to-end list (36, unchanged count). Untested: a sinking at sea on the chart.

### (done) Step 2 — the web chart

**Built.** `Art/ChartGeometry.cs` (pure data: mainland with a bay at Skjærvik and three inlets, Hestøya, Storholmen's hooked island, Ulvøya's crescent with Kvitnes in the notch, Lyngskjær and Måkeskjær with rocks, a handful of islets, shoal contours, 17 soundings, rose, 0–5 nm scale bar, label offsets, and `CheckRoutesClearOfLand`: every route clears land by ≥ 0.29 nm, ports 0.30–0.55 nm off their shores). `Components/Chart.razor` (+ css): paper land on grey-blue water, routes as pencil dashes with shelters named, the planned route dark with "14.0 nm · 2.8 h motor · 2.8 h sail", port marks with crane/shop/pump/anchor glyphs, reachable ports clickable and keyboard-focusable, the boat as a small hull on her heading (a wreck when sunk), the track under way with a dot per fix and a ring where something happened (title: "Hour 2, drifting: The engine coughs…"), earlier tracks in faded wood colour, wind plate with arrow and force, rain as a grey wash and hatching, `Here` as a caption. `GameSession`: `View`/`ShowView`, `PlannedTo`, `PlanTo` (plans, opens the Trip tab), `Chart` asked with `PlannedTo`; PlanTrip and Depart use `PlannedTo`; it is cleared on departure and on a trip card. `Play.razor`: Boat / Chart toggle above the stage; Boat is the old stage unchanged.

**Decided where the notes were silent.**
- The hour-0 fix gets no event ring (its only event is "Left X for Y"); every other event gets the same ring: telling alerts from work would mean the view reading the text.
- `Here` is an HTML caption under the SVG, not SVG text, so it wraps on a phone.
- Phone (< 760 px): one column, 16 px gutters, the body scrolls vertically; on the chart, port names 30 units, shelter names and the plan label 24, soundings and island names hidden (at 375 px the chart is drawn at about a third of its size).
- Earlier tracks are solid, warm and wider than the dashed routes; at first they were thin ink at 0.3 opacity and vanished into the route line under them.

**Verified in the browser** (1440 × 900 and 375 × 812): toggle, chart at Skjærvik, click Hestholmen → plan drawn and Trip tab open, depart, two hours under engine (fires-and-dies in hour 2), heave to, an hour hove-to, sail in; the track, the event titles, the boat mark moving, the earlier track after arrival; no horizontal scroll at 375 px. Not seen: rain, a tow, a sinking, a turn back on screen.
