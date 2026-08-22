# POC-01 — First Measured Findings

Companion to `poc-01-island-and-surveys.md` and `poc-01-decisions.md`. Those record
intent and the decisions taken before code existed. This one records what the
generator, once built, actually does — and it disagrees with the spec in four
places.

**Status: F1 acted on (detail scale 1:5000 -> 1:2500); the rest measured, not acted on.** Every number below comes from the headless
harness (`Tools/run-acceptance.sh`), not from reasoning. Nothing in the generator
has been re-tuned in response; that is the next decision and it is not mine to
take alone.

Run: collection seed 8412, 50 seeds for §13.7 metrics, 10–20 for the gated checks.

---

## 1. Where the POC stands

| check | §13 | result |
|---|---|---|
| A2 determinism | 13.2 | **PASS** — 100 generations bit-identical; unrelated sub-stream draws leave the island untouched |
| A3 no seams | 13.3 | **PASS** — border vertices agree to 0.000000 m; the §6.2 lattice rule holds |
| A4 numbering | 13.4 | **PASS** — 80 surveys, all 1..N contiguous, no duplicates |
| A5 no blank sheets | 13.5 | **PASS** — 284 sheets, every one carries a class its office draws |
| A6 shared-class coverage | 13.6 | **PASS** — 96.5% of 283 overlapping cross-office pairs (target ≥ 90%) |
| A8 island generation | 13.8 | **PASS** — median 116.8 ms (budget 250 ms) |
| A8 sheet re-contour | 13.8 | **FAIL** — median 5117 ms (budget 50 ms) |

Source assertions (§13.2, third clause) pass: no `System.Random`, no
`UnityEngine.Random`, no `GetHashCode` seeding, no `UnityEngine` reference in the
Generation assembly, no wall-clock.

A1 — the primary criterion — is not yet testable; it needs the Compare pane.

---

## F1. The island is about the size of one sheet

**This is the root finding. F2 and F3 are both consequences of it.**

Measured land area per island, over 12 seeds per character:

| character | land area | largest landmass | land bbox extent |
|---|---|---|---|
| Mountainous | 15–27 km² | 26.0 km² (96% of land) | 6.1–7.7 km |
| Fjorded | 5–15 km² | 9.3 km² (89% of land) | 4.5–6.0 km |
| Atoll | 1.3–3.9 km² | 1.0 km² (43% of land) | 6.0–8.1 km |

One 1:5000 A1 sheet covers **2570 × 3805 m = 9.8 km²**.

So a whole fjorded island fits inside a single detail sheet with room to spare.
`NominalRadius` is 6080 m — a 12 km disc, 116 km² — but land occupies only about
a fifth of it, because with `n` centred on 0.5 the mountainous recipe needs
`n ≥ 0.417` to clear sea level even where the falloff is 1.0. The effective land
radius is 2–3 km, not 6 km.

**Consequence for §13.7.** Sheets per island: Mountainous median 19, Fjorded 11,
Atoll 14, range 8–22 — against `requirements.md` §6.1's guess of **30–60**.
§13.7 says what to do about that: *"if it lands far outside, the scale or the
domain size is wrong, not the guess."* It landed far outside, low.

**Consequence for D5.** The 1:50000 whole-island fallback fired on **0 of 50
seeds** — every land bbox fits 1:25000 with a wide margin. D5 was cheap insurance
and it is currently unused. Do not remove it: if F1 is fixed by growing the
islands, it starts firing.

---

## F2. Land Survey is starved by geometry, not by rule

Land Survey ships **0–3 sheets**, and 0 on most islands — 16 sheets across 20
islands, against Hydrographic's 178.

The cull is `landFraction ≥ 0.60` on a 9.8 km² rect, which needs **5.9 km² of
land inside one rectangle**. Per F1 most islands do not contain that much land in
total, let alone within one rect. No threshold tuning fixes this; the rect is
simply larger than the island.

This matters more than it looks. §1.2 makes the POC's primary criterion *two
offices over one hillside* — Hydrographic and Land Survey. With Land Survey at
zero sheets on most seeds, **A1 cannot be evaluated on most seeds**. The Compare
pane will work only on the mountainous islands that clear the threshold.

The three-way overlap in §10.3's coverage picture (`coast covered ×3`) is
likewise not happening: it is currently Hydrographic and Garrison, with Land
Survey absent.

---

## F3. Land Survey's rotation is the fallback, not the ridge

D2 said to measure `|θ_hydro − θ_land| mod 180°` before hardening anything.
Measured: **median exactly 90.0°, with only 3 of 50 below 8°**.

Exactly 90.0° is the signature of D2's degenerate fallback (`θ_hydro + 90°`)
firing, not of geometry. Direct check of the PCA input — land above
`0.35 × MaxElevation` on the 64 m lattice — confirms it: **degenerate on 10 of
15 islands.**

| character | high points | vs `PcaLandMinPoints` = 64 |
|---|---|---|
| Mountainous | 876–2179 | passes |
| Fjorded | 4–67 | mostly fails |
| Atoll | 0–55 | always fails (relief 90 m, threshold 31.5 m) |

The fallback is behaving exactly as D2 designed, and D2's choice of cross-grain
over north is vindicated — without it, Land Survey and Garrison would share 0°
on two thirds of all islands. But the *reason* it fires is F1: atolls have no
ground above 31.5 m, and fjorded islands have almost none above 189 m.

So the "third readable office signal" (§10.1) is currently synthetic on two
characters out of three. Fixing F1 fixes this too.

---

## F4. A8's sheet budget is unreachable by two orders of magnitude

Measured: **5117 ms** to re-contour one 1:5000 sheet. Budget: **50 ms**.

This is arithmetic, not a slow implementation:

```
field cost                 301 ns per Height01 sample (5-octave warped fbm, ~3 fbm evaluations)
1:5000 sheet, lod 6, 1 m   2570 x 3805 = 9.8 M samples
                           9.8e6 x 301 ns = 2.9 s of pure field evaluation
```

The 50 ms budget implies ~5 ns per sample, which no warped multi-octave fbm will
ever reach. The mismatch is structural: **marching squares costs the sheet's
AREA, while the budget was written as if it cost the coastline's LENGTH.**

Levers, in order of leverage:

1. **Hierarchical extraction.** Contour coarsely, then subdivide only cells whose
   corners bracket the level (plus a Lipschitz band so thin features are not
   missed). Cost then scales with contour length, which is what the budget
   assumed all along. This is the real fix and it is a day of work.
2. **Coarsen the paper-detail target.** `0.25 mm/cell` is very fine. `0.5 mm`
   is 4× cheaper and still finer than a drawn line. Gets 2.9 s to 0.7 s — not
   sufficient alone.
3. **Field cost.** 301 ns is not unreasonable for 5 octaves plus a two-component
   warp, but the warp triples it. Dropping the warp to a single shared evaluation
   would roughly halve it.

Note the island view is fine: **116.8 ms** for the whole island including
features and lod-1 contours, inside the 250 ms budget. Only the per-sheet path
is broken, and only at 1:5000.

---

## F5. Garrison is half grid-and-coast — A5b answers its own question

A5b (added by D4 so that A5's pass would not be vacuous) reports:

| office | sheets whose only content is coast and/or grid |
|---|---|
| Hydrographic | 0.0% (0/178) |
| Land Survey | 0.0% (0/16) |
| **Garrison** | **48.6% (34/70)** |

D4 predicted this exact conversation: *"If Garrison comes back at 60%, that is
the evidence-backed answer to whether the office is a chore."* It came back at
48.6%. Nearly half of Garrison's sheets show a grid, a scrap of coast, and
nothing else.

This is not a bug — a garrison grid sheet of empty moorland is a real document,
which is why A5 counts the grid. But it is a design signal, and it should be read
alongside F1: Garrison's block covers a quadrant of the land bbox, and per F1
most of that quadrant is sea.

---

## F6. Two defects in the spec's own formulas, found by building them

**F6a — the Fjorded falloff is discontinuous at θ = ±π.** §5.3's
`cut = 0.18 * fbm1(theta * 6.0)` takes `theta` from `atan2`, which branches on the
negative X axis, and `Fbm1` is not periodic — so `fbm1(-6π)` and `fbm1(+6π)` are
unrelated draws. Measured jump across the ray:

```
max |dh01| across theta = ±pi : 0.0301   at r = 3800 m
typical |dh01| over 1 m elsewhere : 0.000229
```

131× a normal one-metre step. Every fjorded island carries a radial seam where
the coastline can jump. **Fix:** give `Fbm1` an integer lattice period dividing
the circle (evaluate at `(θ/2π + 0.5) * P` with the lattice index taken mod `P`,
`P = 36`), which needs a period argument on `Fbm1`. This changes the recipe, so
it is a spec edit, not a silent repair — it was implemented literally as written
and documented in the source.

**F6b — §6.2's LOD table contradicts its own formula.** For 1:25000,
`targetGroundCell = 6.25 m` and `ceil(log2(64/6.25)) = ceil(3.356) = 4`, giving a
4 m cell. The table's row says lod 3, cell 8 m — that is `floor`, and the 1:5000
row uses `ceil`. The formula is self-consistent and was followed; **the table row
is wrong**. Note this doubles the sample count for every 1:25000 sheet, which
feeds directly into F4.

---

## F7. The debug window had to cap contour detail to stay usable

A consequence of F4 rather than a finding in its own right, but it changes what
the window shows and so it must be recorded.

At the LOD §6.2 prescribes, a 1:5000 sheet is ~10 M field samples, so a pane that
re-contoured on demand would hang for seconds per repaint. The window therefore
applies an explicit **sample budget of 500 000 per level set**, lowering the LOD
until the extraction fits. Contour *density* is untouched — levels stay at the
50 m elevation step — only line smoothness degrades.

So the Compare pane currently shows coastlines at lower fidelity than a real
sheet would carry. That is fine for A1, which asks whether two offices produce
different *documents*, not whether a coastline is smooth. It stops being fine the
moment sheets are rendered for real. F4 and F7 are the same problem and should be
fixed together.

Two smaller notes from the same work:

- **A6 is not in the §11 stats footer.** §11's own list does not include it and
  it is O(n²) in sheet pairs. It is computed by the headless harness instead,
  which is where the 96.5% above comes from. The footer would need a sampled
  estimate rather than the exact figure.
- **Coverage percentages needed definitions §11 never gave.** They are now
  computed on a 96×96 land lattice: coastal = a land sample with a sea
  neighbour; `coast ×3` = coastal samples covered by all three offices;
  `gaps` = land at zero coverage. The whole-island sheet is excluded from
  coverage counting, since it covers everything and would flatten the picture.

---

## F8. There is no unsurveyed ground — R1.8 is not satisfied

Found while accounting for the sheet totals, and it is the one finding here that
is about the *design* rather than the numbers.

Coverage measured on a 40 m lattice over the land, counting how many detail
surveys cover each point:

| island | land | 1 survey | 2 | 3 | 4 | 5 | 6 | 7+ | **0 (gap)** |
|---|---|---|---|---|---|---|---|---|---|
| Mountainous | 25.5 km² | 4.5% | 25.3% | 29.7% | 22.4% | 10.9% | 4.7% | 2.5% | **0.0%** |
| Fjorded | 15.0 km² | 2.1% | 20.4% | 36.4% | 21.0% | 12.4% | 5.1% | 2.6% | **0.0%** |
| Atoll | 1.9 km² | 20.1% | 47.5% | 25.0% | 4.0% | 2.0% | 1.3% | — | **0.0%** |

Every square metre of land is covered by at least one sheet, and typically by
three. But R1.8 says:

> **R1.8** No island is ever fully surveyed. Some ground has no sheet covering it
> at all.

and §10.3's own coverage picture claims `remote / featureless — covered ×0 ←
R1.8 satisfied`. It is not satisfied. It never was: §7.4 argued that R1.5 and
R1.8 "collapse into one mechanism", with unserved ground going uncut — but
Hydrographic keeps every rect the coastline crosses, and on an island this size
the coast is never far from anything, so the ring alone blankets the interior.

This matters beyond bookkeeping. R6.9 says a board can be *worked out* but never
*filled*, and the trust arc in §4.4 of `requirements.md` depends on the composed
map staying incomplete. With 0% gaps, an island can be finished exactly, and one
of the design's load-bearing properties quietly disappears.

Fixing it is not a tuning tweak — the service cull cannot produce gaps while
Hydrographic tiles the whole shore. The honest levers are a Hydrographic arc that
covers only part of the coast (which D2 removed for being circular, but could
return as a seeded arc rather than a derived one), or an explicit
survey-extent rule per office. Worth deciding before the map table is built.

---

## 7. What to decide next

In dependency order. F1 is upstream of almost everything.

1. ~~**F1 — make islands bigger, or sheets smaller.**~~ **DONE, in two steps.**

   **Step 1 — detail scale 1:5000 -> 1:2500** (`Tuning.DetailScaleDenominator`).
   Fixed the sheet economy and Land Survey's starvation, but took the mountainous
   median to 58 sheets per island: more sheets than the player should have to
   track, and the extra ones carried no new information — the same ground cut
   smaller. Footprint reached 6x the land area, with the average square metre
   appearing on four sheets.

   **Step 2 — scale is now PER OFFICE** (`Tuning.CoastalScaleDenominator`):
   Hydrographic 1:5000, the terrain offices 1:2500. Nothing in R2.2 tied surveys
   to a shared scale — that was an implementation default, not a requirement, and
   R2.3's "three or four fixed values" now reads 2500 / 5000 / 25000 / 50000.
   Hydrographic was the whole problem: it keeps every rect the coast crosses, so
   it alone produced 31 of 56 detail sheets on one island. At 1:5000 it produces
   12. It also earns a fourth office signal — scale readable off the sheet,
   alongside style, rotation and coverage — and it is what a coastal
   reconnaissance actually was: small-scale work, where a terrain survey is not.

   Measured after both steps, over 50 seeds:

   | | 1:5000 uniform | 1:2500 uniform | per-office |
   |---|---|---|---|
   | Mountainous median | 13 | 58 | **38** (31-43) |
   | Fjorded median | 11 | 29 | **19** (12-25) |
   | Atoll median | 14 | 27 | **16** (9-23) |
   | Land Survey sheets / 20 islands | 16 | 101 | **101** |
   | A6 shared-class | 96.5% | 99.2% | **99.7%** |
   Not fixed by any of it: atoll Land Survey is still zero (§5.3 says that is
   correct), F3's rotation fallback is unchanged, Garrison's thin-sheet rate got
   *worse* — 48.6% -> 71.3%, because smaller sheets fall more often on empty
   ground inside the block — and **F8's zero gaps are untouched**, because
   Hydrographic still tiles the entire shore, just with larger sheets. F8 is new
   and was exposed by the same measurement.

   The remaining levers on sheet count, unmeasured: overlap 20% -> 10% (R2.5
   allows 10-25%), worth roughly 21% fewer sheets everywhere; and shrinking
   Garrison's block below a full quadrant, which would cut the least informative
   sheets first.

   Note the grid pitch had to be decided for the new scale. D4 gave 1000 m at
   1:25000 and 200 m at 1:5000 — both exactly 40 mm on paper — so the rule was
   always a paper-space one and is now stated as `Tuning.GridPitchPaperMm = 40`,
   reproducing both of D4's values and giving 100 m at 1:2500.
2. **F4 + F7 — decide whether A8's 50 ms is a real requirement.** If sheets are
   rendered on demand while the player holds one, it is. If they are rendered
   once and cached per session, it is not, and hierarchical extraction can wait.
   Either way the debug window is capping detail until this is settled.
3. **F6a — fix or accept the fjord seam.** It is visible, and it is one signature
   change.
4. **F6b — correct the §6.2 table row.** One cell of one table.
5. **F5 and F3 — no action yet.** Both are reported metrics whose values are
   dominated by F1. Re-measure after F1 rather than tuning them directly.
