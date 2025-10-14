using System.Runtime.InteropServices;

namespace Poly.Data;

[DebuggerDisplay("{DebugDisplayString}")]
public readonly struct BitArray : IEnumerable<bool> {
    private readonly List<byte> _data = new();

    private Span<byte> GetByteSpan(int byteIndex) => CollectionsMarshal.AsSpan(_data).Slice(byteIndex);

    public BitArray(byte[] data) {
        Guard.IsNotNull(data);
        _data = data.ToList();
    }

    public BitArray(int bitCapacity) {
        var arraySize = CalculateArraySize(bitCapacity);
        _data = new List<byte>(arraySize);
    }

    public int BitCapacity => _data.Capacity * 8;


    public readonly bool this[int bitPosition] {
        get {
            var (idx, mask) = new BitArrayIndex(bitPosition);
            Guard.IsBetweenOrEqualTo(idx, 0, _data.Count);
            var span = GetByteSpan(idx);
            return mask == (span[0] & mask);
        }
        set {
            var (idx, mask) = new BitArrayIndex(bitPosition);
            Guard.IsBetweenOrEqualTo(idx, 0, _data.Count);
            var span = GetByteSpan(idx);

            span[0] = value
                ? (byte)(span[0] | mask)
                : (byte)(span[0] & ~mask);
        }
    }

    public bool TryGetValue(int bitPosition, out bool value) {
        var (idx, mask) = new BitArrayIndex(bitPosition);

        if (idx < 0 || idx >= _data.Count) {
            return value = false;
        }

        value = mask == (_data[idx] & mask);
        return true;
    }

    public bool TrySetValue(int bitPosition, bool value) {
        var (idx, mask) = new BitArrayIndex(bitPosition);

        if (idx < 0 || idx >= _data.Count)
            return false;

        var span = GetByteSpan(idx);

        var isBitSet = mask == (span[0] & mask);

        if (isBitSet == value)
            return true;

        span[0] = value
            ? (byte)(span[0] | mask)
            : (byte)(span[0] & ~mask);

        return true;
    }

    public void Toggle(int bitPosition) {
        var (idx, mask) = new BitArrayIndex(bitPosition);
        Guard.IsBetweenOrEqualTo(idx, 0, _data.Count);
        _data[idx] ^= mask;
    }

    private string DebugDisplayString => string.Concat(_data.Select(e => e.ToString("B")));
    private static int CalculateArraySize(int numberOfBits) => new BitArrayIndex(numberOfBits).ByteIndex + 1;

    public IEnumerator<bool> GetEnumerator() => new BitArrayEnumerator(this);

    IEnumerator IEnumerable.GetEnumerator() => new BitArrayEnumerator(this);

    private struct BitArrayEnumerator : IEnumerator<bool> {
        int bitIndex;
        BitArray array;

        public BitArrayEnumerator(BitArray array) => this.array = array;

        public bool Current { get; private set; }

        public void Dispose() => array = default;

        public bool MoveNext() => Current = array[bitIndex++];

        public void Reset() => bitIndex = default;

        readonly object IEnumerator.Current => Current;
    }
}