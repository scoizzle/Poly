using System;

using BenchmarkDotNet.Attributes;

namespace Benchmarks.String.Parsers
{
    [MemoryDiagnoser]
    [MinColumn, MaxColumn, MeanColumn, MedianColumn]
    public class Int64Parser {
        readonly string text = ulong.MaxValue.ToString();

        [Benchmark]
        public void System_TryParse() {
            ulong.TryParse(text.AsSpan(), out _);
        }

        [Benchmark]
        public void Poly_TryParse() {
            int index = 0, lastIndex = text.Length;
            Poly.StringInt64Parser.TryParse(text, ref index, lastIndex, out ulong _);
        }
    }
}
