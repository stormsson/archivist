using System;
using System.Collections.Generic;
using System.Globalization;
using Archivist.Building.Collection;
using Archivist.Generation;
using Archivist.Generation.Sheets;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Archivist.Building.Table
{
    /// <summary>
    /// Every colour, size and spacing the cabinet and the header use, in one place.
    ///
    /// <para><b>Why this is not in <see cref="TableOptions"/>.</b> <c>TableOptions</c> is a
    /// ScriptableObject holding "every number spec §10 lists, and no others" — feel values,
    /// settled by playing: a snap tolerance is argued about with a mouse in your hand, so it has
    /// to survive being edited in play mode. None of that is true of a hairline width. These are
    /// <i>look</i> values, and their authority is not play at all but the four PNGs in
    /// <c>docs/UI/cartography_table/</c>. Putting them in the tuning asset would invite someone
    /// to drag the panel cream three stops off the mockup in an inspector, with no diff and
    /// nothing to review; as consts they change in one file, in one commit, against the mockup
    /// they are supposed to match. CLAUDE.md's rule is "one place per assembly, not scattered
    /// into behaviours" — this is that one place for chrome, and the rule it is really serving
    /// is the second half.</para>
    ///
    /// <para><b>Reference space is 1920 × 1080</b>, the same as the room's canvas in
    /// <c>RoomBuilder.BuildInteractionUi</c>. The mockups were rendered at 1442 wide, so every
    /// pixel measured off them is multiplied by about 1.33 before it lands here. Where a number
    /// looked arbitrary in the mockup it has been rounded to something a human can hold.</para>
    ///
    /// <para><b>The fonts are approximations and are meant to be.</b> No font assets may be
    /// added, so the serif is asked for from the OS by name and the sans is Unity's built-in
    /// face. Letter-spaced small caps do not exist in legacy <see cref="Text"/> at all, so
    /// <see cref="Spaced"/> fakes them by putting a space between characters. It is coarse, and
    /// it is visibly the right shape, which for a slice whose point is "does this layout read"
    /// is the thing that matters. When this becomes real type, it becomes real type here and
    /// nowhere else.</para>
    /// </summary>
    public static class CabinetStyle
    {
        // ---- palette (measured off 1b-empty-table.png and 2a-cabinet-states.png) ----

        /// <summary>Warm cream of the cabinet column.</summary>
        public static readonly Color PanelCream = Rgb(0xF4, 0xED, 0xE0);

        /// <summary>The header band, a shade lighter than the cabinet so the two read as
        /// separate pieces of furniture rather than one L-shaped one.</summary>
        public static readonly Color HeaderCream = Rgb(0xF7, 0xF1, 0xE6);

        /// <summary>Dark wood surround. Nothing in this slice paints it — the board camera's
        /// backdrop does — but it lives here so the one place that will can find it.</summary>
        public static readonly Color Wood = Rgb(0x2A, 0x1F, 0x16);

        /// <summary>Gold accent: on-table borders, on-table titles, the table mark.</summary>
        public static readonly Color Gold = Rgb(0xB8, 0x86, 0x3B);

        /// <summary>Border gold — a touch lighter than <see cref="Gold"/>, because a 1 px line
        /// at full accent strength reads as a box drawn around the row rather than as the row
        /// having changed.</summary>
        public static readonly Color GoldBorder = Rgb(0xC9, 0xA0, 0x63);

        /// <summary>Fill behind an on-table row and an all-on-table section header.</summary>
        public static readonly Color GoldTint = Rgb(0xF6, 0xEB, 0xD6);

        /// <summary>Ink: sheet names, island name, section titles.</summary>
        public static readonly Color Ink = Rgb(0x3A, 0x32, 0x29);

        /// <summary>The quiet tan of labels, codes, counts and footer hints. Everything the
        /// player reads second.</summary>
        public static readonly Color Muted = Rgb(0xA9, 0x97, 0x81);

        /// <summary>Row plate — near-white paper on cream.</summary>
        public static readonly Color RowPlate = Rgb(0xFC, 0xFA, 0xF6);

        /// <summary>Row hairline in the drawer state.</summary>
        public static readonly Color RowBorder = Rgb(0xE4, 0xDA, 0xCA);

        /// <summary>Rules: under the header, left of the cabinet, between sections.</summary>
        public static readonly Color Rule = Rgb(0xE0, 0xD5, 0xC2);

        /// <summary>The blank plate a thumbnail shows before its texture arrives (C5.6).</summary>
        public static readonly Color ThumbPlate = Rgb(0xFB, 0xF7, 0xEF);

        public static readonly Color ThumbBorder = Rgb(0xE7, 0xDE, 0xCD);

        // ---- header ----

        public const float HeaderHeight = 96f;
        public const float HeaderPadLeft = 36f;
        public const float HeaderFieldGap = 44f;
        public const float HeaderLabelSize = 13;
        public const float IslandNameSize = 30;
        public const float SheetNameSize = 26;
        public const float SheetCodeSize = 14;
        public const float HeaderLabelGap = 4f;
        public const float HeaderDividerHeight = 46f;

        /// <summary>Minimum width of the ISLAND field, so the divider does not walk left and
        /// right as islands with short and long names come and go.</summary>
        public const float IslandFieldMinWidth = 240f;

        // ---- cabinet column ----

        /// <summary>Fraction of screen width the cabinet takes. A fraction, not a pixel count:
        /// the requirements say "a right column ~22% width", and 22% of an ultrawide is a
        /// different number of pixels from 22% of a laptop while being the same column.</summary>
        public const float CabinetWidthFraction = 0.22f;

        public const float CabinetPadX = 20f;
        public const float CabinetPadTop = 14f;
        public const float SectionSpacing = 2f;
        public const float SectionHeaderHeight = 52f;
        public const float ChevronWidth = 26f;
        public const float SectionTitleSize = 20;
        public const float SectionCountSize = 14;

        // ---- rows ----

        public const float RowHeight = 74f;
        public const float RowSpacing = 6f;
        public const float RowPadLeft = 14f;
        public const float RowPadRight = 16f;
        public const float RowNameSize = 20;
        public const float RowCodeSize = 12;
        public const float RowNameHeight = 26f;
        public const float RowCodeHeight = 18f;
        public const float RowNameOffsetY = 11f;
        public const float RowCodeOffsetY = -13f;

        public const float ThumbWidth = 76f;
        public const float ThumbHeight = 44f;
        public const float ThumbTextGap = 18f;

        /// <summary>How far a thumbnail is knocked off-square when its sheet is out on the
        /// board (C7.4). Small: this is a sheet lying slightly askew, not a jaunty one.</summary>
        public const float OnTableTiltDegrees = -3.5f;

        /// <summary>Opacity of the thumbnail copy that follows the pointer while a row is being
        /// dragged onto the board (C7.5). Transparent enough to read as "not there yet" —
        /// nothing has been laid down until the pointer is released — and opaque enough to still
        /// show which sheet is in hand over the dark wood of the board.</summary>
        public const float GhostAlpha = 0.72f;

        // ---- table mark ----

        public const float MarkWidth = 18f;
        public const float MarkHeight = 14f;
        public const float MarkBarThickness = 2.5f;
        public const float MarkLegHeight = 7f;
        public const float MarkLegInset = 3.5f;

        // ---- groups (G6.1–G6.3) ----
        //
        // ⟨proposed⟩, every one of them. Unlike everything above, no mockup covers the Groups
        // section — 2a-cabinet-states.png predates it — so these are the first playtest's to
        // settle. They are consts here anyway, and not fields on TableOptions, for the reason
        // the class comment gives: they are look values, and the argument against dragging a
        // panel cream three stops off the mockup in an inspector applies just as much to a mark
        // that has no mockup to be dragged off. §8.4 of the groups spec says the same about the
        // hint pulse's alphas.

        /// <summary>The Groups section's title. A plain noun, in the office sections' voice —
        /// they are titled by what they hold, not by what can be done to them, so this is
        /// "Groups" and not "Assembled" or "Your assemblies".</summary>
        public const string GroupsSectionTitle = "Groups";

        /// <summary>The bracket that marks a grouped sheet's office row (G6.2). Narrower and
        /// taller than the trestle, deliberately: two marks that share a slot must differ in
        /// silhouette before they differ in detail.</summary>
        public const float GroupMarkWidth = 14f;

        public const float GroupMarkHeight = 16f;
        public const float GroupMarkThickness = 2.5f;

        /// <summary>How far the bracket's arms reach off its spine. Short — a bracket that is
        /// nearly square reads as an open box.</summary>
        public const float GroupMarkArmWidth = 6f;

        /// <summary>Width of the kin bar down the left edge of a hovered group's rows (G6.3).
        /// Three pixels: one is a hairline and reads as the row's border having changed, which
        /// is C7.4's vocabulary and must not be borrowed.</summary>
        public const float KinMarkerWidth = 3f;

        /// <summary><see cref="Gold"/>, and deliberately the same gold rather than a fifth
        /// accent. The kin bar is a hover, so it is never on screen at the same time as another
        /// row's hover, and it is additive — no state uses a left bar — so it cannot be confused
        /// with the two states of C7.4 despite sharing their colour. Named separately so that
        /// the day the highlight wants its own hue, it has somewhere to be.</summary>
        public static readonly Color KinMarker = Gold;

        // ---- the assisted-snap hint pulse (G7.5) ----
        //
        // ⟨proposed⟩, all four, and §8.4 of the groups spec says so in as many words: unlike
        // every other value in this file they have NO MOCKUP behind them — 1c-snap-moment.png
        // shows the steady snap glow and nothing that pulses — so the first playtest is their
        // authority and not the PNGs. They are consts here anyway, and not fields on
        // TableOptions, for the reason the class comment gives: these are look values, and the
        // argument against dragging a panel cream three stops off the mockup in an inspector
        // applies just as much to a value that has no mockup to be dragged off. What is NOT a
        // look value and is correctly elsewhere is GlowingHintRange — how far the hint reaches
        // is felt with a mouse in your hand, so it is a player-facing setting in
        // GameplayOptions, read from config/generation.yml.

        /// <summary>
        /// The snap glow of C6.4, from mockup <c>1c</c>, and <b>deliberately the same gold</b>
        /// for the hint: G7.5 asks for it explicitly, so that the pulse reads as the snap
        /// affordance <i>anticipated</i> rather than as a second, unrelated signal. G7.2's three
        /// states are then one colour ramp — <see cref="GoldBorder"/> steady for a selection,
        /// this pulsing for "related and near", this steady for "inside tolerance" — and the
        /// player learns one thing rather than three.
        ///
        /// <para><b>There is a second copy of this value.</b> <c>BoardInteractor</c> holds it as
        /// a private <c>SnapGold</c>, from before there was anywhere shared to put it. This is
        /// the place it belongs, by the class comment's own argument; the duplicate should
        /// collapse onto this field the next time that file is opened, and until it does the two
        /// must be kept equal — <c>0xE6A83E</c>.</para>
        /// </summary>
        public static readonly Color SnapGold = Rgb(0xE6, 0xA8, 0x3E);

        /// <summary>Seconds for one full cycle of the hint pulse. "Slow", per the request —
        /// slow enough to read as breathing rather than as a warning light, which is the wrong
        /// register entirely for a table with no fail state and no timer.</summary>
        public const float HintPeriodSeconds = 1.4f;

        /// <summary>The bottom of the pulse. <b>Never zero:</b> a slab that vanished for half a
        /// second would read as broken, or as the game having dropped it. The hint dims; it does
        /// not blink.</summary>
        public const float HintAlphaMin = 0.15f;

        /// <summary>The top of the pulse — full strength, so that at its peak the hint is
        /// exactly the steady <see cref="SnapGold"/> the player is being led toward.</summary>
        public const float HintAlphaMax = 1.0f;

        // ---- the halo the pulse is drawn as (G7.5, second attempt) ----
        //
        // ⟨proposed⟩, every one of them, on exactly the same terms as the four above and with
        // the same warning attached twice over: NO MOCKUP covers any of this. 1c-snap-moment.png
        // shows a steady rim and nothing that glows, so the first playtest is the authority and
        // these numbers are a starting position, not a measurement. They are consts here rather
        // than fields on TableOptions for the class comment's reason — look values, one file,
        // one commit — and see SnapHint's class comment for the arithmetic each one came from.
        //
        // THE FIRST ATTEMPT WAS A HARD RIM AND IT WAS REJECTED ON SIGHT. G7.5's alpha was
        // applied to the 1.02 selection outline, which on island 0's Land Survey slab
        // (12.85 board units short side) is a rim 0.128 units wide — about 3 screen pixels at
        // the old framing, 6 at BoardZoom 2 — and G7.5 then dimmed that hairline to 0.15 at the
        // trough. It was a correct implementation of a signal too small to see. What replaces it
        // is light rather than line: a stack of concentric quads bleeding outward from the
        // paper, each faint, compositing into a gradient.

        /// <summary>How many concentric quads the halo is built from. They are nested filled
        /// quads, not annuli, so a point near the paper is covered by all of them and a point at
        /// the outer edge by only the last — the falloff is the accumulation, and five steps is
        /// the fewest that reads as a gradient rather than as bands. <b>This is the first knob
        /// to turn if banding shows:</b> the cost of another ring is one more draw of a mesh
        /// already resident, and nothing else in the file changes.</summary>
        public const int HaloRings = 5;

        /// <summary>Ceiling on any halo bleed — <see cref="SeatedBleed"/> today — as a fraction
        /// of the slab's <i>half</i> short side, so a sheet smaller than any this project has yet
        /// produced cannot be swallowed by its own glow. It does not bind on any of island 0's
        /// surveys: the tightest, a 2.75-unit Antiquarian detail sheet, allows 0.62 against the
        /// 0.30 asked for. A guard rail, not a tuning.</summary>
        public const float HaloBleedMaxFraction = 0.45f;

        /// <summary>The halo's gold, and deliberately <b>not</b> <see cref="SnapGold"/>: it is
        /// lighter and much less saturated, because this is light spilling off the page rather
        /// than a line drawn on it. Same hue family, so G7.2's ramp still reads as one idea —
        /// pale gold breathing for "related", hot gold steady for "seated".</summary>
        /// <para><b>It is now the GHOST's colour</b> (see the ghost block below), which is where
        /// "light spilling off the page rather than a line drawn on it" turned out to belong: a
        /// slot marked on the table is exactly the thing that must not look like ink. G7.2's
        /// ramp still reads as one idea, redistributed — pale gold marks the empty place, hot
        /// gold marks the paper that is about to fill it.</para>
        public static readonly Color HaloColour = Rgb(0xF5, 0xD8, 0x9A);

        /// <summary>Where the rings sit under their slab, as fractions of
        /// <c>TableOptions.SheetSeparation</c> — innermost ring at the first, outermost at the
        /// second. Both are comfortably inside one slab's slot in §3.3's stack (the next sheet
        /// down is a whole separation below) and both are clear of the selection outline's 0.15,
        /// so the halo and the rim never contend. See <c>SnapHint</c> for why the spacing itself
        /// carries no visual load.</summary>
        public const float HaloDropNear = 0.30f;

        public const float HaloDropFar = 0.70f;

        // ---- state 3, seated (C6.4 / G7.2 rung 3) ----
        //
        // Playtested and reported as broken: "when I drag a sheet over another and the halo
        // starts, releasing does not snap." It always was snapping — Evaluate() and Release()
        // call one TryBestFuse, so the preview and the outcome cannot disagree. What was broken
        // was that G7.2's rungs 2 and 3 differed only in whether a ~5 px gold rim was pulsing or
        // steady, while the hint fires at 19.03 board units (≈750 px) and the fuse at 1.54
        // (≈61 px). The player was shown "release now" twelve times further out than releasing
        // works. So rung 3 is now a CATEGORICAL change and not a brighter rung 2: the motion
        // stops, the halo collapses to a third of its width, and the colour goes hot.

        /// <summary>The seated halo's width, per side, in board units — a little over half of
        /// the 0.55 the retired rung-2 halo drew at (about 22 px at BoardZoom 2 on island 0, the
        /// measurement this one is defined against). The collapse is the point: a wide soft
        /// breath gathering into a
        /// tight bright band is the paper being pulled in, and it happens at the instant the fit
        /// becomes available. <b>It stops at 0.30 and not lower on purpose.</b> 0.18 was tried
        /// first and computes to 7 px at BoardZoom 2 on island 0 — barely more than the ~6 px rim
        /// this whole change exists to replace, i.e. it would have reintroduced the original
        /// defect in the one state that most needs to be seen. 0.30 is about 12 px against rung
        /// 2's 22: unmistakably tighter, and still twice the hairline.</summary>
        public const float SeatedBleed = 0.30f;

        /// <summary>Hotter, more saturated and brighter than <see cref="SnapGold"/>, which the
        /// rim still uses — so at rung 3 the paper carries a hot core and a steady rim together.
        /// Rung 2's <see cref="HaloColour"/> is the same hue at half the saturation; the two
        /// states differ in motion, in width and in colour, which is three channels rather than
        /// the one the playtest could not see.</summary>
        public static readonly Color SeatedColour = Rgb(0xFF, 0xB4, 0x3C);

        /// <summary>Peak alpha of the seated halo's innermost ring. Higher than the 0.45 the
        /// retired rung-2 halo used, and packed into a third of the width, so the band reads as
        /// solid rather than as a gradient — a gradient is what "nearly" looks like. The rings
        /// composite, so raising this raises the whole ramp much faster than it looks like it
        /// should: five rings at 0.45 already stack to about 0.81 at the paper's edge.</summary>
        public const float SeatedAlphaPeak = 0.55f;

        // ---- the ghost slot (the assist, after G7.1 was superseded) ----
        //
        // ⟨proposed⟩, every one of them, and on stronger terms than anything above: NO MOCKUP
        // COVERS ANY OF THIS. 1c-snap-moment.png shows a steady rim on paper and nothing that
        // marks an empty place on the board, so the first playtest is the authority and these
        // four numbers are a starting position and not a measurement. Consts here rather than
        // fields on TableOptions for the class comment's reason — look values, one file, one
        // commit.
        //
        // WHAT THIS IS FOR. The assist used to be feedback only (G7.1): the halo lit at
        // GlowingHintRange — 19.03 board units, ≈750 px at BoardZoom 2 on island 0 — and the
        // release only fused inside reach, 1.54 units, ≈61 px. The player was shown a
        // relationship across a radius twelve times wider than the one in which letting go did
        // anything, and the playtest reported it as broken. G7.1 is now superseded: with the
        // assist on, releasing joins wherever the ghost is showing. The ghost is what makes
        // that legible — it is the place the paper will land, drawn before the player commits.
        //
        // A SLOT, NEVER A COPY OF THE MAP. Four thin bars on the sheet's own footprint: an
        // empty rectangle, so it reads as somewhere to put paper rather than as paper. The
        // alternative — a low-alpha copy of the slab, mesh and texture — was rejected on sight
        // and would have been cheaper: a translucent map at the target pose is a second sheet
        // on the board, and the board's whole grammar is that a sheet on the board is a sheet
        // the player has laid. A filled quad with no texture was the other option and is what
        // the bars beat: a fill has to be dark enough to see, and anything dark enough to see
        // over the mounting sheet stops looking empty.

        /// <summary>Thickness of the ghost's four bars, <b>in board units</b> — the same
        /// reasoning <see cref="SeatedBleed"/> gives, and for the same reason it is not a scale
        /// factor: a scale multiplies each axis by its own length, so one constant would draw a
        /// line 1.5× thicker along a Land Survey slab's long side than its short one, and a
        /// different thickness again on every other survey. A width in board units is the same
        /// width all the way round every sheet, which is what a drawn line is.
        ///
        /// <para>0.20 units is <b>7.87 px at <c>BoardZoom</c> 2 on island 0</b> (39.34 px per
        /// board unit). The bars straddle the sheet's true edge, half in and half out, so the
        /// ghost bleeds 0.10 units — 3.93 px — beyond the footprint the paper will occupy and
        /// cannot be mistaken for a sheet lying slightly proud of its place.</para>
        ///
        /// <para><b>Why not the ~5 px the rim was.</b> That hairline is the thing this whole
        /// family of changes exists to replace (see the halo block above). 7.87 px is a line a
        /// player can follow round a corner; it is deliberately thinner than the seated halo's
        /// 11.8 px, because the halo is light on paper and this is a line on the table.</para>
        /// </summary>
        public const float GhostLineWidth = 0.20f;

        /// <summary>Ceiling on <see cref="GhostLineWidth"/>, as a fraction of the slab's
        /// <i>half</i> short side, so a sheet smaller than any this project has produced cannot
        /// be drawn as four bars meeting in the middle. It does not bind on island 0: the
        /// tightest slab that can ever carry a ghost is the 4.25-unit Hydrographic short side,
        /// which allows 0.425 against the 0.20 asked for. (An Antiquarian detail sheet is
        /// tighter still at 2.75 units — allowing 0.275, which also clears — but §6 says detail
        /// sheets can never group, so one never gets a ghost at all.) A guard rail, not a
        /// tuning.</summary>
        public const float GhostLineMaxFraction = 0.20f;

        /// <summary>Peak alpha of the ghost's bars, before G7.5's pulse scales them. The bars
        /// do not composite — unlike the halo's five nested fills, each one is drawn once — so
        /// this is the alpha you see, and it is high because a pale gold line over the dark
        /// wood of the board at 0.45 is not a line, it is a rumour. G7.5's envelope takes it
        /// down to 0.12 at the trough of every cycle, which is the faintest the ghost ever
        /// gets.</summary>
        public const float GhostAlphaPeak = 0.80f;

        /// <summary>Where the ghost sits under the dragged slab, as a fraction of
        /// <c>TableOptions.SheetSeparation</c> — <b>the dragged slab's own slot in §3.3's
        /// stack</b>, below the halo's 0.30–0.70 and above the slot's floor at 1.0, so the
        /// ghost, the halo and the selection rim each own a band and none of the three can
        /// z-fight another.
        ///
        /// <para>Being in the <i>dragged</i> slab's slot is what makes the ghost visible at
        /// all. <c>BoardInteractor.Lift</c> puts a dragged sheet two whole separations above
        /// the top of the resting stack, so a quad 0.85 of a separation under it still floats
        /// above every sheet on the table and cannot be buried by the paper it is pointing
        /// between. It is still <i>under</i> the dragged slab, which is deliberate: as the
        /// sheet arrives the opaque paper covers the slot, and the target stops being drawn at
        /// the moment it stops being needed.</para></summary>
        public const float GhostDrop = 0.85f;

        // ---- footer ----

        public const float FooterHeight = 92f;
        public const float FooterPadBottom = 18f;
        public const float FooterLineHeight = 20f;
        public const float FooterSize = 11;

        /// <summary>The three lines of <c>1b-empty-table.png</c>, verbatim. They describe verbs
        /// slice S4 has not built yet; they are drawn anyway because the mockup is the authority
        /// on look and a footer that fills in later would change the column's height later.
        /// Order is bottom-up in the mockup and top-down here.</summary>
        public static readonly string[] FooterHints =
        {
            "Drag a sheet onto the table",
            "Click to select · corner handle rotates",
            "Drag back to the cabinet to refile"
        };

        // ---- odds ----

        public const float HairlineWidth = 1f;

        /// <summary>What a row shows when the board cannot resolve its sheet. C7.7d says every
        /// sheet has a name, and it does — but a lookup can still miss (a stale ledger entry, an
        /// island mid-regeneration), and a dash is a quieter failure than a blank row or a
        /// second copy of the code sitting where the name should be.</summary>
        public const string UnknownName = "—";

        /// <summary>Expanded / collapsed markers. Filled triangles rather than the hairline
        /// chevrons of the mockup: the outline glyphs are not in the built-in font, and a
        /// hollow-box fallback next to a section title reads as a bug.</summary>
        public const string ChevronOpen = "▼";
        public const string ChevronClosed = "►";

        // --------------------------------------------------------------------

        static Font serif;
        static Font sans;

        /// <summary>
        /// A serif face for titles, borrowed from the OS. Asked for by a list of names so a Mac,
        /// a Windows box and a Linux CI machine each get the nearest thing they have; falls back
        /// to the built-in sans, which is wrong but legible, rather than to nothing, which is a
        /// screen of invisible text. No font asset is added — that is the constraint this
        /// satisfies.
        /// </summary>
        public static Font Serif()
        {
            if (serif != null) return serif;

            serif = Font.CreateDynamicFontFromOSFont(
                new[] { "Georgia", "Times New Roman", "Palatino", "Palatino Linotype",
                        "Book Antiqua", "DejaVu Serif", "Liberation Serif", "Serif" }, 32);

            if (serif == null) serif = Sans();
            return serif;
        }

        /// <summary>The built-in face, as <c>RoomBuilder.BuiltinFont</c> resolves it. Used for
        /// labels, codes, counts and hints — everything set in faked small caps, where the
        /// spacing does more work than the letterform.</summary>
        public static Font Sans()
        {
            if (sans != null) return sans;

            sans = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (sans == null) sans = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return sans;
        }

        /// <summary>
        /// Letter-spaced small caps, faked. Legacy <see cref="Text"/> has no tracking, so a
        /// space goes between every character and the string is upper-cased with the invariant
        /// culture — invariant because a code like <c>CH·01</c> is an identifier and a Turkish
        /// locale must not render it differently from an English one, which is the same reason
        /// <see cref="SheetNaming.CodeFor"/> formats its digits invariantly.
        /// </summary>
        public static string Spaced(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            string upper = text.ToUpper(CultureInfo.InvariantCulture);
            var sb = new System.Text.StringBuilder(upper.Length * 2);

            for (int i = 0; i < upper.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(upper[i]);
            }
            return sb.ToString();
        }

        // ---- small builders, so no behaviour has to spell out RectTransform maths ----

        public static Color Rgb(int r, int g, int b)
        {
            return new Color(r / 255f, g / 255f, b / 255f, 1f);
        }

        /// <summary>Anchors a rect to fill its parent exactly.</summary>
        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>Pulls an already-stretched rect in by <paramref name="amount"/> on all
        /// sides. How a 1 px border is drawn: a plate, and a fill one hairline smaller.</summary>
        public static void Inset(RectTransform rt, float amount)
        {
            rt.offsetMin = new Vector2(amount, amount);
            rt.offsetMax = new Vector2(-amount, -amount);
        }

        /// <summary>A flat colour filling its parent.</summary>
        public static Image Plate(RectTransform parent, string name, Color colour)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var image = go.AddComponent<Image>();
            image.color = colour;
            image.raycastTarget = false;
            Stretch(image.rectTransform);
            return image;
        }

        /// <summary>A 1 px line. <paramref name="anchorMin"/>/<paramref name="anchorMax"/> pick
        /// which edge it hugs.</summary>
        public static Image Hairline(RectTransform parent, string name, Color colour,
                                     Vector2 anchorMin, Vector2 anchorMax, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var image = go.AddComponent<Image>();
            image.color = colour;
            image.raycastTarget = false;

            var rt = image.rectTransform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.sizeDelta = size;
            return image;
        }

        /// <summary>A non-wrapping, non-raycasting text. Overflow rather than wrap, everywhere:
        /// a sheet name that wraps changes the row's height and the accordion below it jumps.
        /// A long name is clipped by the column mask instead, which is the failure the player
        /// can shrug at.</summary>
        public static Text Label(RectTransform parent, string name, string content,
                                 Font font, float size, Color colour)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var text = go.AddComponent<Text>();
            text.font = font;
            text.fontSize = Mathf.RoundToInt(size);
            text.color = colour;
            text.text = content;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>Places a text block a fixed distance from its parent's left edge, centred on
        /// <paramref name="offsetY"/> about the parent's middle, stretched to the right edge
        /// less <paramref name="padRight"/>.</summary>
        public static void LeftBlock(RectTransform rt, float left, float offsetY,
                                     float height, float padRight)
        {
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, offsetY - height * 0.5f);
            rt.offsetMax = new Vector2(-padRight, offsetY + height * 0.5f);
        }

        /// <summary>A vertical stack that sizes itself to its children — the accordion's
        /// content, a section, a section's rows.</summary>
        public static VerticalLayoutGroup Stack(GameObject go, float spacing,
                                                RectOffset padding = null)
        {
            var group = go.AddComponent<VerticalLayoutGroup>();
            group.spacing = spacing;
            group.padding = padding != null ? padding : new RectOffset(0, 0, 0, 0);
            group.childAlignment = TextAnchor.UpperLeft;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;
            return group;
        }
    }

    // ========================================================================

    /// <summary>
    /// The right-hand column: an accordion of one collapsible section per office, over a footer
    /// of hints. Slice S3, and read-only — this is a picture of the ledger, not a way to move
    /// paper.
    ///
    /// <para><b>Sections are offices, in <c>Offices.All</c> order (C7.1)</b>, and an office that
    /// has issued nothing is <i>not drawn</i> — not drawn empty, not drawn greyed. That is the
    /// second idea of the game showing up in the UI: the cabinet lists what the archive
    /// <i>holds</i>, never what exists. A greyed-out "Garrison (0)" would tell the player there
    /// is a Garrison survey out there to be got, which is precisely the answer the game is
    /// about not giving.</para>
    ///
    /// <para><b>Counts, no fractions, no ticks (C7.2, D-C3, D-C4).</b> The number beside a
    /// section title is how many sheets are in it — an inventory, not a grade. It must never
    /// become "3 / 7": the denominator would leak how many sheets the survey actually has, and
    /// R5.5 forbids the scoreboard that a fraction turns the cabinet into. When every sheet in a
    /// section is out on the table the count is replaced by the table mark and the header tints
    /// gold, per the <c>2a-cabinet-states.png</c> legend — a statement about where the paper is,
    /// which is recoverable by picking it up, not about whether the player has done well.</para>
    ///
    /// <para><b>Names come from the island, not from here (C7.7a).</b> The panel is handed an
    /// <see cref="Island"/> and asks <see cref="SheetNaming"/>. It does not generate, cache or
    /// invent a name, because a name is a fact about the island's paperwork and has to be the
    /// same for a headless test as for this column.</para>
    ///
    /// <para><b>Rebuild is coarse on purpose.</b> When the set of available sheets changes the
    /// whole accordion is thrown away and built again; only thumbnails and row states are
    /// updated in place. A cabinet is a few dozen rows and changes when a folder is laid down —
    /// seconds apart, not frames — so an incremental diff would be more code than it saves and
    /// would have its own bugs. Collapse state is carried across a rebuild by office, so the
    /// section the player closed stays closed.</para>
    ///
    /// <para><b>A conduit for row events, and nothing more.</b> Every row event is re-raised
    /// unchanged. The panel deliberately holds no opinion about what a click or a drop means: it
    /// is torn down and rebuilt by the very changes those gestures cause, so a decision taken
    /// here would be taken by an object about to be destroyed. The panel does own one fact
    /// nobody else can see — whether the pointer is inside the column
    /// (<see cref="PointerOverChanged"/>) — because that is a fact about <i>this</i>
    /// rectangle.</para>
    ///
    /// <para><b>The Groups section (G6.1) is a fifth section, after the offices and before the
    /// footer</b>, listing every group of the bound island — on the table and parked alike,
    /// marked by state exactly as an office row is. <b>It is drawn even when it is empty</b>,
    /// which looks like a contradiction of C7.1's "a section with no issued sheets is not drawn"
    /// and is not. C7.1 suppresses an empty office because an office is an <i>island</i> fact: a
    /// greyed "Garrison (0)" would tell the player there is a Garrison survey out there to be
    /// got, which is precisely the answer the game is about not giving. A group is a
    /// <b>player</b> fact — it exists because two sheets were laid correctly — so an empty
    /// Groups section leaks nothing at all; it says only that nothing has been joined yet, which
    /// the player already knows. G6.1's own wording settles it: the section "starts empty
    /// because no groups exist yet, not because it only holds parked ones", and a section that
    /// is not drawn cannot start empty.</para>
    ///
    /// <para><b>A grouped sheet keeps its office row (G6.2)</b>, marked and inert. Rejected:
    /// moving grouped sheets out of their office section and into the Groups one. It makes the
    /// office count read as "what is still separate", which is a different and less useful fact
    /// than "what this office issued" — the count is an inventory (C7.2, D-C3) and an inventory
    /// that shrinks when paper is rearranged is not one. It also makes a sheet vanish from where
    /// the player last saw it, which is the failure C7.5's "dragging a slab onto the cabinet
    /// returns it to the drawer" exists to avoid at the other end of the gesture.</para>
    ///
    /// <para><b>"n of N" counts what the archive HOLDS, not what the survey shipped — a
    /// deliberate departure from G6.3's wording.</b> G6.3 asks for "members present, sheets in
    /// the survey", and the second number is unavailable to this column on the settled reading of
    /// R5.5. D-C3 permits the section counts at all only on this ground: <i>"a count of sheets
    /// held is inventory, not a grade — and because the accordion lists only issued sheets, it
    /// never reveals how many the survey actually has"</i>, and <c>LedgerSheetSource</c> states
    /// the same invariant from the other side: <i>"the ledger only knows what has come out of
    /// the crates, which is why the cabinet's counts (C7.2) never reveal how large the survey
    /// really is"</i>. <c>Survey.SheetCount</c> is exactly the number both of those refuse, and
    /// putting it on a row would make "2 of 9" tell a player holding five Land Survey sheets
    /// that four more exist — the leak D-C4 dropped the ✓ to prevent, restored in a denominator.
    /// So N is the count of that survey's sheets in <see cref="Available"/>, which is the same
    /// number the section header above already shows, and "n of N" then means <i>this much of
    /// what you have is assembled</i> — a fact the player can act on rather than a score against
    /// an unknown total. <b>This is recorded, not silently fixed</b> (CLAUDE.md): G6.3 is marked
    /// ⟨proposed⟩ and has no mockup, D-C3/D-C4 are settled against a mockup and a numbered
    /// requirement, and where they disagree the earlier and better-evidenced one governs. If the
    /// project owner wants the island total, it is one expression in
    /// <see cref="HeldOfSurvey"/> — and G9.1's <c>complete(group)</c> is where that number
    /// legitimately belongs, since it is consumed by nothing and shown to nobody.</para>
    ///
    /// <para><b>Collapse survives a <see cref="Clear"/>, and so does the Groups section's.</b>
    /// The office flags already do — "a player who closed POIs meant it" — and the same sentence
    /// is true of a player who closed Groups. It is a preference about a section of a column,
    /// not a fact about a board: the groups themselves live in <c>BoardStore</c> and are none of
    /// this panel's business. <b>The section does not open itself when the first group
    /// appears</b>, tempting though it is: nothing else in the accordion moves on its own, and a
    /// section that re-opens after being closed teaches the player that closing it does not
    /// stick.</para>
    /// </summary>
    public sealed class CabinetPanel : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        sealed class Section
        {
            public Office Office;

            /// <summary>True for the one Groups section (G6.1), in which case
            /// <see cref="Office"/> is meaningless — a group's office is a property of the
            /// group, and a section holding two surveys' groups has none of its own.</summary>
            public bool IsGroups;

            public GameObject Root;
            public GameObject Rows;
            public Image HeaderPlate;
            public Text Chevron;
            public Text Title;
            public Text Count;
            public GameObject Mark;
            public readonly List<CabinetRow> RowList = new List<CabinetRow>();
        }

        /// <summary>
        /// What the Groups section was built from, per group — the whole of what a Groups row
        /// draws, so that <see cref="Refresh"/> can tell "nothing changed" from "rebuild me"
        /// without holding the records.
        ///
        /// <para>The frame is deliberately absent. Dragging a group edits exactly one frame
        /// (G5.4) and fires <c>Changed</c> on every pointer move; no row shows a pose, so
        /// including it would rebuild the whole accordion at pointer speed to redraw nothing.
        /// What a row does show — which groups exist, how many members each has, and where each
        /// one is — is precisely these three fields.</para>
        /// </summary>
        readonly struct GroupStamp
        {
            public readonly int GroupId;
            public readonly int MemberCount;
            public readonly bool OnTable;

            public GroupStamp(int groupId, int memberCount, bool onTable)
            {
                GroupId = groupId;
                MemberCount = memberCount;
                OnTable = onTable;
            }

            public bool Matches(GroupRecord group)
            {
                return GroupId == group.GroupId
                    && MemberCount == group.MemberCount
                    && OnTable == group.OnTable;
            }
        }

        readonly List<Section> sections = new List<Section>();
        readonly Dictionary<Office, bool> collapsed = new Dictionary<Office, bool>();
        readonly List<SheetId> built = new List<SheetId>();
        readonly List<GroupStamp> builtGroups = new List<GroupStamp>();

        /// <summary>The Groups section's own collapse flag. Not in <see cref="collapsed"/>
        /// because that is keyed by <see cref="Office"/> and the Groups section has none;
        /// keeping it as a field rather than inventing a nullable key is what makes the absence
        /// of an office a compile-time fact instead of a convention.</summary>
        bool groupsCollapsed;

        /// <summary>The group under the pointer, or 0 (G6.3). Panel state, not row state: a row
        /// knows which group it belongs to but cannot know which one is hovered, and the answer
        /// has to be the same for every row of the accordion at once.</summary>
        int hoveredGroup;

        RectTransform content;
        Island island;
        BoardView board;

        /// <summary>Forwarded from every row — see <see cref="CabinetRow.Clicked"/>. C7.6: a
        /// click on a row selects its sheet.</summary>
        public event Action<SheetId> RowClicked;

        /// <summary>Forwarded from every row — see <see cref="CabinetRow.DragStarted"/>.</summary>
        public event Action<SheetId> DragStarted;

        /// <summary>Forwarded from every row — see <see cref="CabinetRow.Dragging"/>.</summary>
        public event Action<SheetId, PointerEventData> Dragging;

        /// <summary>Forwarded from every row — see <see cref="CabinetRow.DragEnded"/>.</summary>
        public event Action<SheetId, PointerEventData> DragEnded;

        /// <summary>
        /// The four above, keyed by group id instead of by <see cref="SheetId"/> — raised by the
        /// Groups section's rows and by nothing else. A row is either a sheet's or a group's, so
        /// exactly one of the two families fires for any gesture and no listener has to
        /// disambiguate.
        ///
        /// <para><b>They carry an <c>int</c>, not a <c>GroupRecord</c>.</b> The record is a
        /// value copied out of the store when the accordion was built, and the accordion is
        /// rebuilt by the very changes these gestures cause — so a listener handed one would be
        /// holding a snapshot of a group that may already have grown. The id is the durable
        /// half: ids are never reused (<c>BoardStore</c>), so a stale one fails loudly at
        /// <c>TryGetGroup</c> instead of quietly naming somebody else.</para>
        ///
        /// <para><b>Nothing in this slice raises them into any behaviour.</b> Park and retrieve
        /// are G6.4 and G6.5 and belong to slice S5, in <c>BoardInteractor</c> and
        /// <c>TableCanvas</c> — files this slice does not own. They exist now so that S5 is a
        /// wiring change and not a change to this class.</para>
        /// </summary>
        public event Action<int> GroupRowClicked;

        /// <summary>See <see cref="GroupRowClicked"/>. Raised only for a group that is parked —
        /// a group already on the table refuses the drag, exactly as an on-table sheet row
        /// does (C7.4).</summary>
        public event Action<int> GroupDragStarted;

        /// <summary>See <see cref="GroupRowClicked"/>.</summary>
        public event Action<int, PointerEventData> GroupDragging;

        /// <summary>See <see cref="GroupRowClicked"/>. This is the one S5 wires to G6.5:
        /// released over the composition area, the group is laid back down under the pointer
        /// preserving its frame rotation φ; released inside the cabinet, nothing happens.</summary>
        public event Action<int, PointerEventData> GroupDragEnded;

        /// <summary>
        /// True when the pointer enters the column, false when it leaves. C7.5's second
        /// sentence — a slab dragged back onto the cabinet is refiled — needs this to be true of
        /// the pointer at any moment, not only while a <i>row</i> is being dragged, because the
        /// gesture it describes starts on the board and this panel never hears about it.
        ///
        /// <para>Enter and exit are raised for the whole subtree: the event system enters every
        /// ancestor of whatever it hit, so a row, a section header and the bare cream between
        /// them all read as "in the cabinet", and moving between them raises nothing. Only
        /// crossing the column's edge fires.</para>
        /// </summary>
        public event Action<bool> PointerOverChanged;

        /// <summary>
        /// The column's rectangle, for the one caller that must answer "board or cabinet?" about
        /// a screen point. Exposed rather than answered here on purpose: the question is asked
        /// about a drop, and a drop is a decision, and this panel takes none.
        /// </summary>
        public RectTransform Rect { get { return (RectTransform)transform; } }

        // --------------------------------------------------------------------

        /// <summary>
        /// Builds the column under <paramref name="parent"/> — plate, edge rule, scroll view and
        /// footer — and returns it empty. <see cref="Bind"/> fills it.
        ///
        /// <para>A <see cref="ScrollRect"/> with no visible bar: the mockups show none, because
        /// the islands they were drawn from fit. A real survey need not, and a cabinet that
        /// silently hides its last three sheets is worse than a scrollbar the player never
        /// sees.</para>
        /// </summary>
        public static CabinetPanel Create(RectTransform parent)
        {
            var go = new GameObject("Cabinet", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var panel = go.AddComponent<CabinetPanel>();
            panel.Build();
            return panel;
        }

        void Build()
        {
            var rt = (RectTransform)transform;
            rt.anchorMin = new Vector2(1f - CabinetStyle.CabinetWidthFraction, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = new Vector2(0f, -CabinetStyle.HeaderHeight);

            var plate = CabinetStyle.Plate(rt, "Plate", CabinetStyle.PanelCream);
            plate.raycastTarget = true;      // the column swallows clicks meant for the board

            CabinetStyle.Hairline(rt, "EdgeRule", CabinetStyle.Rule,
                                  new Vector2(0f, 0f), new Vector2(0f, 1f),
                                  new Vector2(CabinetStyle.HairlineWidth, 0f));

            // Scroll view: root → viewport (masked) → content (a self-sizing stack).
            var scrollGo = new GameObject("Scroll", typeof(RectTransform));
            scrollGo.transform.SetParent(rt, false);
            var scrollRt = (RectTransform)scrollGo.transform;
            CabinetStyle.Stretch(scrollRt);
            scrollRt.offsetMin = new Vector2(0f, CabinetStyle.FooterHeight);
            scrollRt.offsetMax = new Vector2(0f, -CabinetStyle.CabinetPadTop);

            var viewportGo = new GameObject("Viewport", typeof(RectTransform));
            viewportGo.transform.SetParent(scrollRt, false);
            var viewport = (RectTransform)viewportGo.transform;
            CabinetStyle.Stretch(viewport);
            viewportGo.AddComponent<RectMask2D>();     // no Image needed, unlike Mask

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(viewport, false);
            content = (RectTransform)contentGo.transform;
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;

            CabinetStyle.Stack(contentGo, CabinetStyle.SectionSpacing,
                               new RectOffset((int)CabinetStyle.CabinetPadX,
                                              (int)CabinetStyle.CabinetPadX, 0, 0));

            var fitter = contentGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            BuildFooter(rt);
        }

        void BuildFooter(RectTransform parent)
        {
            var go = new GameObject("Footer", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = new Vector2(0f, CabinetStyle.FooterHeight);

            string[] hints = CabinetStyle.FooterHints;
            for (int i = 0; i < hints.Length; i++)
            {
                var line = CabinetStyle.Label(rt, "Hint" + i, CabinetStyle.Spaced(hints[i]),
                                              CabinetStyle.Sans(), CabinetStyle.FooterSize,
                                              CabinetStyle.Muted);

                // Bottom-up, so the last hint sits on the bottom margin exactly as in
                // 1b-empty-table.png and the block grows upward if a fourth is ever added.
                float y = CabinetStyle.FooterPadBottom
                        + (hints.Length - 1 - i) * CabinetStyle.FooterLineHeight;

                var lineRt = line.rectTransform;
                lineRt.anchorMin = new Vector2(0f, 0f);
                lineRt.anchorMax = new Vector2(1f, 0f);
                lineRt.pivot = new Vector2(0.5f, 0f);
                lineRt.offsetMin = new Vector2(CabinetStyle.CabinetPadX, y);
                lineRt.offsetMax = new Vector2(-CabinetStyle.CabinetPadX,
                                               y + CabinetStyle.FooterLineHeight);
            }
        }

        // --------------------------------------------------------------------

        /// <summary>
        /// Points the panel at one island and one board and rebuilds. <paramref name="island"/>
        /// may be null — the panel then draws rows with <see cref="CabinetStyle.UnknownName"/>
        /// rather than throwing, because a cabinet of dashes is a bug anyone can see whereas an
        /// exception thrown while building a row takes the whole table view down. That is the
        /// same call <see cref="SheetNaming.NameFor"/> and <c>SheetNaming.PrefixFor</c> already
        /// made for themselves.
        /// </summary>
        public void Bind(Island island, BoardView board)
        {
            this.island = island;
            this.board = board;
            Rebuild();
        }

        /// <summary>Empties the accordion and forgets its island. Collapse state survives — a
        /// player who closed POIs meant it, and a player who closed Groups meant that too (see
        /// the class comment). The hover does not survive: it names a row that no longer
        /// exists.</summary>
        public void Clear()
        {
            island = null;
            board = null;
            hoveredGroup = 0;
            Teardown();
            built.Clear();
            builtGroups.Clear();
        }

        /// <summary>
        /// Re-reads thumbnails and row states without touching the hierarchy, and rebuilds only
        /// if the set of available sheets, or the set of groups, has actually changed. Called on
        /// every <c>BoardView.Changed</c>, so it must be cheap and must never allocate a section.
        ///
        /// <para><b>Groups take the coarse path, deliberately.</b> A group appearing, growing,
        /// parking or being retrieved changes which rows exist and which are inert, so it
        /// rebuilds the whole accordion exactly as a new sheet does. Nothing incremental was
        /// written for it: the class comment's argument holds unchanged — a cabinet is a few
        /// dozen rows and changes when paper is put down, seconds apart, not frames — and a
        /// second, partial update path for groups would be more code than it saves and would
        /// have its own bugs. Only thumbnails, row states and group marks are updated in place.
        /// </para>
        /// </summary>
        public void Refresh()
        {
            if (board == null) return;

            if (AvailableChanged() || GroupsChanged()) { Rebuild(); return; }

            for (int s = 0; s < sections.Count; s++)
            {
                Section section = sections[s];
                int onTable = 0;

                for (int r = 0; r < section.RowList.Count; r++)
                {
                    CabinetRow row = section.RowList[r];

                    if (row.IsGroupRow)
                    {
                        // A group row's state is the group's, not any member's: a parked group's
                        // members are on no board at all, so asking IsOnTable about one of them
                        // would report the whole assembly as filed the instant it was parked —
                        // which is true of the paper and wrong about the row, since the row is
                        // the thing the player picks the assembly up by (G6.2).
                        GroupRecord group;
                        bool known = board.TryGetGroup(row.GroupId, out group);

                        // Re-asked here and not only at build, for the same reason a sheet row's
                        // is (C5.6). A group outlives an opening — BoardStore keeps it, the
                        // textures do not — so a board closed and reopened rebuilds this section
                        // in the frames before any raster has landed, and a thumbnail set once
                        // would stay a blank plate for the rest of the session.
                        if (known) row.SetThumbnail(board.TextureFor(LowestMember(group)));

                        bool laidOut = known && group.OnTable;
                        row.SetOnTable(laidOut);
                        if (laidOut) onTable++;
                        continue;
                    }

                    // C5.6: null is the normal answer for the first frames after an opening.
                    row.SetThumbnail(board.TextureFor(row.Id));

                    bool isOut = board.IsOnTable(row.Id);
                    row.SetOnTable(isOut);
                    row.SetGrouped(board.GroupIdOf(row.Id));
                    if (isOut) onTable++;
                }

                ApplyHeaderState(section, onTable);
            }
        }

        void OnDestroy() { board = null; }

        // --------------------------------------------------------------------

        bool AvailableChanged()
        {
            IReadOnlyList<SheetId> available = board.Available;
            if (available == null) return built.Count != 0;
            if (available.Count != built.Count) return true;

            for (int i = 0; i < available.Count; i++)
                if (!available[i].Equals(built[i])) return true;

            return false;
        }

        /// <summary>
        /// Whether the Groups section is out of date. Compared against
        /// <see cref="GroupStamp"/>s rather than against the records themselves: the records are
        /// fresh copies on every call and comparing them would mean comparing member lists,
        /// which is the one part of a group that is expensive and the one part a stamp can
        /// summarise exactly — membership is monotonic (G1.4), so a member list that has not
        /// changed length has not changed.
        /// </summary>
        bool GroupsChanged()
        {
            IReadOnlyList<GroupRecord> groups = board.Groups;
            if (groups == null) return builtGroups.Count != 0;
            if (groups.Count != builtGroups.Count) return true;

            for (int i = 0; i < groups.Count; i++)
                if (!builtGroups[i].Matches(groups[i])) return true;

            return false;
        }

        void Teardown()
        {
            for (int i = 0; i < sections.Count; i++)
            {
                Section section = sections[i];
                for (int r = 0; r < section.RowList.Count; r++)
                {
                    CabinetRow row = section.RowList[r];
                    row.Clicked -= OnRowClicked;
                    row.DragStarted -= OnRowDragStarted;
                    row.Dragging -= OnRowDragging;
                    row.DragEnded -= OnRowDragEnded;
                    row.HoverChanged -= OnRowHoverChanged;
                }

                if (section.Root != null) Destroy(section.Root);
            }
            sections.Clear();
        }

        void Rebuild()
        {
            Teardown();
            built.Clear();
            builtGroups.Clear();
            if (board == null) return;

            IReadOnlyList<SheetId> available = board.Available;
            if (available == null) return;
            for (int i = 0; i < available.Count; i++) built.Add(available[i]);

            IReadOnlyList<GroupRecord> groups = board.Groups;

            // Offices.All, never enum reflection (§4.1) — a fifth office must arrive as a
            // visible gap here, not be silently absent.
            Office[] offices = Offices.All;
            var forOffice = new List<SheetId>();

            for (int o = 0; o < offices.Length; o++)
            {
                Office office = offices[o];

                forOffice.Clear();
                for (int i = 0; i < built.Count; i++)
                    if (built[i].Office == office) forOffice.Add(built[i]);

                // C7.1 — a section with no issued sheets is not drawn at all.
                if (forOffice.Count == 0) continue;

                sections.Add(BuildSection(office, forOffice, sections.Count == 0));
            }

            // G6.1 — after every office section and before the footer, which is a sibling of the
            // scroll view rather than the last thing in it, so "before the footer" is simply
            // "last in the content stack". Drawn whether or not there are groups: see the class
            // comment on why C7.1's suppression does not carry over.
            sections.Add(BuildGroupsSection(groups, sections.Count == 0));

            Refresh();

            // After Refresh, because Refresh is what tells an office row which group it is in.
            // A rebuild can happen with the pointer sitting still on a row — laying a sheet
            // rebuilds the accordion under it — and the freshly built rows have no highlight
            // until they are told; without this the kin bar would go out under a motionless
            // pointer and only come back when it moved.
            ApplyKin();
        }

        Section BuildSection(Office office, List<SheetId> ids, bool first)
        {
            var section = new Section { Office = office };

            BuildSectionShell(section, "Section_" + office,
                              SheetNaming.OfficeTitleFor(office), first);

            for (int i = 0; i < ids.Count; i++)
            {
                SheetId id = ids[i];
                CabinetRow row = CabinetRow.Create((RectTransform)section.Rows.transform,
                                                   id, NameFor(id), SheetNaming.CodeFor(id));
                Adopt(section, row);
            }

            SetCollapsed(section, CollapsedOf(section));
            return section;
        }

        /// <summary>
        /// G6.1's section. One row per group, in the board's own order — which is creation order
        /// (<c>BoardStore.GroupsOn</c>), and is left alone rather than sorted by survey or by
        /// size. Creation order is the order the player made them in, so a group stays where the
        /// player last saw it and a new one always appears at the bottom; sorting would move
        /// rows the player is not touching, which is the failure the office sections avoid by
        /// listing in the source's order (§4.3).
        /// </summary>
        Section BuildGroupsSection(IReadOnlyList<GroupRecord> groups, bool first)
        {
            var section = new Section { IsGroups = true };

            BuildSectionShell(section, "Section_Groups", CabinetStyle.GroupsSectionTitle, first);

            int count = groups != null ? groups.Count : 0;
            for (int i = 0; i < count; i++)
            {
                GroupRecord group = groups[i];
                builtGroups.Add(new GroupStamp(group.GroupId, group.MemberCount, group.OnTable));

                SheetId lowest = LowestMember(group);
                CabinetRow row = CabinetRow.CreateGroup(
                    (RectTransform)section.Rows.transform, group.GroupId,
                    SurveyLabelFor(lowest),
                    SheetNaming.GroupCodeFor(lowest, group.MemberCount, HeldOfSurvey(group)));

                // The thumbnail is left to Refresh, which runs at the end of every rebuild — see
                // there. What is decided here is which thumbnail: the lowest member's, not a
                // composite. G6.3 asks for "the member textures composited at group scale, or
                // the first member's thumbnail until that exists", and this is that fallback,
                // taken deliberately — compositing needs a render target and a pass over the
                // group's frame, both of which belong to the board, and BoardView is not this
                // slice's file. The lowest member rather than the first-joined one so that the
                // picture and the code beside it name one sheet: a thumbnail of FN·07 over a
                // code reading FN·03 is worse than no thumbnail.
                Adopt(section, row);
            }

            SetCollapsed(section, CollapsedOf(section));
            return section;
        }

        /// <summary>Header, rows container and collapse behaviour — everything a section has
        /// before it has any rows. Shared so that the Groups section is the same piece of
        /// furniture as an office section rather than a lookalike built twice; G6.1 asks for it
        /// "marked by state exactly as office rows are", and the cheapest way to keep that
        /// promise is for there to be one implementation of the marking.</summary>
        void BuildSectionShell(Section section, string name, string title, bool first)
        {
            section.Root = new GameObject(name, typeof(RectTransform));
            section.Root.transform.SetParent(content, false);
            CabinetStyle.Stack(section.Root, 0f);

            BuildSectionHeader(section, title, first);

            section.Rows = new GameObject("Rows", typeof(RectTransform));
            section.Rows.transform.SetParent((RectTransform)section.Root.transform, false);
            CabinetStyle.Stack(section.Rows, CabinetStyle.RowSpacing,
                               new RectOffset(0, 0, 0, (int)CabinetStyle.RowSpacing * 2));
        }

        void Adopt(Section section, CabinetRow row)
        {
            row.Clicked += OnRowClicked;
            row.DragStarted += OnRowDragStarted;
            row.Dragging += OnRowDragging;
            row.DragEnded += OnRowDragEnded;
            row.HoverChanged += OnRowHoverChanged;
            section.RowList.Add(row);
        }

        void BuildSectionHeader(Section section, string title, bool first)
        {
            var go = new GameObject("Header", typeof(RectTransform));
            go.transform.SetParent((RectTransform)section.Root.transform, false);
            var rt = (RectTransform)go.transform;

            var element = go.AddComponent<LayoutElement>();
            element.minHeight = CabinetStyle.SectionHeaderHeight;
            element.preferredHeight = CabinetStyle.SectionHeaderHeight;

            section.HeaderPlate = CabinetStyle.Plate(rt, "Plate", Color.clear);
            section.HeaderPlate.raycastTarget = true;

            // A rule above every section but the first, as in 1b-empty-table.png. Not below:
            // a rule under the last section would draw a line across empty cream.
            if (!first)
                CabinetStyle.Hairline(rt, "TopRule", CabinetStyle.Rule,
                                      new Vector2(0f, 1f), new Vector2(1f, 1f),
                                      new Vector2(0f, CabinetStyle.HairlineWidth));

            section.Chevron = CabinetStyle.Label(rt, "Chevron", CabinetStyle.ChevronOpen,
                                                 CabinetStyle.Sans(), CabinetStyle.SectionCountSize,
                                                 CabinetStyle.Muted);
            CabinetStyle.LeftBlock(section.Chevron.rectTransform, 0f, 0f,
                                   CabinetStyle.SectionHeaderHeight, 0f);

            section.Title = CabinetStyle.Label(rt, "Title", title,
                                               CabinetStyle.Serif(), CabinetStyle.SectionTitleSize,
                                               CabinetStyle.Ink);
            CabinetStyle.LeftBlock(section.Title.rectTransform, CabinetStyle.ChevronWidth, 0f,
                                   CabinetStyle.SectionHeaderHeight, CabinetStyle.RowPadRight * 2f);

            section.Count = CabinetStyle.Label(rt, "Count", "",
                                               CabinetStyle.Sans(), CabinetStyle.SectionCountSize,
                                               CabinetStyle.Muted);
            section.Count.alignment = TextAnchor.MiddleRight;
            CabinetStyle.LeftBlock(section.Count.rectTransform, 0f, 0f,
                                   CabinetStyle.SectionHeaderHeight, CabinetStyle.RowPadRight);

            section.Mark = CabinetRow.BuildTableMark(rt, CabinetStyle.Gold);
            var markRt = (RectTransform)section.Mark.transform;
            markRt.anchorMin = markRt.anchorMax = new Vector2(1f, 0.5f);
            markRt.pivot = new Vector2(1f, 0.5f);
            markRt.anchoredPosition = new Vector2(-CabinetStyle.RowPadRight, 0f);
            section.Mark.SetActive(false);

            var button = go.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = section.HeaderPlate;

            Section captured = section;
            button.onClick.AddListener(() => Toggle(captured));
        }

        /// <summary>
        /// C7.2. The count is how many sheets the section holds; when every one of them is out
        /// on the table it is replaced by the table mark and the header goes gold
        /// (<c>2a-cabinet-states.png</c>). Never a fraction — see the class comment.
        ///
        /// <para><b>The Groups section obeys the same rule, one level up</b>: its count is how
        /// many groups exist, and when every one of them is out on the table it too goes gold
        /// and shows the mark. That is not a rule stretched to fit — "nothing left in the drawer
        /// for this section" is literally true of a Groups section whose groups are all laid
        /// out, since parking is what puts a group in the drawer (G6.4). An empty Groups section
        /// reads <c>0</c> rather than going gold, which the <c>RowList.Count &gt; 0</c> guard
        /// already ensured for offices and now earns its keep.</para>
        /// </summary>
        void ApplyHeaderState(Section section, int onTable)
        {
            bool all = section.RowList.Count > 0 && onTable == section.RowList.Count;

            section.HeaderPlate.color = all ? CabinetStyle.GoldTint : Color.clear;
            section.Title.color = all ? CabinetStyle.Gold : CabinetStyle.Ink;
            section.Chevron.color = all ? CabinetStyle.Gold : CabinetStyle.Muted;

            section.Count.text = all
                ? ""
                : section.RowList.Count.ToString(CultureInfo.InvariantCulture);

            section.Mark.SetActive(all);
        }

        void Toggle(Section section)
        {
            bool next = section.Rows != null && section.Rows.activeSelf;
            SetCollapsed(section, next);
        }

        void SetCollapsed(Section section, bool value)
        {
            if (section.IsGroups) groupsCollapsed = value;
            else collapsed[section.Office] = value;

            if (section.Rows != null) section.Rows.SetActive(!value);
            if (section.Chevron != null)
                section.Chevron.text = value ? CabinetStyle.ChevronClosed : CabinetStyle.ChevronOpen;
        }

        /// <summary>What this section's collapse flag was when it was last set, defaulting to
        /// open. Two stores rather than one, because <see cref="collapsed"/> is keyed by
        /// <see cref="Office"/> and the Groups section has none — see
        /// <see cref="groupsCollapsed"/>.</summary>
        bool CollapsedOf(Section section)
        {
            if (section.IsGroups) return groupsCollapsed;

            bool value;
            return collapsed.TryGetValue(section.Office, out value) && value;
        }

        // ---- row events, unpacked ------------------------------------------
        //
        // A row reports that it was touched and hands over itself; which of the two families of
        // events that becomes is decided here, once, on IsGroupRow. See CabinetRow's class
        // comment for why the row does not carry the key itself.

        void OnRowClicked(CabinetRow row)
        {
            if (row.IsGroupRow) { Raise(GroupRowClicked, row.GroupId); return; }

            var handler = RowClicked;
            if (handler != null) handler(row.Id);
        }

        void OnRowDragStarted(CabinetRow row)
        {
            if (row.IsGroupRow) { Raise(GroupDragStarted, row.GroupId); return; }

            var handler = DragStarted;
            if (handler != null) handler(row.Id);
        }

        void OnRowDragging(CabinetRow row, PointerEventData eventData)
        {
            if (row.IsGroupRow) { Raise(GroupDragging, row.GroupId, eventData); return; }

            var handler = Dragging;
            if (handler != null) handler(row.Id, eventData);
        }

        void OnRowDragEnded(CabinetRow row, PointerEventData eventData)
        {
            if (row.IsGroupRow) { Raise(GroupDragEnded, row.GroupId, eventData); return; }

            var handler = DragEnded;
            if (handler != null) handler(row.Id, eventData);
        }

        static void Raise(Action<int> handler, int groupId)
        {
            if (handler != null) handler(groupId);
        }

        static void Raise(Action<int, PointerEventData> handler, int groupId,
                          PointerEventData eventData)
        {
            if (handler != null) handler(groupId, eventData);
        }

        /// <summary>
        /// G6.3's cross-highlight. Hovering a Groups row lights that group's rows in the office
        /// sections above; hovering one of those lights the Groups row and the group's other
        /// members. One implementation serves both directions because a row carries the group it
        /// is about either way (<see cref="CabinetRow.GroupId"/>), so "and vice versa" costs
        /// nothing.
        ///
        /// <para><b>Exit is checked against the row that lit it</b> rather than clearing
        /// unconditionally. The event system raises enter on the new row before exit on the old
        /// one in some orderings, and a blind clear would then extinguish the highlight the new
        /// row had just lit — a flicker that only appears when the pointer moves between two
        /// rows of the <i>same</i> group, which is exactly the case this feature exists
        /// for.</para>
        ///
        /// <para>A loose row lights nothing, and a hover over one clears whatever was lit. That
        /// is what makes the highlight read as a property of the group rather than of the
        /// pointer.</para>
        /// </summary>
        void OnRowHoverChanged(CabinetRow row, bool entered)
        {
            int next;
            if (entered) next = row.GroupId;
            else if (row.GroupId == hoveredGroup) next = 0;
            else return;

            if (next == hoveredGroup) return;
            hoveredGroup = next;
            ApplyKin();
        }

        void ApplyKin()
        {
            for (int s = 0; s < sections.Count; s++)
            {
                List<CabinetRow> rows = sections[s].RowList;
                for (int r = 0; r < rows.Count; r++)
                    rows[r].SetKin(hoveredGroup != 0 && rows[r].GroupId == hoveredGroup);
            }
        }

        void IPointerEnterHandler.OnPointerEnter(PointerEventData eventData)
        {
            var handler = PointerOverChanged;
            if (handler != null) handler(true);
        }

        void IPointerExitHandler.OnPointerExit(PointerEventData eventData)
        {
            var handler = PointerOverChanged;
            if (handler != null) handler(false);
        }

        // --------------------------------------------------------------------

        /// <summary>
        /// The sheet's name, or a dash if the board cannot resolve it. Nothing is invented here
        /// and nothing may be (C7.7): a name is a function of the seed and belongs to
        /// <c>Archivist.Generation</c>.
        /// </summary>
        string NameFor(SheetId id)
        {
            if (island == null || board == null) return CabinetStyle.UnknownName;

            Sheet sheet;
            if (!board.TrySheet(id, out sheet)) return CabinetStyle.UnknownName;

            string name = SheetNaming.NameFor(island, sheet);
            return string.IsNullOrEmpty(name) ? CabinetStyle.UnknownName : name;
        }

        /// <summary>
        /// The survey a group belongs to, named and dated (G6.3), asked through one of its
        /// members because a <see cref="GroupRecord"/> carries the survey <i>key</i> — office and
        /// the whole-island flag (G3.4) — and not the year. That is the right shape for the
        /// record: the key is what a fit test compares, and it is deliberately two fields that
        /// can be checked "without touching the island". The year is a label, and a label is
        /// looked up when it is drawn.
        ///
        /// <para>Falls back to the office alone if the board cannot resolve the member, which is
        /// the same call <see cref="NameFor"/> makes and for the same reason: a row missing its
        /// year is a bug anyone can see, an exception thrown while building a row takes the
        /// whole table view down.</para>
        /// </summary>
        string SurveyLabelFor(SheetId member)
        {
            Sheet sheet;
            if (board != null && board.TrySheet(member, out sheet))
                return SheetNaming.SurveyLabelFor(sheet.Survey);

            return SheetNaming.OfficeTitleFor(member.Office);
        }

        /// <summary>
        /// The member with the lowest sheet number — the group's disambiguator (G6.3), its
        /// label's code and its thumbnail, so that all three name one sheet.
        ///
        /// <para>The lowest number rather than <c>Members[0]</c>, which is the first to join.
        /// Join order is real and load-bearing elsewhere (G5.6 draws a group's members in it),
        /// but it is a fact about the player's hands, and a row that renamed itself because a
        /// lower-numbered sheet joined later would be worse than one that renames itself because
        /// a lower-numbered sheet joined — that at least is the same rule stated once. Sheet
        /// numbers are contiguous <c>1..N</c> within a survey (<c>SheetLookup</c>), so the lowest
        /// is stable, meaningful and easy to find in the section above.</para>
        ///
        /// <para>Returns <c>default</c> for an empty group, which cannot exist —
        /// <c>BoardStore.CreateGroup</c> makes them with two members and membership never
        /// shrinks (G1.4) — but is answered rather than thrown on, so that a bug in the store
        /// costs a dashed row and not the table.</para>
        /// </summary>
        static SheetId LowestMember(GroupRecord group)
        {
            IReadOnlyList<SheetId> members = group.Members;
            if (members == null || members.Count == 0) return default(SheetId);

            SheetId best = members[0];
            for (int i = 1; i < members.Count; i++)
                if (members[i].Number < best.Number) best = members[i];

            return best;
        }

        /// <summary>
        /// How many sheets of this group's survey the archive <b>holds</b> — the denominator of
        /// G6.3's "n of N", and deliberately not <c>Survey.SheetCount</c>. The class comment
        /// argues that at length against D-C3, D-C4 and R5.5; the short form is that the
        /// accordion may count what it lists and may not count what exists.
        ///
        /// <para>Counted off <see cref="built"/>, which is <c>BoardView.Available</c> as of the
        /// last rebuild, so the denominator is by construction the same number the section
        /// header above the members is showing. Two counts of one inventory that could disagree
        /// would be worse than either.</para>
        /// </summary>
        int HeldOfSurvey(GroupRecord group)
        {
            int held = 0;
            for (int i = 0; i < built.Count; i++)
                if (group.SameSurvey(built[i])) held++;

            return held;
        }
    }
}
