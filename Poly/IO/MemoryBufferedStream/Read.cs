using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Poly.Data;

namespace Poly.IO
{
    public partial class MemoryBufferedStream
    {
        public override int Read(byte[] buffer, int offset, int count)
            => In.Read(buffer.AsSpan(offset, count), count) 
             ? count
             : 0;

        public override int Read(Span<byte> buffer)
            => In.Read(buffer, out var count)
             ? count
             : 0;
             
        public int Read(Span<byte> buffer, int count)
            => In.Read(buffer, count)
             ? count
             : 0;

        public async ValueTask<bool> DataAvailableAsync(CancellationToken cancellationToken = default)
            => In.Count > 0 
            || await Stream.ReadAsync(In, cancellationToken);

        public async ValueTask<bool> DataAvailableAsync(int count, CancellationToken cancellationToken = default)
            => In.Count >= count 
            || await Stream.ReadAsync(In, count, cancellationToken);

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
            => await DataAvailableAsync(cancellationToken)
             ? Read(buffer, offset, count)
             : 0;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => await DataAvailableAsync(cancellationToken)
             ? Read(buffer.Span)
             : 0;

        public async ValueTask<int> ReadAsync(Memory<byte> buffer, int count, CancellationToken cancellationToken = default)
            => await DataAvailableAsync(count, cancellationToken)
             ? Read(buffer.Span, count)
             : 0;

        public ValueTask<bool> ReadAsync(CancellationToken cancellationToken = default)
            => Stream.ReadAsync(In, cancellationToken);

        public ValueTask<bool> ReadAsync(int count, CancellationToken cancellationToken = default)
            => Stream.ReadAsync(In, count, cancellationToken);

        public ValueTask<bool> ReadAsync(Stream storage, CancellationToken cancellationToken = default)
            => In.CopyAsync(Stream, storage, cancellationToken);

        public ValueTask<bool> ReadAsync(Stream storage, long length, CancellationToken cancellationToken = default)
            => In.CopyAsync(Stream, storage, length, cancellationToken);
    }
}