# 012 — Tickets and the board

Status: decided 2026-11-08 (Amit: "Yes, start 012"). First round of design note 011 (the ladder). Builds on 010 and its second pass.

## Question

The second pass made the season a budget but not a choice: two Haiku runs kept her with the free wants made, the costly wants skipped, nothing bought for the player's own sake, and work done at three in the morning. This round gives the player a reason to sail more (the skipper's ladder), a place to see what is worth doing (the club noticeboard), a want they buy for themselves (the radio), and a day that ends.

## Decisions

### 1. The day has an end

Work stops at 22:00. A job that would run past it finishes if it has half an hour or less to go; otherwise it stops at 22:00 with "It's late. You turn in; the rest can wait for the morning" and the job's remaining minutes are kept on the component as "part done" (a second command finishes it; nothing is lost, nothing is counted twice). The next command resumes at 08:00 the next morning, or 17:00 on a weekday (work; the evening rule in 010 is unchanged: 180 free minutes). At sea the clock runs through the night as now. The yard office and chandlery keep hours: weekdays 08:00–16:00, Saturday 09:00–13:00, closed Sunday; buying or booking outside them is refused with the hours named ("The chandlery opens at nine"). The fuel pump on the quay is card-operated at any hour. The harbourmaster does not call after 22:00; the call comes at 08:00. Ordering by phone keeps office hours. Data: `economy.json` gains `hours`.

### 2. The skipper's log

The boat has a logbook; the skipper gets a record of their own that the boat's log feeds: miles under way, nights aboard away from the home port, night hours under way (after dark by the daylight table), passages over 20 nm, the strongest wind force logged under way, days aboard, tows taken, seasons completed without a tow. It persists across boats (011) and is shown by a `Skipper` query and on the season page. Miles earned crewing (decision 5) land here and not in the boat's log. No number in it is a score; it is the CV the tickets read.

### 3. Tickets

Data: `src/Haulout.Sim/Data/tickets.json`. Four grades, fictional names, modelled on the national boat licence, the radio licence and the coastal and offshore skipper grades; the thresholds are placeholders and marked UNVERIFIED:

| Ticket | Held | Asks of the skipper's log | Course |
|---|---|---|---|
| Boat licence | from the start | — | — |
| Radio ticket | no | nothing | a weekend at the club (Sat–Sun), 1 500; offered twice a season, on the board |
| Coastal skipper | no | 300 nm; 10 nights away; 1 night passage; the radio ticket | a week at the club (Mon–Fri, 5 vacation days), 9 000; once a season, in September |
| Offshore skipper | no | 1 500 nm; 5 passages over 60 nm; a passage logged in force 7 or more; 50 days aboard; a season with no tow; first-aid weekend | a week, 15 000; listed, not reachable this round |

A course is a dated card on the board. `Enrol(course)` pays at enrolment and marks the days; a wait stops on its first morning; the course days are spent like the friend's week (010 H): played days with a line each, no block. On the last day, if the log meets the asks, the ticket is held; if not, the course is still done and the ticket waits for the log ("the examiner signs the course; the ticket comes when your log shows a night passage"). No seeded fail: the only uncertainty is the player's own log. Tickets are never lost; a reputation mark (010) makes the insurer ask for one more season without a tow. The `Tickets` query shows each ticket as a checklist in words ("300 miles: you have 126"), and the board shows the next course.

What tickets gate this round: the radio ticket is needed to operate a VHF (decision 4); the coastal skipper ticket is what the 32-footer's insurer will ask for (shown on the ladder card, decision 6) and what the far crewing place asks for (decision 5). Nothing else is locked. Tickets gate water and boat size, never rig (011).

### 4. The radio: a want the player buys for themselves

A VHF set is an upgrade component (010 decision 5 shapes): 3 500, ordered, two hours at the chart table, or the yard. With a VHF aboard and the radio ticket held, a fog or night close call (010 second pass D, E) becomes a call: the ferry calls her on channel 16 before it is close, the entry in the log is a call not a fright, and nothing is reported. With a VHF and no ticket the set still works, but a call is made with no call sign and the coast radio reports it: the harbourmaster's letter (010 E) with "operating a radio without a ticket", 1 500 and a reputation mark, delayed to the next morning's post. Without a VHF nothing changes from the second pass. The VHF gets one fault by rule 3 (a corroded antenna connector at the masthead: the set receives but cannot be heard; seeded by exposure; inspect on deck shows the green; a connector and an hour aloft on a calm day). The weather report mentions the coast radio's forecast when a VHF is aboard and working (the same forecast; it is flavour that teaches what the set is for).

### 5. Crewing places

Cards on the board, two a season, dated, from data (`board.json`): a weekend delivery ("Crew wanted: a 36 ft cutter from Hestholmen to Storholmen and back, Saturday and Sunday, days X–Y; food aboard, no pay") and a week north ("Crew for a week on a 45 ft aluminium cutter going north to the outer skerries, days X–Y; coastal skipper ticket or a season's log asked"). `TakeCrewingPlace(card)` marks the days. On the first morning the player is gone: the days run as a day-by-day wait with the harbourmaster's alerts queued, not stopping (you are at sea on another boat; her letter is on the mat when you return, with everything that happened to your own boat while you were away). Each day of the place has a line from data and credits the skipper's log from the trip's plan and that day's weather (the week north logs a night passage and, if the series gives force 7 on a day under way, the blow). Weekdays cost vacation days as 010 says. The week north is also the first look at a far boat: a card describes her, and the last line says what she cost. No pay; the incentive is the log.

### 6. The board

A `Noticeboard` query at any port; the club's board at Skjærvik carries everything, the other ports a card or two of their own (Hestholmen: the regatta; Kvitnes: nothing yet, the 32-footer in 011's third step). Cards, dated and sorted by day: events (010), courses (3), crewing places (5), and the ladder (011 decision 1): four listings from `ladder.json`, each a card with length, asking price, a line of what she is for and against, running costs a year as the yard would quote them, and the ticket her insurer asks for; under each the shortfall in money and in tickets ("you are 240 000 short; her insurer wants the coastal ticket"). None is buyable this round. The 28-footer's card is the player's own, with the broker's current figure. The opening briefing now ends with the board ("The club's board by the slipway has the season on it"). Console `board`; MCP `noticeboard`; web: a Board tab beside Calendar, the ladder cards at the bottom; the Calendar tab keeps the week and the money.

### 7. Listing at any time

`ListForSale` works in any season when she is at Skjærvik or the yard (011, Amit's answer 3). Afloat in season, viewings fall on weekends; the price curve of 008 stays (spring pays); a boat listed under way or at another port is refused with the reason. The season page and Keep are unchanged.

## Guardrails

- Every ticket's ask is a mile the events, courses and crewing places already give a reason to sail. If a run shows a player sailing laps for miles, the asks are wrong, not the player.
- Nothing in the skipper's log is shown as a score. Words and checklists only.
- All numbers in data, marked placeholders: hours, thresholds, prices, the cards' dates.
- Rule 5: the board and the tickets show only what the player can know. The ladder cards' running costs are the yard's quote, not the sim's truth.
- Rule 3: the VHF has a fault; the course has no seeded fail.
- Existing seeded outcomes do not move; new randomness gets its own stream.

## Order of work

1. The day has an end and the hours (decision 1). All four scripts updated where their times move; the calendar run is the test that nothing happens after 22:00 except at sea.
2. The skipper's log, `tickets.json`, the `Tickets` and `Skipper` queries, the board with events and courses, `Enrol` and the course days (2, 3, 6 without ladder and crewing).
3. The VHF and the radio ticket (4), with the close-call machinery changed.
4. Crewing places (5).
5. The ladder cards and listing at any time (6 remainder, 7).
6. Web Board tab, MCP tools, playtest prompt (goals: the radio ticket and a VHF before Midsummer, a crewing place, the coastal course in September) and scorer fields (tickets_held, crewing_days, vhf_fitted, course_days), a scripted `tools/playtest/board-run.txt`.

## Open questions for Amit

- The hours: 22:00 as the end of work, the chandlery's Saturday hours, Sunday closed.
- The thresholds: 300 nm and 10 nights for coastal, 1 500 nm and force 7 for offshore. Is the real coastal grade nearer this, or lower?
- Course prices and lengths (a weekend for radio, a week for coastal).
- Whether a VHF without a ticket is really reported, or merely illegal and ignored.
- The crewing places: do clubs really post crew wanted like this, and does a week's delivery earn the kind of log an insurer reads?
