using System;
using System.Buffers;

using Xunit;

namespace Poly.Data {
    public class DynamicBufferTests {
        [Fact]
        public void BoundsSanityChecks() {
            var owner = MemoryPool<byte>.Shared.Rent(1);
            var buffer = new DynamicBuffer<byte>(owner.Memory.Slice(0, 1)); // need length of the buffer to equal 1 for tests to be correct

            const byte value = 0xFF;

            Assert.False(buffer.Read(out var result), "Read should fail, buffer is empty");

            Assert.True(buffer.Write(value), "Write should succeed, buffer has 1 byte open");

            Assert.False(buffer.Write(value), "Write should fail, buffer is full");
        }

        [Fact]
        public void ReadWriteSingleValue() {
            var owner = MemoryPool<byte>.Shared.Rent(8);
            var buffer = new DynamicBuffer<byte>(owner.Memory);

            const byte value = 0xFF;

            Assert.True(buffer.Write(value)); // Write single byte in

            Assert.True(buffer.Read(out var result)); // Read out single byte we just wrote in

            Assert.Equal(value, result);

            Assert.False(buffer.Read(out result)); // Read should fail, buffer is empty
        }

        [Fact]
        public void ReadWriteSlice()
        {
            var owner = MemoryPool<byte>.Shared.Rent(8);
            var buffer = new DynamicBuffer<byte>(owner.Memory);
            
            var value = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF };

            Assert.True(buffer.Write(value.AsSpan()));
            Assert.True(buffer.Write(value.AsSpan()));
        }
    }
}