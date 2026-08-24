using System.Globalization;
using Archivist.Building.Collection;
using Archivist.Generation;
using Archivist.Generation.Naming;
using Archivist.Generation.Sheets;

namespace Archivist.Building.Table
{
    /// <summary>
    /// What a sheet is <i>called</i>, and what it is <i>numbered</i> — the two strings the
    /// cabinet row (C7.3) and the table header (C7.6) put in front of the player.
    ///
    /// <para><b>A sheet has no name, and must not be given one.</b> C7.7 is explicit: a sheet's
    /// name is <i>derived, deterministic and generated</i> — never authored, never stored. A
    /// <c>name</c> field on <see cref="Sheet"/>, a <c>Dictionary&lt;SheetId,string&gt;</c> beside
    /// the ledger, a name in the save file: each persists a pure function of the seed, the
    /// mistake R1.11 exists to prevent and the design's central bargain
    /// (<c>SheetLookup</c>) — a sheet in the world stores an <i>identity</i>, and everything else
    /// is recovered by regenerating the island. C4.6 refuses a seated sheet's pose for the same
    /// reason, and a name is cheaper to recompute than a pose. <b>Nothing here caches. Nothing
    /// here may cache.</b></para>
    ///
    /// <para><b>The naming itself lives in the generator — this file only asks.</b>
    /// <see cref="SheetNames.For(Island,Sheet)"/> belongs in <c>Archivist.Generation.Naming</c>
    /// because a name drawn on this side of the fence would be a function of the seed <i>plus
    /// whoever called it</i>, and nothing holding only the seed could reproduce it (R1.1,
    /// R1.11).</para>
    ///
    /// <para><b>It names surveys and assemblies too.</b> G6.3's Groups row needs the survey's
    /// name and year and the lowest member's code — the same questions this class answers about
    /// a sheet. <see cref="OfficeTitleFor"/> moved here from <c>CabinetPanel</c> because this was
    /// already the one place the four office labels are spelled.</para>
    ///
    /// <para><b>Names come from the ground a sheet covers, not from a feature standing on
    /// it.</b> Implementing C7.7 literally — scan <c>island.Features</c> for the nearest named
    /// feature — returns the bare-code fallback for most sheets, because the generator names
    /// only settlements (§7.2) and the top <c>Tuning.PeakNamedCount</c> peaks (§7.1): rivers have
    /// no name field, <c>Poi</c> is unnamed by design, and <b>the coastline has no naming at
    /// all</b>. Every name the mockups show is coastal — <i>Cape Vela</i>, <i>Gull Spit</i>,
    /// <i>Cold Harbour</i> — so the Hydrographic section came out a column of bare codes. The one
    /// part of that scan which was right is kept inside <see cref="SheetNames"/>: a sheet
    /// carrying a named settlement is still named for it.</para>
    /// </summary>
    public static class SheetNaming
    {
        /// <summary>
        /// The name to show for one sheet. <b>Never null</b> for a real island — every sheet
        /// gets a name now, so the caller never has to fall back to the code alone. A null
        /// <paramref name="island"/> is the caller's bug and yields null rather than an
        /// exception, because a blank cabinet row is a bug anyone can see whereas an exception
        /// thrown while building a row takes the whole table view down.
        ///
        /// <para>A thin delegation, on purpose: the rules — whole-island sheets take the
        /// island's own name (R2.2a, R6.8a), a sheet carrying a named settlement takes it, and
        /// everything else is composed from the ground it covers — live on
        /// <see cref="SheetNames"/> with the streams and word tables they need. Nothing may be
        /// added here: a string invented on this side would not be a function of the seed.</para>
        ///
        /// <para>Pure, and the caller must have the island in hand. This deliberately does not
        /// take a <see cref="SheetId"/> and regenerate, which would hide a whole island
        /// generation inside a call a UI makes once per visible row.</para>
        /// </summary>
        /// <param name="island">The island the sheet belongs to.</param>
        /// <param name="sheet">The sheet, from
        /// <see cref="SheetLookup.TryFind(Island, SheetId, out Sheet)"/> or straight off a
        /// <see cref="Survey"/>.</param>
        public static string NameFor(Island island, Sheet sheet)
        {
            return SheetNames.For(island, sheet);
        }

        /// <summary>
        /// The sheet's code — office prefix plus its number, zero-padded to two digits:
        /// <c>CH·01</c>, <c>FN·07</c>, <c>SK·12</c>, <c>AQ·03</c>. Never null.
        ///
        /// <para><b>Padded to two, not fixed at two.</b> <c>D2</c> widens past 99, so a large
        /// survey reads <c>CH·104</c> rather than being truncated or mis-sorted. Formatted with
        /// <see cref="CultureInfo.InvariantCulture"/> because a code is an identifier: a culture
        /// with non-ASCII digits would make the same sheet read differently on two
        /// machines.</para>
        ///
        /// <para><b>The separator is a middle dot, not a hyphen</b> — C7.3 and the mockups agree
        /// on <c>CH·01</c>. Presentation only, in one const (<see cref="Separator"/>).</para>
        ///
        /// <para><b>The whole-island sheet renders <c>&lt;PREFIX&gt;·IX</c>.</b> That survey
        /// (R2.2a) borrows one of the first three offices, so its sheet 1 would otherwise render
        /// the same code as that office's own first sheet — indistinguishable in the cabinet at
        /// exactly the moment it matters, since the whole-island sheet is the one that opens the
        /// board (R6.8a). <see cref="IndexSheetMark"/> is the cartographic term for the <b>index
        /// sheet</b>: the key sheet of a series, showing the whole and where every other sheet
        /// falls on it, which is precisely what R2.2a's sheet is.</para>
        ///
        /// <para><b>The borrowed office prefix is kept</b>, not replaced by a neutral one: which
        /// office drew it is still true and still useful — its own drawing conventions are on the
        /// paper — and R2.2a makes the sheet part of the collection rather than a frontispiece
        /// outside it. The number is dropped rather than appended, because a whole-island survey
        /// ships exactly one sheet and <c>CH·IX·01</c> would read as a series that has
        /// more.</para>
        /// </summary>
        public static string CodeFor(SheetId id)
        {
            if (id.WholeIsland) return PrefixFor(id.Office) + Separator + IndexSheetMark;

            return PrefixFor(id.Office) + Separator
                 + id.Number.ToString("D2", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The survey a group is made of, as one line: <c>Land Survey 1894</c> — G6.3's "the
        /// survey's name and year".
        ///
        /// <para>A survey has no generated name the way a sheet and an island do, and must not
        /// be given one here: it is identified by the office that made it and the year, which is
        /// R2.2's definition read back — <i>one island, one office, one year, one scale</i>. Both
        /// halves come off the <see cref="SurveySpec"/> the caller holds.</para>
        ///
        /// <para>The year uses <see cref="CultureInfo.InvariantCulture"/> for the reason
        /// <see cref="CodeFor"/> gives, and is not grouped — <c>1894</c>, never
        /// <c>1,894</c>.</para>
        ///
        /// <para>The whole-island survey renders as its borrowed office plus its year, like any
        /// other. It can never form a group (G3.4), so no Groups row shows it; the case is
        /// answered rather than special-cased, because a naming function that throws on a legal
        /// input is a worse trap than a slightly odd string nobody will see.</para>
        /// </summary>
        public static string SurveyLabelFor(SurveySpec survey)
        {
            return OfficeTitleFor(survey.Office) + " "
                 + survey.Year.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// A group's second line: the lowest member's code, then how much of the survey is in
        /// it — <c>FN·03 — 2 of 5</c>. G6.3's "n of N" and its disambiguator, in the place an
        /// office row puts a code.
        ///
        /// <para><b>The disambiguator is the member's code, not a "from 3" suffix.</b> G6.3
        /// proposes appending the lowest member number, because one survey can hold two groups
        /// at once — two halves assembled in different corners — and the survey name alone would
        /// name both. The code carries that number and the office with it, in the exact form the
        /// player is already reading on every row of the section above, so the disambiguator
        /// doubles as a pointer to a row they can go and find. A bespoke <c>· from 3</c> would
        /// be a second notation for a number that already has one.</para>
        ///
        /// <para><b><paramref name="held"/> is what the archive holds, NOT what the survey
        /// shipped</b> — see <c>CabinetPanel</c>'s class comment, which argues the point at
        /// length against G6.3's wording. D-C3 permits the cabinet's counts only "because the
        /// accordion lists only <i>issued</i> sheets, so it never reveals how many the survey
        /// actually has", and <c>LedgerSheetSource</c> repeats it. This function takes the number
        /// rather than the survey precisely so that it cannot reach for the other one.</para>
        /// </summary>
        public static string GroupCodeFor(SheetId lowest, int present, int held)
        {
            return CodeFor(lowest) + CountJoin
                 + present.ToString(CultureInfo.InvariantCulture) + CountOf
                 + held.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The four labels of C7.1, and the first half of <see cref="SurveyLabelFor"/>. A switch
        /// over <see cref="Office"/> rather than an array indexed by <c>(int)office</c>, and
        /// never a switch over the enum's <i>name</i>, for the reason
        /// <see cref="PrefixFor"/> spells out: the enum is append-only, so a new office can only
        /// arrive as an unhandled case, and it should arrive visibly. The default draws the enum
        /// name rather than throwing — an odd word in a section header is a bug anyone can see
        /// and nobody loses a session to.
        /// </summary>
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

        /// <summary>The character between prefix and number — C7.3's middle dot, as drawn in
        /// the mockups. One place, so changing the house style is one edit.</summary>
        public const string Separator = "·";

        /// <summary>What joins a group's code to its count. An em dash rather than a second
        /// middle dot: the dot already means "prefix, then number" two characters to the left,
        /// and one line using one character for two different joins is a line the player has to
        /// parse twice. <c>CabinetStyle.UnknownName</c> is the same dash, so the built-in font
        /// is known to carry it.</summary>
        public const string CountJoin = " — ";

        /// <summary>The word between the two halves of "n of N". Lower case here and upper-cased
        /// on the way to the screen by <c>CabinetStyle.Spaced</c>, like every other code line —
        /// the small caps are a drawing decision and belong with the drawing.</summary>
        public const string CountOf = " of ";

        /// <summary>What stands where the number would be on the whole-island sheet: the
        /// cartographic <b>index sheet</b> of a series. Beside <see cref="Separator"/> and for
        /// the same reason — it is presentation, and changing it must be one edit.</summary>
        public const string IndexSheetMark = "IX";

        /// <summary>
        /// The two-letter office prefix.
        ///
        /// <para><b>CH, FN and SK are read off the mockups</b> (<c>docs/UI/cartography_table/</c>),
        /// which §0 makes the authority on look. They are not initials of the office names and
        /// must not be "corrected" into any — they read as the surveying service's own
        /// shorthand, which is the point.</para>
        ///
        /// <para><b>AQ is a judgement call, not sourced.</b> The Antiquarian office is POC-03's
        /// fourth and postdates the mockups, so no prefix exists for it anywhere. <c>AQ</c> is
        /// chosen as the two letters that read unambiguously as <i>Antiquarian</i> and collide
        /// with none of the three above. Change it freely if the mockups gain a fourth
        /// section — nothing is keyed by it.</para>
        ///
        /// <para><b><see cref="Office.Antiquarian"/> is going to be renamed</b> — the project
        /// owner intends it. That is safe, and this switch is part of why. Unity serialises an
        /// enum by its <i>ordinal</i>, and the generator indexes several streams by ordinal too
        /// (<c>Streams.For(seed, "year", (int)office)</c> among them), so what may never change
        /// is the <b>value 3</b>; the <b>name</b> may change freely. Because this switches on
        /// the enum <i>member</i>, the rename is a compile-safe refactor — every case follows
        /// the symbol automatically and any site that did not is a build error. A lookup keyed
        /// by the office's <i>name</i> as a string would survive the rename by silently
        /// returning nothing, which is the failure mode worth avoiding. <b>Never switch on a
        /// string here.</b></para>
        ///
        /// <para><b>A switch, and a total one.</b> <see cref="Office"/> is <b>append only</b> —
        /// renumbering a member rewrites existing islands — which means a new office can only
        /// ever arrive at the end, and arrives here as an unhandled case. The default returns
        /// <c>"??"</c> rather than throwing: a cabinet that renders two unknown characters is a
        /// bug anyone can see and nobody loses a session to, whereas an exception thrown while
        /// building a row takes the whole table view down. No array indexed by
        /// <c>(int)office</c> for the same reason <c>Offices.All</c> is a written-out array
        /// rather than enum reflection (§4.1): a gap or an append would go unnoticed.</para>
        /// </summary>
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
    }
}
