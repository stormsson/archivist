using System.Collections.Generic;
using Archivist.Generation.Geometry;

namespace Archivist.Generation.Sheets
{
    /// <summary>R2.2: one island, one office, one year, one scale, one rotation.</summary>
    public readonly struct SurveySpec
    {
        public readonly ulong IslandSeed;
        public readonly Office Office;
        public readonly int Year;                 // label only; no eras in v1
        public readonly MapScale Scale;
        public readonly double RotationDeg;       // fixed per survey (R2.4), quantised to 0.1
        public readonly SheetFormat Format;
        public readonly double OverlapFraction;
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

    /// <summary>One numbered rectangle of one survey. Numbers assigned after culling (§10.4).</summary>
    public readonly struct Sheet
    {
        public readonly SurveySpec Survey;
        public readonly int Number;               // 1..N, contiguous
        public readonly V2 CentreGround;
        public readonly double RotationDeg;       // == Survey.RotationDeg

        public Sheet(SurveySpec survey, int number, V2 centreGround)
        {
            Survey = survey; Number = number; CentreGround = centreGround;
            RotationDeg = survey.RotationDeg;
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
        }

        /// <summary>The sheet rect in FRAME space (axis-aligned there, §10.2 step 2).</summary>
        public Rect2 FrameRect
        {
            get
            {
                V2 c = CentreGround.RotateDeg(-RotationDeg);
                return Rect2.FromCentreSize(c, Survey.SheetGroundWidth, Survey.SheetGroundHeight);
            }
        }

        /// <summary>The four ground-space corners, in order. A rotated rect (§10.2 step 2).</summary>
        public V2[] GroundCorners()
        {
            double hw = Survey.SheetGroundWidth * 0.5, hh = Survey.SheetGroundHeight * 0.5;
            var local = new[] { new V2(-hw, -hh), new V2(hw, -hh), new V2(hw, hh), new V2(-hw, hh) };
            var outp = new V2[4];
            for (int i = 0; i < 4; i++) outp[i] = CentreGround + local[i].RotateDeg(RotationDeg);
            return outp;
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
