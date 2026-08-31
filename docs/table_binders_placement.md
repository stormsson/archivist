# Binders on the Cartography Table — Specification

> **Amended (2026-08-30, branch `rework1`).** B1.1–B1.8 mostly survive. Three
> changes: a binder's identity becomes `island · office`, not island alone
> (Q3.1); B1.3's "identity is its island" becomes the *goal* state reached by
> merging (Q3.3); and B1.1's capacity must never gate which offices can be
> compared (Q4.5). §11's "opening the board" now opens a view, not a workspace
> (Q4.1). See `quarters/requirements.md` §4.

Construction. `UI/cartography_table/requirements.md` is the authority on intent
for the table, `UI/cartography_table/spec.md` on the board it opens, and
`requirements.md` §3.6 on the activity. This document is the authority on the
**physical half**: what happens when the player walks up to the table holding a
binder, a loose sheet, or nothing.

Read `architecture.md` first. This assumes `Archivist.Building` as built and the
determinism contract in `generation_for_agents.md`.

Requirements here are numbered **B**n.n. Existing numbers (R, S, C, T, F, P, D)
refer to their own documents. Where this document disagrees with
`UI/cartography_table/spec.md` the disagreement is **recorded in §14**, not
silently resolved — that spec is not edited by this work.

**This feature is the folder model** that `spec.md` §13 lists as *deliberately
absent* and that §4.3 held the `ISheetSource` seam open for. C4.1–C4.4 stop
being deferred and become enforced by the object.

---

## 1. What this is

A table holds **binders**. Binders are what the player carries, so the table's
contents are folders, not paper (D-C1). A table with binders on it is bound to
their island, offers their sheets to the board, and says so when asked for
anything else.

### 1.1 Settled decisions

| # | decision |
|---|---|
| B1.1 | Capacity is the **child count of `BindingAnchors`**, read at runtime. Never a constant, never a serialised number. |
| B1.2 | Binding is **contents**, not identity: the table is bound because binders are lying on it (C4.2), not because it carries a `tableId`. |
| B1.3 | A binder's **identity is its island**, not its contents. An empty binder binds a table exactly as a full one does. |
| B1.4 | Anchors are **stacked**, not spread. Binders pile up; the pile is the only part of the table's state the player can see. |
| B1.5 | Occupancy is a **stack**: fill the first free anchor, remove the topmost. No holes, no re-packing. |
| B1.6 | The table has **three verbs**, chosen by what is in the hands: binder → place, loose sheet → file, empty → open the board. |
| B1.7 | Every refusal is a **refusal with a reason** (`InteractionState.Refused`), not a silent unavailability. |
| B1.8 | Filing a sheet **consumes the paper**. Only the `SheetId` survives, inside the binder. |

---

## 2. Deviations recorded

| id | against | what | why |
|---|---|---|---|
| **D-B1** | `BinderSpawner.Place` | The yaw jitter of a binder placed on the table is **not deterministic**. It is re-rolled from `UnityEngine.Random` on every placement. | The floor pile jitters from `binder.Number` so a reported floor layout can be reproduced from a bug report. A table is not a layout worth reproducing: the player put those binders there, one at a time, and a binder taken off and put back **should** lie differently — that is the whole tell that it was handled. Nothing about an island depends on it, so **no `StreamNames` sub-stream is involved** and R1.11's contract is untouched. This is presentation, not generation. |
| **D-B2** | reversibility | Filing a loose sheet into a binder is **irreversible in-world**. | The paper is destroyed and the `SheetId` kept (B9.4). There is no "take a sheet out of a binder" verb, and `spec.md` §13 puts moving sheets between folders out of scope. A filed sheet cannot be paper again in that session. Recorded rather than fixed: the reverse verb is a real feature, not an oversight, and it needs a place to put the sheet down. |

---

## 3. What was actually there

A correction of record, kept because the bug was invisible from the outside and
will otherwise be re-derived by whoever reads the old code.

**Before this work, clicking the table while holding a binder appeared to show
that binder's contents. It did not.** `CartographyTable.Interact` ignored the
player's hands entirely and called `TableSession.OpenCurrentIsland()`, which
opens `generator.LastIslandSeed` and fills the cabinet from
`LedgerSheetSource` — *every sheet ever issued of the last island the archive
drew*. Holding a binder changed nothing about which island opened or which
sheets were listed.

The two agreed only for the binder from the **most recent** crate opening, and
were already wrong by one even there, because `MapCrate`'s `looseDebugSheet`
issues a sixth sheet onto the floor that never enters the binder.

Three ways it was visible, all cheap to re-run:

1. Open the crate twice, carry the **first** binder to the table — the board
   opens on island #2.
2. Click the table with **empty hands** — an identical result, which is the
   proof that the hands were never read.
3. **Count the cabinet** against the binder: 6 rows for a 5-sheet binder.

Everything in this document exists so that the table opens on what is lying on
it.

---

## 4. Anchors and capacity

- **B4.1** Binding anchors are children of a `BindingAnchors` empty inside
  `PF_CartographyTable`. Each carries a `PlacementAnchor` (S3.4).
- **B4.2** **Capacity is `BindingAnchors.childCount`, read at runtime.** Adding
  a binder slot is a prefab edit made by eye: duplicate an anchor, drag it until
  the gizmo's footprint sits where a folder should lie. That is exactly what
  `PlacementAnchor` exists for — its class comment records that an *invisible*
  anchor was 0.81 m too low with no way to see it. A capacity constant would
  make the second half of that edit a code change, and the two would drift.
- **B4.3** Anchor order is **sibling order**, so anchor *n* is child *n*. Nothing
  else orders them.

Today there are exactly two, both under `BindingAnchors`, table-local:

| anchor | local position | yaw |
|---|---|---|
| `BinderAnchor1` | (−0.755, **0.8146**, 0.205) | 56.517° |
| `BinderAnchor2` | (−0.755, **0.860**, 0.205) | 62.528° |

Same x and z, **45 mm apart in y**, each with its own hand-authored yaw. They
are a **stack**, not two places on the table: a second binder lands on top of the
first, slightly turned, the way a folder dropped on a folder actually lies. The
yaws are authored per anchor and are not a formula — they were judged by eye, and
§6's jitter is applied *relative* to them so they stay the intended base look.

---

## 5. Placing a binder

The verb when the player interacts with the table **holding a `BinderView`**.

- **B5.1** **Unbound table** (no binders on it): the binder takes the first
  anchor and its island **binds** the table (C4.1, C4.2).
- **B5.2** **Bound table, same island**: the binder takes the next free anchor.
  Its sheets join what the table offers (§13).
- **B5.3** **Bound table, different island**: refused —
  `"This table is laid out for <island name>"` (C4.3 finally spoken aloud rather
  than enforced structurally).
- **B5.3a** The refusal names the **table's** island, never the binder's. The
  player can read the table's state off the pile in front of them but has no way
  to read what is in their own hands; the sentence has to supply the half they
  cannot see. The name comes from the first binder's `BinderView.IslandName`,
  which is a memo of a pure function of the seed and costs no generation; where
  it is empty, fall back to the seed in hex, as `BinderView.Summary` does.
- **B5.4** **No free anchor**: refused — `"No room on this table"`.
- **B5.5** An **empty binder (0 sheets) is accepted**, and it binds (B1.3). A
  binder's identity is its island, not its contents — `BinderView` already
  enforces one island per binder for exactly this reason. Refusing an empty
  binder would mean a table that accepts a folder, has its last sheet filed
  elsewhere, and retroactively should not have accepted it.

- **B5.6** The anchor is **reserved before the travel starts**, not on arrival.
  The glide takes a third of a second (§7), which is long enough for the player
  to pick up a second binder and aim again; two binders sent to one anchor would
  land inside each other with nothing to say which was on top.
- **B5.7** **On landing the binder is parented to its anchor**, world position
  kept. Two things depend on this and neither is decoration: the pile travels
  with the table if the table is ever moved, and `GetComponentInParent` reaches
  the table from anything sitting on it — which is the whole mechanism of §10.
  `PlayerHands.HandOver` unparents on release (`SetParent(null)`) and does not
  re-parent, deliberately: the hands are handed a pose, not a table. Seating is
  the table's, in the landing callback.

Placement is therefore: reserve the anchor, hand the binder over (§7), seat it
on landing, and log one line.

---

## 6. The rotation of a placed binder

- **B6.1** The placed rotation is
  `anchor.rotation * Quaternion.Euler(0f, Random.Range(-20f, 20f), 0f)` —
  jitter **relative to** the anchor's hand-authored yaw (§4), never replacing it.
  The anchor stays the intended look; the jitter is the handling.
- **B6.2** It is **re-rolled on every placement**. A binder taken off and put
  back lies differently, which is what makes the pile look handled rather than
  authored.
- **B6.3** Position is the anchor's exactly. Only the yaw varies: a binder that
  slid off its anchor would look dropped, not placed.

See **D-B1** for why this is deliberately not deterministic, and why that costs
R1.11 nothing.

---

## 7. The travel from the hands

- **B7.1** A placed item **glides** from the hands to the anchor pose, eased on
  the same smoothstep `PlayerHands.Advance` uses for the take, so putting down
  reads as the reverse of picking up.
- **B7.2** Duration is `HandlingOptions.binderPlaceSeconds` (**0.35 s**), which
  sits beside `sheetTakeSeconds` (0.28 s) because this is a handling verb and the
  hands own every other take/put duration in the game. A shade slower than the
  take: setting something where it belongs is a more deliberate movement than
  lifting it off a floor.
- **B7.3** **Not `ItemFall`.** A fall snaps the item to the target's X and Z on
  its first frame and descends under gravity with a sway. That is right for
  letting go over a floor and wrong for setting something on a surface a metre in
  front of the eye, where it reads as a teleport followed by a small unexplained
  hop.
- **B7.4** **The hands still do not know what a table is.** They are handed a
  pose. `Drop()` is *letting go* — the item chooses its own resting place through
  `ICarryable.RestingPose`; `HandOver()` is *giving the item to something that has
  already decided*.
- **B7.5** `HandOver` deliberately does **not** call `ICarryable.Settled()`. For
  a binder that means `BinderSpawner.Register` — counting it as part of the
  **floor** pile, which would make the next dropped binder come to rest in
  mid-air above a table. The collider still comes back on when the item arrives,
  because a placed binder does need to be aimed at again (§10).

---

## 8. Taking a binder back

- **B8.1** Aiming **anywhere at the pile with empty hands takes the topmost
  binder** — whichever binder's collider the ray actually hit. You cannot pull
  the bottom folder out of a pile, so this is what the object already looks like
  it does.
- **B8.2** LIFO is also what keeps §4 honest: occupancy never has holes, so "the
  first available anchor" is always the count of what is on the table. No
  re-packing pass, and no binder left floating over a vacated anchor below it.
- **B8.3** **Removing the last binder returns the table to unbound** (C4.4). The
  last one off is the first one on — the binder that bound it — so the island
  leaves with the object that brought it.

---

## 9. Filing a loose sheet — a new verb

The verb when the player interacts with the table **holding a `SheetView`**.
This is how loose paper gets sorted into binders.

- **B9.1** The sheet is filed into the **first binder on the table** (anchor
  order). Not the topmost: the first binder is the one that bound the table, and
  it is the one the board's cabinet has been listing from the beginning.
- **B9.2** **Same island only.** `BinderView.Add` already enforces it; the table
  produces the same refusal as B5.3, `"This table is laid out for <island name>"`,
  because it is the same fact.
- **B9.3** **No binder on the table**: refused — `"No binder on this table"`.
  **Already in that binder**: refused — `"Already filed"`. Both are things a
  player will genuinely do, which is why `BinderView.Add` returns false rather
  than throwing.
- **B9.4** **The paper is consumed.** The sheet travels to the binder (§7) and
  its `GameObject` is destroyed on arrival; only the `SheetId` survives, inside
  the folder. That is the R1.1/R1.11 bargain — a sheet is a pure function of its
  island's seed, so the identity *is* the whole of it, and keeping the slab would
  be caching what can be recomputed.
- **B9.5** The consequence is worth knowing and is recorded as **D-B2**: this is
  irreversible in-world.

---

## 10. Aiming — a placed binder is its table's face

- **B10.1** `PlayerInteractor` resolves whatever its ray hits with
  `GetComponentInParent<Interactable>()`, so a binder parented under the table
  **shadows the table**. Left alone, the pile — the obvious place to aim, and the
  only part of the table whose state the player can see — would be the one spot
  that refuses.
- **B10.2** Therefore `BinderPickup`, when it finds a `CartographyTable` among
  its parents, **forwards `Label`, `CanInteract` and `Interact` to that table**.
  A placed binder stops being a thing you take and becomes the table's face; §8's
  removal is then the table's empty-handed verb on the pile, not the binder's own
  pickup verb. The alternative — disabling the binder's collider on the table —
  was rejected because it makes the pile unaimable and the table's own collider
  is under the tabletop, not on the paper.

---

## 11. Opening the board

- **B11.1** With **empty hands**, a bound table opens on **its own** island,
  through its binders:

```csharp
session.Open(BoundSeed, new BinderSheetSource(this));
```

- **B11.2** An **empty table is refused** — `"Nothing on this table"`. This is
  **C8.1 finally able to be true**: `CartographyTable`'s own comment records that
  it refuses nothing today only because the table had no contents to refuse for,
  and that "no folders on this table" would have been a permanent state dressed
  up as a temporary one. It is now temporary — one binder fixes it.
- **B11.3** The **`C` debug shortcut is unchanged and deliberately keeps the
  ledger path** (C8.2): it exists to look at an island **nobody has filed**, which
  is precisely the case a binder-fed table cannot show.
- **B11.3a** To stop the two paths inheriting each other's cabinet through
  `BoardView`'s single serialised source field, **the source becomes an argument
  of `TableSession.Open`**. A serialised default that the last caller overwrites
  is a global by another name: open the table from a binder, press `C`, and the
  debug island would list the binder's sheets.

---

## 12. Where the code lives

- **B12.1** All of it on **`CartographyTable`**, with a `readonly
  List<BinderView>` kept in step with the anchors and scanned from the anchor
  children in `Awake` (so a binder authored onto the prefab is found).
- **B12.2** **The reversal is recorded, not hidden.** `CartographyTable`'s class
  comment argues at length that the table is *deliberately just a verb*, and that
  a table id and an island binding were built there and **removed**. This restores
  the binding — but driven by **real contents** this time rather than by a
  serialised identity minted in `OnValidate`. The identity was the part that was
  genuinely hard (preview scenes, `OnValidate` never firing on scripted
  instances) and it is **not** coming back; see §14 and that class comment, which
  stays as the record of why.
- **B12.3** **The hazard the list carries.** `BinderSpawner.Awake` calls
  `ClearAll()` on every binder in the scene at start, and
  `UnityEngine.Object.Destroy` is deferred to end of frame — so an `Awake` scan
  can capture binders that are already doomed and will be null a frame later.
  **The list is pruned of nulls on read.** Not on write, and not by ordering
  `Awake` calls: script execution order is a scene setting that a later prefab
  edit silently breaks, and the symptom here is a `MissingReferenceException` from
  a capacity check.
- **B12.4** Nothing new goes into `Archivist.Generation` or `Archivist.Render`.
  This feature is entirely engine-side (§15).

---

## 13. `BinderSheetSource`

- **B13.1** A new `BinderSheetSource : ISheetSource`, in
  `Building/Runtime/Table/` beside `LedgerSheetSource`. It returns a fresh,
  **de-duplicated** list of the placed binders' `Contents`, in **anchor order**,
  filtered to the asked-for seed. De-duplicated because two binders of one island
  may hold the same `SheetId` and the cabinet must not draw two rows for one
  sheet.
- **B13.2** **It copies**, per the `ISheetSource` contract — the reason
  `LedgerSheetSource`'s comment gives applies here unchanged: an interface is only
  as strong as its loosest implementation, and a caller holding a live list across
  a placement gets either a stale cabinet or an exception mid-`foreach`.
- **B13.3** The island filter is a **no-op in practice** — C4.3 means every binder
  on the table is the bound island — and is kept as a **guard**, so a future table
  that relaxes C4.3 does not make the cabinet wrong silently.

---

## 14. Where this disagrees with `UI/cartography_table/spec.md`

That document is not edited by this work. Per CLAUDE.md — *later never overrules
earlier; where they disagree, that is recorded* — the disagreements are listed
here so a reader of either finds the other.

| against | what changed | what stands |
|---|---|---|
| **§13 "Deliberately absent" — Folders** | The folder model has **partly arrived**: binders lie on a table, bind it to an island (C4.1–C4.4, now enforced by the object), and feed the cabinet. C1.3's "no folder model in this POC" is no longer true of the physical half. | What did **not** arrive: **moving sheets between folders** (still out of scope; filing is one-way, D-B2) and **multiple islands on one table** (still forbidden by C4.3, now with a sentence attached). |
| **§4.3 — `FolderSheetSource`** | The class built is named **`BinderSheetSource`**. | Only the name. The seam is exactly the one §4.3 specified, and the UI layer still never references `SheetLedger`. The physical object in this game is called a *binder*, and the code should say what the player is holding. |
| **§4.1 — serialised `tableId`** | **Still absent.** Binding comes from the binders lying on the table, not from an identity minted on first validate. | C4.1–C4.4 are satisfied without it. §4.1's identity is only needed by §4.2's `BoardStore`, and that is wired to nothing. |
| **§4.2 — `BoardStore`; C4.4 "discards its board state"** | **C4.4 is satisfied vacuously.** No board state is persisted at all, and closing the table already loses it, so emptying a table discards nothing that was going to survive anyway. | For C4.4 to *mean* something, three things must become true together: `BoardStore` wired to a real save (§9), a table identity to key boards by (§4.1), and a board that outlives its own closing. Until then, do not read "discards its board state" as a tested behaviour — nothing exercises it. |
| **§8.1 — C8.1** | C8.1 said `CanInteract` is **false** while unbound and holding no folders. It is implemented as a **refusal with a reason** — `InteractionState.Refused("Nothing on this table")` — not a silent unavailability. | The intent. C8.1 is also the note that widened `CanInteract` from a bool to a reason in the first place; a wordless refusal here would be the one place the widening was not used. |

---

## 15. How this is verified

**None of it can run in the headless acceptance suite.** `Tools/run-acceptance.sh`
runs `Archivist.Generation` and `Archivist.Render`, which never reference
`UnityEngine`, and this feature is entirely `Archivist.Building` — hands,
colliders, prefab anchors, `Interactable`. That is not a gap in the suite; it is
the assembly boundary working as intended (A7b). The suite must still pass
untouched.

Verification is **play-mode, by hand, in `POC04_Room`**, with **one console line
per event** in the register `MapCrate` and `BinderPickup` already log in
(`[Binder] took Binder_2 — Driftcombe, 5 sheets`).

The branch walk, in order, one session:

1. **Place.** Open a crate, take the binder, aim at the table, place it. It
   glides to `BinderAnchor1`, turned off the anchor yaw.
2. **Add a second.** Open the crate again — same island — and place the second
   binder. It lands on `BinderAnchor2`, on top, turned differently.
3. **Third refused.** A third binder of that island: `"No room on this table"`.
4. **Wrong island refused.** A binder of another island:
   `"This table is laid out for <the table's island>"` — check it names the
   table's island, not the one in hand.
5. **File a loose sheet.** Pick up the crate's loose debug sheet and interact:
   it travels to the pile and its GameObject is gone. Interact again with a sheet
   already in that binder → `"Already filed"`; with a sheet of another island →
   the B5.3 refusal.
6. **Take both back.** Empty hands, aim at the pile: the **top** binder comes off
   first, whichever collider was hit. Then the second. The table is unbound again;
   opening it now reads `"Nothing on this table"`.
7. **The two numbers that matter.** With binders on the table, open the board
   with empty hands and confirm the **cabinet count tracks the pile** (it grows
   when a binder is added and by one when a sheet is filed), and that the board
   opens on **the pile's island**, not on `generator.LastIslandSeed` — which is
   the §3 bug, restated as a test.
8. **`C` is unchanged.** Press `C` with a bound table in the room: it still opens
   the last island drawn, through the ledger, and the cabinet it shows is not the
   binder's (B11.3a).
