using System;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;

namespace Poly.Text.View.Benchmarks {
    [SimpleJob(RunStrategy.Throughput)]
    [MinColumn, MaxColumn, MeanColumn, MedianColumn]
    public class StringViewBenchmarks {

        [Benchmark]
        public void StringConsume() {
            var view = new StringView("aaaaa");

            view.Consume('a');
            view.Consume('a');
            view.Consume('a');
            view.Consume('a');
            view.Consume('a');
        }

        [Benchmark]
        public void SpanConsume() {
            var view = new StringView("aaaaa");

            view.Consume('a');
            view.Consume('a');
            view.Consume('a');
            view.Consume('a');
            view.Consume('a');
        }
    }
}