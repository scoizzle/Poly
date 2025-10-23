namespace Poly.Data;

public readonly record struct BitArrayIndex(int ByteIndex, byte BitMask, bool FromMostSignificantBit = false) {
    public BitArrayIndex(int bitIndex) : this(0, 0) {
        (ByteIndex, BitMask) = CalculateArrayIndex(bitIndex);
    }

    public void Deconstruct(out int byteIndex, out byte bitMask) => (byteIndex, bitMask) = (ByteIndex, BitMask);

    (int ByteIndex, byte BitMask) CalculateArrayIndex(int bitIndex) {
        var (byteIndex, bitNumber) = Math.DivRem(bitIndex, 8);

        var bitMask = FromMostSignificantBit
            ? (byte)(0b10000000 >> bitNumber)
            : (byte)(0b00000001 << bitNumber);

        return (byteIndex, bitMask);
    }
}