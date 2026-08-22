using System.Text;
using Archivist.Generation.Sheets;

namespace Archivist.Generation.Analysis
{
    /// <summary>
    /// The single canonical check that a survey's sheets are numbered <c>1..N</c> (§10.4 —
    /// cull first, then number, so a gap always means "missing sheet").
    ///
    /// <para>This lives in Generation because the rule was written THREE times over two
    /// assemblies, and the three copies did not agree:</para>
    /// <list type="bullet">
    ///   <item>the headless harness's A4 walked 20 seeds with a <c>bool[] seen</c> set test;</item>
    ///   <item>the Unity test <c>CutterTests.SheetNumbersAreContiguousFromOne</c> repeated that
    ///         loop line for line over 10 seeds;</item>
    ///   <item>POC-03's C4 asserted <c>Sheets[k].Number == k + 1</c>.</item>
    /// </list>
    ///
    /// <para><b>The positional form is the stronger one.</b> The set form asks only that the
    /// numbers, as a multiset, are exactly <c>1..N</c>; the positional form additionally pins
    /// them to list order. A cutter emitting the sheets of a two-sheet survey as
    /// <c>{2, 1}</c> passes the set form and fails the positional one. Both are real rules, so
    /// <paramref name="requirePositional"/> selects which is being asserted rather than one
    /// silently standing in for the other — and every caller keeps the strictness it had.</para>
    /// </summary>
    public static class SheetNumbering
    {
        /// <summary>
        /// Checks <paramref name="survey"/>'s sheet numbers.
        ///
        /// <para>Always asserted: every <see cref="Sheet.Number"/> lies in <c>1..SheetCount</c>
        /// and no two sheets share one. With <c>SheetCount</c> sheets carrying
        /// <c>SheetCount</c> distinct values drawn from <c>1..SheetCount</c>, that is
        /// contiguity — the run has no gaps and no duplicates.</para>
        ///
        /// <para>With <paramref name="requirePositional"/>, additionally
        /// <c>Sheets[k].Number == k + 1</c>: the numbering must follow list order.</para>
        /// </summary>
        /// <param name="survey">The survey to check. A null survey fails rather than throwing,
        /// so a caller sweeping many islands can report it like any other defect.</param>
        /// <param name="requirePositional">True for the stronger, order-pinning form.</param>
        /// <param name="why">On failure, the first defect found, naming the sheet. Null on success.</param>
        /// <returns>True when the numbering satisfies the selected form.</returns>
        public static bool Validate(Survey survey, bool requirePositional, out string why)
        {
            why = null;
            if (survey == null) { why = "survey is null"; return false; }

            int n = survey.SheetCount;
            // Indexed by sheet number, so 1..n are usable slots; index 0 is never touched.
            var seen = new bool[n + 1];

            for (int k = 0; k < survey.Sheets.Count; k++)
            {
                int num = survey.Sheets[k].Number;
                if (num < 1 || num > n)
                {
                    why = Where(survey, k) + "number " + num + " outside 1.." + n;
                    return false;
                }
                if (seen[num])
                {
                    why = Where(survey, k) + "duplicate sheet number " + num;
                    return false;
                }
                seen[num] = true;

                if (requirePositional && num != k + 1)
                {
                    why = Where(survey, k) + "number " + num + " is not its position " + (k + 1)
                          + " (numbering must follow list order)";
                    return false;
                }
            }
            return true;
        }

        /// <summary>Prefix identifying which sheet of which survey failed, so a sweep over many
        /// islands reports something a reader can go and look at.</summary>
        static string Where(Survey survey, int k)
        {
            var sb = new StringBuilder();
            sb.Append(survey.Spec.IsWholeIsland ? "whole-island" : survey.Spec.Office.ToString());
            sb.Append(" survey, sheet index ").Append(k).Append(" of ").Append(survey.SheetCount).Append(": ");
            return sb.ToString();
        }
    }
}
