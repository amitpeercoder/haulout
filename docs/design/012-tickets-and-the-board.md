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

## Amit's answers (2026-11-08) and the research behind the numbers

**Hours.** Chandlery and yard office Monday to Saturday 09:00–17:00, closed Sunday (Amit). The fuel pump is card-operated at any hour. Office-hours rules (orders by phone, booking the crane) follow the office.

**Sleep is a need, not a curfew (replaces decision 1).** Amit: the player may work whenever, but needs 6–8 hours of sleep. So: the skipper has hours awake, a rate, not a clock rule. The first 16 hours awake are free. Past them every job carries a named tiredness factor on the chance of a botched job (the own-work faults of 006/007: "hose clamp not seated", "patchy work"): ×1.5 from 16 h, ×3 from 20 h, with the result line saying so ("You're tired; this is the kind of job that gets done twice") and the botch, when it comes, logged as tired work so it is diagnosable. Past 20 hours `inspect` and `measure` still work but subtle symptoms (the degree-only bloom under the boot, the set in an impeller vane) are not seen, and the line says your eyes are not to be trusted tonight. `Sleep` (or `turn in`) sleeps until 08:00 or for 8 hours, whichever is later than 6 hours; any wait, `next weekend` and a passage's night watches reset it as the day-by-day tick already sleeps the player. Nothing stops at 22:00. A weekday evening's 180 free minutes are unchanged. The harbourmaster calls at 08:00, never at night. Numbers in economy.json (`sleep: { freeHoursAwake, tiredAt, exhaustedAt, botchFactorTired, botchFactorExhausted, hours }`), placeholders.

**Thresholds and courses (researched 2026-11-08; placeholders in tickets.json with these sources).** The grades are modelled on the RYA ladder: the SRC radio course is one day of about ten hours plus an exam, with course fees of about £50–120 and a £76 exam fee; the Coastal Skipper practical is five consecutive days at about £450–950, with a recommended minimum before it of 15 days at sea, 300 miles, 8 night hours and 2 days as skipper; the Yachtmaster Offshore exam asks 50 days at sea, 5 as skipper, 2 500 miles and 5 passages over 60 miles, two of them overnight. In game units (1 unit ≈ 1 NOK, about 13 to the pound): radio ticket, one Saturday at the club, 1 800, twice a season; coastal skipper, 300 nm, 15 days aboard, 8 night hours under way, 2 passages as skipper over 20 nm, the radio ticket, then five consecutive days (Mon–Fri, 5 vacation days) at 9 500, once a season in September; offshore skipper, 2 500 nm, 50 days aboard, 5 passages over 60 nm with two overnight, a season with no tow, a first-aid day, then five days at 15 000, listed and not reachable this round. Our "coastal" is the course-level grade, not the Yachtmaster Coastal exam (800 miles, 30 days, 12 night hours); the far boats in 011 will ask for the exam grade later. Sources: the RYA's Yachtmaster Offshore page and training providers (rya.org.uk, firstclasssailing.com, uksa.org), the RYA marine radio SRC page and club listings (rya.org.uk, carsingtonsailingclub.co.uk, yotspot.com).

**VHF.** Amit: one must hold the licence to use the radio. Decision 4 stands: a call made without a ticket is reported by the coast radio.

**Crewing places.** Clubs and crewing sites do post "crew wanted" for deliveries and passages, usually unpaid with food aboard, and skipper schools count such miles in the log an examiner reads. Decision 5 stands.

**Process.** Amit asked that finding out how things are in the real world be a habit, done by Claude before asking him. Added to CLAUDE.md: research first, cite in the note, flag what stays unverified; Amit corrects.

## Decision 8: the exam asks real questions (Amit, 2026-11-08)

A ticket is earned at an exam with real, basic questions, not granted by the log alone. Two conditions keep it a sim and not a quiz:

1. **The game teaches the answers before it asks.** Each course has a course book, a logbook document the player can read any time after enrolling (`read radio course book`; web: the logbook's documents). A few pages each, written from researched sources: for the radio ticket, channel 16 and working channels, the call ("Skjærvik harbour, Skjærvik harbour, this is …"), Mayday against Pan-Pan against Sécurité, the radio check, what a DSC alert is; for the coastal ticket, lights and shapes (red to port), the give-way rules a sailing boat meets (power gives way to sail, the overtaking boat keeps clear, two sailing boats on opposite tacks), fog signals, the weather line (dew point against sea temperature, a lee shore, what force 6 means to a 28-footer), a passage plan (fuel, daylight, the tide where it matters), and the engine (what a voltage drop across a clamp means, why weak exhaust flow comes before the alarm). A player with no sailing knowledge passes by reading and playing.
2. **The sim uses what the exam asked.** After the radio course `Call` exists at sea (the harbour, a ferry, the coast radio) and a fog or night close call becomes a choice with the right procedure; the coastal exam's questions are the weather line, the plan and the faults the player has met. A question whose answer never matters aboard is not in the bank.

**Shape.** `src/Haulout.Sim/Data/questions.json`: a bank per ticket, about twenty questions each, multiple choice with four options, one right, each with a one-line note giving the source and the page of the course book that teaches it. The exam is the last day of the course: five questions picked by a seeded stream of their own, asked one at a time by the examiner; `Answer(letter)` answers; a pass is four of five. A fail costs the exam fee only (radio 500, coastal 1 500, in economy.json) and a re-sit at the next date; the course is not repeated. The result says which question was wrong and where the book covers it, never the right letter. Console: the question printed, `answer b`. MCP: `exam_question`/`answer`. Web: the question as a card with four buttons. The bank is research work: every question cites its source in the data and is marked UNVERIFIED until Amit has read it; one wrong answer in the bank costs more trust than a wrong price. Agent playtests will pass it trivially; it is for people, and the scorer records only `exam_passed`.

**Order of work.** Becomes step 2b, after the tickets and the board and before the VHF: the course book documents, the bank, the exam days, `Answer`. The playtest prompt tells the agent to read the book before the exam.
