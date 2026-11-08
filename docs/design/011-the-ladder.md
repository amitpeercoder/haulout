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

## Tickets: the skipper's ladder (proposed 2026-11-08, from Amit's idea)

Amit: miles earn a certificate that allows a better boat; you start with none and the 28-footer; the 32-footer needs miles and money; perhaps a ketch, yawl, cutter or catamaran needs more. Taken with one correction.

**Tickets gate water and size, not rig.** Real skipper tickets (the national boat licence, the coastal and offshore skipper grades, the radio licence) are tied to boat length or engine power and to how far from shelter you go, and the offshore grades ask for logged miles, days aboard, passages over a set distance and night hours. None of them cares whether the boat is a sloop or a ketch. A rig is a skill and a bill: a ketch has two masts' worth of rigging to inspect and replace, a cutter two headsails, a catamaran two engines, double the berth and a yard that charges for the width. The sim already models that kind of thing as components, costs and faults, so rig stays a trade-off in the boat file, not a paper. (Domain check for Amit: this is from general knowledge of the Norwegian licence threshold and the common coastal/offshore grade ladders; the fictional names below are ours.)

**The fictional ladder.**
1. *Boat licence.* Held from the start ("you passed it in the winter, the night before you saw her"). Covers the 28-footer in home waters.
2. *Radio ticket.* A weekend course at the club (two vacation-free days: a weekend, 1 500 money) and a short exam. Needed to buy and use a VHF; a VHF is what makes the fog and night close calls survivable (you hear the ferry) and is the first thing the 32-footer's insurer asks for. The first ticket is deliberately small: a weekend away from the boat, while she sits.
3. *Coastal skipper.* Asks the logbook for a season's miles (placeholder 300 nm), ten nights aboard away from home, one night passage, and the radio ticket; then a week's course (five vacation days, 9 000) and a practical day. Required by the insurer of anything over 10 m or any boat kept in the outer region. This is the 32-footer's gate.
4. *Offshore skipper.* Placeholder 1 500 nm, five passages over 60 nm, a passage completed in a blow (force 7 logged under way), 50 days aboard, a first-aid weekend, a season with no tow. Required for the far region and its boats.

**Miles can be earned on other people's boats.** This is the strongest idea the tickets bring. The club noticeboard offers crewing places: a weekend delivery on a 36-footer, a week on a 45-footer going north. Crewing earns logbook miles and nights (they count for the tickets), costs vacation days, pays nothing, and leaves your own boat alone at her mooring for the week, with everything 010 does to a boat left alone. The CV and the boat compete for the same weekends. It is also how a player first stands on the deck of the boat they will want, which is the window with the price on it.

**Guardrails.** A ticket is never a grind of its own: every mile it asks for is one the events and the regions already give reasons to sail. Requirements are shown in the window and on the season page as a checklist in words ("her insurer wants a night passage in your log: you have none"). Courses are dated on the calendar like events (the club runs the radio course twice a year), so they are turns to plan, not menu items. No ticket is lost; a reputation mark can make an insurer ask for more.

**Other incentives considered, briefly.**
- *Crew and friends.* The friend's week is the first of a roster: people who have sailed with you come back, ask for different things, and a serious passage needs two aboard. Reputation among crew is a second reputation, of the same kind as the yard's.
- *The noticeboard.* One place for the events, the courses, the crewing places and the boats for sale: the club's noticeboard at Skjærvik, read as a query, shown as a web tab. It replaces the broker's window as the opening card, which then becomes one card on the board.
- *Stories as the reward.* The far regions pay in places and nights the logbook keeps, not in numbers. The season page is the trophy case: miles, ports, passages, a line per event. Nothing else is scored.
