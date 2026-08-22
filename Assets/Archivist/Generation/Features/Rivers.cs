using System;
using System.Collections.Generic;
using Archivist.Generation.Determinism;
using Archivist.Generation.Field;
using Archivist.Generation.Geometry;

namespace Archivist.Generation.Features
{
    /// <summary>
    /// §7.3. One river traced per peak, in peak order, each from its own PRNG sub-stream
    /// <c>Streams.For(seed, "rivers", peakIndex)</c> — so adding or losing a peak cannot
    /// reshuffle any other river's jitter (§4.3, asserted by A2 in §13.2).
    /// </summary>
    public static class Rivers
    {
        /// <summary>
        /// §7.3 steps 1-5. Gradient descent from each peak at <see cref="Tuning.RiverStep"/>
        /// with lateral jitter of +/- <see cref="Tuning.RiverJitterRad"/>, terminating on the
        /// sea, on a merge within <see cref="Tuning.RiverMergeDist"/> of an existing river, or
        /// at <see cref="Tuning.RiverMaxSteps"/>; courses shorter than
        /// <see cref="Tuning.RiverMinLength"/> are discarded.
        /// <para>
        /// Atolls have almost no relief, so every course they produce falls under the minimum
        /// length and the list comes back empty. That is §7.3 step 5 falling out of the
        /// algorithm, not a special case — there is no atoll branch here.
        /// </para>
        /// </summary>
        public static List<River> Generate(IHeightField field, IReadOnlyList<Peak> peaks)
        {
            if (field == null) throw new ArgumentNullException("field");

            List<River> result = new List<River>();
            if (peaks == null || peaks.Count == 0) return result;

            ulong seed = field.Params.Seed;
            double mergeDist2 = Tuning.RiverMergeDist * Tuning.RiverMergeDist;

            // Only courses that survive the minimum-length test become "existing rivers" for the
            // merge test: a discarded trace is not a river, so it cannot be flowed into. Peaks are
            // walked in order, so this is decided before the next trace starts.
            List<V2[]> existing = new List<V2[]>();
            List<Rect2> existingBounds = new List<Rect2>();

            for (int pi = 0; pi < peaks.Count; pi++)
            {
                Pcg32 rng = Streams.For(seed, StreamNames.Rivers, pi);

                List<V2> course = new List<V2>();
                V2 p = peaks[pi].Position;
                course.Add(p);

                for (int step = 0; step < Tuning.RiverMaxSteps; step++)
                {
                    // Gradient points uphill; rivers go the other way.
                    V2 g = field.Gradient(p.X, p.Y);
                    double gl = g.Length;
                    if (gl <= 0.0) break;                       // dead flat: nowhere to descend

                    V2 downhill = new V2(-g.X / gl, -g.Y / gl);

                    // The jitter uses sin/cos, but no branch reads the position directly: the two
                    // termination tests read Height01 (quantised, §4.4) and an integer-free
                    // distance comparison whose operands are ~1e-13 m apart at worst. Same margin
                    // argument as D3 — ten orders below anything that can flip a decision.
                    double jitter = rng.Range(-Tuning.RiverJitterRad, Tuning.RiverJitterRad);
                    V2 next = p + downhill.RotateRad(jitter) * Tuning.RiverStep;

                    course.Add(next);

                    if (!field.IsLand(next)) break;                                   // reached the sea
                    if (NearExisting(next, existing, existingBounds, mergeDist2)) break;  // merged

                    p = next;
                }

                if (course.Count < 2) continue;

                Polyline polyline = new Polyline(course, false);
                if (polyline.Length < Tuning.RiverMinLength) continue;

                result.Add(new River(new FeatureId(FeatureClass.River, result.Count), polyline));

                V2[] pts = new V2[course.Count];
                course.CopyTo(pts);
                existing.Add(pts);
                existingBounds.Add(polyline.Bounds.Expanded(Tuning.RiverMergeDist));
            }

            return result;
        }

        /// <summary>
        /// Merge test of §7.3 step 3. Brute force over the vertices of already-kept rivers, with
        /// a per-river bbox reject in front of it. Bounded by peakCap (at most 9) rivers of at
        /// most <see cref="Tuning.RiverMaxSteps"/> vertices, and rivers rarely share a bbox, so
        /// this stays far inside the §13.8 budget without a spatial index to keep deterministic.
        /// </summary>
        static bool NearExisting(V2 p, List<V2[]> existing, List<Rect2> bounds, double mergeDist2)
        {
            for (int r = 0; r < existing.Count; r++)
            {
                if (!bounds[r].Contains(p)) continue;
                V2[] pts = existing[r];
                for (int i = 0; i < pts.Length; i++)
                {
                    if (V2.DistSq(pts[i], p) < mergeDist2) return true;
                }
            }
            return false;
        }
    }
}
