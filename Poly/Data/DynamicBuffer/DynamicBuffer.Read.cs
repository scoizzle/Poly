using System;

namespace Poly.Data
{
    public partial struct DynamicBuffer<T>
    {
        public bool Read(out T value) {
            value = Current;
            return Consume();
        }
        
        public bool Read(Span<T> span, int count)
        {
            return Available >= count
                && count <= span.Length
                && Buffer.Span.Slice(Offset, count).TryCopyTo(span)
                && Consume(count);
        }

        public bool Read(Span<T> span, out int count)
        {
            count = Math.Min(Available, span.Length);

            return Buffer.Span.Slice(Offset, count).TryCopyTo(span)
                && Consume(count);
        }

        public bool Read(DynamicBuffer<T> destination, int count)
        {
            return count <= Available
                && destination.EnsureWriteableCapacity(count)
                && Buffer.Span.Slice(Offset, count).TryCopyTo(destination.WritableSpan)
                && destination.Commit(count)
                && Consume(count);
        }
    }
}