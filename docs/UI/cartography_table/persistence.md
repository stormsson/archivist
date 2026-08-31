# The save — as built

> **SUPERSEDED (2026-08-30, branch `rework1`).** Q4.7 reduces the save to two
> facts: **what is in each binder** (`island · office → quarter → SheetId`) and
> **which binders are on which table**. Sheet poses, board state, group tables
> and fuse history are not saved because they no longer exist. §2's room
> invariant — every issued sheet is somewhere, and somewhere once — still holds
> and is still worth auditing. See `../../quarters/requirements.md` §5.

**Status: as built.** `spec.md` §9 (C9.1–C9.5) is the intent and is not edited
here. `groups_spec.md` §15 asked for an analysis before the code; this is that
analysis and the record of what the code decided. Where this document and §15
disagree, §15 stands as what was thought at the time and this says what was
built — per the project's rule that later never overrules earlier.

---

## 1. What is saved

One JSON document, `archive.json`, under `Application.persistentDataPath`,
holding the ledger, every board and the room (C9.5). Written through
`archive.json.tmp` and moved into place, so there is never a moment with half an
archive on disk.

**Three sections, and the order is the argument** (C9.1): `ledger`, `boards`,
`room`. Each depends only on what came before it — no board may name a sheet the
ledger never issued, and no binder may hold one — so a reader that stops early is
short of paper, never short of the record that justifies it.

```jsonc
{
  "archive": 1,
  "ledger": [ { "seed": "0123456789ABCDEF", "index": 7, "sheets": 11,
                "name": "…", "issued": [ "<sheet>", … ] } ],
  "boards": [ { "table": "<guid>", "seed": "…", "nextGroup": 3,
                "groups": [ { "id": 1, "office": "LandSurvey", "whole": false,
                              "onTable": true, "rotation": φ, "x": tx, "y": ty,
                              "members": [ "<sheet>", … ] } ],
                "placed": [ { "sheet": "…", "x": …, "y": …, "rotation": … },
                            { "sheet": "…", "seated": true },
                            { "sheet": "…", "group": 1 } ] } ],
  "room": { "nextBinder": 5,
            "binders": [ { "number": 1, "seed": "…", "island": "…",
                           "where": "table", "table": "<guid>", "anchor": 0,
                           "pose": { "x": …, "y": …, "z": …, "rx": …, "ry": …, "rz": … },
                           "holds": [ "<sheet>", … ] } ],
            "sheets": [ { "sheet": "…", "where": "floor", "pose": { … } } ] }
}
```

A sheet is one string — `SEED/Office/part|whole/N`, the four fields of `SheetId`
and nothing derived — so a placement stays a flat object and a member list a
list of names.

**JSON, and hand-written rather than `JsonUtility`.** `JsonUtility` is
UnityEngine, and the format may not be: the stores, the room record and
`ArchiveFormat` are engine-free so the whole save runs in the headless suite.
It would also derive the file's shape from the classes' shape, which is
backwards — the file is a format with its own compatibility story. `Json.cs` is
the reader and writer, pretty-printed two spaces a level, because nobody has to
read a save and everybody eventually does.

**Order is content.** Islands in the order the archive met them, sheets in the
order they were issued, placements in lay order — which is draw order (§3.3,
C4.7) — a group's members in join order, a binder's contents in filing order.
Arrays keep all of it for free.

**A damaged record costs that record.** Text that will not parse is refused
whole; a single entry missing a field or naming an unknown office is dropped
with a warning that names it (`boards[0].placed[2]: bad sheet, skipped.`) and
the rest of the save survives. A file that cannot be read costs the player
everything; one bad entry costs them one drag.

**R1.11 is upheld more strongly than before groups.** A nine-sheet assembly is
one frame and nine names. A seated placement is `{ "sheet": …, "seated": true }`
— its pose is `Sheet.CentreGround`/`RotationDeg` and is looked up (C4.6, D-C7).
A6 asked that the pose fields could be deleted by hand and every seated sheet
still return: there are no pose members on a seated placement to delete.

## 2. The room, and the invariant it exists for

**Every issued sheet is somewhere, and somewhere once.** The ledger says a sheet
has entered the world; the room says where it went. Without the second half the
first is a claim about paper that does not exist, which is why the room is in
the file and not deferred.

There are exactly three places paper can be, and they are three states rather
than variations of one:

| where | what is kept |
|---|---|
| **in a binder** | the binder's number, island and contents in filing order; the binder's own place — a table's anchor, or a floor pose |
| **on the floor** | one identity and the pose it was dropped at |
| **in the hands** | one identity, and no pose: the hands are the place |

A sheet **on a board** is not a fourth place. A board slab is drawn from an
identity, and the document itself is in the binder the board was opened from —
so a board placement is never a location for paper.

`RoomSnapshot.Audit` checks all of that against the ledger and **reports rather
than repairs**: paper that was issued and then lost, paper in two places, and
paper the archive never handed out. None is recoverable — a missing sheet has no
pose to invent, a duplicate no way to say which copy is real — so `Archive` says
so loudly in the console and leaves the room as the file described it.

**A binder costs nothing to restore and a loose sheet costs an island.** A binder
holds identities and comes back in one frame. A sheet on the floor has to be
*looked at*: its island is regenerated (~340 ms, engine-free, off the main
thread) and the sheet rasterised, so the floor's paper arrives on a coroutine,
one upload per frame, while the room is already walkable. `Archive` refuses to
write while that is running — a save taken halfway through would record the
sheets that had landed and forget the rest.

**Restored, not re-placed.** `SheetSpawner.Restore` and
`CartographyTable.Restore` exist because the ordinary paths are wrong for a
memory: `Place` scatters by batch index and stacks on what is already down,
`LayOnFloor` probes downward for a pile it is halfway through rebuilding, and
the table re-rolls the jitter that decides the angle a binder lies at. Every
pose in the file is one the player made.

**The view is still not saved.** Zoom and pan reset on every opening (G10.1) —
where someone last scrolled to is not a fact about the archive.

**Still outside the file:** where the player is standing, and anything in the
room that is not paper.

## 3. The five questions §15.2 left open

| § 15.2 asked | answered |
|---|---|
| **Load-time dissolution** — a group that falls below two members needs a survivor pose the store cannot compute. | The group dissolves and **its remaining member's placement is dropped**: that sheet comes back in the cabinet. The survivor's pose is `frame ∘ truth` (G3.1) and needs the island, which load does not have and must not regenerate. A sheet in the drawer is a state the player undoes with one drag; a sheet at an invented pose is one they cannot tell from a real one. Under C9.1's ordering the case cannot arise — it is handled because a file outlives the reasoning that made it safe. |
| **C9.2's save points extend** to fusing, parking, retrieving, releasing a group move. | All four save. A **group frame edit is not a save point on its own** — `MoveGroup` runs every frame of a drag and of a settle. The gesture's *end* is the save point: the release (`BoardInteractor.Release`), the join once settled (`Commit`), the park, the retrieval, and letting go of `Q`/`E`. One write per gesture, never per frame (C9.4). C9.2's second point, a sheet seating, has no producer today (§13, G1.9); the gesture that would make one ends in the same release. |
| **Where a parked group belongs** — table, island, or archive. | **The table.** C1.7 is the constraint and it settles it: board state is keyed by table identity, never by island. A parked assembly is in that table's cabinet drawer and goes with that table's board. |
| **A tuning change moves the ground under a saved collection.** | Nothing changes. A stored frame re-derives onto the new ground (§4.3), poses are in ground metres and degrees, and the file holds no board units. A group whose members no longer overlap after a regeneration is left exactly as it is: it is a player fact, and nothing in the model or in the fit test asks it to still be fusable. Absolute correctness is out of scope (G1.9, §16). |
| **Whether a group should survive `Hide`.** | It does. `Hide` destroys slabs, meshes and textures and touches no placement, no assembly and no frame. Closing a table is not "the deliberate act of clearing it" — **C4.4's act is taking the last binder off**, and that path calls `BoardStore.Clear` and saves. |

## 4. Table identity (§4.1, previously absent)

`CartographyTable` carries a serialised `tableId`, a GUID minted on first
validate in a real scene. The guards its own class comment demanded are all
present: prefab asset, prefab stage, and `IsPreviewSceneObject` —
`PrefabUtility.LoadPrefabContents` loads into a preview scene where the first
two are false, and an id minted into the prefab asset is inherited by every
instance, which is two tables silently sharing one board.

`OnValidate` never fires on an instance created through scripting, so `TableId`
falls back to `scene:hierarchy/path` — stable across runs, unique among tables,
and it costs a board if the table is later moved in the Hierarchy. A context
menu mints a fresh id for a table that was duplicated and arrived holding its
original's.

A board opened without a table — the `C` debug shortcut, a bench — is keyed per
`BoardView` instance. It is saved like any other and comes back as a different
board next session, because nothing in the room can point at it and say which
one it was.

This is what makes `table_binders_placement.md` §14's three conditions true
together: `BoardStore` wired to a real save, a table identity to key boards by,
and a board that outlives its own closing. **C4.4 is no longer satisfied
vacuously** — taking the last binder off a table now discards something.

## 5. Where the code is

| file | scope |
|---|---|
| `Building/Runtime/Collection/Archive.cs` | The save file as a thing in the scene. Finds itself after the scene loads, reads once, and writes at every save point. Engine side. |
| `Building/Runtime/Collection/ArchiveFormat.cs` | The document, in and out. Engine-free. |
| `Building/Runtime/Collection/Json.cs` | Just enough JSON, engine-free: a one-pass pretty-printing writer and a parser that never throws. |
| `Building/Runtime/Table/BoardSnapshot.cs` | One board flat, plus what a restore had to throw away. Engine-free. |
| `BoardStore.Snapshot` / `.Restore` | The store's two ends of the file. Builds a board directly rather than replaying gestures: a replay would renumber every assembly (G4.2) and reshuffle the pile (C4.7). |
| `BoardView.Relay` | One sheet back onto the board as its raster lands (C5.7). Reads the model, never writes it. |
| `BoardView.CommitPose` | A loose sheet's transform into the model, silently. C6.6 says a release that fits nothing produces no feedback; C9.2 says it saves. This is both. |
| `Building/Runtime/Collection/RoomSnapshot.cs` | The room flat: binders, loose paper, the counter, and the audit. Engine-free. |
| `Building/Runtime/Collection/RoomPaper.cs` | The room read off the scene and put back into it. Engine side, and the only place that starts a coroutine to do it. |
| `BinderSpawner.Recreate` / `.AdoptNextNumber` | A binder with the number it already had, and a counter that never rewinds. |
| `SheetSpawner.Restore`, `CartographyTable.Restore`, `PlayerHands.Adopt` | The three doors a memory comes back through, beside the three a gesture goes through. |

## 6. Save points

One write per gesture, never per frame (C9.4), at the end of the gesture rather
than at its start — a resting pose is decided at release (R5.6) and reached a
third of a second later, and the file records where things are standing.

| gesture | where it saves |
|---|---|
| a crate opens | `MapCrate`, after `MarkIssued` — the ledger first (C9.1) |
| paper picked up | `PlayerHands.Take` |
| paper dropped | `PlayerHands.Land` — on landing, not on release |
| a binder onto a table | `CartographyTable.Seat` — on arrival |
| a sheet filed | `CartographyTable.Consume` |
| the last binder off a table | `CartographyTable.Discard` — C4.4, which also clears that table's board |
| a sheet or assembly released on the board | `BoardInteractor.Release` |
| a fuse | `BoardInteractor.Commit`, once settled |
| a group parked or retrieved | `BoardInteractor` |
| `Q`/`E` let go | `BoardInteractor.Turn` — the one verb with no release |
| the table closes | `TableSession.Close` |
| the game quits | `Archive.OnApplicationQuit` |

## 7. What was measured

Round-trip over a store holding a laid sheet, a seated sheet, an on-table
assembly and a parked one, across two tables:

- write → read → write is byte-identical;
- lay order, join order, group ids and `NextGroupId` all survive;
- a laid pose returns exactly (`1234.5, -678.25, 33.75°`);
- a parked assembly returns parked, with its members off the board;
- with a ledger that issued only two of the six sheets, both assemblies
  dissolve, the unissued sheets are dropped, the survivor of a dissolved pair
  goes back to the cabinet and the loose issued sheet stays;
- a file that is not an archive is refused whole; a bad line inside a good file
  costs that line and warns.

The room, same suite (S8, S9):

- a binder keeps its number, island, table, anchor, pose and filing order;
- on a table, on the floor and in the hands stay three distinct states;
- a binder that does not say where it is is dropped whole, warns once, and
  costs nothing else;
- the audit counts lost paper, doubled paper and never-issued paper separately,
  and names each.

All of it runs headless in **0.1 s**, no editor and no island generated:
`Tools/run-acceptance.sh save`, and it is part of `gate` and `all`. The harness
compiles the seven engine-free files the save is built from, which is also what
enforces their staying engine-free — a `using UnityEngine` in any of them is a
broken build rather than a comment somebody stopped believing.

Not measured, and needing the editor: A5 (a near miss is still there after
closing and reopening), A6 as written (the pose fields deleted by hand), and the
room actually coming back — a crate opened, paper scattered, a binder on the
table, one in the hands, then quit and reload.
