# Analysis — Hydrographic Office: contour-following surveys

**Proposal.** Replace the Hydrographic office's current ~6–12 large sheets per
island with 20–30 smaller sheets positioned along the contours of water bodies:
coastline, lakes, rivers.

**Verdict up front: the proposal bundles two independent changes, and only one of
them does what you want.** "Follow the contours" and "more sheets" are separate
levers. Measured below, contour-following at today's scale yields *the same
count we already have*. The count comes entirely from making sheets smaller —
which needs no new cutter at all.

Everything here is measured against the current build, 10 islands, collection
seed 8412.

---

## 1. What the office actually surveys today

| island | character | hydro sheets | coast km | coast loops | inland water km² | bodies > 1 ha | rivers km |
|---|---|---|---|---|---|---|---|
| 0 | Fjorded | 6 | 33.1 | 12 | 1.10 | 5 | 32.0 |
| 1 | Atoll | 8 | 15.5 | 7 | **0.00** | 0 | 0.0 |
| 2 | Atoll | 12 | 22.2 | 5 | **0.00** | 0 | 0.0 |
| 3 | Mountainous | 12 | 44.5 | 9 | 0.03 | 1 | 97.1 |
| 4 | Atoll | 9 | 22.7 | 10 | **0.00** | 0 | 0.0 |
| 5 | Fjorded | 6 | 26.8 | 15 | 0.10 | 2 | 1.1 |
| 6 | Mountainous | 12 | 34.1 | 5 | 3.05 | 2 | 99.3 |
| 7 | Mountainous | 8 | 39.2 | 5 | 0.19 | 2 | 67.2 |
| 8 | Atoll | 8 | 15.2 | 10 | **0.00** | 0 | 0.0 |
| 9 | Atoll | 10 | 18.0 | 10 | **0.00** | 0 | 0.0 |

**Means:** 9.1 hydro sheets, 27.1 km of coast, 0.45 km² of inland water in 1.2
bodies, 29.7 km of river.

---

## 2. The count does not come from following the coast

Sheets laid *along* the coastline with 20% along-track overlap, for the mean
27 km of coast:

| sheet | along-track | strips needed |
|---|---|---|
| A1 landscape @ 1:5000 — **today's scale** | 3805 m | **9** |
| A1 landscape @ 1:2500 | 1902 m | 18 |
| A3 landscape @ 1:2500 | 1188 m | 29 |

Today's bounding-box lattice already produces **9.1** sheets. A coast-following
layout at the same scale produces **9**. They are the same number, because both
are ultimately driven by coastline length divided by sheet length.

**So the 20–30 you want comes from shrinking the sheet, not from following the
contour.** That is a one-line change — `Tuning.CoastalScaleDenominator`, or a
smaller `SheetFormat` — with no new cutter, no new failure modes, and none of
the costs below.

This is the single most important finding here. Every con that follows applies
to the contour-following half of the proposal, which is the half that is not
buying you the sheet count.

---

## 3. Cons

### C1. It breaks "one rotation per survey", which is load-bearing

R2.4 is explicit: *"Rotation is fixed per survey, not per sheet."* R2.2 defines a
survey as *"one island, one office, one year, one scale, one rotation."*

A sheet that follows a winding coast must orient to its **local** tangent, so
rotation becomes per-sheet. That is not a tuning change — four things depend on
one rotation per survey:

- **Frame space** (§2) exists precisely because one rotation makes sheets
  axis-aligned in it. Per-sheet rotation means there is no survey frame.
- **The sheet lattice** (§10.2 steps 2–5) tiles in that frame.
- **Row-major numbering** (§10.2 step 7, "+u then +v, origin bottom-left") is
  undefined without a common frame.
- **A4** asserts contiguity against that ordering.

A contour-following cutter is a second algorithm, not a parameter: walk a
polyline, place oriented sheets at intervals. Its edge cases (self-intersecting
coast, islets shorter than one sheet, loops that close mid-sheet) are all new,
and A4/A5's guarantees have to be re-derived rather than inherited.

### C2. Rivers are the wrong shape *and* Hydrographic does not draw them

Per the §8.3 matrix, Hydrographic draws **coast, settlement, sounding**. It does
not draw rivers — that is Land Survey's class.

So a sheet placed on a river shows: no coastline (a river is inland, above sea
level), no soundings (`Tuning.SoundingDepth` is −4 m; rivers are not 4 m below
sea level), and a settlement only by luck. **It is a blank sheet.** It fails A5,
or forces the office matrix to change — and changing the matrix invalidates the
§8.3 shared-class invariant and the A6 measurement built on it.

River corridors are also 1-D: at 1:2500 a 30 km river is ~16 sheets whose content
is a thin blue line down the middle of otherwise empty paper.

### C3. Lakes barely exist, and there is no lake feature to survey

Measured: **1.2 bodies over 1 hectare per island, 0.45 km² total.** Five of the
ten islands have **zero** inland water.

There is also no `Lake` in the generator: §7's discrete features are Peaks,
Settlements and Rivers. Inland water is simply ground where `h01 < SeaLevel`; it
has no class, no `FeatureId`, no enumeration. Surveying lakes needs a new
discovery pass (connected-component labelling of water unreachable from the
domain border) plus a new feature class plumbed through the matrix, the service
rule and the cutter — a lot of machinery for 1.2 bodies an island.

### C4. The atoll's lagoon is not an enclosed body of water

The most obviously "internal body of water" in the whole generator turns out not
to be one. All four atolls measure **0.00 km²** of inland water, because the ring
breaks into arcs (visible in every atoll render) and the lagoon is therefore open
to the sea.

So the character that *most* suggests this feature has nothing to offer it. This
also kills a fix proposed elsewhere: `poc02/findings.md` F-02.4 suggested a
flood-fill to tint lagoons differently — it would find nothing.

### C5. It reverses the sheet-economy decision taken two steps ago

Sheet economy went 13 → 58 → 38 per island, and 58 was rejected explicitly
because it was more than a player should have to track. Hydro at 9 → 29 puts the
median back to roughly **58**.

If the archive should feel bigger, that is a legitimate design choice — but it is
the *same* choice already made and reversed, and it should be made on its own
terms rather than arriving as a side effect of changing how sheets are placed.

### C6. Thin coast strips are the worst case for the map table

R6.4's assisted fit settles a sheet by matching it against the island's vector
data. A small sheet carrying a stretch of coastline, a few soundings and nothing
else is **nearly featureless**, and coastlines are self-similar — one 1.2 km
stretch of shore looks much like another on the same island.

Twenty-nine near-identical strips is the hardest possible input for the fit, and
§5.4's second v1 proof is *"does assisted fit feel like landing, or like
fighting?"*. This proposal makes that proof harder to pass, before it has been
attempted once.

### C7. Cross-office cross-referencing likely degrades

A6 currently reports **99.7%** of overlapping cross-office sheet pairs sharing a
drawn class in their intersection. Smaller Hydrographic sheets overlap Land
Survey sheets over less ground, so more pairs will intersect in a region
containing nothing both offices draw. The §8.3 invariant is what makes two sheets
of one place recognisably the same place; A6 should be re-measured before, not
after.

### C8. More sheets, less recognition, at the rack

§4.1 splits reading into three ranges: office/era at a glance, survey in hand,
sheet number on inspection. Nine varied rectangles differ from each other by
shape and content. Twenty-nine coast strips differ only by **number**, which is
the range that requires deliberate reading. §4.2 warns that when everything needs
reading, the rhythm collapses into work.

---

## 4. What the proposal does get right

- **It is historically accurate.** Real hydrographic surveys do follow the coast,
  at varying orientation, and a bounding-box lattice over open water is the least
  convincing thing the current cutter does.
- **It would help F8.** Gaps are currently **0%** of land — R1.8 ("some ground has
  no sheet covering it at all") is not satisfied, because Hydrographic's rects
  blanket the island. Coast-hugging strips cover strictly less interior, so
  unsurveyed ground would appear for free. This is the strongest argument for the
  change, and it is about *shape*, not count.
- **Less wasted paper.** Current Hydrographic rects contain a lot of open sea.

---

## 5. Decisions taken

Recorded after review of the above.

| # | decision | which cons it clears |
|---|---|---|
| D-H1 | **Coast only.** No river or lake surveys. | C2, C3, C4 — entirely |
| D-H2 | **Per-sheet rotation is permitted** for this office. | C1 accepted, not avoided |
| D-H3 | **Smaller sheets**, since a coastal strip needs no inland depth. | addresses the count |
| D-H4 | **Overlap 10%**, down from 20%. R2.5 permits 10–25%. | addresses the count |

D-H1 removes three of the eight cons outright: with rivers and lakes out of
scope, there are no blank sheets, no missing `Lake` feature, and the atoll
lagoon's non-existence stops mattering.

D-H2 is an acceptance, not a fix. C1's structural costs are still owed and are
detailed in §6.

---

## 6. Resulting design

### 6.1 Sheet size and count

At the measured mean of 27.1 km of coastline, with 10% along-track overlap:

| format | map area | ground along-track @ 1:2500 | step | sheets/island |
|---|---|---|---|---|
| A1 landscape (today) | 761 × 514 mm | 1902 m | 1712 m | ~16 |
| **A2 landscape** | 534 × 360 mm | 1335 m | 1201 m | **~23** |
| A3 landscape | 380 × 257 mm | 951 m | 856 m | ~32 |

**A2 at 1:2500 is the recommendation.** It lands in the 20–30 target, and its
900 m of cross-track depth covers roughly 450 m either side of the shore rather
than A1's 1285 m — which is the "coast doesn't care about the internal" point
expressed as paper.

This introduces a second `SheetFormat`, which is new but not unprecedented: scale
is already per-office (`Tuning.CoastalScaleDenominator`), so format follows the
same pattern.

### 6.2 What per-sheet rotation actually costs

The data model already allows it. `Sheet.RotationDeg` is a per-sheet readonly
field; today the constructor merely copies `survey.RotationDeg` into it. Adding
an overload that takes a rotation is trivial.

What has to be **replaced** rather than extended:

- **Frame space stops existing for this office.** §2's frame is defined by one
  survey rotation. A coast-following survey has no single frame, so §10.2 steps
  2–5 (project the land bbox, lay a lattice, transform centres back) do not
  apply. The Hydrographic path becomes: walk the coastline polyline, place a
  sheet every `step` metres of arc length, orient each to the local tangent.
- **`SurveySpec.RotationDeg` becomes meaningless for Hydrographic.** It should
  either be documented as "nominal, per-sheet rotation governs" or the office's
  rotation derivation (D2's PCA of the main loop) dropped for this office
  entirely. Dropping it is cleaner: it exists to orient a lattice there is no
  longer any lattice to orient.
- **Numbering changes basis.** §10.2 step 7 is row-major in frame space, which is
  undefined here. The natural replacement is **along-track order**, which is
  arguably better: a survey that walked the shore numbers its sheets in walking
  order, and R2.10b's "a gap in a run is unambiguous" still holds.
- **Multiple loops need a stated rule.** Order loops by the existing total order
  (length descending, ties by first vertex), walk each in turn, number
  continuously across the whole survey. Loops shorter than one sheet's reach
  still get one sheet. **Measured — see §6.6.**
- **A4 still applies unchanged** — numbers must remain 1..N contiguous with no
  duplicates — but it now tests along-track order rather than row-major, so the
  test's ordering assertion needs rewriting.

### 6.6 Loop distribution — measured

12 islands, A2 landscape @ 1:2500, 10% overlap (step 1202 m):

| policy | sheets/island |
|---|---|
| every loop surveyed | **28.9** |
| loops ≥ 200 m only | 27.8 |
| loops ≥ 500 m only | 25.8 |
| main loop only | 15.9 |
| *current lattice cutter* | *9.1* |

**28.9 lands inside the 20–30 target, so A2 @ 1:2500 @ 10% is confirmed** with no
change of format.

Loop length across 99 loops: p10 121 m, p25 364 m, p50 1063 m, p75 2762 m,
p90 7886 m, max 38.1 km. Mean 8.2 loops per island.

Three findings:

- **52% of loops are shorter than one sheet** (4.2 per island), and each costs a
  full sheet regardless. But filtering barely pays: dropping everything under
  500 m saves only 3 sheets. A minimum-length rule is therefore optional, and
  its real justification would be fiction rather than economy.
- **Islets are 45% of the survey** — 28.9 sheets across all loops against 15.9
  for the main shore alone. This is the office *in character*, not waste: a
  hydrographic office charts hazards to navigation, and rocks and skerries are
  exactly what it exists to record. The current lattice cutter buries them inside
  large rects.
- **"Main loop" is meaningless for atolls.** Their largest loop is 16–41% of
  total coastline because the ring breaks into arcs; mountainous islands run
  73–94%. Any rule that branches on "the main shore" fails on exactly one
  character — the same reason D2's PCA rotation goes degenerate there. The
  walk-every-loop rule avoids the problem entirely.

**14% of loops are under 200 m** — single rocks. A full sheet for one 150 m
skerry passes A5 (it carries coast and soundings) but is precisely the thin-sheet
case A5b measures, so A5b should be re-read after the change.

### 6.3 Overlap at 10%, and what it does elsewhere

`SurveySpec.OverlapFraction` is already per-survey, so this can be Hydrographic-
only or global.

If applied **globally**, sheet counts scale by (0.8/0.9)² ≈ 0.79 in both axes for
the lattice offices. Land Survey and Garrison contribute roughly 25 of the
current 38-sheet median, so they would fall to about 20. Combined with
Hydrographic rising from 9 to ~23, the island median lands near **47**.

That is above the 38 that was accepted and below the 58 that was rejected. It
should be a deliberate choice, not a by-product — see C5.

### 6.4 Cons that survive these decisions

- **C5, sheet economy.** Still live, quantified above: median moves 38 → ~47.
- **C6, the map table.** Unchanged and now more acute: A2 strips carry *less*
  content than A1 rects, and coastlines are self-similar. §5.4's second v1 proof
  ("does assisted fit feel like landing, or like fighting?") gets harder. This
  remains the strongest reason to build the map table before committing.
- **C7, A6 cross-referencing.** Currently 99.7%; smaller Hydrographic sheets
  overlap Land Survey sheets over less ground. Must be re-measured.
- **C8, reading at the rack.** ~23 coast strips differ mainly by number.

### 6.5 F8 — the benefit that did NOT materialise

Predicted here that shore-hugging strips would leave unsurveyed ground and
finally satisfy R1.8. **Built and measured: gaps are still 0.0%.**

Two reasons, both obvious in hindsight:

- **The strip is too deep.** A1 landscape at 1:5000 is 3805 m along-track by
  **2570 m across**, so a sheet centred on the shore reaches 1285 m inland.
  Islands measure 5–7 km across, so a ring of such strips still swallows the
  whole interior. Coverage is a property of the sheet's cross-track depth, and
  walking the coast changed only its *placement*.
- **The other two offices still tile.** Even a shallow Hydrographic strip would
  not create gaps where Land Survey and Garrison lay lattices over the land bbox.

Measured coverage of land by number of detail surveys, 12 islands: 0.0% gaps on
eleven of them, 0.1% on one; 60–100% of land is covered **three or more times**.

So F8 needs a different fix. The levers are cross-track depth (a shallower strip
means a smaller scale or a narrower format) and the lattice offices' extent — not
the Hydrographic placement rule. This does not undo the change, but the change
should not be credited with it.

---

## 6.7 Built — measured results

Target revised mid-review from 20–30 to **10–15** sheets. Implemented as a second
cutter (`CoastWalkCutter`) with **no constant changed**: A1 landscape, 1:5000,
20% overlap — the settings already in place.

| | lattice cutter | coast walk |
|---|---|---|
| Hydrographic sheets | mean 9.2, range 6–13 | **median 15, range 9–21** |
| A5b thin sheets (Hydro) | 0.0% | **0.0% (0 of 280)** |
| A6 shared-class | 99.7% | **99.7%** |
| A4 numbering | pass | **pass** (walk order, 1..N contiguous) |
| unsurveyed land | 0.0% | **0.0%** — see §6.5 |

Island totals rose: mountainous median 38 → 43, fjorded 19 → 27, atoll 16 → 18.
All still inside `requirements.md` §6.1's 30–60 guess for mountainous, below it
for the others.

**The target is hit on average and overshot at the top.** Atolls land at 9–14,
inside the band. Mountainous run 14–21 and fjorded 17–20, above it. A long coast
genuinely warrants more sheets, and R1.9 wants islands to vary in survey depth,
so this is left as measured rather than clamped — but if the 15 ceiling is hard,
the lever is a coarser scale for this office, not a change to the walk.

C7 did not materialise: A6 is unchanged at 99.7%, so smaller Hydrographic sheets
did not cost cross-office referencing. C8 is untested and needs a player.

---

## 6.8 Built, looked at, and rebuilt — the sheet was far too big

The first build kept A1 at 1:5000 and only replaced the placement rule. On screen
it was obviously wrong: twenty huge rectangles at scattered angles, each swallowing
most of the island. The numbers had already said so and were not read closely
enough — 60–100% of land covered three or more times, and 0.0% gaps.

**The failure was cross-track depth, not placement.** A1 landscape at 1:5000 is
3805 m long by **2570 m deep**, so a sheet centred on the shore reaches 1285 m
inland on an island 6.7 km across. Walking the coast changed only *where* those
rectangles sat.

| | ground L × D | sheets | redundancy | depth / island | gaps |
|---|---|---|---|---|---|
| lattice cutter (original) | 3805 × 2570 | 9.2 | — | 39% | 0.0% |
| coast walk, A1 @ 1:5000 | 3805 × 2570 | 14.3 | **29.2×** | 39% | 0.0% |
| coast walk, A1 @ 1:2500 | 1902 × 1285 | 23.1 | 11.5× | 20% | — |
| **coast walk, strip @ 1:2500 + arc** | **2002 × 642** | **14.3** | **3.6×** | **10%** | **7.5%** |

*redundancy = total Hydrographic sheet footprint ÷ island land area.*

Two changes got there:

1. **A dedicated `SheetFormat.CoastalStrip`** — 841 × 297 mm, map area 801 × 257 mm,
   at 1:2500 giving **2002 × 642 m**: about 320 m either side of the shore. A
   survey of a shore wants length along the water and almost no depth inland.
2. **A seeded coast arc.** Each loop is surveyed over a contiguous stretch of
   `Tuning.CoastArcMin..Max` (0.32–0.68) of its length, starting at a seeded
   offset, drawn from `Streams.For(seed, "coastArc", loopIndex)`. Loops needing
   only one sheet are covered whole — you cannot survey 60% of a skerry.

The arc is what D2 removed as circular, returning in the form §7 originally
recommended: **seeded, not derived**. Which stretch an expedition worked is a
historical accident, and deriving it from geometry was the mistake.

### Final measured state

- Hydrographic sheets: **mean 14.3, median 15, range 8–21** (target 10–15)
- Redundancy **3.6×**, down from 29.2×
- **Unsurveyed land 7.5%** — R1.8 satisfied for the first time, and finding F8
  closed. §6.5's correction stands: it was the strip depth plus the arc that did
  it, not the walk.
- A4 numbering passes; A6 99.5%; A5b Hydrographic 0.4% thin (1 of 273)
- Unity 21/21, all source assertions pass

---

## 7. Recommendation

Proceed as decided in §5, with A2 landscape at 1:2500 and 10% overlap, in this
order:

1. ~~Measure the loop-length distribution first.~~ **DONE — §6.6.** 28.9 sheets
   per island surveying every loop, inside the 20–30 target. A2 @ 1:2500 @ 10%
   confirmed; no minimum-loop-length rule needed on economic grounds.
2. **Decide whether 10% overlap is global or Hydrographic-only** (§6.3). Global
   moves the island median to ~47; Hydrographic-only leaves the lattice offices
   alone and lands nearer 52. Note §6.6 revises Hydrographic's own contribution
   from ~23 to ~29, so both figures shift up by about 6.
3. **Implement the coast-walking cutter as a second path**, leaving the lattice
   cutter untouched for Land Survey and Garrison. Both must keep producing
   1..N contiguous numbering (A4).
4. **Re-measure A6, A5b and the gap percentage** immediately. A6 is the
   cross-referencing guarantee, and F8's gaps are the payoff being bought.

Deliberately deferred, and worth revisiting only after the map table exists:
extending contour-following to any other office, and surveying rivers or lakes
(D-H1).
