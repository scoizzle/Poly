using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Numerics.Tensors;

// Vectorized sieve using the same optimizations as Poly VM:
// - word-batched inner loop (StridedBatchSet)
// - SIMD PopCount-based prime counting (CountBitsOp)

int limit = args.Length > 0 ? int.Parse(args[0]) : 1000000;
int wordCnt = (limit + 64) / 64;
long[] bits = new long[wordCnt];

var sw = Stopwatch.StartNew();

// ── Mark composites: word-batched inner loop ──
for (int i = 2; i * i <= limit; i++) {
    if ((bits[i >> 6] >> (i & 63) & 1) == 0) {
        long start = (long)i * i;
        int step = i;
        long limitL = limit;
        long j = start;
        while (j <= limitL) {
            int w = (int)(j >> 6);
            long v = bits[w];
            long wbase = (long)w << 6;
            long lastInWord = limitL < wbase + 63 ? limitL : wbase + 63;
            for (; j <= lastInWord; j += step)
                v |= 1L << (int)(j & 63);
            bits[w] = v;
        }
    }
}

// ── Count primes via SIMD PopCount ──
long composites = 0;
int offset = 0;
const int ChunkWords = 4096;
Span<ulong> counts = stackalloc ulong[ChunkWords];
while (offset < wordCnt) {
    int count = ChunkWords < wordCnt - offset ? ChunkWords : wordCnt - offset;
    var chunk = bits.AsSpan(offset, count);
    var ulongChunk = MemoryMarshal.Cast<long, ulong>(chunk);
    TensorPrimitives.PopCount(ulongChunk, counts[..count]);
    composites += (long)TensorPrimitives.Sum<ulong>(counts[..count]);
    offset += count;
}
long result = limit - 1 - composites;

sw.Stop();
Console.WriteLine($"C# vectorized,{limit},{result},{sw.ElapsedMilliseconds}");
