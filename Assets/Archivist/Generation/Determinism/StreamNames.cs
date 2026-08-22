namespace Archivist.Generation.Determinism
{
    /// <summary>
    /// The named PRNG streams (§4.3). Every <see cref="Streams.For(ulong,string,int)"/> call site
    /// takes its purpose from here rather than spelling a bare literal, because nothing else
    /// enforces the spelling: a typo in one of those strings silently regenerates every island in
    /// the collection and no test fails — the archive just quietly becomes a different archive.
    ///
    /// <para><b>These values are a reproducibility contract.</b> The string IS the seed material:
    /// <c>Streams.For</c> hashes it with FNV-1a, so changing a single character of a value re-rolls
    /// every island that draws from that stream. Only the seed is persisted (R1.1, R1.11), so a
    /// re-roll is not a refactor — it is a different world with the same seed written on it.
    /// Do not "normalise" a name, do not fix a capitalisation, do not re-spell
    /// <c>wholeIsland</c>. Rename the C# constant freely; never touch the literal beside it.</para>
    ///
    /// <para><b>Append-only.</b> New purposes are added at the end; existing ones are never
    /// changed, removed or repurposed. Adding a purpose is free precisely because §4.3 draws one
    /// stream per purpose independently — a new name cannot perturb an existing feature, which is
    /// what A2 asserts over 100 generated islands (§13.2).</para>
    ///
    /// <para><b>The dotted prefix is a convention, not a hierarchy.</b> <c>names.island</c> is one
    /// opaque string that happens to start with "names."; the code has no notion of a parent
    /// stream, and <c>names</c> and <c>names.island</c> are as unrelated as <c>radius</c> and
    /// <c>falloff</c>. The prefix is there so a reader can see which feature a sub-stream belongs
    /// to — it buys nothing at runtime and inherits nothing.</para>
    ///
    /// <para>The <c>index</c> argument of <c>Streams.For</c> is the other half of the contract:
    /// streams marked [index] below are drawn per element, so adding or losing an element cannot
    /// reshuffle the others.</para>
    /// </summary>
    public static class StreamNames
    {
        // --- island parameters (Field/IslandParams.cs) ---------------------------------
        /// <summary>Island character draw, Range(0, 3).</summary>
        public const string Character = "character";

        /// <summary>Nominal-radius jitter.</summary>
        public const string Radius = "radius";

        // --- height field (Field/IslandField.cs) ---------------------------------------
        /// <summary>Base composition noise seed.</summary>
        public const string Field = "field";

        /// <summary>Radial falloff noise seed.</summary>
        public const string Falloff = "falloff";

        // --- discrete features (Features/) ---------------------------------------------
        /// <summary>Settlement count (§7.2).</summary>
        public const string Settlements = "settlements";

        /// <summary>River jitter, indexed by peak (§7.3). [index = peakIndex]</summary>
        public const string Rivers = "rivers";

        /// <summary>
        /// Reserved for peak selection. No production call site draws from it today; the
        /// determinism suite uses it as a second live-looking name when asserting that named
        /// streams are independent of call order.
        /// </summary>
        public const string Peaks = "peaks";

        /// <summary>POI cap (§1.3 step 4).</summary>
        public const string Poi = "poi";

        /// <summary>Per-island order in which POI kinds get first refusal.</summary>
        public const string PoiKind = "poi.kind";

        /// <summary>Detail-sheet rotation, indexed by POI. [index = poiIndex]</summary>
        public const string PoiSheet = "poi.sheet";

        // --- surveys and sheets (Sheets/) ----------------------------------------------
        /// <summary>Office for the whole-island survey, Range(0, 3) — NOT Offices.Count (§10.5, D5).</summary>
        public const string WholeIsland = "wholeIsland";

        /// <summary>Survey year, indexed by office. [index = (int)Office]</summary>
        public const string Year = "year";

        /// <summary>Whole-island survey year, indexed by office. [index = (int)Office]</summary>
        public const string YearWholeIsland = "yearWholeIsland";

        /// <summary>Hydrographic coast-walk region: shore anchor point and disc radius.</summary>
        public const string CoastRegion = "coastRegion";

        // --- naming (Naming/) ------------------------------------------------------------
        /// <summary>Which phonology the island speaks (§9).</summary>
        public const string Names = "names";

        /// <summary>The island's own name.</summary>
        public const string NamesIsland = "names.island";

        /// <summary>Settlement names, in feature order. [index = settlement index]</summary>
        public const string NamesSettlements = "names.settlements";

        /// <summary>Peak names, in feature order. [index = peak index]</summary>
        public const string NamesPeaks = "names.peaks";

        // --- reserved (Render/) ----------------------------------------------------------
        /// <summary>
        /// Reserved for seed-derived palette tints (§6.4). Deliberately unused: the name is
        /// claimed now so the work can be added later without disturbing any existing feature,
        /// which <c>Poc02Acceptance</c> asserts by drawing from it. Do not repurpose it.
        /// </summary>
        public const string Palette = "palette";
    }
}
