# Generation Pipeline — As Built

Reference for agents working on this codebase. Describes what the code **does**,
not what the spec intends. Authority order: `poc-01-island-and-surveys.md`
(intent) → `poc-01-decisions.md` (D1–D5) → `poc-01-findings.md` (F1–F8, measured)
→ this file (as built). POC-03 adds `poc03/requirements.md` (intent) and
`poc03/spec.md` (construction) at the same level as POC-01's pair.

Assembly `Archivist.Generation`, `Assets/Archivist/Generation/`. **It must never
reference UnityEngine** (§14) — that is what lets the whole §13 suite run
headless via `Tools/run-acceptance.sh`.

---

## 1. Entry point

```csharp
Island.Generate(ulong collectionSeed, int islandIndex, IslandCharacter? forced = null)
Island.FromSeed(ulong islandSeed,                      IslandCharacter? forced = null)
// island_seed = Hash.Mix(collectionSeed, Fnv1a64(islandIndex))   (R1.1)
```

One seed → one island, completely. **Only the seed is persisted**; nothing
geometric is ever cached or stored (R1.11, R3.1). `Island` exposes `Params`,
`Field`, `LandBounds`, `Coastline`, `Features`, `Names`, `Service`, `Surveys`,
`SurveyFor(Office)`, `WholeIslandSurvey`, `TotalSheets`.

⚠ **Cost per island is disputed and nobody should quote a number here yet.**

This file recorded **~118 ms** (A8 median, inside the 250 ms budget), and
dismissed a ~455 ms reading as a loaded machine, citing two clean runs at 117.3
and 118.3 ms. That does not reproduce. An idle machine now measures **~467 ms**,
and — this is the part that rules out the obvious explanation — a snapshot of the
tree taken *before* the POC-03-era refactoring measures **469.5 ms** on the same
machine in the same session. So the 4× gap is not something recent work
introduced.

Three possibilities remain: the ~118 ms was measured on very different hardware;
it predates something that made generation four times more expensive; or it was
wrong. Until someone instruments `Island.FromSeed` per stage and finds out, treat
both figures as unverified. A8's island-generation clause currently **fails**.

**Re-measured when tuning became configurable, and the slow figure held.** Eight
`fast` runs on the same idle machine, medians of ten islands each:

| `Tuning` shape | A8 island generation |
|---|---|
| `const` (as it was) | 478.8, 492.6 ms |
| `static readonly`, read from `config/generation.yml` once and frozen | 472.7, 481.5, 479.3 ms |
| settable properties (`#if UNITY_EDITOR`, so the editor can tune live) | 565.8, 534.1 ms |

Two things follow. **Making the constants configurable costs nothing**, provided
they end up frozen — the JIT folds a `static readonly` primitive into a literal
exactly as it folds a `const`, and the two measure the same. Leaving them
writable costs ~13%, which is why the player build freezes them and only the
editor pays. The determinism hash was `2933DCFC3DB132D5` under all three shapes,
so no shape generates a different island.

**And a clue for whoever finally instruments this.** Setting
`paper.OverlapFraction` to 0 drops a collection from 590 sheets to 444 — a 25%
cut in everything `SurveyCutter` does — and moves the median only from 479.3 ms
to 461.7 ms, under 4%. **Whatever the ~467 ms is, it is not sheet cutting.** It
is upstream of it: the field, the contour extraction, or the feature passes. That
is where to put the first stopwatch.

A8's *sheet re-contour* clause now **passes** at ~47 ms — that was F4, resolved
for the paper path by capping `LodForScale`; see §5 and §10.

---

## 2. Determinism — the rules you must not break

- **Forbidden:** `System.Random`, `UnityEngine.Random`, `string.GetHashCode`,
  wall-clock, and any dictionary/set **iteration order** driving generation.
  Enforced by `Tools/check-sources.sh`.
- **Randomness is only ever** `Streams.For(seed, purpose, index)` → `Pcg32`.
  One stream per purpose, drawn independently, so adding a purpose or reordering
  a loop cannot reshuffle an existing feature. Asserted by A2.
- **Every selection stage sorts by an explicit total order before selecting.**
  Peaks `(elev desc, x asc, y asc)`; settlements `(score desc, x asc, y asc)`;
  POIs `(kind index asc, x asc, y asc)`; contours by first vertex. Never rely on
  natural iteration order.
- **Quantisation (D3).** `Height01` is quantised at `2^-16` and *every* threshold
  compares that value — marching-squares corner signs, saddle rule,
  `landFraction`, `Elevation < -4`, relief. A tie at exactly `SeaLevel` is
  **land**. `Gradient` is the one exemption (unquantised; callers round
  `|Gradient|` with `Q.Grad` before comparing). Angles quantise to 0.1° via
  `Q.Deg`. Helpers live in `Determinism/Q.cs`.

Named streams currently in use — do not repurpose:
`character`, `radius`, `field`, `falloff`, `settlements`, `rivers`[peakIndex],
`names`, `names.island`, `names.settlements`[i], `names.peaks`[i],
`wholeIsland`, `year`[office], `yearWholeIsland`[office],
`poi`, `poi.kind`, `poi.sheet`[poiIndex], `coastRegion`.

**They are constants now — `Determinism/StreamNames.cs`.** Call `Streams.For(seed,
StreamNames.Poi)`, never `Streams.For(seed, "poi")`. The purpose string is seed
material (`Streams.For` hashes it), so a typo in a literal silently regenerates
every island and *nothing fails* — no test catches it, the archive just quietly
becomes a different one. Routing every call site through one constant is what
makes that typo a compile error instead. The C# constant NAME may be renamed
freely; the string literal beside it may never change. Append only.

The dotted prefixes (`names.island`, `poi.kind`) are a reader convention only —
nothing in the code treats them as a hierarchy, and `names` and `names.island`
are as unrelated as `radius` and `falloff`.

The three literals still spelled out in the suite — `"some.future.purpose"`,
`"unrelated.purpose"`, `"test"` — are deliberately NOT in `StreamNames`. They
exist to prove that an *unregistered* stream leaves the island bit-identical
(§4.3), so naming them would defeat the test.

`coastRegion` seeds the Hydrographic coast-walk survey region — the anchor point
on the main shore and the disc radius around it (`CoastRegionRadiusMin/Max` in
`Tuning.cs`, and the comment above them explaining why the survey is a region
rather than an arc per loop).

⚠ `year`[office] and `yearWholeIsland`[office] are indexed by `(int)Office`, and
`wholeIsland` draws `Range(0, 3)` — **not** `Offices.Count`. Renumbering the
`Office` enum, or widening that draw, rewrites existing islands. Append only.

**All constants live in `Tuning.cs`.** Do not introduce magic numbers elsewhere.

---

## 3. Pipeline order

Order is load-bearing — later stages read earlier ones.

```
1  IslandParams.FromSeed         character, NominalRadius (0.38 * 16 km, ±8% jitter)
2  new IslandField(params)       the analytic height field
3  field.ComputeLandBounds()     AABB of land on the 64 m lattice; may be Rect2.Empty
4  Contours.Extract(domain, lod 1 = 32 m cell, SeaLevel)      -> Coastline
5  Peaks.Generate                                              discrete
6  Settlements.Generate          (needs Coastline)             discrete
7  Rivers.Generate               (needs Peaks, in peak order)  discrete
8  PoiSiting.Generate            (needs Peaks + Settlements)   discrete
9  NameGenerator.Generate        then attached positionally via WithName
10 new ServiceRule(...)          (needs all discrete features, POIs included)
11 CutSurveys                    whole-island first, then Hydro -> Land -> Garrison -> Antiquarian
```

Step 11's office order matters: Land Survey's degenerate rotation falls back to
`θ_hydro + 90°`, so Hydrographic must be derived first.

Step 8 sits where it does because POIs **read** peaks and settlements
(`RuinedTower` needs a peak, `RuinedChapel` needs a settlement) and **nothing
reads POIs**. It is before naming because POIs are unnamed (POC-03 §5 keeps text
out of scope). Its only PRNG draws come from the new `poi` / `poi.kind` streams,
so §4.3 guarantees every feature above it is bit-identical to what it was before
POIs existed — verified by reproducing A2's pre-POC-03 hash `750786CEDCB93665`
with the Antiquarian survey excluded.

---

## 4. The field — `IHeightField`

**The island is `f(x,y)`, never a grid.** Contouring is a *query*, so a sheet
re-contours its own rect at its own detail against the same function.

```
1  p   = (x, y) / 2600 m
2  w   = p + 0.45 * (fbm(p+o1), fbm(p+o2))
3  n   = fbm(w)                          -> [0,1]
4  r   = |(x,y)| / NominalRadius
5  f   = Falloff(character, r, atan2(y,x))
6  h01 = saturate(n * f * gainC + biasC)
7  h01 = Q.H01(h01)                      <- D3; everything downstream compares this
```

`fbm` is our own 5-octave gradient noise (lacunarity 2, gain 0.5, 256-entry
table, quintic fade, no transcendentals in the inner loop). Three falloff
recipes, not a jittered parameter:

| character | falloff | max elev | notes |
|---|---|---|---|
| Mountainous | `1 - smoothstep(0.35, 1.00, r)` | 620 m | compact, one massif |
| Fjorded | `cut = 0.18*fbm1(θ*6)`; `1 - smoothstep(0.30, 1.00, r+cut)` | 540 m | **seam at θ=±π, F6a** |
| Atoll | `1 - smoothstep(0.00, 0.14, abs(r-0.62))` | 90 m | yields TWO loops (shore + lagoon) |

`Elevation` derives from the quantised `h01`. `Gradient` is m/m by central
difference at 20 m against the **unquantised** path.

---

## 5. Contours — and the lattice rule

```csharp
Contours.Extract(IHeightField, Rect2 area, double cellSize, double level01)
Contours.CellSizeForLod(int lod)      // Tuning.BaseCell / 2^lod, BaseCell = 64 m
Contours.LodForScale(int denominator) // target = 0.25/1000 * denom metres;
                                      // lod = clamp(ceil(log2(BaseCell/target)), 0, 8)
```

**§6.2, the single most important invariant here:** `cellSize` is always
`64 / 2^lod`, and grids always land on multiples of `cellSize` **measured from
the domain origin (0,0)** (`Rect2.SnapOut`). Two rects at the same LOD therefore
sample *identical points* along a shared border and cannot tear. Break this and
the map table breaks later. Asserted by A3 to 1e-6·cellSize — currently exact.

Also lattice-locked to (0,0) for the same reason: **soundings** (400 m) and the
**Garrison grid**.

Saddle cases (codes 5/10) always resolve by the sign of the cell-centre sample.
Polylines are welded at `cellSize * 1e-6` and returned in a stable sorted order.
Segments are wound so **land is on the left** (closed land loops CCW).

⚠ `LodForScale(25000)` returns **4**, not the 3 in §6.2's table — the table row
contradicts its own formula (F6b). The formula is authoritative.

⚠ **Cost scales with sheet AREA, not coastline length.** Blanket sampling is what
`Extract` does: every corner of every cell in the rect, whether or not the
contour goes near it. Measured on a 1:2500 sheet, **0.148%** of cells straddled
the coastline — 3 828 of 2.58 M. The rest was discarded.

**`LodForScale` is now capped at `Tuning.MaxPaperContourLod` (4, cell 4 m)**, and
that is what made A8's re-contour clause reachable: 2 903 ms → 47 ms. The paper
rule alone (`PaperDetailMm × denominator`) assumes the coast has structure at
every scale, and it does not — the field is a 5-octave fbm on a 2600 m
`FeatureScale`, so its finest real wavelength is ~160 m. Accuracy plateaus at
lod 3; lod 7 costs 256× lod 3 to move the line 0.03 m, well under what
`PaperDetailMm` can print. Lod 4 is chosen over lod 3 for line *smoothness*
(384 vertices per sheet against 194), not accuracy.

⚠ **The cap is the PAPER path only** — Editor panes, SVG export, A3 and A8.
`RenderLod.ForPixelsPerMetre` deliberately ties the cell to the PIXEL instead,
and **must not be capped the same way**: the fill computes its water edge per
pixel from the analytic field, so a coarser contour makes the stroke float off
the water it edges (see the warning on `Strokes.DrawCoast`). There the fine LOD
buys agreement between two renderings, not coastline detail.

So there are four LOD mechanisms, and they are not interchangeable:

| where | how the LOD is chosen | note |
|---|---|---|
| generation, `Island.FromSeed` | hardcoded lod 1 (32 m) over the whole domain | the island-scale anchor; never calls `LodForScale`, which is why A2 is immune to all of this |
| raster render, `Strokes.DrawCoast` | `RenderLod.ForPixelsPerMetre` — from the pixel | picks the *nearest* power-of-two cell (√2 midpoint), not the first finer one |
| Editor panes / SVG | `LodForScale` — from the paper scale, now capped | the debug window additionally degrades it via `ContourCache.ChooseLod`'s 500 k sample budget |
| A3, A8 | `LodForScale`, no further cap | 

---

## 6. Discrete features

Generated **once per island** with stable `FeatureId`s, because a per-sheet pass
would put the same village in two places on two overlapping sheets.

- **Peaks** — local maxima on the 64 m lattice, ≥ 0.35·MaxElevation, NMS at
  400 m, capped 9/7/2 by character. Top 3 named.
- **Settlements** — candidates within 300 m of coast or with `|Gradient| < 0.04`;
  `score = 0.6*shelter + 0.4*flatness`; greedy at 1200 m spacing; count 4–7/5–9/1–3.
  `shelter = clamp01(27/4 · land² · sea)` over a 600 m disc — peaks at land = ⅔ of
  the neighbourhood (a cove), 0 inland and 0 at open sea. *This formula was
  invented during implementation; the spec never defined it.*
- **Rivers** — descend from each peak, 40 m steps, ±0.15 rad jitter, merge within
  60 m, discard under 800 m. Atolls produce none, by construction.
- **POIs** — 12 kinds in one `PoiKind` enum, two families (`IsRuin` splits them).
  `PoiKinds.Count` is 12; `PoiKinds.IndexRange` is 13 and is **not** the kind
  count — it is one past the highest enum *value*, the size an array indexed by
  `(int)kind` needs, wider because value 1 (`Stack`) is a permanent gap (P5).
  Candidates on the 128 m lattice, one entry per kind whose predicate the point
  passes; sorted `(kind asc, x asc, y asc)`; greedy at 800 m spacing; count
  3–7 / 3–8 / 0–3 by character from `Streams.For(seed, "poi")`. Predicates are
  derived from ground the generator already has — elevation, quantised slope,
  exact distance to the coastline within a 128 m band, the §7.2 shelter measure,
  and the peak/settlement lists. A kind whose predicate is unsatisfiable on an
  island simply does not occur there.
- **Soundings** — field-derived, 400 m global lattice, kept where `Elevation < -4 m`.

⚠ **POI selection is kind-major, not a flat greedy pass** — a deliberate
deviation from POC-03 §1.3, documented in full on `PoiSiting`. With
`(kind index asc, …)` as the primary key, a flat pass lets `SeaArch` (index 0)
eat the whole 3–8 cap and every island ships nothing but sea arches. `Select`
takes at most one POI per kind per round instead, and the round order is rolled
per island from `poi.kind` — the second stream §1.3 explicitly permits — because
bare enum order lists all 6 oddities before all 6 ruins and starves the ruins
(measured 186 oddities / 5 ruins over 50 seeds; now 111 / 80).

`shelter` now lives in `ShelterMeasure.FromLandFraction` so `Settlements` and
`PoiSiting` share one definition of the formula neither spec defined.

---

## 7. ServiceRule (D1)

```csharp
service.Served(V2 p, Office office)
```

`Serving(office) = FeatureMatrix.Drawn(office) \ { Coast }` — the coastline is
island-scale, so it can never be what makes a sheet worth cutting.

| office | serving set | presence within `u` means |
|---|---|---|
| Hydrographic | Settlement, Sounding | a sample with `Elevation < -4 m` |
| Land Survey | Contour, Peak, River, Settlement | relief ≥ 50 m, or a discrete feature |
| Garrison | Peak, Grid | grid is always true |
| Antiquarian | Contour, Peak, River, Settlement, Poi | a discrete feature, or relief ≥ 50 m |

`u = NominalRadius / 4` (~1520 m). Implemented as per-class bitmasks on the 64 m
lattice (disc stamps for discrete, separable max/min filters for relief), built
once in the constructor over `landBounds.Expanded(u)`.

Net effect: Hydrographic is served by soundings, Garrison by its own grid, and
**Land Survey is the only office the lattice cull actually culls.**

⚠ Because of that exclusion, `ServedClass(Coast)` is **always false**. Remember
it before writing any rule that wants "is there a coast here" — this machinery
cannot answer that question. See P3 in §10.

**POC-03 reuses this rule for a second purpose** rather than growing a second
one. `ServedByAny(p, classes)` is the loop `Served` was; `Served` is it over
`FeatureMatrix.Serving(office)`, and the detail sheet's **placeability floor** is
it over `FeatureMatrix.Placeability(office)` — the same set with the POI's own
class removed:

```csharp
served = service.ServedByAny(poi.Position, FeatureMatrix.Placeability(office));
// Placeability(Antiquarian) = Serving(Antiquarian) \ { Poi }
//                           = Contour, Peak, River, Settlement
```

Tightening the floor (POC-03 open question 2) is a change to that one array and
nothing else.

---

## 8. Surveys and sheets

**Five surveys per island:** whole-island + one per office. `Offices.All` is the
canonical list and `Offices.Count` sizes every per-office array — never an inline
`new[] { Hydrographic, LandSurvey, Garrison }`.

**Scale and paper are per office** (F1) — nothing in R2.2 ties surveys to a
shared scale:

| survey | paper | scale | ground per sheet |
|---|---|---|---|
| Hydrographic | coastal strip 380×200, 15 mm margin | 1:2500 | 875 × 425 m |
| Land Survey, Garrison | A1 594×841, 40 mm margin | 1:2500 | 1285 × 1902 m |
| Antiquarian | detail sheet 250×250, 15 mm margin | 1:1250 | 275 × 275 m |
| whole-island | A1 | 1:25000, or 1:50000 if the bbox overruns (D5) | 12850 × 19025 m |

Overlap 20% for the lattice offices; the detail sheets do not tile, so overlap is
meaningless for them. Garrison grid pitch is **40 mm on paper** at any scale
(1000 m / 200 m / 100 m). R2.3 allows three or four fixed scales; the live set is
exactly four — **1250 / 2500 / 25000 / 50000**.

⚠ `Tuning.PoiScaleDenominator` (1:1250) is a **sweep default, not a finding**.
POC-03 §2.1 gives 1:1250 and 1:2500 and says explicitly not to pick one, because
open question 1 says the whole design rests on that number and it can only be
looked at (C7). It is a constant so the sweep is a one-line change.

⚠ The Hydrographic row above is 1:**2500**, not the 1:5000 an older draft of this
file claimed. `Tuning.CoastalScaleDenominator` is authoritative and the surviving
comment beside it still argues for 5000.

**Rotation** (D2) — quantised to 0.1°, normalised to [0,180):

| office | derived from | degenerate fallback |
|---|---|---|
| Hydrographic | PCA of the longest coast loop, sampled **by arc length** at `u/4` | `0°` if λ1/λ2 < 1.15 |
| Land Survey | PCA of land above 0.35·MaxElevation on the 64 m lattice | `θ_hydro + 90°` if λ1/λ2 < 1.15 or < 64 points |
| Garrison | — | always `0°` |
| Antiquarian | — | nominal `0°`; **each detail sheet rolls its own**, see below |

Arc-length sampling is not cosmetic: marching squares emits vertices at a density
that varies with how the coast meets the lattice, so vertex-weighted PCA is
biased. ⚠ The Land Survey fallback fires on ~2/3 of islands (F3).

**Three cutters, one per coverage shape.** `Island.CutSurveys` dispatches:

| office | cutter | shape |
|---|---|---|
| Hydrographic | `CoastWalkCutter.Cut` | walks a stretch of shore, rotation **per sheet** (D-H2) |
| Land Survey, Garrison | `SurveyCutter.Cut` | axis-aligned lattice, one rotation per survey (R2.4) |
| Antiquarian | `DetailSheetCutter.Cut` | one sheet per qualifying POI — no walking, no tiling |

**Lattice cutting** (`SurveyCutter.Cut`):
1. rotate ground by −θ → frame space; sheets tile axis-aligned there
2. frame-space bbox of the land bbox = extent
3. lattice at `step = size * (1 − overlap)`, laid **centred** on the extent
4. cull (below)
5. order row-major in frame space, +u then +v, from bottom-left
6. **number 1..N — after culling, never before** (§10.4). A gap must mean
   "missing sheet"; asserted by A4.

**Cull** — 16×16 samples per rect in frame space:

| office | keeps |
|---|---|
| Hydrographic | any coastline segment crosses the rect (Liang–Barsky in frame space) |
| Land Survey | `landFraction ≥ 0.60` |
| Garrison | in the busiest land-bbox quadrant, `landFraction ≥ 0.02` |

…**and all of them** additionally require `servedFraction ≥ 0.50`. There is no
exemption for any office — D1 removed Garrison's by making the test
office-relative.

⚠ The Hydrographic row above is `SurveyCutter.Keeps`, which the coast walk no
longer routes through. `CoastWalkCutter.Keeps` is its own, simpler test: 16×16
samples on the rotated rect, keep if `served / land ≥ 0.50` over the land
samples, drop a pure-sea rect outright. Same threshold, same sample grid, no
coastline crossing test — the walk only ever places rects on the shore, so
there was nothing left for that test to reject.

**Detail cutting** (`DetailSheetCutter.Cut`) — the simplest of the three:

1. walk `Features.Pois` in generation order;
2. drop any POI failing the **placeability floor** (§7) — it produces *no sheet*.
   The POI still exists on the island; it is simply a thing no expedition fixed
   the position of, which beats shipping an unplaceable sheet;
3. centre a `SheetFormat.DetailSheet` on each survivor;
4. rotation from `Streams.For(islandSeed, "poi.sheet", poiIndex)`, quantised to
   0.1° and normalised to `[0,180)` via `Rotations.NormaliseAxisDeg`. **The only
   rolled rotation in the collection** — every other one is derived (D2). A field
   sketch has no fixed orientation, and resolving it is part of the placement;
5. number `1..M` **after the cull**, the same rule §10.4 imposes on survey runs.

Indexing the rotation by POI rather than by sheet number means a POI that later
starts or stops clearing the floor cannot re-roll another POI's sheet.

`Sheet.IsDetail` partitions the two kinds of sheet. Detail sheets are numbered
`1..M` independently of any survey's `1..N` and display as `D1..DM`, so a gap in
each run stays unambiguous (R2.10b). Because POIs got their own office rather
than being shared across the existing three, that run *is* a whole survey and
A4's existing per-survey contiguity check covers it unchanged.

---

## 9. Office × class matrix (§8.3)

| | coast | contour | peak | river | settlement | grid | sounding | poi |
|---|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|
| Hydrographic | ✓ | — | — | — | ✓ | — | ✓ | — |
| Land Survey | ✓ | ✓ | ✓ | ✓ | ✓ | — | — | — |
| Garrison | ✓ | — | ✓ | — | — | ✓ | — | — |
| Antiquarian | ✓ | ✓ | ✓ | ✓ | ✓ | — | — | ✓ |

Binary — draw or omit. Any renderer must ask `FeatureMatrix.Draws` and nothing
else. The invariant that makes sheets cross-referenceable: any two offices whose
coverage can overlap share at least one drawn class (measured by A6, **100.0% of
527 overlapping cross-office pairs** — 100.0% of 425 before POC-03).

**Why the Antiquarian row draws surroundings and not just its POI.** A detail
sheet showing only its own POI would share no drawn class with any other office
and the invariant would collapse exactly where detail sheets need it most — the
whole design is that a small sheet becomes placeable once the board around it is
assembled. It draws no grid (Garrison's signature) and no soundings
(Hydrographic's), so the row stays distinguishable at a glance.

**Deviation from POC-03 spec §3, decided by the project owner.** §3 / P3.1 shared
POIs across the existing three offices by type. They got their own office
instead, so P3.1 and §3's proposed `DrawsPoi(office, kind)` are superseded:
with one POI office there is no kind-dependent row and `Draws(office, Poi)` is a
plain lookup again. P3.3's blind-spot asymmetry survives in a different form.

---

## 10. Known deviations from spec — read before "fixing" anything

| id | what |
|---|---|
| F3 | Land Survey rotation is the `+90°` fallback on ~2/3 of islands (atolls/fjords lack high ground) |
| F4 | **Resolved for the paper path.** `LodForScale` was uncapped, so a 1:2500 sheet contoured at lod 7 (0.5 m) and cost ~2.9 s against A8's 50 ms. Capped at `Tuning.MaxPaperContourLod` = 4: now 47 ms, and the budget moved to 100 ms so the check is not a coin toss. Hierarchical extraction turned out to be unnecessary — the field is band-limited and the detail was never there. **The raster path is NOT fixed** and is not the same problem: `RenderLod` ties the cell to the pixel so the stroke matches the fill, and capping it would break that. |
| F5 | Garrison is 71% grid-and-coast-only sheets |
| F6a | Fjorded falloff is discontinuous at θ=±π — a visible radial seam |
| F6b | §6.2's LOD table row for 1:25000 is wrong; the formula is right |
| **F8** | **Gaps are 0% — R1.8 ("some ground has no sheet at all") is NOT satisfied.** Hydrographic tiles the whole shore. Fix is a partial arc, not tuning. |

Current sheet economy: mountainous 48, fjorded 31, atoll 17 (median, 50 seeds),
excluding detail sheets. Atolls yield **zero** Land Survey sheets — §5.3 says
that is the correct answer. Whole-island 1:50000 fallback fired on 0/50 seeds.

### POC-03 (points of interest)

As-built observations, not yet written into a `poc-03-findings.md`. Ids are local
to this file.

| id | what |
|---|---|
| **P1** | **The placeability floor never bites.** `u ≈ 1520 m`, the detail sheet is 275 m — the floor asks about ~30× the sheet's own area, so **0 of 191 POIs failed it** over 50 seeds. POC-03 C6 calls that number the interesting one; zero means the siting rules and the floor do not disagree because the floor is not really looking. |
| **P2** | Measured alternatives on 30 seeds (91 POIs): tightened to `{River, Settlement}` → 13 fail (14%); measured on the sheet's **actual footprint** → 4 fail (4.4%). Footprint content: coast 58/91, ≥50 m relief 83/91, river 7, settlement 7, peak 2. C3 passes honestly either way, but the rule as written is not what is doing the work. |
| **P3** | **Open question 2's tightening cannot be written as specified.** It names coast, river, lake shore, settlement as the locally distinctive classes — but `ServedClass(Coast)` is unconditionally false by D1 (§7). Coast is both the class the machinery refuses to answer and the one 58/91 sheets actually carry. Tightening needs a coast presence test outside `ServiceRule`, or must drop coast. |
| **P4** | POC-03 §1.3 step 2 says "score and sort by a total order" but **defines no score**. There is none in the implementation; the total order is the whole of it. See §6 for the kind-major selection deviation that follows from the order being degenerate. |
| **P5** | **`Stack` has since been removed.** It sat offshore by construction, so its detail sheet was mostly open water with nothing to place it against — that is where P2's four footprint failures clustered. Its enum value 1 is left as a permanent gap: kind index is the primary key of the POI total order, so renumbering the survivors would move every other POI on every island. Hence `PoiKinds.Count` 12 against `PoiKinds.IndexRange` 13. |

POI density (50 seeds): mountainous mean 4.85 (3–7), fjorded 5.42 (3–8), atoll
1.39 (0–3). **4 of 50 islands carry no POI at all** — P1.4 permits it. 191 POIs:
111 natural oddities, 80 ruins, spread 11–21 across all 13 kinds — figures
measured while `Stack` was still a kind. There are 12 kinds now (see P5), so
these counts predate the removal and have not been re-measured.

---

## 11. Running things

```bash
Tools/run-acceptance.sh          # source assertions + editor type check + every gated check (~2 min)
Tools/run-acceptance.sh --list   # the sections, the checks, and what each costs
Tools/run-acceptance.sh all      # the above plus the 50-seed metrics and B5's PNG sweep (~3.5 min)
Tools/run-acceptance.sh A8       # one check (~7 s). Build only; no source or editor check
Tools/run-acceptance.sh render   # a group: the POC-02 gates (~4 s)
Tools/run-acceptance.sh gate -A2 -C2   # the gates without the two 100x determinism loops (~1 min)
Tools/run-acceptance.sh metrics --seeds=10   # the sweeps, at a fifth of the seeds
Tools/check-sources.sh           # §4.1 / §14 assertions only
```

A selector is a **group** (`gen`, `poi`, `render`, `metrics`, `sweep`, `describe`),
a **check id** (`A2`, `C4`, `B3`, …), a **section this script owns** (`sources`,
`editor`), or an alias (`all`, `gate`, and the old mode names `fast` and `poc02`).
Prefix any of them with `-` to subtract. Naming specific checks skips the source
assertions and the Unity type check — ask for them by name, or run with no
selector at all, when you want them. Every run ends with a per-check time table:
the whole suite is ~3.5 minutes and nearly all of it is island generation, so that
table is what tells you which sections you can leave out next time.

Where the time goes, measured on this machine (island generation ~0.5 s each,
which is what almost every number below is made of): A2 55 s, A7 27 s, C6 27 s,
C2 24 s, C3/C4/A4/A5 ~10 s each, A8 7 s, A6 5 s, B4 4 s, B2/B3/A3/B5 under 3 s.
**A4, A5, C3 and C4 each regenerate islands 0–19 from scratch, and A6 regenerates
0–9** — the same islands, four to five times over. A memoised `Island.FromSeed`
in the harness (bypassed by A2, C2 and A8, which must generate for real) would cut
the gate run by roughly half; it has not been done because it changes what the
suites measure and nobody has checked that against §13 yet.

Gated checks: A2–A5, A8 and POC-03's **C2** (POI determinism), **C3** (placeability
floor), **C4** (both numbering runs). **A6** reports its percentage against the 90%
target but never fails on it; `--list` marks every ungated check with `*`. A8's
**sheet re-contour clause now passes** at ~47 ms (F4, capped LOD).

⚠ **A8's island-generation clause fails, and the reason is not understood.** This
file records ~118 ms and dismisses a ~455 ms reading as a loaded machine. But
~467 ms is what an idle machine measures now, and a snapshot taken *before* any of
the recent refactoring measures 469.5 ms — so the 4× gap does not come from that
work. Either the ~118 ms was taken on very different hardware, or it predates
something that made generation four times more expensive, or it is simply wrong.
Nobody should quote either number until someone instruments `Island.FromSeed`
per stage and finds out. **C6** (density, kind distribution, no-sheet count) is a metric
in the `metrics` pass. C1 is human-judged and needs the map table; C7 is the
detail-sheet scale sweep and has not been run.

⚠ **`Tools/GenHarness` compiles only `Generation` and `Render`.** The `Editor`
and `Tests` assemblies are NOT covered, so a mistake there builds clean headlessly
and fails only inside Unity. `IslandDebugWindow.cs` alone holds four classes
(`ContourCache`, `HeightMapping`, `IslandStats`, `DebugModel`) plus the window —
a method dropped in the wrong one compiles either way. To check them without
Unity, compile `Generation + Render + Editor + Tests` in a throwaway csproj
against the editor's managed assemblies
(`<Unity install>/Unity.app/Contents/Managed/` and `Library/ScriptAssemblies/`,
`netstandard2.1`, excluding the `System.*` DLLs so the SDK's reference assemblies
win).

Unity: EditMode tests in `Archivist.Tests` (21); debug window at
**Window → Archivist → Island Debug**. The window is destroyed by every domain
reload — reopen from the menu after a recompile. The toolbar's `cut` toggles are
`HYD LS GAR ANT`; the sidebar gained a `poi` layer. Switching an office off
changes what is **generated**, not merely what is drawn, so the footer warns
while `Island.AllOfficesEnabled` is false.
