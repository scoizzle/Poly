namespace Poly.Data;

[DebuggerDisplay("{DebugDisplayString}")]
public readonly struct BitArray : IEnumerable<bool>
{
    readonly int m_BitCapacity;
    readonly byte[] m_Array;

    public BitArray(byte[] data)
    {
        Guard.IsNotNull(data);

        m_Array = data;
        m_BitCapacity = data.Length * 8;
    }

    public BitArray(int bitCapacity)
    {
        var arraySize = CalculateArraySize(bitCapacity);
        m_BitCapacity = arraySize * 8;
        Array.Resize(ref m_Array, arraySize);
    }

    public int BitCapacity => m_BitCapacity;


    public readonly bool this[int bitPosition]
    {
        get
        {
            var (idx, mask) = new BitArrayIndex(bitPosition);
            Guard.IsBetweenOrEqualTo(idx, 0, m_Array.Length);

            return mask == (m_Array[idx] & mask);
        }
        set
        {
            var (idx, mask) = new BitArrayIndex(bitPosition);
            Guard.IsBetweenOrEqualTo(idx, 0, m_Array.Length);

            if (value)
            {
                m_Array[idx] |= mask;
            }
            else
            {
                m_Array[idx] &= (byte)~mask;
            }
        }
    }

    public bool TryGetValue(int bitPosition, out bool value)
    {
        var (idx, mask) = new BitArrayIndex(bitPosition);

        if (idx < 0 || idx >= m_Array.Length)
        {
            return value = false;
        }

        value = mask == (m_Array[idx] & mask);
        return true;
    }

    public bool TrySetValue(int bitPosition, bool value)
    {
        var (idx, mask) = new BitArrayIndex(bitPosition);

        if (idx < 0 || idx >= m_Array.Length)
            return false;

        ref var byteRef = ref m_Array[idx];

        var isBitSet = mask == (byteRef & mask);

        if (isBitSet == value)
            return true;

        if (value)
        {
            byteRef |= mask;
        }
        else
        {
            byteRef &= (byte)~mask;
        }

        return true;
    }

    public void Toggle(int bitPosition)
    {
        var (idx, mask) = new BitArrayIndex(bitPosition);
        Guard.IsBetweenOrEqualTo(idx, 0, m_Array.Length);
        m_Array[idx] ^= mask;
    }

    private string DebugDisplayString => string.Concat(m_Array.Select(e => e.ToString("B")));

    private static int CalculateArraySize(int numberOfBits) => new BitArrayIndex(numberOfBits).ByteIndex + 1;

    public IEnumerator<bool> GetEnumerator() => new BitArrayEnumerator(this);

    IEnumerator IEnumerable.GetEnumerator() => new BitArrayEnumerator(this);

    private struct BitArrayEnumerator : IEnumerator<bool>
    {
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