using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Threading.Tasks;

namespace Poly.Data {
    public class MemoryBuffer {
        public long TotalConsumed { get; private set; }

        public byte[] Array { get; private set; }

        public int Position,
                   Length;

        public readonly int Size;

        public int Available { get { return Length - Position; } }
        public int Remaining { get { return Size - Length; } }

        public MemoryBuffer(int size) {
            Array = new byte[size];
            Size = size;
        }

        public void Consume(int length) {
            Position += length;
            TotalConsumed += length;
        }

        public bool Consume(byte[] chain) {
            if (Available == 0) return false;

            if (Array.CompareSubByteArray(Position, chain) == true) {
                Consume(chain.Length);
                return true;
            }

            return false;
        }

        public void Rebase() {
            if (Available == 0) {
                Reset();
            }
            else
            if (Position > 0) {
                System.Array.Copy(Array, Position, Array, 0, Available);
                Length = Available;
                Position = 0;
            }
        }

        public void Reset() {
            Position = Length = 0;
        }

        public bool Read(Stream stream) {
            Length += stream.Read(Array, Length, Remaining);
            return true;
        }

        public bool Read(Stream stream, int count) {
            if (count > (Size - Available))
                return false;

            if (count > Remaining)
                Rebase();

            var total = 0;

            while (total < count) {
                total += stream.Read(Array, Length, count - total);
            }

            Length += count;
            return true;
        }

        public bool Read(params byte[] buffer) {
            return Read(buffer, 0, buffer.Length);
        }

        public bool Read(byte[] buffer, int index, int count) {
            if (count > (Size - Available))
                return false;

            if (count > Remaining)
                Rebase();

            System.Array.Copy(buffer, index, Array, Length, count);
            Length += count;
            return true;
        }

        public bool Read(MemoryBuffer buffer) {
            var count = buffer.Available;

            if (count > (Size - Available))
                return false;

            if (count > Remaining)
                Rebase();

            var write = buffer.Write(Array, Position, count);
            if (!write)
                return false;

            Length += count;
            return true;
        }

        public async Task<bool> ReadAsync(Stream stream) {
            Length += await stream.ReadAsync(Array, Length, Remaining);
            return true;
        }

        public async Task<bool> ReadAsync(Stream stream, int count) {
            if (count > (Size - Available))
                return false;

            if (count > Remaining)
                Rebase();

            var total = 0;

            while (total < count) {
                total += await stream.ReadAsync(Array, Length, count - total);
            }

            Length += count;
            return true;
        }

        public bool Write(Stream stream) {
            stream.Write(Array, Position, Available);
            stream.Flush();

            Consume(Available);
            Reset();

            return true;
        }

        public bool Write(Stream stream, int length) {
            if (length > Available)
                return false;

            stream.Write(Array, Position, length);
            stream.Flush();

            Consume(length);

            if (Available == 0)
                Reset();

            return true;
        }

        public bool Write(byte[] buffer, int index, int count) {
            if (count > Available)
                return false;

            System.Array.Copy(Array, Position, buffer, index, count);
            Position += count;
            return true;
        }

        public bool Write(MemoryBuffer buffer) {
            var count = Available;

            if (count > (buffer.Size - buffer.Available))
                return false;

            if (count > buffer.Remaining)
                buffer.Rebase();

            var read = buffer.Read(Array, Position, count);
            if (!read)
                return false;

            Position += count;
            return true;
        }

        public async Task<bool> WriteAsync(Stream stream) {
            await stream.WriteAsync(Array, Position, Available);
            await stream.FlushAsync();

            Consume(Available);
            Reset();

            return true;
        }

        public async Task<bool> WriteAsync(Stream stream, int length) {
            if (length > Available)
                return false;

            await stream.WriteAsync(Array, Position, length);
            await stream.FlushAsync();

            Consume(length);

            if (Available == 0)
                Reset();

            return true;
        }
    }
}
