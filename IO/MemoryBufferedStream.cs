using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Poly.IO {

    public partial class MemoryBufferedStream : Stream {
        public const int DefaultBufferSize = 1024 * 16;

        protected internal MemoryBuffer In, Out;
        public MemoryBufferedStream(Stream stream, int in_buffer_size = DefaultBufferSize, int out_buffer_size = DefaultBufferSize) {
            Stream = stream;
            In = new MemoryBuffer(in_buffer_size);
            Out = new MemoryBuffer(out_buffer_size);
        }

        public override bool CanRead => Stream.CanRead;
        public override bool CanSeek => Stream.CanSeek;
        public override bool CanWrite => Stream.CanWrite;
        public override bool CanTimeout => Stream.CanTimeout;
        public override long Length => Stream.Length;
        public override long Position {
            get => Stream.Position;
            set => Stream.Position = value;
        }

        public Stream Stream { get; protected set; }

        public override void Flush() {
            Out.Write(Stream);
            Stream.Flush();
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            Stream.Seek(offset, origin);

        public override void SetLength(long value) =>
            Stream.SetLength(value);

        public override int Read(byte[] buffer, int index, int count) =>
            In.Read(buffer, index, count) ? count : -1;

        public override void Write(byte[] buffer, int offset, int count) =>
            Out.Read(buffer, offset, count);
        
        public override Task FlushAsync(CancellationToken cancellation_token) =>
            Out.WriteAsync(Stream, cancellation_token).ContinueWith(_ => Stream.FlushAsync(cancellation_token));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellation_token) =>
            Task.FromResult(Out.Read(buffer, offset, count) ? count : -1);

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellation_token) =>
            Task.FromResult(Out.Write(buffer, offset, count));

        public new void Close() {
            Stream.Close();
        }

        public void RebaseBuffers() {
            In.Rebase();
            Out.Rebase();
        }

        public bool Read() =>
            In.Read(Stream);

        public bool Read(Stream stream) =>
            In.Copy(Stream, stream);
        
        public bool ReadUntil(byte[] chain, Stream storage, long maxLength = long.MaxValue) {
            if (In.Available == 0) {
                var recv = Read();

                if (!recv)
                    return false;
            }

            var buffer = In;
            var array = buffer.Array;

            var arrayStart = buffer.Position;
            var arrayIndex = arrayStart;
            var arrayLength = buffer.Length;

            var chainIndex = 0;
            var chainLength = chain.Length;

            var lastWasPartial = false;

            do {
                var compare = array.CompareSubByteArray(arrayIndex, arrayLength, chain, chainIndex, chainLength);

                if (compare == true) { // Found chain
                    buffer.Write(storage, arrayIndex - arrayStart);
                    buffer.Consume(chainLength);
                    return true;
                }
                else
                if (compare == false) { // Partial Match
                    if (lastWasPartial) {
                        arrayIndex++;
                    }
                    else {
                        buffer.Write(storage, arrayIndex - arrayStart);
                        buffer.Rebase();

                        Read();

                        arrayIndex = 0;
                        arrayStart = buffer.Position;
                        arrayLength = buffer.Length;

                        lastWasPartial = true;
                    }
                }
                else
                if (compare == null) // No match.
                    arrayIndex++;

                if (arrayIndex == arrayLength) {
                    buffer.Write(storage);
                    Read();

                    arrayIndex = 0;
                    arrayStart = arrayIndex;
                    arrayLength = buffer.Length;
                }
            }
            while (--maxLength > 0);

            return false;
        }

        public bool ReadUntilConstrained(byte[] chain, Stream storage) =>
            ReadUntilConstrained(chain, _ => {
                try {
                    storage.Write(_.Array, _.Offset, _.Count);
                    return true;
                }
                catch (Exception error) {
                    Log.Error(error);
                    return false;
                }
            });

        public T ReadUntilConstrained<T>(byte[] chain, Func<ArraySegment<byte>, T> on_found) {
            if (In.Available == 0)
                if (!Read())
                    return default;

            var chain_length = chain.Length;

            var position = In.Position;
            var length = In.Length;
            var index = In.Array.FindSubByteArray(position, length, chain, 0, chain_length);

            if (index == -1)
                return default;

            In.Consume(index - length + chain_length);
            return on_found(new ArraySegment<byte>(In.Array, position, index - position));
        }

        public bool Write() =>
            Out.Write(Stream);

        public bool Write(Stream stream) =>
            Out.Copy(stream, Stream);

        public bool Write(MemoryBuffer buffer) =>
            Out.Read(buffer);

        public Task<bool> ReadAsync() =>
            In.ReadAsync(Stream, CancellationToken.None);

        public Task<bool> ReadAsync(CancellationToken cancellation_token) =>
            In.ReadAsync(Stream, cancellation_token);

        public Task<bool> ReadAsync(Stream storage) =>
            In.CopyAsync(Stream, storage, CancellationToken.None);

        public Task<bool> ReadAsync(Stream storage, CancellationToken cancellation_token) =>
            In.CopyAsync(Stream, storage, cancellation_token);

        public async Task<bool> ReadUntilAsync(byte[] chain, Stream storage, long maxLength = long.MaxValue) {
            if (In.Available == 0) {
                var recv = ReadAsync();

                if (!await recv)
                    return false;
            }

            var buffer = In;
            var array = buffer.Array;

            var arrayStart = buffer.Position;
            var arrayIndex = arrayStart;
            var arrayLength = buffer.Length;

            var chainIndex = 0;
            var chainLength = chain.Length;

            var lastWasPartial = false;

            do {
                var compare = array.CompareSubByteArray(arrayIndex, arrayLength, chain, chainIndex, chainLength);

                if (compare == true) { // Found chain
                    await buffer.WriteAsync(storage, arrayIndex - arrayStart);
                    buffer.Consume(chainLength);
                    return true;
                }
                else
                if (compare == false) { // Partial Match
                    if (lastWasPartial) {
                        arrayIndex++;
                    }
                    else {
                        await buffer.WriteAsync(storage, arrayIndex - arrayStart);
                        buffer.Rebase();

                        await ReadAsync();

                        arrayIndex = 0;
                        arrayStart = buffer.Position;
                        arrayLength = buffer.Length;

                        lastWasPartial = true;
                    }
                }
                else
                if (compare == null) // No match.
                    arrayIndex++;

                if (arrayIndex == arrayLength) {
                    await buffer.WriteAsync(storage);
                    await ReadAsync();

                    arrayIndex = 0;
                    arrayStart = arrayIndex;
                    arrayLength = buffer.Length;
                }
            }
            while (--maxLength > 0);

            return false;
        }

        public async Task<T> ReadUntilConstrainedAsync<T>(byte[] chain, Func<ArraySegment<byte>, T> on_found, CancellationToken cancellation_token) {
            if (In.Available == 0)
                if (!await ReadAsync(cancellation_token))
                    return default;

            var chain_length = chain.Length;

            var position = In.Position;
            var length = In.Length;
            var index = In.Array.FindSubByteArray(position, length, chain, 0, chain_length);

            if (index == -1)
                return default;

            In.Consume(index + chain_length - position);
            return on_found(new ArraySegment<byte>(In.Array, position, index - position));
        }

        public Task<bool> WriteAsync() =>
            WriteAsync(CancellationToken.None);

        public Task<bool> WriteAsync(CancellationToken cancellation_token) =>
            Out.WriteAsync(Stream, cancellation_token);

        public Task<bool> WriteAsync(Stream stream) =>
            Out.CopyAsync(stream, Stream, CancellationToken.None);

        public Task<bool> WriteAsync(Stream stream, CancellationToken cancellation_token) =>
            Out.CopyAsync(stream, Stream, cancellation_token);
    }
}