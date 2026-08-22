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
        const int OfficeCount = 3;   // Office.Hydrographic .. Office.Garrison
        const int ClassCount = 7;    // FeatureClass.Coast .. FeatureClass.Sounding

        /// <summary>
        /// §8.3, transcribed exactly. Row = <see cref="Office"/>, column = <see cref="FeatureClass"/>,
        /// both indexed by their enum value so the table cannot drift from the enums.
        /// </summary>
        static readonly bool[,] Table = new bool[OfficeCount, ClassCount]
        {
            //                  Coast  Contour  Peak   River  Settle  Grid   Sound
            /* Hydrographic */ { true,  false,  false, false, true,   false, true  },
            /* LandSurvey   */ { true,  true,   true,  true,  true,   false, false },
            /* Garrison     */ { true,  false,  true,  false, false,  true,  false },
        };

        static readonly FeatureClass[] NoClasses = new FeatureClass[0];

        // Drawn sets — §8.3, in FeatureClass enum order.
        static readonly FeatureClass[] DrawnHydrographic =
        {
            FeatureClass.Coast, FeatureClass.Settlement, FeatureClass.Sounding
        };

        static readonly FeatureClass[] DrawnLandSurvey =
        {
            FeatureClass.Coast, FeatureClass.Contour, FeatureClass.Peak,
            FeatureClass.River, FeatureClass.Settlement
        };

        static readonly FeatureClass[] DrawnGarrison =
        {
            FeatureClass.Coast, FeatureClass.Peak, FeatureClass.Grid
        };

        // Serving sets — D1: Drawn \ { Coast }, same order.
        static readonly FeatureClass[] ServingHydrographic =
        {
            FeatureClass.Settlement, FeatureClass.Sounding
        };

        static readonly FeatureClass[] ServingLandSurvey =
        {
            FeatureClass.Contour, FeatureClass.Peak, FeatureClass.River, FeatureClass.Settlement
        };

        static readonly FeatureClass[] ServingGarrison =
        {
            FeatureClass.Peak, FeatureClass.Grid
        };

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
            switch (office)
            {
                case Office.Hydrographic: return DrawnHydrographic;
                case Office.LandSurvey:   return DrawnLandSurvey;
                case Office.Garrison:     return DrawnGarrison;
                default:                  return NoClasses;
            }
        }

        /// <summary>
        /// D1 / §7.4. <c>Serving(office) = Drawn(office) \ { Coast }</c> — the classes whose
        /// presence within <c>u</c> makes a point *served* for this office. Hydrographic is
        /// served by its soundings, Garrison everywhere by its own grid (§6.4), and Land
        /// Survey is the only office the service test actually culls.
        /// </summary>
        public static IReadOnlyList<FeatureClass> Serving(Office office)
        {
            switch (office)
            {
                case Office.Hydrographic: return ServingHydrographic;
                case Office.LandSurvey:   return ServingLandSurvey;
                case Office.Garrison:     return ServingGarrison;
                default:                  return NoClasses;
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
