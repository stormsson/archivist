using System.Collections.Generic;
using Archivist.Generation.Determinism;
using Archivist.Generation.Features;
using Archivist.Generation.Geometry;

namespace Archivist.Generation.Sheets
{
    /// <summary>
    /// POC-03 spec §2 — the detail sheet cutter, beside <see cref="QuarterCutter"/>'s four
    /// fixed rects: <b>one small sheet per qualifying POI, centred on it, seeded rotation, no
    /// tiling</b>.
    ///
    /// <para>What a detail sheet is: one thing, drawn close, with just enough of its
    /// surroundings to say where it was (requirements §1). It gives no position — no grid
    /// reference, no index diagram, no coordinates (P2.3) — and carries no north indication
    /// (P2.6). Where it sits is what the player recovers, once enough of the island has been
    /// assembled from the survey sheets to recognise the ground.</para>
    ///
    /// <para><b>The rule that decides whether the whole idea works</b> is §2.3, implemented in
    /// <see cref="Qualifies"/>.</para>
    /// </summary>
    public static class DetailSheetCutter
    {
        /// <summary>
        /// Spec §2 — one sheet per POI that clears the placeability floor, numbered
        /// <c>1..M</c> in POI order.
        ///
        /// <para>Order: POIs arrive in their canonical generation order (spec §1.3 step 5), the
        /// floor is applied, and the survivors are numbered. Numbering therefore happens AFTER
        /// the cull, exactly as §10.4 requires of survey sheets and for the same reason — a gap
        /// must mean "missing sheet", not "a POI we declined to draw". A4 checks it.</para>
        /// </summary>
        /// <param name="service">
        /// The island's D1 service rule, which is what answers the placeability floor. A null
        /// rule means "served everywhere", so the cutter can be exercised in isolation;
        /// production always passes one.
        /// </param>
        /// <param name="spec">
        /// The Antiquarian survey spec — <see cref="SheetFormat.DetailSheet"/> at
        /// <see cref="MapScale.PoiDetail"/>. Its <c>RotationDeg</c> is nominal: every sheet
        /// carries its own (§2.2).
        /// </param>
        public static Survey Cut(IReadOnlyList<Poi> pois, ServiceRule service, SurveySpec spec)
        {
            List<Sheet> sheets = new List<Sheet>();
            if (pois == null) return new Survey(spec, sheets);

            for (int i = 0; i < pois.Count; i++)
            {
                if (!Qualifies(pois[i], service, spec.Office)) continue;

                sheets.Add(new Sheet(spec, sheets.Count + 1, pois[i].Position,
                                     RotationFor(spec.IslandSeed, i), true));
            }
            return new Survey(spec, sheets);
        }

        /// <summary>
        /// <b>THE PLACEABILITY FLOOR — POC-03 spec §2.3 / P2.4, and C3's gate.</b>
        ///
        /// <code>
        /// A detail sheet must contain at least one drawn feature besides its own POI.
        /// </code>
        ///
        /// <para>A 300 m square of bare hillside is not a puzzle, it is a dead end: every
        /// hillside looks alike. A POI failing this produces <b>no sheet</b>. The POI still
        /// exists on the island — it is simply a thing no expedition managed to fix the position
        /// of, which is a far better outcome than shipping an unplaceable sheet. C6 reports how
        /// many fail, and says that number is the interesting one: a high value means the siting
        /// rules and the placeability floor disagree.</para>
        ///
        /// <para><b>Reuses <see cref="ServiceRule"/>, does not duplicate it.</b> D1 already
        /// answers "does this office draw anything here", per class, on the 64 m lattice. This
        /// is the same question over
        /// <see cref="FeatureMatrix.Placeability"/> — the office's serving set with the POI's
        /// own class removed. Open question 2 ("one contour looks like any other") tightens the
        /// floor to the locally distinctive classes by narrowing that array; nothing in this
        /// method changes.</para>
        /// </summary>
        public static bool Qualifies(Poi poi, ServiceRule service, Office office)
        {
            if (service == null) return true;
            return service.ServedByAny(poi.Position, FeatureMatrix.Placeability(office));
        }

        /// <summary>
        /// Spec §2.2 — rotation is per sheet, seeded from
        /// <c>Streams.For(islandSeed, "poi.sheet", poiIndex)</c>, quantised to 0.1 deg and
        /// normalised to <c>[0, 180)</c> like every other rotation in the generator. A field
        /// sketch has no fixed orientation, and resolving it is part of the placement (P2.6),
        /// so there is nothing to derive it from and it is rolled rather than measured — the one
        /// rotation in the collection that is not derived (contrast D2).
        ///
        /// <para>Indexed by POI, not by sheet number, so a POI that later starts or stops
        /// clearing the placeability floor cannot re-roll another POI's sheet.</para>
        ///
        /// <para>Consequence, already recorded for survey sheets: rotation stored mod 180 leaves
        /// "which way up" undetermined in the data, and only the rendered content resolves it.
        /// Acceptable here for the same reason — the content is asymmetric — but the map table's
        /// fit must not assume a heading.</para>
        /// </summary>
        public static double RotationFor(ulong islandSeed, int poiIndex)
        {
            Pcg32 rng = Streams.For(islandSeed, StreamNames.PoiSheet, poiIndex);
            return NormaliseAxisDeg(rng.Range(0.0, 180.0));
        }

        /// <summary>
        /// An axis angle, folded into [0, 180) and quantised. A rect and the same rect turned
        /// half a turn are the same rect, so 190° and 10° must be one value or two islands with
        /// the same geometry would digest differently.
        /// </summary>
        static double NormaliseAxisDeg(double deg)
        {
            double d = deg % 180.0;
            if (d < 0.0) d += 180.0;

            double q = Q.Deg(d);
            if (q >= 180.0) q -= 180.0;
            return q;
        }
    }
}
