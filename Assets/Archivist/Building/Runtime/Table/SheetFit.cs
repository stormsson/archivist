using System;
using Archivist.Generation.Geometry;
using Archivist.Generation.Sheets;

namespace Archivist.Building.Table
{
    /// <summary>
    /// Does a sheet, as the player has laid it, agree with where a <see cref="BoardFrame"/>
    /// says it belongs (G3.2, G3.3)? At <see cref="BoardFrame.Identity"/> this is bit-for-bit
    /// the absolute test <c>spec.md</c> §6.1 published.
    ///
    /// <para><b>No feature matching, and none should be written.</b> Every <see cref="Sheet"/>
    /// carries <c>CentreGround</c> and <c>RotationDeg</c>, so the fit is a subtraction, a square
    /// root and a modulus over numbers the generator already handed us. Matching coastlines
    /// between neighbours would be a large, fragile, non-deterministic way of recovering those
    /// numbers, and would disagree with the truth at exactly the moment the player has done
    /// everything right (§13).</para>
    ///
    /// <para><b>Tolerance is a fraction of the sheet's SHORTER ground dimension, not a distance
    /// in metres</b> (C6.1, kept by G3.2) — the short side is the direction in which a near-miss
    /// first stops looking like the same sheet. A fixed 100 m would be a third of an Antiquarian
    /// detail sheet (~275 m of ground), which would seat itself off the pointer, and 8% of a
    /// Land Survey A1 (1285 m), which would feel like a refusal to seat. At 0.12 both get the
    /// same gesture: 33 m and 154 m.</para>
    ///
    /// <para><b>Rotation is compared modulo 360, never modulo 180</b> (C6.3). Halving the
    /// modulus is the tempting bug: a mod-180 test accepts an upside-down sheet, and the
    /// Antiquarian's <b>square</b> detail sheet has no outline cue distinguishing any of its
    /// four right-angle poses. POC-03 P2.6 makes resolving orientation part of the placement.
    /// </para>
    ///
    /// <para><b>Compare <c>truth.RotationDeg</c>, not <c>truth.Survey.RotationDeg</c>.</b> The
    /// lattice offices keep one rotation per survey (R2.4) so the two agree — until the
    /// Hydrographic coast walk (D-H2), where each sheet is oriented to its own stretch of shore
    /// and the survey's value is nominal. Taking it there stops Hydrographic sheets alone from
    /// seating. The relative test reads the per-sheet rotation twice, once to build the frame
    /// and once to judge against it, so there are two places to make the mistake;
    /// <c>RenderRequest.ForSheet</c> and <see cref="BoardFrame.ForSheet"/> carry the same
    /// warning.</para>
    ///
    /// <para>Pure, engine-free, and free of tuning: the caller passes the tolerances in from
    /// <c>TableOptions</c> (§10), which is what lets G-A1..G-A6 run in the acceptance
    /// harness.</para>
    /// </summary>
    public static class SheetFit
    {
        /// <summary>
        /// G3.2 and G3.3 together: is <paramref name="truth"/>, as the player is holding it,
        /// where <paramref name="frame"/> says it goes? <paramref name="groundPos"/> and
        /// <paramref name="rotationDeg"/> are in <b>ground</b> space — convert from the board
        /// with <see cref="BoardSpace.ToGround"/> first.
        ///
        /// <para>Runs every frame while dragging (C6.4) and against every fusable candidate on
        /// release (G5.1), so it stays arithmetic — no allocation, no island access.</para>
        ///
        /// <para>A group is judged on one member: the one meeting the join (G3.6).
        /// <see cref="PositionReach"/> scales with the <i>sheet</i>, so grounding the test at
        /// the far end of a nine-sheet assembly would apply a tolerance to a member nowhere
        /// near the seam.</para>
        /// </summary>
        public static bool Fits(Sheet truth, BoardFrame frame, V2 groundPos, double rotationDeg,
                                double positionTolerance, double rotationToleranceDeg)
        {
            double reach = PositionReach(truth, positionTolerance);
            if (PositionError(truth, frame, groundPos) > reach) return false;

            // truth.RotationDeg via frame.RotationOf, NOT truth.Survey.RotationDeg — see the
            // class comment (D-H2).
            double turned = AngleDelta(rotationDeg, frame.RotationOf(truth));
            return Math.Abs(turned) <= rotationToleranceDeg;
        }

        /// <summary>
        /// How far, in ground metres, the player's position is from the one
        /// <paramref name="frame"/> puts this sheet at (the left-hand side of G3.2). Public
        /// because G5.1 ranks candidates by it — several slabs can fit at once and the
        /// smallest error wins, so no caller may measure that differently.
        /// </summary>
        public static double PositionError(Sheet truth, BoardFrame frame, V2 groundPos)
        {
            return (groundPos - frame.PositionOf(truth)).Length;
        }

        /// <summary>
        /// How far, in ground metres, a sheet may be dropped from the frame's pose and still
        /// fit: the <b>shorter</b> ground dimension times <paramref name="positionTolerance"/>
        /// (C6.1). Public so the C6.4 glow and the A5 measurement use the same radius the test
        /// does — a second copy is a second place to apply the fraction to the longer side.
        /// </summary>
        public static double PositionReach(Sheet truth, double positionTolerance)
        {
            return Math.Min(truth.Survey.SheetGroundWidth, truth.Survey.SheetGroundHeight)
                   * positionTolerance;
        }

        /// <summary>
        /// Signed difference <c>a - b</c> folded into <c>(-180, 180]</c>. Modulo <b>360</b>
        /// (C6.3): a sheet turned 180° is 180° away, not 0° away.
        ///
        /// <para>Two comparisons on the truncated remainder, not a <c>Math.Floor</c> round
        /// trip: the result feeds a branch and §4.4 forbids trusting a division's last ulp at a
        /// threshold. C#'s <c>%</c> on doubles takes the sign of the dividend, so the remainder
        /// is in (-360, 360) and one correction each way is enough.</para>
        ///
        /// <para>Also the short way round: G5.3 turns a fusing sheet through this delta so a
        /// sheet 5° out never spins 355° to join.</para>
        /// </summary>
        public static double AngleDelta(double a, double b)
        {
            double d = (a - b) % 360.0;
            if (d <= -180.0) d += 360.0;
            else if (d > 180.0) d -= 360.0;
            return d;
        }
    }
}
