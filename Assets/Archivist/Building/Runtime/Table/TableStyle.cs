using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace Archivist.Building.Table
{
    /// <summary>
    /// Every colour, size and spacing the table's chrome uses, in one place.
    ///
    /// <para><b>Why this is not in <see cref="TableOptions"/>.</b> That asset holds feel values
    /// settled by playing, which have to survive being edited in play mode. None of that is true
    /// of a hairline width. These are <i>look</i> values whose authority is the PNGs in
    /// <c>docs/UI/cartography_table/</c>, and as consts they change in one file, in one commit,
    /// against the mockup they are supposed to match — where an inspector field invites dragging
    /// the header cream three stops off it with no diff to review.</para>
    ///
    /// <para><b>Reference space is 1920 × 1080</b>, the same as the room's canvas in
    /// <c>SceneParts.BuildInteractionUi</c>. The mockups were rendered at 1442 wide, so every
    /// pixel measured off them is multiplied by about 1.33 before it lands here.</para>
    ///
    /// <para><b>The fonts are approximations and are meant to be.</b> No font assets may be
    /// added, so the serif is asked of the OS by name and the sans is Unity's built-in face.
    /// Letter-spaced small caps do not exist in legacy <see cref="Text"/>, so
    /// <see cref="Spaced"/> fakes them with a space between characters.</para>
    ///
    /// <para>Was <c>CabinetStyle</c>, inside the cabinet it was named for. The cabinet is gone
    /// (Q4.2) and the header outlived it; what went with the cabinet were the row, thumbnail,
    /// section, group and snap-hint values, and the column's width fraction — the board camera
    /// now has the whole screen.</para>
    /// </summary>
    public static class TableStyle
    {
        // ---- palette (measured off 1b-empty-table.png) ----

        /// <summary>The header band.</summary>
        public static readonly Color HeaderCream = Rgb(0xF7, 0xF1, 0xE6);

        /// <summary>Dark wood surround — the board camera's backdrop.</summary>
        public static readonly Color Wood = Rgb(0x2A, 0x1F, 0x16);

        /// <summary>Gold accent.</summary>
        public static readonly Color Gold = Rgb(0xB8, 0x86, 0x3B);

        /// <summary>Ink: the island name, titles.</summary>
        public static readonly Color Ink = Rgb(0x3A, 0x32, 0x29);

        /// <summary>The quiet tan of labels and codes — everything the player reads
        /// second.</summary>
        public static readonly Color Muted = Rgb(0xA9, 0x97, 0x81);

        /// <summary>Rules: under the header, between fields.</summary>
        public static readonly Color Rule = Rgb(0xE0, 0xD5, 0xC2);

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

        // ---- odds ----

        public const float HairlineWidth = 1f;

        /// <summary>What the header shows when the board cannot resolve a name. A dash is a
        /// quieter failure than a blank field.</summary>
        public const string UnknownName = "—";

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

        /// <summary>The built-in face, as <c>SceneParts.BuiltinFont</c> resolves it. Used for
        /// labels and codes — everything set in faked small caps, where the spacing does more
        /// work than the letterform.</summary>
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

        /// <summary>A non-wrapping, non-raycasting text. Overflow rather than wrap: a name that
        /// wraps changes its field's height and the row below it jumps. A long name is clipped
        /// instead, which is the failure the player can shrug at.</summary>
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
    }
}
