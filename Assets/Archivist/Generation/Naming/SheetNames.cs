using System.Collections.Generic;
using Archivist.Generation.Determinism;
using Archivist.Generation.Features;
using Archivist.Generation.Field;
using Archivist.Generation.Geometry;
using Archivist.Generation.Sheets;

namespace Archivist.Generation.Naming
{
    /// <summary>
    /// What one <see cref="Sheet"/> is <i>called</i> — <i>Cape Vela</i>, <i>Cold Harbour</i>,
    /// <i>The Crown</i>. A function of the island seed and the sheet's identity, like everything
    /// else on an island (R1.1); never authored, never stored (R1.11).
    ///
    /// <para><b>The sheet is named, not a feature. That is the whole of the idea, and it is a
    /// reversal.</b> C7.7 as written says a sheet's name is "the nearest named feature to
    /// <c>CentreGround</c>, taken from <c>island.Names</c> / <c>island.Features</c>", with the
    /// bare code as a fallback. That was implemented in <c>Building.Table.SheetNaming</c> and
    /// <b>abandoned</b>, because the generator as built names almost nothing a sheet sits on:
    /// every settlement (§7.2) and the top <c>Tuning.PeakNamedCount</c> = 3 peaks (§7.1), and
    /// that is the entire supply. Rivers have no name field, <see cref="Poi"/> is unnamed by
    /// design (POC-03 §5 keeps labels out of scope), and <b>the coastline has no naming at
    /// all</b> — it is a <see cref="Polyline"/> with no named parts. Meanwhile almost every name
    /// in the mockups is coastal: <i>Cape Vela</i>, <i>Gull Spit</i>, <i>Cold Harbour</i>,
    /// <i>Long Reef</i>. So the feature scan returned the fallback for most sheets and the whole
    /// Hydrographic office read as a column of bare codes. A rule whose specified answer is
    /// "nothing" for the majority of its inputs is not a naming rule.</para>
    ///
    /// <para><b>So the name is derived from the ground the sheet covers, not from a thing on
    /// it.</b> A survey office naming a sheet after the water and rock it charts is what the
    /// mockups were always showing — <i>Salt Flats</i> is a description, not a village. The
    /// vocabulary is generic (<see cref="WaterGenerics"/> and friends) because it has to be:
    /// the supply of islands is unbounded (R1.2), so the tables must combine freely rather than
    /// encode specific places, exactly as <see cref="Phonology"/> says of its own.</para>
    ///
    /// <para><b>Why this lives in <c>Archivist.Generation</c> and not in the UI.</b> A name
    /// drawn on the Building side would not be a function of the island seed — it would be a
    /// function of the seed plus whoever called it, and it could not be reproduced by anything
    /// that only holds the seed (R1.11). The island's own naming already lives here (§9); this
    /// is the same kind of fact about the same island. The assembly must never reference
    /// UnityEngine (§14) and nothing here does — that rule is what lets
    /// <c>Tools/run-acceptance.sh</c> run headless.</para>
    ///
    /// <para><b>Determinism (§4.3).</b> One new stream, <see cref="StreamNames.NamesSheets"/>,
    /// appended. It is indexed by <see cref="StableIndex"/> — a pure function of the sheet's
    /// <i>identity</i> (office ordinal, whole-island flag, number), never of its position in a
    /// list — so adding, losing or reordering a sheet cannot rename another one, in the same
    /// way <c>names.settlements</c>[i] keeps one settlement from renaming the next. <b>No other
    /// stream is drawn from</b>, and nothing here feeds back into generation: naming is a
    /// terminal read of the island, run after it exists.</para>
    ///
    /// <para><b>No uniqueness pass, deliberately.</b> <see cref="NameGenerator"/> makes island,
    /// settlement and peak names unique within an island; this does not, and must not. Two
    /// sheets of the same office may both come out <i>Long Reef</i>. Enforcing uniqueness would
    /// mean naming sheets in some global order and retrying on collision — which makes a
    /// sheet's name depend on which other sheets exist, destroying the one property
    /// <see cref="StableIndex"/> exists to buy. The cabinet row shows the name <i>and</i> the
    /// code (C7.3), so two sheets sharing a title are still told apart; a sheet renaming itself
    /// because a neighbouring sheet was culled could not be.</para>
    ///
    /// <para><b>Nothing here caches, and nothing here may cache.</b> The island is in hand when
    /// this is called. If it ever costs too much the fix is to call it less, not to remember
    /// it — the same bargain <c>SheetLookup</c> keeps.</para>
    ///
    /// <para><b>Constants live here, with the tables they shape.</b> §12 puts every constant in
    /// <c>Tuning</c>; naming has no entry there and never has — <see cref="NameGenerator"/>
    /// says so of its own qualifier chances — because these numbers shape a word list and mean
    /// nothing outside it. The two thresholds that are <i>not</i> local are borrowed rather
    /// than re-invented: <c>Tuning.PeakElevationFrac</c> is the generator's own definition of
    /// high ground, and the sample grid matches §10.3's 16x16 cull lattice.</para>
    /// </summary>
    public static class SheetNames
    {
        /// <summary>
        /// The name of one sheet. Never null for a real island; a null <paramref name="island"/>
        /// is the caller's bug and yields null rather than an exception, because a cabinet that
        /// renders a blank row is a bug anyone can see and an exception thrown while building a
        /// row takes the whole table view down.
        ///
        /// <para>Pure: same island, same sheet, same string, on any machine, forever. The
        /// island must already be in hand — this deliberately does not take an identity and
        /// regenerate, because that would hide a whole island generation inside a call a UI
        /// makes once per visible row.</para>
        ///
        /// <para>Three rules, in priority order:</para>
        /// <list type="number">
        /// <item><b>The whole-island sheet is the island.</b> It is the board's outline (R6.8a)
        /// and the entry point of the survey (R2.2a); naming it after a tile of ground it
        /// contains would read as a part of the island rather than the whole of it. So it
        /// takes <see cref="Island.Name"/> verbatim.</item>
        /// <item><b>A sheet carrying a named settlement is named for it.</b> This is the one
        /// part of C7.7 that survives, and it survives because it is right: a town drawn on the
        /// paper is what an archivist would call the sheet. Nearest to
        /// <see cref="Sheet.CentreGround"/> wins.</item>
        /// <item><b>Otherwise the ground is described.</b> See
        /// <see cref="Classify(Island,Sheet)"/>.</item>
        /// </list>
        ///
        /// <para>Peaks are <i>not</i> a source, though three of them are named. A peak name
        /// carries the peak suffix register (§9) — "Braefell" already reads as a summit — and
        /// putting it on a sheet title says the sheet <i>is</i> that summit, which is false of
        /// a 1285 x 1902 m Land Survey tile. The peak still earns the sheet a high-ground
        /// generic through rule 3, which is the honest version of the same fact.</para>
        /// </summary>
        /// <param name="island">The island the sheet belongs to.</param>
        /// <param name="sheet">The sheet, straight off a <see cref="Survey"/> or recovered by
        /// identity.</param>
        public static string For(Island island, Sheet sheet)
        {
            if (island == null) return null;

            // 1. The whole-island sheet is the island itself (R2.2a, R6.8a).
            if (sheet.Survey.IsWholeIsland && !string.IsNullOrEmpty(island.Name)) return island.Name;

            // 2. A named settlement ON the sheet, nearest the centre.
            string town = NearestSettlementOn(island, sheet);
            if (town != null) return town;

            // 3. The ground.
            Pcg32 rng = Streams.For(island.Seed, StreamNames.NamesSheets,
                                    StableIndex(sheet.Survey, sheet.Number));

            return Compose(island, ref rng, Classify(island, sheet));
        }

        // ------------------------------------------------------------------
        // Identity -> stream index
        // ------------------------------------------------------------------

        /// <summary>Bits reserved for the office ordinal: 0..7. Four offices exist
        /// (<see cref="Offices.Count"/>) and <see cref="Office"/> is append-only, so three bits
        /// leave room for four more without moving any existing sheet's name.</summary>
        const int OfficeBits = 3;

        /// <summary>The whole-island flag, sitting just above the office ordinal.</summary>
        const int WholeIslandBit = 1 << OfficeBits;

        /// <summary>Where the sheet number starts: one bit above the flag.</summary>
        const int NumberShift = OfficeBits + 1;

        /// <summary>
        /// The sheet's identity as one integer, for
        /// <c>Streams.For(seed, "names.sheets", index)</c>.
        ///
        /// <para><b>Encoding.</b> <c>(number &lt;&lt; 4) | (wholeIsland ? 8 : 0) | officeOrdinal</c>
        /// — three bits of office, one bit of flag, the rest the number. Exactly the three
        /// fields <c>SheetId</c> uses to tell two sheets of one island apart, and no more: the
        /// island seed is already the other half of <c>Streams.For</c>, so folding it in here
        /// would only weaken the index.</para>
        ///
        /// <para><b>Collision bound.</b> Injective — and therefore collision-free — for office
        /// ordinal in [0, 8) and number in [0, 2^27). Beyond eight offices the ordinal aliases
        /// into the flag bit; beyond 134 217 727 sheets in one survey the number overruns a
        /// positive <c>int</c>. Live values are four offices and surveys of tens of sheets, so
        /// both bounds are unreachable by four to seven orders of magnitude.</para>
        ///
        /// <para><b>Why identity and not list position.</b> A sheet's index into
        /// <c>Survey.Sheets</c> is not stable: numbering happens <i>after</i> the cull (§10.4),
        /// so a rect that starts or stops passing the cull renumbers everything behind it.
        /// Indexing by that would rename half a survey because one sheet appeared. The number
        /// itself has the same property, which is why it is included — the identity IS the
        /// numbering, and two sheets that share an identity are the same sheet.</para>
        /// </summary>
        static int StableIndex(SurveySpec survey, int number)
        {
            int office = (int)survey.Office & (WholeIslandBit - 1);
            int whole = survey.IsWholeIsland ? WholeIslandBit : 0;
            int n = number < 0 ? 0 : number;
            return (n << NumberShift) | whole | office;
        }

        // ------------------------------------------------------------------
        // Rule 2 - a named settlement on the sheet
        // ------------------------------------------------------------------

        /// <summary>
        /// The named settlement nearest <see cref="Sheet.CentreGround"/>, or null.
        ///
        /// <para><b>"On the sheet" means <see cref="Sheet.Contains(V2)"/>, not
        /// <see cref="Sheet.GroundBounds"/>.</b> Its own comment says why:
        /// <c>GroundBounds</c> is the AABB <i>of</i> the rotated rect and strictly over-counts —
        /// at any rotation off a multiple of 90 degrees it admits four corner wedges the sheet
        /// does not cover. The Hydrographic coast walk gives every sheet its own rotation
        /// (D-H2), so those wedges are the normal case here, not the exotic one. Naming a sheet
        /// after a town the paper does not show is the worst failure available to this file:
        /// the player's only access to the ground is paper, so a title naming ground the sheet
        /// does not draw is a lie they cannot check.</para>
        ///
        /// <para>A distance cutoff — "nearest named feature within r" — was the other candidate
        /// and is rejected for the same reason plus one more: it needs a tuning number with no
        /// natural value, since sheets differ in size by office and scale (D-C5), and any radius
        /// reaching past the sheet edge re-admits the lie. "On the sheet, nearest the middle"
        /// needs no constant and cannot lie.</para>
        ///
        /// <para>Ties break by feature order — first strictly nearest wins — which is fixed
        /// (§7.2 sorts settlements by <c>(score desc, x asc, y asc)</c>), so the answer is the
        /// same on any machine.</para>
        /// </summary>
        static string NearestSettlementOn(Island island, Sheet sheet)
        {
            IslandFeatures features = island.Features;
            if (features == null) return null;

            IReadOnlyList<Settlement> towns = features.Settlements;
            if (towns == null) return null;

            V2 centre = sheet.CentreGround;
            string best = null;
            double bestDistSq = double.MaxValue;

            for (int i = 0; i < towns.Count; i++)
            {
                Settlement town = towns[i];
                if (string.IsNullOrEmpty(town.Name)) continue;
                if (!sheet.Contains(town.Position)) continue;

                double d = V2.DistSq(town.Position, centre);
                if (d >= bestDistSq) continue;      // strict: first in feature order wins a tie
                bestDistSq = d;
                best = town.Name;
            }

            return best;
        }

        // ------------------------------------------------------------------
        // Rule 3, part one - what kind of ground is this?
        // ------------------------------------------------------------------

        /// <summary>The five kinds of ground a sheet can cover. Each maps to exactly one
        /// generic-noun table, in <see cref="GenericsFor"/>.</summary>
        enum Ground
        {
            /// <summary>All or mostly water.</summary>
            Water = 0,
            /// <summary>Land and water together — a shore crosses the sheet.</summary>
            Coast = 1,
            /// <summary>Solid land, carrying a river.</summary>
            Watercourse = 2,
            /// <summary>Solid land, high — a peak on it, or ground above the generator's own
            /// high-ground threshold.</summary>
            High = 3,
            /// <summary>Solid land, low and unremarkable.</summary>
            Low = 4
        }

        /// <summary>
        /// Sample grid over the sheet, per side. 16x16 = 256 samples, matching §10.3's cull
        /// lattice — the same rect was already judged at that density when it was cut, so
        /// describing it at a coarser one would be describing ground the cull did not see.
        /// </summary>
        const int SampleGrid = 16;

        /// <summary>At or below this land fraction the sheet reads as water, not as a shore.
        /// One row of 16 samples is 0.0625, so this admits roughly two rows of land before the
        /// sheet stops being open water.</summary>
        const double WaterLandFraction = 0.12;

        /// <summary>At or above this land fraction the sheet reads as solid ground: fewer than
        /// two rows of the lattice are wet, which no real shore crossing the rect could
        /// manage.</summary>
        const double SolidLandFraction = 0.88;

        /// <summary>
        /// What the sheet covers, from the island's height field and discrete features.
        ///
        /// <para><b>Sampled on the sheet, in ground space, through its own rotation.</b> The
        /// 16x16 lattice is laid over cell centres of the <i>rotated</i> rect — the same
        /// symmetric, edge-avoiding lattice §10.3 uses — so no sample lands in a
        /// <c>GroundBounds</c> corner wedge and the description is of the paper, not of its
        /// bounding box.</para>
        ///
        /// <para>Derived from those samples: <b>land fraction</b> (0 = every sample is water),
        /// and over the land samples the <b>mean</b> and <b>max</b> elevation as a fraction of
        /// <c>Params.MaxElevation</c>, the island's own ceiling. Relative, not absolute, because
        /// an atoll tops out at 90 m and a mountainous island at 620 m (§5.3): 60 m is the roof
        /// of one and a foothill of the other, and a table of absolute metres would call every
        /// atoll sheet flat.</para>
        ///
        /// <para>Two discrete tests join them: does a river cross the rect, and does a peak sit
        /// on it. Rivers are walked by vertex — they step every 40 m (§7.3) and the smallest
        /// sheet is 275 m square (POC-03 §2.1), so a course crossing a sheet always drops a
        /// vertex inside it. Every peak counts, named or not: <see cref="Peak.Name"/> is null
        /// below the top three (§7.1) but the ground is just as high either way, and this is a
        /// question about ground.</para>
        ///
        /// <para><see cref="Island.Coastline"/> is deliberately <i>not</i> consulted. It would
        /// answer the same question — is there a shore here — at the cost of a segment/rect
        /// clip per loop, and the land fraction already answers it more usefully, with the
        /// proportion attached. It is also the one thing that cannot mislead a lattice: an
        /// atoll's lagoon reads as water to both.</para>
        /// </summary>
        static Ground Classify(Island island, Sheet sheet)
        {
            IslandField field = island.Field;
            if (field == null) return Ground.Low;

            double w = sheet.Survey.SheetGroundWidth;
            double h = sheet.Survey.SheetGroundHeight;
            V2 centre = sheet.CentreGround;
            double rot = sheet.RotationDeg;

            int landCount = 0;
            double sumRel = 0.0;
            double maxRel = 0.0;

            for (int b = 0; b < SampleGrid; b++)
            {
                for (int a = 0; a < SampleGrid; a++)
                {
                    // Cell centres, symmetric about the rect (§10.3), then rotated onto the
                    // sheet's own orientation (D-H2 makes that per-sheet for Hydrographic).
                    double u = (a + 0.5) / SampleGrid - 0.5;
                    double v = (b + 0.5) / SampleGrid - 0.5;
                    V2 ground = centre + new V2(u * w, v * h).RotateDeg(rot);

                    if (!field.IsLand(ground)) continue;
                    landCount++;

                    double rel = field.Elevation(ground.X, ground.Y) / island.Params.MaxElevation;
                    if (rel < 0.0) rel = 0.0;
                    sumRel += rel;
                    if (rel > maxRel) maxRel = rel;
                }
            }

            double landFraction = (double)landCount / (SampleGrid * SampleGrid);
            if (landFraction <= WaterLandFraction) return Ground.Water;
            if (landFraction < SolidLandFraction) return Ground.Coast;

            // Solid land from here. A river is the strongest thing that can be said about it.
            if (HoldsRiver(island, sheet)) return Ground.Watercourse;

            // High ground: a peak on the sheet, or the sheet's own ceiling at or above the
            // fraction of MaxElevation the generator itself calls a peak (§7.1).
            if (HoldsPeak(island, sheet)) return Ground.High;
            if (maxRel >= Tuning.PeakElevationFrac) return Ground.High;

            double meanRel = landCount > 0 ? sumRel / landCount : 0.0;
            return meanRel >= Tuning.PeakElevationFrac ? Ground.High : Ground.Low;
        }

        /// <summary>True if any river course drops a vertex on the sheet. See
        /// <see cref="Classify"/> for why vertices are enough.</summary>
        static bool HoldsRiver(Island island, Sheet sheet)
        {
            IslandFeatures features = island.Features;
            if (features == null || features.Rivers == null) return false;

            IReadOnlyList<River> rivers = features.Rivers;
            for (int i = 0; i < rivers.Count; i++)
            {
                Polyline course = rivers[i].Course;
                if (course == null) continue;
                for (int p = 0; p < course.Count; p++)
                {
                    if (sheet.Contains(course[p])) return true;
                }
            }
            return false;
        }

        /// <summary>True if any peak sits on the sheet — named or not (§7.1).</summary>
        static bool HoldsPeak(Island island, Sheet sheet)
        {
            IslandFeatures features = island.Features;
            if (features == null || features.Peaks == null) return false;

            IReadOnlyList<Peak> peaks = features.Peaks;
            for (int i = 0; i < peaks.Count; i++)
            {
                if (sheet.Contains(peaks[i].Position)) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------
        // Rule 3, part two - the word tables
        //
        // ONE place. Five arrays, one per Ground, reached only through GenericsFor. Nothing
        // else in this file — and nothing outside it — may spell one of these words.
        // ------------------------------------------------------------------

        /// <summary>Open water: nothing but soundings and the chart's own furniture.</summary>
        static readonly string[] WaterGenerics =
        {
            "Deeps", "Sound", "Roads", "Shoals", "Narrows", "Channel"
        };

        /// <summary>A shore crosses the sheet. This is the Hydrographic office's whole world,
        /// and the register the mockups are written in.</summary>
        static readonly string[] CoastGenerics =
        {
            "Reef", "Spit", "Cape", "Head", "Bay", "Harbour", "Point", "Ness", "Strand", "Bar"
        };

        /// <summary>Solid land carrying a river. Survey English for a crossing or a stretch of
        /// water inland.</summary>
        static readonly string[] WatercourseGenerics =
        {
            "Water", "Ford", "Reach", "Beck", "Mere"
        };

        /// <summary>Solid land, high.</summary>
        static readonly string[] HighGenerics =
        {
            "Ridge", "Crown", "Height", "Tor", "Fell", "Scarp", "Brow"
        };

        /// <summary>Solid land, low and level.</summary>
        static readonly string[] LowGenerics =
        {
            "Flats", "Levels", "Moor", "Marsh", "Heath", "Waste"
        };

        /// <summary>
        /// The generic-noun table for one kind of ground. A switch on the enum member rather
        /// than an array indexed by <c>(int)Ground</c>, for the reason §4.1 prefers
        /// <c>Offices.All</c> to enum reflection: a member added without a table here is a
        /// compile-time hole in a switch and a silent null in an array.
        /// </summary>
        static string[] GenericsFor(Ground ground)
        {
            switch (ground)
            {
                case Ground.Water:       return WaterGenerics;
                case Ground.Coast:       return CoastGenerics;
                case Ground.Watercourse: return WatercourseGenerics;
                case Ground.High:        return HighGenerics;
                default:                 return LowGenerics;
            }
        }

        /// <summary>
        /// Attributive adjectives. Curated, small, and deliberately plain — the register of a
        /// survey party writing a title block in 1780, not of a poet. Every one of them
        /// combines with every generic above without needing agreement or an article, which is
        /// what lets the tables multiply out instead of being authored pairwise (R1.2).
        /// </summary>
        static readonly string[] Adjectives =
        {
            "Long", "Cold", "Salt", "Black", "White", "Grey", "Broad", "Narrow",
            "Little", "Great", "North", "South", "East", "West", "Outer", "Inner",
            "Low", "High", "Far", "Deep", "Bare", "Bleak", "Green", "Foul",
            "Sunken", "Rough", "Still", "Blind", "Old", "Crooked"
        };

        /// <summary>
        /// Attributive nouns — <i>Gull</i> Spit, <i>Ember</i> Ridge. Beasts, weather and the
        /// leavings of people, which is what a survey party actually names ground after. Kept
        /// disjoint from <see cref="Adjectives"/> so the two forms sound different; a reader
        /// should be able to hear which one a sheet drew.
        /// </summary>
        static readonly string[] Nouns =
        {
            "Gull", "Ember", "Otter", "Seal", "Raven", "Heron", "Crow", "Fox",
            "Hart", "Whale", "Anchor", "Beacon", "Chapel", "Kiln", "Gallows", "Lantern",
            "Cable", "Tide", "Storm", "Herring", "Basalt", "Cinder", "Bone", "Thistle",
            "Bramble", "Wreck", "Fern", "Gannet", "Shepherd", "Mast"
        };

        // ------------------------------------------------------------------
        // Rule 3, part three - grammar
        // ------------------------------------------------------------------

        /// <summary>The four shapes a composed sheet name can take. All four appear in the
        /// mockups; the weights below are why <i>The Crown</i> is rarer than <i>Long
        /// Reef</i>.</summary>
        enum Form
        {
            /// <summary>"Long Reef", "Cold Harbour", "Salt Flats".</summary>
            AdjectiveGeneric = 0,
            /// <summary>"Gull Spit", "Ember Ridge".</summary>
            NounGeneric = 1,
            /// <summary>"Cape Vela" — the second word from the island's own phonology.</summary>
            GenericIslandWord = 2,
            /// <summary>"The Crown".</summary>
            TheGeneric = 3
        }

        /// <summary>
        /// Cumulative weights over <see cref="Form"/>, in enum order, ending at 1.0.
        /// 0.34 / 0.28 / 0.26 / 0.12.
        ///
        /// <para>Not uniform, and the shape matters more than the numbers. The two modifier
        /// forms carry most of the traffic because they multiply the widest (30 modifiers x
        /// ~7 generics per ground). <b>"The &lt;Generic&gt;" is held to an eighth on purpose:</b>
        /// it is the definite article, and a chart with four sheets called <i>The Crown</i>,
        /// <i>The Deeps</i> and <i>The Moor</i> reads as a place with only one of each. Rarity
        /// is what makes it land.</para>
        /// </summary>
        static readonly double[] FormCumulative = { 0.34, 0.62, 0.88, 1.00 };

        /// <summary>
        /// One composed name.
        ///
        /// <para><b>Draw order inside one name is fixed</b>, exactly as
        /// <see cref="NameGenerator"/> fixes its own: form, then generic, then the modifier the
        /// form asks for. <c>TheGeneric</c> takes no third draw and needs none — one sheet gets
        /// one name from one stream, so no later draw exists to be shifted.</para>
        /// </summary>
        static string Compose(Island island, ref Pcg32 rng, Ground ground)
        {
            Form form = FormFrom(rng.NextDouble());

            string[] generics = GenericsFor(ground);
            string generic = generics[rng.Range(0, generics.Length)];

            switch (form)
            {
                case Form.AdjectiveGeneric:
                    return Adjectives[rng.Range(0, Adjectives.Length)] + " " + generic;

                case Form.NounGeneric:
                    return Nouns[rng.Range(0, Nouns.Length)] + " " + generic;

                case Form.GenericIslandWord:
                {
                    // A phonology with no roots cannot happen (§9 fixes three tables), but if
                    // it ever did this must still return a name — so the form degrades to the
                    // one that needs no second word rather than inventing a word here.
                    string word = IslandWord(island, ref rng);
                    return word != null ? generic + " " + word : "The " + generic;
                }

                default:
                    return "The " + generic;
            }
        }

        /// <summary>The form for a draw in [0, 1). Walked, not indexed, so the weights stay
        /// readable as weights.</summary>
        static Form FormFrom(double roll)
        {
            for (int i = 0; i < FormCumulative.Length; i++)
            {
                if (roll < FormCumulative[i]) return (Form)i;
            }
            return Form.TheGeneric;
        }

        /// <summary>
        /// A word in the island's own tongue, for "Cape Vela". Null only if the island has no
        /// phonology at all, which §9 makes impossible.
        ///
        /// <para><b>The phonology is the island's, and is not re-derived.</b>
        /// <see cref="NameGenerator.PhonologyFor(ulong)"/> is the one place that answers which
        /// of the three (§9) an island speaks; it is exposed precisely so a second reader can
        /// ask. Reading it here cannot perturb naming: <c>Streams.For</c> builds a fresh
        /// <see cref="Pcg32"/> on every call, so drawing the phonology twice from
        /// <c>names</c> gives the same answer twice and leaves <c>NameGenerator</c>'s own copy
        /// untouched. The <i>word</i> is then drawn from <c>names.sheets</c>, this file's own
        /// stream — the phonology supplies the vocabulary, never the randomness.</para>
        ///
        /// <para><b>A bare root, not root + suffix.</b> <c>NameGenerator.Join</c> owns the two
        /// elision rules that keep "Brae" + "aig" from becoming "Braeaig", and it is private.
        /// Copying those six lines here would be a second copy of a documented string contract,
        /// which is worse than the alternative — and the alternative is good: the mockup's
        /// <i>Cape Vela</i> is one short word, and a root is exactly that, already capitalised
        /// and already ASCII (§4.2). The result sounds like the island because it is made of
        /// the island's morphemes, without duplicating the machine that assembles them.</para>
        /// </summary>
        static string IslandWord(Island island, ref Pcg32 rng)
        {
            Phonology phon = NameGenerator.PhonologyFor(island.Seed);
            if (phon == null || phon.Roots == null || phon.Roots.Length == 0) return null;
            return phon.Roots[rng.Range(0, phon.Roots.Length)];
        }
    }
}
