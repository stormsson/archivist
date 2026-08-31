# The Quarter Model — Decisions

The argument. `requirements.md` in this folder is the rule; this is why each rule
is the one it is, and what was rejected. Kept because most of what follows was a
real fork, and a reader who does not know what was on the other side will
re-propose it.

Decided 2026-08-29/30, on branch `rework1`. Nothing here landed on `main`.

---

## 1. The diagnosis

The map table was taxing. Four candidate causes were put up; three were
confirmed and one exonerated.

| | cause | verdict |
|---|---|---|
| a | The search — the absolute ground pose is unfindable, and R1.2 guarantees it always will be | **confirmed, and the root** |
| b | The volume — 49 sheets on one board (F-S1.3) | **confirmed** |
| c | The controls — drag, wheel, pan, select, fuse, accordion | **exonerated** |
| d | The pointlessness — nothing in the loop read a finished board | **confirmed** |

(c) being clean is what made the rest possible: the fix is to the *goal*, not
the input layer, and the existing handling code is not the problem.

The evidence for (a) as root is that the spec had already conceded it twice —
relative fitting exists because absolute fitting is unfindable (G1.1), and
assisted snap was flipped from feedback-only to capture-widening (G7.1 → G1.8,
recorded in `groups_spec.md` §8.6 — removed by this rework, recoverable at
`main`) because the strict test was not reachable.

---

## 2. The forks

Each row is a decision, its chosen answer, and the reason the alternative lost.
Q-numbers point at the rule in `requirements.md`.

| # | question | chosen | rejected, and why |
|---|---|---|---|
| 1 | What is taxing? | a + b + d | c — controls were never the problem |
| 2 | What does a worked board give? | *(superseded by 15)* | — |
| 3 | Is a quarter owned by an office, or does each office have its own? | **Neither: offices are layers over one shared cut** (Q1.2, Q2.1) | Flat quarters would put each piece of ground in exactly one document, deleting the thesis in CLAUDE.md. Per-office quarter sets would keep it but at four times the pieces |
| 4 | Do layers replace each other or stack? | **Stack in register** (Q2.4) | Alternative-versions would make offices interchangeable, which is the opposite of the point |
| 5 | Where does the puzzle live? | *(superseded by 15 — nowhere on the table)* | — |
| 6 | Grow by finer cuts or more offices? | **More offices** (Q2.5) | 3 × 3 or 4 × 4 walks straight back into (a) and (b); a per-island cut makes every island a puzzle to *learn* before it is one to solve |
| 7 | What is on the board where there is no sheet? | **The base shows through** (Q4.6) | Bare wood is bleak; ruled empty slots are a readout in costume, which R4.9 rules out |
| 8 | How does stacking physically work? | **Moot — one layer at a time** (Q4.3) | A light table was proposed and rejected on setting: medieval fantasy. With one layer visible, opaque paper is correct and no fiction is spent |
| 9 | Can the base improve? | **Fixed forever** (Q4.4) | If the layer flickers *and* the base moves, there is no stable reference and comparison gets harder the more you own |
| 10 | What does a binder hold? | **`island · office`, 1–4 quarters** (Q3.1) | One plate per binder makes binders sheets with covers; a whole island per binder hands you everything at once and kills accretion |
| 11 | What does merging buy? | **Tidiness only** (Q3.4) | Shelf-space compression and table-capacity unlocking were both offered and declined: sorting is the pleasure, and paying for it makes it a task |
| 12 | Does the game notice a completed layer? | **Physical tell + the index** (Q5.2, Q5.3) | Silence was recommended; a marker that appears only at 4/4 was ruled out either way |
| 13 | Who lays the plates out? | **The board is a view** (Q4.1) | Copies (R6.2) solve a problem that no longer exists; originals on a persistent board charge a pack-and-unpack tax every session |
| 14 | What sends the player looking for a binder? | **Requests (later) + merging** (Q6.5) | — |
| 15 | Is the damaged plate still in? | **No** (Q6.3) | Keeping it would have been the table's one manual verb. Declined in favour of the puzzle living in retrieval, across the racks |
| 16 | Does the table keep one small act of its own? | **No — pure reward surface** (Q4.2) | A first-open arranging ritual is clerical with labelled plates, and it puts a chore between finding an island and seeing it |
| 17 | Where does the base plate live? | **In an office's binder** (Q4.4) | Always-available removes the only scarcity the table has; a separate index chart invents a document class R2.2a already covers |
| 18 | Do quarters overlap? | **Exact tiling** (Q1.4) | Overlap exists to help matching, and matching was deleted at 13 and 16 |
| 19 | Fixed paper or fixed scale? | **Fixed paper, per-island scale** (Q1.5, Q1.6) | Fixed scale makes every furniture dimension a function of island size, to enable a cross-island comparison R1.4 and R6.8 forbid |
| 20 | Is the binder atomic, or do loose plates exist? | **Both, weighted loose** (Q6.1, Q6.2) | Binder-atomic leaves five objects per island and starves the primary activity |

---

## 3. Deviations recorded

| id | against | what | why |
|---|---|---|---|
| **D-Q1** | R6.6, `Wheel.cs` | Rotation is **disabled, not removed**. The code stays on disk; the input is unwired. | All offices share one cut (Q1.2), so a quarter has exactly one orientation and turning paper is fiddle with nothing to say. Retained because a later mechanic could want it back, and the code is already written and tested. |
| **D-Q2** | R1.2, §4.6 | A pre-populated room (Q7.1) implies a **finite** collection, and the game's stated shape has no ending. | Recorded, not resolved — Q7.4 states the three positions. The model can be built either way; the long arc cannot. |
| **D-Q3** | §4.5 | Composing no longer *explains* shelving. | §4.5 said shelving reveals and composing explains, as one-directional pressure. Under Q4.1 the table is a reward surface: it shows you the island you have assembled by filing, and feeds nothing back into the filing. The link is now one-directional the other way. |

---

## 4. What this supersedes

### Requirements retired

| requirement | was | why it goes |
|---|---|---|
| **R2.4** | rotation fixed per survey | Q1.2 — one shared cut, no rotation |
| **R2.5** | within-survey overlap, tunable | Q1.4 — exact tiling. The parameter's 0% case was already measured safe |
| **R2.8** | index diagrams on some surveys | It was "the main difficulty dial" for placement. Placement is gone (Q4.2) |
| **R6.2** | the player places a *copy* | Q4.1 — the binder goes to the table and comes back |
| **R6.3** | a sheet can only be copied after handling | follows R6.2 |
| **R6.4** | assisted placement, generous hidden tolerance | Q4.2 |
| **R6.5** | no good fit → stays unsettled | Q4.2 |
| **R6.6** | drag/wheel and two-point pinning | Q4.2; see D-Q1 |
| **R6.7** | fitting always against the true island | Q4.2 |
| **R6.9** | a board can never be filled | Q1.1 makes four plates a complete layer, and completing it is the point. Was already contradicted by measurement (F-S1.8) |
| **R6.10** | boards persist and stack up | Q4.7 — there is no board state to persist |

### Requirements reshaped

| requirement | change |
|---|---|
| **R2.2** | A survey, a binder, and an office layer are now three names for one thing: `island · office`, four quarters plus a chart |
| **R2.2a** | The whole-island sheet becomes the board's base (Q4.4), not a placeable tile — F-S1.6 said so and is now acted on |
| **R2.3** | Scale still comes from a small fixed set, never continuous. It is now chosen per *island* rather than per survey (Q1.6) |
| **R4.4** | Unchanged in shape; the office level moves inside the binder (Q3.5) |
| **R6.8 / R6.8a** | One board per island, opened by the whole-island chart — both hold, unchanged |
| **R7.3 / R7.3a** | Moot. Offices differ by omission (Q2.4), so nothing needs an exemption from R2.13. **Open question 11 closes** |

### Findings closed

| finding | closed by |
|---|---|
| **F-S1.6** the whole-island sheet cannot be a placeable tile | Q4.4 — it is the base |
| **F-S1.7** the sheets do not look like the mockups (`Fill` on) | Q2.2 — `Fill` off, offices are ink subsets |
| **F-S1.8** R6.9 is contradicted by the generator | R6.9 retired; boards fill deliberately |

**F-S1.1, F-S1.2, F-S1.4 and F-S1.5 stand as measured** and are not affected —
they describe the old cutter's output and remain the record of what it did.

### Documents

| document | disposition |
|---|---|
| `UI/cartography_table/groups_spec.md` | **removed.** 1084 lines specifying relative fit, group tables, fusing and assisted snap — a mechanic that never shipped and is now cancelled. Recoverable at `main`, and superseded in full by Q4.1/Q4.2 |
| `UI/cartography_table/spec.md` | superseded; banner added, file kept as the record of what was built |
| `UI/cartography_table/persistence.md` | superseded; banner added. Q4.7 reduces the save to binder contents plus table contents |
| `UI/cartography_table/requirements.md` | rewritten as a pointer — it described the accordion-and-rotate interface |
| `UI/cartography_table/findings.md` | **kept, unedited in substance.** Findings are measured, not reasoned; a closure note is appended |
| `table_binders_placement.md` | banner added; B1.1–B1.8 mostly survive. B1.6's three verbs stand; B1.3 becomes the *goal* state (Q3.3) |

### Code with nothing left to do

`SheetFit`, `SnapHint`, `BoardFusing`, `BoardHandle`, `BoardStore`,
`SheetKinship`, `SheetUnion`, `BoardSnapshot` — fitting, hinting, fusing,
dragging and remembering a board that is now derived. `Wheel` is retained
disabled (D-Q1). `CoastWalkCutter` is replaced by the quarter cutter.

Nothing is deleted by this document. It records what may be.
