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
    /// name is <i>derived, deterministic and generated</i> — never authored, never stored.
    /// <c>SheetView</c> carries <c>IslandName</c>, <c>Office</c> and <c>Number</c> and nothing
    /// else, and that is correct. The obvious implementations are all the same mistake in
    /// different clothes: a <c>name</c> field on <see cref="Sheet"/>, a
    /// <c>Dictionary&lt;SheetId,string&gt;</c> beside the ledger, a name written into the save
    /// file. Each of them persists a pure function of the seed, which is the mistake R1.11
    /// exists to prevent and the same one <c>SheetLookup</c> calls the design's central
    /// bargain: a sheet in the world stores an <i>identity</i>, and everything else about it is
    /// recovered by regenerating the island. C4.6 refuses to store a seated sheet's pose for
    /// exactly this reason; a name is no different, and is cheaper to recompute than the pose
    /// is. <b>Nothing here caches. Nothing here may cache.</b></para>
    ///
    /// <para><b>The naming itself lives in the generator now — this file only asks.</b>
    /// <see cref="SheetNames.For(Island,Sheet)"/>, in <c>Archivist.Generation.Naming</c>, is
    /// where a sheet's name comes from. It belongs there because a name drawn on this side of
    /// the fence would not be a function of the island seed: it would be a function of the seed
    /// plus whoever called it, and nothing holding only the seed could reproduce it (R1.1,
    /// R1.11). The island's own naming (§9) already lives there and this is the same kind of
    /// fact about the same island.</para>
    ///
    /// <para><b>What was tried first, and why it was abandoned.</b> This file used to implement
    /// C7.7 literally: scan <c>island.Features</c> for the nearest <i>named feature</i> on the
    /// sheet, and fall back to the code alone when there is none. It worked, and it was still
    /// the wrong answer, because of what the generator as built actually names. There are
    /// exactly two sources of named ground on an island — every <c>Settlement</c> (§7.2) and the
    /// top <c>Tuning.PeakNamedCount</c> = 3 <c>Peak</c>s (§7.1). Rivers have no name field at
    /// all, <c>Poi</c> is unnamed by design (POC-03 §5 keeps labels out of scope), and
    /// <b>the coastline has no naming at all</b> — it is a polyline with no named parts.
    /// Meanwhile every name the mockups show is coastal: <i>Cape Vela</i>, <i>Gull Spit</i>,
    /// <i>Cold Harbour</i>, <i>Long Reef</i>, <i>Salt Flats</i>. So the scan returned its
    /// fallback for most sheets, and the Hydrographic section of the cabinet — strips walked
    /// along an empty shore — was a column of bare codes. C7.7's fallback was not a rare case;
    /// it was the common one. <b>The project owner's decision: name the sheet from the ground
    /// it covers, not from a feature standing on it.</b> That is a generator concern, so it
    /// moved. The one part of the old scan that was right is kept inside
    /// <see cref="SheetNames"/>: a sheet carrying a named settlement is still named for it.
    /// </para>
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
        /// <para>A thin delegation, on purpose. The rules — whole-island sheets take the
        /// island's own name (R2.2a, R6.8a), a sheet carrying a named settlement takes it, and
        /// everything else is composed from the ground the sheet covers — all live on
        /// <see cref="SheetNames"/>, with the streams and the word tables they need. Nothing is
        /// added here and nothing may be: a string invented on this side would not be a
        /// function of the seed.</para>
        ///
        /// <para>Pure: same island, same sheet, same answer, and no state touched. The caller
        /// must have the island in hand already — this deliberately does not take a
        /// <see cref="SheetId"/> and regenerate, because that would hide a whole island
        /// generation inside a call a UI would make once per visible row.</para>
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
        /// <para><b>Padded to two, not fixed at two.</b> <c>D2</c> widens on its own past 99,
        /// so a large survey reads <c>CH·104</c> rather than being truncated or silently
        /// mis-sorted. Formatted with <see cref="CultureInfo.InvariantCulture"/> because a code
        /// is an identifier, not prose: a culture with non-ASCII digits would make the same
        /// sheet read differently on two machines, which is the one thing an identifier may not
        /// do.</para>
        ///
        /// <para><b>The separator is a middle dot, not a hyphen.</b> C7.3 and the mockups
        /// agree on <c>CH·01</c>; an implementation brief that said <c>CH-01</c> was simply
        /// wrong, and is corrected here. It is presentation only and lives in one const — see
        /// <see cref="Separator"/> — so house style is one edit away either way.</para>
        ///
        /// <para><b>The whole-island collision: RESOLVED.</b> The whole-island survey (R2.2a)
        /// borrows one of the first three offices — <c>SurveyCutter.CutWholeIsland</c> draws
        /// <c>Range(0, 3)</c> — so under the old rule its sheet 1 rendered <c>CH·01</c>, the
        /// same code as that office's own first sheet. <see cref="SheetId"/> told them apart
        /// with <see cref="SheetId.WholeIsland"/> and the code did not, which made two rows of
        /// one cabinet indistinguishable at exactly the moment they matter: the whole-island
        /// sheet is the one that opens the board (R6.8a). It now renders
        /// <c>&lt;PREFIX&gt;·IX</c> — <c>CH·IX</c> — where <c>IX</c> is
        /// <see cref="IndexSheetMark"/>, the cartographic term for the <b>index sheet</b>: the
        /// key sheet of a series, the one that shows the whole and where every other sheet
        /// falls on it. That is precisely what R2.2a's sheet is, so the mark is a description
        /// rather than a disambiguating suffix invented for the purpose.</para>
        ///
        /// <para><b>The borrowed office prefix is kept</b>, not replaced by a neutral one.
        /// Which office drew the whole-island sheet is still true and still useful — it is that
        /// office's own drawing conventions on the paper, the §8.3 classes it does and does not
        /// draw — and R2.2a makes the sheet part of the collection rather than a frontispiece
        /// standing outside it. <c>CH·IX</c> says both things: the Hydrographic office's index
        /// sheet.</para>
        ///
        /// <para>The number is dropped, not appended: a whole-island survey ships exactly one
        /// sheet, so <c>CH·IX·01</c> would carry no information and would read as a series that
        /// has more.</para>
        /// </summary>
        public static string CodeFor(SheetId id)
        {
            if (id.WholeIsland) return PrefixFor(id.Office) + Separator + IndexSheetMark;

            return PrefixFor(id.Office) + Separator
                 + id.Number.ToString("D2", CultureInfo.InvariantCulture);
        }

        /// <summary>The character between prefix and number — C7.3's middle dot, as drawn in
        /// the mockups. One place, so changing the house style is one edit.</summary>
        public const string Separator = "·";

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
