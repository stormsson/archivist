using System;

namespace Archivist.Generation.Determinism
{
    /// <summary>FNV-1a 64. Contractually stable: never use string.GetHashCode (§4.1).</summary>
    public static class Hash
    {
        public const ulong FnvOffset = 14695981039346656037UL;
        public const ulong FnvPrime  = 1099511628211UL;

        public static ulong Fnv1a64(string ascii)
        {
            return Fnv1a64(FnvOffset, ascii);
        }

        public static ulong Fnv1a64(ulong seed, string ascii)
        {
            ulong h = seed;
            for (int i = 0; i < ascii.Length; i++)
            {
                // ASCII only, by contract. Non-ASCII input is a caller error.
                h ^= (byte)ascii[i];
                h *= FnvPrime;
            }
            return h;
        }

        public static ulong Fnv1a64(ulong seed, ulong value)
        {
            ulong h = seed;
            for (int i = 0; i < 8; i++)
            {
                h ^= (value >> (i * 8)) & 0xFF;
                h *= FnvPrime;
            }
            return h;
        }

        /// <summary>SplitMix64 finaliser over a + golden-ratio-stepped b. Avalanches well.</summary>
        public static ulong Mix(ulong a, ulong b)
        {
            ulong z = a + 0x9E3779B97F4A7C15UL + (b * 0xBF58476D1CE4E5B9UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
