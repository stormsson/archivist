namespace Archivist.Generation.Field
{
    /// <summary>
    /// §5.3 — the radial falloff that turns a field of noise into an island.
    ///
    /// <para>Character is <b>not a jittered parameter</b> (R1.7). Each of the three is a different
    /// recipe with a different shape, and they are written out here one by one rather than folded
    /// into a parameterised curve, because folding them would lose exactly the thing R1.7 asks for.</para>
    ///
    /// <para>Returns [0, 1] in every case: <see cref="Smoothstep"/> is clamped, so <c>1 - s</c> is too.</para>
    /// </summary>
    public static class Falloff
    {
        // --- §5.3 recipe constants ---------------------------------------------------------
        //
        // TODO: these should move to Tuning.cs (§12), which is the single home for constants.
        // They are local consts only because Tuning.cs is a frozen contract file in this change.

        /// <summary>Mountainous: land is solid to 0.35 of the nominal radius, gone by 1.00.</summary>
        const double MountainousEdge0 = 0.35;
        const double MountainousEdge1 = 1.00;

        /// <summary>Fjorded: the same ramp started earlier, with an angular cut added to r.</summary>
        const double FjordedEdge0 = 0.30;
        const double FjordedEdge1 = 1.00;

        /// <summary>Fjorded: amplitude and angular frequency of the inlet cut.</summary>
        const double FjordedCutAmplitude = 0.18;
        const double FjordedCutFrequency = 6.00;

        /// <summary>Atoll: the ring sits at 0.62 of the nominal radius and is 0.14 wide either side.</summary>
        const double AtollRingRadius = 0.62;
        const double AtollRingCore   = 0.00;
        const double AtollRingWidth  = 0.14;

        /// <summary>
        /// The falloff multiplier at polar coordinate (<paramref name="r"/>, <paramref name="theta"/>).
        /// <paramref name="r"/> is normalised by <c>NominalRadius</c> (§5.2 step 4);
        /// <paramref name="theta"/> is <c>atan2(y, x)</c> in radians (§5.2 step 5) and is used by
        /// <see cref="IslandCharacter.Fjorded"/> alone — the other two recipes ignore it entirely,
        /// so callers need not compute it for them.
        /// <paramref name="seed"/> seeds the Fjorded angular noise and is unused otherwise.
        ///
        /// <para><b>Fjorded is discontinuous at theta = +/-pi.</b> §5.3 specifies
        /// <c>fbm1(theta * 6.0)</c>, and <c>fbm1</c> is not periodic, so the value at
        /// <c>theta = -pi</c> and at <c>theta = +pi</c> are unrelated draws — a radial seam along
        /// the negative X axis where the cut can jump by up to the full 0.18. Implemented as
        /// specified; flagged for §5.3 rather than silently repaired here, since the fix (giving
        /// <c>fbm1</c> an integer lattice period that divides the circle) changes the recipe.</para>
        /// </summary>
        public static double Evaluate(IslandCharacter character, double r, double theta, ulong seed)
        {
            switch (character)
            {
                case IslandCharacter.Mountainous:
                    // Compact, high relief, one main massif.
                    return 1.0 - Smoothstep(MountainousEdge0, MountainousEdge1, r);

                case IslandCharacter.Fjorded:
                {
                    // The angular term pushes the coast in and out with theta, producing inlets;
                    // where the field dips below sea level mid-island, islets detach naturally.
                    double cut = FjordedCutAmplitude * Noise.Fbm1(theta * FjordedCutFrequency, seed);
                    return 1.0 - Smoothstep(FjordedEdge0, FjordedEdge1, r + cut);
                }

                case IslandCharacter.Atoll:
                default:
                {
                    // A ring, not a disc. The lagoon interior falls below sea level, so the
                    // coastline extracts as TWO closed loops — outer shore and lagoon shore. That
                    // is the point of the recipe (§5.3) and the reason atoll is in the set: the
                    // contour code must handle multiple loops (§6.1). It must never be smoothed
                    // into one loop, and the lagoon must never be filled in.
                    double d = r - AtollRingRadius;
                    if (d < 0.0)
                    {
                        d = -d;
                    }
                    return 1.0 - Smoothstep(AtollRingCore, AtollRingWidth, d);
                }
            }
        }

        /// <summary>
        /// The classic Hermite smoothstep, clamped: 0 at or below <paramref name="edge0"/>,
        /// 1 at or above <paramref name="edge1"/>, <c>t*t*(3-2t)</c> between.
        /// Multiply, add and one divide — no transcendentals (§4.4).
        /// </summary>
        public static double Smoothstep(double edge0, double edge1, double x)
        {
            double span = edge1 - edge0;
            if (span == 0.0)
            {
                // Degenerate ramp: a step at the shared edge. Stated so it is not a NaN.
                return x < edge0 ? 0.0 : 1.0;
            }

            double t = (x - edge0) / span;
            if (t <= 0.0)
            {
                return 0.0;
            }
            if (t >= 1.0)
            {
                return 1.0;
            }
            return t * t * (3.0 - 2.0 * t);
        }
    }
}
