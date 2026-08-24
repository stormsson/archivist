using Archivist.Generation.Geometry;

namespace Archivist.Building.Table
{
    /// <summary>
    /// The ground &lt;-&gt; board transform of the cartography table (spec §3.1, §3.2).
    ///
    /// <para><b>The board is ground, not layout.</b> C1.2 settles it: a sheet is drawn at the
    /// size its ground footprint occupies, so two offices' sheets of the same hillside differ
    /// in size and overlap. That only holds if there is exactly one affine map from ground
    /// metres to board units and every slab goes through it. This struct is that map, and it
    /// is deliberately the whole of it — a second place that multiplied by
    /// <c>BoardUnitsPerMetre</c> would be a second place to get the centre wrong.</para>
    ///
    /// <para><b>Ground X → board X, ground Y → board Z.</b> This is the single easiest thing
    /// here to get wrong, because ground space is 2D and the board is not: the board lies in
    /// the XZ plane at y = 0 and the camera looks down −Y (§3.1). Ground Y is a *northing*,
    /// not a height. Feeding it to Vector3.y would stand the island on edge in front of a
    /// camera that is looking at the wrong plane, and the failure looks like "nothing renders"
    /// rather than like an axis swap. This file has no engine types, so it cannot build the
    /// Vector3 itself: <b>the caller</b> writes <c>new Vector3((float)b.X, y, (float)b.Y)</c>,
    /// taking Y from the draw-order stack of §3.3, never from the ground.</para>
    ///
    /// <para><b>Why a padded rect.</b> <see cref="ForIsland"/> takes <c>island.LandBounds</c>
    /// and grows it by a fraction of its longer side. Coastal sheets are cut against the
    /// shore and routinely hang off the land — a board clipped to LandBounds would put a
    /// Hydrographic strip's true pose half outside the mounting sheet, i.e. would make a
    /// correctly placed sheet look wrong. The padding is a fraction rather than a distance so
    /// a 3 km island and a 12 km island get proportionate margins; the default (0.08) lives in
    /// <c>TableOptions</c>, not here, because it is a feel value.</para>
    ///
    /// <para><b>A slab's size is baked into its quad, not applied as a scale.</b>
    /// <see cref="Archivist.Building.Table.BoardSheetView"/> puts
    /// <c>Survey.SheetGroundWidth * UnitsPerMetre</c> straight into a flat quad's vertices and
    /// leaves <c>localScale</c> at one. A paper-metres-to-board-units factor only exists to undo
    /// a unit conversion nobody needed to make: <c>SheetGroundWidth</c> is the ground covered by
    /// the MAP area, which is what <c>Sheet.FrameRect</c> and <c>Sheet.Contains</c> describe, so
    /// scaling from whole paper over-covers the ground by the margin on all four sides. The
    /// figures §3.2 carries are the corrected ones: a 1:2500 A1 covers <b>1285 × 1902 m</b> (map
    /// 514 × 761 mm), a Hydrographic strip <b>875 × 425 m</b> (map 350 × 170 mm). Sheets still
    /// differ in board size by exactly as much as their ground footprints differ (D-C5).</para>
    ///
    /// <para>No UnityEngine, and no tuning constants: every value arrives as a parameter, so
    /// the transform is testable headlessly (A7) and the defaults stay in one place (§10).</para>
    /// </summary>
    public readonly struct BoardSpace
    {
        /// <summary>The padded ground rect the board covers. Ground metres, ground axes.</summary>
        public readonly Rect2 GroundArea;

        /// <summary>Centre of <see cref="GroundArea"/> — the ground point that sits at board origin.</summary>
        public readonly V2 GroundCentre;

        /// <summary>Board units per ground metre. 0.01 by default, so one unit is 100 m (§3.1).</summary>
        public readonly double UnitsPerMetre;

        public BoardSpace(Rect2 groundArea, double unitsPerMetre)
        {
            GroundArea = groundArea;
            GroundCentre = groundArea.Centre;
            UnitsPerMetre = unitsPerMetre;
        }

        /// <summary>
        /// The board for one island. <paramref name="padding"/> is a <b>fraction of the longer
        /// side</b> of <paramref name="landBounds"/>, applied to all four edges, so the margin
        /// is the same width all round and does not stretch a long thin island.
        ///
        /// <para>An empty <paramref name="landBounds"/> — <see cref="Rect2.Empty"/> carries
        /// MaxValue/MinValue sentinels — is returned unpadded rather than expanded, because
        /// expanding it produces infinities that then travel silently through
        /// <see cref="ToBoard"/> into transform positions.</para>
        /// </summary>
        public static BoardSpace ForIsland(Rect2 landBounds, double padding, double unitsPerMetre)
        {
            if (landBounds.IsEmpty) return new BoardSpace(landBounds, unitsPerMetre);

            double longer = landBounds.Width > landBounds.Height ? landBounds.Width : landBounds.Height;
            return new BoardSpace(landBounds.Expanded(longer * padding), unitsPerMetre);
        }

        /// <summary>
        /// Ground metres to board units. Result <c>.X</c> is board X, result <c>.Y</c> is
        /// board <b>Z</b> — see the class comment; the caller builds the Vector3.
        /// </summary>
        public V2 ToBoard(V2 groundPoint)
        {
            return (groundPoint - GroundCentre) * UnitsPerMetre;
        }

        /// <summary>
        /// Board units back to ground metres — the exact inverse of <see cref="ToBoard"/>.
        /// Pass board (X, Z), not (X, Y). This is the direction a drag runs in: the pointer
        /// hits the board plane, and <see cref="SheetFit"/> answers in ground metres, because
        /// the truth it compares against (<c>Sheet.CentreGround</c>) is ground.
        /// </summary>
        public V2 ToGround(V2 boardPoint)
        {
            return boardPoint / UnitsPerMetre + GroundCentre;
        }

        /// <summary>Board-unit width of <see cref="GroundArea"/> — the mounting sheet's X size.</summary>
        public double BoardWidth { get { return GroundArea.Width * UnitsPerMetre; } }

        /// <summary>Board-unit height of <see cref="GroundArea"/> — the mounting sheet's <b>Z</b> size.</summary>
        public double BoardHeight { get { return GroundArea.Height * UnitsPerMetre; } }
    }
}
