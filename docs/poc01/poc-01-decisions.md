# POC-01 — Blocker Decisions

Companion to `poc-01-island-and-surveys.md`. That document is the authority on
construction; this one records the decisions taken on the five points where it
was self-contradictory, circular, or silent, and which therefore blocked
implementation past step 3 of §18.

**Status: folded in.** All five decisions are now applied to
`poc-01-island-and-surveys.md` — inline in the sections named by each *Edits*
block, and recorded there as amendments §15.5–§15.9. That document is once again
the single authority on construction; this one is kept as the record of *why*
each of the five was decided the way it was, and is not a second source of truth.

Three of the five touch `requirements.md` (R1.5 in D1, R2.4's reading in D2, R2.3
in D5) and are labelled as such in §15. Two are internal corrections to the POC
spec. `requirements.md` itself remains unedited.

---

## Index

| # | blocker | resolves | landed in |
|---|---|---|---|
| D1 | R1.5 cull applied to offices it should not | §10.2 step 6 vs §10.3 contradiction | §7.4, §10.2, §10.3, §12 · §15.5 |
| D2 | Hydrographic rotation is circular | §10.1 "surveyed arc" | §10.1, §12, §13.7 · §15.6 |
| D3 | `theta` vs the transcendental rule | §4.4 vs §5.2 step 5 | §4.4, §5.2, §6.1, §7.2, §12 · §15.7 |
| D4 | Grid is never generated | §8.3 draws a class §7 never makes | §3.1, §6.4, §11, §12, §13.5 · §15.8 |
| D5 | Whole-island bbox assert has no fallback | §8.1 | §8.1, §10.5, §12, §13.7 · §15.9 |

---

## D1 — The service rule is office-relative, and the coastline never serves

### Decision

`served` is tested against the classes an office draws **other than the coast**.
The cull then applies uniformly to all three offices, and Garrison's explicit
exemption in §10.3 is deleted.

```
Serving(office) = drawn(office) \ { Coast }        // drawn() is FeatureMatrix, §8.3
served(p, office) = ∃ class c ∈ Serving(office) present within u of p
```

| office | Serving set |
|---|---|
| Hydrographic | Settlement, Sounding |
| Land Survey | Contour, Peak, River, Settlement |
| Garrison | Peak, Grid |

Presence of class `c` at point `p` within radius `u`:

| class | present when |
|---|---|
| Peak, River, Settlement | a discrete feature of that class lies within `u` of `p` |
| Sounding | some sample within `u` has `Elevation < −4 m` |
| Contour | relief within `u` spans one contour step: `max Elevation − min Elevation ≥ 50 m` |
| Grid | always |

`servedFraction ≥ 0.50` is required of **every** office, Garrison included.

### `u` is fixed to the nominal radius, not the land bbox

```
u = 2 * NominalRadius / 8 = NominalRadius / 4        // 1520 m at default jitter
```

§7.4 says `islandDiameter / 8` and never defines `islandDiameter`; its own worked
example (~1.5 km on a 12 km island) is `2 × NominalRadius`. Fixing it to
`NominalRadius` rather than the land bbox keeps the service radius independent of
the coastline it is used to judge, and keeps it stable across characters.

### Reason

The coastline is island-scale by R1.4, so it cannot be the thing that makes a
sheet worth cutting — every sheet in a coastal survey carries it. Excluding it is
precisely what makes the rule mean *this office draws something here*.

Three consequences then fall out instead of being carved out:

- **Hydrographic.** Coastal rects are served by soundings. The over-cull — an
  empty stretch of shore with no settlement within `u` losing its sheet, despite
  that sheet carrying a coastline and a field of depths — disappears with no
  special case. This was the contradiction: §10.2 step 6 applied R1.5 to every
  office while the §10.3 table only carried `servedFraction` in the Land Survey
  row, and taken literally it broke both the "coast covered ×3" picture in §10.3
  and the premise of §13.5.
- **Garrison.** Served everywhere by its own grid, so the exemption line becomes
  redundant. One fewer special case to remember, and the `landFraction ≥ 0.02`
  floor still removes pure-sea rects.
- **Land Survey.** Keeps a cull with teeth, and a better one. A hillside with
  50 m of relief and no village now survives, where a discrete-only reading would
  have dropped it. Genuinely flat, featureless ground still does not, so R1.8
  holds — carried, on a mountainous island, mostly by the `landFraction ≥ 0.60`
  edge test rather than by service.

### Implementation

One `ServiceRule` per island — already in the §14 layout — holding one bitmask
per class on the 64 m lattice over the land bbox:

- Peak / River / Settlement: stamp a disc of radius `u` at each feature.
- Relief: separable max-filter and min-filter at radius `u`, then threshold at
  the 50 m contour step.
- Sounding: threshold `Elevation < −4 m`, then a max-filter at radius `u`.
- Grid: constant true.

All integer and comparison work, so determinism is free (§13.2 is unaffected).
`servedFraction` reads the masks at the same 16×16 rect samples §10.3 already
takes for `landFraction`; no extra field evaluation.

### Edits

- §7.4 — replace the `served(p)` definition with the office-relative one; fix `u`.
- §10.2 step 6 — "Cull per office (§10.3), which now includes the service test."
- §10.3 — add `servedFraction ≥ 0.50` to the Hydrographic and Garrison rows;
  delete the sentence exempting Garrison from the R1.5 service cull.
- §12 — `u` becomes `NominalRadius / 4`.
- §14 — `ServiceRule.cs` gains the mask construction described above.

---

## D2 — Rotation derives from the main coastline loop, with a stated degenerate case

### Decision

"Surveyed arc" is struck from §10.1. The circularity goes with it.

```
θ_hydro    = PCA(main coast loop, sampled by arc length every u/4)
             → 0.0°               if λ1/λ2 < 1.15
θ_land     = PCA(land points above 0.35 × MaxElevation)
             → θ_hydro + 90°      if λ1/λ2 < 1.15, or fewer than 64 points
θ_garrison = 0.0°                                              (unchanged)
```

Main loop = the longest coastline polyline; ties broken by first vertex
`(x asc, y asc)`. All three results are rounded to 0.1° per §4.4.

### Reason

§10.1 derived Hydrographic rotation from "the coastline points in the surveyed
arc", but the arc is a product of cutting and rotation is step 1 of cutting. The
arc cannot be known yet. Removing it costs nothing: Hydrographic's cull already
keeps every rect the coastline crosses, so the survey follows the whole shore and
there was never an arc to speak of.

**Sample by arc length, not by vertex.** Marching squares emits vertices at a
density that varies with how the coast meets the lattice, so vertex-weighted PCA
is biased toward whichever stretch happens to run diagonally across cells. `u/4`
≈ 380 m is dense enough to describe the loop and coarse enough to be cheap.

**The isotropy guard is not optional.** A round island has no long axis; the
larger eigenvector is then noise, and two seeds a hair either side of isotropic
would sit their whole survey 90° apart for no reason a player could ever read.
`λ1/λ2 < 1.15 → 0°` makes that a stated answer rather than a coin flip. An atoll
is the standing case: its coast is a ring, so it is isotropic by construction and
its Hydrographic survey is north-up.

**Land falls back to cross-grain, not to north.** If both degenerate cases fell
back to 0°, Land Survey and Garrison would share a rotation on exactly the
islands where §10.1's "third readable office signal" is already weakest.
`θ_hydro + 90°` is geometric, deterministic, and distinct from both other
offices. It reads as a traverse run across the island rather than along it.

### Measure before hardening

A ridge running along the island's long axis legitimately gives
`θ_hydro ≈ θ_land`. That is not a bug and must not be pre-empted with a forced
separation rule. Report the distribution of `|θ_hydro − θ_land| mod 180°` over
the 50 seeds of A7 first, and decide with the numbers in hand.

### Consequence to watch

With no arc, Hydrographic surveys the entire shore, and that is the single
largest driver of total sheet count. If A7 (§13.7) lands far above the 30–60 that
§6.1 of `requirements.md` guesses, the Hydrographic ring is the first knob to
reach for — before the scale table or the domain size.

### Edits

- §10.1 — replace the Hydrographic row's "in the surveyed arc"; add the fallback
  column and the isotropy threshold; state arc-length sampling.
- §12 — add `λ1/λ2` isotropy threshold (1.15), PCA arc-length sample step (`u/4`),
  Land Survey minimum point count (64).
- §13.7 — add the Hydro/Land rotation separation distribution to the reported set.

---

## D3 — Quantise `Height01`, not `theta`

### Decision

§4.4's rule becomes: **quantise the derived scalar that feeds the branch, not the
intermediate that feeds it.** For the field that scalar is `h01`:

```
h01 = round(h01_raw * 65536) / 65536          // 2^-16 ≈ 1.5e-5
```

Every threshold test compares the quantised value — marching-squares corner
signs, the saddle centre-sign disambiguation (§6.1), `landFraction`,
`Elevation < −4 m`, and the relief test in D1. Ties at exactly `SeaLevel` count
as land, stated once and never re-litigated.

§4.4's existing "derived rotations → 0.1°" **stays**. That is a different
exposure: an ulp there can shift a rect across the sheet lattice and change the
sheet count.

### Reason

§5.2 step 5 feeds `atan2(y, x)` into the Fjorded falloff, and `h01 ≥ SeaLevel` is
a branch — so §4.4 as written demands theta be quantised before use. But that
fixes the wrong thing. It makes one intermediate reproducible while every other
float path into the same branch stays exposed, and any quantum coarse enough to
be safe puts visible angular banding on the fjord coast, which at lod 6 is being
contoured against a 1 m cell.

Quantising `h01` covers every transcendental in the composition at once, costs
one line, and leaves the coastline geometry untouched.

**The margin.** An `atan2` last-ulp difference is ~4e-16 absolute. Through
`fbm1(θ·6)`, the 0.18 cut, and smoothstep's derivative it arrives at `h01` around
1e-15. Against a 1.5e-5 quantum that is ten orders of magnitude, so the chance a
given sample straddles a quantisation boundary is ~1e-10 — comfortably below one
occurrence across the whole 50-seed suite. In elevation terms the quantum is
about 2 cm, which is invisible at every scale in §8.1.

### Two carve-outs

- **Interpolation.** Edge-crossing interpolation must use the *same* quantised
  values on both sides of a shared border, or §6.2's lattice guarantee is lost.
  A3 (§13.3) is what catches this if it is got wrong; it is the reason A3 exists.
- **Gradient.** `IHeightField.Gradient` is computed from the **unquantised**
  composition — a 2 cm staircase across a central-difference step would be coarse
  — and `|Gradient|` is rounded to 1e-4 before §7.2's `< 0.04` test, which is its
  only branch.

### While here — `Gradient` was undefined

§5.2 declares `Gradient` as "central difference, for rivers" with no step and no
stated quantity, and §7.2 then compares `|Gradient| < 0.04` in no unit. Fixed:

```
Gradient returns d(Elevation) / d(distance), metres per metre,
central difference at h = 20 m.
```

So `0.04` is a slope of about 2.3°, which is the flatness §7.2 wants.

### Edits

- §4.4 — restate the rule; keep the rotation clause; add the `h01` quantum and
  the sea-level tie rule.
- §5.2 — add quantisation as step 7 of the composition; define `Gradient`'s
  quantity and step.
- §6.1 — note that corner signs and the saddle rule compare quantised `h01`.
- §7.2 — state the unit of the `0.04` threshold and its 1e-4 rounding.
- §12 — add `h01` quantum (2^-16), gradient step (20 m).

---

## D4 — The grid is defined, and A5 is restated so it is not vacuous

### Decision, generation

Garrison's grid is field-independent: a square grid in the **true-north frame**
(Garrison rotation is 0°), origin at the **domain origin `(0,0)`**, pitch by
scale, lines labelled with easting and northing in metres from origin.

| scale | grid pitch |
|---|---|
| 1:25000 | 1000 m |
| 1:5000 | 200 m |

It is field-derived in §3.1's sense — re-queryable per rect, no identity, nothing
persisted.

The origin being global rather than per-sheet is the whole point, and it is the
same argument as §6.2's contour lattice: two adjacent Garrison sheets must show
the same grid lines in the same places, or the block stops reading as one survey.

### Decision, acceptance

Grid **counts** as content for A5 (§13.5). A Garrison sheet showing empty
moorland under a grid passes, because that is a real document and Garrison is
exactly the office that would have made it.

But the pass then costs nothing, so the vacuousness is made visible instead of
hidden:

> **A5b — thin sheets · metric, reported per office.** Percentage of sheets whose
> only content is Coast and/or Grid.

Reported, not gated — the same posture as A6 and A7. If Garrison comes back at
60%, that is the evidence-backed answer to whether the office is a chore, and it
belongs beside the sheet-economy numbers in the §11 stats footer.

### Reason

`FeatureClass` includes `Grid` and §8.3 has Garrison drawing it, but no section
generates it and §12 has no entry for it — it was simply missing. And because a
grid covers every rect unconditionally, adding it naively would make A5 —
"every sheet contains at least one drawn feature for its own office" —
automatically true for Garrison, which is the one office whose sheets most need
the check.

Giving Garrison a fourth class was considered and rejected: §2 of
`requirements.md` has it drawing roads and sightlines, neither of which this POC
generates, and adding them is scope §17 already defers.

### Edits

- §6 — add `6.4 Grid` alongside soundings, as field-derived.
- §12 — add grid pitch, both scales.
- §13.5 — state that Grid counts; add A5b.
- §11 — add the A5b percentage to the stats footer.

---

## D5 — The whole-island sheet picks the smallest scale that fits

### Decision

The whole-island survey (§10.5) chooses its scale as the smallest of
`{ 1:25000, 1:50000 }` whose map area contains the island's land bbox in either
orientation. 1:25000 remains the normal answer.

### Reason

§8.1 asserts the island bbox fits the 1:25000 map area, 12 850 × 19 025 m, and
gives no fallback if it does not. `NominalRadius` is `0.38 × 16 000 = 6080 m`
jittered ±8%, so a mountainous island whose land approaches its nominal radius
has a bbox near 13 km and blows the portrait width. Landscape covers most of
those, but not with margin, and the failure mode is a hard stop on an otherwise
valid seed.

Clamping the jitter was rejected: land extent is not a clean function of
`NominalRadius`, so the clamp would be guesswork tuned to whichever seeds were
tried. R2.3 permits three or four fixed scale values, both working scales are
untouched, and the cost of the fallback is one slightly smaller sheet on the
largest islands.

**Check this early.** Generate 50 seeds and report land-bbox extents immediately
after step 3 of §18 — before the cutter exists — so the frequency of the fallback
is known rather than discovered.

### Edits

- §8.1 — replace the assert with the scale selection.
- §10.5 — state the selection; note 1:25000 as the expected case.
- §12 — scales row becomes `1:5000, 1:25000, 1:50000 (whole-island fallback only)`.
- §13.7 — report land-bbox extents and fallback frequency per character.

---

## 6. What is still open

Not decided here, and not blocking.

| open | where it lands |
|---|---|
| `shelter` in §7.2 has no formula — "coastline concavity in a 600 m neighbourhood" | needs one line before step 4 of §18; any monotone concavity measure will do, so it is a tuning choice, not a design one |
| `SurveySpec.Year` has no generation rule | label only in v1; pick a per-office range when the header block is drawn |
| Sheet orientation "chosen to fit the target region" has no tie-break | trivial; decide at implementation |
| Phonology tables are sketched, not written | ~24 roots and ~10 suffixes × 3, per §9. Data entry, step 5 of §18 |
| Hydro/Land rotation separation | measured in A7 first — see D2 |
| Garrison thin-sheet percentage | measured in A5b first — see D4 |

## 7. Effect on the build order

§18 is unchanged. Steps 1–3 were never blocked and are unaffected by any decision
here. D3 lands in step 3, D1 and D2 in step 6, D4 in step 7, D5 at step 3 as a
reported check and step 6 as behaviour.

---

## 8. Fold-in record

Applied to `poc-01-island-and-surveys.md` as 23 edits across §3.1, §4.4, §5.2,
§6.1, §6.4 (new), §7.2, §7.4, §8.1, §10.1, §10.2, §10.3, §10.5, §11, §12, §13.5,
§13.5a (new), §13.7, and §15 (preamble plus §15.5–§15.9). One pre-existing stray
code fence at the end of §18 was removed while there; it closed nothing.
