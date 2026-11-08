# 011 — The ladder: boats you cannot afford yet

Status: proposed 2026-11-08, from Amit's idea. Not built. Build after 010's second pass.

## Question

What is the money for, once the season's bills are paid? 010 made the season a budget and the sale a way to trade up, but there is nothing to trade up to. Amit's idea: the game opens at a brokerage with several boats listed, only one of which the player can afford (our tired 28-footer). The others stay visible: the point is to see what you might own one day.

## Decisions (proposed)

1. **The listing is a view, not a new game mode.** The opening card becomes the broker's window: four to six boats with price, a line of pros and cons, and running costs as the yard would quote them. The player can only buy the one they can afford; the others are greyed with the shortfall ("you are 240 000 short"). After the sale of a boat, the window reopens with the player's balance against it. Keep stays the alternative.

2. **The ladder is a spread of real trade-offs, not a size ranking.** Fictional yards and names. Something like: the 28 ft sloop as now; a 32 ft cutter, older but sound, with a bigger engine and a diesel heater already fitted; a 36 ft fin-keel cruiser-racer, fast and thin on comforts; a 40 ft centre-cockpit bluewater boat with watermaker, windvane and a maintenance list to match; a 42 ft catamaran, two engines and twice the berth bill, a terror in a yard crane. Each has its own faults as sold and its own kinds of wants (the catamaran's club is a different club). Bigger is more: berth scales with length, insurance with value, the lift with tonnage, parts with size.

3. **A boat is data, not code.** `sloop28.json` becomes one of several boat files sharing the `kinds` table, climate and economy. The component model, places, reach and faults already live in data, so a second boat is a second file and new art. This is the test of rule 5 and of the data-driven model: if a second boat needs code, the model was not data-driven.

4. **The sale is the only way up.** No loans beyond the overdraft. The broker's letter for the current boat and the asking price of the next one are both on the window, so the gap is a number the player works on through the season. A well-kept boat sells for more; a season of upgrades is a rung.

5. **The 28-footer stays the only boat that ships first.** The window ships with the other boats as listings only (price, text, running costs). The first second boat to be playable is the 32 ft cutter, as the smallest change to places and art.

## Why not now

010's second pass makes the economy a file and the season a real budget; without that, a price ladder means nothing. The web's cutaway, close-ups and chart are drawn for one hull; a second boat is a second set of drawings. Build the window and the listings in the round after 010's second pass; make the cutter playable in a round of its own.

## Open questions for Amit

- Which boats are on the ladder, and in what order of price? The five above are a sketch.
- Does the starting money change (a player who starts richer could skip the 28-footer)? Recommend no: the starter is the game's teacher.
- Should a kept boat be listable for sale at any time, or only at the autumn lift as now?
