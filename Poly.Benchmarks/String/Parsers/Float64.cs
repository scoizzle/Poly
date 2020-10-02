using System;
using System.Collections.Generic;
using System.Text;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;

namespace Benchmarks.String.Parsers {
    [MemoryDiagnoser]
    [MinColumn, MaxColumn, MeanColumn, MedianColumn]
    public class Float64Parser {
        readonly string text = "-1337.123";

        [Benchmark]
        public void System_TryParse() {
            double.TryParse(text.AsSpan(), out _);
        }

        [Benchmark]
        public void Poly_TryParse() {
            int index = 0, lastIndex = text.Length;
            Poly.StringFloat64Parser.TryParse(text, ref index, lastIndex, out var _);
        }
    }
}
