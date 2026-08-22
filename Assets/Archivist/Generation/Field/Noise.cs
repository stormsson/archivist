using System;
using System.Collections.Generic;
using Archivist.Generation.Determinism;

namespace Archivist.Generation.Field
{
    /// <summary>
    /// Our own gradient (Perlin-style) noise. §5.2 requires the implementation to be ours so
    /// that it is version-stable: no external noise library, no <c>UnityEngine.Mathf.PerlinNoise</c>,
    /// nothing whose output could change under us between runtime versions.
    ///
    /// Determinism (§4.1, §4.4):
    /// <list type="bullet">
    ///   <item>The inner loop is multiply / add / lerp only. The single non-elementary operation
    ///         in the whole file is the <c>Math.Sqrt</c> used to normalise the gradient table,
    ///         and that runs once per seed, outside the loop. IEEE-754 <c>+ - * / sqrt</c> are
    ///         deterministic across platforms (§4.4); transcendentals are not, so there are none.</item>
    ///   <item>The 256-entry gradient table and the 512-entry permutation are built from the seed
    ///         with <see cref="Hash.Mix"/> — never <c>System.Random</c>, never
    ///         <c>string.GetHashCode</c> (§4.1).</item>
    ///   <item>The table cache is a <em>cache</em>, not state. Evicting it re-derives an identical
    ///         table from the same seed, so cache contents can never change a result. Its
    ///         <see cref="Dictionary{TKey,TValue}"/> is only ever probed by key — its iteration
    ///         order never drives generation (§4.1).</item>
    /// </list>
    ///
    /// This is the hottest code in the project (§13.8: island generation &lt; 250 ms, one sheet
    /// re-contoured at 1:5000 &lt; 50 ms). Nothing here allocates after the per-seed table is built.
    /// </summary>
    public static class Noise
    {
        // --- normalisation constants -------------------------------------------------------

        /// <summary>
        /// 2D gradient noise built on unit-length gradients and a quintic fade peaks at
        /// sqrt(2)/2 ~= 0.7071, so the raw lattice value is scaled by sqrt(2) to reach [-1, 1].
        /// Literal rather than <c>Math.Sqrt(2.0)</c> so the constant is fixed in the source.
        /// </summary>
        const double Sqrt2 = 1.4142135623730951;

        /// <summary>
        /// 1D gradient noise on gradients of +/-1 peaks at exactly 0.5 (at t = 0.5 between
        /// opposed gradients), so the raw value is scaled by 2 to reach [-1, 1].
        /// </summary>
        const double Norm1D = 2.0;

        /// <summary>Table size. Also the lattice period, which is far outside any coordinate we use.</summary>
        const int TableSize = 256;
        const int TableMask = 255;

        /// <summary>Upper bound on octaves the offset tables can serve. Tuning.FbmOctaves is 5 (§12).</summary>
        const int MaxOctaves = 8;

        // Fixed, arbitrary, non-integer per-octave lattice offsets. One gradient table serves every
        // octave (this is the standard fbm construction and keeps the per-seed build to one table);
        // the offsets keep the octaves from sampling correlated regions of that one lattice.
        // None of these pairs differ by a multiple of the 256-cell period in both axes at once.
        static readonly double[] OctaveOffsetX =
        {
              0.000,  41.317, 137.913, 269.451, 313.727, 419.283, 523.641, 631.109
        };

        static readonly double[] OctaveOffsetY =
        {
              0.000,  91.773, 211.259, 347.881, 457.339, 563.917, 677.483, 787.061
        };

        static readonly double[] OctaveOffset1D =
        {
              0.000,  53.219, 149.677, 281.443, 397.081, 503.929, 619.373, 733.847
        };

        // Salts. Distinct so the permutation and the gradients are independent draws off one seed.
        const ulong PermSalt  = 0x5011E1D6A11D0C0FUL;
        const ulong Grad2Salt = 0x9E2B7A4C13F5D081UL;
        const ulong Grad1Salt = 0x27C4A9E60B3D7115UL;

        // --- public API --------------------------------------------------------------------

        /// <summary>
        /// One octave of 2D gradient noise. Range [-1, 1], value 0 on every lattice corner.
        /// §5.2: gradients come from a 256-entry table indexed through <see cref="Hash.Mix"/>.
        /// </summary>
        public static double Gradient2D(double x, double y, ulong seed)
        {
            return Gradient2D(x, y, GetTable(seed));
        }

        /// <summary>
        /// Fractal Brownian motion over <see cref="Gradient2D"/>. Octaves, lacunarity and gain all
        /// come from <see cref="Tuning"/> (5 / 2.0 / 0.5, §12).
        ///
        /// <para><b>The remap.</b> The octave sum divided by the sum of the amplitudes is in
        /// [-1, 1] — each octave is in [-1, 1] and the divisor is exactly the weight total. The
        /// natural range is remapped to [0, 1] explicitly, once, at the end:</para>
        /// <code>
        /// signed = SUM(amp_i * Gradient2D(...)) / SUM(amp_i)      in [-1, 1]
        /// fbm    = 0.5 + 0.5 * signed                             in [0, 1]
        /// </code>
        /// <para>With the default 5 octaves at gain 0.5 the divisor is 1.9375. The clamp at the end
        /// only defends the contract against rounding at the extremes; it is not doing the remap.
        /// The distribution is centred on 0.5, which is why <c>SeaLevel</c> is 0.50 (§12).</para>
        /// </summary>
        public static double Fbm(double x, double y, ulong seed)
        {
            Table table = GetTable(seed);

            double amplitude = 1.0;
            double frequency = 1.0;
            double sum = 0.0;
            double weight = 0.0;

            int octaves = Tuning.FbmOctaves;
            for (int o = 0; o < octaves; o++)
            {
                int k = o & (MaxOctaves - 1);
                sum += amplitude * Gradient2D(x * frequency + OctaveOffsetX[k],
                                              y * frequency + OctaveOffsetY[k],
                                              table);
                weight += amplitude;
                amplitude *= Tuning.FbmGain;
                frequency *= Tuning.FbmLacunarity;
            }

            double value = 0.5 + 0.5 * (sum / weight);
            return value < 0.0 ? 0.0 : (value > 1.0 ? 1.0 : value);
        }

        /// <summary>
        /// 1D fbm, range [0, 1]. Used for the Fjorded angular term (§5.3), which needs a
        /// high-frequency function of <c>theta</c> alone. Same octave/lacunarity/gain settings and
        /// the same remap as <see cref="Fbm"/>: <c>0.5 + 0.5 * (sum / SUM(amp))</c>.
        ///
        /// <para><b>Known seam.</b> This function is not periodic. §5.3 evaluates it at
        /// <c>theta * 6.0</c> with <c>theta</c> from <c>atan2</c>, whose branch cut is the negative
        /// X axis, so the Fjorded falloff is discontinuous there. See the note on
        /// <see cref="Falloff.Evaluate"/>.</para>
        /// </summary>
        public static double Fbm1(double t, ulong seed)
        {
            Table table = GetTable(seed);

            double amplitude = 1.0;
            double frequency = 1.0;
            double sum = 0.0;
            double weight = 0.0;

            int octaves = Tuning.FbmOctaves;
            for (int o = 0; o < octaves; o++)
            {
                int k = o & (MaxOctaves - 1);
                sum += amplitude * Gradient1D(t * frequency + OctaveOffset1D[k], table);
                weight += amplitude;
                amplitude *= Tuning.FbmGain;
                frequency *= Tuning.FbmLacunarity;
            }

            double value = 0.5 + 0.5 * (sum / weight);
            return value < 0.0 ? 0.0 : (value > 1.0 ? 1.0 : value);
        }

        // --- lattice evaluation ------------------------------------------------------------

        /// <summary>Quintic fade, 6t^5 - 15t^4 + 10t^3 (§5.2). Multiply and add only.</summary>
        static double Fade(double t)
        {
            return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
        }

        /// <summary>
        /// One octave against an already-resolved table. Callers in a loop resolve the table once
        /// and use this overload so the cache probe does not sit in the inner loop.
        /// </summary>
        static double Gradient2D(double x, double y, Table table)
        {
            double fx = Math.Floor(x);
            double fy = Math.Floor(y);

            // Coordinates here are bounded by a few hundred (domain / featureScale, times the
            // highest octave frequency, plus the octave offsets), so the int cast cannot overflow.
            int ix = (int)fx & TableMask;
            int iy = (int)fy & TableMask;

            double tx = x - fx;
            double ty = y - fy;

            double ux = Fade(tx);
            double uy = Fade(ty);

            int[] perm = table.Perm;
            int a = perm[ix] + iy;
            int b = perm[ix + 1] + iy;

            int g00 = perm[a];
            int g01 = perm[a + 1];
            int g10 = perm[b];
            int g11 = perm[b + 1];

            double[] gx = table.GradX;
            double[] gy = table.GradY;

            double tx1 = tx - 1.0;
            double ty1 = ty - 1.0;

            double n00 = gx[g00] * tx  + gy[g00] * ty;
            double n10 = gx[g10] * tx1 + gy[g10] * ty;
            double n01 = gx[g01] * tx  + gy[g01] * ty1;
            double n11 = gx[g11] * tx1 + gy[g11] * ty1;

            double nx0 = n00 + ux * (n10 - n00);
            double nx1 = n01 + ux * (n11 - n01);
            double n = (nx0 + uy * (nx1 - nx0)) * Sqrt2;

            return n < -1.0 ? -1.0 : (n > 1.0 ? 1.0 : n);
        }

        /// <summary>One octave of 1D gradient noise, range [-1, 1].</summary>
        static double Gradient1D(double x, Table table)
        {
            double fx = Math.Floor(x);
            int ix = (int)fx & TableMask;
            double tx = x - fx;
            double u = Fade(tx);

            int[] perm = table.Perm;
            double[] g = table.Grad1;

            double n0 = g[perm[ix]] * tx;
            double n1 = g[perm[ix + 1]] * (tx - 1.0);
            double n = (n0 + u * (n1 - n0)) * Norm1D;

            return n < -1.0 ? -1.0 : (n > 1.0 ? 1.0 : n);
        }

        // --- per-seed table ----------------------------------------------------------------

        /// <summary>
        /// The permutation and gradient tables for one seed. Immutable once built, so it is safe to
        /// hand the same instance to any number of readers.
        /// </summary>
        sealed class Table
        {
            public readonly ulong Seed;

            /// <summary>512 entries, values 0..255, second half mirroring the first (classic Perlin
            /// layout) so <c>perm[perm[ix] + iy + 1]</c> needs no wrap arithmetic in the inner loop.</summary>
            public readonly int[] Perm;

            /// <summary>256 unit-length 2D gradients, split by component for cache-friendly reads.</summary>
            public readonly double[] GradX;
            public readonly double[] GradY;

            /// <summary>256 1D gradients, each exactly +1 or -1 — the unit vectors of one dimension.</summary>
            public readonly double[] Grad1;

            public Table(ulong seed)
            {
                Seed = seed;

                // --- permutation: Fisher-Yates driven entirely by Hash.Mix (§4.1, §4.2) ---
                int[] p = new int[TableSize];
                for (int i = 0; i < TableSize; i++)
                {
                    p[i] = i;
                }

                ulong permSeed = Hash.Mix(seed, PermSalt);
                for (int i = TableSize - 1; i > 0; i--)
                {
                    ulong h = Hash.Mix(permSeed, unchecked((ulong)i));
                    int j = (int)(h % (ulong)(i + 1));   // modulo bias over 2^64 / 256 is unmeasurable
                    int tmp = p[i];
                    p[i] = p[j];
                    p[j] = tmp;
                }

                Perm = new int[TableSize * 2];
                for (int i = 0; i < TableSize * 2; i++)
                {
                    Perm[i] = p[i & TableMask];
                }

                // --- 2D gradients: rejection-sampled in the unit disc, then normalised ---
                // Rejection + sqrt rather than (cos t, sin t): sqrt is deterministic across
                // platforms and cos/sin are not (§4.4). Acceptance is pi/4, so this converges in
                // ~326 draws for 256 gradients, once per seed.
                GradX = new double[TableSize];
                GradY = new double[TableSize];

                ulong gradSeed = Hash.Mix(seed, Grad2Salt);
                for (int i = 0; i < TableSize; i++)
                {
                    ulong h = Hash.Mix(gradSeed, unchecked((ulong)i));
                    ulong attempt = 0UL;
                    double ax, ay, lenSq;
                    while (true)
                    {
                        ax = ToSigned(h);
                        ay = ToSigned(h >> 32);
                        lenSq = ax * ax + ay * ay;
                        if (lenSq <= 1.0 && lenSq >= 1e-8)
                        {
                            break;
                        }
                        attempt++;
                        h = Hash.Mix(h, attempt);
                    }

                    double inv = 1.0 / Math.Sqrt(lenSq);
                    GradX[i] = ax * inv;
                    GradY[i] = ay * inv;
                }

                // --- 1D gradients: +1 / -1 off an independent salt ---
                Grad1 = new double[TableSize];
                ulong grad1Seed = Hash.Mix(seed, Grad1Salt);
                for (int i = 0; i < TableSize; i++)
                {
                    ulong h = Hash.Mix(grad1Seed, unchecked((ulong)i));
                    Grad1[i] = (h & 1UL) == 0UL ? -1.0 : 1.0;
                }
            }

            /// <summary>32 bits of a hash to a double in [-1, 1). Exact: the scale is a power of two.</summary>
            static double ToSigned(ulong bits)
            {
                uint u = unchecked((uint)bits);
                return u * (2.0 / 4294967296.0) - 1.0;   // 2 / 2^32
            }
        }

        // --- table cache -------------------------------------------------------------------
        //
        // A cache, never state. Two seeds are live at once in practice (the field noise and the
        // Fjorded angular noise, §5.2 / §5.3), and they alternate on every single field sample, so
        // the fast path is a small linear scan of object references rather than a hash probe. A
        // reference read or write is atomic, so the scan needs no lock; the miss path does.

        const int HotSlots = 4;
        const int CacheCap = 32;

        static readonly Table[] Hot = new Table[HotSlots];
        static readonly object CacheLock = new object();
        static readonly Dictionary<ulong, Table> Cache = new Dictionary<ulong, Table>();
        static int _hotNext;

        static Table GetTable(ulong seed)
        {
            for (int i = 0; i < HotSlots; i++)
            {
                Table t = Hot[i];
                if (t != null && t.Seed == seed)
                {
                    return t;
                }
            }
            return GetTableSlow(seed);
        }

        static Table GetTableSlow(ulong seed)
        {
            Table table;
            int slot;
            lock (CacheLock)
            {
                if (!Cache.TryGetValue(seed, out table))
                {
                    // Bounded, so a long batch run cannot grow without limit. Eviction costs a
                    // rebuild and nothing else: the rebuilt table is bit-identical (§4.1).
                    if (Cache.Count >= CacheCap)
                    {
                        Cache.Clear();
                    }
                    table = new Table(seed);
                    Cache[seed] = table;
                }
                slot = _hotNext;
                _hotNext = (slot + 1) % HotSlots;
            }
            Hot[slot] = table;
            return table;
        }
    }
}
