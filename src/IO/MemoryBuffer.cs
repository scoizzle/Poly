using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Poly.IO {
    public class MemoryBuffer {
        public byte[] Array;

        public int Position,
                   Length;

        public readonly int Size;

        public int Available => Length - Position;
        public int Remaining => Size - Length;
        public int Unallocated => Size - Available;

        public MemoryBuffer(int size) {
            Array = new byte[size];
            Size = size;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Consume(int length) {
            Position += length;

            if (Position == Length)
                Reset();
        }

        public bool Consume(byte[] chain) {
            var length = chain.Length;
            if (Available < length) return false;

            var position = Position;

            if (Array.CompareSubByteArray(position, chain, 0, length)) {
                Consume(length);
                return true;
            }

            return false;
        }

        public void Rebase() {
            if (Position == 0)
                return;

            if (Available == 0) {
                Reset();
            }
            else {
                Array.CopyTo(Position, Array, 0, Available);
                Length = Available;
                Position = 0;
            }
        }

        public void Reset() =>
            Position = Length = 0;

        public int FindSubByteArray(byte[] chain) =>
            Array.FindSubByteArray(Position, Length, chain, 0, chain.Length);

        public int FindSubByteArray(byte[] chain, int position) => 
            position < Position || position > Length ? -1 :
                Array.FindSubByteArray(position, Length, chain, 0, chain.Length);

        public bool Read(Stream stream) {
            if (Unallocated == 0)
                return false;

            var total_read = 0;

            try {
                total_read = stream.Read(Array, Length, Remaining);
            }
            catch (NullReferenceException) { }
            catch (IOException error) {
                Log.Error(error);
                return false;
            }

            if (total_read == 0)
                return false;

            Length += total_read;
            return true;
        }

        public bool Read(Stream stream, int count) {
            if (count > Unallocated)
                return false;

            var length = Length;
            var read = 0;

            try {
                stream.Read(Array, length, count);

                if (read == count) {
                    Length = length + count;
                    return true;
                }

                count -= read;
                length += read;

                do {
                    stream.Read(Array, length, count);

                    if (read == 0)
                        break;

                    count -= read;
                    length += read;
                }
                while (count != 0);
            }
            catch (NullReferenceException) { }
            catch (IOException error) {
                Log.Error(error);
                return false;
            }

            if (count == 0) {
                Length = length;
                return true;
            }

            return false;
        }

        public bool Read(params byte[] buffer) => Read(buffer, 0, buffer.Length);

        public bool Read(byte[] buffer, int index, int count) {
            if (count > Unallocated)
                return false;

            if (count > Remaining)
                Rebase();

            var length = Length;
            buffer.CopyTo(index, Array, length, count);
            Length = length + count;
            return true;
        }

        public bool Read(MemoryBuffer buffer) {
            var count = buffer.Available;

            if (count > Unallocated)
                return false;

            var write = buffer.Write(Array, Position, count);
            if (!write)
                return false;

            Length += count;
            return true;
        }

        public bool Write(Stream stream) =>
            Write(stream, Available);

        public bool Write(Stream stream, int length) {
            var available = Available;
            var position = Position;

            if (length > available)
                return false;

            try {
                stream.Write(Array, position, length);
            }
            catch (NullReferenceException) { }
            catch (IOException error) { 
                Log.Error(error);
                return false;
            }

            Consume(length);
            return true;
        }

        public bool Write(byte[] buffer, int index, int count) {
            if (count > Available)
                return false;

            Array.CopyTo(Position, buffer, index, count);
            Consume(count);
            return true;
        }

        public bool Write(MemoryBuffer buffer) {
            var count = Available;

            if (count > buffer.Unallocated)
                return false;

            if (count > buffer.Remaining)
                buffer.Rebase();

            var read = buffer.Read(Array, Position, count);
            if (!read)
                return false;

            Consume(count);
            return true;
        }

        public bool Copy(Stream In, Stream Out) {
            while (Read(In))
                if (!Write(Out))
                    return false;

            return true;
        }

        public Task<bool> ReadAsync(Stream stream) => 
            ReadAsync(stream, CancellationToken.None);

        public async Task<bool> ReadAsync(Stream stream, CancellationToken cancellation_token) {
            int read;

            try { read = await stream.ReadAsync(Array, Length, Remaining, cancellation_token); }
            catch (Exception error) { Log.Error(error); return false; }
            
            Length += read;
            return read != 0;
        }

        public Task<bool> WriteAsync(Stream stream) => WriteAsync(stream, CancellationToken.None);

        public async Task<bool> WriteAsync(Stream stream, CancellationToken cancellation_token) {
            try { await stream.WriteAsync(Array, Position, Available, cancellation_token); }
            catch (Exception error) { Log.Error(error); return false; } 
            
            Reset();
            return true;
        }

        public async Task<bool> CopyAsync(Stream In, Stream Out, CancellationToken cancellation_token) {
            int read;

            try { 
                do {
                    read = await In.ReadAsync(Array, Length, Remaining, cancellation_token);
                    if (read == 0) break;
                    else Length += read;

                    await Out.WriteAsync(Array, Position, Available, cancellation_token);
                    Reset();
                }
                while (true);
            }
            catch (IOException error) { Log.Debug(error); return false; }
            catch (Exception error) { Log.Error(error); return false; }

            return true;
        }

        public async Task<bool> CopyAsync(Stream In, Stream Out, long length, CancellationToken cancellation_token) {
            int read, to_read, to_write;

            try { 
                if (Available >= length) {
                    to_write = (int)(Math.Min(Available, length));
                    await Out.WriteAsync(Array, Position, to_write, cancellation_token);
                    Reset();
                    return true;
                }

                do {
                    to_read = (int)(Math.Min(Remaining, length));

                    read = await In.ReadAsync(Array, Length, to_read, cancellation_token);
                    if (read == 0) break;
                    else Length += read;

                    to_write = (int)(Math.Min(Available, length));
                    await Out.WriteAsync(Array, Position, to_write, cancellation_token);

                    length -= to_write;
                    Reset();
                }
                while (length != 0);
            }
            catch (IOException error) { Log.Debug(error); return false; }
            catch (Exception error) { Log.Error(error); return false; }

            return true;
        }
    }
}