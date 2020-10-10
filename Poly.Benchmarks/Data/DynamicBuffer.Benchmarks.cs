using System;
using System.Buffers;

using System.Collections.Generic;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;

namespace Poly.Data {
    public class DynamicBufferBenchmarks {
        static readonly Memory<byte> data = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };

        [Benchmark]
        public void FillBufferTest() {
            var owner = MemoryPool<byte>.Shared.Rent(Environment.SystemPageSize);
            var buffer = new DynamicBuffer<byte>(owner.Memory);

            var chunksToBeWritten = Environment.SystemPageSize / buffer.Size;

            for (var i = 0; i < chunksToBeWritten; i++)
                buffer.Write(data);
        }

        [Benchmark]
        public void FillAndDrainBufferTest() {
            Span<byte> chunk = stackalloc byte[4];

            var owner = MemoryPool<byte>.Shared.Rent(Environment.SystemPageSize);
            var buffer = new DynamicBuffer<byte>(owner.Memory);

            var chunksToBeWritten = Environment.SystemPageSize / data.Length;

            for (var i = 0; i < chunksToBeWritten; i++)
                buffer.Write(data);

            for (var i = 0; i < chunksToBeWritten; i++) {
                if (!buffer.Read(chunk, 4)) 
                    throw new InvalidOperationException("Failed to read chunk from dynamic buffer");

                buffer.Write(data);
            }
        }
    }
}