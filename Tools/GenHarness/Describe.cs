using System;
using Archivist.Generation;
using Archivist.Generation.Determinism;
using Archivist.Generation.Field;
using Archivist.Generation.Sheets;
using Archivist.Render;
using static Archivist.Harness.Report;

namespace Archivist.Harness
{
    /// <summary>
    /// Not an acceptance check — the thing you read when a render or a cut looks wrong and you
    /// need to know why.
    ///
    /// <para>There used to be two of these, one on each suite, and <see cref="Program"/>'s
    /// <c>describe</c> mode called both: every island printed its header twice, with the survey
    /// list from one copy and the render geometry from the other, and no single place to add a
    /// field to. This is their union, printed once.</para>
    ///
    /// <para>Normalisation (§6.2) and peak count together explain most rendering surprises: an
    /// island with no peaks falls back to the character maximum and its whole ramp shifts.</para>
    /// </summary>
    public static class Describe
    {
        public static void Print(ulong collectionSeed, int index)
        {
            Island isl = Island.FromSeed(Streams.IslandSeed(collectionSeed, index));

            double norm = IslandRenderer.Normalisation(isl);
            int peaks = isl.Features != null ? isl.Features.Peaks.Count : 0;
            double top = peaks > 0 ? isl.Features.Peaks[0].SpotHeightM : 0.0;

            Console.WriteLine();
            Console.WriteLine("island " + index + "  " + isl.Name + "  " + isl.Params.Character
                              + "  radius " + F0(isl.Params.NominalRadius) + " m"
                              + "  seed " + isl.Seed.ToString("X16"));
            Console.WriteLine("  land bbox " + F0(isl.LandBounds.Width) + " x " + F0(isl.LandBounds.Height)
                              + " m   centre " + P(isl.LandBounds.Centre));
            Console.WriteLine("  coast loops " + isl.Coastline.Count
                              + "   peaks " + peaks
                              + "   settlements " + isl.Features.Settlements.Count
                              + "   rivers " + isl.Features.Rivers.Count);
            Console.WriteLine("  " + (peaks > 0
                                  ? "highest peak " + F1(top) + " m"
                                  : "no peaks — normalisation falls back to the character maximum (§6.2)")
                              + "   normalisation used " + F1(norm) + " m"
                              + "   character max " + F1(IslandParams.MaxElevationFor(isl.Params.Character)) + " m");

            for (int i = 0; i < isl.Surveys.Count; i++)
            {
                Survey sv = isl.Surveys[i];
                Console.WriteLine("  " + (sv.Spec.IsWholeIsland ? "whole-island" : sv.Spec.Office.ToString())
                                  + "  " + sv.Spec.Year + "  " + sv.Spec.Scale
                                  + "  rot " + F1(sv.Spec.RotationDeg)
                                  + "  sheets " + sv.SheetCount);
            }
            Console.WriteLine("  total sheets " + isl.TotalSheets);

            RenderRequest overview = RenderRequest.ForIsland(isl, RenderTuning.IslandPreviewPxPerMetre);
            Console.WriteLine("  overview at " + F2(RenderTuning.IslandPreviewPxPerMetre) + " px/m -> "
                              + overview.Width + " x " + overview.Height + " px  ("
                              + F3((long)overview.Width * overview.Height / 1000000.0) + " Mpx)");

            Sheet sheet;
            if (Poc02Acceptance.PickSheet(isl, out sheet))
            {
                RenderRequest req = RenderRequest.ForSheet(sheet, RenderTuning.SheetPxPerPaperMm);
                Console.WriteLine("  sheet " + sheet.Survey.Office + " #" + sheet.Number
                                  + " 1:" + sheet.Survey.Scale.Denominator
                                  + " rot " + F1(sheet.RotationDeg) + " deg at "
                                  + F2(RenderTuning.SheetPxPerPaperMm) + " px/mm -> "
                                  + req.Width + " x " + req.Height + " px  ("
                                  + F3(req.PixelsPerMetre) + " px/m)");
                Console.WriteLine("    sheet frame-rect centre " + P(sheet.FrameRect.Centre)
                                  + "   request area centre " + P(req.Area.Centre));
                Poc02Acceptance.NoteSheetPlacement("desc", sheet, req);
            }
            else
            {
                Console.WriteLine("  no sheets");
            }
        }
    }
}
