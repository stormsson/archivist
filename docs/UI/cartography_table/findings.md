# The Cartography Table — Findings

Measured, not reasoned. `requirements.md` is intent, `spec.md` is construction,
this is what the thing actually did. Where a finding contradicts the spec, the
finding wins and the spec is amended with the disagreement recorded.

Numbers below are from **island index 0** of collection seed `905386350` —
*Driftcombe*, `743A6763368B6692`, character Mountainous — measured twice: once
headless against `Archivist.Generation`, once in the editor through
`Archivist ▸ Cartography Table · Lay Solved Board`. Both agree.

---

## S1 — a solved board

**Slice S1 passes.** This was the one thing in the design that could not be
reasoned about, only looked at: whether sheets drawn at their *ground* footprint,
overlapping by a fifth, read as an island or as a heap.

### F-S1.1 — the sheets assemble into one island

48 sheets laid at their true poses produce a single continuous island. The
**coastline runs unbroken across sheet boundaries**; terrain, rivers and relief
are continuous. There is no visible seam that is not a sheet edge.

This is the finding that unblocks every later slice. C1.2 (ground-space board)
and D-C5 (the mockups' clean grid is impossible) both survive contact.

### F-S1.2 — the rotation convention is correct

Ground X → board X and ground Y → board Z, with the Unity yaw **negated**
(`Quaternion.Euler(0, -(float)sheet.RotationDeg, 0)`). Verified by outcome rather
than by argument: a sign error here produces a board that is individually
plausible and collectively scrambled or mirrored, and the assembled island is
neither. Spec §3.2's warning stands, and the code comment is load-bearing.

### F-S1.3 — the board's dimensions

| | |
|---|---|
| land bounds | 6.91 x 6.27 km |
| board, at `BoardUnitsPerMetre` 0.01 | **80.2 x 73.8 units** |
| sheets on a solved board | **49** (48 + the whole-island sheet) |

### F-S1.4 — what each office contributes

| office | sheets | scale | paper | ground / sheet | board / sheet | rotation |
|---|---|---|---|---|---|---|
| Hydrographic | 13 | 1:2500 | 380 x 200 | 875 x 425 m | 8.8 x 4.3 | **13 distinct** (D-H2) |
| Land Survey | 16 | 1:2500 | 841 x 594 | 1903 x 1285 m | 19.0 x 12.9 | 73.4°, one per survey |
| Garrison | 12 | 1:2500 | 594 x 841 | 1285 x 1903 m | 12.9 x 19.0 | 0.0°, one per survey |
| Antiquarian | 7 | 1:1250 | 250 x 250 | 275 x 275 m | 2.8 x 2.8 | **7 distinct** |
| whole-island | 1 | 1:25000 | 841 x 594 | 19025 x 12850 m | **190.3 x 128.5** | 0.0° |

The two lattice offices cross at roughly 73°, which is the "three offices
disagree" thesis made visible. Hydrographic is ribbons following the shore, not a
grid — exactly as D-C5 predicted and nothing like mockup `1c`.

### F-S1.5 — how deep the paper piles

Sampled on a 200 x 200 grid over the land bounds, land points only:

| | |
|---|---|
| land covered by >= 1 sheet | **100.0%** (24 892 of 24 892 samples) |
| sheets over a covered point | median **3**, mean 3.1, max **9** |

Median 3 is the number that makes the board legible. It is layered, not buried.

---

## Findings that need a decision

### F-S1.6 — the whole-island sheet cannot be a placeable tile

At 1:25 000 on A1 it covers **19.0 x 12.9 km for a 6.9 km island** — 564% of the
land area, and **2.4x the board's own width**. Drawn at true ground scale it does
not sit on the board; it blankets it and overhangs every side.

R6.8a calls it "the board's outline", and at these numbers that cannot mean a
sheet the player positions. It reads as an **underlay** — drawn beneath
everything, non-interactive, not listed in the cabinet with the others.
`CartographyBoardBench.QuickLay` therefore defaults it **off**, with the reason
recorded at the call site.

**Unresolved.** Spec §7 still lists it as an ordinary cabinet row.

### F-S1.7 — the sheets do not look like the mockups

The board is geometrically right and stylistically wrong. `IslandRenderer` is
drawing `LayerMask.All`, which includes `Fill` — so every sheet is a filled
colour relief map, greens and browns and blue water. The mockups
(`1a`, `1b`, `1c`) show **pale paper with fine ink line-work**: contours, a grid,
a coastline, and almost no fill.

This is a `RenderRequest` layer and palette decision, not a geometry one, and it
changes how every sheet on the board reads. Nothing in `spec.md` currently says
which is intended.

**Unresolved.**

### F-S1.8 — R6.9 is contradicted by the generator

R6.9: *"Full coverage of an island is impossible by design. A board can be worked
out but never filled."*

F-S1.5 measures the union of all sheets covering **100%** of the land. If every
sheet can eventually be issued, the board fills. Either R6.9 is aspirational and
unimplemented, or issuance must withhold something deliberately — and which of
those is true decides whether a "complete" board is a state that exists at all.

**Unresolved.** It needs settling before S5 (snapping), because it decides
whether seating the last sheet is a moment the game recognises.

---

## Method

- Headless: `Archivist.Generation` compiled standalone against the real sources;
  island generated, surveys enumerated, coverage sampled. No engine types, so the
  numbers are the generator's and not the renderer's.
- Editor: `Archivist ▸ Cartography Table · Lay Solved Board`, which drives the
  shipping path — `MapCrate.Render` for the raster and `BoardSheetView.Create`
  for the slab. Nothing in the bench is a parallel implementation, so a bench
  that works is evidence the product will.
