namespace Archivist.Generation.Determinism
{
    /// <summary>
    /// PCG-XSH-RR 64/32. Fixed algorithm, fixed constants, no ambient state (§4.1).
    /// Struct by value: copying a Pcg32 forks the stream, which is intentional.
    /// </summary>
    public struct Pcg32
    {
        const ulong Multiplier = 6364136223846793005UL;

        ulong _state;
        readonly ulong _inc;      // stream selector, always odd

        public Pcg32(ulong seed, ulong stream)
        {
            _inc = (stream << 1) | 1UL;
            _state = 0UL;
            NextUInt();
            _state += seed;
            NextUInt();
        }

        public uint NextUInt()
        {
            ulong old = _state;
            _state = unchecked(old * Multiplier + _inc);
            uint xorshifted = (uint)(((old >> 18) ^ old) >> 27);
            int rot = (int)(old >> 59);
            return (xorshifted >> rot) | (xorshifted << ((-rot) & 31));
        }

        /// <summary>[0, 1). 53 bits of mantissa, exactly representable.</summary>
        public double NextDouble()
        {
            ulong hi = NextUInt();
            ulong lo = NextUInt();
            ulong bits = ((hi << 32) | lo) >> 11;          // 53 bits
            return bits * (1.0 / 9007199254740992.0);       // / 2^53
        }

        public double Range(double minInclusive, double maxExclusive)
        {
            return minInclusive + NextDouble() * (maxExclusive - minInclusive);
        }

        /// <summary>Unbiased by rejection. minInclusive..maxExclusive.</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            uint span = (uint)(maxExclusive - minInclusive);
            uint threshold = (uint)(-(int)span) % span;     // 2^32 mod span
            while (true)
            {
                uint r = NextUInt();
                if (r >= threshold) return minInclusive + (int)(r % span);
            }
        }
    }
}
