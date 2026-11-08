# 005 — Fault lifecycle, discovery, deferral and the three fix routes

**Date:** 2026-11-07. **Decided by:** Amit (product). **Status:** adopted.

## Question

One fault with one cause is a puzzle. A season is many faults, most of them not urgent, discovered in different ways, fixed or deferred under money and time pressure. What is the model?

## Decisions

### 1. A fault has a lifecycle
`Latent → Symptomatic → Discovered → Fixed | Deferred`. Deferred faults keep ageing and may escalate (a weeping seacock becomes a seized one; a tired impeller sheds vanes). Escalation is on a timer or on use, visible in the sim's rules, never a dice roll the player cannot trace.

### 2. Four discovery channels
- **Symptom:** the engine clicks, the bilge pump runs too often, the windlass is sluggish.
- **Action:** inspect, uncover, measure, check flow. The detective loop from 001.
- **Professional inspection:** a surveyor or a specialist (engine mechanic, rigger, electrician) writes a report. They find what is visible and some of what is not, they often write *recommend investigation* rather than a cause, and they are sometimes wrong. They never hand over the root cause of a hidden fault; they point at it.
- **Failure at sea:** the deferred item becomes a story.

### 3. Haul state gates what you can fix
Below-waterline work (seacocks, antifouling, prop, shaft seal, rudder bearings, keel bolts) is **on the hard only**. Afloat, those faults are locked until the autumn haulout or an **emergency lift**, which costs several times the planned one and depends on a yard slot. The title is the mechanic.

### 4. Deferral has a visible price
Every discovered fault shows *cost now* and *likely cost later*, plus whether later means "afloat impossible". Deferral is a legitimate, often correct choice; the game never hides what it will cost.

### 5. Three fix routes
| Route | Money | Your time | Quality | Learns |
|---|---|---|---|---|
| DIY | parts only | full | depends on skill; poor work can come back (delayed, 001-style) | yes |
| Assisted | parts + hourly help | full | good | yes, faster |
| Yard | parts + labour | none | reliable | no |

Skill is per system and grows with jobs done. Yard work has lead times; the yard is busy in spring.

### 6. Launch is the decision
Readiness against the deadline. After launch, deferred faults play out during the season. With the right spare and skill, a fix at anchor; without, limp home, a tow, or a rescue. Costs money and pride. The account is never lost (CONCEPT.md: loss is survivable).

### 7. Every consequence traces back
A failure at sea must come from a fault the player saw or could have seen. No untraceable bad luck. Pillar 1.

## Guardrails

- The survey list must inspire, not bury: twelve items, half of them cheap and quick, two or three that matter, one red herring.
- Inspections are information, not answers; the detective loop remains for the hidden faults.
- Failures are stories with costs, never game-overs. Keep it chill.

## Order of work

1. **Pre-launch slice** (this round): the twelve-item survey, three more faults (one below the waterline, one deferrable that bites at sea, one hidden), deferral with cost-now / cost-later, DIY and yard pricing, a launch deadline on the clock. All in the yard we have.
2. **Launch and one trip**: haul state flips, two or three emergent failures from deferrals, spares.
3. Assisted route, skill growth, yard lead times.

## Open questions for Amit

- Realistic ratio of emergency lift to planned haulout cost? Default: 3×.
- Does the surveyor's fee scale with boat length (common) or flat? Default: per foot.
- Which spares does a sensible 28-footer carry? Default: impeller, belts, fuel filters, oil, hose clamps, fuses.

## Implementation notes

**Date:** 2026-11-08. Order of work step 1 (the pre-launch slice) is done in the sim, console, MCP and a minimal web panel.

### Facade
- Commands: `Defer(fault)`, `Undefer(fault)`, `FixByYard(fault)`, `OrderSurvey()`, `WaitDays(n)` (1 to 60), `Launch()`. A fault is named by its name or its part's name; an undiscovered fault gets the same refusal as a made-up name.
- Queries: `SurveyReport()` (a letter; `Ordered=false` before the survey) and `Readiness()` (open discovered problems with state, cost now and later, later note, on-the-hard-only, consequence and the DIY and yard routes; surveyor pointers still to look into; names of fixed or ruled-out problems; day, deadline, money, launched).
- `StatusView` carries the balance, the deadline and whether she is launched. `FoundComponent.Flagged` marks a part a surveyor pointed at. `ActionOption.Target` names the problem for the defer, undefer and yard-fix verbs. `LogEntry.Document` marks the survey report. `KnownFault.Id` is in-process only and never serialised.
- `Finished` still means "the engine has run once". The game no longer ends there; the console runs until quit.

### Lifecycle
- `FaultState`: Latent, Symptomatic, Discovered, Deferred, Fixed.
  - A hint (a reveal that does not discover, or a survey "investigate" item) makes a fault Symptomatic.
  - A discovering reveal, a survey "cause" item or a cleaning that fixes it makes it Discovered.
  - Deferred records the day. A harmless fault (the red herring) goes to Fixed as soon as it is discovered and is logged "Ruled out".
- Escalation is in data: `escalation {to, afterDeferredDays, onLaunch, text}`. The successor is a dormant fault on the same part. It wakes Discovered if the player knew of the predecessor, and silently Latent if not. The predecessor stops being present but keeps its state.
- DIY quality is in `diyQuality {startSkill 0, threshold 2, poorJobChance 0.25, poorJobs per subsystem}`.
  - After a Clean or Replace on a subsystem whose skill is below the threshold, there is a chance (through `IRandom`) of a latent poor-job fault: effect none, found by inspecting, fixed by a redo. Skill then rises by one.
  - The default seed is 4: seed 1's first draw is 0.249, which would spoil the first cleaning.
- Water in the fuel, DIY: drains on three distinct days (`properties.diyDrainDays`), then a new primary filter.

### Data shapes (sloop28.json)
- Top level: `economy`, `survey`, `diyQuality`, `placeholders`.
- Component: `belowWaterline`, `replaceText`.
- Removable part: `uncoverText`, `refitText`. The impeller now has a raw-water pump cover.
- Fault: `effect.kind: none`, `onHardOnly`, `harmless`, `dormant`, `consequence`, `costLaterNok`, `laterNote`, `diy {how, partsNok, minutes}`, `yard {how, nok, leadDays}`, `escalation`, `properties`.
- 43 components (new: navigation lights, cockpit drain hoses, lifelines) and 12 faults.

### The twelve survey items
1. Hull: blistering at the bow, possibly osmotic. Attention, investigate. Red herring: it is old paint, ruled out by inspecting.
2. Antifouling thin. Attention, cause.
3. Raw-water seacock weeping. Before launch, cause.
4. Cockpit drain hose clamps rusted. Attention, cause.
5. Lifeline terminals pitted. Attention, cause.
6. Bicolour lens cracked. Attention, cause.
7. Standing rig: nothing seen from deck, rigger in two seasons. Note.
8. Start battery dated, load test. Attention, investigate (the 60 % charge).
9. Starter clicked, investigate the starter circuit. Before launch, investigate (the corroded terminal; the starter motor is the part flagged).
10. Fuel sample shows water. Before launch, cause.
11. Primary filter element overdue. Attention, cause.
12. Exhaust elbow light rust, monitor. Note.

An item whose fault is no longer there when the survey is ordered is left out.

### Placeholders for Amit (all in data)
- Money: 25 000 NOK starting balance; survey 200 NOK per foot (5 600 NOK), 1 day; emergency lift 9 000 NOK; launch takes 2 h and costs 0 NOK.
- Deadline: day 96. Seacock seizes after 20 deferred days, or on launch.
- DIY quality: threshold 2 jobs, poor-job chance 25 %.
- Costs now / later (NOK):

  | Problem | Now | Later |
  |---|---|---|
  | Weeping seacock | 1 200 | 12 800 |
  | Seized seacock | 1 400 | 13 800 |
  | Impeller | 350 | 4 500 |
  | Water in the fuel | 180 | 4 200 |
  | Antifouling | 1 600 | 3 500 |
  | Navigation light lens | 250 | 900 |
  | Drain clamps | 120 | 600 |
  | Lifeline terminals | 900 | 1 800 |

- Yard prices and lead times: 1 to 5 days, about 3× the DIY parts.

### Open questions
- Should a discovered but undeferred fault age and escalate too, or only a deferred one? Now: only a deferred one, plus every fault on launch.
- Should the emergency lift be a command afloat, at 9 000 NOK and a yard slot? It is cut from this round.
- Does a seized seacock still pass cooling water, so the engine still runs? Now: yes.
- Can you change cockpit drain hose clamps afloat? Now: yes.
- Does the surveyor's fee scale per foot? Now: per foot.
