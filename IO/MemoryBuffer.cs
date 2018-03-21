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

        public int Available { get => Length - Position; }
        public int Remaining { get => Size - Length; }
        public int Unallocated { get => Size - Available; }

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
            var last_index = position + length;

            if (Array.CompareSubByteArray(position, last_index, chain, 0, length) == true) {
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

        public void Reset() {
            Position = Length = 0;
        }

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
            }
            catch (NullReferenceException) { }
            catch (IOException error) {
                Log.Error(error);
                return false;
            }

            if (read == count) {
                Length = length + count;
                return true;
            }

            count -= read;
            length += read;

            do {
                try {
                    stream.Read(Array, length, count);
                }
                catch (NullReferenceException) { }
                catch (IOException error) {
                    Log.Error(error);
                    return false;
                }

                if (read == 0)
                    break;

                count -= read;
                length += read;
            }
            while (count != 0);

            if (count == 0) {
                Length = length;
                return true;
            }

            return false;
        }

        public bool Read(params byte[] buffer) =>
            Read(buffer, 0, buffer.Length);

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

        public Task<bool> ReadAsync(Stream stream, CancellationToken cancellation_token) {
            try {
                return stream.
                    ReadAsync(Array, Length, Remaining, cancellation_token).
                    ContinueWith(_ => {
                        if (_.CatchException() || _.IsCanceled || cancellation_token.IsCancellationRequested)
                            return false;

                        var read = _.Result;

                        if (read == 0)
                            return false;

                        Length += read;
                        return true;
                    });
            }
            catch (NullReferenceException) { }
            catch (IOException) { }
            catch (Exception error) {
                Log.Error(error);
            }

            return Task.FromResult(false);
        }

        public Task<bool> ReadAsync(Stream stream, int count, CancellationToken cancellation_token) {
            if (count > Remaining)
                return Task.FromResult(false);

            try {
                return stream.
                    ReadAsync(Array, Length, count, cancellation_token).
                    ContinueWith(_ => {
                        if (_.CatchException() || _.IsCanceled || cancellation_token.IsCancellationRequested)
                            return false;

                        var read = _.Result;

                        if (read == 0)
                            return false;

                        Length += read;
                        return true;
                    });
            }
            catch (NullReferenceException) { }
            catch (IOException) { }
            catch (Exception error) {
                Log.Error(error);
            }

            return Task.FromResult(false);
        }

        public Task<bool> WriteAsync(Stream stream) =>
            WriteAsync(stream, CancellationToken.None);

        public Task<bool> WriteAsync(Stream stream, CancellationToken cancellation_token) {
            try {
                return stream.
                    WriteAsync(Array, Position, Available, cancellation_token).
                    ContinueWith(_ => {
                        if (_.CatchException() || _.IsCanceled || cancellation_token.IsCancellationRequested)
                            return false;

                        Reset();
                        return true;
                    });
            }
            catch (NullReferenceException) { }
            catch (IOException) { }
            catch (Exception error) {
                Log.Error(error);
            }

            return Task.FromResult(false);
        }
            

        public Task<bool> WriteAsync(Stream stream, int count) =>
            WriteAsync(stream, count, CancellationToken.None);

        public Task<bool> WriteAsync(Stream stream, int count, CancellationToken cancellation_token) {
            if (count > Available)
                return Task.FromResult(false);

            try {
                return stream.
                    WriteAsync(Array, Position, count, cancellation_token).
                    ContinueWith(_ => {
                        if (_.CatchException() || _.IsCanceled || cancellation_token.IsCancellationRequested)
                            return false;

                        Reset();
                        return true;
                    });
            }
            catch (NullReferenceException) { }
            catch (IOException) { }
            catch (Exception error) {
                Log.Error(error);
            }

            return Task.FromResult(false);
        }

        public Task<bool> CopyAsync(Stream In, Stream Out, CancellationToken cancellation_token) {
            var tcs = new TaskCompletionSource<bool>();
            CopyRead(tcs, In, Out, cancellation_token);
            return tcs.Task;
        }

        private void CopyRead(TaskCompletionSource<bool> tcs, Stream In, Stream Out, CancellationToken cancellation_token) =>
            ReadAsync(In, cancellation_token).
                ContinueWith(_ => {
                    if (_.CatchException() || _.IsCanceled || cancellation_token.IsCancellationRequested)
                        tcs.SetResult(false);
                    else
                    if (_.Result) 
                        CopyWrite(tcs, In, Out, cancellation_token);
                    else 
                        tcs.SetResult(true);
                });
        
        private void CopyWrite(TaskCompletionSource<bool> tcs, Stream In, Stream Out, CancellationToken cancellation_token) =>
            WriteAsync(Out, cancellation_token).
                ContinueWith(_ => {
                    if (_.CatchException() || _.IsCanceled || cancellation_token.IsCancellationRequested || !_.Result)
                        tcs.SetResult(false);
                    else
                        CopyRead(tcs, In, Out, cancellation_token);
                });
    }
}