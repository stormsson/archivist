# The Quarter Model — Requirements

Intent. This document is the authority on **what a plate is, what a binder is,
and what the table does**. It supersedes `requirements.md` §3.6 and the whole of
`docs/UI/cartography_table/` except `findings.md`, which is measured and stands.

Requirements here are numbered **Q**n.n. Existing numbers (R, S, C, T, F, P, D,
B, G) refer to their own documents. `decisions.md` records why each of these was
chosen and what was rejected; it is the argument, this is the rule.

Read `../requirements.md` first — the premise, the offices, and the tone are
unchanged. What changed is the geometry underneath them.

---

## 1. Why this exists

The map table was taxing rather than relaxing, and three things caused it:

1. **The target was invisible.** A sheet had to be placed at its absolute ground
   pose on a board whose ground is never shown (R1.2, R7.2). `groups_spec.md`
   §1.1 — removed by this rework, recoverable at `main` — measured the target at
   **2.6% of the board's width**, with nothing on screen indicating where it was.
2. **The volume.** F-S1.3 measured **49 sheets** on one solved board.
3. **It paid nothing.** Nothing in the loop read a finished board.

Two compensations had already been built for (1) — relative fitting, then
capture-widening assisted snap (G1.8, superseded G7.1). Two escalating
compensations for one problem indicated a wrong premise, not a tuning miss.

The premise that was wrong: **that the player should reconstruct the island's
geometry.** They should not. The geometry is given; what the player recovers is
*order*.

---

## 2. The island, cut

| # | rule |
|---|---|
| **Q1.1** | An island is cut **2 × 2** over its land bounds. Four quarters: NW, NE, SW, SE. |
| **Q1.2** | The cut is **shared by every office**. One axis, one origin, one set of four rects per island. An office does not choose its own extent, rotation, or seams. |
| **Q1.3** | The cut is **2 × 2 on every island, forever**. It does not scale with island size, character, or survey depth. The player learns one frame once and it holds for the life of the game. |
| **Q1.4** | Quarters **tile exactly**. No overlap, no shared margin, no ground drawn twice. |
| **Q1.7** | A quarter with **no land in it still gets a plate**, and that plate is blank. The land bounds are a rectangle and an island is not, so a fjorded or crescent island can leave a whole corner of its bounding box empty. The office surveyed that square and found nothing, which is truthful (R2.13) — and Q1.3's four quarters, every island, forever, is worth more than never printing an empty sheet. Measured at 1 plate in 328. |
| **Q1.5** | Paper is **A1 for every plate**, in whichever orientation suits the island. Orientation is not size: the sheet, the binder and the rack are the same object either way, and every plate of one island shares the choice, so Q1.2's register holds. |
| **Q1.6** | Scale is chosen **per island**: the finest rung of **1:5000, 1:10000, 1:25000** at which one quarter fits the map area, trying both orientations at each rung. A small island sits in blank margin; a large one crowds its sheets. Never continuous, never per office. |

Q1.4 is measured-safe: R2.5 already recorded `paper.OverlapFraction` at 0% —
*"A3 finds no seams (borders agree to 4×10⁻⁶ m), A5 finds no blank sheets across
444 of them."*

Q1.6 makes physical size legible without a scale bar, which suits a game with no
readouts (R4.9). Nothing ever compares two islands' scales, because R1.4 and R6.8
forbid islands having any relationship at all.

---

## 3. The offices, as layers

| # | rule |
|---|---|
| **Q2.1** | An office is a **subset of `Render.LayerMask`** over the same ground, not a separate drawing of it. Hydrographic ≈ `Coast \| Soundings`; Land Survey ≈ contours + `Rivers` + `Settlements` + `Peaks`; Garrison ≈ grid + roads + `Peaks`. |
| **Q2.2** | **No relief banding.** A plate is ink on paper stock, not a colour relief map — F-S1.7 measured the renderer producing the second where the mockups show the first. *Amended*: an office may lay a **flat two-tone wash** over the half of the ground it surveys (R23), which is `Fill` with a palette that has no banding — land is exactly the paper it would have been. What the rule forbids is the relief, not the fill. |
| **Q2.3** | An office produces **four plates per island** — its four quarters. **One** whole-island chart exists per island, not per office (Q4.4): it is made by whichever office surveyed the island wholesale, and it is the board's base. Plates per island are therefore `offices x 4 + 1` — **13** at v1's three offices, **21** at five. |
| **Q2.4** | Offices differ by **omission, never by contradiction**. Two offices' plates of one quarter register perfectly and disagree only in what each bothered to draw. |
| **Q2.5** | The collection grows by **adding offices, never by cutting finer**. Q1.3 is fixed; the depth axis is the office list. |
| **Q2.6** | Paper stock, ink and typography stay **distinct per office** (R2.6). With geometry now identical across offices, style is the *only* thing separating layers. |

Q2.2 settles **F-S1.7**, which recorded that `IslandRenderer` was drawing
`LayerMask.All` including `Fill` while the mockups showed pale paper and fine ink.

Q2.4 settles **open question 11 / R7.3a**. A reference no longer needs an
exemption from R2.13, because no artifact ever lies: an office is silent about
what it does not survey. The trust arc survives as *what each hand omits*.

V1 ships **three offices** (§5.1), for the reason §5.4 gives: five styles is more
art before the style-at-a-glance question is answered.

---

## 4. The binder

| # | rule |
|---|---|
| **Q3.1** | A binder's identity is its **island**. Its contents may span **one office or every one** — four quarters of the Hydrographic, or a whole island in every hand it was surveyed in. Which offices are in it is read off the contents, never stored. |
| **Q3.2** | Two binders may name the same island and hold different plates. Uniqueness (R2.10) is unbroken: no *sheet* repeats, only the container. |
| **Q3.3** | Binders of one island can be **merged**. The end state is one spine per island, holding every office. **Merging is done at the map table, not at the rack.** The rack is for filing: a binder goes into a slot or comes out of one, and nothing else happens there. |
| **Q3.4** | Merging is **tidiness and nothing else**. No slot is freed as a mechanic, no capacity is unlocked, no counter moves. The game never suggests it, never marks a binder as unmerged, and never rewards it. **Comparison never waits on it**: a binder that arrives holding two offices already shows two layers. |
| **Q3.5** | The filing hierarchy of R4.4 is unchanged; the office level simply moves *inside* the binder. The rack orders by island; the binder orders by office. |

Q3.3 makes B1.3 — *"a binder's identity is its island"* — the **goal state**
rather than the starting condition. The premise is that the collection was moved
and the order was lost; merging is the one act that undoes that rather than
accommodating it.

Q3.4 is load-bearing against Q4.5: if table capacity ever gated comparison,
merging would become compulsory through the back door.

---

## 5. The table

| # | rule |
|---|---|
| **Q4.1** | The board is a **view, not a state**. Set binders on the table and their plates lay themselves out in register, instantly. Take them off and they go back. |
| **Q4.2** | **Nothing is placed, fitted, rotated, or snapped.** There is no tolerance, no assisted fit, no fusing, no groups. |
| **Q4.3** | **One office layer is visible at a time.** `Q`/`E` cycles which. Diegetically this riffles the pile on the table. |
| **Q4.4** | The **base plate** lies under everything: the island's whole-island chart (R2.2a), one per island, made by whichever office surveyed it wholesale, filed in that office's binder. It never improves. |
| **Q4.5** | Table capacity (B1.1) must **never gate comparison**. Satisfied by Q3.1 rather than by capacity: a table may take one binder, because one binder can hold every office. |
| **Q4.6** | Where the player owns no plate, the **base shows through**. A thin layer is visibly thin; a complete one is edge-to-edge ink. |
| **Q4.7** | Nothing about the board is persisted. The only state is **what is in each binder** and **which binders are on which table**. |

Q4.4 settles **F-S1.6**, which measured the whole-island sheet at 19.0 × 12.9 km
for a 6.9 km island — 564% of the land area — and concluded it could not be a
placeable tile. It becomes the underlay it always wanted to be. At 1:25 000 only
the coast survives, so it reads black-and-white in any office's hand: neutral in
effect, not by rule. R6.8a's gate survives — no chart, no board.

Q4.6 makes the state of a whole island readable in about two seconds by flipping
`Q`/`E`: a complete layer and a one-quarter layer are categorically different
pictures. That is the readout, and it is made of the maps.

**R6.9 is retired.** *"Full coverage of an island is impossible by design"* was
already contradicted by measurement (**F-S1.8**, 100% coverage across 24 892
samples). Under Q1.1 four plates complete a layer, and completing it is the
point. Boards fill, deliberately.

---

## 6. Completion

| # | rule |
|---|---|
| **Q5.1** | The reveal is the reward: when the fourth quarter lands, the gaps close and the island is whole in that hand for the first time. |
| **Q5.2** | A completion may have a **physical** tell — a binder holding four plates is genuinely fatter, and R5.3/R5.4's settle-and-sound vocabulary applies. It may **not** have a marker, colour, badge or stamp that appears only at 4/4. That is T2's achievement text wearing wood. |
| **Q5.3** | The index (R7.8) may record it in writing — *Driftcombe — Hydrographic, complete*. The index is a document the player consults, not a notification. |

---

## 7. What the player actually does

| # | rule |
|---|---|
| **Q6.1** | **Loose plates exist.** The player reads a plate — office from stock and ink (R2.6), island and quarter from the header — and files it into the right binder, at the rack. |
| **Q6.2** | Binders also arrive **intact**. Some of the collection survived its move; some spilled. The supply is mixed. |
| **Q6.3** | Every plate is **truthful and legible** (R2.13). There are no damaged plates whose identity must be recovered. **R2.11 stays cut.** |
| **Q6.4** | There is therefore **no manual placement anywhere in the game**. Filing is clerical by design; sorting is the activity. |
| **Q6.5** | The search that sends a player down the racks is **retrieval**: fetching this island's other binders to the table, where they are compared and merged (Q3.3), and, later, **requests** (§8). |

Three grains of sorting: plate into binder, binder onto shelf, binder into
binder. Q6.1 is what keeps the primary activity supplied with material — under a
binder-atomic model an island is five objects, and sorting loses its subject.

Q6.4 has a cost that must be stated: **R5.2–R5.4 (weight, settle, sound) now
carry the entire feel of the primary activity, with no puzzle underneath them.**
See §9.

---

## 8. The room, and where the paper comes from

| # | rule |
|---|---|
| **Q7.1** | The game **starts with the collection already in the room**: a large quantity of binders and loose plates, amassed and disordered. The player does not wait for material; they arrive to a mess. |
| **Q7.2** | `MapCrate` is a **debug tool**, not the supply. It stays as a development affordance and is not the game's delivery mechanism. |
| **Q7.3** | The starting population is **generated at load**, never authored into the scene. `SheetSpawner` and `BinderSpawner` enforce that a scene never *starts* with paper on it, and `SheetSceneGuard` strips spawned paper before a scene is written to disk. Those guards stay; population is a runtime pass that runs after them. |

**Q7.4 — unresolved.** Q7.1 sits against **R1.2** (*"the supply of islands is
unbounded, `island_index` has no ceiling"*) and **§4.6** (*"there is no completion
state, and now there cannot be one"*). A room pre-filled with a fixed pile is a
**finite** collection, and a finite collection ends. The three honest positions:

- the room's pile is the *visible* collection and more arrives by some later
  means, keeping R1.2;
- the collection is genuinely finite and §4.6 is retired, making the game
  completable;
- the pile is unbounded in practice — replenished off-screen so the room is never
  empty and never counted.

This does not block building the model, but it decides what the long arc is, and
it must be settled before the room is populated for real.

---

## 9. What v1 has to prove

§5.4's original pair was style-at-a-glance and assisted fit. **Assisted fit no
longer exists**, so the second slot is empty and this replaces it:

1. **Can a player read office style at a glance, and does it get faster?**
   Unchanged and now heavier: under Q1.2 the geometry is identical across
   offices, so style (Q2.6) is the only thing that separates them.
2. **Does filing a plate into a binder feel good four hundred times?**
   Filing is clerical by construction (Q6.4). R5.1–R5.4 are carrying the whole
   minute-to-minute game with no puzzle beneath them. If the settle and the sound
   do not hold up, the primary activity does not either.

---

## 10. Build order

1. The 2 × 2 quarter cutter, replacing `CoastWalkCutter`'s ribbons (Q1.1–Q1.4)
2. Per-island scale selection (Q1.6)
3. The office → `LayerMask` mapping, `Fill` off (Q2.1, Q2.2)
4. Two new render layers: contours and grid/roads — `Lattice.cs` exists, roads do
   not (Q2.1)
5. Binder identity becomes `island · office`; `BinderView` enforces one island
   today and must enforce one office too (Q3.1)
6. Merge, as a verb (Q3.3)
7. The table as a view: lay out from binder contents, `Q`/`E` cycling, base
   showing through (Q4.1, Q4.3, Q4.6)
8. Room population at load (Q7.1, Q7.3)

Deletions are listed in `decisions.md` §4. The plan that executes all of this —
order, parallelism, and what blocks what — is `../rework1/`.

---

## 11. Deliberately absent

- **Requests.** Parked. `PoiKinds` — `SeaArch`, `Spring`, `StandingStones`,
  `RuinedChapel`, `RuinedTower`, `Cairn`, `RuinedJetty` — is the natural key: a
  request resolves to one island, one quarter, and the offices that draw POIs.
  Three constraints when it is built: it is paper and obeys paper rules; it never
  expires and withholds nothing; the payoff is the finding, not a reward. It is a
  **lead**, not an objective. T1, T2 and §4.6 are what it must not violate.
- **Office names.** *Hydrographic*, *Revenue*, *Garrison* read as 18th–19th
  century bureaucracy. The setting is medieval fantasy; the same five remits from
  §2 want chapter houses, guilds, a crown surveyor, a tithe office. Cosmetic,
  not now.
- **Rotation.** Disabled, not deleted — see D-Q1 in `decisions.md`.
- **Damaged plates** (R2.11). Cut, and Q6.3 keeps them cut.
- **Sibling islands** (R1.10). Still cut, and weaker still: with truthful labels
  and automatic layout there is nowhere left for the confusion to land.
