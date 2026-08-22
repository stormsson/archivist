using Archivist.Generation;

namespace Archivist.Render
{
    /// <summary>
    /// §6.4 — band index to colour. One global palette for every island in this POC (T2.4),
    /// resolved through a per-island seam so seed-derived tints can arrive later without
    /// restructuring anything.
    ///
    /// <para>The colours are placeholder art direction: a plain hypsometric ramp, to be
    /// replaced wholesale. They are not a finding (§12's posture).</para>
    ///
    /// <para>Ordering matches <see cref="Bands"/> exactly — indices 0..3 sea, 4..11 land — so
    /// a band index is a direct lookup with no mapping table in between.</para>
    /// </summary>
    public static class Palette
    {
        /// <summary>
        /// §6.4. Length is <see cref="Bands.Count"/>; the order is the §6.3 band order.
        /// </summary>
        public static readonly Rgba[] Global =
        {
            // --- sea, deep -> foreshore (indices 0..3) ---
            Rgba.FromHex("16324f"),   //  0 deep
            Rgba.FromHex("22557d"),   //  1 offshore
            Rgba.FromHex("3f86ad"),   //  2 shallow
            Rgba.FromHex("7fb4cd"),   //  3 foreshore

            // --- land, shore -> summit (indices 4..11) ---
            Rgba.FromHex("e8ddc0"),   //  4 shore
            Rgba.FromHex("a9c07a"),   //  5 lowland
            Rgba.FromHex("8fb268"),   //  6 rising
            Rgba.FromHex("b4bd6e"),   //  7 mid
            Rgba.FromHex("cfc177"),   //  8 upper
            Rgba.FromHex("c9a86a"),   //  9 high
            Rgba.FromHex("b2895e"),   // 10 bare
            Rgba.FromHex("cfc4bb")    // 11 summit
        };

        /// <summary>
        /// The ONLY way anything obtains a palette (T2.4). Today it returns
        /// <see cref="Global"/> unchanged — one global palette for all islands is the POC's
        /// scope — but every caller already goes through the island, so seed-derived tints
        /// can be added here alone.
        ///
        /// <para>The stream name for that work is reserved now: <c>Streams.For(seed, "palette")</c>.
        /// §4.3 guarantees one stream per purpose drawn independently, so adding this stream
        /// later cannot reshuffle any feature that already exists — the character, radius,
        /// field, settlement, river or naming draws are all unaffected by a new named purpose.
        /// The door therefore stays open at zero cost and zero risk. Do not repurpose the name.
        /// Tinting is deliberately NOT implemented here yet.</para>
        /// </summary>
        /// <param name="island">The island being rendered; unused today, and the seam's point.</param>
        /// <returns>A palette of <see cref="Bands.Count"/> colours, indexed by band.</returns>
        public static Rgba[] ForIsland(Island island)
        {
            return Global;
        }
    }
}
