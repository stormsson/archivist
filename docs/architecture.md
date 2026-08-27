# Architecture — As Built

Map of the code as it stands. One line per entity, scope only. Deeper detail
lives in `generation_for_agents.md` (generator), `poc02/spec.md` (renderer) and
`space/requirements.md` (the 3D half).

---

## 1. Assemblies

| assembly | path | references | engine |
|---|---|---|---|
| `Archivist.Generation` | `Assets/Archivist/Generation/` | — | **never** |
| `Archivist.Render`     | `Assets/Archivist/Render/`     | Generation | **never** |
| `Archivist.Building`   | `Assets/Archivist/Building/Runtime/` | Generation, Render | yes |
| `Archivist.Building.Editor` | `Assets/Archivist/Building/Editor/` | Building, Generation, Render | Editor |
| `Archivist.Editor`     | `Assets/Archivist/Editor/`     | Generation, Render | Editor |
| `Archivist.Tests`      | `Assets/Archivist/Tests/`      | Generation | Editor |

The engine-free rule on the first two is load-bearing: it is what lets the whole
acceptance suite run headless via `Tools/run-acceptance.sh`.

---

## 2. Generation — what an island *is*

Pure function: one seed → one island, completely. Nothing geometric is ever
persisted; only the seed.

| entity | scope |
|---|---|
| `Island` | Facade. `Generate(collectionSeed, index)` / `FromSeed(seed)`. Exposes `Params`, `Field`, `Coastline`, `Features`, `Names`, `Service`, `Surveys`. |
| `Field/` | The height field: `IslandField`, `IslandParams`, `IslandCharacter`, `Noise`, `Falloff`. |
| `Geometry/` | Engine-free maths: `V2`, `Rect2`, `Segment`, `Polyline`, `MarchingSquares`, `Pca`. |
| `Determinism/` | The contract: `Pcg32`, `Hash`, `Streams`, `StreamNames`, `Q`. Named sub-streams so an unrelated draw cannot move the island. |
| `Features/` | What is on the island: `Peaks`, `Rivers`, `Settlements`, `Poi`, `Lattice`, `ServiceRule`, `TotalOrder`. |
| `Sheets/` | Cutting surveys: `Office`, `SurveyCutter`, `CoastWalkCutter`, `DetailSheetCutter`, `SheetFormat`, `MapScale`, `FeatureMatrix`. |
| `Analysis/` | `IslandDigest`, `SheetNumbering`, `ContourSeam` — measurement, used by the harness. |
| `Naming/` | `NameGenerator`, `Phonology`, `IslandNames`. |
| `Tuning` | Every generation constant in one place. |

## 3. Render — what an island *looks like*

Deterministic raster, no engine types. `RenderTuning` holds the constants.

| entity | scope |
|---|---|
| `IslandRenderer` | The pipeline: normalise → fill → stroke. Takes an `Island`, not a seed — per-island normalisation needs the highest peak. |
| `RenderRequest` / `RenderLod` | What to draw and how finely. |
| `ImageBuffer` / `Rgba` / `PngWriter` | The raster and its output. |
| `Palette`, `Bands`, `Ink`, `Strokes`, `FillRenderer`, `FieldCoast`, `GroundImage` | Layers of the drawing. |

## 4. Building — the room the player walks in

`Building/Runtime/`, namespace `Archivist.Building.*`. This is POC-04 (space)
and POC-05 (interaction).

### Collection — what the archive knows

| entity | scope |
|---|---|
| `IslandGenerator` | The scene's one source of islands. Owns the cache and the ledger as two children. |
| `IslandCache` | Generated islands, kept so a seed is not rebuilt twice. Disposable; losing it costs time, never correctness. |
| `SheetLedger` / `SheetLedgerStore` | Which islands the archive has met and which of their sheets have been *issued*, in the order both happened. Component + engine-free store. Also the collection's own account of itself: holdings, counts, progress. Saved by `Archive`. |
| `Archive` | The save file as a thing in the scene: the ledger and every board, in one file, written at the points `UI/cartography_table/spec.md` §9 names. Finds itself after the scene loads; nothing has to be wired to it. |
| `ArchiveFormat` | That file as JSON, in and out. Engine-free, so a save can be read, edited by hand, and exercised headlessly. |
| `Json` | Just enough JSON for it: a one-pass pretty-printing writer and a parser that never throws. Hand-written because `JsonUtility` is UnityEngine, and because the file is a format rather than a mirror of the classes. |
| `RoomPaper` / `RoomSnapshot` | Every piece of paper in the room, read off the scene and put back into it: binders, what is filed in them, where each one lies, loose sheets, and what the player is carrying. Component + engine-free record. `RoomSnapshot.Audit` checks the invariant the ledger needs — every issued sheet is somewhere, and somewhere once. |
| `IslandHolding` | One island's row as a value: seed, index, name, issued / total, percent. A snapshot of the ledger, for whatever screen lists the collection. |
| `SheetId` | A sheet's identity as a value: island, office, whole-island flag, number. Outlives every regeneration. |
| `SheetLookup` | The walk back: `SheetId` → the `Sheet` it names, by regenerating the island. |
| `SheetPicker` | Chooses which unissued sheets a crate delivers. Pure, off-thread. |

### Sheets — paper as an object

| entity | scope |
|---|---|
| `SheetView` | One sheet as a physical object, at true size. Owns its mesh, material and texture. |
| `SheetMesh` | The slab: a box with its underside at local y = 0. One surface, so it cannot z-fight itself. |
| `SheetTexture` | Composites map onto paper stock and uploads. The runtime's one vertical flip. |
| `SheetRender` | Worker-thread → main-thread carrier. Holds no engine types. |
| `SheetSpawner` | Puts rendered sheets on the floor, and enforces that a scene never *starts* with paper on it. |

### Binders — the folder the sheets arrive in

The player's physical item is the **folder**, never the sheet (cartography table
spec §13, D-C1). A crate delivers one binder, not a pile of paper.

| entity | scope |
|---|---|
| `BinderView` | One binder: a number, an island, and a list of `SheetId`. Holds identities, never geometry or rasters. One island per binder, enforced on `Add`. |
| `BinderSpawner` | Makes binders, owns the `Binder_n` counter, puts them on the floor. Sweeps binders at scene start for the reason `SheetSpawner` sweeps sheets. |

### Handling — carrying and placing

| entity | scope |
|---|---|
| `ICarryable` | What the hands can hold: a root, a collider, a name, and *where this comes to rest when released*. Implemented by `SheetView` and `BinderView`. |
| `PlayerHands` | What the player is carrying — an `ICarryable`. Grows into stacks, weight, settle. |
| `SheetPickup` / `BinderPickup` | The verb attached to a thing on the floor. Deliberately not on the view. |
| `ItemFall` | Release → settle. Scripted, not a `Rigidbody`: the resting place is decided at release. Was `SheetFall`; a binder falls by the same rules. |
| `HandlingOptions` | Feel values, as a ScriptableObject asset so they can be tuned in play mode. |

### Interaction

| entity | scope |
|---|---|
| `IInteractable` / `Interactable` | Narrowest contract: a label, a gate, an act. |
| `PlayerInteractor` | Aim, reach, button. One ray per frame from the eye; walls block it. |
| `InteractionPrompt` | Draws the aim label. The POC's only screen text. |
| `MapCrate` | Aim, press, and an island comes into existence — unseen — followed by a **binder** of its sheets. Plus one loose sheet while `looseDebugSheet` is on, so there is something to file into a binder once that verb exists. |
| `CartographyTable` | The verb that will open the board view. A stub, and says so. |
| `FirstPersonController` | Walk and look. Nothing else. |

## 5. Editor tooling

| entity | scope |
|---|---|
| `Editor/IslandDebugWindow` + `IslandPane`, `SheetPane`, `TexturePane`, `ComparePane` | Look at an island, its sheets, its raster, and two offices side by side. |
| `Editor/SheetContent`, `VectorDraw`, `FeatureLabels`, `OfficeStyle`, `SvgExport` | How the debug window draws and exports. |
| `Building/Editor/SceneParts` | The player, the prompt, the collection and the crate, built the same way for every scene. Shared so a second scene is the same rig, not a copy of one. |
| `Building/Editor/RoomBuilder` | Builds the POC-04 room from the constants in `space/requirements.md`. Geometry as a function of the spec; everything that is not room comes from `SceneParts`. |
| `Building/Editor/GeneratorSceneBuilder` | Builds `Debug_Generator`: the crate, the collection and a player on a bare platform, for working on generation with nothing else in the scene. |
| `Building/Editor/SheetTestBench` | Summons a named case — `LandSurvey:7` — on demand. Drives the shipping path, not a parallel one. |
| `Building/Editor/SheetSceneGuard` | Strips spawned sheets — and the binders holding them — before a scene is written to disk. |
| `Building/Editor/GlbImporterSetup` | Points every `.glb` under `Assets/Models` at glTFast's importer. Re-run after adding a model. |

## 6. Scenes and assets

- `Building/Scenes/POC04_Room.unity` — the game's scene.
- `Building/Scenes/Debug_Generator.unity` — the crate alone, rebuilt by
  `GeneratorSceneBuilder`. Editable only through its builder: re-running overwrites it.
- `Building/Options/HandlingOptions.asset` — the tuning asset.
- `Models/Placeholders/*.glb` — placeholder art. Imported through glTFast; see `GlbImporterSetup`.
- `Tools/run-acceptance.sh`, `Tools/GenHarness/` — headless generation + render checks.
- `<persistentDataPath>/archive.json` — the save. Not in the project; see
  `docs/UI/cartography_table/persistence.md` for what is in it and what is not.

### Prefabs — authored assets, edited in the Inspector

`RoomBuilder` builds the *room* from the numbers in `space/requirements.md`,
because those are spec-derived and have to be cheap to rebuild. **Item prefabs
are not built that way.** They are ordinary assets in `Building/Prefabs/`, opened
and tuned in the editor like any other prefab — size, pivot, collider and verb
are all things you judge by looking at them, and a generator would overwrite the
judgement on its next run.

| asset | root components |
|---|---|
| `PF_Binder.prefab` | `BoxCollider`, `BinderView`, `BinderPickup`, + a `Visual` child instanced from `classic_paper_envelope.glb`. Wired into `BinderSpawner.binderPrefab`. |
| `PF_CartographyTable.prefab` | `BoxCollider`, `CartographyTable`, + a `Visual` child instanced from `wooden_table.glb`. Instanced in the scene. |
| `PF_Archive_Room_Debug.prefab` | The room. This one *is* built by `RoomBuilder`. |

Both item prefabs keep their verb in the `Interactable.label` field rather than a
constant, so the wording is changed in the Inspector.
