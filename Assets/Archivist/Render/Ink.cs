namespace Archivist.Render
{
    /// <summary>
    /// §7 — the palette-derived stroke ink, and the ONE place it is derived.
    ///
    /// <para><b>Why this is its own class.</b> Two code paths draw the coastline:
    /// <see cref="FieldCoast"/> from the fill's own h01 samples (the normal case) and
    /// <see cref="Strokes"/>'s vector contour fallback. They must lay down the SAME ink, or one
    /// island rendered with the fill and without it comes back in two colours. Written out twice,
    /// one copy rounding (<c>v * f + 0.5</c>) and the other truncating, the deep colour
    /// <c>16324f</c> = (22,50,79) at <c>f = 0.55</c> splits on green alone — 27.5 rounds one way
    /// and truncates the other, while 12.1 and 43.45 agree — and the truncating copy also dropped
    /// alpha. Do not re-inline this, and do not add a second rounding rule.</para>
    ///
    /// <para>Art direction is UNDEFINED for POC-02 — §6.4 calls even the fill palette a
    /// placeholder "to be replaced wholesale". These are placeholders too. RenderTuning.cs
    /// holds no colours and may not be edited, so the constants live here.</para>
    ///
    /// <para>Coast and river ink are derived from the palette where that is sensible: a
    /// darkened deep-sea colour for the coast, the shallow colour for rivers, so the overlay
    /// tracks any future re-tint of the fill (<see cref="Palette.ForIsland"/> is the seam for
    /// seed tints). The band indices are <see cref="Bands"/>' own — 0..3 sea, 4..11 land — and
    /// each lookup is guarded on <see cref="Bands.Count"/>, so a short or absent palette falls
    /// back to the constants below rather than throwing. The marks and soundings are NOT
    /// palette-derived — they must read as ink over whatever band they land on — so their
    /// colours stay private to <see cref="Strokes"/>, the only thing that draws them.</para>
    ///
    /// <para>Determinism (§5): every derivation here is a pure function of the palette, with
    /// no state and no transcendental, so it is byte-identical run to run.</para>
    /// </summary>
    public static class Ink
    {
        /// <summary>
        /// The unprinted sheet. Every plate starts as this and the ink goes on top (Q2.2).
        ///
        /// <para><b>Placeholder, and the seam is per office.</b> R2.6 makes paper stock the
        /// fastest signal a player has and Q2.6 makes it one of the few left, so this becomes a
        /// per-office value in W3. One tone until then, because a plate with no paper at all is
        /// a black rectangle — which is what turning <c>Fill</c> off produced before this
        /// existed.</para>
        /// </summary>
        public static readonly Rgba Paper = Rgba.FromHex("f2ece0");

        /// <summary>
        /// Drafting ink on paper: what every line is drawn in when there is no fill.
        ///
        /// <para><b>The palette derivations below are for ink over a FILL</b> — a darkened
        /// deep-sea colour reads as a pen on water, and does read as one there. On bare paper it
        /// is a dark blue line on cream, which is not a survey drawing; and a river taken from
        /// the shallow band is a pale blue that vanishes. Without a fill there are no bands to
        /// derive from and no water to sit on, so the ink is ink.</para>
        /// </summary>
        public static readonly Rgba Drafting = Rgba.FromHex("2e2318");

        /// <summary>Very dark blue-black — a survey pen on water. Reached only when the
        /// palette is too short to derive from.</summary>
        static readonly Rgba CoastFallback = Rgba.FromHex("0c1e2f");

        /// <summary>
        /// §6.4's `shallow`, so a river reads as water where it crosses land. Taken from the
        /// global palette rather than re-typing its hex, so a re-tint of the fill cannot leave
        /// a stale literal behind here. Safe to read in a field initialiser: <see cref="Palette"/>
        /// does not reference <see cref="Ink"/>, so there is no initialisation cycle, and the
        /// CLR runs a type's initialiser before any of its static fields is read.
        /// </summary>
        static readonly Rgba RiverFallback = Palette.Global[ShallowBandIndex];

        /// <summary>Multiplier taking the deep-sea colour down to a coastline pen.</summary>
        const double CoastDarken = 0.55;

        /// <summary>§6.3 band indices, matching <see cref="Bands"/> and <see cref="Palette"/>.</summary>
        const int DeepBandIndex = 0;
        const int ShallowBandIndex = 2;

        /// <summary>
        /// The coastline pen: the deep-sea band taken down by <see cref="CoastDarken"/>. Used
        /// by BOTH coast paths — <see cref="FieldCoast"/> and <see cref="Strokes"/>'s vector
        /// fallback — which is the whole point of it living here.
        /// </summary>
        /// <param name="palette">The fill palette (§6.4), or null.</param>
        public static Rgba CoastInk(Rgba[] palette)
        {
            if (palette == null || palette.Length < Bands.Count) return CoastFallback;
            return Darken(palette[DeepBandIndex], CoastDarken);
        }

        /// <summary>The coastline pen for a plate: <see cref="Drafting"/> without a fill, the
        /// palette derivation with one. One call, so no drawer has to remember which it is
        /// in.</summary>
        public static Rgba CoastInk(Rgba[] palette, bool hasFill)
        {
            return hasFill ? CoastInk(palette) : Drafting;
        }

        /// <summary>The river pen for a plate. See <see cref="Drafting"/> for why a river is not
        /// pale blue on paper.</summary>
        public static Rgba RiverInk(Rgba[] palette, bool hasFill)
        {
            return hasFill ? RiverInk(palette) : Drafting;
        }

        /// <summary>The shallow band, undarkened, so a river reads as water on land.</summary>
        /// <param name="palette">The fill palette (§6.4), or null.</param>
        public static Rgba RiverInk(Rgba[] palette)
        {
            if (palette == null || palette.Length < Bands.Count) return RiverFallback;
            return palette[ShallowBandIndex];
        }

        /// <summary>Scales the colour channels and leaves alpha alone — darkening a pen must
        /// not also make it transparent.</summary>
        static Rgba Darken(Rgba c, double factor)
        {
            return new Rgba(ScaleChannel(c.R, factor), ScaleChannel(c.G, factor),
                            ScaleChannel(c.B, factor), c.A);
        }

        /// <summary>
        /// Rounds rather than truncates — the <c>+ 0.5</c> is load-bearing, not decoration.
        /// It matches <see cref="Rgba.Lerp"/> and <see cref="Rgba.Over"/>, and truncating here
        /// instead is exactly the one-per-channel drift that split the two coast paths.
        /// </summary>
        static byte ScaleChannel(byte v, double factor)
        {
            double s = v * factor + 0.5;
            if (s <= 0.0) return 0;
            if (s >= 255.0) return 255;
            return (byte)s;
        }
    }
}
