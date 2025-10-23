using BenchmarkDotNet.Attributes;

namespace Poly.Text.View.Benchmarks {
    [MinColumn, MaxColumn, MeanColumn, MedianColumn, MemoryDiagnoser]
    public class StringSliceBenchmark {
        [Benchmark]
        public void ExtractUntilChar_A() {
            var view = new StringView("aaaaba");

            var sub = view['b'];
        }

        [Benchmark]
        public void ExtractUntilChar_B() {
            var view = new StringView("aaaaba");

            var sub = view.ExtractUntil('b');
        }

        [Benchmark]
        public void ExtractUntilChar_C() {
            var view = new StringView("aaaaba");

            var sub = view['b'];
        }

        [Benchmark]
        public void ExtractUntilChar_D() {
            var view = new StringView("aaaaba");

            var sub = view.ExtractAndConsumeUntil('b');
        }
    }

    // [MinColumn, MaxColumn, MeanColumn, MedianColumn, MemoryDiagnoser]
    // public class OLD_StringViewBenchmarks
    // {
    //     [Benchmark]
    //     public void OLD_StringViewConsumeSingleCharacter5Times()
    //     {
    //         var view = new StringView("aaaaa");
    //
    //         view.Consume('a');
    //         view.Consume('a');
    //         view.Consume('a');
    //         view.Consume('a');
    //         view.Consume('a');
    //     }
    //
    //     [Benchmark]
    //     public void StringSliceConsumeSingleCharacter5Times()
    //     {
    //         var view = new StringView("aaaaa");
    //
    //         view.Consume('a');
    //         view.Consume('a');
    //         view.Consume('a');
    //         view.Consume('a');
    //         view.Consume('a');
    //     }
    //
    //     [Benchmark]
    //     public void OLD_StringViewConsumeString()
    //     {
    //         var view = new StringView("aaaaa");
    //
    //         view.Consume("aaaaa");
    //     }
    //
    //     [Benchmark]
    //     public void StringSliceConsumeString()
    //     {
    //         var view = new StringView("aaaaa");
    //
    //         view.Consume("aaaaa");
    //     }
    //
    //     [Benchmark]
    //     public void OLD_StringViewCompareToString()
    //     {
    //         var view = new StringView("aaaaa");
    //
    //         view.CompareTo("aaaaa");
    //     }
    //
    //     [Benchmark]
    //     public void StringSliceCompareToString()
    //     {
    //         var view = new StringView("aaaaa");
    //
    //         view.CompareTo("aaaaa");
    //     }
    //
    //     [Benchmark]
    //     public void OLD_StringViewCompareToSameStringSlice()
    //     {
    //         var view = new StringView("aaaaa");
    //
    //         view.CompareTo(view);
    //     }
    //
    //     [Benchmark]
    //     public void StringSliceCompareToSameStringSlice()
    //     {
    //         var view = new StringView("aaaaa");
    //
    //         view.CompareTo(view);
    //     }
    //
    //     [Benchmark]
    //     public void OLD_StringViewCompareToSubStringView()
    //     {
    //         var view = new StringView("aaaaa");
    //         var sub = new StringView(view.String, 2, 5);
    //
    //         view.CompareTo(sub);
    //     }
    //
    //     [Benchmark]
    //     public void StringSliceCompareToSubStringSliceFromRange()
    //     {
    //         var view = new StringView("aaaaa");
    //         var sub = view[2..];
    //
    //         view.CompareTo(sub);
    //     }
    //
    //     [Benchmark]
    //     public void StringSliceCompareToSubStringSliceFromIndicies()
    //     {
    //         var view = new StringView("aaaaa");
    //         var sub = view[2, 5];
    //
    //         view.CompareTo(sub);
    //     }
    //
    //     [Benchmark]
    //     public void StringViewConsumePredicate()
    //     {
    //         var view = new StringView("aaaaa");
    //
    //         view.Consume(static _ => true);
    //     }
    //
    //     [Benchmark]
    //     public void StringSliceConsumePredicate()
    //     {
    //         var view = new StringView("aaaaa");
    //
    //         view.Consume(static _ => true);
    //     }
    // }
}