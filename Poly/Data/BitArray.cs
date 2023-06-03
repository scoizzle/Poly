namespace Poly.Data;

public static class BitArray
{
    const byte InitialBitMask = 0b10000000;

    public static IEnumerable<byte> GetBytes(IEnumerable<bool> bits, bool filler)
    {
        var result = default(byte);
        var bitmask = InitialBitMask;

        foreach (var bit in bits)
        {
            if (bit) {
                result |= bitmask;
            }
            
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

    public static bool TryGetBits(
        this byte b, 
        Span<bool> bits) 
    {
        if (bits.Length < 8)
            return false;

        bits[0] = (b & 0b10000000) != 0;
        bits[1] = (b & 0b01000000) != 0;
        bits[2] = (b & 0b00100000) != 0;
        bits[3] = (b & 0b00010000) != 0;
        bits[4] = (b & 0b00001000) != 0;
        bits[5] = (b & 0b00000100) != 0;
        bits[6] = (b & 0b00000010) != 0;
        bits[7] = (b & 0b00000001) != 0;

        return true;
    }
}