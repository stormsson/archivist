using System.Collections.Generic;
using Archivist.Generation.Features;

namespace Archivist.Generation.Sheets
{
    /// <summary>
    /// §8.3 — the office x class matrix. Binary: an office draws a class or it omits it.
    /// There is no "schematic" render mode (§8.2 keeps every sheet in one line style, so
    /// coverage and rotation are the only office signals this POC is allowed to use).
    ///
    /// <para>
    /// D1 adds a second reading of the same table: <see cref="Serving"/> is the drawn set
    /// minus <see cref="FeatureClass.Coast"/>. The coastline is island-scale by R1.4, so
    /// every sheet of a coastal survey carries it and it cannot be the thing that makes a
    /// sheet worth cutting. Excluding it is what makes the R1.5 service test (§7.4) mean
    /// "this office draws something here".
    /// </para>
    ///
    /// <para>
    /// Every accessor returns a precomputed, stably ordered array (enum order). Nothing
    /// here enumerates a HashSet or a Dictionary — §4.1 forbids set iteration order from
    /// driving generation, and these sets drive both the cull and A6.
    /// </para>
    /// </summary>
    public static class FeatureMatrix
    {
        const int OfficeCount = Offices.Count;          // Hydrographic .. Antiquarian
        const int ClassCount = FeatureClasses.Count;    // FeatureClass.Coast .. FeatureClass.Poi

        /// <summary>
        /// §8.3, transcribed exactly. Row = <see cref="Office"/>, column = <see cref="FeatureClass"/>,
        /// both indexed by their enum value so the table cannot drift from the enums.
        ///
        /// <para>
        /// THIS IS THE ONLY COPY. <see cref="Drawn"/>, <see cref="Serving"/> and
        /// <see cref="Placeability"/> are all derived from it once, at type initialisation.
        /// </para>
        ///
        /// <para>
        /// POC-03, on the Antiquarian row. It draws its POI <b>and its surroundings</b>, which
        /// is the whole point of the office: a 250 mm sheet showing only its own POI would
        /// share no drawn class with any other office and the §8.3 shared-class invariant —
        /// the thing that makes sheets cross-referenceable, measured by A6 — would collapse
        /// exactly where detail sheets need it most. No Grid (that is Garrison's signature)
        /// and no Sounding (that is Hydrographic's), so the row is still distinguishable at a
        /// glance.
        /// </para>
        /// </summary>
        static readonly bool[,] Table = new bool[OfficeCount, ClassCount]
        {
            //                  Coast  Contour  Peak   River  Settle  Grid   Sound  Poi
            /* Hydrographic */ { true,  false,  false, false, true,   false, true,  false },
            /* LandSurvey   */ { true,  true,   true,  true,  true,   false, false, false },
            /* Garrison     */ { true,  false,  true,  false, false,  true,  false, false },
            /* Antiquarian  */ { true,  true,   true,  true,  true,   false, false, true  },
        };

        static readonly FeatureClass[] NoClasses = new FeatureClass[0];

        const int CoastBit = 1 << (int)FeatureClass.Coast;
        const int PoiBit   = 1 << (int)FeatureClass.Poi;

        /// <summary>
        /// §8.3 drawn sets, DERIVED from <see cref="Table"/> row by row, in FeatureClass enum
        /// order. These used to be hand-written literals beside the table; four structures
        /// that all had to agree, with nothing checking that they did.
        /// </summary>
        static readonly FeatureClass[][] DrawnByOffice;

        /// <summary>D1 / §7.4 serving sets: <c>Drawn(office) \ { Coast }</c>, same order.</summary>
        static readonly FeatureClass[][] ServingByOffice;

        /// <summary>
        /// POC-03 spec §2.3, the placeability floor: <c>Serving(Antiquarian) \ { Poi }</c>.
        /// See <see cref="Placeability"/> — this set is the thing open question 2 tightens.
        /// </summary>
        static readonly FeatureClass[] PlaceabilityAntiquarian;

        static FeatureMatrix()
        {
            DrawnByOffice = new FeatureClass[OfficeCount][];
            ServingByOffice = new FeatureClass[OfficeCount][];

            for (int o = 0; o < OfficeCount; o++)
            {
                DrawnByOffice[o] = Row(o, 0);
                ServingByOffice[o] = Row(o, CoastBit);
            }

            PlaceabilityAntiquarian = Row((int)Office.Antiquarian, CoastBit | PoiBit);
        }

        /// <summary>
        /// One row of <see cref="Table"/> as an array, ascending by <see cref="FeatureClass"/>
        /// value, with the classes named in <paramref name="excludeBits"/> withheld. The walk
        /// is over the enum's integer range, never over a set or a dictionary — §4.1 forbids
        /// set iteration order from driving generation, and these arrays drive both the cull
        /// and A6.
        /// </summary>
        static FeatureClass[] Row(int office, int excludeBits)
        {
            var classes = new List<FeatureClass>(ClassCount);
            for (int c = 0; c < ClassCount; c++)
            {
                if ((excludeBits & (1 << c)) != 0) continue;
                if (Table[office, c]) classes.Add((FeatureClass)c);
            }
            return classes.ToArray();
        }

        /// <summary>§8.3. True if <paramref name="office"/> draws <paramref name="cls"/> on its sheets.</summary>
        public static bool Draws(Office office, FeatureClass cls)
        {
            int o = (int)office;
            int c = (int)cls;
            if (o < 0 || o >= OfficeCount || c < 0 || c >= ClassCount) return false;
            return Table[o, c];
        }

        /// <summary>
        /// §8.3. The classes this office draws, in FeatureClass enum order. Used by the
        /// renderer, by A5 (§13.5) and by A6 (§13.6).
        /// </summary>
        public static IReadOnlyList<FeatureClass> Drawn(Office office)
        {
            int o = (int)office;
            if (o < 0 || o >= OfficeCount) return NoClasses;
            return DrawnByOffice[o];
        }

        /// <summary>
        /// D1 / §7.4. <c>Serving(office) = Drawn(office) \ { Coast }</c> — the classes whose
        /// presence within <c>u</c> makes a point *served* for this office. Hydrographic is
        /// served by its soundings, Garrison everywhere by its own grid (§6.4), and Land
        /// Survey is the only office the service test actually culls.
        /// </summary>
        public static IReadOnlyList<FeatureClass> Serving(Office office)
        {
            int o = (int)office;
            if (o < 0 || o >= OfficeCount) return NoClasses;
            return ServingByOffice[o];
        }

        /// <summary>
        /// <b>POC-03 spec §2.3 — the placeability floor (P2.4), and the ONE LINE open question 2
        /// tightens.</b>
        ///
        /// <para>A detail sheet must contain at least one drawn feature <i>besides its own
        /// POI</i>: a 300 m square of bare hillside is not a puzzle, it is a dead end, because
        /// every hillside looks alike. The rule is the D1 service rule applied to a new purpose,
        /// so it reuses that machinery rather than growing a second one — this method returns
        /// the class set, and <see cref="Features.ServiceRule.ServedByAny"/> answers presence
        /// over it on the same 64 m lattice.</para>
        ///
        /// <para>Today that set is <c>Serving(office) \ { Poi }</c> — <i>any</i> class the
        /// office draws, other than the POI itself. Open question 2 is live: "one other feature"
        /// may be too weak, because one contour looks like any other. If sheets prove
        /// unplaceable at the table, tighten this to the LOCALLY DISTINCTIVE classes — coast,
        /// river, lake shore, settlement — by returning a narrower array here. Nothing else
        /// changes; the cutter reads whatever this returns.</para>
        /// </summary>
        public static IReadOnlyList<FeatureClass> Placeability(Office office)
        {
            switch (office)
            {
                case Office.Antiquarian: return PlaceabilityAntiquarian;
                default:                 return NoClasses;
            }
        }

        /// <summary>
        /// §8.3, the shared-class invariant, as a predicate so §13.6 can measure it:
        /// "any two offices whose coverage can overlap must share at least one drawn class".
        /// Returns the first shared class in FeatureClass enum order, which is always
        /// <see cref="FeatureClass.Coast"/> for the three v1 offices — the invariant holds
        /// by construction, and this exists so A6 checks it rather than assuming it.
        /// On <c>false</c> the out value is not meaningful.
        /// </summary>
        public static bool SharesDrawnClass(Office a, Office b, out FeatureClass shared)
        {
            IReadOnlyList<FeatureClass> drawnByA = Drawn(a);
            for (int i = 0; i < drawnByA.Count; i++)
            {
                if (Draws(b, drawnByA[i]))
                {
                    shared = drawnByA[i];
                    return true;
                }
            }
            shared = FeatureClass.Coast;
            return false;
        }
    }
}
