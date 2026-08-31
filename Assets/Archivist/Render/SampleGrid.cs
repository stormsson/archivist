using System.Threading.Tasks;
using Archivist.Generation.Field;
using Archivist.Generation.Geometry;

namespace Archivist.Render
{
    /// <summary>
    /// The field's corner samples for one contour lattice, held so several plates can share
    /// them — and filled on every core, because filling it is the whole cost of a plate.
    ///
    /// <para><b>Why this exists.</b> 96–97% of rendering a plate is
    /// <see cref="IHeightField.Height01"/>: an fBm evaluation per corner, ~0.6 M corners on a
    /// quarter at 1:10000. Q1.2 gives every office <b>the same four rects</b> — that is what
    /// puts the board's layers in register — so three offices ask the field for the same corners
    /// three times over. This answers the second and third from the first.</para>
    ///
    /// <para><b>An <see cref="IHeightField"/> decorator, so nothing in <c>Contours</c> changes.</b>
    /// On-lattice queries come from the raster; anything else — the cell-centre sample a saddle
    /// resolves by (§6.1), which falls between corners — falls through to the real field. A miss
    /// is <i>slower, never wrong</i>, which is the failure mode worth having.</para>
    ///
    /// <para><b>The lattice comes from <c>Contours.Lattice</c>, not from arithmetic repeated
    /// here.</b> A raster laid on corners half a cell from the ones the extraction reads would
    /// miss every time and still produce correct output — a silent 1.0x. Asking is what keeps
    /// that impossible.</para>
    ///
    /// <para><b><c>float</c> is exact here, not an approximation.</b> §4.4 quantises
    /// <c>Height01</c> at 2⁻¹⁶, so every value is <c>k/65536</c> with <c>k ≤ 65536</c>: 17
    /// mantissa bits, and a <c>float</c> has 24. The samples come back bit-identical, so a plate
    /// drawn through this and a plate drawn without it are the same plate. Halving the memory
    /// matters because a quarter's grid is ~2.6 MB and a board holds several.</para>
    ///
    /// <para><b>The parallel fill cannot affect the output.</b> Each row writes a disjoint slice
    /// of a preallocated array, and <c>Height01</c> is a pure function; the marching that reads
    /// the raster stays serial and row-major. Nothing here depends on scheduling.
    /// <c>FillRenderer</c> already parallelises the same way.</para>
    /// </summary>
    public sealed class SampleGrid : IHeightField
    {
        readonly IHeightField inner;
        readonly double cellSize;
        readonly long ix0, iy0;
        readonly int nx, ny;
        readonly float[] samples;

        SampleGrid(IHeightField inner, double cellSize, long ix0, long iy0, int nx, int ny)
        {
            this.inner = inner;
            this.cellSize = cellSize;
            this.ix0 = ix0; this.iy0 = iy0;
            this.nx = nx; this.ny = ny;
            samples = new float[(nx + 1) * (ny + 1)];
        }

        /// <summary>
        /// Samples <paramref name="field"/> over the lattice <c>Contours</c> will use for
        /// <paramref name="area"/>, or returns the field unchanged when the area carries no
        /// cells. A caller may always use what comes back.
        /// </summary>
        public static IHeightField Over(IHeightField field, Rect2 area, double cellSize)
        {
            if (field == null) return null;

            long ix0, iy0;
            int nx, ny;
            if (!Contours.Lattice(area, cellSize, out ix0, out iy0, out nx, out ny)) return field;

            var grid = new SampleGrid(field, cellSize, ix0, iy0, nx, ny);
            grid.Fill();
            return grid;
        }

        /// <summary>True when this grid covers the lattice for <paramref name="area"/> at
        /// <paramref name="cell"/> — how a cache decides whether it may be reused.</summary>
        public bool Covers(Rect2 area, double cell)
        {
            long qx0, qy0;
            int qnx, qny;
            if (!Contours.Lattice(area, cell, out qx0, out qy0, out qnx, out qny)) return false;

            return cell == cellSize && qx0 == ix0 && qy0 == iy0 && qnx == nx && qny == ny;
        }

        void Fill()
        {
            int width = nx + 1;

            // Hoisted exactly as Contours does it — (latticeIndex * cellSize), never an
            // accumulated sum — so a corner has one abscissa whoever computes it.
            var xs = new double[width];
            for (int i = 0; i < width; i++) xs[i] = (ix0 + i) * cellSize;

            IHeightField field = inner;
            float[] into = samples;
            double cell = cellSize;
            long y0 = iy0;

            Parallel.For(0, ny + 1, j =>
            {
                double y = (y0 + j) * cell;
                int row = j * width;
                for (int i = 0; i < width; i++) into[row + i] = (float)field.Height01(xs[i], y);
            });
        }

        public IslandParams Params { get { return inner.Params; } }

        /// <summary>
        /// The sample, from the raster when the point is a corner of this lattice and from the
        /// field otherwise.
        ///
        /// <para>The index is recovered by rounding rather than by division-and-floor, and the
        /// hit is confirmed by rebuilding the corner's own coordinate and comparing: a point
        /// that is merely <i>near</i> a corner must miss, because answering it with the corner's
        /// value would be a different number from the one the field would give.</para>
        /// </summary>
        public double Height01(double x, double y)
        {
            long i = (long)System.Math.Floor(x / cellSize + 0.5) - ix0;
            long j = (long)System.Math.Floor(y / cellSize + 0.5) - iy0;

            if (i >= 0 && i <= nx && j >= 0 && j <= ny
                && (ix0 + i) * cellSize == x && (iy0 + j) * cellSize == y)
            {
                return samples[j * (nx + 1) + i];
            }

            return inner.Height01(x, y);
        }

        public double Elevation(double x, double y) { return inner.Elevation(x, y); }
        public V2 Gradient(double x, double y) { return inner.Gradient(x, y); }
        public double ElevationFrom(double h01) { return inner.ElevationFrom(h01); }

        /// <summary>
        /// Both values from one evaluation. The raster holds <c>Height01</c> only, and
        /// <c>ElevationFrom</c> derives the other from it without touching the field — so a
        /// corner hit here still costs one lookup, not one lookup and a field evaluation.
        /// </summary>
        public double Sample(double x, double y, out double elevation)
        {
            double h01 = Height01(x, y);
            elevation = inner.ElevationFrom(h01);
            return h01;
        }
    }

    /// <summary>
    /// A few <see cref="SampleGrid"/>s, kept between renders that share a lattice.
    ///
    /// <para><b>What shares a lattice</b> is the three offices' plates of one quarter (Q1.2).
    /// <b>What order they arrive in</b> is office-major — a crate deals one binder per office,
    /// and a board lays out what is on it — so the next plate wanting NW's corners is four
    /// plates after the last one, not the next one. A single-entry cache measured 1982 to 593 ms
    /// on a thirteen-plate island; it was missing every time and the win was the parallel fill
    /// alone.</para>
    ///
    /// <para><b>Depth is therefore the number of lattices an island has</b> — four quarters and
    /// a chart — and not one more, because a sixth entry can only hold another island's, and
    /// nothing renders two islands together. At ~2.1 MB a quarter that is about 9 MB held for
    /// the length of a batch, against the 11.4 MB a single plate's <c>ImageBuffer</c>
    /// occupies.</para>
    ///
    /// <para><b>Passed in, never global.</b> A static cache would need a lock, would outlive the
    /// batch that wanted it, and would make two renders of one sheet differ in cost for reasons
    /// nothing could see. This is created by whoever renders a batch and dies with it, so it is
    /// single-threaded by construction. A null cache is legal and means "do not share" — it
    /// costs speed and changes nothing else.</para>
    /// </summary>
    public sealed class SampleGridCache
    {
        /// <summary>Four quarters and a chart (Q1.1, Q2.3). One island's worth, deliberately.
        /// </summary>
        public const int Depth = 5;

        readonly SampleGrid[] held = new SampleGrid[Depth];
        int next;

        /// <summary>The grid for this area and cell, reusing a held one when it covers them.
        /// </summary>
        public IHeightField For(IHeightField field, Rect2 area, double cellSize)
        {
            for (int i = 0; i < held.Length; i++)
            {
                if (held[i] != null && held[i].Covers(area, cellSize)) return held[i];
            }

            IHeightField made = SampleGrid.Over(field, area, cellSize);

            // Round-robin rather than least-recently-used: a batch touches every lattice about
            // equally often, so there is no recency worth tracking, and eviction order cannot
            // change what is drawn.
            held[next] = made as SampleGrid;
            next = (next + 1) % held.Length;

            return made;
        }
    }
}
