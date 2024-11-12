namespace Poly.Data;

public partial class DynamicBuffer<T> : IDisposable
{
    private readonly IMemoryOwner<T>? m_MemoryOwner;
    private Memory<T> m_Memory;
    private int m_Offset, m_Count;

    public DynamicBuffer(int minBufferSize = -1)
    {
        Guard.IsGreaterThanOrEqualTo(minBufferSize, -1);

        m_MemoryOwner = MemoryPool<T>.Shared.Rent(minBufferSize);
        m_Memory = m_MemoryOwner.Memory;
    }

    public DynamicBuffer(Memory<T> memory)
    {
        Guard.IsNotNull(memory);

        m_Memory = memory;
    }

    public DynamicBuffer(IMemoryOwner<T> memoryOwner)
    {
        Guard.IsNotNull(memoryOwner);

        m_MemoryOwner = memoryOwner;
        m_Memory = memoryOwner.Memory;
    }

    public T? this[int index] => GetValueFromBuffer(index);
    public int Offset => m_Offset;
    public int Count => m_Count;
    public int WriteableSize => m_Memory.Length - Offset - Count;
    public int UnallocatedSize => m_Memory.Length - Count;
    public ReadOnlyMemory<T> ReadableMemory => m_Memory.Slice(Offset, Count);
    public ReadOnlySpan<T> ReadableSpan => m_Memory.Span.Slice(Offset, Count);
    public Memory<T> WriteableMemory => m_Memory.Slice(Offset + Count);
    public Span<T> WriteableSpan => m_Memory.Span.Slice(Offset + Count);

    public void Dispose()
    {
        m_Memory = default;
        m_MemoryOwner?.Dispose();
        GC.SuppressFinalize(this);
    }

    public bool Consume(int count = 1)
    {
        if (count > m_Count)
            return false;

        m_Count -= count;
        m_Offset += count;
        return true;
    }

    public bool Commit(int count = 1)
    {
        if (count > WriteableSize)
            return false;

        m_Count += count;
        return true;
    }

    public bool EnsureWriteableCapacity(int count = 1)
    {
        if (count < 0) return false;
        if (count == 0) return true;

        if (m_Count == 0 && m_Offset > 0)
        {
            Reset();
            return count <= m_Memory.Length;
        }

        var unallocated = m_Memory.Length - m_Count;
        if (unallocated < count)
            return false;

        var remaining = unallocated - Offset;
        if (remaining < count)
            Rebase();

        return true;
    }

    public void Reset()
        => m_Offset = m_Count = 0;

    public bool Rebase()
    {
        var span = m_Memory.Span;
        var readableSpan = span.Slice(m_Offset, m_Count);

        if (!readableSpan.TryCopyTo(span))
            return false;

        m_Offset = 0;
        return true;
    }

    public void Clear()
    {
        m_Memory.Span.Clear();
        Reset();
    }

    private T? GetValueFromBuffer(int index)
    {
        var (offset, count) = (Offset, Count);

        if (index >= count)
            return default;

        index += offset;

        var span = m_Memory.Span;
        if (index < 0 || index >= span.Length)
            return default;

        return span[index];
    }
}
