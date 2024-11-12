using System;
using System.Buffers;

using Xunit;

namespace Poly.Data
{
    public class DynamicBufferTests
    {
        static readonly Memory<byte> data = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };

        [Fact]
        public void BoundsSanityChecks()
        {
            var owner = MemoryPool<byte>.Shared.Rent(1);
            var buffer = new DynamicBuffer<byte>(owner.Memory.Slice(0, 1)); // need length of the buffer to equal 1 for tests to be correct

            const byte value = 0xFF;

            Assert.False(buffer.TryRead(out var result), "Read should fail, buffer is empty");

            Assert.True(buffer.TryWrite(value), "Write should succeed, buffer has 1 byte open");

            Assert.False(buffer.TryWrite(value), "Write should fail, buffer is full");
        }

        [Fact]
        public void ReadWriteSingleValue()
        {
            var owner = MemoryPool<byte>.Shared.Rent(8);
            var buffer = new DynamicBuffer<byte>(owner.Memory);

            const byte value = 0xFF;

            Assert.True(buffer.TryWrite(value)); // Write single byte in

            Assert.True(buffer.TryRead(out var result)); // Read out single byte we just wrote in

            Assert.Equal(value, result);

            Assert.False(buffer.TryRead(out result)); // Read should fail, buffer is empty
        }

        [Fact]
        public void ReadWriteSlice()
        {
            var owner = MemoryPool<byte>.Shared.Rent(8);
            var buffer = new DynamicBuffer<byte>(owner.Memory);

            Assert.True(buffer.TryWrite(data));
            Assert.True(buffer.TryWrite(data));

            Assert.True(buffer.TryRead(data.Span, data.Length));
        }

        [Fact]
        public void FillAndDrainBufferTest()
        {
            Span<byte> chunk = stackalloc byte[4];

            var owner = MemoryPool<byte>.Shared.Rent(Environment.SystemPageSize);
            var buffer = new DynamicBuffer<byte>(owner.Memory);

            var chunksToBeWritten = Environment.SystemPageSize / data.Length;

            for (var i = 0; i < chunksToBeWritten; i++)
                buffer.TryWrite(data);

            for (var i = 0; i < chunksToBeWritten; i++)
            {
                Assert.True(buffer.TryRead(chunk, 4), "Failed to read chunk from dynamic buffer");

                Assert.True(buffer.TryWrite(data), "Failed to write chunk to dynamic buffer");
            }
        }
    }
}