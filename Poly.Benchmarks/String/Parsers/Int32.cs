using System;
using System.Collections.Generic;
using System.Text;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;

namespace Benchmarks.String.Parsers {
    [MemoryDiagnoser]
    [MinColumn, MaxColumn, MeanColumn, MedianColumn]
    public class Int32Parser {
        readonly string text = int.MaxValue.ToString();

        [Benchmark]
        public void System_TryParse() {
            int.TryParse(text.AsSpan(), out _);
        }

        [Benchmark]
        public void Poly_TryParse() {
            int index = 0, lastIndex = text.Length;
            Poly.StringInt32Parser.TryParse(text, ref index, lastIndex, out int _);
        }
    }
}
