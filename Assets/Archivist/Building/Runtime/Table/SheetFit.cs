using System;
using Archivist.Generation.Geometry;
using Archivist.Generation.Sheets;

namespace Archivist.Building.Table
{
    /// <summary>
    /// Does a sheet, as the player has laid it, agree with where it truly belongs (spec §6.1)?
    ///
    /// <para><b>No feature matching, and none should be written.</b> Every <see cref="Sheet"/>
    /// already carries <c>CentreGround</c> and <c>RotationDeg</c>, so the correct board pose is
    /// exactly known — the whole snap is a distance and an angle. Matching coastlines between
    /// neighbouring sheets would be a large, fragile, non-deterministic way of recovering a
    /// number the generator handed us for free, and it would disagree with the truth at exactly
    /// the moment the player has done everything right.</para>
    ///
    /// <para><b>Tolerance is a fraction of the sheet, not a distance in metres</b> (C6.1). The
    /// tolerance is <c>min(SheetGroundWidth, SheetGroundHeight) * positionTolerance</c> — the
    /// <i>shorter</i> ground dimension, because that is the direction in which a near-miss
    /// first stops looking like the same sheet. An absolute metre tolerance would be wrong, and
    /// wrong in a way the player feels rather than sees: an Antiquarian detail sheet covers
    /// ~275 m of ground and a Land Survey A1 at 1:2500 covers 1485 m, so a fixed 100 m would be
    /// more than a third of the detail sheet — it would seat itself off the pointer — while
    /// being 7% of the A1, which would feel like the A1 refuses to seat. As a fraction (0.12 by
    /// default) both give the same gesture: 33 m for the detail sheet, 178 m for the A1. Same
    /// feel, different ground.</para>
    ///
    /// <para><b>Rotation is compared modulo 360, never modulo 180</b> (C6.3). Halving the
    /// modulus is the tempting bug: for a rectangle, 180° looks like the same rectangle, so a
    /// mod-180 test "helpfully" accepts an upside-down sheet. The Antiquarian's <b>square</b>
    /// detail sheet is the case this protects — nothing about its outline distinguishes any of
    /// the four right-angle poses — and POC-03 P2.6 is explicit that "the sheet has no north
    /// indication and resolving orientation is part of the placement". Accepting a flip would
    /// delete that part of the activity, and would do it silently, since the seated slab would
    /// then ease to the true pose and visibly spin 180° on release.</para>
    ///
    /// <para><b>Compare <c>truth.RotationDeg</c>, not <c>truth.Survey.RotationDeg</c>.</b> The
    /// lattice offices keep one rotation per survey (R2.4), so the two agree and the mistake
    /// costs nothing — until the Hydrographic coast walk (D-H2), where the survey's rotation is
    /// nominal and each sheet is oriented to its own stretch of shore. Taking the survey's
    /// value there compares the player's angle against a number no sheet was ever cut at, so
    /// Hydrographic sheets alone stop seating while every other office works. That is the worst
    /// kind of wrong: partial, plausible, and only reproducible on one office's paper.
    /// <c>RenderRequest.ForSheet</c> carries the same warning for the same reason.</para>
    ///
    /// <para>Pure, engine-free and parameterless of tuning: the caller passes the tolerances in
    /// from <c>TableOptions</c> (§10), so this file holds no magic numbers and runs headless.</para>
    /// </summary>
    public static class SheetFit
    {
        /// <summary>
        /// C6.1. <paramref name="groundPos"/> and <paramref name="rotationDeg"/> are the pose
        /// the player currently has the sheet in, in <b>ground</b> space — convert from the
        /// board with <see cref="BoardSpace.ToGround"/> first.
        ///
        /// <para>Evaluated every frame while dragging (C6.4), so it stays a subtraction, a
        /// square root and a modulus — no allocation, no island access.</para>
        /// </summary>
        public static bool Fits(Sheet truth, V2 groundPos, double rotationDeg,
                                double positionTolerance, double rotationToleranceDeg)
        {
            double reach = PositionReach(truth, positionTolerance);
            double dropped = (groundPos - truth.CentreGround).Length;
            if (dropped > reach) return false;

            // truth.RotationDeg, NOT truth.Survey.RotationDeg — see the class comment (D-H2).
            double turned = AngleDelta(rotationDeg, truth.RotationDeg);
            return Math.Abs(turned) <= rotationToleranceDeg;
        }

        /// <summary>
        /// How far, in ground metres, a sheet may be dropped from its true centre and still
        /// seat: the sheet's <b>shorter</b> ground dimension times
        /// <paramref name="positionTolerance"/> (C6.1).
        ///
        /// <para>Public because the glow of C6.4 and any acceptance measurement (A5) must use
        /// the same radius the test uses. A second copy of this expression is a second place
        /// for the fraction to be applied to the longer side by accident.</para>
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
        /// <para>The fold is two comparisons on the truncated remainder rather than a
        /// <c>Math.Floor</c> round trip, because the result feeds a branch and §4.4 forbids
        /// trusting a transcendental's or a division's last ulp at a threshold. C#'s <c>%</c>
        /// on doubles takes the sign of the dividend, so the remainder is in (-360, 360) and
        /// one correction each way is enough.</para>
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
