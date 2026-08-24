using System;
using Archivist.Generation.Geometry;
using Archivist.Generation.Sheets;

namespace Archivist.Building.Table
{
    /// <summary>
    /// Does a sheet, as the player has laid it, agree with where a <see cref="BoardFrame"/>
    /// says it belongs (G3.2, G3.3)?
    ///
    /// <para><b>The test is relative now, and that is the only change of substance.</b> It used
    /// to ask whether a sheet was at its absolute true ground pose (C6.1, C6.2), which
    /// groups_spec §1.1 measures as a target 2.6% of the board's width wide with nothing on
    /// screen indicating where it is — and R1.11 guarantees there never will be, because the
    /// island is never shown. Two sheets held in perfect pose <i>relative to each other</i>
    /// scored nothing, because the test never looked at the other sheet: the one piece of
    /// feedback the player could actually generate was the one the rule ignored. So the sheet
    /// is now judged against a frame carried by whatever it is joining (G1.1). G3.2 supersedes
    /// C6.1's measurement point and G3.3 supersedes C6.2's; the tolerances, their formulas and
    /// their defaults are untouched. At <see cref="BoardFrame.Identity"/> this is bit-for-bit
    /// the function <c>spec.md</c> §6.1 published — see <see cref="Fits(Sheet,V2,double,double,double)"/>.</para>
    ///
    /// <para><b>No feature matching, and none should be written.</b> Every <see cref="Sheet"/>
    /// already carries <c>CentreGround</c> and <c>RotationDeg</c>, so both the absolute pose and
    /// the frame-relative one are exactly known — the whole snap is still a subtraction, a
    /// square root and a modulus over numbers the generator handed us. Going relative does not
    /// change that and must not be read as an invitation: matching coastlines between
    /// neighbouring sheets would be a large, fragile, non-deterministic way of recovering a
    /// number we already have, and it would disagree with the truth at exactly the moment the
    /// player has done everything right. §13 restates the prohibition for this rewrite.</para>
    ///
    /// <para><b>Tolerance is a fraction of the sheet, not a distance in metres</b> (C6.1, kept
    /// unchanged by G3.2). The tolerance is
    /// <c>min(SheetGroundWidth, SheetGroundHeight) * positionTolerance</c> — the <i>shorter</i>
    /// ground dimension, because that is the direction in which a near-miss first stops looking
    /// like the same sheet. An absolute metre tolerance would be wrong, and wrong in a way the
    /// player feels rather than sees: an Antiquarian detail sheet covers ~275 m of ground and a
    /// Land Survey A1 at 1:2500 covers 1285 m, so a fixed 100 m would be more than a third of
    /// the detail sheet — it would seat itself off the pointer — while being 8% of the A1,
    /// which would feel like the A1 refuses to seat. As a fraction (0.12 by default) both give
    /// the same gesture: 33 m for the detail sheet, 154 m for the A1. Same feel, different
    /// ground. (This paragraph used to quote 1485 m and 178 m for the A1. Those were the
    /// paper-derived figures; <see cref="BoardSpace"/> records the correction to the map area's
    /// 1285 × 1902 m, and groups_spec §1.1 works from the corrected number.)</para>
    ///
    /// <para><b>Rotation is compared modulo 360, never modulo 180</b> (C6.3, and §3.3 keeps it
    /// intact under the relative test). Halving the modulus is the tempting bug: for a
    /// rectangle, 180° looks like the same rectangle, so a mod-180 test "helpfully" accepts an
    /// upside-down sheet. The Antiquarian's <b>square</b> detail sheet is the case this
    /// protects — nothing about its outline distinguishes any of the four right-angle poses —
    /// and POC-03 P2.6 is explicit that "the sheet has no north indication and resolving
    /// orientation is part of the placement". Accepting a flip would delete that part of the
    /// activity, and would do it silently, since the sheet would then ease to the fitted pose
    /// and visibly spin 180° on release.</para>
    ///
    /// <para><b>Compare <c>truth.RotationDeg</c>, not <c>truth.Survey.RotationDeg</c>.</b> The
    /// lattice offices keep one rotation per survey (R2.4), so the two agree and the mistake
    /// costs nothing — until the Hydrographic coast walk (D-H2), where the survey's rotation is
    /// nominal and each sheet is oriented to its own stretch of shore. Taking the survey's
    /// value there compares the player's angle against a number no sheet was ever cut at, so
    /// Hydrographic sheets alone stop seating while every other office works. That is the worst
    /// kind of wrong: partial, plausible, and only reproducible on one office's paper.
    /// <c>RenderRequest.ForSheet</c> carries the same warning for the same reason, and
    /// <see cref="BoardFrame.ForSheet"/> now carries it too — the relative test reads the
    /// per-sheet rotation twice, once to build the frame and once to judge against it, so there
    /// are two places to make the same mistake and §3.5 makes Hydrographic the one office whose
    /// difficulty depends on getting it right.</para>
    ///
    /// <para>Pure, engine-free and parameterless of tuning: the caller passes the tolerances in
    /// from <c>TableOptions</c> (§10), so this file holds no magic numbers and runs headless —
    /// which is what lets G-A1 through G-A6 live in the acceptance harness.</para>
    /// </summary>
    public static class SheetFit
    {
        /// <summary>
        /// G3.2 and G3.3 together: is <paramref name="truth"/>, as the player is holding it,
        /// where <paramref name="frame"/> says it goes?
        ///
        /// <para><paramref name="groundPos"/> and <paramref name="rotationDeg"/> are the pose
        /// the player currently has the sheet in, in <b>ground</b> space — convert from the
        /// board with <see cref="BoardSpace.ToGround"/> first.</para>
        ///
        /// <para>Evaluated every frame while dragging (C6.4) against every fusable candidate on
        /// release (G5.1), so it stays a rotation, a subtraction, a square root and a
        /// modulus — no allocation, no island access.</para>
        ///
        /// <para>A group is judged through this same method, on one member: the one meeting the
        /// join (G3.6). <see cref="PositionReach"/> scales with the <i>sheet</i>, so grounding
        /// the test at the far end of a nine-sheet assembly would apply a tolerance to a member
        /// nowhere near the seam. There is one definition of "fits", not two.</para>
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
        /// <paramref name="frame"/> puts this sheet at (the left-hand side of G3.2).
        ///
        /// <para>Public because G5.1 has to choose between candidates: several fusable slabs can
        /// all fit at once, and the one with the <b>smallest position error</b> wins. A caller
        /// that re-derived this by calling <see cref="Fits"/> and then measuring differently
        /// would rank candidates by a quantity the acceptance test never checked.</para>
        /// </summary>
        public static double PositionError(Sheet truth, BoardFrame frame, V2 groundPos)
        {
            return (groundPos - frame.PositionOf(truth)).Length;
        }

        /// <summary>
        /// How far, in ground metres, a sheet may be dropped from the pose the frame gives it
        /// and still fit: the sheet's <b>shorter</b> ground dimension times
        /// <paramref name="positionTolerance"/> (C6.1, unchanged by G3.2 — only the point the
        /// distance is measured from moved).
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
