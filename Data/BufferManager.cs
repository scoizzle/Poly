using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;

namespace Poly.Data {
    public class BufferManager {
        public int BufferSize { get; private set; }
        public int MaxBufferCount { get; private set; }

        private ConcurrentStack<byte[]> Buffers;

        public ulong TotalBufferSize { get { return (ulong)(MaxBufferCount * BufferSize); } }

        public BufferManager(int size, int maxCount) {
            BufferSize = size;
            MaxBufferCount = maxCount;

            Buffers = new ConcurrentStack<byte[]>();

            for (int i = 0; i < maxCount; i++) {
                AllocateBuffer();
            }
        }

        private void AllocateBuffer() {
            Buffers.Push(new byte[BufferSize]);
        }

        public byte[] Take() {
            byte[] ret;

            if (Buffers.TryPop(out ret))
                return ret;

            return null;
        }

        public void Return(byte[] buf) {
            if (buf == null) return;

            Buffers.Push(buf);
        }
    }
}
