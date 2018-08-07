using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Poly.IO {

    public partial class MemoryBufferedStream : Stream {
        public const int DefaultBufferSize = (1024 * 16 + 9) * 4;

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
            In.Read(buffer, index, count) ? count : 0;

        public override void Write(byte[] buffer, int offset, int count) =>
            Out.Read(buffer, offset, count);
        
        public override Task FlushAsync(CancellationToken cancellation_token) =>
            Out.WriteAsync(Stream, cancellation_token).ContinueWith(_ => Stream.FlushAsync(cancellation_token));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellation_token) =>
            Task.FromResult(In.Read(buffer, offset, count) ? count : 0);

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
            
        public Task<bool> ReadAsync(Stream storage, long length) =>
            In.CopyAsync(Stream, storage, length, CancellationToken.None);

        public Task<bool> ReadAsync(Stream storage, long length, CancellationToken cancellation_token) =>
            In.CopyAsync(Stream, storage, length, cancellation_token);

        public async Task<T> ReadUntilConstrainedAsync<T>(byte[] chain, Func<byte[], int, int, T> on_found, CancellationToken cancellation_token) {
            if (!await DataAvailableAsync(cancellation_token))
                return default(T);

            var position = In.Position;
            var index = In.FindSubByteArray(chain, position);

            if (index == -1)
                return default(T);
            
            In.Consume(index + chain.Length - position);
            return on_found(In.Array, position, index - position);                    
        }

        public Task<bool> WriteAsync() =>
            WriteAsync(CancellationToken.None);

        public Task<bool> WriteAsync(CancellationToken cancellation_token) =>
            Out.WriteAsync(Stream, cancellation_token);

        public Task<bool> WriteAsync(Stream stream) =>
            Out.CopyAsync(stream, Stream, CancellationToken.None);

        public Task<bool> WriteAsync(Stream stream, CancellationToken cancellation_token) =>
            Out.CopyAsync(stream, Stream, cancellation_token);

        public Task<bool> WriteAsync(Stream stream, long count) =>
            Out.CopyAsync(stream, Stream, count, CancellationToken.None);

        public Task<bool> WriteAsync(Stream stream, long count, CancellationToken cancellation_token) =>
            Out.CopyAsync(stream, Stream, count, cancellation_token);

        public Task<bool> DataAvailableAsync(CancellationToken cancellation_token) =>
            In.Available != 0 ? Task.FromResult(true) : ReadAsync(cancellation_token);
        
        public Task<bool> DataAvailableAsync(int minLength, CancellationToken cancellation_token) =>
            In.Available >= minLength ? Task.FromResult(true) : ReadAsync(cancellation_token);
    }
}