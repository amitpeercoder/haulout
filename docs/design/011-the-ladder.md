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

## Amit's answers (2026-11-08) and the shape they give the ladder

**The ladder is reach, not size.** Amit: the 28-footer is a coastal boat, day trips and weekend tours unless the weather is kind; the 32-footer opens multi-day trips and farther ports, where the player meets the boats that go farther still: aluminium or 42–48 ft boats built for bad weather and far places (the arctic, say, all fictional). So each rung opens a region of the chart, and each region has its own ports, weather, wants and faults. The incentive is the next place on the chart, which the player can see and cannot yet reach. Starting money does not change: the starter is the teacher.

**Where the next boat is found.** Both, with different prices. The broker's window at Skjærvik shows the ladder: what exists, what it costs, how far short you are. The deals are found by sailing: a boat on a mooring at Kvitnes with a card in the window, her owner someone met at the club cruise, a price under the broker's because he knows you and wants her kept. The window teaches the ladder; the voyage finds the rung. Recommendation: the first 32-footer is only ever found this way, so the first trade-up is itself a story of going somewhere.

**Listing at any time: yes.** A boat can be listed in any season; the price curve already favours spring, viewings need her at a port with a broker or the yard, and a boat afloat in season gets viewings on weekends only (the viewer has a job too). The autumn lift stops being the only moment of decision.

**Value and the miles: pushback, and a better incentive.** Amit asked whether a boat's value should rise with the tours made in her, noting it makes little sense. It does not: miles are wear, and a surveyor prices the boat, not her history, beyond what the logbook already earns (600 a documented entry, up to 12, in the broker's letter). What the miles do buy, in the real world, is the *skipper*. Insurers of offshore boats ask for logged experience before they cover a skipper far from the coast, and a club or an owner selling a serious boat asks what you have sailed. So the ladder gates on two things: money, and a logbook of proven passages. The 32-footer's insurer wants a season's logbook with nights away; the aluminium 45-footer's wants offshore miles, a passage completed in a blow, and a boat handed back sound at the end of a season. The player's logbook becomes their CV, and the incentive to sail more is that it is the only way up, not that it inflates a price. The season page already counts miles, nights away and ports reached; the gate reads those. Reputation (010) is the third hand: a skipper the yard trusts is offered the deal at Kvitnes at all.

**What the regions need from the sim, in order.** The 32-footer's region: two or three outer ports and anchorages, an exposed crossing with swell, overnight passages (watches and tiredness as a rate, a night entry at an unlit anchorage), provisioning for days not hours. The far boats' region: multi-day open-sea passages, weather routing with a forecast that runs out, cold, ice warnings, a diesel heater that is survival rather than comfort. Each is a round of design notes of its own; none is in scope until 010's second pass has settled the economy and the 28-footer's season is fun as text.

**Order of work for the ladder.** (1) The broker's window with the listed boats, prices and running costs as text and a web card, the 28-footer the only buy. (2) The logbook gate: what each rung asks of the skipper's log, shown in the window ("her insurer wants a season with nights away: you have two"). (3) The deal at Kvitnes: the 32-footer on a mooring, found at the club cruise, priced by reputation. (4) The 32-footer as a playable data file (places, reach, components, faults) and her region. (5) Her art.
