# Sheet Groups and Assisted Snap — Specification

Construction. `spec.md` in this folder is the authority on the table as built;
`requirements.md` beside it is the authority on intent, and the four PNGs are
the authority on look. `../../requirements.md` §3.6 is the authority on the
activity.

This document **supersedes part of `spec.md` §6**. Later never overrules
earlier, so every superseded C-number is named in §2 with the reason. Nothing
here is a silent correction.

Requirements are numbered **G**n.n. Existing numbers (C, R, S, T, F, P, D)
refer to their own documents.

---

## 1. What this is

Two changes to the cartography table, decided together because the second is
only worth building on top of the first.

**Sheet groups.** Sheets that are laid in the correct pose *relative to each
other* fuse into a single movable entity — a `SheetGroup`. A group moves and
turns as one object, is listed in a new **Groups** section of the cabinet, and
can be parked in that section and taken out again.

**Assisted snap.** A gameplay option, `gameplay.assistedSnap`. When a dragged
sheet comes near a sheet it could join, both slabs pulse a slow sinusoidal
glow. It changes what the player *sees* and nothing about what the game
*accepts*.

### 1.1 The problem this solves

The table as built asks the player to place each sheet at its **absolute** true
ground pose (C6.1). On island 0 (Shawbury) a Land Survey A1 covers
1285 × 1902 m, its position tolerance is `1285 × 0.12 = 154 m`, and the padded
board is about 5940 × 5492 m. The target is therefore **2.6% of the board's
width**, with nothing on screen indicating where it is — and R1.11 guarantees
there never will be, because the island is never shown.

Two sheets held in the correct pose relative to one another score nothing,
because the test never looks at the other sheet. The one piece of feedback the
player can actually generate is the one the rule ignores.

### 1.2 Settled decisions

| # | decision |
|---|---|
| G1.1 | The fit test becomes **relative**. A sheet is judged against a *frame* — an offset and a rotation — carried by whatever it is being joined to. With an identity frame it is byte-for-byte today's test. |
| G1.2 | Two sheets can fuse only if they are from the **same survey**. Cross-office fusing is refused: the difference between two offices' sheets of one hillside is the game (CLAUDE.md), and fusing them would erase it. |
| G1.3 | A group's frame is **one stored pose**. Members store no pose at all — a member's pose is derived, exactly as a seated sheet's pose is derived today (C4.6). |
| G1.4 | Group membership is **monotonic**. Sheets join; none ever leave. The only exit is filing the whole group. |
| G1.5 | Fusing happens **on release**, never mid-drag. |
| G1.6 | A group is the **unit of interaction**: clicking any member selects the group; drag and `Q`/`E` move all of it. |
| G1.7 | The cabinet gains a **Groups** section. Office sections keep listing every sheet, so the inventory stays honest; a grouped sheet's row is marked and inert. |
| G1.8 | `gameplay.assistedSnap` **widens capture**. It shows the slot a sheet would drop into and lets a release inside the hint range take it. Superseded G7.1's feedback-only rule — see §8.6, which records why. |
| G1.9 | **Absolute correctness is out of scope.** See §16. |

---

## 2. What this supersedes

| superseded | by | why |
|---|---|---|
| **C6.1** (position measured from `truth.CentreGround`) | **G3.2** | The absolute centre is unguessable by construction — R1.11 forbids ever showing the island. The *tolerance* formula is unchanged; only the point it is measured from moves. |
| **C6.2, C6.3** (rotation vs `truth.RotationDeg`, mod 360) | **G3.3** | Rotation is now compared against the frame's angle plus the sheet's own truth rotation. The 8° figure, the absoluteness of that figure, and the mod-360 rule are all **kept** — see §3.5. |
| **C6.5** (settle to the exact true pose) | **G4.3** | The settle target becomes the exact *frame-relative* pose. Same smoothstep, same duration. |
| **C6.7** ("a seated sheet dragged again becomes unseated") | **G5.4** | Replaced by group semantics: dragging a member drags the group, and membership never shrinks. The no-lock principle it protects is honoured differently — see G5.5. |
| **§4.2 `Placement`** (`Seated` + three pose fields) | **G4.1** | Gains `GroupId`. When set, the three pose fields go meaningless, by the same discipline `Seated` already imposes. |
| **C1.5** ("snap previews and settles") | extended by **G7** | Still true. A third visual state is added below the existing two. |
| **C8.13** ("no zoom, no pan — the board always frames the whole board") | **G10.1, G10.2** | The reason C8.13 existed was absolute seating: the mounting sheet's extent was the player's only clue to where a sheet belonged, so cropping it removed the one reference on screen. G1.9 takes absolute correctness out of scope and groups are placed relative to each other, so the far corners carry no information. **Both halves are now lifted** — the wheel zooms, the right button pans. |

`SheetFit`'s class comment carries two standing warnings that **remain in
force** and must be reproduced in any rewrite:

- **No feature matching.** Every `Sheet` carries `CentreGround` and
  `RotationDeg`. The relative test is still a subtraction and a modulus over
  numbers the generator handed us; it is not coastline matching, and coastline
  matching must not be written.
- **Compare `truth.RotationDeg`, not `truth.Survey.RotationDeg`** (D-H2). The
  Hydrographic coast walk orients each sheet to its own stretch of shore, so
  the survey's rotation there is nominal. Taking it would break Hydrographic
  sheets alone while every other office worked.

---

## 3. The relative fit

### 3.1 The frame

A **frame** is the rigid transform between island ground space and where the
player has actually put some paper: a rotation `φ` in degrees and an offset `t`
in ground metres.

```
G3.1  A frame (φ, t) maps island ground to board ground:

          pose(sheet M)  =  ( R(φ)·c_M + t ,  θ_M + φ )

      where c_M = M.CentreGround, θ_M = M.RotationDeg, and R(φ) is the
      rotation taking +X toward +Y by φ degrees — the same sense
      Sheet.FrameRect uses, applied in the opposite direction.
```

Every candidate a dragged sheet could join presents a frame, and there is only
one way to build one:

- **A loose sheet B** at player pose `(p_B, r_B)` presents
  `φ = r_B − θ_B`, `t = p_B − R(φ)·c_B`.
- **A group** presents its own stored frame directly (G4.2).

This is the whole abstraction. A loose sheet and a nine-sheet group are the
same kind of thing to the fit test, which is why there is one code path.

### 3.2 Position

```
G3.2  Fits(A, frame) requires

          | p_A  −  (R(φ)·c_A + t) |   ≤   reach(A)

      reach(A) = min(A.Survey.SheetGroundWidth,
                     A.Survey.SheetGroundHeight) * PositionTolerance
```

`reach` is **unchanged from C6.1** — same formula, same default 0.12, same
argument. The shorter ground dimension is still the right scale because it is
still the direction in which a near-miss first stops looking like the same
sheet. Only the point the distance is measured *from* has changed.

### 3.3 Rotation

```
G3.3  Fits(A, frame) also requires

          | AngleDelta(r_A,  θ_A + φ) |   ≤   RotationToleranceDeg
```

`AngleDelta` is unchanged: signed difference folded into `(−180, 180]`,
**modulo 360, never modulo 180** (C6.3 survives intact — see §3.5).

### 3.4 Identity reduces to today

With `φ = 0` and `t = 0`, G3.2 and G3.3 are `|p_A − c_A| ≤ reach` and
`|AngleDelta(r_A, θ_A)| ≤ rotTol` — exactly the function in `spec.md` §6.1.
This is not a coincidence to be admired; it is the migration path, and it means
the existing acceptance measurement (A5) can be run unchanged against an
identity frame.

### 3.5 What the relative test does to the rotation problem

Worth stating because it changes how hard the game is, per office:

| survey | rotation | joining B to A means |
|---|---|---|
| Land Survey | one angle per survey (R2.4) | `θ_A + φ = θ_B + φ` — **be parallel to A**. The truth delta is zero. |
| Garrison | one angle per survey (R2.4) | same — be parallel. |
| Hydrographic | **per sheet** (D-H2) | the truth delta is real; the player must find a specific relative angle. |
| Antiquarian | per sheet, seeded | cannot group at all — see §6. |

So for two of the four surveys the relative test reduces rotation to "line it
up with the one next to it", which is visually obvious. That is most of the
difficulty complained about in §1.1, removed by the fit change alone and not by
any assist.

### 3.6 Candidates

```
G3.4  Two sheets may fuse iff they belong to the SAME SURVEY:

          fusable(A, B)  ⇔  A.Survey == B.Survey

      (same office, same whole-island flag, same scale, same lattice)
```

Cross-office fusing is refused (G1.2). It is also refused for a second,
mechanical reason: two offices survey at different scales and rotations, so
co-located sheets would satisfy a relative test whenever they were roughly on
top of one another, and every group would swallow the board.

The whole-island sheet (R2.2a) is a survey of one. It has no peer, so it can
never fuse. This is consistent with its reservation out of the crate draw.

```
G3.5  Two sheets are NEIGHBOURS iff they are fusable AND their true
      ground rects overlap:

          neighbours(A, B)  ⇔  fusable(A, B)
                            &&  overlap(A.GroundCorners(),
                                        B.GroundCorners())

      by a separating-axis test on the two rotated rects. Edge
      touching counts as overlapping: lattice sheets overlap by
      design (C1.2), so a zero-width contact is a degenerate case
      the hint has no reason to distinguish.
```

**This rule was specified wrongly and is corrected here.** The first version
tested `A.FrameRect` against `B.FrameRect`, on the reasoning that frame space is
axis-aligned and the test is then a plain `Rect2.Intersects`. That is true only
when both sheets share a rotation. Frame space is ground rotated by *that
sheet's* `−RotationDeg`, so when rotation is per-sheet the two rects live in two
different coordinate systems and the comparison is a category error, not an
approximation — it can fail in either direction.

It would have held for Land Survey and Garrison, whose whole lattice shares one
rotation (R2.4), and broken for the **Hydrographic coast walk** (D-H2) — 11 of
island 0's 31 sheets, the largest survey on that seed — and for Antiquarian
detail sheets, which carry per-sheet seeded rotation. The cheap test was cheap
for the two offices that needed it least.

SAT on `Sheet.GroundCorners()` is exact for all four offices. It is recorded
here rather than silently swapped because the wrong version is the one someone
will reach for again.

**Measured**, island 0 (`948AC8A27E42EEF9`), unordered same-survey pairs:

| survey | sheets | pairs | `FrameRect` | SAT | false + | false − |
|---|---|---|---|---|---|---|
| Hydrographic 1917 | 11 | 55 | 12 | **10** | **10** | **8** |
| Land Survey 1894 | 9 | 36 | 19 | 19 | 0 | 0 |
| Garrison 1861 | 6 | 15 | 11 | 11 | 0 | 0 |
| Antiquarian 1879 | 4 | 6 | 0 | 0 | 0 | 0 |

Of the 12 pairs the specified test called neighbours on the largest survey of
that seed, **2 were right**. Over 50 seeds: 385 false positives and 360 false
negatives, against 1766 true neighbour pairs.

The lattice offices are unchanged pair-for-pair. That is the whole danger —
**Land Survey and Garrison would have worked perfectly, which is what makes the
mistake invisible on the two surveys a developer reaches for first.**

Three of those 50-seed false positives are Antiquarian, so **G-A6 would have
failed under the rule this document originally specified**: the check would have
contradicted §6's finding that detail sheets are never neighbours. The category
error was already reaching the acceptance suite.

SAT was verified against an independent oracle rather than trusted — dense
sampling (61×61 grid plus 400 random points over the AABB intersection) through
`Sheet.Contains`, which resolves the rotated rect by its own route. Over 12
seeds and 1998 fusable pairs: no point inside both quads that SAT called
separate, and no overlap SAT claimed with no common point.

**Neighbourhood is used only by the assist (§7), never by the fuse rule.** A
player who correctly poses two same-survey sheets four lattice steps apart gets
a group with a hole in it, and that is allowed. The asymmetry is deliberate:
the *rule* is about the survey, the *hint* is about edges.

### 3.7 Testing a group against a frame

G3.2 and G3.3 judge a *sheet*. A dragged group has a frame of its own, and two
frames cannot be compared by a single distance — a rotation difference displaces
far members more than near ones. So the test is grounded in a sheet:

```
G3.6  Fits(group A, frame F) is evaluated on ONE member: the member
      m of A that is nearest, in board units, to the target's
      nearest fusable slab. Then

          Fits(A, F)  <=>  Fits(m, F)     -- G3.2 and G3.3 verbatim

      Rationale: reach() scales with the SHEET (C6.1), and the sheet
      that must land correctly is the one meeting the join. Grounding
      the test at the far end of a nine-sheet assembly would apply a
      tolerance to a member nowhere near the seam.

      For a group of one member this is exactly the sheet case, so
      there is one definition of "fits", not two.
```

Note this is *not* the same as testing every member pair. Only the meeting
member is judged; the rest follow rigidly from the frame, which is the whole
point of storing a frame rather than poses.

---

## 4. Data model

### 4.1 `Placement` gains a group

```
G4.1  Placement { Seated, GroundX, GroundY, RotationDeg, GroupId }

          GroupId == 0   ->  loose. GroundX/Y/Rot ARE the pose.
          GroupId != 0   ->  grouped. GroundX/Y/Rot are MEANINGLESS —
                             not stale, not approximate, meaningless.
                             The pose is derived via G3.1.
```

The "meaningless" discipline is not new. `Placement.Seated` already imposes it
in exactly these words, in the same struct, for the same reason.

### 4.2 The group table

```
G4.2  A board holds, beside its Placed dictionary and LayOrder list:

          Groups : { GroupId -> { RotationDeg φ, OffsetX, OffsetY,
                                  SurveyKey, OnTable } }

      SurveyKey identifies the survey every member belongs to (G3.4),
      so a candidate can be rejected without touching the island.
      OnTable is false for a group parked in the cabinet (§6.3).
```

### 4.3 Why members store no pose

Four consequences, and they are the reason this shape was chosen over storing a
pose per member:

1. **Fusing needs no corrective write.** The joining sheet was *near* the fit,
   not on it. With no stored pose there is nothing to correct — it is drawn
   from the frame, so it is exactly right the instant it joins. The alternative
   requires computing and writing the corrected pose, and writing the released
   pose by mistake leaves the group permanently, invisibly loose.
2. **A broken group is unrepresentable.** With N stored poses, any path that
   moves one member without the others — a bug, a partial update, an old save —
   produces a group that is internally wrong with nothing to say what it should
   have been. Here members have no poses to disagree with each other.
3. **One authoritative answer to "does C fit?"** The test needs `φ` and `t`.
   Recovering them from a member requires designating an anchor member, which
   is this frame stored indirectly and recomputed on every test.
4. **A tuning change stays coherent.** `config/generation.yml`'s own header:
   *"sheet identities do not move — sheet 7 stays sheet 7 — but the ground
   under them does."* A stored frame re-derives every member onto the new
   ground. N stored poses keep last week's arrangement, still *look*
   assembled, and fail the test that created them.

Float drift is **not** among the reasons. In doubles, repeated compose/decompose
is ~1e-13 relative — nanometres over an island. That argument would be wrong.

### 4.4 Persistence

One frame per group and a `GroupId` per placement. Both are primitives, so
`BoardStore` stays serialisable in one move exactly as §9 describes, and the
saved board still contains **no geometry** — only identities, one pose per loose
sheet, and one pose per group. R1.11 is upheld more strongly than before: a
nine-sheet assembly saves as one pose instead of nine.

---

## 5. Group lifecycle and interaction

### 5.1 Fusing

```
G5.1  On release of a dragged sheet or group A (C6.6's evaluation point):

        candidates = every fusable loose sheet and group on the table
        for each, build its frame (G3.1) and test Fits(A, frame)
        if any fit, take the one with the smallest position error:

            A loose, target loose  ->  new group, frame = target's frame,
                                       members = { A, target }
            A loose, target group  ->  A joins that group, which keeps
                                       its frame
            A group, target loose  ->  A adopts the target's frame and
                                       the target joins A
            A group, target group  ->  one group, holding both member
                                       lists, keeping the TARGET's frame

        if none fit: nothing happens at all. The sheet stays exactly
        where it was released (C6.6 preserved verbatim — no error
        state, no colour, no message, R6.5).
```

```
G5.2  The DRAGGED thing moves; the STATIONARY thing's frame wins.

      The table does not move when paper is put on it. The visible
      correction is bounded by the tolerance — at most 154 m on
      island 0's Land Survey, well under a tenth of a sheet — so
      even a nine-sheet group joining a lone sheet does not jump.
```

Fusing is evaluated **on release only** (G1.5). A group growing under the
pointer mid-drag is unmanageable, and the glow of §7 is a promise about
releasing, not a report on the present.

### 5.2 Settling

```
G5.3  A fusing release eases to the exact frame-relative pose over
      TableOptions.SettleSeconds with the same smoothstep C6.5 uses —
      the one PlayerHands.Advance uses — so joining reads as the same
      kind of movement as a sheet coming to the hands.

      The turn is taken the SHORT way round, via AngleDelta, so a
      sheet 5° out never spins 355° to join.
```

### 5.3 Selection, move, turn

```
G5.4  Clicking any member selects the whole group.
      - the selection outline (C6.8) is drawn round the UNION of the
        members' quads, not round the clicked one
      - dragging moves the group: exactly one frame is edited
      - Q/E turns the group about its union's centre in board space
      - the corner handle (C8.10) sits at the union's corner
```

**Turn pivot ⟨proposed⟩** — the union's bounding centre, not the clicked
member's centre and not the pointer. It is stable regardless of which member
was clicked, and `Q`/`E` works with nothing grabbed, so no grab point exists to
pivot about.

### 5.4 Membership never shrinks

```
G5.5  There is no detach gesture. Sheets join a group; none leave.
```

A group cannot be wrong: the fit test is what created it, so there is no
mis-assembly to repair. C6.7's principle — *"seating is not a lock. A locked
sheet is the harshest error state there is, and R6.5 forbids error states"* — is
honoured by G6.4: the whole group can always be sent to the cabinet and laid
out again. Nothing is ever stuck.

### 5.5 Draw order

```
G5.6  §3.3's stack becomes per-GROUP rather than per-sheet.

      A group's members occupy a CONTIGUOUS run of SheetSeparation
      tiers, in their own lay order, so an assembly always reads as
      one coherent map and can never be interleaved with another
      group's paper. Tier 3 (selected) and tier 4 (dragged) lift the
      whole run together.
```

---

## 6. Which surveys can actually group

Measured from the generator, not reasoned. Island 0 (Shawbury,
seed `948AC8A27E42EEF9`) as the worked case:

| survey | sheets | format | ground | groups? |
|---|---|---|---|---|
| whole-island 1906 | 1 | A1 @ 1:25000 | 12850 × 19025 m | **no** — a survey of one, no peer (G3.4) |
| Hydrographic 1917 | 11 | coastal strip @ 1:2500 | 875 × 425 m | **yes** — consecutive strips overlap along the shore |
| Land Survey 1894 | 9 | A1 @ 1:2500 | 1285 × 1902 m | **yes** — a lattice, 20% overlap (C1.2) |
| Garrison 1861 | 6 | A1 @ 1:2500 | 1285 × 1902 m | **yes** — a lattice |
| Antiquarian 1879 | 4 | detail @ 1:1250 | 275 × 275 m | **no** — see below |

**The Antiquarian survey cannot form groups, and must not be made to.**
`DetailSheetCutter` is *"one small sheet per qualifying POI, centred on it,
seeded rotation, **no walking and no tiling**"*. POIs are separated by
non-maximum suppression, so two 275 m detail sheets essentially never overlap:
they are not neighbours (G3.5) and their relative fit poses are far apart.

This is correct, not a gap. POC-03 P2.3: a detail sheet *"gives no position"*,
and *"where it sits is what the player recovers, once enough of the island has
been assembled from the survey sheets to recognise the ground."* Placing a
detail sheet is the activity that comes **after** the survey sheets are
assembled, and it is a different activity from joining them. This spec leaves
it alone.

Consequence for the Groups section: on island 0 it can hold at most three
groups, and the four Antiquarian sheets stay loose rows in their office section
forever. That is the intended reading.

---

## 7. The cabinet: the Groups section

### 7.1 Where it sits

```
G6.1  A new section is appended to the accordion, after the office
      sections (Offices.All order, C7.1) and before the footer.

      It lists EVERY group of the bound island — on-table and parked
      alike — marked by state exactly as office rows are. It starts
      empty because no groups exist yet, not because it only holds
      parked ones.
```

### 7.2 Office rows stay

```
G6.2  A grouped sheet KEEPS its row in its office section, so the
      office count still reads as the island's inventory. The row
      carries a group mark and is INERT: it cannot be dragged, and
      clicking it does not lay anything.

      The only place an assembly can be picked up is its Groups row.
```

Rejected: moving grouped sheets out of their office section. It makes the
office count read as "what is still separate", which is a different and less
useful fact than "what this office issued", and it makes a sheet vanish from
where the player last saw it.

### 7.3 The row ⟨proposed⟩

```
G6.3  A group row shows:
        - the survey's name and year, as SheetNaming already renders it
        - "n of N" — members present, sheets of that survey the
          archive HOLDS. NOT Survey.SheetCount: see below.
        - a thumbnail: the member textures composited at group scale,
          or the first member's thumbnail until that exists

      One survey can hold more than one group at a time — two halves
      assembled in different corners, not yet brought together — so
      the label must disambiguate. Proposed: append the lowest member
      number, e.g. "Land Survey 1894 · from 3 — 2 of 9".

      Hovering a Groups row highlights that group's rows in the
      office section above, and vice versa.
```

**The denominator was specified wrongly and is corrected here.** This section
first said "sheets in the survey", meaning `Survey.SheetCount`. That is an
R5.5 leak.

D-C3 permits the cabinet's section counts at all on one stated ground: *"because
the accordion lists only issued sheets, it never reveals how many the survey
actually has."* A denominator of `Survey.SheetCount` destroys precisely that
condition. "2 of 9" tells a player holding two Land Survey sheets that seven
more exist — which is the disclosure D-C4 dropped the ✓ to avoid, restored in a
different glyph.

So **N is that survey's sheets the archive holds** — the same number the office
section header already shows. G9.1's `complete(group)` may use the true survey
total, because it is shown to nobody.

### 7.4 Parking and retrieving

```
G6.4  Dragging a group onto the cabinet column parks it: it leaves the
      board, keeps its membership AND its frame, and its Groups row
      changes to the drawer state. This is C7.5's gesture, applied to
      a group.

G6.5  Dragging a Groups row onto the composition area lays the group
      back down under the pointer, PRESERVING its frame rotation φ.
```

**G6.5 deliberately differs from `BeginPlace`**, which lays a single sheet at
rotation 0 *"never at its true rotation"*, because resolving orientation is
part of placing a sheet (POC-03 P2.6, C6.3). A group has already had its
orientation resolved — that is what made it a group — and with absolute
correctness out of scope (G1.9) its `φ` carries no remaining puzzle. Resetting
it would destroy work the player has done, to no end.

---

## 8. Assisted snap

### 8.1 What it is

```
G7.1  gameplay.assistedSnap WIDENS CAPTURE.

      ON   the nearest related slab within the hint range shows a
           halo and a GHOST — the pose the dragged sheet would take
           if it joined — and a release anywhere inside that range
           settles into the ghost, whatever the sheet's angle.
           Effective capture: the hint range (~19.03 board units on
           island 0's Land Survey), rotation tolerance irrelevant.

      OFF  no halo, no ghost. SheetFit.Fits decides, unchanged:
           reach (~1.54 units) and RotationToleranceDeg (8°).
```

**This document originally said the opposite**, and §8.6 records why it changed.

### 8.2 The three visual states

```
G7.2  A slab is in exactly one of:

      1. SelectionGold  (0xC9A063)  steady   selected         C6.8
      2. SEATED band    (0xFFB43C)  steady   releasing joins  C6.4
         + GHOST slot   pulsing     where it would land       §8.6

      2 outranks 1. There is no third rung.
```

**Rung 2 as first written — a pale pulsing halo meaning "related but a release
will do nothing" — is retired**, because §8.6 abolished the state it named. Under
the assist a release inside the hint range always joins, so "related and near"
and "releasing joins" are one fact and must not be two dresses. With the assist
off, neither the halo nor the ghost is drawn at all and the seated band previews
the strict `Fits` exactly as C6.4 always did.

G7.5's pulse is **not** retired. It moves to the ghost, where "provisional" is
what it means, and it is also what separates assist-on from assist-off at a
glance: a steady band alone is the strict game, a steady band with a breathing
slot beside it is the assisted one.

### 8.3 The trigger

```
G7.3  Every frame of a drag, with assistedSnap on, for dragged sheet A:

        best = null ; bestD = ∞
        for each slab B on the table:
            if !neighbours(A, B)          continue     -- G3.5, island
            d = |board(A) − board(B)|                  -- board units, UI
            if d < bestD: best = B ; bestD = d

        range = max(slabW(A), slabH(A)) * GlowingHintRange
        if best != null && bestD <= range:
            pulse A and best

      Rotation is NOT tested. An unturned sheet must still be told
      it is near something related — telling it only once it is
      already aligned defeats the entire purpose.
```

**The split that makes this clean:**

- *"Are these two related?"* — an island question. `neighbours(A, B)`, needing
  the truth.
- *"Are they near?"* — a **pure UI question**. Distance between two slabs in
  board units. No island access, no truth pose, no fit pose.

`GlowingHintRange` scales with the dragged slab's own baked size, which
`BoardSheetView` already carries (`SheetGroundWidth × UnitsPerMetre`). At the
default 1.0 the hint lights when the player is roughly one sheet-step from
home, identically for a 19-unit Land Survey slab and an 8.75-unit Hydrographic
strip. Nothing is read from the island to compute it.

**`max`, and this document first said `min`.** That was wrong, and measurably:

| survey | slab (board units) | range at `min` | true neighbour step | hinted? |
|---|---|---|---|---|
| Land Survey | 12.85 × 19.03 | 12.85 | 10.28 short · **15.22 long** · 18.37 diag | short axis only |
| Hydrographic | 8.75 × 4.25 | **4.25** | **6.13 – 7.62** | **never** |

All eleven Hydrographic strips reported no hint **at their correct pose**, and
Land Survey sheets adjacent only along the long axis were dark everywhere —
LS#1's only neighbours sit at 15.22 and 18.37.

`min` is C6.1's quantity, borrowed for a job it does not fit. It is right for a
**tolerance**, because the short axis is the direction in which a near-miss
first stops looking like the same sheet. A **range** has to span a lattice step,
and a step is `side × (1 − OverlapFraction)` — nothing about the short side
bounds it. At `max` and a factor of 1.0 the range is one slab's long side, and
every step is that side × 0.8 or less, so short, long and diagonal are all
covered with margin and no magic factor: 19.03 ≥ 18.37, 8.75 ≥ 7.62.

It also reconciles this document with itself. The paragraph above already cited
the 19-unit and 8.75-unit figures, which are the **long** sides. The prose was
right and the formula was wrong.

```
G7.4  If the best candidate is a MEMBER of a group, only that member
      pulses — not the group.
```

The pulse names the **edge** you are about to join, not the mass you are
joining, and it therefore also tells you which way round you are. Exactly two
slabs pulse however large the group grows.

### 8.4 The pulse ⟨proposed — no mockup exists⟩

```
G7.5  alpha(t) = HintAlphaMin
               + (HintAlphaMax − HintAlphaMin)
                 * 0.5 * (1 + sin(2π t / HintPeriodSeconds))

      colour           SnapGold (0xE6A83E), the same gold, so the
                       hint reads as the snap affordance anticipated
                       rather than a second unrelated signal
      HintPeriodSeconds  1.4      "slow", per the request
      HintAlphaMin       0.15     never fully off — a slab that
                                  vanished would read as broken
      HintAlphaMax       1.0
      phase              A and B pulse IN SYNC. Antiphase reads as
                         two separate events; sync reads as one
                         relationship.
```

These are **look values, not feel values**, so per `CabinetStyle`'s standing
argument they are `const`s beside the existing palette, not fields on a tuning
asset. Unlike every other value there they have no mockup behind them — hence
⟨proposed⟩ — and the first playtest is their authority.

### 8.5 Implementation note

`BoardInteractor` today owns **one** outline quad, reparented, because exactly
one sheet is selected at a time. The hint needs to glow a slab that is **not**
selected, so a second instance is required. Same construction, same shared-mesh
rule (`BoardSheetView` owns and destroys the mesh; the outline only borrows
it), same `OutlineDrop` fraction so it cannot surface through the slab below.

---

### 8.6 Why the assist stopped being feedback-only

This document first specified `assistedSnap` as pure feedback: identical
tolerances on and off, so *"a player who turns it off is playing the same game
with less help, never a different game with different rules."* Play-testing
killed it.

**The measurement.** At `BoardZoom` 2 on island 0, Land Survey, ≈39.34 px per
board unit:

| | board units | on screen |
|---|---|---|
| hint range — halo lights | 19.03 | ≈ 750 px |
| `reach` — actually fuses | 1.54 | ≈ 61 px |
| sheet short side, for scale | 12.85 | ≈ 505 px |

The halo fired **twelve times further out than the fuse**. The player's report
was exact: *"when I drag a sheet over another and the halo starts, when
releasing the sheet it does not snap in place."* The preview and the outcome
never disagreed in code — `Evaluate` and `Release` call one `TryBestFuse` — but
they disagreed to the eye, because a rung that says "related" and a rung that
says "release now" were both a gold rim a few pixels wide.

**Two ways out, and why the harder one was wrong.** Making rung 3 unmistakable
(done: motion stops, width collapses, colour goes hot) fixes the *lie* but not
the *labour*. It leaves the player told "warm" across 750 px with a 61 px target
and no indication of direction — and for the Hydrographic coast walk, whose
strips each carry their own rotation (D-H2), no way to discover the required
relative angle except by trying angles.

**So the assist now shows the answer and accepts it.** The ghost draws the pose
the sheet would take; a release inside the hint range settles into it. That
makes the halo's promise true by construction rather than by careful wording,
which is the only kind of true a player can check.

**What it costs, honestly.** Working out where a sheet belongs *is* the
activity, and the assist now hands it over. That is the trade the option exists
to offer, and it is the first version in which the option means something: **on**
= the game shows you the slot, **off** = you read the coastlines and deduce it.
Previously it toggled a glow, which is a much thinner difference than the word
"assisted" implies.

**What it does NOT widen.** Kinship. `SheetKinship.Fusable` still gates every
join, so the assist cannot marry two offices (G3.4) and cannot place the
whole-island sheet (G-A5). It widens *aim*, never *truth* — G-A7c holds it.

## 9. Configuration

```
G8.1  gameplay.assistedSnap and gameplay.GlowingHintRange live in a new
      `gameplay:` section of config/generation.yml — the project's one
      config file.

G8.2  Tuning MUST NOT read that section. Archivist.Generation is
      engine-free and its values define what a seed means; a UI assist
      toggle has no business there. Archivist.Building reads the
      section itself, through the public Yaml.Read and TuningFile's
      upward directory walk, which it can already reach — the
      Archivist.Building asmdef references Archivist.Generation.

G8.3  The file's header comment is relaxed from "generation tuning" to
      "configuration", and gains a line saying which assembly owns
      which sections. Its determinism warning stays exactly as written
      and continues to apply to the generation sections only.
```

```yaml
# --- gameplay (read by Archivist.Building, NOT by Tuning) --------------
gameplay:
  assistedSnap: true
  GlowingHintRange: 1.0   # multiples of the dragged slab's LONGER side
```

Missing file, missing key or unreadable value falls back to the compiled
default and records a line in `Problems`, exactly as `TuningFile` already does.
A config file that is not on disk must never stop the table opening.

**This location is provisional.** `assistedSnap` is a player-facing choice and
belongs in a settings screen with the rest of them. It is in the config file
because that screen does not exist yet; when it does, only the reader changes.

---

## 10. Tuning

| value | where | default | note |
|---|---|---|---|
| `PositionTolerance` | `TableOptions` | 0.12 | unchanged (C6.1); now measured from the frame-relative pose |
| `RotationToleranceDeg` | `TableOptions` | 8 | unchanged (C6.2) |
| `SettleSeconds` | `TableOptions` | 0.18 | unchanged (C6.5) |
| `assistedSnap` | `config/generation.yml` | true | G8.1, provisional |
| `GlowingHintRange` | `config/generation.yml` | 1.0 | multiples of the dragged slab's **longer** side (G7.3) |
| `HintPeriodSeconds` | const, beside `CabinetStyle` | 1.4 | ⟨proposed⟩ |
| `HintAlphaMin` / `Max` | const, beside `CabinetStyle` | 0.15 / 1.0 | ⟨proposed⟩ |

No new randomness. Nothing here draws from a stream, so there is no
`StreamNames` entry and no value here can move an island. Board poses and group
frames are player facts.

---

## 11. Build order

| slice | what | done when |
|---|---|---|
| **S1** | `SheetFit` takes a frame. Identity frame path proven byte-identical to today. | A5 passes unchanged against an identity frame |
| **S2** | `Placement.GroupId`, the group table, `PoseOf(SheetId)` derivation. No UI. | headless: fuse two sheets, drag the frame, read both poses back |
| **S3** | Fuse on release; group select, drag, turn; per-group draw order (G5.6). | two Land Survey sheets join and move as one |
| **S4** | The Groups section: rows, marks, inert office rows (G6.1–G6.3). | a group appears, its members' rows go inert |
| **S5** | Park and retrieve (G6.4, G6.5). | a group survives a round trip through the cabinet |
| **S6** | `gameplay:` config section and the reader (§9). | toggling the key changes behaviour without a recompile |
| **S7** | The hint pulse (§8). | dragging near a neighbour pulses exactly two slabs |

S1 and S2 are the load-bearing pair and are worth landing alone. S7 is
deliberately last: §3.5 says the fit change alone removes most of the
difficulty, so the assist should be judged against a table that already works,
not used to paper over one that does not.

---

## 12. Acceptance

| # | check |
|---|---|
| **G-A1** | Identity frame reproduces today's `Fits` exactly, over every sheet of 20 seeds. |
| **G-A2** | Fuse is symmetric: if A fits B's frame, B fits A's frame, to within float equality of the tolerance comparison. |
| **G-A3** | A group's derived member poses satisfy `Fits` against the group's own frame, for every member, after an arbitrary sequence of drags and turns. |
| **G-A4** | Cross-office fusing is refused for every pair of sheets of 20 seeds (G3.4). |
| **G-A5** | The whole-island sheet never fuses, on any seed. |
| **G-A6** | Antiquarian detail sheets are never neighbours of each other, on 50 seeds — the §6 finding, held as a check so it is noticed if the cutter changes. |
| **G-A7** | With `assistedSnap` ON, a release anywhere inside the hint range of a related slab produces **the same group and the same final member poses** as a release dead on the ghost. The assist changes what is *accepted*, never what is *produced*. |
| **G-A7b** | With `assistedSnap` OFF, that same far release produces **no** group — the strict `Fits` path is untouched by the option. |
| **G-A7c** | The assist never widens kinship: a cross-office sheet inside the hint range produces no ghost and no fuse, and the whole-island sheet produces neither in either direction (G3.4, G-A5). |
| **G-A8** | A saved and reloaded board reproduces every member pose exactly, from one frame per group. |

G-A1 through G-A6 are engine-free and belong in the headless harness. G-A7 and
G-A8 need the board.

---

## 13. Board framing

```
G10.1  TableOptions.BoardZoom divides the camera's half-height.
       1 is C8.13's framing; default 2 draws every slab at twice
       the size.

           orthographicSize = BoardHeight * 0.5 / BoardZoom
```

At zoom 1 a Land Survey slab is 35% of the viewport height on island 0 —
small paper for the thing the whole activity consists of reading. At 2 it is
70%.

At 2 the camera shows half the board's height and half its width, so roughly
three quarters of the mounting sheet is off screen. **That is what G10.2 is
for.**

```
G10.2  The wheel zooms; the right button drags to pan.

       - Zoom is about the POINTER, not the board centre: the ground
         under the cursor does not move. Multiplicative per notch,
         because linear steps are fast when zoomed out and glacial
         when zoomed in.
       - Zoom OUT stops at 1.0 — the whole board framed, which is
         exactly C8.13's original view. A meaningful floor, not an
         arbitrary one.
       - Pan is clamped so the board cannot be lost. At zoom 1 there
         is nothing to pan.
       - Right-drag NEVER selects, deselects, unseats or fuses.
```

**It is a view transform and nothing else.** `SheetFit`'s `reach` is ground
metres and `GlowingHintRange` is board units; neither knows the camera exists.
Zooming cannot change what fuses, what a ghost points at, or how far the hint
reaches — and `BoardInteractor.TryGroundUnder` already goes through
`ScreenPointToRay` against the board plane, so it is camera-agnostic by
construction rather than by maintenance.

**Camera state is view state, not board state.** It is deliberately not in
`BoardStore`: that store holds player facts about paper and is shaped to be
persisted (§4.2, G4.4), and where someone last scrolled to is neither. Zoom and
pan reset to `TableOptions.BoardZoom`, centred, on every opening.

```
G10.3  Wheel travel is scaled before it becomes notches.

           TableOptions.BoardWheelSensitivity   (default 0.03, measured)
```

**Measured, after G10.2 was played**, on a macOS trackpad: the zoom was far
too fast. The cause is a fact about the platform rather than a mistake in the
feel. The Input System does not normalise scroll — a Windows detent reports
120, a macOS one about 1, and a trackpad reports a continuous stream of
whatever the OS made of your fingers, several notches inside a single frame.
`BoardZoomStep` is argued from the *range* (about ten notches stop to stop,
G10.2) and is still right; what was wrong was believing the hardware delivered
one notch.

- `BoardWheelSensitivity` is the **device** dial, `BoardZoomStep` the **range**
  one. Folding them together would mean tuning a mouse on the field that
  documents how far the zoom reaches.
- **0.03 is the measured figure on a trackpad**, not a derived one — the raw
  reading there is about thirty units per notch's worth of intent. On hardware
  that reports one clean unit per detent it wants to go back up toward 1, which
  is why it is a serialised field and not a const.
- `MaxNotchesPerFrame` falls from 4 to **1**. At 4 a single frame could apply
  1.75x, so the ceiling only ever caught the pathological case on a wheel and
  never the ordinary one on a trackpad.

```
G10.4  The cabinet has no ScrollRect. CabinetPanel handles the wheel
       itself, takes the Y reading only, and eases toward a target.

           TableOptions.WheelSensitivity            (shared with zoom)
           TableOptions.CabinetScrollPixelsPerNotch (default 40)
```

**Why the component was removed, which is not a matter of taste.** The column
scrolled intermittently on a trackpad — sometimes moving, sometimes not,
sometimes the wrong way — and lowering `scrollSensitivity` made it worse rather
than slower. The cause is in `ScrollRect.OnScroll`, in the UGUI this project
ships with:

```csharp
delta.y *= -1;
if (vertical && !horizontal) {
    if (Mathf.Abs(delta.x) > Mathf.Abs(delta.y)) delta.y = delta.x;   // <-- here
    delta.x = 0;
}
```

A vertical-only `ScrollRect` substitutes the **horizontal** reading whenever it
is the larger one, so that a horizontal-only device can still drive a vertical
list. A mouse wheel has no horizontal reading and never trips it; a trackpad has
one constantly, because a two-finger swipe drifts sideways at the start and end
of every stroke. On exactly those frames the column moved by the drift instead
of the intent — and, since `delta.x` is substituted *after* `delta.y` was
negated, in the opposite direction. No sensitivity fixes that: the number being
scaled is the wrong axis, and a smaller one shrinks the honest frames while
leaving the drift frames as a larger share of what the player feels.

So `CabinetPanel` implements `IScrollHandler` on the column root — the event
system runs it up from whatever was hit, so header, row and bare cream are all
covered — discards X outright, and reads the magnitude from `Mouse.scroll`
rather than from `eventData.scrollDelta`, so both wheels on this table are in
the same units and share one device dial. The wheel sets a **target** and
`Update` eases toward it with `1 − e^(−k·dt)`: a trackpad delivers its travel in
bursts, and a burst applied directly is a column that lurches. Over-scrolling a
list is recoverable in a way that crossing the whole zoom range is not, so the
board's per-frame cap is deliberately not repeated here.

`RectMask2D` on the viewport was always what clipped the column; it is
untouched. Nothing else referenced the `ScrollRect`.

---

## 14. The board camera owns its own rectangle

```
G10.5  The board camera renders the screen MINUS the cabinet column:

           cam.rect = (0, 0, 1 - CabinetWidthFraction, 1)
```

**Two faults that looked like one.** The camera rendered full-bleed with the
opaque cream column laid over the right 22% of it. So C8.13's floor — *"the
whole mounting sheet in view"* — was false at zoom 1, since 22% of the mounting
sheet was behind a panel. And `BoardViewport`'s clamp, `travel = max(0,
boardHalf − viewHalf)`, believed that band was on screen and refused to pan
toward it. On a board no wider than the viewport that made **horizontal panning
impossible at every zoom**: the view "already contained" a strip of board the
player could neither see nor bring out. It presents as a pan that works
vertically and does nothing horizontally, which reads as a broken axis rather
than as a clamp being right about the wrong rectangle.

Narrowing the rect fixes both at the source. `cam.aspect` follows the rect, so
the arithmetic in `BoardViewport` is unchanged and now describes the rectangle
the player is actually looking at; `ScreenPointToRay` and `WorldToScreenPoint`
both account for a camera rect, so hit-testing (§8) and the corner handles need
no adjustment. The alternative — an overscroll margin on the clamp — was
rejected for the reason `BoardViewport` already gives about margins: it is a
second tuning value doing a job a real measurement can do, and it would leave
the framing claim false while making the symptom go away.

**The header band is not subtracted, and that is recorded rather than fixed.**
It is 96 reference pixels whose screen height depends on the `CanvasScaler`'s
match, so it cannot become a viewport fraction without asking the canvas —
where the column's width is an anchor fraction and is exact. Nothing is
unreachable behind it: vertical travel is non-zero at every zoom above 1.

---

## 15. Persistence — required, and needing its own analysis

> The analysis happened and the slice is built: **`persistence.md`**, beside this
> file, answers §15.2's five questions and records what the save holds. This
> section stands as written.

**The table's state must be saved.** This is not a new requirement and not a
consequence of groups: `spec.md` C1.8 already lists it as settled, §9 already
specifies it in full (C9.1–C9.5), and C9.3 already ties it to T6 — *"the player
may stop at any moment with nothing left hanging."* It is simply not built.

Groups do not create that gap. They make it expensive. A parked group is
presented as **stored** — it sits in a drawer, in a section of the cabinet, and
the gesture that put it there is the gesture that files a sheet. `BoardView.Hide`
currently clears everything, so closing the board discards it. A player who
assembles nine sheets, parks the assembly and closes the table has lost real
work to an affordance that promised the opposite. Losing a loose sheet's pose is
the same bug wearing smaller clothes, but the group is the one that will be
noticed.

**This section is a placeholder for an analysis, not the analysis.** What
follows is only what this document can already see, so that the analysis starts
from something rather than nothing.

### 15.1 What groups add to the save

Nothing that changes the shape of §9's format — which was the point of G4.3.

- `Placement.GroupId`, one `int` per placed sheet.
- One record per group: the frame as three doubles, `Office` + `WholeIsland`
  as the survey key, `OnTable`, and the ordered member list.
- The per-board `NextGroupId` counter. It must be saved, because ids must never
  be reused and a reload that rewound the counter would hand a new group the id
  of a dead one.

Every field is a primitive, a `SheetId`, or a flat collection of them, so
`BoardStore` and `SheetLedgerStore` still save in one move (§4.2). A nine-sheet
assembly costs **one** pose on disk, not nine — R1.11 is upheld more strongly
after groups than before.

### 15.2 What the analysis has to settle

- **C9.1's ordering invariant extends.** A group naming a `SheetId` the ledger
  never issued must be dropped like any other stale reference — and a group that
  then falls below two members must dissolve, which needs a survivor pose the
  store cannot compute (§4.3). Load-time dissolution is not the same code path
  as play-time dissolution and must not be assumed to be.
- **C9.2's save points extend** to fusing, parking, retrieving, and releasing a
  group move. Whether a group frame edit is "a sheet released from a drag" for
  C9.2's purposes is a real question, not a rename.
- **A parked group has no board presence at all**, so it is the first piece of
  board state that is not *on* a board. Whether it belongs to the table, the
  island, or the archive is undecided, and C1.7 — board state is keyed by table
  identity, never by island — is the constraint the answer must respect.
- **Tuning changes.** `config/generation.yml`'s header warns that the ground
  moves under a saved collection. A stored frame re-derives cleanly (§4.3), so
  groups survive it — but a group whose members no longer overlap after a
  regeneration is a state the fuse rule could not have produced, and load must
  have an answer.
- **Whether a group should survive `Hide` at all**, or whether closing a table
  is the "deliberate act of clearing it" that §13 of `spec.md` describes.

Until that analysis happens, the honest position is that the Groups drawer keeps
an assembly **for as long as the table is open, and no longer**, and the code
says so at both `Teardown` and the park path.

---

## 16. Deferred, and deliberately absent

**Absolute correctness is deferred.** Nothing in this document makes a pose on
the table right or wrong in island terms; a group's frame is a player fact and
that is all. `Placement.Seated` and the identity-frame case remain in the model
and remain meaningful (§3.4), but nothing consumes them and no gesture produces
them. The idea that motivates picking this up again is **revealing a piece of
the island when a survey is completed**, which is out of scope here.

So that the future feature has something to key off, one derived predicate is
defined now and consumed by nothing:

```
G9.1  complete(group)  ⇔  group holds every sheet of its survey
```

It costs a count and it is the signal that idea needs. It is defined here
rather than invented later so that "completed survey" means one thing.

**Also absent, on purpose:**

- **No detach gesture** (G5.5).
- **No feature matching.** Reproduce `SheetFit`'s warning wherever the fit is
  rewritten.
- **No cross-office groups** (G3.4), and therefore no "recover this hillside
  from every office at once" activity. That is a different feature.
- **No magnetism.** Capture is widened by the assist (G7.1), but the dragged sheet is never pulled while the pointer holds it — it settles on release or not at all.
- **No group nesting.** Groups hold sheets, never other groups. Merging two
  groups produces one flat group.
