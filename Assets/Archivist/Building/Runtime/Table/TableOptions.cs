using UnityEngine;

namespace Archivist.Building.Table
{
    /// <summary>
    /// Tuning for the cartography table — every number spec §10 lists, and no others.
    ///
    /// <para><b>One asset, because the alternative has already been measured.</b> CLAUDE.md's
    /// standing rule is that tuning constants live in one place per assembly and are not
    /// scattered into behaviours, and the table is the feature most able to break that rule:
    /// its numbers are read from a board space, a draw-order stack, a texture budget, a snap
    /// test and an input handler, none of which owns them. Put the scale on the board builder
    /// and the tolerance on the snap component and there is no longer a place to answer "what
    /// is this table set to" — and two of those numbers are the *same* number seen twice
    /// (C5.5: the board slab and the cabinet thumbnail share one render, so they must share
    /// one pixel density or the cache silently renders twice).</para>
    ///
    /// <para><b>A ScriptableObject rather than consts, for the reason
    /// <see cref="Archivist.Building.Handling.HandlingOptions"/> gives.</b> These are feel
    /// values. They are settled by playing — dragging a sheet until the tolerance stops
    /// feeling either generous or fussy — not by reasoning about them and editing a literal.
    /// An asset can be edited *while in play mode and the edit is kept*, so a tuning session
    /// is one session; consts mean exit play mode, edit, recompile, re-enter, and lose the
    /// board you were looking at. The defaults here are starting points, not findings:
    /// findings go in <c>docs/UI/cartography_table/findings.md</c>.</para>
    ///
    /// <para><b>No randomness.</b> Nothing on this table draws from a stream (§10), so there
    /// is no <c>StreamNames</c> entry and no value here can move an island. Every number
    /// below affects only how the board looks and feels; board poses are player facts.</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Archivist/Table Options", fileName = "TableOptions")]
    public sealed class TableOptions : ScriptableObject
    {
        /// <summary>Used when no options asset is wired, so a missing asset costs the right
        /// feel rather than the ability to open a board at all.</summary>
        public const float DefaultBoardUnitsPerMetre = 0.01f;

        public const float DefaultBoardPadding = 0.08f;

        public const float DefaultSheetSeparation = 0.004f;

        public const float DefaultBoardPixelsPerPaperMm = 0.6f;

        public const float DefaultPositionTolerance = 0.12f;

        public const float DefaultRotationToleranceDeg = 8f;

        public const float DefaultSettleSeconds = 0.18f;

        /// <summary>Moved here from <c>HandlingOptions</c> by C8.16, and deliberately lower
        /// than the 120 the hands used: turning a sheet on a board is an aiming movement
        /// against an 8° tolerance, not a gesture.</summary>
        public const float DefaultSheetTurnDegreesPerSecond = 90f;

        [Header("Board space")]
        [Tooltip("Board world units per ground metre (§3.1). 0.01 makes one unit 100 m, so a " +
                 "12 km island is ~120 units — float precision and ortho camera sizes both " +
                 "stay comfortable, and a detail sheet is still bigger than one unit.")]
        [SerializeField, Min(0.0001f)] float boardUnitsPerMetre = DefaultBoardUnitsPerMetre;

        [Tooltip("Fraction of the longer land bound the board is grown by (§3.1). Coastal " +
                 "sheets are cut against the shore and hang off the land, so a board clipped " +
                 "to LandBounds would put a correctly placed sheet half off the mounting " +
                 "sheet. A fraction, not a distance, so a 3 km and a 12 km island match.")]
        [SerializeField, Min(0f)] float boardPadding = DefaultBoardPadding;

        [Tooltip("Board units between one sheet and the next in the draw-order stack (§3.3). " +
                 "Small enough to read as one flat map, large enough that overlapping slabs " +
                 "never z-fight.")]
        [SerializeField, Min(0f)] float sheetSeparation = DefaultSheetSeparation;

        [Header("Textures")]
        [Tooltip("Pixels per millimetre of paper for a table render. Serves the board slab " +
                 "AND the cabinet thumbnail from one cached texture per SheetId (C5.5): the " +
                 "thumbnail is ~60 px wide and a board sheet ~150 px, and there is no zoom, " +
                 "so nothing on this table ever needs more.")]
        [SerializeField, Min(0.05f)] float boardPixelsPerPaperMm = DefaultBoardPixelsPerPaperMm;

        [Header("Snap")]
        [Tooltip("Position tolerance as a fraction of the sheet's SHORTER ground dimension " +
                 "(C6.1), not a distance in metres. A detail sheet covering 275 m and an A1 " +
                 "covering 1485 m should not share a tolerance in metres; as a fraction, both " +
                 "feel the same to place.")]
        [SerializeField, Min(0f)] float positionTolerance = DefaultPositionTolerance;

        [Tooltip("Rotation tolerance in degrees (C6.2). Absolute, because rotation error does " +
                 "not scale with sheet size. Compared modulo 360, not 180 — a sheet placed " +
                 "upside down is not placed (C6.3).")]
        [SerializeField, Min(0f)] float rotationToleranceDeg = DefaultRotationToleranceDeg;

        [Tooltip("Seconds for a sheet released inside tolerance to ease to its exact true " +
                 "pose (C6.5). Smoothstep, the same easing PlayerHands.Advance already uses, " +
                 "so seating reads as the same kind of movement as taking a sheet.")]
        [SerializeField, Min(0f)] float settleSeconds = DefaultSettleSeconds;

        [Header("Board sheet")]
        [Tooltip("Degrees per second while Q or E is held on the board. Moved here from " +
                 "HandlingOptions by C8.16 — the room no longer turns paper, the table does.")]
        [SerializeField, Min(1f)] float sheetTurnDegreesPerSecond = DefaultSheetTurnDegreesPerSecond;

        public float BoardUnitsPerMetre { get { return boardUnitsPerMetre; } }
        public float BoardPadding { get { return boardPadding; } }
        public float SheetSeparation { get { return sheetSeparation; } }
        public float BoardPixelsPerPaperMm { get { return boardPixelsPerPaperMm; } }
        public float PositionTolerance { get { return positionTolerance; } }
        public float RotationToleranceDeg { get { return rotationToleranceDeg; } }
        public float SettleSeconds { get { return settleSeconds; } }
        public float SheetTurnDegreesPerSecond { get { return sheetTurnDegreesPerSecond; } }
    }
}
