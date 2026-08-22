# POC-02 — Measured Findings

Companion to `requirements.md` (intent) and `spec.md` (construction). This one
records what the renderer, once built, actually does — and the four places the
spec was wrong, too slow, or blind.

**Status: F-02.1, F-02.2 and F-02.3 acted on; the rest measured.** Every number
below comes from the headless harness or a probe against the real code, not from
reasoning.

Machine: 8 cores. Reference sheet: 1388×2055 px (2.85 Mpx) at 0.93 m/px,
Land Survey 1:2500.

---

## 1. Where POC-02 stands

| check | result |
|---|---|
| B2 determinism | **PASS** — 100 renders bit-identical, including under `Parallel.For` |
| B3 coherence | **PASS** — 100%, but weaker than it looks; see below |
| B4 performance | reported (§F-02.2) |
| B5 resolution sweep | reported — PNGs in `Tools/GenHarness/out/poc02` |
| B1 same place, two scales | **not yet judged** — needs a human at the Texture tab |

**B3's pass is weaker than it looks.** Its own guard fired: *"1 distinct bands
appear in the overlap — WEAK: too few bands here for this to prove much"*. The
sample region landed on uniform water, so 100% agreement is close to vacuous.
What *is* meaningful is the companion assertion, which held: every sampled pixel
carries exactly `palette[Bands.Index(...)]` for its own ground sample, so the
fill is provably faithful to the field. Before B3 says anything about
*coherence*, its region must straddle a coast.

---

## F-02.1. The coastline cost more than everything else combined

**Found:** one sheet took **6090 ms**, of which the fill was 758 ms. The
coastline stroke was ~87% of the total.

Cause: §7's LOD rule extracted contours at ~1 pixel per cell across the whole
sheet — about 9.8 M cells. That is POC-01's F4 arriving by a new route:
*marching squares costs the sheet's AREA, not the coastline's LENGTH.*

**Two fixes, both applied.**

1. **LOD overshoot.** `RenderLod.ForGroundCell` halved until the cell was
   strictly finer than the target, so a 0.93 m pixel took a 0.5 m cell — 4× the
   cells for a difference no eye can find. Now it takes the nearest rung
   (accept ≤ target × √2). *6090 → 2146 ms.*

2. **The coastline stopped being a stroke.** The fill already samples `h01` at
   every pixel, and the coast *is* the `h01 == SeaLevel` isoline of those very
   samples. `FieldCoast` derives it by signed distance: `(h01 − SeaLevel)`
   over the local gradient magnitude gives a distance in pixels, driving the same
   coverage ramp the vector strokes use. **Zero field evaluations.**
   *2146 → 798 ms.*

Fix 2 is also strictly more correct. §7's LOD rule existed to stop the stroke
drifting off the fill's water edge; now the line is *defined by* that edge, so
the failure mode is gone rather than guarded against. Rivers, settlements, peaks
and soundings stay vector — they are discrete features, not isolines.

| | time | coast's share |
|---|---|---|
| original | 6090 ms | ~87% |
| + LOD fix | 2146 ms | ~60% |
| + field-derived coast | **798 ms** | **~3%** |

---

## F-02.2. Approximation is nearly free, because the field is smooth

After F-02.1 the render was **100% field evaluation**, one `Sample` per pixel at
~275 ns. The sheets are hand-drawn surveys, so micro differences are in fiction —
which licenses approximation.

**Measured on the reference sheet:**

| approach | time | speedup | accuracy cost |
|---|---|---|---|
| baseline (serial, 1 sample/px) | 800 ms | 1.0× | — |
| parallel, 1 sample/px | 256 ms | 3.1× | **none — bit-identical** |
| serial, sample every 2px | 331 ms | 2.4× | max abs dh01 0.00003 |
| serial, sample every 3px | 124 ms | 6.5× | 0.00004 |
| serial, sample every 4px | 60 ms | 13.4× | 0.00008 |
| serial, sample every 6px | 30 ms | 26.8× | 0.00016 |
| serial, sample every 8px | 23 ms | 35.0× | 0.00029 |
| parallel + every 2px | 40 ms | 20.1× | 0.00003 |
| **parallel + every 4px** | **12 ms** | **68.8×** | 0.00008 |
| parallel + every 8px | 5 ms | 174.1× | 0.00029 |

It works because the coastline's characteristic wavelength is
`Tuning.FeatureScale` = 2600 m, while every-4px on this sheet is every 3.7 m.
Bilinear interpolation of a function that smooth reproduces the isoline to well
under a pixel.

**Thin land is the risk, so it was measured separately.** Land/sea
classification flips, as a percentage of each island's own land pixels, at a
0.20 px/m overview:

| island | every 2px | every 4px | every 8px |
|---|---|---|---|
| atoll (thin ring) | 0.066% | 0.289% | **1.039%** |
| fjorded (deep inlets) | 0.089% | 0.336% | 0.875% |
| mountainous | 0.025% | 0.115% | 0.463% |

The atoll is the sensitive case, as expected. At every-4px it loses 202 of
75,846 land pixels and the ring — including its smallest islets — is visually
intact. At every-8px it starts eating the ring.

**Adopted: parallel + every 4px**, bounded twice
(`RenderTuning.FieldSampleStepPx = 4`, `FieldSampleCeilingM = 24`):

- **by pixels**, so error stays sub-pixel in the image;
- **by ground metres**, so thin land cannot survive at one resolution and vanish
  at another. 24 m is ~1/100 of the coastline wiggle wavelength.

At low resolution the ground ceiling takes over and the step falls back toward 1,
which costs nothing because those images are small anyway.

**Measured after adoption** (all layers, warm):

| island | overview | sheet |
|---|---|---|
| mountainous | 1267×1165 — 64 ms | 1388×2055 — 200 ms |
| atoll | 1293×1485 — 63 ms | 1388×2055 — 32 ms |
| fjorded | 1024×934 — 15 ms | 1388×2055 — 56 ms |

**798 ms → 32–200 ms per sheet**, and 6090 ms → 32–200 ms against where POC-02
started. Determinism holds under `Parallel.For`, verified rather than assumed:
pixels are independent and write to their own slots, so the result is
bit-identical regardless of scheduling.

### Two consequences to remember

- **Recorded hashes change.** B2 tests reproducibility, not a golden image, so
  this is harmless — but any hash stored before this change is stale.
- **The raster coastline now differs very slightly from `Contours.Extract`'s
  vector coastline.** Harmless for display: the raster coast derives from the
  raster's own samples and is self-consistent with its own fill. Worth
  remembering when the map table (POC-03) fits sheets against the *true* island
  (R6.7).

---

## F-02.3. `ForSheet` rendered the wrong ground

`RenderRequest.ForSheet` normalised the sheet's frame rect to `(0, 0, W, H)`,
discarding its position. `GroundImage` derives its origin from `Area.MinX/MaxY`
and rotates about the ground origin, so **every sheet rendered a correctly-sized,
correctly-rotated rectangle of ground near the domain origin** — not the sheet's
own. B1 could never have passed.

Found independently by two of the parallel implementers before any integration
run, which is the argument for briefing them to report ambiguities rather than
paper over them. Fixed: the frame rect passes through unchanged.

---

## F-02.4. The atoll's lagoon is indistinguishable from open ocean

Sea bands are absolute (T2.3) and an atoll lagoon is deep, so it lands in the
same band as the surrounding ocean. The atoll's most characteristic feature —
that it *encloses* water — is invisible in colour.

Not a bug: T2.3 is deliberate, and normalising the sea would destroy the only
globally comparable colour axis. But it is a real legibility gap, and the fix is
not a band edge. An enclosed-water test — a flood fill inward from the image
border, marking sea unreachable from outside — would give lagoons their own tint
in one cheap pass over the raster, with no extra field cost.

---

## 5. Open

1. **B1 has not been judged.** It needs a human at the Texture tab. Everything
   else here is automated; this one is not, and it is the primary criterion.
2. **What resolution is recognisable?** B5's ladder is exported and unexamined.
   Open question 1 in `requirements.md` stays open until someone looks.
3. **B3 needs a coast-straddling region** before its 100% means anything.
4. **Field cache across sheets, not built.** Sheet footprint is ~6× the island's
   land area (POC-01 F1), so most field evaluations are duplicated between
   overlapping sheets. One island-wide `h01` grid at ~4 m (≈12 MB) built once
   would make every later sheet pure interpolation. Composes with F-02.2; worth
   doing when the game renders many sheets per island.
5. **GPU was considered and not taken.** The field ports cleanly to a compute
   shader and a sheet would be 1–5 ms, but it breaks the engine-free assembly
   (T3.2) and with it the headless tests; GPU floats are not bit-identical across
   vendors, so T1.3 would weaken to deterministic-per-machine; and the field
   would exist twice, free to drift. Revisit only if textures are ever needed
   per frame rather than per sheet.
