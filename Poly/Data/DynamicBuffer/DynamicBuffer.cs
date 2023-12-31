using System;

namespace Poly.Data {
    public partial class DynamicBuffer<T> {
        public DynamicBuffer(Memory<T> buffer) {
            Buffer = buffer;
            Size = buffer.Length;
            Offset = Count = 0;
        }

        Memory<T> Buffer { get; }

        public int Size { get; }

        public int Offset { get; private set; }

        public int Count { get; private set; }

        public T? this[int index]
            => index >= 0 && index < Count
             ? Buffer.Span[Offset + index]
             : default;

        public T? Current
            => this[0];

        public bool IsEmpty
            => Count == 0;

        public int Available
            => Count;

        public int WriteOffset
            => Offset + Count;

        public int RemainingLength
            => Buffer.Length - WriteOffset;

        public int Unallocated
            => Buffer.Length - Count;

        public ReadOnlyMemory<T> Readable
            => Buffer.Slice(Offset, Count);

        public Memory<T> Writable
            => Buffer.Slice(WriteOffset, RemainingLength);

        public ReadOnlySpan<T> ReadableSpan
            => Buffer.Span.Slice(Offset, Count);

        public Span<T> WritableSpan
            => Buffer.Span.Slice(WriteOffset, RemainingLength);

        public bool Consume(int n = 1) {
            if (n > Count)
                return false;

            Count -= n;
            Offset += n;
            return true;
        }

        public bool Commit(int n = 1) {
            if (n > RemainingLength)
                return false;

            Count += n;
            return true;
        }

        public bool EnsureWriteableCapacity(int n = 1)
        {   if (n < 0) return false;
            if (n == 0) return true;
            
            if (Count == 0 && Offset > 0) {
                Reset();
                return n <= Buffer.Length;
            }

            var unallocated = Buffer.Length - Count;
            if (unallocated < n)
                return false;

            var remaining = unallocated - Offset;
            if (remaining < n)
                Rebase();

            return true;
        }

        public void Reset()
            => Offset = Count = 0;

        public bool Rebase() {
            if (!ReadableSpan.TryCopyTo(Buffer.Span))
                return false;

            Offset = 0;
            return true;
        }

        public void Clear() {
            Reset();

            Buffer.Span.Clear();
        }
    }
}