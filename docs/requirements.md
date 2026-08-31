# The Island Survey Archive — Requirements & Game Loop

Working title. Single-player, first person, no fail state, no timer.

> **Reworked on branch `rework1` (2026-08-30).** The map table, the cut of an
> island, and what a binder holds all changed. `quarters/requirements.md` is the
> authority on that model and `quarters/decisions.md` records why. Requirements
> below that it retires are marked **[retired — Qn.n]** in place rather than
> deleted; where this document and that one disagree, that one is later and
> narrower, and the disagreement is recorded, not silently fixed.

---

## 1. Premise

Islands have been surveyed many times, by different offices, over many years.
Nobody ever put the results in order. The sheets arrive in crates.

The player is the archivist. The job is to receive maps and shelve them
correctly. The archive is a fixed set of shelves in one building. There is no
visitor, no supervisor, no deadline.

There are always more islands. The sea they sit in is never drawn.

A second, optional activity exists: a map table, where the sheets an archivist
has put in order can be looked at as one island — a coast in one office's hand,
then the same ground in another's. Nobody asked for this. It is not required.
*(Q4.1–Q4.3. It was once an assembly puzzle; see `quarters/decisions.md` §1.)*

---

## 2. Lore

Kept thin on purpose. It exists to explain the objects, not to be a story.

**The islands.** Each has a name and stands alone. There is no chart of how
they sit relative to each other, and the game never draws one. Some are well
surveyed, some barely, some known only by name. New ones keep arriving; the
supply does not end.

Some islands resemble each other closely enough to be confused. The offices
confused them too.

**The offices.** Four or five bodies produced maps, at different times, for
different reasons. Each has its own drawing conventions, paper, typography,
and blind spots.

| office | interest | draws well | draws badly / omits |
|---|---|---|---|
| Hydrographic | coasts, depths, anchorages | shoreline, soundings, hazards | anything inland |
| Land Survey | terrain, boundaries | contours, spot heights, settlements | the sea |
| Harbour & Works | ports, roads, engineering | built structures, quays, bridges | open country |
| Revenue | ownership, tax parcels | parcel lines, place names | terrain entirely |
| Garrison | defence | grid, sightlines, roads, heights | civilian detail, names |

**The eras.** Three or four periods. Earlier means hand-drawn, warmer paper,
looser geometry, fewer conventions. Later means printed, cooler paper, tighter
geometry, standard sheet sizes. The islands themselves change between eras:
coasts erode, harbours are built, a settlement is abandoned.

**Why the mess.** One line, never elaborated: the collection was moved and the
order was lost. That is all the explanation the game gives.

---

## 3. Requirements

### 3.1 Island generation

The unit of generation is **one island**. Islands are independent. There is no
world geometry above them and no spatial relationship between them.

- **R1.1** `island_seed = hash(collection_seed, island_index)`. One island seed
  generates one island deterministically and completely. Nothing else is needed
  to reproduce it.
- **R1.2** The supply of islands is unbounded. `island_index` has no ceiling.
- **R1.3** An island is stored as vector data: coastline, height field, rivers,
  roads, settlements, structures. Never as images.
- **R1.4** Features exist at two scales — **island** and **local**. The
  coastline is the island scale and is always present; it is the anchor for
  every other operation. Local scale is everything inside.
- **R1.5** Every point of land must be within one island-scale feature of a
  local-scale one. Land failing this test is excluded from sheet cutting.
- **R1.6 (later, not v1)** Features carry a validity range in years, so the same
  ground renders differently for different eras. **In v1 the island is static.**
  Era survives as a drawing convention only — paper, print, typography — never
  as a change to the ground. Deferring this costs nothing structural: the
  validity field can be added to existing features later without regenerating
  anything, because the seed is unchanged.
- **R1.7** Every island has a name, a set of place names, and a **character**
  drawn from a small set — mountainous, low, atoll, fjorded, wooded, bare,
  volcanic. Character drives the generator and is recognisable at a glance from
  the coastline alone.
- **R1.8** No island is ever fully surveyed. Some ground has no sheet covering
  it at all.
- **R1.9** Islands vary in **survey depth**: a few have many surveys across
  several eras, most have two or three, some have one partial survey and
  nothing else.
- **R1.10 (siblings)** A minority of islands are generated as variants of a
  shared parent seed — same character, similar coast, different detail and
  different names. They are meant to be confusable. Siblings must be
  distinguishable on local-scale detail, never on outline alone.
- **R1.11** Island generation must be cheap enough to run on demand, and the
  result cacheable. Only the seed is persisted.

### 3.2 Sheets

- **R2.1** A sheet is a render of **one island** at
  `island_seed, centre, size, rotation, scale, office, year`. No sheet ever
  shows two islands.
- **R2.2 (reshaped — Q3.1)** Sheets are generated in **surveys**, not individually. One survey =
  one island, one office, one year, one scale, one rotation, covering a coherent
  area with a numbered set of sheets.
- **R2.2a (reshaped — Q4.4: it is the board's base, not a placeable tile)**
  Each island carries at least one survey at whole-island scale — a
  single sheet showing the full coastline. This is the entry point for that
  island and makes every later sheet placeable. It may be old, distorted, or
  damaged, but it exists.
- **R2.3 (reshaped — Q1.6)** Scale is drawn from three or four fixed values.
  Never continuous. It is now chosen per *island*, not per survey.
- **R2.4 [retired — Q1.2]** ~~Rotation is fixed per survey, not per sheet. A
  survey may follow a coast or a ridge and sit at any angle.~~ Every office now
  shares one cut on one axis, so no survey has a rotation to fix.
- **R2.5 [retired — Q1.4]** Quarters tile exactly, so there is no overlap to
  tune. The rule is struck; the measurement below is kept, because it is what
  makes Q1.4 safe.

  ~~Overlap *within* a survey is a tuning parameter
  (`paper.OverlapFraction` in `config/generation.yml`), not a fixed property of
  the world. It defaults to 20%; **0% is permitted** — sheets then tile edge to
  edge and a collection carries about a quarter fewer of them. Sheets from
  different surveys overlap freely and unevenly at any setting, because rotation,
  scale and extent are decided per survey and not by this number.~~

  *Changed from "sheets within a survey overlap by roughly 10–25%". That range
  was intent and was never a measurement; it became a parameter when the
  generator's tuning moved to a config file. Measured at 0%: A3 finds no seams
  (borders agree to 4×10⁻⁶ m), A5 finds no blank sheets across 444 of them, and
  A6 falls from 100.0% of 527 overlapping cross-office pairs to 99.6% of 242 —
  still well above its 90% floor. Nothing gated fails. The lower bound was
  removed because it turned out to be defending nothing: what R2.10a calls
  required overlap is the **cross-office** kind, which this parameter does not
  control and cannot remove.*
- **R2.6** Each office × era pair defines a **style**: line work, fill, colour,
  typography, paper stock, wear. Style is the fastest signal the player has.
- **R2.7** Two sheets covering the same ground in different styles must show the
  same underlying features, drawn differently. Neither may omit them entirely.
- **R2.8 [retired — Q4.2]** ~~Roughly one third of surveys carry an index
  diagram in the margin showing where the sheet sits. The rest do not. This is
  the main difficulty dial.~~ It was the difficulty dial for *placement*, and
  nothing is placed any more.
- **R2.9** Some surveys are incomplete: numbered sheets that do not exist.
- **R2.10 (uniqueness)** Every sheet in the collection is unique. No duplicates,
  no near-duplicates, no reprints. One sheet, one slot, always.
- **R2.10a** Uniqueness is about the *sheet*, not the *ground*. Two offices
  surveying the same coast produce two different sheets of one place, and that
  overlap is required (R2.7). It is coverage, not duplication. The overlap meant
  here is the one *between offices*, which no setting controls; it is not the
  within-survey overlap R2.5 makes tunable, and R2.5 reaching 0% does not weaken
  this.
- **R2.10b** A slot on a rack is therefore binary — filled or empty — and a gap
  in a run is unambiguous. The rack becomes a checklist made of physical space,
  with no readout needed.
- **R2.11 (cut, and Q6.3 keeps it cut)** 2–3% of sheets resist classification: damaged, unstamped, mixed
  provenance, or filed wrongly by a previous hand.
- **R2.12** Distribution is uneven. One office dominates. Some years are a flood,
  others nearly empty.
- **R2.13 (correctness)** Every sheet is truthful. The label names the island it
  shows; the stamps name the office that made it; the sheet number is its real
  place in the survey. A sheet may be *damaged* or *incomplete*, but it is never
  *wrong*. Nothing in the collection lies.

### 3.3 Rendering

- **R3.1** Sheet textures are generated on demand and cached. Only the seed and
  placement are persisted.
- **R3.2** Three levels of detail: pile (paper tone and colour blocks), in-hand
  (full), stored (end cap or corner block only).
- **R3.3** Paper, wear, and fold are authored textures blended by a condition
  value — not generated per sheet.
- **R3.4** Style parameters are shared and cached across all sheets of an
  office × era. Only the map rect and header text differ per sheet.

### 3.4 The archive

- **R4.1** Fixed capacity at start. Expansion exists but is out of scope here.
- **R4.2** Storage furniture by physical class:
  - rolled sheets → racks, end cap readable
  - flat sheets → wide drawers, corner readable
  - folders and volumes → shelves, spine readable
- **R4.3** Every stored item must have one readable face while stored.
- **R4.4** The filing system within an island is **given**, not invented:
  island → office → year → sheet number. The player recovers it; they do not
  define it.
- **R4.4a (allocation)** The *order of islands across the shelves* is **not**
  given. Islands arrive without end, so no fixed layout survives. The player
  decides where each new island goes and may re-order sections at any time.
  This is the one place in the archive where the player has authority.
- **R4.4b** Because islands are unbounded and capacity is fixed, the archive
  will eventually be full. What happens then is out of scope here, but the
  design must not assume the player can keep everything.
- **R4.5** Placement is state, not score. Any item can be moved at any time with
  no cost.
- **R4.6** Bulk operations exist: move a run, re-label a shelf, shift a section.
- **R4.7** Items may be stacked on the floor or on a table as a temporary group.
- **R4.8** An **unresolved drawer** holds items the player cannot yet place. It
  is a legitimate destination, not a penalty.
- **R4.9** No correctness readout. A shelf either looks ordered or it does not.

### 3.5 Placement of an item

- **R5.1** Picking up a stack is one motion, not one action per item.
- **R5.2** Carried weight affects movement speed and hand position.
- **R5.3** Filing an item snaps it into the row with a slight settle.
- **R5.4** Sound is the primary confirmation: varies by material, by how full the
  container is, and slightly at random.
- **R5.5** No checkmarks, no counters, no score. The tidier shelf is the feedback.
- **R5.6** Nothing collides badly. Nothing gets stuck.

### 3.6 The map table

**Superseded in full by `quarters/requirements.md` §5 (Q4.1–Q4.7).** The rules
below described an assembly puzzle: the player positioned each sheet at its true
ground pose and the game settled it against the island's vector data. That is
gone. The table now lays a binder's plates out automatically, shows one office
layer at a time, and stores nothing.

- **R6.1** Optional. The game is completable without ever using it. **(holds)**
- **R6.2 [retired — Q4.1]** ~~The player places a **copy**, not the original.~~
- **R6.3 [retired — Q4.1]** ~~A sheet can only be copied after it has been
  handled.~~
- **R6.4 [retired — Q4.2]** ~~Assisted placement, fitted against the island's
  vector data, tolerance generous and hidden.~~
- **R6.5 [retired — Q4.2]** ~~If no good fit exists, the sheet stays unsettled.~~
- **R6.6 [retired — Q4.2, D-Q1]** ~~Drag and wheel, plus two-point pinning.~~
  Rotation is disabled, not deleted.
- **R6.7 [retired — Q4.2]** ~~Fitting is always against the true island.~~
- **R6.8** **One board per island.** Boards are self-contained. There is no
  master board and no arrangement of islands relative to each other. **(holds)**
- **R6.8a** A board can only be opened once the island's whole-island sheet
  (R2.2a) has been handled. **(holds — it is now the base, Q4.4)**
- **R6.9 [retired — Q1.1]** ~~Full coverage of an island is impossible by design.
  A board can be *worked out* but never *filled*.~~ Four plates complete a layer
  and completing it is the point. Already contradicted by measurement (F-S1.8).
- **R6.10 [retired — Q4.7]** ~~Boards persist and stack up.~~ There is no board
  state: the board is a view of what is in the binders on the table.

### 3.7 Reference maps

Two kinds, and they do different jobs. Neither is a map of the world, because
there is no map of the world.

**Per-island references** — the anchor for one island's board.

- **R7.1** A reference is an item from the collection, generated by the same
  system. It is not UI.
- **R7.2** A reference shows island-scale features only — coastline, main
  ridge, main river, a few named places. Never local scale. It orients a sheet
  on the board; it never places one, and never gives rotation.
- **R7.3** References are wrong in specific ways: smooth distortion of the
  coast, dropped features, two settlements transposed, a peninsula shortened,
  occasionally something drawn that is not there.
- **R7.3a (closed — Q2.4)** Offices differ by *omission*, never by
  contradiction, so nothing needs an exemption from R2.13 and open question 11
  is answered. The original text follows.

  **(unresolved — see §6.11)** R7.3 sits against R2.13. If nothing in the
  collection lies, a reference cannot either, and the trust arc goes with it.
  Either references are exempt as small-scale sketches rather than survey work,
  or R7.3 is dropped and the board's value becomes detail rather than
  correction. This must be settled before §3.7 is built. It does not affect v1,
  where there are no separate references at all.
- **R7.4** An island may hold several partial references that disagree — a
  coastal chart precise on the shore and blank inland, a revenue sheet with
  names and no terrain, a garrison grid with no features at all. Each is strong
  in one respect and useless in others.
- **R7.5** A reference can be underlaid faintly beneath the board so
  disagreements are visible directly.
- **R7.6** The player can mark a reference: strike a feature, note a
  discrepancy.
- **R7.7** No reference is ever replaced by a correct one. The board becomes the
  better map through use.

**The index** — the journal's running list of islands.

- **R7.8** The index is a **written list, not a picture**: island name, known
  offices, known surveys, sheet counts, gaps. No geometry, because the islands
  have no arrangement to draw.
- **R7.9** The index fills as islands are encountered. It is the only
  collection-wide record and the only thing that spans islands.
- **R7.10** Sibling islands (R1.10) appear in the index as separate entries with
  similar names. The index does not warn about this. Noticing is the player's
  job.
- **R7.11** The index is the practical tool for shelf allocation (R4.4a) — it is
  how the player decides where a new island belongs among the racks.

### 3.8 Tone

- **T1** No timer, no day/night cycle, no hunger, no stamina, no currency.
- **T2** No score, no percentage, no achievement text.
- **T3** No characters on screen. No dialogue.
- **T4** Ambient sound only: the building, the paper, the weather outside.
- **T5** Interiors and light are plain and even. The style is quiet and slightly
  empty, not decorated.
- **T6** The player may stop at any moment with nothing left hanging.

---

## 4. Game loop

### 4.1 The single act — four beats

Every item passes through the same four beats. Each is tuned separately.

**Read.** Look at the sheet. Style and paper give the office and era at a
glance. The header block and stamps confirm it. The sheet number and island
name need actual reading.

Three signal ranges, one field each, never doubled up:

| range | when | carries |
|---|---|---|
| far — in the pile | glance | office, era |
| mid — in hand | short look | office confirmed, survey |
| near — inspection | deliberate | island, sheet number, year |

**Decide.** The filing system is fixed, so this resolves or it does not. Most
sheets resolve fast. Some do not, and go to the unresolved drawer.

**Move.** Gather a run of sheets that share a destination, walk once, unload.
Choosing what to carry is a second, quieter puzzle on top of the first.

**Land.** The sheet settles into the rack. Sound and the growing row of end
caps are the whole confirmation.

### 4.2 Rhythm across a few minutes

```
gather  → walk → place run → odd sheet → resolve → gather
(easy)   (rest)  (fast)      (stop)      (slow)
```

Roughly one sheet in ten should be odd. That ratio is a tuning value; find it
by playing. If everything is odd it is work. If nothing is, it is a chore.

### 4.3 A session

```
open a crate
  ↓
sort the crate roughly on the floor — by style, no reading
  ↓
work through the piles, shelving
  ↓
set aside what will not resolve
  ↓
(optional) take a few to the table, copy, place
  ↓
stop
```

The crate is the session unit. Opening one is a clean start; finishing one is a
clean stop.

**Amended — Q7.1, Q7.2.** `MapCrate` is a debug tool, not the supply. The game
starts with the collection already amassed in the room: binders and loose plates,
disordered. The session unit is therefore a *pile the player chooses*, not a
crate the game hands over. What that does to R1.2 and §4.6 is open — Q7.4.

**The island is the chapter.** A crate usually carries sheets for one or two
islands. A new island is a small event — a name that is not in the index yet,
a coastline nobody has seen, an empty board. An island is worked out and then
mostly left alone. That gives the game a shape that a single continuous region
could not: things actually finish, at a scale of a few hours, without the game
ever ending.

### 4.4 The long arc

Three things change over many hours. None is a mechanic.

**The room.** Crates empty, racks fill, floor clears. This is visible without
any display.

**The player.** Style recognition gets faster. Routes get shorter. Piles get
larger. Sheets that needed reading now resolve at a glance. Nothing on screen
records this.

**The map.** The table starts empty. The reference is trusted, then doubted,
then contradicted. Late on, the composed board is the better map and the
printed one is an object of historical interest. That transfer of trust is the
main arc of the game and it is made entirely of the player's own work.

### 4.5 What the two activities give each other

- Shelving **reveals**. A sheet must be handled before it can be copied.
- Composing **explains**. Once an island is partly assembled, its sheets are
  easier to shelve, because the ground is now familiar.

Neither requires the other. The link is one-directional pressure, not a gate.

**Amended — D-Q3.** Composing no longer explains anything, because nothing is
composed. The table shows the island the player assembled *by filing*, and feeds
nothing back into the filing. The link now runs the other way: sorting is the
work, the table is where it pays out.

### 4.6 Ending

There is no completion state, and now there cannot be one. The islands do not
run out.

What *does* finish is an island. A board gets worked out, a section fills, a
name in the index stops having gaps. That happens every few hours and it is a
real ending at a scale the player can feel.

The pressure the design must not create: the player racing an endless supply.
Crates should arrive slowly enough that the room is usually calm, and the game
must never indicate how many are left, because the honest answer is unhelpful.

The player stops when the room looks finished to them. It will not be.

**Open — Q7.4.** A pre-populated room (Q7.1) is a *finite* collection, and a
finite collection ends. Either more arrives by some later means, or §4.6 is
retired and the game becomes completable, or the pile is replenished off-screen
and never counted. Recorded as D-Q2; unsettled.

---

## 5. Version 1 scope

Everything above describes the finished shape. V1 builds the smallest thing
that is still the same game.

The test for keeping something in v1: **does the loop break without it?** Not
"is it good" — most of what is cut below is good. Depth is the last thing to
add, because it is the only thing that can be added safely later.

### 5.1 In

**Generator**
- One island per seed, vector data, deterministic (R1.1–R1.5, R1.7, R1.11)
- **Three offices, not five.** Hydrographic, Land Survey, Garrison. Chosen
  because they disagree most: sea, terrain, and grid.
- **No eras.** Style hangs off office alone. Three styles total.
- Surveys, not loose sheets (R2.2), each with a whole-island sheet (R2.2a)
- ~~Two scales, not four~~ — one scale per island, from R2.3's set (Q1.6)
- ~~Overlap within a survey (R2.5)~~ — quarters tile exactly (Q1.4)
- Every sheet unique (R2.10) — a constraint, not a feature, and it holds from
  v1 onward
- Same ground, different office, same features drawn differently (R2.7) —
  **this is the one requirement v1 exists to test**

**Archive**
- One physical class only: **rolled sheets in racks**, end cap readable
- Given filing: island → office → sheet number (R4.4)
- Player-chosen island allocation across racks (R4.4a)
- Free re-placement (R4.5), floor piles (R4.7), unresolved drawer (R4.8)
- No readout of any kind (R4.9)

**Handling**
- Stack pickup (R5.1), weight (R5.2), settle (R5.3), sound (R5.4) — all of
  §3.5. None of this is cuttable; it *is* the minute-to-minute game.

**Table** *(replaced — `quarters/requirements.md` §5)*
- One board per island (R6.8), opened by the whole-island chart (R6.8a, Q4.4)
- Plates lay themselves out from binder contents; nothing is placed (Q4.1, Q4.2)
- One office layer visible at a time, `Q`/`E` to cycle (Q4.3)
- The base shows through where no plate is owned (Q4.6)
- ~~Assisted fit, drag and wheel only~~ · ~~Copies, not originals~~

**Tone**
- All of §3.8. Free, and the whole point.

### 5.2 Out

Cut, with the reason:

| cut | requirement | why it can wait |
|---|---|---|
| Era, and features changing over time | R1.6 | The largest addition available later. Adding a validity field to existing features regenerates nothing — the seed is unchanged. |
| Sibling islands | R1.10 | Authored difficulty, and much weaker now that labels are truthful (R2.13). Confusion survives only at the table, not the shelf. |
| Index diagrams present on some surveys | R2.8 | The difficulty dial. Set it to "always present" in v1 and turn it down once the fit interaction is proven. |
| Incomplete surveys | R2.9 | Texture, not structure. |
| Unclassifiable sheets | R2.11 | Keep the unresolved drawer; give it nothing to hold yet. |
| Uneven distribution | R2.12 | One line of sampling code, added when there is something to skew. |
| Flat sheets, folders, volumes | R4.2 | Each is a second set of furniture, animation, and sound. One class proves the loop. |
| Bulk shelf operations | R4.6 | Quality of life. Painful to lack at 4000 sheets, irrelevant at 400. |
| ~~Two-point pinning~~ | R6.6 | Moot — nothing is placed at all (Q4.2). |
| Separate reference maps, and their errors | §3.7 per-island | **In v1 the whole-island sheet is the reference.** No second artifact, no distortion pass, no trust arc. The arc is the best thing in the design and it is worth building on a proven base. |
| The written index | R7.8–R7.11 | Needs many islands to mean anything. |
| Board persistence across many islands | R6.10 | Follows from the above. |

### 5.3 Not in any near version

- Archive expansion, and what happens when it fills
- Documents as a second class
- Artifacts — small hand-made set, open shelving, no spine
- Any gallery or display activity
- Anything requiring authored narrative

### 5.4 What v1 has to prove

Two things, in order:

1. **Can a player read office style at a glance, and does it get faster?**
   If the three styles do not separate at pile distance, the far signal range
   is dead and the whole rhythm collapses into reading. Test this before
   building a room.
2. ~~**Does assisted fit feel like landing, or like fighting?**~~
   **Replaced — `quarters/requirements.md` §9.** Assisted fit no longer exists. The second proof is now:
   **does filing a plate into a binder feel good four hundred times?** Filing is
   clerical by construction (Q6.4), so R5.1–R5.4 — stack pickup, weight, settle,
   sound — carry the entire minute-to-minute game with no puzzle beneath them.

Everything cut in §5.2 makes the game deeper. Neither of these two gets better
by adding any of it.

---

## 6. Open questions

Marked **[v1]** where the answer blocks the first version.

1. **[v1] Sheets per island.** Low enough that an island resolves in one
   sitting, high enough that the racks look like an archive. Guess: 30–60.
2. **[v1] Walk speed and rack spacing.** The dullest question here and the one
   most likely to sink the feel. Cannot be reasoned about; only played.
3. ~~**[v1] How wrong a drop can be and still settle.**~~ **Closed — Q4.2.**
   Nothing is dropped; there is no tolerance to tune.
4. Ratio of odd sheets to easy ones. Not a v1 question — v1 has no odd sheets.
5. Whether references are all emergent, or two or three planted contradictions
   are authored for the moments a player will remember.
6. Whether a player-compiled index sheet — a new reference drawn from their own
   board — is worth building, or whether it makes the arc too neat.
7. **Crate rate.** With an unbounded supply, the arrival rate is the only thing
   standing between a calm room and a treadmill. Probably player-triggered — a
   crate arrives when the player fetches one — rather than on a clock.
8. **What full means.** Fixed capacity plus unbounded islands guarantees the
   archive fills. Out of scope here, but the two honest answers are expansion
   or deaccession, and deaccession is the more interesting one.
9. **How many islands are live at once.** One island per crate keeps sessions
   clean but makes the room monotonous. Two or three overlapping is probably
   right, and it is a generator parameter, not a design decision.
10. **Sibling frequency.** R1.10 is the sharpest tool in the generator and the
   easiest to overuse. Rare enough to be a discovery, not a tax.
11. ~~**Does R2.13 bind references too?**~~ **Closed — Q2.4.** It binds
   everything, and no exemption is needed: offices differ by *omission*, so the
   arc is what each hand leaves out rather than what any of them gets wrong.

12. **[new] What happens to the supply?** See Q7.4 / D-Q2. A pre-populated room
   is finite; R1.2 says the islands are not. Unsettled, and it decides the long
   arc.

13. **[new] Does filing feel good enough?** §5.4's second proof. It cannot be
   reasoned about, only played.