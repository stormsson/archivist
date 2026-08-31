# Rework 1 — Overview

Branch `rework1`. Nothing lands on `main`.

| document | what it is |
|---|---|
| `../quarters/requirements.md` | **the authority.** The model, as **Q**-numbers |
| `../quarters/decisions.md` | why each rule is the one it is, and what was rejected |
| `01-removal.md` | what comes out, and what breaks when it does |
| `02-features.md` | what gets built, as **W**-numbers |
| `03-findings.md` | measured, as built. Findings win over both |
| this | order, parallelism, and what blocks what |

---

## 1. The shape of the work

Roughly **9 000 lines out, 3 pieces of real new work in.**

The deletion is large and shallow: one mechanic, one folder, few callers. The
construction is small and deep: a new cutter, an office→layer mapping, and a
per-office style that does not exist at all today.

The three things that carry risk are not the same as the three that carry lines:

| | risk | why |
|---|---|---|
| **W3 per-office style** | highest | Q1.2 makes every office draw identical geometry, so style is the *only* separator. A first pass now exists (R16); §5.4's proof is a looking test and has not been made |
| **W1 the quarter cutter** | structural | everything downstream reads its output; it invalidates every save and every acceptance number |
| **the deletion** | low | mechanical, and W0 makes it verifiable |

---

## 2. Three lanes

The work splits into three lanes that barely touch until they converge.

```
        W0 compile check ─┐
                          │  (enabler — do this first, it is half a day)
                          ▼
  ┌───────────────────────────────────────────────────────────┐
  │ LANE A — GENERATION            engine-free, headless      │
  │   W1  quarter cutter ─────────────────────────────────┐   │
  └───────────────────────────────────────────────────────│───┘
  ┌───────────────────────────────────────────────────────│───┐
  │ LANE B — RENDER                engine-free, headless   │   │
  │   W2  office → layers ────────────────────────────┐    │   │
  │   W3  per-office style ───────────────────────────┤    │   │
  └───────────────────────────────────────────────────│────│───┘
  ┌───────────────────────────────────────────────────│────│───┐
  │ LANE C — BUILDING              engine, editor only │    │   │
  │   DELETE the placement mechanic ──┐                │    │   │
  │   W4  binder identity + merge ◄───────────────────────── │   │
  │   W6  loose plates + filing  ◄────┼─── W4          │    │   │
  │   W7  save, narrowed         ◄────┴─── W4          │    │   │
  │   W5  passive board          ◄─────────────────────┴────┘   │
  │   W8  populated room         ◄─── W1, W4                    │
  └─────────────────────────────────────────────────────────────┘
```

**Lanes A and B are engine-free** (`Archivist.Generation`, `Archivist.Render`
never reference `UnityEngine` — the rule that lets `Tools/run-acceptance.sh` run
headless). They can be built and proved without opening Unity. **Lane C cannot**,
which is what W0 is for.

---

## 3. Where it stands (2026-08-30)

| | work | state |
|---|---|---|
| **W0** | `Tools/check-building.sh` | **done** — verified both ways |
| — | **the deletion**: the placement mechanic, ~9 000 lines | **done** — `01-removal.md` §6 |
| **W1** | the quarter cutter | **done** — R9, and **R21**, which is the one that made the cut real |
| **W2** | office → layers, contours + grid | **done** — R13, R14 |
| **W3** | per-office style | **done** — R16, then **R23**: the offices differ by composition, not colour |
| **W4** | binder identity, the chart's home, merge | **done** — R11, **reversed by R20**: a binder is an island |
| **W7** | the save, narrowed | **done** — format version 3 |
| **W9** | the render budget | **done** — R15, R21, R22 |
| **W5** | the passive board | **done** — R17 `Q`/`E`, R24 the gate and the base underneath |
| **W6** | loose plates, filing at the rack | **not started**. The rack exists (`Shelf`, `ShelfSlot`, `PF_Shelf`); what it is waiting on is the room — `../space/small_room.md` |
| **W8** | the populated room | **not started**, blocked on two numbers |

All 13 gated checks pass. Generation is **9x** faster than when this started
(484 → 53 ms) and a board of thirteen plates **9.8x** (2735 → ~280 ms) while
carrying six times the resolution.

### Also outstanding

- **A7's rewrite** — still `Cost.VerySlow` and off the gate; its subject matter is
  the PCA rotation separation Q1.2 deleted.
- **The stale `BoardInteractor`** in `POC04_Room.unity` and **the second and later
  `BindingAnchors`** on the table prefab (F-R19.3) are **cleared** — the scene
  carries no missing script and the table holds one binder.

---

## 4. W9 — the render budget

**A whole island's plates cost 2735 ms to render once each**, and that is too much.
13 plates: one chart at 309 ms and twelve quarters at ~200 ms.

What has already been taken, and what it bought:

| | |
|---|---|
| `-c Release` on the harness | the 4× that was never a code problem (R4) |
| coastline clipped to `LandBounds` | 4.8× on island generation (R5) |
| `RenderLod.NoFillSlack`, 8 m cell | 3.9× on a plate with no fill (R8) |
| `Contours.ExtractLevels`, one sampling for N levels | 4.3× on Land Survey (R14) |

What is left is the sampling itself: `IHeightField.Height01` is an fBm evaluation
per grid corner, and a plate at 1:10000 covers 39.1 km² at an 8 m cell — **0.61 M
corners per plate**.

**The unexamined redundancy: every office shares the same four rects** (Q1.2). The
same grid is therefore sampled once per office — three times for the same corners,
by construction, and the whole point of the quarter model is that they register.
Nothing yet exploits it.

W9 is scoped and measured separately. Its hard constraints are the ones every
optimisation here has had to keep: `Archivist.Generation` and `Archivist.Render`
never reference UnityEngine, determinism is a contract (A2's digest, A3's seam
agreement to 4×10⁻⁶ m), and no `System.Random`, no wall-clock, no enum reflection.

---

## 5. Decisions that block work

| # | decision | blocks | notes |
|---|---|---|---|
| 1 | **Antiquarian** — conform to four quarters, or keep POI detail sheets as a second class read in hand? | nothing now | postponed (`01-removal.md` §5); that office is being reworked separately |
| 2 | **The supply** — Q7.4 / D-Q2. A pre-populated room is finite; R1.2 says the islands are not | **W8** | decides the long arc, not the model |
| 3 | **Archive capacity** — `space/requirements.md` S3.2's missing input | **W8**, and the real room | now *computable*: plates per island at a fixed paper size |
| 4 | **The room** — W6 files a loose plate "at the rack", and there is one rack standing in a debug box | **W6** | `../space/small_room.md`; the rack itself is built |

Draw order and where merge lives are settled and built. (2) and (3) are the only
things standing between the plan and W8, and (3) has been blocking the real room
since before this rework.

---|---|---|---|
| 1 | **Antiquarian** — conform to four quarters, or keep POI detail sheets as a second class read in hand? | **W1** | `01-removal.md` §5. The second option costs a class of object the room does not have, and is the more interesting one |
| 2 | **Draw order** in `Strokes.Draw` once contours and grid are added | **W2** | documented as load-bearing for the acceptance hash |
| 3 | **Where merge lives** — a `BinderPickup` branch, or a new verb | W4 | merging is rack work, so probably not the table |
| 4 | **The supply** — Q7.4 / D-Q2. A pre-populated room is finite; R1.2 says the islands are not | **W8** | decides the long arc, not the model |
| 5 | **Archive capacity** — `space/requirements.md` S3.2's missing input | **W8**, and the real room | now *computable*: `offices × 4 + 1` plates per island at a fixed paper size |

(1) and (2) block Phase 1 and are worth settling this week. (4) and (5) block
Phase 3 and can wait — but (5) has been blocking the real room since before this
rework, and this is the first time the number can be derived rather than guessed.

---

## 6. How each phase is proved

| phase | proof | where |
|---|---|---|
| 0 | a deliberate type error in `Building/Runtime` fails the script | headless |
| 1 deletion | `Tools/run-acceptance.sh` and the new Building check both pass | headless |
| 1 W1 | 200-seed sweep: `offices × 4 + 1` sheets, land bounds covered exactly once per office, `IslandDigest` stable | headless |
| 1 W3 | **three offices' plates of one quarter told apart at pile distance, by someone not told which is which** | in the room, by looking |
| 2 W4 | two binders of one island merge; ledger unchanged; `RoomSnapshot.Audit` clean across save and load | headless + editor |
| 3 W5 | three binders on a table show one island in three hands; `Q`/`E` flips with nothing moving but the ink; reload reproduces it from binder contents alone | editor |
| 3 W8 | the room opens full, the ledger agrees, and load time is bearable | editor |

The one that is not an assert is the one that matters most.

---

## 7. Acceptance, after

`01-removal.md` §4.1 has the detail. In short: **A2, A3, A4, A8 survive** (values
change for A2); **A5/A5b need porting** and must be taught that a quarter of a
small island is *supposed* to be mostly blank; **A6 degenerates** — every office
now covers identical rects, so cross-office overlap is 100% by construction and
the metric stops measuring anything; **A7 needs a rewrite**, since its subject
matter is the rotation separation and scale fallback that Q1.2 and Q1.6 delete.

Retiring A6 and A7 is not a loss of rigour. They measured a cutter that made
choices; the new one makes none.
