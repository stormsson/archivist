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
    /// </summary>
    public sealed class CabinetPanel : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        sealed class Section
        {
            public Office Office;
            public GameObject Root;
            public GameObject Rows;
            public Image HeaderPlate;
            public Text Chevron;
            public Text Title;
            public Text Count;
            public GameObject Mark;
            public readonly List<CabinetRow> RowList = new List<CabinetRow>();
        }

        readonly List<Section> sections = new List<Section>();
        readonly Dictionary<Office, bool> collapsed = new Dictionary<Office, bool>();
        readonly List<SheetId> built = new List<SheetId>();

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
        /// player who closed POIs meant it.</summary>
        public void Clear()
        {
            island = null;
            board = null;
            Teardown();
            built.Clear();
        }

        /// <summary>
        /// Re-reads thumbnails and row states without touching the hierarchy, and rebuilds only
        /// if the set of available sheets has actually changed. Called on every
        /// <c>BoardView.Changed</c>, so it must be cheap and must never allocate a section.
        /// </summary>
        public void Refresh()
        {
            if (board == null) return;

            if (AvailableChanged()) { Rebuild(); return; }

            for (int s = 0; s < sections.Count; s++)
            {
                Section section = sections[s];
                int onTable = 0;

                for (int r = 0; r < section.RowList.Count; r++)
                {
                    CabinetRow row = section.RowList[r];

                    // C5.6: null is the normal answer for the first frames after an opening.
                    row.SetThumbnail(board.TextureFor(row.Id));

                    bool isOut = board.IsOnTable(row.Id);
                    row.SetOnTable(isOut);
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
                }

                if (section.Root != null) Destroy(section.Root);
            }
            sections.Clear();
        }

        void Rebuild()
        {
            Teardown();
            built.Clear();
            if (board == null) return;

            IReadOnlyList<SheetId> available = board.Available;
            if (available == null) return;
            for (int i = 0; i < available.Count; i++) built.Add(available[i]);

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

            Refresh();
        }

        Section BuildSection(Office office, List<SheetId> ids, bool first)
        {
            var section = new Section { Office = office };

            section.Root = new GameObject("Section_" + office, typeof(RectTransform));
            section.Root.transform.SetParent(content, false);
            CabinetStyle.Stack(section.Root, 0f);

            BuildSectionHeader(section, first);

            section.Rows = new GameObject("Rows", typeof(RectTransform));
            section.Rows.transform.SetParent((RectTransform)section.Root.transform, false);
            CabinetStyle.Stack(section.Rows, CabinetStyle.RowSpacing,
                               new RectOffset(0, 0, 0, (int)CabinetStyle.RowSpacing * 2));

            for (int i = 0; i < ids.Count; i++)
            {
                SheetId id = ids[i];
                CabinetRow row = CabinetRow.Create((RectTransform)section.Rows.transform,
                                                   id, NameFor(id), SheetNaming.CodeFor(id));
                row.Clicked += OnRowClicked;
                row.DragStarted += OnRowDragStarted;
                row.Dragging += OnRowDragging;
                row.DragEnded += OnRowDragEnded;
                section.RowList.Add(row);
            }

            bool isCollapsed;
            if (!collapsed.TryGetValue(office, out isCollapsed)) isCollapsed = false;
            SetCollapsed(section, isCollapsed);

            return section;
        }

        void BuildSectionHeader(Section section, bool first)
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

            section.Title = CabinetStyle.Label(rt, "Title", TitleFor(section.Office),
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
            collapsed[section.Office] = value;

            if (section.Rows != null) section.Rows.SetActive(!value);
            if (section.Chevron != null)
                section.Chevron.text = value ? CabinetStyle.ChevronClosed : CabinetStyle.ChevronOpen;
        }

        void OnRowClicked(SheetId id)
        {
            var handler = RowClicked;
            if (handler != null) handler(id);
        }

        void OnRowDragStarted(SheetId id)
        {
            var handler = DragStarted;
            if (handler != null) handler(id);
        }

        void OnRowDragging(SheetId id, PointerEventData eventData)
        {
            var handler = Dragging;
            if (handler != null) handler(id, eventData);
        }

        void OnRowDragEnded(SheetId id, PointerEventData eventData)
        {
            var handler = DragEnded;
            if (handler != null) handler(id, eventData);
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
        /// The four labels of C7.1. A switch over <see cref="Office"/> rather than an array
        /// indexed by <c>(int)office</c>, and never a switch over the enum's <i>name</i>, for
        /// the reason <c>SheetNaming.PrefixFor</c> spells out: the enum is append-only, so a new
        /// office can only arrive as an unhandled case, and it should arrive visibly. The
        /// default draws the enum name rather than throwing — an odd word in a section header
        /// is a bug anyone can see and nobody loses a session to.
        /// </summary>
        static string TitleFor(Office office)
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
    }
}
