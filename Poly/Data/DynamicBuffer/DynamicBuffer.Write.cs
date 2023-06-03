using System;
using System.Collections.Generic;

namespace Poly.Data
{
    public partial class DynamicBuffer<T>
    {
        public bool Write(T value)
        {
            if (EnsureWriteableCapacity(n: 1)) {
                Buffer.Span[WriteOffset] = value;
                return Commit(n: 1);
            }

            return false;
        }

        public bool Write(params T[] array)
        {
            return array is not null
                && Write(array, 0, array.Length);
        }

        public bool Write(T[] array, int offset, int count)
        {
            return EnsureWriteableCapacity(n: count)
                && array.AsSpan(offset, count).TryCopyTo(WritableSpan)
                && Commit(n: count);
        }
        
        public bool Write(ReadOnlyMemory<T> span)
        {
            return EnsureWriteableCapacity(span.Length)
                && span.TryCopyTo(Writable)
                && Commit(span.Length);
        }

        public bool Write(ReadOnlySpan<T> span)
        {
            return EnsureWriteableCapacity(span.Length)
                && span.TryCopyTo(WritableSpan)
                && Commit(span.Length);
        }

        public bool Write(IEnumerable<T> enumerable)
        {
            if (!EnsureWriteableCapacity())
                return false;

            var i = 0;
            var writeable = WritableSpan;

            foreach (var value in enumerable)
            {
                writeable[i] = value;
                
                if (++i == writeable.Length) // Check for rebase possibly to fit in as much data as possible?
                    break;
            }

            return Commit(i);
        }
    }
}