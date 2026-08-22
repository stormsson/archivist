# POC-03 — Points of Interest · Requirements

Small sheets of small things: ruins and natural oddities, each shown with a little
ground around it. Companion spec: `spec.md`. Upstream: `../requirements.md`,
`../poc01/`, `../poc02/`, `../generation_for_agents.md`,
`../analysis/hydrographic-contour-following.md`.

---

## 1. Purpose

Every sheet the collection holds so far belongs to a **survey**: a numbered run
covering coherent ground, cut by an office with a remit. POC-03 adds the other
kind of document an expedition produces — the **detail sheet**. One thing, drawn
close, with just enough of its surroundings to say where it was.

The design value is not the content. It is that **a small sheet is objectively
harder to place**, and that difficulty resolves itself as the board fills.

> The player knows which island a detail sheet belongs to, but not where on it.
> With only a few hundred metres of ground shown, it can be located only once
> enough of that island has been assembled from the survey sheets to recognise
> the surroundings.

That gives the map table a late-game activity that arrives for free, out of the
fiction, with no gating mechanic. It also puts something in the unresolved drawer
(R4.8) that genuinely belongs there.

### 1.1 Relationship to the map table

**POC-03's primary criterion cannot be evaluated without the map table** (§3.6 of
`../requirements.md`), which §17 of the POC-01 spec had itself assigned to
"POC-03". This document covers the **generator half**: what a POI is, where it
sits, what its sheet shows. That half is buildable and testable on its own.

The placement half — does a detail sheet feel unplaceable early and satisfying
late — needs assisted fit (R6.4) to exist. Treat this as POC-03a and the table as
POC-03b, or build the table first. Either way, do not claim the primary criterion
until a human has placed one.

---

## 2. Why this fits, and what it fixes

- **It answers a measured problem.** The hydrographic analysis flagged C6: thin
  coast strips are near-featureless and coastlines self-similar, making assisted
  fit hard. A distinctive landmark is what makes ground identifiable — POIs are
  the cheapest way to put recognisable things on the island.
- **It fills the office that is measurably thinnest.** Garrison carries no peak
  on 68% of its sheets. Ruined watchtowers and cairns sit on exactly the high
  ground Garrison already surveys.
- **It gives boards a reason to be reopened** (R6.10), and a reason for an island
  to be *worked out* without being *finished* (R6.9).
- **It is reconnaissance, not infrastructure.** Roads, quays and parcels belong to
  offices that only exist after settlement. Ruins and natural oddities are what a
  survey party records about ground nobody has developed.

---

## 3. Requirements

### 3.1 The features

- **P1.1** A POI is a **point feature** with a type, a position, and a stable
  `FeatureId`, generated once per island in a deterministic order — the same
  contract as peaks, settlements and rivers (§3.1 of the POC-01 spec).
- **P1.2** Two families: **ruins** (human traces) and **natural oddities**.
  Both are drawn from a small type table; the table is data for a generator, not
  authored content, so the supply stays unbounded (R1.2).
- **P1.3** Siting is **derived from the ground**, not scattered at random. A sea
  arch belongs on a steep coast, a cairn on high ground, a ruined jetty in
  shelter. A POI whose siting rule is unsatisfiable on an island simply does not
  occur there.
- **P1.4** Density is a few per island, varying by character. An island with no
  POIs at all is a legitimate outcome.
- **P1.5** POIs must not perturb existing generation. Adding them draws from a
  new named stream and leaves every existing feature bit-identical (§4.3, A2).

### 3.2 The sheets

- **P2.1** A detail sheet is **small** and centred on its POI, showing a modest
  area around it. It is a different physical object from a survey sheet, and
  should be recognisable as one at a glance.
- **P2.2** The sheet **names its island** (R2.13 — the label names what it shows),
  so the player always knows which board it belongs to.
- **P2.3** The sheet gives **no position**: no grid reference, no index diagram,
  no coordinates. Where it sits is what the player recovers.
- **P2.4 (placeability floor)** A detail sheet must contain **at least one drawn
  feature besides its own POI**. A 300 m square of bare hillside is not a puzzle,
  it is a dead end — every hillside looks alike. This is the D1 service rule
  applied to a new purpose, and it should reuse that machinery.
- **P2.5** Detail sheets are unique like every other sheet (R2.10), and each is
  truthful (R2.13).
- **P2.6** Rotation is per sheet and carries no north indication. A field sketch
  has no fixed orientation, and resolving it is part of the placement.

### 3.3 Filing

- **P2.7** Detail sheets file under the same given order as everything else:
  island → office → year → number (R4.4). They form their own numbered
  sub-series within a survey, so a gap in the survey run and a gap in the detail
  run each stay unambiguous (R2.10b).

### 3.4 Which office

- **P3.1** POIs are **shared across offices by type**, following each office's
  existing remit — Hydrographic records coastal oddities and wrecks as hazards to
  navigation; Garrison records ruined works and anything on commanding ground;
  Land Survey records inland ruins and landmarks.
- **P3.2** The §8.3 matrix gains at least one POI class, and the **shared-class
  invariant must still hold**: any two offices whose coverage can overlap share
  at least one drawn class. A6 must be re-measured after the change — a class
  only one office draws cannot help two sheets cross-reference.
- **P3.3** Office blind spots still apply. Garrison omits civilian detail, so a
  ruined village does not appear on a Garrison sheet even when a ruined tower
  does. That asymmetry is the point of having offices.

---

## 4. Acceptance

**Primary, human-judged — needs the map table (§1.1).** Take an island whose
board is roughly assembled and one whose board is nearly empty. **Pass:** the
detail sheet is placeable on the first and not on the second, and placing it
feels like recognition rather than search. **Fail:** it is either placeable
immediately (too much context) or never (too little).

**Automated, available now without the table:**

- determinism, and that adding POIs leaves existing features bit-identical
- **every detail sheet satisfies P2.4** — carries something besides its POI
- detail sheets are unique, and their numbering is contiguous within its series
- A6 shared-class coverage after the matrix change

**Reported, not gated:** POIs per island by character; type distribution; how
much of a detail sheet's area is distinguishing content.

---

## 5. Out of scope

| deferred | why |
|---|---|
| The map table and assisted fit | POC-03b, or first — see §1.1 |
| Office style, ink, paper (R2.6) | still POC-02b |
| Text and labels on the sheet | with office style; the island name is a filing label, not map lettering |
| Ruins implying era (R1.6) | a ruin in 1890 was a village in 1790 — the strongest argument for eras, and out of scope until they exist |
| Wrecks as a separate class | fold into oddities first; split only if they earn it |

---

## 6. Open questions

1. **How small is small?** The whole design rests on this number, and it cannot be
   reasoned about — it must be looked at. Too large and the sheet places itself;
   too small and it never places. Report a sweep, do not assert a value.
2. **Does P2.4 do enough?** "One other feature" may not be sufficient context:
   one contour line looks like any other. The stronger version requires a feature
   that is *locally distinctive* — a coast, a river bend, a lake shore — rather
   than merely present.
3. **Does the difficulty actually resolve late, or does it just move?** The
   premise is that a filled board makes placement possible. If the board is
   assembled but the surroundings are still ambiguous, POIs become busywork
   rather than a finale.
4. **Should a detail sheet ever be unplaceable on purpose?** R1.8 leaves ground
   with no coverage; a POI in that ground would have no surroundings to match
   against. That is either a cruelty or the most interesting sheet in the
   collection.
