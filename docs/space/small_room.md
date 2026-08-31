# The small room (L)

**Status: proposed, not built.** The next phase. One room, small, laid out so it
reads as a place that stores things — built entirely from blockout, with no new
mesh and no art direction. It is the cheapest way to answer R4.1, which has been
blocking S3.2, W6, W8 and the real room since before the rework.

Numbers are **L**, so they can be cited the way S-, R- and K-numbers are.

---

## 1. What "plausible" means here (L1)

- **L1.1** **The plan, not the look.** A room is plausible when its *layout*
  reads as an archive: aisles you walk down rather than a floor you see all of at
  once, rack runs with ends, a door, sightlines that are blocked by furniture.
  None of that needs a material.

- **L1.2** **The look is out of scope, and not by preference.** `Models/` is
  empty; every object in the room is a primitive. S4.1 (art direction) is
  deferred, and S4.3 says the mesh import contract changes *retroactively* once
  it resolves. Commissioning or authoring a mesh for this phase would be work
  done against a contract that does not exist yet.

- **L1.3** Consequence: **the phase can fail its own test and still be right.**
  If the room is laid out well and still reads as a grey box, that is evidence
  about S4.1, not about L1.1. Record it; do not fix it here.

---

## 2. The inversion (L2)

- **L2.1** S3.2 states the room as a function of capacity:

  ```
  S3.2:  room size = f(archive capacity, slot pitch, rack height, aisle width)
  ```

  and names archive capacity (R4.1) as the missing input. **This phase runs it
  backwards.**

  ```
  L2.1:  room size = f(what reads as an archive)   →   capacity falls out
  ```

- **L2.2** The reason is arithmetic, and it survives not knowing the rack. A
  binder is an island (R20), so a rack holds as many islands as it has slots.
  Take a deliberately pessimistic rack — four rows of ten — and a room that
  merely *looks* like an archive, eight to fourteen racks: that is already three
  to six hundred islands. **Capacity is nowhere near the binding constraint;
  plausibility is, by more than an order of magnitude, at any rack size anyone
  would model.** Deriving the room from capacity sizes it from the slack
  variable.

- **L2.3** So R4.1 becomes a **measured finding of this phase**, not a decision
  taken before it — with one factor still open. The room is built to look right
  and the racks are counted; capacity is that count times the slots per rack,
  which L3.1 defers to the asset. **The phase delivers the rack count, and the
  multiplication waits.** That is the half that has been missing.

- **L2.4** *Rejected: decide the island count first.* Honest to S3.2 as written,
  and it is how the doc has been blocked for months — the number has nothing to
  check it against, so choosing it is guessing with extra steps. L2.1 produces a
  number that at least came from something.

- **L2.5** What this does **not** settle is Q7.4 / D-Q2 — whether the supply is
  finite. A room that holds nine hundred islands is still a room; whether it
  starts full, fills over time, or never ends is the long arc, and it is
  untouched by how big the building is.

---

## 3. The rack, which does not exist yet (L3)

- **L3.1** **Slots per rack is an asset decision, and it is not made here.**
  `PF_Shelf` is a test mock — its 6 × 18 grid was authored to have something to
  aim at, and nothing in this phase may cite it as a number. How many binders a
  rack holds is settled when the rack is modelled, against S4.1.

- **L3.2** **The phase does not need that number, because layout depends on the
  rack's envelope, not on its contents.** Aisle spacing, run length, sightlines
  and how many racks fit a wall are all functions of **footprint and height**. A
  rack that holds sixty binders and one that holds ninety occupy the same room
  if they are the same size. This is why L1.1 can be answered before the asset
  exists.

- **L3.3** **S3.6 bounds the row count independently of the asset**, and it
  agrees with the four-to-five expectation. The reach band is 0.3 – 2.0 m, so
  1.7 m of usable height:

  | rows | max row pitch | opening, at a 0.10 m shelf |
  |---|---|---|
  | 4 | 0.425 m | 0.32 m |
  | 5 | 0.340 m | 0.24 m |
  | 6 | 0.283 m | 0.18 m |

  A binder standing spine-out wants roughly a 0.25 – 0.32 m opening, which puts
  **four or five rows in the band and six out of it**. The constraint is S3.6
  and R4.3 — a row above 2.0 m stores something with no readable face the player
  can reach — not a preference about furniture.

- **L3.4** **What the phase needs is a placeholder with an honest envelope**: a
  box of the right footprint and height, named per S5.5 so the day a real rack
  arrives, finding every one of them is a `grep`. It carries a `Shelf` so the
  room is walkable and aimable, and its grid numbers are explicitly *not* a
  claim.

- **L3.5** **Smaller racks mean more of them, and that is the point.** Ten
  narrow racks read as an archive where four wide ones read as a wall of
  shelving. It also moves the room toward K1.3's ~20-rack migration trigger
  faster — see L6.2.

---

## 4. What gets built (L4)

- **L4.1** **`RoomBuilder` grows a plan.** Today it emits a 10 × 10 shell. It
  becomes a function of `(rack count, rack footprint, aisle width, run length,
  ceiling)` that lays out runs, leaves the crate and the table somewhere a
  person would put them, and fills `Anchors/` (S3.4) with one anchor per rack.

- **L4.2** The argument is the one already made twice — `RoomBuilder`'s own
  header and `ShelfTools`. **Provisional numbers have to be cheap to rebuild**,
  and every number in this phase is provisional by construction, because L2.3
  says the room is tuned by looking at it.

- **L4.3** **A door, or at least a doorway.** A room with four blank walls reads
  as a box whatever is in it. It need not open or lead anywhere; it needs to
  say which wall is the front.

- **L4.4** **Nothing in this phase changes how a rack is built.** `Shelf`'s
  edit-time slot construction is untouched; the placeholder's grid is set to
  something reachable per L3.3 and re-run, and that is the extent of it.

---

## 5. The proof (L5)

- **L5.1** **It is a walking test, and that is deliberate.** W3's proof was a
  looking test and the rework overview called it the one that mattered most.
  The question here is the same shape: *walk the room and say whether it stores
  things or displays them.* No assert can answer it.

- **L5.2** Two things it should be possible to say afterwards, having walked it
  and not having built it:
  - which aisle you are standing in, without counting from a wall;
  - roughly how much of the archive you have not seen from where you stand.

  If the whole room is legible from the door, it is a display case.

- **L5.3** Measured alongside: rack count, object count against K1.1's table,
  scene load time, and an A6-style metric sanity check on the crossing time.
  Capacity is *not* measured here — L2.3 — it is a multiplication left open.

- **L5.4** Findings go in `docs/space/small_room_findings.md`, measured, and
  they win over this document.

---

## 6. What this phase does not do (L6)

- **L6.1** **No art, no material language, no mesh.** L1.2.
- **L6.2** **It does not trigger `racks_at_scale`.** K1.3 puts the migration at
  about twenty racks; this room is deliberately under it, which is the whole
  reason the built per-slot design survives the phase. **Twenty racks is a
  ceiling on this phase, not a target** — if the layout wants more, that is a
  finding that K2 is due, and it stops being a room-building phase.
- **L6.3** **It does not settle the supply** (L2.5).
- **L6.4** **It does not size or model the rack** (L3.1).
- **L6.5** **It does not build W6.** Filing a loose plate at a rack is the next
  phase and it is unblocked by this one, not part of it.

---

## 7. Before it starts (L7)

- **L7.1** **The open scene is stale in three ways** and would be judged instead
  of the room:
  - 108 `Volume` cubes remain under the rack's slots, referencing a material
    that no longer exists — cleared by *Archivist/Rebuild Shelf Slots*;
  - a `BoardInteractor` carrying a missing script, and `TableCanvas.interactor`
    pointing at nothing — cleared by re-running the rig builder;
  - the second and later `BindingAnchors` on the table prefab (F-R19.3).
- **L7.2** **`docs/rework1/00-overview.md` is stale**: §3 and §5 row 4 both say
  W6 is blocked because *"there are no racks"*. `Shelf`, `ShelfSlot` and
  `PF_Shelf` exist. Correct it, or the plan argues against work that is done.

---

## 8. Open (L8)

- **L8.1** **The rack's footprint** — the one number about the rack this phase
  does need (L3.2), and the only one. A guess is fine and is expected to move
  when the asset arrives; it should be written down as a guess.
- **L8.2** **Aisle width.** Nothing in S1–S7 fixes it. Player radius is 0.3 m
  (S2.1), so anything above ~0.9 m is walkable and the rest is how it feels.
- **L8.3** **Whether the room is one room.** R1 says one building; a building
  with two rooms and a doorway between them is far more plausible than one large
  room, and costs a wall. Not decided, and it should be decided by building the
  single room first.
