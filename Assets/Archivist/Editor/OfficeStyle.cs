using System.Collections.Generic;
using Archivist.Generation.Sheets;
using UnityEngine;

namespace Archivist.Editor
{
    /// <summary>
    /// Everything the §11 debug window says about one office, in one row: the short tag the
    /// Compare pane and the toolbar use, the display name, the lower-case tag the stats footer
    /// uses, and the chrome colour.
    ///
    /// <para>These four facts used to be spelled out seven times — three switch statements on
    /// <see cref="DebugModel"/>, the toolbar's four literal labels, the cut warning's own list of
    /// names, and the footer's two rows of lower-case tags — so a fifth office meant finding all
    /// seven and a renamed office meant the window disagreed with itself. There is one table now,
    /// and every site reads it.</para>
    ///
    /// <para><b>Debug chrome only.</b> <see cref="Colour"/> is the ONLY colour anywhere in the
    /// window: §8.2 keeps the maps themselves to one line style, black on white, so that any
    /// difference the eye finds in Pane 3 is a difference of content.</para>
    ///
    /// <para><b>Indexed by <c>(int)Office</c>, never by enum reflection (§4.1).</b> The enum order
    /// is a determinism contract — <c>Office.cs</c> notes that several PRNG streams are indexed by
    /// <c>(int)office</c> — so the table is sized with <see cref="Offices.Count"/> and its rows sit
    /// at their member's own value. Adding a fifth office is a compile-time hole here, which is the
    /// point.</para>
    /// </summary>
    public sealed class OfficeStyle
    {
        /// <summary>The office this row describes. Also its index into the table.</summary>
        public readonly Office Office;

        /// <summary>
        /// Short tag for the Compare pane, where four headers share one row and the full names
        /// overflow, and for the toolbar's cut toggles. Full names stay everywhere there is room.
        /// </summary>
        public readonly string Abbr;

        /// <summary>Display name — sidebar labels, sheet labels, the cut warning.</summary>
        public readonly string Name;

        /// <summary>Lower-case tag for the stats footer, where the rows are dense and unlabelled.</summary>
        public readonly string FooterTag;

        /// <summary>Chrome colour for this office's survey and sheet outlines.</summary>
        public readonly Color Colour;

        OfficeStyle(Office office, string abbr, string name, string footerTag, Color colour)
        {
            Office = office;
            Abbr = abbr;
            Name = name;
            FooterTag = footerTag;
            Colour = colour;
        }

        /// <summary>
        /// The table, indexed by <c>(int)Office</c>. Sized by <see cref="Offices.Count"/> so a new
        /// member of the enum fails here rather than silently reading as "unknown" at four sites.
        /// </summary>
        static readonly OfficeStyle[] Table = new OfficeStyle[Offices.Count]
        {
            new OfficeStyle(Office.Hydrographic, "HYD", "Hydrographic", "hyd",
                            new Color(0.10f, 0.45f, 0.85f)),
            new OfficeStyle(Office.LandSurvey,   "LS",  "Land Survey",  "land",
                            new Color(0.12f, 0.58f, 0.24f)),
            new OfficeStyle(Office.Garrison,     "GAR", "Garrison",     "garr",
                            new Color(0.85f, 0.45f, 0.10f)),
            new OfficeStyle(Office.Antiquarian,  "ANT", "Antiquarian",  "ant",
                            new Color(0.55f, 0.20f, 0.70f))
        };

        /// <summary>
        /// The whole-island survey is not an office — it is every office's ground at once — so it
        /// has no row and draws in neutral grey.
        /// </summary>
        public static readonly Color WholeIslandColour = new Color(0.45f, 0.45f, 0.45f);

        /// <summary>Every row, in enum order. Iterate this rather than listing offices inline.</summary>
        public static IReadOnlyList<OfficeStyle> All { get { return Table; } }

        /// <summary>
        /// The row for an office, or null for a value outside the enum. Callers fall back to
        /// <c>office.ToString()</c> or magenta, exactly as the switch statements used to.
        /// </summary>
        public static OfficeStyle For(Office office)
        {
            int i = (int)office;
            return i >= 0 && i < Table.Length ? Table[i] : null;
        }
    }
}
