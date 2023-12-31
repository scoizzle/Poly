using System.Buffers;
using System.IO;

using Poly.Data;

namespace Poly.IO
{
    public partial class MemoryBufferedStream : Stream
    {
        const int DEFAULT_BUFFER_SIZE = 4096;

        private readonly IMemoryOwner<byte> inBufferOwner, outBufferOwner;

        public MemoryBufferedStream(Stream stream, int inMinimumSize = 4096, int outMinimumSize = 4096)
        {
            Stream = stream;

            inBufferOwner = MemoryPool<byte>.Shared.Rent(inMinimumSize);
            outBufferOwner = MemoryPool<byte>.Shared.Rent(outMinimumSize);
            
            In = new DynamicBuffer<byte>(inBufferOwner.Memory);
            Out = new DynamicBuffer<byte>(outBufferOwner.Memory);
        }

        protected MemoryBufferedStream(int inMinimumSize = DEFAULT_BUFFER_SIZE, int outMinimumSize = DEFAULT_BUFFER_SIZE) : this(default!, inMinimumSize, outMinimumSize) {

        }

        public DynamicBuffer<byte> In { get; }
        
        public DynamicBuffer<byte> Out { get; }

        public override bool CanRead => Stream.CanRead;

        public override bool CanSeek => Stream.CanSeek;

        public override bool CanWrite => Stream.CanWrite;

        public override bool CanTimeout => Stream.CanTimeout;

        public override long Length => Stream.Length;

        public override long Position
        {
            get => Stream.Position;
            set => Stream.Position = value;
        }

        public Stream Stream { get; protected set; }

        public override void Flush() => Stream.Flush();

        public override long Seek(long offset, SeekOrigin origin) => Stream.Seek(offset, origin);

        public override void SetLength(long value) => Stream.SetLength(value);

        public override void Close() {
            Stream.Close();

            In.Clear();
            Out.Clear();
        }
    }
}