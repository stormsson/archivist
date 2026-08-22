using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Archivist.Harness
{
    /// <summary>
    /// Median-of-N timing, the one place in this repo that is allowed to look at a clock.
    ///
    /// <para>§5's no-wall-clock rule binds Generation and Render; it does not bind the harness,
    /// which is why this helper lives here and NOT beside the other shared checks in
    /// <c>Generation/Analysis</c>. A <see cref="Stopwatch"/> in the Generation assembly would
    /// trip <c>Tools/check-sources.sh</c>, and rightly so.</para>
    ///
    /// <para>The loop was written three times: A8's two loops and POC-02's
    /// <c>MedianRenderMs</c>. Only the render one warmed up first, and its comment explained
    /// exactly why that matters — so A8's first sample was paying for JIT and A8 was measuring
    /// something slightly different from B4 while claiming the same units.</para>
    /// </summary>
    public static class Timing
    {
        /// <summary>
        /// Runs <paramref name="body"/> <paramref name="reps"/> times, timing each, and returns
        /// the median in milliseconds.
        ///
        /// <para><paramref name="body"/> receives the repetition index, so a caller that wants a
        /// different input per sample (A8 generates a different island each time) can vary it
        /// while a caller that repeats one identical call (B4 re-renders one request) can ignore
        /// it.</para>
        /// </summary>
        /// <param name="reps">Number of timed samples. Must be at least one.</param>
        /// <param name="warm">
        /// Run <c>body(0)</c> once, untimed, before measuring. This is not optional politeness:
        /// the first call pays for JIT compilation and for whatever tables the code under test
        /// builds lazily, and with a small <paramref name="reps"/> that one outlier can drag the
        /// median. Pass false only when the cold cost is deliberately part of what is measured.
        /// </param>
        /// <param name="body">The work to time.</param>
        public static double MedianMs(int reps, bool warm, Action<int> body)
        {
            if (body == null) throw new ArgumentNullException("body");
            if (reps < 1) throw new ArgumentOutOfRangeException("reps");

            if (warm) body(0);

            var times = new List<double>(reps);
            var sw = new Stopwatch();
            for (int i = 0; i < reps; i++)
            {
                sw.Restart();
                body(i);
                sw.Stop();
                times.Add(sw.Elapsed.TotalMilliseconds);
            }
            times.Sort();
            return times[times.Count / 2];
        }
    }
}
