namespace Poly.Data;
public partial class DynamicBuffer<T>
{
    public bool TryRead([NotNullWhen(true)] out T? value)
    {
        value = GetValueFromBuffer(0);
        return Consume();
    }

    public bool TryRead(Span<T> span, int count = -1)
    {
        if (count == -1)
            count = span.Length;

        if (count > m_Count)
            return false;

        var readable = ReadableSpan.Slice(0, count);

        if (!readable.TryCopyTo(span))
            return false;

        m_Offset += count;
        m_Count -= count;
        return true;
    }

    public bool TryRead(Span<T> span, out int count)
    {
        count = Math.Min(m_Count, span.Length);

        var readable = ReadableSpan.Slice(0, count);

        if (!readable.TryCopyTo(span))
            return false;

        m_Offset += count;
        m_Count -= count;
        return true;
    }

    public bool TryRead(DynamicBuffer<T> destination, int count = -1)
    {
        if (count == -1)
            count = m_Count;

        return count <= Count
            && destination.EnsureWriteableCapacity(count)
            && ReadableSpan.TryCopyTo(destination.WriteableSpan)
            && destination.Commit(count)
            && Consume(count);
    }
}