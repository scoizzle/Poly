namespace Poly.Data;

[DebuggerDisplay("{DebugDisplayString}")]
public readonly struct BitArray : IEnumerable<bool>, IDisposable {
    private readonly IMemoryOwner<byte>? _memoryOwnership;
    private readonly Memory<byte> _data = new();
    private readonly int _bitCapacity;

    private Span<byte> GetSpan(int byteIndex) {
        ArgumentOutOfRangeException.ThrowIfNegative(byteIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(byteIndex, _data.Length);
        return _data.Span.Slice(byteIndex);
    }

    public BitArray(byte[] data) {
        ArgumentNullException.ThrowIfNull(data);
        _data = data;
        _bitCapacity = data.Length * 8;
    }

    public BitArray(int bitCapacity) {
        var arraySize = CalculateArraySize(bitCapacity);
        _memoryOwnership = MemoryPool<byte>.Shared.Rent(arraySize);
        _data = _memoryOwnership.Memory.Slice(0, arraySize);
        _bitCapacity = bitCapacity;
    }

    public int BitCapacity => _bitCapacity;

    public readonly bool this[int bitPosition] {
        get {
            var (idx, mask) = new BitArrayIndex(bitPosition);
            var span = GetSpan(idx);
            return mask == (span[0] & mask);
        }
        set {
            var (idx, mask) = new BitArrayIndex(bitPosition);
            var span = GetSpan(idx);

            span[0] = value
                ? (byte)(span[0] | mask)
                : (byte)(span[0] & ~mask);
        }
    }

    public bool TryGetValue(int bitPosition, out bool value) {
        var (idx, mask) = new BitArrayIndex(bitPosition);

        if (idx < 0 || idx >= _data.Length) {
            return value = false;
        }

        var span = GetSpan(idx);
        value = mask == (span[0] & mask);
        return true;
    }

    public bool TrySetValue(int bitPosition, bool value) {
        var (idx, mask) = new BitArrayIndex(bitPosition);

        if (idx < 0 || idx >= _data.Length)
            return false;

        var span = GetSpan(idx);

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
        var span = GetSpan(idx);
        span[0] ^= mask;
    }

    private string DebugDisplayString => string.Join("", Enumerable.Select(this, static b => b ? '1' : '0'));
    private static int CalculateArraySize(int numberOfBits) => new BitArrayIndex(numberOfBits).ByteIndex + 1;

    public IEnumerator<bool> GetEnumerator() => new BitArrayEnumerator(this);

    IEnumerator IEnumerable.GetEnumerator() => new BitArrayEnumerator(this);

    public void Dispose() {
        _memoryOwnership?.Dispose();
    }

    private struct BitArrayEnumerator : IEnumerator<bool> {
        private readonly BitArray array;
        private int bitIndex;

        public BitArrayEnumerator(BitArray array) {
            this.array = array;
            bitIndex = -1;
        }

        public bool Current { get; private set; }

        public void Dispose() => bitIndex = array.BitCapacity;

        public bool MoveNext() {
            if (++bitIndex >= array.BitCapacity)
                return false;

            Current = array[bitIndex];
            return true;
        }

        public void Reset() => bitIndex = -1;

        readonly object IEnumerator.Current => Current;
    }
}