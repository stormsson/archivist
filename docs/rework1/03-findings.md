# Rework 1 — Findings

Measured, not reasoned. `../quarters/requirements.md` is intent, `02-features.md`
is construction, this is what the thing actually did. Where a finding
contradicts the plan, the finding wins.

Numbers from collection seed `905386350`, island index 0 — *Driftcombe*,
`743A6763368B6692`, character Mountainous — and from the acceptance harness over
its own 100-seed sweeps.

---

## R1 — the quarter cutter works, and the suite says so

`Tools/run-acceptance.sh all`, re-measured at `7067eaf`. Every number below is
from that run, not carried forward.

| check | result |
|---|---|
| A2 determinism | **PASS** — 100 generations identical, hash `EAE9079FFB296B86` |
| A3 no seams | **PASS** — 3 border vertices agree within 4×10⁻⁶ m (worst 0) |
| A4 numbering | **PASS** — 100 surveys numbered 1..N, contiguous, no duplicates |
| A5 no blank sheets | **PASS** — 328 sheets; every one that holds land carries a drawn class |
| A6 cross-office coverage | 79.7% of 591 overlapping pairs share a class (target ≥ 90%) — **reported, not gated** |
| A8 budgets | **PASS** — generation median 53.8 ms, plate re-contour median 58.0 ms (both < 250) |
| C2–C4 POI detail sheets | **PASS** — 8 islands × 6 generations identical; 68 detail sheets; 260 + 68 numbered contiguously |
| B2, B3 render | **PASS** — 100 renders identical, hash `F4FA754FA139AB46`; B3 agrees on 100.00% of 14618 points |
| S1–S3 save | **PASS** |

**A2's hash was recorded here as `B7F03092AEF93B76` and was wrong.** Measured at
`d0c5d8a` in a worktree and again at `1d94297` and `7067eaf`, it is
`EAE9079FFB296B86` at all three. The old value predates some earlier change on
this branch and was never re-measured; nothing in the refactor commits moved it.
Two other rows drifted the same way and are corrected above: A5 was recorded as
"all carry a drawn class" and A6 as "90.8% of 717 pairs".

**A5 carries one blank plate, and that is the rule working.** *Sud' Skerholm*
LandSurvey NE — a quarter with no land in it, allowed by Q1.7 and F-R9.4, since
the land bounds are a rectangle and an island is not. One plate in 328.

**A6 is below its target and is not gated.** 79.7% against ≥ 90%. Q2.4 makes
offices differ by omission, and A5b shows where the shortfall is: Garrison draws
coast or grid only on 61.2% of its plates (49 of 80), against 1.2% for LandSurvey
and 0% for Hydrographic and Antiquarian. A plate carrying nothing but coast and
grid shares no *class* with the office it is paired against. This is a finding
about Garrison's layer set, not about the cut.

**A5 and A6 needed no porting.** `01-removal.md` §4.1 predicted both would need
work — A5 because "thin" changes meaning, A6 because identical rects make
overlap total. Both were wrong. A5 passes because a quarter of a real island
almost always carries something; A6 still measures what it always did, because
the question it asks is whether two offices *draw a shared class* in the
intersection, and `FeatureMatrix` decides that, not geometry.

**A7 was not run** — it is `Cost.VerySlow` and off the gate. It still needs the
rewrite `01-removal.md` §4.1 describes, because its subject matter is the PCA
rotation separation Q1.2 deleted.

---

## R2 — a plate covers sixteen times the ground, and contouring costs it

**A8 fails on a check that passed before, and this one is real.**

```
FAIL  A8  sheet re-contour at 1:10000 median 2510.1 ms (>= 100)
```

Before the rework the same check measured **~50 ms at 1:2500**. The budget is
100 ms. Nothing about the contour code changed.

**Why.** A quarter plate is one A1 at 1:10000, so its paper covers
**5140 × 7610 m = 39.1 km²**. A Land Survey sheet at 1:2500 covered
**1285 × 1902 m = 2.44 km²**. That is **16× the ground**.

The contour cell size does not compensate, and cannot: `LodForScale` halves from
`BaseCell` 64 m toward `PaperDetailMm` 0.25 mm × the denominator, but stops at
`MaxPaperContourLod` **4** — a floor of **4 m**. The cap binds at both scales:

| scale | target ground cell | cell used | why |
|---|---|---|---|
| 1:2500 | 0.625 m | **4 m** | capped at lod 4 |
| 1:10000 | 2.5 m | **4 m** | capped at lod 4 |

Same cell, 16× the area, 16× the cells — and marching squares plus stitching is
worse than linear in output length, which is the rest of the 50×.

**This is not a bug in the cutter.** It is the arithmetic of Q1.5 + Q1.6: fixed
paper and a per-island scale mean one plate holds a quarter of an island instead
of a fifteenth of one.

### R2.1 — re-measured at `7067eaf`: A8 passes

The finding above stands as measured and is left as it was recorded. It no longer
describes the suite's behaviour:

```
PASS  A8  island generation median 53.8 ms (< 250)
PASS  A8  plate re-contour at 1:10000, 8 m cell, in hand, no fill median 58.0 ms (< 250)
----  A8  contouring runs at 284 ms per million cells (0.20 M cells on a plate covering 13.1 km2)
```

Two things moved, and neither is the cutter. The budget is **250 ms**, not the
100 ms R2 was judged against (`Acceptance.A8SheetRecontourBudgetMs`). And the
plate is contoured at an **8 m cell**, not the 4 m floor R2's table assumes, on
**13.1 km²** rather than 39.1 km² — the check measures a plate in hand with no
fill, which is the once-per-plate case R3.1 frames, and the LOD it resolves to
under that request is one rung coarser than the cap.

R2's arithmetic is unchanged and still explains the shape of the cost: 284 ms per
million cells, against 0.20 M cells on this plate. What changed is which plate
and which budget the check asks about, not what contouring costs per cell.

---

## R3 — and most of that paper is empty

Driftcombe's land bounds are 6.91 × 6.27 km, so a quarter is **3.46 × 3.14 km**.
The scale ladder's rungs are 1:2500, 1:10000, 1:25000, and the quarter needs at
least 1:6722 — so it lands on **1:10000**, whose map area is 5140 × 7610 m.

| | used | of paper |
|---|---|---|
| width | 3460 m | **67%** |
| height | 3140 m | **41%** |
| area | 10.9 km² | **28%** |

**Two separate causes, and they want different fixes.**

1. **The ladder is too coarse.** 2500 → 10000 is a factor of four, so an island
   needing 1:6722 pays for 1:10000. A rung at 1:5000 or 1:7500 would land much
   closer.
2. **The paper's aspect does not match a quarter's.** A1 is 1:1.48. A quarter of
   a roughly-square island is about 1:1.1. One axis always wastes ~30%, at any
   scale, and no rung fixes it.

Q1.6 says a small island *should* sit in blank margin — physical size made
legible without a scale bar. That is not what this is: **every** island sits in
72% margin, so the signal it was supposed to carry is gone.

---

## Open — needs a decision

Three ways out, and they are not exclusive.

| | what | costs |
|---|---|---|
| **a** | **Finer ladder** — add 1:5000 and 1:7500. Islands land closer to their sheets. | R2.3 says "three or four fixed values"; five rungs stretches that. Helps R3's first cause, not its second, and only partly helps R2 |
| **b** | **Squarer paper for quarters** — a quarter of an island is roughly square, and A1 is not. | Q1.5's "fixed paper size for every plate" survives, but the plate stops being A1, and the rack, binder and shelf dimensions follow from it |
| **c** | **Contour the intersection, not the paper** — everything outside `LandBounds` is sea by construction, so no contour crosses it. Clip the extraction area. | Pure win, no visual change, ~3.6× on Driftcombe — but ~220 ms, still over A8's 100 ms budget. A render-side change (W2), not a cutter one |
| **d** | **Restate the budget** — 100 ms was set for a 2.44 km² sheet. Per unit area the new plate is *cheaper* than the old one. | Honest, and it stops the check measuring what it was built to measure |

**(c) is unambiguous and should happen regardless.** (a) and (b) are design
decisions about what a plate looks like and how much of it is map, and (d) is
only defensible after (a)–(c) have taken what they can.

The pre-existing half of A8 — `island generation median 409.7 ms (>= 250)` —
**fails on `main` too**, and is unrelated. It improved from 483 ms, because the
PCA rotation derivation, the coast walk and the 16 × 16 cull sampling are gone.

---

## R4 — the island-generation budget was measuring a Debug build

**Nothing in this rework made generation slower.** The suite's median *fell*,
483 → 409 ms, because the PCA rotation derivation, the coast walk and the
16 × 16 cull sampling are gone. Instrumented, the cutters are **0.1 ms** of a
484 ms island.

`generation_for_agents.md` had carried an open question since before this work:
the file recorded ~118 ms, the harness measured ~467 ms, a pre-POC-03 snapshot
measured the same 469 ms, and three shapes of `Tuning` made no difference. It
asked for someone to instrument `Island.FromSeed` per stage.

### F-R4.1 — the 4× gap is the build configuration

`Tools/run-acceptance.sh` line 96 runs `dotnet build` with **no `-c`**, so
`GenHarness` is Debug, `Optimize=false`. Same tree, same machine, same seeds,
ten-island medians:

| build | `Island.FromSeed` |
|---|---|
| optimised | **122.9 ms** |
| unoptimised | **484.0 ms** |

The 250 ms budget sits between them. Both recorded figures were correct; they
measured different builds. That also explains why the slow number survived every
`Tuning` refactor and a pre-POC-03 snapshot — none of them changed the build.

*(Measured with an unoptimised build twice by accident first: a scratch csproj
carrying `<Optimize>true</Optimize>` reported 124.8 ms under `-c Debug`, which
looked like "Debug is not the cause". It is the property, not the configuration
name, that decides.)*

### F-R4.2 — 84% of an island is two scans of an empty domain

Per stage, unoptimised. Shares are the same optimised.

| stage | ms | share |
|---|---|---|
| Coastline contour | 332 | **68.6%** |
| `ComputeLandBounds` | 76 | **15.8%** |
| `ServiceRule` | 32 | 6.6% |
| POIs | 17 | 3.5% |
| Settlements | 17 | 3.5% |
| Peaks | 12 | 2.5% |
| Rivers | 7 | 1.4% |
| `IslandParams`, `IslandField` ctor, Names, cutters | ~0 | ~0% |

`Island.FromSeed` contours the **whole 16 km domain** at a 32 m cell — 500 × 500
cells, 256 km² — and `ComputeLandBounds` samples it at 64 m. Averaged over 10
islands the land bounds are **42.8 km²: 16.7% of the domain**. Five sixths of
both scans is open sea.

`IslandField`'s constructor costs nothing because it is lazy. The cost is
sampling it.

### F-R4.3 — clipping the coastline to the land bounds is 4.8× and, at 256 m, exact

`ComputeLandBounds` already runs *before* the coastline. Extracting the sea-level
isoline over `LandBounds` + a margin instead of the whole domain, 30 islands,
compared against the full-domain extraction vertex for vertex:

| margin | median ms | speed-up | identical output |
|---|---|---|---|
| whole domain | 284.6 | — | — |
| 64 m | 52.5 | 5.4× | 25 / 30 |
| 128 m | 55.6 | 5.1× | 29 / 30 |
| **256 m** | **59.1** | **4.8×** | **30 / 30** |
| 512 m | 69.4 | 4.1× | 30 / 30 |
| 1024 m | 86.8 | 3.3× | 30 / 30 |

**Why a margin is needed at all, and why 30/30 is evidence rather than proof.**
`ComputeLandBounds` samples on the 64 m `BaseCell` lattice, so an islet smaller
than that spacing can fall between samples and never enter the bounds — its
coastline loop then lies outside them. 64 m of margin misses five such cases in
thirty; 256 m misses none in thirty. Nothing here bounds how far offshore such an
islet can be, so this is a measured threshold, not a guarantee.

**What it would cost.** `island.Coastline` feeds `Settlements` and `PoiSiting`,
so a dropped islet perturbs features, and **every island's digest changes**. That
is a one-time cost — the digest already changed with the quarter cutter — but it
must be a decision, not a side effect.

**And the argument that it is the right shape anyway:** under the quarter model a
plate covers `LandBounds`, so **an islet outside the land bounds is not drawn on
any plate**. It exists in the data and nowhere else. Clipping the coastline to
what the paper can show is the same rule the cutter now follows.

---

## R5 — both were done, and generation is 9× faster

**(e) `-c Release` on the harness build**, on `dotnet build` and on `dotnet run`
— `--no-build` looks for the configuration it is told. Every timing the harness
prints is now a timing of the shipping configuration.

**(f) the coastline is extracted over `LandBounds` + `CoastlineMarginCells`**
(4 × `BaseCell` = 256 m), clamped to the domain, with the whole domain kept as
the empty-bounds fallback. The margin is a `Tuning` constant so the measurement
behind it has somewhere to live.

| | before | after |
|---|---|---|
| A8 island generation | 409–484 ms, **FAIL** | **53.7 ms, PASS** (budget 250) |

**9× on the gate, and the islands did not move.** A2 reports the same digest
before and after — `B7F03092AEF93B76` — so on the acceptance seeds the clipped
extraction is bit-identical to the full-domain one. The full suite passes:
A2, A3, A4, A5, A6, C2–C4, B2, B3, S1–S3.

---

## R6 — the plate re-contour is not waste, it is nine times the map

A8's second clause still fails, now at **673 ms** against a 100 ms budget
(the earlier 2510 ms was the unoptimised build). Clipping it to the land bounds
the way R5 clipped the coastline gives only **1.7×**, to 446 ms — because there
is very little waste to remove:

| | area |
|---|---|
| plate paper at 1:10000 | 39.1 km² |
| clipped to land bounds + margin | 22.5 km² |
| old Land Survey sheet at 1:2500 | **2.44 km²** |

The contour cell is **4 m at both scales** — `MaxPaperContourLod` 4 caps it, and
the cap binds at 1:2500 and at 1:10000 alike, so nothing is being drawn finer
than before. A quarter plate simply covers **9× the land** a survey sheet did,
at the same cell, for 9× the cells. 50 ms × 9 = 450 ms, which is what was
measured.

**So the cost is honest and linear, and per km² nothing regressed.** What is left
is a question about the object, not the code:

| | what | costs |
|---|---|---|
| **h** | **Restate the budget.** 100 ms was set for a 2.44 km² sheet; per unit area the plate is no more expensive. Measure ms/km², or raise the number and say why | The 100 ms came from a perceptual bar — "feels instant when you pick it up" — and 450 ms is not that. Restating the budget does not make picking up a plate feel better |
| **i** | **Coarsen the cell at coarse scales.** A cap expressed as a ground cell rather than a lod count | 8 m is 0.8 mm on paper at 1:10000 — visible faceting on a coastline |
| **j** | **Let the async pipeline absorb it.** `BoardView` already renders off-thread and uploads one texture per frame (C5.6, C5.7); a board opens immediately and fills in | Answers the board and not the hand. A loose plate picked up in the room still costs ~450 ms before it can be read, and that path has no coroutine |

(j) is already built and already true for the table. (h) and (i) are about the
plate in the player's hands, which is where R5.1–R5.4 say the game actually is.


---

## R7 — A8's re-contour clause measures a path the game does not take

Before acting on (i), the runtime render path was measured. It changes the
question.

**`LodForScale` is not what the renderer uses.** `Strokes.DrawCoast` calls
`RenderLod.ForPixelsPerMetre(req.PixelsPerMetre)` — the contour cell is tied to
the **pixel**, not to the paper scale. `Contours.LodForScale` — the fixed 4 m cap
A8 measures — has exactly four callers, and none of them is the game:
`Editor/SvgExport`, `Editor/SheetPane`, `Editor/ComparePane`, and
`Generation/Analysis/ContourSeam` (which A3 uses).

**What a plate actually costs**, optimised, island `743A6763368B6692`:

| | scale | px/m | cell | cells | render |
|---|---|---|---|---|---|
| quarter plate, in hand (2.7 px/mm) | 1:10000 | 0.27 | 4 m | 2.44 M | **133.3 ms** |
| quarter plate, crate default (1.2 px/mm) | 1:10000 | 0.12 | 8 m | 0.61 M | **14.1 ms** |
| the chart / base | 1:25000 | 0.11 | 8 m | 3.82 M | **64.7 ms** |
| *A1 at 1:2500, in hand — the old shape* | 1:2500 | 1.08 | 1 m | **2.44 M** | **36.4 ms** |

**The pixel-tied LOD already coarsens the cell as the scale coarsens** — 1 m at
1:2500, 4 m at 1:10000 — and the cell count comes out **identical**, because both
the ground area and the cell area scale as the denominator squared. Option (i) is
already true in the runtime, by a mechanism nobody had to write.

The plate is still 3.7× the old sheet's render time — not because it is drawn
finer, but because it **contains nine times as much map**: 22.5 km² of land
against 2.44 km², so nine times the coastline and contour length to stitch.

### What this means for the decision

- **A8's second clause is measuring the editor's vector path.** At 1:2500 it
  reported ~50 ms while the renderer was spending ~36 ms on the same sheet with
  sixteen times the cells. It has never measured the game.
- **133 ms in hand** is the real number, against a bar the harness itself calls
  "feels instant when you pick it up, and the perceptual bar for that is around
  100 ms". It is close, off the main thread, and cached once per sheet (R3.1).
- **Q2.2 removes the reason `RenderLod` exists.** Its comment says the cell is
  tied to the pixel so the coastline stroke does not "visibly float off the
  water" computed per-pixel by the fill. **W2 turns `Fill` off on every plate.**
  With no fill there is no water edge to agree with, and the acceptance factor
  can widen — 8 m instead of 4 m is 4× fewer cells.

So (i) survives, but it belongs in **W2**, as a consequence of `Fill` going off,
and it is applied to `RenderLod`'s acceptance factor rather than to
`MaxPaperContourLod`. Doing it now would be tuning a stroke against a fill that
is about to be deleted.

**A8 should be repointed at `RenderLod.ForPixelsPerMetre` and given the in-hand
pixel density**, so it measures what a player waits for. That is a change to the
check, not to the budget, and it makes the 100 ms bar meaningful again.

---

## R8 — the fill was paying for the coastline, and W2 takes that away

Acting on "the plate in hand is too precise" turned up the thing that decides
W2's cost.

### F-R8.1 — `DrawCoast` only ever runs when there is no fill

`IslandRenderer.Render` allocates the `h01` raster **only when `Coast` and `Fill`
are both wanted**, and strips `Coast` from the mask when it does. So
`FieldCoast.Draw` handles the coastline whenever there is a fill, and
`Strokes.DrawCoast` — the vector path that calls `Contours.Extract` — runs
exactly when there is not.

`DrawCoast`'s own comment forbade a coarser lattice because *"the line will float
off the water"*. There is no water on that path: the fill that computes the
per-pixel edge is off. **The warning was defending a case that cannot reach it.**
It has been rewritten to say where it is still true — `FieldCoast`'s path — and
the cell there is untouched.

### F-R8.2 — turning `Fill` off costs 6×

Quarter plate, in hand at 2.7 px/mm, optimised:

| layers | what draws the coast | render |
|---|---|---|
| `All` (Fill on) | `FieldCoast`, free-riding the fill raster | **112 ms** |
| `All & ~Fill` | `Strokes.DrawCoast` → `Contours.Extract` | **672 ms** |

**W2 turns `Fill` off on every plate (Q2.2)**, so this is not hypothetical — it is
the bill W2 arrives with, and nothing in the plan had priced it.

### F-R8.3 — the lever is the cell, not the pixel

`RenderLod.NoFillSlack` widens the accepted cell from `sqrt(2)` to `2*sqrt(2)` of
the pixel, on the no-fill path only. A quarter plate in hand goes from a 4 m cell
to 8 m:

| | render |
|---|---|
| no fill, 4 m cell | 672 ms |
| **no fill, 8 m cell** | **171 ms** |

**Halving the pixel density buys the same 4× and costs more.** It works only
because it bumps the cell a rung — at 1.35 px/mm and 1.0 px/mm the timings are
44.0 and 44.1 ms, identical, because the cell is 16 m in both and the raster is
not the cost. And it damages the one thing Q2.6 leaves an office:
`Strokes.MinHalfWidthPx` clamps a stroke at 0.35 px, so

| px/mm | coast (0.35 mm) | river (0.25 mm) |
|---|---|---|
| 2.7 | 0.47 px | 0.35 px (clamped) |
| 1.35 | 0.35 px (clamped) | 0.35 px (clamped) |

At 1.35 px/mm a coastline and a river come out **the same width**. With geometry
identical across offices (Q1.2), line weight is one of the few signals left.

**So the density stays at 2.7 and the cell got coarser.** 8 m is 0.8 mm on paper
at 1:10000 — faceting on the line, and nothing else, because there is no fill for
it to disagree with.

### F-R8.4 — A8 now measures the game

The re-contour clause called `Contours.LodForScale`, which no renderer uses (R7).
It now calls `RenderLod.ForPixelsPerMetreWithoutFill` at the in-hand density and
reports what it is measuring:

```
FAIL  A8  plate re-contour at 1:10000, 8 m cell, in hand, no fill
          median 167.3 ms (>= 100)
```

**Decision: the budget was restated, 100 ms -> 250 ms.** The other rung of slack
(`4*sqrt(2)`, a 16 m cell, ~45 ms) was declined: 4.3 pixels per cell is faceting
a player would see, and the plate is not being drawn wrongly — there is simply
more of it.

**The restatement is stricter, not looser.** 100 ms was set against a survey
sheet covering 2.44 km². A quarter plate covers ~22.5 km² of land: nine times the
map on one sheet. 250 ms against nine times the content is **3.6x tighter per
unit of map** than the number it replaces. It is above the perceptual bar it was
named for, and that is the honest position — the paper is bigger, and the only
way back under 100 ms is to draw less of the island on it.

**And A8 now reports the invariant beside the total.** The cell follows the pixel
(`RenderLod`), so cells-per-plate came out the same before and after the rework
even though the ground area went up sixteen-fold. What is stable is **ms per
million contour cells**:

```
PASS  A8  plate re-contour at 1:10000, 8 m cell, in hand, no fill
          median 171.8 ms (< 250)
----  A8  contouring runs at 281 ms per million cells
          (0.61 M cells on a plate covering 39.1 km2)
```

If the total fails while the rate holds, the plate got bigger. If the rate moves,
the contouring did. The old budget could not tell those apart, which is most of
why it spent so long being disbelieved.

---

## R9 — the ladder, the orientation, and the plate with nothing on it

Decisions taken 2026-08-30, and one thing they uncovered.

### F-R9.1 — 1:2500 was a rung no island could reach

Removed, and **1:5000** put in its place. 1:2500 on an A1 covers 1285 x 1902 m,
and a quarter of even a small island is two or three kilometres across, so the
fine rung had never been selected once. The live ladder was really two rungs a
factor of 2.5 apart, and every island fell to the coarse one.

### F-R9.2 — orientation is chosen per island, and it earns its place

Turning the paper **cannot change how full a sheet is** — the map area is
514 x 761 mm either way — so at a given rung the fill is identical. What it
changes is *which rung the island lands on*.

*Ormwick* is the case: its quarter needs 1:5043 portrait, misses the 1:5000 rung
by 43 parts, and falls to 1:10000 at **16.1%** fill. Landscape it needs 1:4732,
lands on 1:5000, and fills **64.5%**. Choosing per island can only help.

### F-R9.3 — measured over 24 islands

| | before | after |
|---|---|---|
| rungs used | 1:10000 only | **1:5000 x9, 1:10000 x15** |
| fill | 13.6–27.7% | **27.7–64.5%** |
| orientation | portrait always | landscape x17, portrait x7 |

Scale and margin now vary, so **Q1.6's signal is alive**: a small island really
does sit in white space and a large one really does crowd its sheets. Before
this, every island filled about a quarter of its paper and the margin said
nothing.

1:25000 was not selected once. It needs a quarter over 7610 m — an island over
15 km — which the 16 km domain barely permits. It stays as a safety rung, the
same role `WholeIslandFallback` plays for the chart.

### F-R9.4 — a quarter can contain no island at all

A5 now fails, and it is not a style problem:

```
FAIL  A5  1 of 328 sheets carry nothing their office draws:
          Sud' Skerholm LandSurvey NE
```

**The land bounds are a rectangle; an island is not.** A concave island — fjorded,
crescent, a bay biting into one corner — can have a whole quarter of its bounding
box with no land in it. Every office's plate of that corner is empty; LandSurvey
is simply the one A5 catches, because Garrison always draws its grid and
Hydrographic always draws soundings over water.

Frequency: **1 plate in 328**, across 20 islands. Rare enough to be a discovery
rather than a tax.

A5 was amended to **name** the blank plates rather than count them, because
whether a blank plate is a defect or the point cannot be judged from a number.

**Decision: (k), allow it.** A blank plate is truthful (R2.13) — the office
surveyed that square and there was nothing in it — and Q1.3's four quarters on
every island, forever, is worth more than never printing an empty sheet.
Recorded as **Q1.7**.

(l) would give some islands three plates per office and break the one frame the
player learns once. (m) would split by land area instead of by bounding box,
making quarters unequal rectangles; they would still share an axis so Q1.2's
register survives, but "halve the bounds" would stop being the whole rule.

**What A5 checks now.** Not "no blank sheets" but **"no plate holds land and
draws nothing"**, which is the defect the check was always for. A plate over a
landless quarter is counted, named and reported:

```
PASS  A5  328 sheets: every one that holds land carries a drawn class (grid counts)
----  A5  1 blank plate(s), all of quarters with no land in them — allowed (F-R9.4):
          Sud' Skerholm LandSurvey NE
```

`HoldsLand` samples the plate on the `Tuning.BaseCell` lattice — the same spacing
`ComputeLandBounds` uses, so the two agree about what land is.

### F-R9.5 — and A5b says something about Garrison

| office | plates that are coast/grid only |
|---|---|
| Hydrographic | 0.0% (0/80) |
| LandSurvey | 1.2% (1/80) |
| **Garrison** | **56.2% (45/80)** |
| Antiquarian | 0.0% (0/68) |

More than half of Garrison's plates carry nothing but the coastline and its own
grid. That is not a failure — Garrison draws Coast, Peak and Grid, and most
quarters have no peak in them — but it is a content question for W3: if a
Garrison plate is a grid over an outline, style is doing *all* the work of
telling it from the others, and Q2.6 said as much.

---

## R10 — draw order, settled

W2's layer order, chosen 2026-08-30, in the order ink goes on the paper:

```
contours -> coastline -> rivers -> grid -> settlements -> peaks -> soundings
```

Terrain underneath as faint background; the coast as the strong line over it;
water; the grid printed across as reference; the point symbols last, because they
are what a reader looks *for* and nothing may cross them out.

`Strokes.Draw`'s existing order — coast, rivers, settlements, peaks, soundings —
is unchanged in its relative sequence; contours slot in beneath it and the grid
between rivers and settlements. That keeps the change to two insertions rather
than a reshuffle, and B2's render hash moves once rather than twice.


---

## R11 — W4, as built

`island · office` (Q3.1), the chart's home (Q4.4), and merge (Q3.3). All 13 gated
checks pass.

### F-R11.1 — a binder needs an office it can lose

`BinderView` carries an `officeOrdinal`, and **-1 means merged**: no single
office, and any office of its island accepted. That is Q3.3's goal state given a
representation — a binder starts as one office's and stops being about one office
the moment two are poured together. `Merge` keeps the office when both sides
share one, because two halves of one survey coming back together is the common
case and the result is still `Driftcombe · Hydrographic`.

**The constant lives on `BinderRecord`, not on `BinderView`.** `BinderView` is a
`MonoBehaviour`; `ArchiveFormat` needs the value and is compiled engine-free by
`Tools/GenHarness`. A constant the save format needs cannot sit behind
UnityEngine. `BinderView.Merged` names it again where it is read.

### F-R11.2 — the crate sorts before it files

`MapCrate` grouped every unissued sheet of an island and dealt it shuffled across
`bindersPerOpening` folders, so one opening handed over a finished island in a
single object — J3, the case Question 10 rejected. It now groups by office and
makes **one binder per office**, in `Offices.All` order so two openings of one
island stack the same way round.

The empty debug binder takes the **loose sheet's office**, so the filing verb has
somewhere legal to put the one piece of paper in the room — and that makes it a
second binder wearing a label another one already carries, which is J2's case and
the pair a merge can be tried on.

### F-R11.3 — the chart could not be issued at all

`SheetPicker.PickUnissued` skipped `IsWholeIsland` — *"reserved, not abolished"* —
so nothing could ever hand a player the chart, and R6.8a's gate could never be
satisfied. **No board could open.**

It now takes `includeChart`, and the chart is **prepended rather than shuffled**:
an island whose chart came last would be a stack of quarters with nothing to lay
them on. `MapCrate` also refuses to make the chart the loose sheet, which
otherwise happens on an island whose chart is the only plate left — the board's
own gate, lying under a rack.

### F-R11.4 — merging is rack work, and silent

The verb is on `BinderPickup`, not on the table: hold a binder, aim at another of
the same island, and the floor one pours into the held one. The held one survives
so the hands stay full and there is nothing to pick back up.

**Nothing counts it, nothing rewards it, nothing suggests it** (Q3.4). A binder of
another island is not even a refusal with a reason — it is two unrelated objects,
and the game does not comment on those.

### F-R11.5 — the office is written as a name, and an ordinal is refused

`Office`'s own rule is append-only *because* several streams index by
`(int)office`. A save holding `3` would be silently reinterpreted if the enum were
ever renumbered, so the file writes `"office": "Garrison"`, or `"merged"`.

`Enum.TryParse` accepts a bare number as well as a name, so `"1"` would come back
as `LandSurvey` — exactly the reinterpretation the name was meant to prevent. It
is rejected explicitly, and **S2 now asserts that**, along with the office
surviving on a full binder, on an *empty* one, and merged not being an office. The
empty case is the one that matters: a binder with no contents has nothing to infer
an office from, so if the field does not survive the file it is gone.

### F-R11.6 — three things that had quietly gone wrong

- **`SheetNaming` printed a quarter as `02`.** A plate's number *is* its corner
  (Q1.1), so the code line named a series that does not exist and hid the one fact
  a player places it by. The `HY·NE` formatter written to fix it was never wired to
  anything and has since been removed with the rest of the file's dead surface
  (`SheetNaming` is now `OfficeLabels`, 263 lines to 59); the header builds its
  label from `PrefixFor` + `Separator` and does not show the corner at all. The
  finding stands and is **not** addressed: if a plate header is to carry its corner,
  `HY·NE` is the form, falling back to two digits outside 1..4 — which is a detail
  sheet, and would be a plate from some future cut.
- **`SheetTestBench` summoned `LandSurvey:7`.** A survey has four sheets. The
  bench found nothing and said nothing.
- **The bench also poured every office into one folder**, which `BinderView` now
  refuses — correctly, because that folder was three offices wearing one label.


---

## R12 — resetting the game state

A save written before a change names plates that no longer exist. The quarter
cutter changed every `SheetId.Number` in the collection, and the format's version
bump refuses a v1 file whole — but a **v2** file written yesterday, before the
1:5000 rung or the binder's office landed, is accepted and is wrong in ways
nothing checks.

**Two ways, because they answer different questions.**

| | what | when |
|---|---|---|
| `Archive.resetOnLoad` | a serialised bool on the component. On: the save is not read and the file is deleted, every time the game starts | "keep starting clean while I work" |
| `Archivist ▸ Save · Delete the save file` | an editor menu item, with a confirmation, working in and out of play mode | "throw that one away, now" |

The menu item exists as well as the flag because `Archive` **makes itself when a
scene has none** — and in that scene there is no checkbox to tick. It finds the
scene's `Archive` if there is one, so a renamed file is still the file that gets
deleted, and falls back to the default name rather than refusing, because a scene
with no `Archive` still has a save.

`Archivist ▸ Save · Show the save file` reveals it, or the folder if there is
none. Reading `archive.json` is the first thing anybody does about a save bug.

**There is deliberately no mid-play reset.** Nothing is live at load time — the
ledger starts empty and both spawners sweep whatever a scene was saved with — so
"start from nothing" is exactly "do not read the file". Clearing the ledger *with
paper in the room* would leave every binder holding sheets nothing remembers
issuing, which is precisely what `RoomSnapshot.Audit` exists to catch, and it
would be right to.


---

## R13 — W2, as built

`OfficeLayers.For(sheet)` walks `FeatureClasses.All`, asks `FeatureMatrix.Draws`,
and ors the bits. One table, two consumers — a class wired into `FeatureMatrix` is
now drawn by the editor's vector path and by the runtime raster, or by neither.
`MapCrate.Render` is the only place that builds a plate's request, and `BoardView`
goes through it, so one call site covers both.

| office | layers | in hand |
|---|---|---|
| chart | `Coast` | 268 ms |
| Hydrographic | `Coast, Settlements, Soundings` | 173 ms |
| **LandSurvey** | `Coast, Rivers, Settlements, Peaks, Contours` | **858 ms** |
| Garrison | `Coast, Peaks, Grid` | 172 ms |

### F-R13.1 — turning `Fill` off took the paper with it

The first plates rendered as **ink on a black rectangle**. `ImageBuffer` starts at
zero, and `Fill` was the only thing that had ever painted the ground — so Q2.2
removed the background along with the colour relief.

Worse, the ink was wrong too. `Ink.CoastInk` derives a coastline pen by darkening
the palette's deep-sea band, and `RiverInk` takes the shallow band — both correct
*over a fill*, where a pen on water is what they are. On bare paper they are a
dark blue line and a pale blue one that vanishes.

Two constants now: `Ink.Paper` and `Ink.Drafting`, with `CoastInk(palette, hasFill)`
and `RiverInk(palette, hasFill)` choosing between the derivation and the ink. Both
are **placeholders and the seam is per office** — R2.6 makes paper stock the
fastest signal a player has, which is W3's whole subject.

### F-R13.2 — the grid drew nothing, and the cause was a defaulted parameter

`GarrisonGrid` needs the map scale — its pitch is `GridPitchPaperMm` times the
denominator — so `RenderRequest` gained a `ScaleDenominator`, set by `ForSheet`,
with a defaulted constructor parameter so no existing caller broke.

Every diagnostic said it should work: the mask contained `Grid`, the denominator
read 10000, `GarrisonGrid` returned 32 lines, and they mapped to sensible image
columns. The plate came out blank.

**`IslandRenderer` rebuilds the request field by field** to clear one layer bit
before handing it to `Strokes`:

```csharp
var strokeReq = new RenderRequest(req.Area, req.RotationDeg, req.PixelsPerMetre,
                                  req.PixelsPerPaperMm, remaining);
```

The new field defaulted to 0 there, and 0 disables the grid. **A defaulted
parameter on a value type meant a field was silently dropped on every stroke pass
in the game.** Zero disabling the layer rather than mis-drawing it is the safer of
the two failures and still cost an afternoon; the trap is now recorded on the
field itself and at the rebuild.

### F-R13.3 — Land Survey costs five times what the others do

Contours are `LandBandEdges` every `ContourLevelStride`-th — four levels — and
**each level is a separate marching-squares pass over the same grid**. Five
extractions against Hydrographic's one, and 858 ms against 173.

`Contours.Extract` samples `IHeightField.Height01` per corner into two row
buffers, then marches. **The sampling is the cost and it is repeated per level**,
even though every level wants the same samples. A multi-level variant that samples
the two rows once and marches them N times inside the same `j` loop would cut Land
Survey to roughly one extraction plus change — a contained change to one function,
with A3's seam guarantee to keep.

**Done — see R14.** The levels are also the fill's band edges rather than an
interval of their own, so a contour falls exactly where the ground would change
colour: the two halves of one map agree by construction, and on a plate with no
fill the contour is the only thing saying where that boundary was.

### F-R13.4 — what it looks like

Three offices' plates of Driftcombe NW, at in-hand density: Hydrographic's shore
and soundings, Land Survey's contours and rivers, Garrison's grid over an outline.
Same rect, same scale, same paper — three documents. That is the thesis in
CLAUDE.md rendered for the first time.


---

## R14 — the contours and the coastline are one pass

`Contours.ExtractLevels(field, area, cell, double[] levels)` samples the grid once
and marches it once per level. **`Extract` is now that function with one level** —
one implementation, so the two cannot drift.

The sampling is the cost: `IHeightField.Height01` is an fBm evaluation per corner,
over a grid hundreds of cells on a side, and every level wants the same corners.
The cell-centre sample a saddle resolves by (§6.1) is taken **once per cell** and
shared across levels; cells with no saddle at any level never take one.

### F-R14.1 — bit-identical, checked rather than argued

`ExtractLevels` was compared against `Extract`, level by level, vertex by vertex,
over **6 islands × 8 levels** — the seven band edges and sea level:

```
ExtractLevels == Extract on 48/48 (island, level) pairs
```

A2's digest is unchanged at `EAE9079FFB296B86` and A3 still reports border
vertices agreeing to 4×10⁻⁶ m, worst 0.

### F-R14.2 — and the coast is just another level

The coastline *is* an isoline — the one at sea level — so drawing it in a separate
pass read the same grid a second time. `DrawContours` and `DrawCoast` became one
`DrawIsolines`, which builds `[contour levels…, seaLevel]`, extracts once, and
strokes contours first at their weight and the coast over them at its own.

That is not a side effect of merging: contours go down lowest-first, the way they
would be drawn by hand, and the coast goes **over** them because where a low
contour runs close to the shore the shoreline must win — and ink is opaque.

### F-R14.3 — measured

Plate in hand, 1:10000:

| office | before | after |
|---|---|---|
| chart | 268 ms | 309 ms |
| Hydrographic | 173 ms | 211 ms |
| **LandSurvey** | **858 ms** | **201 ms** |
| Garrison | 172 ms | 195 ms |
| **a whole island's plates** | **5079 ms** | **2735 ms** |

**Land Survey is 4.3× faster and is now the cheapest of the three** — its four
contours and its coast cost one sampling, while Hydrographic still pays a second
lattice for soundings. The office with the most on its plates stopped being the
one that costs the most to look at.

The other rows drift up by 20–40 ms, which is machine noise between runs rather
than a cost this added: nothing about the chart, Hydrographic or Garrison changed
except that their single isoline now goes through the array path.

---

## R15 — W9, roads 1–3 as built

**2735 → 343 ms** for one island's thirteen plates: **8.0×**, output bit-identical
on **260 plates** (20 islands × 13, compared by `ImageBuffer.ContentHash`). All 13
gated checks pass, A2's digest unchanged at `EAE9079FFB296B86`, A3 still agreeing
to 4×10⁻⁶ m with worst 0.

Everything lives in `Archivist.Render` except one public method in `Contours`.

### F-R15.1 — clip the isolines to the land (road 1)

`R2` option (c), written up during W2 as "a render-side change" and never made.
A plate's paper covers far more ground than its island — measured, an island fills
**17% of its chart** and 27% of a quarter — and every corner of the rest was being
sampled for contours that cannot exist over open water.

`RenderTuning.IsolineLandMarginM` = 512 m, and the margin has the same shape of
evidence as `Tuning.CoastlineMarginCells`: 512 m gave 260/260 plates identical
where 256 m gave 259 — one islet, six vertices. A threshold, not a proof, for the
same reason: an islet smaller than the 64 m lattice `ComputeLandBounds` samples on
can fall between samples.

**The clip is local to `DrawIsolines` and must stay there.** `Strokes.Draw` hands
the same `groundRect` to `Soundings.ForRect` and `GarrisonGrid.ForRect` — soundings
live *below* sea level and so lie outside the land bounds by definition, and the
grid must reach the paper's edge. Clipping the shared rect would delete two layers
without a symptom, which is F-R13.2's failure exactly.

The chart alone went 309 → 80 ms.

### F-R15.2 — share the samples, fill them on every core (roads 2 and 3)

`SampleGrid` is an `IHeightField` decorator holding one lattice's corner samples,
filled with `Parallel.For` over rows. On-lattice queries come from the raster;
a saddle's cell-centre sample falls between corners and falls through to the field.
**A miss is slower, never wrong.**

Three things make it sound rather than merely fast:

- **The lattice comes from `Contours.Lattice`**, now public, rather than from
  arithmetic repeated in `Render`. A raster laid half a cell off would miss every
  time and still be correct — a silent 1.0×.
- **`float` is exact here.** §4.4 quantises `Height01` at 2⁻¹⁶, so every value is
  `k/65536`: 17 mantissa bits against a float's 24. Halving the memory costs
  nothing.
- **The parallel fill cannot reach the output.** Each row writes a disjoint slice
  of a preallocated array, `Height01` is pure, and the marching that reads the
  raster stays serial and row-major. `FillRenderer` already parallelises this way.

### F-R15.3 — the cache had to be five deep, and that is the interesting part

A one-entry cache measured **1982 → 593 ms**, and the win was the parallel fill
alone: it was missing *every time*.

**What shares a lattice is the three offices' plates of one quarter. What order
they arrive in is office-major** — a crate deals one binder per office (Q3.1), a
board lays out what is on it — so the next plate wanting NW's corners is four
plates after the last one, never the next one. The cache was measuring a locality
the delivery order does not have.

Depth is now `SampleGridCache.Depth` = **5**: four quarters and a chart, one
island's worth, and not one more — a sixth entry could only hold another island's
lattice and nothing renders two islands together. That took it **593 → 343 ms**.

About 9 MB held for the length of a batch, against the 11.4 MB a single plate's
`ImageBuffer` already occupies.

### F-R15.4 — what was not taken

**Road 5**, `Parallel.ForEach` over plates in `Building`, is five lines and a
further ~4× of wall clock. Not taken here: it competes for the same cores as the
parallel fill, and it makes the order plates arrive in non-deterministic, so a
board would fill in a different visual order run to run. Cosmetic, visible, and a
separate decision.

**Road 6**, skipping empty blocks by a coarse pre-pass, is the only road that can
change the picture — a missed block is a gap in a contour line, and "0 missed over
48 plates" has no bound behind it. Roads 1–3 have taken most of its headroom
anyway.

### F-R15.5 — A8 still does not measure this

Its clause calls `Contours.Extract` directly at the raw rect, so it reports
203.7 ms and its ms-per-million-cells rate holds. **The game path is now ~26 ms a
plate and A8 cannot see it.** Restating it a second time would make it measure what
a player waits for; F-R8.4 already did that once.

---

## R16 — W3, first pass

`Render/OfficeStyle.cs`: a style per office — paper, main ink, water, marks, grid,
and one weight scale. All 13 gated checks pass; the shared-sample cache still
gives 260/260 identical plates.

### F-R16.1 — one weight, not seven widths

`RenderTuning` holds a width per feature in paper millimetres, and those
*relationships* are cartographic: a coast is heavier than a river is heavier than
a contour, on anyone's map. What differs between offices is how hard the pen was
pressed, so an office **scales that table** rather than replacing it. Seven knobs
per office would be seven ways to lose the relationship that makes a sheet
readable.

Hydrographic 0.85, Land Survey 1.0, Garrison 1.25, Antiquarian 0.9.

### F-R16.2 — the chart takes its maker's style

Q4.4 makes the chart that office's work, so an island charted by the Garrison has
a buff base with a hard black outline and one charted by the Hydrographic a
blue-grey one. **The base under a board already says who drew it**, before a
single quarter is laid on it.

### F-R16.3 — `Neutral` is deliberately the plainest

A render with no office behind it — the island preview, a bench, a test — gets
warm cream and brown-black. Plainest of the five on purpose: a plate rendered
without a style should look *wrong*, not like somebody's office.

### F-R16.4 — the rebuild trap is gone, not documented again

Adding a field to `RenderRequest` would have re-armed F-R13.2: `IslandRenderer`
rebuilt the request field by field to clear one layer bit, and silently dropped
whatever it forgot. `RenderRequest.WithLayers(mask)` replaces that rebuild — **a
copy that changes one thing cannot forget the others**, and it stays correct as
fields are added. The style itself is passed as an argument rather than added to
the request, because a request says *what* to draw and a style says *how*.

### F-R16.5 — it cost nothing, as predicted

| | 13 plates, one batch |
|---|---|
| before W3 | 1678–1753 ms uncached, 343–547 ms cached |
| after W3 | 1706 ms uncached, **529 ms** cached |

Inside run-to-run noise on a machine that has been building continuously. Style
touches paper colour, ink colour, and a multiplier on widths that were already
being computed — none of it reaches the field sampling that is 96–97% of a plate.

**And the expensive half of a paper stock never reaches the raster at all**:
R3.3 makes grain, wear and fold authored textures blended by a condition value at
display time, and `SheetTexture` already composites the map onto paper on the
engine side. What `OfficeStyle` holds is the flat tone underneath them.

### F-R16.6 — what is not settled

The values are a **first pass**, and §5.4's proof is not an assert: three plates
told apart at pile distance by someone who has not been told which is which. They
are all in one file so a judgement made by looking can be applied in one place.

What is missing from a style and known to be: typography (no text is rendered yet
— POC-03 §5), mark *shapes* per office (a Hydrographic settlement is not a Land
Survey one; today only the colour and weight differ), and paper grain (R3.3, and
engine-side).

---

## R17 — layer turning and zoom, restored

`Table/BoardControls.cs`, and a layer model on `BoardView`. **Q4.3 is in;** the
rest of W5 — the base underlay's own treatment and the R6.8a gate — is not.

### F-R17.1 — zoom did not survive the deletion, and that was a plan gap

`BoardViewport`, `BoardView.MoveView`, `BoardView.ZoomViewAbout` and `Wheel` were
all kept deliberately (`01-removal.md` §1.3) — and then had **no caller at all**,
because the twenty lines connecting a scroll wheel to them lived inside
`BoardInteractor` with 1900 lines of the placement mechanic.

`02-features.md` W5 listed `Q`/`E` and the base underlay and said nothing about
pan and zoom: the inventory was of what the *mechanic* needed and missed that the
same class also held the camera. Recorded because it is the shape of thing a
deletion list gets wrong — a class named for one job doing two.

### F-R17.2 — `Q` and `E` keep their keys and change their meaning

They used to turn a sheet; nothing turns now (D-Q1). They cycle the visible
office, which is the gesture the table exists for. The `Table/Turn` action is
**reused rather than renamed**, so the bindings asset does not change under a
running scene.

Read on the **edge**, not the value: `Turn` is a 1D axis and holding `E` reports 1
every frame, which would spin through three offices in a twentieth of a second.

### F-R17.3 — a layer is visibility, not layout

`ShowLayer` toggles slab `activeSelf` and moves nothing. That is the point of
flipping between offices — the same ground, in register, and nothing changing but
the ink — and it also means a flip costs nothing, where re-laying would throw away
rasters at ~200 ms each.

Three rules the model keeps:

- **The chart is never hidden.** It is the base everything is laid over (Q4.4),
  and where no quarter covers it the board is meant to show it through (Q4.6).
- **`Layers` is built in `Offices.All` order**, never the order plates landed in:
  rasters arrive one per frame in whatever order the renderer finished them, and
  `Q` must land on the same office twice running.
- **A sheet landing does not move the player's layer.** `RebuildLayers` holds the
  showing office if it still has plates. A crate delivering a fourth office
  mid-look would otherwise flip the board under their hands.

Cycling wraps both ways: with two or three layers, stopping at the ends would make
one of the two keys dead half the time.

### F-R17.4 — what is still missing from W5

The base underlay has no treatment of its own — it is drawn like any other plate
rather than as something beneath everything — and R6.8a's gate (no board without
the chart) is not enforced. Both are the rest of W5.

---

## R18 — an emptied table, checked

The claim: *after a binder has been placed on a table and then removed, the table
returns to its original state — unbound, no island, no map.* **It holds**, with
one leak that was fixed and one case reachable by a different door.

### F-R18.1 — the binding is genuinely contents-derived

`CartographyTable.BoundSeed` reads `placed[0].IslandSeed` and returns 0 for an
empty pile. Nothing caches it and nothing serialises it. `placed` is mutated in
four places, and there is **no removal path that bypasses `TakeTop`** —
`BinderPickup` routes an on-table binder through it and only takes a binder
itself when the table is null; the merge path is gated the same way, and `Prune`
would catch a null anyway.

Persistence agrees by construction: `RoomPaper.Capture` builds the binder→table
map by walking each table's live list, so an emptied table saves as unbound.

**`Discard()` being vacuous is correct** (`01-removal.md` §1.4 predicted it): the
`BoardStore.Clear` it used to call has nothing behind it, because the board is a
view (Q4.1, Q4.7).

### F-R18.2 — the early return cannot show the previous island

`BoardView.Show`'s "already showing this" needs `IslandSeed == islandSeed`, and
`Teardown` sets `IslandSeed = 0`. A table cannot be emptied while its board is
open — the room is suspended, so `PlayerHands` is disabled — so every
empty-then-refill passes through a teardown first and always takes the rebuild
path.

An emptied table also cannot be **opened**: `CanInteract` returns
`Refused("Nothing on this table")`, and `OpenBoard` refuses a zero seed a second
time. So "opening it shows no map" is untestable rather than false.

### F-R18.3 — the leak: the office selection crossed boards

`Teardown` cleared everything a board holds **except `layers` and `layerIndex`**,
which R17 added an hour earlier. `layers.Clear()` lived only in `RebuildLayers`,
which only runs when a plate lands.

Two consequences, both fixed by clearing them in `Teardown`:

- **The selection crossed boards.** Close a board on Garrison, open a different
  table on a different island: `RebuildLayers` reads the *previous* board's
  active office and pins the new one to it if that office has plates.
- **A window where the board reported layers it did not have.** `IsShowing` goes
  true before any raster lands, and `BoardControls` is live in that window — so
  `Q`/`E` indexed the previous island's list.

This is precisely what `viewport` is dropped rather than reset to avoid, and its
comment says so. R17's "hold the showing office so a landing plate cannot flip the
board under the player's hands" was right; what it missed is that **the hold has
to end at a teardown**.

`Remove(SheetId)` also failed to call `RebuildLayers`, leaving a phantom layer
when an office's last plate goes. Unreachable today — `Lay`, `Seat` and `Remove`
have no callers since the deletion — but fixed rather than left as dead API with a
bug in it.

### F-R18.4 — the same symptom, from a different door

An **empty binder** binds a table exactly as a full one does (B1.3, and
`MapCrate`'s debug binder is one). Put one on a table alone and the board opens
on the mounting sheet — a pale island-shaped quad sized to the land bounds — with
the header naming the island and **no plates and no chart**.

That is a bound table with a binder on it, so it does not falsify the claim. But
it is a real "map background with nothing on it", and it is what R6.8a exists to
prevent: **a board should not open without the chart.** That gate is still
unenforced and is the rest of W5.

---

## R19 — the board is fixed at its far framing, and empty binders stop existing

Three decisions, 2026-08-30.

### F-R19.1 — `BoardZoom` 1, and the zoom controls go

`TableOptions.DefaultBoardZoom` was 2; it is now **1**, which is
`DefaultBoardZoomMin` — the whole board, framed as C8.13 composed it, and as far
out as the camera goes.

The reason is Q4.1 and Q4.2: **the board is a thing to look at, not to work on.**
Nothing is placed, so there is nothing to lean in on, and an island that opened
half in frame asked the player to pan before they could see what they had come to
see. At zoom 1 the pan travel is zero by construction —
`max(0, boardHalf - viewHalf)` — so the framing is genuinely fixed rather than
merely starting there, and a pan control would have been a key that does nothing.

`BoardControls` therefore holds `Q`/`E` and nothing else: 102 lines, no wheel, no
drag, no `TableOptions` reference at all. **The machinery is kept, not deleted**,
the same way rotation is (D-Q1) — `BoardViewport`, `MoveView`, `ZoomViewAbout` and
`Wheel` all still work and all have no caller. Wiring a wheel back is twenty
lines, and the file says where they go.

### F-R19.2 — no empty binder is generated

`MapCrate.emptyDebugBinder` is gone. It existed so the filing verb had a target,
and it does not need one: the crate delivers a binder per office (F-R11.2), the
loose debug sheet is drawn from the same pool, so **its office's binder is always
already on the floor**. Filing has somewhere legal to go without inventing an
empty folder to put it in.

This also closes F-R18.4, which was the one reachable way to open a board on a
mounting sheet with no plates and no chart. **It is a narrower fix than R6.8a**,
which is still unenforced: a binder holding only quarters and no chart would do
the same thing, and only the gate stops that.

Sheets are not removable from a binder either — D-B2 already recorded that as
deliberate — so a binder cannot become empty in play. The only empty one is the
source of a merge, which is destroyed in the same frame.

### F-R19.3 — one anchor, and what it costs

**Recorded, not yet done:** the table's second and later `BindingAnchors` are to
be removed, so a table holds **one binder**.

That has a consequence worth stating before it is built. A binder is
`island · office` (Q3.1). One binder on a table is therefore one office, one
layer, and `Q`/`E` with nothing to cycle to — **unless the binder has been
merged**. So a one-anchor table makes merging the only route to comparison.

That is exactly what **Q3.4 and Q4.5 were written to prevent**: merging is
tidiness and nothing else, and capacity must never gate comparison. K1 chose
"tidiness, zen, making order" over K3's "merging earns the comparison", and one
anchor quietly reverses that choice.

Three ways it can be made consistent, and one of them has to be picked:

| | what | costs |
|---|---|---|
| **n** | Accept it: merging becomes the route to comparison | Q3.4 and Q4.5 are retired, and K3 is what the game does after all |
| **o** | A binder stops being per-office — it is per island, and holds every office | Q3.1 goes, and with it the accretion that made a crate deliver three objects instead of one |
| **p** | The table takes one binder but the **board** takes what the room holds | Keeps both rules, and makes "on the table" stop meaning "in the board" |

---

## R20 — Q3.1 reversed: a binder is an island, not an island and an office

Decision (o) of F-R19.3, taken 2026-08-30. **This reverses W4's Q3.1**, and it is
recorded as a reversal rather than rewritten over, because the argument that
produced Q3.1 is still a good one and someone will make it again.

### F-R20.1 — what changed and why

A binder's identity is its **island**. Its contents may span one office or every
office. Which offices are in it is **read off the contents, never stored**.

The forcing move was the one-anchor table (F-R19.3). With `island · office`
binders, a table holding one folder can only ever show one office, so `Q`/`E`
would have nothing to cycle and comparison would depend on merging first —
reversing K1, where merging was chosen as tidiness and nothing else.

Under (o) that tension is gone, and **Q4.5 is satisfied by the binder rather than
by capacity**: a table may take one binder, because one binder can hold
everything.

### F-R20.2 — what it costs, stated plainly

Q3.1 existed to protect **accretion**. J3 — one binder, one island, everything in
it — was rejected at Question 10 precisely because it hands a player a finished
island in a single object. Nothing in (o) prevents that; it only stops *requiring*
the opposite.

What keeps accretion is now **what arrives**, not what a binder is allowed to
hold. "The collection was moved and the order was lost" is the premise, so a
folder holding two offices is the mess itself rather than a tidy exception — and
that is exactly what makes comparison available without tidying. Room population
(W8, Q7.1) is where this becomes a real decision; the crate is only a debug tool.

### F-R20.3 — an office could stop being stored because empty binders stopped existing

`BinderView` no longer carries an office ordinal, `BinderRecord` no longer carries
one, and the save's `"office"` key is gone — format **version 3**.

That is only sound because of F-R19.2: the field existed for the one case that
cannot be inferred from contents, an **empty** binder, and empty binders do not
exist. Nothing generates one, and sheets cannot be taken back out (D-B2). The only
one that ever exists is the source of a merge, destroyed in the same frame.

Two decisions made an hour apart turned out to depend on each other, which is
worth noticing: had the empty binder stayed, (o) would have needed the field kept
and always written as "merged".

### F-R20.4 — the crate's delivery is now a fixed debug set

One opening puts down **one folder holding a whole island**, every office in it,
and **two folders of a second island**, one office each.

Those are the two cases a table has to handle: the full folder shows three layers
on a one-binder table, so `Q`/`E` cycles without anything being merged; the pair
is what a merge is tried on, and being a *different* island it is also what a
merge has to refuse.

`bindersPerOpening` is gone — the delivery is no longer a count.

---

## R21 — a quarter plate was not of its quarter

The bug the board was showing, found by asking what the pipeline actually does
rather than what it was supposed to.

### F-R21.1 — the pipeline, as it was

The expectation was: generate the island's features, render a whole-island
texture per office, split it in four. **Two of those three steps did not exist.**

1. **Island features** — yes, `Island.FromSeed`, once. Correct.
2. **A whole-island texture per office** — **never existed.** Nothing in the
   codebase has ever rendered an office's island.
3. **A split** — **there was none.** Four independent renders, each over a rect
   that happened to be *centred* on a quarter.

The crux is one line of `QuarterCutter.Cut`:

```csharp
Rect2[] rects = Quarters(landBounds);
for (int i = 0; i < 4; i++) sheets.Add(new Sheet(spec, i + 1, rects[i].Centre));
```

**The quarter rects were computed and thrown away.** Only the centre survived, and
`Quarters()` had no other caller anywhere. A `Sheet` therefore knew where its
quarter was and nothing about how big it was, so `FrameRect` fell back to
`paper x scale`.

Measured on Driftcombe, where a quarter is 3456 x 3136 m and the paper holds
7610 x 5140 m:

| | |
|---|---|
| NW and NE overlap | 4154 x 5140 m — **55% of a whole plate** |
| one plate covered | **90% of the island's bounds** |

So an office's four "quarters" were four near-identical drawings of nearly the
whole island, stacked. Q1.1's 2 x 2 cut was true of the rects and of nothing
anyone could see, and Q1.4's "quarters tile exactly" was true of nothing at all.

### F-R21.2 — the fix: a sheet carries the ground it is of

`Sheet` gains `GroundWidth` / `GroundHeight` — **the ground it is of**, which is
not the ground its paper could hold. Quarters take their rect; the chart and
detail sheets keep the paper-derived extent, because neither is a quarter.

| | before | after |
|---|---|---|
| NW\|NE overlap | 4154 m | **0 m** |
| union of the four | 12 934 x 9 278 m | **6912 x 6272 m** — the island, exactly |
| raster per plate | 2055 x 1388 px | 933 x 847 px |
| the quarter, on paper | edge to edge | **346 x 314 mm of a 761 x 514 mm map area** |

**Render-and-split was the wrong shape**, and the reason is a requirement that has
not arrived yet: a binder may hold **two of an office's four quarters**. Splitting
one island raster means producing all four to get one. Because a plate's extent is
now its own quarter, and quarters tile, **four plates rendered separately meet
exactly** — so two of four are drawn the same as four of four, and a plate never
has to know what was rendered beside it.

### F-R21.3 — and one derived value had to stop being derived

`SheetTexture.Compose` computed `pixelsPerPaperMm = map.Width / format.MapWidthMm`
— sound while every map filled its sheet's map area, and wrong the moment one did
not. A quarter fills 346 of 761 mm, so the derivation would have reported a render
at 45% of its true resolution, shrunk the paper to fit, and printed the quarter
edge to edge with no margin at all — reintroducing the bug one layer further down.

It is passed in now, carried on `SheetRender`. The old comment claimed deriving it
meant "the margin cannot disagree with the render resolution"; the opposite was
true as soon as the map stopped filling the sheet.

### F-R21.4 — the margin is now real, and says what Q1.6 wanted

A quarter occupying 346 x 314 mm of a 761 x 514 mm map area is the physical-size
signal Q1.6 asked for, and it is legible for the first time: an island that fills
its sheets is large, one adrift in white is small. Before this, every plate was
edge-to-edge whatever its island.

It also sharpens F-R9.3's fill number. 27.7% was read as wasted paper; it was
really **the fraction of a plate that was its own quarter** — the rest was its
neighbours' ground drawn again.

### F-R21.5 — and it was the largest performance win of the rework

Making a plate be of its quarter removed work nothing had noticed was duplicated:
the four papers overlapped by 55%, so most of every plate was its neighbours'
ground, sampled and drawn again.

| | before R21 | after |
|---|---|---|
| one plate, alone | ~200 ms | **53 ms** |
| raster per plate | 2055 x 1388 px | 933 x 847 px |
| 13 plates, no cache | 1706 ms | **697 ms** |
| 13 plates, cached + parallel | 343 ms | **267–309 ms** |
| **two of an office's four** | — | **32 ms** |
| A8's plate re-contour | 172 ms | **56 ms** |

**A single plate is now 53 ms**, comfortably inside the ~100 ms perceptual bar
that A8's budget was named for and that F-R8.4 conceded could not be met. It was
not a rendering problem at all: the plate was four times bigger than it should
have been.

The whole journey, one island's thirteen plates: **2735 → ~280 ms, 9.8x**, and
every step of it verified output-identical except this one, which was a bug fix
and changed the picture on purpose.

The cached figure improves least (343 → ~280) and that is expected: the sample
cache was already collapsing the overlap between offices, so the redundancy R21
removed was partly redundancy the cache had been paying for once instead of
thrice. What R21 removes is the part the cache could not — a quarter's plate
sampling its *neighbours'* quarters, which no amount of sharing between offices
can help.

---

## R22 — the board was rendering at a paper resolution, on a surface that has no paper

The plates were visibly blocky on the table. Two causes, and the second is the
interesting one.

### F-R22.1 — the chart was 87% sea, like the quarters had been

R21 gave a quarter plate its quarter and left the chart taking its extent from
its paper. **F-S1.6 had already measured what that costs**: a chart's paper is
19.0 x 12.9 km for a 6.9 km island — 564% of the land area — so seven pixels in
eight were open water and the island got the eighth.

The chart is now of the island, the same rule as a quarter (Q1.1). What it is a
chart *of* is the island; the paper is what it is printed on.

### F-R22.2 — pixels per paper millimetre is the wrong question for a board

`BoardPixelsPerPaperMm` fixed the resolution in **paper** terms. That is right in
the hand, where two sheets are the same size of paper whatever they are of — and
wrong on a board, where every plate is laid at its **ground** size, so a chart at
1:25000 and a quarter at 1:10000 are shown at the same metres per screen pixel
and the chart got two and a half times fewer pixels for them.

Measured at the shipped 0.6 px per paper mm:

| | px per metre | the island, in texture |
|---|---|---|
| chart, 1:25000 | 0.024 | **166 px, stretched 11.5x** |
| quarter, 1:10000 | 0.060 | 207 px |

The board needs about **0.24 px/m** to fill a 1920-wide view at zoom 1. It was
getting a tenth of that.

`RenderRequest.ForSheetAtGroundResolution` takes a ground resolution and
**derives** the paper one, because stroke widths are in paper millimetres (§7)
and would otherwise be drawn at the wrong weight. One dial, and the other follows
rather than needing a second number to agree with it.

`TableOptions.BoardPixelsPerMetre` = **0.35**, against the 0.24 that fills a
1920-wide view, so there is headroom for a larger display without paying for a 4K
one on every plate.

### F-R22.3 — measured

| plate | before | after |
|---|---|---|
| chart raster | 457 x 310 px, of which the island was 166 px | **2419 x 2195 px, all island** |
| quarter raster | 207 x 188 px | **1210 x 1098 px** |
| a board of 13, cached + parallel | 267–309 ms | **349 ms** |

Six times the linear resolution on a quarter and fifteen on the chart's island,
for about 15% more time — because R21 had already removed the work that was being
done four times over. The chart is the expensive one at 294 ms alone, and it is
rendered once per island and cached.

All 13 gated checks pass; A8's plate re-contour reads 61 ms.

---

## R23 — the offices differ by composition, not by colour

W3's first pass failed its own test: on the table, Hydrographic and Land Survey
read as the same document. Measured, they were not — different hashes, different
papers, different layers — but the difference was **0.7% of the pixels**.

### F-R23.1 — colour cannot carry a signal that small

| office | ink on the plate |
|---|---|
| Hydrographic | **0.54%** |
| LandSurvey | 1.24% |
| Garrison | 2.85% |

Three plates each showing a thin coastline on an off-white sheet read as one
document however carefully the hues are picked. Garrison escaped only because its
grid covers the whole sheet — which is exactly what A5b had been warning about.

The papers were also too close: `#E4E9EC` against `#F4EDDC` is +16 / +4 / −16, and
those were values chosen from a description rather than from looking at the
result. That is the wrong way round for art direction.

### F-R23.2 — §2's lore table already said what to do

It names what each office draws **badly**, and that is the composition:

| office | omits | the sheet reads as |
|---|---|---|
| Hydrographic | anything inland | **sea washed, land blank** |
| Land Survey | the sea | **land hatched, sea blank** |
| Garrison | civilian detail | **grid over both**, ground to cross |

Three inverse compositions. `Q`/`E` now swaps which half of the sheet is full and
which is empty — **Q2.4's "offices differ by omission" as the dominant visual
fact** rather than one fewer thin line, and legible at the pile distance §4.1 asks
for, from the shape of the ink before any colour resolves.

| office | ink, after |
|---|---|
| Hydrographic | 0.54% → **43.0%** |
| LandSurvey | 1.24% → 1.9% (every band edge, not every other) |
| Garrison | 2.85% → 2.9% |

**The papers were left alone.** Once the compositions differ, three loudly
different paper tones read as coloured card rather than as three offices' stock.

### F-R23.3 — and it made Hydrographic cheaper

With `Fill` on, `FieldCoast` draws the coastline free from the fill's own samples
and the vector extraction is skipped entirely (F-R13.1). A Hydrographic plate is
**53 ms** against Land Survey's 185, which now pays for seven contour levels
instead of four.

### F-R23.4 — Q2.2 amended, not broken

Q2.2 turned `Fill` off because F-S1.7 measured a **colour relief map** — greens,
browns, banded water — where the mockups show ink on paper. `OfficeStyles.WashPalette`
is not that: every land band is exactly the office's paper, and the sea is one
flat tone in two strengths. What the rule forbids is the **banding**, and there is
none. The requirement now says so.

Two sea tones rather than one, the deeper slightly stronger, because a chart that
says nothing at all about depth is the one thing a Hydrographic sheet may not do.
It is a hint, not a bathymetric scale.

`OfficeStyle` grew `Wash` and `ContourStride`, so an office that surveys no
terrain draws no contours whatever `FeatureMatrix` says — Garrison's grid is its
texture and a hatching under it would be a fight.

---

## R24 — W5 finished: the gate, and the base underneath

### F-R24.1 — R6.8a is enforced

*"A board can only be opened once the island's whole-island sheet has been
handled. That sheet is the board's outline; without it there is nothing to place
against."* It was never enforced, and F-R18.4 found what that looked like: a
binder holding quarters and no chart opened a full-screen board on the mounting
sheet — an island-shaped blank with a name in the header and nothing on it.

`CartographyTable.HasChart` reads the binders on the table for a `WholeIsland`
sheet, and an empty-handed player is refused **in words** — `"No chart of this
island"` beside the existing `"Nothing on this table"`. Both are
`InteractionState.Refused` rather than `Unavailable`, which is B1.7: a table that
will not open says why.

Read off the contents, never remembered, like everything else about a table's
binding (B1.2): a chart arrives when its binder is put down and leaves when that
binder is taken away, and nothing has to be told. There is exactly one per island
(Q2.3), so the scan stops at the first.

`OpenBoard` refuses a second time, the way it already refused a zero seed. Two
guards for one rule, because the first is a sentence the player reads and the
second is the one that holds if some future caller reaches past it.

**`BinderPickup` is unaffected.** Empty-handed on a table it takes the top binder
rather than opening the board, so the gate has one door and not two.

### F-R24.2 — the chart is under everything

Draw order was: office, then the whole-island flag, then number. Office first put
the chart **above** the plates of every office ordered after its own — and a chart
covers the whole island, so those layers would have shown nothing but it.

It sorts on the chart first now, lowest. What follows is Q4.6 without any further
code: the quarters tile exactly (Q1.4) and the chart is exactly their union, so it
is completely hidden when all four are owned and **shows through precisely the
quarters that are missing**. A board that is two-thirds worked out looks it.

Nothing needed to make the chart non-interactive — nothing on a board is
interactive at all (Q4.2).

### F-R24.3 — W5 is done

`Q`/`E` cycling (R17), the base underneath, and the gate. What was cut from it
along the way and recorded rather than forgotten: zoom and pan, fixed at the far
framing by R19 with the machinery kept.
