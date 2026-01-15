using System;

using BenchmarkDotNet.Attributes;

namespace Benchmarks.String.Parsers {
    [MemoryDiagnoser]
    [MinColumn, MaxColumn, MeanColumn, MedianColumn]
    public class Int8Parser {
        readonly string success_text = sbyte.MaxValue.ToString();
        readonly string failure_text = byte.MaxValue.ToString();

        [Benchmark]
        public void System_Int8_TryParse_Success()
        {
            sbyte.TryParse(success_text.AsSpan(), out _);
        }

        [Benchmark]
        public void System_Int8_TryParse_Failure()
        {
            sbyte.TryParse(failure_text.AsSpan(), out _);
        }

        [Benchmark]
        public void Poly_Int8_TryParse_Success()
        {
            int index = 0, lastIndex = success_text.Length;
            Poly.StringInt8Parser.TryParse(success_text, ref index, lastIndex, out sbyte _);
        }

        [Benchmark]
        public void Poly_Int8_TryParse_Failure()
        {
            int index = 0, lastIndex = failure_text.Length;
            Poly.StringInt8Parser.TryParse(failure_text, ref index, lastIndex, out sbyte _);
        }

        [Benchmark]
        public void Poly_Int32_Int8_TryParse_Success()
        {
            int index = 0, lastIndex = success_text.Length;
            var result = Poly.StringInt32Parser.TryParse(success_text, ref index, lastIndex, out int value)
                && value >= sbyte.MinValue
                && value <= sbyte.MaxValue;
        }

        [Benchmark]
        public void Poly_Int32_Int8_TryParse_Failure()
        {
            int index = 0, lastIndex = failure_text.Length;
            var result = Poly.StringInt32Parser.TryParse(failure_text, ref index, lastIndex, out int value)
                && value >= sbyte.MinValue
                && value <= sbyte.MaxValue;
        }
    }
}