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
