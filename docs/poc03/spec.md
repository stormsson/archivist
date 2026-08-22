# POC-03 — Points of Interest · Specification

Construction. `requirements.md` is the authority on intent. Read
`../generation_for_agents.md` first — this assumes the generator's API and
determinism contract.

Covers the **generator half** only: POI features and their detail sheets. The
placement half needs the map table (requirements §1.1).

---

## 1. The features

### 1.1 Type table

```csharp
public enum PoiKind
{
    // natural oddities
    SeaArch, Stack, CaveMouth, Blowhole, Spring, ErraticBoulder, LandmarkTree,
    // ruins
    RuinedTower, Cairn, StandingStones, RuinedChapel, RuinedJetty, Enclosure
}
```

Two families, kept in one enum so a single `FeatureClass.Poi` covers both and the
§8.3 matrix does not need two rows. `PoiKind.IsRuin` distinguishes them where it
matters (office remit, §4).

### 1.2 Siting — derived, never scattered

Each kind has a predicate over ground the generator already knows. A kind whose
predicate is unsatisfiable on an island simply does not occur there (P1.3), which
is why an atoll has arches and stacks but no cairns.

| kind | sited where |
|---|---|
| SeaArch, Stack, CaveMouth | coastline with high local relief — steep shore |
| Blowhole | steep shore, on land within ~60 m of the coast |
| Spring | inland, local gradient convergence, above sea level |
| ErraticBoulder | open ground, low gradient, mid elevation |
| LandmarkTree | low gradient, low-to-mid elevation, away from settlements |
| RuinedTower | on or beside a peak — commanding ground |
| Cairn | high ground, ≥ 0.5 × the island's highest peak |
| StandingStones | flat ground, low gradient |
| RuinedChapel | within ~1 km of a settlement, or on a headland |
| RuinedJetty | sheltered coast — reuse §7.2's `shelter` measure |
| Enclosure | moderate slope, inland |

Predicates read the existing field and feature lists. **No new field evaluation
strategy** — sample candidates on the 128 m lattice, exactly as settlements do.

### 1.3 Generation order

Runs **after** peaks, settlements and rivers, and before naming — POIs may
reference them (RuinedChapel needs settlements, RuinedTower needs peaks) and
nothing references POIs.

```
1  candidates: 128 m lattice points passing any kind's predicate
2  score and sort by a TOTAL ORDER — (kind index asc, x asc, y asc)
3  greedy select with a minimum spacing, so POIs do not cluster
4  cap at a per-character count drawn from Streams.For(seed, "poi")
5  assign FeatureId(FeatureClass.Poi, i) in final order
```

Determinism rules are unchanged and non-negotiable: one named stream (`"poi"`,
plus `"poi.kind"` if needed), an explicit total order before every selection, no
dictionary iteration. Adding this stream must leave every existing feature
bit-identical — A2 already asserts exactly that (P1.5).

---

## 2. The detail sheet

### 2.1 Format

A small square sheet, centred on its POI.

```
DetailSheet : 250 x 250 mm paper, 15 mm margin -> map 220 x 220 mm
```

| scale | ground covered | note |
|---|---|---|
| 1:1250 | 275 x 275 m | tight — a stretch of shore, a river bend |
| 1:2500 | 550 x 550 m | roomier; matches the terrain offices' scale |

**Do not pick one from this table.** Open question 1 says the whole design rests
on this number and it cannot be reasoned about. Ship both behind
`RenderTuning`-style constants, render a sweep, and look. R2.3 permits three or
four fixed scale values; the set is currently 2500 / 25000 / 50000, so adding
1250 keeps it legal at four.

### 2.2 Rotation

Per sheet, seeded from `Streams.For(islandSeed, "poi.sheet", poiIndex)`,
quantised to 0.1° and normalised to `[0, 180)` like every other rotation. No
grid, no north arrow (P2.6) — resolving orientation is part of the placement.

Note the consequence already recorded for survey sheets: rotation stored mod 180
means "which way up" is undetermined in the data, and only the rendered content
resolves it. That is acceptable here for the same reason — the content is
asymmetric — but the map table's fit must not assume a heading.

### 2.3 The placeability floor (P2.4)

**This is the rule that decides whether the whole idea works.**

> A detail sheet must contain at least one drawn feature besides its own POI.

Implement it by reusing `ServiceRule` rather than writing a second one. The D1
mechanism already answers "does this office draw anything here", per class, on a
64 m lattice. What POI sheets need is the same question with the POI's own class
excluded:

```csharp
served = ServiceRule.Served(poi.Position, office)   // over Serving(office) \ { Poi }
```

A POI failing this is **not cut as a sheet**. The POI still exists on the island —
it is simply a thing no expedition managed to fix the position of, which is a
better outcome than an unplaceable sheet.

Open question 2 is live here: "one other feature" may be too weak, because one
contour looks like any other. If sheets prove unplaceable at the table, tighten
this to require a *locally distinctive* class — coast, river, lake shore,
settlement — rather than any class at all. Structure the predicate so that
tightening it is a one-line change.

### 2.4 Filing

Detail sheets belong to a survey but form their own numbered sub-series (P2.7),
so a gap in each run stays unambiguous (R2.10b):

```csharp
public readonly struct Sheet
{
    // ... existing
    public readonly bool IsDetail;      // false for survey sheets
}
```

Numbering: survey sheets `1..N` as today; detail sheets `1..M` independently,
displayed as `D1..DM`. A4 must assert contiguity over **both** runs separately.

---

## 3. Office assignment

POIs are shared by type (P3.1). One new class in the §8.3 matrix:

| | coast | contour | peak | river | settlement | grid | sounding | **poi** |
|---|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|
| Hydrographic | ✓ | — | — | — | ✓ | — | ✓ | **coastal kinds only** |
| Land Survey | ✓ | ✓ | ✓ | ✓ | ✓ | — | — | **all inland kinds** |
| Garrison | ✓ | — | ✓ | — | — | ✓ | — | **ruined works, high ground** |

`FeatureMatrix.Draws(office, Poi)` is therefore no longer a plain lookup — it
depends on the *kind*. Add:

```csharp
public static bool DrawsPoi(Office office, PoiKind kind);
```

and keep `Draws(office, FeatureClass.Poi)` returning true if the office draws
*any* kind, so the invariant check and A6 keep working.

**Watch the blind spots** (P3.3): Garrison omits civilian detail, so
`RuinedChapel` and `Enclosure` do not appear on its sheets even where
`RuinedTower` does. That asymmetry is the point of having offices.

**Re-measure A6 after this change** (P3.2). Adding a class that only one office
draws in a given place can *lower* shared-class coverage.

---

## 4. Acceptance

### C1 — Placeable late, not early · **primary** · manual · needs the map table

See requirements §4. Do not claim it until a human has placed one.

### C2 — Determinism · automated
Same seed, identical POIs and detail sheets over 100 generations. Adding the
`"poi"` stream leaves peaks, settlements, rivers and every survey sheet
bit-identical (P1.5) — this is the check that proves POIs were bolted on safely.

### C3 — Placeability floor · automated · GATED
**Every detail sheet contains at least one drawn feature besides its own POI**
(P2.4). This is C1's precondition; if it fails, C1 cannot pass.

### C4 — Numbering · automated
Survey run `1..N` and detail run `1..M` are each contiguous with no duplicates.

### C5 — Shared-class coverage · metric
A6 re-measured with the POI class present. Report before and after.

### C6 — Density and distribution · metric, reported
POIs per island by character; kind distribution; how many POIs were generated but
produced no sheet because they failed C3. That last number is the interesting
one — a high value means the siting rules and the placeability floor disagree.

### C7 — Sheet size sweep · metric, reported
Detail sheets at 1:1250 and 1:2500, exported for eyeballing. **This answers open
question 1**, which nothing else can.

---

## 5. Code layout

```
Generation/
  Features/   Poi.cs  PoiKinds.cs  PoiSiting.cs       (new)
              ServiceRule.cs                          (extended, not replaced)
  Sheets/     DetailSheetCutter.cs                    (new)
              FeatureMatrix.cs                        (gains DrawsPoi)
              SheetFormat.cs                          (gains DetailSheet)
```

The detail cutter is a **third** cutter beside the lattice and the coast walk, and
the simplest of the three: one sheet per qualifying POI, centred, seeded
rotation, no walking and no tiling.

---

## 6. Build order

1. `PoiKind` + siting predicates + `Poi` feature — visible in the island pane
   before any sheet exists.
2. C2 determinism, including the bit-identical check against existing features.
3. `ServiceRule` extension and the placeability floor — **C3 before any cutter**,
   because it decides which POIs become sheets at all.
4. `DetailSheetCutter` + C4 numbering.
5. Matrix change + `DrawsPoi` + re-measure A6 (C5).
6. Rendering via POC-02's renderer, then C7's size sweep.
7. C1 only once the map table exists.
