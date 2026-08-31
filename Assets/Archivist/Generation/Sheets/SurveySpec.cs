using System.Collections.Generic;
using Archivist.Generation.Geometry;

namespace Archivist.Generation.Sheets
{
    /// <summary>
    /// One island, one office, one year, one scale (R2.2 as reshaped by Q3.1 — a survey, a
    /// binder and an office layer are now three names for one thing).
    ///
    /// <para><b><see cref="RotationDeg"/> is 0 for every quarter and for the chart</b> (Q1.2):
    /// all offices share one axis, which is what puts the board's layers in register. It is not
    /// a dead field — <see cref="DetailSheetCutter"/> still rolls a rotation per POI, and a
    /// detail sheet's own <c>Sheet.RotationDeg</c> governs.</para>
    ///
    /// <para><b><see cref="OverlapFraction"/> is 0 everywhere</b> (Q1.4): quarters tile exactly.
    /// The field is kept because <c>SvgExport</c> writes it into the debug JSON, where a
    /// disappearing key is a worse diff than a zero.</para>
    /// </summary>
    public readonly struct SurveySpec
    {
        public readonly ulong IslandSeed;
        public readonly Office Office;
        public readonly int Year;                 // label only; no eras in v1
        public readonly MapScale Scale;
        public readonly double RotationDeg;       // 0 for quarters and the chart (Q1.2)
        public readonly SheetFormat Format;
        public readonly double OverlapFraction;   // 0 everywhere (Q1.4)
        public readonly bool IsWholeIsland;

        public SurveySpec(ulong islandSeed, Office office, int year, MapScale scale,
                          double rotationDeg, SheetFormat format, double overlapFraction,
                          bool isWholeIsland = false)
        {
            IslandSeed = islandSeed; Office = office; Year = year; Scale = scale;
            RotationDeg = rotationDeg; Format = format; OverlapFraction = overlapFraction;
            IsWholeIsland = isWholeIsland;
        }

        public double SheetGroundWidth  { get { return Scale.GroundMetres(Format.MapWidthMm); } }
        public double SheetGroundHeight { get { return Scale.GroundMetres(Format.MapHeightMm); } }
    }

    /// <summary>
    /// One numbered rectangle of one survey.
    ///
    /// <para>For a quarter plate <see cref="Number"/> <b>is the quarter</b> — 1 NW, 2 NE, 3 SW,
    /// 4 SE, in <see cref="QuarterCutter.QuarterNames"/> order (Q1.1). It is a plate's identity
    /// through <c>SheetId</c>, so renumbering the quarters renames every plate in every binder
    /// in every save.</para>
    /// </summary>
    public readonly struct Sheet
    {
        public readonly SurveySpec Survey;
        public readonly int Number;               // 1..N; 1..4 = NW NE SW SE for a quarter
        public readonly V2 CentreGround;
        public readonly double RotationDeg;       // == Survey.RotationDeg

        /// <summary>
        /// The ground this sheet is <b>of</b>, in metres — which is not the same as the ground
        /// its paper could hold.
        ///
        /// <para><b>A quarter plate is its quarter</b> (Q1.1). It used to take its extent from
        /// the paper, and on a 6.9 km island at 1:10000 that meant a 3456 x 3136 m quarter drawn
        /// on 7610 x 5140 m of ground: each plate covered 90% of the island, neighbours
        /// overlapped by 55%, and the four "quarters" of an office were four near-identical
        /// drawings stacked on each other. Q1.4's "quarters tile exactly" was true of the rects
        /// and of nothing anyone could see.</para>
        ///
        /// <para><b>Independent by construction.</b> Because a plate's extent is its own quarter
        /// and the quarters tile, four plates rendered separately meet exactly — so a binder
        /// holding two of an office's four is drawn the same as one holding all four, and a
        /// plate never has to know what else was rendered beside it.</para>
        ///
        /// <para>The chart and detail sheets keep the paper-derived extent: neither is a quarter,
        /// and each is a sheet of whatever its paper reaches at its scale.</para>
        /// </summary>
        public readonly double GroundWidth;
        public readonly double GroundHeight;

        /// <summary>
        /// POC-03 spec §2.4 — false for every survey sheet, true for a detail sheet.
        ///
        /// <para>Detail sheets file under the same given order as everything else (P2.7) but
        /// form their own numbered run <c>1..M</c>, independent of any survey run, so a gap in
        /// each stays unambiguous (R2.10b). With POIs given their own office (the project
        /// owner's deviation from spec §3) that run is a whole survey rather than a sub-series,
        /// and A4's existing per-survey contiguity check covers it unchanged.</para>
        /// </summary>
        public readonly bool IsDetail;

        public Sheet(SurveySpec survey, int number, V2 centreGround)
        {
            Survey = survey; Number = number; CentreGround = centreGround;
            RotationDeg = survey.RotationDeg;
            GroundWidth = survey.SheetGroundWidth;
            GroundHeight = survey.SheetGroundHeight;
            IsDetail = false;
        }

        /// <summary>A sheet of an explicit patch of ground — a quarter plate, whose extent is
        /// its quarter and not its paper.</summary>
        public Sheet(SurveySpec survey, int number, V2 centreGround,
                     double groundWidth, double groundHeight)
        {
            Survey = survey; Number = number; CentreGround = centreGround;
            RotationDeg = survey.RotationDeg;
            GroundWidth = groundWidth;
            GroundHeight = groundHeight;
            IsDetail = false;
        }

        /// <summary>
        /// Explicit per-sheet rotation (D-H2). The lattice offices keep one rotation per
        /// survey (R2.4); the Hydrographic coast-walk orients each sheet to its own stretch
        /// of shore, so its Survey.RotationDeg is nominal and this value governs.
        /// </summary>
        public Sheet(SurveySpec survey, int number, V2 centreGround, double rotationDeg)
        {
            Survey = survey; Number = number; CentreGround = centreGround;
            RotationDeg = rotationDeg;
            GroundWidth = survey.SheetGroundWidth;
            GroundHeight = survey.SheetGroundHeight;
            IsDetail = false;
        }

        /// <summary>
        /// POC-03 spec §2.2/§2.4 — a detail sheet: per-sheet rotation, its own numbering run,
        /// and <see cref="IsDetail"/> set. Only <see cref="DetailSheetCutter"/> calls this.
        /// </summary>
        public Sheet(SurveySpec survey, int number, V2 centreGround, double rotationDeg, bool isDetail)
        {
            Survey = survey; Number = number; CentreGround = centreGround;
            RotationDeg = rotationDeg;
            GroundWidth = survey.SheetGroundWidth;
            GroundHeight = survey.SheetGroundHeight;
            IsDetail = isDetail;
        }

        /// <summary>The sheet rect in FRAME space (axis-aligned there, §10.2 step 2).</summary>
        public Rect2 FrameRect
        {
            get
            {
                V2 c = CentreGround.RotateDeg(-RotationDeg);
                return Rect2.FromCentreSize(c, GroundWidth, GroundHeight);
            }
        }

        /// <summary>The four ground-space corners, in order. A rotated rect (§10.2 step 2).</summary>
        public V2[] GroundCorners()
        {
            double hw = GroundWidth * 0.5, hh = GroundHeight * 0.5;
            var local = new[] { new V2(-hw, -hh), new V2(hw, -hh), new V2(hw, hh), new V2(-hw, hh) };
            var outp = new V2[4];
            for (int i = 0; i < 4; i++) outp[i] = CentreGround + local[i].RotateDeg(RotationDeg);
            return outp;
        }

        /// <summary>
        /// Exact point-in-sheet test: is this ground point inside the sheet's rotated rect?
        ///
        /// <para>Point-in-rotated-rect is awkward in ground space, so the point is rotated by
        /// <c>-RotationDeg</c> into FRAME space — the same transform <see cref="FrameRect"/>
        /// applies to the centre — where the sheet is axis-aligned and the test is a plain
        /// <see cref="Rect2.Contains"/>.</para>
        ///
        /// <para>This exists because <see cref="GroundBounds"/> is the AABB *of* the rotated
        /// rect, so it strictly over-counts: for any rotation that is not a multiple of 90°
        /// the AABB includes four corner wedges that the sheet does not cover, and a point
        /// there passes an AABB test while lying off the sheet. Callers asking "does this
        /// sheet cover that point" must use this; <see cref="GroundBounds"/> is only for
        /// snapping a contouring area to the lattice, where over-covering is harmless.</para>
        /// </summary>
        public bool Contains(V2 groundPoint)
        {
            V2 f = groundPoint.RotateDeg(-RotationDeg);
            return FrameRect.Contains(f);
        }

        /// <summary>Ground-space AABB of the rotated rect — what gets snapped to the lattice for contouring.</summary>
        public Rect2 GroundBounds
        {
            get
            {
                var c = GroundCorners();
                Rect2 r = Rect2.Empty;
                for (int i = 0; i < 4; i++) r = r.Encapsulate(c[i]);
                return r;
            }
        }
    }

    /// <summary>A survey and the sheets it actually shipped.</summary>
    public sealed class Survey
    {
        public Survey(SurveySpec spec, IReadOnlyList<Sheet> sheets) { Spec = spec; Sheets = sheets; }
        public SurveySpec Spec { get; private set; }
        public IReadOnlyList<Sheet> Sheets { get; private set; }
        public int SheetCount { get { return Sheets.Count; } }
    }
}
