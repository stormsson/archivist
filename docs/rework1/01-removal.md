# Rework 1 — What Comes Out

> **§1 is done** (2026-08-30). The placement mechanic is deleted, the four callers
> are rewritten, `Tools/check-building.sh` and `Tools/check-editor.sh` pass. What
> actually happened is recorded in §6, including the two things this document did
> not predict.

Construction. `../quarters/requirements.md` is the authority on the model;
`../quarters/decisions.md` records why. This document is the **removal list**:
what is deleted, what is cut down, and what breaks when it goes.

Nothing here is a judgement about code quality. Every file listed served a
mechanic that is cancelled (Q4.1, Q4.2), and most of it is good work.

**Rule for this branch:** delete rather than comment out. `main` holds the
history; a commented-out mechanic is a second copy that drifts.

---

## 1. The board's placement mechanic — `Building/Runtime/Table/`

27 files, ≈13 150 lines. The delete-tagged set is **≈8 600 lines**.

### 1.1 Delete

| file | lines | what it was for |
|---|---|---|
| `BoardInteractor.cs` | 1904 | drag / select / turn / snap input — this **is** the mechanic |
| `CabinetPanel.cs` | 1874 | the accordion, and the drag-source into the board |
| `SnapHint.cs` | 1181 | assisted-snap ghost, seated glow, widened capture (G7.1/G1.8) |
| `BoardStore.cs` | 1157 | per-table pose state: the `Placement` struct, `GroupRecord`, groups, lay order |
| `CabinetRow.cs` | 737 | one accordion row: thumbnail, states, drag start |
| `BoardFusing.cs` | 293 | candidate search — what a dragged sheet would join, and under what frame |
| `BoardHandle.cs` | 196 | screen-space corner knob for grab-to-rotate |
| `SheetKinship.cs` | 158 | which two sheets may join (the same-survey rule, G1.2) |
| `BoardFrame.cs` | 149 | the rigid transform a group is placed under |
| `BoardOutline.cs` | 144 | gold selection rim under the selected slab |
| `SheetFit.cs` | 116 | the fit test itself — position and rotation tolerance |
| `BoardSnapshot.cs` | 99 | the save DTO for placements and group frames |
| `SheetUnion.cs` | 86 | board-space bounding box of an assembly |
| `BoardSlabs.cs` | 64 | slab-for-identity, and a group's run in join order |

**`Placement` disambiguation.** The struct being deleted is defined at
`BoardStore.cs:46`. `Archivist.Building.Placement` is an unrelated *namespace*
(the room's anchors, `Placement/PlacementAnchor.cs`) and stays. This is the trap
recorded against the assembly: inside `Archivist.Building.*` the unqualified name
`Placement` resolves to the namespace, and a file-scope `using` alias does not
win against it.

### 1.2 Rewrite

| file | lines | keep | cut |
|---|---|---|---|
| `BoardView.cs` | 1669 | the rig: `BuildCamera`, mounting sheet + `URP/Unlit` materials, `boardOrigin`, `Table` layer and culling mask, the async `IslandRenderer` render-and-upload pipeline, `Hide`/`OnDestroy` | `Lay` / `Seat` / `Remove` / `MoveGroup` and the `BoardStore` they drive |
| `TableCanvas.cs` | 579 | the chrome root and `Show`/`Hide` | all cabinet wiring |
| `TableOptions.cs` | 262 | `BoardUnitsPerMetre`, `BoardPadding`, `SheetSeparation`, `BoardZoom*`, `WheelSensitivity`, `BoardPixelsPerPaperMm` | `PositionTolerance`, `RotationToleranceDeg`, `SettleSeconds`, `SheetTurnDegreesPerSecond`, `CabinetScrollPixelsPerNotch` |
| `BoardSheetView.cs` | 293 | mesh, material, texture-borrowing constructor, quad sizing | the pose-setting API |
| `BinderSheetSource.cs` | 92 | the seam | it lists *sheets*; it will list plates by quarter and office |

### 1.3 Keep untouched

`TableSession.cs` (525) — the room↔table mode switch. Not a scene load: it
disables `Camera.main`, `FirstPersonController`, `PlayerInteractor` and
`PlayerHands`, enables the Table and UI action maps, bootstraps an
`EventSystem`, and calls `BoardView.Show` / `TableCanvas.Show`. Orthogonal to
placement, needed unchanged.

`BoardSpace.cs` (108) — ground↔board affine transform, metres → board units,
ground X → board X and ground Y → board Z. Needed for any board at all.
**F-S1.2 measured the rotation convention here and it must not be re-derived.**

`BoardViewport.cs` (162) — camera zoom and pan, deliberately never persisted.
`Wheel.cs` (62) — wheel-notch normalisation; **retained for zoom, and D-Q1 keeps
it on disk regardless.**
`SheetNaming.cs` (247), `ISheetSource.cs` (61), `LedgerSheetSource.cs` (77),
`BoardSheet.cs` (53).

### 1.4 What breaks outside `Table/`

| caller | breaks on | fix |
|---|---|---|
| `Collection/Archive.cs` | `.Boards`, `BoardStore`, `BoardSnapshot` | boards leave the save (Q4.7) — see §3 |
| `Collection/ArchiveFormat.cs` | `Table.Placement.SeatedAtTruth/InGroup/Laid` | the placement keys leave the format |
| `Interactables/CartographyTable.cs` | `.Boards.Clear`, `BinderSheetSource` ctor | rewritten with the new source |
| `Sheets/SheetSpawner.cs` | `BoardSheet` | unaffected — `BoardSheet` is kept |
| `Building/Editor/CartographyRigBuilder.cs` | 223 | `TableOptions`, `BoardView`, `BoardInteractor`, `TableCanvas`, `TableSession` — rebuild for the passive rig |
| `Building/Editor/CartographyBoardBench.cs` | 292 | `BoardSpace`, `BoardSheetView`, `TableOptions`, `QuickLay` — replace with a quarter-layout bench |

### 1.5 Config

`gameplay.assistedSnap` and `gameplay.glowingHintRange` in
`config/generation.yml`, read by `Building/Runtime/Config/GameplayOptions.cs`.
Both keys and the whole `gameplay:` surface go with `SnapHint.cs` — it is the
only consumer.

---

## 2. The cutter — `Generation/Sheets/`

Only `Island.cs` calls any cutter. `Island.CutSurveys` (from `FromSeed`) and
`Island.CutSurvey(Office)` are the two entry points; nothing outside
`Generation/Island.cs` reaches a cutter directly. That is the whole blast radius.

### 2.1 Delete

| file | lines | what it was for |
|---|---|---|
| `SurveyCutter.cs` | 931 | the lattice cutter: per-office rotation derivation (`Rotations`, PCA of coast and land), centred lattice, Garrison block, coast-crossing cull |
| `CoastWalkCutter.cs` | 390 | Hydrographic's shore walk: per-*sheet* rotation, chord stepping, region discs, seaward bias, min-separation dedup |
| `RectCull.cs` | 86 | the 16×16 land/served sampler both cutters used to decide whether a candidate rect survived |

Nothing decides *whether* a rect survives any more (Q1.2 — every office gets the
same four), so the cull has no question to answer.

`PlanSurvey`'s year draw and the whole-island paper choice are the only parts
worth carrying across; they are ~40 lines inside a 931-line file.

### 2.2 Rewrite

| file | lines | change |
|---|---|---|
| `SurveySpec.cs` | 142 | `SurveySpec.RotationDeg` and `OverlapFraction` go; `Format` becomes constant. `Sheet.RotationDeg` goes. `Sheet.Number` becomes the quarter (§2.4) |
| `MapScale.cs` | 69 | `ForOffice` goes — scale is per island now (Q1.6). Add "smallest denominator at which the land bounds fit four quarters" |
| `SheetFormat.cs` | 51 | one paper size (Q1.5). `CoastalStrip` dies with `CoastWalkCutter`; `Landscape` and orientation choice die with rotation |

### 2.3 Keep

`Office.cs` (55) — **do not touch.** `Office` ordinals are load-bearing:
`Streams.For(seed, StreamNames.Year, (int)office)`, `FeatureMatrix.Table[office,
class]`, and five editor call sites index by `(int)office`. Renumbering rewrites
every island.

`FeatureMatrix.cs` (194) — office × `FeatureClass`, orthogonal to geometry, and
about to become **more** important: it is the table the render layer mapping is
built from (see `02-features.md` W2).

`GarrisonGrid.cs` (80) — takes a rect, draws grid lines. Does not care how the
rect was chosen.

### 2.4 Identity

`Collection/SheetId.cs` today is `{ ulong IslandSeed; Office Office; bool
WholeIsland; int Number; }`. `Number` is a cull-dependent 1..N. Under Q1.1 it
becomes a quarter, one of four. Keeping the field and constraining its range to
`{1,2,3,4}` = NW/NE/SW/SE is the smallest change that works, and it keeps
`ArchiveFormat`'s sheet key (`"{seedHex}/{Office}/{whole|part}/{number}"`)
byte-compatible. **Every existing save is invalidated regardless** — the plates
behind those ids no longer exist.

### 2.5 Config

`config/generation.yml` — these sections lose their consumers:

| key / section | dies with |
|---|---|
| `paper.OverlapFraction` | Q1.4, exact tiling |
| `hydrographic_coastal_strip:` (all 8 keys) | `CoastWalkCutter` |
| `cull:` (`LandFractionMin*`, `CullSampleGrid`) | `RectCull` |
| `rotation:` (`Pca*`) | `Rotations`, Q1.2 |
| `gameplay:` (`assistedSnap`, `glowingHintRange`) | `SnapHint` |
| `scales.CoastalScaleDenominator` | `MapScale.ForOffice` |

`Tuning.Values.cs` is generated from that file's `Parameter` table; removing keys
means regenerating it, not hand-editing.

### 2.6 Determinism

`StreamNames.CoastRegion` becomes unused — it existed only for the coast walk's
random anchor and radius. Leave the member in place: `StreamNames` is a set of
named sub-streams, and removing one does not move an island, but the file's own
rule is that names are stable. Mark it reserved, as `Peaks` and `Palette`
already are.

Rotation was never seeded — `Rotations` derived angles from PCA geometry, not
from a stream — so no sub-stream retires with it.

**`StreamNames.WholeIsland`** (the office pick for the whole-island survey)
**survives**, and matters more than before: Q2.3 makes one chart per *island*,
from one office, and that draw is what picks the office.

---

## 3. Render, binders and the save

### 3.1 Render — nothing is removed, and that is the finding

There is **no office→layer mapping in the runtime render path**. The only
runtime caller, `MapCrate.Render`, uses `RenderRequest.ForSheet(sheet,
pixelsPerPaperMm)` with the default `LayerMask.All`. Every sheet in the game is
drawn identically regardless of office.

There is also **no per-office style**. `Palette.Global` is one 12-entry
placeholder used for every island and every office; `Ink` and `Bands` take no
office parameter; `RenderTuning`'s stroke widths are global constants;
`SheetSpawner.paperTint` is a single serialised colour. `Editor/OfficeStyle.cs`
is debug-window chrome — four rows of abbreviation, name, tag and colour — and is
documented as not touching the map.

So the removal list here is empty, and the implication is in `02-features.md`:
**§5.4's first proof — can a player read office style at a glance — has no
implementation to test.** It is not a tuning job. It is the largest single piece
of new work in the rework.

### 3.2 Binders

`BinderView.Add` enforces one island (`if (id.IslandSeed != IslandSeed) return
false;`). Q3.1 makes identity `island · office`, so it must enforce one office
too. Nothing is deleted; the guard widens.

`MapCrate` stays, demoted (Q7.2). `looseDebugSheet` — the flag that drops one
real rendered sheet on the floor so the filing verb has something to file — stops
being a debug affordance and becomes the normal case (Q6.1): loose plates are
what the player sorts. The flag itself goes; the behaviour is promoted.

### 3.3 The save

`ArchiveFormat` writes four top-level keys: `archive`, `ledger`, `boards`,
`room`.

| key | disposition |
|---|---|
| `archive` | version bumps to 2. Version mismatch already makes a file unreadable, which is correct — every v1 save is invalid |
| `ledger` | **kept whole.** `SheetLedgerStore` is the R2.10 issuance gate and the `IsIssued` check that validates binder contents on load |
| `boards` | **deleted.** `BoardSnapshot`, `groups[]`, `placed[]` and the `Placement` keys `seated` / `group` / `x,y,rotation` all go with `BoardStore` (Q4.7) |
| `room` | **narrowed.** `binders[]` keeps `number, seed, island, where/table/anchor, holds[]`. `sheets[]` — the loose-sheet array — is where the decision below bites |

**One thing to settle before writing the format.** Q4.7 says the save is binder
contents plus which binders are on which table. But Q6.1 puts loose plates on the
floor as the primary activity's material, and a floor pile the player sorted is
exactly the state they will expect to survive quitting. Dropping `sheets[]` costs
`RoomPaper.RestoreSheets`' expensive coroutine — one island regeneration and one
raster per loose sheet on load — and loses the pile.

Recommendation: **keep `sheets[]`.** Q4.7 was written about the *board*, not the
room, and `RoomSnapshot.Audit`'s invariant — every issued sheet is somewhere, and
somewhere once — only holds if loose sheets are recorded. The load cost is real
and is a reason to cap how many plates lie loose at once, not a reason to forget
them.

`PaperWhere.Hands` can go either way and is not worth a rule.

---

## 4. Tests, tooling and scenes

### 4.1 Acceptance — `Tools/GenHarness/`

| id | what it checks | after |
|---|---|---|
| A2 | determinism via `IslandDigest.Hash` | **survives**, values all change — the digest hashes cutter output |
| A3 | no contour seams | unaffected |
| A4 | sheet numbers contiguous 1..N | **survives** — four quarters number 1..4 trivially. `CutterTests.SheetNumbersAreContiguousFromOne` likewise |
| A5 / A5b | no blank sheets, thin-sheet metric | **needs porting** to whatever replaces `Sheet.Contains` / `Sheet.GroundBounds`. "Thin" changes meaning: a quarter of a small island is *supposed* to be mostly blank |
| A6 | cross-office shared-class coverage | **degenerates.** Every office now covers identical rects, so overlap is 100% by construction. Reported not gated, so it will not fail — but it stops measuring anything and should be retired or repointed |
| A7 | sheet economy — D2 rotation separation, D5 whole-island scale fallback | **rewrite.** Its subject matter is what Q1.2 and Q1.6 delete |
| A8 | performance budget | survives |
| C2–C6 | POI determinism, placeability, detail numbering, density | survive **iff** `DetailSheetCutter` is kept — see §5 |

`CutterTests.EveryIslandCarriesAWholeIslandSheet` asserts `WholeIslandSurvey` is
non-null with `SheetCount == 1`. Q2.3 keeps exactly one chart per island, so this
holds — it would have broken under one-chart-per-office.

`CutterTests.GarrisonAlwaysSurveysTrueNorth` asserts `RotationDeg == 0`. Q1.2
satisfies it trivially, for every office.

### 4.2 Editor tooling

| file | lines | disposition |
|---|---|---|
| `Building/Editor/CartographyRigBuilder.cs` | 223 | rewrite — builds the rig from `TableOptions`, `BoardView`, `BoardInteractor`, `TableCanvas` |
| `Building/Editor/CartographyBoardBench.cs` | 292 | rewrite — `QuickLay` becomes "lay the quarters", which is now the only way anything is laid |
| `Editor/SvgExport.cs`, `IslandDebugWindow.cs`, `SheetPane.cs`, `ComparePane.cs`, `VectorDraw.cs`, `SheetContent.cs` | — | these already draw per-`FeatureClass` via `FeatureMatrix`. They are the closest thing to the office-layer model that exists, and are worth **reading before** building W2 rather than deleting |

### 4.3 Scenes

`POC04_Room.unity` needs the cartography rig rebuilt. `Debug_Generator.unity` is
overwritten by `GeneratorSceneBuilder` and costs nothing.

`SheetSpawner.Awake` and `BinderSpawner.Awake` clear any paper present at scene
start; `SheetSceneGuard` destroys paper on `sceneSaving` so it can never be baked
into a `.unity` file. **All three stay** (Q7.3) — the pre-populated room is a
runtime pass that runs after them, never authored content.

### 4.4 The compile trap

`Archivist.Building` is type-checked nowhere outside the Unity editor —
`Tools/check-editor.sh` covers Generation, Render, Editor and Tests only. Deleting
14 files with 40-odd cross-references will not fail any headless check. Either
extend the script to compile `Building/Runtime/**` and `Building/Editor/**`
against `Library/ScriptAssemblies`, or expect the editor to be the only oracle.

**Do this before the deletion, not after.** It is the difference between one
error list and fifty rounds of the editor.

---

## 5. Antiquarian — postponed (2026-08-30)

**Decision: postponed. That office is being reworked separately**, so nothing
here forces it either way. `DetailSheetCutter` is untouched, `Offices.CutsSurvey`
already excludes Antiquarian from survey cutting, and `QuarterCutter.PlanDetail`
exists only to hand it a spec — so the quarter cutter and the detail cutter do
not meet. Acceptance C2–C6 keep passing.

What that leaves live, for whoever does the rework: an Antiquarian plate is
250 x 250 mm at 1:1250 and covers ~275 m of ground, so it is not a quarter and
has no place in a 2 x 2 layout (Q1.1). It is the only thing in the collection at
a human scale rather than a survey one, and `PoiKinds` is the natural key for the
request system (§11). The original framing follows.

---

## 5a. The original question, kept

### One thing this document cannot decide

**Antiquarian and its detail sheets.** `Office` has four members, not three:
`Hydrographic = 0, LandSurvey = 1, Garrison = 2, Antiquarian = 3`.
`Offices.CutsSurvey` is false only for Antiquarian, and `DetailSheetCutter` (109
lines) gives it one square 250 mm sheet per POI at 1:1250 — seven of them on
island 0 (F-S1.4).

A POI detail sheet covers ~275 m of ground. It is not a quarter, it has no place
in a 2 × 2 layout, and Q2.3 says an office produces four plates. So either:

- **Antiquarian conforms** — it becomes an ordinary layer with four quarters, and
  `DetailSheetCutter`, `StreamNames.PoiSheet`, `SheetFormat.DetailSheet`,
  `MapScale.PoiDetail` and acceptance C2–C6 all go; or
- **detail sheets survive as a second class** — small plates that live in a binder
  and are read in hand, never on the board. That contradicts nothing in the
  quarter model, keeps `PoiKinds` visible as the future request key (§11), and is
  the only thing in the collection at a human scale rather than a survey one.

The second is more interesting and costs a class of object the room does not yet
have. It is a design decision, not an implementation one, and it blocks W1.

---

## 6. What actually happened — §1, as executed

### 6.1 Deleted, as listed

All 14 files in §1.1, with their `.meta`. `BoardView.cs` went from **1669 to
1101** lines and `TableCanvas.cs` from **579 to ~340**.

### 6.2 Two things this document did not predict

**`CabinetStyle` was inside `CabinetPanel.cs`.** Every colour, size, font and
RectTransform helper the *header* uses lived in the cabinet's file, and
`BoardView` read `CabinetWidthFraction` from it to set the board camera's
viewport. Deleting the cabinet took the header's style with it.

Recovered as **`Table/TableStyle.cs`** (230 lines): the palette, the header
metrics, `Serif`/`Sans`/`Spaced`, and the small builders. Dropped with the
cabinet: row, thumbnail, section, group, snap-hint and footer values, `Stack`,
and `CabinetWidthFraction` — the board camera now takes the whole screen
(`cam.rect = new Rect(0f, 0f, 1f, 1f)`).

**`Tools/GenHarness` compiled `BoardStore.cs` and `BoardSnapshot.cs`** out of
`Assets/` to run the save checks headlessly, so the acceptance build broke on
files §1.1 had already removed. Both `<Compile Include>` lines are gone from
`GenHarness.csproj`.

### 6.3 The save acceptance, rewritten

`SaveAcceptance.cs` was 833 lines and S1–S7 tested placements, assemblies,
frames and lay order through the file. None of that exists. Rewritten to three
checks over what the file still carries:

| was | now |
|---|---|
| S1 board round trip | **S1** file round trip: write → read → write is identical, the ledger survives, issue order survives, and a version-1 file is refused whole |
| S2–S7 board internals | **gone** |
| S8 the room | **S2**, unchanged in substance |
| S9 every issued sheet is somewhere | **S3**, unchanged |

`Suite.cs` registers the three. The version check is new and is the one place a
test asserts Q1.1's consequence: **every v1 save is unreadable, deliberately**,
because the plates it names no longer exist.

### 6.4 The board still works

The deletion did not leave a stub. `BoardView.Relay` — which used to put a sheet
back at its stored placement as its raster landed — became `LayOut`, which seats
it at its true ground pose. So the board already does what Q4.1 asks: set
binders on a table, every plate lays itself out, nothing is dragged. When W1
lands, those poses become quarters and nothing in the view changes.

`BoardSpace`'s rotation convention is untouched, including the negation
**F-S1.2** verified by outcome. `TryPoseOf` survives, loose-only, as the inverse
of `Put`'s negation.

### 6.5 Still to do in the room

`POC04_Room.unity` may carry a `BoardInteractor` GameObject with a missing
script. `CartographyRigBuilder` no longer creates one and says so in its log; the
stale object is deleted by hand, once, when the scene is next opened.

---

## 7. Debris — swept 2026-08-30

Found by grepping for readers after §1 and §2 landed, and now removed. None of it
broke a build; all of it described a mechanic that is gone, which is the kind of
thing that gets re-read as if it were still true.

### 7.1 Code with no callers left — removed

| what | lines | why it survived the delete |
|---|---|---|
| `Generation/Geometry/Pca.cs` | ~110 | only `Rotations` called it, and `Rotations` went with `SurveyCutter` |
| `SheetFormat.CoastalStrip` | 6 | Hydrographic's long thin shore sheet; it gets quarters like everyone else (Q1.2) |
| `MapScale.Detail`, `.Coastal`, `.ForOffice` | 20 | scale per **office**. Q1.6 chooses one per **island**, shared by every office — that is what puts the board's layers in register |
| `Building/Runtime/Config/GameplayOptions.cs` | 100 | read `gameplay.assistedSnap` for `SnapHint`, which is gone. The `Config/` folder went with it |

### 7.2 Config keys with no readers — removed

Seventeen keys, and three whole sections of `config/generation.yml`:

| section | keys | died with |
|---|---|---|
| `hydrographic_coastal_strip` | all 8 | `CoastWalkCutter` and `SheetFormat.CoastalStrip` |
| `rotation` | all 3 (`Pca*`) | `Rotations`, and Q1.2 — nothing rotates |
| `gameplay` | both | `SnapHint` |
| `cull` | `LandFractionMinLandSurvey`, `LandFractionMinGarrison` | `RectCull` |
| `paper` | `OverlapFraction` | Q1.4 — quarters tile exactly |
| `scales` | `DetailScaleDenominator`, `CoastalScaleDenominator` | per-office scale |

**`CullSampleGrid` stays**, alone in a section named for a cull that no longer
exists: `Editor/IslandDebugWindow` samples a sheet on the same 16 x 16 lattice to
decide whether it is worth drawing. Its comment now says so, so the next reader
does not delete it for looking orphaned.

Checked afterwards, and all four sets agree exactly: every key in
`generation.yml` has a `Parameter` row, every `Parameter` row has a `Mix` entry
for the tuning digest, and neither table carries a key the file does not.

### 7.3 Things that said a sheet has a number — fixed

| where | was | now |
|---|---|---|
| `Table/SheetNaming.cs` | formatted a quarter as `02` | `HY·NE`; two digits only outside 1..4, which is a detail sheet |
| `Building/Editor/SheetTestBench.cs` | summoned `LandSurvey:7` | `LandSurvey:2`; a survey has four sheets |
| `Building/Editor/CartographyBoardBench.cs` | described sheets overlapping "by a fifth" | records that its question was answered by F-S1.1 and its subject is gone |
