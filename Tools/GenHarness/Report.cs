using System;
using System.Globalization;
using Archivist.Generation.Geometry;

namespace Archivist.Harness
{
    /// <summary>
    /// The harness's one output channel and its one failure flag.
    ///
    /// <para>Every suite in this harness used to declare its own <c>Collection</c>,
    /// <c>Inv</c>, <c>Failed</c> and a byte-identical set of <c>Pass</c>/<c>Fail</c>/
    /// <c>Info</c>/<c>Metric</c> helpers. Three copies of the printing is merely tedious;
    /// three copies of <c>Failed</c> is a hazard — <see cref="Program"/> had to remember to OR
    /// all three together, and a fourth suite that forgot to be added there would have failed
    /// silently with an exit code of 0.</para>
    ///
    /// <para>Suites pull these in with <c>using static Archivist.Harness.Report;</c>, so call
    /// sites read exactly as they did before.</para>
    /// </summary>
    public static class Report
    {
        /// <summary>
        /// The collection seed every suite reports on, so POC-01, POC-02 and POC-03 are all
        /// talking about the same islands. The Unity test assembly cannot see this constant and
        /// keeps its own copy in <c>Archivist.Tests.TestSeeds</c>; those two are the only two
        /// places the literal 8412 may appear.
        /// </summary>
        public const ulong Collection = 8412UL;

        /// <summary>Every number this harness prints goes through invariant culture: this
        /// machine's current culture prints decimal commas, which would make recorded numbers
        /// unmatchable against a run elsewhere.</summary>
        public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Set by <see cref="Fail"/>, read once by <see cref="Program"/> for the exit
        /// code. One flag for the whole harness — never one per suite.</summary>
        public static bool Failed;

        public static void Pass(string id, string msg)   { Console.WriteLine("  PASS  " + id + "  " + msg); }
        public static void Fail(string id, string msg)   { Console.WriteLine("  FAIL  " + id + "  " + msg); Failed = true; }
        public static void Info(string msg)              { Console.WriteLine("        " + msg); }
        public static void Metric(string id, string msg) { Console.WriteLine("  ----  " + id + "  " + msg); }

        public static string F(double d)  { return d.ToString("F6", Inv); }
        public static string F0(double d) { return d.ToString("F0", Inv); }
        public static string F1(double d) { return d.ToString("F1", Inv); }
        public static string F2(double d) { return d.ToString("F2", Inv); }
        public static string F3(double d) { return d.ToString("F3", Inv); }

        /// <summary>A ground point. <c>V2.ToString()</c> formats with the CURRENT culture, so it
        /// is never used for output here.</summary>
        public static string P(V2 v) { return "(" + F1(v.X) + ", " + F1(v.Y) + ")"; }
    }
}
