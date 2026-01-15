using System;

using BenchmarkDotNet.Attributes;

namespace Benchmarks.String.Parsers {
    [MemoryDiagnoser]
    [MinColumn, MaxColumn, MeanColumn, MedianColumn]
    public class Int32Parser {
        readonly string success_text = int.MaxValue.ToString();
        readonly string failure_text = ulong.MaxValue.ToString();

        [Benchmark]
        public void System_Int8_TryParse_Success()
        {
            int.TryParse(success_text.AsSpan(), out _);
        }

        [Benchmark]
        public void System_Int8_TryParse_Failure()
        {
            int.TryParse(failure_text.AsSpan(), out _);
        }

        [Benchmark]
        public void Poly_Int32_Int8_TryParse_Success()
        {
            int.TryParse(success_text.AsSpan(), out _);
        }

        [Benchmark]
        public void Poly_Int32_Int8_TryParse_Failure()
        {
            int.TryParse(failure_text.AsSpan(), out _);
        }


        [Benchmark]
        public void System_TryParse_Success()
        {
            int.TryParse(success_text.AsSpan(), out _);
        }

        [Benchmark]
        public void System_TryParse_Failure()
        {
            int.TryParse(failure_text.AsSpan(), out _);
        }

        [Benchmark]
        public void Poly_TryParse_Success()
        {
            int index = 0, lastIndex = success_text.Length;
            Poly.StringInt32Parser.TryParse(success_text, ref index, lastIndex, out int _);
        }

        [Benchmark]
        public void Poly_TryParse_Failure()
        {
            int index = 0, lastIndex = failure_text.Length;
            Poly.StringInt32Parser.TryParse(failure_text, ref index, lastIndex, out int _);
        }

        [Benchmark]
        public void Poly_UInt_TryParse_Success()
        {
            int index = 0, lastIndex = success_text.Length;
            Poly.StringInt32Parser.TryParse(success_text, ref index, lastIndex, out uint _);
        }

        [Benchmark]
        public void Poly_UInt_TryParse_Failure()
        {
            int index = 0, lastIndex = failure_text.Length;
            Poly.StringInt32Parser.TryParse(failure_text, ref index, lastIndex, out uint _);
        }

        [Benchmark]
        public void Poly_long_TryParse_Success()
        {
            int index = 0, lastIndex = success_text.Length;
            Poly.StringInt64Parser.TryParse(success_text, ref index, lastIndex, out long _);
        }

        [Benchmark]
        public void Poly_long_TryParse_Failure()
        {
            int index = 0, lastIndex = failure_text.Length;
            Poly.StringInt64Parser.TryParse(failure_text, ref index, lastIndex, out long _);
        }
    }
}