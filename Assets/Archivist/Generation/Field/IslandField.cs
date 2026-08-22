using System;
using Archivist.Generation.Determinism;
using Archivist.Generation.Geometry;

namespace Archivist.Generation.Field
{
    /// <summary>
    /// §5.2 — the island as an analytic height field. The island is <c>f(x, y)</c> and never a
    /// grid (§3): contouring is a query against this object, so nothing geometric is cached or
    /// persisted here (R3.1). The only thing held per instance is the derived seed set; the only
    /// thing cached anywhere is the per-seed noise table inside <see cref="Noise"/>, which is a
    /// cache and not state.
    ///
    /// <para>The domain is the <c>DomainMetres</c> square centred on the origin, and the island is
    /// centred on the origin too — <c>r</c> in step 4 is measured from <c>(0, 0)</c>, which is the
    /// same origin the §6.2 contour lattice and the §6.4 Garrison grid are measured from.</para>
    /// </summary>
    public sealed class IslandField : IHeightField
    {
        // §5.2 step 2 — "o1, o2 are fixed distinct offsets". Fixed, arbitrary, non-integer, and
        // far enough apart that the two warp components are independent samples of the same fbm.
        const double WarpOffset1X = 137.331;
        const double WarpOffset1Y =  71.907;
        const double WarpOffset2X = -53.417;
        const double WarpOffset2Y = 219.643;

        readonly IslandParams _params;

        /// <summary>Seed of the terrain fbm — both warp components and the base layer (§5.2 steps 2-3).</summary>
        readonly ulong _fieldSeed;

        /// <summary>Seed of the Fjorded angular term (§5.3). Separate purpose, separate stream (§4.3).</summary>
        readonly ulong _falloffSeed;

        // Hoisted out of the inner loop. All are pure functions of Params, so caching them changes
        // nothing; they are per-character recipe constants (§5.3), read through IslandParams.
        readonly IslandCharacter _character;
        readonly double _gain;
        readonly double _bias;
        readonly double _invNominalRadius;
        readonly double _seaLevel;
        readonly double _maxElevation;
        readonly bool _usesTheta;

        /// <summary>
        /// Everything is derived from <c>p.Seed</c> — nothing else is stored, and nothing else is
        /// needed to reproduce the island (R1.1, R1.11).
        /// </summary>
        public IslandField(IslandParams p)
        {
            _params = p;

            // One stream per purpose, drawn independently (§4.3): adding a purpose later must not
            // reshuffle these. Never System.Random or UnityEngine.Random (§4.1).
            _fieldSeed = SeedFrom(p.Seed, "field");
            _falloffSeed = SeedFrom(p.Seed, "falloff");

            _character = p.Character;
            _gain = IslandParams.GainFor(p.Character);
            _bias = IslandParams.BiasFor(p.Character);
            _invNominalRadius = 1.0 / p.NominalRadius;
            _seaLevel = p.SeaLevel;
            _maxElevation = p.MaxElevation;

            // Only Fjorded reads theta (§5.3), and atan2 is the most expensive call in the
            // composition, so the other two characters never pay for it.
            _usesTheta = p.Character == IslandCharacter.Fjorded;
        }

        public IslandParams Params
        {
            get { return _params; }
        }

        /// <summary>
        /// §5.2 step 7 / D3. The <b>quantised</b> normalised height, sea level 0.50.
        ///
        /// <para>This is the one scalar every threshold in the codebase compares — marching-squares
        /// corner signs and the saddle centre-sign rule (§6.1), <c>landFraction</c>,
        /// <c>Elevation &lt; -4 m</c>, the §7.4 relief test — and quantising it at <c>2^-16</c> is
        /// what makes all of them reproducible across platforms (§4.4). It covers every
        /// transcendental in the composition at once, which is why the rejected alternative of
        /// quantising <c>theta</c> is not what happens here: that would fix one intermediate, leave
        /// every other float path exposed, and band the fjord coast visibly at lod 6.</para>
        ///
        /// <para>A tie at exactly <c>SeaLevel</c> counts as <b>land</b> (§4.4). See
        /// <see cref="HeightFieldExtensions"/>, which is the
        /// one place that is written down.</para>
        /// </summary>
        public double Height01(double x, double y)
        {
            return Q.H01(RawH01(x, y));
        }

        /// <summary>
        /// §5.2. Metres, negative below sea. Derived from the <b>quantised</b>
        /// <see cref="Height01"/>, so it inherits its reproducibility: the quantum is about 2 cm of
        /// elevation, invisible at every scale in §8.1.
        /// </summary>
        public double Elevation(double x, double y)
        {
            return ElevationFrom(Height01(x, y));
        }

        /// <summary>
        /// Height01 and Elevation from a single composition. Bit-identical to calling both
        /// separately — same RawH01, same Q.H01, same ElevationFrom — but half the cost,
        /// which matters at one call per pixel.
        /// </summary>
        public double Sample(double x, double y, out double elevation)
        {
            double h01 = Q.H01(RawH01(x, y));
            elevation = ElevationFrom(h01);
            return h01;
        }

        /// <summary>
        /// §5.2 / D3. <c>d(Elevation) / d(distance)</c> in <b>metres per metre</b> — a slope, not a
        /// normalised difference — by central difference at <c>Tuning.GradientStep</c> (20 m).
        ///
        /// <para>This is the <b>one exemption</b> to the quantisation rule (§4.4): it is computed
        /// from the <b>unquantised</b> composition, because a 2 cm staircase across a 40 m
        /// difference span would be coarse — 5e-4 of slope, an eighth of §7.2's whole threshold.</para>
        ///
        /// <para><b>Callers must round before they branch.</b> The exemption is paid for by the
        /// caller: <c>|Gradient|</c> is rounded to 1e-4 with <see cref="Q.Grad"/> before any
        /// comparison — §7.2's <c>&lt; 0.04</c> (a slope of about 2.3 degrees) is its only branch
        /// in this POC. This method deliberately does not do that rounding, because rounding the
        /// components rather than the magnitude would round the wrong quantity.</para>
        /// </summary>
        public V2 Gradient(double x, double y)
        {
            double h = Tuning.GradientStep;
            double inv2h = 1.0 / (2.0 * h);

            double ex1 = RawElevation(x + h, y);
            double ex0 = RawElevation(x - h, y);
            double ey1 = RawElevation(x, y + h);
            double ey0 = RawElevation(x, y - h);

            return new V2((ex1 - ex0) * inv2h, (ey1 - ey0) * inv2h);
        }

        /// <summary>
        /// Axis-aligned bounding box of land over the whole generation domain, sampled on the
        /// <c>Tuning.BaseCell</c> (64 m) lattice — the lod-0 root of the §6.2 lattice, so the
        /// samples are a subset of the same global lattice every contour uses.
        ///
        /// <para>Land is tested with the quantised <see cref="Height01"/> against <c>SeaLevel</c>,
        /// ties counting as land (§4.4). The result feeds §8.1 / D5, which picks the smallest of
        /// <c>{1:25000, 1:50000}</c> whose map area contains this box in either orientation.</para>
        ///
        /// <para>Returns <see cref="Rect2.Empty"/> (which reports <c>IsEmpty</c>) if the seed
        /// produced no land at all on the lattice. Callers must handle that rather than assume a
        /// box: it is reachable, most plausibly for a thin atoll ring.</para>
        ///
        /// <para>Not cached — R3.1. It is ~251 x 251 samples on the default domain; call it once
        /// and keep the answer in the caller if it is needed twice.</para>
        /// </summary>
        public Rect2 ComputeLandBounds()
        {
            double cell = Tuning.BaseCell;
            double half = _params.DomainMetres * 0.5;
            Rect2 domain = new Rect2(-half, -half, half, half).SnapOut(cell);

            // Integer step counts, not floating accumulation: the sample points must land exactly
            // on multiples of cell measured from the domain origin (§6.2).
            int nx = (int)Math.Round(domain.Width / cell);
            int ny = (int)Math.Round(domain.Height / cell);

            Rect2 bounds = Rect2.Empty;
            for (int iy = 0; iy <= ny; iy++)
            {
                double y = domain.MinY + iy * cell;
                for (int ix = 0; ix <= nx; ix++)
                {
                    double x = domain.MinX + ix * cell;
                    if (Height01(x, y) >= _seaLevel)
                    {
                        bounds = bounds.Encapsulate(new V2(x, y));
                    }
                }
            }

            return bounds;
        }

        // --- composition -------------------------------------------------------------------

        /// <summary>
        /// §5.2 steps 1-6, <b>unquantised</b>. Step 7 lives in <see cref="Height01"/>.
        ///
        /// <para>Internal on purpose: the only legitimate consumer of the unquantised value is
        /// <see cref="Gradient"/> (§4.4 carve-out). Everything that branches must go through
        /// <see cref="Height01"/>, or the reproducibility guarantee is lost.</para>
        /// </summary>
        double RawH01(double x, double y)
        {
            // 1. p = (x, y) / featureScale
            double px = x / Tuning.FeatureScale;
            double py = y / Tuning.FeatureScale;

            // 2. w = p + warpAmp * ( fbm(p + o1), fbm(p + o2) )
            //    fbm is [0, 1], so the warp is a one-sided displacement in [0, warpAmp]. That is
            //    what §5.2 says; a domain translation costs nothing, since the noise has no
            //    privileged origin.
            double wx = px + Tuning.WarpAmp * Noise.Fbm(px + WarpOffset1X, py + WarpOffset1Y, _fieldSeed);
            double wy = py + Tuning.WarpAmp * Noise.Fbm(px + WarpOffset2X, py + WarpOffset2Y, _fieldSeed);

            // 3. n = fbm(w) -> [0, 1]
            double n = Noise.Fbm(wx, wy, _fieldSeed);

            // 4. r = |(x, y)| / NominalRadius
            double r = Math.Sqrt(x * x + y * y) * _invNominalRadius;

            // 5. f = Falloff(character, r, atan2(y, x)) -> [0, 1]
            double theta = _usesTheta ? Math.Atan2(y, x) : 0.0;
            double f = Falloff.Evaluate(_character, r, theta, _falloffSeed);

            // 6. h01 = saturate( (n * f) * gainC + biasC )
            double h01 = (n * f) * _gain + _bias;
            return h01 < 0.0 ? 0.0 : (h01 > 1.0 ? 1.0 : h01);
        }

        /// <summary>Elevation from the unquantised composition. <see cref="Gradient"/> only (§4.4).</summary>
        double RawElevation(double x, double y)
        {
            return ElevationFrom(RawH01(x, y));
        }

        /// <summary>
        /// §5.2: <c>(h01 - SeaLevel) / (1 - SeaLevel) * MaxElevation</c> above sea,
        /// <c>(h01 - SeaLevel) / SeaLevel * MaxDepth</c> below. Both branches are 0 at sea level,
        /// so the field is continuous across the coast; below sea the numerator is negative, which
        /// is what makes the depth negative.
        /// </summary>
        /// <summary>
        /// Elevation for an already-known Height01. Public so a renderer that INTERPOLATES
        /// h01 between coarse samples can convert without re-evaluating the field.
        /// </summary>
        public double ElevationFrom(double h01)
        {
            if (h01 >= _seaLevel)
            {
                return (h01 - _seaLevel) / (1.0 - _seaLevel) * _maxElevation;
            }
            return (h01 - _seaLevel) / _seaLevel * Tuning.MaxDepth;
        }

        /// <summary>
        /// A 64-bit noise seed from a named sub-stream (§4.3). Two draws off the stream, high word
        /// then low word, so the whole 64 bits vary with the purpose and the island seed.
        /// </summary>
        static ulong SeedFrom(ulong islandSeed, string purpose)
        {
            Pcg32 rng = Streams.For(islandSeed, purpose);
            ulong hi = rng.NextUInt();
            ulong lo = rng.NextUInt();
            return (hi << 32) | lo;
        }
    }
}
