# Generation Pipeline — As Built

Reference for agents working on this codebase. Describes what the code **does**,
not what the spec intends. Authority order: `poc-01-island-and-surveys.md`
(intent) → `poc-01-decisions.md` (D1–D5) → `poc-01-findings.md` (F1–F8, measured)
→ this file (as built).

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

Cost: ~115 ms per island.

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
  contours by first vertex. Never rely on natural iteration order.
- **Quantisation (D3).** `Height01` is quantised at `2^-16` and *every* threshold
  compares that value — marching-squares corner signs, saddle rule,
  `landFraction`, `Elevation < -4`, relief. A tie at exactly `SeaLevel` is
  **land**. `Gradient` is the one exemption (unquantised; callers round
  `|Gradient|` with `Q.Grad` before comparing). Angles quantise to 0.1° via
  `Q.Deg`. Helpers live in `Determinism/Q.cs`.

Named streams currently in use — do not repurpose:
`character`, `radius`, `field`, `falloff`, `settlements`, `rivers`[peakIndex],
`names`, `names.island`, `names.settlements`[i], `names.peaks`[i],
`wholeIsland`, `year`[office], `yearWholeIsland`[office].

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
8  NameGenerator.Generate        then attached positionally via WithName
9  new ServiceRule(...)          (needs all discrete features)
10 CutSurveys                    whole-island first, then Hydro -> Land -> Garrison
```

Step 10's office order matters: Land Survey's degenerate rotation falls back to
`θ_hydro + 90°`, so Hydrographic must be derived first.

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

⚠ **Cost scales with sheet AREA, not coastline length**: ~9.8 M samples per
sheet at any scale (the denominator cancels — paper detail is per-mm), ≈5 s.
A8's 50 ms budget is unreachable without hierarchical extraction (F4).

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
- **Soundings** — field-derived, 400 m global lattice, kept where `Elevation < -4 m`.

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

`u = NominalRadius / 4` (~1520 m). Implemented as per-class bitmasks on the 64 m
lattice (disc stamps for discrete, separable max/min filters for relief), built
once in the constructor over `landBounds.Expanded(u)`.

Net effect: Hydrographic is served by soundings, Garrison by its own grid, and
**Land Survey is the only office the test actually culls.**

---

## 8. Surveys and sheets

Four surveys per island: whole-island + one per office.

**Scale is per office** (F1) — nothing in R2.2 ties surveys to a shared scale:

| survey | scale | ground per sheet |
|---|---|---|
| Hydrographic | 1:5000 | 2570 × 3805 m |
| Land Survey, Garrison | 1:2500 | 1285 × 1902 m |
| whole-island | 1:25000, or 1:50000 if the bbox overruns (D5) | 12850 × 19025 m |

Sheet format is A1 (594×841, 40 mm margin); only orientation varies. Overlap 20%.
Garrison grid pitch is **40 mm on paper** at any scale (1000 m / 200 m / 100 m).

**Rotation** (D2) — quantised to 0.1°, normalised to [0,180):

| office | derived from | degenerate fallback |
|---|---|---|
| Hydrographic | PCA of the longest coast loop, sampled **by arc length** at `u/4` | `0°` if λ1/λ2 < 1.15 |
| Land Survey | PCA of land above 0.35·MaxElevation on the 64 m lattice | `θ_hydro + 90°` if λ1/λ2 < 1.15 or < 64 points |
| Garrison | — | always `0°` |

Arc-length sampling is not cosmetic: marching squares emits vertices at a density
that varies with how the coast meets the lattice, so vertex-weighted PCA is
biased. ⚠ The Land Survey fallback fires on ~2/3 of islands (F3).

**Cutting** (`SurveyCutter.Cut`):
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

…**and all three** additionally require `servedFraction ≥ 0.50`. There is no
exemption for any office — D1 removed Garrison's by making the test
office-relative.

---

## 9. Office × class matrix (§8.3)

| | coast | contour | peak | river | settlement | grid | sounding |
|---|:--:|:--:|:--:|:--:|:--:|:--:|:--:|
| Hydrographic | ✓ | — | — | — | ✓ | — | ✓ |
| Land Survey | ✓ | ✓ | ✓ | ✓ | ✓ | — | — |
| Garrison | ✓ | — | ✓ | — | — | ✓ | — |

Binary — draw or omit. Any renderer must ask `FeatureMatrix.Draws` and nothing
else. The invariant that makes sheets cross-referenceable: any two offices whose
coverage can overlap share at least one drawn class (measured by A6, 99.7%).

---

## 10. Known deviations from spec — read before "fixing" anything

| id | what |
|---|---|
| F3 | Land Survey rotation is the `+90°` fallback on ~2/3 of islands (atolls/fjords lack high ground) |
| F4 | Per-sheet contouring is ~5 s against A8's 50 ms; scale-invariant, needs hierarchical extraction |
| F5 | Garrison is 71% grid-and-coast-only sheets |
| F6a | Fjorded falloff is discontinuous at θ=±π — a visible radial seam |
| F6b | §6.2's LOD table row for 1:25000 is wrong; the formula is right |
| **F8** | **Gaps are 0% — R1.8 ("some ground has no sheet at all") is NOT satisfied.** Hydrographic tiles the whole shore. Fix is a partial arc, not tuning. |

Current sheet economy: mountainous 38, fjorded 19, atoll 16 (median, 50 seeds).
Atolls yield **zero** Land Survey sheets — §5.3 says that is the correct answer.

---

## 11. Running things

```bash
Tools/run-acceptance.sh          # build + source assertions + full §13 suite
Tools/run-acceptance.sh fast     # skip the 50-seed metrics
Tools/check-sources.sh           # §4.1 / §14 assertions only
```

Unity: EditMode tests in `Archivist.Tests` (21); debug window at
**Window → Archivist → Island Debug**. The window is destroyed by every domain
reload — reopen from the menu after a recompile.
