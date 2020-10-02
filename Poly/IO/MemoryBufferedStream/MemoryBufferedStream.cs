using System.Buffers;
using System.IO;

using Poly.Data;

namespace Poly.IO
{
    public partial class MemoryBufferedStream : Stream
    {
        public MemoryBufferedStream(int inMinimumSize = 4096, int outMinimumSize = 4096)
        {
            In = new DynamicBuffer<byte>(ArrayPool<byte>.Shared.Rent(inMinimumSize));
            Out = new DynamicBuffer<byte>(ArrayPool<byte>.Shared.Rent(outMinimumSize));
        }
        
        public DynamicBuffer<byte> In { get; }
        
        public DynamicBuffer<byte> Out { get; }

        public MemoryBufferedStream(Stream stream, int inMinimumSize = -1, int outMinimumSize = -1)
             : this(inMinimumSize, outMinimumSize)
            => Stream = stream;

        public override bool CanRead
            => Stream.CanRead;

        public override bool CanSeek
            => Stream.CanSeek;

        public override bool CanWrite
            => Stream.CanWrite;

        public override bool CanTimeout
            => Stream.CanTimeout;

        public override long Length
            => Stream.Length;

        public override long Position
        {
            get => Stream.Position;
            set => Stream.Position = value;
        }

        public Stream Stream { get; protected set; }

        public override void Flush()
            => Stream.Flush();

        public override long Seek(long offset, SeekOrigin origin)
            => Stream.Seek(offset, origin);

        public override void SetLength(long value)
            => Stream.SetLength(value);

        public override void Close() =>
            Stream.Close();

        public void RebaseBuffers() { In.Rebase(); Out.Rebase(); }

        public void ResetBuffers() { In.Reset(); Out.Reset(); }
    }
}