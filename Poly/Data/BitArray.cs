namespace Poly.Data;

public readonly struct BitArray : IEnumerable<bool> {
    readonly byte[] array;

    public BitArray(byte[] data) => array = data;

    public BitArray(int bitCapacity) => Array.Resize(ref array, CalculateArraySize(bitCapacity));

    public int BitCapacity => array.Length * 8;

    public readonly bool this[int bitPosition] {
        get {
            var (idx, mask) = CalculateArrayIndex(bitPosition);

            Guard.IsBetweenOrEqualTo(idx, 0, array.Length);

            return mask == (array[idx] & mask);
        }
        set {
            var (idx, mask) = CalculateArrayIndex(bitPosition);

            Guard.IsBetweenOrEqualTo(idx, 0, array.Length);

            if (value) {
                array[idx] |= mask; 
            }
            else {
                array[idx] &= (byte)~mask;
            }
        }
    }

    public void Toggle(int bitPosition) {
        var (idx, mask) = CalculateArrayIndex(bitPosition);

        Guard.IsBetweenOrEqualTo(idx, 0, array.Length);

        array[idx] ^= mask;
    }

    static int CalculateArraySize(int numberOfBits) => (numberOfBits / 8) + (numberOfBits % 8 > 0 ? 1 : 0);

    static byte CalculateBitMask(int bitPosition) => (byte)(0b10000000 >> bitPosition);

    static (int ByteIndex, byte BitMask) CalculateArrayIndex(int bitPosition) {
        var byteNumber = bitPosition / 8;
        var bitNumber = bitPosition % 8;

        if (byteNumber > 0 && bitNumber > 3)
            byteNumber--;

        var bitMask = CalculateBitMask(bitNumber);

        return (byteNumber, bitMask);
    }

    public IEnumerator<bool> GetEnumerator() => new Enumerator(this);

    IEnumerator IEnumerable.GetEnumerator() => new Enumerator(this);

    struct Enumerator : IEnumerator<bool>
    {
        int bitIndex;
        BitArray array;

        public Enumerator(BitArray array) => this.array = array;
        
        public bool Current { get; private set; }

        public void Dispose() => array = default;

        public bool MoveNext() => Current = array[bitIndex++];

        public void Reset() => bitIndex = default;

        readonly object IEnumerator.Current => Current;
    }
}