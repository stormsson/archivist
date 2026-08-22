using System;

namespace Archivist.Harness
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            string mode = args.Length > 0 ? args[0] : "all";
            string outDir = args.Length > 1 ? args[1] : null;      // B5's PNG folder; optional
            Console.WriteLine("Archivist acceptance harness  (POC-01 §13, POC-02 §11)");
            Console.WriteLine("======================================================");

            if (mode == "all" || mode == "fast")
            {
                Acceptance.A2_Determinism();
                Acceptance.A3_NoSeams();
                Acceptance.A4_Numbering();
                Acceptance.A5_NoBlankSheets();
                Acceptance.A6_SharedClassCoverage();
                Acceptance.A8_Performance();

                Console.WriteLine();
                Console.WriteLine("-- POC-03 points of interest ---------------------------");
                Poc03Acceptance.C2_Determinism();
                Poc03Acceptance.C3_PlaceabilityFloor();
                Poc03Acceptance.C4_Numbering();
            }
            if (mode == "all" || mode == "metrics")
            {
                Acceptance.A7_SheetEconomy(mode == "all" ? 50 : 50);
                Poc03Acceptance.C6_Density(50);
            }

            // POC-02 §11. Only B2 and B3 gate, so only those two join `all`; B4 and B5 are
            // measurements (and B5 writes megabytes of PNG), so they need the poc02 mode.
            if (mode == "all" || mode == "poc02")
            {
                Console.WriteLine();
                Console.WriteLine("-- POC-02 rendering ------------------------------------");
                Poc02Acceptance.B2_Determinism();
                Poc02Acceptance.B3_Coherence();
            }
            if (mode == "poc02")
            {
                Poc02Acceptance.B4_Performance();
                Poc02Acceptance.B5_ResolutionSweep(outDir);
            }
            if (mode == "sweep")
            {
                Poc02Acceptance.B5_ResolutionSweep(outDir);
            }

            if (mode == "describe")
            {
                // One Describe, not one per suite: the two used to print the same island header
                // twice, each with half the fields.
                for (int i = 0; i < 6; i++) Describe.Print(Report.Collection, i);
            }

            // One flag for the whole harness (Report.Failed). This used to be three separate
            // flags OR'd together here, so a new suite that forgot to be added exited 0.
            bool failed = Report.Failed;
            Console.WriteLine();
            Console.WriteLine(failed ? "RESULT: FAILURES PRESENT" : "RESULT: all gated checks pass");
            return failed ? 1 : 0;
        }
    }
}
