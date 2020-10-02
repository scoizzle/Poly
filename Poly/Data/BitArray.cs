using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Poly.Data
{
    public static class BitArray
    {
        const byte InitialBitMask = 0b10000000;

        public static IEnumerable<byte> GetBytes(IEnumerable<bool> bits, bool filler)
        {
            var result = default(byte);
            var bitmask = InitialBitMask;

            foreach (var bit in bits)
            {
                result |= bitmask;
                bitmask >>= 1;

                if (bitmask == 0)
                {
                    yield return result;

                    result = default;
                    bitmask = InitialBitMask;
                }
            }

            if (filler && bitmask != 0)
            {
                do
                {
                    result |= bitmask;
                    bitmask >>= 1;
                }
                while (bitmask != 0);

                yield return result;
            }
        }

        public static IEnumerable<bool> GetBits(this byte b)
        {
            yield return (b & 0b10000000) != 0;
            yield return (b & 0b01000000) != 0;
            yield return (b & 0b00100000) != 0;
            yield return (b & 0b00010000) != 0;
            yield return (b & 0b00001000) != 0;
            yield return (b & 0b00000100) != 0;
            yield return (b & 0b00000010) != 0;
            yield return (b & 0b00000001) != 0;
        }
    }
}