namespace Poly.Data;
public partial class DynamicBuffer<T>
{
    public bool TryWrite(T value)
    {
        if (!EnsureWriteableCapacity(count: 1))
            return false;

        var writeable = WriteableSpan;
        writeable[0] = value;

        return Commit();
    }

    // public bool TryWrite(params Span<T> values)
    // {
    //     var count = values.Length;
    //     if (!EnsureWriteableCapacity(count))
    //         return false;

    //     var writeable = WriteableSpan;
    //     if (!values.TryCopyTo(writeable))
    //         return false;

    //     return Commit(count);
    // }

    public bool TryWrite(ReadOnlyMemory<T> span)
    {
        return EnsureWriteableCapacity(span.Length)
            && span.TryCopyTo(WriteableMemory)
            && Commit(span.Length);
    }

    public bool TryWrite(ReadOnlySpan<T> span)
    {
        return EnsureWriteableCapacity(span.Length)
            && span.TryCopyTo(WriteableSpan)
            && Commit(span.Length);
    }

    public bool TryWrite(IEnumerable<T> enumerable)
    {
        if (!EnsureWriteableCapacity())
            return false;

        var i = 0;
        var writeable = WriteableSpan;

        foreach (var value in enumerable)
        {
            if (i == writeable.Length)
            {
                if (!EnsureWriteableCapacity())
                    break;

                writeable = WriteableSpan;
            }

            writeable[i++] = value;
        }

        return Commit(i);
    }
}