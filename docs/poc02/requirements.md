# POC-02 — Island Colour Rendering · Requirements

What the second POC must do and why. Companion spec: `spec.md` (construction).
Upstream authority: `../requirements.md` (game), `../poc01/poc-01-island-and-surveys.md`
(generator intent), `../poc01/poc-01-decisions.md`, `../poc01/poc-01-findings.md`,
`../generation_for_agents.md` (generator as built).

---

## 1. Purpose

POC-01 proved the island is a function and that surveys cut from it are truthful,
different documents. It drew everything in **one neutral line style, black on
white** (§8.2), deliberately, so that any difference the eye found was a
difference of *content*.

POC-02 lifts that constraint in one direction only: **colour**.

It builds a deterministic renderer that turns an island — or any rect of one —
into a coloured raster: the ground seen from above, in a simple placeholder
style. That raster is the base layer every later sheet is drawn on top of.

### 1.1 What POC-02 is NOT

**It is not §5.4's proof 1.** `../poc01/poc-01-island-and-surveys.md` §17 defers
*"office style — ink, paper, typography, wear (R2.6)"* to "POC-02", and §5.4
frames that as *"can a player read office style at a glance?"*. That proof still
matters, but it needs art direction that does not exist yet, and it needs a base
render to sit on. **This POC is that base render.** Office style becomes POC-02b
or later; this document supersedes §17's assignment.

Consequently POC-02 has **no office style at all**: one visual treatment, applied
to every render, whoever drew the sheet.

---

## 2. Requirements

### 2.1 Generation model

- **T1.1** The unit of rendering is an **arbitrary ground rect at an arbitrary
  rotation and resolution**. The island overview and a single sheet are the same
  call with different arguments. This mirrors §3 of POC-01 — the island is a
  function, and rendering is a query over it, not a build step.
- **T1.2** Resolution is a **caller parameter** (`pixelsPerMetre`), never fixed
  in the renderer. A paper-space helper derives it for sheets, so one setting
  gives every office the same detail *on paper* regardless of scale.
- **T1.3** Rendering is **deterministic**: identical inputs produce a
  byte-identical image, across runs and sessions. Same rules as §4 of POC-01 —
  no `System.Random`, no wall-clock, no dictionary iteration order, and no
  transcendental in a per-pixel path.
- **T1.4** Nothing rendered is persisted as source data. Only the seed and the
  render request are needed to reproduce an image (R3.1, R1.11).
- **T1.5** The renderer consumes the **existing** generator output. It adds no
  new geometry and must not perturb generation in any way.

### 2.2 Colour model

- **T2.1** Land colour is a **banded hypsometric fill**: one field sample per
  pixel, elevation mapped to a discrete band, band to a colour. Bands are
  discrete, not a continuous ramp — this is a map, not a terrain render, and hard
  band edges are correct rather than a limitation.
- **T2.2** Land bands are **normalised per island** against that island's own
  highest peak. Every island therefore uses the full ramp and reads as varied,
  including a 90 m atoll.
  - **T2.2a** Accepted cost, recorded deliberately: **colour does not encode
    absolute height across islands.** The same green is 90 m on an atoll and
    600 m on a mountain. If sibling islands (R1.10) are ever built, colour will
    not help tell them apart.
- **T2.3** Sea bands are **absolute**, in metres. `Tuning.MaxDepth` is a global
  220 m for every character, so depth is already comparable across islands and
  normalising it would destroy the only globally meaningful colour axis.
- **T2.4** One **global palette** for all islands in this POC. The renderer must
  resolve colour through a per-island seam so that seed-derived tints can be
  added later without restructuring.
- **T2.5** Vector features are stroked over the fill: coastline, rivers,
  settlements, peaks, soundings. Drawn or omitted per the caller, not per office
  — POC-02 has no office matrix.
- **T2.6** **No text.** Settlements, peaks and soundings are marks, not labels.
  Typography is office style (R2.6) and is deferred with it.

### 2.3 Output

- **T3.1** The renderer emits a plain **RGBA32 byte buffer** with dimensions —
  no engine types. This is the layout a GPU texture upload takes directly, so
  the in-game path is a copy with no decode.
- **T3.2** The rendering assembly, like `Archivist.Generation`, **must not
  reference `UnityEngine`**. That is what keeps the acceptance suite headless.
- **T3.3** A PNG writer exists for **debug inspection and run-to-run diffing
  only**, never on a runtime path.
- **T3.4** The output format must be usable unchanged as the in-game map and
  sheet texture. The player will unroll maps and inspect sheets in first person,
  so the buffer must suit mipmapping and anisotropic filtering.

### 2.4 Performance

- **T4.1** Generation time is a **primary concern**, not a footnote. Neither the
  island overview nor a sheet is deeply zoomable in play, so detail beyond
  recognisability is waste.
- **T4.2** The renderer must be **precise enough to be recognisable by eye** —
  that is the quality bar, not fidelity to the field.
- **T4.3** The resolution at which T4.2 holds is a **finding, reported by a
  sweep**, not a constant asserted up front. Same posture as §13.7, which
  measured sheet economy rather than assuming §6.1's guess.
- **T4.4** Per-pixel work is order-independent and must stay so, so that
  parallelism is available without touching T1.3.

### 2.5 Inspection

- **T5.1** Renders are inspectable in the Unity Editor beside the existing
  island and sheet views, and exportable to disk.
- **T5.2** The tooling must support the primary criterion directly: an island
  overview and a sheet of that island, side by side, at their own resolutions.

---

## 3. Acceptance

**Primary, human-judged.** Render one island overview and one sheet covering part
of it, side by side. **Pass:** a viewer can locate the sheet's ground on the
overview unaided. **Fail:** the sheet could be anywhere.

This is the direct test of T4.2, and it fails loudly for the right reasons —
mush, wrong normalisation, or a coastline stroke that does not sit on the fill's
water edge.

**Automated, gating:** byte-identical determinism; colour coherence between two
overlapping renders of different rect, rotation and resolution; a stated
performance budget.

**Reported, not gated:** the resolution sweep (T4.3), and render cost per
character.

---

## 4. Out of scope

| deferred | to |
|---|---|
| Office style — ink, paper, typography, wear (R2.6) | POC-02b |
| Text and label placement | with office style |
| Paper stock, wear, fold, condition blending (R3.3) | v1 |
| The three LODs — pile / in-hand / stored (R3.2) | v1; they need the archive to judge |
| Hillshade / shaded relief | later; costs 2-4 extra field samples per pixel |
| Seed-derived palette tints | later; the seam exists from the start (T2.4) |
| Era as a drawing convention (R1.6) | post-v1, unchanged |

---

## 5. Open questions

1. **What resolution is "recognisable"?** The reason T4.3 measures rather than
   asserts. Everything else is sized off the answer.
2. **Do hard band edges read as a map or as a contour-fill artifact?** T2.1
   assumes the former. If they read badly, the fallback is a narrow dither or
   blend at band boundaries, which costs nothing extra in field samples.
3. **Does the normalised ramp make atolls look *wrong* rather than merely
   varied?** T2.2 trades cross-island meaning for within-island legibility; only
   looking at output settles whether that was the right trade.
4. **Is one field sample per pixel enough at low resolution?** At 10 m/px a
   pixel spans a lot of ground and thin features (an isthmus, a lagoon mouth)
   may alias out. Supersampling the fill is the fix and costs linearly.
