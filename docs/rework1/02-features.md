# Rework 1 — What Gets Built

Construction. `../quarters/requirements.md` (**Q**-numbers) is intent; this is
the work, numbered **W**n. `01-removal.md` is what comes out first.

Each item states its rule, its shape, what "done" means, and what it waits on.
Where a decision is missing, it says so rather than guessing.

---

## W0 — A compile check for `Archivist.Building` — **DONE**

`Tools/check-building.sh`. Verified both ways: passes on a clean tree, and a
deliberate `CS0246` in `Building/Runtime` fails it. ~25 s.

**Enabler, and it goes first.** `Tools/check-editor.sh` compiles Generation,
Render, Editor and Tests. `Archivist.Building` — where 14 deletions and 40-odd
cross-references land — is type-checked nowhere but the Unity editor.

Extend the script (or add `Tools/check-building.sh`) with a throwaway csproj over
`Building/Runtime/**` and `Building/Editor/**`, referencing
`<UnityManaged>/UnityEngine.dll`, `UnityEditor.dll`, **every**
`<UnityManaged>/UnityEngine/UnityEngine*.dll` module (without the modules,
anything touching UGUI fails with "CoreModule missing"), plus
`Library/ScriptAssemblies/Unity.InputSystem.dll`, `Unity.InputSystem.ForUI.dll`
and `UnityEngine.UI.dll`.

**Trap:** `Archivist.Building.Placement` is a *namespace* (the room's anchors),
so inside `Archivist.Building.*` the bare name `Placement` never resolves to the
board's struct and a file-scope `using` alias does not win. Qualify.

**Done when** a deliberate type error in a `Building/Runtime` file fails the
script.

---

## W1 — The quarter cutter — **DONE, with one open decision**

`Generation/Sheets/QuarterCutter.cs` (233 lines) replaces `SurveyCutter` (931),
`CoastWalkCutter` (390) and `RectCull` (86). A2, A3, A4, A5, A6 and C2–C4 pass;
A8's re-contour check does not, for a reason that is arithmetic rather than a
bug — see `03-findings.md` R2 and R3, which need an answer before W2.


*Q1.1–Q1.6. Assembly: `Archivist.Generation`. Engine-free.*

Replaces `SurveyCutter`, `CoastWalkCutter` and `RectCull` with one function.

```
QuarterCutter.Cut(island, office) -> Survey        // 4 sheets
QuarterCutter.CutChart(island, office) -> Survey   // 1 sheet, whole island
```

**The cut.** `island.LandBounds` (already computed once in `Island.FromSeed`, via
`IslandField.ComputeLandBounds`) is the input. Halve it on both axes: four rects,
axis-aligned, no rotation, no overlap, no cull. Quarter order NW, NE, SW, SE →
`Sheet.Number` 1, 2, 3, 4, so A4's contiguity holds for free.

**The scale.** One `SheetFormat`, fixed (Q1.5). Choose the smallest denominator
from the existing set at which one quarter's ground extent fits the map area:

```
quarterGround = max(LandBounds.Width, LandBounds.Height) / 2
denominator   = min { d in Scales : quarterGround <= GroundMetres(MapWidthMm, d)
                                 && quarterGround <= GroundMetres(MapHeightMm, d) }
```

A small island therefore sits in blank margin — that is the intent, not a defect,
and A5b's "thin sheet" metric must be taught the difference (`01-removal.md` §4.1).

**The chart.** One per island, not per office (Q2.3). The office that makes it is
drawn from `StreamNames.WholeIsland`, as today. The existing whole-island
fallback denominator stays: an island that does not fit at 1:25 000 uses 1:50 000.

**Determinism.** The cut is a pure function of `LandBounds`. No sub-stream is
consumed and none is added. `StreamNames.CoastRegion` becomes reserved.

**Done when** `IslandDigest` is stable across runs, A2/A4 pass, and every island
in a 200-seed sweep yields exactly `offices × 4 + 1` sheets with the land bounds
covered exactly once per office.

**Waits on:** the Antiquarian decision (`01-removal.md` §5).

---

## W2 — An office is a set of layers — **DONE**

`OfficeLayers` bridges `FeatureMatrix` to `LayerMask`; `Fill` is off; contours and
the grid are drawn; the order is R10's. All 13 gated checks pass, and three
offices' plates of one quarter are visibly three documents. What it took, and the
two things it needed that were not in the plan, are in `03-findings.md` R13.


*Q2.1, Q2.2. Assemblies: `Archivist.Render`, `Archivist.Generation`.*

Today `MapCrate.Render` calls `RenderRequest.ForSheet(sheet, pixelsPerPaperMm)`
and takes the default `LayerMask.All`. Every sheet in the game is drawn
identically. This is the change that makes an office mean something.

**The mapping already exists, on the wrong side of the fence.**
`Generation/Sheets/FeatureMatrix.cs` is the office × `FeatureClass` table:

```
                 Coast  Contour  Peak   River  Settle  Grid   Sound  Poi
Hydrographic     true   false    false  false  true    false  true   false
LandSurvey       true   true     true   true   true    false  false  false
Garrison         true   false    true   false  false   true   false  false
Antiquarian      true   true     true   true   true    false  false  true
```

It is consumed only by editor tooling (`VectorDraw`, `SheetContent`, `SheetPane`,
`ComparePane`, `SvgExport`). **Read that tooling before writing this** — it is
the closest thing to a working office-layer model in the repo.

**Work:**

1. `LayerMask` gains `Contours` and `Grid`. It has `Fill, Coast, Rivers,
   Settlements, Peaks, Soundings`; `FeatureClass` has `Contour`, `Grid` and `Poi`
   with no raster equivalent.
2. A `FeatureClass → LayerMask` bridge, so one table drives both the vector
   (editor) and raster (runtime) paths. One table, two consumers — not two tables.
3. `Fill` off on every plate (Q2.2). This settles **F-S1.7**.
4. Draw contour lines. `Strokes.DrawCoast` draws the *sea-level isoline* only;
   there is no intermediate-elevation contour drawer. `Contours.Extract` and
   `RenderLod.ForPixelsPerMetre` give the extraction and the LOD ladder; the
   drawing and a width constant in `RenderTuning` are new.
5. Draw the grid. `GarrisonGrid.cs` generates the lines from a rect and is
   currently editor-only.

**Trap:** `Strokes.Draw`'s order — coast → rivers → settlements → peaks →
soundings — is documented as load-bearing for the acceptance hash. Inserting
layers changes it. Decide the new order once, and record it.

**Done when** the same quarter rendered as Hydrographic and as Land Survey
produces two visibly different rasters, headlessly, with no `Fill` in either.

**Waits on:** nothing. Can start immediately, in parallel with W1.

---

## W3 — Per-office style — **FIRST PASS DONE**

`Render/OfficeStyle.cs` — paper, inks and one weight scale per office. Costs
nothing to render (F-R16.5). The values are art direction and are settled by
looking, not by a check.


*Q2.6, and §5.4's first proof. Assembly: `Archivist.Render`.*

**The largest piece of new work in the rework, and the riskiest.** There is no
per-office style anywhere: `Palette.Global` is one 12-entry placeholder used for
every island and office, `Ink` and `Bands` take no office parameter,
`RenderTuning`'s stroke widths are global constants, and `SheetSpawner.paperTint`
is one serialised colour. `Editor/OfficeStyle.cs` is debug-window chrome and says
so.

Under Q1.2 every office draws identical geometry. **Style is now the only thing
that separates one office from another** — at pile distance, in hand, and when
`Q`/`E` flips a layer. §5.4 already called this v1's first proof; the rework
makes it load-bearing rather than important.

**Work:** an office style record — paper stock colour and grain, ink colour,
stroke weight multipliers, mark shapes, typography — threaded through `Palette`,
`Ink`, `RenderTuning` and `SheetSpawner.paperTint`. `Palette.ForIsland` already
has the seam (`Streams.For(seed, StreamNames.Palette)`, reserved, deliberately
unimplemented); this is per *office*, not per island, so it does not consume it.

**Done when** three offices' plates of the same quarter are told apart at pile
distance in the room, by someone who has not been told which is which. That is a
looking test, not an assert.

**Waits on:** nothing. **Start it first if anything is started first** — it is
the longest pole and the one that can invalidate the design.

---

## W4 — Binder identity, and merge — **DONE**

`island · office` identity, the chart's home, and the merge verb. All 13 gated
checks pass. What it actually took is in `03-findings.md` R11.


*Q3.1–Q3.5. Assembly: `Archivist.Building`.*

`BinderView.Add` today refuses a wrong-island sheet:
`if (id.IslandSeed != IslandSeed) return false;`. Identity becomes
`island · office`, so it refuses a wrong-office plate the same way. `Bind`
takes the office; `BinderName`, `Summary` and `Describe` say it.

**Merge** is a new verb. Source and target must share an island; contents move;
the empty binder is destroyed. It is silent (Q3.4) — no counter, no prompt, no
mark, and the game never suggests it. A merged binder holds up to
`offices × 4 + 1` plates.

`CountFor(Office)` already exists and stays — a merged binder needs it.

**Open:** where the verb lives. `BinderPickup` already defers to
`CartographyTable` when a binder is parented under one, so "hold a binder, aim at
a binder" has no home yet. Cheapest is a `BinderPickup` branch when the hands
hold a binder of the same island; the table is not the right place, because
merging is rack work.

**Done when** two binders of one island become one, the ledger is unchanged, and
`RoomSnapshot.Audit` stays clean across a save and load.

**Waits on:** W1, for `SheetId`'s quarter.

---

## W5 — The passive board

*Q4.1–Q4.7. Assembly: `Archivist.Building`.*

After `01-removal.md` §1 the table opens, renders and closes but lays out
nothing. `TableSession` (the room↔table mode switch), `BoardSpace` (the
ground↔board transform, whose sign convention **F-S1.2 measured and must not be
re-derived**), `BoardViewport` (zoom and pan) and `BoardView`'s rig — camera,
mounting sheet, `URP/Unlit` materials, the async `IslandRenderer` render-and-upload
pipeline — all survive untouched.

**What replaces the mechanic:**

1. **Layout is derived.** For each plate in the binders on the table, its board
   pose is `BoardSpace` applied to its quarter rect. No stored pose, no
   `Placement`, no tolerance. Q4.1.
2. **One layer visible.** `Q`/`E` cycles offices. The set of layers is the set of
   offices present in the binders on the table, in `Offices.All` order. Q4.3.
3. **The base underneath.** The island's chart (Q4.4), drawn beneath everything,
   non-interactive, always visible where no quarter covers it. Q4.6.
4. **No board opens without the chart.** R6.8a, unchanged.
5. **Capacity never gates comparison.** Q4.5 — if `BindingAnchors` cannot hold
   every office's binder, binders must swap freely.

`BinderSheetSource` (an `ISheetSource`) is the seam and stays; it returns plates
grouped by office and quarter instead of a flat sheet list.

**Done when** setting three binders of one island on a table shows the island in
three hands, `Q`/`E` flips between them with nothing moving but the ink, and
quitting and reloading reproduces it exactly from binder contents alone.

**Waits on:** the deletion (`01-removal.md` §1), W1, W2.

---

## W6 — Loose plates, and filing

*Q6.1–Q6.5. Assembly: `Archivist.Building`.*

**Most of this exists.** `CartographyTable.File(sheet, hands)` is the filing
verb: it checks island and duplicates through `BinderView.Add`, animates the
plate to the binder anchor with `hands.HandOver`, destroys the paper, keeps the
`SheetId`, calls `SheetSpawner.Forget` and `Archive.Note`. `PlayerHands`,
`ItemFall`, `SheetPickup` and `BinderPickup` are all in place.

**Work:**

1. The office check joins the island check (W4).
2. `MapCrate.looseDebugSheet` — the flag that drops one rendered sheet on the
   floor so filing has something to file — becomes the normal case. Loose plates
   are the primary activity's material, not a debug affordance. The flag goes;
   the behaviour is promoted.
3. Filing at the **rack**, not only at a table. Today the verb lives on
   `CartographyTable` because that is where binders are. Q6.1 files at the shelf.

**D-B2 stands:** filing is irreversible in-world. There is no verb to take a
plate back out, and nothing in the quarter model adds one.

**Waits on:** W4.

---

## W7 — The save, narrowed — **DONE**

The `boards` section went with the deletion; the binder's office landed with W4.
`room.sheets[]` was kept, for the reason `01-removal.md` §3.3 gives.


*Q4.7. Assembly: `Archivist.Building`.*

`archive` version → 2; every v1 save becomes unreadable, which is correct.

- `ledger` — **kept whole.** `SheetLedgerStore.MarkIssued` is the single R2.10
  enforcement point and the `IsIssued` check that validates binder contents.
- `boards` — **deleted** with `BoardStore` and `BoardSnapshot`.
- `room` — `binders[]` keeps `number, seed, island, where/table/anchor, holds[]`,
  plus the office (W4).
- `room.sheets[]` — **kept.** See `01-removal.md` §3.3: a sorted floor pile is
  state the player will expect to survive quitting, and `RoomSnapshot.Audit`'s
  invariant — every issued sheet is somewhere, and somewhere once — only holds
  if loose plates are recorded. The load cost (one island regeneration and one
  raster per loose plate, on a coroutine) is a reason to cap how many lie loose,
  not to forget them.

**Waits on:** the deletion, W4.

---

## W8 — The room, populated

*Q7.1–Q7.3. Assembly: `Archivist.Building`.*

The game starts with the collection already in the room.

**Generated at load, never authored.** `SheetSpawner.Awake` and
`BinderSpawner.Awake` clear any paper present at scene start, and
`SheetSceneGuard` destroys paper on `sceneSaving` so none can be baked into a
`.unity` file. All three stay (Q7.3). Population is a runtime pass that runs
after them and issues through the ledger exactly as `MapCrate` does — reusing
`SheetPicker` and `MarkIssued`, so R2.10 holds by construction.

**This needs two numbers the design does not have yet:**

- **How many islands the room starts with.** Plates per island is now
  `offices × 4 + 1` — **13** at three offices. So 20 islands is 260 plates and
  up to 60 binders.
- **Archive capacity.** `space/requirements.md` S3.2 makes room size a function
  of capacity, and its §10 calls capacity *"the highest-leverage open question —
  blocks S3.2, and therefore blocks the real room, the rack count, and the first
  furniture asset."* **The quarter model is what unblocks it:** fixed paper size
  (Q1.5) and a known plate count per island make the number computable rather
  than chosen.

Load cost is the constraint, not memory: every loose plate on the floor is one
island regeneration plus one raster. Binders are identities and cost nothing.
Start the room heavy on binders and light on loose paper.

**Waits on:** W1, W4, and the supply decision (Q7.4 / D-Q2 — a pre-populated room
is finite, and R1.2 says the islands are not).

---

## Open decisions that block work

| # | decision | blocks |
|---|---|---|
| 1 | **Antiquarian.** Does it conform to four quarters, or do POI detail sheets survive as a second class of object read in hand? (`01-removal.md` §5) | W1 |
| 2 | **The supply.** Q7.4 / D-Q2 — finite room vs unbounded islands | W8 |
| 3 | **Archive capacity.** S3.2's missing input, now computable | W8, and the real room |
| 4 | **Draw order.** `Strokes.Draw`'s order is load-bearing for the acceptance hash; adding contours and grid changes it | W2 |
| 5 | **Where merge lives.** `BinderPickup` branch, or a new verb | W4 |


---

## W9 — The render budget

*New, 2026-08-30. `03-findings.md` R13/R14 for how the current numbers were
reached.*

**2735 ms to render one island's thirteen plates** — a 309 ms chart and twelve
~200 ms quarters. Four optimisations have already landed (overview §4) and the
remaining cost is the field sampling itself: 0.61 M `Height01` evaluations per
plate, each an fBm of `Tuning.FbmOctaves` = 5.

**The redundancy nothing exploits yet:** Q1.2 gives every office **the same four
rects**. Three offices therefore sample the same corners three times, by
construction — and that construction is the whole point of the quarter model,
because it is what puts the board's layers in register.

**Constraints any answer must keep**

- `Archivist.Generation` and `Archivist.Render` never reference UnityEngine — it
  is what lets the acceptance suite run headless.
- Determinism is a hard contract: A2 hashes an island, A3 asserts that two rects
  sharing a border agree to 4×10⁻⁶ m. Output order is a total order and must stay
  one. §4.1 forbids enum reflection; §13.2 forbids `System.Random` and wall-clock.
- R3.1 already renders on demand and caches, off the main thread, one texture
  upload per frame — so this is latency before paper appears, never a frame stall.

**Not decided here.** Whether the answer is a per-island sample cache, coarser
sampling with interpolation (the fill already does something like this —
`RenderTuning.FieldSampleStepPx`), parallelism with a deterministic reduction, a
cheaper field, or drawing less, is what W9 exists to work out.
