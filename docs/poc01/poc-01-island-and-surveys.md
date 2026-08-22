# POC-01 — Island Generation & Survey Cutting

Implementation specification. Companion to `requirements.md`; that document is
the authority on intent, this one on construction. Where they disagree, §15
(Amendments) records the delta and the reason.

Host: Unity 6000.0.34f1, URP. Target: an Editor tool, not a build.

---

## 1. Scope

### 1.1 What this POC builds

One island, generated from a seed. Three surveys cut from it, one per office.
An Editor window to look at both.

Nothing else. No room, no racks, no player, no map table, no paper texture, no
sound.

### 1.2 What this POC proves

**Primary criterion — two offices, one hillside.**

The deliverable artifact is a side-by-side comparison: one piece of ground, cut
by the Hydrographic office and by the Land Survey, at different rotations, with
different coverage and different feature classes drawn.

The POC **passes** if the two sheets are plainly different documents, both
truthful, and still recognisable as the same place.

The POC **fails** if they read as one sheet printed twice.

This is R2.7 — which §5.4 of `requirements.md` calls *"the one requirement v1
exists to test"* — with the art direction stripped out. Style is deliberately
neutral here (§8.1) so that any difference the eye finds is a difference of
*content*, not of ink.

**Secondary criteria** (supporting, not gating):

- **Pipeline soundness.** Same seed → identical island, across runs, sessions,
  and unrelated code changes. Automated (§13.2).
- **Sheet economy.** How many sheets does one island actually yield? §6.1 of
  `requirements.md` guesses 30–60. This is the first thing that can answer it
  with evidence. Reported, not asserted (§13.7).

### 1.3 What this POC explicitly does not prove

§5.4 names two v1 proofs. This POC tests **neither**, and that is intentional —
both need the generator underneath them first.

| §5.4 proof | status here | why |
|---|---|---|
| Can a player read office style at a glance? | not tested | style is neutral by §8.1; this is POC-02 |
| Does assisted fit feel like landing or fighting? | not tested | no map table in scope |

---

## 2. Vocabulary

**"Expedition" ≡ survey.** The word does not appear in `requirements.md`. It is
the informal name for what R2.2 calls a **survey**: one island, one office, one
year, one scale, one rotation, covering a coherent area with a numbered set of
sheets. This document uses *survey* throughout so the vocabulary matches the
requirements exactly. See §15.1.

| term | meaning |
|---|---|
| **ground space** | metres, island-local, origin at the centre of the generation domain |
| **frame space** | ground space rotated by a survey's rotation; sheets tile axis-aligned here |
| **paper space** | millimetres on a physical sheet |
| **survey** | R2.2. The unit of sheet generation. Informally, an "expedition" |
| **sheet** | one numbered rectangle of one survey |
| **island scale** | the coastline. Always present, the anchor for everything (R1.4) |
| **local scale** | everything inside — peaks, settlements, rivers (R1.4) |
| **class** | a kind of feature: coast, contour, peak, river, settlement, grid, sounding |

---

## 3. The central decision — the island is a function

**The island is `f(x, y)`, an analytic height field. It is never a grid.**

Contouring is a **query**, not a build step. The whole-island view contours the
domain coarsely. A sheet at 1:5000 re-contours *its own rect* against the same
function, at that sheet's detail. Same coast, more of it.

Consequences, all load-bearing:

- **Nothing geometric is ever cached or persisted.** Only the seed (R1.11, R3.1).
- **A sheet has no maximum zoom.** There is no grid pitch to stair-step against.
- **Adjacent sheets must agree at their shared border.** This is not automatic —
  it is bought by the lattice rule in §6.2, and tested in §13.3.

### 3.1 The two data paths

Not everything can be re-queried. This split runs through the whole spec.

| | **field-derived** | **discrete** |
|---|---|---|
| examples | coastline, contours, soundings, grid | peaks, settlements, rivers |
| produced by | contouring `f` over a rect | one generation pass per island |
| detail | re-queried per sheet, unbounded | fixed, stable list |
| identity | none needed | stable `FeatureId`, order matters |

A contour can be re-extracted at any resolution and stays the same line. A
*settlement* cannot — if the discrete pass ran per sheet, the same village
would land in two different places on two overlapping sheets. Discrete features
are generated **once per island**, in a **deterministic order**, with **stable
ids**.

---

## 4. Determinism

Everything is reproducible from `collection_seed` and `island_index` (R1.1).
Nothing else is stored.

### 4.1 Prohibited

- `System.Random` — not contractually stable across runtime versions.
- `UnityEngine.Random` — global state, not stable across Unity versions.
- `string.GetHashCode()` — process-randomised on .NET Core, not contractual on Mono.
- Any dictionary/set iteration order used to drive generation.
- Wall-clock, frame count, `Time.*`, or anything else ambient.

### 4.2 Hash and PRNG

```csharp
namespace Archivist.Generation.Determinism
{
    public static class Hash
    {
        public static ulong Fnv1a64(string ascii);
        public static ulong Fnv1a64(ulong seed, string ascii);
        public static ulong Mix(ulong a, ulong b);
    }

    public struct Pcg32
    {
        public Pcg32(ulong seed, ulong stream);
        public uint   NextUInt();
        public double NextDouble();                    // [0, 1)
        public double Range(double minInc, double maxEx);
        public int    Range(int minInc, int maxEx);     // unbiased, rejection
        public T      Pick<T>(IReadOnlyList<T> items);
    }
}
```

`island_seed = Hash.Mix(collection_seed, Hash.Fnv1a64(island_index))` — R1.1.

### 4.3 Named sub-streams, never one linear stream

```csharp
public static class Streams
{
    // stream id = Fnv1a64(purpose) mixed with index; independent of call order
    public static Pcg32 For(ulong islandSeed, string purpose, int index = 0);
}

var rngCoast   = Streams.For(islandSeed, "field");
var rngPeaks   = Streams.For(islandSeed, "peaks");
var rngTowns   = Streams.For(islandSeed, "settlements");
var rngSurvey  = Streams.For(islandSeed, "survey", (int)Office.Hydrographic);
```

**Rule: one stream per purpose, drawn independently.** With a single linear
stream, adding a feature type or reordering a loop reshuffles the entire
island — which would make "only the seed is persisted" a lie the first time the
code is touched. This is asserted in §13.2.

### 4.4 Floating point

Ground space is `double`. Paper and UI are `float`.

IEEE-754 `+ - * / sqrt` are deterministic across platforms. **Transcendentals
are not** — `sin`, `cos`, `atan2`, `pow` may differ in the last ulp between
libm implementations, and a last-ulp difference at a threshold can flip a cull
decision and change the sheet count.

**Rule: quantise the derived scalar that feeds the branch, not the intermediate
that feeds it.**

Quantising the transcendental itself fixes the wrong thing — it makes one
intermediate reproducible while every other float path into the same branch stays
exposed, and any quantum coarse enough to be safe is coarse enough to be seen
(quantising `theta` in §5.2 puts angular banding on the fjord coast, which at
lod 6 is contoured against a 1 m cell). In the field there is exactly one scalar
that every threshold reads, so that is the one quantised:

```
h01 = round(h01_raw * 65536) / 65536          // 2^-16 ≈ 1.5e-5
```

- **`Height01` is quantised at `2^-16`** before any comparison — marching-squares
  corner signs, the saddle centre-sign rule (§6.1), `landFraction`,
  `Elevation < -4 m`, and the relief test in §7.4. A tie at exactly `SeaLevel`
  counts as **land**, stated once here and never re-decided.
- **Derived rotations → rounded to `0.1°`.** A different exposure: an ulp here
  can shift a rect across the sheet lattice and change the sheet count.
- **`Gradient` is exempt** — computed from the unquantised composition, since a
  2 cm staircase across a central-difference step would be coarse — and
  `|Gradient|` is rounded to `1e-4` before its one branch in §7.2.
- thresholds → compared against pre-rounded values
- noise uses only multiply/add/lerp; no transcendentals in the inner loop

**The margin.** An `atan2` last-ulp difference is ~4e-16 absolute; through
`fbm1(θ·6)`, the `0.18` cut, and smoothstep's derivative it arrives at `h01`
around 1e-15. Against a 1.5e-5 quantum that is ten orders of magnitude, so the
chance a sample straddles a quantisation boundary is ~1e-10 — below one
occurrence across the whole 50-seed suite. In elevation terms the quantum is
about 2 cm, invisible at every scale in §8.1.

---

## 5. Island generation

### 5.1 Parameters

```csharp
public enum IslandCharacter { Mountainous, Fjorded, Atoll }

public readonly struct IslandParams
{
    public readonly ulong  Seed;
    public readonly IslandCharacter Character;
    public readonly double DomainMetres;    // 16000 — generation square
    public readonly double NominalRadius;   // 0.38 * Domain, jittered per seed
    public readonly double MaxElevation;    // metres, per character
    public readonly double SeaLevel;        // 0.50, normalised
}
```

Character is chosen by `Streams.For(seed, "character")`, uniformly over the
three, unless overridden in the debug UI.

### 5.2 The field

```csharp
public interface IHeightField
{
    double Height01  (double x, double y);   // normalised, sea level = 0.50
    double Elevation (double x, double y);   // metres; negative below sea
    V2     Gradient  (double x, double y);   // d(Elevation)/d(distance), m/m
}
```

Composition, in order:

```
1.  p     = (x, y) / featureScale                     featureScale = 2600 m
2.  w     = p + warpAmp * ( fbm(p + o1), fbm(p + o2) )     warpAmp = 0.45
3.  n     = fbm(w)                                    → [0, 1]
4.  r     = |(x, y)| / NominalRadius
5.  f     = Falloff(character, r, atan2(y, x))        → [0, 1]
6.  h01   = saturate( (n * f) * gainC + biasC )
7.  h01   = round(h01 * 65536) / 65536                quantised, §4.4
```

`fbm`: 5 octaves of 2D **gradient noise**, lacunarity 2.0, gain 0.5, gradients
from a 256-entry table indexed by `Hash.Mix`. No external noise library — the
implementation must be ours so it is version-stable.

Step 7 is what makes every threshold in the spec reproducible; see §4.4 for why
it sits here rather than on `theta` in step 5.

`Gradient` returns `d(Elevation) / d(distance)` in **metres per metre** — a
slope, not a normalised difference — by central difference at `h = 20 m`, against
the **unquantised** composition (§4.4). This is what gives §7.2's `< 0.04` a
unit: a slope of about 2.3°.

`Elevation(x,y) = (h01 - SeaLevel) / (1 - SeaLevel) * MaxElevation` above sea,
and `(h01 - SeaLevel) / SeaLevel * MaxDepth` below (`MaxDepth = 220 m`).

### 5.3 Falloff per character

Character is not a jittered parameter — each is **a different recipe** (R1.7).

**Mountainous** — compact, high relief, one main massif.
```
f = 1 - smoothstep(0.35, 1.00, r)
gainC = 1.15   biasC = 0.02   MaxElevation = 620 m
```

**Fjorded** — deep radial inlets, long coastline, detached islets.
```
cut = 0.18 * fbm1(theta * 6.0)          // angular high-frequency noise
f   = 1 - smoothstep(0.30, 1.00, r + cut)
gainC = 1.05   biasC = 0.00   MaxElevation = 540 m
```
The angular term pushes the coast in and out with `theta`, producing inlets;
where the field dips below sea level mid-island, islets detach naturally.

**Atoll** — ring of land around a lagoon.
```
f = 1 - smoothstep(0.00, 0.14, |r - 0.62|)
gainC = 0.95   biasC = 0.00   MaxElevation = 90 m
```
The lagoon interior falls below sea level, so the coastline extracts as **two**
closed loops — outer shore and lagoon shore. The contour code must handle
multiple loops (§6.1); this is why atoll is in the set.

> **Atoll is the deliberate stress case.** The Land Survey coverage rule (§7.3)
> keeps rects with land ≥ 60% — on a thin atoll ring, essentially none qualify,
> so the Land Survey may produce **zero sheets**. That is either a bug or the
> correct answer (the Land Survey never bothered with an atoll). Either way it
> is a finding, and it is better found now.

---

## 6. Field-derived features

### 6.1 Marching squares

```csharp
public static class Contours
{
    public static IReadOnlyList<Polyline> Extract(
        IHeightField field, Rect2 area, double cellSize, double level01);
}
```

- Standard marching squares over `area`, expanded by one cell so lines crossing
  the border are correct, then clipped back to `area`.
- Edge crossings by linear interpolation of `Height01`, using the **quantised**
  value (§4.4). Both sides of a shared border must interpolate identical numbers
  or the §6.2 lattice guarantee is lost — this is what A3 (§13.3) exists to catch.
- **Saddle cases (5 and 10) always resolve the same way** — pick the
  disambiguation by the sign of the cell-centre sample, quantised, never by a
  coin flip. Determinism depends on this.
- Segments stitched into polylines by endpoint welding at `cellSize * 1e-6`.
  Closed loops flagged.

Classes:
- **coastline** = `Extract(field, area, cell, SeaLevel)`
- **contours** = `Extract(...)` at each of `SeaLevel + k * contourStep01`,
  for `contourStep = 50 m` of elevation.

### 6.2 The lattice rule — why sheets don't tear

Adjacent sheets contour independently. If their grids are not aligned, their
coastlines will disagree along the shared border by up to one cell — a visible
tear, and a fatal one for the map table later.

**Rule: contour grids are never free. Cell size is always**

```
cellSize = BaseCell / 2^lod          BaseCell = 64 m
```

**and grid corners always land on multiples of `cellSize` measured from the
domain origin `(0,0)`.** A rect is snapped outward to the lattice before
contouring.

So every contour at every LOD samples a subset of one global lattice. Two
sheets at the same LOD sample *identical points* along their shared border and
agree exactly. Tested in §13.3.

LOD is chosen from paper detail, not from the rect:

```
targetGroundCell = 0.25 mm / 1000 * scaleDenominator
lod              = clamp( ceil(log2(BaseCell / targetGroundCell)), 0, 8 )
```

| scale | target cell | lod | actual cell |
|---|---|---|---|
| 1:25000 | 6.25 m | 3 | 8 m |
| 1:5000 | 1.25 m | 6 | 1 m |
| island view (fit to window) | — | 0–1 | 64 / 32 m |

### 6.3 Soundings

Offshore depth spot values, Hydrographic only. Sampled on a lattice of
`400 m` within the sheet rect, kept where `Elevation < -4 m`, value rounded to
the metre. Field-derived, so no stable id is needed.

### 6.4 The Garrison grid

`FeatureClass.Grid`, Garrison only (§8.3). A square grid in the **true-north
frame** — Garrison's rotation is always 0° (§10.1) — with its origin at the
**domain origin `(0,0)`** and pitch by scale. Lines carry easting and northing
labels in metres from origin.

| scale | grid pitch |
|---|---|
| 1:25000 | 1000 m |
| 1:5000 | 200 m |

Field-independent, so it is field-derived in §3.1's sense: re-queryable per rect,
no identity, nothing persisted.

The origin being global rather than per-sheet is the whole point, and it is the
same argument as §6.2's contour lattice — two adjacent Garrison sheets must show
the same grid lines in the same places, or the block stops reading as one survey.

Because the grid covers every rect unconditionally it makes A5 (§13.5) trivially
true for Garrison. That is handled by measuring rather than by gating; see A5b.

---

## 7. Discrete features

Generated **once per island**, in the order below. Each stage sorts its
candidate list by a **total order** before selecting, so results never depend
on iteration order.

```csharp
public enum FeatureClass { Coast, Contour, Peak, River, Settlement, Grid, Sounding }

public readonly struct FeatureId  { public readonly FeatureClass Class; public readonly int Index; }

public sealed class IslandFeatures
{
    public IReadOnlyList<Peak>       Peaks       { get; }
    public IReadOnlyList<Settlement> Settlements { get; }
    public IReadOnlyList<River>      Rivers      { get; }
}
```

### 7.1 Peaks

1. Sample `Elevation` on the `64 m` lattice over the land bbox.
2. Local maxima (8-neighbour), elevation ≥ `0.35 * MaxElevation`.
3. Sort by `(elevation desc, x asc, y asc)` — the total order.
4. Non-maximum suppression at radius `400 m`.
5. Keep at most `peakCap` (mountainous 9, fjorded 7, atoll 2).
6. Spot height = elevation rounded to the metre. Top 3 get names (§9).

### 7.2 Settlements

1. Candidates: land points on the `128 m` lattice within `300 m` of a coastline
   polyline, plus land points with `|Gradient| < 0.04` anywhere — a slope in
   metres per metre (§5.2), about 2.3°, with `|Gradient|` rounded to `1e-4`
   before the comparison (§4.4).
2. Score = `0.6 * shelter + 0.4 * flatness`, where `shelter` is coastline
   concavity in a `600 m` neighbourhood.
3. Sort by `(score desc, x asc, y asc)`.
4. Greedy selection with minimum spacing `1200 m`.
5. Count by character: mountainous 4–7, fjorded 5–9, atoll 1–3, drawn from
   `Streams.For(seed, "settlements")`.
6. Every settlement is named (§9).

### 7.3 Rivers

1. Source = each peak, in peak order.
2. Gradient descent, step `40 m`, with a small seeded lateral jitter
   (`±0.15 rad`) so rivers are not straight.
3. Terminate on reaching sea level, on coming within `60 m` of an existing
   river (merge), or after 400 steps.
4. Discard rivers shorter than `800 m`.
5. Atolls produce none — no relief. Expected, not a bug.

### 7.4 R1.5 — the service rule

> **R1.5** *"Every point of land must be within one island-scale feature of a
> local-scale one. Land failing this test is excluded from sheet cutting."*

Read as a distance rule (see §15.3), and as an **office-relative** one (§15.5).

```
u = 2 * NominalRadius / 8 = NominalRadius / 4        // 1520 m at default jitter

Serving(office) = drawn(office) \ { Coast }          // drawn() is FeatureMatrix, §8.3
served(p, office) = ∃ class c ∈ Serving(office) present within u of p
```

`u` is the **island-scale unit**. It is fixed to the nominal radius rather than
the land bbox so that the service radius stays independent of the coastline it is
used to judge, and stable across characters.

| office | Serving set |
|---|---|
| Hydrographic | Settlement, Sounding |
| Land Survey | Contour, Peak, River, Settlement |
| Garrison | Peak, Grid |

Presence of class `c` at point `p` within radius `u`:

| class | present when |
|---|---|
| Peak, River, Settlement | a discrete feature of that class lies within `u` of `p` |
| Sounding | some sample within `u` has `Elevation < -4 m` |
| Contour | relief within `u` spans one contour step: `max Elevation - min Elevation ≥ 50 m` |
| Grid | always |

**Why the coast is excluded.** The coastline is island-scale by R1.4, so it
cannot be the thing that makes a sheet worth cutting — every sheet in a coastal
survey carries it. Excluding it is precisely what makes the rule mean *this
office draws something here*. Three consequences then fall out instead of being
carved out: Hydrographic's coastal rects are served by soundings, so a bare
stretch of shore keeps its sheet; Garrison is served everywhere by its own grid,
so it needs no exemption; and Land Survey keeps a cull with teeth, one that now
spares a hillside with 50 m of relief and no village.

R1.5 and R1.8 (*"some ground has no sheet covering it at all"*) still collapse
into **one mechanism**: ground an office draws nothing on is unserved for that
office, unserved ground is not cut, and so it has no coverage from that office.
Blank sheets become impossible by construction rather than by tuning. On a
mountainous island the gaps are carried mostly by Land Survey's
`landFraction ≥ 0.60` edge test rather than by service, which is honest — steep
ground is worth surveying.

**Implementation.** One `ServiceRule` per island (§14) holding one bitmask per
class on the 64 m lattice over the land bbox: stamp a disc of radius `u` at each
discrete feature; separable max- and min-filter at radius `u` for relief, then
threshold at the contour step; threshold-then-max-filter for soundings; constant
true for grid. All integer and comparison work, so determinism is free (§13.2 is
unaffected). `servedFraction` reads the masks at the same 16×16 rect samples
§10.3 already takes for `landFraction`, so no extra field evaluation.

---

## 8. Sheets, paper, and scale

### 8.1 Paper is real

R2.1 defines a sheet by `centre, size, rotation, scale` — `size` is paper,
`scale` is the ratio to ground. Sheet count is therefore **not a knob**; it is
a consequence of island size and survey scale.

```csharp
public readonly struct SheetFormat   // A1
{
    public readonly double WidthMm;   // 594
    public readonly double HeightMm;  // 841
    public readonly double MarginMm;  // 40
    public double MapWidthMm  => WidthMm  - 2 * MarginMm;   // 514
    public double MapHeightMm => HeightMm - 2 * MarginMm;   // 761
}

public readonly struct MapScale
{
    public readonly int Denominator;                        // 5000 | 25000
    public double GroundMetres(double paperMm) => paperMm / 1000.0 * Denominator;
}
```

| scale | map area covers | used for |
|---|---|---|
| 1:25000 | 12 850 × 19 025 m | whole-island sheet (R2.2a), normal case |
| 1:50000 | 25 700 × 38 050 m | whole-island sheet, fallback only |
| 1:5000 | 2 570 × 3 805 m | detail surveys |

Orientation (portrait/landscape) is chosen per survey to better fit the target
region.

The whole-island sheet takes the **smallest** of `{1:25000, 1:50000}` whose map
area contains the island's land bbox in either orientation; 1:25000 is the normal
answer. The fallback replaces an assert that had no recovery: `NominalRadius` is
`0.38 × 16 000 = 6080 m` jittered ±8%, so a mountainous island whose land
approaches its nominal radius has a bbox near 13 km and overruns the 12 850 m
portrait width. Landscape covers most of those, but without margin, and the
failure mode would otherwise be a hard stop on an otherwise valid seed. Clamping
the jitter was rejected — land extent is not a clean function of `NominalRadius`,
so the clamp would be guesswork tuned to whichever seeds were tried. R2.3 permits
three or four fixed values, both working scales are untouched, and the cost is
one slightly smaller sheet on the largest islands. Frequency is reported in
§13.7.

### 8.2 Neutral rendering

Every sheet in this POC draws in **one line style** — uniform weight, black on
white. No paper tone, no ink colour, no typography, no wear, no stamps.

This is deliberate. The primary criterion (§1.2) asks whether two offices
produce *different documents*. If the offices also differed in ink and paper,
any difference the eye found would be unattributable. Style is POC-02.

### 8.3 The office × class matrix

```csharp
public static class FeatureMatrix
{
    public static bool Draws(Office office, FeatureClass cls);
}
```

|  | coast | contour | peak | river | settlement | grid | sounding |
|---|:--:|:--:|:--:|:--:|:--:|:--:|:--:|
| **Hydrographic** | ✓ | — | — | — | ✓ | — | ✓ |
| **Land Survey** | ✓ | ✓ | ✓ | ✓ | ✓ | — | — |
| **Garrison** | ✓ | — | ✓ | — | — | ✓ | — |

Binary — draw or omit. There is no "schematic" render mode.

**The shared-class invariant** (this replaces R2.7 — see §15.2):

> Any two offices whose coverage can overlap must share at least one drawn
> feature class.

```
Hydro ∩ Land = { coast, settlement }   ✓
Land  ∩ Garr = { coast, peak }         ✓
Hydro ∩ Garr = { coast }               ✓
```

The purpose behind R2.7 is **placeability**: two sheets of one place must share
enough that they are recognisably the same ground. This invariant states that
directly, and unlike "neither may omit them entirely" it is mechanically
checkable (§13.6) rather than a matter of taste.

Note where the invariant already holds by construction: on the **coast** all
three overlap and all three draw the coastline; **inland**, only Land Survey and
Garrison overlap, and both draw peaks.

---

## 9. Naming

R1.7 requires an island name and place names; R2.13 requires that *"the label
names the island it shows"* — which needs a name to exist.

**Morpheme tables + per-island phonology.** Each island draws one phonology, so
its own names cohere with each other and differ from the next island's. The
tables are data for a generator, not authored content — supply stays unbounded
per R1.2.

```csharp
public sealed class Phonology
{
    public string[] Roots;        // Kirk, Brae, Sten, Vald, Orm, …
    public string[] Suffixes;     // -wick, -holm, -ness, -voe, …
    public string[] Qualifiers;   // Nor', Little, Muckle, …
}
```

Three phonologies minimum, ~24 roots and ~10 suffixes each.

```
seed → phonology B
  island       Stenholm
  settlements  Kirkwick, Ormvoe, Little Braeness
  peaks        Braefell 412 m
```

Names are drawn from `Streams.For(seed, "names")`, in feature order, with
in-island uniqueness enforced by retry.

---

## 10. Survey cutting

A survey is one office's expedition over one island (§2).

```csharp
public enum Office { Hydrographic, LandSurvey, Garrison }

public readonly struct SurveySpec
{
    public readonly ulong      IslandSeed;
    public readonly Office     Office;
    public readonly int        Year;              // label only; no eras in v1
    public readonly MapScale   Scale;
    public readonly double     RotationDeg;       // fixed per survey (R2.4)
    public readonly SheetFormat Format;
    public readonly double     OverlapFraction;   // 0.20
}

public readonly struct Sheet
{
    public readonly SurveySpec Survey;
    public readonly int        Number;            // 1..N, contiguous
    public readonly V2         CentreGround;      // metres
    public readonly double     RotationDeg;       // == Survey.RotationDeg
}
```

### 10.1 Rotation is derived, not rolled

R2.4: *"A survey may follow a coast or a ridge and sit at any angle."* The
generator actually derives it, so rotation becomes a **third readable office
signal** alongside style and coverage, at the cost of one 2×2 covariance.

| office | rotation | degenerate case | follows |
|---|---|---|---|
| Hydrographic | principal axis (PCA) of the **main coastline loop**, sampled by arc length every `u/4` | `0.0°` if `λ1/λ2 < 1.15` | the shore |
| Land Survey | principal axis (PCA) of land points above `0.35 * MaxElevation` | `θ_hydro + 90°` if `λ1/λ2 < 1.15`, or fewer than 64 points | the ridge |
| Garrison | **0°, always** | — | true north — grid discipline is what Garrison *is* |

Main loop = the longest coastline polyline; ties broken by first vertex
`(x asc, y asc)`.

PCA: 2×2 covariance of the sampled points, take the eigenvector of the larger
eigenvalue, `atan2` → degrees, **rounded to 0.1°** (§4.4).

**Sample by arc length, not by vertex.** Marching squares emits vertices at a
density that varies with how the coast meets the lattice, so vertex-weighted PCA
is biased toward whichever stretch happens to run diagonally across cells. `u/4`
≈ 380 m is dense enough to describe the loop and coarse enough to be cheap.

**The isotropy guard is not optional.** A round island has no long axis; the
larger eigenvector is then noise, and two seeds a hair either side of isotropic
would sit their whole survey 90° apart for no reason a player could read.
`λ1/λ2 < 1.15 → 0°` makes that a stated answer rather than a coin flip. An atoll
is the standing case: its coast is a ring, isotropic by construction, so its
Hydrographic survey comes out north-up.

**Land falls back to cross-grain, not to north.** If both degenerate cases fell
back to 0°, Land Survey and Garrison would share a rotation on exactly the
islands where the third office signal is already weakest. `θ_hydro + 90°` is
geometric, deterministic, and distinct from both other offices; it reads as a
traverse run across the island rather than along it.

**Measure before hardening.** A ridge running along the island's long axis
legitimately gives `θ_hydro ≈ θ_land`. That is not a bug and must not be
pre-empted with a forced-separation rule — report the separation distribution
first (§13.7).

Note what removing the *surveyed arc* costs: nothing. The arc was circular — it
is a product of cutting, and rotation is step 1 of cutting — and Hydrographic's
cull already keeps every rect the coastline crosses, so the survey follows the
whole shore and there was never an arc to speak of. The consequence to watch is
that a whole-shore ring is the single largest driver of total sheet count; if A7
lands far above 30–60, that ring is the first knob to reach for, before the scale
table or the domain size.

### 10.2 The cutting algorithm

1. **Rotation** `θ` per §10.1.
2. **Frame** — rotate ground space by `−θ`. Sheets tile axis-aligned in frame
   space and are rotated rects in ground space.
3. **Extent** — project the island's land bbox into frame space; take its
   frame-space bbox.
4. **Lattice** — sheet ground size from §8.1. `step = size * (1 − overlap)`.
   Lay the lattice **centred** on the extent, so leftover margin is split
   evenly rather than dumped on one edge.
5. **Candidates** — one rect per lattice cell, centre transformed back to
   ground space.
6. **Cull** per office (§10.3), which includes the office-relative R1.5 service
   test (§7.4) — applied to all three offices, with no exemption.
7. **Order** — row-major in frame space, `+u` then `+v`, origin bottom-left.
8. **Number** `1..N`.

### 10.3 Cull rules — office drives coverage

Both R1.8 (*coverage must be partial*) and R2.10a (*overlap is required*) are
satisfied by construction here, not by tuning.

Sampling: each rect is sampled on a 16×16 lattice in frame space.
`landFraction` = fraction of samples with `Height01 ≥ SeaLevel`.
`servedFraction` = fraction of *land* samples that are `served` (§7.4).

| office | keeps | shape produced |
|---|---|---|
| **Hydrographic** | rects any coastline polyline crosses, **and** `servedFraction ≥ 0.50` | a ring following the shore, sea on one side |
| **Land Survey** | `landFraction ≥ 0.60` **and** `servedFraction ≥ 0.50` | a filled interior blob |
| **Garrison** | every rect in the chosen block with `landFraction ≥ 0.02`, **and** `servedFraction ≥ 0.50` | a tidy rectangular block that ignores geography |

`servedFraction` is office-relative (§7.4), so one uniform test means something
different in each row and no office needs an exemption. Hydrographic is served by
its soundings, so a bare stretch of shore keeps its sheet. Garrison is served
everywhere by its own grid, so the test is a no-op there and the `≥ 2%` land
threshold is what removes pure-sea rects, leaving no sheet literally blank. Land
Survey is the only office the service test actually culls.

**Garrison's block**: in the true-north frame, pick the quadrant of the land
bbox containing the most land; the block is that quadrant's land bbox expanded
outward to whole sheets.

Resulting coverage picture:

```
coast     covered ×3   (Hydro ∩ Land ∩ Garrison)
interior  covered ×1–2 (Land, sometimes Garrison)
remote /  covered ×0   ← R1.8 satisfied
featureless
```

### 10.4 Cull first, then number

**Sheet numbers are assigned after culling, never before.**

If a 6×4 grid is numbered 1–24 and the sea-only rects are then dropped, the
survey ships with permanent holes at 3, 7, 18. R2.10b requires that *"a gap in
a run is unambiguous"* — a gap must mean **missing sheet**. R2.9 (incomplete
surveys) is cut from v1 by §5.2, so every numbered sheet must exist. Asserted
in §13.4.

### 10.5 The whole-island survey (R2.2a)

Every island carries one. One office, chosen by
`Streams.For(seed, "wholeIsland")`; rotation 0°; one sheet centred on the land
bbox; orientation chosen to fit; scale the smallest of `{1:25000, 1:50000}` that
contains the land bbox (§8.1), which is 1:25000 in the normal case. It is the entry point for
the island and, in v1, doubles as the reference map (§5.2 of `requirements.md`).

---

## 11. Debug UI

`Window → Archivist → Island Debug`. An `EditorWindow` built on UI Toolkit,
drawing with `Painter2D` — a real vector path API (`BeginPath`/`LineTo`/
`Stroke`), an exact fit for polyline features.

No scene, no play mode, no camera, no build.

### 11.0 Layout

```
┌─ collection seed [8412]  island [0]  [Regenerate]  character [Auto ▾] ─────┐
├───────────────────────────┬───────────────────────────────────────────────┤
│                           │  surveys                                      │
│      ISLAND PANE          │   ▸ Hydrographic 1892   16 sheets   rot 34.2° │
│   coast, contours,        │   ▸ Land Survey  1913   12 sheets   rot 108.7°│
│   rivers, peaks, towns    │   ▸ Garrison     1927    9 sheets   rot 0.0°  │
│   + sheet outlines        │   ▸ whole-island 1874     1 sheet             │
│     colour-coded          │                                               │
│                           │  layers  ☑coast ☑contour ☑peak ☑river ☑town   │
├───────────────────────────┴───────────────────────────────────────────────┤
│  Stenholm · fjorded · 38 sheets · coast ×3 · gaps 22% land · gen 84 ms    │
└───────────────────────────────────────────────────────────────────────────┘
```

**Pane 1 — Island.** Whole island, fit to view, pan/zoom. Draws every feature
class. Sheet outlines overlaid as rotated rects, colour-coded by office,
toggleable per survey. Hover → tooltip with office and sheet number. Click →
opens Pane 2.

**Pane 2 — Sheet.** One sheet drawn at paper aspect with its margin. **Only the
classes that office draws** (§8.3). Header strip: island name, office, sheet
number, year, scale, rotation. A *true size* toggle renders the paper at actual
millimetres, which is the only way to judge whether 16 sheets per survey reads
as an archive or as a chore.

**Pane 3 — Compare.** *This is the acceptance artifact.* Click a point on the
island, or pick a sheet; the pane lists every sheet covering that point and
renders up to four side by side, each cropped to the shared intersection.
Optional north-up normalisation to separate "different because rotated" from
"different because differently drawn".

**Stats footer.** Sheet count per office and total, coast/interior/gap
percentages, overlap histogram, thin-sheet percentage per office (A5b, §13.5),
the whole-island scale actually used, and generation time. These are the
§13.6–§13.7 numbers.

**Export** (optional, cheap): writes `island.svg`, one SVG per sheet, and
`manifest.json` to a folder — diffable, shareable, pasteable.

---

## 12. Tuning values

Every constant in one place. Defaults are starting points, not findings.

| value | default | affects |
|---|---|---|
| `DomainMetres` | 16 000 m | island size ceiling |
| `NominalRadius` | 0.38 × domain (jittered ±8%) | island size |
| `SeaLevel` | 0.50 | land/sea ratio |
| `featureScale` | 2 600 m | coastline wiggle wavelength |
| `warpAmp` | 0.45 | coastline organic-ness |
| fbm octaves / lacunarity / gain | 5 / 2.0 / 0.5 | roughness |
| `MaxElevation` | 620 / 540 / 90 m | relief per character |
| `MaxDepth` | 220 m | soundings range |
| `contourStep` | 50 m | contour density |
| `BaseCell` | 64 m | LOD lattice root |
| `h01` quantum | `2^-16` ≈ 1.5e-5 | determinism at thresholds (§4.4) |
| gradient step `h` | 20 m | central difference (§5.2) |
| paper detail target | 0.25 mm/cell | LOD selection |
| `SheetFormat` | A1, 594×841, 40 mm margin | ground per sheet |
| scales | 1:5000, 1:25000, + 1:50000 whole-island fallback | ground per sheet, sheet count |
| grid pitch | 1000 m @ 1:25000, 200 m @ 1:5000 | Garrison grid density (§6.4) |
| `OverlapFraction` | 0.20 | R2.5 (spec says 10–25%) |
| `u` (service radius) | `NominalRadius / 4` | R1.5 cull severity |
| served threshold | 0.50 | R1.5 cull severity |
| PCA isotropy threshold | `λ1/λ2 < 1.15` | rotation degenerate fallback (§10.1) |
| PCA coast sample step | `u / 4` ≈ 380 m | rotation stability (§10.1) |
| Land Survey PCA min points | 64 | rotation degenerate fallback (§10.1) |
| Land `landFraction` min | 0.60 | Land Survey coverage |
| Garrison `landFraction` min | 0.02 | blank-sheet floor |
| peak NMS radius | 400 m | peak density |
| `peakCap` | 9 / 7 / 2 | peak density |
| settlement min spacing | 1 200 m | settlement density |
| settlement count | 4–7 / 5–9 / 1–3 | settlement density |
| river min length | 800 m | river count |
| sounding lattice | 400 m | Hydro sheet density |

---

## 13. Acceptance

### 13.1 A1 — Two offices, one hillside · **primary** · manual

Open Compare on a coastal point covered by both Hydrographic and Land Survey.
The pane shows two sheets of one place.

**Pass:** plainly different documents; both truthful; both recognisably the same
ground. **Fail:** interchangeable.

### 13.2 A2 — Determinism · automated

- Generating `seed` 100 times yields an identical hash over serialised island +
  features + sheets.
- Adding a new named stream, or drawing extra values from an unrelated stream,
  leaves every existing feature bit-identical.
- No `System.Random` / `UnityEngine.Random` / `GetHashCode` in the Generation
  assembly (source assertion).

### 13.3 A3 — No seams · automated

Two adjacent sheets of one survey, contoured independently at the same LOD,
produce coastline vertices that agree along their shared border to within
`1e-6 * cellSize`. This tests the §6.2 lattice rule, which is the whole basis
of "the island is a function".

### 13.4 A4 — Numbering · automated

For every survey: numbers are exactly `1..N`, contiguous, no duplicates,
row-major in frame space. (§10.4)

### 13.5 A5 — No blank sheets · automated

Every sheet contains at least one drawn feature *for its own office* under
§8.3. (§7.4 should make this impossible; the test proves it.)

`Grid` counts. A Garrison sheet showing empty moorland under a grid passes,
because that is a real document and Garrison is exactly the office that would
have made it. But the pass then costs nothing, so the vacuousness is measured
instead of hidden.

### 13.5a A5b — Thin sheets · metric, reported per office

Percentage of sheets whose only content is `Coast` and/or `Grid`. Reported, not
gated — the same posture as A6 and A7. If Garrison comes back at 60%, that is the
evidence-backed answer to whether the office is a chore, and it belongs beside
the sheet-economy numbers in the §11 footer.

### 13.6 A6 — Shared-class coverage · metric, target ≥ 90%

For every pair of overlapping sheets from different offices: does the
intersection contain at least one feature of a class **both** offices draw?
Reported as a percentage. Below 90% means sheets exist that cannot be
cross-referenced — a real finding, so this is measured, not asserted.

### 13.7 A7 — Sheet economy · metric

Total sheets per island, per character, over 50 seeds. `requirements.md` §6.1
guesses 30–60. Report the distribution; if it lands far outside, the scale or
the domain size is wrong, not the guess. If it is far *above*, the Hydrographic
whole-shore ring is the first knob (§10.1).

Three further quantities are reported from the same 50 seeds, because each
settles a decision that was deliberately measured rather than guessed:

- **Land-bbox extents, per character**, and how often the whole-island sheet
  falls back to 1:50000 (§8.1). Run this immediately after step 3 of §18, before
  the cutter exists.
- **Rotation separation** — the distribution of `|θ_hydro − θ_land| mod 180°`
  (§10.1). A forced-separation rule is decided from these numbers or not at all.
- **Thin-sheet percentage** per office (A5b, §13.5).

### 13.8 A8 — Performance · automated

- Island generation (field + discrete features + island-view contours): **< 250 ms**
- One sheet re-contoured at 1:5000: **< 50 ms**

---

## 14. Code layout

```
Assets/Archivist/
  Generation/                        asmdef: Archivist.Generation
    Determinism/  Hash.cs  Pcg32.cs  Streams.cs
    Geometry/     V2.cs  Rect2.cs  Polyline.cs  MarchingSquares.cs  Pca.cs
    Field/        Noise.cs  Falloff.cs  IslandField.cs  IslandCharacter.cs
    Features/     Peaks.cs  Settlements.cs  Rivers.cs  Soundings.cs
                  IslandFeatures.cs  ServiceRule.cs
    Naming/       Phonology.cs  NameGenerator.cs
    Sheets/       SheetFormat.cs  MapScale.cs  Office.cs  SurveySpec.cs
                  Sheet.cs  SurveyCutter.cs  FeatureMatrix.cs
    Island.cs                        façade: seed → Island
  Editor/                            asmdef: Archivist.Editor → Generation
    IslandDebugWindow.cs  IslandPane.cs  SheetPane.cs  ComparePane.cs
    VectorDraw.cs  SvgExport.cs
  Tests/                             asmdef: Archivist.Tests → Generation
    DeterminismTests.cs  ContourSeamTests.cs  CutterTests.cs  MetricTests.cs
```

**The Generation assembly does not reference `UnityEngine`.** Its own `V2`,
`Rect2`, `Polyline`. Conversion to `Vector2` happens at the Editor boundary.
This keeps it unit-testable, version-stable, and reusable at runtime later
without touching a line.

---

## 15. Amendments to `requirements.md`

`requirements.md` is **unchanged**. These deltas are recorded here so each is
traceable to the POC that caused it, and can be folded upstream once this POC
has actually tested them.

§15.1–§15.4 predate implementation. §15.5–§15.9 come from
`poc-01-decisions.md`, which resolved the five points on which this document was
self-contradictory, circular, or silent; three of those touch a requirement and
two are internal corrections, and each is labelled accordingly. All five are
folded into the body above — the entries below record what changed and why.

### 15.1 "Expedition" ≡ survey

The word does not appear in `requirements.md`. It is the informal name for R2.2's
**survey**. No new concept; no requirement changes. (§2)

### 15.2 R2.7 replaced by the shared-class invariant

**Conflict.** §2's office table says Hydrographic *"draws badly / omits: anything
inland"*, while R2.7 says *"Two sheets covering the same ground in different
styles must show the same underlying features, drawn differently. **Neither may
omit them entirely.**"* These cannot both be literally true: if a Hydrographic
sheet and a Land Survey sheet both cover one hillside, either the Hydro sheet
shows the contours (violating the table) or it does not (violating R2.7).

**Replacement:**

> Any two offices whose coverage can overlap must share at least one drawn
> feature class. Outside its remit, an office may omit freely.

**Reason.** The purpose behind R2.7 is placeability, not completeness. The
invariant states the purpose directly, is satisfied by the office matrix in
§8.3, and is mechanically checkable (§13.6) where the original is a judgement
call. It also preserves the office blind spots that make the offices worth
having.

### 15.3 R1.5 read as a distance rule

*"Within one island-scale feature of a local-scale one"* is not a distance and
cannot be implemented as written. Read as a unit-of-measure phrasing — within
one island-scale-unit `u = islandDiameter / 8` — per §7.4. The intent is legible:
no sheet should be blank paper.

### 15.4 Untouched

- **R1.6** (era, feature validity ranges) — out of scope, no era in POC.
- **R7.3a / §6.11** (do references lie?) — unaffected. There are no separate
  references here; the whole-island sheet *is* the reference, per §5.2.
- **§6.1** (sheets per island) — not decided, **measured** (§13.7).

### 15.5 R1.5 is office-relative, and the coast never serves

*Amends `requirements.md` — extends §15.3.* Folded into §7.4, §10.2, §10.3, §12.

**Conflict.** §10.2 step 6 applied the R1.5 service cull to every office, while
the §10.3 table carried `servedFraction` only in the Land Survey row and exempted
Garrison in prose. Taken literally, a Hydrographic rect on a bare stretch of
shore — coastline plus a field of soundings, plainly not blank — was culled for
having no settlement within `u`, which broke both the "coast covered ×3" picture
in §10.3 and the premise of §13.5.

**Replacement.**

> `served` is tested against the classes an office draws **other than the
> coast**. The cull then applies uniformly to all three offices.

**Reason.** The coastline is island-scale by R1.4, so it cannot be what makes a
sheet worth cutting. Excluding it makes the rule mean *this office draws
something here*, and the three special cases stop being special: Hydrographic is
served by soundings, Garrison by its own grid, and Land Survey keeps the only
cull that was ever doing work — a better one, since a hillside with relief and no
village now survives. `u` is additionally pinned to `NominalRadius / 4`;
§7.4 said `islandDiameter / 8` and never defined `islandDiameter`.

### 15.6 Rotation derives from the main coast loop, not from an arc

*Amends `requirements.md` R2.4 in reading only.* Folded into §10.1, §12, §13.7.

**Conflict.** §10.1 derived Hydrographic rotation from "the coastline points in
the surveyed arc", but the arc is a product of cutting and rotation is step 1 of
cutting. It could not be computed. The spec also had no answer for a
near-circular island, where the larger eigenvector is noise and a hair of seed
difference flips the whole survey by 90°.

**Replacement.** PCA over the longest coastline loop, sampled by arc length;
`λ1/λ2 < 1.15 → 0°`; Land Survey falls back to `θ_hydro + 90°` rather than to
north so it never collides with Garrison.

**Reason.** Removing the arc costs nothing — the Hydrographic cull already keeps
every rect the coast crosses, so the survey follows the whole shore. R2.4 says a
survey "may follow a coast or a ridge and sit at any angle", which the loop axis
satisfies; the degenerate cases are where R2.4 is silent and the generator cannot
be.

### 15.7 Quantise `Height01`, not the transcendental

*Internal correction.* Folded into §4.4, §5.2, §6.1, §7.2, §12.

**Conflict.** §4.4 required any transcendental feeding a branch to be quantised;
§5.2 step 5 feeds `atan2(y, x)` into the Fjorded falloff and `h01 ≥ SeaLevel` is
a branch. Obeying the rule as written meant quantising `theta`, which fixes one
intermediate while leaving every other float path exposed, and bands the fjord
coast at a scale the 1 m lod-6 cell would show.

**Replacement.** Quantise the derived scalar that feeds the branch — `h01`, at
`2^-16` — with `Gradient` exempt and its own `1e-4` rounding at its single
branch. Ten orders of margin over `atan2` noise; 2 cm of elevation; no banding.

Also fixed here: `Gradient` had no stated quantity and no step, which left
§7.2's `|Gradient| < 0.04` without a unit. It is now m/m at `h = 20 m`.

### 15.8 The Garrison grid is defined; A5 gains a companion metric

*Internal correction.* Folded into §3.1, §6.4 (new), §11, §12, §13.5.

**Conflict.** `FeatureClass.Grid` existed and §8.3 had Garrison drawing it, but
no section generated it and §12 had no entry for it. Adding it naively would then
have made A5 automatically true for Garrison — the one office whose sheets most
need the check.

**Replacement.** A true-north grid on the domain origin, 1000 m at 1:25000 and
200 m at 1:5000, field-derived. Grid counts for A5, and A5b reports the
percentage of sheets carrying nothing but coast and grid.

**Reason.** A grid sheet of empty moorland is a real document and Garrison is the
office that would have made it, so failing it would be wrong. Measuring it is the
honest alternative, and the number is the evidence for whether the office is a
chore. Giving Garrison a fourth class was rejected: §2 of `requirements.md` has
it drawing roads and sightlines, neither of which this POC generates.

### 15.9 The whole-island sheet picks the smallest scale that fits

*Amends `requirements.md` R2.3 — a third fixed value.* Folded into §8.1, §10.5,
§12, §13.7.

**Conflict.** §8.1 asserted that the island bbox fits the 1:25000 map area and
gave no fallback. A mountainous island near the top of the `NominalRadius` jitter
has a bbox around 13 km against a 12 850 m portrait width, so the assert can fire
on a valid seed.

**Replacement.** The whole-island survey takes the smallest of
`{1:25000, 1:50000}` that fits in either orientation.

**Reason.** R2.3 permits three or four fixed scale values. Clamping the jitter
instead was rejected — land extent is not a clean function of `NominalRadius`, so
any clamp is tuned to whichever seeds were tried. Fallback frequency is reported
in §13.7 rather than assumed to be rare.

---

## 16. Traceability

| § | requirement |
|---|---|
| 3, 5 | R1.1, R1.2, R1.3, R1.11 |
| 3.1, 6, 7 | R1.4, R1.5 |
| 5.3 | R1.7 |
| 7.4, 10.3 | R1.8 |
| 8.1, 10 | R2.1, R2.2, R2.3, R2.4 |
| 10.5 | R2.2a |
| 10.2 | R2.5 |
| 8.3, 15.2 | R2.6 (deferred), R2.7 (amended) |
| 10.4 | R2.10, R2.10b |
| 10.3 | R2.10a |
| 9 | R2.13 |
| 6.2, 4 | R3.1 |

---

## 17. Out of scope

Deferred by `requirements.md` §5.2, and additionally deferred by this POC.

| deferred here | goes to |
|---|---|
| Office style — ink, paper, typography, wear (R2.6) | POC-02 (§5.4 proof 1) |
| The map table and assisted fit (§3.6) | POC-03 (§5.4 proof 2) |
| The archive, racks, handling, sound (§3.4, §3.5) | v1 |
| Multiple islands, the index (§3.7) | v1 |
| Eras and feature validity (R1.6) | post-v1 |
| Sibling islands (R1.10) | post-v1 |
| Index diagrams (R2.8), incomplete surveys (R2.9) | post-v1 |
| Unclassifiable sheets (R2.11), uneven distribution (R2.12) | post-v1 |
| Levels of detail, texture caching (R3.2, R3.3, R3.4) | v1 |

---

## 18. Build order

Each step is testable before the next begins.

1. `Determinism/` + tests (§13.2)
2. `Geometry/` + marching squares + seam test (§13.3)
3. `Field/` + three characters — first visual: island pane, coast only
4. Contours, then `Features/` — island pane complete
5. `Naming/`
6. `Sheets/` cutter + numbering test (§13.4) — island pane with sheet outlines
7. Sheet pane + office matrix + blank-sheet test (§13.5)
8. Compare pane → **A1, the primary criterion** (§13.1)
9. Metrics + stats footer (§13.6, §13.7), performance (§13.8)
10. SVG export (optional)
