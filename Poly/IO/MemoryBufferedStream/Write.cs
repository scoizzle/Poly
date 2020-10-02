using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Poly.Data;

namespace Poly.IO
{
    public partial class MemoryBufferedStream
    {
        public override void Write(byte[] buffer, int index, int count)
            => Out.Write(buffer, index, count);

        public override void Write(ReadOnlySpan<byte> buffer)
            => Out.Write(buffer);

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
            => Out.Write(buffer, offset, count) ?
                Task.CompletedTask :
                Task.FromException(new IOException());

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> memory, CancellationToken cancellationToken = default)
            => Out.Write(memory.Span)
             ? default(ValueTask)
             : default;

        public ValueTask<bool> WriteAsync(CancellationToken cancellationToken = default)
            => Stream.WriteAsync(Out, cancellationToken);

        public ValueTask<bool> WriteAsync(int count, CancellationToken cancellationToken = default)
            => Stream.WriteAsync(Out, count, cancellationToken);

        public ValueTask<bool> WriteAsync(Stream stream, CancellationToken cancellationToken = default)
            => Out.CopyAsync(stream, Stream, cancellationToken);

        public ValueTask<bool> WriteAsync(Stream stream, long count, CancellationToken cancellationToken = default)
            => Out.CopyAsync(stream, Stream, count, cancellationToken);

        public override async Task FlushAsync(CancellationToken cancellationToken = default) {
            await Stream.WriteAsync(Out, cancellationToken);
            await Stream.FlushAsync(cancellationToken);
        }
    }
}