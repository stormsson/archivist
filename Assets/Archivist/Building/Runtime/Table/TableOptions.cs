using UnityEngine;
using UnityEngine.Serialization;

namespace Archivist.Building.Table
{
    /// <summary>
    /// Tuning for the cartography table — every number spec §10 lists, and no others.
    ///
    /// <para><b>One asset.</b> These numbers are read from a board space, a draw-order stack, a
    /// texture budget, a snap test and an input handler, none of which owns them; scattered,
    /// there is no place to answer "what is this table set to". Two of them are also the
    /// <i>same</i> number seen twice (C5.5: the board slab and the cabinet thumbnail share one
    /// render, so they must share one pixel density or the cache silently renders twice).</para>
    ///
    /// <para><b>A ScriptableObject rather than consts</b>, for the reason
    /// <see cref="Archivist.Building.Handling.HandlingOptions"/> gives: these are feel values,
    /// settled by dragging a sheet until the tolerance stops feeling either generous or fussy.
    /// An asset can be edited <i>in play mode and the edit is kept</i>; consts mean exit play
    /// mode, edit, recompile, re-enter, and lose the board you were looking at. The defaults are
    /// starting points — findings go in <c>docs/UI/cartography_table/findings.md</c>.</para>
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

        /// <summary>
        /// Where the board camera starts, as a divisor of "the whole board fits" (§3.1, C5.1).
        /// 1 is C8.13's original framing; 2 draws every sheet at twice the size.
        ///
        /// <para><b>C8.13 is superseded outright, both halves</b> (G10.1,
        /// <see cref="BoardViewport"/>). This is no longer the board's framing but only where the
        /// framing <b>starts</b>: the wheel moves it between <see cref="BoardZoomMin"/> and
        /// <see cref="BoardZoomMax"/> and a right-drag pans, and both reset to this number on
        /// every opening, because a camera is not a player fact and does not belong in
        /// <c>BoardStore</c> (§4.2, G4.4).</para>
        ///
        /// <para><b>Why 2 rather than 1 as the resting view.</b> At 1 a Land Survey slab is 35%
        /// of the viewport height on island 0 — small paper for the thing the whole activity
        /// consists of reading — and at 2 it is 70%. The cost G10.1 named is now paid rather
        /// than deferred: at 2 the camera shows half the board's height and width, so roughly
        /// three quarters of the mounting sheet is off screen, and a group retrieved from the
        /// drawer or a board restored from a save can put paper outside the view. Panning is
        /// what reaches it.</para>
        /// </summary>
        public const float DefaultBoardZoom = 2f;

        /// <summary>
        /// The zoom-out stop, and it is C8.13's view exactly: at 1 the camera's half-height is
        /// the board's half-height, so the whole mounting sheet is framed. A floor with a
        /// meaning, rather than an arbitrary one — the composition the spec was written around
        /// stays a place the player can always get back to, and it is also the one zoom at
        /// which panning does nothing at all, because the view already contains the board.
        ///
        /// <para>Going below 1 was considered and rejected: it buys nothing but empty ground
        /// (the sea is never drawn — R1.4) and it would make the mounting sheet a shrinking
        /// rectangle in a field of clear colour, which is the one composition that says the
        /// board is a small object rather than the surface the game happens on.</para>
        /// </summary>
        public const float DefaultBoardZoomMin = 1f;

        /// <summary>
        /// The zoom-in stop. 4, and the ceiling is the <b>raster</b>, not the geometry.
        ///
        /// <para>C5.5 renders one texture per sheet at <see cref="BoardPixelsPerPaperMm"/> —
        /// 0.6 px per millimetre of paper — and it serves both the board slab and the cabinet
        /// thumbnail, so there is no second, finer copy to zoom into. At 1:2500 that works out
        /// at 24 texels per board unit, and on a 1080-high viewport island 0's board draws
        /// 19.67 screen pixels per board unit at zoom 1 — so texel parity, one screen pixel per
        /// texel, falls at zoom 1.22. Every zoom this table has ever shipped is already
        /// magnifying. At the default 2 one texel covers 1.6 screen pixels; at 4 it covers 3.3,
        /// which is about where paper stops reading as paper and starts reading as pixels. Past
        /// that the player is being shown the render settings.</para>
        ///
        /// <para>4 also lands on a framing that means something: it puts 13.73 board units
        /// across the viewport's height on island 0, and a Land Survey A1 slab is 12.85 units
        /// across its short side — so at the stop, the sheet being read fills the frame (93% of
        /// the viewport's height). The mounting sheet is then 2.4 screens wide and 4 tall, which
        /// is about where it stops giving any spatial context at all.</para>
        /// </summary>
        public const float DefaultBoardZoomMax = 4f;

        /// <summary>
        /// What one wheel notch multiplies the zoom by. <b>Multiplicative, and that is the whole
        /// point of the value.</b> A linear step is a quarter of the view at zoom 1 and a
        /// sixteenth at zoom 4, so the same notch feels violent zoomed out and glacial zoomed
        /// in; a constant ratio is the same apparent step everywhere.
        ///
        /// <para>1.15 puts the full range at about 10 notches — five either side of the default
        /// 2 — which is one comfortable sweep of a wheel from stop to stop without being able to
        /// cross it by accident.</para>
        /// </summary>
        public const float DefaultBoardZoomStep = 1.15f;

        /// <summary>
        /// How many notches one raw unit of wheel travel is worth, after <see cref="Wheel"/>
        /// has bucketed the reading. <b>The device dial, and it is one dial for both wheels</b>
        /// — the board's zoom and the cabinet's accordion are turned by the same hand on the
        /// same hardware, so a table that needed two numbers for that would be stating the same
        /// fact twice and drifting.
        ///
        /// <para><b>Why a dial rather than a smaller zoom step.</b> The Input System does not
        /// normalise scroll: a Windows detent reports 120, a macOS one about 1, and a trackpad a
        /// continuous stream — several "notches" inside one frame.
        /// <see cref="DefaultBoardZoomStep"/> is what one notch is worth, argued from the range;
        /// this is how much of a notch the hardware delivered. Folded together, tuning a trackpad
        /// would mean editing the field that documents how far the zoom reaches.</para>
        ///
        /// <para><b>0.03 is measured, not reasoned</b> — settled with a hand on a macOS
        /// trackpad, where the raw reading is roughly thirty units per notch's worth of intent.
        /// That is a device fact and nothing else: on hardware that reports one clean unit per
        /// detent it wants to go back up toward 1. It is a serialised field precisely so it can
        /// be dragged in play mode with the wheel in your hand, which is the only way this
        /// number is ever settled.</para>
        /// </summary>
        public const float DefaultWheelSensitivity = 0.03f;

        /// <summary>
        /// How far one notch scrolls the cabinet, in canvas pixels — the accordion's half of
        /// <see cref="DefaultWheelSensitivity"/>, which supplies the notches.
        ///
        /// <para>40 is a little over half a row (<c>CabinetStyle.RowHeight</c> is 74), so a
        /// notch moves the list by an amount the eye can follow back to where it was. A whole
        /// row per notch was tried on paper and rejected: rows are tall, and a list that jumps a
        /// full row loses the sense of a continuous column of paper.</para>
        /// </summary>
        public const float DefaultCabinetScrollPixelsPerNotch = 40f;

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

        [Tooltip("Where the board camera starts, as a divisor of 'the whole board fits'. 1 is " +
                 "C8.13's framing; 2 draws every sheet at twice the size and shows a quarter " +
                 "of the mounting sheet. The wheel moves it between the two stops below and a " +
                 "right-drag pans; both reset to this on every opening.")]
        [SerializeField, Min(0.1f)] float boardZoom = DefaultBoardZoom;

        [Tooltip("Zoom-out stop. 1 is C8.13's framing exactly — the whole mounting sheet in " +
                 "view — and at 1 there is nothing to pan to, because the view already " +
                 "contains the board.")]
        [SerializeField, Min(0.1f)] float boardZoomMin = DefaultBoardZoomMin;

        [Tooltip("Zoom-in stop. Bounded by the raster, not the geometry: one texture per sheet " +
                 "at BoardPixelsPerPaperMm serves the slab and the thumbnail (C5.5), and past " +
                 "about 4 the board is magnifying texels rather than showing more map.")]
        [SerializeField, Min(0.1f)] float boardZoomMax = DefaultBoardZoomMax;

        [Tooltip("What one wheel notch MULTIPLIES the zoom by. A ratio, not a step: a linear " +
                 "step feels fast zoomed out and glacial zoomed in. 1.15 is about ten notches " +
                 "from stop to stop.")]
        [SerializeField, Min(1.001f)] float boardZoomStep = DefaultBoardZoomStep;

        [Tooltip("How much of a notch one raw unit of wheel travel is worth — the DEVICE dial, " +
                 "shared by the board's zoom and the cabinet's accordion. The Input System does " +
                 "not normalise scroll: a Windows detent reports 120, a macOS one about 1, a " +
                 "trackpad a continuous stream. Lower it if both wheels feel too fast.")]
        [FormerlySerializedAs("boardWheelSensitivity")]
        [SerializeField, Min(0.001f)] float wheelSensitivity = DefaultWheelSensitivity;

        [Tooltip("Canvas pixels the cabinet scrolls per notch. A row is 74, so 40 is a little " +
                 "over half a row. Raise this, not WheelSensitivity, if only the column is " +
                 "sluggish — WheelSensitivity moves the board's zoom with it.")]
        [SerializeField, Min(1f)] float cabinetScrollPixelsPerNotch = DefaultCabinetScrollPixelsPerNotch;

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
        public float BoardZoom { get { return boardZoom; } }
        public float BoardZoomMin { get { return boardZoomMin; } }
        public float BoardZoomMax { get { return boardZoomMax; } }
        public float BoardZoomStep { get { return boardZoomStep; } }
        public float WheelSensitivity { get { return wheelSensitivity; } }
        public float CabinetScrollPixelsPerNotch { get { return cabinetScrollPixelsPerNotch; } }
        public float BoardPixelsPerPaperMm { get { return boardPixelsPerPaperMm; } }
        public float PositionTolerance { get { return positionTolerance; } }
        public float RotationToleranceDeg { get { return rotationToleranceDeg; } }
        public float SettleSeconds { get { return settleSeconds; } }
        public float SheetTurnDegreesPerSecond { get { return sheetTurnDegreesPerSecond; } }

        /// <summary>
        /// The loaded <c>TableOptions</c> asset, or null.
        ///
        /// <para>An asset is not a scene object, so <c>FindFirstObjectByType</c> cannot see one;
        /// but it IS loaded whenever anything on the table serialises a reference to it, which
        /// <see cref="BoardView"/> does. So the loaded-object search finds the one the
        /// board is already using — which is the point: two components on one table reading two
        /// different assets would be a board that glows at one tolerance and seats at
        /// another.</para>
        /// </summary>
        public static TableOptions FindLoaded()
        {
            TableOptions[] all = Resources.FindObjectsOfTypeAll<TableOptions>();
            return all != null && all.Length > 0 ? all[0] : null;
        }
    }
}
