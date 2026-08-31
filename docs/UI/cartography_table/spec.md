# The Cartography Table — Specification

> **SUPERSEDED (2026-08-30, branch `rework1`) — kept as the record of what was
> built.** The board this specifies asked the player to place each sheet at its
> absolute ground pose. That mechanic is cancelled: see
> `../../quarters/requirements.md` §5 for what replaces it and
> `../../quarters/decisions.md` §4 for the disposition of every section here.
>
> What survives: §3.1 (ground-space coordinates), §3.4 (material and light),
> §4.1 (table identity), §5 (scene structure), §8.1 (opening the board).
> What does not: §3.2's per-sheet poses, §3.3 draw order by survey, §4.2
> `BoardStore`, §6 snap in full, §7 the cabinet, §8.3 board input, §9
> persistence, §10's fit tuning, §12's acceptance cases for fitting.

Construction. `requirements.md` in this folder is the authority on intent; the
four PNGs beside it are the authority on look. `../../requirements.md` §3.6 is
the authority on the activity, and where this document disagrees with it the
disagreement is **recorded in §2**, not silently resolved.

Read `../../architecture.md` first. This assumes `Archivist.Building`'s existing
shape and the determinism contract in `../../generation_for_agents.md`.

Requirements are numbered **C**n.n. Existing numbers (R, S, T, F, P, D) refer to
their own documents.

---

## 1. What this is

A full-screen view, opened from a table in the room, in which the sheets the
archive holds for **one island** are laid out and joined until they reconstruct
that island's ground.

The player's physical item is the **folder**, never the sheet. Sheets exist as
world objects nowhere except on this table. That single fact removes most of
§3.6's machinery — see D-C1.

### 1.1 Settled decisions

| # | decision |
|---|---|
| C1.1 | The composition area is an **orthographic camera on real `SheetView` slabs**, not a UI layout. Chrome (header, cabinet) is UGUI on top. |
| C1.2 | The board is **ground space**. A sheet is drawn at the size its ground footprint occupies, so sheets of different offices differ in size and **overlap by 20%**. |
| C1.3 | The accordion is fed by the **ledger** — `SheetLedger.IssuedSheets(seed)` grouped by `SheetId.Office`. No folder model in this POC. |
| C1.4 | Entry is an **`Interactable` on the table** (aim + F, "Open Cartography table"). `C` is a debug shortcut behind a serialized flag. `Esc` closes. |
| C1.5 | Snap **previews** — edges glow while dragging inside tolerance — and **settles** on release. Seating is **not sticky**: a seated sheet can be picked up again. |
| C1.6 | This POC is **additive except for one removal**: `Q`/`E` turning leaves `PlayerHands` and becomes a table verb (D-C10). `MapCrate`, `SheetSpawner` and the floor pile are untouched. |
| C1.7 | Board state is keyed by **table identity**, not island. A table adopts an island from the first folder laid on it and reverts to unbound when emptied. |
| C1.8 | Board state and the ledger are **persisted**. The ledger saves at issuance; boards save on close, on seat, and on any sheet release. |

---

## 2. Deviations recorded

Later never overrules earlier. Each row states what disagrees and why the
disagreement was accepted.

| id | against | what | why |
|---|---|---|---|
| **D-C1** | R6.2, R6.3 | The player does not place a *copy*; there is no original to copy. | R6.2 exists to solve one problem — a sheet is a physical item, so composing it would take it off the shelf where it is also needed. With the **folder** as the physical item and sheets never instantiated in the room, that problem cannot arise. R6.2 and R6.3 are **obsolete, not satisfied**. |
| **D-C2** | R6.4 ("tolerance is generous and **hidden**") | Edges glow the moment a dragged sheet enters tolerance. | The glow reveals no number, only "yes, here". With sheets at ground scale and 20% overlap the true pose is an invisible target; without a preview the activity is fiddly rather than calm (T5). The *value* stays hidden; the *boundary* is felt. |
| **D-C3** | R5.5 ("no checkmarks, no counters") | Section counts (4, 3, 2) are kept. The ✓ is **dropped**. | A count of sheets held is inventory, not a grade — and because the accordion lists only *issued* sheets, it never reveals how many the survey actually has. The ✓ meant "seated", which is precisely what R5.5 names, and is already visible on the board itself. |
| **D-C4** | mockups `1a`, `1c` | The ✓ drawn on some cabinet rows is legacy. | The updated `2a` legend documents two states only — *laid out, unseated* and *all sheets on table*. `1a`/`1c` were not re-rendered. The legend governs. |
| **D-C5** | mockups `1a`, `1c` | The clean 3 × 3 grid of same-size sheets butting edge-to-edge **cannot occur**. | `Tuning.OverlapFraction = 0.20`, so sheets of one survey overlap by a fifth. Offices use different paper (Hydrographic 380 × 200 strips, Land Survey / Garrison A1 594 × 841, Antiquarian 250 × 250) and different scales, and Hydrographic carries **per-sheet** rotation (D-H2, the coast walk) so its sheets are ribbons at varying angles. The mockup's grid is a diagram of the *idea*, not of the output. |
| **D-C6** | mockup caption | "MOUNTING SHEET · 3 × 3" and "SCALE 1:24 000" become **derived**. | A fixed 3 × 3 has no referent once the board is ground space. The caption shows the island's real ground extent and the board's computed scale. |
| **D-C7** | R1.11 ("nothing geometric is ever persisted") | A board stores the pose of every *unseated* sheet. | R1.11 governs the **island**, which stays a pure function of its seed. A board pose is a **player** fact, not an island fact, and is exactly as unrecomputable as the ledger. Note the halving that keeps it honest: a **seated** sheet's pose *is* `CentreGround` + `RotationDeg` and is therefore never stored — only a flag. |
| **D-C8** | R6.6 (two-point pinning) | Not in this POC. Drag and a rotate handle only. | Deferred, not rejected. |
| **D-C9** | §3.7 (reference maps) | Absent. R6.7's "fit against the true island, never against references" is trivially satisfied because references do not exist. | |
| **D-C10** | POC-05 as built | `Q`/`E` no longer turn a sheet **in hand**. The verb moves to the table. | Turning paper to read it belongs where paper is read. In the room the player carries a sheet from a pile to a table; the orientation they choose there is discarded the moment it is laid on a board, because the board has a true orientation of its own. Keeping the verb in both places would mean two turn states — one in hand, one on the board — that can disagree, and the hand's is the one with no consequence. See §8.4. |

---

## 3. The board

### 3.1 Coordinate space

The board is a rectangle of **ground**, taken from `island.LandBounds` padded by
`TableOptions.BoardPadding` (default 8% of the longer side, so a coastal sheet
that overhangs the land has somewhere to sit).

Board world units are ground metres scaled by `BoardUnitsPerMetre`
(default **0.01**, so one unit is 100 m). An island of ~12 km is then ~120 units
across — small enough that float precision and ortho camera sizes stay in
comfortable ranges, large enough that a detail sheet is not sub-unit.

```
boardPos(groundPoint) = (groundPoint - boardCentre) * BoardUnitsPerMetre
```

Ground **X** maps to board **X**, ground **Y** maps to board **Z**. The board
lies in the XZ plane at y = 0; the camera looks down −Y.

### 3.2 Sheets at ground size — the map, not the paper

- **C3.2** A board sheet shows **the map alone. No paper, no margin.** It is a
  `BoardSheetView` — a flat quad carrying the raw raster — and not a `SheetView`,
  because a thing on the board is not a sheet of paper in the room.

**This is a correctness rule, not a preference.** The margin is not merely
unwanted on the board; drawing it is *wrong*:

```
SurveySpec.SheetGroundWidth = Scale.GroundMetres(Format.MapWidthMm)
                                                       ^^^ the MAP, inside the margin
```

Every ground question the generator answers — `Sheet.FrameRect`,
`Sheet.Contains`, `Sheet.GroundCorners`, and therefore the whole cull and the
whole render — describes the **map area**. A slab drawn at full paper size
over-covers the ground by the margin on all four sides, so every edge sits where
no edge is, adjacent sheets appear to abut ground they do not cover, and the
snap target disagrees with the thing being aimed at. On an A1 the margin is
40 mm, which at 1:2500 is **100 m of ground per edge**.

- **C3.2a** The quad is sized **directly in board units**, baked into its
  vertices, with `localScale` left at **1**:

```
width  = Survey.SheetGroundWidth  * BoardUnitsPerMetre
height = Survey.SheetGroundHeight * BoardUnitsPerMetre
```

  No `Scale.Denominator`, no scale factor. A caller reading a transform sees the
  pose and nothing else; a hidden unit conversion in `localScale` is a trap.

- **C3.2b** The quad's centre is `Sheet.CentreGround`, mapped through
  `BoardSpace.ToBoard`. Map centre and paper centre coincide anyway — the margin
  is symmetric — so this was never the part that was wrong.

**Superseded:** an earlier version of this section reused `SheetView`'s box mesh
at paper size, scaled by `Scale.Denominator`, and needed a **non-uniform**
`(s, 1, s)` scale to stop a 1:25 000 whole-island sheet being ten times thicker
than a 1:2500 survey sheet. A quad has no thickness, so that whole problem —
and the `SlabScaleFor` helper that fed it — is gone rather than solved.

Worked example at `BoardUnitsPerMetre = 0.01`. A 1:2500 A1 has a 514 × 761 mm
map area → 1285 × 1902 m of ground → **12.85 × 19.02 units**. A Hydrographic
strip's 350 × 170 mm map → 875 × 425 m → **8.75 × 4.25 units**. A detail sheet's
220 × 220 mm at 1:1250 → 275 × 275 m → **2.75 × 2.75 units**. The size difference
is real and is information — a detail sheet *is* a small window.

### 3.3 Draw order

Sheets overlap, so order is a design element, not an accident. The quads are
flat and coplanar by default, so separation is entirely the caller's job: Y
offset by draw index, `TableOptions.SheetSeparation` (default 0.004 units)
apart:

1. **seated** sheets, lowest, in office order then sheet number — they are the map
2. **unseated** sheets above them, in the order they were laid down
3. the **selected** sheet topmost
4. the **dragged** sheet above that

A seated sheet sinking below unseated ones is the whole visual argument that the
board is being *assembled*.

### 3.4 Material and light

Board slabs use an **unlit** material variant, not `SheetSpawner`'s URP Lit one.
The mockups are flat and evenly lit; more importantly it makes the board
independent of where its root sits in the scene and of the room's lighting,
which is what allows §5.1's offset root.

### 3.5 The caption

Bottom-left and bottom-right of the mounting sheet, per the mockups, but derived
(D-C6):

- left: the board's ground extent, e.g. `BOARD · 9.4 × 7.1 km`
- right: the board's scale, `1 : round(1 / (BoardUnitsPerMetre * pixelsPerUnit))`
  computed from the ortho camera's current pixel size

---

## 4. Data model

### 4.1 Table identity

A table is a **board** (R6.8). Each instance carries a stable GUID serialised on
the prefab instance — `[SerializeField] string tableId`, generated on first
validate, never regenerated.

Binding is **soft**:

- **C4.1** A table with no folders on it is *unbound* and accepts any folder.
- **C4.2** The first folder laid on a table fixes its `islandSeed`.
- **C4.3** While bound, the table accepts only that island's sheets — R6.8
  enforced by the object rather than by a rule.
- **C4.4** Removing the last folder returns the table to unbound and **discards
  its board state**. Emptying a table is the deliberate act of clearing it.

For this POC the folder model does not exist, so the table's `islandSeed` is
assigned in the inspector or by the `C` shortcut (`generator.LastIslandSeed`).

### 4.2 BoardStore

Engine-free, in `Building/Runtime/Table/`, mirroring `SheetLedgerStore` exactly
— same lifetime, same shape, same serialisation story, so the day either is
persisted both are, in one move.

```csharp
public sealed class BoardStore                    // no UnityEngine
{
    sealed class Board
    {
        public readonly string TableId;
        public ulong IslandSeed;                  // 0 while unbound
        public readonly Dictionary<SheetId, Placement> Placed;
        public readonly List<SheetId> LayOrder;   // draw order, §3.3
    }

    public readonly struct Placement
    {
        public readonly bool Seated;
        public readonly double GroundX, GroundY;  // ignored when Seated
        public readonly double RotationDeg;       // ignored when Seated
    }
}
```

- **C4.5** A sheet not present in `Placed` is in the cabinet. There is no third
  state and no "removed" record.
- **C4.6** `Seated == true` stores **no pose**. The pose is
  `SheetLookup.TryFind(...).CentreGround` / `.RotationDeg`. Storing it would be
  caching a pure function of the seed, which is the mistake R1.11 exists to
  prevent (D-C7).
- **C4.7** Order is kept, for the same reason `SheetLedgerStore` keeps it: a
  board that reordered itself between two openings would be unreadable.

### 4.3 The source seam

The accordion is fed through one interface so the folder model can replace the
ledger later without the UI changing:

```csharp
public interface ISheetSource
{
    IReadOnlyList<SheetId> SheetsFor(ulong islandSeed);
}
```

`LedgerSheetSource` wraps `SheetLedger.IssuedSheets`. A future
`FolderSheetSource` filters the folders on the table. **The UI layer must never
reference `SheetLedger` directly.**

---

## 5. Scene structure

### 5.1 Objects

```
BoardRoot                    (0, -500, 0) — clear of the room
  BoardCamera                orthographic, looks down −Y, cullingMask = Table only
  MountingSheet              a quad, the pale board surface
  Sheets/                    SheetView slabs, layer Table
TableCanvas                  Screen Space - Overlay, disabled until opened
  Header  Cabinet  RotateHandle  Footer
EventSystem                  + InputSystemUIInputModule    ← does not exist yet
```

- **C5.1** New layer **`Table`**. The main camera's culling mask **must** exclude
  it; `BoardCamera`'s mask includes only it.
- **C5.2** `BoardRoot` sits well away from the room so nothing on the board can
  be seen, hit or lit by it. The unlit material (§3.4) makes the offset free.
- **C5.3** `RoomBuilder` gains a `BuildCartographyTable` step. Geometry is built
  by script, not hand-placed, per the project's standing rule.

### 5.2 The `SheetSpawner` exclusion — required

`SheetSpawner.AllInScene()` uses `Resources.FindObjectsOfTypeAll<SheetView>()`
and returns **every scene-bound `SheetView` regardless of hideFlags**. Three
things then go wrong with board slabs unless they are excluded:

1. `SheetSpawner.Awake()` **destroys** all of them at scene start.
2. `Place()` uses the count as the floor pile height, so board slabs push floor
   paper into the air.
3. `ClearAll()` destroys them.

- **C5.4** Board slabs carry a marker component `BoardSheet`, and
  `AllInScene()` skips any `SheetView` that has one. A layer test is not enough
  — the layer can be misconfigured in the inspector and the failure is silent.

### 5.3 Textures

- **C5.5** One texture per `SheetId`, cached, used for **both** the board slab
  and the cabinet thumbnail. The thumbnail is ~60 px wide and a board sheet
  ~150 px, so a single render at `TableOptions.BoardPixelsPerPaperMm`
  (default **0.6**) serves both. There is no zoom (per `requirements.md`), so
  there is no case that needs more.
- **C5.6** Rendering runs on a **worker thread** via the existing pure path —
  `RenderRequest.ForSheet` → `IslandRenderer.Render` — exactly as
  `MapCrate.Draw` does, and uploads **one texture per frame** in a coroutine.
  `MapCrate` already learned this: five uploads in one frame is a visible hitch,
  and T5's "quiet" cannot survive it.
- **C5.7** Opening a table costs one island generation (~340 ms, cached after
  the first) plus N renders. The view opens on the mounting sheet with the
  cabinet filling in as textures land; it never blocks.

---

## 6. Snap

### 6.1 Truth

**No feature matching is required and none should be written.** Every `Sheet`
carries `CentreGround` and `RotationDeg`. The correct board pose is exactly
known:

```csharp
bool Fits(Sheet truth, V2 groundPos, double rotationDeg, TableOptions o)
{
    double reach   = Math.Min(truth.Survey.SheetGroundWidth,
                              truth.Survey.SheetGroundHeight) * o.PositionTolerance;
    double dropped = (groundPos - truth.CentreGround).Length;
    double turned  = AngleDelta(rotationDeg, truth.RotationDeg);
    return dropped <= reach && Math.Abs(turned) <= o.RotationToleranceDeg;
}
```

- **C6.1** Position tolerance is a **fraction of the sheet's shorter ground
  dimension** (default 0.12), not an absolute distance. A detail sheet covering
  275 m and an A1 covering 1485 m should not share a tolerance in metres; as a
  fraction, both feel the same to place.
- **C6.2** Rotation tolerance is **8°** (default). Absolute, because rotation
  error does not scale with sheet size.
- **C6.3** Rotation compares modulo 360, **not** modulo 180. A sheet placed
  upside down is not placed. The Antiquarian's square detail sheet is the case
  this protects (POC-03 P2.6: "the sheet has no north indication and resolving
  orientation is part of the placement").

### 6.2 Behaviour

- **C6.4** While dragging, `Fits` is evaluated each frame. Inside tolerance, the
  sheet's edges glow gold. Outside, nothing — no colour, no message (R6.5).
- **C6.5** On release inside tolerance the sheet eases to the **exact** true pose
  over `TableOptions.SettleSeconds` (default 0.18) with a smoothstep, and is
  marked seated. The same easing `PlayerHands.Advance` already uses.
- **C6.6** On release outside tolerance the sheet stays exactly where it was
  released, unseated. No error state (R6.5).
- **C6.7** A seated sheet that is dragged again becomes unseated immediately.
  Seating is not a lock. A locked sheet is the harshest error state there is,
  and R6.5 forbids error states.
- **C6.8** The glow is a child quad scaled ~1.02 with an unlit gold material,
  enabled and disabled. No shader work, no outline pass.

### 6.3 Wrong-island sheets

Cannot be attempted (C4.3): the cabinet only ever lists the bound island's
sheets. R6.8's promise is kept structurally, not by a rejection.

---

## 7. The cabinet

Right column, per the mockups.

- **C7.1** Sections are **offices** of the bound island, in `Offices.All` order,
  labelled *Hydrographic*, *Land Survey*, *Garrison*, *POIs*. A section with no
  issued sheets is not drawn.
- **C7.2** A section header shows its title and the **count of issued sheets in
  it**. When every sheet in a section is on the table the count is replaced by
  the table mark (`2a`).
- **C7.3** A row is a thumbnail, the sheet's name, and its code
  (`CH·01` — office prefix plus `SheetId.Number`, middle dot).
- **C7.3a** The **whole-island sheet renders `<PREFIX>·IX`** — the cartographic
  term for the index, or key, sheet of a series. It has to differ, and the reason
  is structural: the whole-island survey (R2.2a) has no office of its own and
  **borrows one of the first three** (`SurveyCutter.CutWholeIsland` draws
  `Range(0, 3)`), so its sheet 1 would otherwise render exactly the code of that
  office's own sheet 1. `SheetId` already carries a `WholeIsland` flag for
  precisely this collision — its own class comment says so — and the displayed
  code must honour the same distinction. It matters most here of anywhere:
  R6.8a makes this the sheet the board cannot open without, so it is the one row
  that must never be confused with an ordinary tile.
- **C7.4** Two row states only (`2a`): **in the drawer** (plain) and **on the
  table** (gold border and tint, thumbnail tilted off-square, table mark). No
  ✓ (D-C3, D-C4). No state words — icon, tint and weight carry it.
- **C7.5** Dragging a row onto the board lays that sheet down, unseated, at the
  drop point. Dragging a slab onto the cabinet returns it to the drawer.
- **C7.6** Selecting a sheet — by clicking it on the board or its row — puts its
  name and code in the header. Nothing selected reads `None selected  —·—`
  (`1b`).

### 7.1 Sheet names

The mockups show names — *Cape Vela*, *Gull Spit*, *Cold Harbour*, *Long Reef*,
*Salt Flats*, *The Crown*, *Ember Ridge*.

**What was tried first, and why it failed.** C7.7 originally said a sheet takes
the name of the nearest *named feature*. Measured against the generator as built,
that does not work: only settlements and the **top three** peaks
(`Tuning.PeakNamedCount`) carry names, `River` has no name field at all, POIs are
unnamed by design (POC-03 §5 keeps text out of scope), and **the coastline has no
naming whatsoever** — it is a bare polyline. Nearly every name in the mockups is
coastal. The approach would have produced a cabinet of bare codes.

- **C7.7** A sheet's name is **derived from the ground the sheet covers**, not
  from a feature it happens to contain. Deterministic, seed-pure, generated —
  never stored, for the same reason nothing else geometric is (R1.11).
- **C7.7a** It lives in **`Archivist.Generation`**
  (`Naming/SheetNames.cs`), not in the table's UI layer. A sheet's name is a fact
  about the island's paperwork, not about the screen showing it: it must sound
  like the island (same `Phonology`), it must be headlessly testable, and R7.8's
  index will want the same names without reaching into `Archivist.Building`.
- **C7.7b** Randomness goes through **one new appended `StreamNames` entry**,
  `names.sheets`, indexed by a stable function of the sheet's *identity* (office,
  whole-island flag, number) so that adding or losing a sheet cannot reshuffle
  another's name. `StreamNames`' own comment is the authority here: appending is
  free, existing literals are a reproducibility contract and are never touched.
- **C7.7c** Priority: the **whole-island sheet takes the island's own name** (it
  is the outline, not a tile); a sheet containing a **named settlement** takes
  that settlement's name; everything else is **composed** from terrain character
  — land fraction, relative elevation, river and peak presence — into a generic
  noun (*Reef*, *Spit*, *Cape*, *Flats*, *Ridge*, *Crown*, *Deeps*) and one of
  four grammatical forms drawn from the stream. All four appear in the mockups:
  `<Adjective> <Generic>`, `<Noun> <Generic>`, `<Generic> <IslandWord>`, and
  `The <Generic>`.
- **C7.7d** Every sheet gets a name. There is no fallback-to-code case.

---

## 8. Input and mode

### 8.1 Opening

- **C8.1** `CartographyTable : Interactable`, label `"Open Cartography table"`,
  on the `wooden_table.glb` prefab. `CanInteract` is false while unbound and
  holding no folders — and per `notes.md`, **this is the second interactable
  that needs to refuse for a reason**, which is the trigger that note names for
  widening `CanInteract` from a bool to a reason. Do that now, and take
  `MapCrate`'s busy state out of its `Label` while you are there.
- **C8.2** A new `OpenTable` action in the **Player** map, bound to
  `<Keyboard>/c`, opens the table on `generator.LastIslandSeed`. Gated behind a
  serialized `debugShortcut` flag. **`Crouch` currently owns `c` and is read by
  no code anywhere** — take the binding.
- **C8.3** `Esc` closes, through the existing `UI/Cancel` action
  (`*/{Cancel}`, already bound).

### 8.2 The mode switch

- **C8.4** Opening disables `FirstPersonController`, `PlayerInteractor` and
  `PlayerHands` as components, and enables the `UI` map, the `EventSystem` and
  `TableCanvas`. Closing reverses it.
- **C8.5** Disable the **components**, not just the map.
  `PlayerInteractor.OnEnable` and `PlayerHands.OnEnable` call `action.Enable()`
  on individual actions; a component that wakes while the table is open would
  silently re-arm interaction underneath it.
- **C8.6** The cursor is released and re-captured by
  `FirstPersonController.OnEnable/OnDisable`, which already does exactly this.
  Do not add a second cursor owner.
- **C8.7** `PlayerInteractor.OnDisable` already clears `current` and hides the
  prompt. Nothing further is needed to clear the reticle.

### 8.3 On the board

- **C8.8** Hit-testing is `Physics.Raycast` from `BoardCamera` through the
  pointer against the `Table` layer. `SheetView`'s `BoxCollider` already exists
  and scales with the transform — reuse it, add nothing.
- **C8.9** Click selects and shows a gold outline (`1a`). Drag moves.
- **C8.10** Rotation has **two inputs**, which is R6.6's "two input methods"
  honoured in the shape this POC can afford:
  - the **corner handle** from the mockups — a UGUI knob placed at the selected
    sheet's corner via `BoardCamera.WorldToScreenPoint`, dragged to set the
    angle about the sheet's centre. Coarse and direct.
  - **`Q`/`E`**, held rather than pressed, turning the selected sheet about its
    centre at `TableOptions.SheetTurnDegreesPerSecond`. Fine and two-handed —
    the player can hold a sheet in place with the pointer and trim its angle.
- **C8.11** `Q`/`E` read the **`Turn` action moved out of the Player map**
  (D-C10, §8.4). It stays a 1D axis composite, so a future stick or shoulder
  pair binds with no code change — the reason `PlayerHands` expressed it that
  way in the first place.
- **C8.12** With nothing selected, `Q`/`E` do nothing. They never rotate the
  board, which has no rotation.
- **C8.13** No zoom, no pan (`requirements.md`). The board always frames the
  whole island.

### 8.4 What leaves `PlayerHands`

- **C8.14** A new **`Table`** action map holds `Turn` (`Q`/`E`, 1D axis
  composite), moved wholesale from the Player map. A dedicated map, not the `UI`
  map: `UI` is Unity's stock pointer/navigate map driving
  `InputSystemUIInputModule`, and game verbs put there are invisible to anyone
  reading the asset for what the table can do. `TableSession` enables `Table`
  and `UI` together and disables both on close.
- **C8.15** `PlayerHands` loses `turnAction`, `Turn()` and `heldTurn`. `Drop()`
  simplifies — its yaw becomes `transform.eulerAngles.y` alone, since there is
  no longer a turn to add.
- **C8.16** `HandlingOptions.SheetTurnDegreesPerSecond` and its default move to
  `TableOptions`. Leave nothing behind in `HandlingOptions`; a tuning value that
  nothing reads is a value someone will later tune and wonder why nothing moved.
- **C8.17** `PlayerHands`' class comment must **record** the removal, not lose
  it. The rationale it currently carries for `Turn()` — held-not-pressed because
  turning to read is a continuous adjustment, rotation about local Y because
  that is the face normal while carried — is exactly what the table's
  implementation needs, and the project's convention is that class comments
  explain why, including what was tried before.

---

## 9. Persistence

### 9.1 Order

The ledger is saved **first and independently**, so a restored board can never
reference a sheet the ledger says was never issued.

```
crate opens  →  MarkIssued  →  SAVE LEDGER
                                    ↓
player takes folders, lays them on a table
                                    ↓
sheet released / sheet seats / table closes  →  SAVE BOARDS
```

- **C9.1** On load: **ledger first, boards second**. A board entry naming a
  `SheetId` the ledger does not have issued is dropped, with a warning. Under
  the ordering above this cannot happen; it is asserted anyway, because a save
  format outlives the reasoning that made it safe.

### 9.2 Save points

- **C9.2** The board saves on: the table UI closing; a sheet seating; and
  **any sheet released from a drag**, seated or not, including a drag back to
  the cabinet.
- **C9.3** The third point is what satisfies T6 — "the player may stop at any
  moment with nothing left hanging". An unseated sheet is a legitimate resting
  state (R6.5), not unfinished work, so losing a deliberate near-miss to an
  unclean exit is exactly the failure T6 was written against.
- **C9.4** No timer, no polling, no per-frame write. Board state is a few dozen
  structs; an event-driven write is free.
- **C9.5** One archive file holding the ledger and every board, written
  atomically (temp file, then move). Per-table files can desync from the ledger,
  and C9.1's invariant is only cheap to hold if both are written together.

---

## 10. Tuning

Per the project's standing rule, one place per assembly. Feel values go in a
**`TableOptions`** ScriptableObject beside `HandlingOptions`, so they are
tunable in play mode.

| field | default | note |
|---|---|---|
| `BoardUnitsPerMetre` | 0.01 | one unit = 100 m (§3.1) |
| `BoardPadding` | 0.08 | fraction of the longer land bound |
| `SheetSeparation` | 0.004 | board units between stacked sheets (§3.3) |
| `BoardPixelsPerPaperMm` | 0.6 | serves board and thumbnail (C5.5) |
| `PositionTolerance` | 0.12 | fraction of shorter ground dimension (C6.1) |
| `RotationToleranceDeg` | 8.0 | absolute (C6.2) |
| `SettleSeconds` | 0.18 | smoothstep, as `PlayerHands.Advance` (C6.5) |
| `SheetTurnDegreesPerSecond` | 90.0 | `Q`/`E` turn rate, moved from `HandlingOptions` (C8.16) |

**No new randomness.** Nothing on the table draws from a stream, so no
`StreamNames` entry is added and no island can move because of this feature.
Board poses are player facts.

---

## 11. Build order

Each slice is independently demonstrable.

| # | slice | proves |
|---|---|---|
| **S1** | `Table` layer, `BoardRoot`, `BoardCamera`, mounting sheet, and every issued sheet laid at its **true** pose. Driven by an editor menu item — no input at all. | The transform in §3.2, and what overlap and mixed scales actually look like. **The riskiest unknown, first.** |
| **S2** | Open/close: `C` shortcut, `Esc`, the mode switch of §8.2. | The room hands over and takes back cleanly. |
| **S3** | `EventSystem` + `InputSystemUIInputModule`, `TableCanvas`, header, cabinet with thumbnails. Read-only. | The cabinet and the render budget of C5.7. |
| **S4** | Drag row → board, drag slab → cabinet, click to select, move, rotate handle **and `Q`/`E`**. The `Table` action map; `Turn` out of `PlayerHands`. No snap. | The verbs. |
| **S5** | `Fits`, the glow, the settle, unseating. | The activity. |
| **S6** | `BoardStore`, table identity, save points. | R6.10. |
| **S7** | `CartographyTable` interactable on `wooden_table.glb`, `CanInteract` widened to a reason (C8.1). | The diegetic entry. |

**S1 before anything else.** If sheets at ground scale with 20% overlap turn out
to be illegible, every later slice is built on a mistake — and that is the one
thing in this design that cannot be reasoned about, only looked at.

---

## 12. Acceptance

Measured, not argued. Findings go in `findings.md` beside this file.

- **A1** With every issued sheet seated, the assembled board reproduces the
  island: each slab's centre is within 1 board unit of
  `CentreGround * BoardUnitsPerMetre`, and its yaw within 0.1° of
  `RotationDeg`.
- **A2** Opening a table with 20 issued sheets never drops a frame below 30 fps,
  and the first texture appears within 500 ms of the view opening.
- **A3** Closing the table restores walk, look, interact and the cursor lock
  exactly as they were. Aiming at the crate afterwards still opens it.
- **A3a** `Q`/`E` do nothing to a carried sheet in the room, and turn the
  selected sheet on the board. Neither leaks into the other: pressing `Q` while
  carrying paper moves nothing, and the `Table` map is disabled whenever the
  player is walking.
- **A4** No board slab is ever destroyed, counted or swept by `SheetSpawner`.
  Open a table, open a crate, confirm floor paper stacks from the floor.
- **A5** A sheet released just outside tolerance stays exactly where released —
  measured, not eyeballed — and is still there after closing and reopening.
- **A6** Seated sheets survive a reload with no stored pose: delete the pose
  fields from the save by hand and every seated sheet still returns to its exact
  true position. This is what proves C4.6.
- **A7** `Tools/run-acceptance.sh` still passes.
- **A7a** `Archivist.Render` is untouched, and `Archivist.Generation` is touched
  in **exactly two places**: the new `Naming/SheetNames.cs`, and one appended
  `StreamNames` const. This is a deliberate amendment to A7's original "nothing
  may touch Generation" — C7.7a explains why sheet naming belongs there. The
  guard is the suite itself: A2 asserts stream independence over 100 islands, so
  an appended name that perturbed an existing feature would fail it.
- **A7b** Neither assembly gains a `using UnityEngine`. That rule is not amended
  and is not amendable — it is what lets the suite run headless.

---

## 13. Deliberately absent

Not omissions — decisions.

- **Folders.** The whole model. Sheets reach the table through the ledger
  (C1.3). The seam is `ISheetSource` (§4.3).
- **Moving sheets between folders.** A real verb, out of scope here.
- **Multiple islands on one table.** C4.3 forbids it by design for this POC.
- **Two-point pinning** (R6.6). D-C8.
- **Reference maps** (§3.7). D-C9.
- **Zoom and pan.** C8.11.
- **Sound.** R5.4 makes sound the primary confirmation everywhere else; the
  table will want it, and does not have it yet.
- **Filed-correctly.** Still absent from the ledger for the reason `notes.md`
  gives: there are no racks, so there is nowhere to be correct. Do not stub it.
