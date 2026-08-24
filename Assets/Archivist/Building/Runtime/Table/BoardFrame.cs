using Archivist.Generation.Geometry;
using Archivist.Generation.Sheets;

namespace Archivist.Building.Table
{
    /// <summary>
    /// The rigid transform between island ground space and where the player has actually put
    /// some paper (G3.1): a rotation <c>phi</c> in degrees and an offset <c>t</c> in ground
    /// metres. One frame answers, for every sheet at once, "if this arrangement is right, where
    /// does that sheet go?"
    ///
    /// <code>
    ///     pose(M) under (phi, t)  =  ( R(phi) * M.CentreGround + t ,  M.RotationDeg + phi )
    /// </code>
    ///
    /// <para><b>Why a frame at all, rather than comparing two sheets directly.</b> The table as
    /// built judged each sheet against its <i>absolute</i> true pose (C6.1), a target that is
    /// 2.6% of the board's width with nothing on screen indicating where it is — and R1.11
    /// guarantees there never will be, because the island is never shown. The fit test becomes
    /// relative (G1.1). The thing a dragged sheet is judged against is then whatever it is
    /// joining, and that is a loose sheet on some occasions and a nine-sheet assembly on
    /// others. Both present a frame — a loose sheet derives one through <see cref="ForSheet"/>,
    /// a group has one stored (G4.2) — so there is exactly <b>one</b> fit path, not a sheet
    /// case and a group case that can drift apart.</para>
    ///
    /// <para><b>Two frames cannot be compared to each other</b> (G3.6). A rotation difference
    /// displaces a far member more than a near one, so "how far apart are these two
    /// arrangements" has no single answer. Every test is therefore grounded in one sheet, which
    /// is why this struct offers <see cref="PositionOf"/> and <see cref="RotationOf"/> — a
    /// question about a sheet — and nothing that takes another <see cref="BoardFrame"/>.</para>
    ///
    /// <para><b>The rotation sense, and how a sign error hides.</b> <c>R(phi)</c> is
    /// <see cref="V2.RotateDeg"/>, which takes +X toward +Y — the same sense
    /// <see cref="Sheet.FrameRect"/> uses, applied in the opposite direction: <c>FrameRect</c>
    /// rotates a ground centre by <c>-RotationDeg</c> to reach frame space, and this rotates a
    /// ground centre by <c>+phi</c> to reach the board. Do not write a second rotation here;
    /// use that one. A flipped sign is the mistake worth naming, because of where it does
    /// <i>not</i> show: at <see cref="Identity"/> it is invisible (phi is 0, and every existing
    /// behaviour goes through the identity path), and it is invisible again on the very sheet a
    /// frame was built from, since <see cref="ForSheet"/> would then cancel its own error. It
    /// shows only on the <b>second</b> sheet laid at a non-zero phi, which lands mirrored about
    /// the first. G-A2 — if A fits B's frame then B fits A's — is the check that catches it,
    /// and it is stated in the spec for that reason.</para>
    ///
    /// <para><b>A frame is a player fact, so a transcendental is allowed here.</b> §4.4 forbids
    /// letting <c>sin</c>/<c>cos</c> reach a branch <i>in the generator</i>, where a last-ulp
    /// difference between libm implementations can flip a cull and change an island's sheet
    /// count. Nothing here feeds generation: phi comes from where the player pointed, no value
    /// on this struct is drawn from a stream (§10), and a board pose is never an input to a
    /// seed. The worst an ulp can do is decide a release that lands exactly on the tolerance
    /// circle, which is a coin the player cannot aim at and no acceptance check depends on.
    /// Quantising phi to protect it would band every join instead.</para>
    ///
    /// <para>No UnityEngine, no tuning constants: like <see cref="SheetFit"/> and
    /// <see cref="BoardSpace"/> this runs headless, which is what lets G-A1 through G-A6 live
    /// in the acceptance harness rather than in the editor.</para>
    /// </summary>
    public readonly struct BoardFrame
    {
        /// <summary>phi — degrees the whole arrangement is turned from island ground.</summary>
        public readonly double RotationDeg;

        /// <summary>t — ground metres the arrangement is displaced by, applied after the turn.</summary>
        public readonly V2 Offset;

        public BoardFrame(double rotationDeg, V2 offset)
        {
            RotationDeg = rotationDeg;
            Offset = offset;
        }

        /// <summary>
        /// The frame under which every sheet sits at its true island pose — the absolute test
        /// the table has always run (C6.1), expressed in the new vocabulary.
        ///
        /// <para>It is kept, and kept meaningful, for two reasons. It is the migration path:
        /// §3.4 requires the identity path to reproduce today's <see cref="SheetFit.Fits"/>
        /// exactly, so the existing acceptance measurement (A5) runs unchanged against it and
        /// G-A1 is a real check rather than a restatement. And absolute correctness is only
        /// deferred, not abandoned (§13) — the day a completed survey reveals a piece of the
        /// island, this is the frame that question is asked in.</para>
        /// </summary>
        public static readonly BoardFrame Identity = new BoardFrame(0.0, V2.Zero);

        /// <summary>
        /// Exact comparison against zero, deliberately, with no epsilon. This is a bookkeeping
        /// question — "is this the absolute test, or has the player made an arrangement?" — not
        /// a geometric near-test, and the two callers who care (the identity fast path and any
        /// future absolute-correctness check) both need the strict answer. An epsilon here
        /// would quietly report a real frame as the absolute one and make a wrong pose read as
        /// a right one.
        /// </summary>
        public bool IsIdentity
        {
            get { return RotationDeg == 0.0 && Offset.X == 0.0 && Offset.Y == 0.0; }
        }

        /// <summary>
        /// The frame a single laid sheet presents (G3.1): <c>phi = r - theta</c>,
        /// <c>t = p - R(phi) * c</c>. Inverting <see cref="PositionOf"/> and
        /// <see cref="RotationOf"/>, so <c>ForSheet(s, p, r)</c> always puts <c>s</c> back at
        /// exactly <c>(p, r)</c> — a sheet trivially fits its own frame, which is what makes
        /// "join B to A" mean "agree with where A says you are".
        ///
        /// <para><paramref name="rotationDeg"/> and <paramref name="groundPos"/> are the pose
        /// the player has the sheet in, in <b>ground</b> space — convert from the board with
        /// <see cref="BoardSpace.ToGround"/> first.</para>
        ///
        /// <para><c>truth.RotationDeg</c>, not <c>truth.Survey.RotationDeg</c> — see
        /// <see cref="SheetFit"/>'s class comment (D-H2). Here the mistake would be quieter
        /// still: it would build a frame that is wrong by the sheet's own coast-walk angle, so
        /// a correctly assembled pair of Hydrographic strips would refuse to fuse while every
        /// lattice office worked.</para>
        /// </summary>
        public static BoardFrame ForSheet(Sheet truth, V2 groundPos, double rotationDeg)
        {
            double phi = rotationDeg - truth.RotationDeg;
            return new BoardFrame(phi, groundPos - truth.CentreGround.RotateDeg(phi));
        }

        /// <summary>
        /// Where this frame puts <paramref name="truth"/>, in ground metres (G3.1).
        ///
        /// <para>At <see cref="Identity"/> this returns <c>truth.CentreGround</c> bit-for-bit,
        /// not merely to within rounding: <c>0 * PI / 180</c> is exactly 0, IEEE-754 gives
        /// <c>cos 0 == 1.0</c> and <c>sin 0 == 0.0</c> exactly, so the rotation is
        /// <c>(X*1 - Y*0, X*0 + Y*1)</c>, and adding <see cref="V2.Zero"/> changes nothing.
        /// That exactness is the whole of §3.4 and is what makes G-A1 checkable by equality
        /// rather than by tolerance.</para>
        /// </summary>
        public V2 PositionOf(Sheet truth)
        {
            return truth.CentreGround.RotateDeg(RotationDeg) + Offset;
        }

        /// <summary>
        /// The angle this frame puts <paramref name="truth"/> at (G3.1). Not folded into a
        /// range: the caller compares it through <see cref="SheetFit.AngleDelta"/>, which folds
        /// modulo 360 (C6.3), and folding twice is one more place to fold by 180 by mistake.
        ///
        /// <para>§3.5 is worth keeping in mind when reading this line. For the lattice offices
        /// every sheet of a survey shares one rotation (R2.4), so <c>theta_A + phi</c> and
        /// <c>theta_B + phi</c> are equal and joining means <i>be parallel to the sheet next to
        /// you</i> — the difficulty of §1.1 mostly removed by the fit change alone. For the
        /// Hydrographic coast walk (D-H2) the truth delta is real and the player must find a
        /// specific relative angle; that is the office this method exists for.</para>
        /// </summary>
        public double RotationOf(Sheet truth)
        {
            return truth.RotationDeg + RotationDeg;
        }
    }
}
