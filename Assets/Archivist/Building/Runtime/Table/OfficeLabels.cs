using Archivist.Generation.Sheets;

namespace Archivist.Building.Table
{
    /// <summary>
    /// The four offices as the player reads them — a title (C7.1) and a two-letter prefix — in
    /// the one place either is spelled.
    ///
    /// <para><b>CH, FN and SK are read off the mockups</b> (<c>docs/UI/cartography_table/</c>),
    /// which §0 makes the authority on look. They are not initials of the office names and must
    /// not be "corrected" into any — they read as the surveying service's own shorthand, which is
    /// the point. <b>AQ is a judgement call, not sourced</b>: the Antiquarian office is POC-03's
    /// fourth and postdates the mockups, so no prefix exists for it anywhere. Change it freely if
    /// the mockups gain a fourth section — nothing is keyed by it.</para>
    ///
    /// <para><b>A switch over the <see cref="Office"/> member — never over its name, never an
    /// array indexed by <c>(int)office</c>.</b> The enum is <b>append only</b>, since renumbering
    /// a member rewrites existing islands, so a new office can only arrive at the end and should
    /// arrive here as an unhandled case. <see cref="Office.Antiquarian"/> is going to be renamed:
    /// switching on the member makes that a compile-safe refactor, where a lookup keyed by the
    /// office's <i>name</i> would survive the rename by silently returning nothing.</para>
    ///
    /// <para>Both defaults draw a placeholder rather than throwing — an odd word in a header is a
    /// bug anyone can see and nobody loses a session to, whereas an exception thrown while
    /// building the header takes the whole table view down.</para>
    /// </summary>
    public static class OfficeLabels
    {
        /// <summary>The four labels of C7.1.</summary>
        public static string OfficeTitleFor(Office office)
        {
            switch (office)
            {
                case Office.Hydrographic: return "Hydrographic";
                case Office.LandSurvey:   return "Land Survey";
                case Office.Garrison:     return "Garrison";
                case Office.Antiquarian:  return "POIs";
                default:                  return office.ToString();
            }
        }

        /// <summary>The two-letter office prefix.</summary>
        public static string PrefixFor(Office office)
        {
            switch (office)
            {
                case Office.Hydrographic: return "CH";
                case Office.LandSurvey:   return "FN";
                case Office.Garrison:     return "SK";
                case Office.Antiquarian:  return "AQ";
                default:                  return "??";
            }
        }

        /// <summary>The character between prefix and number — C7.3's middle dot, as drawn in
        /// the mockups. One place, so changing the house style is one edit.</summary>
        public const string Separator = "·";
    }
}
