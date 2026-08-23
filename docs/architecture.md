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
| `SheetLedger` / `SheetLedgerStore` | Which sheets have been *issued*, per island. Component + engine-free store. Not yet persisted. |
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

### Handling — carrying and placing

| entity | scope |
|---|---|
| `PlayerHands` | What the player is carrying. One sheet for now; grows into stacks, weight, settle. |
| `SheetPickup` | The verb attached to a sheet on the floor. Deliberately not on `SheetView`. |
| `SheetFall` | Release → settle. Scripted, not a `Rigidbody`: the resting place is decided at release. |
| `HandlingOptions` | Feel values, as a ScriptableObject asset so they can be tuned in play mode. |

### Interaction

| entity | scope |
|---|---|
| `IInteractable` / `Interactable` | Narrowest contract: a label, a gate, an act. |
| `PlayerInteractor` | Aim, reach, button. One ray per frame from the eye; walls block it. |
| `InteractionPrompt` | Draws the aim label. The POC's only screen text. |
| `MapCrate` | Aim, press, and an island comes into existence — unseen — followed by some of its sheets on the floor. |
| `FirstPersonController` | Walk and look. Nothing else. |

## 5. Editor tooling

| entity | scope |
|---|---|
| `Editor/IslandDebugWindow` + `IslandPane`, `SheetPane`, `TexturePane`, `ComparePane` | Look at an island, its sheets, its raster, and two offices side by side. |
| `Editor/SheetContent`, `VectorDraw`, `FeatureLabels`, `OfficeStyle`, `SvgExport` | How the debug window draws and exports. |
| `Building/Editor/RoomBuilder` | Builds the POC-04 room from the constants in `space/requirements.md`. Geometry as a function of the spec. |
| `Building/Editor/SheetTestBench` | Summons a named case — `LandSurvey:7` — on demand. Drives the shipping path, not a parallel one. |
| `Building/Editor/SheetSceneGuard` | Strips spawned sheets before a scene is written to disk. |

## 6. Scenes and assets

- `Building/Scenes/POC04_Room.unity` — the only scene.
- `Building/Options/HandlingOptions.asset` — the tuning asset.
- `Tools/run-acceptance.sh`, `Tools/GenHarness/` — headless generation + render checks.
