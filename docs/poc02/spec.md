# POC-02 — Island Colour Rendering · Specification

Construction. Companion to `requirements.md`, which is the authority on intent.
Host: Unity 6000.0.34f1, URP. Target: an Editor tool plus a headless harness.

Read `../generation_for_agents.md` first — this document assumes the generator's API.

---

## 1. Architecture

A **new assembly**, `Archivist.Render`, referencing `Archivist.Generation` and,
like it, **not referencing `UnityEngine`** (T3.2). Keeping it separate from
`Generation` keeps that assembly's determinism contract narrow: `Generation`
answers *what the island is*, `Render` answers *how it looks*.

```
Assets/Archivist/
  Generation/     asmdef Archivist.Generation   (unchanged, do not touch)
  Render/         asmdef Archivist.Render -> Generation, noEngineReferences
    ImageBuffer.cs      RenderRequest.cs   IslandRenderer.cs
    Palette.cs          Bands.cs           Strokes.cs
    RenderTuning.cs     PngWriter.cs
  Editor/         asmdef Archivist.Editor -> Generation, Render
    TexturePane.cs
```

---

## 2. Spaces

Inherits POC-01's vocabulary and adds one.

| space | unit | origin |
|---|---|---|
| ground | metres | domain centre `(0,0)` |
| frame | metres, ground rotated by `-θ` | as ground |
| **image** | **pixels, +x right, +y DOWN** | **top-left of the buffer** |

Image space is y-down because that is what every raster consumer expects. The
ground→image transform therefore flips y; get this wrong and every render is
mirrored, which is easy to miss on a roughly symmetric island.

---

## 3. API

```csharp
namespace Archivist.Render
{
    public sealed class ImageBuffer
    {
        public ImageBuffer(int width, int height);
        public int Width { get; }
        public int Height { get; }
        public byte[] Pixels { get; }          // RGBA32, row-major, 4 * W * H
        public void SetPixel(int x, int y, Rgba c);
        public ulong ContentHash();            // FNV-1a over Pixels, for A-tests
    }

    public readonly struct Rgba
    {
        public readonly byte R, G, B, A;
        public Rgba(byte r, byte g, byte b, byte a = 255);
        public static Rgba Lerp(Rgba a, Rgba b, double t);
        public static Rgba FromHex(string rrggbb);
    }

    public readonly struct RenderRequest
    {
        public readonly Rect2  Area;              // ground-space rect to cover
        public readonly double RotationDeg;       // 0.1-quantised (§5)
        public readonly double PixelsPerMetre;
        public readonly double PixelsPerPaperMm;  // stroke widths only; default 2.7
        public readonly LayerMask Layers;

        public static RenderRequest ForIsland(Island isl, double pixelsPerMetre);
        public static RenderRequest ForSheet (Sheet sheet, double pixelsPerPaperMm);
    }

    [System.Flags]
    public enum LayerMask
    {
        Fill = 1, Coast = 2, Rivers = 4, Settlements = 8, Peaks = 16, Soundings = 32,
        All = 63
    }

    public static class IslandRenderer
    {
        public static ImageBuffer Render(Island island, RenderRequest req);
    }

    public static class PngWriter
    {
        public static void Write(ImageBuffer buf, string path);   // debug only (T3.3)
    }
}
```

`ForSheet` derives `PixelsPerMetre = PixelsPerPaperMm * 1000 / scaleDenominator`,
so one paper setting gives every office the same detail on paper regardless of
scale — 1:5000 Hydrographic and 1:2500 terrain sheets look equally sharp in hand.

`Render` takes an `Island`, not a seed: per-island normalisation (§6.2) needs the
island's highest peak, so a sheet render is **not** a pure function of its rect.
Callers generate the island once (~115 ms) and render many sheets from it.

---

## 4. Pipeline

```
1  resolve  normalisation (§6.2) and palette (§6.4) from the island — once
2  fill     one field sample per pixel -> band -> colour        (§6)
3  strokes  vector overlays, clipped to the rect                (§7)
```

Fill first, strokes over. Nothing reads back a pixel it wrote except the stroke
compositor, so step 2 is embarrassingly parallel over rows (T4.4).

Image dimensions:

```
W = max(1, round(Area.Width  * PixelsPerMetre))
H = max(1, round(Area.Height * PixelsPerMetre))
```

`round`, not `ceil`, and computed once — so the same request always yields the
same dimensions without depending on accumulated float error.

---

## 5. Determinism

Inherits §4.1's prohibitions verbatim: no `System.Random`, no `UnityEngine.Random`,
no `string.GetHashCode`, no wall-clock, no dictionary or set iteration order
driving output. Extend `Tools/check-sources.sh` to cover `Render/`.

**The rotation rule.** `RotationDeg` is already quantised to 0.1° by the
generator. Compute `cos`/`sin` **once per render**, then walk pixels by pure
addition:

```
step_x = (cos θ, -sin θ) / pixelsPerMetre        // ground metres per +1 image x
step_y = (sin θ,  cos θ) / pixelsPerMetre        // ground metres per +1 image y
origin = ground position of pixel centre (0,0)
p(x,y) = origin + x * step_x + y * step_y
```

No transcendental in the inner loop — the same argument as §4.4, which quantised
`h01` rather than `theta`. Accumulate `p` per row from `origin + y * step_y`
rather than incrementally across the whole image, so error cannot build up along
a scanline.

**Colour is a threshold on an already-quantised value.** `Height01` is quantised
at `2^-16` upstream, `Elevation` derives from it, and band selection is a
comparison — so band index is exactly as reproducible as the field. Nothing in
the fill path needs its own quantisation.

**Parallelism is permitted and does not affect determinism**, because each pixel
writes only its own bytes and reads no other pixel. Rows may be dispatched in any
order. Stroke compositing runs single-threaded after the fill.

---

## 6. The fill

### 6.1 Per pixel

```
p    = ground position of the pixel centre        (§5)
h01  = field.Height01(p.x, p.y)                   // quantised upstream
elev = field.Elevation(p.x, p.y)
band = elev >= 0 ? LandBand(elev / norm) : SeaBand(elev)
rgba = palette[band]
```

One field sample per pixel. No supersampling in v1 — see `requirements.md` §5.4;
if thin features alias out at low resolution, 2x2 supersampling of the fill is
the fix and costs linearly.

**Sea/land boundary.** Use `h01 >= SeaLevel` for the land test, not `elev >= 0`,
so the fill agrees exactly with everything else in the codebase (§4.4 states the
tie at `SeaLevel` counts as land).

### 6.2 Normalisation (T2.2)

```
norm = island.Features.Peaks.Count > 0
     ? island.Features.Peaks[0].SpotHeightM          // peaks sort (elev desc, x, y)
     : IslandParams.MaxElevationFor(character);      // atolls often have none
norm = max(norm, 1.0);                               // never divide by zero
```

Resolve once per island and pass it down; never recompute per pixel or per sheet,
or two sheets of one island could normalise differently and stop cohering.

### 6.3 Bands

Land, normalised `t = elev / norm`, clamped to `[0,1]`:

| t | band |
|---|---|
| 0.00 – 0.02 | shore |
| 0.02 – 0.12 | lowland |
| 0.12 – 0.28 | rising |
| 0.28 – 0.45 | mid |
| 0.45 – 0.62 | upper |
| 0.62 – 0.78 | high |
| 0.78 – 0.92 | bare |
| 0.92 – 1.00 | summit |

Sea, **absolute metres** (T2.3), `MaxDepth` is a global 220 m:

| elevation | band |
|---|---|
| < −120 m | deep |
| −120 – −40 m | offshore |
| −40 – −4 m | shallow |
| −4 – 0 m | foreshore |

The `−4 m` edge is deliberate: it is `Tuning.SoundingDepth`, so the shallow-water
colour boundary and the sounding cut-off are the same line, and a Hydrographic
sheet's soundings sit exactly where its water colour changes.

### 6.4 Palette

Placeholder art direction — a plain hypsometric ramp, to be replaced wholesale.

```
deep #16324f   offshore #22557d   shallow #3f86ad   foreshore #7fb4cd
shore #e8ddc0  lowland #a9c07a    rising  #8fb268   mid       #b4bd6e
upper #cfc177  high    #c9a86a    bare    #b2895e   summit    #cfc4bb
```

Resolved through a seam so seed tints can arrive later (T2.4):

```csharp
public static class Palette
{
    public static Rgba[] ForIsland(Island isl);   // returns Global today
    public static readonly Rgba[] Global;
}
```

Reserve the stream name `"palette"` now — `Streams.For(seed, "palette")`. §4.3
guarantees adding a stream later cannot reshuffle anything that already exists,
so the door stays open at zero cost and zero risk.

---

## 7. Strokes

Drawn after the fill, clipped to the rect, composited with coverage-based
anti-aliasing. **Only the strokes are anti-aliased** — band edges stay hard,
because that is what a hypsometric map looks like.

Widths are in **paper millimetres**, converted by `PixelsPerPaperMm`, so a
feature has the same apparent weight on every sheet whatever its scale:

| feature | source | form | width / size |
|---|---|---|---|
| coastline | `Contours.Extract` at the render LOD | polyline | 0.35 mm |
| rivers | `island.Features.Rivers[i].Course` | polyline | 0.25 mm |
| settlements | `island.Features.Settlements` | ring mark | 1.2 mm dia |
| peaks | `island.Features.Peaks` | triangle | 1.6 mm |
| soundings | `Soundings.ForRect` | dot | 0.5 mm |

No labels, no numerals (T2.6).

**The LOD rule, and it is load-bearing.** The coastline stroke must be extracted
at a cell size matched to the pixel, not at some fixed LOD:

```
lod = LodForGroundCell(1.0 / PixelsPerMetre)      // cell ~= 1 pixel
```

Otherwise the fill's water edge is computed per-pixel from the analytic field
while the stroke follows a polyline extracted at, say, 32 m cells — and the coast
line visibly floats off the water. Tying them makes them agree by construction.
This is the same argument as §6.2 of POC-01, applied to the raster.

---

## 8. Output

`ImageBuffer.Pixels` is RGBA32, row-major, top-left origin — exactly what
`Texture2D.LoadRawTextureData` consumes, so the Unity path is a copy with no
decode and Unity generates mipmaps (T3.4).

`PngWriter` writes a real `.png` using stored (uncompressed) deflate blocks plus
the required adler32 and crc32 — roughly 120 lines, no external dependency, and
correct rather than fast. Debug only.

Filenames encode the request so exports are self-describing and diffable:

```
island_s<seed>_px<pixelsPerMetre>.png
sheet_s<seed>_<office>_<number>_pp<pixelsPerPaperMm>.png
```

**Caching is out of scope for this POC.** R3.1 wants generated-on-demand-and-
cached, but nothing here re-renders often enough to need it, and a cache would
obscure the honest per-render timings A4 exists to report.

---

## 9. Debug UI

A fourth tab in the existing `Window → Archivist → Island Debug`, beside Island /
Sheet / Compare, so the seed, index and character controls are shared.

- **Texture tab.** Island overview on the left, the selected sheet on the right,
  each at its own resolution — this pairing *is* the primary criterion (T5.2), so
  it is the default view, not an option.
- Resolution sliders for both panes, live, with the resulting pixel dimensions
  and the measured render time shown next to each.
- Layer toggles mapping to `LayerMask`.
- A **resolution sweep** button: renders the current island at a ladder of
  `pixelsPerMetre` values, reporting dimensions and milliseconds for each. This
  is how T4.3's open question gets answered with evidence.
- Export buttons writing PNGs to a chosen folder.

The window is destroyed by every domain reload; reopen from the menu.

---

## 10. Tuning

One class, `RenderTuning.cs`, mirroring `Tuning.cs`'s role — no magic numbers
elsewhere.

| value | default | affects |
|---|---|---|
| `IslandPreviewPxPerMetre` | 0.10 | overview default (starting point only) |
| `SheetPxPerPaperMm` | 2.7 | ~68 dpi; sheet default |
| `CoastWidthMm` | 0.35 | stroke weight |
| `RiverWidthMm` | 0.25 | stroke weight |
| `SettlementMarkMm` | 1.2 | mark size |
| `PeakMarkMm` | 1.6 | mark size |
| `SoundingDotMm` | 0.5 | mark size |
| land band edges | §6.3 | colour distribution |
| sea band edges | §6.3 | colour distribution |

Every default is a starting point, not a finding — §12's posture.

---

## 11. Acceptance

### B1 — Same place, two scales · **primary** · manual

Render an island overview and a sheet covering part of it, side by side at their
own resolutions. **Pass:** a viewer locates the sheet's ground on the overview
unaided. **Fail:** the sheet could be anywhere.

### B2 — Determinism · automated

`ContentHash()` is identical over 100 renders of one request. Drawing from an
unrelated named stream first leaves it unchanged. Source assertions over
`Render/` per §5.

### B3 — Coherence · automated

Render two rects covering common ground with **different origin, rotation and
resolution**. For ground points sampled in the overlap, take the nearest pixel in
each and compare band index. **Target: ≥ 99% agree exactly**, and every
disagreement lies within one pixel of a band edge.

Not 100%, and the reason matters: two rasters at different rotations sample
different points of `f(x,y)`, so unlike A3's contour seams this can never be
exact. The images agree about *where the coast is*; they cannot agree pixel for
pixel. B3 tests the former.

### B4 — Performance · metric, reported

Render time for the island overview and for one sheet, per character, at the
default resolutions, single-threaded, with pixel counts. Reported, not gated —
until T4.3's sweep settles what resolution is needed, a budget would be a guess.

### B5 — Resolution sweep · metric, reported

Overview and sheet rendered at a ladder of resolutions, with time and dimensions
for each, exported as PNGs for eyeballing. **This is how open question 1 in
`requirements.md` gets answered.**

---

## 12. Build order

Each step is testable before the next begins.

1. `ImageBuffer`, `Rgba`, `PngWriter` + a synthetic test image — proves the
   format and the encoder before any field work.
2. Ground↔image transform + `RenderRequest` — verify a rotated rect maps corner
   to corner, and that y is not flipped.
3. Fill: bands, normalisation, global palette — first real render, island only,
   `Fill` layer alone.
4. B2 determinism + source assertions.
5. Strokes with the §7 LOD rule — coast first, and confirm it sits on the water
   edge before adding the rest.
6. Sheet rendering via `ForSheet`, rotation exercised.
7. B3 coherence.
8. Texture tab in the debug window → **B1, the primary criterion**.
9. B4 and B5 metrics, PNG export.
