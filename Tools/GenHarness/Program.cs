using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Archivist.Harness
{
    /// <summary>
    /// Command line, run loop, exit code. What to run is <see cref="Suite"/>'s business; this
    /// file only parses, calls in order, times, and reports.
    ///
    /// <para>The timing summary at the end is not decoration. The whole suite is three minutes
    /// and nearly all of it is island generation, so the useful question when you are about to
    /// iterate is "which of these do I actually need" — and that needs the per-check numbers in
    /// front of you, measured, not guessed from the <c>--list</c> cost tags.</para>
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            var selectors = new List<string>();
            var opt = new Suite.Options();
            bool list = false;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--list" || a == "-l") { list = true; }
                else if (a == "--help" || a == "-h") { Usage(); return 0; }
                else if (a.StartsWith("--out=")) { opt.OutDir = a.Substring(6); }
                else if (a.StartsWith("--seeds="))
                {
                    int n;
                    if (!int.TryParse(a.Substring(8), out n) || n < 1)
                    { Console.Error.WriteLine("--seeds needs a positive integer"); return 2; }
                    opt.Seeds = n;
                }
                else if (a.StartsWith("--")) { Console.Error.WriteLine("unknown option " + a); Usage(); return 2; }
                else selectors.Add(a);
            }

            if (list) { List(); return 0; }

            // No selector means the default gate set: everything that can fail, nothing that only
            // prints. It used to mean "all", which pulled in the 50-seed sweeps and made the
            // cheapest possible answer to "is it broken" the most expensive run available.
            if (selectors.Count == 0) selectors.Add("gate");

            List<Suite.Check> plan;
            string error;
            if (!Suite.Resolve(selectors, out plan, out error))
            { Console.Error.WriteLine(error); Usage(); return 2; }

            Console.WriteLine("Archivist acceptance harness  (POC-01 §13, POC-02 §11, POC-03)");
            Console.WriteLine("==============================================================");
            if (plan.Count == 0)
            {
                Console.WriteLine("nothing selected: " + string.Join(" ", selectors.ToArray()));
                return 0;
            }

            var elapsed = new List<double>(plan.Count);
            var failedHere = new List<bool>(plan.Count);
            string group = null;
            bool anyGated = false;
            var whole = Stopwatch.StartNew();

            for (int i = 0; i < plan.Count; i++)
            {
                Suite.Check c = plan[i];
                if (c.Group != group)
                {
                    group = c.Group;
                    Console.WriteLine();
                    Console.WriteLine(Suite.BannerFor(group));
                }
                anyGated |= c.Gates;

                // Report.Failed only ever goes from false to true, so the difference across one
                // call is exactly "did THIS check fail" — no per-check flag needed in Report.
                bool before = Report.Failed;
                var sw = Stopwatch.StartNew();
                c.Run(opt);
                sw.Stop();
                elapsed.Add(sw.Elapsed.TotalSeconds);
                failedHere.Add(Report.Failed && !before);
            }
            whole.Stop();

            Console.WriteLine();
            Console.WriteLine("-- time ------------------------------------------------");
            for (int i = 0; i < plan.Count; i++)
                Console.WriteLine("        " + plan[i].Id.PadRight(9)
                                  + elapsed[i].ToString("F1", Report.Inv).PadLeft(7) + " s"
                                  + (failedHere[i] ? "   FAILED" : ""));
            Console.WriteLine("        " + "total".PadRight(9)
                              + whole.Elapsed.TotalSeconds.ToString("F1", Report.Inv).PadLeft(7) + " s");

            // One flag for the whole harness (Report.Failed). This used to be three separate flags
            // OR'd together here, so a new suite that forgot to be added exited 0.
            bool failed = Report.Failed;
            Console.WriteLine();
            if (failed) Console.WriteLine("RESULT: FAILURES PRESENT");
            else if (anyGated) Console.WriteLine("RESULT: all gated checks pass  (" + Selected(plan) + ")");
            else Console.WriteLine("RESULT: nothing gated in this selection — measurements only");
            return failed ? 1 : 0;
        }

        static string Selected(List<Suite.Check> plan)
        {
            var ids = new List<string>(plan.Count);
            for (int i = 0; i < plan.Count; i++) if (plan[i].Gates) ids.Add(plan[i].Id);
            return ids.Count + " of " + GatedCount() + ": " + string.Join(" ", ids.ToArray());
        }

        static int GatedCount()
        {
            int n = 0;
            for (int i = 0; i < Suite.All.Length; i++) if (Suite.All[i].Gates) n++;
            return n;
        }

        static void List()
        {
            Console.WriteLine("Selectors. A group name, a check id, or an alias; prefix with - to subtract.");
            Console.WriteLine("Cost is a hint — one island generation is ~0.5 s and dominates everything.");
            Console.WriteLine();
            for (int g = 0; g < Suite.Groups.Length; g++)
            {
                Suite.Group grp = Suite.Groups[g];
                Console.WriteLine("  " + grp.Name.PadRight(10) + grp.What);
                for (int i = 0; i < Suite.All.Length; i++)
                {
                    Suite.Check c = Suite.All[i];
                    if (c.Group != grp.Name || c.Id == grp.Name) continue;
                    Console.WriteLine("    " + c.Id.PadRight(8) + Tag(c).PadRight(12) + c.What);
                }
                Console.WriteLine();
            }
            Console.WriteLine("  aliases   all = gen poi render metrics sweep");
            Console.WriteLine("            gate = gen poi render          (the default)");
            Console.WriteLine("            fast = gen poi                 (the old mode name; not fast)");
            Console.WriteLine("            poc02 = render B4 B5           (the old mode name)");
        }

        static string Tag(Suite.Check c)
        {
            string cost = c.Cost == Suite.Cost.Quick ? "quick"
                        : c.Cost == Suite.Cost.Slow ? "slow" : "very slow";
            return c.Gates ? cost : cost + "*";
        }

        static void Usage()
        {
            Console.WriteLine();
            Console.WriteLine("usage:  GenHarness [selector ...] [--seeds=N] [--out=DIR] [--list]");
            Console.WriteLine("        no selector  =  gate  (every check that can fail)");
            Console.WriteLine("        --list       =  the groups, the checks, and what they cost");
            Console.WriteLine("        --seeds=N    =  seeds for the metrics sweeps (default 50)");
            Console.WriteLine("        --out=DIR    =  where B5 writes its PNGs");
            Console.WriteLine("        * in --list marks a check that only measures and can never fail");
        }
    }
}
